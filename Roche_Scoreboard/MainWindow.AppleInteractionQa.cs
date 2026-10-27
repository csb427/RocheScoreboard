using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Button = System.Windows.Controls.Button;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using Brushes = System.Windows.Media.Brushes;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    internal async Task<Dictionary<string,bool>> RunAppleInteractionChecksAsync(string directory)
    {
        var checks=new Dictionary<string,bool>();
        var fixture=new Window { Width=1000,Height=760,Owner=this,Title="Apple interaction regression" };
        bool? motion=OperatorMotion.AnimationOverride;bool glass=OperatorAppearance.GlassEnabled,reduced=OperatorAppearance.ReduceMotion;
        UiDensity density=((App)System.Windows.Application.Current).ThemeService.CurrentDensity;
        OperatorDesign.Apply(fixture);
        async Task Settle(Func<bool> complete)
        {
            var deadline=System.Diagnostics.Stopwatch.StartNew();
            while(!complete() && deadline.ElapsedMilliseconds<1500)
            {await fixture.Dispatcher.InvokeAsync(()=>fixture.UpdateLayout(),System.Windows.Threading.DispatcherPriority.Render);await Task.Delay(16);}
        }
        try
        {
            OperatorAppearance.Configure(true,false);OperatorMotion.AnimationOverride=true;
            var home=new SportSelectionScreen();fixture.Content=home;fixture.Show();await Task.Delay(250);
            int selected=0;var elapsed=System.Diagnostics.Stopwatch.StartNew();long arrived=-1;
            home.SportSelected+=_=>{selected++;arrived=elapsed.ElapsedMilliseconds;};
            elapsed.Restart();InvokeCricketQa(home,"ActivateSport",Models.SportMode.Cricket);
            checks["sport-navigation-dispatches-without-animation-wait"] = selected==1 && arrived>=0 && arrived<100;
            checks["sport-navigation-leaves-input-ready"] = home.IsHitTestVisible;
            for(int i=0;i<12;i++)InvokeCricketQa(home,"ActivateSport",i%2==0?Models.SportMode.AFL:Models.SportMode.Settings);
            checks["sport-navigation-repeated-actions-not-latched"] = selected==13 && home.IsHitTestVisible;
            var wrap=new WrapPanel { Margin=new Thickness(24) };
            var symbols=new List<OperatorSymbol>();
            foreach(string kind in new[]{"settings","display","match","scorecard","light","dark","compact","comfortable","updates","automation"})
            {
                var symbol=new OperatorSymbol {Kind=kind,Width=44,Height=44,Foreground=Brushes.White};symbols.Add(symbol);
                wrap.Children.Add(new ToggleButton { Content=symbol,Width=76,Height=76,Margin=new Thickness(6) });
            }
            fixture.Content=wrap;await Task.Delay(180);
            foreach(var button in wrap.Children.OfType<ToggleButton>())button.IsChecked=true;
            await Task.Delay(90);
            checks["semantic-symbols-animate-native-selected-state"] = symbols.All(s=>s.IsActive && s.IsAnimating);
            checks["semantic-symbols-never-transform-hit-targets"] = symbols.All(s=>s.RenderTransform.Value.IsIdentity);
            await Task.Delay(650);
            await Settle(()=>symbols.All(s=>!s.IsAnimating && s.FillProgress==1));
            checks["semantic-symbols-settle-and-stop"] = symbols.All(s=>!s.IsAnimating && s.FillProgress==1);
            if(!checks["semantic-symbols-settle-and-stop"])File.WriteAllText(Path.Combine(directory,"symbol-settle-diagnostic.json"),System.Text.Json.JsonSerializer.Serialize(symbols.Select(s=>s.Diagnostics)));
            foreach(var button in wrap.Children.OfType<ToggleButton>())button.IsChecked=false;
            await Task.Delay(400);
            await Settle(()=>symbols.All(s=>!s.IsActive && s.FillProgress==0 && !s.IsAnimating));
            checks["semantic-symbols-clear-on-deselection"] = symbols.All(s=>!s.IsActive && s.FillProgress==0 && !s.IsAnimating);
            if(!checks["semantic-symbols-clear-on-deselection"])File.WriteAllText(Path.Combine(directory,"symbol-clear-diagnostic.json"),System.Text.Json.JsonSerializer.Serialize(symbols.Select(s=>s.Diagnostics)));
            CaptureCricketQa(wrap,Path.Combine(directory,"semantic-symbols.png"));
            OperatorMotion.AnimationOverride=false;
            foreach(var button in wrap.Children.OfType<ToggleButton>())button.IsChecked=true;
            checks["semantic-symbols-respect-reduced-motion"] = symbols.All(s=>s.FillProgress==1 && !s.IsAnimating);
            OperatorMotion.AnimationOverride=true;
            var densityRoot=new StackPanel {Margin=new Thickness(24)};
            var heading=new TextBlock {Text="Interface density",Foreground=Brushes.White};heading.SetResourceReference(TextBlock.FontSizeProperty,"ControlCentre.BodySize");densityRoot.Children.Add(heading);
            for(int i=0;i<8;i++){var text=new TextBlock {Text="Native text reflows without a screenshot",Foreground=Brushes.White,Margin=new Thickness(0,6,0,0)};text.SetResourceReference(TextBlock.FontSizeProperty,"ControlCentre.BodySize");densityRoot.Children.Add(text);}
            fixture.Content=densityRoot;OperatorDesign.UpdateDensity(UiDensity.Comfortable);await Task.Delay(450);
            OperatorDesign.UpdateDensity(UiDensity.Compact);await Task.Delay(60);
            checks["density-native-text-reflow-animates"] = OperatorDensityTransition.IsAnimating(densityRoot) && densityRoot.Children.OfType<TextBlock>().Any(text=>text.RenderTransform is TransformGroup);
            checks["density-stretched-label-animates-glyph-width-as-well-as-height"] = heading.RenderTransform is TransformGroup typeReflow && typeReflow.Children.OfType<ScaleTransform>().Any(scale=>scale.ScaleX>1.01 && scale.ScaleY>1.01);
            for(int i=0;i<8;i++){OperatorDesign.UpdateDensity(i%2==0?UiDensity.Comfortable:UiDensity.Compact);await Task.Delay(25);}
            await Task.Delay(450);
            await Settle(()=>!OperatorDensityTransition.IsAnimating(densityRoot));
            checks["density-rapid-reversal-cleans-transforms"] = !OperatorDensityTransition.IsAnimating(densityRoot) && densityRoot.Children.OfType<TextBlock>().All(text=>text.RenderTransform.Value.IsIdentity);
            if(!checks["density-rapid-reversal-cleans-transforms"])File.WriteAllText(Path.Combine(directory,"density-diagnostic.json"),System.Text.Json.JsonSerializer.Serialize(new {Active=OperatorDensityTransition.IsAnimating(densityRoot),Transforms=densityRoot.Children.OfType<TextBlock>().Select(text=>text.RenderTransform.Value.ToString())}));
            var settings=new GlobalSettingsScreen();
            settings.SetApplicationPreferences(((App)System.Windows.Application.Current).ThemeService.CurrentTheme,UiDensity.Compact,true);
            fixture.Content=settings;await Task.Delay(200);
            settings.GlassMaterialToggle.IsChecked=false;
            checks["operator-solid-material-choice-applies"] = !OperatorAppearance.GlassEnabled;
            settings.ReducedMotionToggle.IsChecked=true;OperatorMotion.AnimationOverride=null;
            checks["operator-motion-choice-applies"] = OperatorAppearance.ReduceMotion && !OperatorMotion.Enabled;
            using(var saved=System.Text.Json.JsonDocument.Parse(File.ReadAllText(OperatorAppearance.StoragePath)))
                checks["operator-material-motion-choices-persist"] = !saved.RootElement.GetProperty("GlassEnabled").GetBoolean() && saved.RootElement.GetProperty("ReduceMotion").GetBoolean();
            checks["operator-appearance-storage-isolated"] = OperatorAppearance.StoragePath.StartsWith(SelfTestStorage.RootPath+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
            CaptureCricketQa(settings,Path.Combine(directory,"settings-solid-reduced-motion.png"));
            settings.GlassMaterialToggle.IsChecked=true;settings.ReducedMotionToggle.IsChecked=false;
            checks["operator-material-motion-choices-can-be-restored"] = OperatorAppearance.GlassEnabled && !OperatorAppearance.ReduceMotion;
            var label=new OperatorTabLabelConverter();
            checks["floating-navigation-labels-have-no-ordinal-clutter"] = Equals(label.Convert("2  OUTPUT",typeof(string),null!,System.Globalization.CultureInfo.InvariantCulture),"Output");
            OperatorMotion.AnimationOverride=false;
            foreach(var setup in new (string Name,FrameworkElement View,int Steps)[]{("afl",new SetupWizard(),3),("cricket",new CricketSetupWizard(),4)})
            {
                fixture.Content=setup.View;
                foreach(var dimensions in new[]{(420d,320d),(780d,1100d),(1800d,600d)})
                {
                    fixture.Width=dimensions.Item1;fixture.Height=dimensions.Item2;await Task.Delay(180);fixture.UpdateLayout();
                    bool fits=true;
                    for(int step=1;step<=setup.Steps;step++)
                    {
                        var choice=setup.View.FindName($"Step{step}Navigation") as ToggleButton;
                        var text=setup.View.FindName($"Step{step}Label") as TextBlock;
                        fits &= choice is not null && text is not null && CricketQaBoundsWithin(choice,setup.View) && CricketQaBoundsWithin(text,choice) && CricketTextInkFits(text);
                    }
                    checks[$"{setup.Name}-wizard-step-labels-fit-{dimensions.Item1}x{dimensions.Item2}"]=fits;
                }
            }
            var springType=typeof(SportSelectionScreen).GetNestedType("HomeSpring",System.Reflection.BindingFlags.NonPublic)!;
            object Spring()=>Activator.CreateInstance(springType,System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance,null,new object[]{0d},null)!;
            var single=Spring();var split=Spring();springType.GetField("Target")!.SetValue(single,1d);springType.GetField("Target")!.SetValue(split,1d);
            springType.GetMethod("Step")!.Invoke(single,new object[]{.1,25d});
            for(int i=0;i<10;i++)springType.GetMethod("Step")!.Invoke(split,new object[]{.01,25d});
            checks["spring-motion-independent-of-delayed-frames"] = Math.Abs((double)springType.GetField("Value")!.GetValue(single)!-(double)springType.GetField("Value")!.GetValue(split)!)<.000001;
            var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(1,Models.CricketDeliveryType.Runs);
            var panel=new CricketControlPanel();panel.SetMatch(match);fixture.Content=panel;fixture.Width=1100;fixture.Height=780;await Task.Delay(180);
            panel.MainTabControl.SelectedItem=panel.ScorecardTab;await Task.Delay(180);
            var edit=QaDescendants(panel).OfType<Button>().FirstOrDefault(button=>Equals(button.ToolTip,"Edit Runs"));
            checks["scorecard-runs-editor-remains-discoverable"] = edit is not null;
            if(edit is not null)
            {
                edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(200);
                foreach(var dimensions in new[]{(420d,320d),(620d,600d)})
                {
                    fixture.Width=dimensions.Item1;fixture.Height=dimensions.Item2;await Task.Delay(150);fixture.UpdateLayout();
                    var sheet=QaDescendants(panel).OfType<Border>().FirstOrDefault(border=>Equals(border.Tag,"cricket-decision-sheet"));
                    var input=sheet is null?null:QaDescendants(sheet).OfType<System.Windows.Controls.TextBox>().FirstOrDefault(field=>Equals(field.Tag,"scorecard-figure-input"));
                    var apply=sheet is null?null:QaDescendants(sheet).OfType<Button>().FirstOrDefault(button=>Equals(button.Tag,"apply"));
                    input?.BringIntoView();await Task.Delay(50);
                    checks[$"scorecard-numeric-editor-full-height-{dimensions.Item1}x{dimensions.Item2}"] = input is {ActualHeight:>=64} && sheet is not null && CricketQaBoundsWithin(input,sheet);
                    checks[$"scorecard-numeric-primary-reachable-{dimensions.Item1}x{dimensions.Item2}"] = apply is {IsEnabled:true,IsDefault:true} && sheet is not null && CricketQaBoundsWithin(apply,sheet);
                    CaptureCricketQa(panel,Path.Combine(directory,$"scorecard-numeric-editor-{dimensions.Item1}x{dimensions.Item2}.png"));
                }
                CricketPrompt.CancelAll(panel.CricketRoot);await Task.Delay(150);
                checks["scorecard-editor-cancel-keeps-ledger"] = match.TotalRuns==1 && match.CurrentInnings!.Deliveries.Count==1;
            }
        }
        finally { fixture.Close();OperatorAppearance.Configure(glass,reduced);OperatorMotion.AnimationOverride=motion;OperatorDesign.UpdateDensity(density); }
        foreach(var check in await RunIphoneControlChecksAsync(directory)) checks[check.Key]=check.Value;
        foreach(var check in await RunLiquidSelectionChecksAsync(directory)) checks[check.Key]=check.Value;
        foreach(var check in await RunAppleReviewChecksAsync(directory)) checks[check.Key]=check.Value;
        foreach(var check in await RunSculptedGlassChecksAsync(directory)) checks[check.Key]=check.Value;
        foreach(var check in await RunOperatorResponseChecksAsync(directory)) checks[check.Key]=check.Value;
        foreach(var check in await RunIllustratedOperatorChecksAsync(directory)) checks[check.Key]=check.Value;
        return checks;
    }
}

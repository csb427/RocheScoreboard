using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Button = System.Windows.Controls.Button;
using RadioButton = System.Windows.Controls.RadioButton;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private async Task<Dictionary<string,bool>> RunSculptedGlassChecksAsync(string directory)
    {
        var checks=new Dictionary<string,bool>();
        var oldIntensity=OperatorAppearance.GlassIntensity;var oldBackdrop=OperatorAppearance.Backdrop;
        bool glass=OperatorAppearance.GlassEnabled,reduced=OperatorAppearance.ReduceMotion;bool? motion=OperatorMotion.AnimationOverride;
        var window=new Window{Width=1100,Height=820,Owner=this,ShowInTaskbar=false};OperatorDesign.Apply(window);window.Show();
        async Task Wait(Func<bool> complete)
        {var elapsed=System.Diagnostics.Stopwatch.StartNew();while(!complete() && elapsed.ElapsedMilliseconds<1800){await window.Dispatcher.InvokeAsync(()=>window.UpdateLayout(),System.Windows.Threading.DispatcherPriority.Render);await Task.Delay(16);}}
        try
        {
            checks["material-tests-use-isolated-preference-storage"]=SelfTestStorage.IsEnabled && OperatorAppearance.StoragePath.StartsWith(SelfTestStorage.RootPath,StringComparison.OrdinalIgnoreCase);
            OperatorMotion.AnimationOverride=true;OperatorAppearance.Configure(true,false);OperatorAppearance.ConfigureMaterial(OperatorGlassIntensity.Vivid,OperatorBackdropStyle.Colour);
            var home=new SportSelectionScreen();window.Content=home;await Task.Delay(650);
            foreach(var size in new[]{(1100d,820d),(420d,720d),(420d,320d),(320d,240d)})
            {
                window.Width=size.Item1;window.Height=size.Item2;await Task.Delay(160);window.UpdateLayout();
                checks[$"sculpted-home-all-native-actions-fit-{size.Item1}x{size.Item2}"]=new[]{home.AflCard,home.CricketCard,home.TrainingCard}.All(card=>card.ActualWidth>=70 && card.ActualHeight>=40 && CricketQaBoundsWithin(card,home));
                checks[$"sculpted-home-no-label-clipping-{size.Item1}x{size.Item2}"]=new[]{home.AflTextPanel,home.CricketTextPanel,home.TrainingTextPanel}.All(panel=>CricketQaBoundsWithin(panel,home) && panel.ActualHeight>20);
                CaptureCricketQa(home,Path.Combine(directory,$"sculpted-home-{size.Item1}x{size.Item2}.png"));
            }
            window.Width=1000;window.Height=850;
            var stage=new Grid();stage.RowDefinitions.Add(new(){Height=GridLength.Auto});stage.RowDefinitions.Add(new());
            var scene=new OperatorSceneBackdrop();Grid.SetRowSpan(scene,2);stage.Children.Add(scene);
            var preferences=new Grid{Margin=new Thickness(24)};preferences.ColumnDefinitions.Add(new());preferences.ColumnDefinitions.Add(new());
            var first=new OperatorMaterialPreferences{Margin=new Thickness(0,0,18,0)};var second=new OperatorMaterialPreferences();Grid.SetColumn(second,1);preferences.Children.Add(first);preferences.Children.Add(second);stage.Children.Add(preferences);
            var label=new TextBlock{Text="Clear controls. Live scoring.",FontSize=24,Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(24)};
            var surface=new OperatorGlassSurface{GlassSource=scene,Width=460,Height=170,CornerRadius=new CornerRadius(34),HorizontalAlignment=System.Windows.HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Child=label};surface.SetResourceReference(BackgroundProperty,"ControlCentre.GlassBrush");Grid.SetRow(surface,1);stage.Children.Add(surface);
            window.Content=stage;await Task.Delay(220);window.UpdateLayout();var position=label.TranslatePoint(new Point(),stage);
            RadioButton Choice(FrameworkElement root,string text)=>QaDescendants(root).OfType<RadioButton>().Single(choice=>Equals(choice.Content,text));
            ((ISelectionItemProvider)new RadioButtonAutomationPeer(Choice(first,"Soft")).GetPattern(PatternInterface.SelectionItem)).Select();
            checks["material-selection-synchronizes-other-settings-instances"]=Choice(second,"Soft").IsChecked==true && OperatorAppearance.GlassIntensity==OperatorGlassIntensity.Soft;
            var hashes=new HashSet<string>();double previousBend=0;
            foreach(var strength in Enum.GetValues<OperatorGlassIntensity>())
            {
                OperatorAppearance.ConfigureMaterial(strength,OperatorBackdropStyle.Colour);await Task.Delay(130);window.UpdateLayout();
                if(surface.UsesShader){checks[$"material-optics-increase-with-intensity-{strength}"]=surface.OpticalSettings.X>previousBend;previousBend=surface.OpticalSettings.X;}
                string file=Path.Combine(directory,$"sculpted-material-{strength}.png");CaptureSculptedMaterial(surface,file);hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))));
                checks[$"material-text-stays-on-original-native-baseline-{strength}"]=label.Effect is null && label.TranslatePoint(new Point(),stage)==position;
            }
            checks["material-presets-produce-distinct-native-rendered-results"]=hashes.Count==3;
            checks["dedicated-backdrop-is-live-not-a-frozen-owner-capture"]=!surface.HasCapturedBackdrop;
            foreach(var strength in Enum.GetValues<OperatorGlassIntensity>())foreach(var backdrop in Enum.GetValues<OperatorBackdropStyle>())
            {
                OperatorAppearance.ConfigureMaterial(strength,backdrop);await Wait(()=>!scene.IsTransitioning);
                checks[$"material-combination-stays-synchronized-and-keeps-native-text-{strength}-{backdrop}"]=QaDescendants(first).OfType<RadioButton>().Count(button=>button.IsChecked==true)==2
                    && QaDescendants(second).OfType<RadioButton>().Count(button=>button.IsChecked==true)==2 && label.Effect is null && label.TranslatePoint(new Point(),stage)==position;
            }
            for(int i=0;i<45;i++){OperatorAppearance.ConfigureMaterial(OperatorGlassIntensity.Vivid,(OperatorBackdropStyle)(i%3));await Task.Delay(7);}
            OperatorAppearance.ConfigureMaterial(OperatorGlassIntensity.Vivid,OperatorBackdropStyle.Colour);
            await Wait(()=>!scene.IsTransitioning);
            checks["rapid-backdrop-reversal-remains-bounded-and-retires-animation"]=scene.CachedSceneCount<=3 && !scene.IsTransitioning;
            using(var saved=System.Text.Json.JsonDocument.Parse(File.ReadAllText(OperatorAppearance.StoragePath)))
                checks["material-and-backdrop-choices-are-persisted"]=saved.RootElement.GetProperty("GlassIntensity").GetInt32()==2 && saved.RootElement.GetProperty("Backdrop").GetInt32()==1;
            if(SelfTestStorage.IsEnabled)
            {
                string saved=File.ReadAllText(OperatorAppearance.StoragePath);
                try
                {
                    var load=typeof(OperatorAppearance).GetMethod("Load",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
                    File.WriteAllText(OperatorAppearance.StoragePath,"{\"GlassEnabled\":false,\"ReduceMotion\":true}");var legacy=load.Invoke(null,null)!;
                    object Value(object preferences,string name)=>preferences.GetType().GetProperty(name)!.GetValue(preferences)!;
                    checks["legacy-material-preferences-preserve-accessibility-and-gain-new-defaults"]=Equals(Value(legacy,"GlassEnabled"),false) && Equals(Value(legacy,"ReduceMotion"),true)
                        && Equals(Value(legacy,"GlassIntensity"),OperatorGlassIntensity.Vivid) && Equals(Value(legacy,"Backdrop"),OperatorBackdropStyle.Colour);
                    File.WriteAllText(OperatorAppearance.StoragePath,"{\"GlassIntensity\":999,\"Backdrop\":-9}");var invalid=load.Invoke(null,null)!;
                    checks["invalid-material-preferences-recover-to-valid-defaults"]=Equals(Value(invalid,"GlassIntensity"),OperatorGlassIntensity.Vivid) && Equals(Value(invalid,"Backdrop"),OperatorBackdropStyle.Colour);
                }
                finally{File.WriteAllText(OperatorAppearance.StoragePath,saved);}
            }
            CaptureCricketQa(stage,Path.Combine(directory,"sculpted-material-preferences-synchronized.png"));
            InvokeCricketQa(surface,"Illuminate",new Point(230,85),.55);await Task.Delay(40);InvokeCricketQa(surface,"Illuminate",new Point(230,85),0d);
            await Wait(()=>!surface.IsMaterialAnimating);checks["hover-reflection-reversal-releases-its-clock"]=!surface.IsMaterialAnimating;
            OperatorMotion.AnimationOverride=null;OperatorAppearance.Configure(true,true);OperatorAppearance.ConfigureMaterial(OperatorGlassIntensity.Balanced,OperatorBackdropStyle.Black);
            checks["reduced-motion-material-switch-is-immediate-and-readable"]=!scene.IsTransitioning && !surface.IsMaterialAnimating && label.IsVisible;
            OperatorAppearance.Configure(false,true);checks["solid-material-preference-removes-shader"]=!surface.UsesShader;
            OperatorAppearance.Configure(true,false);OperatorAppearance.ConfigureMaterial(OperatorGlassIntensity.Soft,OperatorBackdropStyle.Neutral);
            var reset=QaDescendants(first).OfType<Button>().Single(button=>System.Windows.Automation.AutomationProperties.GetName(button)=="Restore material defaults");reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            checks["material-reset-restores-vivid-colour-without-changing-motion-preference"]=OperatorAppearance.GlassIntensity==OperatorGlassIntensity.Vivid && OperatorAppearance.Backdrop==OperatorBackdropStyle.Colour && !OperatorAppearance.ReduceMotion;
            window.Content=null;await Task.Delay(30);checks["unloaded-material-and-backdrop-have-no-animation-clocks"]=!scene.IsTransitioning && !surface.IsMaterialAnimating;
        }
        finally
        {window.Close();OperatorAppearance.Configure(glass,reduced);OperatorAppearance.ConfigureMaterial(oldIntensity,oldBackdrop);OperatorMotion.AnimationOverride=motion;}
        return checks;
    }
    private static void CaptureSculptedMaterial(FrameworkElement element,string file)
    {
        var root=(FrameworkElement)Window.GetWindow(element).Content;root.UpdateLayout();
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(root.ActualWidth)),Math.Max(1,(int)Math.Ceiling(root.ActualHeight)),96,96,PixelFormats.Pbgra32);bitmap.Render(root);
        var bounds=element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        int left=Math.Clamp((int)Math.Floor(bounds.Left),0,bitmap.PixelWidth-1),top=Math.Clamp((int)Math.Floor(bounds.Top),0,bitmap.PixelHeight-1);
        var crop=new System.Windows.Media.Imaging.CroppedBitmap(bitmap,new Int32Rect(left,top,Math.Max(1,Math.Min((int)Math.Floor(bounds.Width),bitmap.PixelWidth-left)),Math.Max(1,Math.Min((int)Math.Floor(bounds.Height),bitmap.PixelHeight-top))));
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(crop));using(var output=File.Create(file))encoder.Save(output);
    }
}

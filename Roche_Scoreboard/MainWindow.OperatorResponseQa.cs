using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Button=System.Windows.Controls.Button;
using RadioButton=System.Windows.Controls.RadioButton;
using Brushes=System.Windows.Media.Brushes;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private async Task<Dictionary<string,bool>> RunOperatorResponseChecksAsync(string directory)
    {
        var checks=new Dictionary<string,bool>();
        bool? oldMotion=OperatorMotion.AnimationOverride;
        var window=new Window{Width=1100,Height=820,Owner=this,ShowInTaskbar=false};OperatorDesign.Apply(window);window.Show();
        try
        {
            OperatorMotion.AnimationOverride=true;
            var host=new StackPanel{Margin=new Thickness(30)};
            var button=new Button{Content="Keyboard and mouse",Width=250,Margin=new Thickness(5)};
            var transparent=new Button{Content="Figure",Width=100,Background=Brushes.Transparent,BorderThickness=new Thickness(0)};
            var choice=new RadioButton{Content="Filled selection",GroupName="ResponseQA",Width=250,MinHeight=80,Margin=new Thickness(5)};choice.SetResourceReference(StyleProperty,"SettingsCommand");
            host.Children.Add(button);host.Children.Add(transparent);host.Children.Add(choice);window.Content=host;await Task.Delay(150);window.UpdateLayout();
            button.Focus();OperatorInputFocus.SetIsKeyboardMode(window,false);await Task.Delay(30);
            var surface=QaDescendants(button).OfType<OperatorGlassSurface>().FirstOrDefault();
            var normalBorder=surface?.BorderBrush;
            checks["pointer-focus-does-not-add-a-keyboard-border"]=button.IsKeyboardFocused && surface?.BorderThickness.Left==1;
            OperatorInputFocus.SetIsKeyboardMode(window,true);await Task.Delay(30);
            checks["keyboard-focus-has-neutral-shaped-ring"]=surface?.BorderThickness.Left==2 && surface.BorderBrush is SolidColorBrush ink && ink.Color.R==ink.Color.G;
            button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseDownEvent});
            checks["pointer-input-clears-inherited-keyboard-mode"]=!OperatorInputFocus.GetIsKeyboardMode(button);
            checks["transparent-figures-do-not-allocate-glass-passes"]=QaDescendants(transparent).OfType<OperatorGlassSurface>().All(glass=>!glass.UsesShader && !glass.HasCapturedBackdrop);
            choice.IsChecked=true;await Task.Delay(65);
            double progress=OperatorChoiceMotion.GetProgress(choice);
            checks["filled-choice-interpolates-without-a-checkmark"]=progress>0 && progress<1 && choice.Template.FindName("PART_CheckPath",choice) is null;
            for(int i=0;i<16;i++)choice.IsChecked=i%2==0;
            choice.IsChecked=true;await Task.Delay(350);
            checks["filled-choice-rapid-reversal-settles"]=Math.Abs(OperatorChoiceMotion.GetProgress(choice)-1)<.001 && !choice.HasAnimatedProperties;
            CaptureCricketQa(host,Path.Combine(directory,"response-controls.png"));

            var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(1,Models.CricketDeliveryType.Runs);
            var panel=new CricketControlPanel();panel.SetMatch(match);window.Content=panel;await Task.Delay(180);window.UpdateLayout();
            panel.MainTabControl.SelectedItem=panel.ScorecardTab;await Task.Delay(300);window.UpdateLayout();
            int initial=panel.ScorecardRenderCount;
            var samples=new List<double>();var clock=System.Diagnostics.Stopwatch.StartNew();double previous=0;
            TimeSpan lastFrame=TimeSpan.MinValue;
            EventHandler frame=(_,args)=>{if(args is System.Windows.Media.RenderingEventArgs render){if(render.RenderingTime==lastFrame)return;lastFrame=render.RenderingTime;}double now=clock.Elapsed.TotalMilliseconds;if(previous>0)samples.Add(now-previous);previous=now;};
            CompositionTarget.Rendering+=frame;
            try
            {
                for(int i=0;i<6;i++){panel.MainTabControl.SelectedIndex=0;await Task.Delay(55);panel.MainTabControl.SelectedItem=panel.ScorecardTab;await Task.Delay(55);}
                await Task.Delay(350);
            }
            finally{CompositionTarget.Rendering-=frame;}
            checks["unchanged-scorecard-reentry-reuses-native-figure-controls"]=panel.ScorecardRenderCount==initial;
            InvokeCricketQa(panel,"SelectScorecardTeam",true);await Task.Delay(300);
            checks["bowling-switch-renders-once"]=panel.ScorecardRenderCount==initial+1;
            InvokeCricketQa(panel,"SelectScorecardTeam",true);
            checks["reselecting-bowling-does-not-rebuild-table"]=panel.ScorecardRenderCount==initial+1;
            InvokeCricketQa(panel,"SelectScorecardTeam",false);await Task.Delay(300);
            checks["batting-switch-renders-once"]=panel.ScorecardRenderCount==initial+2;
            int beforeCorrection=panel.ScorecardRenderCount;match.RecordDelivery(2,Models.CricketDeliveryType.Runs);InvokeCricketQa(panel,"RefreshScorecard",false);
            checks["new-delivery-invalidates-scorecard-cache"]=panel.ScorecardRenderCount>beforeCorrection && match.TotalRuns==3;
            CaptureCricketQa(panel,Path.Combine(directory,"response-scorecard.png"));
            File.WriteAllText(Path.Combine(directory,"response-frame-observations.json"),System.Text.Json.JsonSerializer.Serialize(new{Frames=samples.Count,MedianMs=samples.Count>0?samples.Order().ElementAt(samples.Count/2):0,P95Ms=samples.Count>0?samples.Order().ElementAt(Math.Min(samples.Count-1,(int)(samples.Count*.95))):0,MaximumMs=samples.Count>0?samples.Max():0,Note="Observed native compositor intervals during repeated tab switching, not a universal frame-rate guarantee."},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            panel.ClearMatch();

            var wizard=new SetupWizard();window.Content=wizard;await Task.Delay(120);InvokeCricketQa(wizard,"ShowStep",2,false);
            foreach(var size in new[]{(1000d,820d),(420d,600d)})
            {
                window.Width=size.Item1;window.Height=size.Item2;await Task.Delay(120);
                var countdown=(RadioButton)wizard.FindName("SetupCountdown");countdown.IsChecked=true;await Task.Delay(300);window.UpdateLayout();
                var mode=(FrameworkElement)wizard.FindName("CountdownCard");var duration=(FrameworkElement)wizard.FindName("SetupDurationPanel");
                var bottom=mode.TranslatePoint(new System.Windows.Point(0,mode.ActualHeight),wizard);var top=duration.TranslatePoint(new System.Windows.Point(),wizard);
                checks[$"afl-clock-choices-clear-quarter-length-{size.Item1}"]=top.Y>=bottom.Y-1;
                CaptureCricketQa(wizard,Path.Combine(directory,$"response-afl-clock-{size.Item1}.png"));
            }
            checks["afl-live-settings-use-shared-workspace"]=_aflSettingsWorkspace is not null && _aflSettingsWorkspace.Sections.Contains(SettingsSection_Match) && _aflSettingsWorkspace.Sections.Contains(SettingsSection_Teams);
        }
        finally{window.Close();OperatorMotion.AnimationOverride=oldMotion;}
        return checks;
    }
}

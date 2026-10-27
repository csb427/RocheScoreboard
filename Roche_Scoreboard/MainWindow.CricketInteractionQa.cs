using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private async Task<Dictionary<string,bool>> RunCricketInteractionChecksAsync(string directory)
    {
        var checks = new Dictionary<string,bool>();
        var window = new Window {Width=1000,Height=760,ShowInTaskbar=false}; window.Show();
        var wizard = new CricketSetupWizard(); window.Content=wizard;
        bool? oldMotion = OperatorMotion.AnimationOverride;
        try
        {
            await Task.Delay(700); InvokeCricketQa(wizard,"ShowStep",3,false);
            foreach(var size in new[]{(1000d,760d),(420d,520d),(360d,360d),(650d,420d),(1100d,400d)})
            {
                window.Width=size.Item1; window.Height=size.Item2; await Task.Delay(120);
                var viewport=FindQaVisual<WizardViewport>(wizard)!;
                checks[$"format-card-content-stays-inside-both-cards-{size.Item1}"] = CricketQaBoundsWithin(wizard.OversInputPanel,wizard.Step3bPanel)
                    && CricketQaBoundsWithin(wizard.LimitedFormatChoice,wizard.LimitedOversCard) && CricketQaBoundsWithin(wizard.MultiFormatChoice,wizard.MultiInningsCard)
                    && CricketQaBoundsWithin(wizard.LimitedOversCard,wizard.FormatCardsGrid) && CricketQaBoundsWithin(wizard.MultiInningsCard,wizard.FormatCardsGrid);
                checks[$"format-has-no-horizontal-overflow-{size.Item1}"] = viewport.ScrollableWidth<1;
                viewport.ScrollToVerticalOffset(viewport.VerticalOffset+wizard.FormatCardsGrid.TranslatePoint(new System.Windows.Point(),viewport).Y);
                await Task.Delay(80); CaptureCricketQa(wizard,Path.Combine(directory,$"format-cards-{size.Item1}.png"));
            }
            checks["default-rule-toggle-visuals-are-on-when-format-first-opens"] = new[]{wizard.ReplaceWideBallsToggle,wizard.ReplaceNoBallsToggle}.All(toggle => toggle.IsChecked==true && toggle.Template.FindName("Thumb",toggle) is TranslateTransform thumb && Math.Abs(thumb.X-20)<.01);
            checks["format-choice-uses-one-native-full-card-selection"] = wizard.LimitedOversCard.Child==wizard.LimitedFormatChoice && wizard.MultiInningsCard.Child==wizard.MultiFormatChoice
                && wizard.LimitedFormatChoice.MinHeight>=130 && wizard.MultiFormatChoice.MinHeight>=130;
            wizard.MultiFormatChoice.IsChecked=true;
            checks["format-native-radio-selection-updates-engine-choice"] = wizard.MultiFormatChoice.IsChecked==true && wizard.LimitedFormatChoice.IsChecked==false;
            InvokeCricketQa(wizard,"FinishWizard");
            checks["format-native-selection-is-carried-to-setup-result"] = wizard.Result?.Format==Models.CricketFormat.MultiInnings;
            wizard.LimitedFormatChoice.IsChecked=true;
            InvokeCricketQa(wizard,"FinishWizard");
            checks["format-radio-return-restores-limited-overs-result"] = wizard.Result?.Format==Models.CricketFormat.LimitedOvers;
            var toggle=wizard.FreeHitsToggle;
            OperatorMotion.AnimationOverride=true;
            for(int i=0;i<20;i++) {toggle.IsChecked=i%2==0; await Task.Delay(12);}
            toggle.IsChecked=true; await Task.Delay(400);
            var thumb=(TranslateTransform)toggle.Template.FindName("Thumb",toggle);
            for(int frame=0;frame<40 && Math.Abs(thumb.X-20)>=.01;frame++)await Task.Delay(16);
            checks["rapid-toggle-reversal-settles-on-correct-end"] = Math.Abs(thumb.X-20)<.01;
            OperatorMotion.AnimationOverride=false; toggle.IsChecked=false;
            checks["reduced-motion-toggle-applies-state-without-a-clock"] = Math.Abs(thumb.X)<.01 && !thumb.HasAnimatedProperties;
            OperatorMotion.AnimationOverride=true;
            var button=new Button {Content="Test action",Style=(Style)wizard.FindResource("CricketActionButton"),Width=160,Height=50};
            var choice=new ComboBox {Style=(Style)wizard.FindResource("SettingsChoice"),Width=220,ItemsSource=new[]{"First","Second"},SelectedIndex=0};
            var stack=new StackPanel();stack.Children.Add(button);stack.Children.Add(choice);window.Content=stack;await Task.Delay(100);
            var scale=(ScaleTransform)button.Template.FindName("TbSc",button);
            bool clickSafe=true;int activations=0;
            button.Click+=(_,_)=>{activations++;button.Visibility=Visibility.Collapsed;};
            for(int i=0;i<20;i++)
            {
                button.Visibility=Visibility.Visible;window.UpdateLayout();button.Focus();
                try {button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));} catch {clickSafe=false;}
            }
            checks["rapid-action-hides-are-single-activation-and-do-not-crash"] = clickSafe && activations==20;
            checks["hidden-action-resets-template-motion-to-rest"] = Math.Abs(scale.ScaleX-1)<.001 && Math.Abs(scale.ScaleY-1)<.001 && !scale.HasAnimatedProperties;
            button.Visibility=Visibility.Visible;await Task.Delay(50);
            checks["reopened-action-does-not-retain-stale-hover-scale"] = Math.Abs(scale.ScaleX-1)<.001;
            choice.IsDropDownOpen=true;await Task.Delay(300);choice.SelectedIndex=1;choice.IsDropDownOpen=false;await Task.Delay(20);
            checks["animated-settings-dropdown-keeps-native-selection-and-dismissal"] = choice.SelectedIndex==1 && !choice.IsDropDownOpen;
            OperatorMotion.AnimationOverride=false;choice.IsDropDownOpen=true;await Task.Delay(50);choice.IsDropDownOpen=false;
            checks["reduced-motion-dropdown-still-selects-and-closes"] = choice.SelectedIndex==1 && !choice.IsDropDownOpen;
        }
        finally {OperatorMotion.AnimationOverride=oldMotion;window.Close();}
        return checks;
    }
}

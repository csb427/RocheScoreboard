using KeyEventArgs=System.Windows.Input.KeyEventArgs;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Button = System.Windows.Controls.Button;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private async Task<Dictionary<string,bool>> RunSportInteractionChecksAsync(string directory)
    {
        var checks=new Dictionary<string,bool>();bool? motion=OperatorMotion.AnimationOverride;
        OperatorMotion.AnimationOverride=true;
        var screen=new SportSelectionScreen();var choices=new List<SportMode>();screen.SportSelected+=choices.Add;
        var window=new Window {Content=screen,Width=1000,Height=650,ShowInTaskbar=false};window.Show();
        try
        {
            await Task.Delay(500);
            Button Find(string name)=>(Button)screen.FindName(name);
            void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            var afl=Find("AflLaunchAction");var training=Find("TrainingLaunchAction");var settings=Find("SettingsLaunchAction");
            checks["sports-launch-cards-and-settings-expose-native-keyboard-and-button-semantics"]=new[] {afl,training,settings}.All(button=>button.Focusable && !string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)) && new ButtonAutomationPeer(button).GetAutomationControlType()==AutomationControlType.Button);
            var original=screen.AflCard.TransformToAncestor(screen).TransformBounds(new Rect(0,0,screen.AflCard.ActualWidth,screen.AflCard.ActualHeight));
            afl.Focus();await Task.Delay(220);
            var focused=screen.AflCard.TransformToAncestor(screen).TransformBounds(new Rect(0,0,screen.AflCard.ActualWidth,screen.AflCard.ActualHeight));
            checks["sports-keyboard-focus-is-visible-without-moving-the-card-hit-target"]=afl.IsKeyboardFocused && screen.AflGlow.Opacity>.5 && original==focused;
            checks["sports-launch-surface-does-not-add-an-inset-second-focus-box"]=afl.Template.FindName("FocusRing",afl) is null;
            afl.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,0,Key.Right) {RoutedEvent=Keyboard.PreviewKeyDownEvent});
            checks["sports-arrow-navigation-moves-between-launch-actions"]=training.IsKeyboardFocused;
            CaptureCricketQa(screen,Path.Combine(directory,"sports-native-focus-and-stable-full-card-targets.png"));

            Invoke(afl);await Task.Delay(35);screen.PrepareForDisplay();Invoke(training);await Task.Delay(450);
            checks["sports-dispatch-each-click-immediately-without-delayed-replay"]=choices.SequenceEqual(new[] {SportMode.AFL,SportMode.Training}) && screen.IsHitTestVisible;
            choices.Clear();Invoke(training);await Task.Delay(20);window.Content=null;await Task.Delay(40);window.Content=screen;screen.PrepareForDisplay();await Task.Delay(350);
            checks["leaving-and-returning-does-not-replay-navigation-or-lock-input"]=choices.SequenceEqual(new[] {SportMode.Training}) && screen.IsHitTestVisible;
            afl.Focus();var previous=Keyboard.FocusedElement;
            InvokeCricketQa(screen,"ShowChangelog");await Task.Delay(45);InvokeCricketQa(screen,"HideChangelog");InvokeCricketQa(screen,"ShowChangelog");await Task.Delay(350);
            for(int frame=0;frame<40 && screen.ChangelogOverlay.Opacity<=.99;frame++)await Task.Delay(16);
            checks["reopening-changelog-during-close-cannot-hide-the-new-sheet"]=screen.ChangelogOverlay.Visibility==Visibility.Visible && screen.ChangelogOverlay.Opacity>.99;
            checks["changelog-sheet-keeps-keyboard-tab-navigation-inside-the-sheet"]=KeyboardNavigation.GetTabNavigation(screen.ChangelogOverlay)==KeyboardNavigationMode.Cycle && screen.ChangelogSearchBox.IsKeyboardFocusWithin;
            InvokeCricketQa(screen,"HideChangelog");await Task.Delay(220);
            for(int frame=0;frame<40 && screen.ChangelogOverlay.Visibility!=Visibility.Collapsed;frame++)await Task.Delay(16);
            checks["closing-changelog-restores-the-previous-keyboard-focus"]=screen.ChangelogOverlay.Visibility==Visibility.Collapsed && ReferenceEquals(previous,Keyboard.FocusedElement);

            OperatorMotion.AnimationOverride=false;screen.PrepareForDisplay();choices.Clear();Invoke(training);await Task.Delay(70);
            checks["reduced-motion-sports-selection-remains-responsive-without-cinematic-delay"]=choices.SequenceEqual(new[] {SportMode.Training});
            window.Content=null;await Task.Delay(30);
            checks["hidden-sports-picker-stops-ambient-and-hover-animation-clocks"]=!screen.AflIconFloat.HasAnimatedProperties && !screen.CricketIconFloat.HasAnimatedProperties && !screen.AflGlow.HasAnimatedProperties && !screen.CricketGlow.HasAnimatedProperties;
        }
        finally {window.Close();OperatorMotion.AnimationOverride=motion;}
        return checks;
    }
}

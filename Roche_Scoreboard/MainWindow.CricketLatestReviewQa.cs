using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Button=System.Windows.Controls.Button;
using Brushes=System.Windows.Media.Brushes;
using TabControl=System.Windows.Controls.TabControl;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private async Task<Dictionary<string,bool>> RunCricketLatestReviewChecksAsync(string directory)
    {
        var checks=new Dictionary<string,bool>();
        var window=new Window {Width=1050,Height=780,ShowInTaskbar=false};window.Show();
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            var settings=new GlobalSettingsScreen();window.Content=settings;settings.SetScreensaverPreferences(null,true);await Task.Delay(120);
            checks["general-settings-exclude-afl-only-controls"] = settings.GeneralSettingsTab.IsChecked == true && !settings.GoalAnimationCombo.IsVisible && !settings.BreakScreenToggle.IsVisible;
            checks["general-settings-build-identifier-is-selectable-and-explicit"] = settings.BuildInfoBox.IsReadOnly
                && settings.BuildInfoBox.Text == BuildIdentity.Description && settings.BuildInfoBox.Text.Contains("build ");
            settings.AflSettingsTab.IsChecked = true; await Task.Delay(120);
            checks["afl-settings-include-goal-and-quarter-break-options"] = settings.GoalAnimationCombo.IsVisible && settings.BreakScreenToggle.IsVisible;
            settings.CricketSettingsScopeTab.IsChecked = true; await Task.Delay(120);
            checks["cricket-settings-have-specific-defaults-without-afl-options"] = settings.CricketEventsDefaultToggle.IsVisible && !settings.GoalAnimationCombo.IsVisible && !settings.BreakScreenToggle.IsVisible;
            settings.GeneralSettingsTab.IsChecked = true; await Task.Delay(120);
            checks["screensaver-choices-use-full-presentation-cards-and-show-active-source"] = settings.BuiltinScreensaverButton.ActualHeight>=100 && System.Windows.Automation.AutomationProperties.GetItemStatus(settings.BuiltinScreensaverButton)=="Selected";
            bool mediaRequested=false;settings.ScreensaverMediaRequested+=()=>mediaRequested=true;Click(settings.CustomScreensaverButton);
            checks["cancelling-custom-media-selection-retains-current-source"] = mediaRequested && System.Windows.Automation.AutomationProperties.GetItemStatus(settings.BuiltinScreensaverButton)=="Selected";
            settings.SetScreensaverPreferences(Path.Combine(directory,"missing-custom.png"),true);
            checks["configured-custom-media-has-selected-state-and-missing-file-fallback-explanation"] = System.Windows.Automation.AutomationProperties.GetItemStatus(settings.CustomScreensaverButton)=="Selected" && settings.GlobalScreensaverStatus.Text.Contains("unavailable");
            settings.CustomScreensaverButton.BringIntoView();await Task.Delay(80);
            checks["screensaver-source-cards-remain-visible-after-scrolling"] = CricketQaBoundsWithin(settings.CustomScreensaverButton,settings.SettingsWorkspace.Scroller);
            CaptureCricketQa(settings,Path.Combine(directory,"review-screensaver-source-cards.png"));

            var match=CricketReliabilityChecks.CreateMatch(true);var resume=new ResumeMatchPrompt(new GameState {Sport="cricket",CricketMatchState=match.CapturePersistenceState()});
            window.Content=resume;await Task.Delay(120);
            var primaryFill = ((SolidColorBrush)resume.ResumeButton.Background).Color;
            var laterFill = resume.LaterButton.Background is SolidColorBrush solid ? solid.Color : ((LinearGradientBrush)resume.LaterButton.Background).GradientStops[0].Color;
            checks["resume-primary-action-is-visibly-stronger-than-later"] = ContrastHelper.ContrastRatio(primaryFill, laterFill) >= 4.5
                && ContrastHelper.ContrastRatio(((SolidColorBrush)resume.ResumeButton.Foreground).Color, primaryFill) >= 4.5
                && ContrastHelper.ContrastRatio(((SolidColorBrush)resume.LaterButton.Foreground).Color, laterFill) >= 4.5;
            Click(resume.DiscardButton);checks["discard-is-separated-and-requires-confirmation"] = !resume.DiscardChosen && Equals(resume.DiscardButton.Content,"CONFIRM DISCARD");
            CaptureCricketQa(resume,Path.Combine(directory,"review-resume-primary-and-discard-confirmation.png"));Click(resume.LaterButton);

            var wizard=new CricketSetupWizard();window.Content=wizard;await Task.Delay(1100);
            var preset=new TeamPreset {PresetName="Review Cricket Club",TeamName="Review Cricket Club",Abbreviation="RCC",PrimaryColor="#17568A",SecondaryColor="#FFFFFF"};
            var presets=(List<TeamPreset>)typeof(CricketSetupWizard).GetField("_presets",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(wizard)!;
            presets.Clear();presets.Add(preset);InvokeCricketQa(wizard,"ApplyPresetToHome",preset);await Task.Delay(120);
            checks["selected-cricket-preset-keeps-club-text-with-a-drawn-check-selection"] = QaDescendants(wizard.HomePresetPanel).OfType<System.Windows.Shapes.Path>().Any(b=>Equals(b.Tag,"preset-selection-marker"))
                && !QaDescendants(wizard.HomePresetPanel).OfType<System.Windows.Controls.TextBlock>().Any(t=>t.Text.Contains("SELECTED"))
                && QaDescendants(wizard.HomePresetPanel).OfType<System.Windows.Controls.TextBlock>().Any(t=>t.Text=="Review Cricket Club") && wizard.HomePresetPanel.Children.OfType<Border>().All(b=>b.Focusable);
            CaptureCricketQa(wizard,Path.Combine(directory,"review-cricket-preset-selected-card.png"));

            var panel=new CricketControlPanel();window.Content=panel;panel.SetMatch(match);await Task.Delay(100);
            for(int over=0;over<2;over++)
            {if(match.CurrentInnings!.NeedsBowlerSelection)match.ConfirmBowler(over);for(int ball=0;ball<6;ball++)match.RecordDelivery(0,CricketDeliveryType.Dot);}
            await Task.Delay(100);
            var indices=panel.BowlerSelectionList.Tag as List<int>;
            checks["returning-bowler-is-first-and-clearly-recommended"] = indices is {Count:>0} && indices[0]==match.SuggestedNextBowler() && panel.BowlerSelectionList.SelectedIndex==0
                && panel.BowlerSelectionList.Items[0].ToString()!.Contains("RECOMMENDED");
            CaptureCricketQa(panel,Path.Combine(directory,"review-recommended-bowler-first.png"));panel.ClearMatch();

            var allOut=CricketReliabilityChecks.CreateMatch(true);panel.SetMatch(allOut);
            while(!allOut.CurrentInnings!.IsComplete)
            {
                if(allOut.CurrentInnings.NeedsBatterSelection)allOut.SelectNewBatter(Enumerable.Range(0,allOut.CurrentInnings.BattingOrder.Count).First(i=>!allOut.CurrentInnings.BattingOrder[i].IsOut&&i!=allOut.CurrentInnings.StrikerIndex&&i!=allOut.CurrentInnings.NonStrikerIndex));
                if(allOut.CurrentInnings.NeedsBowlerSelection)allOut.ConfirmBowler(allOut.SuggestedNextBowler());
                allOut.RecordDelivery(0,CricketDeliveryType.Wicket,dismissal:DismissalType.Bowled);
            }
            await Task.Delay(150);CricketPrompt.CancelAll(panel.CricketRoot);
            var dismissed=allOut.CurrentInnings.BattingOrder.Where(p=>p.IsOut).Select(p=>p.DisplayName).ToArray();
            checks["tenth-wicket-keeps-dismissed-operator-name-grey"] = allOut.TotalWickets==10 && (dismissed.Contains(panel.StrikerName.Text)&&panel.StrikerName.Foreground==Brushes.Gray || dismissed.Contains(panel.NonStrikerName.Text)&&panel.NonStrikerName.Foreground==Brushes.Gray);
            CaptureCricketQa(panel,Path.Combine(directory,"review-all-out-dismissed-player-grey.png"));panel.ClearMatch();

            foreach(var type in new[]{CricketDeliveryType.Wide,CricketDeliveryType.Bye,CricketDeliveryType.LegBye})
            {
                var entry=new CricketDeliveryDialog(CricketReliabilityChecks.CreateMatch(true),initialType:type);window.Content=entry;await Task.Delay(60);
                var buttons=entry.RunsTiles.Children.OfType<Button>().Where(b=>b.Tag is string).ToArray();
                if(type==CricketDeliveryType.Wide)
                {
                    var two=buttons.Single(b=>Equals(b.Tag,"2"));Click(two);
                    checks["wide-extra-selection-has-check-mark-filled-highlight-and-state"] = Equals(two.Content,"✓  2") && System.Windows.Automation.AutomationProperties.GetItemStatus(two)=="Selected";
                    CaptureCricketQa(entry,Path.Combine(directory,"review-wide-strong-selection.png"));
                }
                else checks[$"{type}-does-not-offer-zero-runs"] = buttons.Single(b=>Equals(b.Tag,"0")).Visibility==Visibility.Collapsed;
                entry.Cancel();
            }
            var wicket=new CricketDeliveryDialog(CricketReliabilityChecks.CreateMatch(true));window.Content=wicket;wicket.DismissalChoice.SelectedIndex=3;await Task.Delay(80);
            checks["runout-crossing-controls-are-not-presented-to-operator"] = !wicket.RunOutCrossed.IsVisible && !wicket.RunOutNotCrossed.IsVisible;
            CaptureCricketQa(wicket,Path.Combine(directory,"review-simplified-runout.png"));wicket.Cancel();

            var root=new Grid();window.Content=root;await Task.Delay(50);
            var child=CricketPrompt.ChooseAsync(root,"Child options","Back returns to options. Close dismisses.",new("back","Back to options"),new("close","Close"),new("apply","Apply",true));
            await Task.Delay(80);root.UpdateLayout();
            var back=QaDescendants(root).OfType<Button>().First(b=>Equals(b.Tag,"back"));var close=QaDescendants(root).OfType<Button>().First(b=>Equals(b.Tag,"close"));
            checks["child-option-sheet-has-distinct-back-arrow-and-close"] = Equals(back.Content,"←")&&Equals(close.Content,"×");Click(back);checks["child-sheet-back-returns-a-separate-result"]=await child=="back";
#if DEBUG
            var priorMatch=_cricketMatch;var priorBug=_cricketScorebug;
            if(_cricketDiagnostics is null)
            {
                var previewBug=new CricketScorebugControl();_cricketMatch=CricketReliabilityChecks.CreateMatch(true);_cricketScorebug=previewBug;
                try
                {
                    OpenCricketDiagnostics();await Task.Delay(150);
                    var categories=QaDescendants((DependencyObject)_cricketDiagnostics!.Content).OfType<TabControl>().Single(t=>Equals(t.Tag,"cricket-diagnostics-categories"));
                    checks["developer-preview-tools-are-separated-into-three-categories"] = categories.Items.Count==3 && categories.Items.Cast<TabItem>().Select(t=>t.Header?.ToString()).SequenceEqual(new[]{"SCENARIOS","ANIMATIONS","CONTEXT"});
                    checks["developer-preview-category-tabs-share-operator-design"] = categories.Items.Cast<TabItem>().All(t=>t.Style==CricketControlPanelView.FindResource("PanelTab"));
                    CaptureCricketQa((FrameworkElement)_cricketDiagnostics.Content,Path.Combine(directory,"review-categorized-preview-tools.png"));
                }
                finally {_cricketDiagnostics?.Close();_cricketMatch=priorMatch;_cricketScorebug=priorBug;previewBug.Dispose();}
            }
#endif
        }
        finally {window.Close();}
        return checks;
    }
}

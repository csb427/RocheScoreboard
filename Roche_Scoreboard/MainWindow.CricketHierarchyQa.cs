using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Button = System.Windows.Controls.Button;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private async Task<Dictionary<string, bool>> RunCricketHierarchyChecksAsync(string directory)
    {
        var checks = new Dictionary<string, bool>();
        var match = CricketReliabilityChecks.CreateMatch(true);
        var panel = new CricketControlPanel(); var bug = new CricketScorebugControl(); var summary = new CricketSummaryControl();
        var stage = new Grid(); stage.Children.Add(bug); stage.Children.Add(summary);
        var output = new Window { Content = stage, Width = 386, Height = 193, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        var window = new Window { Content = panel, Width = 1050, Height = 780, ShowInTaskbar = false };
        panel.SetMatch(match); panel.SetScorebug(bug); panel.SetSummaryControl(summary); output.Show(); window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Popup? Menu() => (Popup?)typeof(CricketControlPanel).GetField("_playerMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel);
        try
        {
            match.TossPending=true;match.RaiseMatchChanged();
            checks["all-cricket-player-presentations-wait-until-toss-is-recorded"] = !panel.Presentationbatting.IsEnabled && !panel.Presentationbowling.IsEnabled && !panel.Presentationsummary.IsEnabled;
            match.TossPending=false;match.RaiseMatchChanged();
            match.SetBreak(CricketBreakKind.Rain); await Task.Delay(100);
            checks["break-gate-is-full-panel-clear-direct-resume-action"] = panel.ScoringLockBanner.Visibility == Visibility.Visible && Grid.GetRowSpan(panel.ScoringLockBanner) == 2
                && panel.ScoringReadyHint.Text == "RAIN DELAY" && panel.ScoringReadyDetail.Text == "Click to resume play" && panel.ScoringCommands.Opacity < .2;
            CaptureCricketQa(panel, Path.Combine(directory, "hierarchy-rain-direct-resume.png"));
            Click(panel.ScoringLockBanner); await Task.Delay(100);
            checks["break-gate-resumes-without-opening-options"] = match.BreakKind == CricketBreakKind.None && panel.ScoringCommands.IsEnabled && !QaDescendants(panel).OfType<Border>().Any(b=>Equals(b.Tag,"cricket-decision-sheet"));
            InvokeCricketQa(panel, "ShowPlayerMenu", panel.StrikerBar, false, match.CurrentInnings!.StrikerIndex); await Task.Delay(100);
            var popup = Menu(); var buttons = popup?.Child is DependencyObject content ? QaDescendants(content).OfType<Button>().ToArray() : [];
            checks["batter-actions-are-anchored-menu-with-figures-and-strike"] = popup?.IsOpen == true && popup.Placement == PlacementMode.Custom && ReferenceEquals(popup.PlacementTarget,panel.StrikerBar)
                && buttons.Any(b => Equals(b.Tag,"replace-batter")) && buttons.Any(b => Equals(b.Tag,"swap-strike")) && buttons.Any(b => Equals(b.Tag,"figures"));
            if (popup?.Child is FrameworkElement menuSurface) CaptureCricketQa(menuSurface, Path.Combine(directory,"hierarchy-batter-context-menu.png"));
            var swap = buttons.FirstOrDefault(b => Equals(b.Tag,"swap-strike")); int previous = match.CurrentInnings.StrikerIndex; if (swap is not null) Click(swap);
            checks["context-swap-strike-is-direct-and-closes-menu"] = match.CurrentInnings.StrikerIndex != previous && Menu() is null;
            InvokeCricketQa(panel,"ShowPlayerMenu",panel.BowlerBar,true,match.CurrentInnings.CurrentBowlerIndex); await Task.Delay(70);
            var figures = Menu()?.Child is DependencyObject bowlerMenu ? QaDescendants(bowlerMenu).OfType<Button>().FirstOrDefault(b=>Equals(b.Tag,"figures")) : null;
            if (figures is not null) Click(figures); await WaitCricketQaStateAsync(window, () => panel.MainTabControl.SelectedItem == panel.ScorecardTab && Menu() is null);
            checks["context-edit-bowler-figures-opens-scorecard-without-extra-dialog"] = panel.MainTabControl.SelectedItem == panel.ScorecardTab && Menu() is null;
            CaptureCricketQa(panel,Path.Combine(directory,"hierarchy-player-figures-scorecard.png"));
            panel.MainTabControl.SelectedItem = panel.MatchTab;
            InvokeCricketQa(panel,"ShowPlayerMenu",panel.StrikerBar,false,match.CurrentInnings.StrikerIndex);
            match.RecordDelivery(1,CricketDeliveryType.Runs);
            checks["score-change-closes-stale-player-menu"] = Menu() is null;
            match.SetBreak(CricketBreakKind.Drinks); panel.BreakPresentationRotationEnabled = true;
#if DEBUG
            panel.PreviewNextBreakPresentation(); checks["break-rotation-first-shows-batting-without-scoring-mutation"] = panel.Presentationbatting.BorderBrush.ToString()=="#FF58A6FF" && match.TotalRuns==1;
            for(int cycle=0;cycle<6 && panel.Presentationsummary.BorderBrush.ToString()!="#FF58A6FF";cycle++)panel.PreviewNextBreakPresentation();
            checks["break-rotation-includes-distinct-match-summary"] = panel.Presentationsummary.BorderBrush.ToString()=="#FF58A6FF";
#endif
            Click(panel.Presentationsummary); await Task.Delay(500); CaptureCricketQa(summary,Path.Combine(directory,"hierarchy-break-summary-386x193.png"));
            int deliveries=match.CurrentInnings.Deliveries.Count; match.SetBreak(CricketBreakKind.None);
            await WaitCricketQaStateAsync(window, () => summary.Visibility != Visibility.Visible && panel.ScoringCommands.IsEnabled);
            checks["resuming-break-restores-scorebug-and-preserves-deliveries"] = summary.Visibility != Visibility.Visible && match.CurrentInnings.Deliveries.Count==deliveries && panel.ScoringCommands.IsEnabled;
            match.CurrentInnings.IsComplete = true; match.RaiseMatchChanged(); await Task.Delay(140);
            CricketPrompt.CancelAll(panel.CricketRoot); await WaitCricketQaStateAsync(window, () => !QaDescendants(panel).OfType<Border>().Any(border => Equals(border.Tag, "cricket-decision-sheet")));
            panel.MainTabControl.SelectedItem=panel.ScorecardTab; panel.MainTabControl.SelectedItem=panel.MatchTab;
            checks["completed-innings-still-has-full-action-after-scorecard-review"] = panel.ScoringLockBanner.Visibility==Visibility.Visible && panel.ScoringReadyHint.Text=="INNINGS COMPLETE" && Equals(panel.EndInningsButton.Content,"Next innings");
            CaptureCricketQa(panel,Path.Combine(directory,"hierarchy-completed-innings-gate.png"));
            window.Width=420;window.Height=320;await Task.Delay(180);
            checks["small-operator-gate-retains-scroll-reachability"] = panel.ConsoleViewport.ScrollableWidth<1 && panel.ScoringLockBanner.ActualWidth>200;
            window.Width=1050;window.Height=780;
            CricketPrompt.CancelAll(panel.CricketRoot);
            bool next = match.TryStartAdditionalInnings(false,out _);
            if(next) {match.SetStriker(0);match.SetNonStriker(1);match.ConfirmBowler(0);match.RecordDelivery(4,CricketDeliveryType.Four);}
            await Task.Delay(180);
            var finishSheet=QaDescendants(panel).OfType<Border>().FirstOrDefault(b=>Equals(b.Tag,"cricket-decision-sheet"));
            var firstAction=finishSheet is null ? null : QaDescendants(finishSheet).OfType<Button>().FirstOrDefault(b=>!Equals(b.Content,"×"));
            checks["limited-overs-successful-chase-prioritises-end-game"] = next && match.CompletionDecisionRequired && Equals(firstAction?.Tag,"finish");
            CaptureCricketQa(panel,Path.Combine(directory,"hierarchy-chase-complete-primary-end-game.png"));
        }
        finally { panel.ClearMatch(); window.Close(); output.Close(); }

        var fixture=CricketReliabilityChecks.CreateMatch(true); var entry=new CricketDeliveryDialog(fixture); var entryWindow=new Window {Content=entry,Width=620,Height=760,ShowInTaskbar=false};entryWindow.Show();
        try
        {
            foreach(int dismissal in new[]{0,1})
            {
                entry.DismissalChoice.SelectedIndex=dismissal;await Task.Delay(50);
                var choices=entry.DismissedTiles.Children.OfType<Button>().ToArray();
                checks[$"striker-only-dismissal-{dismissal}-clearly-disables-nonstriker"] = choices.Length==2 && choices.Count(b=>b.IsEnabled)==1 && choices.Any(b=>!b.IsEnabled && b.Opacity<.25) && entry.DismissedBatterHint.Visibility==Visibility.Visible;
            }
            CaptureCricketQa(entry,Path.Combine(directory,"hierarchy-wicket-striker-eligibility.png"));
            entry.DismissalChoice.SelectedIndex=3;await Task.Delay(50);
            checks["runout-restores-both-dismissed-batter-choices"] = entry.DismissedTiles.Children.OfType<Button>().All(b=>b.IsEnabled && b.Opacity==1) && entry.DismissedBatterHint.Visibility==Visibility.Collapsed;
        }
        finally {entry.Cancel();entryWindow.Close();}
        return checks;
    }
    private static async Task<bool> WaitCricketQaStateAsync(Window window, Func<bool> predicate)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < 3500)
        {
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), System.Windows.Threading.DispatcherPriority.Background);
            if (predicate()) return true;
            await Task.Delay(12);
        }
        return predicate();
    }
}

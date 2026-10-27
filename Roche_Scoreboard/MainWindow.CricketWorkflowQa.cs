using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private async Task<Dictionary<string, bool>> RunCricketWorkflowVisualChecksAsync(string directory)
    {
        Dictionary<string, bool> checks = new();
        var match = CricketReliabilityChecks.CreateMatch(true);
        match.TeamALogoPath = CreateCricketQaLogo(directory, "A", Colors.RoyalBlue);
        match.TeamBLogoPath = CreateCricketQaLogo(directory, "B", Colors.Firebrick);
        var bug = new CricketScorebugControl { EventAnimationsEnabled=false };
        var host = new Window { Width = 386, Height = 193, WindowStyle = WindowStyle.None, Content = bug, ShowInTaskbar = false };
        host.Show();
        try
        {
            match.MatchChanged += () => bug.UpdateFromMatch(match);
            bug.UpdateFromMatch(match);
            await Task.Delay(200);
            double fixedSize = bug.ScoreRuns.FontSize;
            match.RecordDelivery(6, CricketDeliveryType.Six);
            for (int i = 0; i < 5; i++) match.RecordDelivery(0, CricketDeliveryType.Dot);
            await Task.Delay(500);
            checks["output-completed-over-remains-visible-before-bowler-confirmation"] = bug.CurrentOverBallsPanel.Children.OfType<Border>().Count(pip => (string?)pip.Tag != "") == 6;
            checks["main-score-stacks-wickets-for-runs-with-fixed-digit-size"] = bug.ScoreSlash.Text == "FOR"
                && bug.ScoreRuns.FontSize == bug.ScoreWickets.FontSize && Math.Abs(bug.ScoreRuns.FontSize - fixedSize) < .01;
            checks["main-logos-live-in-team-bars-not-behind-score"] = bug.BattingLogo.Visibility == Visibility.Collapsed
                && bug.BattingHeaderLogo.Source is not null && bug.BowlingHeaderLogo.Source is not null;
            checks["strike-arrow-remains-readable-on-dark-player-bars"] = ContrastHelper.ContrastRatio(bug.BattingIndicatorBrush.Color, Color.FromRgb(6, 8, 11)) >= 4.5;
            checks["main-team-bars-and-score-use-gradients"] = bug.BattingHeaderPanel.Background is LinearGradientBrush
                && bug.BowlingHeaderPanel.Background is LinearGradientBrush && bug.LeftScoreRail.Background is LinearGradientBrush;
            CaptureCricketQa(bug, Path.Combine(directory, "over-complete-held.png"));
            match.ConfirmBowler(1);
            await Task.Delay(100);
            match.RecordDelivery(6, CricketDeliveryType.Six);
            await Task.Delay(800);
            checks["scoring-during-over-clear-cancels-stale-animation"] = bug.CurrentOverBallsPanel.Children.Count == 6
                && (string?)((Border)bug.CurrentOverBallsPanel.Children[0]).Tag == "6"
                && ((ScaleTransform)((Border)bug.CurrentOverBallsPanel.Children[0]).RenderTransform).ScaleY > .99;
            for (int i = 0; i < 5; i++) match.RecordDelivery(0, CricketDeliveryType.Dot);
            match.ConfirmBowler(2);
            await Task.Delay(1000);
            checks["over-clearing-folds-settle-to-six-empty-circles"] = bug.CurrentOverBallsPanel.Children.Count == 6
                && bug.CurrentOverBallsPanel.Children.OfType<Border>().All(pip => (string?)pip.Tag == "" && ((ScaleTransform)pip.RenderTransform).ScaleY > .99);
            CaptureCricketQa(bug, Path.Combine(directory, "new-over-six-empty-circles.png"));

            var panel = new CricketControlPanel(); panel.SetMatch(match);
            host.Content = panel; host.Width = 1200; host.Height = 780;
            await Task.Delay(200); CaptureCricketQa(panel, Path.Combine(directory, "team-themed-scoring-panel.png"));
            checks["delivery-log-can-jump-to-a-single-over"] = panel.DeliveryOverChoice.Items.Count >= 3;
            panel.DeliveryOverChoice.SelectedIndex = 1;
            checks["delivery-log-over-filter-hides-unselected-overs"] = panel.EventLog.Children.OfType<StackPanel>().Count(group => group.Visibility == Visibility.Visible) == 1;
            panel.DeliveryOverChoice.SelectedIndex = 0;
            checks["background-logos-never-expand-operator-cards"] = panel.BattingZone.ActualHeight < 250 && panel.BowlingZone.ActualHeight < 250;
            checks["operator-batting-and-bowling-zones-use-live-team-theming"] = panel.BattingZone.Background is LinearGradientBrush
                && panel.BowlingZone.Background is LinearGradientBrush && panel.BattingZoneLogo.Source is null && panel.BowlingZoneLogo.Source is null
                && ((Grid)panel.BattingZone.Child).Background is null && ((Grid)panel.BowlingZone.Child).Background is null;
            panel.MainTabControl.SelectedItem = panel.OutputTab;
            await Task.Delay(250); CaptureCricketQa(panel, Path.Combine(directory, "afl-style-cricket-output-tab.png"));
            checks["cricket-tabs-use-afl-full-width-navigation-template"] = panel.MainTabControl.Style == panel.FindResource("OperatorTabs");
            host.Width = 420; host.Height = 320;
            await Task.Delay(200); CaptureCricketQa(panel, Path.Combine(directory, "cricket-output-tab-small.png"));
            checks["small-cricket-output-tab-avoids-horizontal-clipping"] = panel.ConsoleViewport.ScrollableWidth < 1;
            var outputScroll=QaDescendants(panel).OfType<ScrollViewer>().Where(view=>view.IsVisible).OrderByDescending(view=>view.ScrollableHeight).First();
            outputScroll.ScrollToVerticalOffset(100);
            await Task.Delay(160);
            checks["custom-scroll-handles-activate-on-scroll"] = ScrollActivity.GetActive(outputScroll)
                && QaDescendants(outputScroll).OfType<ScrollBar>().Any(bar=>bar.Orientation==System.Windows.Controls.Orientation.Vertical && bar.ActualWidth>0 && bar.ActualWidth<=12);
            await Task.Delay(1700);
            checks["custom-scroll-handles-rest-after-scroll"] = !ScrollActivity.GetActive(outputScroll);
            panel.MainTabControl.SelectedItem = panel.CricketSettingsTab;
            await Task.Delay(200);
            var settingsWorkspace=QaDescendants(panel).OfType<SettingsWorkspace>().First();
            settingsWorkspace.Scroller.ScrollToBottom();
            await WaitCricketQaStateAsync(host,()=>CricketQaBoundsWithin(panel.CricketRollingFigures,settingsWorkspace.Scroller));
            var listBottom = panel.CricketRollingFigures.TransformToAncestor(panel).Transform(new Point(0, panel.CricketRollingFigures.ActualHeight));
            checks["small-cricket-settings-last-controls-remain-reachable"] = listBottom.Y <= panel.ActualHeight + 1;
            CaptureCricketQa(panel, Path.Combine(directory, "cricket-settings-tab-small-bottom.png"));
            panel.ClearMatch();
        }
        finally { bug.Dispose(); host.Close(); }

        var entry = CricketReliabilityChecks.CreateMatch(true);
        entry.TeamALogoPath = match.TeamALogoPath;
        bool DialogAction(CricketDeliveryDialog dialog, Action<CricketDeliveryDialog> action)
        {
            var modal=new Window { Content=dialog,Title="Delivery test harness",Width=double.IsNaN(dialog.Width) ? 620 : dialog.Width,Height=double.IsNaN(dialog.Height) ? 710 : dialog.Height,ShowInTaskbar=false };
            dialog.Width=dialog.Height=double.NaN;
            dialog.Completed+=applied=>modal.DialogResult=applied;
            dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(new Action(()=>action(dialog)));
            return modal.ShowDialog()==true;
        }
        var extras = new CricketDeliveryDialog(entry, initialType: CricketDeliveryType.NoBall) { Width = 420, Height = 380 };
        bool extrasApplied = DialogAction(extras, dialog =>
        {
            dialog.NoBallChoice.SelectedIndex = 2; dialog.RunsBox.Text = "2"; dialog.UpdateLayout();
            checks["no-ball-dialog-confirm-button-remains-visible-in-short-window"] = CricketQaBoundsWithin(dialog.ApplyButton, dialog);
            CaptureCricketQa(dialog, Path.Combine(directory, "no-ball-entry-short.png"));
            InvokeCricketQa(dialog, "Apply_Click", dialog.ApplyButton, new RoutedEventArgs());
            if (dialog.IsVisible) Window.GetWindow(dialog)?.Close();
        });
        checks["no-ball-dialog-actually-applies-leg-byes-plus-one-penalty"] = extrasApplied && entry.TotalRuns == 3
            && entry.CurrentInnings!.LegByes == 2 && entry.CurrentInnings.NoBalls == 1;
        entry.RecordDelivery(2, CricketDeliveryType.Runs);
        var original = entry.CurrentInnings!.Deliveries[0];
        var editor = new CricketDeliveryDialog(entry, original) { Width = 520, Height = 620 };
        bool editApplied = false;
        var correctionWindow = new Window { Content=editor, Width=550, Height=700, ShowInTaskbar=false };
        editor.Completed += applied => editApplied=applied;
        correctionWindow.Show();
        try
        {
            await Task.Delay(100);
            editor.BatterChoice.SelectedIndex=1;
            editor.TypeChoice.SelectedIndex=(int)CricketDeliveryType.Runs;
            editor.RunsBox.Text="3";
            checks["delivery-editor-strike-choice-only-lists-the-two-batters-at-that-ball"] = editor.BatterTiles.Children.Count==2;
            InvokeCricketQa(editor,"Apply_Click",editor.ApplyButton,new RoutedEventArgs());
            await WaitCricketQaStateAsync(correctionWindow, () => QaDescendants(editor).OfType<System.Windows.Controls.Button>().Any(button => Equals(button.Tag,"innings")));
            var scope=QaDescendants(editor).OfType<System.Windows.Controls.Button>().SingleOrDefault(button=>Equals(button.Tag,"innings"));
            checks["delivery-editor-asks-strike-scope-before-applying-new-batter"] = scope is not null && entry.DeliveryRevision==0 && !editApplied;
            CaptureCricketQa(editor,Path.Combine(directory,"delivery-correction-scope.png"));
            scope?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            // Applying resumes only after the confirmation surface has finished closing.
            await WaitCricketQaStateAsync(correctionWindow, () => editApplied && entry.DeliveryRevision == 1);
        }
        finally { editor.Cancel(); correctionWindow.Close(); }
        checks["delivery-editor-applies-correction-and-recalculates-history"] = editApplied && entry.DeliveryRevision == 1
            && entry.TotalRuns == 5 && entry.CurrentInnings.TotalExtras == 0 && entry.CurrentInnings.BattingOrder[1].Runs == 3;
        var wicketMatch = CricketReliabilityChecks.CreateMatch(true);
        var wicket = new CricketDeliveryDialog(wicketMatch) { Width = 520, Height = 620 };
        bool wicketApplied = DialogAction(wicket, dialog =>
        {
            dialog.DismissalChoice.SelectedIndex = 3; dialog.DismissedChoice.SelectedIndex = 1;
            dialog.FielderChoice.SelectedIndex = 4; dialog.RunsBox.Text = "1"; dialog.UpdateLayout();
            CaptureCricketQa(dialog, Path.Combine(directory, "wicket-runout-entry.png"));
            InvokeCricketQa(dialog, "Apply_Click", dialog.ApplyButton, new RoutedEventArgs());
            if (dialog.IsVisible) Window.GetWindow(dialog)?.Close();
        });
        checks["wicket-dialog-actually-records-nonstriker-runout-and-fielder"] = wicketApplied
            && wicketMatch.CurrentInnings!.BattingOrder[1].IsOut && wicketMatch.CurrentInnings.BattingOrder[1].DismissalFielder is not null
            && wicketMatch.CurrentInnings.BowlingAttack[0].Wickets == 0;
        return checks;
    }
}

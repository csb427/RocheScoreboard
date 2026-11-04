using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Roche_Scoreboard.Services;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    // Runs through the existing WPF workflow harness on a Windows dispatcher.
    // Compilation on another platform is not evidence this GUI check ran.
    private Dictionary<string, bool> RunHomeCommandChecks()
    {
        var checks = new Dictionary<string, bool>();
        var fixture = new MainWindow();
        try
        {
            fixture.SportSelectionPanel.Visibility = Visibility.Visible;
            fixture.MainContent.Visibility = Visibility.Collapsed;
#if WINDOWS
            fixture.SportSelectionPanel.PrepareForDisplay();
            fixture.Show(); fixture.UpdateLayout();
            checks["home-history-entry-is-visible-and-labelled"] =
                fixture.SportSelectionPanel.HistoryButton.Visibility == Visibility.Visible
                && fixture.SportSelectionPanel.HistoryButton.Content?.ToString()?.StartsWith("Match history", System.StringComparison.Ordinal) == true;
            var completedState = new GameState
            {
                Sport = "afl",
                MatchState = new Roche_Scoreboard.Models.MatchManager().ToState(),
                Messages = [],
                TickerSlots = []
            };
            completedState.MatchState.CompletedQuarterCount = 4;
            completedState.MatchState.QuarterSnapshots =
            [
                new Roche_Scoreboard.Models.QuarterSnapshot(), new Roche_Scoreboard.Models.QuarterSnapshot(),
                new Roche_Scoreboard.Models.QuarterSnapshot(), new Roche_Scoreboard.Models.QuarterSnapshot()
            ];
            var checkpoint = new MatchCheckpointStore.Checkpoint(1, "qa", System.DateTimeOffset.UtcNow,
                "Harbour v Rovers", "AFL · full time", completedState);
            var history = new Views.MatchHistoryView(true, [checkpoint]);
            history.ActionChosen += (_, _) => false;
            try
            {
                fixture.SportSelectionPanel.ShowHistory(history); fixture.UpdateLayout();
                checks["history-is-hosted-in-home-window"] = ReferenceEquals(Window.GetWindow(history), fixture)
                    && fixture.SportSelectionPanel.IsHistoryActive(history)
                    && fixture.SportSelectionPanel.HistoryViewHost.Content == history;
                checks["history-uses-shared-operator-theme-and-fills-home-sheet"] =
                    OperatorDesign.GetIsEnabled(fixture.SportSelectionPanel)
                    && fixture.SportSelectionPanel.HistoryOverlay.Visibility == Visibility.Visible
                    && fixture.SportSelectionPanel.HistoryPanel.ActualWidth >= 500
                    && fixture.SportSelectionPanel.HistoryPanel.ActualHeight >= 320;
                checks["history-completed-checkpoint-is-reviewable-and-restartable-but-not-continuable"] =
                    history.ActionButtons.Count == 3 && history.ActionButtons[0].Content?.ToString() == "Review"
                    && !history.ActionButtons[1].IsEnabled && history.ActionButtons[2].IsEnabled;
                var stateBeforeReview = checkpoint.State.MatchState!.CompletedQuarterCount;
                history.ActionButtons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                checks["history-review-is-inline-and-read-only"] = history.IsReviewing
                    && checkpoint.State.MatchState.CompletedQuarterCount == stateBeforeReview
                    && ReferenceEquals(Window.GetWindow(history), fixture);
                history.ActionButtons[2].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                checks["history-restart-declines-without-closing-or-changing-current-view"] = history.IsReviewing
                    && fixture.SportSelectionPanel.IsHistoryActive(history);
            }
            finally { fixture.SportSelectionPanel.CloseHistory(history, restoreFocus: false); }
            var emptyHistory = new Views.MatchHistoryView(true, []);
            try
            {
                fixture.SportSelectionPanel.ShowHistory(emptyHistory); fixture.UpdateLayout();
                checks["history-empty-state-is-visible-in-home-sheet"] = emptyHistory.HasEmptyState
                    && ReferenceEquals(Window.GetWindow(emptyHistory), fixture);
            }
            finally { fixture.SportSelectionPanel.CloseHistory(emptyHistory, restoreFocus: false); }
#endif
            checks["command-bar-opens-once"] = fixture.OpenCommandBar() && !fixture.OpenCommandBar();
            fixture.CommandSearch.Text = "zzzzzz";
            checks["command-empty-result-does-not-execute"] = fixture.CommandResults.Items.Count == 0 && !fixture.ExecuteCommand("does-not-exist");
            fixture.OpenCommandBar();
            fixture.CommandSearch.Text = "sound";
            checks["command-synonym-is-discoverable"] = fixture._visibleCommands.Any(c => c.Id == "settings");
            fixture.CloseCommandBar();
            checks["command-close-restores-input"] = fixture.SportSelectionPanel.IsEnabled && fixture.CommandOverlay.Visibility == Visibility.Collapsed;
            checks["home-does-not-activate-output"] = fixture._outputActivationRequired;
            checks["command-typo-uses-local-ranker"] = OperatorCommandSearch.Rank("bolwer",
                [new("bowler", "Change bowler", "bowling change")]).Single().Approximate;
        }
        finally { fixture.CloseCommandBar(); fixture.Close(); }
        return checks;
    }
}

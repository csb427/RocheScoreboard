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
            checks["home-history-entry-is-visible-and-labelled"] =
                fixture.SportSelectionPanel.HistoryButton.Visibility == Visibility.Visible
                && fixture.SportSelectionPanel.HistoryButton.Content?.ToString()?.StartsWith("History", System.StringComparison.Ordinal) == true;
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
            var history = new Views.MatchHistoryWindow(true, [checkpoint]) { Owner = fixture };
            foreach (var dictionary in fixture.Resources.MergedDictionaries) history.Resources.MergedDictionaries.Add(dictionary);
            try
            {
                history.Show(); history.UpdateLayout();
                checks["history-completed-checkpoint-is-reviewable-and-restartable-but-not-continuable"] =
                    history.ActionButtons.Count == 3 && history.ActionButtons[0].Content?.ToString() == "Review"
                    && !history.ActionButtons[1].IsEnabled && history.ActionButtons[2].IsEnabled;
            }
            finally { history.Close(); }
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

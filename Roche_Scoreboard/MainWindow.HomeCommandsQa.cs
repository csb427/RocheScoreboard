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

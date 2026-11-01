using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    // Explicit existing workflow self-test only. These checks need a native
    // Windows dispatcher; a successful Linux cross-build does not run them.
    private async Task<Dictionary<string, bool>> RunPresentationLifetimeChecksAsync()
    {
        var checks = new Dictionary<string, bool>();
        string layoutPath = Path.Combine(SelfTestStorage.DocumentsDirectory, "window_layout.json");
        byte[]? priorLayout = File.Exists(layoutPath) ? File.ReadAllBytes(layoutPath) : null;
        var fixture = new MainWindow(); fixture.Show();
        try
        {
            fixture.SportSelectionPanel.Visibility = Visibility.Collapsed;
            fixture.FinalizeAflSetup(new() { HomeName = "Home", AwayName = "Away", WeatherLocation = "" });
            await Task.Delay(600);
            var output = fixture._displayWindow!;
            void CancelClose(object? sender, CancelEventArgs args) => args.Cancel = true;
            output.Closing += CancelClose; output.Close(); output.Closing -= CancelClose;
            checks["output-cancel-close-keeps-live-activation"] = output.IsVisible && !fixture._outputActivationRequired;
            fixture._match.AddGoal(TeamSide.Home); fixture.ResetScoreAnimationQueue();
            string state = JsonSerializer.Serialize(fixture._match.ToState());
            fixture.SwitchToStatsScreen_Click(fixture, new RoutedEventArgs());
            await Task.Delay(700);
            output.Left = 80; output.Top = 90; output.Width = 410; output.Height = 207;
            output.UpdateLayout(); var bounds = WindowLayoutStorage.Capture(output);
            int generation = fixture._screenTransitionVersion; output.Close();
            checks["output-close-invalidates-transition"] = fixture._screenTransitionVersion > generation;
            checks["output-close-saves-current-bounds"] = WindowLayoutStorage.Get(WindowLayoutStorage.AflDisplayKey) == bounds;
            fixture.EnsureScoreboardWindow();
            checks["output-close-requires-explicit-show"] = !fixture._displayWindow!.IsVisible && fixture._outputActivationRequired;
            fixture.EnableOperatorOutput();
            checks["output-reopen-restores-stats-without-scorebug-flash"] = fixture._statsScreen!.IsVisible && !fixture._scorebug!.IsVisible
                && fixture._activeScreenKey == "stats" && fixture._displayWindow.IsVisible;
            checks["output-reopen-preserves-match"] = JsonSerializer.Serialize(fixture._match.ToState()) == state;
            fixture.SwitchToScorebug_Click(fixture, new RoutedEventArgs()); await Task.Delay(700);
            int shown = 0, cancelled = 0; DispatcherTimer? timer = null;
            fixture._autoOverlaysEnabled = true;
            fixture.ScheduleAutoOverlay(() => shown++, TimeSpan.FromMilliseconds(50), () => timer, value => timer = value,
                allowWhenClockStopped: true, onCancelled: () => cancelled++);
            fixture.GoHome(); await Task.Delay(250);
            checks["output-home-cancels-delayed-overlay-once"] = shown == 0 && cancelled == 1 && timer is null
                && fixture._scheduledOverlayCancellations.Count == 0;
        }
        finally
        {
            fixture.Close();
            if (priorLayout is null) File.Delete(layoutPath);
            else { Directory.CreateDirectory(Path.GetDirectoryName(layoutPath)!); File.WriteAllBytes(layoutPath, priorLayout); }
        }
        return checks;
    }
}

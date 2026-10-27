using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private async Task<Dictionary<string, bool>> RunCricketReviewChecksAsync(string directory)
    {
        var checks = new Dictionary<string, bool>();
        var match = CricketReliabilityChecks.CreateMatch(true);
        match.TeamALogoPath=CreateCricketQaLogo(directory,"toss-sized",System.Windows.Media.Colors.SteelBlue);
        match.TeamBLogoPath=match.TeamALogoPath;match.TossPending=true;
        var panel = new CricketControlPanel(); panel.SetMatch(match);
        var window = new Window { Content = panel, Width = 1050, Height = 780, ShowInTaskbar = false }; window.Show();
        try
        {
            InvokeCricketQa(panel,"OpenToss_Click",this,new RoutedEventArgs());panel.TossHomeChoice.IsChecked=true;panel.TossBatChoice.IsChecked=true;await Task.Delay(100);panel.UpdateLayout();
            checks["toss-logo-images-cannot-inflate-team-choice-height"] = panel.TossHomeChoice.ActualHeight<=90 && panel.TossAwayChoice.ActualHeight<=90;
            CaptureCricketQa(panel,Path.Combine(directory,"compact-toss-bounded-logo-cards.png"));
            InvokeCricketQa(panel,"CloseToss_Click",this,new RoutedEventArgs());match.TossPending=false;match.RaiseMatchChanged();
            foreach (var type in new[] { CricketDeliveryType.Dot, CricketDeliveryType.Wide, CricketDeliveryType.Dot, CricketDeliveryType.NoBall, CricketDeliveryType.Dot, CricketDeliveryType.Dot })
                match.RecordDelivery(type is CricketDeliveryType.Wide or CricketDeliveryType.NoBall ? 1 : 0, type);
            await Task.Delay(100);
            checks["six-physical-deliveries-with-extras-do-not-lock-ui"] = match.CurrentInnings!.LegalBallsBowled == 4
                && !match.CurrentInnings.NeedsBowlerSelection && panel.ScoringCommands.IsEnabled
                && panel.ScoringLockBanner.Visibility == Visibility.Collapsed && panel.ScoringReadyHint.Text == ""
                && panel.BowlerSelectionOverlay.Visibility == Visibility.Collapsed;
            match.RecordDelivery(0, CricketDeliveryType.Dot); match.RecordDelivery(0, CricketDeliveryType.Dot); await Task.Delay(100);
            checks["sixth-legal-ball-opens-required-bowler-selection"] = match.CurrentInnings.NeedsBowlerSelection && panel.BowlerSelectionOverlay.Visibility == Visibility.Visible;
            InvokeCricketQa(panel, "SkipPlayerSelection_Click", this, new RoutedEventArgs());
            InvokeCricketQa(panel, "ScoringLock_Click", panel.ScoringLockBanner, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            checks["clickable-scoring-guidance-reopens-required-selector"] = panel.BowlerSelectionOverlay.Visibility == Visibility.Visible;
            match.UndoLastDelivery(); await Task.Delay(100);
            checks["undoing-over-boundary-closes-stale-bowler-selector"] = !match.CurrentInnings.NeedsBowlerSelection
                && panel.BowlerSelectionOverlay.Visibility == Visibility.Collapsed && panel.ScoringCommands.IsEnabled;
            CaptureCricketQa(panel, Path.Combine(directory, "extras-over-still-live.png"));
            var lineup = match.GetLineup(true); lineup[2] = lineup[2] with { BattingPosition = 4 }; lineup[3] = lineup[3] with { BattingPosition = 3 };
            match.TryUpdateTeam(true, match.TeamAName, match.TeamAAbbr, lineup, out _);
            match.RecordDelivery(0, CricketDeliveryType.Wicket, dismissal: DismissalType.Bowled); await Task.Delay(100);
            checks["replacement-selector-honours-edited-batting-order-with-stable-identities"] = panel.BatterSelectionList.Tag is List<int> order && order.FirstOrDefault(-1) == 3;
            InvokeCricketQa(panel,"SkipPlayerSelection_Click",this,new RoutedEventArgs());
            InvokeCricketQa(panel,"ResetMatch_Click",this,new RoutedEventArgs());await Task.Delay(100);
            var cancel=QaDescendants(panel).OfType<System.Windows.Controls.Button>().FirstOrDefault(b=>Equals(b.Tag,"cancel"));
            checks["reset-confirmation-default-focus-keeps-the-match"] = cancel is not null && Equals(cancel.Content,"×")
                && (cancel.IsKeyboardFocused || ReferenceEquals(FocusManager.GetFocusedElement(FocusManager.GetFocusScope(cancel)),cancel));
            cancel?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await WaitCricketQaStateAsync(window, () => !QaDescendants(panel).OfType<Border>().Any(border => Equals(border.Tag, "cricket-decision-sheet")));
        }
        finally { panel.ClearMatch(); window.Close(); }

        var fixture = CricketReliabilityChecks.CreateMatch(true);
        var entry = new CricketDeliveryDialog(fixture); var entryWindow = new Window { Content = entry, Width = 620, Height = 760, ShowInTaskbar = false }; entryWindow.Show();
        try
        {
            entry.DismissalChoice.SelectedIndex = 3; entry.DismissedChoice.SelectedIndex = 1; entry.RunOutChoice.SelectedIndex = 2;
            entry.RunsBox.Text = "1"; entry.RunOutCrossed.IsChecked = true; await Task.Delay(100);
            checks["wicket-entry-is-neutral-and-fielding-uses-custom-dropdown"] = entry.TeamLogo.Visibility == Visibility.Collapsed
                && entry.FielderChoice.Visibility == Visibility.Visible && entry.FielderTiles.Visibility == Visibility.Collapsed;
            checks["runout-previews-the-next-facing-batter"] = entry.PreviewText.Text.Contains("Next on strike:") && entry.PreviewText.Text.Contains("no-ball");
            CaptureCricketQa(entry, Path.Combine(directory, "runout-crossing-next-strike-preview.png"));
            InvokeCricketQa(entry, "Apply_Click", entry.ApplyButton, new RoutedEventArgs()); await Task.Delay(100);
            checks["runout-entry-records-crossing-and-one-penalty"] = fixture.TotalRuns == 2 && fixture.TotalWickets == 1
                && fixture.CurrentInnings!.LegalBallsBowled == 0 && fixture.CurrentInnings.Deliveries.Single().RunOutCrossed
                && fixture.CurrentInnings.NoBalls == 1;
        }
        finally { entry.Cancel(); entryWindow.Close(); }

        var wizard = new CricketSetupWizard();
        InvokeCricketQa(wizard, "OversPreset45_Click", wizard, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left));
        checks["limited-overs-has-45-over-preset"] = wizard.OversDisplay.Text == "45";
        var settings = new GlobalSettingsScreen(); bool clock = false, custom = false, builtin = false;
        settings.ScreensaverClockChanged += value => clock = !value; settings.ScreensaverMediaRequested += () => custom = true; settings.BuiltInScreensaverRequested += () => builtin = true;
        settings.SetScreensaverPreferences(null, true); settings.ScreensaverClockToggle.IsChecked = false;
        InvokeCricketQa(settings, "ScreensaverSource_Click", new System.Windows.Controls.Button { Tag = "custom" }, new RoutedEventArgs());
        InvokeCricketQa(settings, "ScreensaverSource_Click", new System.Windows.Controls.Button { Tag = "builtin" }, new RoutedEventArgs());
        checks["global-screensaver-source-and-clock-controls-emit-actions"] = clock && custom && builtin;
        var saver = new OutputScreensaverControl(); var output = new Window { Content = saver, Width = 386, Height = 193, WindowStyle = WindowStyle.None, ShowInTaskbar = false }; output.Show();
        try
        {
            saver.Configure(null); await Task.Delay(100); CaptureCricketQa(saver, Path.Combine(directory, "default-screensaver-386x193.png"));
            var logo = CreateCricketQaLogo(directory, "screensaver", System.Windows.Media.Colors.SteelBlue);
            saver.Configure(logo); saver.SetClockVisible(true); await Task.Delay(100);
            checks["custom-screensaver-image-supports-clock-overlay"] = saver.CustomImage.Source is not null && saver.ClockText.IsVisible && saver.DefaultScene.Visibility == Visibility.Collapsed;
            CaptureCricketQa(saver, Path.Combine(directory, "custom-screensaver-with-clock.png"));
            saver.SetClockVisible(false); checks["screensaver-clock-can-be-hidden-for-custom-media"] = saver.ClockText.Visibility == Visibility.Collapsed;
            saver.Configure(Path.Combine(directory, "missing.mp4"));
            checks["missing-screensaver-media-falls-back-to-built-in"] = saver.DefaultScene.Visibility == Visibility.Visible && saver.CustomVideoHost.Visibility == Visibility.Collapsed;
            string movie = Path.Combine(Path.GetDirectoryName(directory)!, "screensaver-test.mp4");
            if (File.Exists(movie))
            {
                saver.Configure(movie); saver.SetClockVisible(true);
                var player = saver.CustomVideoHost.Children.OfType<VideoPlayerControl>().Single();
                for (int i = 0; i < 40 && (!player.IsPlaying || player.Position.TotalMilliseconds < 250); i++) await Task.Delay(200);
                checks["custom-mp4-screensaver-actually-plays-through-libvlc"] = player.IsPlaying && player.Position.TotalMilliseconds >= 250 && player.Loop;
                var videoClock = ((Grid)player.PresentationOverlay!).Children.OfType<System.Windows.Controls.TextBlock>().Single();
                checks["mp4-clock-is-on-native-video-foreground-surface"] = videoClock.Text == saver.ClockText.Text && videoClock.IsVisible && videoClock.FontSize == saver.ClockText.FontSize
                    && Window.GetWindow(videoClock) is Window foreground && !ReferenceEquals(foreground, output) && foreground.IsVisible;
                saver.SetClockVisible(false); await Task.Delay(50); checks["mp4-foreground-clock-follows-clock-preference"] = videoClock.Visibility == Visibility.Collapsed;
                saver.ShowDefault(); checks["leaving-custom-video-stops-decoding"] = !player.IsPlaying;
            }
        }
        finally { output.Close(); }
        foreach (var check in await RunCricketHierarchyChecksAsync(directory)) checks[check.Key] = check.Value;
        foreach (var check in await RunCricketLatestReviewChecksAsync(directory)) checks[check.Key] = check.Value;
        return checks;
    }
}

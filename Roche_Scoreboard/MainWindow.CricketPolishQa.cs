using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private async Task<Dictionary<string, bool>> RunCricketPolishChecksAsync(string directory)
    {
        var checks = new Dictionary<string, bool>();
        var match = CricketReliabilityChecks.CreateMatch(true, 20);
        var bug = new CricketScorebugControl { EventAnimationsEnabled = false, RollingFiguresEnabled = false };
        var host = new CricketOverlayHost(match) { AutomaticEnabled = false }; host.Scene.Children.Add(bug);
        match.MatchChanged += () => bug.UpdateFromMatch(match); bug.UpdateFromMatch(match);
        var window = new Window { Content = host, Width = 386, Height = 193, ShowInTaskbar = false }; ApplyOutputWindowChrome(window); window.Show();
        try
        {
            await Task.Delay(150);
#if DEBUG
            int previewCount = match.CurrentInnings!.Deliveries.Count, previewRuns = match.TotalRuns;
            bug.EventAnimationsEnabled = true; bug.PreviewMoment(CricketDeliveryType.Six); await Task.Delay(200);
            checks["developer-moment-preview-is-visible-without-adding-a-delivery"] = bug.EventShowing && bug.EventWord.Text == "SIX" && match.TotalRuns == previewRuns && match.CurrentInnings.Deliveries.Count == previewCount;
            bug.StopPreviewMoment(); bug.EventAnimationsEnabled = false;
            checks["developer-moment-cancel-returns-to-current-live-score"] = !bug.EventShowing && bug.ScoreRuns.Text == match.TotalRuns.ToString();
#endif
            typeof(CricketScorebugControl).GetField("_infoRotationIndex", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(bug, 2);
            match.CurrentInnings!.LegalBallsBowled = 32; bug.UpdateFromMatch(match); await Task.Delay(500);
            checks["limited-overs-remaining-context-uses-six-ball-notation"] = bug.InfoLabel.Text == "OVERS LEFT" && bug.InfoValue.Text == "14.4";
            match.CurrentInnings.LegalBallsBowled = 60; bug.UpdateFromMatch(match); await Task.Delay(100);
            checks["overs-remaining-exact-over-boundary-is-ten-point-zero"] = bug.InfoValue.Text == "10.0";
            match.CurrentInnings.LegalBallsBowled = 125; bug.UpdateFromMatch(match); await Task.Delay(100);
            checks["overs-remaining-never-becomes-negative"] = bug.InfoValue.Text == "0.0";
            match.Format = CricketFormat.MultiDay; bug.UpdateFromMatch(match); await Task.Delay(500);
            checks["multi-day-context-never-shows-an-overs-limit"] = bug.InfoLabel.Text != "OVERS LEFT";
            match.TeamAPrimaryColor = "#FFFFFF"; match.TeamASecondaryColor = "#FFF6DD"; bug.UpdateFromMatch(match);
            checks["white-team-header-keeps-white-lettering-readable-over-logo-wash"] = ((LinearGradientBrush)bug.BattingHeaderPanel.Background).GradientStops.All(stop =>
            {
                var c = stop.Color; var mixed = System.Windows.Media.Color.FromRgb((byte)(c.R * .78 + 255 * .22), (byte)(c.G * .78 + 255 * .22), (byte)(c.B * .78 + 255 * .22));
                return ContrastHelper.ContrastRatio(Colors.White, mixed) >= 4.5;
            });
            CaptureCricketQa(host, Path.Combine(directory, "white-team-safe-header-contrast.png"));
            match.TeamAPrimaryColor = "#A32020"; match.TeamASecondaryColor = "#FFFFFF";
            match.Format = CricketFormat.LimitedOvers; match.CurrentInnings.LegalBallsBowled = 0;
            match.RecordDelivery(1, CricketDeliveryType.NoBall); match.RecordDelivery(2, CricketDeliveryType.Wide); match.RecordDelivery(0, CricketDeliveryType.Dot);
            bool detail = (bool)InvokeCricketQa(bug, "ShowBowlerDetails")!;
            await Task.Delay(15100); InvokeCricketQa(bug, "TickBowlerDetails");
            checks["bowler-economy-and-nonzero-extras-stay-together-for-at-least-fifteen-seconds"] = detail && bug.BowlerDetailLane.Visibility == Visibility.Visible && bug.BowlerDetailText.Text.Contains("ECONOMY") && bug.BowlerDetailText.Text.Contains("NO-BALL") && bug.BowlerDetailText.Text.Contains("WIDE");
            CaptureCricketQa(host, Path.Combine(directory, "bowler-context-after-fifteen-seconds.png"));
            match.RecordDelivery(0, CricketDeliveryType.Dot); await WaitCricketQaStateAsync(window,()=>bug.BowlerDetailLane.Visibility==Visibility.Hidden);
            checks["live-ball-preempts-context-strip-even-during-longer-hold"] = bug.BowlerDetailLane.Visibility == Visibility.Hidden;
            host.Request(CricketOverlayKind.SinceScore); await Task.Delay(750);
            var pressure = QaDescendants(host).OfType<Viewbox>().Single(view => Equals(view.Tag, "pressure-cluster"));
            checks["repeat-overlay-request-extends-hold-without-another-layout-transition"] = host.Request(CricketOverlayKind.SinceScore) && !host.Status.Contains("CHANGING") && ReferenceEquals(pressure, QaDescendants(host).OfType<Viewbox>().Single(view => Equals(view.Tag, "pressure-cluster")));
            host.Cancel(); await Task.Delay(150); host.Request(CricketOverlayKind.Extras); await Task.Delay(1400); host.Cancel(); await WaitCricketQaStateAsync(window,()=>!host.IsShowing && bug.BattingHeaderPanel.Opacity>.99 && bug.BowlingHeaderPanel.Opacity>.99);
            checks["cancel-replace-cancel-restores-both-team-bars-and-full-rail"] = !host.IsShowing && bug.BattingHeaderPanel.Opacity > .99 && bug.BowlingHeaderPanel.Opacity > .99 && bug.CompactOversPanel.Opacity < .01;
            match.RecordDelivery(0, CricketDeliveryType.Wicket); host.Request(CricketOverlayKind.LastWicket); await Task.Delay(750);
            var texts = QaDescendants(host).OfType<TextBlock>().Select(text => text.Text).ToList();
            checks["last-wicket-shows-player-runs-and-parenthesized-balls-without-word-labels"] = QaDescendants(host).OfType<FrameworkElement>().Any(element => Equals(element.Tag, "last-wicket-player")) && texts.Any(text => text.Contains("(") && text.Contains(")")) && !texts.Contains("RUNS") && !texts.Contains("BALLS");
            CaptureCricketQa(host, Path.Combine(directory, "last-wicket-player-and-figures.png"));
            host.Cancel();
            var summary = new CricketSummaryControl(); host.Scene.Children.Add(summary); bug.Visibility = Visibility.Collapsed; summary.ShowBowlingSummary(match); await Task.Delay(300);
            checks["bowling-summary-headings-are-short-and-unclipped"] = QaDescendants(summary.SummaryColumns).OfType<TextBlock>().Select(text => text.Text).SequenceEqual(new[] { "O", "M", "R", "W", "ECON" });
            CaptureCricketQa(host, Path.Combine(directory, "bowling-summary-readable-short-headings.png"));
        }
        finally { host.Dispose(); bug.Dispose(); window.Close(); }

        foreach (var source in new[] { CricketWicketRuns.Wide, CricketWicketRuns.NoBall })
        {
            var fixture = CricketReliabilityChecks.CreateMatch(true, 20);
            var dialog = new CricketDeliveryDialog(fixture); var entry = new Window { Content = dialog, Width = 680, Height = 700, ShowInTaskbar = false }; entry.Show();
            try
            {
                dialog.DismissalChoice.SelectedIndex = 3; dialog.RunOutChoice.SelectedIndex = (int)source; dialog.RunsBox.Text = "2";
                if (source == CricketWicketRuns.NoBall) dialog.NoBallChoice.SelectedIndex = 2;
                await Task.Delay(100); CaptureCricketQa(dialog, Path.Combine(directory, $"run-out-{source}-extras.png"));
                InvokeCricketQa(dialog, "Apply_Click", dialog.ApplyButton, new RoutedEventArgs());
                var inn = fixture.CurrentInnings!;
                checks[$"run-out-{source}-panel-applies-penalty-plus-completed-extras"] = inn.TotalRuns == 3 && inn.TotalWickets == 1 && inn.LegalBallsBowled == 0 && (source == CricketWicketRuns.Wide ? inn.Wides == 3 : inn.NoBalls == 1 && inn.LegByes == 2);
            }
            finally { entry.Close(); }
        }
        var controlMatch = CricketReliabilityChecks.CreateMatch(); controlMatch.TossPending = true;
        var panel = new CricketControlPanel(); panel.SetMatch(controlMatch);
        var control = new Window { Content = panel, Width = 1100, Height = 850, ShowInTaskbar = false }; control.Show();
        try
        {
            InvokeCricketQa(panel, "OpenToss_Click", this, new RoutedEventArgs()); panel.TossAwayChoice.IsChecked = true; panel.TossBowlChoice.IsChecked = true;
            await Task.Delay(100);
            checks["toss-selection-preview-identifies-winner-and-decision"] = panel.TossSelectionSummary.Text.Contains(controlMatch.TeamBName) && panel.TossSelectionSummary.Text.Contains("bowl", StringComparison.OrdinalIgnoreCase);
            CaptureCricketQa(panel, Path.Combine(directory, "toss-club-cards-and-selection-sentence.png"));
            InvokeCricketQa(panel, "ConfirmToss_Click", this, new RoutedEventArgs()); await Task.Delay(100);
            checks["unavailable-scoring-has-readable-independent-instruction"] = panel.ScoringLockBanner.Visibility == Visibility.Visible && panel.ScoringLockBanner.Opacity == 1 && panel.ScoringReadyHint.Text.Contains("BATTERS") && panel.ScoringReadyHint.Text.Contains("BOWLER");
            controlMatch.SetStriker(0); controlMatch.SetNonStriker(1); controlMatch.ConfirmBowler(0); controlMatch.RecordDelivery(1, CricketDeliveryType.Runs); await Task.Delay(150);
            checks["scoring-instruction-goes-away-once-players-are-ready"] = panel.ScoringLockBanner.Visibility == Visibility.Collapsed && panel.ScoringCommands.IsEnabled;
            var ballNumbers = QaDescendants(panel.EventLog).OfType<TextBlock>().Where(text => Equals(text.Tag, "ball-number")).ToArray();
            checks["delivery-card-emphasizes-over-ball-instead-of-unexplained-cumulative-score"] = ballNumbers.Length == 1 && ballNumbers[0].Text.Contains("0.1") && ballNumbers[0].FontSize >= 14;
            checks["over-header-carries-cumulative-score-and-runs-wickets"] = QaDescendants(panel.EventLog).OfType<TextBlock>().Any(text => Equals(text.Tag, "overScore")) && QaDescendants(panel.EventLog).OfType<TextBlock>().Any(text => Equals(text.Tag, "overDetail") && text.Text.Contains("run", StringComparison.OrdinalIgnoreCase));
            CaptureCricketQa(panel, Path.Combine(directory, "compact-delivery-log-with-over-totals.png"));
        }
        finally { panel.ClearMatch(); control.Close(); }
        var library = new TeamLibraryWindow("home", TeamLibrarySport.Cricket) { Width = 1000, Height = 760, ShowInTaskbar = false }; library.Show();
        try
        {
            library.FavouriteOnly.IsChecked = false; library.LeagueFilter.SelectedIndex = 0; library.StateFilter.SelectedIndex = 0; await Task.Delay(150);
            checks["cricket-finder-shows-cricket-clubs-only"] = library.TeamList.Items.Cast<TeamCatalogEntry>().All(team => team.Sport == TeamLibrarySport.Cricket) && library.TeamList.Items.Count == 53;
            library.SearchBox.Text = "Carlton"; await Task.Delay(100);
            checks["cricket-finder-search-filters-without-afl-clubs"] = library.TeamList.Items.Count > 0 && library.TeamList.Items.Cast<TeamCatalogEntry>().All(team => team.TeamName.Contains("Carlton", StringComparison.OrdinalIgnoreCase) && team.Sport == TeamLibrarySport.Cricket);
            library.TeamList.SelectedIndex = 0;
            for (int i = 0; i < 100 && !library.UseButton.IsEnabled; i++) await Task.Delay(50);
            checks["cricket-finder-selected-club-prepares-real-logo-and-enables-use"] = library.LogoPreview.Source is not null && library.UseButton.IsEnabled;
            string clubId = ((TeamCatalogEntry)library.TeamList.SelectedItem).Id;
            library.FavouriteButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            for (int i = 0; i < 100 && (!library.PresetsChanged || !library.FavouriteButton.IsEnabled); i++) await Task.Delay(50);
            checks["saving-a-cricket-club-adds-only-to-cricket-presets"] = PresetStorage.LoadAllCricket().Any(preset => preset.LibraryTeamId == clubId) && !PresetStorage.LoadAll().Any(preset => preset.LibraryTeamId == clubId);
            library.FavouriteButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            for (int i = 0; i < 100 && PresetStorage.LoadAllCricket().Any(preset => preset.LibraryTeamId == clubId); i++) await Task.Delay(50);
            checks["saved-cricket-club-can-be-removed-without-touching-afl-presets"] = !PresetStorage.LoadAllCricket().Any(preset => preset.LibraryTeamId == clubId) && !PresetStorage.LoadAll().Any(preset => preset.LibraryTeamId == clubId);
            CaptureCricketQa((FrameworkElement)library.Content, Path.Combine(directory, "cricket-club-finder-selected.png"));
            library.Width = 430; library.Height = 600; await Task.Delay(150);
            checks["cricket-finder-stacks-results-and-details-in-narrow-window"] = Grid.GetRow(library.DetailSurface) == 1 && Grid.GetColumn(library.DetailSurface) == 0;
            CaptureCricketQa((FrameworkElement)library.Content, Path.Combine(directory, "cricket-club-finder-narrow.png"));
            library.SearchBox.Text = "no-such-club-qa"; await Task.Delay(50);
            checks["cricket-finder-empty-search-has-a-readable-state"] = library.TeamList.Items.Count == 0 && library.EmptyResults.Visibility == Visibility.Visible;
        }
        finally { library.Close(); }
        var footballLibrary = new TeamLibraryWindow("home") { Width = 1000, Height = 760, ShowInTaskbar = false }; footballLibrary.Show();
        try
        {
            footballLibrary.FavouriteOnly.IsChecked = false; footballLibrary.LeagueFilter.SelectedIndex = 0; footballLibrary.StateFilter.SelectedIndex = 0; await Task.Delay(100);
            checks["afl-finder-keeps-football-clubs-and-does-not-leak-cricket-clubs"] = footballLibrary.TeamList.Items.Count > 0 && footballLibrary.TeamList.Items.Cast<TeamCatalogEntry>().All(team => team.Sport == TeamLibrarySport.AustralianFootball);
            CaptureCricketQa((FrameworkElement)footballLibrary.Content, Path.Combine(directory, "afl-club-finder-keeps-football-catalogue.png"));
        }
        finally { footballLibrary.Close(); }
#if DEBUG
        checks["developer-previews-are-compiled-only-in-debug-builds"] = typeof(MainWindow).GetMethod("CricketDiagnostics_KeyDown", BindingFlags.NonPublic | BindingFlags.Instance) is not null;
#else
        checks["developer-previews-are-absent-from-release-builds"]=typeof(MainWindow).GetMethod("CricketDiagnostics_KeyDown",BindingFlags.NonPublic|BindingFlags.Instance) is null && typeof(CricketScorebugControl).GetMethod("PreviewMoment",BindingFlags.NonPublic|BindingFlags.Instance) is null;
#endif
        return checks;
    }
}

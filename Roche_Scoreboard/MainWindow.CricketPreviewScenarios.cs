#if DEBUG
using System.Windows;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private CricketSerializableState? _cricketPreviewBackup;
    private bool _previewSwitching;
    private int _previewGeneration;

    private async Task<bool> LoadCricketPreviewAsync(string scenario)
    {
        if (_cricketMatch is null || _previewSwitching) return false;
        _previewSwitching = true;
        int generation = ++_previewGeneration;
        var requestedMatch = _cricketMatch;
        try
        {
            if (_cricketPreviewBackup is null)
            {
                var tools = _cricketDiagnostics;
                tools?.Hide();
                string? choice;
                try
                {
                    choice = await CricketPrompt.ChooseAsync(CricketControlPanelView.CricketRoot,
                        "Start a practice scenario?",
                        "Your current match will be kept in memory. Practice scores are not saved or sent to remote scoring. Close Preview Tools or choose Restore to return to your match.",
                        new("practice", "Start practice", true), new("back", "Keep current match"));
                }
                finally
                {
                    if (ReferenceEquals(_cricketDiagnostics, tools) && tools?.IsLoaded == true) tools.Show();
                }
                if (choice != "practice" || _cricketMatch is null || _cricketDiagnostics is null) return false;
                _cricketPreviewBackup = _cricketMatch.CapturePersistenceState();
            }
            var backup = _cricketPreviewBackup;
            CricketControlPanelView.IsEnabled = false;
            var fixture = await Task.Run(() => CreateCricketPreviewScenario(backup, scenario).CapturePersistenceState());
            if (generation != _previewGeneration || !ReferenceEquals(requestedMatch, _cricketMatch) || !ReferenceEquals(backup, _cricketPreviewBackup) || _cricketDiagnostics is null) return false;
            CricketControlPanelView.PrepareForScenarioReload();
            _cricketScorebug?.StopPreviewMoment();
            _cricketMatch.LoadState(fixture);
            OnCricketPresentationChanged("scorebug");
            Title = "Roche Scoreboard · CRICKET PRACTICE · scores are not saved";
            return true;
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Cricket practice scenario: " + error);
            RestoreCricketPreview();
            await CricketPrompt.ChooseAsync(CricketControlPanelView.CricketRoot, "Practice scenario unavailable", "Your original match has been retained. Try another scenario.", new CricketPrompt.Choice("close", "Return to match", true));
            return false;
        }
        finally { _previewSwitching = false; CricketControlPanelView.IsEnabled = true; }
    }

    private void RestoreCricketPreview()
    {
        _previewGeneration++;
        CricketControlPanelView.IsEnabled = true;
        if (_previewSwitching) CricketPrompt.CancelAll(CricketControlPanelView.CricketRoot);
        if (_cricketPreviewBackup is not { } saved || _cricketMatch is null) return;
        CricketControlPanelView.PrepareForScenarioReload();
        _cricketScorebug?.StopPreviewMoment();
        _cricketPreviewBackup = null;
        _cricketMatch.LoadState(saved);
        Title = $"Roche Scoreboard v{AutoUpdateService.CurrentVersion.ToString(3)} · Cricket";
    }

    private async Task<Dictionary<string, bool>> RunCricketPreviewRoundtripChecksAsync()
    {
        var checks = new Dictionary<string, bool>();
        if (_cricketMatch is not null) return checks;
        string title = Title;
        var match = CricketReliabilityChecks.CreateMatch(true);
        match.RecordDelivery(2, CricketDeliveryType.Runs);
        string original = System.Text.Json.JsonSerializer.Serialize(match.ToState());
        _cricketMatch = match; CricketControlPanelView.SetMatch(match);
        CricketControlPanelView.Visibility = Visibility.Visible; SportSelectionPanel.Visibility = Visibility.Collapsed;
        _cricketDiagnostics = new Window { Owner = this, Width = 350, Height = 300, ShowInTaskbar = false };
        _cricketDiagnostics.Show();
        try
        {
            var start = LoadCricketPreviewAsync("chase");
            await Task.Delay(100);
            var practice = QaDescendants(CricketControlPanelView).OfType<System.Windows.Controls.Button>().Single(b => Equals(b.Tag, "practice"));
            checks["debug-practice-confirmation-is-not-hidden-behind-tools"] = !_cricketDiagnostics.IsVisible && practice.IsVisible;
            practice.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            bool started = await start;
            checks["debug-practice-loads-chase-with-detached-backup"] = started && _cricketPreviewBackup is not null
                && match.CurrentInningsNumber == 2 && match.Target > match.TotalRuns && match.CurrentInnings!.LegalBallsBowled == 90
                && match.TotalWickets > 0 && _cricketDiagnostics.IsVisible;
            match.RecordDelivery(2, CricketDeliveryType.Runs);
            RestoreCricketPreview();
            checks["debug-practice-restores-original-figures-and-history"] = _cricketPreviewBackup is null
                && System.Text.Json.JsonSerializer.Serialize(match.ToState()) == original;
            var cancel = LoadCricketPreviewAsync("middle"); await Task.Delay(80);
            RestoreCricketPreview();
            checks["debug-practice-cancel-before-confirmation-keeps-original"] = !await cancel
                && _cricketPreviewBackup is null && System.Text.Json.JsonSerializer.Serialize(match.ToState()) == original;
        }
        finally
        {
            RestoreCricketPreview(); _cricketDiagnostics.Close(); _cricketDiagnostics = null;
            CricketControlPanelView.ClearMatch(); CricketControlPanelView.Visibility = Visibility.Collapsed;
            _cricketMatch = null; SportSelectionPanel.Visibility = Visibility.Visible; Title = title;
        }
        return checks;
    }

    internal static CricketMatchManager CreateCricketPreviewScenario(CricketSerializableState original, string scenario)
    {
        var setup = new CricketSetupResult
        {
            TeamAPlayers = original.TeamAPlayers.Select(p => p.Clone()).ToList(),
            TeamBPlayers = original.TeamBPlayers.Select(p => p.Clone()).ToList()
        };
        setup.FillEmptySquads();
        var match = new CricketMatchManager
        {
            TeamAPrimaryColor = original.TeamAPrimaryColor,
            TeamASecondaryColor = original.TeamASecondaryColor,
            TeamBPrimaryColor = original.TeamBPrimaryColor,
            TeamBSecondaryColor = original.TeamBSecondaryColor,
            TeamALogoPath = original.TeamALogoPath,
            TeamBLogoPath = original.TeamBLogoPath,
            TeamAPlayers = setup.TeamAPlayers,
            TeamBPlayers = setup.TeamBPlayers,
            Format = scenario is "third" or "fourth" ? CricketFormat.MultiInnings : CricketFormat.LimitedOvers,
            TotalOvers = scenario is "third" or "fourth" ? 90 : 20,
            TotalInnings = scenario is "third" or "fourth" ? 4 : 2,
            TeamABatsFirst = true,
            TossWinner = original.TeamAName,
            TossWinnerElectedToBat = true,
            TossPending = false
        };
        match.SetTeams(original.TeamAName, original.TeamAAbbr, original.TeamBName, original.TeamBAbbr);
        match.StartMatch();
        void Ready()
        {
            var innings = match.CurrentInnings!;
            if (innings.Striker is null) match.SetStriker(0);
            if (innings.NonStriker is null) match.SetNonStriker(1);
            if (innings.NeedsBatterSelection && innings.Striker is not null && innings.NonStriker is not null)
            {
                int next = innings.BattingOrder.FindIndex(p => !p.IsOut && !p.IsRetired && !ReferenceEquals(p, innings.Striker) && !ReferenceEquals(p, innings.NonStriker));
                if (next >= 0) match.SelectNewBatter(next);
            }
            if (innings.NeedsBowlerSelection)
            {
                int next = Enumerable.Range(0, Math.Min(5, innings.BowlingAttack.Count))
                    .Where(match.CanSelectBowler).OrderBy(index => innings.BowlingAttack[index].BallsBowled).FirstOrDefault(-1);
                if (next < 0) next = match.SuggestedNextBowler();
                match.ConfirmBowler(next);
            }
        }
        void Bowl(int legalBalls, int totalRuns, int wickets, int seed)
        {
            var innings = match.CurrentInnings!;
            var random = new Random(seed);
            // Fixed seeds keep bug reports reproducible. Scores themselves are
            // delivered ball by ball: every batter, bowler, extra and over then
            // comes from the same ledger used by a real match.
            wickets = Math.Min(wickets, Math.Max(0, innings.BattingOrder.Count - 2));
            var wicketBalls = new HashSet<int>();
            for (int wicket = 0; wicket < wickets; wicket++)
            {
                int index = Math.Clamp((wicket + 1) * legalBalls / (wickets + 1) + random.Next(-2, 3), 1, legalBalls - 1);
                while (!wicketBalls.Add(index)) index = (index + 1) % legalBalls;
            }
            var extras = new Dictionary<int, (CricketDeliveryType Type, int Runs, CricketNoBallRuns Source)>();
            if (legalBalls > 7) extras[7] = (CricketDeliveryType.Wide, 1, CricketNoBallRuns.Bat);
            if (legalBalls > 24) extras[24] = (CricketDeliveryType.NoBall, 2, CricketNoBallRuns.Bat);
            if (legalBalls > 43) extras[43] = (CricketDeliveryType.Wide, 2, CricketNoBallRuns.Bat);
            if (legalBalls > 76) extras[76] = (CricketDeliveryType.NoBall, 1, CricketNoBallRuns.LegByes);
            if (legalBalls > 103) extras[103] = (CricketDeliveryType.Wide, 1, CricketNoBallRuns.Bat);
            int budget = totalRuns - extras.Values.Sum(extra => extra.Runs);
            int[] outcomes = [0, 0, 0, 0, 1, 1, 1, 1, 1, 2, 2, 4, 6];
            int[] plan = Enumerable.Range(0, legalBalls).Select(index => wicketBalls.Contains(index) ? 0 : outcomes[random.Next(outcomes.Length)]).ToArray();
            var scoringBalls = Enumerable.Range(0, legalBalls).Where(index => !wicketBalls.Contains(index)).ToArray();
            int remaining = budget - plan.Sum();
            for (int adjustment = 0; remaining != 0 && adjustment < 100_000; adjustment++)
            {
                int index = scoringBalls[random.Next(scoringBalls.Length)];
                int delta = remaining > 0 ? 1 : -1;
                if (plan[index] + delta is < 0 or > 6) continue;
                plan[index] += delta; remaining -= delta;
            }
            if (remaining != 0) throw new InvalidOperationException("The practice score could not be distributed across its deliveries.");
            int finalInnings = scenario == "fourth" ? 4 : scenario == "third" ? 3
                : scenario is "second" or "chase" or "lastover" or "target" ? 2 : 1;
            DateTime firstBall = DateTime.Now.AddHours(-2 * (finalInnings - match.CurrentInningsNumber)).AddSeconds(-legalBalls * 25);
            for (int ball = 0; ball < legalBalls && !innings.IsComplete; ball++)
            {
                Ready();
                if (extras.TryGetValue(ball, out var extra))
                    match.RecordDelivery(extra.Runs, extra.Type, noBallRuns: extra.Source, timestamp: firstBall.AddSeconds(ball * 25 - 8));
                if (wicketBalls.Contains(ball))
                {
                    var dismissal = (ball % 3) switch { 0 => DismissalType.Bowled, 1 => DismissalType.Caught, _ => DismissalType.LBW };
                    string? fielder = dismissal == DismissalType.Caught ? innings.BowlingAttack[(innings.CurrentBowlerIndex + 2) % innings.BowlingAttack.Count].DisplayName : null;
                    match.RecordDelivery(0, CricketDeliveryType.Wicket, dismissal: dismissal, fielder: fielder,
                        dismissalText: CricketMatchManager.DismissalLabel(dismissal, fielder, innings.CurrentBowler?.DisplayName), timestamp: firstBall.AddSeconds(ball * 25));
                }
                else
                {
                    int runs = plan[ball];
                    var type = runs switch { 0 => CricketDeliveryType.Dot, 4 => CricketDeliveryType.Four, 6 => CricketDeliveryType.Six, _ => CricketDeliveryType.Runs };
                    if (runs is 1 or 2 && ball % 31 == 19)
                        type = ball % 2 == 0 ? CricketDeliveryType.Bye : CricketDeliveryType.LegBye;
                    match.RecordDelivery(runs, type, timestamp: firstBall.AddSeconds(ball * 25));
                }
            }
            if (innings.LegalBallsBowled != legalBalls || innings.TotalRuns != totalRuns)
                throw new InvalidOperationException("The practice innings ended before its intended match situation.");
        }
        Ready();
        switch (scenario)
        {
            case "middle": Bowl(72, 95, 4, 101); Ready(); break;
            case "firstbreak": Bowl(120, 171, 6, 201); break;
            case "large": Bowl(120, 248, 3, 301); break;
            case "extras":
                match.RecordDelivery(1, CricketDeliveryType.Wide); match.RecordDelivery(0, CricketDeliveryType.Dot);
                match.RecordDelivery(1, CricketDeliveryType.Runs); match.RecordDelivery(2, CricketDeliveryType.NoBall);
                match.RecordDelivery(2, CricketDeliveryType.Bye); match.RecordDelivery(4, CricketDeliveryType.Four); break;
            case "collapse": Bowl(87, 91, 8, 401); Ready(); break;
            case "second":
            case "lastover":
            case "chase":
            case "target":
                Bowl(120, 171, 6, 201); match.TryStartAdditionalInnings(false, out _); Ready();
                if (scenario == "chase") { Bowl(90, 118, 3, 501); Ready(); }
                else if (scenario == "lastover") { Bowl(114, match.Target!.Value - 8, 5, 601); Ready(); }
                else if (scenario == "target") { Bowl(114, match.Target!.Value - 4, 5, 701); Ready(); match.RecordDelivery(6, CricketDeliveryType.Six); }
                break;
            case "third":
            case "fourth":
                Bowl(216, 198, 7, 801); match.TryStartAdditionalInnings(false, out _); Ready(); Bowl(216, 172, 8, 901);
                match.TryStartAdditionalInnings(scenario == "third", out _); Ready();
                if (scenario == "third") { Bowl(90, 72, 2, 1001); Ready(); }
                else
                {
                    Bowl(216, 186, 7, 1101); match.TryStartAdditionalInnings(true, out _); Ready();
                    Bowl(120, 96, 3, 1201); Ready();
                }
                break;
            case "rain": Bowl(72, 78, 3, 1301); Ready(); match.SetBreak(CricketBreakKind.Rain, "Practice rain delay"); break;
        }
        return match;
    }
}
#endif

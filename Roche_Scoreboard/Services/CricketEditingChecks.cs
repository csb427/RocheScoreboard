using System.Text.Json;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;

namespace Roche_Scoreboard.Services;

internal static class CricketEditingChecks
{
    internal static Dictionary<string, bool> Run()
    {
        Dictionary<string, bool> checks = new();
        string Snapshot(CricketMatchManager match) => JsonSerializer.Serialize(match.ToState());
        CricketMatchManager Chase(int needed)
        {
            var game = CricketReliabilityChecks.CreateMatch(true);
            game.RecordDelivery(needed - 1, CricketDeliveryType.Runs); game.EndCurrentInnings(); game.StartNextInnings();
            game.SetStriker(0); game.SetNonStriker(1); game.ConfirmBowler(0); return game;
        }
        foreach (int needed in new[] { 1, 2, 3, 5, 7 })
            foreach (var source in Enum.GetValues<CricketWicketRuns>())
                foreach (var origin in source == CricketWicketRuns.NoBall ? Enum.GetValues<CricketNoBallRuns>() : new[] { CricketNoBallRuns.Bat })
                    foreach (bool nonStrikerOut in new[] { false, true })
                    {
                        var game = Chase(needed); var inn = game.CurrentInnings!; string before = Snapshot(game);
                        int victim = nonStrikerOut ? inn.NonStrikerIndex : inn.StrikerIndex;
                        game.RecordDelivery(needed + 1, CricketDeliveryType.Wicket, noBallRuns: origin, dismissal: DismissalType.RunOut, dismissedBatterIndex: victim, wicketRuns: source);
                        var ball = inn.Deliveries.Single(); string key = $"winning-runout-{needed}-{source}-{origin}-{nonStrikerOut}";
                        int expectedBat = source == CricketWicketRuns.Bat ? needed : source == CricketWicketRuns.NoBall && origin == CricketNoBallRuns.Bat ? needed - 1 : 0;
                        checks[key + "-stops-at-winning-run-before-later-dismissal"] = game.TotalRuns == needed && game.TotalWickets == 0 && inn.BattingOrder.All(player => !player.IsOut)
                            && inn.BattingOrder[0].Runs == expectedBat && inn.TotalExtras == needed - expectedBat && inn.IsComplete && !inn.NeedsBatterSelection
                            && ball.Type != CricketDeliveryType.Wicket && ball.Runs == needed && game.MatchResult?.Contains("10 wickets") == true && ValidTotals(inn)
                            && inn.BattingOrder[0].Fours == 0 && inn.BattingOrder[0].Sixes == 0 && !CricketOverlayAnalysis.IsBoundary(ball);
                        var restored = new CricketMatchManager(); restored.LoadState(JsonSerializer.Deserialize<CricketSerializableState>(Snapshot(game))!);
                        checks[key + "-recovery-and-edit-replay-preserve-winning-margin"] = restored.TryEditDelivery(restored.CurrentInnings!.Deliveries.Single(), CricketMatchManager.DescribeDelivery(restored.CurrentInnings, restored.CurrentInnings.Deliveries.Single()), out _)
                            && restored.TotalRuns == needed && restored.TotalWickets == 0 && restored.MatchResult == game.MatchResult && ValidTotals(restored.CurrentInnings);
                        checks[key + "-undo-reopens-the-chase-exactly"] = game.UndoLastDelivery() && Snapshot(game) == before && game.MatchResult is null;
                    }
        foreach (var type in new[] { CricketDeliveryType.Wide, CricketDeliveryType.NoBall })
            foreach (var origin in Enum.GetValues<CricketNoBallRuns>())
            {
                var game = Chase(1); string before = Snapshot(game); game.RecordDelivery(7, type, noBallRuns: origin);
                var inn = game.CurrentInnings!; var ball = inn.Deliveries.Single();
                checks[$"winning-{type}-{origin}-penalty-precedes-additional-runs"] = game.TotalRuns == 1 && game.TotalWickets == 0 && inn.TotalExtras == 1
                    && inn.BattingOrder.All(player => player.Runs == 0 && !player.IsOut) && ball.Type == type && ball.Runs == 1 && ValidTotals(inn);
                checks[$"winning-{type}-{origin}-undo-removes-only-the-winning-penalty"] = game.UndoLastDelivery() && Snapshot(game) == before;
            }
        foreach (var type in new[] { CricketDeliveryType.Four, CricketDeliveryType.Six, CricketDeliveryType.Wide, CricketDeliveryType.NoBall, CricketDeliveryType.Bye, CricketDeliveryType.LegBye })
        {
            var game = Chase(2); int allowance = type == CricketDeliveryType.Six ? 6 : type is CricketDeliveryType.Wide or CricketDeliveryType.NoBall ? 5 : 4;
            game.RecordDelivery(allowance, type);
            checks[$"winning-{type}-retains-the-full-boundary-allowance"] = game.TotalRuns == allowance && game.TotalWickets == 0 && game.MatchResult?.Contains("10 wickets") == true;
        }
        foreach (var source in Enum.GetValues<CricketWicketRuns>())
            foreach (var origin in source == CricketWicketRuns.NoBall ? Enum.GetValues<CricketNoBallRuns>() : new[] { CricketNoBallRuns.Bat })
                foreach (bool nonStrikerOut in new[] { false, true })
                {
                    var match = CricketReliabilityChecks.CreateMatch(true);
                    var innings = match.CurrentInnings!; int victim = nonStrikerOut ? innings.NonStrikerIndex : innings.StrikerIndex;
                    string before = Snapshot(match); bool penalty = source is CricketWicketRuns.NoBall or CricketWicketRuns.Wide;
                    int added = 2 + (penalty ? 1 : 0);
                    match.RecordDelivery(added, CricketDeliveryType.Wicket, dismissal: DismissalType.RunOut, noBallRuns: origin, dismissedBatterIndex: victim, wicketRuns: source);
                    var delivery = innings.Deliveries.Single();
                    int bat = source == CricketWicketRuns.Bat || source == CricketWicketRuns.NoBall && origin == CricketNoBallRuns.Bat ? 2 : 0;
                    string key = $"runout-{source}-{origin}-{(nonStrikerOut ? "nonstriker" : "striker")}";
                    checks[key + "-credits-runs-extras-legal-ball-and-wicket-once"] = innings.TotalRuns == added && innings.TotalWickets == 1
                        && innings.BattingOrder[0].Runs == bat && innings.TotalExtras == added - bat && innings.LegalBallsBowled == (penalty ? 0 : 1)
                        && innings.BowlingAttack[0].Wickets == 0 && innings.BattingOrder[victim].IsOut && innings.NeedsBatterSelection
                        && innings.BattingOrder[0].BallsFaced == (source == CricketWicketRuns.Wide ? 0 : 1) && ValidTotals(innings);
                    checks[key + "-extra-slot-and-analytics-use-delivery-legality"] = CricketOverlayAnalysis.IsLegal(delivery) == !penalty
                        && CricketOverTracker.CreateSlots(innings.CurrentOverBalls).Length == (penalty ? 7 : 6);
                    var restored = new CricketMatchManager(); restored.LoadState(JsonSerializer.Deserialize<CricketSerializableState>(Snapshot(match))!);
                    checks[key + "-survives-recovery-and-correction-replay"] = restored.CurrentInnings!.Deliveries[0].WicketRuns == source
                        && restored.TryEditDelivery(restored.CurrentInnings.Deliveries[0], CricketMatchManager.DescribeDelivery(restored.CurrentInnings, restored.CurrentInnings.Deliveries[0]), out _)
                        && ValidTotals(restored.CurrentInnings) && restored.TotalRuns == added && restored.TotalWickets == 1;
                    checks[key + "-undo-restores-all-before-delivery-state"] = match.UndoLastDelivery() && Snapshot(match) == before;
                }
        {
            var match = CricketReliabilityChecks.CreateMatch(true); string before = Snapshot(match);
            match.RecordDelivery(1, CricketDeliveryType.Wicket, dismissal: DismissalType.Bowled, wicketRuns: CricketWicketRuns.NoBall);
            checks["non-runout-cannot-use-runout-extra-options"] = Snapshot(match) == before;
            match.RecordDelivery(2, CricketDeliveryType.Wicket, dismissal: DismissalType.RunOut, wicketRuns: CricketWicketRuns.Byes);
            var innings = match.CurrentInnings!; var ball = innings.Deliveries[0];
            var edit = CricketMatchManager.DescribeDelivery(innings, ball) with { WicketRuns = CricketWicketRuns.Wide, Runs = 3 };
            checks["runout-extra-correction-recalculates-over-and-bowler-accounting"] = match.TryEditDelivery(ball, edit, out _)
                && innings.LegalBallsBowled == 0 && innings.TotalRuns == 3 && innings.Wides == 3 && innings.Byes == 0
                && innings.BowlingAttack[0].RunsConceded == 3 && innings.BattingOrder[0].Runs == 0 && ValidTotals(innings);
        }
        foreach (var origin in Enum.GetValues<CricketNoBallRuns>())
        {
            var match = CricketReliabilityChecks.CreateMatch(true);
            match.RecordDelivery(4, CricketDeliveryType.NoBall, noBallRuns: origin);
            var innings = match.CurrentInnings!;
            checks[$"no-ball-{origin}-credits-penalty-and-runs-correctly"] = innings.TotalRuns == 4
                && innings.NoBalls == 1 && innings.Wides == 0 && innings.LegalBallsBowled == 0
                && innings.BattingOrder[0].Runs == (origin == CricketNoBallRuns.Bat ? 3 : 0)
                && innings.TotalExtras == (origin == CricketNoBallRuns.Bat ? 1 : 4)
                && innings.CurrentBowler!.RunsConceded == (origin == CricketNoBallRuns.Bat ? 4 : 1)
                && innings.Byes == (origin == CricketNoBallRuns.Byes ? 3 : 0)
                && innings.LegByes == (origin == CricketNoBallRuns.LegByes ? 3 : 0);
            var restored = new CricketMatchManager(); restored.LoadState(JsonSerializer.Deserialize<CricketSerializableState>(Snapshot(match))!);
            checks[$"no-ball-{origin}-survives-save-restore"] = restored.CurrentInnings!.Deliveries[0].NoBallRuns == origin
                && restored.TotalRuns == 4;
        }
        {
            var match = CricketReliabilityChecks.CreateMatch(true);
            match.RecordDelivery(1, CricketDeliveryType.Wide);
            for (int i = 0; i < 6; i++) match.RecordDelivery(0, CricketDeliveryType.Dot);
            checks["wide-in-completed-over-prevents-false-maiden"] = match.CurrentInnings!.BowlingAttack[0].Maidens == 0;
            checks["completed-over-keeps-all-seven-circles-until-selection"] = match.CurrentInnings.CurrentOverBalls.Count == 7 && match.CurrentInnings.NeedsBowlerSelection;
            match.ConfirmBowler(1);
            checks["confirmed-next-bowler-clears-over-once"] = match.CurrentInnings.CurrentOverBalls.Count == 0 && !match.CurrentInnings.NeedsBowlerSelection;
        }
        {
            var match = CricketReliabilityChecks.CreateMatch(true);
            for (int i = 0; i < 6; i++) match.RecordDelivery(1, CricketDeliveryType.Bye);
            checks["bye-only-over-is-a-maiden"] = match.CurrentInnings!.BowlingAttack[0].Maidens == 1 && match.TotalRuns == 6;
        }
        foreach (var scope in Enum.GetValues<CricketCorrectionScope>())
        {
            var match = CricketReliabilityChecks.CreateMatch(true);
            match.RecordDelivery(2, CricketDeliveryType.Runs);
            match.RecordDelivery(1, CricketDeliveryType.Runs);
            match.RecordDelivery(0, CricketDeliveryType.Dot);
            var innings = match.CurrentInnings!;
            var first = innings.Deliveries[0];
            int changed = 0, added = 0;
            match.MatchChanged += () => changed++; match.DeliveryAdded += _ => added++;
            var edit = CricketMatchManager.DescribeDelivery(innings, first) with { StrikerIndex = 1, StrikeScope = scope };
            bool result = match.TryEditDelivery(first, edit, out _);
            checks[$"striker-correction-{scope}-replays-without-live-delivery-events"] = result && changed == 1 && added == 0 && match.TotalRuns == 3;
            checks[$"striker-correction-{scope}-uses-requested-scope"] = innings.BattingOrder[1].Runs == (scope == CricketCorrectionScope.ThisBall ? 2 : 3)
                && innings.BattingOrder[0].Runs == (scope == CricketCorrectionScope.ThisBall ? 1 : 0);
            checks[$"corrected-history-{scope}-has-fresh-undo-snapshots"] = match.UndoLastDelivery()
                && innings.Deliveries.Count == 2 && match.TotalRuns == 3;
        }
        {
            var match = CricketReliabilityChecks.CreateMatch(true);
            match.RecordDelivery(1, CricketDeliveryType.Runs); match.RecordDelivery(2, CricketDeliveryType.Runs);
            var innings = match.CurrentInnings!;
            var edit = CricketMatchManager.DescribeDelivery(innings, innings.Deliveries[0]) with { Runs = 2, StrikeScope = CricketCorrectionScope.RestOfInnings };
            checks["run-parity-edit-recalculates-subsequent-strike"] = match.TryEditDelivery(innings.Deliveries[0], edit, out _)
                && innings.BattingOrder[0].Runs == 4 && innings.BattingOrder[1].Runs == 0;
        }
        foreach (bool remaining in new[] { false, true })
        {
            var match = CricketReliabilityChecks.CreateMatch(true);
            for (int i = 0; i < 4; i++) match.RecordDelivery(2, CricketDeliveryType.Runs);
            var innings = match.CurrentInnings!;
            var edit = CricketMatchManager.DescribeDelivery(innings, innings.Deliveries[1]) with { BowlerIndex = 1, BowlerForRestOfOver = remaining };
            checks[$"bowler-correction-{remaining}-moves-only-requested-figures"] = match.TryEditDelivery(innings.Deliveries[1], edit, out _)
                && innings.BowlingAttack[1].BallsBowled == (remaining ? 3 : 1)
                && innings.BowlingAttack[1].RunsConceded == (remaining ? 6 : 2)
                && innings.BowlingAttack.Sum(player => player.BallsBowled) == 4;
        }
        {
            var match = CricketReliabilityChecks.CreateMatch(true);
            match.RecordDelivery(3, CricketDeliveryType.NoBall);
            match.RecordDelivery(1, CricketDeliveryType.Runs);
            var innings = match.CurrentInnings!;
            var edit = CricketMatchManager.DescribeDelivery(innings, innings.Deliveries[0]) with { NoBallRuns = CricketNoBallRuns.LegByes };
            checks["no-ball-edit-rebalances-batter-bowler-and-extras"] = match.TryEditDelivery(innings.Deliveries[0], edit, out _)
                && innings.TotalRuns == 4 && innings.TotalExtras == 3 && innings.LegByes == 2
                && innings.BowlingAttack.Sum(player => player.RunsConceded) == 2 && innings.BattingOrder.Sum(player => player.Runs) == 1;
        }
        {
            var match = CricketReliabilityChecks.CreateMatch(true, 1);
            match.RecordDelivery(1, CricketDeliveryType.NoBall);
            for (int i = 0; i < 6; i++) match.RecordDelivery(0, CricketDeliveryType.Dot);
            var innings = match.CurrentInnings!; string before = Snapshot(match);
            var edit = CricketMatchManager.DescribeDelivery(innings, innings.Deliveries[0]) with { Type = CricketDeliveryType.Dot, Runs = 0 };
            checks["correction-never-silently-discards-balls-after-new-innings-end"] = !match.TryEditDelivery(innings.Deliveries[0], edit, out string error)
                && error.Contains("No", StringComparison.OrdinalIgnoreCase) && Snapshot(match) == before;
        }
        {
            var match = CricketReliabilityChecks.CreateMatch(true);
            match.RecordDelivery(0, CricketDeliveryType.Dot);
            var innings = match.CurrentInnings!; string before = Snapshot(match);
            var old = innings.Deliveries[0];
            var invalid = CricketMatchManager.DescribeDelivery(innings, old) with { StrikerIndex = 900 };
            checks["invalid-edit-rolls-back-every-statistic"] = !match.TryEditDelivery(old, invalid, out _) && Snapshot(match) == before;
            match.UndoLastDelivery();
            checks["editing-a-stale-undone-delivery-is-rejected"] = !match.TryEditDelivery(old, invalid, out _);
        }
        {
            var match = CricketReliabilityChecks.CreateMatch(true);
            match.RecordDelivery(1, CricketDeliveryType.Wicket, "run out (Player 4)", dismissal: DismissalType.RunOut, fielder: "Player 4", dismissedBatterIndex: 1);
            var innings = match.CurrentInnings!;
            checks["non-striker-runout-credits-completed-run-and-no-bowler-wicket"] = innings.BattingOrder[1].IsOut
                && !innings.BattingOrder[0].IsOut && innings.BattingOrder[0].Runs == 1 && innings.BowlingAttack[0].Wickets == 0;
        }
        {
            var match = CricketReliabilityChecks.CreateMatch(true);
            checks["empty-bowling-summary-has-no-unused-placeholders"] = CricketSummaryControl.BowledPlayers(match.CurrentInnings!).Count == 0;
            match.RecordDelivery(1, CricketDeliveryType.Wide);
            checks["bowler-who-has-only-bowled-a-wide-still-appears"] = CricketSummaryControl.BowledPlayers(match.CurrentInnings!).Count == 1;
        }
        foreach (var scope in new[] { CricketCorrectionScope.RestOfOver, CricketCorrectionScope.RestOfInnings })
        {
            var match = CricketReliabilityChecks.CreateMatch(true);
            for (int i = 0; i < 8; i++)
            {
                if (match.CurrentInnings!.NeedsBowlerSelection) match.ConfirmBowler(1);
                match.RecordDelivery(2, CricketDeliveryType.Runs);
            }
            var innings = match.CurrentInnings!;
            var edit = CricketMatchManager.DescribeDelivery(innings, innings.Deliveries[0]) with { StrikerIndex = 1, StrikeScope = scope };
            checks[$"strike-scope-{scope}-respects-over-boundaries"] = match.TryEditDelivery(innings.Deliveries[0], edit, out _)
                && innings.BattingOrder[1].Runs == (scope == CricketCorrectionScope.RestOfOver ? 16 : 12)
                && innings.BattingOrder[0].Runs == (scope == CricketCorrectionScope.RestOfOver ? 0 : 4);
        }
        {
            var match = CricketReliabilityChecks.CreateMatch(true);
            for (int i = 0; i < 6; i++) match.RecordDelivery(0, CricketDeliveryType.Dot);
            match.ConfirmBowler(1);
            match.RecordDelivery(1, CricketDeliveryType.Wide);
            match.RecordDelivery(3, CricketDeliveryType.NoBall, noBallRuns: CricketNoBallRuns.Byes);
            match.RecordDelivery(0, CricketDeliveryType.Dot);
            var innings = match.CurrentInnings!;
            var old = innings.Deliveries[0];
            var input = CricketMatchManager.DescribeDelivery(innings, old);
            checks["history-replay-preserves-consecutive-extras-at-start-of-over"] = match.TryEditDelivery(old, input, out _)
                && innings.CurrentOverBalls.SequenceEqual(new[] { "1wd", "nb+2b", "•" });
            match.EndCurrentInnings();
            old = innings.Deliveries[0];
            checks["correction-preserves-manually-ended-innings"] = match.TryEditDelivery(old, CricketMatchManager.DescribeDelivery(innings, old), out _)
                && innings.IsComplete;
        }
        {
            var match = CricketReliabilityChecks.CreateMatch(true);
            match.RecordDelivery(0, CricketDeliveryType.Dot);
            match.ConfirmBowler(1); // Injury replacement mid-over.
            for (int i = 0; i < 5; i++) match.RecordDelivery(0, CricketDeliveryType.Dot);
            checks["next-over-excludes-all-bowlers-who-bowled-part-of-previous-over"] = !match.CanSelectBowler(0)
                && !match.CanSelectBowler(1) && match.CanSelectBowler(2);
        }
        {
            var match = CricketReliabilityChecks.CreateMatch(true);
            DateTime firstTime = new(2026, 10, 24, 13, 12, 30);
            match.RecordDelivery(2, CricketDeliveryType.Runs, timestamp: firstTime);
            match.RecordDelivery(1, CricketDeliveryType.Runs, timestamp: firstTime.AddSeconds(45));
            var innings = match.CurrentInnings!;
            var old = innings.Deliveries[0];
            checks["corrections-preserve-original-delivery-timestamps"] = match.TryEditDelivery(old,
                CricketMatchManager.DescribeDelivery(innings, old) with { Runs = 3 }, out _)
                && innings.Deliveries[0].Timestamp == firstTime && innings.Deliveries[1].Timestamp == firstTime.AddSeconds(45);
            int count = innings.Deliveries.Count;
            match.RecordDelivery(0, CricketDeliveryType.Wicket, dismissal: DismissalType.Caught, dismissedBatterIndex: innings.NonStrikerIndex);
            checks["caught-or-bowled-cannot-dismiss-nonstriker"] = innings.Deliveries.Count == count;
        }
        bool randomEdits = true;
        for (int seed = 0; seed < 10; seed++)
        {
            var match = CricketReliabilityChecks.CreateMatch(true, 50);
            Random random = new(seed);
            for (int ball = 0; ball < 60; ball++)
            {
                if (match.CurrentInnings!.NeedsBowlerSelection) match.ConfirmBowler((match.CurrentInnings.CurrentBowlerIndex + 1) % 3);
                match.RecordDelivery(random.Next(4), CricketDeliveryType.Runs);
            }
            for (int operation = 0; operation < 30; operation++)
            {
                var innings = match.CurrentInnings!;
                var ball = innings.Deliveries[random.Next(innings.Deliveries.Count)];
                var edit = CricketMatchManager.DescribeDelivery(innings, ball) with
                {
                    Type = (CricketDeliveryType)random.Next(8),
                    Runs = random.Next(5),
                    NoBallRuns = (CricketNoBallRuns)random.Next(3),
                    BowlerIndex = random.Next(3),
                    StrikeScope = (CricketCorrectionScope)random.Next(3)
                };
                string before = Snapshot(match);
                bool success = match.TryEditDelivery(ball, edit, out _);
                randomEdits &= success ? ValidTotals(innings) : Snapshot(match) == before;
                var restored = new CricketMatchManager(); restored.LoadState(JsonSerializer.Deserialize<CricketSerializableState>(Snapshot(match))!);
                randomEdits &= ValidTotals(restored.CurrentInnings!);
            }
        }
        checks["300-random-history-corrections-preserve-totals-and-restoration"] = randomEdits;
        return checks;
    }
    /// <summary>Native dialog checks run separately from the model suite: button storyboards require a loaded template namescope.</summary>
    internal static async Task<Dictionary<string, bool>> RunDialogChecksAsync()
    {
        var checks = new Dictionary<string, bool>();
        foreach (int needed in new[] { 5, 7 })
            foreach (bool changeBowler in new[] { false, true })
            {
                var game = CricketReliabilityChecks.CreateMatch(true);
                game.RecordDelivery(needed - 1, CricketDeliveryType.Runs); game.EndCurrentInnings(); game.StartNextInnings();
                game.SetStriker(0); game.SetNonStriker(1); game.ConfirmBowler(0);
                game.RecordDelivery(needed + 1, CricketDeliveryType.Wicket, dismissal: DismissalType.RunOut, wicketRuns: CricketWicketRuns.NoBall);
                var inn = game.CurrentInnings!; var original = inn.Deliveries.Single(); bool applied = false;
                var dialog = new CricketDeliveryDialog(game, original); dialog.Completed += value => applied = value;
                var window = new System.Windows.Window { Content = dialog, Width = 620, Height = 720, ShowInTaskbar = false };
                string key = $"delivery-editor-{needed}-completed-no-ball-{(changeBowler ? "bowler-only" : "no-op")}-retains-nonboundary-classification";
                try
                {
                    window.Show();
                    await window.Dispatcher.InvokeAsync(() =>
                    {
                        window.ApplyTemplate(); dialog.ApplyTemplate(); window.UpdateLayout(); dialog.ApplyButton.ApplyTemplate();
                    }, System.Windows.Threading.DispatcherPriority.Loaded);
                    if (dialog.ApplyButton.Template.FindName("TbSc", dialog.ApplyButton) is null) { checks[key] = false; continue; }
                    if (changeBowler) dialog.BowlerChoice.SelectedIndex = 1;
                    int revisionBefore = game.DeliveryRevision;
                    dialog.ApplyButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    if (changeBowler)
                    {
                        await WaitForDialogStateAsync(window, () => Descendants(dialog).OfType<System.Windows.Controls.Button>().Any(button => Equals(button.Tag, "no")));
                        var confirmation = Descendants(dialog).OfType<System.Windows.Controls.Button>().FirstOrDefault(button => Equals(button.Tag, "no"));
                        if (confirmation is null) { checks[key] = false; continue; }
                        confirmation.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    }
                    await WaitForDialogStateAsync(window, () => applied && (!changeBowler || game.DeliveryRevision > revisionBefore));
                    var corrected = inn.Deliveries.Single();
                    checks[key] = applied && corrected.Type == CricketDeliveryType.NoBall && corrected.Runs == needed && corrected.CompletedRunsOnly
                        && corrected.BowlerIndex == (changeBowler ? 1 : 0) && !CricketOverlayAnalysis.IsBoundary(corrected)
                        && inn.BattingOrder[0].Fours == 0 && inn.BattingOrder[0].Sixes == 0
                        && game.TotalRuns == needed && game.TotalWickets == 0 && game.MatchResult?.Contains("10 wickets") == true && ValidTotals(inn);
                }
                finally { window.Close(); }
            }
        return checks;
    }
    private static async Task<bool> WaitForDialogStateAsync(System.Windows.Window window, Func<bool> predicate)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), System.Windows.Threading.DispatcherPriority.Background);
            if (predicate()) return true;
            await Task.Delay(12);
        } while (timeout.ElapsedMilliseconds < 3500);
        return predicate();
    }
    private static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static bool ValidTotals(CricketInnings innings) => innings.TotalRuns == innings.Deliveries.Sum(delivery => delivery.Runs)
        && innings.TotalRuns == innings.BattingOrder.Sum(player => player.Runs) + innings.TotalExtras
        && innings.TotalExtras == innings.Wides + innings.NoBalls + innings.Byes + innings.LegByes
        && innings.LegalBallsBowled == innings.BowlingAttack.Sum(player => player.BallsBowled)
        && innings.LegalBallsBowled == innings.Deliveries.Count(delivery => delivery.ScoringType is not CricketDeliveryType.NoBall and not CricketDeliveryType.Wide)
        && innings.BowlingAttack.Sum(player => player.RunsConceded) == innings.TotalRuns - innings.Byes - innings.LegByes;
}

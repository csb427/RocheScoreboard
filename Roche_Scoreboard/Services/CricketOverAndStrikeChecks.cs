using System.Text.Json;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

internal static class CricketOverAndStrikeChecks
{
    internal static Dictionary<string, bool> Run()
    {
        var checks = new Dictionary<string, bool>();
        bool permutations = true, restored = true, replayed = true;
        // Every six-delivery ordering of legal balls, wides and no-balls.
        for (int pattern = 0; pattern < 729; pattern++)
        {
            var match = CricketReliabilityChecks.CreateMatch(true);
            int digits = pattern, legal = 0, prompts = 0;
            match.BowlerSelectionNeeded += () => prompts++;
            for (int physical = 0; physical < 6; physical++)
            {
                int kind = digits % 3; digits /= 3;
                var type = kind == 0 ? CricketDeliveryType.Dot : kind == 1 ? CricketDeliveryType.Wide : CricketDeliveryType.NoBall;
                match.RecordDelivery(kind == 0 ? 0 : 1, type);
                if (kind == 0) legal++;
                permutations &= match.CurrentInnings!.LegalBallsBowled == legal
                    && match.CurrentInnings.Deliveries.Count == physical + 1
                    && match.CurrentInnings.NeedsBowlerSelection == (legal == 6)
                    && prompts == (legal == 6 ? 1 : 0);
            }
            var state = JsonSerializer.Deserialize<CricketSerializableState>(JsonSerializer.Serialize(match.ToState()))!;
            match.LoadState(state);
            restored &= match.CurrentInnings!.NeedsBowlerSelection == (legal == 6);
            var first = match.CurrentInnings.Deliveries[0];
            replayed &= match.TryEditDelivery(first, CricketMatchManager.DescribeDelivery(match.CurrentInnings, first), out _)
                && match.CurrentInnings.LegalBallsBowled == legal && match.CurrentInnings.NeedsBowlerSelection == (legal == 6);
            while (legal < 6)
            {
                match.RecordDelivery(0, CricketDeliveryType.Dot); legal++;
                permutations &= match.CurrentInnings.LegalBallsBowled == legal
                    && match.CurrentInnings.NeedsBowlerSelection == (legal == 6);
            }
        }
        checks["729-extra-interleavings-finish-only-after-six-legal-balls"] = permutations;
        checks["729-extra-interleavings-recovery-preserves-over-boundary"] = restored;
        checks["729-extra-interleavings-history-replay-preserves-over-boundary"] = replayed;
        {
            var match = CricketReliabilityChecks.CreateMatch(true);
            match.RecordDelivery(0, CricketDeliveryType.Dot); match.RecordDelivery(1, CricketDeliveryType.Wide);
            match.CurrentInnings!.NeedsBowlerSelection = true; // Legacy/corrupt flag from physical-ball counting.
            match.LoadState(JsonSerializer.Deserialize<CricketSerializableState>(JsonSerializer.Serialize(match.ToState()))!);
            checks["recovery-repairs-impossible-mid-over-bowler-prompt"] = !match.CurrentInnings!.NeedsBowlerSelection;
            match.CurrentInnings.NeedsBowlerSelection = true;
            match.RecordDelivery(0, CricketDeliveryType.Dot);
            checks["scoring-recovers-stale-mid-over-bowler-flag"] = match.CurrentInnings.LegalBallsBowled == 2 && !match.CurrentInnings.NeedsBowlerSelection;
        }
        {
            var match = CricketReliabilityChecks.CreateMatch(true, 1);
            for (int i = 0; i < 6; i++) match.RecordDelivery(1, i % 2 == 0 ? CricketDeliveryType.Wide : CricketDeliveryType.NoBall);
            checks["six-extras-do-not-end-innings"] = !match.CurrentInnings!.IsComplete && match.CurrentInnings.LegalBallsBowled == 0;
            for (int i = 0; i < 6; i++) match.RecordDelivery(0, CricketDeliveryType.Dot);
            checks["six-legal-balls-after-extras-complete-first-innings"] = match.CurrentInnings.IsComplete && !match.CurrentInnings.NeedsBowlerSelection && match.CanStartNextInnings();
            match.StartNextInnings();
            checks["new-innings-resets-ball-state-and-uses-australian-previous-score"] = match.CurrentInningsNumber == 2
                && match.CurrentInnings!.LegalBallsBowled == 0 && match.CurrentInnings.Deliveries.Count == 0
                && match.CurrentInnings.NeedsBowlerSelection && match.OpponentPreviousInningsScore?.Score == "0/6";
        }
        bool strike = true, replayStrike = true, undo = true;
        foreach (var source in Enum.GetValues<CricketWicketRuns>())
            foreach (var noBall in Enum.GetValues<CricketNoBallRuns>())
                foreach (int completed in new[] { 0, 1, 2, 3 })
                    foreach (bool dismissedStriker in new[] { false, true })
                        foreach (bool crossed in new[] { false, true })
                            foreach (bool ballSix in new[] { false, true })
                            {
                                var match = CricketReliabilityChecks.CreateMatch(true);
                                if (ballSix) for (int i = 0; i < 5; i++) match.RecordDelivery(0, CricketDeliveryType.Dot);
                                int runs = completed + (source is CricketWicketRuns.Wide or CricketWicketRuns.NoBall ? 1 : 0);
                                int victim = dismissedStriker ? 0 : 1;
                                bool legal = source is not CricketWicketRuns.Wide and not CricketWicketRuns.NoBall;
                                bool swap = (completed % 2 == 1) ^ crossed ^ (ballSix && legal);
                                int initialStrikerAfter = swap ? 1 : 0;
                                int expectedStriker = initialStrikerAfter == victim ? 2 : initialStrikerAfter;
                                match.RecordDelivery(runs, CricketDeliveryType.Wicket, dismissal: DismissalType.RunOut,
                                    dismissedBatterIndex: victim, noBallRuns: noBall, wicketRuns: source, runOutCrossed: crossed);
                                var innings = match.CurrentInnings!;
                                var delivery = innings.Deliveries[^1];
                                match.SelectNewBatter(2);
                                strike &= innings.StrikerIndex == expectedStriker && innings.TotalRuns == runs && innings.TotalWickets == 1
                                    && innings.BowlingAttack[0].Wickets == 0 && innings.NeedsBowlerSelection == (ballSix && legal)
                                    && innings.LegalBallsBowled == (ballSix ? 5 : 0) + (legal ? 1 : 0);
                                replayStrike &= match.TryEditDelivery(delivery, CricketMatchManager.DescribeDelivery(innings, delivery), out _)
                                    && innings.StrikerIndex == expectedStriker && innings.Deliveries[^1].RunOutCrossed == crossed;
                                undo &= match.UndoLastDelivery() && innings.StrikerIndex == 0 && innings.NonStrikerIndex == 1
                                    && innings.TotalRuns == 0 && innings.TotalWickets == 0;
                            }
        checks["480-run-out-combinations-preserve-correct-surviving-batter-end"] = strike;
        checks["480-run-out-combinations-replay-crossing-penalties-and-over-end"] = replayStrike;
        checks["480-run-out-combinations-undo-restores-original-pair"] = undo;
        return checks;
    }
}

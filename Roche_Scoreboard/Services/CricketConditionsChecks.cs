using System.Globalization;
using System.Text.Json;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

internal static class CricketConditionsChecks
{
    internal static Dictionary<string, bool> Run()
    {
        var checks = new Dictionary<string, bool>();
        static string Snapshot(CricketMatchManager game) => JsonSerializer.Serialize(game.ToState());
        static void Dot(CricketMatchManager game)
        {
            if (game.CurrentInnings!.NeedsBowlerSelection) game.ConfirmBowler(game.SuggestedNextBowler());
            game.RecordDelivery(0, CricketDeliveryType.Dot);
        }
        static CricketMatchManager Chase()
        {
            var game = CricketReliabilityChecks.CreateMatch(true);
            game.RecordDelivery(6, CricketDeliveryType.Six); game.EndCurrentInnings(); game.StartNextInnings();
            game.SetStriker(0); game.SetNonStriker(1); game.ConfirmBowler(0);
            return game;
        }
        {
            var game = CricketReliabilityChecks.CreateMatch(true);
            for (int i = 0; i < 6; i++) Dot(game);
            string token = game.MatchConditionsToken, before = Snapshot(game);
            checks["rain-revision-rejects-first-innings-target-atomically"] = !game.TryApplyRainAdjustment(10, 50, token, out _) && Snapshot(game) == before;
            checks["rain-revision-reduces-first-innings-without-rewriting-history"] = game.TryApplyRainAdjustment(1, null, token, out _)
                && game.CurrentOverLimit == 1 && game.CurrentInnings!.IsComplete && game.CurrentInnings.Deliveries.Count == 6 && game.HasRainAdjustment;
            checks["raising-rain-limit-reopens-natural-end-and-requires-new-bowler"] = game.TryApplyRainAdjustment(2, null, game.MatchConditionsToken, out _)
                && !game.CurrentInnings!.IsComplete && game.CurrentInnings.NeedsBowlerSelection;
            Dot(game);
            before = Snapshot(game);
            checks["rain-limit-cannot-be-below-already-bowled-legal-balls"] = !game.TryApplyRainAdjustment(1, null, game.MatchConditionsToken, out _) && Snapshot(game) == before;
            var loaded = new CricketMatchManager(); loaded.LoadState(JsonSerializer.Deserialize<CricketSerializableState>(Snapshot(game))!);
            checks["rain-limit-survives-detached-recovery"] = loaded.CurrentOverLimit == 2 && loaded.HasRainAdjustment && loaded.CurrentInnings!.LegalBallsBowled == 7;
            checks["rain-limit-survives-undo-of-older-delivery"] = loaded.UndoLastDelivery() && loaded.CurrentOverLimit == 2 && loaded.CurrentInnings!.LegalBallsBowled == 6;
            loaded.TryStartAdditionalInnings(false, out _);
            checks["new-innings-does-not-inherit-other-teams-rain-override"] = loaded.CurrentOverLimit == 20 && !loaded.HasRainAdjustment && loaded.Target == 1;
        }
        {
            var game = Chase(); game.RecordDelivery(2, CricketDeliveryType.Runs);
            string before = Snapshot(game);
            checks["revised-chase-requires-explicit-target"] = !game.TryApplyRainAdjustment(10, null, game.MatchConditionsToken, out _) && Snapshot(game) == before;
            checks["external-target-updates-required-runs-rate-and-balls"] = game.TryApplyRainAdjustment(10, 30, game.MatchConditionsToken, out _)
                && game.Target == 30 && game.RunsRequired == 28 && game.BallsRemaining == 59 && game.RequiredRunRate == Math.Round(28 * 6d / 59, 2);
            var ball = game.CurrentInnings!.Deliveries[0];
            checks["rain-revision-allows-old-delivery-replay-with-override-retained"] = game.TryEditDelivery(ball, CricketMatchManager.DescribeDelivery(game.CurrentInnings, ball), out _)
                && game.Target == 30 && game.CurrentOverLimit == 10 && game.TotalRuns == 2;
            checks["clearing-rain-revision-restores-ledger-derived-target"] = game.TryClearRainAdjustment(game.MatchConditionsToken, out _)
                && game.Target == 7 && game.CurrentOverLimit == 20 && !game.HasRainAdjustment;
            string token = game.MatchConditionsToken; game.RecordDelivery(0, CricketDeliveryType.Dot);
            before = Snapshot(game);
            checks["open-conditions-sheet-rejects-new-delivery"] = !game.TryApplyRainAdjustment(8, 40, token, out _) && Snapshot(game) == before;
        }
        {
            var game = Chase(); game.RecordDelivery(2, CricketDeliveryType.Runs); game.RecordDelivery(2, CricketDeliveryType.Runs);
            game.TryApplyRainAdjustment(10, 3, game.MatchConditionsToken, out _);
            checks["revised-target-already-reached-completes-correct-team-result"] = game.CurrentInnings!.IsComplete
                && game.MatchResult?.StartsWith(game.TeamBName + " won by") == true && game.TotalRuns == 4;
            var ball = game.CurrentInnings.Deliveries[0];
            checks["replay-does-not-retrospectively-truncate-pre-rain-runs"] = game.TryEditDelivery(ball,
                CricketMatchManager.DescribeDelivery(game.CurrentInnings, ball), out _) && game.TotalRuns == 4 && game.CurrentInnings.Deliveries.Count == 2;
            checks["raising-revised-target-reopens-chase"] = game.TryApplyRainAdjustment(10, 9, game.MatchConditionsToken, out _)
                && !game.CurrentInnings.IsComplete && game.RunsRequired == 5;
        }
        {
            var game = Chase(); game.RecordDelivery(2, CricketDeliveryType.Runs);
            game.TryApplyRainAdjustment(1, 4, game.MatchConditionsToken, out _);
            for (int i = 1; i < 6; i++) Dot(game);
            checks["defended-revised-target-result-uses-revised-par-score"] = game.MatchResult == game.TeamAName + " won by 1 run";
            string token = game.MatchConditionsToken;
            checks["changing-format-reopens-limit-completion-and-clears-rain"] = game.TryUpdateMatchConditions(CricketFormat.MultiInnings, 40, token, out _)
                && game.Format == CricketFormat.MultiInnings && game.TotalInnings == 4 && !game.CurrentInnings!.IsComplete && !game.HasRainAdjustment;
            checks["historical-first-innings-limit-remains-frozen"] = game.AllInnings[0].OverLimitOverride == 20 && game.CurrentOverLimit == 40;
        }
        {
            var game = CricketReliabilityChecks.CreateMatch(true, 50);
            for (int i = 0; i < 59; i++) Dot(game);
            CricketOverlayFact Fact(CricketOverlayKind kind) => CricketOverlayAnalysis.Build(game).Single(fact => fact.Kind == kind);
            checks["drought-under-ten-overs-remains-legal-balls"] = Fact(CricketOverlayKind.SinceScore).Metrics[0] == new CricketOverlayMetric("LEGAL BALLS", "59");
            Dot(game);
            checks["ten-over-drought-uses-whole-overs"] = Fact(CricketOverlayKind.SinceScore).Title.StartsWith("OVERS")
                && Fact(CricketOverlayKind.SinceScore).Metrics[0] == new CricketOverlayMetric("OVERS", "10");
            Dot(game);
            checks["ten-over-one-ball-drought-uses-cricket-over-notation"] = Fact(CricketOverlayKind.SinceBoundary).Metrics[0] == new CricketOverlayMetric("OVERS", "10.1");
            game.RecordDelivery(2, CricketDeliveryType.Runs);
            var projection = Fact(CricketOverlayKind.Projection);
            double Rate(CricketOverlayMetric metric) => double.Parse(metric.Label.StartsWith("CURRENT ") ? metric.Label[8..] : metric.Label.Split(' ')[0], CultureInfo.CurrentCulture);
            var rates = projection.Metrics.Select(Rate).ToArray();
            checks["projection-rates-ascending-with-current-in-correct-position"] = rates.SequenceEqual(rates.Order())
                && projection.Metrics.Count(metric => metric.Label.StartsWith("CURRENT ")) == 1 && rates.Distinct().Count() == rates.Length;
        }
        return checks;
    }
}

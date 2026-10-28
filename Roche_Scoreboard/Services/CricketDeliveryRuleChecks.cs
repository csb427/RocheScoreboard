using System.Text.Json;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

internal static class CricketDeliveryRuleChecks
{
    internal static Dictionary<string, bool> Run()
    {
        var checks = new Dictionary<string, bool>();
        static string Snapshot(CricketMatchManager game) => JsonSerializer.Serialize(game.ToState());
        static void Ready(CricketMatchManager game)
        { if (game.CurrentInnings!.NeedsBowlerSelection) game.ConfirmBowler(game.SuggestedNextBowler()); }
        static CricketMatchManager FreeHitGame()
        { var game = CricketReliabilityChecks.CreateMatch(true); game.FreeHitsEnabled = true; game.RecordDelivery(1, CricketDeliveryType.NoBall); return game; }
        bool count = true, pips = true, restore = true, replay = true;
        foreach (bool replaceWide in new[] { false, true })
        foreach (bool replaceNoBall in new[] { false, true })
        for (int pattern = 0; pattern < 81; pattern++)
        {
            var game = CricketReliabilityChecks.CreateMatch(true);
            game.ReplaceWideBalls = replaceWide; game.ReplaceNoBalls = replaceNoBall;
            int digits = pattern, counted = 0, faced = 0;
            for (int ball = 0; ball < 4; ball++)
            {
                int kind = digits % 3; digits /= 3;
                var type = kind == 0 ? CricketDeliveryType.Dot : kind == 1 ? CricketDeliveryType.Wide : CricketDeliveryType.NoBall;
                bool consumes = kind == 0 || kind == 1 && !replaceWide || kind == 2 && !replaceNoBall;
                game.RecordDelivery(kind == 0 ? 0 : 1, type);
                if (consumes) counted++;
                if (kind != 1) faced++;
                var innings = game.CurrentInnings!;
                count &= innings.LegalBallsBowled == counted && innings.BowlingAttack.Sum(player => player.BallsBowled) == counted
                    && innings.BattingOrder.Sum(player => player.BallsFaced) == faced && innings.Deliveries[^1].IsCountedBall == consumes;
                pips &= CricketOverTracker.CreateSlots(innings).Length == 6 + ball + 1 - counted;
            }
            var loaded = new CricketMatchManager(); loaded.LoadState(JsonSerializer.Deserialize<CricketSerializableState>(Snapshot(game))!);
            restore &= loaded.ReplaceWideBalls == replaceWide && loaded.ReplaceNoBalls == replaceNoBall && loaded.CurrentInnings!.LegalBallsBowled == counted;
            var first = loaded.CurrentInnings!.Deliveries[0];
            replay &= loaded.TryEditDelivery(first, CricketMatchManager.DescribeDelivery(loaded.CurrentInnings, first), out _)
                && loaded.CurrentInnings.LegalBallsBowled == counted && loaded.CurrentInnings.BattingOrder.Sum(player => player.BallsFaced) == faced;
            while (counted < 6) { loaded.RecordDelivery(0, CricketDeliveryType.Dot); counted++; }
            count &= loaded.CurrentInnings.LegalBallsBowled == 6 && loaded.CurrentInnings.NeedsBowlerSelection;
        }
        checks["324-replacement-rule-permutations-reconcile-counted-balls-bowlers-and-batters"] = count;
        checks["324-replacement-rule-permutations-size-pips-from-recorded-flags"] = pips;
        checks["324-replacement-rule-permutations-survive-recovery"] = restore;
        checks["324-replacement-rule-permutations-survive-history-replay"] = replay;
        {
            var legacy = JsonSerializer.Deserialize<CricketSerializableState>("{}")!;
            checks["legacy-match-defaults-replace-extras-and-disable-free-hits"] = legacy.ReplaceWideBalls && legacy.ReplaceNoBalls && !legacy.FreeHitsEnabled;
            checks["legacy-deliveries-use-standard-counting-fallback"] = !new CricketDelivery { Type = CricketDeliveryType.Wide }.IsCountedBall
                && !new CricketDelivery { Type = CricketDeliveryType.NoBall }.IsCountedBall && new CricketDelivery { Type = CricketDeliveryType.Dot }.IsCountedBall;
            var game = CricketReliabilityChecks.CreateMatch(true); game.RecordDelivery(1, CricketDeliveryType.NoBall);
            checks["free-hit-remains-optional-and-default-off"] = !game.FreeHitPending && !game.CurrentInnings!.FreeHitPending;
        }
        foreach (bool replaceWide in new[] { false, true })
        foreach (bool replaceNoBall in new[] { false, true })
        {
            var game = CricketReliabilityChecks.CreateMatch(true); game.FreeHitsEnabled = true;
            game.ReplaceWideBalls = replaceWide; game.ReplaceNoBalls = replaceNoBall;
            for (int i = 0; i < 5; i++) game.RecordDelivery(0, CricketDeliveryType.Dot);
            game.RecordDelivery(1, CricketDeliveryType.NoBall);
            bool correct = game.FreeHitPending;
            Ready(game); game.RecordDelivery(1, CricketDeliveryType.Wide);
            correct &= game.FreeHitPending && game.CurrentInnings!.Deliveries[^1].WasFreeHit;
            Ready(game); game.RecordDelivery(1, CricketDeliveryType.NoBall);
            correct &= game.FreeHitPending && game.CurrentInnings!.Deliveries[^1].WasFreeHit;
            var saved = new CricketMatchManager(); saved.LoadState(JsonSerializer.Deserialize<CricketSerializableState>(Snapshot(game))!);
            correct &= saved.FreeHitPending && saved.FreeHitsEnabled;
            Ready(saved); saved.RecordDelivery(0, CricketDeliveryType.Dot);
            correct &= !saved.FreeHitPending && saved.CurrentInnings!.Deliveries[^1].WasFreeHit;
            correct &= saved.UndoLastDelivery() && saved.FreeHitPending;
            checks[$"free-hit-chain-crosses-over-and-illegal-deliveries-{replaceWide}-{replaceNoBall}"] = correct;
        }
        foreach (var dismissal in new[] { DismissalType.Bowled, DismissalType.Caught, DismissalType.LBW, DismissalType.Stumped, DismissalType.HitWicket, DismissalType.CaughtAndBowled, DismissalType.Other })
        {
            var game = FreeHitGame(); string before = Snapshot(game);
            game.RecordDelivery(0, CricketDeliveryType.Wicket, dismissal: dismissal);
            checks[$"free-hit-protects-{dismissal}-without-consuming-delivery"] = Snapshot(game) == before && game.FreeHitPending && game.LastOperationError.Length > 0;
        }
        foreach (var dismissal in new[] { DismissalType.RunOut, DismissalType.ObstructingField, DismissalType.HitBallTwice })
        {
            var game = FreeHitGame(); game.RecordDelivery(0, CricketDeliveryType.Wicket, dismissal: dismissal);
            checks[$"free-hit-allows-{dismissal}-without-bowler-credit"] = game.TotalWickets == 1 && !game.FreeHitPending
                && game.CurrentInnings!.BowlingAttack[0].Wickets == 0 && game.CurrentInnings.Deliveries[^1].WasFreeHit;
        }
        {
            var game = FreeHitGame(); game.RecordDelivery(0, CricketDeliveryType.Dot);
            var first = game.CurrentInnings!.Deliveries[0];
            checks["editing-away-no-ball-recalculates-free-hit-markers"] = game.TryEditDelivery(first,
                CricketMatchManager.DescribeDelivery(game.CurrentInnings, first) with { Type = CricketDeliveryType.Dot, Runs = 0 }, out _)
                && game.CurrentInnings.LegalBallsBowled == 2 && !game.CurrentInnings.Deliveries[1].WasFreeHit && !game.FreeHitPending;
            game = CricketReliabilityChecks.CreateMatch(true); game.FreeHitsEnabled = true;
            game.RecordDelivery(0, CricketDeliveryType.Dot); game.RecordDelivery(0, CricketDeliveryType.Wicket, dismissal: DismissalType.Caught);
            first = game.CurrentInnings!.Deliveries[0]; string before = Snapshot(game);
            checks["history-edit-cannot-create-impossible-free-hit-wicket"] = !game.TryEditDelivery(first,
                CricketMatchManager.DescribeDelivery(game.CurrentInnings, first) with { Type = CricketDeliveryType.NoBall, Runs = 1 }, out _) && Snapshot(game) == before;
        }
        {
            var game = CricketReliabilityChecks.CreateMatch(true); game.ReplaceWideBalls = false; game.RecordDelivery(1, CricketDeliveryType.Wide);
            game.ReplaceWideBalls = true; var first = game.CurrentInnings!.Deliveries[0];
            checks["recorded-counting-survives-later-rule-preference-change"] = game.TryEditDelivery(first,
                CricketMatchManager.DescribeDelivery(game.CurrentInnings, first), out _) && game.CurrentInnings.LegalBallsBowled == 1
                && CricketOverlayAnalysis.IsLegal(game.CurrentInnings.Deliveries[0]);
        }
        return checks;
    }
}

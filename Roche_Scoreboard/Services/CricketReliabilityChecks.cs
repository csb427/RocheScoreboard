using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

/// <summary>Isolated cricket regressions; never reads or writes operator data.</summary>
internal static class CricketReliabilityChecks
{
    internal static Dictionary<string, bool> Run()
    {
        Dictionary<string, bool> checks = CricketEditingChecks.Run();
        foreach (var check in CricketOverlayChecks.Run()) checks[check.Key] = check.Value;
        foreach (var check in CricketTossChecks.Run()) checks[check.Key] = check.Value;
        foreach (var check in CricketWorkflowChecks.Run()) checks[check.Key] = check.Value;
        foreach (var check in CricketOverAndStrikeChecks.Run()) checks[check.Key] = check.Value;
        foreach (var check in CricketConditionsChecks.Run()) checks[check.Key] = check.Value;
        foreach (var check in CricketDeliveryRuleChecks.Run()) checks[check.Key] = check.Value;
        foreach (var check in CricketDeliveryLogChecks.Run()) checks[check.Key] = check.Value;
        checks["over-tracker-starts-with-six-empty-circles"] = CricketOverTracker.CreateSlots([]).SequenceEqual(Enumerable.Repeat("", 6));
        checks["over-tracker-wide-and-no-ball-add-slots"] = CricketOverTracker.CreateSlots(["1wd", "•", "2nb", "4"]).SequenceEqual(new[] { "1wd", "•", "2nb", "4", "", "", "", "" });
        checks["over-tracker-wickets-byes-do-not-add-slots"] = CricketOverTracker.CreateSlots(["W", "2b", "1lb", "6"]).Length == 6;
        checks["over-tracker-many-extras-remain-visible"] = CricketOverTracker.CreateSlots(Enumerable.Repeat("1wd", 24).ToArray()).Length == 30;
        checks["summary-paginates-five-then-six"] = Views.CricketSummaryControl.PageRange(11, 0) == (0, 5) && Views.CricketSummaryControl.PageRange(11, 1) == (5, 6);
        bool themedContrast = true;
        for (int red = 0; red <= 255; red += 17)
        for (int green = 0; green <= 255; green += 17)
        for (int blue = 0; blue <= 255; blue += 17)
        {
            var colour = System.Windows.Media.Color.FromRgb((byte)red, (byte)green, (byte)blue);
            double strength = BroadcastTeamTheme.DecorationStrength(colour);
            var (dark, light) = BroadcastTeamTheme.Extremes(colour, strength);
            var text = ContrastHelper.GetContrastForeground(colour);
            themedContrast &= ContrastHelper.ContrastRatio(text, dark) >= 4.5 && ContrastHelper.ContrastRatio(text, light) >= 4.5;
        }
        checks["4096-team-colours-preserve-contrast-through-logo-and-gradient-washes"] = themedContrast;
        CricketSetupResult setup = new();
        setup.FillEmptySquads();
        checks["empty-squads-provide-two-distinct-elevens"] = setup.TeamAPlayers.Count == 11
            && setup.TeamBPlayers.Count == 11
            && setup.TeamAPlayers.Select(player => player.FullName).Distinct().Count() == 11
            && setup.TeamAPlayers[0].FullName == "Player 1"
            && setup.TeamAPlayers[^1].FullName == "Player 11"
            && !ReferenceEquals(setup.TeamAPlayers[0], setup.TeamBPlayers[0]);

        CricketPlayer named = new() { FirstName = "Alex", LastName = "Smith", IsWicketkeeper = true };
        CricketSetupResult mixed = new() { TeamAPlayers = [named, new() { LastName = "Jones" }] };
        mixed.FillEmptySquads();
        checks["named-squad-preserved-while-empty-opposition-is-filled"] = mixed.TeamAPlayers.Count == 2
            && ReferenceEquals(mixed.TeamAPlayers[0], named) && named.IsWicketkeeper
            && mixed.TeamBPlayers.Count == 11;

        CricketMatchManager match = CreateMatch();
        match.RecordDelivery(4, CricketDeliveryType.Four);
        checks["scoring-blocked-before-selections"] = match.TotalRuns == 0;
        match.SetStriker(0);
        checks["one-batter-still-needs-selection"] = match.CurrentInnings!.NeedsBatterSelection;
        match.SetNonStriker(0);
        checks["same-player-cannot-fill-both-ends"] = match.CurrentInnings.NonStrikerIndex == -1;
        match.SetNonStriker(1);
        checks["opening-pair-unblocks-batter-selection"] = !match.CurrentInnings.NeedsBatterSelection;
        match.RecordDelivery(4, CricketDeliveryType.Four);
        checks["bowler-still-required"] = match.TotalRuns == 0;
        match.ConfirmBowler(0);
        match.RecordDelivery(4, CricketDeliveryType.Four);
        checks["opening-selection-can-score"] = match.TotalRuns == 4 && match.CurrentInnings.LegalBallsBowled == 1;

        match = CreateMatch(ready: true);
        int batterPrompts = 0, bowlerPrompts = 0;
        match.BatterSelectionNeeded += () => batterPrompts++;
        match.BowlerSelectionNeeded += () => bowlerPrompts++;
        for (int ball = 0; ball < 5; ball++) match.RecordDelivery(0, CricketDeliveryType.Dot);
        match.RecordDelivery(0, CricketDeliveryType.Wicket, "bowled");
        CricketInnings innings = match.CurrentInnings!;
        checks["sixth-ball-wicket-requires-batter-before-bowler"] = batterPrompts == 1 && bowlerPrompts == 0
            && innings.NonStriker!.IsOut && innings.NeedsBatterSelection && innings.NeedsBowlerSelection;
        match.SelectNewBatter(2);
        checks["sixth-ball-wicket-replaces-dismissed-end"] = innings.NonStrikerIndex == 2
            && innings.StrikerIndex == 1 && !innings.NeedsBatterSelection && bowlerPrompts == 1;
        match.ConfirmBowler(0);
        checks["consecutive-over-bowler-rejected"] = innings.NeedsBowlerSelection;
        match.ConfirmBowler(1);
        match.RecordDelivery(1, CricketDeliveryType.Runs);
        checks["play-continues-after-sixth-ball-wicket"] = innings.LegalBallsBowled == 7 && innings.TotalRuns == 1;

        match = CreateMatch(ready: true, overs: 1);
        batterPrompts = bowlerPrompts = 0;
        match.BatterSelectionNeeded += () => batterPrompts++;
        match.BowlerSelectionNeeded += () => bowlerPrompts++;
        for (int ball = 0; ball < 5; ball++) match.RecordDelivery(0, CricketDeliveryType.Dot);
        match.RecordDelivery(0, CricketDeliveryType.Wicket, "bowled");
        checks["final-ball-does-not-open-unneeded-selections"] = match.CurrentInnings!.IsComplete
            && !match.CurrentInnings.NeedsBatterSelection && !match.CurrentInnings.NeedsBowlerSelection
            && batterPrompts == 0 && bowlerPrompts == 0;
        match.UndoLastDelivery();
        checks["undo-reopens-completed-innings"] = !match.CurrentInnings.IsComplete
            && match.CurrentInnings.LegalBallsBowled == 5 && !match.CurrentInnings.Striker!.IsOut;
        match.RecordDelivery(0, CricketDeliveryType.Dot);
        match.StartNextInnings();
        checks["next-innings-has-fresh-independent-players"] = match.CurrentInningsNumber == 2
            && match.AllInnings[0].IsComplete && match.CurrentInnings!.TotalRuns == 0
            && match.CurrentInnings.NeedsBatterSelection && match.CurrentInnings.NeedsBowlerSelection
            && match.AllInnings[0].BattingOrder.All(player => player.Runs == 0);
        SelectPlayers(match);
        match.RecordDelivery(1, CricketDeliveryType.Wide);
        checks["chase-finishes-on-winning-extra"] = match.RunsRequired == 0 && match.CurrentInnings!.IsComplete;
        int deliveries = match.CurrentInnings!.Deliveries.Count;
        match.RecordDelivery(6, CricketDeliveryType.Six);
        match.StartNextInnings();
        checks["finished-match-rejects-further-deliveries-and-innings"] = match.CurrentInningsNumber == 2
            && match.CurrentInnings.Deliveries.Count == deliveries;

        match = CreateMatch(ready: true);
        CricketSerializableState restoredState = JsonSerializer.Deserialize<CricketSerializableState>(
            JsonSerializer.Serialize(match.ToState()))!;
        // Saved by a version that left this flag set after choosing both batters.
        restoredState.AllInnings[0].NeedsBatterSelection = true;
        match.LoadState(restoredState);
        match.RecordDelivery(1, CricketDeliveryType.Runs);
        checks["restore-repairs-stale-opening-selection-flag"] = match.TotalRuns == 1;
        int beforeInvalid = match.TotalRuns;
        match.RecordDelivery(20, (CricketDeliveryType)999);
        checks["invalid-delivery-kind-is-ignored"] = match.TotalRuns == beforeInvalid;
        match.CurrentInnings!.CurrentBowlerIndex = 999;
        match.RecordDelivery(4, CricketDeliveryType.Four);
        checks["out-of-range-player-cannot-score"] = match.TotalRuns == beforeInvalid;

        bool randomized = true;
        for (int seed = 0; seed < 40; seed++)
        {
            Random random = new(seed);
            match = CreateMatch(ready: true);
            for (int operation = 0; operation < 100; operation++)
            {
                if (match.CurrentInnings!.IsComplete) match = CreateMatch(ready: true);
                SelectPlayers(match);
                string before = JsonSerializer.Serialize(match.ToState());
                var type = (CricketDeliveryType)random.Next(0, 9);
                int runs = type switch
                {
                    CricketDeliveryType.Dot or CricketDeliveryType.Wicket => 0,
                    CricketDeliveryType.Four => 4,
                    CricketDeliveryType.Six => 6,
                    _ => random.Next(1, 4)
                };
                match.RecordDelivery(runs, type);
                CricketInnings current = match.CurrentInnings!;
                randomized &= current.TotalRuns == current.BattingOrder.Sum(player => player.Runs) + current.TotalExtras
                    && current.LegalBallsBowled == current.BowlingAttack.Sum(player => player.BallsBowled)
                    && current.TotalExtras == current.Wides + current.NoBalls + current.Byes + current.LegByes;
                if (random.Next(3) == 0)
                {
                    randomized &= match.UndoLastDelivery()
                        && JsonSerializer.Serialize(match.ToState()) == before;
                }
                else if (random.Next(4) == 0)
                {
                    string saved = JsonSerializer.Serialize(match.ToState());
                    match.LoadState(JsonSerializer.Deserialize<CricketSerializableState>(saved)!);
                    randomized &= JsonSerializer.Serialize(match.ToState()) == saved;
                }
            }
        }
        checks["4000-random-scoring-undo-and-save-restore-operations"] = randomized;
        return checks;
    }

    internal static CricketMatchManager CreateMatch(bool ready = false, int overs = 20)
    {
        CricketSetupResult setup = new();
        setup.FillEmptySquads();
        CricketMatchManager match = new()
        {
            TeamAPlayers = setup.TeamAPlayers,
            TeamBPlayers = setup.TeamBPlayers,
            TotalOvers = overs
        };
        match.StartMatch();
        if (ready) SelectPlayers(match);
        return match;
    }

    private static void SelectPlayers(CricketMatchManager match)
    {
        CricketInnings innings = match.CurrentInnings!;
        if (innings.Striker is null) match.SetStriker(0);
        if (innings.NonStriker is null) match.SetNonStriker(1);
        if (innings.NeedsBatterSelection)
        {
            int next = innings.BattingOrder.FindIndex(player => !player.IsOut
                && !ReferenceEquals(player, innings.Striker) && !ReferenceEquals(player, innings.NonStriker));
            if (next >= 0) match.SelectNewBatter(next);
        }
        if (innings.NeedsBowlerSelection) match.ConfirmBowler(match.SuggestedNextBowler());
    }
}

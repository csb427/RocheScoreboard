using System.Text.Json;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

internal static class CricketWorkflowChecks
{
    internal static Dictionary<string, bool> Run()
    {
        var checks = new Dictionary<string, bool>();
        static string Snapshot(CricketMatchManager match) => JsonSerializer.Serialize(match.ToState());
        static void Select(CricketMatchManager match)
        { match.SetStriker(0); match.SetNonStriker(1); match.ConfirmBowler(0); }
        {
            var game = CricketReliabilityChecks.CreateMatch(true, 1);
            for (int i = 0; i < 6; i++) game.RecordDelivery(0, CricketDeliveryType.Dot);
            checks["over-limit-requires-explicit-continuation"] = game.CompletionDecisionRequired && game.TotalRuns == 0;
            checks["operator-can-continue-past-over-limit"] = game.TryContinueCurrentInnings(out _) && !game.CompletionDecisionRequired;
            game.ConfirmBowler(1); game.RecordDelivery(4, CricketDeliveryType.Four);
            checks["continued-innings-keeps-ball-ledger-and-score"] = game.TotalRuns == 4 && game.CurrentInnings!.LegalBallsBowled == 7;
        }
        {
            var game = CricketReliabilityChecks.CreateMatch(true);
            game.RecordDelivery(4, CricketDeliveryType.Four); game.EndCurrentInnings(); game.StartNextInnings(); Select(game);
            game.RecordDelivery(6, CricketDeliveryType.Six);
            checks["target-result-preserved-until-operator-decision"] = game.CompletionDecisionRequired && game.MatchResult?.Contains("won by") == true;
            checks["explicit-continue-reopens-target-without-reset"] = game.TryContinueCurrentInnings(out _) && game.MatchResult is null;
            game.RecordDelivery(2, CricketDeliveryType.Runs);
            checks["continued-chase-does-not-clamp-new-runs"] = game.TotalRuns == 8;
            checks["finish-locks-and-persists-result"] = game.TryFinishMatch(out _) && game.IsMatchFinalized && game.MatchResult is not null;
            var loaded = new CricketMatchManager(); loaded.LoadState(JsonSerializer.Deserialize<CricketSerializableState>(Snapshot(game))!);
            int before = loaded.TotalRuns; loaded.RecordDelivery(2, CricketDeliveryType.Runs);
            checks["restored-finalized-match-rejects-scoring"] = loaded.IsMatchFinalized && loaded.TotalRuns == before && loaded.FinalResult == game.FinalResult;
        }
        {
            var game = CricketReliabilityChecks.CreateMatch(true);
            checks["cannot-start-second-before-opponent-first"] = !game.TryStartAdditionalInnings(true, out _);
            checks["opponent-first-and-follow-on-allowed"] = game.TryStartAdditionalInnings(false, out _) && game.TryStartAdditionalInnings(false, out _)
                && game.CurrentInnings!.BattingTeamName == game.TeamBName;
            checks["fourth-innings-selects-team-with-one-innings"] = game.TryStartAdditionalInnings(true, out _)
                && game.AllInnings.Select(i => i.BattingTeamName).SequenceEqual(new[] { game.TeamAName, game.TeamBName, game.TeamBName, game.TeamAName });
            checks["fifth-innings-rejected-without-history-change"] = !game.TryStartAdditionalInnings(false, out _) && game.AllInnings.Count == 4;
        }
        foreach (var kind in Enum.GetValues<CricketBreakKind>().Where(k => k != CricketBreakKind.None))
        {
            var game = CricketReliabilityChecks.CreateMatch(true);
            game.SetBreak(kind, "Ground inspection"); game.RecordDelivery(2, CricketDeliveryType.Runs);
            var loaded = new CricketMatchManager(); loaded.LoadState(JsonSerializer.Deserialize<CricketSerializableState>(Snapshot(game))!);
            checks[$"break-{kind}-blocks-and-restores"] = game.TotalRuns == 0 && loaded.BreakKind == kind && loaded.BreakNote == "Ground inspection";
            loaded.SetBreak(CricketBreakKind.None); loaded.RecordDelivery(2, CricketDeliveryType.Runs);
            checks[$"break-{kind}-resumes-without-reset"] = loaded.TotalRuns == 2;
        }
        {
            var game = CricketReliabilityChecks.CreateMatch(true); game.RecordDelivery(2, CricketDeliveryType.Runs);
            var draft = game.CreateScorecardDraft(1); string before = Snapshot(game); draft.TotalRuns++;
            checks["inconsistent-scorecard-is-atomic"] = !game.TryApplyScorecard(draft, out _) && Snapshot(game) == before;
            draft = game.CreateScorecardDraft(1); draft.TotalRuns = 5; draft.Batters[0].Runs = 5; draft.Bowlers[0].RunsConceded = 5;
            checks["reconciled-scorecard-applies-as-checkpoint"] = game.TryApplyScorecard(draft, out _) && game.TotalRuns == 5 && game.HasScorecardAdjustments;
            var restored = new CricketMatchManager(); restored.LoadState(JsonSerializer.Deserialize<CricketSerializableState>(Snapshot(game))!);
            checks["checkpoint-and-revert-survive-recovery"] = restored.HasScorecardAdjustments && !restored.UndoLastDelivery()
                && restored.TryRevertLatestScorecardCorrection(1, out _) && restored.TotalRuns == 2;
            checks["older-delivery-edit-protected-after-checkpoint"] = !game.TryEditDelivery(game.CurrentInnings!.Deliveries[0],
                CricketMatchManager.DescribeDelivery(game.CurrentInnings, game.CurrentInnings.Deliveries[0]) with { Runs = 3 }, out _);
            game.RecordDelivery(2, CricketDeliveryType.Runs);
            var latest = game.CurrentInnings!.Deliveries[^1];
            checks["newer-delivery-edit-replays-from-checkpoint"] = game.TryEditDelivery(latest,
                CricketMatchManager.DescribeDelivery(game.CurrentInnings, latest) with { Runs = 3 }, out _) && game.TotalRuns == 8;
            checks["newer-balls-undo-to-checkpoint"] = game.UndoLastDelivery() && game.TotalRuns == 5 && !game.UndoLastDelivery();
            checks["reverting-checkpoint-restores-old-history-editability"] = game.TryRevertLatestScorecardCorrection(1, out _)
                && game.TotalRuns == 2 && game.UndoLastDelivery() && game.TotalRuns == 0;
        }
        {
            var game = CricketReliabilityChecks.CreateMatch(true); var draft = game.CreateScorecardDraft(1);
            game.RecordDelivery(0, CricketDeliveryType.Dot); string before = Snapshot(game);
            checks["stale-scorecard-cannot-overwrite-new-ball"] = !game.TryApplyScorecard(draft, out _) && Snapshot(game) == before;
            var players = game.GetLineup(true); players[0] = players[0] with { FirstName = "Renamed", IsCaptain = true, IsWicketkeeper = true, BattingPosition = 3 };
            players[2] = players[2] with { IsWicketkeeper = true, BattingPosition = 1 };
            checks["lineup-update-keeps-stats-identities-and-multiple-keepers"] = game.TryUpdateTeam(true, "New team", "NEW", players, out _)
                && game.CurrentInnings!.BattingOrder[0].BallsFaced == 1 && game.CurrentInnings.BattingOrder[0].IsCaptain
                && game.TeamAPlayers.Count(p => p.IsWicketkeeper) == 2;
            checks["lineup-rename-survives-old-ball-undo"] = game.UndoLastDelivery() && game.CurrentInnings!.BattingOrder[0].FirstName == "Renamed";
            checks["team-name-collision-rejected"] = !game.TryUpdateTeam(true, game.TeamBName, "NEW", players, out _);
        }
        {
            var game = CricketReliabilityChecks.CreateMatch(true);
            game.RecordDelivery(2, CricketDeliveryType.Runs);
            var draft = game.CreateScorecardDraft(1);
            var replacement = JsonSerializer.Deserialize<CricketSerializableState>(Snapshot(game))!;
            replacement.TeamAAbbr = "NEW";
            int revision = game.DeliveryRevision;
            game.TryContinueCurrentInnings(out _);
            game.LoadState(replacement);
            checks["state-replacement-clears-stale-operation-error"] = game.LastOperationError.Length == 0;
            string before = Snapshot(game);
            checks["same-count-state-replacement-invalidates-open-scorecard"] = game.DeliveryRevision > revision
                && !game.TryApplyScorecard(draft, out _) && Snapshot(game) == before;
        }
        {
            var game = CricketReliabilityChecks.CreateMatch(true); game.RecordDelivery(2, CricketDeliveryType.Runs);
            checks["wrong-batter-carry-remaps-ledger"] = game.TryReplaceBatter(true, 2, CricketBatterReplacementReason.WrongPlayer, true, out _)
                && game.CurrentInnings!.BattingOrder[2].Runs == 2 && game.CurrentInnings.BattingOrder[0].Runs == 0 && game.CurrentInnings.Deliveries[0].StrikerIndex == 2;
            checks["wrong-batter-carry-retains-correct-undo"] = game.UndoLastDelivery() && game.TotalRuns == 0 && game.CurrentInnings!.BattingOrder[2].Runs == 0;
        }
        foreach (var reason in new[] { CricketBatterReplacementReason.RetiredHurt, CricketBatterReplacementReason.RetiredNotOut, CricketBatterReplacementReason.RetiredOut })
        {
            var game = CricketReliabilityChecks.CreateMatch(true); game.RecordDelivery(2, CricketDeliveryType.Runs);
            checks[$"replacement-{reason}-preserves-outgoing-runs"] = game.TryReplaceBatter(true, 2, reason, false, out _)
                && game.CurrentInnings!.BattingOrder[0].Runs == 2 && game.CurrentInnings.BattingOrder[2].Runs == 0
                && game.TotalWickets == (reason == CricketBatterReplacementReason.RetiredOut ? 1 : 0) && game.CurrentInnings.BowlingAttack[0].Wickets == 0;
            checks[$"replacement-{reason}-revert-restores-pair"] = game.TryRevertLatestScorecardCorrection(1, out _) && game.CurrentInnings!.StrikerIndex == 0 && game.TotalWickets == 0;
        }
        {
            var game = CricketReliabilityChecks.CreateMatch(true);
            var draft = game.CreateScorecardDraft(1);
            for (int i = 0; i < draft.Batters.Count - 1; i++) draft.Batters[i].IsOut = true;
            draft.TotalWickets = draft.Batters.Count - 1;
            checks["all-out-scorecard-cannot-continue-without-correction"] = game.TryApplyScorecard(draft, out _)
                && game.CurrentInnings!.IsAllOut && !game.TryContinueCurrentInnings(out _);
            var invalid = game.CreateScorecardDraft(1); invalid.Bowlers[0].Maidens = 1;
            checks["scorecard-rejects-maiden-without-completed-over"] = !game.TryApplyScorecard(invalid, out _);
        }
        {
            var game = CricketReliabilityChecks.CreateMatch(true);
            game.SetBreak(CricketBreakKind.Tea);
            checks["starting-innings-clears-break-and-keeps-history"] = game.TryStartAdditionalInnings(false, out _)
                && game.BreakKind == CricketBreakKind.None && game.AllInnings.Count == 2;
        }
        {
            var game = CricketReliabilityChecks.CreateMatch(true); game.RecordDelivery(2, CricketDeliveryType.Runs);
            string before = Snapshot(game); int notifications = 0; game.MatchChanged += () => notifications++;
            var a = game.GetLineup(true); var b = game.GetLineup(false);
            b[0] = b[0] with { FirstName = "", LastName = "" };
            checks["two-team-edit-validates-both-before-mutation"] = !game.TryUpdateTeams("New A", "NA", a, "New B", "NB", b, out _)
                && Snapshot(game) == before && notifications == 0;
            string oldA = game.TeamAName, oldB = game.TeamBName;
            b = game.GetLineup(false);
            checks["atomic-team-name-swap-preserves-ledger-and-notifies-once"] = game.TryUpdateTeams(oldB, "AA", a, oldA, "BB", b, out _)
                && notifications == 1 && game.TeamAName == oldB && game.TeamBName == oldA
                && game.CurrentInnings!.BattingTeamName == oldB && game.CurrentInnings.BowlingTeamName == oldA
                && game.CurrentInnings.BattingOrder[0].Runs == 2 && game.UndoLastDelivery() && game.TotalRuns == 0;
        }
        return checks;
    }
}

using System.Text.Json;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class ExceptionalCricketTests
{
    private static CricketMatchManager Ready() => CricketReliabilityChecks.CreateMatch(true);
    private static string Snapshot(CricketMatchManager match) => JsonSerializer.Serialize(match.ToState());
    private static CricketMatchManager Reload(CricketMatchManager match)
    { var restored = new CricketMatchManager(); restored.LoadState(JsonSerializer.Deserialize<CricketSerializableState>(Snapshot(match))!); return restored; }

    [Theory]
    [InlineData(0, false)] [InlineData(5, false)] [InlineData(0, true)]
    public void UndeliveredRunOutChangesOnlyTheNonStrikerWicketAndCanBeUndoneAfterReload(int dots, bool freeHit)
    {
        var match = Ready(); match.ConfirmBowler(5); match.FreeHitsEnabled = freeHit;
        for (int i = 0; i < dots; i++) match.RecordDelivery(0, CricketDeliveryType.Dot);
        if (freeHit) match.RecordDelivery(1, CricketDeliveryType.NoBall);
        string before = Snapshot(match); var innings = match.CurrentInnings!;
        int count = innings.Deliveries.Count, balls = innings.LegalBallsBowled, faced = innings.Striker!.BallsFaced;
        int deliveryEvents = 0; match.DeliveryAdded += _ => deliveryEvents++;
        Assert.True(match.TryRecordNonStrikerRunOutBeforeDelivery(match.WorkflowToken, out string error), error);
        Assert.Equal(count, innings.Deliveries.Count); Assert.Equal(balls, innings.LegalBallsBowled);
        Assert.Equal(balls, innings.BowlingAttack.Sum(player => player.BallsBowled));
        Assert.Equal(faced, innings.Striker!.BallsFaced); Assert.Equal(0, innings.BowlingAttack.Sum(player => player.Wickets));
        Assert.Equal(1, innings.TotalWickets); Assert.True(innings.NonStriker!.IsOut); Assert.Equal(DismissalType.RunOut, innings.NonStriker.HowOut);
        Assert.Equal(freeHit ? 1 : 0, innings.TotalRuns); Assert.Equal(freeHit ? 1 : 0, innings.TotalExtras);
        Assert.Equal(0, innings.PartnershipRuns); Assert.Equal(0, innings.PartnershipBalls);
        Assert.Equal("Player 6", innings.NonStriker.DismissalFielder); Assert.Null(innings.NonStriker.DismissalBowler);
        Assert.Equal(5, Assert.Single(innings.ScorecardChanges).NonDeliveryWicket!.BowlerIndex);
        Assert.Equal(0, innings.StrikerIndex); Assert.Equal(freeHit, match.FreeHitPending); Assert.Equal(0, deliveryEvents);
        Assert.Single(match.CreateScorecardDraft(1).FallOfWickets);
        match = Reload(match); Assert.True(match.CanUndoNonDeliveryRunOut);
        Assert.True(match.TryUndoLastScoringAction(match.WorkflowToken, out error), error);
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void LaterBallsMustBeUndoneBeforeTheNonDeliveryCheckpoint()
    {
        var match = Ready(); match.RecordDelivery(4, CricketDeliveryType.Four);
        string before = Snapshot(match); var oldBall = match.CurrentInnings!.Deliveries[0];
        Assert.True(match.TryRecordNonStrikerRunOutBeforeDelivery(match.WorkflowToken, out _));
        Assert.False(match.TryEditDelivery(oldBall, CricketMatchManager.DescribeDelivery(match.CurrentInnings, oldBall) with { Runs = 6 }, out _));
        match.SelectNewBatter(2); match.RecordDelivery(1, CricketDeliveryType.Runs);
        Assert.False(match.CanUndoNonDeliveryRunOut);
        Assert.True(match.TryUndoLastScoringAction(match.WorkflowToken, out _));
        Assert.True(match.CanUndoNonDeliveryRunOut);
        Assert.True(match.TryUndoLastScoringAction(match.WorkflowToken, out _));
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void UndeliveredAllOutFinishesWithoutASelectionPromptOrAnExtraBall()
    {
        var match = Ready(); match.TeamAPlayers = match.TeamAPlayers.Take(2).ToList(); match.StartMatch();
        match.SetStriker(0); match.SetNonStriker(1); match.ConfirmBowler(0); int prompts = 0;
        match.BatterSelectionNeeded += () => prompts++;
        Assert.True(match.TryRecordNonStrikerRunOutBeforeDelivery(match.WorkflowToken, out _));
        Assert.True(match.CurrentInnings!.IsAllOut); Assert.True(match.CurrentInnings.IsComplete);
        Assert.Equal(0, prompts); Assert.Equal(0, match.CurrentInnings.LegalBallsBowled);
        Assert.True(match.TryUndoLastScoringAction(match.WorkflowToken, out _)); Assert.False(match.CurrentInnings.IsComplete);
    }

    [Fact]
    public void StaleNonDeliveryActionDoesNotChangeStateRevisionTokenOrEvents()
    {
        var match = Ready(); string stale = match.WorkflowToken; match.SetNonStriker(2);
        string before = Snapshot(match), token = match.WorkflowToken; int revision = match.DeliveryRevision, events = 0;
        match.MatchChanged += () => events++;
        Assert.False(match.TryRecordNonStrikerRunOutBeforeDelivery(stale, out string error));
        Assert.Contains("changed", error); Assert.Equal(before, Snapshot(match));
        Assert.Equal(token, match.WorkflowToken); Assert.Equal(revision, match.DeliveryRevision); Assert.Equal(0, events);
    }

    [Theory]
    [InlineData(CricketMatchResultKind.Abandoned, "Match abandoned")]
    [InlineData(CricketMatchResultKind.NoResult, "No result")]
    public void ExplicitOutcomePreservesFiguresAndReopensTheExactPriorRainBreakAfterReload(CricketMatchResultKind kind, string expected)
    {
        var match = Ready(); match.RecordDelivery(4, CricketDeliveryType.Four); match.SetBreak(CricketBreakKind.Rain, "Wet ground");
        string before = Snapshot(match);
        Assert.True(match.TrySetMatchOutcome(kind, "Umpires confirmed", match.WorkflowToken, out string error), error);
        Assert.True(match.IsMatchFinalized); Assert.False(match.CurrentInnings!.IsComplete);
        Assert.Equal(4, match.TotalRuns); Assert.Equal(expected, match.MatchResult); Assert.Equal("Umpires confirmed", match.ResultNote);
        Assert.True(match.HasRecoverableCricketState);
        match.RecordDelivery(6, CricketDeliveryType.Six); Assert.Equal(4, match.TotalRuns);
        match = Reload(match); Assert.Equal(kind, match.ResultKind); Assert.Equal(expected, match.MatchResult);
        Assert.True(match.TryReopenMatch(match.WorkflowToken, out error), error); Assert.Equal(before, Snapshot(match));
    }

    [Theory]
    [InlineData(CricketMatchResultKind.Abandoned)] [InlineData(CricketMatchResultKind.NoResult)]
    public void ScorecardCorrectionsAndTeamRenamingDoNotReplaceAnExplicitOutcomeWithAWinner(CricketMatchResultKind kind)
    {
        var match = Ready(); match.RecordDelivery(4, CricketDeliveryType.Four);
        Assert.True(match.TrySetMatchOutcome(kind, "Officials decided", match.WorkflowToken, out _)); string result = match.MatchResult!;
        var draft = match.CreateScorecardDraft(1); draft.TotalRuns = 6; draft.Batters[0].Runs = 6; draft.Bowlers[0].RunsConceded = 6;
        Assert.True(match.TryApplyScorecard(draft, out string error), error); Assert.Equal(result, match.MatchResult); Assert.Equal(kind, match.ResultKind);
        Assert.True(match.TryUpdateTeam(true, "Renamed team", "NEW", match.GetLineup(true), out error), error);
        Assert.Equal(result, match.MatchResult); Assert.Equal("Officials decided", match.ResultNote);
        Assert.True(match.TryReopenMatch(match.WorkflowToken, out error), error); Assert.Equal(6, match.TotalRuns);
    }

    [Fact]
    public void EarlyFinishDoesNotInventARunMarginForAnUnfinishedChase()
    {
        var match = Ready(); match.RecordDelivery(4, CricketDeliveryType.Four); match.EndCurrentInnings(); match.StartNextInnings();
        match.SetStriker(0); match.SetNonStriker(1); match.ConfirmBowler(0); match.RecordDelivery(1, CricketDeliveryType.Runs);
        Assert.True(match.TryFinishMatch(out _)); Assert.DoesNotContain("won", match.MatchResult!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CricketMatchResultKind.Unspecified, match.ResultKind);
    }

    [Fact]
    public void AbandonmentBeforeTheTossPersistsAndPreventsTossUntilReopened()
    {
        var match = Ready(); match.TossPending = true; match.StartMatch(); string before = Snapshot(match);
        Assert.True(match.TrySetMatchOutcome(CricketMatchResultKind.Abandoned, "No play", match.WorkflowToken, out _));
        Assert.False(match.CompleteToss(true, true)); Assert.True(match.HasRecoverableCricketState);
        match = Reload(match); Assert.True(match.TryReopenMatch(match.WorkflowToken, out _)); Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void StaleOrDuplicateEndAndReopenActionsLeaveSavedStateAndRevisionsUnchanged()
    {
        var match = Ready(); string stale = match.WorkflowToken; match.RecordDelivery(1, CricketDeliveryType.Runs);
        string before = Snapshot(match); int revision = match.DeliveryRevision;
        Assert.False(match.TrySetMatchOutcome(CricketMatchResultKind.NoResult, "", stale, out _));
        Assert.Equal(before, Snapshot(match)); Assert.Equal(revision, match.DeliveryRevision);
        Assert.True(match.TrySetMatchOutcome(CricketMatchResultKind.NoResult, "Official", match.WorkflowToken, out _));
        string closed = Snapshot(match); revision = match.DeliveryRevision;
        Assert.False(match.TrySetMatchOutcome(CricketMatchResultKind.Abandoned, "Different", match.WorkflowToken, out _));
        Assert.False(match.TryReopenMatch(stale, out _)); Assert.Equal(closed, Snapshot(match)); Assert.Equal(revision, match.DeliveryRevision);
    }

    [Theory]
    [InlineData("break")] [InlineData("selection")] [InlineData("bowler")] [InlineData("ended")]
    public void UnavailableRunOutActionsHaveNoStateRevisionOrNotificationEffects(string reason)
    {
        var match = Ready();
        if (reason == "break") match.SetBreak(CricketBreakKind.Rain);
        if (reason == "selection") match.RecordDelivery(0, CricketDeliveryType.Wicket);
        if (reason == "bowler") { for (int i = 0; i < 6; i++) match.RecordDelivery(0, CricketDeliveryType.Dot); }
        if (reason == "ended") Assert.True(match.TrySetMatchOutcome(CricketMatchResultKind.NoResult, "", match.WorkflowToken, out _));
        string before = Snapshot(match), token = match.WorkflowToken; int revision = match.DeliveryRevision, events = 0;
        match.MatchChanged += () => events++;
        Assert.False(match.TryRecordNonStrikerRunOutBeforeDelivery(token, out string error)); Assert.NotEmpty(error);
        Assert.Equal(before, Snapshot(match)); Assert.Equal(token, match.WorkflowToken);
        Assert.Equal(revision, match.DeliveryRevision); Assert.Equal(0, events);
    }

    [Fact]
    public void LaterInningsProtectAnEarlierNonDeliveryCheckpoint()
    {
        var match = Ready(); Assert.True(match.TryRecordNonStrikerRunOutBeforeDelivery(match.WorkflowToken, out _));
        match.EndCurrentInnings(); match.StartNextInnings(); string before = Snapshot(match);
        Assert.False(match.TryRevertLatestScorecardCorrection(1, out string error));
        Assert.Contains("later innings", error); Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void CorrectingAFinalisedTargetCanReopenTheChaseWithoutDiscardingTheCorrection()
    {
        var match = Ready(); match.RecordDelivery(4, CricketDeliveryType.Four); match.EndCurrentInnings(); match.StartNextInnings();
        match.SetStriker(0); match.SetNonStriker(1); match.ConfirmBowler(0); match.RecordDelivery(6, CricketDeliveryType.Six);
        Assert.True(match.TryFinishMatch(out _)); Assert.Equal(CricketMatchResultKind.ScoreBased, match.ResultKind);
        var draft = match.CreateScorecardDraft(1); draft.TotalRuns = 6; draft.Batters[0].Runs = 6; draft.Bowlers[0].RunsConceded = 6;
        Assert.True(match.TryApplyScorecard(draft, out string error), error);
        Assert.Contains("needs review", match.MatchResult);
        match = Reload(match); Assert.True(match.TryReopenMatch(match.WorkflowToken, out error), error);
        Assert.False(match.CurrentInnings!.IsComplete); Assert.Equal(7, match.Target); Assert.Equal(1, match.RunsRequired);
        Assert.Equal(6, match.AllInnings[0].TotalRuns); Assert.Null(match.MatchResult);
    }

    [Fact]
    public void ExplicitOutcomeSurvivesRevertingScorecardCorrectionsAndRetainsPendingFreeHitForReopening()
    {
        var match = Ready(); match.FreeHitsEnabled = true; match.RecordDelivery(1, CricketDeliveryType.NoBall);
        Assert.True(match.TrySetMatchOutcome(CricketMatchResultKind.NoResult, "Officials confirmed", match.WorkflowToken, out _));
        Assert.False(match.FreeHitPending); Assert.True(match.CurrentInnings!.FreeHitPending);
        var draft = match.CreateScorecardDraft(1); draft.TotalRuns = 2; draft.Wides = 1;
        draft.Bowlers[0].Wides = 1; draft.Bowlers[0].RunsConceded = 2;
        Assert.True(match.TryApplyScorecard(draft, out string error), error);
        Assert.True(match.TryRevertLatestScorecardCorrection(1, out error), error);
        Assert.Equal(CricketMatchResultKind.NoResult, match.ResultKind); Assert.Equal("No result", match.MatchResult);
        match = Reload(match); Assert.True(match.TryReopenMatch(match.WorkflowToken, out error), error);
        Assert.True(match.FreeHitPending); Assert.Equal(1, match.TotalRuns);
    }

    [Theory]
    [InlineData(501, (int)CricketMatchResultKind.NoResult)] [InlineData(0, 999)]
    public void InvalidOutcomeInputDoesNotMutateState(int noteLength, int kind)
    {
        var match = Ready(); string before = Snapshot(match), token = match.WorkflowToken; int revision = match.DeliveryRevision;
        Assert.False(match.TrySetMatchOutcome((CricketMatchResultKind)kind, new string('X', noteLength), token, out string error));
        Assert.NotEmpty(error); Assert.Equal(before, Snapshot(match)); Assert.Equal(token, match.WorkflowToken); Assert.Equal(revision, match.DeliveryRevision);
    }

    [Fact]
    public void LegacyFinalisedSaveWithoutACheckpointCannotBeReopenedByGuessing()
    {
        var match = Ready(); var state = match.ToState(); state.IsMatchFinalized = true; state.FinalResult = "Legacy result";
        match.LoadState(state); string before = Snapshot(match);
        Assert.False(match.TryReopenMatch(match.WorkflowToken, out string error));
        Assert.Contains("no reopening checkpoint", error); Assert.Equal(before, Snapshot(match));
    }

    [Theory]
    [InlineData(CricketFormat.LimitedOvers)] [InlineData(CricketFormat.MultiDay)]
    public void EndedUnfinishedMatchesRejectRoleStrikeAndInningsCommandsUntilReopened(CricketFormat format)
    {
        var match = Ready(); match.Format = format;
        Assert.True(match.TrySetMatchOutcome(CricketMatchResultKind.NoResult, "Officials ended play", match.WorkflowToken, out _));
        string before = Snapshot(match), token = match.WorkflowToken; int revision = match.DeliveryRevision, events = 0;
        match.MatchChanged += () => events++;
        match.SetStriker(2); match.SetNonStriker(3); match.RotateStrike(); match.ConfirmBowler(5); match.SetCurrentBowler(6);
        match.SelectNewBatter(4); match.DeclareInnings(); match.EndCurrentInnings(); match.StartNextInnings();
        Assert.Equal(before, Snapshot(match)); Assert.Equal(token, match.WorkflowToken);
        Assert.Equal(revision, match.DeliveryRevision); Assert.Equal(0, events);
    }
}

using System.Text.Json;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class RefinementRulesTests
{
    private static CricketMatchManager Ready() => CricketReliabilityChecks.CreateMatch(ready: true);
    private static string Snapshot(CricketMatchManager match) => JsonSerializer.Serialize(match.ToState());

    [Theory]
    [InlineData(DismissalType.Stumped)]
    [InlineData(DismissalType.HitWicket)]
    public void WideWicketKeepsPenaltyDoesNotCountBallAndSurvivesCorrectionReloadAndUndo(DismissalType dismissal)
    {
        var match = Ready();
        string before = Snapshot(match);
        match.RecordDelivery(1, CricketDeliveryType.Wicket, dismissal: dismissal, wicketRuns: CricketWicketRuns.Wide);
        var innings = match.CurrentInnings!;
        Assert.Single(innings.Deliveries);
        Assert.Equal(1, innings.TotalRuns); Assert.Equal(1, innings.Wides);
        Assert.Equal(1, innings.TotalWickets); Assert.Equal(1, innings.BowlingAttack[0].Wickets);
        Assert.Equal(0, innings.LegalBallsBowled); Assert.Equal(0, innings.BattingOrder[0].BallsFaced);
        Assert.Equal(0, innings.StrikerIndex); Assert.True(innings.NeedsBatterSelection);
        var ball = innings.Deliveries[0];
        Assert.True(match.TryEditDelivery(ball, CricketMatchManager.DescribeDelivery(innings, ball), out string error), error);
        match.LoadState(JsonSerializer.Deserialize<CricketSerializableState>(Snapshot(match))!);
        Assert.True(match.UndoLastDelivery());
        Assert.Equal(before, Snapshot(match));
    }

    [Theory]
    [InlineData(DismissalType.Bowled, CricketWicketRuns.Wide)]
    [InlineData(DismissalType.Caught, CricketWicketRuns.Wide)]
    [InlineData(DismissalType.Stumped, CricketWicketRuns.NoBall)]
    [InlineData(DismissalType.HitWicket, CricketWicketRuns.NoBall)]
    [InlineData(DismissalType.HitBallTwice, CricketWicketRuns.Wide)]
    public void ImpossibleExtraWicketsAreRejectedWithoutChangingState(DismissalType dismissal, CricketWicketRuns source)
    {
        var match = Ready(); string before = Snapshot(match);
        match.RecordDelivery(1, CricketDeliveryType.Wicket, dismissal: dismissal, wicketRuns: source);
        Assert.Equal(before, Snapshot(match));
        Assert.False(string.IsNullOrWhiteSpace(match.LastOperationError));
    }

    [Fact]
    public void ReturningRetiredBatterIsActiveAndUndoRetainsOriginalRetirementCheckpoint()
    {
        var match = Ready();
        match.RecordDelivery(4, CricketDeliveryType.Four);
        Assert.True(match.TryReplaceBatter(true, 2, CricketBatterReplacementReason.RetiredHurt, false, out _));
        match.RecordDelivery(0, CricketDeliveryType.Wicket, dismissal: DismissalType.Bowled);
        match.SelectNewBatter(0);
        Assert.False(match.CurrentInnings!.Striker!.IsRetired);
        Assert.Null(match.CurrentInnings.Striker.RetiredReason);
        Assert.Equal(DismissalType.NotOut, match.CurrentInnings.Striker.HowOut);
        Assert.Equal(4, match.CurrentInnings.Striker.Runs);
        match.LoadState(JsonSerializer.Deserialize<CricketSerializableState>(Snapshot(match))!);
        Assert.False(match.CurrentInnings!.Striker!.IsRetired);
        Assert.True(match.UndoLastDelivery());
        Assert.True(match.CurrentInnings.BattingOrder[0].IsRetired);
        Assert.Equal(2, match.CurrentInnings.StrikerIndex);
    }

    [Fact]
    public void FinalisedMatchesRequireExplicitScorecardCorrectionRatherThanDeliveryReplay()
    {
        var match = Ready(); match.RecordDelivery(4, CricketDeliveryType.Four);
        var ball = match.CurrentInnings!.Deliveries[0];
        Assert.True(match.TryFinishMatch(out _)); string before = Snapshot(match);
        Assert.False(match.TryEditDelivery(ball, CricketMatchManager.DescribeDelivery(match.CurrentInnings, ball)
            with { Type = CricketDeliveryType.Six, Runs = 6 }, out string error));
        Assert.Contains("scorecard", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void ExhaustedTrainingActionsDoNotSkipTimersAddedLater()
    {
        var training = new TrainingSession();
        training.AddTimer(new TrainingTimer("First", TimeSpan.FromSeconds(5)));
        training.Skip(); training.Skip(); training.AdvanceAfterFinish(true);
        var next = new TrainingTimer("Added later", TimeSpan.FromSeconds(10));
        training.AddTimer(next);
        Assert.Same(next, training.Active);
        Assert.False(next.HasFinished);
        training.Start(); Assert.True(training.IsRunning); training.Pause();
    }

    [Fact]
    public void FinishCallbackCannotConsumeAnUnfinishedReplacementTimer()
    {
        var training = new TrainingSession();
        var next = new TrainingTimer("Replacement", TimeSpan.FromSeconds(10)); training.AddTimer(next);
        training.AdvanceAfterFinish(true);
        Assert.Same(next, training.Active); Assert.False(next.HasFinished); Assert.False(training.IsRunning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WideWicketOnTheSixthLegalBallRespectsTheCompetitionCountingSetting(bool replaceWides)
    {
        var match = Ready(); match.ReplaceWideBalls = replaceWides;
        for (int ball = 0; ball < 5; ball++) match.RecordDelivery(0, CricketDeliveryType.Dot);
        match.RecordDelivery(1, CricketDeliveryType.Wicket, dismissal: DismissalType.Stumped, wicketRuns: CricketWicketRuns.Wide);
        Assert.Equal(replaceWides ? 5 : 6, match.CurrentInnings!.LegalBallsBowled);
        Assert.Equal(replaceWides ? 0 : 1, match.CurrentInnings.StrikerIndex);
        Assert.Equal(replaceWides ? 1 : 0, match.CurrentInnings.NonStrikerIndex);
        Assert.Equal(replaceWides ? 5 : 6, match.CurrentInnings.BowlingAttack[0].BallsBowled);
        Assert.Equal(5, match.CurrentInnings.BattingOrder[0].BallsFaced);
        Assert.True(match.UndoLastDelivery()); Assert.Equal(5, match.CurrentInnings.LegalBallsBowled);
    }

    [Theory]
    [InlineData(CricketNoBallRuns.Bat)]
    [InlineData(CricketNoBallRuns.Byes)]
    [InlineData(CricketNoBallRuns.LegByes)]
    public void NoBallRunOutCombinationsReconcileAndRetainCrossingThroughReload(CricketNoBallRuns source)
    {
        var match = Ready(); string before = Snapshot(match);
        match.RecordDelivery(3, CricketDeliveryType.Wicket, noBallRuns: source, dismissal: DismissalType.RunOut,
            dismissedBatterIndex: 1, wicketRuns: CricketWicketRuns.NoBall, runOutCrossed: true);
        var innings = match.CurrentInnings!;
        Assert.Equal(3, innings.TotalRuns); Assert.Equal(1, innings.NoBalls); Assert.Equal(0, innings.LegalBallsBowled);
        Assert.Equal(source == CricketNoBallRuns.Bat ? 2 : 0, innings.BattingOrder[0].Runs);
        Assert.Equal(source == CricketNoBallRuns.Bat ? 3 : 1, innings.BowlingAttack[0].RunsConceded);
        Assert.Equal(0, innings.BowlingAttack[0].Wickets);
        Assert.Equal(innings.TotalRuns, innings.TotalExtras + innings.BattingOrder.Sum(player => player.Runs));
        match.LoadState(JsonSerializer.Deserialize<CricketSerializableState>(Snapshot(match))!);
        Assert.True(match.CurrentInnings!.Deliveries[0].RunOutCrossed);
        Assert.True(match.UndoLastDelivery()); Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void WinningWideEndsTheChaseBeforeAStumping()
    {
        var match = Ready(); match.EndCurrentInnings(); match.StartNextInnings();
        match.SetStriker(0); match.SetNonStriker(1); match.ConfirmBowler(0);
        match.RecordDelivery(1, CricketDeliveryType.Wicket, dismissal: DismissalType.Stumped, wicketRuns: CricketWicketRuns.Wide);
        Assert.Equal(1, match.TotalRuns); Assert.Equal(0, match.TotalWickets);
        Assert.Equal(CricketDeliveryType.Wide, match.CurrentInnings!.Deliveries[0].Type);
        Assert.True(match.CurrentInnings.IsComplete); Assert.Contains("10 wickets", match.MatchResult);
    }

    [Fact]
    public void EqualTotalsWithFinalInningsAllOutAreATieInAMultiInningsMatch()
    {
        var match = Ready(); match.Format = CricketFormat.MultiDay; match.TotalInnings = 4;
        for (int number = 1; number <= 4; number++)
        {
            if (number > 1)
            { match.StartNextInnings(); match.SetStriker(0); match.SetNonStriker(1); match.ConfirmBowler(0); }
            if (number <= 2) match.RecordDelivery(4, CricketDeliveryType.Four);
            for (int wicket = 0; wicket < 10; wicket++)
            {
                match.RecordDelivery(0, CricketDeliveryType.Wicket);
                if (wicket < 9) match.SelectNewBatter(wicket + 2);
                if (match.CurrentInnings!.NeedsBowlerSelection) match.ConfirmBowler(match.SuggestedNextBowler());
            }
            Assert.True(match.CurrentInnings!.IsAllOut);
        }
        Assert.Equal("Match tied", match.MatchResult);
        match.LoadState(JsonSerializer.Deserialize<CricketSerializableState>(Snapshot(match))!);
        Assert.Equal("Match tied", match.MatchResult);
    }

    [Fact]
    public void AflPostSirenScoreRestoreUndoAndContinueKeepTheEndedQuarter()
    {
        var match = new MatchManager(); match.SetElapsed(TimeSpan.FromMinutes(20)); match.AddGoal(TeamSide.Home);
        Assert.True(match.EndQuarter()); match.AddGoal(TeamSide.Away);
        var restored = new MatchManager(); restored.LoadState(JsonSerializer.Deserialize<SerializableState>(JsonSerializer.Serialize(match.ToState()))!);
        Assert.False(restored.EndQuarter()); Assert.True(restored.UndoLastScore());
        Assert.Equal(0, restored.GetQuarterSnapshot(1)!.AwayTotal); Assert.Equal(6, restored.GetQuarterSnapshot(1)!.HomeTotal);
        Assert.True(restored.ContinueQuarter()); Assert.Equal(1, restored.Quarter);
        Assert.Equal(TimeSpan.FromMinutes(20), restored.ElapsedInQuarter);
    }

    [Fact]
    public async Task ProductionRecoveryWriterKeepsLatestStateAndRejectsStaleWritesAfterReset()
    {
        string path = Path.Combine(Path.GetTempPath(), "roche-refinement-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            var checks = await CricketPersistenceChecks.RunAsync(path);
            Assert.NotEmpty(checks); Assert.All(checks, check => Assert.True(check.Value, check.Key));
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Fact]
    public void MixedExtrasWicketsAndCrossingPreserveTotalsAcrossSixHundredUndoAndReloadOperations()
    {
        var random = new Random(427); var match = Ready();
        for (int operation = 0; operation < 600; operation++)
        {
            if (match.CurrentInnings!.IsComplete) match = Ready();
            var innings = match.CurrentInnings!;
            if (innings.NeedsBatterSelection)
                match.SelectNewBatter(innings.BattingOrder.FindIndex(player => !player.IsOut
                    && !ReferenceEquals(player, innings.Striker) && !ReferenceEquals(player, innings.NonStriker)));
            if (innings.NeedsBowlerSelection) match.ConfirmBowler(match.SuggestedNextBowler());
            string before = Snapshot(match);
            var type = (CricketDeliveryType)random.Next(9);
            int runs = random.Next(4);
            var origin = (CricketNoBallRuns)random.Next(3);
            if (type == CricketDeliveryType.Wicket)
            {
                var source = (CricketWicketRuns)random.Next(5);
                var dismissal = source == CricketWicketRuns.Wide && random.Next(2) == 0 ? DismissalType.Stumped : DismissalType.RunOut;
                match.RecordDelivery(runs, type, noBallRuns: origin, dismissal: dismissal,
                    dismissedBatterIndex: dismissal == DismissalType.Stumped || random.Next(2) == 0 ? innings.StrikerIndex : innings.NonStrikerIndex,
                    wicketRuns: source, runOutCrossed: random.Next(2) == 0);
            }
            else match.RecordDelivery(runs, type, noBallRuns: origin);
            Assert.Equal(innings.TotalRuns, innings.BattingOrder.Sum(player => player.Runs) + innings.TotalExtras);
            Assert.Equal(innings.TotalExtras, innings.Wides + innings.NoBalls + innings.Byes + innings.LegByes);
            Assert.Equal(innings.LegalBallsBowled, innings.BowlingAttack.Sum(player => player.BallsBowled));
            Assert.Equal(innings.TotalWickets, innings.BattingOrder.Count(player => player.IsOut));
            if (random.Next(3) == 0)
            { Assert.True(match.UndoLastDelivery()); Assert.Equal(before, Snapshot(match)); }
            else if (random.Next(3) == 0)
            {
                string saved = Snapshot(match); match.LoadState(JsonSerializer.Deserialize<CricketSerializableState>(saved)!);
                Assert.Equal(saved, Snapshot(match));
            }
        }
    }

    [Theory]
    [InlineData(CricketWicketRuns.Bat, 1, true)]
    [InlineData(CricketWicketRuns.Wide, 2, true)]
    [InlineData(CricketWicketRuns.NoBall, 3, false)]
    [InlineData(CricketWicketRuns.Byes, 2, true)]
    [InlineData(CricketWicketRuns.LegByes, 1, false)]
    public void UmpireConfirmedObstructionRunsAndCrossingAreReversible(CricketWicketRuns source, int runs, bool crossed)
    {
        var match = Ready(); string before = Snapshot(match);
        match.RecordDelivery(runs, CricketDeliveryType.Wicket, dismissal: DismissalType.ObstructingField,
            dismissedBatterIndex: 1, wicketRuns: source, runOutCrossed: crossed);
        var innings = match.CurrentInnings!;
        Assert.Equal(runs, match.TotalRuns); Assert.Equal(1, match.TotalWickets);
        Assert.Equal(0, innings.BowlingAttack[0].Wickets);
        Assert.True(innings.Deliveries[0].CompletedRunsOnly);
        int completed = runs - (source is CricketWicketRuns.Wide or CricketWicketRuns.NoBall ? 1 : 0);
        Assert.Equal((completed % 2 == 1) ^ crossed ? 1 : 0, innings.StrikerIndex);
        var delivery = innings.Deliveries[0];
        Assert.True(match.TryEditDelivery(delivery, CricketMatchManager.DescribeDelivery(innings, delivery), out string error), error);
        match.LoadState(JsonSerializer.Deserialize<CricketSerializableState>(Snapshot(match))!);
        Assert.True(match.UndoLastDelivery()); Assert.Equal(before, Snapshot(match));
    }
}

using System.Text.Json;
using Roche_Scoreboard.Models;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class AflCommandLifetimeTests
{
    [Fact]
    public void OrdinaryScoresUndoClockAndTeamEditsDoNotRetireQueuedCommands()
    {
        var match = new MatchManager(); long lifetime = match.LifecycleVersion;
        match.SetTeams("Home", "HOM", "Away", "AWY");
        match.AddGoal(TeamSide.Home); match.AddBehind(TeamSide.Away);
        Assert.True(match.UndoLastScore());
        match.SetElapsed(TimeSpan.FromSeconds(12)); match.StartClock(); match.Tick(); match.PauseClock();
        Assert.Equal(lifetime, match.LifecycleVersion);
        Assert.Equal(6, match.HomeTotal); Assert.Equal(0, match.AwayTotal); Assert.Single(match.Events);
    }

    [Fact]
    public void ExplicitRetirementChangesOnlyInMemoryAuthorityWithoutScoreHistoryOrNotifications()
    {
        var match = new MatchManager(); match.AddGoal(TeamSide.Home); match.SetElapsed(TimeSpan.FromSeconds(71));
        string before = JsonSerializer.Serialize(match.ToState()); long lifetime = match.LifecycleVersion; int changes = 0;
        match.MatchChanged += () => changes++;
        match.RetireLifecycle();
        Assert.NotEqual(lifetime, match.LifecycleVersion);
        Assert.Equal(before, JsonSerializer.Serialize(match.ToState())); Assert.Equal(0, changes);
        Assert.DoesNotContain("Lifecycle", before, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResetAndEvenIdenticalRestoreRetireOldAuthorityWhileInvalidRestoreDoesNot()
    {
        var match = new MatchManager(); match.AddGoal(TeamSide.Home);
        var state = match.ToState(); long beforeLoad = match.LifecycleVersion;
        match.LoadState(state); Assert.NotEqual(beforeLoad, match.LifecycleVersion); Assert.Equal(6, match.HomeTotal);
        long afterLoad = match.LifecycleVersion; string beforeInvalid = JsonSerializer.Serialize(match.ToState());
        Assert.Throws<ArgumentNullException>(() => match.LoadState(null!));
        Assert.Equal(afterLoad, match.LifecycleVersion); Assert.Equal(beforeInvalid, JsonSerializer.Serialize(match.ToState()));
        match.ResetForNewGame();
        Assert.NotEqual(afterLoad, match.LifecycleVersion); Assert.Equal(0, match.HomeTotal); Assert.Empty(match.Events);
        Assert.Equal(1, match.Quarter); Assert.Equal(TimeSpan.Zero, match.ElapsedInQuarter); Assert.False(match.ClockRunning);
    }
}

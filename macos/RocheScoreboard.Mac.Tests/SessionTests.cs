using System.Text.Json;
using Roche_Scoreboard.Models;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class SessionTests
{
    [Fact]
    public void PostSirenScoreAndUndoRebuildTheEndedQuarter()
    {
        var session = new DemoSession(); var match = session.Afl;
        match.SetElapsed(TimeSpan.FromMinutes(20)); match.AddGoal(TeamSide.Home);
        Assert.True(match.EndQuarter());
        Assert.False(match.EndQuarter());
        match.AddBehind(TeamSide.Away);
        Assert.Equal(1, match.Events[^1].Quarter);
        Assert.Equal(TimeSpan.FromMinutes(20), match.Events[^1].GameTime);
        Assert.Equal(1, match.GetQuarterSnapshot(1)!.AwayTotal);
        Assert.True(match.UndoLastScore());
        Assert.Equal(0, match.GetQuarterSnapshot(1)!.AwayTotal);
        Assert.True(match.ContinueQuarter());
        Assert.Equal(1, match.Quarter);
        Assert.Equal(TimeSpan.FromMinutes(20), match.ElapsedInQuarter);
    }

    [Fact]
    public void CorrectionsReorderHistoryAndRecalculateSnapshots()
    {
        var match = new DemoSession().Afl;
        match.SetElapsed(TimeSpan.FromMinutes(5)); match.AddGoal(TeamSide.Home);
        match.SetElapsed(TimeSpan.FromMinutes(10)); match.AddBehind(TeamSide.Away);
        match.EndQuarter();
        match.ModifyEvent(1, TeamSide.Away, ScoreType.Goal, TimeSpan.FromMinutes(2), 1);
        Assert.Equal(TeamSide.Away, match.Events[0].Team);
        Assert.Equal(6, match.GetQuarterSnapshot(1)!.AwayTotal);
        Assert.Equal(6, match.GetQuarterSnapshot(1)!.HomeTotal);
        Assert.Empty(match.ValidateScoreSequence());
    }

    [Fact]
    public void SwitchingSportsPausesTheInactiveClock()
    {
        var session = new DemoSession();
        session.Afl.StartClock(); session.SwitchSport(DemoSport.Training);
        Assert.False(session.Afl.ClockRunning);
        session.Training.AddTimer(new TrainingTimer("Test", TimeSpan.FromMinutes(2)));
        session.Training.Start(); session.SwitchSport(DemoSport.Cricket);
        Assert.False(session.Training.IsRunning);
    }

    [Fact]
    public void CountdownExpiryOnlyFiresOnce()
    {
        var match = new DemoSession().Afl; int events = 0;
        match.QuarterTimeExpired += () => events++;
        match.SetClockMode(ClockMode.Countdown);
        match.SetQuarterDuration(TimeSpan.FromSeconds(1));
        match.SetElapsed(TimeSpan.FromSeconds(2)); match.StartClock();
        match.Tick(); match.Tick();
        Assert.Equal(1, events); Assert.False(match.ClockRunning);
        Assert.Equal(TimeSpan.Zero, match.DisplayClock);
    }

    internal static CricketMatchManager ReadyCricket()
    {
        var match = new DemoSession().Cricket;
        match.SetNonStriker(1); match.SelectNewBatter(0); match.ConfirmBowler(0);
        return match;
    }

    [Fact]
    public void ExtrasAndUndoPreserveLegalBallsStrikeAndBatterStats()
    {
        var match = ReadyCricket(); var inn = match.CurrentInnings!;
        match.RecordDelivery(1, CricketDeliveryType.Wide);
        match.RecordDelivery(5, CricketDeliveryType.NoBall);
        Assert.Equal(6, match.TotalRuns); Assert.Equal(0, inn.LegalBallsBowled);
        Assert.Equal(4, inn.Striker!.Runs); Assert.Equal(1, inn.Striker.BallsFaced);
        Assert.Equal(2, inn.TotalExtras);
        match.UndoLastDelivery();
        Assert.Equal(1, match.TotalRuns); Assert.Equal(0, inn.Striker!.Runs);
        Assert.Equal(0, inn.Striker.BallsFaced); Assert.Equal(1, inn.TotalExtras);
    }

    [Fact]
    public void WicketBlocksScoringUntilIncomingBatterIsSelected()
    {
        var match = ReadyCricket();
        match.RecordDelivery(0, CricketDeliveryType.Wicket, "bowled");
        Assert.True(match.CurrentInnings!.NeedsBatterSelection);
        match.RecordDelivery(4, CricketDeliveryType.Four);
        Assert.Equal(0, match.TotalRuns);
        match.SelectNewBatter(2); match.RecordDelivery(4, CricketDeliveryType.Four);
        Assert.Equal(4, match.TotalRuns);
    }

    [Fact]
    public void BowlerSelectionAndChaseCompletionUseTheSharedEngine()
    {
        var match = ReadyCricket();
        for (int i = 0; i < 6; i++) match.RecordDelivery(0, CricketDeliveryType.Dot);
        Assert.True(match.CurrentInnings!.NeedsBowlerSelection);
        match.ConfirmBowler(0); Assert.True(match.CurrentInnings.NeedsBowlerSelection);
        match.ConfirmBowler(1); Assert.False(match.CurrentInnings.NeedsBowlerSelection);
        match.EndCurrentInnings(); match.StartNextInnings();
        Assert.Equal(1, match.Target);
        match.SetNonStriker(1); match.SelectNewBatter(0); match.ConfirmBowler(0);
        match.RecordDelivery(1, CricketDeliveryType.Runs);
        Assert.True(match.CurrentInnings!.IsComplete); Assert.NotNull(match.MatchResult);
        match.UndoLastDelivery(); Assert.False(match.CurrentInnings.IsComplete); Assert.Null(match.MatchResult);
    }

    [Fact]
    public void FullSessionRoundTripRestoresEverySportAndPausesClocks()
    {
        var original = new DemoSession();
        original.Afl.SetTeams("Home Test", "HT", "Away Test", "AT");
        original.Afl.AddGoal(TeamSide.Home); original.Afl.SetElapsed(TimeSpan.FromMinutes(7));
        original.Afl.StartClock();
        original.Cricket.SetNonStriker(1); original.Cricket.SelectNewBatter(0); original.Cricket.ConfirmBowler(0);
        original.Cricket.RecordDelivery(6, CricketDeliveryType.Six);
        original.Training.AddTimer(new TrainingTimer("Warmup", TimeSpan.FromSeconds(60)));
        original.Training.AddTimer(new TrainingTimer("Run", TimeSpan.FromSeconds(90)));
        original.Training.Skip(); original.Training.Start();
        original.Messages.Add("Welcome"); original.HomeColor = "#112233"; original.Stacked = true;
        var restored = new DemoSession();
        SessionStore.Restore(restored, SessionStore.Serialize(original));
        Assert.Equal(6, restored.Afl.HomeTotal); Assert.Equal("Home Test", restored.Afl.HomeName);
        Assert.True(restored.Afl.ElapsedInQuarter >= TimeSpan.FromMinutes(7)); Assert.False(restored.Afl.ClockRunning);
        Assert.Equal(6, restored.Cricket.TotalRuns); Assert.True(restored.Cricket.UndoLastDelivery());
        Assert.Equal(0, restored.Cricket.TotalRuns);
        Assert.Equal("Run", restored.Training.Active!.Title); Assert.False(restored.Training.IsRunning);
        Assert.Equal("Welcome", Assert.Single(restored.Messages)); Assert.True(restored.Stacked); Assert.Equal("#112233", restored.HomeColor);
        original.Afl.PauseClock(); original.Training.Pause();
    }

    [Fact]
    public void CompletionHoldDoesNotStrandATimerAfterRestore()
    {
        var session = new DemoSession();
        session.Training.AddTimer(new TrainingTimer("Finished", TimeSpan.FromSeconds(5)) { HasFinished = true, Remaining = TimeSpan.Zero });
        session.Training.AddTimer(new TrainingTimer("Next", TimeSpan.FromSeconds(10)));
        var restored = new DemoSession(); SessionStore.Restore(restored, SessionStore.Serialize(session));
        Assert.Equal("Next", restored.Training.Active!.Title);
    }

    [Fact]
    public void InvalidVersionDoesNotReplaceTheLiveSession()
    {
        var session = new DemoSession(); session.Afl.AddGoal(TeamSide.Home);
        var snapshot = SessionStore.Capture(new DemoSession()); snapshot.Version = 99;
        Assert.Throws<JsonException>(() => SessionStore.Restore(session, JsonSerializer.Serialize(snapshot)));
        Assert.Equal(6, session.Afl.HomeTotal);
    }

    [Fact]
    public void AutosaveCanRecoverFromACorruptPrimaryFile()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SessionStore(System.IO.Path.Combine(dir, "session.json")); var session = new DemoSession();
            session.Afl.AddGoal(TeamSide.Home); store.Save(session);
            session.Afl.AddBehind(TeamSide.Home); store.Save(session);
            File.WriteAllText(store.Path, "broken JSON");
            var restored = new DemoSession();
            Assert.Contains("backup", store.TryRestore(restored)); Assert.Equal(6, restored.Afl.HomeTotal);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void TrainingEditsAndReorderingKeepTheActiveTimer()
    {
        var session = new DemoSession().Training;
        var first = new TrainingTimer("First", TimeSpan.FromSeconds(60)); var next = new TrainingTimer("Next", TimeSpan.FromSeconds(30));
        session.AddTimer(first); session.AddTimer(next); session.MoveTimer(first, 1);
        Assert.Same(first, session.Active);
        session.UpdateTimer(first, "Renamed", TimeSpan.FromSeconds(90)); session.AddTimeToActive(TimeSpan.FromSeconds(30));
        Assert.Equal("Renamed", session.Active!.Title); Assert.Equal(TimeSpan.FromSeconds(120), session.ActiveRemaining);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TrainingExpiryAdvancesImmediatelyEvenWithoutAnOutputWindow(bool autoAdvance)
    {
        var session = new DemoSession(); session.SwitchSport(DemoSport.Training);
        session.Training.AutoAdvance = autoAdvance;
        session.Training.AddTimer(new TrainingTimer("Done", TimeSpan.Zero));
        session.Training.AddTimer(new TrainingTimer("Next", TimeSpan.FromSeconds(30)));
        session.Training.Start(); session.Tick();
        Assert.Equal("Next", session.Training.Active!.Title);
        Assert.Equal(autoAdvance, session.Training.IsRunning);
        Assert.Equal("Done · COMPLETE", session.Celebration);
        session.Training.Pause();
    }
}

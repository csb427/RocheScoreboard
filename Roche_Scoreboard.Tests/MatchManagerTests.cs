using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Tests;

public class MatchManagerTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Creates a manager with two named teams already set.</summary>
    private static MatchManager CreateWithTeams(
        string homeName = "Hawks", string homeAbbr = "HAW",
        string awayName = "Blues", string awayAbbr = "CAR")
    {
        var m = new MatchManager();
        m.SetTeams(homeName, homeAbbr, awayName, awayAbbr);
        return m;
    }

    // ── SetTeams ─────────────────────────────────────────────────────────────

    [Fact]
    public void SetTeams_StoresValidNames()
    {
        var m = new MatchManager();
        m.SetTeams("Richmond", "RIC", "Collingwood", "COL");

        Assert.Equal("Richmond", m.HomeName);
        Assert.Equal("RIC", m.HomeAbbr);
        Assert.Equal("Collingwood", m.AwayName);
        Assert.Equal("COL", m.AwayAbbr);
    }

    [Fact]
    public void SetTeams_EmptyHome_FallsBackToDefaults()
    {
        var m = new MatchManager();
        m.SetTeams("", "", "Blues", "CAR");

        Assert.Equal("HOME", m.HomeName);
        Assert.Equal("HOM", m.HomeAbbr);
    }

    [Fact]
    public void SetTeams_WhitespaceAway_FallsBackToDefaults()
    {
        var m = new MatchManager();
        m.SetTeams("Hawks", "HAW", "   ", "  ");

        Assert.Equal("AWAY", m.AwayName);
        Assert.Equal("AWY", m.AwayAbbr);
    }

    [Fact]
    public void SetTeams_RaisesMatchChanged()
    {
        var m = new MatchManager();
        int count = 0;
        m.MatchChanged += () => count++;
        m.SetTeams("A", "A", "B", "B");
        Assert.Equal(1, count);
    }

    // ── Default state ────────────────────────────────────────────────────────

    [Fact]
    public void DefaultState_ScoresAreZero()
    {
        var m = new MatchManager();
        Assert.Equal(0, m.HomeGoals);
        Assert.Equal(0, m.HomeBehinds);
        Assert.Equal(0, m.AwayGoals);
        Assert.Equal(0, m.AwayBehinds);
    }

    [Fact]
    public void DefaultState_QuarterIsOne()
    {
        var m = new MatchManager();
        Assert.Equal(1, m.Quarter);
    }

    [Fact]
    public void DefaultState_ClockNotRunning()
    {
        var m = new MatchManager();
        Assert.False(m.ClockRunning);
    }

    // ── Score totals ─────────────────────────────────────────────────────────

    [Fact]
    public void HomeTotal_IsGoalsTimes6PlusBehinds()
    {
        var m = CreateWithTeams();
        m.AddGoal(TeamSide.Home);
        m.AddGoal(TeamSide.Home);
        m.AddBehind(TeamSide.Home);
        // 2 goals × 6 + 1 behind = 13
        Assert.Equal(13, m.HomeTotal);
    }

    [Fact]
    public void AwayTotal_IsGoalsTimes6PlusBehinds()
    {
        var m = CreateWithTeams();
        m.AddGoal(TeamSide.Away);
        m.AddBehind(TeamSide.Away);
        m.AddBehind(TeamSide.Away);
        // 1 goal × 6 + 2 behinds = 8
        Assert.Equal(8, m.AwayTotal);
    }

    [Fact]
    public void Margin_PositiveWhenHomeLeads()
    {
        var m = CreateWithTeams();
        m.AddGoal(TeamSide.Home);    // Home: 6
        m.AddBehind(TeamSide.Away);  // Away: 1
        Assert.Equal(5, m.Margin);
    }

    [Fact]
    public void Margin_NegativeWhenAwayLeads()
    {
        var m = CreateWithTeams();
        m.AddGoal(TeamSide.Away);   // Away: 6
        m.AddBehind(TeamSide.Home); // Home: 1
        Assert.Equal(-5, m.Margin);
    }

    [Fact]
    public void Margin_ZeroWhenScoresEqual()
    {
        var m = CreateWithTeams();
        m.AddGoal(TeamSide.Home);
        m.AddGoal(TeamSide.Away);
        Assert.Equal(0, m.Margin);
    }

    // ── AddGoal / AddBehind ─────────────────────────────────────────────────

    [Fact]
    public void AddGoal_Home_IncrementsHomeGoals()
    {
        var m = CreateWithTeams();
        m.AddGoal(TeamSide.Home);
        Assert.Equal(1, m.HomeGoals);
        Assert.Equal(0, m.AwayGoals);
    }

    [Fact]
    public void AddGoal_Away_IncrementsAwayGoals()
    {
        var m = CreateWithTeams();
        m.AddGoal(TeamSide.Away);
        Assert.Equal(1, m.AwayGoals);
        Assert.Equal(0, m.HomeGoals);
    }

    [Fact]
    public void AddBehind_Home_IncrementsHomeBehinds()
    {
        var m = CreateWithTeams();
        m.AddBehind(TeamSide.Home);
        Assert.Equal(1, m.HomeBehinds);
    }

    [Fact]
    public void AddBehind_Away_IncrementsAwayBehinds()
    {
        var m = CreateWithTeams();
        m.AddBehind(TeamSide.Away);
        Assert.Equal(1, m.AwayBehinds);
    }

    [Fact]
    public void AddGoal_AddsEventToLog()
    {
        var m = CreateWithTeams();
        m.AddGoal(TeamSide.Home);
        Assert.Single(m.Events);
        Assert.Equal(ScoreType.Goal, m.Events[0].Type);
        Assert.Equal(TeamSide.Home, m.Events[0].Team);
    }

    [Fact]
    public void AddBehind_AddsEventToLog()
    {
        var m = CreateWithTeams();
        m.AddBehind(TeamSide.Away);
        Assert.Single(m.Events);
        Assert.Equal(ScoreType.Behind, m.Events[0].Type);
        Assert.Equal(TeamSide.Away, m.Events[0].Team);
    }

    [Fact]
    public void AddGoal_RaisesScoreEventAdded()
    {
        var m = CreateWithTeams();
        ScoreEvent? captured = null;
        m.ScoreEventAdded += ev => captured = ev;
        m.AddGoal(TeamSide.Home);
        Assert.NotNull(captured);
        Assert.Equal(ScoreType.Goal, captured!.Type);
    }

    [Fact]
    public void AddGoal_AfterQ4Ended_DoesNotScore()
    {
        var m = CreateWithTeams();
        // End all 4 quarters to finalise the match
        for (int i = 0; i < 4; i++) m.EndQuarter();

        m.AddGoal(TeamSide.Home);
        // Score must remain 0
        Assert.Equal(0, m.HomeGoals);
    }

    [Fact]
    public void AddBehind_AfterQ4Ended_DoesNotScore()
    {
        var m = CreateWithTeams();
        for (int i = 0; i < 4; i++) m.EndQuarter();

        m.AddBehind(TeamSide.Away);
        Assert.Equal(0, m.AwayBehinds);
    }

    // ── UndoLastScore ────────────────────────────────────────────────────────

    [Fact]
    public void UndoLastScore_RemovesLastEvent()
    {
        var m = CreateWithTeams();
        m.AddGoal(TeamSide.Home);
        m.AddBehind(TeamSide.Away);
        bool result = m.UndoLastScore();

        Assert.True(result);
        Assert.Single(m.Events);
        Assert.Equal(ScoreType.Goal, m.Events[0].Type);
    }

    [Fact]
    public void UndoLastScore_RebuildsScore()
    {
        var m = CreateWithTeams();
        m.AddGoal(TeamSide.Home);
        m.AddGoal(TeamSide.Home);
        m.UndoLastScore(); // remove second goal

        Assert.Equal(1, m.HomeGoals);
        Assert.Equal(0, m.HomeBehinds);
    }

    [Fact]
    public void UndoLastScore_ReturnsFalseWhenNoEvents()
    {
        var m = CreateWithTeams();
        bool result = m.UndoLastScore();
        Assert.False(result);
    }

    [Fact]
    public void UndoLastScore_RaisesMatchChanged()
    {
        var m = CreateWithTeams();
        m.AddGoal(TeamSide.Home);
        int count = 0;
        m.MatchChanged += () => count++;
        m.UndoLastScore();
        Assert.Equal(1, count);
    }

    // ── EndQuarter ──────────────────────────────────────────────────────────

    [Fact]
    public void EndQuarter_AdvancesQuarter()
    {
        var m = CreateWithTeams();
        m.EndQuarter();
        Assert.Equal(2, m.Quarter);
    }

    [Fact]
    public void EndQuarter_StopsAtQ4()
    {
        var m = CreateWithTeams();
        for (int i = 0; i < 4; i++) m.EndQuarter();
        Assert.Equal(4, m.Quarter);
    }

    [Fact]
    public void EndQuarter_ReturnsFalse_WhenCalledTwiceOnSameQuarter()
    {
        var m = CreateWithTeams();
        m.EndQuarter(); // Q1 → Q2
        // Q1 snapshot now exists; calling again while on Q2 should succeed
        bool secondResult = m.EndQuarter(); // Q2 → Q3
        Assert.True(secondResult);

        // Now call EndQuarter for Q4
        m.EndQuarter(); // Q3 → Q4 (still at 4)
        m.EndQuarter(); // Ends Q4

        // Calling EndQuarter again after Q4 snapshot exists should return false
        bool result = m.EndQuarter();
        Assert.False(result);
    }

    [Fact]
    public void EndQuarter_SavesSnapshot()
    {
        var m = CreateWithTeams();
        m.AddGoal(TeamSide.Home);     // Home: 1G = 6pts
        m.AddBehind(TeamSide.Away);   // Away: 1B = 1pt
        m.EndQuarter();

        var snap = m.GetQuarterSnapshot(1);
        Assert.NotNull(snap);
        Assert.Equal(1, snap!.HomeGoals);
        Assert.Equal(1, snap.AwayBehinds);
        Assert.Equal(6, snap.HomeTotal);
        Assert.Equal(1, snap.AwayTotal);
    }

    [Fact]
    public void GetQuarterSnapshot_ReturnsNullForInvalidQuarter()
    {
        var m = CreateWithTeams();
        Assert.Null(m.GetQuarterSnapshot(0));
        Assert.Null(m.GetQuarterSnapshot(5));
    }

    [Fact]
    public void GetQuarterSnapshot_ReturnsNullBeforeQuarterEnds()
    {
        var m = CreateWithTeams();
        Assert.Null(m.GetQuarterSnapshot(1));
    }

    // ── Clock ────────────────────────────────────────────────────────────────

    [Fact]
    public void StartClock_SetsClockRunningTrue()
    {
        var m = CreateWithTeams();
        m.StartClock();
        Assert.True(m.ClockRunning);
    }

    [Fact]
    public void PauseClock_SetsClockRunningFalse()
    {
        var m = CreateWithTeams();
        m.StartClock();
        m.PauseClock();
        Assert.False(m.ClockRunning);
    }

    [Fact]
    public void StartClock_NoOp_WhenAlreadyRunning()
    {
        var m = CreateWithTeams();
        m.StartClock();
        int count = 0;
        m.MatchChanged += () => count++;
        m.StartClock(); // should be no-op
        Assert.Equal(0, count);
        Assert.True(m.ClockRunning);
    }

    [Fact]
    public void PauseClock_NoOp_WhenAlreadyStopped()
    {
        var m = CreateWithTeams();
        int count = 0;
        m.MatchChanged += () => count++;
        m.PauseClock(); // already stopped
        Assert.Equal(0, count);
    }

    [Fact]
    public void SetClockMode_UpdatesMode()
    {
        var m = new MatchManager();
        m.SetClockMode(ClockMode.Countdown);
        Assert.Equal(ClockMode.Countdown, m.ClockMode);
    }

    [Fact]
    public void SetQuarterDuration_UpdatesDuration()
    {
        var m = new MatchManager();
        m.SetQuarterDuration(TimeSpan.FromMinutes(25));
        Assert.Equal(TimeSpan.FromMinutes(25), m.QuarterDuration);
    }

    [Fact]
    public void SetQuarterDuration_Ignores_ZeroOrNegative()
    {
        var m = new MatchManager();
        var original = m.QuarterDuration;
        m.SetQuarterDuration(TimeSpan.Zero);
        Assert.Equal(original, m.QuarterDuration);
        m.SetQuarterDuration(TimeSpan.FromMinutes(-5));
        Assert.Equal(original, m.QuarterDuration);
    }

    [Fact]
    public void DisplayClock_InCountUpMode_ReturnsElapsed()
    {
        var m = new MatchManager();
        m.SetClockMode(ClockMode.CountUp);
        // Without running the clock, elapsed should be zero
        Assert.Equal(TimeSpan.Zero, m.DisplayClock);
    }

    [Fact]
    public void DisplayClock_InCountdownMode_ReturnsTimeRemaining()
    {
        var m = new MatchManager();
        m.SetClockMode(ClockMode.Countdown);
        m.SetQuarterDuration(TimeSpan.FromMinutes(20));
        // Clock hasn't started, so remaining = full duration
        Assert.Equal(TimeSpan.FromMinutes(20), m.DisplayClock);
    }

    [Fact]
    public void StartClock_BlockedAfterQ4Ends()
    {
        var m = CreateWithTeams();
        for (int i = 0; i < 4; i++) m.EndQuarter();
        m.StartClock();
        Assert.False(m.ClockRunning);
    }

    // ── ResetForNewGame ──────────────────────────────────────────────────────

    [Fact]
    public void ResetForNewGame_ClearsScoresAndQuarter()
    {
        var m = CreateWithTeams();
        m.AddGoal(TeamSide.Home);
        m.AddGoal(TeamSide.Away);
        m.EndQuarter();
        m.ResetForNewGame();

        Assert.Equal(0, m.HomeGoals);
        Assert.Equal(0, m.HomeBehinds);
        Assert.Equal(0, m.AwayGoals);
        Assert.Equal(0, m.AwayBehinds);
        Assert.Equal(1, m.Quarter);
        Assert.Empty(m.Events);
        Assert.False(m.ClockRunning);
    }

    [Fact]
    public void ResetForNewGame_ClearsQuarterSnapshots()
    {
        var m = CreateWithTeams();
        m.EndQuarter();
        m.ResetForNewGame();
        Assert.Null(m.GetQuarterSnapshot(1));
    }

    // ── DarkMode ─────────────────────────────────────────────────────────────

    [Fact]
    public void SetDarkMode_UpdatesProperty()
    {
        var m = new MatchManager();
        m.SetDarkMode(false);
        Assert.False(m.DarkMode);
    }

    [Fact]
    public void SetDarkMode_RaisesMatchChanged()
    {
        var m = new MatchManager();
        int count = 0;
        m.MatchChanged += () => count++;
        m.SetDarkMode(false);
        Assert.Equal(1, count);
    }

    // ── Serialisation / LoadState ────────────────────────────────────────────

    [Fact]
    public void ToState_CapturesCurrentMatchState()
    {
        var m = CreateWithTeams("Hawks", "HAW", "Blues", "CAR");
        m.AddGoal(TeamSide.Home);
        m.AddBehind(TeamSide.Away);

        var state = m.ToState();

        Assert.Equal("Hawks", state.HomeName);
        Assert.Equal("HAW", state.HomeAbbr);
        Assert.Equal("Blues", state.AwayName);
        Assert.Equal("CAR", state.AwayAbbr);
        Assert.Equal(1, state.HomeGoals);
        Assert.Equal(1, state.AwayBehinds);
        Assert.Equal(2, state.Events!.Count);
    }

    [Fact]
    public void LoadState_RestoresMatch()
    {
        var state = new SerializableState
        {
            HomeName = "Hawks",
            HomeAbbr = "HAW",
            AwayName = "Blues",
            AwayAbbr = "CAR",
            HomeGoals = 3,
            HomeBehinds = 2,
            AwayGoals = 1,
            AwayBehinds = 4,
            Quarter = 3,
            ClockRunning = false,
            ElapsedInQuarterTicks = 0,
            ClockMode = (int)ClockMode.CountUp,
            QuarterDurationTicks = TimeSpan.FromMinutes(20).Ticks,
            DarkMode = true,
            Events = new List<ScoreEvent>()
        };

        var m = new MatchManager();
        m.LoadState(state);

        Assert.Equal("Hawks", m.HomeName);
        Assert.Equal(3, m.HomeGoals);
        Assert.Equal(2, m.HomeBehinds);
        Assert.Equal(1, m.AwayGoals);
        Assert.Equal(4, m.AwayBehinds);
        Assert.Equal(3, m.Quarter);
        Assert.Equal(20, m.HomeTotal);   // 3×6+2
        Assert.Equal(10, m.AwayTotal);   // 1×6+4
    }

    [Fact]
    public void LoadState_ClampsQuarterTo1_4()
    {
        var state = new SerializableState { Quarter = 99 };
        var m = new MatchManager();
        m.LoadState(state);
        Assert.Equal(4, m.Quarter);
    }

    [Fact]
    public void LoadState_NullNames_UseDefaults()
    {
        var state = new SerializableState
        {
            HomeName = null,
            HomeAbbr = null,
            AwayName = null,
            AwayAbbr = null
        };
        var m = new MatchManager();
        m.LoadState(state);

        Assert.Equal("HOME", m.HomeName);
        Assert.Equal("HOM", m.HomeAbbr);
        Assert.Equal("AWAY", m.AwayName);
        Assert.Equal("AWY", m.AwayAbbr);
    }

    [Fact]
    public void LoadState_NegativeScores_ClampedToZero()
    {
        var state = new SerializableState { HomeGoals = -5, AwayBehinds = -3 };
        var m = new MatchManager();
        m.LoadState(state);
        Assert.Equal(0, m.HomeGoals);
        Assert.Equal(0, m.AwayBehinds);
    }

    // ── RaiseMatchChanged ────────────────────────────────────────────────────

    [Fact]
    public void RaiseMatchChanged_FiresEvent()
    {
        var m = new MatchManager();
        int count = 0;
        m.MatchChanged += () => count++;
        m.RaiseMatchChanged();
        Assert.Equal(1, count);
    }

    // ── Event snapshot correctness ───────────────────────────────────────────

    [Fact]
    public void ScoreEvent_SnapshotReflectsScoreAfterEvent()
    {
        var m = CreateWithTeams();
        m.AddGoal(TeamSide.Home);
        m.AddBehind(TeamSide.Away);

        var events = m.Events;
        Assert.Equal(2, events.Count);

        // First event: Home goal → Home 1.0.6, Away 0.0.0
        Assert.Equal(1, events[0].HomeGoals);
        Assert.Equal(0, events[0].AwayGoals);

        // Second event: Away behind → Home 1.0.6, Away 0.1.1
        Assert.Equal(1, events[1].HomeGoals);
        Assert.Equal(1, events[1].AwayBehinds);
    }

    // ── Tick (countdown expiry) ──────────────────────────────────────────────

    [Fact]
    public void Tick_InCountUpMode_DoesNotFireExpiry()
    {
        var m = new MatchManager();
        m.SetClockMode(ClockMode.CountUp);
        m.StartClock();
        int fired = 0;
        m.QuarterTimeExpired += () => fired++;
        m.Tick(); // count-up never expires via Tick
        Assert.Equal(0, fired);
    }
}

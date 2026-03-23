using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Tests;

public class WinProbabilityEngineTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static MatchSnapshot MakeSnap(
        int homeGoals = 0, int homeBehinds = 0,
        int awayGoals = 0, int awayBehinds = 0,
        int quarter = 1,
        double elapsedMinutes = 0.0,
        double quarterDurationMinutes = 20.0)
    {
        return new MatchSnapshot
        {
            HomeGoals = homeGoals,
            HomeBehinds = homeBehinds,
            AwayGoals = awayGoals,
            AwayBehinds = awayBehinds,
            Quarter = quarter,
            ElapsedMatchMinutes = elapsedMinutes,
            QuarterDurationMinutes = quarterDurationMinutes
        };
    }

    // ── Probability sanity ───────────────────────────────────────────────────

    [Fact]
    public void Compute_ProbabilitiesSumToOne()
    {
        var engine = new WinProbabilityEngine();
        var snap = MakeSnap(quarter: 1, elapsedMinutes: 5);
        var result = engine.Compute(snap);

        double total = result.HomeWinPct + result.AwayWinPct + result.DrawPct;
        Assert.Equal(1.0, total, precision: 4);
    }

    [Fact]
    public void Compute_ProbabilitiesAreNonNegative()
    {
        var engine = new WinProbabilityEngine();
        var snap = MakeSnap(quarter: 2, elapsedMinutes: 25);
        var result = engine.Compute(snap);

        Assert.True(result.HomeWinPct >= 0.0);
        Assert.True(result.AwayWinPct >= 0.0);
        Assert.True(result.DrawPct >= 0.0);
    }

    [Fact]
    public void Compute_ProbabilitiesAreBetweenZeroAndOne()
    {
        var engine = new WinProbabilityEngine();
        var snap = MakeSnap(quarter: 4, elapsedMinutes: 78, homeGoals: 10, awayGoals: 5);
        var result = engine.Compute(snap);

        Assert.True(result.HomeWinPct is >= 0.0 and <= 1.0);
        Assert.True(result.AwayWinPct is >= 0.0 and <= 1.0);
        Assert.True(result.DrawPct is >= 0.0 and <= 1.0);
    }

    // ── Large late-game lead → strong probability ─────────────────────────────

    [Fact]
    public void Compute_LargeHomeLead_LateQ4_HomeWinPctIsHigh()
    {
        var engine = new WinProbabilityEngine();
        // Home leading by 7 goals (42 pts) near end of Q4
        var snap = MakeSnap(
            homeGoals: 14, homeBehinds: 5,
            awayGoals: 7, awayBehinds: 8,
            quarter: 4,
            elapsedMinutes: 78); // 78 of 80 minutes elapsed

        var result = engine.Compute(snap);
        // Expect a decisive home win probability
        Assert.True(result.HomeWinPct > 0.90,
            $"Expected >0.90 but got {result.HomeWinPct:F4}");
    }

    [Fact]
    public void Compute_LargeAwayLead_LateQ4_AwayWinPctIsHigh()
    {
        var engine = new WinProbabilityEngine();
        var snap = MakeSnap(
            homeGoals: 3, homeBehinds: 4,
            awayGoals: 10, awayBehinds: 2,
            quarter: 4,
            elapsedMinutes: 79);

        var result = engine.Compute(snap);
        Assert.True(result.AwayWinPct > 0.90,
            $"Expected >0.90 but got {result.AwayWinPct:F4}");
    }

    // ── Early game near-50/50 ────────────────────────────────────────────────

    [Fact]
    public void Compute_TiedEarlyQ1_ProbabilitiesNearFiftyFifty()
    {
        var engine = new WinProbabilityEngine();
        var snap = MakeSnap(
            homeGoals: 0, homeBehinds: 0,
            awayGoals: 0, awayBehinds: 0,
            quarter: 1,
            elapsedMinutes: 0);

        var result = engine.Compute(snap);

        // With no information the model should lean near 50/50 for each team
        Assert.True(result.HomeWinPct is > 0.30 and < 0.70,
            $"Expected near 0.5, got {result.HomeWinPct:F4}");
    }

    // ── Confidence ───────────────────────────────────────────────────────────

    [Fact]
    public void Compute_LateGame_ConfidenceIsHigherThanEarlyGame()
    {
        var engine = new WinProbabilityEngine();
        var earlySnap = MakeSnap(quarter: 1, elapsedMinutes: 2);
        var lateSnap = MakeSnap(
            homeGoals: 10, awayGoals: 8,
            quarter: 4, elapsedMinutes: 75);

        var earlyResult = engine.Compute(earlySnap);
        var lateResult = engine.Compute(lateSnap);

        Assert.True(lateResult.Confidence > earlyResult.Confidence,
            $"Late confidence {lateResult.Confidence:F4} should exceed early {earlyResult.Confidence:F4}");
    }

    [Fact]
    public void Compute_ConfidenceIsBetweenZeroAndOne()
    {
        var engine = new WinProbabilityEngine();
        var snap = MakeSnap(quarter: 2, elapsedMinutes: 30);
        var result = engine.Compute(snap);

        Assert.True(result.Confidence is >= 0.0 and <= 1.0,
            $"Confidence {result.Confidence:F4} is out of [0,1]");
    }

    // ── HomeDelta ────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_FirstCall_DeltaIsRelativeToInitialFiftyFifty()
    {
        var engine = new WinProbabilityEngine();
        var snap = MakeSnap(quarter: 1, elapsedMinutes: 5);
        var result = engine.Compute(snap);
        // The engine initialises _previousHomeWinPct to 0.5, so the first
        // HomeDelta equals (HomeWinPct - 0.5).
        double expectedDelta = result.HomeWinPct - 0.5;
        Assert.Equal(expectedDelta, result.HomeDelta, precision: 4);
    }

    [Fact]
    public void Compute_HomeDeltaReflectsChange()
    {
        var engine = new WinProbabilityEngine();
        // First call: balanced game
        var snap1 = MakeSnap(quarter: 2, elapsedMinutes: 20);
        var result1 = engine.Compute(snap1);

        // Second call: Home now leads heavily
        var snap2 = MakeSnap(homeGoals: 12, awayGoals: 3, quarter: 3, elapsedMinutes: 50);
        var result2 = engine.Compute(snap2);

        // Delta should be positive (home probability increased)
        Assert.True(result2.HomeDelta > 0.0,
            $"Expected positive HomeDelta but got {result2.HomeDelta:F4}");
    }

    // ── Reset ────────────────────────────────────────────────────────────────

    [Fact]
    public void Reset_ReturnsHomeDeltaToInitialHalfDifference()
    {
        var engine = new WinProbabilityEngine();

        // Compute once to move _previousHomeWinPct away from 0.5
        var snap = MakeSnap(homeGoals: 10, awayGoals: 2, quarter: 4, elapsedMinutes: 70);
        engine.Compute(snap);

        engine.Reset();

        // After reset, the next computation should treat _previousHomeWinPct as 0.5
        var snap2 = MakeSnap(quarter: 1, elapsedMinutes: 1);
        var result = engine.Compute(snap2);
        // HomeDelta should again be relative to 0.5
        double impliedPrevious = result.HomeWinPct - result.HomeDelta;
        Assert.Equal(0.5, impliedPrevious, precision: 3);
    }

    // ── MatchSnapshot.FromMatch ───────────────────────────────────────────────

    [Fact]
    public void MatchSnapshot_FromMatch_CapturesCorrectValues()
    {
        var match = new MatchManager();
        match.SetTeams("H", "H", "A", "A");
        match.AddGoal(TeamSide.Home);
        match.AddBehind(TeamSide.Away);

        var snap = MatchSnapshot.FromMatch(match);

        Assert.Equal(1, snap.HomeGoals);
        Assert.Equal(1, snap.AwayBehinds);
        Assert.Equal(1, snap.Quarter);
        Assert.Equal(6, snap.HomeTotal);
        Assert.Equal(1, snap.AwayTotal);
        Assert.Equal(5, snap.CurrentMargin);
    }

    [Fact]
    public void MatchSnapshot_TotalMatchMinutes_IsFourTimesQuarterDuration()
    {
        var snap = new MatchSnapshot { QuarterDurationMinutes = 20.0 };
        Assert.Equal(80.0, snap.TotalMatchMinutes);
    }

    // ── MarginStdDev is non-negative ──────────────────────────────────────────

    [Fact]
    public void Compute_MarginStdDevIsNonNegative()
    {
        var engine = new WinProbabilityEngine();
        var snap = MakeSnap(homeGoals: 5, awayGoals: 5, quarter: 2, elapsedMinutes: 25);
        var result = engine.Compute(snap);
        Assert.True(result.MarginStdDev >= 0.0);
    }
}

using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Tests;

public class MatchStatsTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static MatchManager CreateAndScore(Action<MatchManager> setup)
    {
        var m = new MatchManager();
        m.SetTeams("Home", "HOM", "Away", "AWY");
        setup(m);
        return m;
    }

    // ── Empty match ──────────────────────────────────────────────────────────

    [Fact]
    public void Calculate_NoEvents_AllZero()
    {
        var m = new MatchManager();
        var stats = MatchStats.Calculate(m);

        Assert.Equal(0, stats.HomeScoringShots);
        Assert.Equal(0, stats.AwayScoringShots);
        Assert.Equal(0.0, stats.HomeAccuracy);
        Assert.Equal(0.0, stats.AwayAccuracy);
        Assert.Equal(0.0, stats.HomeTimePctInFront);
        Assert.Equal(0.0, stats.AwayTimePctInFront);
        Assert.Equal(0.0, stats.DrawPct);
        Assert.Equal(0, stats.HomeLargestLead);
        Assert.Equal(0, stats.AwayLargestLead);
        Assert.Equal(0, stats.LeadChanges);
    }

    // ── Scoring shots ────────────────────────────────────────────────────────

    [Fact]
    public void ScoringShots_AreGoalsPlusBehinds()
    {
        var m = CreateAndScore(mm =>
        {
            mm.AddGoal(TeamSide.Home);
            mm.AddGoal(TeamSide.Home);
            mm.AddBehind(TeamSide.Home);   // 3 shots
            mm.AddGoal(TeamSide.Away);
            mm.AddBehind(TeamSide.Away);   // 2 shots
        });

        var stats = MatchStats.Calculate(m);
        Assert.Equal(3, stats.HomeScoringShots);
        Assert.Equal(2, stats.AwayScoringShots);
    }

    // ── Accuracy ─────────────────────────────────────────────────────────────

    [Fact]
    public void HomeAccuracy_AllGoals_Is100Percent()
    {
        var m = CreateAndScore(mm =>
        {
            mm.AddGoal(TeamSide.Home);
            mm.AddGoal(TeamSide.Home);
        });
        var stats = MatchStats.Calculate(m);
        Assert.Equal(100.0, stats.HomeAccuracy);
    }

    [Fact]
    public void HomeAccuracy_NoGoals_IsZeroPercent()
    {
        var m = CreateAndScore(mm =>
        {
            mm.AddBehind(TeamSide.Home);
            mm.AddBehind(TeamSide.Home);
        });
        var stats = MatchStats.Calculate(m);
        Assert.Equal(0.0, stats.HomeAccuracy);
    }

    [Fact]
    public void HomeAccuracy_Mixed_IsRounded()
    {
        var m = CreateAndScore(mm =>
        {
            // 2 goals, 1 behind → accuracy = 66.7%
            mm.AddGoal(TeamSide.Home);
            mm.AddGoal(TeamSide.Home);
            mm.AddBehind(TeamSide.Home);
        });
        var stats = MatchStats.Calculate(m);
        Assert.Equal(66.7, stats.HomeAccuracy);
    }

    [Fact]
    public void AwayAccuracy_IsCalculatedSeparately()
    {
        var m = CreateAndScore(mm =>
        {
            mm.AddGoal(TeamSide.Away);
            mm.AddBehind(TeamSide.Away);
            mm.AddBehind(TeamSide.Away);
            // 1 goal, 2 behinds → accuracy = 33.3%
        });
        var stats = MatchStats.Calculate(m);
        Assert.Equal(33.3, stats.AwayAccuracy);
    }

    // ── Largest lead ─────────────────────────────────────────────────────────

    [Fact]
    public void HomeLargestLead_TracksMaximumHomeAdvantage()
    {
        var m = CreateAndScore(mm =>
        {
            mm.AddGoal(TeamSide.Home); // Margin: +6
            mm.AddGoal(TeamSide.Home); // Margin: +12
            mm.AddGoal(TeamSide.Away); // Margin: +6
        });
        var stats = MatchStats.Calculate(m);
        Assert.Equal(12, stats.HomeLargestLead);
    }

    [Fact]
    public void AwayLargestLead_TracksMaximumAwayAdvantage()
    {
        var m = CreateAndScore(mm =>
        {
            mm.AddGoal(TeamSide.Away);  // Margin: -6
            mm.AddGoal(TeamSide.Away);  // Margin: -12
            mm.AddGoal(TeamSide.Home);  // Margin: -6
        });
        var stats = MatchStats.Calculate(m);
        Assert.Equal(12, stats.AwayLargestLead);
    }

    // ── Lead changes ─────────────────────────────────────────────────────────

    [Fact]
    public void LeadChanges_ZeroWhenOneTeamAlwaysLeads()
    {
        var m = CreateAndScore(mm =>
        {
            mm.AddGoal(TeamSide.Home);
            mm.AddGoal(TeamSide.Home);
            mm.AddBehind(TeamSide.Away);
        });
        var stats = MatchStats.Calculate(m);
        Assert.Equal(0, stats.LeadChanges);
    }

    [Fact]
    public void LeadChanges_CountsEachTimeLeaderChanges()
    {
        var m = CreateAndScore(mm =>
        {
            mm.AddGoal(TeamSide.Home);  // Home leads
            mm.AddGoal(TeamSide.Away);
            mm.AddGoal(TeamSide.Away);  // Away leads (+6) → 1 lead change
            mm.AddGoal(TeamSide.Home);
            mm.AddGoal(TeamSide.Home);  // Home leads (+6) → 2 lead changes
        });
        var stats = MatchStats.Calculate(m);
        Assert.Equal(2, stats.LeadChanges);
    }

    // ── Time % in front ──────────────────────────────────────────────────────

    [Fact]
    public void HomeTimePct_IsProportionOfEventsWhereHomeLeads()
    {
        // 3 events: Home goal (Home leads), Away goal (draw), Away goal (Away leads)
        var m = CreateAndScore(mm =>
        {
            mm.AddGoal(TeamSide.Home); // after: Home 6, Away 0 → Home leads
            mm.AddGoal(TeamSide.Away); // after: Home 6, Away 6 → draw
            mm.AddGoal(TeamSide.Away); // after: Home 6, Away 12 → Away leads
        });
        var stats = MatchStats.Calculate(m);
        // 1 event where Home leads out of 3
        Assert.Equal(33.3, stats.HomeTimePctInFront);
        // 1 event where Away leads
        Assert.Equal(33.3, stats.AwayTimePctInFront);
        // 1 draw event
        Assert.Equal(33.3, stats.DrawPct);
    }

    // ── Per-quarter breakdown ────────────────────────────────────────────────

    [Fact]
    public void GoalsPerQuarter_AttributedCorrectly()
    {
        var m = new MatchManager();
        m.SetTeams("H", "H", "A", "A");

        // Score 2 goals in Q1
        m.AddGoal(TeamSide.Home);
        m.AddGoal(TeamSide.Home);
        m.EndQuarter(); // advance to Q2

        // Score 1 goal in Q2
        m.AddGoal(TeamSide.Away);

        var stats = MatchStats.Calculate(m);
        Assert.Equal(2, stats.HomeGoalsPerQuarter[0]); // Q1
        Assert.Equal(0, stats.HomeGoalsPerQuarter[1]); // Q2
        Assert.Equal(1, stats.AwayGoalsPerQuarter[1]); // Q2
    }

    // ── Best quarter ─────────────────────────────────────────────────────────

    [Fact]
    public void HomeBestQuarter_IsQuarterWithMostPoints()
    {
        var m = new MatchManager();
        m.SetTeams("H", "H", "A", "A");

        // Q1: 1 goal (6pts)
        m.AddGoal(TeamSide.Home);
        m.EndQuarter();

        // Q2: 3 goals (18pts) – should be best
        m.AddGoal(TeamSide.Home);
        m.AddGoal(TeamSide.Home);
        m.AddGoal(TeamSide.Home);
        m.EndQuarter();

        var stats = MatchStats.Calculate(m);
        Assert.Equal(2, stats.HomeBestQuarter);
    }

    [Fact]
    public void AwayBestQuarter_IsQuarterWithMostPoints()
    {
        var m = new MatchManager();
        m.SetTeams("H", "H", "A", "A");

        m.AddBehind(TeamSide.Away); // Q1: 1pt
        m.EndQuarter();
        m.AddGoal(TeamSide.Away);
        m.AddGoal(TeamSide.Away);  // Q2: 12pts

        var stats = MatchStats.Calculate(m);
        Assert.Equal(2, stats.AwayBestQuarter);
    }

    // ── Behind-per-quarter ────────────────────────────────────────────────────

    [Fact]
    public void BehindsPerQuarter_AttributedCorrectly()
    {
        var m = new MatchManager();
        m.SetTeams("H", "H", "A", "A");

        m.AddBehind(TeamSide.Home);
        m.AddBehind(TeamSide.Home);
        m.EndQuarter();

        m.AddBehind(TeamSide.Away);

        var stats = MatchStats.Calculate(m);
        Assert.Equal(2, stats.HomeBehindsPerQuarter[0]);
        Assert.Equal(0, stats.HomeBehindsPerQuarter[1]);
        Assert.Equal(1, stats.AwayBehindsPerQuarter[1]);
    }
}

using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Tests;

public class ScoreEventTests
{
    // ── Description ──────────────────────────────────────────────────────────

    [Fact]
    public void Description_HomeGoal()
    {
        var ev = new ScoreEvent { Team = TeamSide.Home, Type = ScoreType.Goal };
        Assert.Equal("Home Goal", ev.Description);
    }

    [Fact]
    public void Description_AwayBehind()
    {
        var ev = new ScoreEvent { Team = TeamSide.Away, Type = ScoreType.Behind };
        Assert.Equal("Away Behind", ev.Description);
    }

    // ── Score totals ─────────────────────────────────────────────────────────

    [Fact]
    public void HomeTotal_IsGoalsTimes6PlusBehinds()
    {
        var ev = new ScoreEvent { HomeGoals = 3, HomeBehinds = 2 };
        Assert.Equal(20, ev.HomeTotal); // 3×6 + 2
    }

    [Fact]
    public void AwayTotal_IsGoalsTimes6PlusBehinds()
    {
        var ev = new ScoreEvent { AwayGoals = 1, AwayBehinds = 5 };
        Assert.Equal(11, ev.AwayTotal); // 1×6 + 5
    }

    [Fact]
    public void Margin_PositiveWhenHomeLeads()
    {
        var ev = new ScoreEvent { HomeGoals = 2, HomeBehinds = 0, AwayGoals = 0, AwayBehinds = 1 };
        Assert.Equal(11, ev.Margin); // 12 - 1
    }

    [Fact]
    public void Margin_NegativeWhenAwayLeads()
    {
        var ev = new ScoreEvent { HomeGoals = 0, HomeBehinds = 1, AwayGoals = 2, AwayBehinds = 0 };
        Assert.Equal(-11, ev.Margin);
    }

    [Fact]
    public void Margin_Zero_WhenScoresEqual()
    {
        var ev = new ScoreEvent { HomeGoals = 1, HomeBehinds = 1, AwayGoals = 1, AwayBehinds = 1 };
        Assert.Equal(0, ev.Margin);
    }

    // ── FormatLog ─────────────────────────────────────────────────────────────

    [Fact]
    public void FormatLog_HomeGoal_ContainsCorrectInfo()
    {
        var ev = new ScoreEvent
        {
            Quarter = 2,
            GameTime = new TimeSpan(0, 5, 30), // 05:30
            Team = TeamSide.Home,
            Type = ScoreType.Goal,
            HomeGoals = 3,
            HomeBehinds = 1,
            AwayGoals = 1,
            AwayBehinds = 2
        };

        string log = ev.FormatLog("Hawks", "Blues");
        Assert.Contains("Q2", log);
        Assert.Contains("05:30", log);
        Assert.Contains("Hawks", log);
        Assert.Contains("Goal", log);
        // Score display: 3.1.19
        Assert.Contains("3.1.19", log);
    }

    [Fact]
    public void FormatLog_AwayBehind_ContainsCorrectInfo()
    {
        var ev = new ScoreEvent
        {
            Quarter = 3,
            GameTime = new TimeSpan(0, 10, 00),
            Team = TeamSide.Away,
            Type = ScoreType.Behind,
            HomeGoals = 2,
            HomeBehinds = 0,
            AwayGoals = 1,
            AwayBehinds = 3
        };

        string log = ev.FormatLog("Hawks", "Blues");
        Assert.Contains("Q3", log);
        Assert.Contains("Blues", log);
        Assert.Contains("Behind", log);
        // Away behind: 1.3.9
        Assert.Contains("1.3.9", log);
    }

    [Fact]
    public void FormatLog_ClockFormattedWithLeadingZeros()
    {
        var ev = new ScoreEvent
        {
            Quarter = 1,
            GameTime = new TimeSpan(0, 3, 5), // 3 minutes, 5 seconds
            Team = TeamSide.Home,
            Type = ScoreType.Goal
        };

        string log = ev.FormatLog("H", "A");
        // Minutes should be two digits: "03", seconds should be "05"
        Assert.Contains("03:05", log);
    }
}

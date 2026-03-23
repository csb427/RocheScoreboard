using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Tests;

public class QuarterSnapshotTests
{
    [Fact]
    public void HomeTotal_IsGoalsTimes6PlusBehinds()
    {
        var snap = new QuarterSnapshot { HomeGoals = 4, HomeBehinds = 3 };
        Assert.Equal(27, snap.HomeTotal); // 4×6 + 3
    }

    [Fact]
    public void AwayTotal_IsGoalsTimes6PlusBehinds()
    {
        var snap = new QuarterSnapshot { AwayGoals = 2, AwayBehinds = 7 };
        Assert.Equal(19, snap.AwayTotal); // 2×6 + 7
    }

    [Fact]
    public void ZeroScores_TotalsAreZero()
    {
        var snap = new QuarterSnapshot();
        Assert.Equal(0, snap.HomeTotal);
        Assert.Equal(0, snap.AwayTotal);
    }
}

public class SerializableStateTests
{
    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var events = new List<ScoreEvent>
        {
            new ScoreEvent
            {
                Quarter = 1,
                Team = TeamSide.Home,
                Type = ScoreType.Goal,
                HomeGoals = 1,
                HomeBehinds = 0,
                AwayGoals = 0,
                AwayBehinds = 0
            }
        };

        var original = new SerializableState
        {
            HomeName = "Hawks",
            HomeAbbr = "HAW",
            AwayName = "Blues",
            AwayAbbr = "CAR",
            HomeGoals = 5,
            HomeBehinds = 3,
            AwayGoals = 2,
            AwayBehinds = 8,
            Quarter = 3,
            ClockRunning = false,
            ElapsedInQuarterTicks = 600_000_000L,
            ClockMode = (int)ClockMode.CountUp,
            QuarterDurationTicks = TimeSpan.FromMinutes(20).Ticks,
            DarkMode = false,
            Events = events
        };

        // Serialize via the MatchManager round-trip
        var m = new MatchManager();
        m.LoadState(original);
        var restored = m.ToState();

        Assert.Equal(original.HomeName, restored.HomeName);
        Assert.Equal(original.HomeAbbr, restored.HomeAbbr);
        Assert.Equal(original.HomeGoals, restored.HomeGoals);
        Assert.Equal(original.HomeBehinds, restored.HomeBehinds);
        Assert.Equal(original.AwayGoals, restored.AwayGoals);
        Assert.Equal(original.AwayBehinds, restored.AwayBehinds);
        Assert.Equal(original.Quarter, restored.Quarter);
        Assert.Equal(original.DarkMode, restored.DarkMode);
        Assert.Single(restored.Events!);
    }
}

public class TeamPresetTests
{
    [Fact]
    public void TeamPreset_DefaultValues()
    {
        var preset = new TeamPreset();
        Assert.Equal("", preset.PresetName);
        Assert.Equal("", preset.TeamName);
        Assert.Equal("", preset.Abbreviation);
        Assert.Equal("#0A2A6A", preset.PrimaryColor);
        Assert.Equal("#FFFFFF", preset.SecondaryColor);
        Assert.Null(preset.LogoPath);
        Assert.Null(preset.GoalVideoPath);
        Assert.Null(preset.CricketPlayers);
    }

    [Fact]
    public void CricketPlayerEntry_DefaultValues()
    {
        var entry = new CricketPlayerEntry();
        Assert.Equal("", entry.FirstName);
        Assert.Equal("", entry.LastName);
    }
}

public class SetupResultTests
{
    [Fact]
    public void SetupResult_DefaultValues()
    {
        var result = new SetupResult();
        Assert.Equal("Home Team", result.HomeName);
        Assert.Equal("HOM", result.HomeAbbr);
        Assert.Equal("Away Team", result.AwayName);
        Assert.Equal("AWA", result.AwayAbbr);
        Assert.Equal(ClockMode.CountUp, result.ClockMode);
        Assert.Equal(20, result.QuarterMinutes);
        Assert.Equal(0, result.QuarterSeconds);
    }
}

public class CricketSetupResultTests
{
    [Fact]
    public void CricketSetupResult_DefaultValues()
    {
        var result = new CricketSetupResult();
        Assert.Equal("Team A", result.TeamAName);
        Assert.Equal("TMA", result.TeamAAbbr);
        Assert.Equal("Team B", result.TeamBName);
        Assert.Equal("TMB", result.TeamBAbbr);
        Assert.Equal(CricketFormat.LimitedOvers, result.Format);
        Assert.Equal(50, result.TotalOvers);
        Assert.True(result.TeamABatsFirst);
    }
}

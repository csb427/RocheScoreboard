using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Tests;

public class CricketPlayerTests
{
    // ── DisplayName ──────────────────────────────────────────────────────────

    [Fact]
    public void DisplayName_WithBothNames_ShowsInitialAndLastName()
    {
        var p = new CricketPlayer { FirstName = "Virat", LastName = "Kohli" };
        Assert.Equal("V. Kohli", p.DisplayName);
    }

    [Fact]
    public void DisplayName_NoFirstName_ShowsLastNameOnly()
    {
        var p = new CricketPlayer { FirstName = "", LastName = "Kohli" };
        Assert.Equal("Kohli", p.DisplayName);
    }

    [Fact]
    public void FullName_CombinesBothNames()
    {
        var p = new CricketPlayer { FirstName = "Steve", LastName = "Smith" };
        Assert.Equal("Steve Smith", p.FullName);
    }

    [Fact]
    public void FullName_NoFirstName_IsJustLastName()
    {
        var p = new CricketPlayer { FirstName = "", LastName = "Smith" };
        Assert.Equal("Smith", p.FullName);
    }

    // ── StrikeRate ───────────────────────────────────────────────────────────

    [Fact]
    public void StrikeRate_ZeroWhenNoBallsFaced()
    {
        var p = new CricketPlayer { Runs = 10, BallsFaced = 0 };
        Assert.Equal(0.0, p.StrikeRate);
    }

    [Fact]
    public void StrikeRate_CalculatedCorrectly()
    {
        var p = new CricketPlayer { Runs = 50, BallsFaced = 40 };
        Assert.Equal(125.0, p.StrikeRate);
    }

    [Fact]
    public void StrikeRate_RoundedToOneDecimal()
    {
        var p = new CricketPlayer { Runs = 1, BallsFaced = 3 };
        // 100/3 = 33.33... → 33.3
        Assert.Equal(33.3, p.StrikeRate);
    }

    // ── Economy ──────────────────────────────────────────────────────────────

    [Fact]
    public void Economy_ZeroWhenNoBallsBowled()
    {
        var p = new CricketPlayer { RunsConceded = 20, BallsBowled = 0 };
        Assert.Equal(0.0, p.Economy);
    }

    [Fact]
    public void Economy_CalculatedCorrectly()
    {
        // 24 runs in 12 balls = 2 overs → Economy = 12.0
        var p = new CricketPlayer { RunsConceded = 24, BallsBowled = 12 };
        Assert.Equal(12.0, p.Economy);
    }

    [Fact]
    public void Economy_StandardOver()
    {
        // 30 runs in 6 balls = 1 over → Economy = 30.0
        var p = new CricketPlayer { RunsConceded = 30, BallsBowled = 6 };
        Assert.Equal(30.0, p.Economy);
    }

    // ── BowlingFigures ────────────────────────────────────────────────────────

    [Fact]
    public void BowlingFigures_ShowsWicketsAndRunsConceded()
    {
        var p = new CricketPlayer { Wickets = 3, RunsConceded = 42 };
        Assert.Equal("3-42", p.BowlingFigures);
    }

    // ── OversDisplay ─────────────────────────────────────────────────────────

    [Fact]
    public void OversDisplay_CompletedOversOnly()
    {
        var p = new CricketPlayer { BallsBowled = 12 }; // 2 full overs
        Assert.Equal("2", p.OversDisplay);
    }

    [Fact]
    public void OversDisplay_WithPartialOver()
    {
        var p = new CricketPlayer { BallsBowled = 14 }; // 2 overs + 2 balls
        Assert.Equal("2.2", p.OversDisplay);
    }

    // ── DismissalSummary ─────────────────────────────────────────────────────

    [Fact]
    public void DismissalSummary_NotOut()
    {
        var p = new CricketPlayer { IsOut = false };
        Assert.Equal("not out", p.DismissalSummary);
    }

    [Fact]
    public void DismissalSummary_Bowled()
    {
        var p = new CricketPlayer
        {
            IsOut = true,
            HowOut = DismissalType.Bowled,
            DismissalBowler = "Cummins"
        };
        Assert.Equal("b: Cummins", p.DismissalSummary);
    }

    [Fact]
    public void DismissalSummary_Caught()
    {
        var p = new CricketPlayer
        {
            IsOut = true,
            HowOut = DismissalType.Caught,
            DismissalFielder = "Warner",
            DismissalBowler = "Starc"
        };
        Assert.Equal("c: Warner b: Starc", p.DismissalSummary);
    }

    [Fact]
    public void DismissalSummary_CaughtAndBowled()
    {
        var p = new CricketPlayer
        {
            IsOut = true,
            HowOut = DismissalType.CaughtAndBowled,
            DismissalBowler = "Hazlewood"
        };
        Assert.Equal("c&b: Hazlewood", p.DismissalSummary);
    }

    [Fact]
    public void DismissalSummary_LBW()
    {
        var p = new CricketPlayer
        {
            IsOut = true,
            HowOut = DismissalType.LBW,
            DismissalBowler = "Anderson"
        };
        Assert.Equal("lbw b: Anderson", p.DismissalSummary);
    }

    [Fact]
    public void DismissalSummary_Stumped()
    {
        var p = new CricketPlayer
        {
            IsOut = true,
            HowOut = DismissalType.Stumped,
            DismissalFielder = "Pant",
            DismissalBowler = "Jadeja"
        };
        Assert.Equal("st: Pant b: Jadeja", p.DismissalSummary);
    }

    [Fact]
    public void DismissalSummary_RunOut()
    {
        var p = new CricketPlayer
        {
            IsOut = true,
            HowOut = DismissalType.RunOut,
            DismissalFielder = "Root"
        };
        Assert.Equal("run out (Root)", p.DismissalSummary);
    }

    [Fact]
    public void DismissalSummary_HitWicket()
    {
        var p = new CricketPlayer
        {
            IsOut = true,
            HowOut = DismissalType.HitWicket,
            DismissalBowler = "Bumrah"
        };
        Assert.Equal("hit wicket b: Bumrah", p.DismissalSummary);
    }

    [Fact]
    public void DismissalSummary_RetiredHurt()
    {
        var p = new CricketPlayer { IsOut = true, HowOut = DismissalType.RetiredHurt };
        Assert.Equal("retired hurt", p.DismissalSummary);
    }

    [Fact]
    public void DismissalSummary_Other_UsesText()
    {
        var p = new CricketPlayer
        {
            IsOut = true,
            HowOut = DismissalType.Other,
            DismissalText = "handled the ball"
        };
        Assert.Equal("handled the ball", p.DismissalSummary);
    }

    [Fact]
    public void DismissalSummary_Other_NullText_FallsBack()
    {
        var p = new CricketPlayer { IsOut = true, HowOut = DismissalType.Other, DismissalText = null };
        Assert.Equal("out", p.DismissalSummary);
    }

    // ── ResetBatting / ResetBowling ──────────────────────────────────────────

    [Fact]
    public void ResetBatting_ClearsAllBattingStats()
    {
        var p = new CricketPlayer
        {
            Runs = 50,
            BallsFaced = 40,
            Fours = 5,
            Sixes = 2,
            IsOut = true,
            DismissalText = "bowled",
            HowOut = DismissalType.Bowled,
            DismissalBowler = "Jones",
            DismissalFielder = "Smith"
        };

        p.ResetBatting();

        Assert.Equal(0, p.Runs);
        Assert.Equal(0, p.BallsFaced);
        Assert.Equal(0, p.Fours);
        Assert.Equal(0, p.Sixes);
        Assert.False(p.IsOut);
        Assert.Null(p.DismissalText);
        Assert.Equal(DismissalType.NotOut, p.HowOut);
        Assert.Null(p.DismissalBowler);
        Assert.Null(p.DismissalFielder);
    }

    [Fact]
    public void ResetBowling_ClearsAllBowlingStats()
    {
        var p = new CricketPlayer
        {
            BallsBowled = 24,
            RunsConceded = 45,
            Wickets = 3,
            Maidens = 1,
            Wides = 2,
            NoBalls = 1,
            LastOverBowledAt = 4
        };

        p.ResetBowling();

        Assert.Equal(0, p.BallsBowled);
        Assert.Equal(0, p.RunsConceded);
        Assert.Equal(0, p.Wickets);
        Assert.Equal(0, p.Maidens);
        Assert.Equal(0, p.Wides);
        Assert.Equal(0, p.NoBalls);
        Assert.Equal(-1, p.LastOverBowledAt);
    }
}

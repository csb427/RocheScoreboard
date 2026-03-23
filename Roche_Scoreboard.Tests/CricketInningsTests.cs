using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Tests;

public class CricketInningsTests
{
    // ── OversDisplay ─────────────────────────────────────────────────────────

    [Fact]
    public void OversDisplay_ZeroBalls_IsZero()
    {
        var inn = new CricketInnings { LegalBallsBowled = 0 };
        Assert.Equal("0", inn.OversDisplay);
    }

    [Fact]
    public void OversDisplay_SixBalls_IsOneOver()
    {
        var inn = new CricketInnings { LegalBallsBowled = 6 };
        Assert.Equal("1", inn.OversDisplay);
    }

    [Fact]
    public void OversDisplay_PartialOver_ShowsDotNotation()
    {
        var inn = new CricketInnings { LegalBallsBowled = 8 }; // 1 over + 2 balls
        Assert.Equal("1.2", inn.OversDisplay);
    }

    [Fact]
    public void OversDisplay_ExactlyTwoOvers()
    {
        var inn = new CricketInnings { LegalBallsBowled = 12 };
        Assert.Equal("2", inn.OversDisplay);
    }

    // ── CompletedOvers / BallsInCurrentOver ──────────────────────────────────

    [Fact]
    public void CompletedOvers_IsLegalBallsDividedBy6()
    {
        var inn = new CricketInnings { LegalBallsBowled = 15 };
        Assert.Equal(2, inn.CompletedOvers);
    }

    [Fact]
    public void BallsInCurrentOver_IsLegalBallsModulo6()
    {
        var inn = new CricketInnings { LegalBallsBowled = 15 };
        Assert.Equal(3, inn.BallsInCurrentOver);
    }

    // ── RunRate ──────────────────────────────────────────────────────────────

    [Fact]
    public void RunRate_ZeroWhenNoBallsBowled()
    {
        var inn = new CricketInnings { TotalRuns = 50, LegalBallsBowled = 0 };
        Assert.Equal(0.0, inn.RunRate);
    }

    [Fact]
    public void RunRate_CalculatedCorrectly()
    {
        // 60 runs in 12 legal balls = 2 overs → RunRate = 30.0
        var inn = new CricketInnings { TotalRuns = 60, LegalBallsBowled = 12 };
        Assert.Equal(30.0, inn.RunRate);
    }

    [Fact]
    public void RunRate_RoundedToTwoDecimals()
    {
        // 10 runs in 7 balls → 6 × 10/7 = 8.571... → 8.57
        var inn = new CricketInnings { TotalRuns = 10, LegalBallsBowled = 7 };
        Assert.Equal(8.57, inn.RunRate);
    }

    // ── IsAllOut ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsAllOut_FalseWhenWicketsLessThanBattersMinusOne()
    {
        var inn = new CricketInnings
        {
            BattingOrder = Enumerable.Range(0, 11).Select(_ => new CricketPlayer()).ToList(),
            TotalWickets = 9
        };
        Assert.False(inn.IsAllOut);
    }

    [Fact]
    public void IsAllOut_TrueWhenTenWicketsFallen()
    {
        var inn = new CricketInnings
        {
            BattingOrder = Enumerable.Range(0, 11).Select(_ => new CricketPlayer()).ToList(),
            TotalWickets = 10
        };
        Assert.True(inn.IsAllOut);
    }

    // ── Striker / NonStriker / CurrentBowler (index access) ──────────────────

    [Fact]
    public void Striker_NullWhenIndexNegative()
    {
        var inn = new CricketInnings { StrikerIndex = -1 };
        Assert.Null(inn.Striker);
    }

    [Fact]
    public void Striker_ReturnsCorrectPlayer()
    {
        var p = new CricketPlayer { LastName = "Root" };
        var inn = new CricketInnings
        {
            BattingOrder = new List<CricketPlayer> { p },
            StrikerIndex = 0
        };
        Assert.Equal(p, inn.Striker);
    }

    [Fact]
    public void NonStriker_NullWhenIndexOutOfRange()
    {
        var inn = new CricketInnings
        {
            BattingOrder = new List<CricketPlayer> { new CricketPlayer() },
            NonStrikerIndex = 5
        };
        Assert.Null(inn.NonStriker);
    }
}

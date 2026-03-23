using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Tests;

public class CricketDeliveryTests
{
    // ── Description property ─────────────────────────────────────────────────

    [Fact]
    public void Description_Dot()
    {
        var d = new CricketDelivery { Type = CricketDeliveryType.Dot };
        Assert.Equal("Dot ball", d.Description);
    }

    [Fact]
    public void Description_Four()
    {
        var d = new CricketDelivery { Type = CricketDeliveryType.Four };
        Assert.Equal("FOUR", d.Description);
    }

    [Fact]
    public void Description_Six()
    {
        var d = new CricketDelivery { Type = CricketDeliveryType.Six };
        Assert.Equal("SIX", d.Description);
    }

    [Fact]
    public void Description_Wide_IncludesRuns()
    {
        var d = new CricketDelivery { Type = CricketDeliveryType.Wide, Runs = 1 };
        Assert.Equal("Wide (+1)", d.Description);
    }

    [Fact]
    public void Description_NoBall_IncludesRuns()
    {
        var d = new CricketDelivery { Type = CricketDeliveryType.NoBall, Runs = 2 };
        Assert.Equal("No ball (+2)", d.Description);
    }

    [Fact]
    public void Description_Bye_IncludesRuns()
    {
        var d = new CricketDelivery { Type = CricketDeliveryType.Bye, Runs = 3 };
        Assert.Equal("Bye (+3)", d.Description);
    }

    [Fact]
    public void Description_LegBye_IncludesRuns()
    {
        var d = new CricketDelivery { Type = CricketDeliveryType.LegBye, Runs = 2 };
        Assert.Equal("Leg bye (+2)", d.Description);
    }

    [Fact]
    public void Description_Wicket_IncludesDismissalText()
    {
        var d = new CricketDelivery
        {
            Type = CricketDeliveryType.Wicket,
            DismissalText = "c: Warner b: Starc"
        };
        Assert.Equal("WICKET — c: Warner b: Starc", d.Description);
    }

    [Fact]
    public void Description_SingleRun()
    {
        var d = new CricketDelivery { Type = CricketDeliveryType.Runs, Runs = 1 };
        Assert.Equal("1 run", d.Description);
    }

    [Fact]
    public void Description_MultipleRuns()
    {
        var d = new CricketDelivery { Type = CricketDeliveryType.Runs, Runs = 3 };
        Assert.Equal("3 runs", d.Description);
    }
}

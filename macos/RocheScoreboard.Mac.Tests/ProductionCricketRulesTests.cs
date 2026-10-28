using Roche_Scoreboard.Services;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public class ProductionCricketRulesTests
{
    [Theory]
    [InlineData("Conditions")][InlineData("DeliveryRule")][InlineData("OverAndStrike")]
    [InlineData("Workflow")][InlineData("DeliveryLog")][InlineData("Toss")]
    public void CurrentWindowsRuleChecksPassUnmodifiedOnTheSharedMacEngine(string suite)
    {
        var checks=suite switch
        {
            "Conditions"=>CricketConditionsChecks.Run(),"DeliveryRule"=>CricketDeliveryRuleChecks.Run(),
            "OverAndStrike"=>CricketOverAndStrikeChecks.Run(),"Workflow"=>CricketWorkflowChecks.Run(),
            "DeliveryLog"=>CricketDeliveryLogChecks.Run(),"Toss"=>CricketTossChecks.Run(),
            _=>throw new ArgumentOutOfRangeException(nameof(suite))
        };
        Assert.NotEmpty(checks);
        Assert.All(checks,check=>Assert.True(check.Value,suite+": "+check.Key));
    }
}

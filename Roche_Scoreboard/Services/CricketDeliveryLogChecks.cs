using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

internal static class CricketDeliveryLogChecks
{
    internal static Dictionary<string, bool> Run()
    {
        var checks = new Dictionary<string, bool>();
        foreach (var source in Enum.GetValues<CricketNoBallRuns>())
            checks[$"plain-no-ball-{source}-omits-zero-additional-runs"] = new CricketDelivery
                { Type = CricketDeliveryType.NoBall, Runs = 1, NoBallRuns = source }.Description == "No ball";
        checks["no-ball-describes-additional-byes-not-penalty-as-bye"] = new CricketDelivery
            { Type = CricketDeliveryType.NoBall, Runs = 3, NoBallRuns = CricketNoBallRuns.Byes }.Description == "No ball + 2 byes";
        checks["no-ball-describes-one-additional-leg-bye"] = new CricketDelivery
            { Type = CricketDeliveryType.NoBall, Runs = 2, NoBallRuns = CricketNoBallRuns.LegByes }.Description == "No ball + 1 leg bye";
        checks["no-ball-describes-bat-runs-separately-from-penalty"] = new CricketDelivery
            { Type = CricketDeliveryType.NoBall, Runs = 5, NoBallRuns = CricketNoBallRuns.Bat }.Description == "No ball + 4 off bat";
        return checks;
    }
}

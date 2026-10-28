using System.Globalization;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Views;

public partial class CricketScorebugControl
{
    internal static (string Label, string Value) SelectRailContext(CricketMatchManager match, int phase)
    {
        var innings = match.CurrentInnings;
        if (innings is null) return ("RUN RATE", "0.00");
        if (match.BreakKind != CricketBreakKind.None)
            return ("BREAK", match.BreakKind switch { CricketBreakKind.Rain => "RAIN", CricketBreakKind.Other => "PAUSED", _ => match.BreakKind.ToString().ToUpperInvariant() });
        if(match.FreeHitsEnabled && match.FreeHitPending) return ("NEXT BALL","FREE HIT");
        string Rate() => innings.RunRate.ToString("0.00", CultureInfo.InvariantCulture);
        (string, string) Partnership() => ("PARTNERSHIP", $"{innings.PartnershipRuns}({innings.PartnershipBalls})");
        (string, string) OversLeft()
        {
            int balls = Math.Max(0, match.CurrentOverLimit * 6 - innings.LegalBallsBowled);
            return ("OVERS LEFT", $"{balls / 6}.{balls % 6}");
        }
        (string, string) LeadTrail()
        {
            int runs = match.LeadTrailRuns ?? 0;
            return runs == 0 ? ("SCORES", "LEVEL") : (runs > 0 ? "LEAD BY" : "TRAIL BY", Math.Abs(runs).ToString(CultureInfo.InvariantCulture));
        }
        (string, string) Needed() => ("RUNS NEEDED", Math.Max(0, match.RunsRequired ?? 0).ToString(CultureInfo.InvariantCulture));
        (string, string) Target() => ("TARGET", match.Target?.ToString(CultureInfo.InvariantCulture) ?? "—");
        int Slot(int count) => ((phase % count) + count) % count;
        if (match.Format == CricketFormat.LimitedOvers && match.CurrentInningsNumber > 1 && match.Target is not null)
        {
            // The chase owns this rail. Complementary context is an eight-second
            // mini-card, rather than replacing the requirement for 90 seconds.
            return Slot(2)==0 ? Needed() : Target();
        }
        if (match.CurrentInningsNumber > 1)
        {
            if (match.Target is not null)
                return Slot(6) switch { 0 or 2 or 4 => Needed(), 1 => Target(), 3 => LeadTrail(), _ => ("RUN RATE", Rate()) };
            var previous = match.OpponentPreviousInningsScore;
            return Slot(6) switch
            {
                0 or 2 or 4 => LeadTrail(),
                1 when previous is not null => (previous.Value.Label, previous.Value.Score),
                5 => Partnership(),
                _ => ("RUN RATE", Rate())
            };
        }
        if (match.Format == CricketFormat.LimitedOvers)
            return Slot(8) switch { 1 or 5 => Partnership(), 2 or 6 => OversLeft(), _ => ("RUN RATE", Rate()) };
        return Slot(4) == 3 ? Partnership() : ("RUN RATE", Rate());
    }
    internal static (string Label,string Value) SelectChaseMiniContext(CricketMatchManager match,int phase)
    {
        var innings=match.CurrentInnings;
        if(innings is null) return ("RUN RATE","0.00");
        return (((phase%4)+4)%4) switch
        {
            0 => ("REQUIRED RATE",match.RequiredRunRate?.ToString("0.00",CultureInfo.InvariantCulture) ?? "—"),
            1 => ("OVERS LEFT",$"{Math.Max(0,match.BallsRemaining ?? 0)/6}.{Math.Max(0,match.BallsRemaining ?? 0)%6}"),
            2 => ("RUN RATE",innings.RunRate.ToString("0.00",CultureInfo.InvariantCulture)),
            _ => ("PARTNERSHIP",$"{innings.PartnershipRuns}({innings.PartnershipBalls})")
        };
    }
}

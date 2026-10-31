using System.Globalization;

namespace Roche_Scoreboard.Models;

/// <summary>Actual chronological, completed innings; never reconstruct order from alternating teams.</summary>
public static class CricketInningsRecap
{
    public static CricketOverlayFact Build(CricketMatchManager match)
    {
        // Match replacement is scoped by the scheduler. Ordinary scoring in
        // the open innings does not make an unchanged historical recap new.
        string identity=match.CurrentInningsNumber+":"+match.DeliveryRevision+":recap";
        CricketOverlayFact Missing(string reason)=>new(CricketOverlayKind.InningsRecap,CricketOverlayGroup.Context,
            "INNINGS RECAP","",[],false,false,65,identity+":unavailable",reason);
        if(match.TossPending || match.CurrentInnings is null)return Missing("Complete an innings before showing its recap.");
        if(match.CurrentInningsNumber>4 || match.CurrentInningsNumber>match.TotalInnings || match.AllInnings.Count<match.CurrentInningsNumber)
            return Missing("The innings sequence needs round information before it can be recapped.");
        if(string.IsNullOrWhiteSpace(match.TeamAName) || string.IsNullOrWhiteSpace(match.TeamBName)
            || string.Equals(match.TeamAName,match.TeamBName,StringComparison.OrdinalIgnoreCase)
            || match.AllInnings.Take(match.CurrentInningsNumber).Any(i=>i.BattingTeamName!=match.TeamAName && i.BattingTeamName!=match.TeamBName))
            return Missing("Confirm the teams in the completed innings history before showing their innings order.");
        var metrics=new List<CricketOverlayMetric>();var ordinals=new Dictionary<string,int>(StringComparer.Ordinal);
        bool priorIncomplete=false;
        for(int i=0;i<match.CurrentInningsNumber;i++)
        {
            var innings=match.AllInnings[i];string team=innings.BattingTeamName;
            if(string.IsNullOrWhiteSpace(team))
            {priorIncomplete=true;continue;}
            int ordinal=ordinals.GetValueOrDefault(team)+1;ordinals[team]=ordinal;
            if(ordinal>2)return Missing("Separate round information is needed for more than two innings per team.");
            if(!innings.IsComplete || innings.TotalRuns<0 || innings.TotalWickets<0)
            {if(i<match.CurrentInningsNumber-1)priorIncomplete=true;continue;}
            bool current=i==match.CurrentInningsNumber-1;
            if(current && match.IsMatchFinalized)
            {
                // Older or externally supplied closures may mark an unfinished
                // innings complete. The original closure metadata still decides
                // whether it belongs in a completed-innings statistical recap.
                if(match.ClosureBefore is { } closure && closure.InningsNumber==match.CurrentInningsNumber)
                {if(!closure.IsComplete && !closure.NaturallyComplete)continue;}
                else if(match.ResultKind!=CricketMatchResultKind.ScoreBased && !innings.IsDeclared && !innings.IsAllOut)
                    continue;
            }
            string abbreviation=team==match.TeamAName?match.TeamAAbbr:team==match.TeamBName?match.TeamBAbbr:"";
            if(string.IsNullOrWhiteSpace(abbreviation) || string.Equals(match.TeamAAbbr,match.TeamBAbbr,StringComparison.OrdinalIgnoreCase))abbreviation=team;
            string label=(i+1).ToString(CultureInfo.InvariantCulture)+" · "+abbreviation.ToUpperInvariant()+" "+(ordinal==1?"1ST":"2ND");
            // Keep the app's Australian wickets/runs convention. A zero score
            // cannot tell us that an innings was forfeited or never started.
            string score=innings.TotalWickets.ToString(CultureInfo.InvariantCulture)+"/"+innings.TotalRuns.ToString(CultureInfo.InvariantCulture)
                +(innings.IsDeclared?" dec":"");
            metrics.Add(new(label,score));
        }
        if(metrics.Count==0)return Missing("No completed innings with a known team and score is available yet.");
        string detail=priorIncomplete?"RECORDED COMPLETED INNINGS":"IN CHRONOLOGICAL ORDER";
        string fingerprint=identity+":"+string.Join(";",metrics.Select(m=>m.Label+"="+m.Value));
        bool automatic=!match.IsMatchFinalized && !match.CurrentInnings.IsComplete && !priorIncomplete
            && match.BreakKind==CricketBreakKind.None && match.CurrentInnings.LegalBallsBowled<12;
        return new(CricketOverlayKind.InningsRecap,CricketOverlayGroup.Context,"INNINGS RECAP",detail,metrics,true,automatic,65,fingerprint);
    }
}

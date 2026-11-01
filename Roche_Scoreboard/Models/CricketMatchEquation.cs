using System.Globalization;

namespace Roche_Scoreboard.Models;

/// <summary>A statement of the configured match equation, never a win probability.
/// Unknown competition entitlements/returnability must not become an exact wicket claim.</summary>
public static class CricketMatchEquation
{
    public static CricketOverlayFact Build(CricketMatchManager match)
    {
        var innings=match.CurrentInnings;string identity=match.CurrentInningsNumber+":"+match.DeliveryRevision+":equation";
        CricketOverlayFact Missing(string reason)=>new(CricketOverlayKind.MatchState,CricketOverlayGroup.Context,
            "MATCH STATE","",[],false,false,60,identity+":unavailable",reason);
        if(match.TossPending || match.IsMatchFinalized || innings is null || innings.IsComplete || innings.IsAllOut)
            return Missing("A live match equation is available during an open innings.");
        if(string.IsNullOrWhiteSpace(match.TeamAName) || string.IsNullOrWhiteSpace(match.TeamBName)
            || string.Equals(match.TeamAName,match.TeamBName,StringComparison.OrdinalIgnoreCase)
            || match.AllInnings.Count!=match.CurrentInningsNumber || match.TotalInnings is <2 or >4)
            return Missing("The teams and innings entitlement must be known before showing a winning requirement.");
        var history=match.AllInnings;
        if(history.Any(i=>i.TotalRuns<0 || i.TotalWickets<0 || i.BattingTeamName!=match.TeamAName && i.BattingTeamName!=match.TeamBName)
            || history.Take(history.Count-1).Any(i=>!i.IsComplete))
            return Missing("The completed innings history needs reconciliation before showing a winning requirement.");
        string batting=innings.BattingTeamName,bowling=batting==match.TeamAName?match.TeamBName:match.TeamAName;
        if(innings.BowlingTeamName!=bowling || history.Count(i=>i.BattingTeamName==batting)>2 || history.Count(i=>i.BattingTeamName==bowling)>2)
            return Missing("The innings sequence does not identify a supported two-team match equation.");
        bool opponentHasBatted=history.Any(i=>i.BattingTeamName==bowling);
        if(!opponentHasBatted)return Missing("There is no opposing innings to compare yet.");
        bool limited=match.Format==CricketFormat.LimitedOvers;
        bool normalLimited=limited && match.TotalInnings==2 && history.Count==2;
        bool multi=!limited && match.Format is CricketFormat.MultiDay or CricketFormat.MultiInnings;
        bool finalInnings=match.CurrentInningsNumber==match.TotalInnings;
        bool canWinThisInnings=!innings.IgnoreTarget && (normalLimited || multi && finalInnings);
        int? target=canWinThisInnings?match.Target:null;
        if(canWinThisInnings && target is >0 && innings.TotalRuns>=target.Value)
            return Missing("The target has been reached; confirm the innings or match outcome.");
        if(limited && !innings.IgnoreOverLimit && match.CurrentOverLimit>0
            && innings.LegalBallsBowled>=(long)match.CurrentOverLimit*6)
            return Missing("The innings limit has been reached; confirm its outcome before showing a live equation.");

        long battingAggregate=history.Where(i=>i.BattingTeamName==batting).Sum(i=>(long)i.TotalRuns);
        long bowlingAggregate=history.Where(i=>i.BattingTeamName==bowling).Sum(i=>(long)i.TotalRuns);
        long lead=battingAggregate-bowlingAggregate;
        var metrics=new List<CricketOverlayMetric>();
        string Number(long value)=>value.ToString(CultureInfo.InvariantCulture);
        string Caption(string team)
        {
            string a=match.TeamAAbbr?.Trim()??"",b=match.TeamBAbbr?.Trim()??"";
            string abbreviation=team==match.TeamAName?a:b;
            return (!string.IsNullOrWhiteSpace(abbreviation) && !string.Equals(a,b,StringComparison.OrdinalIgnoreCase)?abbreviation:team).ToUpperInvariant();
        }
        long? needed=canWinThisInnings && target is >0 ? (long)target.Value-innings.TotalRuns : null;
        if(needed is >0)metrics.Add(new(Caption(batting)+" RUNS TO WIN",Number(needed.Value)));
        else metrics.Add(lead==0?new(Caption(batting)+" AGGREGATE","LEVEL")
            :new(Caption(batting)+(lead>0?" LEAD BY":" TRAIL BY"),Number(Math.Abs(lead))));

        // A third innings can be this team's last entitled innings. It is an
        // innings victory only while the fielding aggregate is strictly ahead.
        bool battingFinal=finalInnings || multi && match.TotalInnings==4 && history.Count(i=>i.BattingTeamName==batting)==2;
        bool fieldingWouldWin=!innings.IgnoreTarget && battingFinal && (normalLimited && target is >0
            ? innings.TotalRuns<(long)target.Value-1
            : multi && innings.TargetOverride is null && bowlingAggregate>battingAggregate);
        string? partialReason=null;
        if(fieldingWouldWin)
        {
            var roster=innings.BattingOrder;
            bool known=roster.Count>=2 && roster.Count(p=>p.IsOut)==innings.TotalWickets && !roster.Any(p=>p.IsRetired && !p.IsOut);
            int remaining=roster.Count-1-innings.TotalWickets;
            if(known && remaining>0)metrics.Add(new(Caption(bowling)+" WICKETS TO WIN",Number(remaining)));
            else partialReason="Exact wickets required are unavailable while batting eligibility or returnability is uncertain.";
        }
        string detail=needed is >0?"TARGET "+Number(target!.Value):"AGGREGATE SCORES";
        if(normalLimited && !innings.IgnoreOverLimit && match.CurrentOverLimit>0)
        {
            long remaining=(long)match.CurrentOverLimit*6-innings.LegalBallsBowled;
            if(remaining>=0)detail+=" · "+Number(remaining)+" BALLS LEFT";
        }
        string fingerprint=identity+":"+detail+":"+string.Join(";",metrics.Select(m=>m.Label+"="+m.Value));
        return new(CricketOverlayKind.MatchState,CricketOverlayGroup.Context,"MATCH STATE",detail,metrics,true,
            match.BreakKind==CricketBreakKind.None,needed is >0 || fieldingWouldWin?60:32,fingerprint,partialReason);
    }
}

using System.Globalization;

namespace Roche_Scoreboard.Models;

/// <summary>Current pair's recorded deliveries, not legal-over balls or summed batter BF.
/// ACS statistical rulings Q8 includes wides; its illegal-ball ruling requires a delivered ball.
/// Non-delivery run-outs/replacements are checkpoints and never add a ball.</summary>
public static class CricketPartnershipAnalysis
{
    public const string DeliveryConvention = "Balls include delivered wides and no-balls. Unbowled actions do not add a ball.";
    public static CricketOverlayFact Build(CricketMatchManager match)
    {
        var innings=match.CurrentInnings;
        string identity=match.CurrentInningsNumber+":"+match.DeliveryRevision+":partnership";
        CricketOverlayFact Missing(string reason)=>new(CricketOverlayKind.Partnership,CricketOverlayGroup.Context,
            "CURRENT PARTNERSHIP",innings?.BattingTeamName??"",[],false,false,35,identity+":unavailable",reason);
        if(match.TossPending || match.IsMatchFinalized || innings is null || innings.IsComplete)
            return Missing("Current partnership is available during an open innings.");
        int striker=innings.StrikerIndex,nonStriker=innings.NonStrikerIndex;
        bool Pair(int a,int b)=>(a==striker && b==nonStriker)||(a==nonStriker && b==striker);
        if(striker==nonStriker || striker<0 || nonStriker<0 || striker>=innings.BattingOrder.Count || nonStriker>=innings.BattingOrder.Count
            || innings.BattingOrder[striker].IsOut || innings.BattingOrder[nonStriker].IsOut
            || innings.BattingOrder[striker].IsRetired || innings.BattingOrder[nonStriker].IsRetired || innings.NeedsBatterSelection)
            return Missing("Choose two active batters to show their current partnership.");

        var deliveries=innings.Deliveries;
        int start=0,lastResetChange=-1,lastUnknownChange=-1,unknownOffset=-1;
        long baseRuns=0;
        bool knownReset=false;
        for(int i=0;i<deliveries.Count;i++)
        {
            if(deliveries[i].Type!=CricketDeliveryType.Wicket)continue;
            start=i+1;knownReset=true;
            baseRuns=deliveries[i].StateBefore is { } before ? (long)before.TotalRuns+deliveries[i].Runs : deliveries[i].TotalAfter;
        }
        for(int i=0;i<innings.ScorecardChanges.Count;i++)
        {
            var change=innings.ScorecardChanges[i];
            if(change.AfterDeliveryCount<0 || change.AfterDeliveryCount>deliveries.Count)
                return Missing("The current partnership's delivery history is incomplete.");
            bool reset=change.NonDeliveryWicket is not null || change.Reason=="Batter replacement";
            if(reset)
            {
                lastResetChange=i;
                if(change.AfterDeliveryCount>=start)
                {start=change.AfterDeliveryCount;baseRuns=change.Before.TotalRuns;knownReset=true;}
            }
            else if(!PreservesPartnership(change,innings))
            {lastUnknownChange=i;unknownOffset=Math.Max(unknownOffset,change.AfterDeliveryCount);}
        }
        // A later delivered wicket, or a later explicit non-ball replacement,
        // establishes a new stand after an unreconciled manual correction.
        if(lastUnknownChange>=0 && !(start>unknownOffset || lastResetChange>lastUnknownChange))
            return Missing("Partnership figures cannot be verified after the scorecard correction; they resume with the next known partnership.");
        if(innings.ScorecardAnchor>=0 && !innings.ScorecardChanges.Any(c=>c.AfterDeliveryCount==innings.ScorecardAnchor)
            && start<=innings.ScorecardAnchor)
            return Missing("The saved partnership history is incomplete.");

        var segment=deliveries.Skip(start).ToArray();
        if(segment.Any(d=>!Pair(d.StrikerIndex,d.NonStrikerIndex) || d.Runs<0 || !Enum.IsDefined(d.Type)))
            return Missing("The saved history does not identify this pair's complete partnership.");
        if(!knownReset)
        {
            // With no recorded reset, only an intact innings-from-zero ledger
            // can establish the start. Default legacy counters are not proof.
            if(deliveries.Count==0 && (innings.TotalRuns!=0 || innings.TotalWickets!=0 || innings.LegalBallsBowled!=0)
                || deliveries.Count>0 && deliveries[0].StateBefore is { } first
                    && (first.TotalRuns!=0 || first.TotalWickets!=0 || first.LegalBallsBowled!=0)
                || innings.TotalWickets!=0 || innings.LegalBallsBowled!=deliveries.Count(d=>d.IsCountedBall))
                return Missing("The saved history does not contain this partnership's start.");
        }
        long runs=segment.Sum(d=>(long)d.Runs);
        // This also catches a penalty/manual total with no attributable event.
        // Never attach unexplained team runs to the current pair or invent balls.
        if(runs>int.MaxValue || baseRuns+runs!=innings.TotalRuns)
            return Missing("The current partnership cannot be reconciled with the corrected team total.");
        string value=runs.ToString(CultureInfo.InvariantCulture)+" ("+segment.Length.ToString(CultureInfo.InvariantCulture)+")";
        return new(CricketOverlayKind.Partnership,CricketOverlayGroup.Context,"CURRENT PARTNERSHIP",innings.BattingTeamName,
            [new("RUNS (BALLS)",value)],true,segment.Length>=12 && match.BreakKind==CricketBreakKind.None,35,
            identity+$":{Math.Min(striker,nonStriker)}:{Math.Max(striker,nonStriker)}:{start}:"+value);
    }

    private static bool PreservesPartnership(CricketScorecardChange change,CricketInnings innings)
    {
        var before=change.Before;
        bool Same(int runs,int wickets,int balls,int striker,int nonStriker)=>before.TotalRuns==runs && before.TotalWickets==wickets
            && before.LegalBallsBowled==balls && ((before.StrikerIndex==striker && before.NonStrikerIndex==nonStriker)
                || (before.StrikerIndex==nonStriker && before.NonStrikerIndex==striker));
        // The next ball's snapshot captures the state after a correction. A
        // bowling-only/statistical correction need not destroy a known stand.
        if(change.AfterDeliveryCount<innings.Deliveries.Count)
        {
            var after=innings.Deliveries[change.AfterDeliveryCount].StateBefore;
            return after is not null && Same(after.TotalRuns,after.TotalWickets,after.LegalBallsBowled,after.StrikerIndex,after.NonStrikerIndex);
        }
        return Same(innings.TotalRuns,innings.TotalWickets,innings.LegalBallsBowled,innings.StrikerIndex,innings.NonStrikerIndex);
    }
}

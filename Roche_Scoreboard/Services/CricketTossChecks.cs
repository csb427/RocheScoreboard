using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

internal static class CricketTossChecks
{
    internal static Dictionary<string,bool> Run()
    {
        var checks=new Dictionary<string,bool>();
        foreach(bool home in new[] {false,true}) foreach(bool bat in new[] {false,true})
        {
            var match=CricketReliabilityChecks.CreateMatch(); match.TossPending=true;
            match.SetStriker(0); match.SetNonStriker(1); match.ConfirmBowler(0); match.RecordDelivery(6,CricketDeliveryType.Six);
            checks[$"toss-{home}-{bat}-blocks-players-and-scoring-before-decision"]=match.TotalRuns==0 && match.CurrentInnings!.Striker is null && match.CurrentInnings.CurrentBowler is null;
            checks[$"toss-{home}-{bat}-blocks-overlays-before-decision"]=CricketOverlayAnalysis.Build(match).All(f=>!f.Available && !f.Automatic);
            var restored=new CricketMatchManager(); restored.LoadState(match.ToState());
            checks[$"toss-{home}-{bat}-pending-state-survives-recovery"]=restored.TossPending;
            bool confirmed=match.CompleteToss(home,bat);
            checks[$"toss-{home}-{bat}-selects-correct-batting-side"]=confirmed && !match.TossPending && match.TeamABatsFirst==(home==bat)
                && match.CurrentInnings!.BattingTeamName==(home==bat ? match.TeamAName : match.TeamBName);
            checks[$"toss-{home}-{bat}-does-not-select-players-or-accept-double-confirm"]=match.CurrentInnings!.Striker is null && match.CurrentInnings.CurrentBowler is null && !match.CompleteToss(!home,!bat);
            restored.LoadState(match.ToState());
            checks[$"toss-{home}-{bat}-confirmed-state-survives-recovery"]=!restored.TossPending && restored.TossWinner==match.TossWinner && restored.TeamABatsFirst==match.TeamABatsFirst;
        }
        var legacy=CricketReliabilityChecks.CreateMatch(true); legacy.RecordDelivery(4,CricketDeliveryType.Four);
        var state=legacy.ToState(); state.TossPending=true;
        var recovered=new CricketMatchManager(); recovered.LoadState(state);
        checks["toss-recovery-does-not-lock-an-existing-scored-match"]=!recovered.TossPending && recovered.TotalRuns==4;
        var fact=new CricketOverlayFact(CricketOverlayKind.LastOver,CricketOverlayGroup.Review,"LAST OVER","Team",[new("RUNS","10")],true,true,38,"over-1");
        var scheduler=new CricketOverlayScheduler(); scheduler.Observe(legacy,0);
        checks["scheduler-does-not-show-initial-overlays-immediately"]=scheduler.Select([fact],10)==null && scheduler.Select([fact],19)!=null;
        scheduler.MarkShown(fact,20);
        var other=fact with { Kind=CricketOverlayKind.Projection,Group=CricketOverlayGroup.Trajectory,Fingerprint="projection" };
        checks["scheduler-enforces-long-quiet-gap-between-automatic-overlays"]=scheduler.Select([other],100)==null && scheduler.Select([other],131)!=null;
        scheduler.MarkShown(other,131);
        var third=fact with { Kind=CricketOverlayKind.Extras,Group=CricketOverlayGroup.Context,Fingerprint="extras" };
        checks["scheduler-limits-automatic-overlay-budget-to-two-per-five-minutes"]=scheduler.Select([third],250)==null && scheduler.Select([third],321)!=null;
        var extras=CricketReliabilityChecks.CreateMatch(true);
        extras.RecordDelivery(3,CricketDeliveryType.Wide);
        extras.RecordDelivery(5,CricketDeliveryType.NoBall);
        extras.RecordDelivery(3,CricketDeliveryType.NoBall,noBallRuns:CricketNoBallRuns.LegByes);
        extras.RecordDelivery(2,CricketDeliveryType.Bye);
        for(int i=0;i<4;i++) extras.RecordDelivery(0,CricketDeliveryType.Dot);
        extras.RecordDelivery(0,CricketDeliveryType.Wicket);
        var over=CricketOverlayAnalysis.Build(extras).Single(f=>f.Kind==CricketOverlayKind.LastOver);
        checks["last-over-shows-runs-wickets-and-correct-extras-including-no-ball-splits"]=over.Metrics.Count==3
            && over.Metrics[0].Value=="13" && over.Metrics[1].Value=="1" && over.Metrics[2].Value=="9";
        return checks;
    }
}

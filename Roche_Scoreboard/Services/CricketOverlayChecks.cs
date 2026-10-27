using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

internal static class CricketOverlayChecks
{
    internal static Dictionary<string,bool> Run()
    {
        var checks = new Dictionary<string,bool>();
        CricketOverlayFact Get(CricketMatchManager match, CricketOverlayKind kind) => CricketOverlayAnalysis.Build(match).Single(f => f.Kind == kind);
        void Score(CricketMatchManager match, int runs, CricketDeliveryType type)
        {
            var innings = match.CurrentInnings!;
            if (innings.Striker is null) match.SetStriker(0);
            if (innings.NonStriker is null) match.SetNonStriker(1);
            if (innings.NeedsBowlerSelection) match.ConfirmBowler(match.SuggestedNextBowler());
            if (innings.NeedsBatterSelection)
            {
                int player = innings.BattingOrder.FindIndex(p => !p.IsOut && p != innings.Striker && p != innings.NonStriker);
                match.SelectNewBatter(player);
            }
            match.RecordDelivery(runs,type);
        }
        var match = CricketReliabilityChecks.CreateMatch(true);
        checks["overlay-all-families-have-defined-resting-states"] = CricketOverlayAnalysis.Build(match).Select(f => f.Kind).Distinct().Count() == Enum.GetValues<CricketOverlayKind>().Length;
        checks["overlay-resting-drought-zero-and-no-fabricated-toss"] = Get(match,CricketOverlayKind.SinceScore).Metrics[0].Value == "0" && !Get(match,CricketOverlayKind.Toss).Available;
        match.TossWinner = match.TeamBName; match.TossWinnerElectedToBat = false;
        var restored = new CricketMatchManager(); restored.LoadState(match.ToState());
        checks["overlay-toss-survives-save-and-restore"] = restored.TossWinner == match.TeamBName && !restored.TossWinnerElectedToBat;
        checks["overlay-toss-prominent-only-at-start-of-game"] = Get(match,CricketOverlayKind.Toss) is { Automatic: true, Priority: 75 };
        Score(match,4,CricketDeliveryType.Four);
        for (int i=0;i<3;i++) Score(match,0,CricketDeliveryType.Dot);
        Score(match,1,CricketDeliveryType.Wide);
        checks["overlay-wide-resets-score-drought-not-boundary-or-legal-count"] = Get(match,CricketOverlayKind.SinceScore).Metrics[0].Value == "0"
            && Get(match,CricketOverlayKind.SinceBoundary).Metrics[0].Value == "3";
        Score(match,0,CricketDeliveryType.Dot); Score(match,0,CricketDeliveryType.Dot);
        checks["overlay-last-over-includes-extra-deliveries"] = Get(match,CricketOverlayKind.LastOver).Metrics[0].Value == "5" && Get(match,CricketOverlayKind.LastOver).Automatic;
        var eventFact = Get(match,CricketOverlayKind.LastOver);
        var scheduler = new CricketOverlayScheduler(); scheduler.Observe(match,0);
        checks["overlay-scheduler-allows-first-completed-over-after-quiet-time"] = scheduler.Select([eventFact],20) != null;
        scheduler.MarkShown(eventFact,20);
        checks["overlay-scheduler-does-not-repeat-same-completed-over"] = scheduler.Select([eventFact],500) == null;
        scheduler.AutomaticEnabled=false;
        checks["overlay-scheduler-toggle-disables-automatic-selection"] = scheduler.Select(CricketOverlayAnalysis.Build(match),1000) == null;
        var revisionBall = match.CurrentInnings!.Deliveries[0];
        var edit = CricketMatchManager.DescribeDelivery(match.CurrentInnings,revisionBall) with { Type = CricketDeliveryType.Dot, Runs = 0 };
        bool edited = match.TryEditDelivery(revisionBall,edit,out _);
        scheduler.AutomaticEnabled=true; scheduler.Observe(match,1000);
        checks["overlay-correction-rebuilds-last-over-and-adds-quiet-period"] = edited && Get(match,CricketOverlayKind.LastOver).Metrics[0].Value == "1" && scheduler.Select(CricketOverlayAnalysis.Build(match),1001) == null;
        match.UndoLastDelivery();
        checks["overlay-undo-removes-no-longer-completed-last-over"] = Get(match,CricketOverlayKind.LastOver).Detail == "NO COMPLETED OVER";
        match = CricketReliabilityChecks.CreateMatch(true);
        match.RecordDelivery(0,CricketDeliveryType.Wicket);
        var dismissal = Get(match,CricketOverlayKind.LastWicket);
        checks["overlay-last-wicket-includes-dismissed-batter-final-ball"] = dismissal.Metrics[0].Value == "0" && dismissal.Metrics[1].Value == "1";
        match = CricketReliabilityChecks.CreateMatch(true);
        match.RecordDelivery(4, CricketDeliveryType.Four);
        match.RecordDelivery(3, CricketDeliveryType.Wicket, dismissal: DismissalType.RunOut, dismissedBatterIndex: 0,
            wicketRuns: CricketWicketRuns.NoBall);
        var noBallRunOut = match.CurrentInnings!.Deliveries.Last();
        dismissal = Get(match, CricketOverlayKind.LastWicket);
        checks["overlay-no-ball-run-out-keeps-batter-credit-and-faced-ball-correct"] = !CricketOverlayAnalysis.IsLegal(noBallRunOut)
            && dismissal.Metrics[0].Value == "6" && dismissal.Metrics[1].Value == "2";
        for(int ball=0;ball<5;ball++) Score(match,0,CricketDeliveryType.Dot);
        var runOutOver = Get(match, CricketOverlayKind.LastOver);
        checks["overlay-last-over-keeps-run-out-no-ball-extras-and-wicket-together"] = runOutOver.Metrics.Single(metric=>metric.Label=="RUNS").Value=="7"
            && runOutOver.Metrics.Single(metric=>metric.Label=="WICKETS").Value=="1"
            && runOutOver.Metrics.Single(metric=>metric.Label=="EXTRAS").Value=="1";
        match = CricketReliabilityChecks.CreateMatch(true);
        match.RecordDelivery(2, CricketDeliveryType.Wicket, dismissal: DismissalType.RunOut, dismissedBatterIndex: 0,
            wicketRuns: CricketWicketRuns.Wide);
        dismissal = Get(match, CricketOverlayKind.LastWicket);
        checks["overlay-wide-run-out-does-not-add-batter-runs-or-balls-faced"] = dismissal.Metrics[0].Value=="0" && dismissal.Metrics[1].Value=="0"
            && !CricketOverlayAnalysis.IsLegal(match.CurrentInnings!.Deliveries.Last());
        match = CricketReliabilityChecks.CreateMatch(true);
        match.RecordDelivery(3, CricketDeliveryType.Wicket, noBallRuns: CricketNoBallRuns.Byes, dismissal: DismissalType.RunOut,
            dismissedBatterIndex: 0, wicketRuns: CricketWicketRuns.NoBall);
        dismissal = Get(match, CricketOverlayKind.LastWicket);
        checks["overlay-run-out-off-no-ball-byes-counts-faced-ball-without-batter-credit"] = dismissal.Metrics[0].Value=="0" && dismissal.Metrics[1].Value=="1";
        match = CricketReliabilityChecks.CreateMatch(true,overs:50);
        for (int i=0;i<30;i++) Score(match, i%6 == 0 ? 6 : 0,i%6 == 0 ? CricketDeliveryType.Six : CricketDeliveryType.Dot);
        var projection = Get(match,CricketOverlayKind.Projection);
        checks["overlay-projection-uses-remaining-legal-balls-not-decimal-overs"] = projection.Metrics.Single(metric=>metric.Label.StartsWith("CURRENT ")).Value == "300";
        match.EndCurrentInnings(); match.StartNextInnings();
        checks["overlay-toss-never-shows-in-second-innings"] = !Get(match,CricketOverlayKind.Toss).Available;
        for(int i=0;i<3;i++) Score(match,0,CricketDeliveryType.Dot);
        var comparison = Get(match,CricketOverlayKind.Comparison);
        checks["overlay-chase-comparison-uses-exact-legal-ball-point"] = comparison.Available && comparison.Title.Contains("0.3") && comparison.Metrics[0].Value == "0/6";
        match = CricketReliabilityChecks.CreateMatch(true,overs:50);
        for(int i=0;i<60;i++) Score(match,i<5 ? 0 : i==10 ? 4 : 0,i<5 ? CricketDeliveryType.Wicket : i==10 ? CricketDeliveryType.Four : CricketDeliveryType.Dot);
        var window = Get(match,CricketOverlayKind.ScoringWindow);
        checks["overlay-window-detects-five-wickets-for-four-runs-in-ten-overs"] = window.Automatic && window.Available && window.Metrics[1].Value == "5";
        match.Format=CricketFormat.MultiDay;
        checks["overlay-projections-and-comparisons-disabled-for-multiday"] = !Get(match,CricketOverlayKind.Projection).Available && !Get(match,CricketOverlayKind.Comparison).Available;
        bool randomValid = true; var random = new Random(41203);
        for (int step=0;step<2000;step++)
        {
            if (match.CurrentInnings!.IsComplete) match=CricketReliabilityChecks.CreateMatch(true,overs:50);
            if(random.Next(6)==0 && match.CurrentInnings!.Deliveries.Count>0) match.UndoLastDelivery();
            else { int runs=random.Next(7); Score(match,runs,runs==4 ? CricketDeliveryType.Four : runs==6 ? CricketDeliveryType.Six : runs==0 ? CricketDeliveryType.Dot : CricketDeliveryType.Runs); }
            var facts=CricketOverlayAnalysis.Build(match);
            randomValid &= facts.Count==Enum.GetValues<CricketOverlayKind>().Length && facts.Select(f=>f.Kind).Distinct().Count()==Enum.GetValues<CricketOverlayKind>().Length
                && facts.All(f=>f.Metrics.Count>0 && f.Metrics.All(m=>!string.IsNullOrEmpty(m.Value)));
        }
        checks["overlay-facts-survive-2000-random-score-undo-operations"] = randomValid;
        return checks;
    }
}

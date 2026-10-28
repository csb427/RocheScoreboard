using System.Windows;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;

namespace Roche_Scoreboard.Services;

internal static class CricketOutputChecks
{
    internal static Dictionary<string,bool> Run()
    {
        var checks=new Dictionary<string,bool>();
        var blueRed=CricketBroadcastStyle.Gradient("#0033CC","#CC0000");
        var blueGold=CricketBroadcastStyle.Gradient("#0033CC","#FFCC00");
        checks["cricket-gradients-keep-primary-hue-independent-of-secondary"]=blueRed.GradientStops.Select(stop=>stop.Color).SequenceEqual(blueGold.GradientStops.Select(stop=>stop.Color)) && blueRed.GradientStops.All(stop=>stop.Color.R==0 && stop.Color.B>stop.Color.G);
        checks["invalid-primary-falls-back-to-secondary-without-mixing-colours"]=CricketBroadcastStyle.Gradient("invalid","#0033CC").GradientStops.Select(stop=>stop.Color).SequenceEqual(blueRed.GradientStops.Select(stop=>stop.Color));
        checks["pale-primary-stays-readable-through-white-crest-wash"]=CricketBroadcastStyle.Gradient("#FFFFFF","#FF0000").GradientStops.All(stop=>
        {
            var c=stop.Color;
            var composite=System.Windows.Media.Color.FromRgb((byte)(c.R*.78+255*.22),(byte)(c.G*.78+255*.22),(byte)(c.B*.78+255*.22));
            return ContrastHelper.ContrastRatio(Colors.White,composite)>=4.5;
        });
        var drawing=new GeometryDrawing(System.Windows.Media.Brushes.White,null,new RectangleGeometry(new Rect(0,0,200,400)));
        drawing.Freeze(); var image=new DrawingImage(drawing); image.Freeze();
        var logo=CricketBroadcastStyle.CenterLogo(image,.18)!;
        checks["background-crest-brush-is-centre-cropped-and-frozen"]=logo.AlignmentX==AlignmentX.Center && logo.AlignmentY==AlignmentY.Center && logo.Stretch==Stretch.UniformToFill && logo.IsFrozen && logo.Opacity==.18;
        checks["missing-crest-has-a-plain-background-fallback"]=CricketBroadcastStyle.CenterLogo(null) is null;
        checks["detailed-cricket-overlays-allow-eighteen-seconds-for-reading"]=new[] {CricketOverlayKind.LastWicket,CricketOverlayKind.Toss,CricketOverlayKind.Projection}.All(kind=>CricketOverlayHost.DisplaySeconds(kind)==18)
            && CricketOverlayHost.DisplaySeconds(CricketOverlayKind.Extras)==16 && CricketOverlayHost.DisplaySeconds(CricketOverlayKind.SinceScore)==14;
        var match=CricketReliabilityChecks.CreateMatch(true);
        var scheduler=new CricketOverlayScheduler(); scheduler.Observe(match,0);
        var manual=new CricketOverlayFact(CricketOverlayKind.LastOver,CricketOverlayGroup.Review,"LAST OVER","",[new("RUNS","10")],true,true,38,"over-1");
        var automatic=manual with {Kind=CricketOverlayKind.Projection,Group=CricketOverlayGroup.Trajectory,Fingerprint="projected"};
        var next=manual with {Kind=CricketOverlayKind.Extras,Group=CricketOverlayGroup.Context,Fingerprint="extras"};
        scheduler.MarkShown(manual,20,false);
        bool quiet=scheduler.Select([automatic],100) is null;
        scheduler.MarkShown(automatic,131);
        checks["manual-overlay-keeps-quiet-spacing-without-consuming-automatic-airtime-budget"]=quiet && scheduler.Select([next],250) is not null;
        match.SetBreak(CricketBreakKind.Rain); scheduler.Observe(match,260);
        checks["automatic-overlays-cannot-start-during-a-rain-break"]=scheduler.Select([next],500) is null;
        match.SetBreak(CricketBreakKind.None); scheduler.Observe(match,510);
        checks["automatic-overlays-resume-only-after-a-fresh-quiet-period"]=scheduler.Select([next],520) is null && scheduler.Select([next],531) is not null;
        var first=CricketReliabilityChecks.CreateMatch(true,20);
        var firstContexts=Enumerable.Range(0,8).Select(index=>CricketScorebugControl.SelectRailContext(first,index)).ToArray();
        checks["first-innings-rail-prioritizes-run-rate-without-losing-overs-left"]=firstContexts.Count(context=>context.Label=="RUN RATE")==4
            && firstContexts.Count(context=>context.Label=="OVERS LEFT")==2 && firstContexts.Count(context=>context.Label=="PARTNERSHIP")==2;
        first.RecordDelivery(4,CricketDeliveryType.Four); first.EndCurrentInnings(); first.StartNextInnings();
        var chase=Enumerable.Range(0,9).Select(index=>CricketScorebugControl.SelectRailContext(first,index)).ToArray();
        checks["chase-rail-keeps-target-and-runs-needed-dominant-between-brief-mini-cards"]=chase.All(context=>context.Label is "TARGET" or "RUNS NEEDED")
            && chase[0].Label=="RUNS NEEDED" && chase[0].Value=="5";
        checks["chase-mini-cards-retain-required-rate-overs-left-rate-and-partnership"]=Enumerable.Range(0,4).Select(index=>CricketScorebugControl.SelectChaseMiniContext(first,index).Label)
            .SequenceEqual(new[] {"REQUIRED RATE","OVERS LEFT","RUN RATE","PARTNERSHIP"});
        var review=CricketReliabilityChecks.CreateMatch(true,20); review.RecordDelivery(0,CricketDeliveryType.Wicket);
        var delayed=new CricketOverlayScheduler(); delayed.Observe(review,10);
        var wicket=CricketOverlayAnalysis.Build(review).Single(fact=>fact.Kind==CricketOverlayKind.LastWicket);
        checks["last-wicket-auto-waits-two-minutes-and-expires-after-five"]=delayed.Select([wicket],30) is null && delayed.Select([wicket],129.9) is null
            && delayed.Select([wicket],130) is not null && delayed.Select([wicket],310) is not null && delayed.Select([wicket],310.1) is null;
        review.UndoLastDelivery(); delayed.Observe(review,320);
        checks["undo-invalidates-a-previously-eligible-last-wicket-review"]=delayed.Select([wicket],500) is null;
        return checks;
    }
}

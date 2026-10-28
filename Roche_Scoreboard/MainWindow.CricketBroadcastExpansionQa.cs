using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Button=System.Windows.Controls.Button;
using TextBlock=System.Windows.Controls.TextBlock;
using Point=System.Windows.Point;

namespace Roche_Scoreboard;
public partial class MainWindow
{
    private async Task<Dictionary<string,bool>> RunCricketBroadcastExpansionChecksAsync(string directory)
    {
        var checks=new Dictionary<string,bool>();
        var match=CricketReliabilityChecks.CreateMatch(false,50);var innings=match.CurrentInnings!;
        match.SetStriker(7);match.SetNonStriker(4);match.ConfirmBowler(6);
        match.RecordDelivery(1,CricketDeliveryType.Wide);
        match.RecordDelivery(0,CricketDeliveryType.Wicket);match.SelectNewBatter(9);match.RecordDelivery(0,CricketDeliveryType.Dot);
        var order=CricketPresentationOrder.Batters(innings);
        checks["batting-presentation-orders-selected-openers-and-incoming-player"]=order.Take(3).Select(item=>item.Index).SequenceEqual(new[]{7,4,9});
        checks["presentation-sorting-preserves-indexed-squad"]=ReferenceEquals(innings.BattingOrder[7],order[0].Player);
        for(int ball=0;ball<6 && !innings.NeedsBowlerSelection;ball++)match.RecordDelivery(0,CricketDeliveryType.Dot);
        match.ConfirmBowler(2);match.RecordDelivery(1,CricketDeliveryType.NoBall);
        checks["bowling-presentation-orders-first-appearance-including-illegal-balls"]=CricketPresentationOrder.Bowlers(innings).Select(item=>item.Index).SequenceEqual(new[]{6,2});
        var restored=System.Text.Json.JsonSerializer.Deserialize<CricketInnings>(System.Text.Json.JsonSerializer.Serialize(innings))!;
        checks["appearance-order-survives-serialization"]=CricketPresentationOrder.Batters(restored).Take(3).Select(item=>item.Index).SequenceEqual(new[]{7,4,9});
        restored.BattingOrder[9].BattingEntryOrder=1;
        checks["delivery-history-prevents-later-batter-jumping-ahead-after-correction"]=CricketPresentationOrder.Batters(restored).Take(3).Select(item=>item.Index).SequenceEqual(new[]{7,4,9});
        foreach(var player in restored.BattingOrder)player.BattingEntryOrder=0;
        checks["older-saves-reconstruct-order-from-delivery-history"]=CricketPresentationOrder.Batters(restored).Take(3).Select(item=>item.Index).SequenceEqual(new[]{7,4,9});
        match.UndoLastDelivery();
        checks["undo-removes-unbowled-bowler-from-presentation"]=CricketPresentationOrder.Bowlers(match.CurrentInnings!).Count==1;
        bool replacement=match.TryReplaceBatter(true,3,CricketBatterReplacementReason.RetiredHurt,false,out _);
        checks["replacement-before-facing-retains-its-entry-position"]=replacement && CricketPresentationOrder.Batters(match.CurrentInnings!).Take(4).Select(item=>item.Index).SequenceEqual(new[]{7,4,9,3});
        var penaltyMilestone=CricketReliabilityChecks.CreateMatch(true,50);
        penaltyMilestone.CurrentInnings!.Striker!.Runs=49;penaltyMilestone.CurrentInnings.TotalRuns=49;
        penaltyMilestone.RecordDelivery(1,CricketDeliveryType.Wide);
        checks["penalty-runs-cannot-create-batter-milestones"]=CricketMilestone.FromDelivery(penaltyMilestone.CurrentInnings.Deliveries[^1],penaltyMilestone.CurrentInnings) is null;
        penaltyMilestone.RecordDelivery(2,CricketDeliveryType.NoBall,noBallRuns:CricketNoBallRuns.Bat);
        checks["no-ball-bat-runs-can-create-batter-milestones"]=CricketMilestone.FromDelivery(penaltyMilestone.CurrentInnings.Deliveries[^1],penaltyMilestone.CurrentInnings)?.Runs==50;
        var scheduler=new CricketOverlayScheduler();var boundaries=CricketReliabilityChecks.CreateMatch(true,50);
        scheduler.Observe(boundaries,0);boundaries.RecordDelivery(4,CricketDeliveryType.Four);scheduler.Observe(boundaries,30);
        var boundaryFacts=CricketOverlayAnalysis.Build(boundaries);
        checks["innings-boundaries-waits-before-automatic-display"]=scheduler.Select(boundaryFacts,37)==null;
        var chosen=scheduler.Select(boundaryFacts,38);
        checks["innings-boundaries-selected-eight-seconds-after-boundary"]=chosen?.Kind==CricketOverlayKind.Boundaries;
        if(chosen is not null)scheduler.MarkShown(chosen,38);
        checks["same-boundary-never-automatically-repeats"]=scheduler.Select(boundaryFacts,300)?.Kind!=CricketOverlayKind.Boundaries;
        boundaries.RecordDelivery(7,CricketDeliveryType.NoBall,noBallRuns:CricketNoBallRuns.Bat);
        var boundaryFact=CricketOverlayAnalysis.Build(boundaries).Single(fact=>fact.Kind==CricketOverlayKind.Boundaries);
        checks["boundary-tallies-include-no-ball-boundaries"]=boundaryFact.Metrics[0].Value=="1" && boundaryFact.Metrics[1].Value=="1";
        boundaries.RecordDelivery(4,CricketDeliveryType.Bye);
        checks["byes-do-not-count-as-batter-boundaries"]=CricketOverlayAnalysis.Build(boundaries).Single(fact=>fact.Kind==CricketOverlayKind.Boundaries).Metrics[0].Value=="1";

        var window=new Window{Owner=this,Width=386,Height=193,WindowStyle=WindowStyle.None,ShowInTaskbar=false};window.Show();
        async Task Wait(int delay=150){await Task.Delay(delay);window.UpdateLayout();}
        try
        {
            var summary=new CricketSummaryControl();window.Content=summary;
            while(innings.BattingOrder.Count<12)innings.BattingOrder.Add(new CricketPlayer{FirstName="Player",LastName=(innings.BattingOrder.Count+1).ToString(),BattingPosition=innings.BattingOrder.Count+1});
            summary.ShowBattingSummary(match);typeof(CricketSummaryControl).GetField("_pageIndex",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(summary,1);summary.RefreshBattingSummary(match);await Wait(350);
            foreach(int number in new[]{10,11,12})
            {
                var label=QaDescendants(summary.RowsPanel).OfType<TextBlock>().FirstOrDefault(text=>text.Text==number+".");
                checks[$"batting-order-{number}-has-room-for-all-digits"]=label is not null && label.ActualWidth>=label.DesiredSize.Width-.5 && label.DesiredSize.Width<80;
            }
            CaptureCricketQa(summary,Path.Combine(directory,"batting-order-10-11-12.png"));
            var chartMatch=CricketReliabilityChecks.CreateMatch(true,50);
            void Score(int runs,CricketDeliveryType type)
            {
                var inn=chartMatch.CurrentInnings!;
                if(inn.NeedsBatterSelection)chartMatch.SelectNewBatter(inn.BattingOrder.FindIndex(player=>!player.IsOut && !ReferenceEquals(player,inn.Striker) && !ReferenceEquals(player,inn.NonStriker)));
                if(inn.NeedsBowlerSelection)chartMatch.ConfirmBowler(chartMatch.SuggestedNextBowler());
                chartMatch.RecordDelivery(runs,type);
            }
            for(int ball=0;ball<150;ball++)Score(ball%9==0?4:ball%4==0?1:0,ball%9==0?CricketDeliveryType.Four:ball%4==0?CricketDeliveryType.Runs:CricketDeliveryType.Dot);
            chartMatch.EndCurrentInnings();chartMatch.StartNextInnings();chartMatch.SetStriker(0);chartMatch.SetNonStriker(1);chartMatch.ConfirmBowler(0);
            for(int ball=0;ball<45;ball++)Score(ball%13==0?0:ball%5==0?6:ball%3==0?1:0,ball%13==0?CricketDeliveryType.Wicket:ball%5==0?CricketDeliveryType.Six:ball%3==0?CricketDeliveryType.Runs:CricketDeliveryType.Dot);
            Score(1,CricketDeliveryType.Wide);
            var data=CricketChartData.Build(chartMatch);
            checks["charts-compare-two-innings-on-legal-ball-progress"]=data.Count==2 && data[1].Progress[^1].LegalBalls==45;
            checks["manhattan-includes-all-extras-and-partial-over"]=data[1].Overs.Sum(over=>over.Runs)==chartMatch.TotalRuns && data[1].Overs[^1].Over==8;
            checks["charts-retain-wicket-markers"]=data[1].Progress.Sum(point=>point.Wickets)==chartMatch.TotalWickets;
            foreach(var size in new[]{(386d,193d),(772d,386d),(286d,193d)})
            {
                window.Width=size.Item1;window.Height=size.Item2;
                foreach(string mode in new[]{"worm","manhattan"})
                {
                    summary.ShowChart(chartMatch,mode);await Wait(400);
                    var chart=summary.RowsPanel.Children.OfType<CricketChartVisual>().Single();
                    checks[$"{mode}-{size.Item1}-has-two-series-and-bounded-layout"]=chart.SeriesCount==2 && chart.ActualWidth>100 && chart.ActualHeight>100 && double.IsFinite(chart.ActualHeight);
                    CaptureCricketQa(summary,Path.Combine(directory,$"cricket-{mode}-{size.Item1}x{size.Item2}.png"));
                }
            }
            window.Width=386;window.Height=193;
            summary.ShowChart(chartMatch,"manhattan");typeof(CricketSummaryControl).GetField("_pageIndex",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(summary,1);summary.RefreshChart(chartMatch,"manhattan");await Wait(200);
            checks["long-manhattan-pages-without-dropping-late-overs"]=summary.RowsPanel.Children.OfType<CricketChartVisual>().Single().FirstOver==21;
            CaptureCricketQa(summary,Path.Combine(directory,"cricket-manhattan-second-page.png"));
            var router=new CricketControlPanel();router.SetMatch(chartMatch);router.SetSummaryControl(summary);
            foreach(string mode in new[]{"worm","manhattan"})
            {
                var button=(Button)router.FindName("Presentation"+mode);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait(400);
                checks[$"{mode}-operator-button-routes-to-the-public-presentation"]=summary.Visibility==Visibility.Visible && summary.HeaderText.Text==(mode=="worm"?"SCOREWORM":"MANHATTAN") && System.Windows.Automation.AutomationProperties.GetItemStatus(button)=="Selected";
                var previous=summary.RowsPanel.Children.OfType<CricketChartVisual>().Single();
                chartMatch.RecordDelivery(0,CricketDeliveryType.Dot);await Wait();
                checks[$"{mode}-refreshes-on-live-deliveries-without-reselection"]=!ReferenceEquals(previous,summary.RowsPanel.Children.OfType<CricketChartVisual>().Single());
            }
            router.ClearMatch();
            var comparison=CricketOverlayAnalysis.Build(chartMatch).Single(fact=>fact.Kind==CricketOverlayKind.Comparison);
            checks["comparison-only-contains-first-innings-score"]=comparison.Metrics.Count==1 && comparison.Metrics[0].Label=="WAS";
            using(var overlay=new CricketOverlayHost(chartMatch){AutomaticEnabled=false})
            {
                var chartBug=new CricketScorebugControl{EventAnimationsEnabled=false};chartBug.UpdateFromMatch(chartMatch);overlay.Scene.Children.Add(chartBug);window.Content=overlay;await Wait();
                overlay.Request(CricketOverlayKind.Comparison);await Wait(750);
                checks["comparison-themes-only-the-referenced-team"]=overlay.ThemedTeam==chartMatch.AllInnings[0].BattingTeamName && !overlay.UsesBothTeamBackgrounds;
                CaptureCricketQa(overlay,Path.Combine(directory,"comparison-large-opponent-only.png"));
                overlay.Request(CricketOverlayKind.Boundaries);await Wait(1400);CaptureCricketQa(overlay,Path.Combine(directory,"innings-boundaries-overlay.png"));chartBug.Dispose();
            }
            var panel=new CricketControlPanel();panel.SetMatch(CricketReliabilityChecks.CreateMatch(true,50));window.Content=panel;
            foreach(var size in new[]{(620d,620d),(420d,320d)})
            {
                window.Width=size.Item1;window.Height=size.Item2;await Wait();
                foreach(var type in new[]{CricketDeliveryType.Wide,CricketDeliveryType.NoBall,CricketDeliveryType.Bye,CricketDeliveryType.LegBye})
                {
                    InvokeCricketQa(panel,"OpenDeliveryDialog",type,null!);await Wait(200);
                    var entry=(CricketDeliveryDialog)panel.DeliveryEntryContent.Content;
                    var footer=entry.EntryFooter.TranslatePoint(new Point(),entry);
                    var preview=entry.PreviewPanel.TranslatePoint(new Point(),entry);var apply=entry.ApplyButton.TranslatePoint(new Point(),entry);
                    checks[$"extras-{type}-{size.Item1}-footer-fits-and-does-not-overlap"]=footer.Y>=0 && apply.Y>=preview.Y+entry.PreviewPanel.ActualHeight && apply.Y+entry.ApplyButton.ActualHeight<=entry.ActualHeight+1;
                    checks[$"extras-{type}-{size.Item1}-preview-and-shell-are-rounded"]=entry.PreviewPanel.CornerRadius.TopLeft==18 && panel.DeliveryEntryContent.Parent is Border{Clip:RectangleGeometry{RadiusX:26}};
                    CaptureCricketQa(panel,Path.Combine(directory,$"extras-{type}-{size.Item1}x{size.Item2}.png"));entry.Cancel();await Wait(100);
                }
            }
            window.Width=900;window.Height=700;await Wait();
            InvokeCricketQa(panel,"ShowBatterSelection","Select batter");await Wait();
            var batterScroll=QaDescendants(panel.BatterSelectionList).OfType<ScrollViewer>().First();batterScroll.ScrollToBottom();
            panel.BatterSelectionOverlay.Visibility=Visibility.Collapsed;InvokeCricketQa(panel,"ShowBatterSelection","Select batter");await Wait();
            checks["batter-selector-reopens-at-top"]=batterScroll.VerticalOffset<1;
            panel.BatterSelectionOverlay.Visibility=Visibility.Collapsed;
            InvokeCricketQa(panel,"ShowBowlerSelection",false);await Wait();var bowlerScroll=QaDescendants(panel.BowlerSelectionList).OfType<ScrollViewer>().First();bowlerScroll.ScrollToBottom();
            panel.BowlerSelectionOverlay.Visibility=Visibility.Collapsed;InvokeCricketQa(panel,"ShowBowlerSelection",false);await Wait();
            checks["bowler-selector-reopens-with-suggestion-visible"]=bowlerScroll.VerticalOffset<1 && panel.BowlerSelectionList.SelectedIndex==0;
            panel.ClearMatch();

            window.Width=386;window.Height=193;var milestoneMatch=CricketReliabilityChecks.CreateMatch(true,50);
            var bug=new CricketScorebugControl();window.Content=bug;bug.UpdateFromMatch(milestoneMatch);milestoneMatch.MatchChanged+=()=>bug.UpdateFromMatch(milestoneMatch);await Wait();
            var active=milestoneMatch.CurrentInnings!.Striker!;active.Runs=48;active.BallsFaced=30;milestoneMatch.CurrentInnings.TotalRuns=48;
            milestoneMatch.RecordDelivery(4,CricketDeliveryType.Four);await Wait(500);
            checks["boundary-celebration-precedes-fifty"]=bug.EventWord.Text=="FOUR";
            await Wait(3800);
            checks["fifty-shows-name-and-balls-faced"]=bug.EventShowing && bug.EventWord.Text=="50" && QaDescendants(bug.EventAccents).OfType<TextBlock>().Any(text=>Equals(text.Tag,"milestone-balls") && text.Text=="31 BALLS");
            CaptureCricketQa(bug,Path.Combine(directory,"fifty-celebration-386x193.png"));
            active=milestoneMatch.CurrentInnings.Striker!;active.Runs=98;active.BallsFaced=55;milestoneMatch.CurrentInnings.TotalRuns=98;
            milestoneMatch.RecordDelivery(2,CricketDeliveryType.Runs);await Wait(1500);
            checks["hundred-on-non-boundary-delivery-replaces-active-fifty"]=bug.EventShowing && bug.EventWord.Text=="100" && QaDescendants(bug.EventAccents).OfType<Grid>().Any(grid=>Equals(grid.Tag,"century-celebration"));
            CaptureCricketQa(bug,Path.Combine(directory,"hundred-celebration-386x193.png"));
            milestoneMatch.UndoLastDelivery();await Wait(100);
            checks["undo-cancels-stale-milestone"]=!bug.EventShowing && bug.ScoreRuns.Text==milestoneMatch.TotalRuns.ToString();
#if DEBUG
            int realRuns=milestoneMatch.TotalRuns,realBalls=milestoneMatch.CurrentInnings.Deliveries.Count;
            foreach(int milestone in new[]{50,100})
            {
                bug.PreviewMilestone(milestone);await Wait(100);
                checks[$"developer-{milestone}-preview-does-not-change-match-records"]=bug.EventShowing && bug.EventWord.Text==milestone.ToString() && milestoneMatch.TotalRuns==realRuns && milestoneMatch.CurrentInnings.Deliveries.Count==realBalls;
                bug.StopPreviewMoment();
            }
#endif
            bug.Dispose();
        }
        finally{window.Close();}
        return checks;
    }
}

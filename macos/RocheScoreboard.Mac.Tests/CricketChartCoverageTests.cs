using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;
namespace RocheScoreboard.Mac.Tests;

public sealed class CricketChartCoverageTests
{
    [Fact]
    public void FollowOnOrderRetainsMatchInningsNumbersAndTeamInningsOrdinals()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);match.Format=CricketFormat.MultiInnings;match.TotalInnings=4;
        for(int n=1;n<4;n++){match.EndCurrentInnings();match.StartNextInnings();}
        // Exercise an actual saved follow-on order without inferring team identity
        // from odd/even innings numbers.
        match.AllInnings[0].BattingTeamName=match.TeamAName;match.AllInnings[1].BattingTeamName=match.TeamBName;
        match.AllInnings[2].BattingTeamName=match.TeamBName;match.AllInnings[3].BattingTeamName=match.TeamAName;
        var series=CricketChartData.Build(match);
        Assert.Equal(new[]{1,4,2,3},series.Select(s=>s.Innings));Assert.Equal(new[]{1,2,1,2},series.Select(s=>s.TeamInnings));
        Assert.All(series.Take(2),s=>Assert.Equal(match.TeamAName,s.Name));Assert.All(series.Skip(2),s=>Assert.Equal(match.TeamBName,s.Name));
    }
    [Fact]
    public void ExtrasWithoutALegalBallKeepTheirRunsAtTheSameAxisPosition()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(3,CricketDeliveryType.Wide);
        var s=Assert.Single(CricketChartData.Build(match));
        Assert.Equal(0,s.LegalBalls);Assert.Equal(3,s.TotalRuns);Assert.Equal(new CricketProgressPoint(0,3,0),s.Progress.Last());
        Assert.Equal(3,Assert.Single(s.Overs).Runs);Assert.False(s.HistoryIncomplete);
    }
    [Fact]
    public void ARunOutBeforeDeliveryAppearsAsAWicketWithoutInventingABall()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);
        Assert.True(match.TryRecordNonStrikerRunOutBeforeDelivery(match.WorkflowToken,out string error),error);
        var s=Assert.Single(CricketChartData.Build(match));var wicket=Assert.Single(s.Progress,p=>p.Wickets==1);
        Assert.Equal(1,wicket.LegalBalls);Assert.Equal(4,wicket.Runs);Assert.Equal(1,s.LegalBalls);Assert.False(s.HistoryIncomplete);
    }
    [Fact]
    public void AScorecardAdjustmentIsExplicitAndDoesNotInventRunsPerOver()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);
        var draft=match.CreateScorecardDraft(1);draft.TotalRuns=9;draft.Batters[0].Runs=9;draft.Bowlers[0].RunsConceded=9;
        Assert.True(match.TryApplyScorecard(draft,out string error),error);
        var s=Assert.Single(CricketChartData.Build(match));Assert.True(s.HistoryIncomplete);Assert.Equal(9,s.TotalRuns);
        Assert.Equal(4,Assert.Single(s.Overs).Runs);Assert.True(s.Progress.Last().BreakBefore);Assert.Equal(9,s.Progress.Last().Runs);
    }
    [AvaloniaTheory]
    [InlineData(false,386,193)][InlineData(true,386,193)][InlineData(false,772,386)][InlineData(true,772,386)]
    public async Task FourInningsRemainDistinctAndManhattanPagesStayReadable(bool manhattan,int width,int height)
    {
        var series=Enumerable.Range(1,4).Select(i=>new CricketChartSeries(i<=2?"Home long team name":"Away long team name",i<=2?"HOM":"AWY",i<=2?"#DD2020":"#2040DD","#FFFFFF",i,
            Enumerable.Range(0,151-i).Select(b=>new CricketProgressPoint(b,b,b==10?1:0)).ToArray(),
            Enumerable.Range(1,25).Select(over=>new CricketOverPoint(over,6,over==2?1:0)).ToArray())
            {TeamInnings=(i-1)%2+1,TotalRuns=150-i,LegalBalls=150-i}).ToArray();
        var chart=new CricketChartVisual(series,manhattan,1){Width=960,Height=430};
        var w=new NativeWindow{Title="Chart Display",Width=width,Height=height,Content=new Viewbox{Child=chart}};w.Show();
        try
        {
            await Task.Delay(150);Dispatcher.UIThread.RunJobs();
            string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??"/tmp/roche-batch-4-captures";Directory.CreateDirectory(dir);
            using(var frame=w.CaptureRenderedFrame())frame!.Save(Path.Combine(dir,$"four-innings-{manhattan}-{width}x{height}.png"));
            Assert.Equal(4,chart.SeriesCount);Assert.Equal(13,chart.FirstOver);Assert.Equal(24,chart.LastOver);
            var last=new CricketChartVisual(series,true,2){Width=960,Height=430};((Viewbox)w.Content!).Child=last;await Task.Delay(100);Dispatcher.UIThread.RunJobs();
            using(var frame=w.CaptureRenderedFrame())Assert.NotNull(frame);
            Assert.Equal(25,last.FirstOver);Assert.Equal(25,last.LastOver);
        }
        finally{w.Close();}
    }
}

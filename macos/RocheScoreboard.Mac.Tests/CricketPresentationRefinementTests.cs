using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class CricketPresentationRefinementTests
{
    private static async Task Settle(int ms=200) { await Task.Delay(ms); Dispatcher.UIThread.RunJobs(); }
    private static void Capture(Window w,string name)
    {
        string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??"/tmp/roche-batch-4-captures";
        Directory.CreateDirectory(dir);using var frame=w.CaptureRenderedFrame();frame!.Save(Path.Combine(dir,name+".png"));
    }
    [AvaloniaFact]
    public async Task ExtrasAndBoundariesHaveDistinctCaptionsAndRouteDifferentFacts()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);match.RecordDelivery(2,CricketDeliveryType.Wide);
        var host=new CricketOverlayHost(match){MotionEnabled=false,AutomaticEnabled=false};
        var panel=new CricketControlPanel();panel.SetMatch(match);panel.SetOverlayHost(host);
        var window=new NativeWindow{Title="Overlay Display",Width=386,Height=193,Content=host};window.Show();
        try
        {
            await Settle();
            var buttons=panel.GetVisualDescendants().OfType<Button>().Where(b=>b.Tag is CricketOverlayKind).ToArray();
            // Controls are also discoverable before mounting through their registered panel.
            if(buttons.Length==0)buttons=panel.FindControl<StackPanel>("CricketOverlayButtons")!.GetLogicalDescendants().OfType<Button>().Where(b=>b.Tag is CricketOverlayKind).ToArray();
            var captions=buttons.ToDictionary(b=>(CricketOverlayKind)b.Tag!,b=>string.Join(" ",((StackPanel)b.Content!).Children.OfType<TextBlock>().Select(t=>t.Text)));
            Assert.Contains("INNINGS BOUNDARIES",captions[CricketOverlayKind.Boundaries]);
            Assert.Contains("EXTRAS",captions[CricketOverlayKind.Extras]);Assert.DoesNotContain("BOUNDARIES",captions[CricketOverlayKind.Extras]);
            buttons.Single(b=>Equals(b.Tag,CricketOverlayKind.Extras)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Settle();Capture(window,"extras-data");
            Assert.Equal("ON AIR · EXTRAS",host.Status);Assert.Contains(host.Facts.Single(f=>f.Kind==CricketOverlayKind.Extras).Metrics,m=>m.Label=="WIDES" && m.Value=="2");
            var wideLabel=host.GetVisualDescendants().OfType<TextBlock>().Single(t=>t.Text=="WIDES");
            Assert.Contains(wideLabel.GetVisualAncestors().OfType<Viewbox>().Single(view=>Equals(view.Tag,"overlay-metric")).GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="2");
            foreach(string label in new[]{"NO-BALLS","BYES","LEG-BYES"})Assert.Contains(host.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text==label);
            host.Cancel();buttons.Single(b=>Equals(b.Tag,CricketOverlayKind.Boundaries)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Settle(1000);Capture(window,"boundaries-data");Assert.Equal("ON AIR · INNINGS BOUNDARIES",host.Status);
            Assert.Contains(host.Facts.Single(f=>f.Kind==CricketOverlayKind.Boundaries).Metrics,m=>m.Label=="FOURS" && m.Value=="1");
            var fourLabel=host.GetVisualDescendants().OfType<TextBlock>().Single(t=>t.Text=="FOURS");
            Assert.Contains(fourLabel.GetVisualAncestors().OfType<Viewbox>().Single(view=>Equals(view.Tag,"overlay-metric")).GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="1");
        }
        finally{panel.ClearMatch();host.Dispose();window.Close();}
    }

    [AvaloniaFact]
    public async Task ChartsRejectManualAutomaticAndPendingFreeHitOverlaysAndRestoreCleanly()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);match.FreeHitsEnabled=true;
        for(int ball=0;ball<12;ball++)
        {if(match.CurrentInnings!.NeedsBowlerSelection)match.ConfirmBowler((ball/6)%2);match.RecordDelivery(0,CricketDeliveryType.Dot);}
        match.ConfirmBowler(0);match.RecordDelivery(5,CricketDeliveryType.Wide);
        var bug=new CricketScorebugControl();bug.UpdateFromMatch(match);
        var summary=new CricketSummaryControl(){IsVisible=false};
        var host=new CricketOverlayHost(match){MotionEnabled=false,AutomaticEnabled=false};host.Scene.Children.Add(bug);host.Scene.Children.Add(summary);
        var panel=new CricketControlPanel();panel.SetMatch(match);panel.SetScorebug(bug);panel.SetSummaryControl(summary);panel.SetOverlayHost(host);
        var w=new NativeWindow{Title="Cricket Display",Width=386,Height=193,Content=host};w.Show();
        try
        {
            await Settle();Assert.True(host.Request(CricketOverlayKind.Extras));
            panel.FindControl<Button>("Presentationworm")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle();
            Capture(w,"worm-overlay-gate");Assert.False(host.IsShowing);Assert.False(host.Request(CricketOverlayKind.Extras));
            foreach(string mode in new[]{"batting","bowling","summary","manhattan","worm"})
            {
                panel.FindControl<Button>("Presentation"+mode)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(host.Request(CricketOverlayKind.Boundaries));Assert.False(host.IsShowing);
            }
            Assert.Contains(host.Facts,f=>f.Available&&f.Automatic);
            Assert.False(bug.EventShowing);host.AutomaticEnabled=true;host.EvaluateAt(1000);Assert.False(host.IsShowing);
            match.RecordDelivery(1,CricketDeliveryType.NoBall);host.AutomaticEnabled=true;
            typeof(CricketOverlayHost).GetMethod("Tick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(host,[null,EventArgs.Empty]);
            await Settle();Assert.False(host.IsShowing);Assert.False(host.FreeHitAlarmShowing);
            panel.FindControl<Button>("Presentationmanhattan")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            panel.FindControl<Button>("Presentationscorebug")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(700);
            Assert.False(host.IsShowing);Assert.True(host.Request(CricketOverlayKind.Extras));
        }
        finally{panel.ClearMatch();host.Dispose();bug.Dispose();w.Close();}
    }

    [Fact]
    public void ChartsKeepEveryTeamInningsInsteadOfOnlyTheLatestPair()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);match.Format=CricketFormat.MultiInnings;match.TotalInnings=4;
        for(int i=1;i<=4;i++)
        {
            match.SetStriker(0);match.SetNonStriker(1);match.ConfirmBowler(0);
            for(int ball=0;ball<i;ball++)match.RecordDelivery(1,CricketDeliveryType.Runs);
            if(i<4){match.EndCurrentInnings();match.StartNextInnings();}
        }
        var series=CricketChartData.Build(match);
        Assert.Equal(4,series.Count);Assert.Equal(new[]{1,2,3,4},series.Select(s=>s.Innings).Order());
        Assert.Equal(2,series.Count(s=>s.Name==match.TeamAName));Assert.Equal(2,series.Count(s=>s.Name==match.TeamBName));
        foreach(var s in series)Assert.Equal(match.AllInnings.Single(i=>i.InningsNumber==s.Innings).TotalRuns,s.Progress.Last().Runs);
    }

    [AvaloniaFact]
    public async Task SwitchingBetweenSummaryPagesKeepsAnOpaqueCompositionDuringTheTransition()
    {
        bool? previous=OperatorMotion.AnimationOverride;OperatorMotion.AnimationOverride=true;
        var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);
        var summary=new CricketSummaryControl();var w=new NativeWindow{Title="Cricket Display",Width=386,Height=193,Content=summary};w.Show();
        try
        {
            summary.ShowChart(match,"worm");await Settle(650);Capture(w,"worm-before-switch");
            summary.ShowChart(match,"manhattan");await Settle(35);Capture(w,"chart-transition");
            Assert.InRange(summary.Opacity,.999,1);
            Assert.Equal("MANHATTAN",summary.FindControl<TextBlock>("HeaderText")!.Text);
            Assert.Contains(summary.GetVisualDescendants().OfType<Image>(),image=>Equals(image.Tag,"presentation-outgoing-frame"));
            summary.ShowBattingSummary(match);summary.ShowChart(match,"worm");await Settle(700);Assert.True(summary.IsVisible);Assert.InRange(summary.Opacity,.999,1);
            OperatorMotion.AnimationOverride=false;summary.HideSummary();Assert.False(summary.IsVisible);
            summary.ShowChart(match,"manhattan");Assert.True(summary.IsVisible);Assert.Equal(1,summary.Opacity);
        }
        finally{w.Close();OperatorMotion.AnimationOverride=previous;}
    }
}

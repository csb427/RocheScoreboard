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

public sealed class CricketSituationOverlayUiTests
{
    private static async Task Settle(int ms=100){await Task.Delay(ms);Dispatcher.UIThread.RunJobs();}
    private static object? Field(object owner,string name)=>owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner);
    private static void Set(object owner,string name,object? value)=>owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(owner,value);
    private static void ShowPending(CricketOverlayHost host)=>typeof(CricketOverlayHost).GetMethod("ShowPending",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(host,null);
    private static void BlockForMoment(CricketOverlayHost host)=>Set(host,"_momentReady",(Action)(()=>{}));
    private static CricketMatchManager Partnership()
    {var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);return match;}
    private static void Capture(Window window,string name)
    {
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??"/tmp/roche-situation-overlays";Directory.CreateDirectory(directory);
        using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(directory,name+".png"));
    }
    [AvaloniaTheory]
    [InlineData(1000,750)][InlineData(420,320)]
    public async Task NewOverlayControlsHaveDistinctDiscoverableCaptions(int width,int height)
    {
        var match=CricketSituationOverlayTests.Scores(("A",300,8),("B",250,10),("A",200,5),("B",100,8));
        using var host=new CricketOverlayHost(match){MotionEnabled=false,AutomaticEnabled=false};
        var panel=new CricketControlPanel();var window=new NativeWindow{Width=width,Height=height,Content=panel};
        try
        {
            panel.SetMatch(match);panel.SetOverlayHost(host);host.AutomaticEnabled=false;window.Show();
            panel.FindControl<TabControl>("MainTabControl")!.SelectedItem=panel.FindControl<TabItem>("OutputTab");await Settle(400);
            foreach(var (kind,caption) in new[]{(CricketOverlayKind.Partnership,"CURRENT PARTNERSHIP"),(CricketOverlayKind.MatchState,"MATCH STATE"),(CricketOverlayKind.InningsRecap,"INNINGS RECAP")})
            {
                var button=Assert.Single(panel.GetVisualDescendants().OfType<Button>(),b=>Equals(b.Tag,kind));
                Assert.Contains(caption,string.Join(" ",button.GetVisualDescendants().OfType<TextBlock>().Select(t=>t.Text)));
                Assert.Equal(caption,Avalonia.Automation.AutomationProperties.GetName(button));
                var symbol=Assert.Single(button.GetVisualDescendants().OfType<OperatorSymbol>());
                Assert.Equal(kind switch {CricketOverlayKind.Partnership=>"partnership",CricketOverlayKind.MatchState=>"equation",_=>"innings-recap"},symbol.Kind);
                Assert.True(ToolTip.GetShowOnDisabled(button));
            }
            Capture(window,"new-overlay-controls-"+width);
        }
        finally{panel.ClearOverlayHost(host);window.Close();}
    }
    [AvaloniaTheory]
    [InlineData("correction")][InlineData("target")][InlineData("ended")][InlineData("innings")][InlineData("presentation")]
    public void PendingEquationCannotSurviveAChangedMatchContext(string change)
    {
        var match=CricketSituationOverlayTests.Scores(("A",200,10),("B",100,5));match.Format=CricketFormat.LimitedOvers;match.TotalInnings=2;
        using var host=new CricketOverlayHost(match){MotionEnabled=false,AutomaticEnabled=false};BlockForMoment(host);
        Assert.True(host.Request(CricketOverlayKind.MatchState));Assert.NotNull(Field(host,"_pending"));string error;
        switch(change)
        {
            case "correction":Assert.True(match.TryApplyScorecard(match.CreateScorecardDraft(2),true,out error),error);break;
            case "target":Assert.True(match.TryApplyRainAdjustment(20,151,match.MatchConditionsToken,out error),error);break;
            case "ended":Assert.True(match.TrySetMatchOutcome(CricketMatchResultKind.Abandoned,"Rain",match.WorkflowToken,out error),error);break;
            case "innings":match.LoadState(CricketSituationOverlayTests.Scores(("A",200,10),("B",100,10),("A",5,0)).CapturePersistenceState());break;
            default:host.SetPresentation("manhattan");break;
        }
        Assert.Null(Field(host,"_pending"));Set(host,"_momentReady",null);ShowPending(host);Assert.False(host.IsShowing);
    }
    [AvaloniaFact]
    public void ManualPendingRequestSurvivesDisablingAutomaticOverlaysAndShowsAHandoffStatus()
    {
        using var host=new CricketOverlayHost(Partnership()){MotionEnabled=false};BlockForMoment(host);
        Assert.True(host.Request(CricketOverlayKind.Partnership));Assert.Equal("WAITING FOR SCORE ANIMATION",host.Status);
        host.AutomaticEnabled=false;Assert.NotNull(Field(host,"_pending"));
    }
    [AvaloniaFact]
    public void AvailabilityIsReevaluatedBeforeEnqueueAndImmediatelyBeforeDisplay()
    {
        var match=Partnership();using var host=new CricketOverlayHost(match){MotionEnabled=false,AutomaticEnabled=false};
        match.CurrentInnings!.TotalRuns+=5;Assert.False(host.Request(CricketOverlayKind.Partnership));
        match.CurrentInnings.TotalRuns-=5;typeof(CricketOverlayHost).GetMethod("Refresh",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(host,null);
        BlockForMoment(host);Assert.True(host.Request(CricketOverlayKind.Partnership));match.CurrentInnings.TotalRuns+=5;
        Set(host,"_momentReady",null);ShowPending(host);Assert.False(host.IsShowing);Assert.Null(Field(host,"_pending"));
    }
    [AvaloniaFact]
    public async Task ACorrectionHidesOldActiveContentWhileTheExistingExitHandoffCompletes()
    {
        var match=Partnership();using var host=new CricketOverlayHost(match){MotionEnabled=false,AutomaticEnabled=false};
        Assert.True(host.Request(CricketOverlayKind.Partnership));Assert.True(Assert.IsAssignableFrom<Control>(Field(host,"_shell")).IsVisible);
        Assert.True(match.TryApplyScorecard(match.CreateScorecardDraft(1),out string error),error);
        Assert.False(Assert.IsAssignableFrom<Control>(Field(host,"_shell")).IsVisible);
        await Settle(220);Assert.False(host.IsShowing);
    }
    [AvaloniaFact]
    public void OrdinaryScoringAndStrikeSwapsKeepTheManualRequestButUseTheLatestFigures()
    {
        var match=Partnership();using var host=new CricketOverlayHost(match){MotionEnabled=false,AutomaticEnabled=false};BlockForMoment(host);
        Assert.True(host.Request(CricketOverlayKind.Partnership));match.RecordDelivery(1,CricketDeliveryType.Runs);
        Assert.NotNull(Field(host,"_pending"));Set(host,"_momentReady",null);ShowPending(host);
        Assert.True(host.IsShowing);var active=Assert.IsType<CricketOverlayFact>(Field(host,"_active"));Assert.Contains(active.Metrics,m=>m.Value=="5 (2)");
        int generation=(int)Field(host,"_motionGeneration")!;Assert.True(host.Request(CricketOverlayKind.Partnership));Assert.Equal(generation,(int)Field(host,"_motionGeneration")!);
    }

    [AvaloniaTheory]
    [InlineData("output")][InlineData("correction")]
    public async Task ARealBoundaryHandoffCannotReplayAnObsoletePartnership(string interruption)
    {
        var match=Partnership();var bug=new CricketScorebugControl();bug.UpdateFromMatch(match);
        using var host=new CricketOverlayHost(match){MotionEnabled=false,AutomaticEnabled=false};host.Scene.Children.Add(bug);
        var window=new NativeWindow{Title="Cricket Display",Width=386,Height=193,Content=host};window.Show();
        try
        {
            await Settle(150);Assert.True(host.Request(CricketOverlayKind.Partnership));
            match.RecordDelivery(4,CricketDeliveryType.Four);Assert.True(bug.MomentAwaitingLayout);
            Assert.True(host.Request(CricketOverlayKind.Partnership));Assert.NotNull(Field(host,"_pending"));
            if(interruption=="output")host.SetOutputActive(false);
            else Assert.True(match.TryApplyScorecard(match.CreateScorecardDraft(1),out string error),error);
            await Settle(500);Assert.Null(Field(host,"_pending"));Assert.False(bug.EventShowing);Assert.False(bug.MomentAwaitingLayout);
            host.SetOutputActive(true);await Settle(200);Assert.False(host.IsShowing);
            Assert.Null(Field(host,"_momentReady"));
        }
        finally{window.Close();}
    }

    [AvaloniaTheory]
    [InlineData(CricketOverlayKind.MatchState)][InlineData(CricketOverlayKind.InningsRecap)]
    public async Task LongClubNamesKeepTheirCaptionSizeAndCannotConsumeTheNumericSpace(CricketOverlayKind kind)
    {
        var original=CricketSituationOverlayTests.Scores(("A",300,8),("B",250,10),("A",200,5),("B",100,8));
        var state=original.CapturePersistenceState();state.TeamAName="Northern District Community Cricket Association First Eleven";state.TeamBName="Southern District Community Cricket Association First Eleven";
        state.TeamAAbbr=state.TeamBAbbr="CLUB";
        foreach(var innings in state.AllInnings){bool first=innings.BattingTeamName=="A";innings.BattingTeamName=first?state.TeamAName:state.TeamBName;innings.BowlingTeamName=first?state.TeamBName:state.TeamAName;}
        var match=new CricketMatchManager();match.LoadState(state);
        using var host=new CricketOverlayHost(match){MotionEnabled=false,AutomaticEnabled=false};
        var window=new NativeWindow{Title="Cricket Display",Width=386,Height=193,Content=host};window.Show();
        try
        {
            await Settle();Assert.True(host.Request(kind));await Settle();Capture(window,"long-clubs-"+kind);
            var foreground=Assert.IsType<Grid>(Field(host,"_foreground"));
            var captions=foreground.GetVisualDescendants().OfType<TextBlock>().Where(t=>t.Text?.Contains("DISTRICT")==true).ToArray();
            Assert.NotEmpty(captions);
            foreach(var caption in captions){Assert.True(caption.FontSize>=23);Assert.Equal(Avalonia.Media.TextTrimming.CharacterEllipsis,caption.TextTrimming);Assert.True(caption.Bounds.Width<400);}
            if(kind==CricketOverlayKind.MatchState)
            {Assert.Contains(foreground.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="RUNS TO WIN");Assert.Contains(foreground.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="WICKETS TO WIN");}
            Assert.Contains(foreground.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text==(kind==CricketOverlayKind.MatchState?"151":"8/300"));
        }
        finally{window.Close();}
    }

    [AvaloniaTheory]
    [InlineData("partnership")][InlineData("equation")][InlineData("innings-recap")]
    public async Task SemanticOverlayIconsRetargetWithoutMovingTheHitTargetAndSettleWhenMotionIsReduced(string kind)
    {
        bool glass=OperatorAppearance.GlassEnabled,reduced=OperatorAppearance.ReduceMotion;
        var symbol=new OperatorSymbol{Kind=kind,Width=24,Height=24};var button=new Button{Width=72,Height=72,Content=symbol};
        var window=new NativeWindow{Width=420,Height=320,Content=button};
        try
        {
            OperatorAppearance.Configure(glass,false);window.Show();await Settle();var bounds=button.Bounds;
            for(int i=0;i<4;i++){button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(30);Capture(window,"icon-"+kind+"-"+i);}
            Assert.Equal(bounds,button.Bounds);Assert.True(symbol.IsAnimating);
            OperatorAppearance.Configure(glass,true);Assert.False(symbol.IsAnimating);Assert.Equal(bounds,button.Bounds);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.False(symbol.IsAnimating);
        }
        finally{window.Close();OperatorAppearance.Configure(glass,reduced);}
    }

    [AvaloniaTheory]
    [InlineData(CricketOverlayKind.Partnership,386,193)]
    [InlineData(CricketOverlayKind.Partnership,1280,640)]
    [InlineData(CricketOverlayKind.MatchState,386,193)]
    [InlineData(CricketOverlayKind.MatchState,1280,640)]
    [InlineData(CricketOverlayKind.InningsRecap,386,193)]
    [InlineData(CricketOverlayKind.InningsRecap,1280,640)]
    public async Task SituationOutputPreservesReadableFiguresAndChronology(CricketOverlayKind kind,int width,int height)
    {
        var match=kind==CricketOverlayKind.Partnership?Partnership():CricketSituationOverlayTests.Scores(("A",500,6),("B",200,10),("B",310,10),("A",10,0));
        if(kind!=CricketOverlayKind.Partnership)match.AllInnings[0].IsDeclared=true;
        var bug=new CricketScorebugControl();bug.UpdateFromMatch(match);
        using var host=new CricketOverlayHost(match){MotionEnabled=false,AutomaticEnabled=false};host.Scene.Children.Add(bug);
        var panel=new CricketControlPanel();panel.SetMatch(match);panel.SetOverlayHost(host);host.AutomaticEnabled=false;
        var window=new NativeWindow{Title="Cricket Display",Width=width,Height=height,Content=host};window.Show();
        try
        {
            await Settle(300);Assert.Equal(width,window.Bounds.Width,1);Assert.Equal(height,window.Bounds.Height,1);
            var button=Assert.Single(panel.GetLogicalDescendants().OfType<Button>(),b=>Equals(b.Tag,kind));Assert.True(button.IsEnabled);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(350);Capture(window,"situation-"+kind+"-"+width);
            Assert.Equal(kind,Assert.IsType<CricketOverlayFact>(Field(host,"_active")).Kind);
            var foreground=Assert.IsType<Grid>(Field(host,"_foreground"));
            Assert.Contains(foreground.GetVisualDescendants(),c=>Equals((c as Control)?.Tag,"situation-"+kind));
            var texts=foreground.GetVisualDescendants().OfType<TextBlock>().Select(t=>t.Text).ToArray();
            if(kind==CricketOverlayKind.Partnership){Assert.Contains("4 (1)",texts);Assert.Contains("CURRENT PARTNERSHIP",texts);Assert.Equal(match.BattingTeamName,host.ThemedTeam);}
            else
            {
                Assert.Null(host.ThemedTeam);
                if(kind==CricketOverlayKind.MatchState){Assert.Contains("1",texts);Assert.Contains("A RUNS TO WIN",texts);Assert.DoesNotContain(texts,t=>t?.Contains("WICKETS TO WIN")==true);}
                else {Assert.Contains("DECLARED",texts);Assert.Contains("6/500",texts);Assert.Contains("1 · A 1ST",texts);Assert.Contains("2 · B 1ST",texts);Assert.Contains("3 · B 2ND",texts);Assert.DoesNotContain("4 · A 2ND",texts);}
            }
            foreach(var value in foreground.GetVisualDescendants().OfType<Viewbox>().Where(v=>Equals(v.Tag,"situation-value")))
            {
                Assert.True(value.Bounds.Width>35 && value.Bounds.Height>12,$"Unusable numeric region: {value.Bounds}");
                Assert.True(value.IsEffectivelyVisible);
            }
        }
        finally{panel.ClearOverlayHost(host);window.Close();}
    }
}

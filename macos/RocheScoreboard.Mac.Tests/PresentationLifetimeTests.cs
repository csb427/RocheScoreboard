using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class PresentationLifetimeTests
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static object? Invoke(object owner,string name,params object[] args)=>owner.GetType().GetMethod(name,Private)!.Invoke(owner,args);
    private static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,Private)!.GetValue(owner)!;
    private static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,Private)!.SetValue(owner,value);
    private static async Task Settle(int ms=100){await Task.Delay(ms);Dispatcher.UIThread.RunJobs();}
    private static void Capture(Window window,string name)
    {
        string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??"/tmp/roche-batch-10-captures";Directory.CreateDirectory(dir);
        using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(dir,name+".png"));
    }
    private sealed class Fixture:IDisposable
    {
        private readonly App app=(App)Application.Current!;private readonly bool previous;
        private readonly Dictionary<string,byte[]?> files=new();
        internal Roche_Scoreboard.MainWindow Window{get;}
        internal Fixture()
        {
            previous=app.IsSelfTestMode;app.IsSelfTestMode=true;
            string workflow=(string)typeof(OperatorWorkflowPreferences).GetMethod("GetStoragePath",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[false])!;
            foreach(string path in new[]{Path.Combine(SelfTestStorage.DocumentsDirectory,"window_layout.json"),workflow})files[path]=File.Exists(path)?File.ReadAllBytes(path):null;
            Window=new();Window.Show();
        }
        internal async Task Ready(string mode="afl")
        {
            await Settle(180);
            Window.FindControl<SportSelectionScreen>("SportSelectionPanel")!.IsVisible=false;
            if(mode=="afl")Invoke(Window,"FinalizeAflSetup",new SetupResult{HomeName="Home",AwayName="Away",WeatherLocation=""});
            else if(mode=="cricket"){var setup=new CricketSetupResult{TeamAName="First",TeamBName="Second",WeatherLocation=""};setup.FillEmptySquads();Invoke(Window,"FinalizeCricketSetup",setup,false);}
            else Invoke(Window,"EnterTrainingMode");
            await Settle(550);
            if(mode=="cricket")Invoke(Window,"EnableOperatorOutput");
        }
        internal NativeWindow Output(string mode="afl")=>Field<NativeWindow>(Window,mode=="afl"?"_displayWindow":mode=="cricket"?"_cricketDisplayWindow":"_trainingDisplayWindow");
        public void Dispose()
        {
            Window.Close();
            foreach(var (path,bytes) in files) {if(bytes is null)File.Delete(path);else{Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,bytes);}}
            app.IsSelfTestMode=previous;
        }
    }
    [AvaloniaTheory]
    [InlineData("afl")][InlineData("cricket")][InlineData("training")]
    public async Task ClosedOutputCannotAutomaticallyReopenBeforeExplicitActivation(string mode)
    {
        using var f=new Fixture();await f.Ready(mode);var output=f.Output(mode);Assert.True(output.IsVisible);
        output.Close();await Settle();Assert.True(Field<bool>(f.Window,"_outputActivationRequired"));
        Invoke(f.Window,mode=="afl"?"EnsureScoreboardWindow":mode=="cricket"?"EnsureCricketDisplayWindow":"EnsureTrainingDisplayWindow");
        Assert.False(f.Output(mode).IsVisible);
        Invoke(f.Window,"EnableOperatorOutput");Assert.True(f.Output(mode).IsVisible);
    }
    [AvaloniaFact]
    public async Task ClosingCricketWhileAnOverlayIsActiveRestoresTheReusableScorebugGeometry()
    {
        using var f=new Fixture();await f.Ready("cricket");var panel=f.Window.FindControl<CricketControlPanel>("CricketControlPanelView")!;
        var host=Field<CricketOverlayHost>(panel,"_overlayHost");host.MotionEnabled=false;host.AutomaticEnabled=false;
        Assert.True(host.Request(CricketOverlayKind.Extras));var bug=Field<CricketScorebugControl>(f.Window,"_cricketScorebug");
        Assert.True(Field<bool>(bug,"_overlayCompact"));f.Output("cricket").Close();
        Assert.False(Field<bool>(bug,"_overlayCompact"));
    }
    [AvaloniaFact]
    public async Task ClosingAflInvalidatesThePresentationTransitionGeneration()
    {
        using var f=new Fixture();await f.Ready();int before=Field<int>(f.Window,"_screenTransitionVersion");f.Output().Close();
        Assert.True(Field<int>(f.Window,"_screenTransitionVersion")>before);
    }
    [AvaloniaTheory]
    [InlineData("home")][InlineData("replace")]
    public async Task DelayedAutoOverlayCannotRunAfterItsPresentationLifetimeEnds(string boundary)
    {
        using var f=new Fixture();await f.Ready();Set(f.Window,"_autoOverlaysEnabled",true);
        int shown=0,canceled=0;DispatcherTimer? timer=null;
        var schedule=f.Window.GetType().GetMethods(Private).Single(m=>m.Name=="ScheduleAutoOverlay"&&m.GetParameters().Length==6);
        schedule.Invoke(f.Window,[new Action(()=>shown++),TimeSpan.FromMilliseconds(75),new Func<DispatcherTimer?>(()=>timer),new Action<DispatcherTimer?>(value=>timer=value),true,new Action(()=>canceled++)]);
        Invoke(f.Window,boundary=="home"?"GoHome":"RetireSessionForReplacement");await Settle(250);
        Assert.Equal(0,shown);Assert.Equal(1,canceled);
    }
    [AvaloniaFact]
    public async Task RetainedOldScorebugHiddenCallbackCannotDequeueTheNewDisplaysOverlay()
    {
        using var f=new Fixture();await f.Ready();Invoke(f.Window,"EnsureOverlayQueueWired");
        var old=Field<ScorebugControl>(f.Window,"_scorebug");var callback=Field<Action>(old,"OverlayHidden");
        f.Output().Close();Invoke(f.Window,"EnableOperatorOutput");
        Invoke(f.Window,"QueueOverlay",new Action(()=>{}),true,new Action(()=>{}));
        var queue=Field<System.Collections.ICollection>(f.Window,"_overlayQueue");Assert.Single(queue);
        callback();Assert.Single(queue);
    }
    [AvaloniaTheory]
    [InlineData("afl")][InlineData("cricket")][InlineData("training")]
    public async Task ClosingOutputRemembersItsLatestBoundsRatherThanOnlySavingOnAppExit(string mode)
    {
        using var f=new Fixture();string key=mode=="afl"?WindowLayoutStorage.AflDisplayKey:mode=="cricket"?WindowLayoutStorage.CricketDisplayKey:WindowLayoutStorage.TrainingDisplayKey;
        WindowLayoutStorage.Set(key,new(10,10,386,193));await f.Ready(mode);
        var output=f.Output(mode);output.Left=200;output.Top=140;output.Width=410;output.Height=207;await Settle(150);
        var expected=WindowLayoutStorage.Capture(output);output.Close();var saved=WindowLayoutStorage.Get(key);Assert.NotNull(saved);
        Assert.Equal(expected.Left,saved.Left);Assert.Equal(expected.Top,saved.Top);Assert.Equal(expected.Width,saved.Width);Assert.Equal(expected.Height,saved.Height);
    }
    [AvaloniaTheory]
    [InlineData("worm","_scoreworm","SwitchToScoreworm_Click")]
    [InlineData("stats","_statsScreen","SwitchToStatsScreen_Click")]
    [InlineData("summary","_breakScreen","SwitchToBreakScreen_Click")]
    public async Task ReopeningAflKeepsTheRequestedStaticPresentationAndCurrentMatch(string key,string field,string action)
    {
        using var f=new Fixture();await f.Ready();var match=Field<MatchManager>(f.Window,"_match");
        match.AddGoal(TeamSide.Home);match.AddBehind(TeamSide.Away);Invoke(f.Window,"ResetScoreAnimationQueue");
        string before=JsonSerializer.Serialize(match.ToState());Invoke(f.Window,action,f.Window,new RoutedEventArgs());await Settle(700);
        Assert.Equal(key,Field<string>(f.Window,"_activeScreenKey"));Assert.True(Field<Control>(f.Window,field).IsVisible);
        f.Output().Close();Invoke(f.Window,"EnableOperatorOutput");await Settle(700);
        Assert.Equal(key,Field<string>(f.Window,"_activeScreenKey"));Assert.True(Field<Control>(f.Window,field).IsVisible);
        Assert.False(Field<Control>(f.Window,"_scorebug").IsVisible);Assert.Equal(before,JsonSerializer.Serialize(match.ToState()));
    }
    [AvaloniaTheory]
    [InlineData("initial")][InlineData("poll")][InlineData("settle")]
    public async Task EveryDelayedOverlayPhaseCancelsOnceWithoutReplayingOnResume(string phase)
    {
        using var f=new Fixture();await f.Ready();Set(f.Window,"_autoOverlaysEnabled",true);
        int shown=0,canceled=0;DispatcherTimer? timer=null;
        var bug=Field<ScorebugControl>(f.Window,"_scorebug");if(phase!="initial")bug.LockScoreUpdate(true);
        var schedule=f.Window.GetType().GetMethods(Private).Single(m=>m.Name=="ScheduleAutoOverlay"&&m.GetParameters().Length==6);
        schedule.Invoke(f.Window,[new Action(()=>shown++),TimeSpan.FromMilliseconds(30),new Func<DispatcherTimer?>(()=>timer),new Action<DispatcherTimer?>(value=>timer=value),true,new Action(()=>canceled++)]);
        if(phase=="initial")await Settle(5);
        else
        {
            await Settle(70);Assert.NotNull(timer);Assert.Equal(TimeSpan.FromMilliseconds(200),timer.Interval);
            if(phase=="settle"){bug.CancelScoreAnimations();await Settle(230);Assert.NotNull(timer);Assert.Equal(TimeSpan.FromMilliseconds(360),timer.Interval);}
        }
        Invoke(f.Window,"GoHome");Assert.Null(timer);await Settle(650);
        Assert.Equal(0,shown);Assert.Equal(1,canceled);
        Assert.Empty((System.Collections.IDictionary)Field<object>(f.Window,"_scheduledOverlayCancellations"));
    }
    [AvaloniaFact]
    public async Task SupersedingAnOverlayCancelsOnlyTheOldRequestAndRunsTheNewOneOnce()
    {
        using var f=new Fixture();await f.Ready();Set(f.Window,"_autoOverlaysEnabled",true);
        int oldShown=0,oldCanceled=0,newShown=0,newCanceled=0;DispatcherTimer? timer=null;
        var schedule=f.Window.GetType().GetMethods(Private).Single(m=>m.Name=="ScheduleAutoOverlay"&&m.GetParameters().Length==6);
        schedule.Invoke(f.Window,[new Action(()=>oldShown++),TimeSpan.FromMilliseconds(150),new Func<DispatcherTimer?>(()=>timer),new Action<DispatcherTimer?>(value=>timer=value),true,new Action(()=>oldCanceled++)]);
        schedule.Invoke(f.Window,[new Action(()=>newShown++),TimeSpan.FromMilliseconds(30),new Func<DispatcherTimer?>(()=>timer),new Action<DispatcherTimer?>(value=>timer=value),true,new Action(()=>newCanceled++)]);
        await Settle(250);Assert.Equal(0,oldShown);Assert.Equal(1,oldCanceled);Assert.Equal(1,newShown);Assert.Equal(0,newCanceled);Assert.Null(timer);
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task RapidAflSwitchCloseAndReopenHasOneCurrentSurfaceAndFreshScores(bool reduced)
    {
        bool? previous=OperatorMotion.AnimationOverride;OperatorMotion.AnimationOverride=!reduced;
        using var f=new Fixture();
        try
        {
            await f.Ready();var match=Field<MatchManager>(f.Window,"_match");match.AddGoal(TeamSide.Home);match.AddBehind(TeamSide.Away);Invoke(f.Window,"ResetScoreAnimationQueue");
            string before=JsonSerializer.Serialize(match.ToState());
            foreach(string action in new[]{"SwitchToStatsScreen_Click","SwitchToScoreworm_Click","SwitchToBreakScreen_Click","SwitchToStatsScreen_Click"})
            {Invoke(f.Window,action,f.Window,new RoutedEventArgs());await Settle(25);}
            f.Output().Close();Invoke(f.Window,"EnableOperatorOutput");await Settle(800);
            Assert.True(Field<Control>(f.Window,"_statsScreen").IsVisible);Assert.False(Field<Control>(f.Window,"_retroCrtOverlay").IsVisible);
            Invoke(f.Window,"SwitchToScorebug_Click",f.Window,new RoutedEventArgs());await Settle(700);
            var bug=Field<ScorebugControl>(f.Window,"_scorebug");Assert.True(bug.IsVisible);
            Assert.Equal("6",bug.FindControl<TextBlock>("HomeTotalText")!.Text);Assert.Equal("1",bug.FindControl<TextBlock>("AwayTotalText")!.Text);
            foreach(string field in new[]{"_breakScreen","_statsScreen","_scoreworm","_quarterBreakScorebug","_weatherScreen"})Assert.False(Field<Control>(f.Window,field).IsVisible);
            Assert.Equal(before,JsonSerializer.Serialize(match.ToState()));Capture(f.Output(),"afl-rapid-reopen-"+reduced);
        }
        finally{OperatorMotion.AnimationOverride=previous;}
    }
    [AvaloniaFact]
    public async Task HiddenCricketOutputRejectsManualWorkAndDoesNotReplayAnOffAirFreeHitAlarm()
    {
        using var f=new Fixture();await f.Ready("cricket");
        var match=Field<CricketMatchManager>(f.Window,"_cricketMatch");match.LoadState(CricketReliabilityChecks.CreateMatch(true).CapturePersistenceState());match.FreeHitsEnabled=true;
        var panel=f.Window.FindControl<CricketControlPanel>("CricketControlPanelView")!;var host=Field<CricketOverlayHost>(panel,"_overlayHost");host.AutomaticEnabled=false;host.MotionEnabled=false;
        Assert.True(host.Request(CricketOverlayKind.Extras));Assert.True(host.Request(CricketOverlayKind.Boundaries));
        Invoke(f.Window,"GoHome");Assert.False(host.OutputActive);Assert.False(host.IsShowing);Assert.False(host.Request(CricketOverlayKind.Extras));
        match.RecordDelivery(1,CricketDeliveryType.NoBall);Assert.True(match.FreeHitPending);Assert.False(Field<bool>(host,"_pendingFreeHitAlarm"));
        Invoke(f.Window,"ResumeFromHome");Invoke(f.Window,"EnableOperatorOutput");await Settle(750);
        Assert.True(host.OutputActive);Assert.False(host.IsShowing);Assert.False(host.FreeHitAlarmShowing);Assert.True(match.FreeHitPending);
        Assert.True(host.Request(CricketOverlayKind.Extras));Capture(f.Output("cricket"),"cricket-clean-output-reactivation");
    }
    [AvaloniaFact]
    public async Task CancelingCloseKeepsTheOutputVisibleAndActivated()
    {
        using var f=new Fixture();await f.Ready();var output=f.Output();
        void Cancel(object? sender,WindowClosingEventArgs args)=>args.Cancel=true;
        output.Closing+=Cancel;output.Close();output.Closing-=Cancel;
        Assert.True(output.IsVisible);Assert.False(Field<bool>(f.Window,"_outputActivationRequired"));
    }
    [AvaloniaFact]
    public async Task CompactAflPresentationActionsStayReachableAfterScrollAndKeyboardClose()
    {
        using var f=new Fixture();await f.Ready();var preferences=Field<object>(f.Window,"_uxPreferences");var guided=preferences.GetType().GetProperty("GuidedOperatorMode")!;bool previous=(bool)guided.GetValue(preferences)!;
        try
        {
            guided.SetValue(preferences,false);Invoke(f.Window,"ApplyGuidedOperatorMode");f.Window.Width=420;f.Window.Height=320;
            f.Window.FindControl<TabControl>("MainTabControl")!.SelectedItem=f.Window.FindControl<TabItem>("OutputStudioTab");await Settle(600);
            foreach(string name in new[]{"PresCardScorebug","PresCardWorm","PresCardWindow"})
            {
                var action=f.Window.FindControl<Control>(name)!;
                f.Window.KeyPressQwerty(PhysicalKey.Tab,RawInputModifiers.None);
                Assert.True(action.Focus(NavigationMethod.Tab));
                f.Window.KeyReleaseQwerty(PhysicalKey.Tab,RawInputModifiers.None);await Settle(150);
                var point=action.TranslatePoint(default,f.Window)!.Value;
                Capture(f.Window,"compact-"+name);
                Assert.True(new Rect(f.Window.Bounds.Size).Contains(new Rect(point,action.Bounds.Size)),name+" must fit after scrolling: "+new Rect(point,action.Bounds.Size)+" within "+f.Window.Bounds);
            }
            Capture(f.Window,"compact-output-actions");f.Window.KeyPressQwerty(PhysicalKey.Space,RawInputModifiers.None);f.Window.KeyReleaseQwerty(PhysicalKey.Space,RawInputModifiers.None);await Settle();
            Assert.True(Field<bool>(f.Window,"_outputActivationRequired"));
        }
        finally{guided.SetValue(preferences,previous);}
    }
}

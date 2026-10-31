using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class AflStoppageMotionTests
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static object? Invoke(object owner,string name,params object?[] args)=>owner.GetType().GetMethod(name,Private)!.Invoke(owner,args);
    private static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,Private)!.GetValue(owner)!;
    private static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,Private)!.SetValue(owner,value);
    private static T Property<T>(object owner,string name)=>(T)owner.GetType().GetProperty(name,Private)!.GetValue(owner)!;
    private static async Task Settle(int ms=100){await Task.Delay(ms);Dispatcher.UIThread.RunJobs();}
    private sealed class Fixture:IDisposable
    {
        private readonly App app=(App)Application.Current!;
        private readonly bool oldMode,glass=OperatorAppearance.GlassEnabled,reduced=OperatorAppearance.ReduceMotion;
        private readonly OperatorMotionStyle style=OperatorAppearance.MotionStyle;
        private readonly byte[]? appearance=File.Exists(OperatorAppearance.StoragePath)?File.ReadAllBytes(OperatorAppearance.StoragePath):null;
        private readonly Dictionary<string,byte[]?> files=new();
        internal readonly Roche_Scoreboard.MainWindow Window;
        internal Fixture()
        {
            oldMode=app.IsSelfTestMode;app.IsSelfTestMode=true;
            string workflow=(string)typeof(OperatorWorkflowPreferences).GetMethod("GetStoragePath",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[false])!;
            foreach(string path in new[]{Path.Combine(SelfTestStorage.DocumentsDirectory,"window_layout.json"),workflow})files[path]=File.Exists(path)?File.ReadAllBytes(path):null;
            OperatorAppearance.Configure(glass,true);Window=new();Window.Show();
        }
        internal T Named<T>(string name) where T:Control=>Assert.IsAssignableFrom<T>(Window.FindControl<T>(name));
        internal async Task Ready()
        {
            await Settle(180);Named<SportSelectionScreen>("SportSelectionPanel").IsVisible=false;
            Invoke(Window,"FinalizeAflSetup",new SetupResult{HomeName="Home",AwayName="Away",WeatherLocation="",ClockMode=ClockMode.CountUp,ShowControlPanelCountdown=true});await Settle(600);
        }
        internal void Open()
        {
            var trigger=Named<Button>("ControlPanelCountdownEditButton");
            trigger.Focus(NavigationMethod.Tab);trigger.BringIntoView();Dispatcher.UIThread.RunJobs();
            trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        internal void Apply()=>Invoke(Window,"ControlPanelCountdownApplyButton_Click",Window,new RoutedEventArgs());
        public void Dispose()
        {
            Window.Close();app.IsSelfTestMode=oldMode;OperatorAppearance.Configure(glass,reduced);OperatorAppearance.ConfigureMotionStyle(style);
            if(appearance is null)File.Delete(OperatorAppearance.StoragePath);else File.WriteAllBytes(OperatorAppearance.StoragePath,appearance);
            foreach(var (path,bytes) in files){if(bytes is null)File.Delete(path);else{Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,bytes);}}
        }
    }
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    public async Task ReducedMotionOpensTheStoppageEditorAtItsFinalPose(int width,int height)
    {
        using var f=new Fixture();await f.Ready();f.Window.Width=width;f.Window.Height=height;await Settle();f.Open();
        Assert.True(f.Named<Popup>("ControlPanelCountdownEditPopupHost").IsOpen);
        Assert.Equal(1,f.Named<Border>("ControlPanelCountdownEditPopup").Opacity,3);
        Assert.Equal(1,Property<ScaleTransform>(f.Window,"StoppageScale").ScaleX,3);
        Assert.Equal(1,Property<ScaleTransform>(f.Window,"StoppageScale").ScaleY,3);
        Assert.Equal(0,Property<TranslateTransform>(f.Window,"StoppageTravel").X,3);
        Assert.Equal(0,Property<TranslateTransform>(f.Window,"StoppageTravel").Y,3);
    }
    [AvaloniaFact]
    public async Task ReducedMotionCancelClosesImmediatelyWithoutChangingEitherClock()
    {
        using var f=new Fixture();await f.Ready();var match=Field<MatchManager>(f.Window,"_match");var elapsed=match.ElapsedInQuarter;var remaining=Field<TimeSpan>(f.Window,"_controlPanelCountdownRemaining");
        f.Open();await Settle(250);Invoke(f.Window,"ControlPanelCountdownCancelEditButton_Click",f.Window,new RoutedEventArgs());
        Assert.False(f.Named<Popup>("ControlPanelCountdownEditPopupHost").IsOpen);Assert.Equal(elapsed,match.ElapsedInQuarter);Assert.Equal(remaining,Field<TimeSpan>(f.Window,"_controlPanelCountdownRemaining"));
    }
    [AvaloniaFact]
    public async Task ClockRefreshPreservesTheDraftAndAClosedApplyCannotOverwriteLaterClockState()
    {
        using var f=new Fixture();await f.Ready();f.Open();await Settle(250);
        var minutes=f.Named<TextBox>("ControlPanelCountdownEditMinutesBox");var seconds=f.Named<TextBox>("ControlPanelCountdownEditSecondsBox");
        minutes.Text="03";seconds.Text="15";Invoke(f.Window,"UpdateControlPanelCountdownUi");Assert.Equal("03",minutes.Text);Assert.Equal("15",seconds.Text);
        f.Apply();Assert.Equal(TimeSpan.FromSeconds(195),Field<TimeSpan>(f.Window,"_controlPanelCountdownRemaining"));
        Set(f.Window,"_controlPanelCountdownRemaining",TimeSpan.FromSeconds(100));f.Apply();
        Assert.Equal(TimeSpan.FromSeconds(100),Field<TimeSpan>(f.Window,"_controlPanelCountdownRemaining"));
    }

    [AvaloniaFact]
    public async Task RepeatedRefreshOfAnAlreadyHiddenClockDoesNotRestartEditorCleanup()
    {
        using var f=new Fixture();await f.Ready();Field<MatchManager>(f.Window,"_match").SetClockMode(ClockMode.Countdown);
        int generation=Field<int>(f.Window,"_countdownEditorGeneration");var remaining=Field<TimeSpan>(f.Window,"_controlPanelCountdownRemaining");
        for(int i=0;i<20;i++)Invoke(f.Window,"UpdateControlPanelCountdownUi");
        Assert.Equal(generation,Field<int>(f.Window,"_countdownEditorGeneration"));Assert.Equal(remaining,Field<TimeSpan>(f.Window,"_controlPanelCountdownRemaining"));
        Assert.False(f.Named<Popup>("ControlPanelCountdownEditPopupHost").IsOpen);
    }

    [AvaloniaFact]
    public async Task ReopeningDuringCloseAndLiveReducedMotionKeepTheLatestPopupAndFocus()
    {
        using var f=new Fixture();await f.Ready();OperatorAppearance.Configure(true,false);f.Open();await Settle(60);
        Invoke(f.Window,"ControlPanelCountdownCancelEditButton_Click",f.Window,new RoutedEventArgs());await Settle(25);f.Open();await Settle(35);
        Assert.True(f.Named<TextBox>("ControlPanelCountdownEditMinutesBox").IsFocused);
        OperatorAppearance.Configure(true,true);Assert.True(f.Named<Popup>("ControlPanelCountdownEditPopupHost").IsOpen);Assert.Equal(1,f.Named<Border>("ControlPanelCountdownEditPopup").Opacity);
        await Settle(650);Assert.True(f.Named<Popup>("ControlPanelCountdownEditPopupHost").IsOpen);
        Invoke(f.Window,"ControlPanelCountdownCancelEditButton_Click",f.Window,new RoutedEventArgs());await Settle();
        Assert.False(f.Named<Popup>("ControlPanelCountdownEditPopupHost").IsOpen);Assert.True(f.Named<Button>("ControlPanelCountdownEditButton").IsFocused);
    }

    [AvaloniaTheory]
    [InlineData("quarter")][InlineData("reset")][InlineData("replacement")]
    public async Task ContextChangesInvalidateTheOldCorrectionWithoutApplyingIt(string change)
    {
        using var f=new Fixture();await f.Ready();f.Open();await Settle();f.Named<TextBox>("ControlPanelCountdownEditMinutesBox").Text="03";
        if(change=="quarter"){Assert.True(Field<MatchManager>(f.Window,"_match").EndQuarter());Invoke(f.Window,"UpdateControlPanelCountdownUi");}
        else if(change=="reset")Invoke(f.Window,"ControlPanelCountdownResetButton_Click",f.Window,new RoutedEventArgs());
        else Invoke(f.Window,"RetireSessionForReplacement");
        var remaining=Field<TimeSpan>(f.Window,"_controlPanelCountdownRemaining");Assert.False(f.Named<Popup>("ControlPanelCountdownEditPopupHost").IsOpen);
        f.Apply();Assert.Equal(remaining,Field<TimeSpan>(f.Window,"_controlPanelCountdownRemaining"));
    }

    [AvaloniaFact]
    public async Task InvalidCorrectionKeepsTheEditorOpenAndValidCorrectionPreservesBothRunStates()
    {
        using var f=new Fixture();await f.Ready();var match=Field<MatchManager>(f.Window,"_match");match.StartClock();Set(f.Window,"_controlPanelCountdownRunning",true);
        f.Open();await Settle();var before=Field<TimeSpan>(f.Window,"_controlPanelCountdownRemaining");
        f.Named<TextBox>("ControlPanelCountdownEditMinutesBox").Text="00";f.Named<TextBox>("ControlPanelCountdownEditSecondsBox").Text="00";f.Apply();
        Assert.True(f.Named<Popup>("ControlPanelCountdownEditPopupHost").IsOpen);Assert.True(f.Named<TextBlock>("ControlPanelCountdownEditErrorText").IsVisible);Assert.Equal(before,Field<TimeSpan>(f.Window,"_controlPanelCountdownRemaining"));
        var elapsed=match.ElapsedInQuarter;f.Named<TextBox>("ControlPanelCountdownEditMinutesBox").Text="03";f.Named<TextBox>("ControlPanelCountdownEditSecondsBox").Text="15";f.Apply();
        Assert.True(match.ClockRunning);Assert.True(match.ElapsedInQuarter>=elapsed);Assert.True(Field<bool>(f.Window,"_controlPanelCountdownRunning"));Assert.Equal(TimeSpan.FromSeconds(195),Field<TimeSpan>(f.Window,"_controlPanelCountdownRemaining"));
    }

    [AvaloniaTheory]
    [InlineData("minutes",false)][InlineData("seconds",false)][InlineData("cancel",false)][InlineData("apply",false)]
    [InlineData("minutes",true)][InlineData("seconds",true)][InlineData("cancel",true)][InlineData("apply",true)]
    public async Task EscapeClosesOnlyTheEditorAndReturnsKeyboardFocus(string focus,bool animate)
    {
        using var f=new Fixture();await f.Ready();OperatorAppearance.Configure(true,!animate);f.Open();await Settle();
        var match=Field<MatchManager>(f.Window,"_match");var elapsed=match.ElapsedInQuarter;var remaining=Field<TimeSpan>(f.Window,"_controlPanelCountdownRemaining");
        bool running=match.ClockRunning,stoppageRunning=Field<bool>(f.Window,"_controlPanelCountdownRunning");
        Control target=focus switch
        {
            "minutes"=>f.Named<TextBox>("ControlPanelCountdownEditMinutesBox"),
            "seconds"=>f.Named<TextBox>("ControlPanelCountdownEditSecondsBox"),
            "apply"=>f.Named<Button>("ControlPanelCountdownApplyButton"),
            _=>f.Named<Border>("ControlPanelCountdownEditPopup").GetVisualDescendants().OfType<Button>().Single(button=>string.Equals(button.Content?.ToString(),"Cancel",StringComparison.OrdinalIgnoreCase))
        };
        target.Focus(NavigationMethod.Tab);
        Assert.True(target.IsFocused);
        f.Window.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None);f.Window.KeyReleaseQwerty(PhysicalKey.Escape,RawInputModifiers.None);await Settle(animate?250:120);
        Assert.False(f.Named<Popup>("ControlPanelCountdownEditPopupHost").IsOpen);Assert.True(f.Named<Button>("ControlPanelCountdownEditButton").IsFocused);
        Assert.Equal(elapsed,match.ElapsedInQuarter);Assert.Equal(remaining,Field<TimeSpan>(f.Window,"_controlPanelCountdownRemaining"));
        Assert.Equal(running,match.ClockRunning);Assert.Equal(stoppageRunning,Field<bool>(f.Window,"_controlPanelCountdownRunning"));
        Assert.True(Field<bool>(f.Window,"_aflMatchReady"));Assert.False(f.Named<SportSelectionScreen>("SportSelectionPanel").IsVisible);
    }

    [Fact]
    public void WindowsPopupSurfaceDeclaresItsOwnEscapeRoute()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null && !File.Exists(Path.Combine(directory.FullName,"Roche_Scoreboard/MainWindow.xaml")))directory=directory.Parent;
        Assert.NotNull(directory);
        var document=System.Xml.Linq.XDocument.Load(Path.Combine(directory.FullName,"Roche_Scoreboard/MainWindow.xaml"));
        System.Xml.Linq.XNamespace x="http://schemas.microsoft.com/winfx/2006/xaml";
        var surface=document.Descendants().Single(element=>(string?)element.Attribute(x+"Name")=="ControlPanelCountdownEditPopup");
        Assert.Equal("StoppageEditor_KeyDown",(string?)surface.Attribute("PreviewKeyDown"));
    }

    [AvaloniaFact]
    public async Task CommandBarCannotDisableTheWindowBehindAnUnresolvedClockCorrection()
    {
        using var f=new Fixture();await f.Ready();f.Open();await Settle();
        f.Named<TextBox>("ControlPanelCountdownEditMinutesBox").Text="07";
        Assert.False((bool)Invoke(f.Window,"OpenCommandBar")!);Assert.True(f.Named<Popup>("ControlPanelCountdownEditPopupHost").IsOpen);
        Assert.Equal("07",f.Named<TextBox>("ControlPanelCountdownEditMinutesBox").Text);Assert.True(f.Named<Control>("MainContent").IsEnabled);
        Invoke(f.Window,"ControlPanelCountdownCancelEditButton_Click",f.Window,new RoutedEventArgs());Assert.True((bool)Invoke(f.Window,"OpenCommandBar")!);
        Invoke(f.Window,"CloseCommandBar");
    }

    [AvaloniaFact]
    public async Task CancelAndReducedMotionBeforePlacementCannotReplayQueuedOpeningWork()
    {
        using var f=new Fixture();await f.Ready();OperatorAppearance.Configure(true,false);f.Open();
        Assert.True(Field<bool>(f.Window,"_stoppagePlacementPending"));
        Invoke(f.Window,"ControlPanelCountdownCancelEditButton_Click",f.Window,new RoutedEventArgs());Assert.False(f.Named<Popup>("ControlPanelCountdownEditPopupHost").IsOpen);
        await Settle(500);Assert.False(f.Named<Popup>("ControlPanelCountdownEditPopupHost").IsOpen);
        f.Open();OperatorAppearance.Configure(true,true);Assert.Equal(1,f.Named<Border>("ControlPanelCountdownEditPopup").Opacity);
        await Settle(500);Assert.True(f.Named<Popup>("ControlPanelCountdownEditPopupHost").IsOpen);Assert.False(Field<bool>(f.Window,"_stoppagePlacementPending"));
        Assert.Equal(1,Property<ScaleTransform>(f.Window,"StoppageScale").ScaleX);
    }

    [AvaloniaTheory]
    [InlineData(false,false,false)][InlineData(true,false,false)][InlineData(false,true,false)]
    [InlineData(false,false,true)][InlineData(true,false,true)][InlineData(false,true,true)]
    public async Task TimedPartsFollowTheMeasuredSourceAndMotionPreference(bool calm,bool reduced,bool compact)
    {
        using var f=new Fixture();await f.Ready();
        if(compact){f.Window.Width=420;f.Window.Height=320;await Settle();}
        OperatorAppearance.Configure(true,reduced);OperatorAppearance.ConfigureMotionStyle(calm?OperatorMotionStyle.Calm:OperatorMotionStyle.Expressive);
        f.Open();Dispatcher.UIThread.RunJobs();
        var surface=f.Named<Border>("ControlPanelCountdownEditPopup");var scale=Property<ScaleTransform>(f.Window,"StoppageScale");var travel=Property<TranslateTransform>(f.Window,"StoppageTravel");
        var header=f.Named<Grid>("ControlPanelCountdownEditHeader");var headerTravel=Assert.IsType<TranslateTransform>(header.RenderTransform);
        var symbol=f.Named<OperatorSymbol>("ControlPanelCountdownEditSymbol");var source=f.Named<Button>("ControlPanelCountdownEditButton");
        var popup=f.Named<Popup>("ControlPanelCountdownEditPopupHost");var top=TopLevel.GetTopLevel(popup.Child!)!;
        Point InHost(Visual visual,Point point)=>visual.TranslatePoint(point,top)??top.PointToClient(visual.PointToScreen(point));
        var parent=Assert.IsAssignableFrom<Visual>(surface.GetVisualParent());
        // Measure the layout frame through the parent, independently of both
        // the animated surface transform and production's _stoppageOrigin.
        // Center is a placement preference; monitor edges may shift the host.
        var destination=new Rect(InHost(parent,surface.Bounds.TopLeft),InHost(parent,surface.Bounds.BottomRight));
        var trigger=new Rect(InHost(source,default),InHost(source,new Point(source.Bounds.Width,source.Bounds.Height)));
        double triggerWidth=Math.Min(trigger.Width,destination.Width),triggerHeight=Math.Min(trigger.Height,destination.Height);
        double localX=Math.Clamp(trigger.X-destination.X,0,destination.Width-triggerWidth);
        double localY=Math.Clamp(trigger.Y-destination.Y,0,destination.Height-triggerHeight);
        var origin=Field<ExpansionOrigin>(f.Window,"_stoppageOrigin");double amount=calm?.4:1;
        var initial=new Rect(destination.X+localX*amount,destination.Y+localY*amount,
            destination.Width+(triggerWidth-destination.Width)*amount,destination.Height+(triggerHeight-destination.Height)*amount);
        Assert.Equal(1-(1-Math.Clamp(source.Bounds.Width/surface.Bounds.Width,.04,1))*amount,origin.ScaleX,3);
        Assert.Equal(1-(1-Math.Clamp(source.Bounds.Height/surface.Bounds.Height,.04,1))*amount,origin.ScaleY,3);
        if(!reduced)
        {
            Assert.True(new Rect(-.01,-.01,surface.Bounds.Width+.02,surface.Bounds.Height+.02).Contains(new Rect(surface.Bounds.Width*(1-origin.ScaleX)/2+origin.OffsetX,surface.Bounds.Height*(1-origin.ScaleY)/2+origin.OffsetY,surface.Bounds.Width*origin.ScaleX,surface.Bounds.Height*origin.ScaleY)),"The first rendered pose must stay inside the native popup host");
        }
        string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??"/tmp/roche-stoppage-captures";Directory.CreateDirectory(dir);
        var samples=new List<object>();
        foreach(double time in new[]{0,60,OperatorMotion.SymbolAccent.TotalMilliseconds,OperatorMotion.SurfaceOpen.TotalMilliseconds,OperatorMotion.PanelOpen.TotalMilliseconds,OperatorMotion.SymbolArrival.TotalMilliseconds}.Distinct().Order())
        {
            foreach(AvaloniaObject part in new AvaloniaObject[]{scale,travel,header,headerTravel,symbol})Motion.SampleAt(part,TimeSpan.FromMilliseconds(time));
            Motion.SampleAt(surface,TimeSpan.FromMilliseconds(time));
            if(time==0)
            {
                Assert.Equal(reduced?1:origin.ScaleX,scale.ScaleX,3);Assert.Equal(reduced?1:origin.ScaleY,scale.ScaleY,3);
                Assert.Equal(reduced?1:.45,header.Opacity,3);Assert.Equal(reduced?0:7*amount,headerTravel.Y,3);
                Assert.Equal(reduced?0:origin.OffsetX,travel.X,3);Assert.Equal(reduced?0:origin.OffsetY,travel.Y,3);
            }
            double pulse=(double)symbol.Diagnostics.GetType().GetProperty("Pulse")!.GetValue(symbol.Diagnostics)!;
            if(reduced || time>=OperatorMotion.SymbolArrival.TotalMilliseconds)Assert.InRange(pulse,0,.001);
            else if(time==OperatorMotion.SymbolAccent.TotalMilliseconds)Assert.InRange(pulse,.999,1);
            if(!reduced && time==OperatorMotion.PanelOpen.TotalMilliseconds)Assert.True(pulse>0,"The clock gesture continues after the shell settles");
            if(time>=OperatorMotion.SurfaceOpen.TotalMilliseconds){Assert.Equal(1,header.Opacity,3);Assert.Equal(0,headerTravel.Y,3);}
            if(time>=OperatorMotion.PanelOpen.TotalMilliseconds){Assert.Equal(1,scale.ScaleX,3);Assert.Equal(1,scale.ScaleY,3);Assert.Equal(0,travel.Y,3);}
            if(time==0 || time>=OperatorMotion.PanelOpen.TotalMilliseconds)
            {
                var expected=time==0 && !reduced?initial:destination;
                var rendered=new Rect(InHost(surface,default),InHost(surface,new Point(surface.Bounds.Width,surface.Bounds.Height)));
                Assert.InRange(Math.Abs(expected.X-rendered.X),0,1.1);Assert.InRange(Math.Abs(expected.Y-rendered.Y),0,1.1);
                Assert.InRange(Math.Abs(expected.Width-rendered.Width),0,1.1);Assert.InRange(Math.Abs(expected.Height-rendered.Height),0,1.1);
            }
            samples.Add(new{Time=time,scale.ScaleX,scale.ScaleY,travel.X,travel.Y,HeaderOpacity=header.Opacity,Symbol=symbol.Diagnostics});
            using var frame=top.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(dir,$"timed-stoppage-{calm}-{reduced}-{compact}-{time:0}.png"));
        }
        File.WriteAllText(Path.Combine(dir,$"timed-stoppage-{calm}-{reduced}-{compact}.json"),System.Text.Json.JsonSerializer.Serialize(samples));
        Assert.False(Motion.IsAnimating(scale));Assert.False(Motion.IsAnimating(surface));
        Assert.Equal(TimeSpan.FromMinutes(20),Field<TimeSpan>(f.Window,"_controlPanelCountdownRemaining"));
    }

    [AvaloniaTheory]
    [InlineData(1280,850,false)][InlineData(420,320,false)][InlineData(420,320,true)]
    public async Task CapturePopupHierarchyAtNormalAndCompactSizes(int width,int height,bool reduced)
    {
        using var f=new Fixture();await f.Ready();f.Window.Width=width;f.Window.Height=height;
        OperatorAppearance.Configure(true,reduced);OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);await Settle();f.Open();await Settle(650);
        var popup=f.Named<Popup>("ControlPanelCountdownEditPopupHost");Assert.True(popup.IsOpen);
        var surface=Assert.IsAssignableFrom<Control>(popup.Child);var top=Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(surface));
        string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??"/tmp/roche-stoppage-captures";Directory.CreateDirectory(dir);
        using var frame=top.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(dir,$"stoppage-editor-{width}-{reduced}.png"));
        Assert.True(surface.Bounds.Width>250);Assert.True(surface.Bounds.Height>100);
        var point=surface.TranslatePoint(default,top)!.Value;
        File.WriteAllText(Path.Combine(dir,$"stoppage-editor-{width}-{reduced}.json"),System.Text.Json.JsonSerializer.Serialize(new{Host=top.Bounds,Surface=surface.Bounds,Position=point}));
        Assert.True(new Rect(top.Bounds.Size).Contains(new Rect(point,surface.Bounds.Size)),"The entire popup must fit its actual hosting surface");
        var actions=surface.GetVisualDescendants().OfType<Button>().Where(button=>button.IsEffectivelyVisible).ToArray();Assert.Equal(2,actions.Length);
        foreach(var action in actions)
        {var position=action.TranslatePoint(default,top)!.Value;Assert.True(new Rect(top.Bounds.Size).Contains(new Rect(position,action.Bounds.Size)),"Cancel and Apply must remain fully reachable");}
    }
}

// Public ordinary Choose/Form routes; no private prompt invocation or clock sampling.
// App preference seam: OperatorAppearance.Configure is the actual persisted setter
// used by GlobalSettingsScreen. This does not simulate a native OS reduce event.
using System.Diagnostics;
using System.Collections.Specialized;
using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    private static void PromptStorage()
    {
        string temp = Path.GetFullPath(Path.GetTempPath());
        string captures = Path.GetFullPath(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")!);
        SelfTestStorage.Initialize(true);
        Assert.Equal(Path.Combine(temp,"RocheScoreboard-SelfTests",Environment.ProcessId.ToString()),SelfTestStorage.RootPath);
        Assert.Equal(Path.Combine(SelfTestStorage.RootPath,"preferences"),SelfTestStorage.DocumentsDirectory);
        Assert.Equal(Path.Combine(SelfTestStorage.DocumentsDirectory,"operator_appearance.json"),OperatorAppearance.StoragePath);
        string dataHome=Path.GetFullPath(Environment.GetEnvironmentVariable("ROCHE_DATA_HOME")!);
        Assert.Equal(Path.Combine(temp,"roche-production-tests-"+Environment.ProcessId),dataHome);
        // The authoritative runner may place explicit captures outside TMPDIR.
        // Supporting execution additionally requires every path in its worker.
        if(temp.Contains(Path.DirectorySeparatorChar+"roche-parallel-support"+Path.DirectorySeparatorChar,StringComparison.Ordinal))
        {
            string worker=Path.GetDirectoryName(temp.TrimEnd(Path.DirectorySeparatorChar))!;
            Assert.EndsWith(Path.DirectorySeparatorChar+"temp"+Path.DirectorySeparatorChar,temp);
            Assert.StartsWith(worker+Path.DirectorySeparatorChar,captures);
            Assert.StartsWith(worker+Path.DirectorySeparatorChar,dataHome);
        }
        Record("prompt-storage-" + Environment.ProcessId, new { temp, captures, dataHome,
            SelfTestStorage.RootPath, SelfTestStorage.DocumentsDirectory, OperatorAppearance.StoragePath, Environment.ProcessId });
    }
    private static Border PromptSheet(CricketControlPanel panel) => Assert.Single(
        Named<Grid>(panel,"CricketRoot").Children.OfType<Border>(), value => Equals(value.Tag,"cricket-decision-sheet"));
    private static Control PromptSurface(Border sheet)
    {
        // The ordinary shell is the veil direct child; action button templates
        // legitimately contain additional OperatorGlassSurface instances.
        var shell=Assert.IsAssignableFrom<Control>(sheet.Child);
        Assert.Equal("OperatorGlassSurface",shell.GetType().Name);return shell;
    }
    private static bool PromptMounted(CricketControlPanel panel, Border sheet) =>
        Named<Grid>(panel,"CricketRoot").Children.Contains(sheet);
    private static Button PromptAction(Border sheet,string tag) => Assert.Single(
        sheet.GetVisualDescendants().OfType<Button>(), value=>Equals(value.Tag,tag));
    private static object PromptPose(Window window, CricketControlPanel panel, Border sheet,Control surface,double elapsed) => new
    {
        elapsed, mounted=PromptMounted(panel,sheet), sheet.IsLoaded, sheet.IsEffectivelyVisible,
        veilOpacity=sheet.Opacity,surfaceOpacity=surface.Opacity,
        scaleX=Scale(surface).ScaleX,scaleY=Scale(surface).ScaleY,shiftX=Shift(surface).X,shiftY=Shift(surface).Y,
        origin=surface.RenderTransformOrigin.Point,shellBounds=surface.Bounds,veilBounds=sheet.Bounds,window.ClientSize,focus=(window.FocusManager?.GetFocusedElement() as Control)?.Name,
        busy=panel.HasActiveDecision
    };
    private static void PromptCapture(Window window,string name)
    {
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")!;
        using var image=window.CaptureRenderedFrame();Assert.NotNull(image);image.Save(Path.Combine(directory,name+".png"));
    }
    private static async Task<(Border Sheet,Control Surface)> PromptOpen(Window window,CricketControlPanel panel,string role,PromptReduceObservation? observation=null)
    {
        var options=Named<Button>(panel,"MatchOptionsButton");
        await Reveal(window,options);await StableReachable(window,options);
        if(role=="choose")observation?.ArmOpening();
        TapNow(window,options);
        if(role=="choose")observation?.GestureReturned();
        if(role=="form")
        {
            await Settle(650);var optionSheet=PromptSheet(panel);var outcome=PromptAction(optionSheet,"outcome");
            await Reveal(window,outcome);await StableReachable(window,outcome);
            observation?.ArmOpening();TapNow(window,outcome);observation?.GestureReturned();
            // The old ordinary sheet really exits before the awaited caller
            // creates the form; poll only until the actual new note is mounted.
            for(int i=0;i<80&&!panel.GetVisualDescendants().OfType<TextBox>().Any(x=>Equals(x.Tag,"outcome-note"));i++)
                await Settle(5);
        }
        var sheet=PromptSheet(panel);var surface=PromptSurface(sheet);
        if(role=="form")Assert.Single(sheet.GetVisualDescendants().OfType<TextBox>(),x=>Equals(x.Tag,"outcome-note"));
        Assert.True(panel.HasActiveDecision);return(sheet,surface);
    }
    private static void PromptEndpoint(Border sheet,Control surface)
    {
        Assert.Equal(1,sheet.Opacity);Assert.Equal(1,surface.Opacity);
        Assert.Equal(1,Scale(surface).ScaleX);Assert.Equal(1,Scale(surface).ScaleY);
        Assert.Equal(0,Shift(surface).X);Assert.Equal(0,Shift(surface).Y);
    }
    private static async Task PromptRetired(CricketControlPanel panel,Border sheet)
    {
        await Until(()=>!PromptMounted(panel,sheet)&&!panel.HasActiveDecision,"Actual completed prompt must release sheet and busy authority.");
        Assert.False(sheet.IsLoaded);
    }

    // These are public layout/property observations, not pre-Reduce raster frames.
    // Capture them before any disk/PNG work so natural completion cannot satisfy
    // the immediate post-preference endpoint/removal oracle later.
    private sealed record PromptObservedPose(double SinceGestureMilliseconds,string Signal,
        bool Mounted,bool Loaded,bool Visible,bool AttachedToWindow,bool Enabled,bool Busy,bool OutcomeNoteMounted,bool MotionEnabled,bool? AnimationOverride,
        double VeilOpacity,double SurfaceOpacity,double ScaleX,double ScaleY,double ShiftX,double ShiftY,
        Rect SurfaceBounds,Rect VeilBounds);
    private static void PromptEndpoint(PromptObservedPose pose)
    {
        Assert.True(pose.Mounted);Assert.True(pose.Loaded);Assert.True(pose.Busy);
        Assert.Equal(1,pose.VeilOpacity);Assert.Equal(1,pose.SurfaceOpacity);
        Assert.Equal(1,pose.ScaleX);Assert.Equal(1,pose.ScaleY);
        Assert.Equal(0,pose.ShiftX);Assert.Equal(0,pose.ShiftY);
    }
    private sealed class PromptReduceObservation : IDisposable
    {
        private readonly Window window;
        private readonly CricketControlPanel panel;
        private readonly string role;
        private readonly bool closing;
        private readonly Grid host;
        private readonly Stopwatch clock=new();
        private Border? sheet;
        private Control? surface;
        private ScaleTransform? scale;
        private TranslateTransform? shift;
        private bool gestureReturned,layoutSeen,armed,creationReturned;
        private DispatcherOperation? creationGate;
        private Exception? error;
        internal bool Applied {get;private set;}
        internal bool HandlersRetired {get;private set;}
        internal PromptObservedPose? Before {get;private set;}
        internal PromptObservedPose? After {get;private set;}
        internal PromptReduceObservation(Window window,CricketControlPanel panel,string role,bool closing)
        {this.window=window;this.panel=panel;this.role=role;this.closing=closing;host=Named<Grid>(panel,"CricketRoot");}
        internal void ArmOpening()
        {
            Assert.False(armed);Assert.False(closing);armed=true;clock.Restart();
            ((INotifyCollectionChanged)host.Children).CollectionChanged+=Added;
        }
        internal void ArmClosing(Border currentSheet,Control currentSurface)
        {
            Assert.False(armed);Assert.True(closing);armed=true;creationReturned=true;clock.Restart();
            Attach(currentSheet,currentSurface);
        }
        internal void GestureReturned()
        {gestureReturned=true;Observe("gesture-returned");}
        private void Added(object? sender,NotifyCollectionChangedEventArgs args)
        {
            try
            {
                if(args.NewItems is null)return;
                foreach(var candidate in args.NewItems.OfType<Border>().Where(x=>Equals(x.Tag,"cricket-decision-sheet")))
                {
                    // ScrollViewer/form templates may not expose visual
                    // descendants until layout. Attach the actual newly added
                    // sheet now; check its public role content at observation.
                    Assert.Null(sheet);Attach(candidate,PromptSurface(candidate));
                }
            }
            catch(Exception failure){Fail(failure);}
        }
        private void Attach(Border currentSheet,Control currentSurface)
        {
            sheet=currentSheet;surface=currentSurface;scale=Scale(surface);shift=Shift(surface);
            surface.LayoutUpdated+=Layout;
            sheet.PropertyChanged+=Changed;surface.PropertyChanged+=Changed;
            scale.PropertyChanged+=Changed;shift.PropertyChanged+=Changed;
            if(!closing)
            {
                // The Form is created asynchronously after the outcome input
                // returns. Wait for this creation stack to unwind too, so an
                // observer cannot interrupt startup between clock registrations.
                creationGate=Dispatcher.UIThread.InvokeAsync(()=>
                {
                    if(HandlersRetired||!ReferenceEquals(sheet,currentSheet))return;
                    creationReturned=true;Observe("sheet-creation-returned");
                },DispatcherPriority.Normal);
            }
        }
        private void Layout(object? sender,EventArgs args)
        {layoutSeen=true;Observe("layout-updated");}
        private void Changed(object? sender,AvaloniaPropertyChangedEventArgs args)
        {Observe("property-changed:"+args.Property.Name);}
        private PromptObservedPose Pose(string signal)
        {
            var currentSheet=sheet!;var currentSurface=surface!;var currentScale=scale!;var currentShift=shift!;
            return new(clock.Elapsed.TotalMilliseconds,signal,host.Children.Contains(currentSheet),
                currentSheet.IsLoaded&&currentSurface.IsLoaded,currentSheet.IsEffectivelyVisible&&currentSurface.IsEffectivelyVisible,
                ReferenceEquals(TopLevel.GetTopLevel(currentSurface),window),currentSurface.IsEnabled,
                panel.HasActiveDecision,currentSheet.GetVisualDescendants().OfType<TextBox>().Any(x=>Equals(x.Tag,"outcome-note")),
                OperatorMotion.Enabled,OperatorMotion.AnimationOverride,
                currentSheet.Opacity,currentSurface.Opacity,currentScale.ScaleX,currentScale.ScaleY,currentShift.X,currentShift.Y,
                currentSurface.Bounds,currentSheet.Bounds);
        }
        private static bool PositiveBounds(PromptObservedPose pose)=>pose.SurfaceBounds.Width>0&&pose.SurfaceBounds.Height>0
            &&pose.VeilBounds.Width>0&&pose.VeilBounds.Height>0;
        private bool Partial(PromptObservedPose pose)=>closing
            ? !pose.Enabled&&pose.VeilOpacity>=.001&&pose.VeilOpacity<=.999999
            : Math.Abs(pose.ShiftY)>.001||Math.Abs(pose.ScaleY-1)>.001;
        private void Observe(string signal)
        {
            if(HandlersRetired||!gestureReturned||sheet is null||surface is null||(!closing&&(!layoutSeen||!creationReturned)))return;
            try
            {
                var candidate=Pose(signal);
                if(!candidate.Mounted||!candidate.Loaded||!candidate.Visible||!candidate.AttachedToWindow||!candidate.Busy||!PositiveBounds(candidate)||!Partial(candidate))return;
                if(candidate.OutcomeNoteMounted!=(role=="form"))return;
                Assert.True(candidate.MotionEnabled);Assert.Null(candidate.AnimationOverride);
                // Retire before Configure: no test subscriber remains to observe
                // reentrant cancellation/property cleanup from the product setter.
                Dispose();
                Before=Pose(signal);
                Assert.True(Before.Mounted&&Before.Loaded&&Before.Visible&&Before.AttachedToWindow&&Before.Busy&&PositiveBounds(Before)&&Partial(Before));
                OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
                After=Pose("immediately-after-configure");Applied=true;
            }
            catch(Exception failure){Fail(failure);}
        }
        private void Fail(Exception failure){error=failure;Dispose();}
        internal async Task AwaitOneObservation()
        {
            try
            {
                // Same80x5ms bounded route-readiness pump as the existing form
                // helper. This waits for one gesture's observer; never reopens
                // or retries a gesture and never changes an animation duration.
                for(int i=0;i<80&&!Applied&&error is null;i++)await Settle(5);
            }
            finally{Dispose();}
            if(!Applied)Record($"ordinary-{role}-{closing}-observation-unmet",new
            {gestureReturned,creationReturned,layoutSeen,armed,HandlersRetired,last=sheet is null?null:Pose("unmet-observation"),error=error?.ToString()});
            if(error is not null)ExceptionDispatchInfo.Capture(error).Throw();
            Assert.True(Applied,"One real mounted positive-bounds nonendpoint observation must precede Reduce; an absent/late observation is a fixture prerequisite failure.");
            Assert.True(HandlersRetired);
        }
        public void Dispose()
        {
            if(HandlersRetired)return;HandlersRetired=true;
            ((INotifyCollectionChanged)host.Children).CollectionChanged-=Added;
            creationGate?.Abort();creationGate=null;
            if(surface is not null){surface.LayoutUpdated-=Layout;surface.PropertyChanged-=Changed;}
            if(sheet is not null)sheet.PropertyChanged-=Changed;
            if(scale is not null)scale.PropertyChanged-=Changed;
            if(shift is not null)shift.PropertyChanged-=Changed;
        }
    }

    [AvaloniaTheory]
    [InlineData("choose",false)][InlineData("form",false)]
    [InlineData("choose",true)][InlineData("form",true)]
    public async Task OrdinaryPromptLiveReduceSettlesActualOpeningOrEscapeClock(string role,bool closing)
    {
        PromptStorage();using var scope=new Scope();
        var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);
        var window=scope.Panel(panel);await Settle(650);
        string initial=State(match);int revision=match.DeliveryRevision,events=0;
        void Changed()=>events++;match.MatchChanged+=Changed;
        using var observation=new PromptReduceObservation(window,panel,role,closing);
        try
        {
            var opened=await PromptOpen(window,panel,role,closing?null:observation);
            if(closing)
            {
                await Settle(650);observation.ArmClosing(opened.Sheet,opened.Surface);
                EscapeNow(window);observation.GestureReturned();
            }
            await observation.AwaitOneObservation();
            var before=Assert.IsType<PromptObservedPose>(observation.Before);
            var after=Assert.IsType<PromptObservedPose>(observation.After);
            Assert.True(before.Mounted&&before.Loaded&&before.Visible&&before.AttachedToWindow&&before.Busy);
            Assert.True(before.SurfaceBounds.Width>0&&before.SurfaceBounds.Height>0&&before.VeilBounds.Width>0&&before.VeilBounds.Height>0);
            Assert.Equal(role=="form",before.OutcomeNoteMounted);
            Assert.True(before.MotionEnabled);Assert.Null(before.AnimationOverride);
            if(closing){Assert.False(before.Enabled);Assert.InRange(before.VeilOpacity,.001,.999999);}
            else Assert.True(Math.Abs(before.ShiftY)>.001||Math.Abs(before.ScaleY-1)>.001,
                "Actual opening transform must still move before Reduce; a settled fixture is not a baseline failure.");
            // Both value snapshots already exist. Disk and rendering below can
            // no longer turn natural completion into a passing Reduce oracle.
            Record($"ordinary-{role}-{closing}-observed-before-reduce",before);
            Record($"ordinary-{role}-{closing}-observed-after-reduce",new{pose=after,observation.HandlersRetired});
            PromptCapture(window,$"ordinary-{role}-{closing}-post-reduce");
            Assert.False(after.MotionEnabled);Assert.Null(after.AnimationOverride);
            Assert.False(OperatorMotion.Enabled);Assert.Null(OperatorMotion.AnimationOverride);
            if(closing)Assert.False(after.Mounted);else PromptEndpoint(after);
            if(!closing)
            {
                Assert.True(panel.HasActiveDecision);await Settle(80);
                Assert.Contains(window.FocusManager?.GetFocusedElement() as Visual,opened.Sheet.GetVisualDescendants());
                EscapeNow(window);
            }
            await PromptRetired(panel,opened.Sheet);await Settle(650);
            Assert.Equal(initial,State(match));Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(0,events);
            // A new real operator request still opens under reduced motion;
            // repeated Escape and old clocks must not retire its new authority.
            var current=await PromptOpen(window,panel,role);PromptEndpoint(current.Sheet,current.Surface);
            Assert.NotSame(opened.Sheet,current.Sheet);await Settle(600);
            Assert.True(PromptMounted(panel,current.Sheet));Assert.True(panel.HasActiveDecision);
            EscapeNow(window);await PromptRetired(panel,current.Sheet);
            Assert.Equal(initial,State(match));Assert.Equal(0,events);
        }
        finally{match.MatchChanged-=Changed;}
    }

    [AvaloniaFact]
    public async Task OrdinaryPromptLiveReduceDuringActualOutcomeCommitCompletesOnce()
    {
        PromptStorage();using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Panel(panel);await Settle(650);
        var opened=await PromptOpen(window,panel,"form");await Settle(650);
        var action=PromptAction(opened.Sheet,"no-result");await Reveal(window,action);await StableReachable(window,action);
        int events=0;void Changed()=>events++;match.MatchChanged+=Changed;
        try
        {
            TapNow(window,action);Assert.True(PromptMounted(panel,opened.Sheet));Assert.False(opened.Surface.IsEnabled);
            await Task.Delay(20);Dispatcher.UIThread.RunJobs();PromptCapture(window,"ordinary-commit-moving");
            Assert.True(PromptMounted(panel,opened.Sheet));Assert.InRange(opened.Sheet.Opacity,.001,.999999);
            Record("ordinary-commit-before-reduce",PromptPose(window,panel,opened.Sheet,opened.Surface,0));
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
            Record("ordinary-commit-at-reduce",PromptPose(window,panel,opened.Sheet,opened.Surface,0));
            Assert.False(PromptMounted(panel,opened.Sheet));
            await Until(()=>match.IsMatchFinalized,"Actual requested no-result commit must finish after settled close.");
            Assert.Equal(CricketMatchResultKind.NoResult,match.ResultKind);Assert.Equal(1,events);
            string committed=State(match);int revision=match.DeliveryRevision;await Settle(650);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
            Assert.Equal(committed,State(match));Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(1,events);
        }
        finally{match.MatchChanged-=Changed;}
    }

    [AvaloniaTheory]
    [InlineData("choose","rebind")][InlineData("form","rebind")]
    [InlineData("choose","detach")][InlineData("form","detach")]
    [InlineData("choose","close")][InlineData("form","close")]
    public async Task OrdinaryPromptOwnerRetirementLeavesNoOldAuthorityAndRealReopenWorks(string role,string transition)
    {
        PromptStorage();using var scope=new Scope();var old=CricketReliabilityChecks.CreateMatch(true);
        var panel=new CricketControlPanel();panel.SetMatch(old);var window=scope.Panel(panel);await Settle(650);
        var opened=await PromptOpen(window,panel,role);await Settle(650);string initial=State(old);
        var current=CricketReliabilityChecks.CreateMatch(true);string expected=State(current);
        if(transition=="rebind")panel.SetMatch(current);
        else if(transition=="detach")window.Content=null;
        else window.Close();
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);await PromptRetired(panel,opened.Sheet);
        Assert.Equal(initial,State(old));Assert.Equal(expected,State(current));
        if(transition!="rebind")
        {
            // NativeWindow retains its accessibility scroll wrapper after
            // detach/close. A newly opened owner gets a new public panel;
            // reparenting the old wrapper-owned control is not an app route.
            panel.ClearMatch();panel=new CricketControlPanel();panel.SetMatch(current);
            window=scope.Panel(panel);
        }
        await Settle(650);var reopened=await PromptOpen(window,panel,role);await Settle(80);
        PromptEndpoint(reopened.Sheet,reopened.Surface);Assert.True(panel.HasActiveDecision);
        Assert.Contains(window.FocusManager?.GetFocusedElement() as Visual,reopened.Sheet.GetVisualDescendants());
        EscapeNow(window);await PromptRetired(panel,reopened.Sheet);await Settle(600);
        Assert.Equal(initial,State(old));Assert.Equal(expected,State(current));
    }
}

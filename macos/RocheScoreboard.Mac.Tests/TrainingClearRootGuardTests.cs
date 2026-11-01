// OUTSIDE DRAFT ONLY: TWO actual MainWindow integration cases, uncompiled/unrun.
// Reuses the existing no-latch safe StorageScope in this partial test class.
// No private handler invocation, forced view visibility, remote or Exit operation.
// Home while an active Clear review is intentionally unavailable through public
// UI (its command bar is blocked); case2 cancels first and does NOT claim Home
// itself retired an active receipt. It checks retained receipt + off-air Resume.
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class BreakRotationModeIsolationTests
{
    private static string TrainingRootClearState(TrainingSession session)=>JsonSerializer.Serialize(new
    {
        session.ActiveIndex,session.AutoAdvance,session.IsRunning,Remaining=session.ActiveRemaining.Ticks,
        Timers=session.Timers.Select(timer=>new{timer.Id,timer.Title,Duration=timer.Duration.Ticks,Remaining=timer.Remaining.Ticks,timer.HasFinished})
    });
    private static Button TrainingRootClearAction(TrainingControlPanel panel,string tag)=>
        Assert.Single(panel.GetVisualDescendants().OfType<Button>(),button=>Equals(button.Tag,tag));
    private static void TrainingRootClearKey(Window owner,PhysicalKey key,RawInputModifiers modifiers=RawInputModifiers.None)
    {owner.KeyPressQwerty(key,modifiers);owner.KeyReleaseQwerty(key,RawInputModifiers.None);}
    private static async Task TrainingRootClearTabTo(Window owner,Control target)
    {
        // Real traversal; no Focus/private focus flag/scroll offset setter.
        for(int i=0;i<140&&!target.IsFocused;i++){TrainingRootClearKey(owner,PhysicalKey.Tab);await Pump(20);}
        Assert.True(target.IsFocused,"Real Tab must reach original "+target.Name);
    }
    private static void TrainingRootClearCapture(Window owner,string name)
    {
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is not {Length:>0} dir)return;
        Directory.CreateDirectory(dir);using var frame=owner.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(dir,name+".png"));
    }
    private static async Task<(TrainingControlPanel Panel,TrainingSession Session)> TrainingRootClearOpenTraining(StorageScope scope)
    {
        // The existing scope proves private GUID/PID paths BEFOREIO, preserves
        // files and the existing storage-latch mode, restores flags/environment.
        // Do not replace it with Initialize(true) or an ambient desktop path.
        scope.AssertOwnedPaths();scope.Main=new Roche_Scoreboard.MainWindow{Width=1280,Height=850};var main=scope.Main;main.Show();
        var home=Named<SportSelectionScreen>(main,"SportSelectionPanel");
        await Until(()=>home.IsEffectivelyVisible&&home.GetVisualDescendants().OfType<Button>().Any(button=>button.Name=="TrainingLaunchAction"&&button.IsEffectivelyVisible&&button.Bounds.Width>0),"Actual Home Training action must mount.");
        var launch=Assert.Single(home.GetVisualDescendants().OfType<Button>(),button=>button.Name=="TrainingLaunchAction");
        // Home's source-origin entry temporarily disables its actions; wait for
        // the real action to become enabled rather than clicking during entry.
        await Until(()=>launch.IsEffectivelyEnabled,"Home Training action must be enabled after its real entry.");
        await Pump(520);
        // Existing real pointer helper proves all5input probes in actualowner.
        RotationTap(main,launch);
        var panel=Named<TrainingControlPanel>(main,"TrainingControlPanelView");
        await Until(()=>panel.IsLoaded&&panel.IsEffectivelyVisible&&!home.IsEffectivelyVisible,"Physical Home Training must mount the original console.");
        var session=Read<TrainingSession>(main,"_trainingSession");Assert.NotNull(session);Assert.False(session.IsRunning);
        // Public model fixture data only; adding timers is not the behavior under review.
        session.AddTimer(new TrainingTimer("Root guard active drill",TimeSpan.FromMinutes(3)));
        session.AddTimer(new TrainingTimer("Root guard queued drill",TimeSpan.FromSeconds(95)));
        await Pump(150);Assert.Equal(2,session.Timers.Count);Assert.False(session.IsRunning);
        // Returning to the operator window after external output opens is an
        // explicit ordinary window-activation fixture action, not view/focus forcing.
        main.Activate();await Pump(80);
        Assert.True(Named<Button>(panel,"ClearAllButton").IsEffectivelyEnabled);
        return(panel,session);
    }
    private static async Task<(Button Cancel,Button Confirm)> TrainingRootClearOpenReview(
        Roche_Scoreboard.MainWindow main,TrainingControlPanel panel,TrainingSession session)
    {
        string before=TrainingRootClearState(session);var clear=Named<Button>(panel,"ClearAllButton");
        await TrainingRootClearTabTo(main,clear);TrainingRootClearKey(main,PhysicalKey.Space);
        Assert.Equal(before,TrainingRootClearState(session));
        await Until(()=>panel.HasActiveClearDecision&&Named<Border>(panel,"TrainingClearReviewOverlay").IsEffectivelyVisible,"Real original Clear Space must open review in actual Training context.");
        var cancel=TrainingRootClearAction(panel,"training-clear-cancel");var confirm=TrainingRootClearAction(panel,"training-clear-confirm");
        Assert.True(cancel.IsDefault);Assert.False(confirm.IsDefault);Assert.True(cancel.IsFocused);
        Assert.Equal(before,TrainingRootClearState(session));return(cancel,confirm);
    }

    [AvaloniaFact][Trait("Evidence","TrainingClearActualRootCommandGuards")]
    public async Task ActualRootCtrlKAndCachedClearInvocationCannotOpenConflictingReviewsInEitherOrder()
    {
        await using var scope=new StorageScope(true,null);var(panel,session)=await TrainingRootClearOpenTraining(scope);var main=scope.Main!;
        string state=TrainingRootClearState(session),afl=JsonSerializer.Serialize(Read<MatchManager>(main,"_match").ToState());
        int structural=0,exits=0;session.StructuralChanged+=()=>structural++;panel.ExitRequested+=()=>exits++;
        var command=Named<Grid>(main,"CommandOverlay");var clear=Named<Button>(panel,"ClearAllButton");
        var cachedProvider=Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(clear));
        var(cancel,confirm)=await TrainingRootClearOpenReview(main,panel,session);
        Assert.False(main.CanOpenTrainingClearReview);
        TrainingRootClearKey(main,PhysicalKey.K,RawInputModifiers.Control);await Pump(80);
        Assert.False(command.IsVisible);Assert.True(panel.HasActiveClearDecision);
        Assert.Same(cancel,TrainingRootClearAction(panel,"training-clear-cancel"));Assert.Same(confirm,TrainingRootClearAction(panel,"training-clear-confirm"));
        Assert.True(cancel.IsFocused);Assert.Equal(state,TrainingRootClearState(session));Assert.Equal(0,structural);Assert.Equal(0,exits);
        TrainingRootClearKey(main,PhysicalKey.Escape);await Pump(60);
        Assert.False(panel.HasActiveClearDecision);Assert.False(Named<Border>(panel,"TrainingClearReviewOverlay").IsVisible);Assert.True(main.CanOpenTrainingClearReview);

        TrainingRootClearKey(main,PhysicalKey.K,RawInputModifiers.Control);
        await Until(()=>command.IsEffectivelyVisible&&Named<TextBox>(main,"CommandSearch").IsFocused,"Real CtrlK must open command bar after cancelling review.");
        var search=Named<TextBox>(main,"CommandSearch");Assert.False(main.CanOpenTrainingClearReview);Assert.False(clear.IsEffectivelyEnabled);
        // Real cached native provider may refuse disabled input itself. Accept
        // only that standard provider contract, not arbitrary hidden failures.
        var refusal=Record.Exception(()=>cachedProvider.Invoke());
        Assert.True(refusal is null||refusal.GetType().Name=="ElementNotEnabledException",refusal?.ToString());await Pump(80);
        Assert.True(command.IsEffectivelyVisible);Assert.True(search.IsFocused);Assert.False(panel.HasActiveClearDecision);
        Assert.False(Named<Border>(panel,"TrainingClearReviewOverlay").IsVisible);Assert.Equal(state,TrainingRootClearState(session));
        // Controlled retained routedClick ALSO exercises the current original
        // handler/context guard beyond native disabled-Invoke rejection. This
        // is explicitly NOT a physical click on the disabled underlying panel.
        clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Pump(60);
        Assert.True(command.IsEffectivelyVisible);Assert.True(search.IsFocused);Assert.False(panel.HasActiveClearDecision);
        Assert.False(Named<Border>(panel,"TrainingClearReviewOverlay").IsVisible);Assert.Equal(state,TrainingRootClearState(session));
        Assert.Equal(afl,JsonSerializer.Serialize(Read<MatchManager>(main,"_match").ToState()));Assert.Equal(0,structural);Assert.Equal(0,exits);
        TrainingRootClearCapture(main,"training-clear-root-command-first");
        TrainingRootClearKey(main,PhysicalKey.Escape);await Pump(60);
        Assert.False(command.IsVisible);Assert.True(panel.IsEffectivelyEnabled);Assert.True(main.CanOpenTrainingClearReview);
        await TrainingRootClearOpenReview(main,panel,session);TrainingRootClearKey(main,PhysicalKey.Escape);await Pump(60);
        Assert.False(panel.HasActiveClearDecision);Assert.Equal(state,TrainingRootClearState(session));Assert.Equal(0,structural);Assert.Equal(0,exits);scope.AssertOwnedPaths();
    }

    [AvaloniaFact][Trait("Evidence","TrainingClearActualRootHomeResume")]
    public async Task CancelledClearReceiptStaysRetiredThroughActualHomeCommandAndResumeWithOutputOffAir()
    {
        await using var scope=new StorageScope(true,null);var(panel,session)=await TrainingRootClearOpenTraining(scope);var main=scope.Main!;
        string state=TrainingRootClearState(session),afl=JsonSerializer.Serialize(Read<MatchManager>(main,"_match").ToState());
        int structural=0,exits=0;session.StructuralChanged+=()=>structural++;panel.ExitRequested+=()=>exits++;
        var(_,oldConfirm)=await TrainingRootClearOpenReview(main,panel,session);
        // Active review intentionally has no public Home path: CtrlK is blocked.
        // Cancel legitimately, THEN navigate; no private GoHome/state/visibility
        // forcing is used to manufacture an active-review navigation scenario.
        TrainingRootClearKey(main,PhysicalKey.Escape);await Pump(60);Assert.False(panel.HasActiveClearDecision);
        var output=Read<Window?>(main,"_trainingDisplayWindow");Assert.NotNull(output);Assert.True(output.IsVisible);
        TrainingRootClearKey(main,PhysicalKey.K,RawInputModifiers.Control);
        var command=Named<Grid>(main,"CommandOverlay");var search=Named<TextBox>(main,"CommandSearch");
        await Until(()=>command.IsEffectivelyVisible&&search.IsFocused,"Real CtrlK must open the actual commandbar.");
        main.KeyTextInput("home");await Pump(100);Assert.Equal("home",search.Text);
        var results=Named<ListBox>(main,"CommandResults");
        var homeItem=Assert.Single(results.Items.OfType<ListBoxItem>(),item=>Equals(item.Tag,"home"));
        Assert.Same(homeItem,results.SelectedItem);Assert.True(search.IsFocused);
        TrainingRootClearKey(main,PhysicalKey.Enter);
        var home=Named<SportSelectionScreen>(main,"SportSelectionPanel");
        await Until(()=>home.IsEffectivelyVisible&&!command.IsVisible&&!panel.IsEffectivelyVisible,"Typed actual Home command must hide current Training and show the single Home.");
        Assert.False(panel.IsVisible);Assert.True(panel.IsLoaded);Assert.False(panel.HasActiveClearDecision);
        Assert.False(Named<Border>(panel,"TrainingClearReviewOverlay").IsVisible);
        Assert.Same(session,Read<TrainingSession>(main,"_trainingSession"));Assert.False(output.IsVisible);Assert.True(Read<bool>(main,"_outputActivationRequired"));
        Assert.Equal(state,TrainingRootClearState(session));Assert.Equal(0,structural);Assert.Equal(0,exits);
        oldConfirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Pump(80);
        Assert.True(home.IsEffectivelyVisible);Assert.False(panel.HasActiveClearDecision);Assert.Equal(state,TrainingRootClearState(session));
        var resume=Named<Button>(home,"HomeResumeButton");
        await Until(()=>resume.IsEffectivelyVisible&&resume.Bounds.Width>0,"Real Home current-session Resume must be mounted.");
        Assert.Equal("Return to current session",resume.Content);await Pump(520);
        RotationTap(main,resume);
        await Until(()=>panel.IsEffectivelyVisible&&!home.IsEffectivelyVisible,"Physical current-session Resume must restore the same Training console.");
        Assert.Same(session,Read<TrainingSession>(main,"_trainingSession"));Assert.Equal(state,TrainingRootClearState(session));
        Assert.Same(output,Read<Window?>(main,"_trainingDisplayWindow"));Assert.False(output.IsVisible);Assert.True(Read<bool>(main,"_outputActivationRequired"));
        Assert.False(panel.HasActiveClearDecision);Assert.False(Named<Border>(panel,"TrainingClearReviewOverlay").IsVisible);
        oldConfirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Pump(80);
        Assert.Equal(state,TrainingRootClearState(session));Assert.Equal(afl,JsonSerializer.Serialize(Read<MatchManager>(main,"_match").ToState()));
        Assert.Equal(0,structural);Assert.Equal(0,exits);
        TrainingRootClearCapture(main,"training-clear-root-home-resume-offair");
        await TrainingRootClearOpenReview(main,panel,session);TrainingRootClearKey(main,PhysicalKey.Escape);await Pump(50);
        Assert.False(panel.HasActiveClearDecision);Assert.Equal(state,TrainingRootClearState(session));Assert.False(output.IsVisible);Assert.Equal(0,structural);Assert.Equal(0,exits);scope.AssertOwnedPaths();
    }
}

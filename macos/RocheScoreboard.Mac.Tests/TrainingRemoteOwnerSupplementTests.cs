// OUTSIDE ONLY, UNCOMPILED/UNRUN. Three supplemental contracts, not four new
// bug reproductions. Reuses current actual Root / safe StorageScope helpers.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services.Cloud;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class BreakRotationModeIsolationTests
{
    private static readonly string[] TrainingOwnerAllTypes=
    [RemoteEventTypes.TrainingStartPause,RemoteEventTypes.TrainingSkip,RemoteEventTypes.TrainingResetActive,
     RemoteEventTypes.TrainingAddTimer,RemoteEventTypes.TrainingUpdateTimer,RemoteEventTypes.TrainingRemoveTimer,
     RemoteEventTypes.TrainingClearQueue,RemoteEventTypes.TrainingSetAutoAdvance,RemoteEventTypes.TrainingReorder,
     RemoteEventTypes.TrainingAdjustActive,RemoteEventTypes.TrainingExit];
    private static Dictionary<string,Func<bool>> TrainingOwnerCaptureAll(Roche_Scoreboard.MainWindow main)
    {
        Assert.Equal(11,TrainingOwnerAllTypes.Length);Assert.Equal(11,TrainingOwnerAllTypes.Distinct().Count());
        var scope=(IRemoteCommandScope)main;
        return TrainingOwnerAllTypes.ToDictionary(type=>type,type=>Assert.IsType<Func<bool>>(scope.CaptureExecutionGuard(type)));
    }

    [AvaloniaFact]
    public async Task AllElevenTrainingReceiptsTrackRuntimeIdentityAndNullReceiptsCannotAdoptLaterTraining()
    {
        await using var scope=new StorageScope(true,null);
        var(_,old)=await TrainingRootClearOpenTraining(scope);var main=scope.Main!;
        var current=TrainingOwnerCaptureAll(main);Assert.All(current.Values,guard=>Assert.True(guard()));
        ((IRemoteCommandTarget)main).TrainingExit();Assert.Null(Read<TrainingSession?>(main,"_trainingSession"));
        Assert.All(current.Values,guard=>Assert.False(guard()));
        var acceptedWithoutOwner=TrainingOwnerCaptureAll(main);Assert.All(acceptedWithoutOwner.Values,guard=>Assert.False(guard()));
        var replacement=await TrainingOwnerReenterHome(main,old);Assert.NotSame(old,replacement);
        Assert.All(current.Values,guard=>Assert.False(guard()));
        Assert.All(acceptedWithoutOwner.Values,guard=>Assert.False(guard()));
        var fresh=TrainingOwnerCaptureAll(main);Assert.All(fresh.Values,guard=>Assert.True(guard()));
        scope.AssertOwnedPaths();
    }

    [AvaloniaFact]
    public async Task RetainedGlobalSettingsKeepsAllReceiptsAndTheQueuedCurrentTrainingAdjustment()
    {
        await using var scope=new StorageScope(true,null);
        var(panel,session)=await TrainingRootClearOpenTraining(scope);var main=scope.Main!;
        var receipts=TrainingOwnerCaptureAll(main);string metadata=ExitQueueMetadata(session);
        var active=session.Active!;var duration=active.Duration;var remaining=active.Remaining;
        var callbacks=new Queue<Action>();var dispatcher=new RemoteCommandDispatcher((IRemoteCommandTarget)main,callbacks.Enqueue);
        TrainingOwnerEnqueueDuplicate(dispatcher,TrainingOwnerEnvelope(RemoteEventTypes.TrainingAdjustActive,new{seconds=8}));
        Assert.Single(callbacks);Assert.True(main.OpenCommandBar());Assert.True(main.ExecuteCommand("settings"));await Pump(80);
        Assert.True(Named<GlobalSettingsScreen>(main,"GlobalSettingsPanel").IsEffectivelyVisible);
        Assert.False(panel.IsEffectivelyVisible);Assert.Same(session,Read<TrainingSession>(main,"_trainingSession"));
        Assert.Equal("Training",Read<object>(main,"_sportMode").ToString());
        Assert.All(receipts.Values,guard=>Assert.True(guard()));
        Assert.Equal(metadata,ExitQueueMetadata(session));Assert.Equal(remaining,active.Remaining);
        Assert.Equal(1,TrainingOwnerDrain(callbacks));
        Assert.Same(active,session.Active);Assert.Equal(duration+TimeSpan.FromSeconds(8),active.Duration);
        Assert.Equal(remaining+TimeSpan.FromSeconds(8),active.Remaining);Assert.False(session.IsRunning);
        Assert.All(receipts.Values,guard=>Assert.True(guard()));scope.AssertOwnedPaths();
    }

    [AvaloniaFact]
    public async Task PublicTrainingExitAndActualAflWizardLeaveOldTrainingCommandsIneligibleWithoutChangingAfl()
    {
        await using var scope=new StorageScope(true,null);
        var(_,old)=await TrainingRootClearOpenTraining(scope);var main=scope.Main!;
        var receipts=TrainingOwnerCaptureAll(main);
        var callbacks=new Queue<Action>();var dispatcher=new RemoteCommandDispatcher((IRemoteCommandTarget)main,callbacks.Enqueue);
        TrainingOwnerEnqueueDuplicate(dispatcher,TrainingOwnerEnvelope(RemoteEventTypes.TrainingAdjustActive,new{seconds=9}));
        Assert.Single(callbacks);string oldBefore=TrainingRootClearState(old);
        ((IRemoteCommandTarget)main).TrainingExit();Assert.Null(Read<TrainingSession?>(main,"_trainingSession"));
        var home=Named<SportSelectionScreen>(main,"SportSelectionPanel");Assert.True(home.IsEffectivelyVisible);
        var start=Assert.Single(home.GetVisualDescendants().OfType<Button>(),button=>button.Name=="AflLaunchAction");
        await Until(()=>start.IsEffectivelyEnabled&&start.Bounds.Width>0,"Actual Home Start Game must be ready.");
        await Pump(520);RotationTap(main,start);
        var wizard=Named<SetupWizard>(main,"SetupWizardPanel");
        await Until(()=>Named<Control>(wizard,"SportStepContainer").IsEffectivelyVisible,"Actual unified sport step must open.");
        var afl=Named<RadioButton>(wizard,"AflSportChoice");afl.IsChecked=true;
        // Public routed native actions on the real wizard, not private Finalize
        // calls or assertions of physical pointer/keyboard coverage here.
        var next=Named<Button>(wizard,"NextButton");next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Pump(60);
        Named<TextBox>(wizard,"SetupHomeNameBox").Text="Ownership AFL home";Named<TextBox>(wizard,"SetupHomeAbbrBox").Text="OAH";
        Named<TextBox>(wizard,"SetupAwayNameBox").Text="Ownership AFL away";Named<TextBox>(wizard,"SetupAwayAbbrBox").Text="OAA";
        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Pump(60);
        Named<RadioButton>(wizard,"SetupCountdown").IsChecked=true;
        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Pump(60);
        Named<ComboBox>(wizard,"SetupWeatherLocation").Text=""; // No weather/network fixture.
        Assert.False(Read<bool>(main,"_aflMatchReady"));Assert.Null(Read<TrainingSession?>(main,"_trainingSession"));
        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(int count=0;count<180&&!Named<Control>(main,"MainContent").IsEffectivelyVisible;count++)await Pump(25);
        Assert.True(Named<Control>(main,"MainContent").IsEffectivelyVisible);Assert.False(wizard.IsEffectivelyVisible);
        Assert.Equal("AFL",Read<object>(main,"_sportMode").ToString());Assert.True(Read<bool>(main,"_aflMatchReady"));
        var match=Read<MatchManager>(main,"_match");Assert.Equal("Ownership AFL home",match.HomeName);Assert.False(match.ClockRunning);
        string aflBefore=JsonSerializer.Serialize(match.ToState());
        Assert.All(receipts.Values,guard=>Assert.False(guard()));
        var afterSwitch=TrainingOwnerCaptureAll(main);Assert.All(afterSwitch.Values,guard=>Assert.False(guard()));
        Assert.Equal(1,TrainingOwnerDrain(callbacks));Assert.Equal(oldBefore,TrainingRootClearState(old));
        Assert.Equal(aflBefore,JsonSerializer.Serialize(match.ToState()));scope.AssertOwnedPaths();
    }
}

// OUTSIDE ONLY: four actual production-route cases, UNCOMPILED/UNRUN.
// Existing safe StorageScope and actual Home Training route; no app-private
// handler invocation, new test hook, real network or native desktop claim.
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
    private static RemoteEvent TrainingOwnerEnvelope(string type,object? payload=null)=>new()
    {Id=Guid.NewGuid(),SessionId=Guid.NewGuid(),EventType=type,Payload=JsonSerializer.SerializeToElement(payload??new{})};
    private static void TrainingOwnerEnqueueDuplicate(RemoteCommandDispatcher dispatcher,RemoteEvent envelope)
    {dispatcher.Dispatch(envelope);dispatcher.Dispatch(envelope);}
    private static int TrainingOwnerDrain(Queue<Action> callbacks)
    {int count=0;while(callbacks.TryDequeue(out var action)){Assert.True(++count<50);action();}return count;}
    private static async Task<TrainingSession> TrainingOwnerReenterHome(Roche_Scoreboard.MainWindow main,TrainingSession previous)
    {
        var home=Named<SportSelectionScreen>(main,"SportSelectionPanel");
        await Until(()=>home.IsEffectivelyVisible,"Accepted old Exit must expose the real Home.");
        var launch=Assert.Single(home.GetVisualDescendants().OfType<Button>(),button=>button.Name=="TrainingLaunchAction");
        await Until(()=>launch.IsEffectivelyEnabled&&launch.Bounds.Width>0,"Actual current Training launch must be ready.");
        await Pump(520);RotationTap(main,launch);
        var panel=Named<TrainingControlPanel>(main,"TrainingControlPanelView");
        await Until(()=>panel.IsEffectivelyVisible&&!home.IsEffectivelyVisible&&Read<TrainingSession?>(main,"_trainingSession") is not null,
            "Actual Home Training must mount a replacement owner.");
        var current=Read<TrainingSession>(main,"_trainingSession");Assert.NotSame(previous,current);
        current.AddTimer(new TrainingTimer("Replacement owner active",TimeSpan.FromSeconds(95)));
        current.AddTimer(new TrainingTimer("Replacement owner queued",TimeSpan.FromSeconds(80)));
        Assert.False(current.IsRunning);main.Activate();await Pump(80);return current;
    }
    private static async Task TrainingOwnerReplacementBoundary(string type)
    {
        await using var scope=new StorageScope(true,null);
        var(panel,old)=await TrainingRootClearOpenTraining(scope);var main=scope.Main!;
        var target=(IRemoteCommandTarget)main;string oldBefore=TrainingRootClearState(old);
        var callbacks=new Queue<Action>();var dispatcher=new RemoteCommandDispatcher(target,callbacks.Enqueue);
        var stale=TrainingOwnerEnvelope(type);TrainingOwnerEnqueueDuplicate(dispatcher,stale);
        Assert.Single(callbacks);Assert.Equal(oldBefore,TrainingRootClearState(old));
        target.TrainingExit();Assert.Null(Read<TrainingSession?>(main,"_trainingSession"));Assert.False(old.IsRunning);
        var replacement=await TrainingOwnerReenterHome(main,old);string before=TrainingRootClearState(replacement);
        var output=Read<Window>(main,"_trainingDisplayWindow");Assert.True(output.IsVisible);
        int structure=0;replacement.StructuralChanged+=()=>structure++;
        Assert.Single(callbacks);Assert.Equal(1,TrainingOwnerDrain(callbacks));
        // FIRST rejection oracle: old Clear deletes replacement data; old Exit
        // disposes its owner. These do not depend on future APIs or part names.
        Assert.Same(replacement,Read<TrainingSession?>(main,"_trainingSession"));
        Assert.Equal(before,TrainingRootClearState(replacement));Assert.Equal(0,structure);
        Assert.Same(output,Read<Window?>(main,"_trainingDisplayWindow"));Assert.True(output.IsVisible);
        Assert.True(panel.IsEffectivelyVisible);Assert.Equal(oldBefore,TrainingRootClearState(old));
        dispatcher.Dispatch(stale);Assert.Empty(callbacks); // Existing ID dedupe preserved.
        var fresh=TrainingOwnerEnvelope(type);TrainingOwnerEnqueueDuplicate(dispatcher,fresh);
        Assert.Single(callbacks);Assert.Equal(1,TrainingOwnerDrain(callbacks));
        if(type==RemoteEventTypes.TrainingClearQueue)
        {
            Assert.Same(replacement,Read<TrainingSession>(main,"_trainingSession"));Assert.Empty(replacement.Timers);
            Assert.Equal(1,structure);Assert.True(panel.IsEffectivelyVisible);Assert.True(output.IsVisible);
            Assert.False(panel.HasActiveClearDecision);Assert.False(panel.HasActiveExitDecision);
        }
        else
        {
            Assert.Null(Read<TrainingSession?>(main,"_trainingSession"));Assert.Null(Read<Window?>(main,"_trainingDisplayWindow"));
            Assert.False(output.IsVisible);Assert.False(panel.IsEffectivelyVisible);Assert.False(replacement.IsRunning);
            Assert.True(Named<SportSelectionScreen>(main,"SportSelectionPanel").IsEffectivelyVisible);
            Assert.False(panel.HasActiveExitDecision);Assert.Equal(0,structure);
        }
        dispatcher.Dispatch(fresh);Assert.Empty(callbacks);scope.AssertOwnedPaths();
    }

    [AvaloniaFact]
    public Task QueuedOldTrainingClearCannotDeleteTheActualHomeReplacement()=>
        TrainingOwnerReplacementBoundary(RemoteEventTypes.TrainingClearQueue);

    [AvaloniaFact]
    public Task QueuedOldTrainingExitCannotDisposeTheActualHomeReplacement()=>
        TrainingOwnerReplacementBoundary(RemoteEventTypes.TrainingExit);

    [AvaloniaFact]
    public async Task RetainedHomeKeepsRapidOrderedAddSkipCommandsButClosedOwnerRejectsQueuedSkip()
    {
        await using var scope=new StorageScope(true,null);
        var(panel,session)=await TrainingRootClearOpenTraining(scope);var main=scope.Main!;
        var callbacks=new Queue<Action>();var dispatcher=new RemoteCommandDispatcher((IRemoteCommandTarget)main,callbacks.Enqueue);
        var commands=new[]{TrainingOwnerEnvelope(RemoteEventTypes.TrainingAddTimer,new{title="Rapid first",seconds=31}),
            TrainingOwnerEnvelope(RemoteEventTypes.TrainingAddTimer,new{title="Rapid second",seconds=47}),
            TrainingOwnerEnvelope(RemoteEventTypes.TrainingSetAutoAdvance,new{enabled=false}),
            TrainingOwnerEnvelope(RemoteEventTypes.TrainingSkip),TrainingOwnerEnvelope(RemoteEventTypes.TrainingSkip)};
        int uiThread=Environment.CurrentManagedThreadId,receiveThread=0;Exception? receiveError=null;
        var changedThreads=new List<int>();session.Changed+=()=>changedThreads.Add(Environment.CurrentManagedThreadId);
        // A bounded receive thread only enqueues; Join deliberately withholds
        // UI pumping until Dispatch returns. No native/network execution claim.
        var receiver=new System.Threading.Thread(()=>
        {
            try{receiveThread=Environment.CurrentManagedThreadId;foreach(var command in commands)TrainingOwnerEnqueueDuplicate(dispatcher,command);}
            catch(Exception error){receiveError=error;}
        }){IsBackground=true};
        receiver.Start();Assert.True(receiver.Join(TimeSpan.FromSeconds(5)));Assert.Null(receiveError);Assert.NotEqual(uiThread,receiveThread);
        Assert.Empty(changedThreads);Assert.Equal(5,callbacks.Count);Assert.Equal(2,session.Timers.Count);Assert.Equal(0,session.ActiveIndex);
        Assert.True(main.OpenCommandBar());Assert.True(main.ExecuteCommand("home"));await Pump(80);
        Assert.Same(session,Read<TrainingSession>(main,"_trainingSession"));Assert.False(panel.IsEffectivelyVisible);
        Assert.True(Named<SportSelectionScreen>(main,"SportSelectionPanel").IsEffectivelyVisible);
        var output=Read<Window>(main,"_trainingDisplayWindow");Assert.False(output.IsVisible);
        Assert.Equal(5,TrainingOwnerDrain(callbacks));
        Assert.Equal(5,changedThreads.Count);Assert.All(changedThreads,thread=>Assert.Equal(uiThread,thread));
        Assert.Equal(4,session.Timers.Count);Assert.Equal(2,session.ActiveIndex);Assert.False(session.AutoAdvance);Assert.False(session.IsRunning);
        Assert.Equal("Rapid first",session.Active!.Title);Assert.Equal("Rapid second",session.Timers[3].Title);
        Assert.True(session.Timers[0].HasFinished);Assert.True(session.Timers[1].HasFinished);
        Assert.Equal(TimeSpan.FromSeconds(31),session.Active.Remaining);Assert.Equal(TimeSpan.FromSeconds(47),session.Timers[3].Remaining);
        foreach(var command in commands)dispatcher.Dispatch(command);Assert.Empty(callbacks);
        var stale=TrainingOwnerEnvelope(RemoteEventTypes.TrainingSkip);TrainingOwnerEnqueueDuplicate(dispatcher,stale);Assert.Single(callbacks);
        bool closed=false;main.Closed+=(_,_)=>closed=true;main.Close();await Pump(60);Assert.True(closed);Assert.False(main.IsVisible);
        string afterClose=TrainingRootClearState(session);int structure=0;session.StructuralChanged+=()=>structure++;
        Assert.Equal(1,TrainingOwnerDrain(callbacks));
        // FIRST negative oracle after all positive route prerequisites. Original
        // target still reads its non-null root session and skips this third timer.
        Assert.Equal(afterClose,TrainingRootClearState(session));Assert.Equal(0,structure);
        dispatcher.Dispatch(stale);Assert.Empty(callbacks);scope.AssertOwnedPaths();
    }

    [AvaloniaFact]
    public async Task IntervalAddStopsItsContinuationWhenAStructuralObserverReplacesTheActualOwner()
    {
        await using var scope=new StorageScope(true,null);
        var(panel,old)=await TrainingRootClearOpenTraining(scope);var main=scope.Main!;
        var target=(IRemoteCommandTarget)main;
        var callbacks=new Queue<Action>();var dispatcher=new RemoteCommandDispatcher(target,callbacks.Enqueue);
        var envelope=TrainingOwnerEnvelope(RemoteEventTypes.TrainingAddTimer,
            new{interval=new{rounds=3,workSeconds=20,restSeconds=5,titlePrefix="Captured interval"}});
        TrainingOwnerEnqueueDuplicate(dispatcher,envelope);Assert.Single(callbacks);Assert.Equal(2,old.Timers.Count);
        bool replaced=false;TrainingSession? replacement=null;Window? output=null;string? before=null;int laterStructure=0;
        void Replace()
        {
            if(replaced||!old.Timers.Any(timer=>timer.Title=="Captured interval 1"))return;
            replaced=true;target.TrainingExit();Assert.Null(Read<TrainingSession?>(main,"_trainingSession"));
            var home=Named<SportSelectionScreen>(main,"SportSelectionPanel");Assert.True(home.IsEffectivelyVisible);
            var launch=Assert.Single(home.GetVisualDescendants().OfType<Button>(),button=>button.Name=="TrainingLaunchAction");
            // Controlled synchronous actual public action delegate, not a
            // physical click during the decorative entry-disabled period.
            launch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            replacement=Read<TrainingSession>(main,"_trainingSession");Assert.NotSame(old,replacement);
            replacement.AddTimer(new TrainingTimer("Replacement sentinel",TimeSpan.FromSeconds(121)));
            before=TrainingRootClearState(replacement);output=Read<Window>(main,"_trainingDisplayWindow");
            replacement.StructuralChanged+=()=>laterStructure++;
        }
        old.StructuralChanged+=Replace;
        try
        {
            Assert.Equal(1,TrainingOwnerDrain(callbacks));Assert.True(replaced);
            Assert.NotNull(replacement);Assert.NotNull(before);Assert.NotNull(output);
            Assert.Same(replacement,Read<TrainingSession>(main,"_trainingSession"));
            // FIRST continuation oracle: original code rereads _trainingSession
            // inside its loop and appends remaining work/rest into the new owner.
            Assert.Equal(before,TrainingRootClearState(replacement));Assert.Equal(0,laterStructure);
            Assert.Single(replacement.Timers);Assert.Equal("Replacement sentinel",replacement.Active!.Title);
            Assert.Equal(3,old.Timers.Count);Assert.Equal("Captured interval 1",old.Timers[2].Title);Assert.False(old.IsRunning);
            Assert.Same(output,Read<Window>(main,"_trainingDisplayWindow"));Assert.True(output.IsVisible);Assert.True(panel.IsEffectivelyVisible);
            var fresh=TrainingOwnerEnvelope(RemoteEventTypes.TrainingAddTimer,new{title="Fresh current input",seconds=19});
            TrainingOwnerEnqueueDuplicate(dispatcher,fresh);Assert.Single(callbacks);Assert.Equal(1,TrainingOwnerDrain(callbacks));
            Assert.Equal(2,replacement.Timers.Count);Assert.Equal("Fresh current input",replacement.Timers[1].Title);Assert.Equal(1,laterStructure);
            dispatcher.Dispatch(envelope);dispatcher.Dispatch(fresh);Assert.Empty(callbacks);scope.AssertOwnedPaths();
        }
        finally{old.StructuralChanged-=Replace;old.Pause();replacement?.Pause();}
    }
}

// OUTSIDE ONLY: UNCOMPILED/UNRUN. One actual Root-close contract for all direct
// targets, independent of dispatcher rejection and using valid existing IDs.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services.Cloud;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class BreakRotationModeIsolationTests
{
    [AvaloniaFact]
    public async Task ActualClosedRootRejectsEachOfElevenDirectTrainingTargetsAndEveryCapturedReceipt()
    {
        await using var scope=new StorageScope(true,null);
        var(_,session)=await TrainingRootClearOpenTraining(scope);var main=scope.Main!;
        var target=(IRemoteCommandTarget)main;
        Assert.False(session.IsRunning);Assert.Equal(2,session.Timers.Count);
        var active=session.Active!;var queued=session.Timers[1];
        Assert.NotEqual(Guid.Empty,active.Id);Assert.NotEqual(Guid.Empty,queued.Id);Assert.NotSame(active,queued);
        Assert.False(active.HasFinished);Assert.False(queued.HasFinished);Assert.Same(active,session.Timers[0]);
        var guards=TrainingOwnerCaptureAll(main);Assert.Equal(11,guards.Count);
        Assert.All(guards.Values,guard=>Assert.True(guard()));
        bool closed=false;main.Closed+=(_,_)=>closed=true;
        main.Close();await Pump(60);
        Assert.True(closed);Assert.False(main.IsVisible);
        // This prerequisite rules out a vacuous null-field rejection. Current
        // Root retains the actual model when its window closes; targets must
        // explicitly reject its closed lifetime despite valid IDs/data.
        Assert.Same(session,Read<TrainingSession?>(main,"_trainingSession"));
        Assert.False(session.IsRunning);Assert.Equal(2,session.Timers.Count);
        Assert.All(guards.Values,guard=>Assert.False(guard()));
        string baseline=TrainingRootClearState(session);var original=session.Timers.ToArray();
        int changed=0,structural=0;
        void Changed()=>changed++;void Structural()=>structural++;
        session.Changed+=Changed;session.StructuralChanged+=Structural;
        var actions=new (string Name,Action Run)[]
        {
            (RemoteEventTypes.TrainingStartPause,()=>target.TrainingStartPause()),
            (RemoteEventTypes.TrainingSkip,()=>target.TrainingSkip()),
            (RemoteEventTypes.TrainingResetActive,()=>target.TrainingResetActive()),
            (RemoteEventTypes.TrainingAddTimer,()=>target.TrainingAddTimer("Must not append on close",23,null)),
            (RemoteEventTypes.TrainingUpdateTimer,()=>target.TrainingUpdateTimer(active.Id,"Must not rename on close",41)),
            (RemoteEventTypes.TrainingRemoveTimer,()=>target.TrainingRemoveTimer(queued.Id)),
            (RemoteEventTypes.TrainingClearQueue,()=>target.TrainingClearQueue()),
            (RemoteEventTypes.TrainingSetAutoAdvance,()=>target.TrainingSetAutoAdvance(!session.AutoAdvance)),
            (RemoteEventTypes.TrainingReorder,()=>target.TrainingReorder(queued.Id,"up")),
            (RemoteEventTypes.TrainingAdjustActive,()=>target.TrainingAdjustActive(7)),
            (RemoteEventTypes.TrainingExit,()=>target.TrainingExit())
        };
        Assert.Equal(11,actions.Length);Assert.Equal(11,actions.Select(action=>action.Name).Distinct().Count());
        try
        {
            foreach(var action in actions)
            {
                Assert.Same(active,session.Active);Assert.Same(queued,session.Timers[1]);
                action.Run(); // Actual public target; dispatcher intentionally bypassed.
                Assert.True(baseline==TrainingRootClearState(session),action.Name+" must preserve the exact closed model.");
                Assert.Equal(0,changed);Assert.Equal(0,structural);
                Assert.Equal(original,session.Timers);Assert.Same(session,Read<TrainingSession?>(main,"_trainingSession"));
                Assert.False(session.IsRunning);Assert.All(guards.Values,guard=>Assert.False(guard()));
            }
            await Pump(100);
            Assert.Equal(baseline,TrainingRootClearState(session));Assert.Equal(0,changed);Assert.Equal(0,structural);
            scope.AssertOwnedPaths();
        }
        finally
        {
            session.Changed-=Changed;session.StructuralChanged-=Structural;
            // Cleanup only after an oracle/failure; never repair a missed guard.
            session.Pause();
        }
    }
}

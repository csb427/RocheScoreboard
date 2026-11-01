using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services.Cloud;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed partial class BreakRotationModeIsolationTests
{
    [AvaloniaTheory] [InlineData("single")] [InlineData("interval")] [InlineData("edit")] [InlineData("adjust")]
    [Trait("Evidence","TrainingRemoteAggregateCapacity")]
    public async Task BeyondTimeSpanCapacityRejectsRemoteMutationWithoutPartialQueueOrTitleChanges(string action)
    {
        await using var scope=new StorageScope(true,null);var(_,session)=await TrainingRootClearOpenTraining(scope);var target=(IRemoteCommandTarget)scope.Main!;
        session.Clear();
        // Two normal model objects with extreme durations. No large allocation
        // or claim that a phone actually supplied a multi-billion-day session.
        var active=new TrainingTimer("Capacity witness",TimeSpan.MaxValue-TimeSpan.FromSeconds(30));
        var queued=new TrainingTimer("Queued witness",TimeSpan.FromSeconds(5));session.AddTimer(active);session.AddTimer(queued);
        string before=TrainingRootClearState(session);int changes=0,structural=0;void Changed()=>changes++;void Structural()=>structural++;session.Changed+=Changed;session.StructuralChanged+=Structural;
        try
        {
            var error=Record.Exception(()=>
            {
                if(action=="single")target.TrainingAddTimer("Rejected single",60,null);
                else if(action=="interval")target.TrainingAddTimer("",0,new TrainingIntervalSpec(2,20,20,"Rejected set"));
                else if(action=="edit")target.TrainingUpdateTimer(queued.Id,"Must not rename",60);
                else target.TrainingAdjustActive(60);
            });
            Assert.Null(error);Assert.Equal(before,TrainingRootClearState(session));Assert.Equal(0,changes);Assert.Equal(0,structural);
            Assert.Equal(TimeSpan.MaxValue-TimeSpan.FromSeconds(25),session.TotalDuration);Assert.False(session.IsRunning);
            // A fitting fresh request must still be accepted after rejection.
            target.TrainingAddTimer("Fits",2,null);Assert.Equal(3,session.Timers.Count);Assert.Equal("Fits",session.Timers[2].Title);
            Assert.Equal(TimeSpan.MaxValue-TimeSpan.FromSeconds(23),session.TotalDuration);Assert.True(changes>0);Assert.True(structural>0);
            target.TrainingAddTimer("Exact fit",23,null);Assert.Equal(TimeSpan.MaxValue,session.TotalDuration);Assert.Equal(4,session.Timers.Count);
            string full=TrainingRootClearState(session);int changesAtCapacity=changes,structuralAtCapacity=structural;
            target.TrainingAddTimer("One second too far",1,null);Assert.Equal(full,TrainingRootClearState(session));Assert.Equal(changesAtCapacity,changes);Assert.Equal(structuralAtCapacity,structural);scope.AssertOwnedPaths();
        }
        finally{session.Changed-=Changed;session.StructuralChanged-=Structural;session.Clear();}
    }
}

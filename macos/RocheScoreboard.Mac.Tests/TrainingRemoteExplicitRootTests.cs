using System.Text.Json;
using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Services.Cloud;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed partial class BreakRotationModeIsolationTests
{
    [AvaloniaTheory] [InlineData("missing-auto")] [InlineData("missing-direction")] [InlineData("direct-direction")]
    [Trait("Evidence","TrainingRemoteExplicitRoot")]
    public async Task InvalidIntentPreservesTheActualTrainingOwnerAndFreshExplicitInputWorks(string kind)
    {
        await using var scope=new StorageScope(true,null);var(_,session)=await TrainingRootClearOpenTraining(scope);var target=(IRemoteCommandTarget)scope.Main!;target.TrainingSetAutoAdvance(false);var first=session.Timers[0];string before=TrainingRootClearState(session);int changes=0,structure=0;void Changed()=>changes++;void Structural()=>structure++;session.Changed+=Changed;session.StructuralChanged+=Structural;
        try
        {
            if(kind=="direct-direction")target.TrainingReorder(first.Id,"sideways");
            else
            {
                var dispatcher=new RemoteCommandDispatcher(target,a=>a());dispatcher.Dispatch(new RemoteEvent{Id=Guid.NewGuid(),EventType=kind=="missing-auto"?RemoteEventTypes.TrainingSetAutoAdvance:RemoteEventTypes.TrainingReorder,Payload=kind=="missing-auto"?JsonSerializer.SerializeToElement(new{}):JsonSerializer.SerializeToElement(new{id=first.Id})});
            }
            Assert.Equal(before,TrainingRootClearState(session));Assert.Equal(0,changes);Assert.Equal(0,structure);
            if(kind=="missing-auto"){target.TrainingSetAutoAdvance(true);Assert.True(session.AutoAdvance);}else{target.TrainingReorder(first.Id,"down");Assert.Same(first,session.Timers[1]);Assert.True(structure>0);}
            scope.AssertOwnedPaths();
        }
        finally{session.Changed-=Changed;session.StructuralChanged-=Structural;}
    }
}

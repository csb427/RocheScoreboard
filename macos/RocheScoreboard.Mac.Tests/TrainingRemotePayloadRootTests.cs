using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Services.Cloud;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed partial class BreakRotationModeIsolationTests
{
    [AvaloniaFact]
    [Trait("Evidence","TrainingRemotePayloadRoot")]
    public async Task InvalidDirectTrainingEditsKeepActualQueueAndValidInputStillWorks()
    {
        await using var scope=new StorageScope(true,null);var(_,session)=await TrainingRootClearOpenTraining(scope);var main=scope.Main!;
        var target=(IRemoteCommandTarget)main;var active=session.Active!;string before=TrainingRootClearState(session);int changed=0,structural=0;
        void Changed()=>changed++;void Structural()=>structural++;session.Changed+=Changed;session.StructuralChanged+=Structural;
        try
        {
            target.TrainingUpdateTimer(active.Id,"Must not rename",0);
            Assert.Equal(before,TrainingRootClearState(session));Assert.Equal(0,changed);Assert.Equal(0,structural);
            target.TrainingAddTimer("Must not append",0,new TrainingIntervalSpec(101,20,0,"Set"));
            Assert.Equal(before,TrainingRootClearState(session));Assert.Equal(0,changed);Assert.Equal(0,structural);
            target.TrainingUpdateTimer(active.Id,"Valid edit",30);
            Assert.Equal("Valid edit",active.Title);Assert.Equal(TimeSpan.FromSeconds(30),active.Duration);Assert.True(changed>0);Assert.True(structural>0);
            scope.AssertOwnedPaths();
        }
        finally{session.Changed-=Changed;session.StructuralChanged-=Structural;}
    }
}

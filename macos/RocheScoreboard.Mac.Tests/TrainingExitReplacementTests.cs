using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services.Cloud;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class BreakRotationModeIsolationTests
{
    [AvaloniaTheory][InlineData("training")][InlineData("afl")][InlineData("cricket")]
    public async Task OldPauseReplacementMakesEachSupersededOuterSetupReturnBeforeOverwritingTheNewOwner(string outer)
    {
        await using var scope=new StorageScope(true,null);var(panel,old)=await TrainingRootClearOpenTraining(scope);var main=scope.Main!;
        var target=(IRemoteCommandTarget)main;target.TrainingStartPause();Assert.True(old.IsRunning);
        string oldMetadata=ExitQueueMetadata(old);var oldOutput=Read<Window>(main,"_trainingDisplayWindow");
        TrainingSession? replacement=null;Window? replacementOutput=null;int stops=0;
        // Controlled synchronous callback contract: invoke the actual production
        // entry/finalization methods to exercise all three private caller return
        // guards. This is NOT a claim of physical wizard input or native GUI.
        void ProductionCall(string name,params object[] args)=>main.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,args);
        void Paused()
        {
            if(old.IsRunning)return;if(++stops!=1)return;
            Assert.Null(Read<TrainingSession?>(main,"_trainingSession"));
            ProductionCall("EnterTrainingMode");replacement=Read<TrainingSession>(main,"_trainingSession");Assert.NotSame(old,replacement);
            replacement.AddTimer(new TrainingTimer("New callback owner",TimeSpan.FromMinutes(4)));
            replacementOutput=Read<Window>(main,"_trainingDisplayWindow");Assert.NotSame(oldOutput,replacementOutput);
        }
        old.Changed+=Paused;
        try
        {
            if(outer=="afl")ProductionCall("FinalizeAflSetup",new SetupResult{HomeName="Superseded outer",AwayName="Must not appear",WeatherLocation=""});
            else if(outer=="cricket")
            {
                var setup=new CricketSetupResult{TeamAName="Superseded outer",TeamBName="Must not appear",WeatherLocation=""};setup.FillEmptySquads();
                ProductionCall("FinalizeCricketSetup",setup,false);
            }
            else ProductionCall("EnterTrainingMode");
            Assert.Equal(1,stops);Assert.False(old.IsRunning);Assert.NotNull(replacement);Assert.NotNull(replacementOutput);
            Assert.Same(replacement,Read<TrainingSession>(main,"_trainingSession"));Assert.Equal("Training",Read<object>(main,"_sportMode").ToString());
            Assert.Same(replacementOutput,Read<Window>(main,"_trainingDisplayWindow"));Assert.True(replacementOutput.IsVisible);Assert.True(panel.IsEffectivelyVisible);
            Assert.False(oldOutput.IsVisible);Assert.Equal(oldMetadata,ExitQueueMetadata(old));Assert.Single(replacement.Timers);
            Assert.Equal("New callback owner",replacement.Active!.Title);Assert.False(replacement.IsRunning);
            var banked=old.ActiveRemaining;await Pump(160);
            Assert.Equal(banked,old.ActiveRemaining);Assert.Same(replacement,Read<TrainingSession>(main,"_trainingSession"));
            Assert.Same(replacementOutput,Read<Window>(main,"_trainingDisplayWindow"));scope.AssertOwnedPaths();
        }
        finally{old.Changed-=Paused;old.Pause();replacement?.Pause();}
    }
}

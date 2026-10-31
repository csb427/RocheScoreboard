using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class OperationalTrustTests
{
    [Theory]
    [InlineData("clean","source aaaaaaaaaaaa · clean checkout at build")]
    [InlineData("modified","LOCAL CHANGES · based on aaaaaaaaaaaa")]
    [InlineData("unknown","source aaaaaaaaaaaa · local/unverified build")]
    public void BuildIdentitySeparatesCleanLocalAndUnknownState(string state,string expected)
    {
        var id=Guid.Parse("12345678-1234-1234-1234-123456789abc");
        string text=BuildIdentity.Format("5.0.0",new string('a',40),state,id);
        Assert.Contains("v5.0.0",text);Assert.Contains(expected,text);Assert.Contains("build 123456781234",text);
        if(state!="clean")Assert.DoesNotContain("clean checkout",text);
    }
    [Theory]
    [InlineData(null,null)]
    [InlineData("C:\\private\\version","/private/secret")]
    [InlineData("5.0.0","invalid-revision")]
    public void InvalidOrMissingBuildMetadataDoesNotLeakItsRawValue(string? version,string? revision)
    {
        string text=BuildIdentity.Format(version,revision,"clean",Guid.Empty);
        Assert.Contains("source unknown · local/unverified build",text);Assert.Contains("build unknown",text);
        Assert.DoesNotContain("private",text);Assert.DoesNotContain("secret",text);Assert.DoesNotContain("invalid-revision",text);Assert.DoesNotContain("clean checkout",text);
    }
    [AvaloniaTheory]
    [InlineData(900,650)][InlineData(420,320)]
    public async Task SettingsExposeASelectableBuildIdentifierInGeneralSettings(int width,int height)
    {
        var view=new GlobalSettingsScreen();var window=new NativeWindow{Width=width,Height=height,Content=view};window.Show();
        try
        {
            await Task.Delay(550);Dispatcher.UIThread.RunJobs();var field=view.FindControl<TextBox>("BuildInfoBox")!;
            Assert.Equal(BuildIdentity.Description,field.Text);
            string? state=typeof(BuildIdentity).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a=>a.Key=="RocheSourceState")?.Value;
            Assert.Contains(state=="modified"?"LOCAL CHANGES":state=="clean"?"clean checkout at build":"local/unverified build",field.Text);
            Assert.Contains("v"+typeof(BuildIdentity).Assembly.GetName().Version!.ToString(3),field.Text);
            Assert.True(field.IsReadOnly);field.Focus();field.BringIntoView();await Task.Delay(250);Dispatcher.UIThread.RunJobs();
            Assert.True(field.IsEffectivelyVisible);Assert.True(field.Bounds.Width>150);
            var workspace=view.FindControl<SettingsWorkspace>("SettingsWorkspace")!;
            Assert.True(field.Bounds.Height<=workspace.Scroller.Bounds.Height+.1,"Build identifier field must fit the short settings viewport");
            string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.GetTempPath();Directory.CreateDirectory(dir);
            using var image=window.CaptureRenderedFrame();Assert.NotNull(image);image.Save(Path.Combine(dir,$"build-info-{width}x{height}.png"));
        }
        finally{window.Close();}
    }
    [Fact]
    public void AnAflSnapshotDoesNotChangeWhenTheLiveEventIsCorrected()
    {
        var match=new MatchManager();match.AddGoal(TeamSide.Home);var saved=match.ToState();
        var timestamp=saved.Events![0].Timestamp;
        Assert.True(match.ModifyEvent(0,TeamSide.Away,ScoreType.Behind,TimeSpan.FromSeconds(20)));
        Assert.Equal(1,saved.HomeGoals);Assert.Equal(ScoreType.Goal,saved.Events[0].Type);Assert.Equal(TeamSide.Home,saved.Events[0].Team);
        Assert.Equal(1,saved.Events[0].HomeGoals);Assert.Equal(timestamp,saved.Events[0].Timestamp);
        var reopened=new MatchManager();reopened.LoadState(saved);Assert.Equal(6,reopened.HomeTotal);Assert.Equal(0,reopened.AwayTotal);
    }
    [Fact]
    public void LoadingAnAflSnapshotDoesNotMutateItsEventsOrShareThemWithTheMatch()
    {
        var source=new MatchManager();source.AddGoal(TeamSide.Home);var saved=source.ToState();saved.Events![0].Quarter=99;
        string before=JsonSerializer.Serialize(saved);var restored=new MatchManager();restored.LoadState(saved);
        Assert.Equal(before,JsonSerializer.Serialize(saved));
        Assert.True(restored.ModifyEvent(0,TeamSide.Away,ScoreType.Behind,TimeSpan.FromSeconds(10)));
        Assert.Equal(before,JsonSerializer.Serialize(saved));Assert.Equal(6,source.HomeTotal);
    }
    [Fact]
    public void UndoThenRescoreAtTheSameDeliveryCountCannotAcceptAnOldScorecardDraft()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(1,CricketDeliveryType.Runs);
        var draft=match.CreateScorecardDraft(1);Assert.True(match.UndoLastDelivery());match.RecordDelivery(6,CricketDeliveryType.Six);
        string before=JsonSerializer.Serialize(match.ToState());int events=0;match.MatchChanged+=()=>events++;
        Assert.False(match.TryApplyScorecard(draft,out string error));Assert.Contains("match changed",error);
        Assert.Equal(before,JsonSerializer.Serialize(match.ToState()));Assert.Equal(0,events);Assert.Equal(6,match.TotalRuns);
    }
    [Fact]
    public void RejectedUndoDoesNotInvalidateAnOtherwiseCurrentDraft()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);var draft=match.CreateScorecardDraft(1);int version=match.DeliveryRevision,events=0;match.MatchChanged+=()=>events++;
        Assert.False(match.UndoLastDelivery());Assert.Equal(version,match.DeliveryRevision);Assert.Empty(match.ValidateScorecardStructure(draft));Assert.Equal(0,events);
    }
    [Fact]
    public void CricketCorrectionReopenAndUndoKeepTheirIndependentCheckpoints()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(1,CricketDeliveryType.Runs);
        var draft=match.CreateScorecardDraft(1);draft.TotalRuns=2;draft.Batters[0].Runs=2;draft.Bowlers[0].RunsConceded=2;
        Assert.True(match.TryApplyScorecard(draft,out string error),error);match.RecordDelivery(0,CricketDeliveryType.Dot);
        var restored=CricketReliabilityChecks.CreateMatch(true);restored.LoadState(JsonSerializer.Deserialize<CricketSerializableState>(JsonSerializer.Serialize(match.CapturePersistenceState()))!);
        Assert.True(restored.UndoLastDelivery());Assert.Equal(2,restored.TotalRuns);Assert.False(restored.UndoLastDelivery());
        Assert.True(restored.TryRevertLatestScorecardCorrection(1,out error),error);Assert.Equal(1,restored.TotalRuns);
        Assert.True(restored.UndoLastDelivery());Assert.Equal(0,restored.TotalRuns);Assert.Empty(restored.CurrentInnings!.Deliveries);
    }
    [Fact]
    public void AflBreakSnapshotRemainsStableAcrossLiveCorrectionsAndRepeatedEndAfterReload()
    {
        var match=new MatchManager();match.AddGoal(TeamSide.Home);Assert.True(match.EndQuarter());var saved=match.ToState();
        Assert.True(match.ModifyEvent(0,TeamSide.Away,ScoreType.Behind,TimeSpan.Zero));
        var reopened=new MatchManager();reopened.LoadState(saved);Assert.Equal(2,reopened.Quarter);Assert.Equal(6,reopened.HomeTotal);
        int events=0;reopened.MatchChanged+=()=>events++;Assert.False(reopened.EndQuarter());Assert.Equal(2,reopened.Quarter);Assert.Equal(0,events);
        Assert.True(reopened.ContinueQuarter());Assert.Equal(1,reopened.Quarter);Assert.True(reopened.UndoLastScore());Assert.Equal(0,reopened.HomeTotal);
    }
    [Fact]
    public void RepairingThePrimaryRecoveryFilePreservesTheValidBackup()
    {
        string dir=Path.Combine(Path.GetTempPath(),"roche-batch-6-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        string path=Path.Combine(dir,"game_state.json");
        try
        {
            var expected=new GameState{Sport="afl",MatchState=new MatchManager().ToState()};
            string valid=GameStateService.SerializeRecoverySnapshot(expected);File.WriteAllText(path,"{broken");File.WriteAllText(path+".bak",valid);
            Assert.Equal("afl",GameStateService.LoadFrom(path)!.Sport);
            Assert.Equal(valid,File.ReadAllText(path+".bak"));Assert.Equal("afl",JsonSerializer.Deserialize<GameState>(File.ReadAllText(path))!.Sport);
            File.WriteAllText(path,"{second interruption");Assert.Equal("afl",GameStateService.LoadFrom(path)!.Sport);
        }
        finally{Directory.Delete(dir,true);}
    }
    [Fact]
    public void FailedPrimaryRepairKeepsTheBackupAndCleansItsTemporaryFile()
    {
        string dir=Path.Combine(Path.GetTempPath(),"roche-batch-6-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        string path=Path.Combine(dir,"game_state.json");Directory.CreateDirectory(path);
        try
        {
            string valid=GameStateService.SerializeRecoverySnapshot(new GameState{Sport="cricket",CricketMatchState=CricketReliabilityChecks.CreateMatch(true).CapturePersistenceState()});File.WriteAllText(path+".bak",valid);
            Assert.Equal("cricket",GameStateService.LoadFrom(path)!.Sport);Assert.Equal(valid,File.ReadAllText(path+".bak"));
            Assert.Empty(Directory.GetFiles(dir,"*.tmp"));Assert.True(Directory.Exists(path));
        }
        finally{Directory.Delete(dir,true);}
    }
    [Fact]
    public void EntirelyInvalidRecoveryFilesAreNotReplacedWithAnEmptyMatch()
    {
        string dir=Path.Combine(Path.GetTempPath(),"roche-batch-6-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        string path=Path.Combine(dir,"game_state.json");
        try
        {
            File.WriteAllText(path,"{primary invalid");File.WriteAllText(path+".bak","{backup invalid");
            Assert.Null(GameStateService.LoadFrom(path));Assert.Equal("{primary invalid",File.ReadAllText(path));Assert.Equal("{backup invalid",File.ReadAllText(path+".bak"));
        }
        finally{Directory.Delete(dir,true);}
    }
    [AvaloniaFact]
    public async Task AnInningsConfirmationDoesNotActOnPlayRecordedWhileItWasOpen()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);
        var window=new NativeWindow{Width=800,Height=600,Content=panel};window.Show();
        async Task Pump(){await Task.Delay(400);Dispatcher.UIThread.RunJobs();}
        Button Button(string tag)=>panel.GetVisualDescendants().OfType<Button>().First(b=>Equals(b.Tag,tag)&&b.IsEffectivelyVisible);
        try
        {
            await Pump();var task=(Task<bool>)typeof(CricketControlPanel).GetMethod("OpenInningsDecisionAsync",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(panel,[false,true])!;
            await Pump();Button("b").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));await Pump();
            match.RecordDelivery(1,CricketDeliveryType.Runs);string before=JsonSerializer.Serialize(match.ToState());
            Button("yes").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));await Pump();
            Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="Innings changed");
            Button("close").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));await Pump();await task;
            Assert.Equal(before,JsonSerializer.Serialize(match.ToState()));Assert.Equal(1,match.CurrentInningsNumber);
        }
        finally{panel.ClearMatch();window.Close();}
    }
}

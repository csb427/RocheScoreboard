// OUTSIDE PREPARATION ONLY — UNCOMPILED/UNRUN. Root owns integration/verification.
// Three actual public-route cases; real dispatcher/wall time, no private Tick,
// deadline mutation, DEBUG preview, mock clock or direct visibility forcing.
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class BreakRotationModeIsolationTests
{
    private sealed record RotatorFixture(Roche_Scoreboard.MainWindow Main, CricketControlPanel Panel,
        CricketMatchManager Match, Window Output);
    private static async Task RotatorRealWait(int milliseconds)
    {
        var watch=Stopwatch.StartNew();
        while(watch.ElapsedMilliseconds<milliseconds)await Pump(100);
    }
    private static async Task RotatorInvoke(Control control)
    {
        // Home/sheet entry can deliberately disable a visible action. Wait
        // for its real loaded/visible/enabled state; invoke exactly once.
        await Until(()=>control.IsLoaded&&control.IsEffectivelyVisible&&control.IsEffectivelyEnabled,
            "The original action must finish entry and actually accept input.");
        Assert.True(control.IsLoaded);Assert.True(control.IsEffectivelyVisible);Assert.True(control.IsEffectivelyEnabled);
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(control)).Invoke();
    }
    private static string RotatorPresentation(CricketControlPanel panel)=>Read<string>(panel,"_currentPresentation");
    private static string RotatorState(CricketMatchManager model)=>JsonSerializer.Serialize(model.CapturePersistenceState());
    private static void RotatorEvidence(string name,object value)
    {
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??throw new InvalidOperationException("Explicit private capture directory required.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,name+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
    }
    private static async Task RotatorCommand(Roche_Scoreboard.MainWindow main,string query,string expectedId)
    {
        RotationKey(main,PhysicalKey.K,RawInputModifiers.Control);
        var command=Named<Grid>(main,"CommandOverlay");var search=Named<TextBox>(main,"CommandSearch");
        await Until(()=>command.IsEffectivelyVisible&&search.IsFocused,"Real CtrlK must expose the live command search.");
        main.KeyTextInput(query);await Pump(100);
        var results=Named<ListBox>(main,"CommandResults");
        var intended=Assert.Single(results.Items.OfType<ListBoxItem>(),item=>Equals(item.Tag,expectedId));
        Assert.Same(intended,results.SelectedItem);Assert.True(search.IsFocused);
        RotationKey(main,PhysicalKey.Enter);
        await Until(()=>!command.IsVisible,"Actual Enter must execute and retire only current command.");
    }
    private static async Task<RotatorFixture> RotatorOpenRestoredBreak(StorageScope scope)
    {
        scope.AssertOwnedPaths();
        var seed=CricketReliabilityChecks.CreateMatch(true);
        seed.SetBreak(CricketBreakKind.Lunch,"Rotation lifetime comparison");
        Assert.False(seed.TossPending);Assert.False(seed.IsMatchFinalized);
        GameStateService.Save(new GameState{Sport="cricket",CricketMatchState=seed.CapturePersistenceState(),WeatherLocation=""});
        scope.Main=new Roche_Scoreboard.MainWindow{Width=1280,Height=850};var main=scope.Main;main.Show();
        var home=Named<SportSelectionScreen>(main,"SportSelectionPanel");
        await Until(()=>home.IsEffectivelyVisible,"Actual Home must show recoverable match.");
        home.PrepareForDisplay();
        var resume=Named<Button>(home,"HomeResumeButton");
        await Until(()=>resume.IsEffectivelyVisible,"Saved private match must expose real Home Resume.");
        await RotatorInvoke(resume);
        var recovery=Named<Grid>(main,"RecoveryOverlay");
        await Until(()=>recovery.IsEffectivelyVisible&&recovery.Children.OfType<ResumeMatchPrompt>().Any(),"Home Resume must open actual owned recovery choice.");
        var prompt=Assert.Single(recovery.Children.OfType<ResumeMatchPrompt>());
        await RotatorInvoke(Named<Button>(prompt,"ResumeButton"));
        var panel=Named<CricketControlPanel>(main,"CricketControlPanelView");
        await Until(()=>panel.IsEffectivelyVisible&&!recovery.IsVisible&&!home.IsVisible,"Actual Continue Match must restore Cricket.");
        var match=Read<CricketMatchManager>(main,"_cricketMatch");
        var output=Read<Window>(main,"_cricketDisplayWindow");
        Assert.Same(match,Read<CricketMatchManager>(panel,"_match"));
        Assert.Equal(CricketBreakKind.Lunch,match.BreakKind);Assert.True(panel.BreakPresentationRotationEnabled);
        Assert.False(output.IsVisible);Assert.True(Read<bool>(main,"_outputActivationRequired"));
        scope.AssertOwnedPaths();return new(main,panel,match,output);
    }
    private static async Task RotatorEnable(RotatorFixture f)
    {
        await RotatorCommand(f.Main,"show output window","enable-output");
        await Until(()=>f.Output.IsVisible,"Only deliberate output command must enable output.");
        Assert.False(Read<bool>(f.Main,"_outputActivationRequired"));
    }

    [AvaloniaFact][Trait("Evidence","CricketBreakRotationActualHomeOffAir")]
    public async Task RetainedHomeAndOffAirResumeCannotAdvanceAutomaticCricketPresentation()
    {
        await using var scope=new StorageScope(true,true);var f=await RotatorOpenRestoredBreak(scope);await RotatorEnable(f);
        var emissions=new List<string>();f.Panel.PresentationChanged+=emissions.Add;
        string presentation=RotatorPresentation(f.Panel),state=RotatorState(f.Match);
        await RotatorCommand(f.Main,"home","home");
        var home=Named<SportSelectionScreen>(f.Main,"SportSelectionPanel");
        await Until(()=>home.IsEffectivelyVisible&&!f.Panel.IsVisible,"Actual Home command must collapse retained Cricket panel.");
        Assert.True(f.Panel.IsLoaded);Assert.Same(f.Match,Read<CricketMatchManager>(f.Main,"_cricketMatch"));
        Assert.False(f.Output.IsVisible);Assert.True(Read<bool>(f.Main,"_outputActivationRequired"));
        emissions.Clear();await RotatorRealWait(26_000); // Cross actual24s+1s dispatcher cadence.
        RotatorEvidence("cricket-rotation-home-offair",new{presentation,actual=RotatorPresentation(f.Panel),emissions,outputVisible=f.Output.IsVisible});
        Assert.Equal(presentation,RotatorPresentation(f.Panel));Assert.Empty(emissions);
        Assert.False(f.Output.IsVisible);Assert.Equal(state,RotatorState(f.Match));
        var resume=Named<Button>(home,"HomeResumeButton");await Until(()=>resume.IsEffectivelyVisible,"Current-session Resume must remain reachable.");
        await RotatorInvoke(resume);await Until(()=>f.Panel.IsEffectivelyVisible&&!home.IsVisible,"Current Resume must restore same retained panel.");
        await RotatorRealWait(3_000);
        Assert.Empty(emissions);Assert.Equal(presentation,RotatorPresentation(f.Panel));Assert.False(f.Output.IsVisible);
        Assert.True(Read<bool>(f.Main,"_outputActivationRequired"));Assert.Equal(state,RotatorState(f.Match));scope.AssertOwnedPaths();
    }

    [AvaloniaFact][Trait("Evidence","CricketBreakRotationOnAirSettingsPositive")]
    public async Task OnAirCricketBreakKeepsRotatingWhileOperatorUsesGlobalSettings()
    {
        await using var scope=new StorageScope(true,true);var f=await RotatorOpenRestoredBreak(scope);await RotatorEnable(f);
        string before=RotatorPresentation(f.Panel),state=RotatorState(f.Match);var emissions=new List<string>();f.Panel.PresentationChanged+=emissions.Add;
        await RotatorCommand(f.Main,"application settings","settings");
        var settings=Named<GlobalSettingsScreen>(f.Main,"GlobalSettingsPanel");
        await Until(()=>settings.IsEffectivelyVisible&&!f.Panel.IsVisible,"Actual settings route must hide retained panel.");
        Assert.True(f.Panel.IsLoaded);Assert.True(f.Output.IsVisible);Assert.False(Read<bool>(f.Main,"_outputActivationRequired"));
        emissions.Clear();await RotatorRealWait(26_000);
        RotatorEvidence("cricket-rotation-settings-onair",new{before,actual=RotatorPresentation(f.Panel),emissions,outputVisible=f.Output.IsVisible});
        Assert.Single(emissions);Assert.NotEqual(before,RotatorPresentation(f.Panel));Assert.True(f.Output.IsVisible);
        Assert.Equal(state,RotatorState(f.Match));
        await RotatorInvoke(Named<Button>(settings,"BackButton"));
        await Until(()=>f.Panel.IsEffectivelyVisible&&!settings.IsVisible,"Real Back returns to same cricket panel.");
        Assert.Equal(emissions.Single(),RotatorPresentation(f.Panel));Assert.True(f.Output.IsVisible);scope.AssertOwnedPaths();
    }

    private sealed record RotatorObservedEmission(string Target,double AtSeconds);
    [AvaloniaFact][Trait("Evidence","CricketBreakRotationRealDwell")]
    public async Task ExplicitOutputStartsFreshTwentyFourSecondDwellAndManualSelectionResetsIt()
    {
        await using var scope=new StorageScope(true,true);var f=await RotatorOpenRestoredBreak(scope);
        var clock=Stopwatch.StartNew();var emissions=new List<RotatorObservedEmission>();
        f.Panel.PresentationChanged+=target=>emissions.Add(new(target,clock.Elapsed.TotalSeconds));
        await RotatorRealWait(5_000);Assert.Empty(emissions);Assert.False(f.Output.IsVisible);
        double enableRequestAt=clock.Elapsed.TotalSeconds;
        await RotatorEnable(f);emissions.Clear();
        await RotatorRealWait(23_000);
        // If dispatcher scheduling overshoots23s, a legitimate late event is
        // allowed. Its actual observed timestamp still must not precede24s
        // from the enabling request. No assertion treats scheduler lag as
        // proof that a valid event was early.
        Assert.All(emissions,entry=>Assert.True(entry.AtSeconds-enableRequestAt>=24,
            "Automatic event before fresh output dwell: "+entry));
        // Witness the first natural expiry as well as its lower-bound guard.
        for(int i=0;i<60&&emissions.Count==0;i++)await Pump(100);
        Assert.Single(emissions);Assert.Equal("summary",emissions[0].Target);
        Assert.True(emissions[0].AtSeconds-enableRequestAt>=24);
        var firstAutomatic=emissions[0];
        await RotatorRealWait(5_000);Assert.Single(emissions);
        var outputTab=Named<TabItem>(f.Panel,"OutputTab");
        Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(outputTab)).Select();
        await Until(()=>Named<Button>(f.Panel,"Presentationsummary").IsEffectivelyVisible,"Actual output tab must expose manual summary card.");
        Assert.Equal("summary",RotatorPresentation(f.Panel));
        emissions.Clear(); // The next event must come from the actual same-screen action.
        double manualRequestAt=clock.Elapsed.TotalSeconds;
        await RotatorInvoke(Named<Button>(f.Panel,"Presentationsummary"));
        Assert.Equal("summary",RotatorPresentation(f.Panel));Assert.Single(emissions);Assert.Equal("summary",emissions[0].Target);
        emissions.Clear();await RotatorRealWait(23_000);
        Assert.All(emissions,entry=>Assert.True(entry.AtSeconds-manualRequestAt>=24,
            "Automatic event before fresh manual dwell: "+entry));
        for(int i=0;i<60&&emissions.Count==0;i++)await Pump(100);
        RotatorEvidence("cricket-rotation-real-dwell",new{enableRequestAt,firstAutomatic,manualRequestAt,now=clock.Elapsed.TotalSeconds,emissions,current=RotatorPresentation(f.Panel)});
        Assert.Single(emissions);Assert.Equal("scorebug",emissions[0].Target);
        Assert.True(emissions[0].AtSeconds-manualRequestAt>=24);
        Assert.True(f.Output.IsVisible);scope.AssertOwnedPaths();
    }
}

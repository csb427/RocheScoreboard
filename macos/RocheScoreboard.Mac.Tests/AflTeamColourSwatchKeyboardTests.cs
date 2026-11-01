// Reviewed supporting-copy regressions; authoritative integration is verified separately.
// Calibrated original failures prove missing contextual names/Invoke, not absent Avalonia Tab focus.
// Existing ColorPicker/TryPick is reached only through the original owner UI.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class BreakRotationModeIsolationTests
{
    private static readonly string[] TeamSwatchNames={"HomeColorPreview","HomeSecondaryColorPreview","AwayColorPreview","AwaySecondaryColorPreview"};
    private static readonly string[] TeamSwatchFields={"HomeColorBox","HomeSecondaryColorBox","AwayColorBox","AwaySecondaryColorBox"};
    private static string[] TeamSwatchValues(Roche_Scoreboard.MainWindow main)=>TeamSwatchFields.Select(name=>Named<TextBox>(main,name).Text??"").ToArray();
    private static void TeamSwatchEvidence(string name,object value)
    {
        string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??throw new InvalidOperationException("Private capture directory required.");
        Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,name+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
    }
    private static void TeamSwatchCapture(Window window,string name)
    {
        string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??throw new InvalidOperationException("Private capture directory required.");
        Directory.CreateDirectory(dir);using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(dir,name+".png"));
    }
    private static async Task<LogoCropFixture> TeamSwatchOpenFromOwnedRecovery(StorageScope scope)
    {
        scope.AssertOwnedPaths();
        // Read only the TEST scope's private owned directory, not app state.
        string directory=Read<string>(scope,"privateHome");
        Assert.Equal(Path.GetFullPath(directory),Path.GetFullPath(Environment.GetEnvironmentVariable("ROCHE_DATA_HOME")!));
        string homePath=Path.Combine(directory,"home-crop-fixture.png"),awayPath=Path.Combine(directory,"away-crop-fixture.png");
        Assert.False(File.Exists(homePath));Assert.False(File.Exists(awayPath));
        LogoCropCreateOwnedPng(homePath,true);LogoCropCreateOwnedPng(awayPath,false);
        var seed=new MatchManager();seed.AddGoal(TeamSide.Home);seed.AddBehind(TeamSide.Away);
        Assert.False(seed.ClockRunning);Assert.Equal(2,seed.Events.Count);
        // Public persistence format/Save route. Explicit VALID colours are
        // fixture data for ordinary colour cancellation. Empty legacy colours
        // remain a separate unresolved Cancel normalization case; no private
        // mutable app fields are seeded or repaired by this fixture.
        GameStateService.Save(new GameState{Sport="afl",MatchState=seed.ToState(),HomeLogoPath=homePath,
            AwayLogoPath=awayPath,WeatherLocation="",HomePrimaryColor="#0A2A6A",HomeSecondaryColor="#FFFFFF",
            AwayPrimaryColor="#7A1A1A",AwaySecondaryColor="#E8E8E8"});
        var saved=GameStateService.Load();Assert.NotNull(saved);
        Assert.Equal(homePath,saved.HomeLogoPath);Assert.Equal(awayPath,saved.AwayLogoPath);
        scope.Main=new Roche_Scoreboard.MainWindow{Width=1280,Height=850};var main=scope.Main;main.Show();
        var home=Named<SportSelectionScreen>(main,"SportSelectionPanel");
        await Until(()=>home.IsEffectivelyVisible,"Actual initial Home must display the private recovery slot.");
        home.PrepareForDisplay();
        var resume=Named<Button>(home,"HomeResumeButton");await RotatorInvoke(resume);
        var recovery=Named<Grid>(main,"RecoveryOverlay");
        await Until(()=>recovery.IsEffectivelyVisible&&recovery.Children.OfType<ResumeMatchPrompt>().Any(),"Home Resume opens actual recovery choice.");
        var prompt=Assert.Single(recovery.Children.OfType<ResumeMatchPrompt>());
        await RotatorInvoke(Named<Button>(prompt,"ResumeButton"));
        await Until(()=>Named<Control>(main,"MainContent").IsEffectivelyVisible&&!recovery.IsVisible&&!home.IsVisible,
            "Actual Continue Match restores AFL operator controls.");
        var match=Read<MatchManager>(main,"_match");var output=Read<Window>(main,"_displayWindow");
        Assert.False(match.ClockRunning);Assert.Equal(6,match.HomeTotal);Assert.Equal(1,match.AwayTotal);Assert.Equal(2,match.Events.Count);
        Assert.Equal(homePath,Read<string?>(main,"_homeLogoPath"));Assert.Equal(awayPath,Read<string?>(main,"_awayLogoPath"));
        Assert.False(output.IsVisible);Assert.True(Read<bool>(main,"_outputActivationRequired"));
        var tools=Named<Button>(main,"GuidedModeButton");
        if(Equals(tools.Content,"ADVANCED TOOLS"))await RotatorInvoke(tools);
        Assert.Equal("RETURN TO ESSENTIALS",tools.Content);
        var setup=Named<TabItem>(main,"MatchSetupTab");
        await Until(()=>setup.IsLoaded&&setup.IsEffectivelyVisible&&setup.IsEffectivelyEnabled,"Actual advanced Setup tab must be available.");
        Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(setup)).Select();
        await Until(()=>setup.IsSelected&&main.GetVisualDescendants().OfType<SettingsWorkspace>().Any(value=>value.IsEffectivelyVisible),
            "Original Setup tab must mount its SettingsWorkspace.");
        scope.AssertOwnedPaths();return new(main,match,output,homePath,awayPath);
    }
    private static async Task<Control> TeamSwatchReachByOriginalTab(Roche_Scoreboard.MainWindow main,string name,string stage)
    {
        await LogoCropSearch(main,"teams","SettingsSection_Teams");
        var target=Named<Control>(main,name);
        await Until(()=>target.IsLoaded&&target.IsEffectivelyVisible&&target.IsEffectivelyEnabled,"Original selected swatch must be mounted before keyboard proof.");
        var anchor=Named<TextBox>(main,"HomeNameBox");anchor.BringIntoView();
        Assert.True(anchor.Focus(NavigationMethod.Tab));await Pump(40);
        Assert.True(anchor.IsFocused);
        TeamSwatchCapture(main,"team-swatch-tab-start-"+name+"-"+stage);
        var visited=new List<string>();
        // Focus starts at a real public text field, NEVER forced onto target.
        // Tab must discover the actual original swatch, not a proxy/double.
        for(int step=0;step<100&&!target.IsFocused;step++)
        {
            RotationKey(main,PhysicalKey.Tab);await Pump(10);
            var current=TopLevel.GetTopLevel(main)?.FocusManager?.GetFocusedElement();
            visited.Add((current as Control)?.Name+":"+current?.GetType().Name);
            if(step>1&&anchor.IsFocused)break; // completed a real focus cycle.
        }
        TeamSwatchCapture(main,"team-swatch-tab-result-"+name+"-"+stage);
        TeamSwatchEvidence("team-swatch-tab-"+name+"-"+stage,new{name,reached=target.IsFocused,target.Focusable,visited});
        Assert.True(target.IsFocused,"Original "+name+" must be reachable by real sequential Tab; raw pointer-only Border is not a keyboard route.");
        Assert.True(target.Focusable);
        return target;
    }
    private static async Task TeamSwatchUseActualPicker(Roche_Scoreboard.MainWindow main,string name,Action activate,string route,string hex,bool accept)
    {
        Assert.Empty(main.OwnedWindows.OfType<ColorPickerWindow>());
        ExceptionDispatchInfo? failure=null;int seen=0;
        var finished=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // The original activation synchronously enters the native owned modal.
        // Known dispatcher callback is scheduled beforehand; no modal bypass.
        using var driver=DispatcherTimer.RunOnce(async()=>
        {
            ColorPickerWindow? picker=null;
            try
            {
                await Until(()=>main.OwnedWindows.OfType<ColorPickerWindow>().Any(),"Original swatch key must open its actual owned picker.");
                picker=Assert.Single(main.OwnedWindows.OfType<ColorPickerWindow>());seen++;
                Assert.Same(main,picker.Owner);
                var box=Named<TextBox>(picker,"HexBox");
                await Until(()=>picker.IsLoaded&&picker.IsVisible&&box.Bounds.Width>0,"Original modal must load/measure before typing.");
                box.BringIntoView();Assert.True(box.Focus(NavigationMethod.Tab));await Pump(30);Assert.True(box.IsFocused);
                RotationKey(picker,PhysicalKey.A,RawInputModifiers.Control);picker.KeyTextInput(hex);await Pump(40);Assert.Equal(hex,box.Text);
                TeamSwatchCapture(picker,"team-swatch-picker-"+name+"-"+route+(accept?"-accept":"-cancel"));
                var action=Assert.Single(picker.GetVisualDescendants().OfType<Button>(),button=>Equals(button.Content,accept?"USE COLOUR":"CANCEL"));
                await RotatorInvoke(action);
                await Until(()=>!picker.IsVisible,"Original modal button must complete the real picker transaction.");
            }
            catch(Exception error){failure=ExceptionDispatchInfo.Capture(error);}
            finally
            {
                // Infrastructure cleanup only, never used as the success path.
                if(picker?.IsVisible==true)picker.Close(false);
                foreach(var remaining in main.OwnedWindows.OfType<ColorPickerWindow>().ToArray())remaining.Close(false);
                finished.TrySetResult();
            }
        },TimeSpan.FromMilliseconds(60));
        activate(); // Original real key or raw pointer route enters the same owned picker.
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));failure?.Throw();
        Assert.Equal(1,seen);
        await Until(()=>!main.OwnedWindows.OfType<ColorPickerWindow>().Any(),"Actual modal completion must release its owner link.");
    }

    [AvaloniaTheory]
    [InlineData("HomeColorPreview","Choose home primary colour",PhysicalKey.Enter)]
    [InlineData("HomeSecondaryColorPreview","Choose home secondary colour",PhysicalKey.Space)]
    [InlineData("AwayColorPreview","Choose away primary colour",PhysicalKey.Enter)]
    [InlineData("AwaySecondaryColorPreview","Choose away secondary colour",PhysicalKey.Space)]
    [Trait("Evidence","AflTeamSwatchOriginalKeyboardRoute")]
    public async Task OriginalTeamColourSwatchHasTabKeyAndNamedInvokePatternWithRealCancelAndAccept(string name,string expectedName,PhysicalKey key)
    {
        await using var scope=new StorageScope(true,true);
        // Existing verified public Save→Home Resume fixture; meaningful files
        // are incidental private context, not private color state seeding.
        var f=await TeamSwatchOpenFromOwnedRecovery(scope);
        string match=JsonSerializer.Serialize(f.Match.ToState());int scores=0;f.Match.ScoreEventAdded+=_=>scores++;
        var before=TeamSwatchValues(f.Main);Assert.Equal(new[]{"#0A2A6A","#FFFFFF","#7A1A1A","#E8E8E8"},before);var history=RecentColorStorage.Load().ToArray();
        var target=await TeamSwatchReachByOriginalTab(f.Main,name,"cancel-start");
        var peer=ControlAutomationPeer.CreatePeerForElement(target);
        TeamSwatchEvidence("team-swatch-semantics-"+name,new{Target=name,AccessibleName=peer?.GetName(),HasInvoke=peer is IInvokeProvider,
            Border=Assert.IsAssignableFrom<Border>(target).BorderThickness,Before=before});
        Assert.Equal(expectedName,peer?.GetName());Assert.IsAssignableFrom<IInvokeProvider>(peer);
        Assert.Equal(2d,Assert.IsAssignableFrom<Border>(target).BorderThickness.Left);
        await TeamSwatchUseActualPicker(f.Main,name,()=>RotationKey(f.Main,key),"keyboard","#C72E85",false);
        Assert.Equal(before,TeamSwatchValues(f.Main));Assert.Equal(history,RecentColorStorage.Load().ToArray());
        Assert.Equal(match,JsonSerializer.Serialize(f.Match.ToState()));Assert.Equal(0,scores);
        Assert.False(f.Output.IsVisible);Assert.True(Read<bool>(f.Main,"_outputActivationRequired"));
        target=await TeamSwatchReachByOriginalTab(f.Main,name,"accept-start");
        await TeamSwatchUseActualPicker(f.Main,name,()=>RotationKey(f.Main,key),"keyboard","#12AB34",true);
        var expected=before.ToArray();int index=Array.IndexOf(TeamSwatchNames,name);Assert.InRange(index,0,3);expected[index]="#12AB34";
        Assert.Equal(expected,TeamSwatchValues(f.Main));
        Assert.Equal(Color.FromRgb(0x12,0xAB,0x34),Assert.IsAssignableFrom<ISolidColorBrush>(Assert.IsAssignableFrom<Border>(target).Background).Color);
        Assert.Contains("#12AB34",RecentColorStorage.Load());
        Assert.Equal(match,JsonSerializer.Serialize(f.Match.ToState()));Assert.Equal(0,scores);
        Assert.False(f.Output.IsVisible);Assert.True(Read<bool>(f.Main,"_outputActivationRequired"));
        // Ordinary pointer behaviour remains the same ORIGINAL swatch handler.
        // Cancel from a valid accepted colour must not change any side/history.
        var pointerHistory=RecentColorStorage.Load().ToArray();target.BringIntoView();await Pump(50);
        var point=target.TranslatePoint(new Point(target.Bounds.Width/2,target.Bounds.Height/2),f.Main);Assert.NotNull(point);
        Assert.Same(target,f.Main.InputHitTest(point.Value));
        await TeamSwatchUseActualPicker(f.Main,name,()=>{f.Main.MouseMove(point.Value);f.Main.MouseDown(point.Value,MouseButton.Left);f.Main.MouseUp(point.Value,MouseButton.Left);},
            "pointer","#4682A1",false);
        Assert.Equal(expected,TeamSwatchValues(f.Main));Assert.Equal(pointerHistory,RecentColorStorage.Load().ToArray());
        Assert.Equal(match,JsonSerializer.Serialize(f.Match.ToState()));Assert.Equal(0,scores);
        Assert.False(f.Output.IsVisible);Assert.True(Read<bool>(f.Main,"_outputActivationRequired"));scope.AssertOwnedPaths();
        TeamSwatchCapture(f.Main,"team-swatch-accepted-"+name);
        TeamSwatchEvidence("team-swatch-current-"+name,new{name,key,values=TeamSwatchValues(f.Main),scores,outputVisible=f.Output.IsVisible});
    }
}

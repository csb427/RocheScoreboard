// Supporting isolated-source regressions. Actual run status is recorded in worker receipts.
// Fail-first empty/default recovered-colour rollback, independent of the valid-colour a11y fixture.
// Reuses existing owned StorageScope + actual Save/Home Resume + original pointer controls.
// No private app handler/state writes, no output warmup, no provider/network injection.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class BreakRotationModeIsolationTests
{
    private static readonly string[] EmptyColourTargets={"HomeColorPreview","HomeSecondaryColorPreview","AwayColorPreview","AwaySecondaryColorPreview"};
    private static readonly string[] EmptyColourFields={"HomeColorBox","HomeSecondaryColorBox","AwayColorBox","AwaySecondaryColorBox"};
    private sealed record EmptyColourUi(string[] Fields,string[] Brushes,string Model,bool ClockRunning,TimeSpan Elapsed,int Events,bool OutputVisible,bool ActivationRequired);
    private static EmptyColourUi EmptyColourRead(LogoCropFixture f)=>new(
        EmptyColourFields.Select(name=>Named<TextBox>(f.Main,name).Text??"").ToArray(),
        EmptyColourTargets.Select(name=>Assert.IsAssignableFrom<ISolidColorBrush>(Named<Border>(f.Main,name).Background).Color.ToString()).ToArray(),
        JsonSerializer.Serialize(f.Match.ToState()),f.Match.ClockRunning,f.Match.ElapsedInQuarter,f.Match.Events.Count,
        f.Output.IsVisible,Read<bool>(f.Main,"_outputActivationRequired"));
    private static SortedDictionary<string,byte[]?> EmptyColourStoredBytes(StorageScope scope)
    {
        scope.AssertOwnedPaths(); // includes exact private recovery/Documents/process-latch ownership
        string recovery=GameStateService.RecoveryStatePath,history=Path.Combine(SelfTestStorage.DocumentsDirectory,"recent_colours.json");
        var paths=new HashSet<string>(StringComparer.Ordinal);
        foreach(string path in new[]{recovery,history})foreach(string suffix in new[]{"",".bak",".tmp"})paths.Add(path+suffix);
        // AtomicFile can use a GUID temporary suffix. Include current relevant
        // files only inside the scope-proved private Documents directory.
        if(Directory.Exists(SelfTestStorage.DocumentsDirectory))
            foreach(string file in Directory.EnumerateFiles(SelfTestStorage.DocumentsDirectory))
                if(Path.GetFileName(file).StartsWith("recent_colours.json.",StringComparison.Ordinal)&&file.EndsWith(".tmp",StringComparison.Ordinal))paths.Add(file);
        var result=new SortedDictionary<string,byte[]?>(StringComparer.Ordinal);
        foreach(string path in paths)result[path]=File.Exists(path)?File.ReadAllBytes(path):null;
        return result;
    }
    private static object EmptyColourFileHashes(SortedDictionary<string,byte[]?> files)=>files.ToDictionary(pair=>pair.Key,
        pair=>pair.Value is null?null:Convert.ToHexString(SHA256.HashData(pair.Value)),StringComparer.Ordinal);
    private static void EmptyColourAssertBytes(SortedDictionary<string,byte[]?> before,SortedDictionary<string,byte[]?> after)
    {
        Assert.Equal(before.Keys.ToArray(),after.Keys.ToArray());
        foreach(var pair in before)
            if(pair.Value is null)Assert.Null(after[pair.Key]);else Assert.Equal(pair.Value,after[pair.Key]);
    }
    private static void EmptyColourWrite(string token,string stage,object value)
    {
        string root=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??throw new InvalidOperationException("Explicit private capture directory required.");
        string folder=Path.Combine(root,"empty-colour-"+token);Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,stage+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
    }
    private static void EmptyColourCapture(Window window,string token,string stage)
    {
        string root=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??throw new InvalidOperationException("Explicit private capture directory required.");
        string folder=Path.Combine(root,"empty-colour-"+token);Directory.CreateDirectory(folder);
        using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(folder,stage+".png"));
    }
    private static async Task<Point> EmptyColourPointerPoint(Window window,Control target)
    {
        target.BringIntoView();
        bool Ready()
        {
            if(!target.IsLoaded||!target.IsEffectivelyVisible||!target.IsEffectivelyEnabled||!target.IsHitTestVisible)return false;
            var point=target.TranslatePoint(default,window);if(point is null)return false;
            var box=new Rect(point.Value,target.Bounds.Size);if(box.Width<=8||box.Height<=8||!new Rect(window.ClientSize).Contains(box))return false;
            var points=new[]{box.Center,new Point(box.Left+4,box.Center.Y),new Point(box.Right-4,box.Center.Y),new Point(box.Center.X,box.Top+4),new Point(box.Center.X,box.Bottom-4)};
            return points.All(p=>ReferenceEquals(window.InputHitTest(p),target)||window.InputHitTest(p) is Visual child&&child.GetVisualAncestors().Contains(target));
        }
        await Until(Ready,"Original raw-pointer target must own all five real input probes; do not replace this route with semantic Invoke or private activation.");
        var origin=target.TranslatePoint(default,window);Assert.NotNull(origin);
        return new Point(origin.Value.X+target.Bounds.Width/2,origin.Value.Y+target.Bounds.Height/2);
    }
    private static async Task EmptyColourPointer(Window window,Control target)
    {
        var center=await EmptyColourPointerPoint(window,target);
        window.MouseMove(center);window.MouseDown(center,MouseButton.Left);window.MouseUp(center,MouseButton.Left);
    }
    private static async Task EmptyColourActualPreviewAndCancel(LogoCropFixture f,string targetName,string exit,string token,Action<EmptyColourUi> observedPreview,Func<ColorPickerWindow,TextBox,Task>? duringPreview=null)
    {
        var target=Named<Control>(f.Main,targetName);Assert.Empty(f.Main.OwnedWindows.OfType<ColorPickerWindow>());
        // Prove readiness BEFORE arming the modal driver. No asynchronous
        // reveal can outlive that driver's window and leave an undriven modal.
        var activationPoint=await EmptyColourPointerPoint(f.Main,target);
        ExceptionDispatchInfo? failure=null;int opened=0;bool closedByOriginalAction=false;
        var finished=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var driver=DispatcherTimer.RunOnce(async()=>
        {
            ColorPickerWindow? picker=null;
            try
            {
                await Until(()=>f.Main.OwnedWindows.OfType<ColorPickerWindow>().Any(),"Original swatch pointer must open the actual owned colour picker.");
                picker=Assert.Single(f.Main.OwnedWindows.OfType<ColorPickerWindow>());opened++;Assert.Same(f.Main,picker.Owner);
                var hex=Named<TextBox>(picker,"HexBox");
                await Until(()=>picker.IsLoaded&&picker.IsVisible&&hex.IsLoaded&&hex.IsEffectivelyVisible&&hex.Bounds.Width>0,"Actual picker/HexBox must be ready before text input.");
                hex.BringIntoView();Assert.True(hex.Focus(NavigationMethod.Tab));await Pump(30);Assert.True(hex.IsFocused);
                RotationKey(picker,PhysicalKey.A,RawInputModifiers.Control);picker.KeyTextInput("#C72E85");await Pump(40);Assert.Equal("#C72E85",hex.Text);
                observedPreview(EmptyColourRead(f));EmptyColourCapture(picker,token,"actual-valid-preview");
                if(duringPreview is not null)await duringPreview(picker,hex);
                if(exit=="owner-close")
                {
                    Assert.False(f.Main.IsVisible);Assert.False(picker.IsVisible);
                }
                else if(exit=="hidden-close")
                {
                    // Controlled public window-lifetime stimulus. Regular12
                    // separately cover the original physical header× route.
                    Assert.False(f.Main.IsVisible);picker.Close(false);
                }
                else if(exit=="escape")
                {
                    picker.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None);
                    // Modal Escape may dispose its backend on key-down; real
                    // key-up belongs to the owner in that case.
                    Window release=picker.IsVisible?picker:f.Main;
                    release.KeyReleaseQwerty(PhysicalKey.Escape,RawInputModifiers.None);
                }
                else
                {
                    var action=Assert.Single(picker.GetVisualDescendants().OfType<Button>(),button=>Equals(button.Content,exit=="accept"?"USE COLOUR":exit=="cancel"?"CANCEL":"×"));
                    await EmptyColourPointer(picker,action);
                }
                await Until(()=>!picker.IsVisible,"Original CANCEL/Escape/header close must close the real modal before cleanup.");
                closedByOriginalAction=true;
            }
            catch(Exception error){failure=ExceptionDispatchInfo.Capture(error);}
            finally
            {
                // Infrastructure cleanup ONLY; cannot satisfy success.
                if(picker?.IsVisible==true)picker.Close(false);
                foreach(var remaining in f.Main.OwnedWindows.OfType<ColorPickerWindow>().ToArray())remaining.Close(false);
                finished.TrySetResult();
            }
        },TimeSpan.FromMilliseconds(60));
        // Original raw pointer activation immediately follows driver arming;
        // no intervening await/reveal and no semantic/private activation.
        f.Main.MouseMove(activationPoint);f.Main.MouseDown(activationPoint,MouseButton.Left);f.Main.MouseUp(activationPoint,MouseButton.Left);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(12));failure?.Throw();Assert.Equal(1,opened);Assert.True(closedByOriginalAction);
        await Until(()=>!f.Main.OwnedWindows.OfType<ColorPickerWindow>().Any(),"Actual cancellation must retire the owned picker link.");
    }

    [AvaloniaTheory]
    [InlineData("HomeColorPreview","cancel")][InlineData("HomeColorPreview","escape")][InlineData("HomeColorPreview","close")]
    [InlineData("HomeSecondaryColorPreview","cancel")][InlineData("HomeSecondaryColorPreview","escape")][InlineData("HomeSecondaryColorPreview","close")]
    [InlineData("AwayColorPreview","cancel")][InlineData("AwayColorPreview","escape")][InlineData("AwayColorPreview","close")]
    [InlineData("AwaySecondaryColorPreview","cancel")][InlineData("AwaySecondaryColorPreview","escape")][InlineData("AwaySecondaryColorPreview","close")]
    [Trait("Evidence","EmptyRecoveredColourCancelExactRollback")]
    public async Task OriginalPointerPickerCancelRestoresExactEmptyRecoveredColoursInsteadOfCanonicalizingThem(string targetName,string exit)
    {
        await using var scope=new StorageScope(true,true);
        // Existing public-format Save + actual Home/Resume path deliberately
        // leaves the supported default colour strings EMPTY. Do NOT replace
        // this with the separate valid-colour keyboard accessibility fixture.
        var f=await LogoCropOpenFromOwnedRecovery(scope);await LogoCropSearch(f.Main,"teams","SettingsSection_Teams");
        var before=EmptyColourRead(f);Assert.Equal(new[]{"","","",""},before.Fields);Assert.False(before.ClockRunning);Assert.Equal(TimeSpan.Zero,before.Elapsed);
        Assert.Equal(2,before.Events);Assert.False(before.OutputVisible);Assert.True(before.ActivationRequired);
        var beforeFiles=EmptyColourStoredBytes(scope);var beforeHistory=RecentColorStorage.Load().ToArray();
        int matchChanges=0,scoreEvents=0;void Changed()=>matchChanges++;void Scored(ScoreEvent _)=>scoreEvents++;
        f.Match.MatchChanged+=Changed;f.Match.ScoreEventAdded+=Scored;
        string token=Guid.NewGuid().ToString("N");EmptyColourUi? preview=null;EmptyColourCapture(f.Main,token,"before-original-pointer");
        try
        {
            await EmptyColourActualPreviewAndCancel(f,targetName,exit,token,current=>
            {
                preview=current;int slot=Array.IndexOf(EmptyColourTargets,targetName);Assert.InRange(slot,0,3);
                var expectedPreview=before.Fields.ToArray();expectedPreview[slot]="#C72E85";Assert.Equal(expectedPreview,current.Fields);
                Assert.Equal(Color.FromRgb(0xC7,0x2E,0x85).ToString(),current.Brushes[slot]);
                for(int other=0;other<4;other++)if(other!=slot)Assert.Equal(before.Brushes[other],current.Brushes[other]);
                Assert.Equal(before.Model,current.Model);Assert.Equal(0,matchChanges);Assert.Equal(0,scoreEvents);
                Assert.False(current.OutputVisible);Assert.True(current.ActivationRequired);
            });
            await Pump(300); // bounded drain of existing coalesced callbacks; NOT a production save proof
            var after=EmptyColourRead(f);var afterFiles=EmptyColourStoredBytes(scope);var afterHistory=RecentColorStorage.Load().ToArray();
            EmptyColourCapture(f.Main,token,"after-original-cancel");
            // Capture ALL observations before fail-first rollback assertions.
            EmptyColourWrite(token,"observations",new{Target=targetName,Exit=exit,Before=before,Preview=preview,After=after,
                BeforeHistory=beforeHistory,AfterHistory=afterHistory,BeforeFileHashes=EmptyColourFileHashes(beforeFiles),AfterFileHashes=EmptyColourFileHashes(afterFiles),
                MatchChanges=matchChanges,ScoreEvents=scoreEvents,SelfTestSaveGuard=((RocheScoreboard.Mac.App)Application.Current!).IsSelfTestMode});
            Assert.NotNull(preview);
            Assert.Equal(before.Fields,after.Fields); // expected first real failure on unmodified controller: selected ""→"#FFFFFF"
            Assert.Equal(before.Brushes,after.Brushes);Assert.Equal(beforeHistory,afterHistory);EmptyColourAssertBytes(beforeFiles,afterFiles);
            Assert.Equal(before.Model,after.Model);Assert.Equal(before.ClockRunning,after.ClockRunning);Assert.Equal(before.Elapsed,after.Elapsed);Assert.Equal(before.Events,after.Events);
            Assert.Equal(0,matchChanges);Assert.Equal(0,scoreEvents);Assert.False(after.OutputVisible);Assert.True(after.ActivationRequired);scope.AssertOwnedPaths();
        }
        finally{f.Match.MatchChanged-=Changed;f.Match.ScoreEventAdded-=Scored;}
    }
    private static async Task<LogoCropFixture> EmptyColourOpenValidRecovery(StorageScope scope,string homePrimary="#0A2A6A")
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
            AwayLogoPath=awayPath,WeatherLocation="",HomePrimaryColor=homePrimary,HomeSecondaryColor="#FFFFFF",
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
    [AvaloniaTheory]
    [InlineData("HomeColorPreview")][InlineData("HomeSecondaryColorPreview")][InlineData("AwayColorPreview")][InlineData("AwaySecondaryColorPreview")]
    public async Task OrdinaryValidOriginalPointerColourCancelThenAcceptPreservesOnlyItsIntendedSideAndHistory(string targetName)
    {
        await using var scope=new StorageScope(true,true);var f=await EmptyColourOpenValidRecovery(scope);await LogoCropSearch(f.Main,"teams","SettingsSection_Teams");
        var before=EmptyColourRead(f);Assert.Equal(new[]{"#0A2A6A","#FFFFFF","#7A1A1A","#E8E8E8"},before.Fields);
        string token=Guid.NewGuid().ToString("N");var history=RecentColorStorage.Load().ToArray();int scores=0;void Scored(ScoreEvent _)=>scores++;f.Match.ScoreEventAdded+=Scored;
        try
        {
            await EmptyColourActualPreviewAndCancel(f,targetName,"cancel",token+"-cancel",_=>{});
            var cancelled=EmptyColourRead(f);Assert.Equal(before.Fields,cancelled.Fields);Assert.Equal(before.Brushes,cancelled.Brushes);Assert.Equal(history,RecentColorStorage.Load().ToArray());
            Assert.Equal(before.Model,cancelled.Model);Assert.Equal(0,scores);Assert.False(cancelled.OutputVisible);Assert.True(cancelled.ActivationRequired);
            await EmptyColourActualPreviewAndCancel(f,targetName,"accept",token+"-accept",_=>{});
            var accepted=EmptyColourRead(f);int slot=Array.IndexOf(EmptyColourTargets,targetName);Assert.InRange(slot,0,3);
            var expected=before.Fields.ToArray();expected[slot]="#C72E85";Assert.Equal(expected,accepted.Fields);
            Assert.Equal(Color.FromRgb(0xC7,0x2E,0x85).ToString(),accepted.Brushes[slot]);for(int other=0;other<4;other++)if(other!=slot)Assert.Equal(before.Brushes[other],accepted.Brushes[other]);
            Assert.Contains("#C72E85",RecentColorStorage.Load());Assert.Equal(before.Model,accepted.Model);Assert.Equal(0,scores);Assert.False(accepted.OutputVisible);Assert.True(accepted.ActivationRequired);
            EmptyColourWrite(token,"valid-cancel-accept",new{Target=targetName,Before=before,Cancelled=cancelled,Accepted=accepted,Scores=scores});scope.AssertOwnedPaths();
        }
        finally{f.Match.ScoreEventAdded-=Scored;}
    }
    private static async Task EmptyColourTypeLive(ColorPickerWindow picker,TextBox hex,string raw)
    {
        Assert.True(picker.IsVisible);hex.BringIntoView();Assert.True(hex.Focus(NavigationMethod.Tab));await Pump(20);
        RotationKey(picker,PhysicalKey.A,RawInputModifiers.Control);picker.KeyTextInput(raw);await Pump(35);Assert.Equal(raw,hex.Text);
    }
    [AvaloniaTheory]
    [InlineData("reload","cancel")][InlineData("reload","accept")][InlineData("closed-owner","owner-close")]
    [InlineData("cancelled-close","accept")][InlineData("hidden-owner","hidden-close")][InlineData("ordinary-score","accept")]
    public async Task CurrentColourTransactionRespectsRealOwnerLifetimeAndPublicModelReplacementWithoutRetiringOrdinaryScores(string scenario,string exit)
    {
        await using var scope=new StorageScope(true,true);var f=await LogoCropOpenFromOwnedRecovery(scope);await LogoCropSearch(f.Main,"teams","SettingsSection_Teams");
        var before=EmptyColourRead(f);string token=Guid.NewGuid().ToString("N");long life=f.Match.LifecycleVersion;
        EmptyColourUi? retired=null,afterAttempt=null;bool cancelledCloseUsable=false;int scoreEvents=0;void Scored(ScoreEvent _)=>scoreEvents++;f.Match.ScoreEventAdded+=Scored;
        string[] historyBefore=RecentColorStorage.Load().ToArray();
        try
        {
            await EmptyColourActualPreviewAndCancel(f,"HomeColorPreview",exit,token,_=>{},async(picker,hex)=>
            {
                if(scenario=="reload")
                {
                    // Explicit PUBLIC MODEL fixture seam, not a claim of full
                    // Home/session replacement UI while a modal disables it.
                    var replacement=new MatchManager();replacement.AddBehind(TeamSide.Home);replacement.AddGoal(TeamSide.Away);
                    f.Match.LoadState(replacement.ToState());Assert.NotEqual(life,f.Match.LifecycleVersion);retired=EmptyColourRead(f);
                    await EmptyColourTypeLive(picker,hex,"#12AB34");afterAttempt=EmptyColourRead(f);
                }
                else if(scenario=="closed-owner")
                {
                    // Real public owner.Close retires its owned modal. Read
                    // retained controls only; never render a disposed backend.
                    f.Main.Close();Assert.False(f.Main.IsVisible);Assert.False(picker.IsVisible);retired=EmptyColourRead(f);afterAttempt=retired;
                }
                else if(scenario=="cancelled-close")
                {
                    // Installed AFTER picker opening: a naive earlier Closing
                    // subscriber must not permanently retire this valid receipt.
                    void LaterCancel(object? sender,System.ComponentModel.CancelEventArgs args)=>args.Cancel=true;
                    f.Main.Closing+=LaterCancel;
                    try{f.Main.Close();Assert.True(f.Main.IsVisible);Assert.True(picker.IsVisible);}
                    finally{f.Main.Closing-=LaterCancel;}
                    Assert.Equal(life,f.Match.LifecycleVersion);await EmptyColourTypeLive(picker,hex,"#12AB34");afterAttempt=EmptyColourRead(f);cancelledCloseUsable=true;
                }
                else if(scenario=="hidden-owner")
                {
                    f.Main.Hide();Assert.False(f.Main.IsVisible);Assert.Equal(life,f.Match.LifecycleVersion);retired=EmptyColourRead(f);
                }
                else
                {
                    // Public model scoring stimulus: ordinary scores stay in
                    // the same lifecycle. This is not a disabled-owner UI click.
                    f.Match.AddGoal(TeamSide.Home);Assert.Equal(life,f.Match.LifecycleVersion);retired=EmptyColourRead(f);
                    await EmptyColourTypeLive(picker,hex,"#12AB34");afterAttempt=EmptyColourRead(f);
                }
                EmptyColourWrite(token,"lifetime-stimulus",new{Scenario=scenario,Exit=exit,Before=before,Retired=retired,AfterAttempt=afterAttempt,CancelledCloseUsable=cancelledCloseUsable,ScoreEvents=scoreEvents});
            });
            if(scenario=="hidden-owner"){Assert.False(f.Main.IsVisible);f.Main.Show();await Pump(80);}
            var after=EmptyColourRead(f);EmptyColourWrite(token,"lifetime-completed",new{Scenario=scenario,Exit=exit,Before=before,Retired=retired,AfterAttempt=afterAttempt,After=after,CancelledCloseUsable=cancelledCloseUsable,ScoreEvents=scoreEvents,HistoryBefore=historyBefore,HistoryAfter=RecentColorStorage.Load().ToArray()});
            if(scenario=="reload"||scenario=="closed-owner")
            {
                Assert.NotNull(retired);Assert.NotNull(afterAttempt);Assert.Equal(retired.Fields,afterAttempt.Fields);Assert.Equal(retired.Brushes,afterAttempt.Brushes);
                Assert.Equal(retired.Fields,after.Fields);Assert.Equal(retired.Brushes,after.Brushes);Assert.Equal(retired.Model,after.Model);Assert.Equal(0,scoreEvents);
                // Accept still records a recent colour in the existing picker
                // API. No global-recents behavior change is claimed here.
                if(exit!="accept")Assert.Equal(historyBefore,RecentColorStorage.Load().ToArray());
            }
            else if(scenario=="hidden-owner")
            {
                Assert.Equal(before.Fields,after.Fields);Assert.Equal(before.Brushes,after.Brushes);Assert.Equal(before.Model,after.Model);Assert.Equal(0,scoreEvents);Assert.Equal(historyBefore,RecentColorStorage.Load().ToArray());
            }
            else
            {
                var expected=before.Fields.ToArray();expected[0]="#12AB34";Assert.Equal(expected,after.Fields);Assert.Equal(Color.FromRgb(0x12,0xAB,0x34).ToString(),after.Brushes[0]);
                Assert.Contains("#12AB34",RecentColorStorage.Load());
                if(scenario=="cancelled-close"){Assert.True(cancelledCloseUsable);Assert.Equal(before.Model,after.Model);Assert.Equal(0,scoreEvents);}
                else{Assert.NotNull(retired);Assert.Equal(retired.Model,after.Model);Assert.Equal(1,scoreEvents);}
            }
            Assert.False(after.OutputVisible);Assert.True(after.ActivationRequired);scope.AssertOwnedPaths();
        }
        finally{f.Match.ScoreEventAdded-=Scored;}
    }
    [AvaloniaTheory][InlineData("cancel")][InlineData("accept")]
    public async Task SynchronousPublicSelectedTextObserverCannotHaveItsNewColourRepaintedByAnOlderCompletion(string completion)
    {
        await using var scope=new StorageScope(true,true);
        // Lowercase same-colour spelling deliberately survives live preview's
        // existing ordinal-ignore-case optimization, then canonical Accept
        // writes uppercase. Cancel instead writes the original empty value.
        var f=completion=="accept"?await EmptyColourOpenValidRecovery(scope,"#c72e85"):await LogoCropOpenFromOwnedRecovery(scope);
        await LogoCropSearch(f.Main,"teams","SettingsSection_Teams");var before=EmptyColourRead(f);
        var field=Named<TextBox>(f.Main,"HomeColorBox");var swatch=Named<Border>(f.Main,"HomeColorPreview");
        string token=Guid.NewGuid().ToString("N"),trigger=completion=="accept"?"#C72E85":"";bool intervened=false,subscribed=false;
        EmptyColourUi? replacement=null;
        void DifferentValue(object? sender,AvaloniaPropertyChangedEventArgs args)
        {
            // Actual synchronous PUBLIC property notification, not a manually
            // raised TextChanged event/private handler or synthetic callback.
            // It models a same-session external setter; no lifecycle changes.
            if(intervened||args.Property!=TextBox.TextProperty||field.Text!=trigger)return;
            intervened=true;field.Text="#194D7A";swatch.Background=new SolidColorBrush(Color.FromRgb(0x19,0x4D,0x7A));replacement=EmptyColourRead(f);
        }
        try
        {
            await EmptyColourActualPreviewAndCancel(f,"HomeColorPreview",completion,token,_=>{},(picker,hex)=>
            {
                Assert.Equal(completion=="accept"?"#c72e85":"#C72E85",field.Text);
                field.PropertyChanged+=DifferentValue;subscribed=true;return Task.CompletedTask;
            });
            var after=EmptyColourRead(f);
            EmptyColourWrite(token,"synchronous-value-reentry",new{Completion=completion,Before=before,Replacement=replacement,After=after,Intervened=intervened,Source="synchronous public TextProperty notification"});
            Assert.True(intervened);Assert.NotNull(replacement);Assert.Equal(replacement.Fields,after.Fields);Assert.Equal(replacement.Brushes,after.Brushes);
            Assert.Equal(before.Model,after.Model);Assert.False(after.OutputVisible);Assert.True(after.ActivationRequired);scope.AssertOwnedPaths();
        }
        finally{if(subscribed)field.PropertyChanged-=DifferentValue;}
    }
}

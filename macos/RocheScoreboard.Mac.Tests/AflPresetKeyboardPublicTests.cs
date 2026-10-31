// Public saved-club input regressions; isolated PID/GUID storage leases.
// Extends existing exclusive BreakRotationModeIsolationTests partial and its
// reviewed StorageScope/Named/Pump/Until. No private application state/handlers.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class BreakRotationModeIsolationTests
{
    private static readonly TeamLibrarySport KbAfl=TeamLibrarySport.AustralianFootball;
    private static readonly TeamLibrarySport KbCricket=TeamLibrarySport.Cricket;
    private static string KbDraft(SetupWizard wizard,string side)=>string.Join("|",new[]{"Name","Abbr","Color","Secondary"}.Select(part=>Named<TextBox>(wizard,"Setup"+side+part+"Box").Text));
    private static async Task KbKey(Window window,PhysicalKey key)
    {window.KeyPressQwerty(key,RawInputModifiers.None);window.KeyReleaseQwerty(key,RawInputModifiers.None);await Pump(15);}
    private static async Task<Point> KbPoint(Window window,Control control)
    {
        control.BringIntoView();await Pump(30);Assert.True(control.IsLoaded&&control.IsEffectivelyVisible&&control.IsEffectivelyEnabled);
        var point=Assert.IsType<Point>(control.TranslatePoint(default,window));var box=new Rect(point,control.Bounds.Size);
        Assert.True(new Rect(window.ClientSize).Contains(box));Assert.True(box.Width>0&&box.Height>0);
        var hit=window.InputHitTest(box.Center);Assert.True(ReferenceEquals(hit,control)||hit is Visual v&&v.GetVisualAncestors().Contains(control));return box.Center;
    }
    private static async Task KbClick(Window window,Control control)
    {Point point=await KbPoint(window,control);window.MouseMove(point);window.MouseDown(point,MouseButton.Left);window.MouseUp(point,MouseButton.Left);await Pump(20);}
    private static async Task KbTabTo(KbRoot f,Border target,string side)
    {
        TextBox anchor=Named<TextBox>(f.Wizard,"Setup"+side+"NameBox");anchor.BringIntoView();Assert.True(anchor.Focus(NavigationMethod.Tab));await Pump(20);
        var visited=new List<string>();
        for(int i=0;i<140&&!target.IsFocused;i++)
        {
            await KbKey(f.Main,PhysicalKey.Tab);var focused=f.Main.FocusManager?.GetFocusedElement() as Control;visited.Add((focused?.Name??"")+":"+focused?.GetType().Name);
            if(i>1&&anchor.IsFocused)break;
        }
        KbEvidence("tab-"+side,new{targetType=target.GetType().Name,target.Focusable,reached=target.IsFocused,visited});
        Assert.True(target.IsFocused,"Actual sequential Tab must reach original saved tile; any failure here is a focus-route prerequisite, not assumed native WPF proof.");
    }
    private static void KbEvidence(string stage,object value)
    {
        string path=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-preset-keyboard-draft");Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path,"saved-club-"+stage+".json"),System.Text.Json.JsonSerializer.Serialize(value,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    }
    private static void KbAssertChosen(KbRoot f,string side,string name)
    {
        TeamPreset expected=Assert.Single(KbPresetDirectory.Teams(),team=>team.TeamName==name);
        Assert.Equal(string.Join("|",expected.TeamName,expected.Abbreviation,expected.PrimaryColor,expected.SecondaryColor),KbDraft(f.Wizard,side));
        Border current=f.Tile(side,name);
        Assert.Same(current,Assert.Single(Named<WrapPanel>(f.Wizard,side+"PresetPanel").Children.OfType<Border>(),tile=>AutomationProperties.GetItemStatus(tile)=="Selected"));
    }
    private static void KbScreenshot(Window window,string name)
    {
        string path=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")!;Directory.CreateDirectory(path);
        using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(path,name+".png"));
    }
    private sealed class KbCountingIo(IPresetStorageIo inner):IPresetStorageIo
    {
        private int writes;internal int Writes=>Volatile.Read(ref writes);
        public Task<string> ReadAllTextAsync(string path)=>inner.ReadAllTextAsync(path);
        public Task WriteAllTextAsync(string path,string value){Interlocked.Increment(ref writes);return inner.WriteAllTextAsync(path,value);}
    }
    private static bool KbAllowedRefusal(Exception? error)=>error is null||error.GetType().FullName is "Avalonia.Automation.ElementNotEnabledException" or "Avalonia.Automation.ElementNotAvailableException";
    private static IInvokeProvider KbProvider(Border tile,string team,string side)
    {
        var peer=ControlAutomationPeer.CreatePeerForElement(tile);
        KbEvidence("peer-"+side,new{tileType=tile.GetType().Name,name=AutomationProperties.GetName(tile),peerName=peer?.GetName(),peerType=peer?.GetType().Name,invoke=peer is IInvokeProvider,status=AutomationProperties.GetItemStatus(tile)});
        Assert.NotNull(peer);Assert.Equal(team+" · "+side+" preset",peer.GetName());return Assert.IsAssignableFrom<IInvokeProvider>(peer);
    }
    // Own only PIDselftest presets without changing the self-test latch. This
    // complements existing StorageScope, including when its latch is false.
    private sealed class KbPresetDirectory:IAsyncDisposable
    {
        private readonly string root=SelfTestStorage.RootPath;
        private readonly string scratch=Path.Combine(Path.GetTempPath(),"roche-preset-keyboard-"+Guid.NewGuid().ToString("N"));
        internal string DirectoryPath=>Path.Combine(root,"presets");private bool moved,owned;
        internal static async Task<KbPresetDirectory> Create()
        {
            var f=new KbPresetDirectory();try
            {
                Assert.True(((App)Application.Current!).IsSelfTestMode);
                Assert.Equal(Path.GetFullPath(Path.Combine(Path.GetTempPath(),"RocheScoreboard-SelfTests",Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture))),Path.GetFullPath(f.root));
                Assert.Equal(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),Path.GetFullPath(Path.GetDirectoryName(f.scratch)!));
                string leaf=Path.GetFileName(f.scratch);Assert.True(Guid.TryParseExact(leaf["roche-preset-keyboard-".Length..],"N",out _));Assert.False(Directory.Exists(f.scratch));
                await Task.WhenAll(PresetStorage.ReadAsync(KbAfl),PresetStorage.ReadAsync(KbCricket));
                Directory.CreateDirectory(f.scratch);if(Directory.Exists(f.DirectoryPath)){Directory.Move(f.DirectoryPath,Path.Combine(f.scratch,"original"));f.moved=true;}
                else Assert.False(File.Exists(f.DirectoryPath));Directory.CreateDirectory(f.DirectoryPath);f.owned=true;
                await PresetStorage.SaveAllAsync(Teams());await PresetStorage.SaveAllCricketAsync([]);await Task.WhenAll(PresetStorage.ReadAsync(KbAfl),PresetStorage.ReadAsync(KbCricket));return f;
            }
            catch(Exception original){try{await f.DisposeAsync();}catch(Exception cleanup){throw new AggregateException(original,cleanup);}throw;}
        }
        internal static List<TeamPreset> Teams()=>new(){
            new(){PresetName="First keyboard club",TeamName="First keyboard club",Abbreviation="ONE",PrimaryColor="#123456",SecondaryColor="#FEDCBA"},
            new(){PresetName="Second keyboard club",TeamName="Second keyboard club",Abbreviation="TWO",PrimaryColor="#5A1728",SecondaryColor="#F1F1F1"},
            new(){PresetName="Third keyboard club with a deliberately long full accessible identity",TeamName="Third keyboard club with a deliberately long full accessible identity",Abbreviation="THR",PrimaryColor="#195A37",SecondaryColor="#FAEAB0"}};
        internal byte[]?[] Bytes()=>new[]{"team_presets.json","team_presets.json.bak","cricket_team_presets.json","cricket_team_presets.json.bak"}.Select(name=>{var path=Path.Combine(DirectoryPath,name);return File.Exists(path)?File.ReadAllBytes(path):null;}).ToArray();
        internal void AssertBytes(byte[]?[] expected){var actual=Bytes();for(int i=0;i<expected.Length;i++)Assert.Equal(expected[i],actual[i]);}
        public async ValueTask DisposeAsync()
        {
            await Task.WhenAll(PresetStorage.ReadAsync(KbAfl),PresetStorage.ReadAsync(KbCricket));
            if(owned&&Directory.Exists(DirectoryPath))Directory.Delete(DirectoryPath,true);
            if(moved&&Directory.Exists(Path.Combine(scratch,"original")))Directory.Move(Path.Combine(scratch,"original"),DirectoryPath);
            if(Directory.Exists(scratch))Directory.Delete(scratch,true);
        }
    }
    private sealed class KbRoot:IAsyncDisposable
    {
        private readonly StorageScope scope;internal readonly KbPresetDirectory Files;internal Roche_Scoreboard.MainWindow Main=>scope.Main!;
        internal SetupWizard Wizard=>Named<SetupWizard>(Main,"SetupWizardPanel");
        private KbRoot(StorageScope s,KbPresetDirectory p){scope=s;Files=p;}
        internal static async Task<KbRoot> Create()
        {
            var scope=new StorageScope(false,null);KbPresetDirectory? files=null;
            try{scope.AssertOwnedPaths();files=await KbPresetDirectory.Create();scope.Main=new Roche_Scoreboard.MainWindow{Width=1280,Height=850};scope.Main.Show();var f=new KbRoot(scope,files);await f.OpenAfl();return f;}
            catch(Exception original)
            {
                var errors=new List<Exception>{original};try{scope.Main?.Close();}catch(Exception cleanup){errors.Add(cleanup);}
                if(files is not null)try{await files.DisposeAsync();}catch(Exception cleanup){errors.Add(cleanup);}
                try{await scope.DisposeAsync();}catch(Exception cleanup){errors.Add(cleanup);}
                throw new AggregateException("Public saved-club fixture setup failed; cleanup attempted independently.",errors);
            }
        }
        internal Border Tile(string side,string preset)=>Assert.Single(Named<WrapPanel>(Wizard,side+"PresetPanel").Children.OfType<Border>(),b=>b.Tag is TeamPreset p&&p.PresetName==preset);
        internal async Task OpenAfl()
        {
            await Until(()=>Named<SportSelectionScreen>(Main,"SportSelectionPanel").IsEffectivelyVisible,"Actual one Home must be visible before Start Game.");
            var home=Named<SportSelectionScreen>(Main,"SportSelectionPanel");
            await KbClick(Main,Assert.Single(home.GetVisualDescendants().OfType<Button>(),button=>button.Name=="AflLaunchAction"));
            await Until(()=>Named<Control>(Wizard,"SportStepContainer").IsEffectivelyVisible,"Original unified wizard must open sport step.");
            await KbClick(Main,Named<RadioButton>(Wizard,"AflSportChoice"));await KbClick(Main,Named<Button>(Wizard,"NextButton"));
            await Until(()=>Named<WrapPanel>(Wizard,"HomePresetPanel").IsEffectivelyVisible&&Named<WrapPanel>(Wizard,"HomePresetPanel").Children.OfType<Border>().Count()==3,"Original AFL teams step must publish confirmed clubs.");
        }
        internal byte[]?[] Recovery()=>new[]{GameStateService.RecoveryStatePath,GameStateService.RecoveryStatePath+".bak"}.Select(path=>File.Exists(path)?File.ReadAllBytes(path):null).ToArray();
        internal void AssertRecovery(byte[]?[] before){var after=Recovery();for(int i=0;i<before.Length;i++)Assert.Equal(before[i],after[i]);Assert.Null(Wizard.Result);}
        public async ValueTask DisposeAsync()
        {
            var errors=new List<Exception>();try{foreach(var owned in Main.OwnedWindows.ToArray())owned.Close();Main.Close();}catch(Exception cleanup){errors.Add(cleanup);}
            try{await Files.DisposeAsync();}catch(Exception cleanup){errors.Add(cleanup);}
            try{await scope.DisposeAsync();}catch(Exception cleanup){errors.Add(cleanup);}
            if(errors.Count>0)throw new AggregateException("Public saved-club fixture restoration failed; retain scratch evidence.",errors);
        }
    }

    [AvaloniaTheory]
    [InlineData("Home",PhysicalKey.Enter)][InlineData("Away",PhysicalKey.Space)]
    public async Task PublicUnifiedAflSavedClubKeyboardChooseUsesCurrentNamedTile(string side,PhysicalKey key)
    {
        await using var f=await KbRoot.Create();string other=side=="Home"?"Away":"Home";
        string oldOther=KbDraft(f.Wizard,other);var recovery=f.Recovery();var saved=f.Files.Bytes();Border original=f.Tile(side,"First keyboard club");
        Assert.False(NativePointer.IsCaptureWithin(f.Wizard));await KbTabTo(f,original,side);
        f.Main.KeyPressQwerty(key,RawInputModifiers.None);f.Main.KeyReleaseQwerty(key,RawInputModifiers.None);
        KbEvidence("key-effect-"+side,new{side,key=key.ToString(),name=Named<TextBox>(f.Wizard,"Setup"+side+"NameBox").Text,originalType=original.GetType().Name,focused=f.Main.FocusManager?.GetFocusedElement()?.GetType().Name});
        // FIRST semantic oracle, before the future named peer/Invoke contract.
        Assert.Equal("First keyboard club",Named<TextBox>(f.Wizard,"Setup"+side+"NameBox").Text);
        Assert.False(NativePointer.IsCaptureWithin(f.Wizard));KbAssertChosen(f,side,"First keyboard club");Assert.Equal(oldOther,KbDraft(f.Wizard,other));f.AssertRecovery(recovery);f.Files.AssertBytes(saved);
        Border current=f.Tile(side,"First keyboard club");Assert.True(current.IsFocused);Assert.Equal("Selected",AutomationProperties.GetItemStatus(current));
        _=KbProvider(current,"First keyboard club",side);
        await Pump(40);KbScreenshot(f.Main,"saved-club-selected-focused-"+side);
        f.Main.Width=900;f.Main.Height=760;await Pump(60);current.BringIntoView();await Pump(40);
        Assert.True(current.IsFocused);KbAssertChosen(f,side,"First keyboard club");
        KbEvidence("compact-focus-"+side,new{viewport=f.Main.ClientSize,current.Bounds,current.IsFocused,status=AutomationProperties.GetItemStatus(current)});
        KbScreenshot(f.Main,"saved-club-selected-focused-compact-"+side);
        TextBox focusSink=Named<TextBox>(f.Wizard,"Setup"+side+"NameBox");Assert.True(focusSink.Focus(NavigationMethod.Tab));await Pump(40);
        Assert.False(current.IsKeyboardFocusWithin);KbAssertChosen(f,side,"First keyboard club");
        KbEvidence("compact-unfocused-"+side,new{viewport=f.Main.ClientSize,current.Bounds,current.IsFocused,status=AutomationProperties.GetItemStatus(current)});
        KbScreenshot(f.Main,"saved-club-selected-unfocused-compact-"+side);
    }

    [AvaloniaFact]
    public async Task OriginalSavedClubInvokeCannotChooseWhenDisabledHiddenOrDetachedByRealSetupRestart()
    {
        await using var f=await KbRoot.Create();var recovery=f.Recovery();var saved=f.Files.Bytes();
        await KbClick(f.Main,f.Tile("Home","Second keyboard club"));string draft=KbDraft(f.Wizard,"Home");
        Assert.Equal("Second keyboard club",Named<TextBox>(f.Wizard,"SetupHomeNameBox").Text);
        // Capture a CURRENT first-club provider AFTER normal selection repaints,
        // so disabled and hidden phases do not accidentally start already detached.
        Border first=f.Tile("Home","First keyboard club");IInvokeProvider retained=KbProvider(first,"First keyboard club","Home");
        Assert.Contains(first,Named<WrapPanel>(f.Wizard,"HomePresetPanel").Children);
        // Original baseline may fail missing real peer/Invoke here. Do NOT claim
        // later safety cases were reached or fake a provider to get past it.
        async Task Rejected(string stage)
        {
            await Pump(40);var focused=f.Main.FocusManager?.GetFocusedElement();
            Assert.NotSame(first,focused);
            Exception? error=null;try{retained.Invoke();}catch(Exception ex){error=ex;}await Pump(40);
            KbEvidence(stage,new{providerError=error?.ToString(),selected=Named<TextBox>(f.Wizard,"SetupHomeNameBox").Text,attached=first.GetVisualAncestors().Contains(f.Main),first.IsLoaded,first.IsEffectivelyVisible});
            Assert.Equal(draft,KbDraft(f.Wizard,"Home"));f.AssertRecovery(recovery);f.Files.AssertBytes(saved);Assert.True(KbAllowedRefusal(error));
            Assert.Same(focused,f.Main.FocusManager?.GetFocusedElement());Assert.False(first.IsKeyboardFocusWithin);
        }
        var panel=Named<WrapPanel>(f.Wizard,"HomePresetPanel");
        retained.Invoke(); // Queued while enabled, invalidated before dispatch.
        using(panel.SetValue((AvaloniaProperty)InputElement.IsEnabledProperty,false,BindingPriority.Animation))
        {Assert.False(panel.IsEffectivelyEnabled);await Pump(40);Assert.Equal(draft,KbDraft(f.Wizard,"Home"));f.AssertRecovery(recovery);f.Files.AssertBytes(saved);}
        using(panel.SetValue((AvaloniaProperty)InputElement.IsEnabledProperty,false,BindingPriority.Animation))
        {Assert.False(panel.IsEffectivelyEnabled);await Rejected("invoke-disabled");}
        await KbClick(f.Main,Named<Button>(f.Wizard,"CancelSetupButton"));await Until(()=>!f.Wizard.IsEffectivelyVisible,"Actual setup Cancel must hide the wizard.");
        await Rejected("invoke-hidden");await f.OpenAfl();
        await KbClick(f.Main,f.Tile("Home","Second keyboard club"));draft=KbDraft(f.Wizard,"Home");
        Assert.DoesNotContain(first,Named<WrapPanel>(f.Wizard,"HomePresetPanel").Children);
        await Rejected("invoke-detached-after-restart");
    }

    [AvaloniaTheory]
    [InlineData("Home")][InlineData("Away")]
    public async Task PublicUnifiedAflSavedClubInvokeChoosesCurrentSide(string side)
    {
        await using var f=await KbRoot.Create();string other=side=="Home"?"Away":"Home";
        string before=KbDraft(f.Wizard,other);var recovery=f.Recovery();var saved=f.Files.Bytes();
        KbProvider(f.Tile(side,"Third keyboard club with a deliberately long full accessible identity"),"Third keyboard club with a deliberately long full accessible identity",side).Invoke();await Pump(40);
        KbAssertChosen(f,side,"Third keyboard club with a deliberately long full accessible identity");Assert.True(f.Tile(side,"Third keyboard club with a deliberately long full accessible identity").IsFocused);
        Assert.Equal(before,KbDraft(f.Wizard,other));f.AssertRecovery(recovery);f.Files.AssertBytes(saved);
    }

    [AvaloniaFact]
    public async Task PublicSavedClubInvokeRejectsPendingPointerPressAndAcceptedUpStillChooses()
    {
        await using var f=await KbRoot.Create();await KbClick(f.Main,f.Tile("Home","Second keyboard club"));
        string draft=KbDraft(f.Wizard,"Home");var recovery=f.Recovery();var saved=f.Files.Bytes();
        Border first=f.Tile("Home","First keyboard club");var invoke=KbProvider(first,"First keyboard club","Home");Point start=await KbPoint(f.Main,first);
        IPointer? pointer=null;first.AddHandler(InputElement.PointerPressedEvent,(_,e)=>pointer=e.Pointer,RoutingStrategies.Tunnel,true);NativePointer.EnsureInitialized();
        try
        {
            f.Main.MouseMove(start);f.Main.MouseDown(start,MouseButton.Left);Dispatcher.UIThread.RunJobs();
            Assert.NotNull(pointer);Assert.Same(first,pointer.Captured);invoke.Invoke();await Pump(40);
            Assert.Equal(draft,KbDraft(f.Wizard,"Home"));Assert.Same(first,pointer.Captured);f.AssertRecovery(recovery);f.Files.AssertBytes(saved);
            f.Main.MouseUp(start,MouseButton.Left);KbAssertChosen(f,"Home","First keyboard club");
            Assert.False(NativePointer.IsCaptureWithin(f.Wizard));f.AssertRecovery(recovery);f.Files.AssertBytes(saved);
        }
        finally{pointer?.Capture(null);f.Main.MouseUp(start,MouseButton.Left);}
    }

    [AvaloniaFact]
    public async Task PublicSavedClubRapidClicksRemainImmediateAndHeldReorderPersistsOnce()
    {
        await using var f=await KbRoot.Create();var recovery=f.Recovery();var saved=f.Files.Bytes();string away=KbDraft(f.Wizard,"Away");
        var count=new KbCountingIo(PresetStorage.CurrentIoForTests);using var io=PresetStorage.UseIoForTests(count);
        foreach(string name in new[]{"First keyboard club","Second keyboard club","First keyboard club"})
        {
            Point point=await KbPoint(f.Main,f.Tile("Home",name));f.Main.MouseMove(point);f.Main.MouseDown(point,MouseButton.Left);f.Main.MouseUp(point,MouseButton.Left);
            KbAssertChosen(f,"Home",name);Assert.Equal(away,KbDraft(f.Wizard,"Away"));f.AssertRecovery(recovery);f.Files.AssertBytes(saved);Assert.Equal(0,count.Writes);
        }
        string draft=KbDraft(f.Wizard,"Home");Border first=f.Tile("Home","First keyboard club"),second=f.Tile("Home","Second keyboard club");
        Point start=await KbPoint(f.Main,first),end=await KbPoint(f.Main,second);IPointer? pointer=null;
        first.AddHandler(InputElement.PointerPressedEvent,(_,e)=>pointer=e.Pointer,RoutingStrategies.Tunnel,true);NativePointer.EnsureInitialized();
        try
        {
            f.Main.MouseMove(start);f.Main.MouseDown(start,MouseButton.Left);Dispatcher.UIThread.RunJobs();Assert.NotNull(pointer);Assert.Same(first,pointer.Captured);
            f.Main.MouseMove(end);Dispatcher.UIThread.RunJobs();Assert.Equal(.85,first.Opacity);
            f.Main.MouseUp(end,MouseButton.Left);var result=await PresetStorage.ReadAsync(KbAfl);await Pump(40);
            Assert.Equal(new[]{"Second keyboard club","First keyboard club","Third keyboard club with a deliberately long full accessible identity"},result.Presets.Select(p=>p.PresetName));Assert.Equal(1,count.Writes);
            Assert.Equal(draft,KbDraft(f.Wizard,"Home"));Assert.Equal(away,KbDraft(f.Wizard,"Away"));f.AssertRecovery(recovery);Assert.False(NativePointer.IsCaptureWithin(f.Wizard));
            KbAssertChosen(f,"Home","First keyboard club");
        }
        finally{pointer?.Capture(null);f.Main.MouseUp(end,MouseButton.Left);await PresetStorage.ReadAsync(KbAfl);}
    }
}

// OUTSIDE DRAFT ONLY — six cases total; uncompiled/unrun. Root owns integration.
// Uses actual providers, real dispatcher/rendering, public Close and real storage.
// One case is explicitly isolated Scorebug component API composition, not end-to-end UI.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private sealed record Mr7LcParts(CheckBox Toggle,OperatorSymbol Symbol,
        OperatorChoiceSurface Selection,TranslateTransform Shift,ScaleTransform Scale);
    private sealed record Mr7LcFrame(string Stage,double Fill,double Selection,double ThumbX,
        double ScaleX,double ScaleY,double Pulse,double Turn,bool Checked);
    private static Mr7LcParts Mr7LcReadParts(CheckBox toggle)
    {
        var thumb=Mr5FinalsPart<Border>(toggle,"NamesThumb");
        var transforms=Assert.IsType<TransformGroup>(thumb.RenderTransform);
        return new(toggle,Mr5FinalsPart<OperatorSymbol>(toggle,"NamesSymbol"),
            Mr5FinalsPart<OperatorChoiceSurface>(toggle,"NamesSelection"),
            Assert.Single(transforms.Children.OfType<TranslateTransform>()),
            Assert.Single(transforms.Children.OfType<ScaleTransform>()));
    }
    private static Mr7LcFrame Mr7LcRead(Mr7LcParts p,string stage)=>new(stage,
        p.Symbol.FillProgress,p.Selection.Progress,p.Shift.X,p.Scale.ScaleX,p.Scale.ScaleY,
        Mr5FinalsDiagnostic(p.Symbol,"Pulse"),Mr5FinalsDiagnostic(p.Symbol,"Turn"),p.Toggle.IsChecked==true);
    private static bool Mr7LcAt(Mr7LcFrame p,bool on)=>p.Fill==(on?1d:0d)
        &&p.Selection==(on?1d:0d)&&Math.Abs(p.ThumbX-(on?20d:0d))<.00001
        &&Math.Abs(p.ScaleX-1)<.00001&&Math.Abs(p.ScaleY-1)<.00001&&p.Pulse==0&&p.Turn==0;
    private static void Mr7LcCanonical(Mr7LcParts p,bool on)
    {
        var value=Mr7LcRead(p,"assert-canonical");Assert.True(Mr7LcAt(value,on),value.ToString());
        Assert.Equal(on,p.Toggle.IsChecked==true);Assert.Equal(on,p.Symbol.IsActive);
    }
    private static bool Mr7LcInterior(Mr7LcFrame p)=>p.Fill>.001&&p.Fill<.999
        &&p.Selection>.001&&p.Selection<.999&&p.ThumbX>.01&&p.ThumbX<19.99;
    private static async Task<Mr7LcParts> Mr7LcPrepare(AflFixture f)
    {
        OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);
        await OpenClockSection(f,false);Assert.Null(OperatorMotion.AnimationOverride);Assert.True(OperatorMotion.Enabled);
        var toggle=Named<CheckBox>(f.Window,"SettingsHideTeamNamesToggle");await RevealHideTeamNames(f,toggle);
        f.Window.MouseMove(new Point(1,1));await Settle(400);
        var parts=Mr7LcReadParts(toggle);
        if(toggle.IsChecked==true)Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle)).Toggle();
        await Mr5FinalsObserveUntil(()=>Mr7LcAt(Mr7LcRead(parts,"off-readiness"),false),"Actual OFF selection must naturally settle before a new departure.");
        return parts;
    }

    [AvaloniaTheory][InlineData("tab")][InlineData("search")][InlineData("window")]
    [Trait("Evidence","MR7ActualHostSelectionLifetime")]
    public async Task ActualHideNamesHostRetiresObservedSelectionAndReturnsOnlyCurrentTruth(string route)
    {
        await using var f=await AflFixture.Create(1280,850);var previousStyle=OperatorAppearance.MotionStyle;
        EventHandler? observer=null;var trace=new List<Mr7LcFrame>();string stage="prepare";
        try
        {
            var parts=await Mr7LcPrepare(f);string match=Scores(f.Match);
            var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(parts.Toggle));
            observer=(_,_)=>trace.Add(Mr7LcRead(parts,stage));NativeRendering.Rendering+=observer;
            stage="entry";provider.Toggle();Assert.True(HideTeamNamesValue(f));Assert.True(HideTeamNamesPreference(f));
            await Mr5FinalsObserveUntil(()=>trace.Any(x=>x.Stage=="entry"&&Mr7LcInterior(x)),"Require actual rendering frames with independently interior icon, surface and thumb before hiding host.");
            Assert.True(Mr7LcInterior(Mr7LcRead(parts,"before-hide")),"Host must still hide DURING the current transition, not after an old interior frame settled.");
            stage="hide";
            if(route=="tab")
            {
                Key(f.Window,PhysicalKey.Digit1,RawInputModifiers.Control);
                Assert.True(Named<TabItem>(f.Window,"MatchControlTab").IsSelected);
                await Until(()=>!parts.Toggle.IsLoaded&&TopLevel.GetTopLevel(parts.Toggle) is null,"Actual old settings content must detach.");
                Assert.Null(parts.Toggle.GetVisualRoot());
            }
            else if(route=="search")
            {
                Key(f.Window,PhysicalKey.F,RawInputModifiers.Control);f.Window.KeyTextInput("no-hide-names-section-7931");
                Assert.False(parts.Toggle.IsEffectivelyVisible);
            }
            else {f.Window.Hide();Assert.False(f.Window.IsVisible);}
            // Proposed ownership contract: at actual host hide/detach, no old
            // selected-state timelines continue/replay. This may expose a real
            // shared-motion gap; do not force opacity/progress to make it pass.
            Mr7LcCanonical(parts,true);Assert.Equal(match,Scores(f.Match));
            await Settle(700);Mr7LcCanonical(parts,true);
            stage="return";int first=trace.Count;
            if(route=="tab")
            {
                Key(f.Window,PhysicalKey.Digit4,RawInputModifiers.Control);await Settle(480);await OpenClockSection(f,false);
            }
            else if(route=="search"){Key(f.Window,PhysicalKey.Escape);await Settle(100);}
            else {f.Window.Show();await Settle(100);}
            await Until(()=>parts.Toggle.IsLoaded&&TopLevel.GetTopLevel(parts.Toggle) is not null&&parts.Toggle.IsEffectivelyVisible,"Current original checkbox must actually return to its host.");
            await RevealHideTeamNames(f,parts.Toggle);await Settle(700);
            Assert.True(HideTeamNamesValue(f));Assert.True(HideTeamNamesPreference(f));Assert.Equal(match,Scores(f.Match));
            Mr7LcCanonical(parts,true);Assert.True(trace.Count>first);
            Assert.All(trace.Skip(first),frame=>Assert.True(Mr7LcAt(frame,true),frame.ToString()));
        }
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace("mr7-actual-host-"+route,trace);OperatorAppearance.ConfigureMotionStyle(previousStyle);
        }
    }

    [AvaloniaFact][Trait("Evidence","MR7ActualCloseSelectionLifetime")]
    public async Task ActualCloseDuringObservedHideNamesSelectionPreservesModelAndRetiresMotion()
    {
        await using var f=await AflFixture.Create(1280,850);var previousStyle=OperatorAppearance.MotionStyle;
        EventHandler? observer=null;var trace=new List<Mr7LcFrame>();string stage="prepare";
        try
        {
            var parts=await Mr7LcPrepare(f);string match=Scores(f.Match);
            observer=(_,_)=>trace.Add(Mr7LcRead(parts,stage));NativeRendering.Rendering+=observer;
            stage="entry";Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(parts.Toggle)).Toggle();
            await Mr5FinalsObserveUntil(()=>trace.Any(x=>x.Stage=="entry"&&Mr7LcInterior(x)),"Close must follow real interior selection frames.");
            Assert.True(Mr7LcInterior(Mr7LcRead(parts,"before-close")),"Close must still interrupt actual current geometry.");
            bool closed=false;f.Window.Closed+=(_,_)=>closed=true;stage="close";f.Window.Close();
            Assert.True(closed);Assert.False(f.Window.IsVisible);Mr7LcCanonical(parts,true);
            Assert.Equal(match,Scores(f.Match));Assert.True(HideTeamNamesValue(f));Assert.True(HideTeamNamesPreference(f));
            await Settle(700);Mr7LcCanonical(parts,true);Assert.Equal(match,Scores(f.Match));
            Assert.All(trace.Where(x=>x.Stage=="close"),frame=>Assert.True(Mr7LcAt(frame,true),frame.ToString()));
        }
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace("mr7-actual-close-selection",trace);OperatorAppearance.ConfigureMotionStyle(previousStyle);
        }
    }

    [AvaloniaFact][Trait("Evidence","MR7RealUxDebounceAndConstructorReload")]
    public async Task OriginalHideNamesToggleDebouncesRealIsolatedUxFileAndCloseFlushReloadsConstructor()
    {
        string markerDirectory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-mr7-ux-diagnosis");Directory.CreateDirectory(markerDirectory);
        void Marker(string stage)=>File.AppendAllText(Path.Combine(markerDirectory,"mr7-ux-stages.log"),DateTimeOffset.UtcNow.ToString("O")+" "+stage+"\n");
        Marker("begin fixture");
        await using var f=await AflFixture.Create(1280,850);
        Marker("fixture ready");
        var app=Assert.IsType<RocheScoreboard.Mac.App>(Application.Current);bool previousSelfTest=app.IsSelfTestMode;
        Roche_Scoreboard.MainWindow? reload=null;
        try
        {
            await OpenClockSection(f,true);var toggle=Named<CheckBox>(f.Window,"SettingsHideTeamNamesToggle");await RevealHideTeamNames(f,toggle);
            var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle));
            if(toggle.IsChecked==true)provider.Toggle();await Settle(600); // drain suppressed prior UI saves
            bool latched=SelfTestStorage.IsEnabled;
            string fixtureHome=Assert.IsType<string>(typeof(AflFixture).GetField("dataHome",HiddenInstance)!.GetValue(f));
            void AssertOwnedDirectory()
            {
                Assert.Equal(latched,SelfTestStorage.IsEnabled);
                Assert.Equal(Path.GetFullPath(fixtureHome),Path.GetFullPath(Environment.GetEnvironmentVariable("ROCHE_DATA_HOME")!));
                Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(fixtureHome),StringComparison.Ordinal);
                Assert.StartsWith("roche-live-settings-search-",Path.GetFileName(fixtureHome),StringComparison.Ordinal);
                Assert.Equal(Path.GetFullPath(fixtureHome),NativeStoragePaths.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                string expected=latched?Path.Combine(SelfTestStorage.RootPath,"preferences"):Path.Combine(fixtureHome,"Roche_Scoreboard");
                Assert.Equal(Path.GetFullPath(expected),Path.GetFullPath(SelfTestStorage.DocumentsDirectory));
            }
            AssertOwnedDirectory();
            string path=UxPreferences.GetStoragePath();
            string expectedPath=Path.Combine(SelfTestStorage.DocumentsDirectory,"ux_preferences.json");
            Assert.Equal(Path.GetFullPath(expectedPath),Path.GetFullPath(path));
            Assert.False(f.Match.ClockRunning);string match=Scores(f.Match);Assert.False(toggle.IsChecked);
            // Actual TrySaveUxPreferences intentionally skips IsSelfTestMode.
            // Narrowly permit that existing timer while storage stays latched;
            // do not create/load windows or start a clock in this interval.
            Marker("before real debounce flag");
            app.IsSelfTestMode=false;AssertOwnedDirectory();Assert.Equal(path,UxPreferences.GetStoragePath());
            Marker("before original provider ON");provider.Toggle();Marker("original provider ON returned");Assert.True(toggle.IsChecked);Assert.True(HideTeamNamesPreference(f));
            await Until(()=>File.Exists(path)&&UxPreferences.Load().HideTeamNames,"Actual350ms UX debounce must write the new preference without invoking private save methods.");
            Marker("real debounce persisted");app.IsSelfTestMode=true;
            Assert.Equal(match,Scores(f.Match));Assert.True(UxPreferences.Load().HideTeamNames);
            // A fresh ORIGINAL constructor reads the real file. This is not a
            // fake Restore handler and not a full match-recovery acceptance.
            Marker("before first fresh constructor");reload=new Roche_Scoreboard.MainWindow();Marker("first fresh constructor returned");
            Assert.True(Named<CheckBox>(reload,"SettingsHideTeamNamesToggle").IsChecked);
            Marker("before first unshown reload close");reload.Close();Marker("first unshown reload close returned");Mr7LcDetachQuarterLabels(reload);reload=null;

            // OFF followed by immediate actual Close exercises the other value
            // and pending-save flush before waiting for350ms. Close may also
            // persist recovery/layout into the same isolated fixture storage.
            Marker("before pending flush flag");app.IsSelfTestMode=false;AssertOwnedDirectory();Assert.Equal(path,UxPreferences.GetStoragePath());provider.Toggle();Marker("provider OFF returned");Assert.False(toggle.IsChecked);
            Assert.NotNull(f.Window.GetType().GetField("_uxPreferencesSaveTimer",HiddenInstance)!.GetValue(f.Window));
            bool closed=false;f.Window.Closed+=(_,_)=>closed=true;Marker("before original close flush");f.Window.Close();Marker("original close flush returned");Assert.True(closed);
            app.IsSelfTestMode=true;
            Assert.True(File.Exists(path));Assert.False(UxPreferences.Load().HideTeamNames);
            Assert.Equal(match,Scores(f.Match));
            reload=new Roche_Scoreboard.MainWindow();
            Assert.False(Named<CheckBox>(reload,"SettingsHideTeamNamesToggle").IsChecked);
        }
        finally
        {
            app.IsSelfTestMode=true; // no startup/cloud service activation on cleanup
            if(reload is not null){reload.Close();Mr7LcDetachQuarterLabels(reload);}
            app.IsSelfTestMode=previousSelfTest;
        }
    }
    private static void Mr7LcDetachQuarterLabels(Roche_Scoreboard.MainWindow window)
    {
        // Same fixture-only cleanup as existing AflFixture; never clear global
        // subscribers or invoke a private handler.
        var handler=window.GetType().GetMethod("OnQuarterLabelServiceChanged",HiddenInstance);
        if(handler is not null)QuarterLabelService.LabelsChanged-=handler.CreateDelegate<Action>(window);
    }

    [AvaloniaFact][Trait("Evidence","MR7IsolatedScorebugOverlayComposition")]
    public async Task IsolatedScorebugApiKeepsHidePreferenceAndOverlaySuppressionComposed()
    {
        // Explicit component API test, not a checkbox/menu path or live hardware.
        // No private state is seeded; private reveal progress is READ only.
        await using var f=await AflFixture.Create(1280,850);
        var bug=new ScorebugControl();var output=new Window{Width=772,Height=386,Content=bug};
        double Progress()=>(double)bug.GetType().GetField("_teamNameRevealProgress",HiddenInstance)!.GetValue(bug)!;
        try
        {
            bug.SetTeams("Composition Home","CMP","Composition Away","AWY");bug.SetScores(1,0,0,1);output.Show();
            await Until(()=>bug.IsLoaded,"Real component must load before observing name-panel composition.");
            bug.SetHideTeamNames(false);await Until(()=>Progress()>=.999,"Names allowed without an overlay must reveal normally.");
            bug.ShowScorelessBar();await Until(()=>bug.IsOverlayActive&&Progress()<=.001,"Actual public overlay must suppress name panels.");
            bug.SetHideTeamNames(true);bug.SetHideTeamNames(false);
            await Settle(300);Assert.True(bug.IsOverlayActive);Assert.InRange(Progress(),0,.001);
            Assert.True(bug.CancelCurrentOverlay());await Until(()=>!bug.IsOverlayActive&&Progress()>=.999,"OFF only allows panels once actual overlay exit is complete.");
            bug.SetHideTeamNames(true);await Until(()=>Progress()<=.001,"Explicit hide must suppress names without an overlay.");
            bug.ShowScorelessBar();await Until(()=>bug.IsOverlayActive,"Second real overlay must enter.");
            Assert.True(bug.CancelCurrentOverlay());await Until(()=>!bug.IsOverlayActive,"Actual second overlay must close.");
            await Settle(700);Assert.InRange(Progress(),0,.001);
            Assert.Equal("6",Named<TextBlock>(bug,"ExpHomeTotalText").Text);Assert.Equal("1",Named<TextBlock>(bug,"ExpAwayTotalText").Text);
            Capture(output,"mr7-isolated-overlay-hidden-preference");
        }
        finally {output.Close();}
    }
}

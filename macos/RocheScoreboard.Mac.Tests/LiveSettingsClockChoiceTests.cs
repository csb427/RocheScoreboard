using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

/// <summary>
/// Regression checks. The real Windows-derived MainWindow opens AFL through its
/// visible Home/setup/Settings routes; no private focus, selection or navigation
/// state is forced. Only fixture storage and read-only model observation use
/// reflection. Linux headless routing is not native WPF keyboard acceptance.
/// </summary>
public sealed partial class LiveSettingsClockChoiceTests
{
    private const BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private static async Task Settle(int milliseconds = 100)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Until(Func<bool> state, string explanation)
    {
        // Fixture readiness bound, not a performance or motion-duration oracle.
        for (int attempt = 0; attempt < 50 && !state(); attempt++) await Settle(100);
        Assert.True(state(), explanation);
    }

    private static T Named<T>(Control owner, string name) where T : Control =>
        Assert.IsAssignableFrom<T>(owner.FindControl<T>(name));

    private static void Key(Window owner, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        owner.KeyPressQwerty(key, modifiers);
        owner.KeyReleaseQwerty(key, modifiers);
    }

    private static async Task Tap(Window owner, Control target)
    {
        WriteNativeFontFixtureStage(owner, target, "before-reveal");
        target.BringIntoView();
        await Settle();
        WriteNativeFontFixtureStage(owner, target, "after-reveal");
        Assert.True(target.IsEffectivelyVisible, target.Name + " must be visible before pointer activation.");
        Assert.True(target.IsEnabled, target.Name + " must be enabled before pointer activation.");
        var origin = target.TranslatePoint(default, owner);
        bool clock=target.Name is "ClockModeCountUp" or "ClockModeCountdown";
        Point[] Probes(Point p)=>new[]
        {
            new Point(p.X+target.Bounds.Width/2,p.Y+target.Bounds.Height/2),
            new Point(p.X+3,p.Y+target.Bounds.Height/2),new Point(p.X+target.Bounds.Width-3,p.Y+target.Bounds.Height/2),
            new Point(p.X+target.Bounds.Width/2,p.Y+3),new Point(p.X+target.Bounds.Width/2,p.Y+target.Bounds.Height-3)
        };
        bool ClockReady()=>origin is Point p && new Rect(owner.ClientSize).Contains(new Rect(p,target.Bounds.Size))
            && Probes(p).All(point=>Within(target,owner.InputHitTest(point)));
        string scrollName=target.Name+"-"+(int)owner.ClientSize.Width+"x"+(int)owner.ClientSize.Height+"-reduce-"+OperatorAppearance.ReduceMotion;
        var scrollEvidence=new List<object>();
        for(int step=0;step<20 && clock && !ClockReady();step++)
        {
            var outer=Named<ScrollViewer>(owner,"AflSettingsPageViewport");
            var wheel=outer.TranslatePoint(new Point(3,outer.Bounds.Height/2),owner);
            Assert.NotNull(wheel);Assert.NotNull(origin);
            var wheelHit=owner.InputHitTest(wheel.Value);
            Assert.True(Within(outer,wheelHit),"Compact clock reveal wheel must reach the real outer page, not the navigation dock.");
            var probes=Probes(origin.Value);bool top=Within(target,owner.InputHitTest(probes[3]));bool bottom=Within(target,owner.InputHitTest(probes[4]));
            double direction=!top && bottom?1:top && !bottom?-1:probes[0].Y<wheel.Value.Y?1:-1;
            scrollEvidence.Add(new{step,origin,target.Bounds,outer.Offset,outer.Extent,outer.Viewport,top,bottom,direction,
                hits=probes.Select(point=>(owner.InputHitTest(point) as Control)?.Name+":"+owner.InputHitTest(point)?.GetType().Name).ToArray()});
            if(step==0)Capture(owner,"mr4-scroll-before-"+scrollName);
            owner.MouseWheel(wheel.Value,new Vector(0,direction),RawInputModifiers.None);
            await Settle(80);origin=target.TranslatePoint(default,owner);
        }
        if(clock && Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} scrollDirectory)
        {
            Directory.CreateDirectory(scrollDirectory);
            File.WriteAllText(Path.Combine(scrollDirectory,"mr4-scroll-"+scrollName+".json"),JsonSerializer.Serialize(scrollEvidence,new JsonSerializerOptions{WriteIndented=true}));
            Capture(owner,"mr4-scroll-after-"+scrollName);
        }
        Assert.NotNull(origin);
        if(clock)Assert.All(Probes(origin.Value),point=>Assert.True(Within(target,owner.InputHitTest(point)),
            target.Name+" all five real input probes must reach its own template after bounded physical scrolling; failed "+point));
        Assert.True(new Rect(owner.ClientSize).Contains(new Rect(origin.Value, target.Bounds.Size)),
            target.Name + " must have its actual hit region inside the current window.");
        var point = new Point(origin.Value.X + target.Bounds.Width / 2, origin.Value.Y + target.Bounds.Height / 2);
        var hit = owner.InputHitTest(point);
        if (!ReferenceEquals(hit, target) && !(hit is Visual visual && visual.GetVisualAncestors().Contains(target)))
        {
            string directory = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") ?? Path.Combine(Path.GetTempPath(), "roche-live-settings-search");
            Directory.CreateDirectory(directory);
            using var frame = owner.CaptureRenderedFrame();
            frame?.Save(Path.Combine(directory, "occluded-" + target.Name + ".png"));
        }
        Assert.True(ReferenceEquals(hit, target) || hit is Visual child && child.GetVisualAncestors().Contains(target),
            target.Name + " actual pointer probe must hit its own template; hit " + (hit as Control)?.Name + ":" + hit?.GetType().Name + " at " + point);
        WriteNativeFontFixtureStage(owner, target, "before-pointer");
        owner.MouseMove(point);
        owner.MouseDown(point, MouseButton.Left);
        owner.MouseUp(point, MouseButton.Left);
        await Settle(220);
        WriteNativeFontFixtureStage(owner, target, "after-pointer");
    }

    private sealed class AflFixture : IAsyncDisposable
    {
        private readonly App app = Assert.IsType<App>(Application.Current);
        private readonly bool previousSelfTest = ((App)Application.Current!).IsSelfTestMode;
        private readonly AppTheme previousTheme = ((App)Application.Current!).ThemeService.CurrentTheme;
        private readonly UiDensity previousDensity = ((App)Application.Current!).ThemeService.CurrentDensity;
        private readonly bool? previousAnimation = OperatorMotion.AnimationOverride;
        private readonly bool previousGlass = OperatorAppearance.GlassEnabled;
        private readonly bool previousReduce = OperatorAppearance.ReduceMotion;
        private readonly string? previousQuarterOverride = QuarterLabelService.CurrentOverride;
        private readonly string? previousDataHome = Environment.GetEnvironmentVariable("ROCHE_DATA_HOME");
        private readonly string dataHome = Path.Combine(Path.GetTempPath(), "roche-live-settings-search-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, byte[]?> files = new(StringComparer.Ordinal);
        private string? documents;
        private Roche_Scoreboard.MainWindow? window;
        internal Roche_Scoreboard.MainWindow Window => window!;
        internal MatchManager Match => Assert.IsType<MatchManager>(Window.GetType().GetField("_match", HiddenInstance)!.GetValue(Window));

        private void Preserve(string path)
        {
            foreach (string candidate in new[] { path, path + ".bak", path + ".tmp" })
                if (!files.ContainsKey(candidate)) files[candidate] = File.Exists(candidate) ? File.ReadAllBytes(candidate) : null;
        }

        internal static async Task<AflFixture> Create(int width, int height)
        {
            var fixture = new AflFixture();
            try
            {
                Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", fixture.dataHome);
                fixture.documents = SelfTestStorage.DocumentsDirectory;
                Directory.CreateDirectory(fixture.documents);
                // SelfTestStorage may be process-latched rather than use the
                // new environment directory. Preserve its actual files too.
                foreach (string file in Directory.EnumerateFiles(fixture.documents, "*", SearchOption.AllDirectories))
                    fixture.files[file] = File.ReadAllBytes(file);
                // Resolve conditional storage paths in the SAME mode used by
                // the actual constructor and interaction. The native App does
                // not initialize the global storage latch for this fixture.
                fixture.app.IsSelfTestMode = true;
                foreach (string path in new[]
                {
                    GameStateService.RecoveryStatePath,
                    (string)typeof(GameStateService).GetProperty("LegacyStateFilePath", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!,
                    UxPreferences.GetStoragePath(),
                    (string)typeof(OperatorWorkflowPreferences).GetMethod("GetStoragePath", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [false])!,
                    (string)typeof(AflKeybindSettings).GetMethod("GetStoragePath", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!
                }) fixture.Preserve(path);
                foreach (string path in fixture.files.Keys) if (File.Exists(path)) File.Delete(path);

                OperatorMotion.AnimationOverride = false;
                fixture.window = new Roche_Scoreboard.MainWindow();
                fixture.Window.Show();
                fixture.Window.Width = 1280; fixture.Window.Height = 850;
                var home = Named<SportSelectionScreen>(fixture.Window, "SportSelectionPanel");
                await Until(() => home.IsEffectivelyVisible && home.GetVisualDescendants().OfType<Button>().Any(button => button.Name == "AflLaunchAction" && button.Bounds.Width > 0), "The real Home route and launch target must be mounted and measured.");
                var start = Assert.Single(home.GetVisualDescendants().OfType<Button>(), button => button.Name == "AflLaunchAction");
                await Tap(fixture.Window, start);

                var wizard = Named<SetupWizard>(fixture.Window, "SetupWizardPanel");
                await Until(() => Named<Control>(wizard, "SportStepContainer").IsEffectivelyVisible, "Start Game must open the unified sport choice.");
                await Tap(fixture.Window, Named<RadioButton>(wizard, "AflSportChoice"));
                await Tap(fixture.Window, Named<Button>(wizard, "NextButton"));
                await Until(() => Named<TextBox>(wizard, "SetupHomeNameBox").IsEffectivelyVisible, "AFL teams must be the active setup step.");
                // Public fixture fields, not focus/navigation state. Search
                // interaction itself is exercised with actual keyboard input.
                Named<TextBox>(wizard, "SetupHomeNameBox").Text = "Search Route Home";
                Named<TextBox>(wizard, "SetupAwayNameBox").Text = "Search Route Away";
                await Tap(fixture.Window, Named<Button>(wizard, "NextButton"));
                await Tap(fixture.Window, Named<OperatorGlassSurface>(wizard, "CountUpCard"));
                Assert.True(Named<RadioButton>(wizard,"SetupCountUp").IsChecked);
                await Tap(fixture.Window, Named<Button>(wizard, "NextButton"));
                // Empty location ensures this genuine completion route does
                // not request weather/network services in the test fixture.
                Named<ComboBox>(wizard, "SetupWeatherLocation").Text = "";
                await Tap(fixture.Window, Named<Button>(wizard, "NextButton"));
                await Until(() => Named<Control>(fixture.Window, "MainContent").IsEffectivelyVisible && !wizard.IsEffectivelyVisible,
                    "The completed AFL setup must mount the real operator console.");

                await Tap(fixture.Window, Named<Button>(fixture.Window, "HomeGoalButton"));
                await Tap(fixture.Window, Named<Button>(fixture.Window, "AwayBehindButton"));
                Assert.Equal(6, fixture.Match.HomeTotal);
                Assert.Equal(1, fixture.Match.AwayTotal);
                Assert.Equal(2, fixture.Match.Events.Count);
                Assert.False(fixture.Match.ClockRunning);

                // Default Essentials bypasses the offending ancestor branch.
                // Enter Advanced Tools through its real visible command.
                var tools = Named<Button>(fixture.Window, "GuidedModeButton");
                Assert.Equal("ADVANCED TOOLS", tools.Content);
                await Tap(fixture.Window, tools);
                Assert.Equal("RETURN TO ESSENTIALS", tools.Content);
                fixture.Window.Width = width; fixture.Window.Height = height;
                await Settle(180);
                await Tap(fixture.Window, Named<TabItem>(fixture.Window, "MatchSetupTab"));
                await Until(() => fixture.Window.GetVisualDescendants().OfType<SettingsWorkspace>().Any(item => item.IsEffectivelyVisible),
                    "The real AFL SETUP tab must mount its visible SettingsWorkspace.");
                // Production absorbs trailing pointer input for450ms after a
                // tab change. Establish an independent new interaction after
                // that guard, rather than misreporting a blocked fixture click.
                await Settle(480);
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (window is not null)
                {
                    window.Close();
                    // This existing static subscription lacks production
                    // teardown. Detach only this fixture window's own handler;
                    // do not replace or clear anyone else's event callbacks.
                    var handler = window.GetType().GetMethod("OnQuarterLabelServiceChanged", HiddenInstance);
                    if (handler is not null) QuarterLabelService.LabelsChanged -= handler.CreateDelegate<Action>(window);
                    await Settle(180);
                }
                OperatorAppearance.Configure(previousGlass, previousReduce);
                app.ThemeService.SetTheme(previousTheme);
                app.ThemeService.SetDensity(previousDensity);
                QuarterLabelService.SetCurrentOverride(previousQuarterOverride);
                if (documents is not null && Directory.Exists(documents))
                    foreach (string path in Directory.EnumerateFiles(documents, "*", SearchOption.AllDirectories))
                        if (!files.ContainsKey(path)) File.Delete(path);
                foreach (var (path, bytes) in files)
                {
                    if (bytes is null) { if (File.Exists(path)) File.Delete(path); }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.WriteAllBytes(path, bytes);
                    }
                }
            }
            finally
            {
                OperatorMotion.AnimationOverride = previousAnimation;
                app.IsSelfTestMode = previousSelfTest;
                Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", previousDataHome);
                if (Directory.Exists(dataHome)) Directory.Delete(dataHome, true);
            }
        }
    }


    private static bool Within(Control parent, object? item) => ReferenceEquals(parent, item)
        || item is Visual visual && visual.GetVisualAncestors().Contains(parent);

    private static async Task<SettingsWorkspace> OpenClockSection(AflFixture fixture, bool reduced)
    {
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled, reduced);
        OperatorMotion.AnimationOverride = null;
        Assert.Equal(!reduced, OperatorMotion.Enabled); // This headless host enables normal client animations.
        var workspace = Assert.Single(fixture.Window.GetVisualDescendants().OfType<SettingsWorkspace>(), x => x.IsEffectivelyVisible);
        // Real Ctrl+F route avoids invoking private category methods or forcing a
        // collapsed MATCH section visible. This exact copy uniquely identifies
        // the existing live-clock section, including at the compact viewport.
        Key(fixture.Window, PhysicalKey.F, RawInputModifiers.Control);
        var search = Assert.Single(workspace.GetVisualDescendants().OfType<OperatorSearchBox>(), x => x.IsEffectivelyVisible);
        Assert.True(search.IsFocused);
        fixture.Window.KeyTextInput("Choose the scoreboard clock");
        await Settle(150);
        Assert.True(Named<Border>(fixture.Window, "SettingsSection_Match").IsEffectivelyVisible);
        Assert.False(Named<Border>(fixture.Window, "SettingsSection_Teams").IsVisible);
        return workspace;
    }

    private static RadioButton VisibleChoice(AflFixture fixture, string cardName)
    {
        var card = Named<Border>(fixture.Window, cardName);
        return Assert.Single(card.GetVisualDescendants().OfType<RadioButton>(), x => x.IsEffectivelyVisible);
    }

    private static string Scores(MatchManager match) => JsonSerializer.Serialize(new
    {
        match.HomeTotal, match.AwayTotal, match.Quarter, match.Events,
        match.ElapsedInQuarter, match.ClockRunning, match.QuarterDuration
    });

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(directory, name + ".png"));
    }

    [AvaloniaTheory]
    [InlineData(1280,850,false)][InlineData(1280,850,true)]
    [InlineData(420,320,false)][InlineData(420,320,true)]
    [Trait("Evidence","MR4PublicLiveSettings")]
    public async Task ClockCardsExposeOnlyTheirOriginalNamedRadioAndSelectionPeer(int width,int height,bool reduced)
    {
        await using var f = await AflFixture.Create(width,height);
        await OpenClockSection(f,reduced);
        string before = JsonSerializer.Serialize(f.Match.ToState());
        var up = Named<RadioButton>(f.Window,"ClockModeCountUp");
        var down = Named<RadioButton>(f.Window,"ClockModeCountdown");
        var upCard = Named<Border>(f.Window,"SettingsCountUpCard");
        var downCard = Named<Border>(f.Window,"SettingsCountdownCard");
        var visibleUp = VisibleChoice(f,"SettingsCountUpCard");
        var visibleDown = VisibleChoice(f,"SettingsCountdownCard");
        Capture(f.Window,$"mr4-mounted-{width}-{reduced}");
        Assert.Same(up,visibleUp); // Baseline: current visible proxy is a different object.
        Assert.Same(down,visibleDown);
        foreach(var choice in new[]{up,down})
        {
            var intended=Assert.IsType<Avalonia.Styling.ControlTheme>(RocheScoreboard.Mac.Native.NativeResources.FindOrNull(choice,"WizardClockChoice"));
            Assert.Same(intended,choice.Theme);
            Assert.Single(choice.GetVisualDescendants().OfType<Border>(),part=>part.Name=="ClockFocus");
            Assert.Equal(.12,Assert.Single(choice.GetVisualDescendants().OfType<OperatorChoiceSurface>()).OriginX);
            Assert.DoesNotContain(choice.GetVisualDescendants().OfType<Control>(),part=>part.Name=="SelectionMark");
        }
        Assert.Equal("ClockMode",up.GroupName); Assert.Equal("ClockMode",down.GroupName);
        Assert.Equal(typeof(Border),upCard.GetType()); Assert.Equal(typeof(Border),downCard.GetType());
        Assert.False(upCard.Focusable); Assert.False(downCard.Focusable);
        Assert.DoesNotContain(f.Window.GetVisualDescendants().OfType<RadioButton>(),x=>x.GroupName=="AflLiveClockDesign");
        Assert.True(up.IsChecked); Assert.False(down.IsChecked);
        Assert.True(Named<Border>(f.Window,"SettingsCountUpCheck").IsEffectivelyVisible);
        Assert.False(Named<Border>(f.Window,"SettingsCountdownCheck").IsEffectivelyVisible);
        Assert.Equal(new Thickness(1),upCard.BorderThickness);Assert.Equal(new Thickness(1),downCard.BorderThickness);
        var upPeer = Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(up));
        var downPeer = Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(down));
        Assert.True(upPeer.IsSelected); Assert.False(downPeer.IsSelected);
        Assert.False(ControlAutomationPeer.CreatePeerForElement(upCard) is IInvokeProvider);
        Assert.False(ControlAutomationPeer.CreatePeerForElement(downCard) is IInvokeProvider);
        Assert.Contains("elapsed",(AutomationProperties.GetName(up)??"").ToLowerInvariant());
        Assert.Contains("remaining",(AutomationProperties.GetName(down)??"").ToLowerInvariant());
        Assert.Equal(before,JsonSerializer.Serialize(f.Match.ToState()));
    }

    [AvaloniaTheory]
    [InlineData(1280,850,false)][InlineData(1280,850,true)]
    [InlineData(420,320,false)][InlineData(420,320,true)]
    [Trait("Evidence","MR4PublicLiveSettings")]
    public async Task RealTabArrowSpacePreserveScoreAndUseOneClockSelection(int width,int height,bool reduced)
    {
        await using var f = await AflFixture.Create(width,height);
        await OpenClockSection(f,reduced);
        var up = VisibleChoice(f,"SettingsCountUpCard");
        var down = VisibleChoice(f,"SettingsCountdownCard");
        string score = Scores(f.Match);
        // Begin from the actual search focus established by Ctrl+F, never Focus()
        // a hidden original or synthesize Checked. Native selected-group entry
        // may have one Tab stop; the test does not require two separate stops.
        bool entered = false;
        for(int i=0;i<90;i++)
        {
            Key(f.Window,PhysicalKey.Tab); await Settle(15);
            var focused = f.Window.FocusManager?.GetFocusedElement();
            Assert.NotSame(Named<Border>(f.Window,"SettingsCountUpCard"),focused);
            Assert.NotSame(Named<Border>(f.Window,"SettingsCountdownCard"),focused);
            if(ReferenceEquals(focused,up) || ReferenceEquals(focused,down)){entered=true;break;}
        }
        Assert.True(entered,"Real Tab traversal must enter a visible native clock radio without a duplicate card focus stop.");
        Assert.True(Within(up,f.Window.FocusManager?.GetFocusedElement()),"The selected Count up radio must own initial group focus.");
        Key(f.Window,PhysicalKey.ArrowRight); await Settle(30);
        Assert.True(down.IsChecked); Assert.False(up.IsChecked); Assert.Equal(ClockMode.Countdown,f.Match.ClockMode);
        Assert.Equal(score,Scores(f.Match));
        Key(f.Window,PhysicalKey.Space); await Settle(30);
        Assert.True(down.IsChecked); Assert.False(up.IsChecked); Assert.Equal(ClockMode.Countdown,f.Match.ClockMode);
        Assert.Equal(score,Scores(f.Match));
        Key(f.Window,PhysicalKey.ArrowLeft); await Settle(30);
        Assert.True(up.IsChecked); Assert.False(down.IsChecked); Assert.Equal(ClockMode.CountUp,f.Match.ClockMode);
        Assert.Equal(score,Scores(f.Match));
        // Space above is native radio selection, not the match clock shortcut.
        // Letter shortcuts on non-text controls retain the existing app policy.
        Capture(f.Window,$"mr4-keyboard-{width}-{reduced}");
        Key(f.Window,PhysicalKey.Tab); await Settle(30);
        Assert.False(ReferenceEquals(up,f.Window.FocusManager?.GetFocusedElement()) || ReferenceEquals(down,f.Window.FocusManager?.GetFocusedElement()),"Tab must leave the radio group.");
        Assert.Same(Named<RadioButton>(f.Window,"ClockModeCountUp"),up);
        Assert.Same(Named<RadioButton>(f.Window,"ClockModeCountdown"),down);
    }

    [AvaloniaTheory]
    [InlineData(1280,850,false)][InlineData(1280,850,true)]
    [InlineData(420,320,false)][InlineData(420,320,true)]
    [Trait("Evidence","MR4PublicLiveSettings")]
    public async Task PointerAndSelectionProviderShareAuthorityAndSearchCancelDoesNotMutateMatch(int width,int height,bool reduced)
    {
        await using var f = await AflFixture.Create(width,height);
        var workspace = await OpenClockSection(f,reduced);
        var up = VisibleChoice(f,"SettingsCountUpCard"); var down = VisibleChoice(f,"SettingsCountdownCard");
        string score = Scores(f.Match);
        await Tap(f.Window,down);
        Assert.Equal(ClockMode.Countdown,f.Match.ClockMode); Assert.True(down.IsChecked); Assert.False(up.IsChecked);
        Assert.Equal(score,Scores(f.Match));
        string selected = JsonSerializer.Serialize(f.Match.ToState());
        await Tap(f.Window,down);
        Assert.Equal(selected,JsonSerializer.Serialize(f.Match.ToState()));
        var upProvider = Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(up));
        var downProvider = Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(down));
        upProvider.Select(); // Real Avalonia accessibility provider; NOT native Windows UIA execution.
        await Settle(30);
        Assert.Equal(ClockMode.CountUp,f.Match.ClockMode); Assert.True(upProvider.IsSelected); Assert.False(downProvider.IsSelected);
        Assert.True(Named<RadioButton>(f.Window,"ClockModeCountUp").IsChecked);
        Assert.False(Named<RadioButton>(f.Window,"ClockModeCountdown").IsChecked);
        Assert.Equal(score,Scores(f.Match));
        string beforeClose = JsonSerializer.Serialize(f.Match.ToState());
        Key(f.Window,PhysicalKey.F,RawInputModifiers.Control);
        f.Window.KeyTextInput("zz-no-clock-section-5839"); await Settle(100);
        Assert.False(Named<Border>(f.Window,"SettingsSection_Match").IsVisible);
        Key(f.Window,PhysicalKey.Escape); await Settle(120);
        Assert.True(Named<Border>(f.Window,"SettingsSection_Match").IsEffectivelyVisible);
        Assert.Same(up,VisibleChoice(f,"SettingsCountUpCard")); Assert.Same(down,VisibleChoice(f,"SettingsCountdownCard"));
        Assert.Equal(beforeClose,JsonSerializer.Serialize(f.Match.ToState()));
        await Settle(800);
        Assert.Equal(beforeClose,JsonSerializer.Serialize(f.Match.ToState()));
        Assert.Same(Named<RadioButton>(f.Window,"ClockModeCountUp"),up);
        Assert.Same(Named<RadioButton>(f.Window,"ClockModeCountdown"),down);
        Capture(f.Window,$"mr4-pointer-{width}-{reduced}");
    }
}

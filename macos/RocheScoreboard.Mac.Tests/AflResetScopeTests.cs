// Actual public-route and painted-geometry checks in the translated runtime;
// regressions, not Windows WPF desktop/assistive-technology acceptance.
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Services.Cloud;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class AflResetScopeTests
{
    private static T Named<T>(Control root, string name) where T : Control =>
        Assert.IsAssignableFrom<T>(root.GetVisualDescendants().OfType<T>().SingleOrDefault(item => item.Name == name) ?? root.FindControl<T>(name));
    // Reflection reads the live readonly model and existing state; it never
    // invokes handlers or changes private eligibility/focus/visibility fields.
    private static T Read<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static string Snapshot(MatchManager match) => JsonSerializer.Serialize(match.ToState());
    private static async Task Settle(int milliseconds = 100)
    { await Task.Delay(milliseconds); Dispatcher.UIThread.RunJobs(); }
    private static async Task Until(Func<bool> condition, string reason)
    {
        for (int attempt = 0; attempt < 40 && !condition(); attempt++) await Settle(100);
        Assert.True(condition(), reason);
    }
    private static void Key(Window owner, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    { owner.KeyPressQwerty(key, modifiers); owner.KeyReleaseQwerty(key, modifiers); Dispatcher.UIThread.RunJobs(); }
    private static bool Within(Control root, IInputElement? target) =>
        ReferenceEquals(root, target) || target is Visual visual && visual.GetVisualAncestors().Contains(root);

    private static Rect PaintedBounds(Window owner, Control target)
    {
        // Translate all four corners, so a still-active scale/travel is not
        // mistaken for the untransformed layout allocation.
        target.UpdateLayout();
        var points = new[] { new Point(), new Point(target.Bounds.Width, 0),
            new Point(0, target.Bounds.Height), new Point(target.Bounds.Width, target.Bounds.Height) }
            .Select(p => target.TranslatePoint(p, owner)).ToArray();
        Assert.All(points, point => Assert.NotNull(point));
        double left = points.Min(p => p!.Value.X), top = points.Min(p => p!.Value.Y);
        return new Rect(left, top, points.Max(p => p!.Value.X) - left, points.Max(p => p!.Value.Y) - top);
    }
    private static void AssertPhysicalTarget(Window owner, Control target)
    {
        Assert.True(target.IsEffectivelyVisible && target.IsEnabled && target.IsHitTestVisible);
        Rect painted = PaintedBounds(owner, target);
        Assert.True(painted.Width > 0 && painted.Height > 0);
        Assert.True(new Rect(owner.Bounds.Size).Contains(painted), $"{target.Name} full painted target must fit: {painted}, owner {owner.Bounds}");
        foreach (var point in new[] { painted.Center, new Point(painted.Left + 4, painted.Center.Y),
                     new Point(painted.Right - 4, painted.Center.Y), new Point(painted.Center.X, painted.Top + 4),
                     new Point(painted.Center.X, painted.Bottom - 4) })
            Assert.True(Within(target, owner.InputHitTest(point)), target.Name + " must own its actual pointer probe at " + point);
    }
    private static async Task Tap(Window owner, Control target)
    {
        AssertPhysicalTarget(owner, target);
        Point point = PaintedBounds(owner, target).Center;
        owner.MouseMove(point); owner.MouseDown(point, MouseButton.Left); owner.MouseUp(point, MouseButton.Left);
        await Settle();
    }
    private static async Task RevealByKeyboard(Window owner, Control target)
    {
        // OperatorViewport intentionally refuses ordinary programmatic focus
        // scrolling. Hold actual Tab while requesting its public focus route.
        owner.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        try { Assert.True(target.Focus(NavigationMethod.Tab)); target.BringIntoView(); Dispatcher.UIThread.RunJobs(); }
        finally { owner.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None); }
        await Settle();
    }
    private static void Capture(Fixture fixture, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var image = fixture.Window.CaptureRenderedFrame();
        Assert.NotNull(image); image.Save(Path.Combine(directory, name + ".png"));
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(new
        {
            owner = fixture.Window.Bounds, shell = PaintedBounds(fixture.Window, fixture.Shell),
            cancel = PaintedBounds(fixture.Window, fixture.Cancel), confirm = PaintedBounds(fixture.Window, fixture.Confirm),
            overlayVisible = fixture.Overlay.IsVisible, fixture.Overlay.Opacity,
            focused = (fixture.Window.FocusManager?.GetFocusedElement() as Control)?.Name
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly App app = Assert.IsType<App>(Application.Current);
        private readonly bool previousSelfTest = ((App)Application.Current!).IsSelfTestMode;
        private readonly AppTheme previousTheme = ((App)Application.Current!).ThemeService.CurrentTheme;
        private readonly UiDensity previousDensity = ((App)Application.Current!).ThemeService.CurrentDensity;
        private readonly bool? previousMotion = OperatorMotion.AnimationOverride;
        private readonly string? previousDataHome = Environment.GetEnvironmentVariable("ROCHE_DATA_HOME");
        private readonly string dataHome = Path.Combine(Path.GetTempPath(), "roche-reset-scope-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        private readonly HashSet<string> directories = new(StringComparer.Ordinal);
        private Roche_Scoreboard.MainWindow? window;
        private bool storageCaptured, disposed;
        internal Roche_Scoreboard.MainWindow Window => window!;
        internal MatchManager Match => Read<MatchManager>(Window, "_match");
        internal Border Overlay => Named<Border>(Window, "ResetOverlay");
        internal Button Cancel => Named<Button>(Window, "CancelResetBtn");
        internal Button Confirm => Named<Button>(Window, "ConfirmResetBtn");
        internal Button Opener => Assert.Single(Window.GetVisualDescendants().OfType<Button>(),
            button => (string.Equals(button.Content as string, "Reset match", StringComparison.OrdinalIgnoreCase) ||
                button.GetVisualDescendants().OfType<TextBlock>().Any(text => string.Equals(text.Text, "Reset match", StringComparison.OrdinalIgnoreCase))) && button.IsEffectivelyVisible);
        // The painted material now morphs independently of stationary text
        // and hit regions. Measure that actual surface, not its layout host.
        internal Control Shell => Named<Control>(Window, "ResetSurface");
        internal IRemoteCommandTarget Remote => (IRemoteCommandTarget)Window;
        internal int Changes;

        internal static async Task<Fixture> Create()
        {
            // Construct first, then mutate. Every setup assertion/exception is
            // covered by rollback, even before the window can be returned.
            var fixture = new Fixture();
            try
            {
                SelfTestStorage.Initialize(true); // Process latch is intentional through final callbacks.
                Assert.True(SelfTestStorage.IsEnabled);
                if (Directory.Exists(SelfTestStorage.RootPath))
                {
                    foreach (string directory in Directory.EnumerateDirectories(SelfTestStorage.RootPath, "*", SearchOption.AllDirectories).Append(SelfTestStorage.RootPath))
                        fixture.directories.Add(directory);
                    foreach (string path in Directory.EnumerateFiles(SelfTestStorage.RootPath, "*", SearchOption.AllDirectories))
                        fixture.files.Add(path, File.ReadAllBytes(path));
                }
                fixture.storageCaptured = true;
                fixture.app.IsSelfTestMode = true;
                OperatorMotion.AnimationOverride = false;
                Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", fixture.dataHome);
                Directory.CreateDirectory(fixture.dataHome);
                // Latched paths, not an assumed environment redirect. Preserve
                // every existing file above, including backups/temporary files.
                GameStateService.Clear();
                foreach (string suffix in new[] { "", ".bak", ".tmp" })
                {
                    string ux = Path.Combine(SelfTestStorage.DocumentsDirectory, "ux_preferences.json") + suffix;
                    if (File.Exists(ux)) File.Delete(ux);
                }
                foreach (string suffix in new[] { "", ".bak", ".tmp" })
                {
                    string legacy = SelfTestStorage.PathFor("legacy", "game_state.json") + suffix;
                    if (File.Exists(legacy)) File.Delete(legacy);
                    string workflow = SelfTestStorage.PathFor("workflow", "operator_workflow.json") + suffix;
                    if (File.Exists(workflow)) File.Delete(workflow);
                }
                AflKeybindSettings.SaveBindings(AflKeybindSettings.GetDefaultBindings());
                // Optional deterministic replay of an earlier test's remembered
                // bounds. The original files were captured above and are restored
                // by Dispose; the app's actual Loaded restore still runs.
                if (int.TryParse(Environment.GetEnvironmentVariable("ROCHE_TEST_RESET_REMEMBERED_CONTROL_PANEL_WIDTH"), out int rememberedWidth))
                    WindowLayoutStorage.Set(WindowLayoutStorage.ControlPanelKey,
                        new WindowLayoutStorage.WindowLayout(0, 0, rememberedWidth, rememberedWidth == 0 ? 0 : 320));
                fixture.window = new Roche_Scoreboard.MainWindow { Width = 1280, Height = 850 };
                fixture.Window.Show();
                var home = Named<SportSelectionScreen>(fixture.Window, "SportSelectionPanel");
                await Until(() => home.IsEffectivelyVisible && home.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "AflLaunchAction" && b.Bounds.Width > 0), "Actual Home route must mount before setup.");
                var launch = Named<Button>(home, "AflLaunchAction");
                var wizard = Named<SetupWizard>(fixture.Window, "SetupWizardPanel");
                try
                {
                    // A measured Home view may still be covered by startup work.
                    // Match the real pointer entry precondition before Click.
                    await Until(() =>
                    {
                        // Home rebuilds dynamic actions after session refresh.
                        // Its namescope can still refer to the retired button.
                        var current = home.GetVisualDescendants().OfType<Button>()
                            .SingleOrDefault(button => button.Name == "AflLaunchAction");
                        if (current is not { IsLoaded: true, IsEffectivelyVisible: true, IsEffectivelyEnabled: true }
                            || current.Bounds.Width <= 0 || current.Bounds.Height <= 0) return false;
                        var center = current.TranslatePoint(new Point(current.Bounds.Width / 2, current.Bounds.Height / 2), fixture.Window);
                        if (center is null || !Within(current, fixture.Window.InputHitTest(center.Value))) return false;
                        launch = current;
                        return true;
                    }, "The actual Home Start Game target must be attached and hit-testable before setup.");
                    Click(launch);
                    await Until(() => Named<Control>(wizard, "SportStepContainer").IsEffectivelyVisible, "Home must enter the actual sport step.");
                }
                catch (Exception failure)
                {
                    try
                    {
                    try { Capture(fixture, "reset-home-route-failure"); }
                    catch (Exception diagnostic) { failure.Data["Home capture diagnostics"] = diagnostic.ToString(); }
                    if (Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is { Length: > 0 } directory)
                        File.WriteAllText(Path.Combine(directory, "reset-home-route-failure-state.json"), JsonSerializer.Serialize(new
                        {
                            homeLoaded = home.IsLoaded, homeVisible = home.IsEffectivelyVisible,
                            actionEnabled = Named<Button>(home, "AflLaunchAction").IsEffectivelyEnabled,
                            mode = Read<object>(fixture.Window, "_sportMode").ToString(),
                            matchReady = Read<bool>(fixture.Window, "_aflMatchReady"),
                            wizardLoaded = wizard.IsLoaded, wizardVisible = wizard.IsEffectivelyVisible,
                            owned = fixture.Window.OwnedWindows.Select(window => new { window.Title, window.IsVisible }).ToArray()
                        }));
                    }
                    catch (Exception diagnostic) { failure.Data["Home entry diagnostics"] = diagnostic.ToString(); }
                    throw;
                }
                var afl = Named<RadioButton>(wizard, "AflSportChoice"); afl.IsChecked = true; Click(afl);
                Click(Named<Button>(wizard, "NextButton")); await Settle();
                Named<TextBox>(wizard, "SetupHomeNameBox").Text = "Reset home";
                Named<TextBox>(wizard, "SetupHomeAbbrBox").Text = "RSH";
                Named<TextBox>(wizard, "SetupAwayNameBox").Text = "Reset away";
                Named<TextBox>(wizard, "SetupAwayAbbrBox").Text = "RSA";
                Click(Named<Button>(wizard, "NextButton")); await Settle();
                var countdown = Named<RadioButton>(wizard, "SetupCountdown");
                Assert.True(countdown.IsEffectivelyVisible); countdown.IsChecked = true;
                Click(Named<Button>(wizard, "NextButton")); await Settle();
                Named<ComboBox>(wizard, "SetupWeatherLocation").Text = ""; // No weather/network fixture route.
                Click(Named<Button>(wizard, "NextButton"));
                await Until(() => Named<Control>(fixture.Window, "MainContent").IsEffectivelyVisible && !wizard.IsEffectivelyVisible, "Public wizard completion must produce the live AFL console.");
                Click(Named<Button>(fixture.Window, "HomeGoalButton"));
                Click(Named<Button>(fixture.Window, "AwayBehindButton"));
                fixture.Match.SetElapsed(TimeSpan.FromSeconds(71));
                Assert.Equal(6, fixture.Match.HomeTotal); Assert.Equal(1, fixture.Match.AwayTotal);
                Assert.Equal(2, fixture.Match.Events.Count); Assert.False(fixture.Match.ClockRunning);
                // Establish the real scoring page before asking for its reset
                // action; a remembered tab can otherwise keep its template unmounted.
                Key(fixture.Window, PhysicalKey.Digit1, RawInputModifiers.Control);
                await Settle(500);
                Assert.Same(Named<TabItem>(fixture.Window, "MatchControlTab"), Named<TabControl>(fixture.Window, "MainTabControl").SelectedItem);
                fixture.SaveRecovery();
                fixture.Match.MatchChanged += fixture.Changed;
                return fixture;
            }
            catch (Exception setup)
            {
                try { fixture.Dispose(); }
                catch (Exception cleanup) { throw new AggregateException("Reset fixture setup and rollback failed.", setup, cleanup); }
                throw;
            }
        }
        private void Changed() => Changes++;
        internal void SaveRecovery()
        {
            GameStateService.Save(new GameState { Sport = "afl", MatchState = Match.ToState() });
            Assert.True(File.Exists(GameStateService.RecoveryStatePath), "Compare an actual saved recovery file, not two absent paths.");
        }
        internal Dictionary<string, byte[]?> Recovery() => new[] { "", ".bak", ".tmp" }.ToDictionary(
            suffix => suffix, suffix => File.Exists(GameStateService.RecoveryStatePath + suffix)
                ? File.ReadAllBytes(GameStateService.RecoveryStatePath + suffix) : null);
        internal async Task Open(bool settle = true)
        {
            await RevealByKeyboard(Window, Opener);
            await Tap(Window, Opener);
            if (settle) await Settle(320);
            Assert.True(Overlay.IsEffectivelyVisible);
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            var errors = new List<Exception>();
            void Attempt(Action action) { try { action(); } catch (Exception error) { errors.Add(error); } }
            Attempt(() => { if (window is null) return; Match.MatchChanged -= Changed;
                foreach (var owned in Window.OwnedWindows.ToArray()) owned.Close(); Window.Close(); Dispatcher.UIThread.RunJobs(); });
            Attempt(() => app.IsSelfTestMode = previousSelfTest);
            Attempt(() => OperatorMotion.AnimationOverride = previousMotion);
            Attempt(() => app.ThemeService.SetTheme(previousTheme));
            Attempt(() => app.ThemeService.SetDensity(previousDensity));
            if (storageCaptured)
            {
                Attempt(() => { if (Directory.Exists(SelfTestStorage.RootPath))
                    foreach (string path in Directory.EnumerateFiles(SelfTestStorage.RootPath, "*", SearchOption.AllDirectories))
                        if (!files.ContainsKey(path)) File.Delete(path); });
                foreach (var (path, contents) in files) Attempt(() => { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, contents); });
                Attempt(() => { if (Directory.Exists(SelfTestStorage.RootPath))
                    foreach (string directory in Directory.EnumerateDirectories(SelfTestStorage.RootPath, "*", SearchOption.AllDirectories).Append(SelfTestStorage.RootPath).OrderByDescending(path => path.Length))
                        if (!directories.Contains(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); });
            }
            Attempt(() => Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", previousDataHome));
            Attempt(() => { if (Directory.Exists(dataHome)) Directory.Delete(dataHome, true); });
            if (errors.Count > 0) throw new AggregateException("Reset fixture cleanup failed.", errors);
        }
    }

    private static void AssertRecovery(Dictionary<string, byte[]?> expected, Dictionary<string, byte[]?> actual)
    { foreach (var (suffix, contents) in expected) Assert.Equal(contents, actual[suffix]); }
    private static void AssertDeclined(Fixture fixture, string before, Dictionary<string, byte[]?> recovery, Button opener)
    {
        Assert.Equal(before, Snapshot(fixture.Match)); Assert.Equal(0, fixture.Changes);
        AssertRecovery(recovery, fixture.Recovery());
        Assert.False(fixture.Overlay.IsVisible); Assert.False(fixture.Overlay.IsHitTestVisible);
        Assert.Same(opener, fixture.Window.FocusManager?.GetFocusedElement());
        Assert.True(Named<Control>(fixture.Window, "MainContent").IsEffectivelyVisible);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task LiveReductionAfterAStyleRoundTripRetiresCancelVisualsWithoutChangingTheMatch(int styleValue)
    {
        using var f = await Fixture.Create();
        bool oldGlass = OperatorAppearance.GlassEnabled, oldReduced = OperatorAppearance.ReduceMotion;
        var oldStyle = OperatorAppearance.MotionStyle;
        try
        {
            OperatorMotion.AnimationOverride = null;
            OperatorAppearance.Configure(oldGlass, false);
            OperatorAppearance.ConfigureMotionStyle((OperatorMotionStyle)styleValue);
            Assert.True(OperatorMotion.Enabled);
            string before = Snapshot(f.Match);
            var recovery = f.Recovery();
            await f.Open();
            OperatorAppearance.Configure(oldGlass, true);
            Assert.True(f.Overlay.IsEffectivelyVisible);
            Assert.Same(f.Cancel, f.Window.FocusManager?.GetFocusedElement());
            OperatorAppearance.Configure(oldGlass, false);
            Assert.True(OperatorMotion.Enabled);
            Click(f.Cancel);
            Assert.True(f.Overlay.IsVisible);
            Assert.False(f.Overlay.IsHitTestVisible);
            OperatorAppearance.Configure(oldGlass, true);
            Assert.False(f.Overlay.IsVisible);
            foreach (string name in new[] { "ResetBackdrop", "ResetHeader", "ResetDetails", "ResetActions", "ResetWarningMark" })
                Assert.False(Motion.IsAnimating(Named<Control>(f.Window, name)));
            Assert.False(Motion.IsAnimating(Read<ScaleTransform>(f.Window, "_resetSurfaceScale")));
            Assert.False(Motion.IsAnimating(Read<TranslateTransform>(f.Window, "_resetSurfaceTravel")));
            Assert.False(Motion.IsAnimating(Read<RotateTransform>(f.Window, "_resetArrowTurn")));
            Assert.Equal(before, Snapshot(f.Match));
            AssertRecovery(recovery, f.Recovery());
            // The old visual deadline must not close a new reduced-motion scope.
            f.Remote.ResetMatch();
            await Settle(400);
            Assert.True(f.Overlay.IsEffectivelyVisible);
            Assert.Same(f.Cancel, f.Window.FocusManager?.GetFocusedElement());
            Click(f.Cancel);
            Assert.False(f.Overlay.IsVisible);
            Assert.Equal(before, Snapshot(f.Match));
        }
        finally
        {
            OperatorAppearance.ConfigureMotionStyle(oldStyle);
            OperatorAppearance.Configure(oldGlass, oldReduced);
        }
    }

    [AvaloniaFact]
    public async Task CancelReleasesScoringBeforeItsDecorativeExitFinishes()
    {
        using var f = await Fixture.Create();
        OperatorMotion.AnimationOverride = true;
        await f.Open();
        int before = f.Match.HomeTotal, events = f.Match.Events.Count;
        Click(f.Cancel);
        Assert.True(f.Overlay.IsVisible);
        Assert.False(f.Overlay.IsHitTestVisible);
        Key(f.Window, PhysicalKey.A);
        Assert.Equal(before + 6, f.Match.HomeTotal);
        Assert.Equal(events + 1, f.Match.Events.Count);
        await Settle(400);
        Assert.False(f.Overlay.IsVisible);
    }

    [AvaloniaFact]
    public async Task ExplanationHandoffDoesNotFlashFullOpacityBeforeItsStagger()
    {
        using var f = await Fixture.Create();
        OperatorMotion.AnimationOverride = true;
        string before = Snapshot(f.Match);
        f.Remote.ResetMatch(); // Synchronous public route, before a render tick.
        Assert.True(f.Overlay.IsEffectivelyVisible);
        var details = Named<Control>(f.Window, "ResetDetails");
        Assert.True(Motion.IsAnimating(details));
        Assert.InRange(details.Opacity, .149, .151);
        Motion.SampleAt(details, TimeSpan.FromTicks(OperatorMotion.Stagger.Ticks / 2));
        Assert.InRange(details.Opacity, .149, .151);
        Motion.SampleAt(details, OperatorMotion.Stagger + OperatorMotion.SurfaceOpen + TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, details.Opacity, 6);
        Assert.Equal(before, Snapshot(f.Match));
        Click(f.Cancel);
        await Settle(400);
    }

    [AvaloniaFact]
    public async Task RemoteResetCannotOpenUnderTheActiveCommandBar()
    {
        using var f = await Fixture.Create();
        string before = Snapshot(f.Match);
        var recovery = f.Recovery();
        var commands = Named<Control>(f.Window, "CommandOverlay");
        Key(f.Window, PhysicalKey.K, RawInputModifiers.Control);
        await Settle(120);
        Assert.True(commands.IsEffectivelyVisible);
        Assert.False(f.Overlay.IsVisible);

        f.Remote.ResetMatch();
        await Settle(120);

        Assert.False(f.Overlay.IsVisible);
        Assert.True(commands.IsEffectivelyVisible);
        Assert.Equal(before, Snapshot(f.Match));
        AssertRecovery(recovery, f.Recovery());
        Key(f.Window, PhysicalKey.Escape);
        await Settle(250);
        Assert.False(commands.IsVisible);
        f.Remote.ResetMatch();
        await Settle(300);
        Assert.True(f.Overlay.IsEffectivelyVisible);
        Key(f.Window, PhysicalKey.Escape);
        await Settle(300);
        Assert.Equal(before, Snapshot(f.Match));
        AssertRecovery(recovery, f.Recovery());
    }

    [AvaloniaFact]
    public async Task PointerCancelPreservesPausedMatchHistoryRecoveryAndReturnsToTheSource()
    {
        using var f = await Fixture.Create(); var opener = f.Opener;
        string before = Snapshot(f.Match); var recovery = f.Recovery();
        await f.Open(); await Tap(f.Window, f.Cancel); await Settle(320);
        AssertDeclined(f, before, recovery, opener);
    }

    [AvaloniaTheory]
    [InlineData(PhysicalKey.Escape, 1280, 850)]
    [InlineData(PhysicalKey.Enter, 1280, 850)]
    [InlineData(PhysicalKey.Escape, 420, 320)]
    [InlineData(PhysicalKey.Enter, 420, 320)]
    public async Task OpeningOwnsSafeInitialFocusAndEscapeOrEnterDeclines(PhysicalKey key, int width, int height)
    {
        using var f = await Fixture.Create(); var opener = f.Opener;
        string before = Snapshot(f.Match); var recovery = f.Recovery(); await f.Open();
        f.Window.Width = width; f.Window.Height = height; await Settle();
        Assert.Same(f.Cancel, f.Window.FocusManager?.GetFocusedElement());
        Key(f.Window, key); await Settle(400);
        AssertDeclined(f, before, recovery, opener);
    }

    [AvaloniaFact]
    public async Task CompactLongMatchContextCanScrollToConsequencesWithoutMovingActionsOrChangingMatch()
    {
        using var f = await Fixture.Create();
        f.Match.SetTeams("Very long home club name with multiple suburban districts and sponsors", "HOM",
            "Very long away club name with multiple suburban districts and sponsors", "AWY");
        await f.Open();
        f.Window.Width = 420; f.Window.Height = 320; await Settle(400);
        var details = Named<ScrollViewer>(f.Window, "ResetDetails");
        var consequence = Assert.Single(details.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == "This action cannot be undone.");
        string before = Snapshot(f.Match); int changes = f.Changes; var recovery = f.Recovery();
        Rect cancel = PaintedBounds(f.Window, f.Cancel), confirm = PaintedBounds(f.Window, f.Confirm);
        Assert.True(details.Extent.Height > details.Viewport.Height + 1, "Long real context must overflow the body, not the pinned actions.");
        details.ScrollToEnd(); await Settle();
        Assert.True(details.Offset.Y > 0);
        Assert.True(PaintedBounds(f.Window, details).Contains(PaintedBounds(f.Window, consequence)),
            "The final destructive consequence must be fully readable after scrolling.");
        Assert.Equal(cancel, PaintedBounds(f.Window, f.Cancel)); Assert.Equal(confirm, PaintedBounds(f.Window, f.Confirm));
        AssertPhysicalTarget(f.Window, f.Cancel); AssertPhysicalTarget(f.Window, f.Confirm);
        Capture(f, "afl-reset-long-context-420x320-scrolled");
        await Tap(f.Window, f.Cancel);
        Assert.Equal(before, Snapshot(f.Match)); Assert.Equal(changes, f.Changes); AssertRecovery(recovery, f.Recovery());
    }

    [AvaloniaTheory]
    [InlineData(1280, 850)]
    [InlineData(420, 320)]
    public async Task ForwardAndReverseTabRemainInsideTheActiveDecisionAndReachBothActions(int width, int height)
    {
        using var f = await Fixture.Create(); await f.Open();
        f.Window.Width = width; f.Window.Height = height; await Settle(); Assert.True(f.Cancel.Focus(NavigationMethod.Tab));
        var reached = new HashSet<IInputElement>();
        foreach (var modifiers in new[] { RawInputModifiers.None, RawInputModifiers.Shift })
            for (int count = 0; count < 8; count++)
            {
                Key(f.Window, PhysicalKey.Tab, modifiers);
                var focused = f.Window.FocusManager?.GetFocusedElement();
                Assert.True(Within(f.Overlay, focused), "An active reset decision must retain keyboard navigation.");
                reached.Add(focused!);
            }
        Assert.Contains(f.Cancel, reached); Assert.Contains(f.Confirm, reached);
        Key(f.Window, PhysicalKey.Escape); await Settle(400);
    }

    [AvaloniaTheory]
    [InlineData(PhysicalKey.A, RawInputModifiers.None)]
    [InlineData(PhysicalKey.N, RawInputModifiers.None)]
    [InlineData(PhysicalKey.Z, RawInputModifiers.Control)]
    [InlineData(PhysicalKey.Digit4, RawInputModifiers.Control)]
    [InlineData(PhysicalKey.K, RawInputModifiers.Control)]
    [InlineData(PhysicalKey.Digit2, RawInputModifiers.None)]
    public async Task ActiveResetBlocksScoreQuarterUndoTabPresentationAndCommandShortcuts(PhysicalKey key, RawInputModifiers modifiers)
    {
        using var f = await Fixture.Create(); await f.Open(); Assert.True(f.Cancel.Focus(NavigationMethod.Tab));
        string before = Snapshot(f.Match); var recovery = f.Recovery();
        int tab = Named<TabControl>(f.Window, "MainTabControl").SelectedIndex;
        string screen = Read<string>(f.Window, "_activeScreenKey");
        Key(f.Window, key, modifiers); await Settle(150);
        Assert.Equal(before, Snapshot(f.Match)); Assert.Equal(0, f.Changes);
        Assert.Equal(tab, Named<TabControl>(f.Window, "MainTabControl").SelectedIndex);
        Assert.Equal(screen, Read<string>(f.Window, "_activeScreenKey"));
        Assert.False(Named<Control>(f.Window, "CommandOverlay").IsVisible);
        Assert.True(f.Overlay.IsEffectivelyVisible); AssertRecovery(recovery, f.Recovery());
        Assert.True(Within(f.Overlay, f.Window.FocusManager?.GetFocusedElement()));
    }

    [AvaloniaTheory]
    [InlineData(1280, 850)]
    [InlineData(420, 320)]
    public async Task ResizedOpenDecisionFitsPaintedShellAndBothPhysicalActionsRemainReachable(int width, int height)
    {
        using var f = await Fixture.Create(); await f.Open();
        f.Window.Width = width; f.Window.Height = height; await Settle(400);
        Capture(f, $"afl-reset-geometry-{width}x{height}");
        Rect shell = PaintedBounds(f.Window, f.Shell);
        Assert.True(new Rect(f.Window.Bounds.Size).Contains(shell), "The full material shell must fit the supported operator viewport.");
        foreach (var action in new[] { f.Cancel, f.Confirm })
        {
            Assert.True(action.Focus(NavigationMethod.Tab)); action.BringIntoView(); await Settle();
            AssertPhysicalTarget(f.Window, action);
            var label = Assert.Single(action.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == action.Content?.ToString());
            Assert.True(PaintedBounds(f.Window, action).Contains(PaintedBounds(f.Window, label)), "The action label must fit its painted target.");
        }
        await Tap(f.Window, f.Cancel); await Settle(320);
        Assert.False(f.Overlay.IsVisible);
    }

    [AvaloniaFact]
    public async Task RetainedConfirmAfterCancelCannotClearTheCurrentMatchOrRecovery()
    {
        using var f = await Fixture.Create(); await f.Open(); Button retained = f.Confirm;
        string before = Snapshot(f.Match); var recovery = f.Recovery();
        await Tap(f.Window, f.Cancel); await Settle(320);
        Click(retained); await Settle(); // Public retained callback, not a private handler invocation.
        Assert.Equal(before, Snapshot(f.Match)); Assert.Equal(0, f.Changes); AssertRecovery(recovery, f.Recovery());
    }

    [AvaloniaFact]
    public async Task ConfirmCannotClearAReplacementLoadedIntoTheSameReadonlyMatchManager()
    {
        using var f = await Fixture.Create(); await f.Open(); Button retained = f.Confirm;
        var replacement = new MatchManager(); replacement.SetTeams("Replacement home", "NEW", "Replacement away", "OTH");
        replacement.SetElapsed(TimeSpan.FromSeconds(43)); replacement.AddGoal(TeamSide.Away);
        f.Match.LoadState(replacement.ToState()); f.SaveRecovery();
        string before = Snapshot(f.Match); var recovery = f.Recovery(); int changes = f.Changes;
        Click(retained); await Settle();
        Assert.Equal(before, Snapshot(f.Match)); Assert.Equal(changes, f.Changes); AssertRecovery(recovery, f.Recovery());
    }

    [AvaloniaFact]
    public async Task RetainedConfirmAfterOwnerClosesIsInert()
    {
        using var f = await Fixture.Create(); await f.Open(); Button retained = f.Confirm;
        f.Window.Close(); await Settle();
        string before = Snapshot(f.Match); var recovery = f.Recovery(); int changes = f.Changes;
        Click(retained); await Settle();
        Assert.Equal(before, Snapshot(f.Match)); Assert.Equal(changes, f.Changes); AssertRecovery(recovery, f.Recovery());
    }

    [AvaloniaFact]
    public async Task ActualConfirmResetsOnceReturnsHomeAndCannotLaterDeleteAReplacement()
    {
        using var f = await Fixture.Create(); await f.Open(); Button retained = f.Confirm;
        await Tap(f.Window, f.Confirm); await Settle(300);
        Assert.Equal(0, f.Match.HomeTotal); Assert.Equal(0, f.Match.AwayTotal); Assert.Empty(f.Match.Events);
        Assert.False(f.Match.ClockRunning); Assert.Equal(TimeSpan.Zero, f.Match.ElapsedInQuarter);
        Assert.False(File.Exists(GameStateService.RecoveryStatePath));
        Assert.True(Named<SportSelectionScreen>(f.Window, "SportSelectionPanel").IsEffectivelyVisible);
        Assert.False(f.Overlay.IsVisible); Assert.False(f.Overlay.IsHitTestVisible);
        Assert.Equal(1, f.Changes);
        var replacement = new MatchManager(); replacement.AddBehind(TeamSide.Home);
        f.Match.LoadState(replacement.ToState()); f.SaveRecovery();
        string before = Snapshot(f.Match); var recovery = f.Recovery(); int changes = f.Changes;
        Click(retained); await Settle();
        Assert.Equal(before, Snapshot(f.Match)); Assert.Equal(changes, f.Changes); AssertRecovery(recovery, f.Recovery());
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedPublicOpenAndCloseInterruptionLeaveOneCurrentCancelableDecision(bool motion)
    {
        using var f = await Fixture.Create(); OperatorMotion.AnimationOverride = motion;
        await f.Open(); string before = Snapshot(f.Match); var recovery = f.Recovery();
        f.Remote.ResetMatch(); Assert.Single(f.Window.GetVisualDescendants().OfType<Border>(), b => b.Name == "ResetOverlay");
        Click(f.Cancel); // Retire the first decision, then interrupt its visual close through a real public route.
        f.Remote.ResetMatch(); await Settle(450);
        Assert.True(f.Overlay.IsEffectivelyVisible); Assert.True(f.Overlay.IsHitTestVisible);
        Assert.True(Within(f.Overlay, f.Window.FocusManager?.GetFocusedElement()));
        Assert.Equal(before, Snapshot(f.Match)); Assert.Equal(0, f.Changes);
        await Tap(f.Window, f.Cancel); await Settle(400);
        Assert.False(f.Overlay.IsVisible); AssertRecovery(recovery, f.Recovery());
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenAndCancelPreserveTheExistingStoppedOrRunningClockPolicy(bool running)
    {
        using var f = await Fixture.Create();
        if (running) f.Match.StartClock();
        int changes = f.Changes; string history = JsonSerializer.Serialize(f.Match.Events);
        TimeSpan before = f.Match.ElapsedInQuarter; var recovery = f.Recovery();
        await f.Open(); Assert.Equal(running, f.Match.ClockRunning);
        await Tap(f.Window, f.Cancel); await Settle(320);
        Assert.Equal(running, f.Match.ClockRunning); Assert.Equal(changes, f.Changes);
        Assert.Equal(history, JsonSerializer.Serialize(f.Match.Events)); AssertRecovery(recovery, f.Recovery());
        if (running) Assert.True(f.Match.ElapsedInQuarter >= before);
        else Assert.Equal(before, f.Match.ElapsedInQuarter);
    }

    [AvaloniaFact]
    public async Task SpaceOnSafeCancelNeverStartsTheClockOrConfirmsReset()
    {
        using var f = await Fixture.Create(); await f.Open(); Assert.True(f.Cancel.Focus(NavigationMethod.Tab));
        string before = Snapshot(f.Match); var recovery = f.Recovery();
        Key(f.Window, PhysicalKey.Space); await Settle(400);
        Assert.Equal(before, Snapshot(f.Match)); Assert.Equal(0, f.Changes); Assert.False(f.Match.ClockRunning);
        AssertRecovery(recovery, f.Recovery());
        Assert.True(Named<Control>(f.Window, "MainContent").IsEffectivelyVisible);
        // Native Button Space may activate Cancel on key-up; requiring the
        // dialog to remain open would incorrectly outlaw that safe behavior.
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceGestureMovesOnlyLocalShellAndReducedModeOpensAndDeclinesWithoutClocks(bool motion)
    {
        using var f = await Fixture.Create(); OperatorMotion.AnimationOverride = motion;
        await RevealByKeyboard(f.Window, f.Opener);
        Rect source = PaintedBounds(f.Window, f.Opener);
        var console = Named<Control>(f.Window, "MainContent");
        Rect consoleBefore = PaintedBounds(f.Window, console);
        Point point = source.Center;
        AssertPhysicalTarget(f.Window, f.Opener);
        var inputTime = System.Diagnostics.Stopwatch.StartNew();
        // The headless raw MouseUp itself pumped rendering for279ms in the
        // diagnostic (longer than the250ms surface timeline). The physical
        // action/hits are covered by the other public-pointer cases. Here use
        // that same public Click route synchronously to inspect its source pose.
        Click(f.Opener);
        inputTime.Stop();
        // Do not drain the dispatcher before the live geometry sample.
        Assert.True(f.Overlay.IsEffectivelyVisible);
        if (motion)
        {
            var transform = Assert.IsType<TransformGroup>(f.Shell.RenderTransform);
            var scale = Read<ScaleTransform>(f.Window, "_resetSurfaceScale");
            var travel = Read<TranslateTransform>(f.Window, "_resetSurfaceTravel");
            if (Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "source-clock-diagnostic.json"), JsonSerializer.Serialize(new
                {
                    inputMilliseconds = inputTime.Elapsed.TotalMilliseconds, enabled = OperatorMotion.Enabled,
                    overrideValue = OperatorMotion.AnimationOverride, style = OperatorAppearance.MotionStyle.ToString(),
                    scaleFieldClock = Motion.IsAnimating(scale), travelFieldClock = Motion.IsAnimating(travel),
                    sameScale = ReferenceEquals(scale, transform.Children[0]), sameTravel = ReferenceEquals(travel, transform.Children[1]),
                    scale.ScaleX, scale.ScaleY, travel.X, travel.Y,
                    children = transform.Children.Select(item => new { type = item.GetType().FullName, clock = Motion.IsAnimating(item) })
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            Assert.Contains(transform.Children, item => Motion.IsAnimating(item));
        }
        Rect early = PaintedBounds(f.Window, f.Shell);
        Assert.Equal(consoleBefore, PaintedBounds(f.Window, console));
        Capture(f, "afl-reset-source-early-" + motion);
        if (!motion)
        {
            Assert.Equal(1, f.Overlay.Opacity, 6);
            Assert.False(Motion.IsAnimating(f.Overlay)); Assert.False(Motion.IsAnimating(f.Shell));
        }
        await Settle(400);
        Rect settled = PaintedBounds(f.Window, f.Shell);
        Assert.Equal(consoleBefore, PaintedBounds(f.Window, console));
        Capture(f, "afl-reset-source-settled-" + motion);
        if (motion)
        {
            // Observable source continuity, not a check for a newly invented
            // template part or for a particular easing coefficient.
            Assert.True(early.Width < settled.Width - 1 || early.Height < settled.Height - 1);
            static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
            Assert.True(Distance(early.Center, source.Center) < Distance(settled.Center, source.Center));
        }
        else Assert.Equal(early, settled);
        Click(f.Cancel);
        if (!motion)
        {
            Assert.False(f.Overlay.IsVisible); Assert.False(f.Overlay.IsHitTestVisible);
            Assert.False(Motion.IsAnimating(f.Overlay)); Assert.False(Motion.IsAnimating(f.Shell));
        }
        await Settle(400);
        Assert.False(f.Overlay.IsVisible); Assert.Same(f.Opener, f.Window.FocusManager?.GetFocusedElement());
        // Retained action must remain an inert callback after that gesture.
        string before = Snapshot(f.Match); int changes = f.Changes; Click(f.Confirm);
        Assert.Equal(before, Snapshot(f.Match)); Assert.Equal(changes, f.Changes);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
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
public sealed class LiveSettingsSearchKeyboardTests
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
        target.BringIntoView();
        await Settle();
        Assert.True(target.IsEffectivelyVisible, target.Name + " must be visible before pointer activation.");
        Assert.True(target.IsEnabled, target.Name + " must be enabled before pointer activation.");
        var origin = target.TranslatePoint(default, owner);
        Assert.NotNull(origin);
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
        owner.MouseMove(point);
        owner.MouseDown(point, MouseButton.Left);
        owner.MouseUp(point, MouseButton.Left);
        await Settle(220);
    }

    private sealed class AflFixture : IAsyncDisposable
    {
        private readonly App app = Assert.IsType<App>(Application.Current);
        private readonly bool previousSelfTest = ((App)Application.Current!).IsSelfTestMode;
        private readonly AppTheme previousTheme = ((App)Application.Current!).ThemeService.CurrentTheme;
        private readonly UiDensity previousDensity = ((App)Application.Current!).ThemeService.CurrentDensity;
        private readonly bool? previousAnimation = OperatorMotion.AnimationOverride;
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
                foreach (string path in new[]
                {
                    GameStateService.RecoveryStatePath,
                    (string)typeof(GameStateService).GetProperty("LegacyStateFilePath", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!,
                    UxPreferences.GetStoragePath(),
                    (string)typeof(OperatorWorkflowPreferences).GetMethod("GetStoragePath", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [false])!,
                    (string)typeof(AflKeybindSettings).GetMethod("GetStoragePath", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!
                }) fixture.Preserve(path);
                foreach (string path in fixture.files.Keys) if (File.Exists(path)) File.Delete(path);

                fixture.app.IsSelfTestMode = true;
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

    [AvaloniaTheory]
    [InlineData(1280, 850, false)]
    [InlineData(1280, 850, true)]
    [InlineData(420, 320, false)]
    [InlineData(420, 320, true)]
    [Trait("Evidence", "PublicKeyboardRoute")]
    public async Task CtrlFFocusesTheVisibleMountedAflSettingsSearchAndFiltersWithoutScoring(int width, int height, bool fromLiveField)
    {
        await using var fixture = await AflFixture.Create(width, height);
        var window = fixture.Window;
        var workspace = Assert.Single(window.GetVisualDescendants().OfType<SettingsWorkspace>(), item => item.IsEffectivelyVisible);
        var search = Assert.Single(workspace.GetVisualDescendants().OfType<OperatorSearchBox>(), item => item.IsEffectivelyVisible);
        var legacySearch = Named<TextBox>(window, "SettingsSearchBox");
        Assert.False(legacySearch.IsEffectivelyVisible);
        Assert.False(legacySearch.IsFocused);
        Assert.True(search.IsEffectivelyVisible);
        Assert.True(string.IsNullOrEmpty(search.Text));
        var sections = workspace.Sections.OfType<Control>().ToArray();
        var system = Assert.Single(sections, section => section.Name == "SettingsSection_Advanced");
        var teams = Assert.Single(sections, section => section.Name == "SettingsSection_Teams");
        var originalVisibility = sections.ToDictionary(section => section, section => section.IsVisible);
        Assert.True(teams.IsVisible);
        Assert.Contains(system, sections);
        if (fromLiveField)
        {
            var field = Named<TextBox>(window, "HomeNameBox");
            await Tap(window, field);
            if (!field.IsFocused)
            {
                string directory = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") ?? Path.Combine(Path.GetTempPath(), "roche-live-settings-search");
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(directory, "field-focus-failure-" + width + ".png"));
            }
            if (field.IsFocused && Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is { Length: > 0 } fieldDirectory)
            {
                Directory.CreateDirectory(fieldDirectory);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(fieldDirectory, "field-focused-settings-" + width + ".png"));
            }
            var fieldCenter = field.TranslatePoint(new Point(field.Bounds.Width / 2, field.Bounds.Height / 2), window);
            var actualHit = fieldCenter is Point point ? window.InputHitTest(point) : null;
            Assert.True(field.IsFocused, "The public live-field route must own focus before Ctrl+F. Center=" + fieldCenter +
                " Hit=" + (actualHit as Control)?.Name + ":" + actualHit?.GetType().Name + " Bounds=" + field.Bounds);
        }
        else await Tap(window, Named<TabItem>(window, "MatchSetupTab"));
        string before = JsonSerializer.Serialize(fixture.Match.ToState());
        string legacyBefore = legacySearch.Text ?? "";

        Key(window, PhysicalKey.F, RawInputModifiers.Control);
        await Settle();
        Assert.True(search.IsFocused,
            "Ctrl+F must focus the visible mounted SettingsWorkspace search, not the hidden legacy SettingsSearchBox. " +
            "Actual focused controls: " + string.Join(", ", window.GetVisualDescendants().OfType<Control>()
                .Where(control => control.IsFocused).Select(control => control.GetType().Name + ":" + control.Name)));
        Assert.False(legacySearch.IsFocused);
        if (Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is { Length: > 0 } captureDirectory)
        {
            Directory.CreateDirectory(captureDirectory);
            using var frame = window.CaptureRenderedFrame();
            frame?.Save(Path.Combine(captureDirectory, "focused-settings-" + width + "-" + fromLiveField + ".png"));
        }
        window.KeyTextInput("cloud remote");
        await Settle();
        Assert.Equal("cloud remote", search.Text);
        Assert.True(system.IsVisible);
        Assert.False(teams.IsVisible);
        Assert.Equal(legacyBefore, legacySearch.Text ?? "");

        // Exercise actual keydown routing for the default score/clock keys
        // while text input owns focus. These must not act on the live match.
        foreach (var key in new[] { PhysicalKey.A, PhysicalKey.S, PhysicalKey.K, PhysicalKey.L, PhysicalKey.Space }) Key(window, key);
        Assert.Equal(before, JsonSerializer.Serialize(fixture.Match.ToState()));
        Assert.False(fixture.Match.ClockRunning);
        Assert.Equal(2, fixture.Match.Events.Count);

        Key(window, PhysicalKey.F, RawInputModifiers.Control);
        window.KeyTextInput("zzzz-no-setting-40987");
        await Settle();
        Assert.Equal("zzzz-no-setting-40987", search.Text);
        Assert.All(sections, section => Assert.False(section.IsVisible));
        Assert.Contains(workspace.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == "No matching settings");
        Key(window, PhysicalKey.Escape);
        await Settle();
        Assert.True(string.IsNullOrEmpty(search.Text));
        Assert.True(search.IsFocused);
        foreach (var (section, wasVisible) in originalVisibility) Assert.Equal(wasVisible, section.IsVisible);
        Assert.Equal(legacyBefore, legacySearch.Text ?? "");
        Assert.Equal(before, JsonSerializer.Serialize(fixture.Match.ToState()));
    }
}

// Actual control regressions; Linux headless checks do not prove native WPF GUI behavior.
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class AflSettingsInputSafetyTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    private static void Seed(object owner, string name, object value) =>
        owner.GetType().GetField(name, Private)!.SetValue(owner, value);
    private static async Task Settle(int milliseconds = 100)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Enter(Window window)
    {
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly App _app = Assert.IsType<App>(Application.Current);
        private readonly bool _selfTest;
        private readonly bool? _motion = OperatorMotion.AnimationOverride;
        private readonly AppTheme _theme;
        private readonly UiDensity _density;
        private readonly string? _previousDataHome = Environment.GetEnvironmentVariable("ROCHE_DATA_HOME");
        private readonly string _dataHome = Path.Combine(Path.GetTempPath(), "roche-settings-input-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, byte[]?> _files = new();
        private readonly HashSet<string> _oldDirectories;
        private Roche_Scoreboard.MainWindow? _window;
        private bool _disposed;
        internal Roche_Scoreboard.MainWindow Window => _window ?? throw new InvalidOperationException("Fixture window unavailable.");
        internal MatchManager Match => Field<MatchManager>(Window, "_match");
        internal T Named<T>(string name) where T : Control => Assert.IsAssignableFrom<T>(Window.FindControl<T>(name));

        internal Fixture()
        {
            SelfTestStorage.Initialize(true);
            _selfTest = _app.IsSelfTestMode;
            _theme = _app.ThemeService.CurrentTheme;
            _density = _app.ThemeService.CurrentDensity;
            // Storage getters can create directories even before Window.Show.
            _oldDirectories = Directory.Exists(SelfTestStorage.RootPath)
                ? Directory.EnumerateDirectories(SelfTestStorage.RootPath, "*", SearchOption.AllDirectories)
                    .Append(SelfTestStorage.RootPath).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            try
            {
                _app.IsSelfTestMode = true;
                OperatorMotion.AnimationOverride = false;
                Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", _dataHome);
                Directory.CreateDirectory(_dataHome);
                string workflow = (string)typeof(OperatorWorkflowPreferences)
                    .GetMethod("GetStoragePath", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [false])!;
                string display = (string)typeof(DisplaySettings)
                    .GetMethod("GetStoragePath", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
                foreach (string path in new[]
                {
                    Path.Combine(SelfTestStorage.DocumentsDirectory, "window_layout.json"),
                    Path.Combine(SelfTestStorage.DocumentsDirectory, "ux_preferences.json"),
                    OperatorAppearance.StoragePath, workflow, display, GameStateService.RecoveryStatePath,
                    SelfTestStorage.PathFor("legacy", "game_state.json")
                })
                    foreach (string suffix in new[] { "", ".bak", ".tmp" })
                        _files.TryAdd(path + suffix, File.Exists(path + suffix) ? File.ReadAllBytes(path + suffix) : null);
                // Diagnostic input only: seed the actual latched backend AFTER
                // byte backups, so existing cleanup restores the whole profile.
                string? rememberedWidth = Environment.GetEnvironmentVariable("ROCHE_TEST_REMEMBERED_CONTROL_PANEL_WIDTH");
                if (!string.IsNullOrEmpty(rememberedWidth))
                {
                    if (rememberedWidth is not ("420" or "0"))
                        throw new ArgumentException("ROCHE_TEST_REMEMBERED_CONTROL_PANEL_WIDTH accepts only 420 or 0.");
                    double width = rememberedWidth == "420" ? 420d : 0d;
                    double height = rememberedWidth == "420" ? 320d : 0d;
                    WindowLayoutStorage.Set(WindowLayoutStorage.ControlPanelKey,
                        new WindowLayoutStorage.WindowLayout(0, 0, width, height));
                    var persisted = WindowLayoutStorage.Get(WindowLayoutStorage.ControlPanelKey);
                    Assert.NotNull(persisted);
                    Assert.Equal(width, persisted.Width);
                    Assert.Equal(height, persisted.Height);
                }
                _window = new Roche_Scoreboard.MainWindow();
                _window.Width = 1280;
                _window.Height = 850;
                _window.Show();
            }
            catch (Exception setup)
            {
                var failures = Cleanup();
                if (failures.Count > 0)
                    throw new AggregateException("Settings fixture setup and rollback failed.", new[] { setup }.Concat(failures));
                throw;
            }
        }

        internal async Task Ready(ClockMode mode = ClockMode.CountUp)
        {
            async Task AwaitContext(Func<bool> ready, string explanation)
            {
                for (int attempt = 0; attempt < 50 && !ready(); attempt++) await Settle(100);
                Assert.True(ready(), explanation);
            }
            await AwaitContext(() => Window.IsLoaded, "The actual window must finish loading before the fixture chooses its viewport.");
            await Settle(); // Loaded restores the remembered backend geometry first.
            Window.Width = 1280;
            Window.Height = 850;
            Named<SportSelectionScreen>("SportSelectionPanel").IsVisible = false;
            Named<Control>("MainContent").IsVisible = true;
            Seed(Window, "_aflMatchReady", true);
            Seed(Window, "_sportMode", SportMode.AFL);
            if (Field<UxPreferencesData>(Window, "_uxPreferences").GuidedOperatorMode)
                Click(Named<Button>("GuidedModeButton"));
            Named<TabControl>("MainTabControl").SelectedItem = Named<TabItem>("MatchSetupTab");
            // Fixture state only; tested mutations below always use real controls/input.
            Named<RadioButton>(mode == ClockMode.Countdown ? "ClockModeCountdown" : "ClockModeCountUp").IsChecked = true;
            Match.SetClockMode(mode);
            Match.SetQuarterDuration(TimeSpan.FromMinutes(20));
            Match.SetTeams("Input safety home", "HOM", "Input safety away", "AWY");
            Match.SetElapsed(TimeSpan.FromSeconds(71));
            Match.AddGoal(TeamSide.Home);
            Match.AddBehind(TeamSide.Away);
            await Settle(350);
            Assert.False(Match.ClockRunning);
            Assert.Equal(mode, Match.ClockMode);
            var tabs = Named<TabControl>("MainTabControl");
            var expectedTab = Named<TabItem>("MatchSetupTab");
            var workspace = Field<SettingsWorkspace>(Window, "_aflSettingsWorkspace");
            await AwaitContext(() => ReferenceEquals(tabs.SelectedItem, expectedTab) && expectedTab.IsSelected
                && tabs.SelectedContent is Control { IsEffectivelyVisible: true } content
                && ReferenceEquals(TopLevel.GetTopLevel(content), Window)
                && workspace.IsEffectivelyVisible && ReferenceEquals(TopLevel.GetTopLevel(workspace), Window)
                && workspace.TranslatePoint(default, Window) is not null && workspace.Bounds.Width >= 820
                && Window.ClientSize.Width > 0 && Window.ClientSize.Height > 0,
                "These normal-viewport regressions require the attached selected settings content and a workspace at least 820px wide after restore and resize.");
        }

        internal async Task OpenCategory(string category)
        {
            // This is the actual visible settings navigation button, not a private navigation method.
            var workspace = Field<SettingsWorkspace>(Window, "_aflSettingsWorkspace");
            var buttons = workspace.GetVisualDescendants().OfType<Button>().ToArray();
            var matching = buttons.Where(button => Equals(button.Tag, category) && button.IsEffectivelyVisible).ToArray();
            if (matching.Length == 0) DiagnoseMissingCategory(workspace, category, buttons);
            // Snapshot the original visibility predicate BEFORE optional capture;
            // rendering diagnostics cannot make a failed precondition pass.
            var action = Assert.Single(matching);
            Click(action);
            await Settle();
            if (category == "MATCH")
            {
                // The actual correction controls must be rooted and laid out;
                // a retained child of a collapsed Expander is not an input baseline.
                foreach (var expander in Named<TextBox>("ClockSetSecondsBox").GetLogicalAncestors().OfType<Expander>())
                    expander.IsExpanded = true;
                await Settle(200);
            }
        }

        private void DiagnoseMissingCategory(SettingsWorkspace workspace, string category, Button[] buttons)
        {
            try
            {
                static double? Number(double value) => double.IsFinite(value) ? value : null;
                static object Box(Rect bounds) => new
                { x = Number(bounds.X), y = Number(bounds.Y), width = Number(bounds.Width), height = Number(bounds.Height) };
                object Brief(Control control)
                {
                    var point = control.TranslatePoint(default, Window);
                    return new
                    {
                        type = control.GetType().Name, control.Name,
                        tag = control.Tag as string ?? control.Tag?.GetType().Name,
                        control.IsVisible, control.IsEffectivelyVisible, control.IsEnabled, control.IsEffectivelyEnabled,
                        bounds = Box(control.Bounds),
                        attachedToOwner = ReferenceEquals(TopLevel.GetTopLevel(control), Window),
                        translatedOrigin = point is { } origin ? new { x = Number(origin.X), y = Number(origin.Y) } : null
                    };
                }
                object WithAncestors(Control control) => new
                {
                    control = Brief(control),
                    ancestors = control.GetVisualAncestors().OfType<Control>().Select(Brief).ToArray()
                };
                var tabs = Window.FindControl<TabControl>("MainTabControl");
                var namedSettingsTab = Window.FindControl<TabItem>("MatchSetupTab");
                var selected = tabs?.SelectedItem as Control;
                var selectedContent = tabs?.SelectedContent as Control;
                var namedSettingsContent = namedSettingsTab?.Content as Control;
                var attachedSettingsTabs = Window.GetVisualDescendants().OfType<TabItem>()
                    .Where(item => item.Name == "MatchSetupTab").ToArray();
                string diagnostic = JsonSerializer.Serialize(new
                {
                    requestedCategory = category,
                    rememberedWidthInput = Environment.GetEnvironmentVariable("ROCHE_TEST_REMEMBERED_CONTROL_PANEL_WIDTH"),
                    storedControlPanelLayout = WindowLayoutStorage.Get(WindowLayoutStorage.ControlPanelKey),
                    window = new
                    {
                        width = Number(Window.Width), height = Number(Window.Height), bounds = Box(Window.Bounds),
                        clientSize = new { width = Number(Window.ClientSize.Width), height = Number(Window.ClientSize.Height) },
                        Window.IsVisible, Window.IsEnabled, Window.IsActive
                    },
                    workspace = WithAncestors(workspace),
                    selectedTabIndex = tabs?.SelectedIndex,
                    selectedTab = selected is null ? null : WithAncestors(selected),
                    selectedContent = selectedContent is null ? null : WithAncestors(selectedContent),
                    namedSettingsContent = namedSettingsContent is null ? null : WithAncestors(namedSettingsContent),
                    selectedContentIsNamedSettingsContent = ReferenceEquals(selectedContent, namedSettingsContent),
                    selectedIsNamedSettingsTab = ReferenceEquals(selected, namedSettingsTab),
                    namedSettingsTab = namedSettingsTab is null ? null : new
                    { namedSettingsTab.IsSelected, details = WithAncestors(namedSettingsTab) },
                    attachedSettingsTabs = attachedSettingsTabs.Select(WithAncestors).ToArray(),
                    requestedTagCount = buttons.Count(button => Equals(button.Tag, category)),
                    requestedVisibleCount = buttons.Count(button => Equals(button.Tag, category) && button.IsEffectivelyVisible),
                    tags = buttons.Select(button => button.Tag as string).Distinct().ToArray(),
                    buttons = buttons.Select(WithAncestors).ToArray()
                }, new JsonSerializerOptions { WriteIndented = true });
                Console.Error.WriteLine("AflSettingsInputSafety missing-category diagnostic:\n" + diagnostic);
                string? directory = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    string stem = "afl-settings-missing-" + category.ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N");
                    File.WriteAllText(Path.Combine(directory, stem + ".json"), diagnostic);
                    using var frame = Window.CaptureRenderedFrame();
                    frame?.Save(Path.Combine(directory, stem + ".png"));
                }
            }
            catch (Exception error)
            {
                // Keep the original missing-category assertion as the failure;
                // inability to capture diagnostics must not replace its cause.
                Console.Error.WriteLine("AflSettingsInputSafety diagnostic capture failed: " + error);
            }
        }

        internal Button ClockSetAction()
        {
            var seconds = Named<TextBox>("ClockSetSecondsBox");
            foreach (var panel in seconds.GetVisualAncestors().OfType<Panel>())
            {
                var set = panel.Children.OfType<Button>().Where(button =>
                    string.Equals(button.Content as string, "SET", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (set.Length == 1)
                {
                    Assert.Same(Window, TopLevel.GetTopLevel(set[0]));
                    Assert.True(set[0].IsEffectivelyVisible, "The actual clock SET action must be visible in the selected settings category.");
                    Assert.True(set[0].IsEffectivelyEnabled);
                    Assert.True(set[0].Bounds.Width > 0 && set[0].Bounds.Height > 0,
                        "The actual clock SET action must have a usable layout area.");
                    return set[0];
                }
            }
            throw new InvalidOperationException("The actual SET action must be beside its clock fields.");
        }

        internal async Task Decrement(TextBox field)
        {
            // The size stepper lives inside an initially collapsed expander.
            // Open that real container before claiming keyboard reachability.
            foreach (var expander in field.GetLogicalAncestors().OfType<Expander>()) expander.IsExpanded = true;
            await Settle(200);
            OperatorActionBorder? decrement = null;
            var ancestors = field.GetVisualAncestors().ToArray();
            foreach (var panel in ancestors.OfType<Panel>())
            {
                int fieldIndex = panel.Children.ToList().FindIndex(child => ReferenceEquals(child, field) || ancestors.Contains(child));
                if (fieldIndex < 0) continue;
                var previous = panel.Children.Take(fieldIndex).OfType<OperatorActionBorder>().LastOrDefault();
                if (previous is not null && previous.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "−"))
                {
                    decrement = previous;
                    break;
                }
            }
            Assert.NotNull(decrement);
            Assert.True(decrement.IsEffectivelyVisible);
            decrement.BringIntoView();
            await Settle();
            Assert.True(decrement.Focus(NavigationMethod.Tab));
            Assert.True(decrement.IsFocused);
            // Native keyboard route -> OperatorActionBorder -> its existing source event wrapper.
            // No private handler reflection or internal Activate call.
            Enter(Window);
            await Settle();
        }

        private List<Exception> Cleanup()
        {
            var failures = new List<Exception>();
            if (_disposed) return failures;
            _disposed = true;
            void Attempt(Action action) { try { action(); } catch (Exception error) { failures.Add(error); } }
            if (_window is { } owner)
            {
                Attempt(() =>
                {
                    foreach (var owned in owner.OwnedWindows.ToArray()) Attempt(owned.Close);
                });
                Attempt(owner.Close);
                Attempt(() => Dispatcher.UIThread.RunJobs());
                _window = null;
            }
            // Restore every captured value even when an earlier close/resource/file
            // operation failed; SelfTestStorage's intentional process latch stays set.
            Attempt(() => _app.ThemeService.SetTheme(_theme));
            Attempt(() => _app.ThemeService.SetDensity(_density));
            foreach (var (path, bytes) in _files)
                Attempt(() =>
                {
                    if (bytes is null) { if (File.Exists(path)) File.Delete(path); }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.WriteAllBytes(path, bytes);
                    }
                });
            Attempt(() => _app.IsSelfTestMode = _selfTest);
            Attempt(() => OperatorMotion.AnimationOverride = _motion);
            Attempt(() => Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", _previousDataHome));
            Attempt(() => { if (Directory.Exists(_dataHome)) Directory.Delete(_dataHome, true); });
            Attempt(() =>
            {
                if (!Directory.Exists(SelfTestStorage.RootPath)) return;
                foreach (string directory in Directory.EnumerateDirectories(SelfTestStorage.RootPath, "*", SearchOption.AllDirectories)
                    .Append(SelfTestStorage.RootPath).OrderByDescending(path => path.Length))
                    if (!_oldDirectories.Contains(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                        Directory.Delete(directory);
            });
            return failures;
        }

        public void Dispose()
        {
            var failures = Cleanup();
            if (failures.Count > 0) throw new AggregateException("Settings fixture cleanup failed.", failures);
        }
    }

    [AvaloniaTheory]
    [InlineData(ClockMode.CountUp, "", "12")]
    [InlineData(ClockMode.Countdown, "", "12")]
    [InlineData(ClockMode.CountUp, "bad", "12")]
    [InlineData(ClockMode.Countdown, "bad", "12")]
    [InlineData(ClockMode.CountUp, "02", "")]
    [InlineData(ClockMode.Countdown, "02", "")]
    [InlineData(ClockMode.CountUp, "02", "xx")]
    [InlineData(ClockMode.Countdown, "02", "xx")]
    [InlineData(ClockMode.CountUp, "-1", "12")]
    [InlineData(ClockMode.CountUp, "1000", "12")]
    [InlineData(ClockMode.CountUp, "02", "60")]
    [InlineData(ClockMode.CountUp, "02", "-1")]
    [InlineData(ClockMode.Countdown, "-1", "12")]
    [InlineData(ClockMode.Countdown, "1000", "12")]
    [InlineData(ClockMode.Countdown, "02", "60")]
    [InlineData(ClockMode.Countdown, "02", "-1")]
    public async Task InvalidExactClockDraftDoesNotMutateStateElapsedHistoryOrEmitMatchChanges(
        ClockMode mode, string minutes, string seconds)
    {
        using var fixture = new Fixture();
        await fixture.Ready(mode);
        await fixture.OpenCategory("MATCH");
        var minutesBox = fixture.Named<TextBox>("ClockSetMinutesBox");
        var secondsBox = fixture.Named<TextBox>("ClockSetSecondsBox");
        minutesBox.Text = minutes;
        secondsBox.Text = seconds;
        string before = JsonSerializer.Serialize(fixture.Match.ToState());
        string history = JsonSerializer.Serialize(fixture.Match.Events);
        var elapsed = fixture.Match.ElapsedInQuarter;
        var displayed = fixture.Match.DisplayClock;
        int changes = 0;
        fixture.Match.MatchChanged += () => changes++;

        Click(fixture.ClockSetAction());

        Assert.Equal(before, JsonSerializer.Serialize(fixture.Match.ToState()));
        Assert.Equal(history, JsonSerializer.Serialize(fixture.Match.Events));
        Assert.Equal(elapsed, fixture.Match.ElapsedInQuarter);
        Assert.Equal(displayed, fixture.Match.DisplayClock);
        Assert.Equal(0, changes);
        Assert.Equal(minutes, minutesBox.Text);
        Assert.Equal(seconds, secondsBox.Text);
        var error = fixture.Named<TextBlock>("ClockSetError");
        Assert.True(error.IsEffectivelyVisible);
        Assert.Contains("clock has not changed", error.Text, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaTheory]
    [InlineData(ClockMode.CountUp)]
    [InlineData(ClockMode.Countdown)]
    public async Task ValidExactClockDraftPreservesExistingCountUpAndCountdownMapping(ClockMode mode)
    {
        using var fixture = new Fixture();
        await fixture.Ready(mode);
        await fixture.OpenCategory("MATCH");
        fixture.Named<TextBox>("ClockSetMinutesBox").Text = "02";
        fixture.Named<TextBox>("ClockSetSecondsBox").Text = "30";
        string history = JsonSerializer.Serialize(fixture.Match.Events);
        int changes = 0;
        fixture.Match.MatchChanged += () => changes++;

        Click(fixture.ClockSetAction());

        Assert.Equal(TimeSpan.FromSeconds(150), fixture.Match.DisplayClock);
        Assert.Equal(mode == ClockMode.Countdown ? TimeSpan.FromMinutes(20) - TimeSpan.FromSeconds(150) : TimeSpan.FromSeconds(150),
            fixture.Match.ElapsedInQuarter);
        Assert.Equal(1, changes);
        Assert.Equal(history, JsonSerializer.Serialize(fixture.Match.Events));
        Assert.False(fixture.Match.ClockRunning);
        Assert.False(fixture.Named<TextBlock>("ClockSetError").IsVisible);
    }

    [AvaloniaTheory]
    [InlineData("WindowWidthBox")]
    [InlineData("WindowHeightBox")]
    public async Task PixelDecrementAtTheSupportedFiftyPixelBoundaryCannotIncreaseTheDraft(string fieldName)
    {
        using var fixture = new Fixture();
        await fixture.Ready();
        await fixture.OpenCategory("SYSTEM");
        var field = fixture.Named<TextBox>(fieldName);
        field.Text = "50";
        string before = JsonSerializer.Serialize(fixture.Match.ToState());

        await fixture.Decrement(field);

        Assert.Equal("50", field.Text);
        Assert.Equal(before, JsonSerializer.Serialize(fixture.Match.ToState()));
        // These controls adjust only the draft. No output/apply action is invoked.
    }

    [AvaloniaFact]
    public async Task MinuteDecrementCannotTurnTheSupportedThirtySecondDurationIntoNinetySeconds()
    {
        using var fixture = new Fixture();
        await fixture.Ready(ClockMode.Countdown);
        await fixture.OpenCategory("MATCH");
        var minutes = fixture.Named<TextBox>("QuarterMinutesBox");
        var seconds = fixture.Named<TextBox>("QuarterSecondsBox");
        minutes.Text = "0";
        seconds.Text = "30";
        seconds.BringIntoView();
        Assert.True(seconds.Focus(NavigationMethod.Tab));
        Enter(fixture.Window); // Existing real Enter-to-apply path establishes support for0:30.
        Assert.Equal(TimeSpan.FromSeconds(30), fixture.Match.QuarterDuration);
        string history = JsonSerializer.Serialize(fixture.Match.Events);
        var elapsed = fixture.Match.ElapsedInQuarter;

        await fixture.Decrement(minutes);

        Assert.Equal("0", minutes.Text);
        Assert.Equal("30", seconds.Text);
        Assert.Equal(TimeSpan.FromSeconds(30), fixture.Match.QuarterDuration);
        Assert.Equal(elapsed, fixture.Match.ElapsedInQuarter);
        Assert.Equal(history, JsonSerializer.Serialize(fixture.Match.Events));
    }
}

// Integrated public-path regressions; Linux translated runtime is not native Windows desktop acceptance.
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

using static AflValidationDraftSupport;

internal static class AflValidationDraftSupport
{

    internal static T Named<T>(Control root, string name) where T : Control =>
        Assert.IsAssignableFrom<T>(root.GetVisualDescendants().OfType<T>().SingleOrDefault(item => item.Name == name) ?? root.FindControl<T>(name));
    // Reflection reads the live readonly model and existing state; it never
    // invokes handlers or changes private eligibility/focus/visibility fields.
    internal static T Read<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    internal static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    internal static string Snapshot(MatchManager match) => JsonSerializer.Serialize(match.ToState());
    internal static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame); frame.Save(Path.Combine(directory, name + ".png"));
    }
    internal static async Task Settle(int milliseconds = 100)
    { await Task.Delay(milliseconds); Dispatcher.UIThread.RunJobs(); }
    internal static async Task Until(Func<bool> condition, string reason)
    {
        for (int attempt = 0; attempt < 40 && !condition(); attempt++) await Settle(100);
        Assert.True(condition(), reason);
    }
    internal static void Key(Window owner, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    { owner.KeyPressQwerty(key, modifiers); owner.KeyReleaseQwerty(key, modifiers); Dispatcher.UIThread.RunJobs(); }
    internal static bool Within(Control root, IInputElement? target) =>
        ReferenceEquals(root, target) || target is Visual visual && visual.GetVisualAncestors().Contains(root);

    internal static Rect PaintedBounds(Window owner, Control target)
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
    internal static void AssertPhysicalTarget(Window owner, Control target)
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
    internal static async Task Tap(Window owner, Control target)
    {
        AssertPhysicalTarget(owner, target);
        Point point = PaintedBounds(owner, target).Center;
        owner.MouseMove(point); owner.MouseDown(point, MouseButton.Left); owner.MouseUp(point, MouseButton.Left);
        await Settle();
    }
    internal static async Task RevealByKeyboard(Window owner, Control target)
    {
        // OperatorViewport intentionally refuses ordinary programmatic focus
        // scrolling. Hold actual Tab while requesting its public focus route.
        owner.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        try { Assert.True(target.Focus(NavigationMethod.Tab)); target.BringIntoView(); Dispatcher.UIThread.RunJobs(); }
        finally { owner.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None); }
        await Settle();
    }
    internal sealed class Fixture : IDisposable
    {
        private readonly App app = Assert.IsType<App>(Application.Current);
        private readonly bool previousSelfTest = ((App)Application.Current!).IsSelfTestMode;
        private readonly AppTheme previousTheme = ((App)Application.Current!).ThemeService.CurrentTheme;
        private readonly UiDensity previousDensity = ((App)Application.Current!).ThemeService.CurrentDensity;
        private readonly bool? previousMotion = OperatorMotion.AnimationOverride;
        private readonly string? previousDataHome = Environment.GetEnvironmentVariable("ROCHE_DATA_HOME");
        private readonly string dataHome = Path.Combine(Path.GetTempPath(), "roche-afl-validation-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        private readonly HashSet<string> directories = new(StringComparer.Ordinal);
        private Roche_Scoreboard.MainWindow? window;
        private bool storageCaptured, disposed;
        internal Roche_Scoreboard.MainWindow Window => window!;
        internal MatchManager Match => Read<MatchManager>(Window, "_match");
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
                fixture.window = new Roche_Scoreboard.MainWindow { Width = 1280, Height = 850 };
                fixture.Window.Show();
                var home = Named<SportSelectionScreen>(fixture.Window, "SportSelectionPanel");
                await Until(() => home.IsEffectivelyVisible && home.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "AflLaunchAction" && b.Bounds.Width > 0), "Actual Home route must mount before setup.");
                Click(Named<Button>(home, "AflLaunchAction"));
                var wizard = Named<SetupWizard>(fixture.Window, "SetupWizardPanel");
                await Until(() => Named<Control>(wizard, "SportStepContainer").IsEffectivelyVisible, "Home must enter the actual sport step.");
                var afl = Named<RadioButton>(wizard, "AflSportChoice"); afl.IsChecked = true; Click(afl);
                Click(Named<Button>(wizard, "NextButton")); await Settle();
                Named<TextBox>(wizard, "SetupHomeNameBox").Text = "Validation home";
                Named<TextBox>(wizard, "SetupHomeAbbrBox").Text = "RSH";
                Named<TextBox>(wizard, "SetupAwayNameBox").Text = "Validation away";
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

}

public sealed class AflAddScoreOwnershipDraftTests
{
    private static async Task OpenActualAddDialog(Fixture f, Action<ScoreEventEditorWindow> whileOpen)
    {
        var source = Assert.Single(f.Window.GetVisualDescendants().OfType<Button>(),
            button => button.IsEffectivelyVisible && button.Content?.ToString()?.Contains("ADD SCORE", StringComparison.Ordinal) == true);
        await RevealByKeyboard(f.Window, source);
        AssertPhysicalTarget(f.Window, source);
        bool observed = false;
        Exception? responseError = null;
        // Mouse injection can pump work before Button.Click opens its modal.
        // Poll actual ownership instead of assuming one queued callback sees it.
        var waiting = System.Diagnostics.Stopwatch.StartNew();
        var responder = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        responder.Tick += (_, _) =>
        {
            try
            {
                if (waiting.Elapsed > TimeSpan.FromSeconds(15))
                    throw new TimeoutException("The actual Add Score modal did not complete in the bounded fixture.");
                var editors = f.Window.OwnedWindows.OfType<ScoreEventEditorWindow>().ToArray();
                if (!observed && editors.Length != 0)
                {
                    var editor = Assert.Single(editors);
                    observed = true;
                    Assert.Same(f.Window, editor.Owner);
                    Assert.Equal("Add Score Event", editor.Title);
                    Assert.False(Named<Button>(editor, "DeleteButton").IsVisible);
                    whileOpen(editor);
                }
                if (!observed) return;
                foreach (var notice in f.Window.OwnedWindows.Where(window => window is not ScoreEventEditorWindow).ToArray())
                {
                    Assert.Same(f.Window, notice.Owner);
                    var dismiss = Assert.Single(notice.GetVisualDescendants().OfType<Button>(),
                        button => Equals(button.Content, "OK") || Equals(button.Content, "Close"));
                    Click(dismiss);
                }
            }
            catch (Exception error)
            {
                responseError = error;
                responder.Stop();
                foreach (var child in f.Window.OwnedWindows.ToArray()) child.Close();
            }
        };
        responder.Start();
        try
        {
            Point point = PaintedBounds(f.Window, source).Center;
            f.Window.MouseMove(point);
            f.Window.MouseDown(point, MouseButton.Left); f.Window.MouseUp(point, MouseButton.Left);
        }
        finally { responder.Stop(); }
        await Settle();
        Assert.True(observed, "The actual public Add Score button must open its actual owned editor.");
        Assert.Null(responseError);
        Assert.Empty(f.Window.OwnedWindows);
    }

    private static void SetCustomDraft(ScoreEventEditorWindow editor)
    {
        var away = Named<RadioButton>(editor, "AwayTeamBtn"); away.IsChecked = true; Click(away);
        var goal = Named<RadioButton>(editor, "GoalBtn"); goal.IsChecked = true; Click(goal);
        Named<CheckBox>(editor, "UseCurrentQuarterBox").IsChecked = false;
        Named<CheckBox>(editor, "UseCurrentTimeBox").IsChecked = false;
        Named<ComboBox>(editor, "QuarterBox").SelectedIndex = 0;
        Named<TextBox>(editor, "MinutesBox").Text = "00";
        Named<TextBox>(editor, "SecondsBox").Text = "43";
    }
    private static void AssertRecovery(Dictionary<string, byte[]?> expected, Dictionary<string, byte[]?> actual)
    { foreach (var (suffix, contents) in expected) Assert.Equal(contents, actual[suffix]); }

    [AvaloniaFact]
    public async Task ActualOwnedAddCommitsChosenQuarterTimeOnceAndPreservesPriorEvents()
    {
        using var f = await Fixture.Create();
        var original = f.Match.Events.ToArray();
        string history = JsonSerializer.Serialize(original);
        await OpenActualAddDialog(f, editor =>
        {
            SetCustomDraft(editor);
            Assert.Equal(history, JsonSerializer.Serialize(f.Match.Events));
            Assert.Equal(0, f.Changes);
            var save = Named<Button>(editor, "SaveButton"); Click(save); Click(save);
            Assert.Equal(ScoreEventEditorWindow.EditorResult.Saved, editor.Result);
        });
        Assert.Equal(1, f.Changes);
        Assert.Equal(3, f.Match.Events.Count);
        Assert.Equal(history, JsonSerializer.Serialize(f.Match.Events.Where(score => original.Contains(score))));
        var added = Assert.Single(f.Match.Events, score => !original.Contains(score));
        Assert.Equal(TeamSide.Away, added.Team); Assert.Equal(ScoreType.Goal, added.Type);
        Assert.Equal(1, added.Quarter); Assert.Equal(TimeSpan.FromSeconds(43), added.GameTime);
        Assert.Equal(6, f.Match.HomeTotal); Assert.Equal(7, f.Match.AwayTotal);
        Assert.False(f.Match.ClockRunning);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedAddCannotAppendOldDraftToReplacementLoadedIntoSameReadonlyManager(bool laterQuarter)
    {
        using var f = await Fixture.Create();
        var sameManager = f.Match;
        string? replacementSnapshot = null;
        Dictionary<string, byte[]?>? replacementRecovery = null;
        int changesAfterReplacement = -1;
        await OpenActualAddDialog(f, editor =>
        {
            SetCustomDraft(editor);
            Assert.Contains("Validation home", Named<TextBlock>(editor, "HomeTeamLabel").Text);
            var replacement = new MatchManager();
            replacement.SetTeams("Replacement home", "NEW", "Replacement away", "OTH");
            replacement.AddBehind(TeamSide.Home);
            if (laterQuarter) Assert.True(replacement.EndQuarter());
            replacement.SetElapsed(TimeSpan.FromSeconds(29));
            f.Match.LoadState(replacement.ToState());
            Assert.Same(sameManager, f.Match); // Identity-only receipt would miss this change.
            Assert.Equal(laterQuarter ? 2 : 1, f.Match.Quarter);
            f.SaveRecovery();
            replacementSnapshot = Snapshot(f.Match); replacementRecovery = f.Recovery();
            changesAfterReplacement = f.Changes;
            Click(Named<Button>(editor, "SaveButton"));
            Assert.Equal(ScoreEventEditorWindow.EditorResult.Saved, editor.Result);
        });
        Assert.NotNull(replacementSnapshot); Assert.NotNull(replacementRecovery);
        Assert.Equal(replacementSnapshot, Snapshot(f.Match));
        Assert.Equal(changesAfterReplacement, f.Changes);
        AssertRecovery(replacementRecovery, f.Recovery());
    }

    [AvaloniaFact]
    public async Task CancelFromTheActualOwnedAddDialogPreservesReplacementAndRecovery()
    {
        using var f = await Fixture.Create(); string? beforeCancel = null;
        Dictionary<string, byte[]?>? recovery = null; int changes = -1;
        await OpenActualAddDialog(f, editor =>
        {
            var replacement = new MatchManager(); replacement.AddGoal(TeamSide.Away);
            f.Match.LoadState(replacement.ToState()); f.SaveRecovery();
            beforeCancel = Snapshot(f.Match); recovery = f.Recovery(); changes = f.Changes;
            var cancel = Assert.Single(editor.GetVisualDescendants().OfType<Button>(), button => button.IsCancel);
            Click(cancel);
            Click(Named<Button>(editor, "SaveButton")); // Existing editor completion contract remains inert.
            Assert.Equal(ScoreEventEditorWindow.EditorResult.Cancelled, editor.Result);
        });
        Assert.NotNull(beforeCancel); Assert.NotNull(recovery);
        Assert.Equal(beforeCancel, Snapshot(f.Match)); Assert.Equal(changes, f.Changes);
        AssertRecovery(recovery, f.Recovery());
    }

    [AvaloniaFact]
    public async Task OrdinaryRunningClockDoesNotMakeAnOtherwiseCurrentAddDraftIneligible()
    {
        using var f = await Fixture.Create(); f.Match.StartClock(); int changes = f.Changes;
        var before = f.Match.ElapsedInQuarter;
        await OpenActualAddDialog(f, editor =>
        {
            Assert.True(f.Match.ClockRunning);
            SetCustomDraft(editor); Click(Named<Button>(editor, "SaveButton"));
        });
        Assert.True(f.Match.ClockRunning); Assert.True(f.Match.ElapsedInQuarter >= before);
        Assert.Equal(changes + 1, f.Changes); Assert.Equal(3, f.Match.Events.Count);
        Assert.Equal(7, f.Match.AwayTotal);
    }
}

public sealed class AflLiveQuarterDurationDraftTests
{
    private static async Task OpenLiveDuration(Fixture f, int width = 1280, int height = 850)
    {
        // Establish this fixture's normal navigation viewport after startup
        // restoration; the requested compact test size is applied below.
        f.Window.Width = 1280; f.Window.Height = 850;
        await Settle(250);
        if (Read<UxPreferencesData>(f.Window, "_uxPreferences").GuidedOperatorMode)
            Click(Named<Button>(f.Window, "GuidedModeButton"));
        Key(f.Window, PhysicalKey.Digit4, RawInputModifiers.Control); await Settle();
        Assert.Same(Named<TabItem>(f.Window, "MatchSetupTab"), Named<TabControl>(f.Window, "MainTabControl").SelectedItem);
        var workspace = Read<SettingsWorkspace>(f.Window, "_aflSettingsWorkspace");
        var match = Assert.Single(workspace.GetVisualDescendants().OfType<Button>(),
            button => button.IsEffectivelyVisible && Equals(button.Tag, "MATCH"));
        Click(match); await Settle(300);
        Assert.Equal(ClockMode.Countdown, f.Match.ClockMode);
        Assert.True(Named<Control>(f.Window, "QuarterDurationPanel").IsEffectivelyVisible);
        f.Window.Width = width; f.Window.Height = height; await Settle(250);
    }
    private static (bool Running, bool AutoPaused, TimeSpan Duration, TimeSpan Remaining) Countdown(Fixture f) =>
        (Read<bool>(f.Window, "_controlPanelCountdownRunning"), Read<bool>(f.Window, "_controlPanelCountdownAutoPausedClock"),
         Read<TimeSpan>(f.Window, "_controlPanelCountdownDuration"), Read<TimeSpan>(f.Window, "_controlPanelCountdownRemaining"));
    private static void AssertRecovery(Dictionary<string, byte[]?> expected, Dictionary<string, byte[]?> actual)
    { foreach (var (suffix, contents) in expected) Assert.Equal(contents, actual[suffix]); }

    [AvaloniaTheory]
    [InlineData("", "00", "QuarterMinutesBox", 1280, 850)]
    [InlineData("bad", "00", "QuarterMinutesBox", 1280, 850)]
    [InlineData("-1", "00", "QuarterMinutesBox", 1280, 850)]
    [InlineData("61", "00", "QuarterMinutesBox", 1280, 850)]
    [InlineData("20", "", "QuarterSecondsBox", 1280, 850)]
    [InlineData("20", "xx", "QuarterSecondsBox", 1280, 850)]
    [InlineData("20", "60", "QuarterSecondsBox", 1280, 850)]
    [InlineData("20", "-1", "QuarterSecondsBox", 1280, 850)]
    [InlineData("0", "00", "QuarterMinutesBox", 1280, 850)]
    [InlineData("bad", "00", "QuarterMinutesBox", 420, 320)]
    [InlineData("20", "60", "QuarterSecondsBox", 420, 320)]
    public async Task RejectedLiveDurationIsAtomicAndShowsReachableUnitAndRangeExplanation(
        string minutes, string seconds, string invalidField, int width, int height)
    {
        using var f = await Fixture.Create(); await OpenLiveDuration(f, width, height);
        var minutesBox = Named<TextBox>(f.Window, "QuarterMinutesBox");
        var secondsBox = Named<TextBox>(f.Window, "QuarterSecondsBox");
        minutesBox.Text = minutes; secondsBox.Text = seconds;
        var field = Named<TextBox>(f.Window, invalidField);
        await RevealByKeyboard(f.Window, field);
        string before = Snapshot(f.Match); var flags = Countdown(f); var recovery = f.Recovery();
        int changes = f.Changes;
        Key(f.Window, PhysicalKey.Enter); await Settle(120);
        Capture(f.Window, $"afl-duration-rejected-{width}-{minutes}-{seconds}");
        Assert.Equal(before, Snapshot(f.Match)); Assert.Equal(flags, Countdown(f));
        Assert.Equal(changes, f.Changes); AssertRecovery(recovery, f.Recovery());
        Assert.Equal(minutes, minutesBox.Text); Assert.Equal(seconds, secondsBox.Text);
        // This named inline status is a proposed stable part for this feature;
        // current source has no live-duration error channel. Identity may be
        // adapted by root while keeping truthful text/visibility/geometry checks.
        var error = Named<TextBlock>(f.Window, "QuarterDurationError");
        Assert.True(error.IsEffectivelyVisible); Assert.False(string.IsNullOrWhiteSpace(error.Text));
        Assert.Contains("minute", error.Text!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("second", error.Text!, StringComparison.OrdinalIgnoreCase);
        if (minutes == "0" && seconds == "00")
            Assert.True(ReferenceEquals(minutesBox, f.Window.FocusManager?.GetFocusedElement())
                || ReferenceEquals(secondsBox, f.Window.FocusManager?.GetFocusedElement()), "A zero total must retain a useful correction focus in the duration pair.");
        else Assert.Same(field, f.Window.FocusManager?.GetFocusedElement());
        Assert.True(new Rect(f.Window.Bounds.Size).Contains(PaintedBounds(f.Window, error)),
            "The real rejection route must reveal its full explanation without the test scrolling it into view.");
        AssertPhysicalTarget(f.Window, field);
    }

    [AvaloniaTheory]
    [InlineData("0", "30", 30)]
    [InlineData("20", "00", 1200)]
    [InlineData("60", "59", 3659)]
    public async Task ValidLiveDurationRetainsExistingSubminuteAndUpperRangeContract(string minutes, string seconds, int totalSeconds)
    {
        using var f = await Fixture.Create(); await OpenLiveDuration(f);
        var minutesBox = Named<TextBox>(f.Window, "QuarterMinutesBox");
        var secondsBox = Named<TextBox>(f.Window, "QuarterSecondsBox");
        minutesBox.Text = minutes; secondsBox.Text = seconds;
        await RevealByKeyboard(f.Window, secondsBox);
        string history = JsonSerializer.Serialize(f.Match.Events); var elapsed = f.Match.ElapsedInQuarter;
        int changes = f.Changes;
        Key(f.Window, PhysicalKey.Enter); await Settle();
        Assert.Equal(TimeSpan.FromSeconds(totalSeconds), f.Match.QuarterDuration);
        Assert.Equal(elapsed, f.Match.ElapsedInQuarter); Assert.Equal(history, JsonSerializer.Serialize(f.Match.Events));
        Assert.False(f.Match.ClockRunning); Assert.Equal(changes + 1, f.Changes);
        var flags = Countdown(f); Assert.Equal(TimeSpan.FromSeconds(totalSeconds), flags.Duration);
        Assert.Equal(flags.Duration, flags.Remaining);
    }

    [AvaloniaFact]
    public async Task CorrectingARejectedDraftCommitsOnceAndClearsItsExplanation()
    {
        using var f = await Fixture.Create(); await OpenLiveDuration(f);
        var minutes = Named<TextBox>(f.Window, "QuarterMinutesBox");
        var seconds = Named<TextBox>(f.Window, "QuarterSecondsBox");
        minutes.Text = "20"; seconds.Text = "60";
        await RevealByKeyboard(f.Window, seconds); int changes = f.Changes;
        Key(f.Window, PhysicalKey.Enter); await Settle();
        Assert.Equal(changes, f.Changes);
        var error = Named<TextBlock>(f.Window, "QuarterDurationError"); Assert.True(error.IsEffectivelyVisible);
        seconds.Text = "30";
        Key(f.Window, PhysicalKey.Enter); await Settle();
        Assert.Equal(TimeSpan.FromMinutes(20) + TimeSpan.FromSeconds(30), f.Match.QuarterDuration);
        Assert.Equal(changes + 1, f.Changes); Assert.False(error.IsVisible);
        Assert.False(f.Match.ClockRunning);
    }
}

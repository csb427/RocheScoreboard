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
using Roche_Scoreboard.Services.Cloud;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

/// <summary>
/// Real Home Training button,
/// public remote/model APIs and actual headless pointer/key input. Reflection is
/// read-only model observation and removal of this fixture's existing callback;
/// no private navigation/handler/focus/state invocation.
/// </summary>
public sealed class TrainingFormStateRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private static async Task Settle(int milliseconds = 60)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Until(Func<bool> condition, string reason)
    {
        // Readiness bound only, never a performance/animation acceptance threshold.
        for (int attempt = 0; attempt < 50 && !condition(); attempt++) await Settle();
        Assert.True(condition(), reason);
    }

    private static T Named<T>(Control scope, string name) where T : Control =>
        Assert.IsAssignableFrom<T>(scope.FindControl<T>(name));

    private static T Field<T>(object owner, string name) where T : class =>
        Assert.IsAssignableFrom<T>(owner.GetType().GetField(name, Hidden)!.GetValue(owner));

    private static void Key(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
    }

    private static async Task TabTo(Window window, Control target)
    {
        // Real navigation; no Focus(), private focus flags or forced scroll offset.
        for (int attempt = 0; attempt < 100 && !target.IsFocused; attempt++)
        {
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            await Settle(15);
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            await Settle(15);
        }
        Assert.True(target.IsFocused, target.Name + " must be reachable by actual Tab navigation.");
    }

    private static async Task Tap(Window window, Control target)
    {
        Assert.True(target.IsEffectivelyVisible, target.Name + " must be visible.");
        Assert.True(target.IsEnabled, target.Name + " must be enabled.");
        Assert.True(target.Bounds.Width > 0 && target.Bounds.Height > 0, "Target must be measured.");
        var origin = target.TranslatePoint(default, window);
        Assert.NotNull(origin);
        Assert.True(new Rect(window.ClientSize).Contains(new Rect(origin.Value, target.Bounds.Size)),
            target.Name + " must have its whole actual region inside the window before pointer input.");
        var point = new Point(origin.Value.X + target.Bounds.Width / 2, origin.Value.Y + target.Bounds.Height / 2);
        var hit = window.InputHitTest(point);
        Assert.True(ReferenceEquals(hit, target) || hit is Visual child && child.GetVisualAncestors().Contains(target),
            target.Name + " pointer probe must hit its own template; actual=" +
            (hit as Control)?.Name + ":" + hit?.GetType().Name + " point=" + point);
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        await Settle();
    }

    private static string QueueState(TrainingSession session) => JsonSerializer.Serialize(new
    {
        session.ActiveIndex,
        session.IsRunning,
        Active = session.Active?.Id,
        Timers = session.Timers.Select(timer => new
        {
            timer.Id, timer.Title, timer.Duration, timer.Remaining, timer.HasFinished
        }).ToArray()
        // AutoAdvance deliberately omitted: that is the one requested change.
    });

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly App app = Assert.IsType<App>(Application.Current);
        private readonly bool previousSelfTest = ((App)Application.Current!).IsSelfTestMode;
        private readonly AppTheme previousTheme = ((App)Application.Current!).ThemeService.CurrentTheme;
        private readonly UiDensity previousDensity = ((App)Application.Current!).ThemeService.CurrentDensity;
        private readonly bool? previousAnimation = OperatorMotion.AnimationOverride;
        private readonly string? previousQuarter = QuarterLabelService.CurrentOverride;
        private readonly string? previousDataHome = Environment.GetEnvironmentVariable("ROCHE_DATA_HOME");
        private readonly string dataHome = Path.Combine(Path.GetTempPath(), "roche-training-form-state-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, byte[]> previousFiles = new(StringComparer.Ordinal);
        private string? isolatedRoot;
        private bool rootCaptured;
        private Roche_Scoreboard.MainWindow? window;
        internal Roche_Scoreboard.MainWindow Window => window!;
        internal TrainingControlPanel Panel => Named<TrainingControlPanel>(Window, "TrainingControlPanelView");
        internal TrainingSession Session => Field<TrainingSession>(Window, "_trainingSession");
        internal MatchManager Match => Field<MatchManager>(Window, "_match");

        internal static async Task<Fixture> Create(int width, int height)
        {
            var fixture = new Fixture();
            try
            {
                SelfTestStorage.Initialize(true);
                Assert.True(SelfTestStorage.IsEnabled, "Require process-isolated storage before any fixture file access.");
                fixture.isolatedRoot = Path.GetFullPath(SelfTestStorage.RootPath);
                string expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RocheScoreboard-SelfTests"))
                    + Path.DirectorySeparatorChar;
                Assert.StartsWith(expectedParent, fixture.isolatedRoot);
                Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", fixture.dataHome);
                Directory.CreateDirectory(fixture.dataHome);
                // SelfTestStorage is process-latched, so ROCHE_DATA_HOME alone
                // does not isolate actual preferences, layouts or recovery.
                if (Directory.Exists(fixture.isolatedRoot))
                    foreach (string file in Directory.EnumerateFiles(fixture.isolatedRoot, "*", SearchOption.AllDirectories))
                        fixture.previousFiles[file] = File.ReadAllBytes(file);
                fixture.rootCaptured = true;
                foreach (string file in fixture.previousFiles.Keys) File.Delete(file);
                Directory.CreateDirectory(SelfTestStorage.DocumentsDirectory);
                fixture.app.IsSelfTestMode = true;
                OperatorMotion.AnimationOverride = false;
                fixture.window = new Roche_Scoreboard.MainWindow();
                fixture.Window.Show();
                fixture.Window.Width = 1280;
                fixture.Window.Height = 850;
                var home = Named<SportSelectionScreen>(fixture.Window, "SportSelectionPanel");
                await Until(() => home.IsEffectivelyVisible &&
                    home.GetVisualDescendants().OfType<Button>().Any(button =>
                        button.Name == "TrainingLaunchAction" && button.Bounds.Width > 0 &&
                        TopLevel.GetTopLevel(button) == fixture.Window && button.TranslatePoint(default, fixture.Window) is not null),
                    "Actual Home and measured Training launch action must be ready.");
                var training = Assert.Single(home.GetVisualDescendants().OfType<Button>(),
                    button => button.Name == "TrainingLaunchAction");
                await Tap(fixture.Window, training);
                await Until(() => fixture.Panel.IsEffectivelyVisible,
                    "Actual Training launch action must mount the production console.");
                Assert.False(home.IsEffectivelyVisible);
                fixture.Session.AddTimer(new TrainingTimer("Existing active drill", TimeSpan.FromMinutes(3)));
                fixture.Session.AddTimer(new TrainingTimer("Existing next drill", TimeSpan.FromSeconds(50)));
                Assert.False(fixture.Session.IsRunning);
                fixture.Window.Width = width;
                fixture.Window.Height = height;
                await Settle(180);
                return fixture;
            }
            catch
            {
                // Rollback runs even if construction/readiness assertions fail.
                await fixture.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            var errors = new List<Exception>();
            void Attempt(Action action) { try { action(); } catch (Exception error) { errors.Add(error); } }
            if (window is not null)
            {
                Attempt(() => window.Close());
                // Detach only this window's pre-existing leaked static handler.
                Attempt(() =>
                {
                    var method = window.GetType().GetMethod("OnQuarterLabelServiceChanged", Hidden);
                    if (method is not null)
                        QuarterLabelService.LabelsChanged -= method.CreateDelegate<Action>(window);
                });
                try { await Settle(180); } catch (Exception error) { errors.Add(error); }
            }
            Attempt(() => app.ThemeService.SetTheme(previousTheme));
            Attempt(() => app.ThemeService.SetDensity(previousDensity));
            Attempt(() => QuarterLabelService.SetCurrentOverride(previousQuarter));
            // Restore every process-isolated file, including .bak/.tmp, layouts,
            // recency, app appearance, match/recovery and any shutdown writes.
            if (rootCaptured && isolatedRoot is not null)
            {
                Attempt(() =>
                {
                    if (Directory.Exists(isolatedRoot))
                        foreach (string file in Directory.EnumerateFiles(isolatedRoot, "*", SearchOption.AllDirectories))
                            if (!previousFiles.ContainsKey(file)) File.Delete(file);
                });
                foreach (var saved in previousFiles)
                    Attempt(() =>
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(saved.Key)!);
                        File.WriteAllBytes(saved.Key, saved.Value);
                    });
            }
            Attempt(() => OperatorMotion.AnimationOverride = previousAnimation);
            Attempt(() => app.IsSelfTestMode = previousSelfTest);
            Attempt(() => Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", previousDataHome));
            Attempt(() => { if (Directory.Exists(dataHome)) Directory.Delete(dataHome, true); });
            if (errors.Count > 0) throw new AggregateException("Training fixture restoration failed.", errors);
        }
    }

    [AvaloniaTheory]
    [InlineData(1280, 850, true)]
    [InlineData(1280, 850, false)]
    [InlineData(420, 320, true)]
    [InlineData(420, 320, false)]
    [Trait("Evidence", "PendingBaseline")]
    public async Task PublicAutoAdvanceChangeUpdatesVisibleStateAndNativeToggleWithoutTimerMutation(
        int width, int height, bool throughRemote)
    {
        await using var fixture = await Fixture.Create(width, height);
        var session = fixture.Session;
        var active = session.Active;
        var checkbox = Named<CheckBox>(fixture.Panel, "AutoAdvanceToggle");
        Assert.True(session.AutoAdvance);
        Assert.True(checkbox.IsChecked);
        string queueBefore = QueueState(session);
        string matchBefore = JsonSerializer.Serialize(fixture.Match.ToState());
        int structural = 0;
        void CountStructural() => structural++;
        session.StructuralChanged += CountStructural;
        try
        {
            // Public production remote boundary; no network or cloud session is
            // started. Alternative case exercises the public model contract.
            if (throughRemote) ((IRemoteCommandTarget)fixture.Window).TrainingSetAutoAdvance(false);
            else session.AutoAdvance = false;
            Assert.False(session.AutoAdvance);
            await Until(() => checkbox.IsChecked == false,
                "Visible AutoAdvance must reflect accepted " + (throughRemote ? "remote" : "public setter") + " false.");
            Assert.Equal(queueBefore, QueueState(session));
            Assert.Same(active, session.Active);
            Assert.Equal(0, structural);

            if (throughRemote) ((IRemoteCommandTarget)fixture.Window).TrainingSetAutoAdvance(true);
            else session.AutoAdvance = true;
            await Until(() => checkbox.IsChecked == true, "The true reversal must also reach the current checkbox.");
            Assert.Equal(queueBefore, QueueState(session));

            await TabTo(fixture.Window, checkbox);
            // Validate actual pointer hit without changing state, then use its
            // native keyboard activation. This tests semantics, not ClickEvent.
            var center = checkbox.TranslatePoint(new Point(checkbox.Bounds.Width / 2, checkbox.Bounds.Height / 2), fixture.Window);
            Assert.NotNull(center);
            var hit = fixture.Window.InputHitTest(center.Value);
            Assert.True(ReferenceEquals(hit, checkbox) || hit is Visual child && child.GetVisualAncestors().Contains(checkbox));
            Key(fixture.Window, PhysicalKey.Space);
            await Settle();
            Assert.False(checkbox.IsChecked);
            Assert.False(session.AutoAdvance);
            Assert.Equal(queueBefore, QueueState(session));
            Assert.Same(active, session.Active);
            Assert.False(session.IsRunning);
            Assert.Equal(0, structural);
            Assert.Equal(matchBefore, JsonSerializer.Serialize(fixture.Match.ToState()));
        }
        finally { session.StructuralChanged -= CountStructural; }
    }

    [AvaloniaTheory]
    [InlineData(1280, 850)]
    [InlineData(420, 320)]
    [Trait("Evidence", "PendingBaseline")]
    public async Task PointerRefocusPreservesTheTypedTitleAndAllowsNativeCaretCorrectionWithoutAppending(
        int width, int height)
    {
        await using var fixture = await Fixture.Create(width, height);
        var title = Named<TextBox>(fixture.Panel, "NewTitleBox");
        var minutes = Named<TextBox>(fixture.Panel, "NewMinutesBox");
        var seconds = Named<TextBox>(fixture.Panel, "NewSecondsBox");
        string queueBefore = QueueState(fixture.Session);
        string matchBefore = JsonSerializer.Serialize(fixture.Match.ToState());
        int structural = 0;
        void CountStructural() => structural++;
        fixture.Session.StructuralChanged += CountStructural;
        try
        {
            await TabTo(fixture.Window, title);
            await Tap(fixture.Window, title);
            Assert.True(title.IsFocused, "Actual pointer hit must leave title input focused.");
            Key(fixture.Window, PhysicalKey.A, RawInputModifiers.Control);
            const string draft = "Sprint r recovery 12";
            fixture.Window.KeyTextInput(draft);
            Assert.Equal(draft, title.Text);
            string minutesBefore = minutes.Text ?? "";
            string secondsBefore = seconds.Text ?? "";

            await Tap(fixture.Window, minutes);
            Assert.True(minutes.IsFocused, "Leaving the title must actually focus its sibling input.");
            Assert.False(title.IsFocused);
            Assert.Equal(draft, title.Text);
            await Tap(fixture.Window, title);
            Assert.True(title.IsFocused, "Refocus must actually hit and focus the title input.");
            Assert.Equal(draft, title.Text); // Expected source-candidate baseline failure.
            Assert.InRange(title.CaretIndex, 0, draft.Length);

            // Real key movement establishes a known insertion caret; native
            // pointer placement itself may depend on font/platform glyph widths.
            Key(fixture.Window, PhysicalKey.End);
            for (int step = 0; step < 3; step++) Key(fixture.Window, PhysicalKey.ArrowLeft);
            Assert.Equal(draft.Length - 3, title.CaretIndex);
            Assert.Equal(title.SelectionStart, title.SelectionEnd);
            fixture.Window.KeyTextInput("7");
            string corrected = draft[..^3] + "7" + draft[^3..];
            Assert.Equal(corrected, title.Text);
            Assert.Equal(draft.Length - 2, title.CaretIndex);
            Assert.Equal(minutesBefore, minutes.Text ?? "");
            Assert.Equal(secondsBefore, seconds.Text ?? "");
            Assert.Equal(queueBefore, QueueState(fixture.Session));
            Assert.False(fixture.Session.IsRunning);
            Assert.Equal(0, structural);
            Assert.Equal(matchBefore, JsonSerializer.Serialize(fixture.Match.ToState()));
        }
        finally { fixture.Session.StructuralChanged -= CountStructural; }
    }

    private static string RecentFileBytes()
    {
        string directory = SelfTestStorage.DocumentsDirectory;
        var files = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "training_recent_timers.json*")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToDictionary(path => Path.GetFileName(path)!, path => Convert.ToBase64String(File.ReadAllBytes(path)))
            : new Dictionary<string, string>();
        return JsonSerializer.Serialize(files);
    }

    private static void AssertInsideOwner(Window owner, Control target, string reason)
    {
        Assert.True(target.IsEffectivelyVisible, reason + " must be visible.");
        Assert.True(target.Bounds.Width > 0 && target.Bounds.Height > 0, reason + " must be measured.");
        var origin = target.TranslatePoint(default, owner);
        Assert.NotNull(origin);
        Assert.True(new Rect(owner.ClientSize).Contains(new Rect(origin.Value, target.Bounds.Size)),
            reason + " must fit the owner after validation reveal.");
    }

    [AvaloniaTheory]
    [InlineData(1280, 850, "oops", "05", "NewMinutesBox", "02", 125, "whole", true)]
    [InlineData(420, 320, "oops", "05", "NewMinutesBox", "02", 125, "whole", false)]
    [InlineData(1280, 850, "02", "oops", "NewSecondsBox", "05", 125, "whole", false)]
    [InlineData(420, 320, "02", "oops", "NewSecondsBox", "05", 125, "whole", true)]
    [InlineData(1280, 850, "0", "0", "NewMinutesBox", "2", 120, "at least", true)]
    [InlineData(420, 320, "0", "0", "NewMinutesBox", "2", 120, "at least", false)]
    [InlineData(1280, 850, "35791395", "00", "NewMinutesBox", "2", 120, "too long", false)]
    [InlineData(420, 320, "35791395", "00", "NewMinutesBox", "2", 120, "too long", true)]
    public async Task RejectedAddFocusesItsBadFieldWithoutChangingDraftQueueOrRecencyAndCorrectedEnterAddsOnce(
        int width, int height, string rawMinutes, string rawSeconds, string badFieldName,
        string correction, int expectedSeconds, string expectedExplanation, bool pointerAdd)
    {
        await using var fixture = await Fixture.Create(width, height);
        var title = Named<TextBox>(fixture.Panel, "NewTitleBox");
        var minutes = Named<TextBox>(fixture.Panel, "NewMinutesBox");
        var seconds = Named<TextBox>(fixture.Panel, "NewSecondsBox");
        var add = Named<Button>(fixture.Panel, "AddTimerButton");
        var error = Named<TextBlock>(fixture.Panel, "TrainingEntryError");
        var bad = Named<TextBox>(fixture.Panel, badFieldName);
        const string rawTitle = "  Field retry drill  ";
        // Public values exercise paste/import-style malformed input; the digit
        // typing filter is intentionally not bypassed for the valid correction.
        title.Text = rawTitle;
        minutes.Text = rawMinutes;
        seconds.Text = rawSeconds;
        RecentTrainingTimerStorage.Record("Existing reusable drill", 45);
        Assert.Single(RecentTrainingTimerStorage.Load());
        string recencyBefore = RecentFileBytes();
        string queueBefore = QueueState(fixture.Session);
        var originalTimers = fixture.Session.Timers.ToArray();
        string originalTimerValues = JsonSerializer.Serialize(originalTimers);
        var active = fixture.Session.Active;
        int activeIndex = fixture.Session.ActiveIndex;
        bool autoAdvance = fixture.Session.AutoAdvance;
        string matchBefore = JsonSerializer.Serialize(fixture.Match.ToState());
        int structural = 0, changes = 0;
        void CountStructural() => structural++;
        void CountChanges() => changes++;
        fixture.Session.StructuralChanged += CountStructural;
        fixture.Session.Changed += CountChanges;
        try
        {
            if (pointerAdd)
            {
                await TabTo(fixture.Window, add);
                await Tap(fixture.Window, add);
            }
            else
            {
                // Submit from the OTHER numeric field, so unchanged focus is
                // not mistaken for successful bad-field focus recovery.
                var origin = ReferenceEquals(bad, minutes) ? seconds : minutes;
                await TabTo(fixture.Window, origin);
                await Tap(fixture.Window, origin);
                Assert.True(origin.IsFocused);
                Assert.False(bad.IsFocused);
                Key(fixture.Window, PhysicalKey.Enter);
                await Settle();
            }

            Assert.Equal(queueBefore, QueueState(fixture.Session));
            Assert.Same(active, fixture.Session.Active);
            Assert.False(fixture.Session.IsRunning);
            Assert.Equal(autoAdvance, fixture.Session.AutoAdvance);
            Assert.Equal(0, structural);
            Assert.Equal(0, changes);
            Assert.Equal(recencyBefore, RecentFileBytes());
            Assert.Equal(rawTitle, title.Text);
            Assert.Equal(rawMinutes, minutes.Text);
            Assert.Equal(rawSeconds, seconds.Text);
            Assert.Contains(expectedExplanation, error.Text ?? "", StringComparison.OrdinalIgnoreCase);
            if (Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var frame = fixture.Window.CaptureRenderedFrame();
                Assert.NotNull(frame); frame.Save(Path.Combine(directory, $"training-add-rejected-{width}-{rawMinutes}-{rawSeconds}.png"));
            }
            AssertInsideOwner(fixture.Window, error, "Rejected Add explanation");
            Assert.True(bad.IsFocused,
                "Rejected Add must focus " + badFieldName + " rather than retain the submitting command/other field.");
            AssertInsideOwner(fixture.Window, bad, "Rejected Add correction field");
            var badOrigin = bad.TranslatePoint(default, fixture.Window);
            Assert.NotNull(badOrigin);
            var hit = fixture.Window.InputHitTest(badOrigin.Value + new Vector(bad.Bounds.Width / 2, bad.Bounds.Height / 2));
            Assert.True(ReferenceEquals(hit, bad) || hit is Visual child && child.GetVisualAncestors().Contains(bad),
                "The focused invalid field must also remain physically reachable after the real rejection route.");

            Key(fixture.Window, PhysicalKey.A, RawInputModifiers.Control);
            fixture.Window.KeyTextInput(correction);
            Assert.Equal(correction, bad.Text);
            Key(fixture.Window, PhysicalKey.Enter);
            await Settle();

            Assert.Equal(1, structural);
            Assert.Equal(1, changes);
            Assert.Equal(originalTimers.Length + 1, fixture.Session.Timers.Count);
            for (int index = 0; index < originalTimers.Length; index++)
                Assert.Same(originalTimers[index], fixture.Session.Timers[index]);
            Assert.Equal(originalTimerValues, JsonSerializer.Serialize(originalTimers));
            Assert.Same(active, fixture.Session.Active);
            Assert.Equal(activeIndex, fixture.Session.ActiveIndex);
            Assert.False(fixture.Session.IsRunning);
            Assert.Equal(autoAdvance, fixture.Session.AutoAdvance);
            var appended = fixture.Session.Timers[^1];
            Assert.Equal(rawTitle.Trim(), appended.Title);
            Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), appended.Duration);
            Assert.Equal(appended.Duration, appended.Remaining);
            Assert.False(appended.HasFinished);
            Assert.DoesNotContain(appended.Id, originalTimers.Select(timer => timer.Id));
            Assert.False(error.IsVisible);
            Assert.True(string.IsNullOrEmpty(error.Text));
            Assert.Equal(rawTitle, title.Text);
            Assert.Equal(ReferenceEquals(bad, minutes) ? correction : rawMinutes, minutes.Text);
            Assert.Equal(ReferenceEquals(bad, seconds) ? correction : rawSeconds, seconds.Text);
            var recents = RecentTrainingTimerStorage.Load();
            Assert.Equal(2, recents.Count);
            Assert.Equal(appended.Title, recents[0].Title);
            Assert.Equal(expectedSeconds, recents[0].Seconds);
            Assert.Equal("Existing reusable drill", recents[1].Title);
            Assert.Equal(45, recents[1].Seconds);
            Assert.Equal(matchBefore, JsonSerializer.Serialize(fixture.Match.ToState()));
        }
        finally
        {
            fixture.Session.StructuralChanged -= CountStructural;
            fixture.Session.Changed -= CountChanges;
        }
    }

    [AvaloniaTheory]
    [InlineData(1280, 850)]
    [InlineData(420, 320)]
    public async Task AddTimerFieldsExposeDistinctAccessiblePurposesAndRemainReachableByNativeTab(int width, int height)
    {
        await using var fixture = await Fixture.Create(width, height);
        string before = QueueState(fixture.Session);
        string recencyBefore = RecentFileBytes();
        foreach (var (name, purpose) in new[]
        {
            ("NewTitleBox", "New timer title"),
            ("NewMinutesBox", "New timer minutes"),
            ("NewSecondsBox", "New timer seconds")
        })
        {
            var field = Named<TextBox>(fixture.Panel, name);
            await TabTo(fixture.Window, field);
            AssertInsideOwner(fixture.Window, field, purpose);
            Assert.Equal(purpose, Avalonia.Automation.AutomationProperties.GetName(field));
        }
        Assert.Equal(before, QueueState(fixture.Session));
        Assert.Equal(recencyBefore, RecentFileBytes());
    }
}

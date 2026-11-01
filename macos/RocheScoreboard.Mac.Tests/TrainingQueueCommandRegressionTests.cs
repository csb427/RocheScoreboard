using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

/// <summary>
/// OUTSIDE-REPOSITORY DRAFT: not compiled or executed. Production Training panel,
/// public model APIs and actual Tab/Space/pointer input; no private handlers or
/// forced focus after activation. Readiness failures are not product baselines.
/// </summary>
public sealed partial class TrainingQueueCommandRegressionTests
{
    private static async Task Settle(int milliseconds = 60)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Key(Window owner, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        owner.KeyPressQwerty(key, modifiers);
        owner.KeyReleaseQwerty(key, modifiers);
    }

    private static T Named<T>(Control owner, string name) where T : Control =>
        Assert.IsAssignableFrom<T>(owner.FindControl<T>(name));

    private static Button RowCommand(TrainingControlPanel panel, TrainingTimer timer, string purpose)
    {
        var row = Assert.Single(Named<ItemsControl>(panel, "QueueItems").Items.OfType<Border>(),
            candidate => candidate.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == timer.Title));
        return Assert.Single(row.GetVisualDescendants().OfType<Button>(),
            command => Equals(ToolTip.GetTip(command), purpose));
    }

    private static TextBox[] CorrectionFields(TrainingControlPanel panel)
    {
        var save = Assert.Single(panel.GetVisualDescendants().OfType<Button>(),
            command => Equals(ToolTip.GetTip(command), "Save (Enter)"));
        return save.GetVisualAncestors().OfType<Grid>()
            .First(grid => grid.GetVisualDescendants().OfType<TextBox>().Count() == 3)
            .GetVisualDescendants().OfType<TextBox>().ToArray();
    }

    private static async Task TabTo(Window owner, Control target)
    {
        for (int count = 0; count < 100 && !target.IsFocused; count++)
        {
            owner.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            await Settle(15);
            owner.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            await Settle(15);
        }
        Assert.True(target.IsFocused, "Actual Tab must reach " + target.Name + " / " + ToolTip.GetTip(target));
    }

    private static Point ActualPoint(Window owner, Control target, bool requireEnabled = true)
    {
        Assert.True(target.IsEffectivelyVisible);
        if (requireEnabled) Assert.True(target.IsEnabled);
        Assert.True(target.Bounds.Width > 0 && target.Bounds.Height > 0);
        var origin = target.TranslatePoint(default, owner);
        Assert.NotNull(origin);
        Assert.True(new Rect(owner.ClientSize).Contains(new Rect(origin.Value, target.Bounds.Size)),
            "Command must fit the actual owner before/after input: " + ToolTip.GetTip(target));
        var point = origin.Value + new Vector(target.Bounds.Width / 2, target.Bounds.Height / 2);
        if (requireEnabled)
        {
            var hit = owner.InputHitTest(point);
            Assert.True(ReferenceEquals(hit, target) || hit is Visual child && child.GetVisualAncestors().Contains(target),
                "Actual pointer must hit command template, not a stale/overlapped point. Hit=" + (hit as Control)?.Name + ":" + hit?.GetType().Name);
        }
        return point;
    }

    private static async Task Activate(Window owner, Button command, bool pointer)
    {
        // Caller must establish focus before activation. Nothing in this helper
        // repairs focus after the operation.
        Assert.True(command.IsFocused);
        if (pointer)
        {
            var point = ActualPoint(owner, command);
            owner.MouseMove(point);
            owner.MouseDown(point, MouseButton.Left);
            owner.MouseUp(point, MouseButton.Left);
        }
        else Key(owner, PhysicalKey.Space);
        await Settle();
    }

    private static string Values(IEnumerable<TrainingTimer> timers) => JsonSerializer.Serialize(timers.Select(timer => new
    {
        timer.Id, timer.Title, timer.Duration, timer.Remaining, timer.HasFinished
    }).ToArray());

    private static string QueueState(TrainingSession session) => JsonSerializer.Serialize(new
    {
        session.ActiveIndex, session.IsRunning, session.AutoAdvance,
        Active = session.Active?.Id, Timers = Values(session.Timers)
    });

    private static void AssertRunningProgress(TrainingSession session, TimeSpan before, Stopwatch observation)
    {
        Assert.True(session.IsRunning);
        TimeSpan after = session.ActiveRemaining;
        Assert.True(after > TimeSpan.Zero);
        // Bound progress by actual observed wall time, not a performance limit.
        // This rejects reset/skip/pause while tolerating scheduling delays.
        Assert.InRange((before - after).Ticks, 0,
            observation.Elapsed.Ticks + TimeSpan.FromMilliseconds(20).Ticks);
    }

    private sealed record Draft(TextBox Title, TextBox Minutes, TextBox Seconds,
        string RawTitle, string RawMinutes, string RawSeconds, int Caret, int Start, int End);

    private static Draft ReadDraft(TrainingControlPanel panel)
    {
        var fields = CorrectionFields(panel);
        return new(fields[0], fields[1], fields[2], fields[0].Text ?? "", fields[1].Text ?? "", fields[2].Text ?? "",
            fields[0].CaretIndex, fields[0].SelectionStart, fields[0].SelectionEnd);
    }

    private static void AssertDraft(TrainingControlPanel panel, Draft before)
    {
        var fields = CorrectionFields(panel);
        Assert.Same(before.Title, fields[0]);
        Assert.Same(before.Minutes, fields[1]);
        Assert.Same(before.Seconds, fields[2]);
        Assert.Equal(before.RawTitle, fields[0].Text);
        Assert.Equal(before.RawMinutes, fields[1].Text);
        Assert.Equal(before.RawSeconds, fields[2].Text);
        Assert.Equal(before.Caret, fields[0].CaretIndex);
        Assert.Equal(before.Start, fields[0].SelectionStart);
        Assert.Equal(before.End, fields[0].SelectionEnd);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly App app = Assert.IsType<App>(Application.Current);
        private readonly bool oldSelfTest = ((App)Application.Current!).IsSelfTestMode;
        private readonly bool? oldAnimation = OperatorMotion.AnimationOverride;
        private readonly string? oldDataHome = Environment.GetEnvironmentVariable("ROCHE_DATA_HOME");
        private readonly string dataHome = Path.Combine(Path.GetTempPath(), "roche-queue-command-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        private string? documents;
        private bool storageCaptured;
        private NativeWindow? window;
        internal NativeWindow Window => window!;
        internal TrainingControlPanel Panel { get; private set; } = null!;
        internal TextBox OtherScope { get; private set; } = null!;
        internal TrainingSession Session { get; } = new();
        internal TrainingTimer Active { get; } = new("Active drill", TimeSpan.FromMinutes(3)) { Remaining = TimeSpan.FromSeconds(150) };
        internal TrainingTimer Editor { get; } = new("Draft interval", TimeSpan.FromSeconds(90));
        internal TrainingTimer Target { get; } = new("Queue target", TimeSpan.FromSeconds(125));
        internal TrainingTimer Tail { get; } = new("Final drill", TimeSpan.FromSeconds(45));
        internal int Exits;

        internal static async Task<Fixture> Create(int width = 980, int height = 780, bool onlyActive = false,
            IReadOnlyList<(string Title, int Seconds)>? recentSeeds = null)
        {
            var fixture = new Fixture();
            try
            {
                SelfTestStorage.Initialize(true);
                Assert.True(SelfTestStorage.IsEnabled);
                fixture.documents = Path.GetFullPath(SelfTestStorage.DocumentsDirectory);
                Assert.StartsWith(Path.GetFullPath(SelfTestStorage.RootPath) + Path.DirectorySeparatorChar, fixture.documents);
                Directory.CreateDirectory(fixture.documents);
                foreach (string path in Directory.EnumerateFiles(fixture.documents, "training_recent_timers.json*"))
                    fixture.files[path] = File.ReadAllBytes(path);
                fixture.storageCaptured = true;
                foreach (string path in fixture.files.Keys) File.Delete(path);
                Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", fixture.dataHome);
                Directory.CreateDirectory(fixture.dataHome);
                fixture.app.IsSelfTestMode = true;
                OperatorMotion.AnimationOverride = false;
                fixture.Session.AddTimer(fixture.Active);
                if (!onlyActive)
                {
                    fixture.Session.AddTimer(fixture.Editor);
                    fixture.Session.AddTimer(fixture.Target);
                    fixture.Session.AddTimer(fixture.Tail);
                }
                if (recentSeeds is not null)
                    foreach (var (title, seconds) in recentSeeds)
                        RecentTrainingTimerStorage.Record(title, seconds);
                fixture.Panel = new TrainingControlPanel();
                fixture.Panel.Bind(fixture.Session);
                fixture.Panel.ExitRequested += () => fixture.Exits++;
                fixture.OtherScope = new TextBox { Name = "IndependentScopeDraft", Text = "Unrelated draft", Height = 36, Margin = new Thickness(8) };
                var host = new Grid();
                host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                host.Children.Add(fixture.OtherScope);
                Grid.SetRow(fixture.Panel, 1);
                host.Children.Add(fixture.Panel);
                fixture.window = new NativeWindow { Width = width, Height = height, Content = host };
                fixture.Window.Show();
                await Settle(150);
                Assert.True(fixture.Panel.IsEffectivelyVisible);
                Assert.Equal(onlyActive ? 1 : 4, fixture.Session.Timers.Count);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        internal async Task OpenDraft()
        {
            var edit = RowCommand(Panel, Editor, "Edit title and duration");
            await TabTo(Window, edit);
            await Activate(Window, edit, false);
            var fields = CorrectionFields(Panel);
            Assert.True(fields[0].IsFocused, "Actual Edit must enter its current title field.");
            Key(Window, PhysicalKey.A, RawInputModifiers.Control);
            Window.KeyTextInput("Uncommitted lane revision");
            fields[1].Text = "001";
            fields[2].Text = "009";
            Key(Window, PhysicalKey.Home);
            Key(Window, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
            Key(Window, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
            Assert.NotEqual(fields[0].SelectionStart, fields[0].SelectionEnd);
        }

        public async ValueTask DisposeAsync()
        {
            var errors = new List<Exception>();
            void Attempt(Action action) { try { action(); } catch (Exception error) { errors.Add(error); } }
            Attempt(() => Session.Pause());
            if (window is not null)
            {
                Attempt(() => window.Close());
                try { await Settle(100); } catch (Exception error) { errors.Add(error); }
            }
            if (storageCaptured && documents is not null)
            {
                Attempt(() =>
                {
                    if (Directory.Exists(documents))
                        foreach (string path in Directory.EnumerateFiles(documents, "training_recent_timers.json*"))
                            if (!files.ContainsKey(path)) File.Delete(path);
                });
                foreach (var saved in files)
                    Attempt(() => { Directory.CreateDirectory(Path.GetDirectoryName(saved.Key)!); File.WriteAllBytes(saved.Key, saved.Value); });
            }
            Attempt(() => OperatorMotion.AnimationOverride = oldAnimation);
            Attempt(() => app.IsSelfTestMode = oldSelfTest);
            Attempt(() => Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", oldDataHome));
            Attempt(() => { if (Directory.Exists(dataHome)) Directory.Delete(dataHome, true); });
            if (errors.Count > 0) throw new AggregateException("Queue fixture restoration failed.", errors);
        }
    }

    [AvaloniaTheory]
    [InlineData("Move up", false, 980, 780)]
    [InlineData("Move down", true, 980, 780)]
    [InlineData("Move up", true, 420, 500)]
    [InlineData("Move down", false, 420, 500)]
    public async Task NativeMovePreservesRunningActiveAndRawEditorAndRetainsItsEquivalentCommandFocus(
        string direction, bool pointer, int width, int height)
    {
        await using var fixture = await Fixture.Create(width, height);
        await fixture.OpenDraft();
        fixture.Session.Start();
        var command = RowCommand(fixture.Panel, fixture.Target, direction);
        await TabTo(fixture.Window, command);
        var draft = ReadDraft(fixture.Panel);
        string values = Values(fixture.Session.Timers);
        var observation = Stopwatch.StartNew();
        TimeSpan before = fixture.Session.ActiveRemaining;
        int structural = 0, changes = 0;
        void CountStructure() => structural++;
        void CountChanges() => changes++;
        fixture.Session.StructuralChanged += CountStructure;
        fixture.Session.Changed += CountChanges;
        try
        {
            await Activate(fixture.Window, command, pointer);
            var expected = direction == "Move up"
                ? new[] { fixture.Active, fixture.Target, fixture.Editor, fixture.Tail }
                : new[] { fixture.Active, fixture.Editor, fixture.Tail, fixture.Target };
            Assert.Equal(expected.Select(timer => timer.Id), fixture.Session.Timers.Select(timer => timer.Id));
            Assert.Equal(1, structural);
            Assert.Equal(1, changes);
            Assert.Same(fixture.Active, fixture.Session.Active);
            Assert.Equal(0, fixture.Session.ActiveIndex);
            AssertRunningProgress(fixture.Session, before, observation);
            Assert.Equal(TimeSpan.FromSeconds(150), fixture.Active.Remaining);
            Assert.Equal(TimeSpan.FromMinutes(3), fixture.Active.Duration);
            Assert.Equal(values, Values(new[] { fixture.Active, fixture.Editor, fixture.Target, fixture.Tail }));
            AssertDraft(fixture.Panel, draft);
            var arrow = RowCommand(fixture.Panel, fixture.Target, direction);
            // Down moved this timer to the global last position. A genuinely
            // disabled arrow cannot own keyboard focus: keep the same timer's
            // existing, non-destructive Edit command as the explicit anchor.
            Button current;
            if (direction == "Move down")
            {
                Assert.False(arrow.IsEnabled);
                current = RowCommand(fixture.Panel, fixture.Target, "Edit title and duration");
            }
            else { Assert.True(arrow.IsEnabled); current = arrow; }
            Assert.True(current.IsFocused, "Accepted move must retain its enabled command or safe same-timer anchor without test repair.");
            ActualPoint(fixture.Window, current);
            Assert.Equal(0, fixture.Exits);
        }
        finally { fixture.Session.StructuralChanged -= CountStructure; fixture.Session.Changed -= CountChanges; }
    }

    [AvaloniaTheory]
    [InlineData(false, false, 980, 780)]
    [InlineData(true, true, 980, 780)]
    [InlineData(false, true, 420, 500)]
    [InlineData(true, false, 420, 500)]
    public async Task NativeUpcomingRemoveKeepsRunningClockAndEditorAndFocusesNearestSurvivingRemove(
        bool last, bool pointer, int width, int height)
    {
        await using var fixture = await Fixture.Create(width, height);
        await fixture.OpenDraft();
        fixture.Session.Start();
        var removed = last ? fixture.Tail : fixture.Target;
        var survivor = last ? fixture.Target : fixture.Tail;
        var command = RowCommand(fixture.Panel, removed, "Remove");
        await TabTo(fixture.Window, command);
        var draft = ReadDraft(fixture.Panel);
        var observation = Stopwatch.StartNew();
        TimeSpan before = fixture.Session.ActiveRemaining;
        var retained = new[] { fixture.Active, fixture.Editor, survivor };
        string values = Values(retained);
        int structure = 0;
        void Count() => structure++;
        fixture.Session.StructuralChanged += Count;
        try
        {
            await Activate(fixture.Window, command, pointer);
            Assert.Equal(retained.Select(timer => timer.Id), fixture.Session.Timers.Select(timer => timer.Id));
            Assert.DoesNotContain(removed, fixture.Session.Timers);
            Assert.Equal(1, structure);
            Assert.Equal(values, Values(retained));
            Assert.Same(fixture.Active, fixture.Session.Active);
            AssertRunningProgress(fixture.Session, before, observation);
            AssertDraft(fixture.Panel, draft);
            var current = RowCommand(fixture.Panel, survivor, "Remove");
            Assert.True(current.IsFocused, "Removal must focus the next or previous surviving Remove without forced focus.");
            ActualPoint(fixture.Window, current);
            Assert.Equal(0, fixture.Exits);
        }
        finally { fixture.Session.StructuralChanged -= Count; }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeActiveRemoveRetainsExistingPausedSuccessorPolicyAndCurrentCommandFocus(bool pointer)
    {
        await using var fixture = await Fixture.Create();
        fixture.Session.Start();
        var command = RowCommand(fixture.Panel, fixture.Active, "Remove");
        await TabTo(fixture.Window, command);
        int structure = 0;
        void Count() => structure++;
        fixture.Session.StructuralChanged += Count;
        try
        {
            await Activate(fixture.Window, command, pointer);
            Assert.Equal(1, structure);
            Assert.Equal(new[] { fixture.Editor.Id, fixture.Target.Id, fixture.Tail.Id }, fixture.Session.Timers.Select(timer => timer.Id));
            Assert.Same(fixture.Editor, fixture.Session.Active);
            Assert.Equal(0, fixture.Session.ActiveIndex);
            Assert.False(fixture.Session.IsRunning);
            Assert.Equal(fixture.Editor.Duration, fixture.Session.ActiveRemaining);
            var current = RowCommand(fixture.Panel, fixture.Editor, "Remove");
            Assert.True(current.IsFocused);
            ActualPoint(fixture.Window, current);
            Assert.Equal(0, fixture.Exits);
        }
        finally { fixture.Session.StructuralChanged -= Count; }
    }

    [AvaloniaFact]
    public async Task RemovingTheOnlyActiveTimerFocusesTheExistingAddActionWithoutStartingAnything()
    {
        await using var fixture = await Fixture.Create(onlyActive: true);
        fixture.Session.Start();
        var command = RowCommand(fixture.Panel, fixture.Active, "Remove");
        await TabTo(fixture.Window, command);
        await Activate(fixture.Window, command, false);
        Assert.Empty(fixture.Session.Timers);
        Assert.Null(fixture.Session.Active);
        Assert.False(fixture.Session.IsRunning);
        var add = Named<Button>(fixture.Panel, "AddTimerButton");
        Assert.True(add.IsFocused);
        ActualPoint(fixture.Window, add);
        Assert.Equal(0, fixture.Exits);
    }

    [AvaloniaFact]
    public async Task UnavailableGlobalEdgeArrowsAreDisabledAndNativePointerAndTabCannotMutateTheQueue()
    {
        await using var fixture = await Fixture.Create();
        var firstUp = RowCommand(fixture.Panel, fixture.Active, "Move up");
        var lastDown = RowCommand(fixture.Panel, fixture.Tail, "Move down");
        string before = QueueState(fixture.Session);
        int structure = 0;
        void Count() => structure++;
        fixture.Session.StructuralChanged += Count;
        try
        {
            Assert.False(firstUp.IsEnabled);
            Assert.False(lastDown.IsEnabled);
            foreach (var (timer, disabled) in new[] { (fixture.Active, firstUp), (fixture.Tail, lastDown) })
            {
                // Natural navigation to its enabled sibling reveals the same
                // row before probing the unavailable edge target.
                await TabTo(fixture.Window, RowCommand(fixture.Panel, timer, "Remove"));
                var point = ActualPoint(fixture.Window, disabled, requireEnabled: false);
                fixture.Window.MouseMove(point);
                fixture.Window.MouseDown(point, MouseButton.Left);
                fixture.Window.MouseUp(point, MouseButton.Left);
            }
            await Settle();
            for (int step = 0; step < 20; step++)
            {
                Key(fixture.Window, PhysicalKey.Tab);
                Assert.False(firstUp.IsFocused);
                Assert.False(lastDown.IsFocused);
            }
            Assert.Equal(before, QueueState(fixture.Session));
            Assert.Equal(0, structure);
            Assert.Equal(0, fixture.Exits);
        }
        finally { fixture.Session.StructuralChanged -= Count; }
    }

    [AvaloniaFact]
    public async Task EveryMoveAndRemoveNameIncludesItsCurrentTimerAndAction()
    {
        await using var fixture = await Fixture.Create();
        string before = QueueState(fixture.Session);
        foreach (var timer in fixture.Session.Timers)
            foreach (string purpose in new[] { "Move up", "Move down", "Remove" })
            {
                string name = Avalonia.Automation.AutomationProperties.GetName(RowCommand(fixture.Panel, timer, purpose)) ?? "";
                Assert.Contains(timer.Title, name, StringComparison.OrdinalIgnoreCase);
                foreach (string word in purpose.Split(' '))
                    Assert.Contains(word, name, StringComparison.OrdinalIgnoreCase);
            }
        Assert.Equal(before, QueueState(fixture.Session));
    }

    [AvaloniaTheory]
    [InlineData("Move up", "rebind")]
    [InlineData("Remove", "rebind")]
    [InlineData("Move up", "hide_then_show")]
    [InlineData("Remove", "hide_then_show")]
    public async Task SynchronousOwnerTransitionCannotRestoreRetiredCommandFocusOverAnotherInput(string purpose, string boundary)
    {
        await using var fixture = await Fixture.Create();
        var replacement = new TrainingSession();
        replacement.AddTimer(new TrainingTimer("Replacement current", TimeSpan.FromSeconds(70)));
        replacement.AddTimer(new TrainingTimer("Replacement next", TimeSpan.FromSeconds(80)));
        string replacementBefore = QueueState(replacement);
        var command = RowCommand(fixture.Panel, fixture.Target, purpose);
        await TabTo(fixture.Window, command);
        int callbacks = 0;
        void Interrupt()
        {
            fixture.Session.StructuralChanged -= Interrupt;
            callbacks++;
            if (boundary == "rebind") fixture.Panel.Bind(replacement);
            else fixture.Panel.IsVisible = false;
            // Actual Tab input to an already-mounted separate scope. This is
            // the interruption's user focus, not a post-action test Focus().
            for (int step = 0; step < 40 && !fixture.OtherScope.IsFocused; step++) Key(fixture.Window, PhysicalKey.Tab);
            Assert.True(fixture.OtherScope.IsFocused, "Interruption must establish real independent input focus.");
            if (boundary == "hide_then_show") fixture.Panel.IsVisible = true;
        }
        fixture.Session.StructuralChanged += Interrupt;
        try
        {
            await Activate(fixture.Window, command, false);
            Assert.Equal(1, callbacks);
            Assert.True(fixture.OtherScope.IsFocused, "Retired repair must not reclaim focus after the observer's real Tab input.");
            Assert.Equal("Unrelated draft", fixture.OtherScope.Text);
            Assert.Equal(replacementBefore, QueueState(replacement));
            if (purpose == "Remove") Assert.DoesNotContain(fixture.Target, fixture.Session.Timers);
            else Assert.Equal(new[] { fixture.Active.Id, fixture.Target.Id, fixture.Editor.Id, fixture.Tail.Id }, fixture.Session.Timers.Select(timer => timer.Id));
            Assert.Equal(0, fixture.Exits);
        }
        finally { fixture.Session.StructuralChanged -= Interrupt; }
    }
}

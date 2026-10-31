using Avalonia;
using Avalonia.Controls;
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

/// <summary>Production Training corrections, including mutation and keyboard interruption.</summary>
public sealed class TrainingCorrectionFlowTests
{
    private const string EditTip = "Edit title and duration";
    private const string SaveTip = "Save (Enter)";
    private const string CancelTip = "Cancel (Esc)";

    private static async Task Settle(int milliseconds = 100)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Button Action(TrainingControlPanel panel, string tip) =>
        Assert.Single(panel.GetVisualDescendants().OfType<Button>(), button => Equals(ToolTip.GetTip(button), tip));

    private static Button RowAction(TrainingControlPanel panel, TrainingTimer timer, string tip) =>
        Assert.Single(panel.GetVisualDescendants().OfType<Button>(), button =>
            Equals(ToolTip.GetTip(button), tip)
            && button.GetVisualAncestors().OfType<Border>().First().GetVisualDescendants()
                .OfType<TextBlock>().Any(text => text.Text == timer.Title));

    private static TextBox[] Inputs(TrainingControlPanel panel)
    {
        var save = Action(panel, SaveTip);
        var editor = save.GetVisualAncestors().OfType<Grid>()
            .First(grid => grid.GetVisualDescendants().OfType<TextBox>().Count() == 3);
        return editor.GetVisualDescendants().OfType<TextBox>().ToArray();
    }

    private static void AssertDraft(TrainingControlPanel panel, string title, string minutes, string seconds)
    {
        var inputs = Inputs(panel);
        Assert.Equal(title, inputs[0].Text);
        Assert.Equal(minutes, inputs[1].Text);
        Assert.Equal(seconds, inputs[2].Text);
    }

    private static void Key(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
    }

    private static void AssertInside(Window window, Control control, string reason)
    {
        Assert.True(control.IsEffectivelyVisible, reason + " must be visible");
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0, reason + " must have a hit area");
        var origin = control.TranslatePoint(default, window);
        Assert.NotNull(origin);
        Assert.True(new Rect(window.Bounds.Size).Contains(new Rect(origin.Value, control.Bounds.Size)),
            reason + " must fit the hosting window after keyboard reveal");
    }

    private static async Task RevealWithKeyboard(Window window, Control control)
    {
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        control.Focus(NavigationMethod.Tab);
        control.BringIntoView();
        window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        await Settle();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string? previousDataHome = Environment.GetEnvironmentVariable("ROCHE_DATA_HOME");
        private readonly string dataHome = Path.Combine(Path.GetTempPath(), "roche-training-corrections-" + Guid.NewGuid().ToString("N"));
        private readonly string recentPath;
        private readonly byte[]? previousRecentTimers;
        private readonly byte[]? previousRecentBackup;
        internal readonly TrainingSession Session = new();
        internal readonly TrainingTimer Active = new("Current drill", TimeSpan.FromMinutes(3));
        internal readonly TrainingTimer Target = new("Queued correction", TimeSpan.FromSeconds(125));
        internal readonly TrainingTimer Tail = new("Final drill", TimeSpan.FromSeconds(45));
        internal readonly TrainingControlPanel Panel;
        internal readonly NativeWindow Window;
        internal int Exits;

        internal Fixture(int width = 900, int height = 680)
        {
            Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", dataHome);
            recentPath = Path.Combine(SelfTestStorage.DocumentsDirectory, "training_recent_timers.json");
            previousRecentTimers = File.Exists(recentPath) ? File.ReadAllBytes(recentPath) : null;
            previousRecentBackup = File.Exists(recentPath + ".bak") ? File.ReadAllBytes(recentPath + ".bak") : null;
            Directory.CreateDirectory(Path.GetDirectoryName(recentPath)!);
            File.Delete(recentPath);
            File.Delete(recentPath + ".bak");
            RecentTrainingTimerStorage.Record("Repeat drill", 45);
            Session.AddTimer(Active);
            Session.AddTimer(Target);
            Session.AddTimer(Tail);
            Panel = new TrainingControlPanel();
            Panel.Bind(Session);
            Panel.ExitRequested += () => Exits++;
            Window = new NativeWindow { Width = width, Height = height, Content = Panel };
            Window.Show();
        }

        internal async Task Open(TrainingTimer timer)
        {
            await Settle();
            var edit = RowAction(Panel, timer, EditTip);
            await RevealWithKeyboard(Window, edit);
            Click(edit);
            await Settle();
            Assert.Equal(timer.Title, Inputs(Panel)[0].Text);
        }

        public void Dispose()
        {
            Session.Pause();
            Window.Close();
            if (previousRecentTimers is null) File.Delete(recentPath);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(recentPath)!);
                File.WriteAllBytes(recentPath, previousRecentTimers);
            }
            if (previousRecentBackup is null) File.Delete(recentPath + ".bak");
            else File.WriteAllBytes(recentPath + ".bak", previousRecentBackup);
            Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", previousDataHome);
            if (Directory.Exists(dataHome)) Directory.Delete(dataHome, true);
        }
    }

    [AvaloniaTheory]
    [InlineData("external append")]
    [InlineData("local append")]
    [InlineData("recent append")]
    [InlineData("upcoming reorder")]
    public async Task QueueMutationPreservesRawCorrectionSelectionAndRunningTimer(string mutation)
    {
        using var f = new Fixture();
        await f.Open(f.Target);
        var inputs = Inputs(f.Panel);
        inputs[0].Text = "Unsaved lane revision";
        inputs[1].Text = "01";
        inputs[2].Text = "009";
        inputs[0].Focus();
        inputs[0].CaretIndex = 9;
        inputs[0].SelectionStart = 3;
        inputs[0].SelectionEnd = 9;
        int caret = inputs[0].CaretIndex, selectionStart = inputs[0].SelectionStart, selectionEnd = inputs[0].SelectionEnd;
        Assert.NotEqual(selectionStart, selectionEnd);
        f.Session.Start();
        int structural = 0;
        f.Session.StructuralChanged += () => structural++;

        if (mutation == "external append") f.Session.AddTimer(new TrainingTimer("External addition", TimeSpan.FromSeconds(40)));
        else if (mutation == "local append")
        {
            f.Panel.FindControl<TextBox>("NewTitleBox")!.Text = "Local addition";
            f.Panel.FindControl<TextBox>("NewMinutesBox")!.Text = "0";
            f.Panel.FindControl<TextBox>("NewSecondsBox")!.Text = "40";
            Click(f.Panel.FindControl<Button>("AddTimerButton")!);
        }
        else if (mutation == "recent append")
        {
            var recent = Assert.Single(f.Panel.FindControl<WrapPanel>("RecentTimersPanel")!.Children.OfType<Button>());
            Click(recent);
        }
        else Click(RowAction(f.Panel, f.Tail, "Move up"));
        await Settle();

        AssertDraft(f.Panel, "Unsaved lane revision", "01", "009");
        var current = Inputs(f.Panel)[0];
        Assert.True(current.IsFocused);
        Assert.Equal(caret, current.CaretIndex);
        Assert.Equal(selectionStart, current.SelectionStart);
        Assert.Equal(selectionEnd, current.SelectionEnd);
        Assert.Equal(1, structural);
        Assert.Same(f.Active, f.Session.Active);
        Assert.True(f.Session.IsRunning);
        Assert.InRange(f.Session.ActiveRemaining.TotalSeconds, 170, 180);
        Assert.Equal("Queued correction", f.Target.Title);
        Assert.Equal(TimeSpan.FromSeconds(125), f.Target.Duration);
        if (mutation == "upcoming reorder") Assert.Equal(new[] { f.Active.Id, f.Tail.Id, f.Target.Id }, f.Session.Timers.Select(timer => timer.Id));
        else
        {
            Assert.Equal(4, f.Session.Timers.Count);
            Assert.Equal(mutation == "recent append" ? "Repeat drill" : mutation == "local append" ? "Local addition" : "External addition", f.Session.Timers[^1].Title);
            Assert.Equal(TimeSpan.FromSeconds(mutation == "recent append" ? 45 : 40), f.Session.Timers[^1].Duration);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TitleOnlySaveKeepsAcceptedExtraTimeAndTheTransportState(bool running)
    {
        using var f = new Fixture();
        await f.Open(f.Active);
        Inputs(f.Panel)[0].Text = "Corrected active title";
        if (running) f.Session.Start();
        Click(f.Panel.FindControl<Button>("AddTenSecButton")!);
        Assert.Equal(TimeSpan.FromSeconds(190), f.Active.Duration);
        Click(Action(f.Panel, SaveTip));
        await Settle();

        Assert.Equal("Corrected active title", f.Active.Title);
        Assert.Equal(TimeSpan.FromSeconds(190), f.Active.Duration);
        Assert.Same(f.Active, f.Session.Active);
        Assert.Equal(running, f.Session.IsRunning);
        Assert.InRange(f.Session.ActiveRemaining.TotalSeconds, 180, 190);
        Assert.DoesNotContain(f.Panel.GetVisualDescendants().OfType<Button>(), button => Equals(ToolTip.GetTip(button), SaveTip));
    }

    [AvaloniaFact]
    public async Task DurationOnlySaveKeepsAnExternalTitleCorrection()
    {
        using var f = new Fixture();
        await f.Open(f.Target);
        Inputs(f.Panel)[1].Text = "2";
        Inputs(f.Panel)[2].Text = "30";
        f.Session.UpdateTimer(f.Target, "Renamed externally", null);
        await Settle();
        Click(Action(f.Panel, SaveTip));
        await Settle();

        Assert.Equal("Renamed externally", f.Target.Title);
        Assert.Equal(TimeSpan.FromSeconds(150), f.Target.Duration);
        Assert.Equal(TimeSpan.FromSeconds(150), f.Target.Remaining);
        Assert.Same(f.Active, f.Session.Active);
        Assert.False(f.Session.IsRunning);
    }

    [AvaloniaTheory]
    [InlineData("title")]
    [InlineData("duration")]
    public async Task ConcurrentChangeToTheEditedFieldRejectsSaveWithoutChangingEitherModelField(string field)
    {
        using var f = new Fixture();
        await f.Open(f.Target);
        var draft = Inputs(f.Panel);
        draft[0].Text = field == "title" ? "My title correction" : f.Target.Title;
        draft[1].Text = "2";
        draft[2].Text = field == "duration" ? "30" : "05";
        if (field == "title") f.Session.UpdateTimer(f.Target, "External title correction", null);
        else f.Session.UpdateTimer(f.Target, null, TimeSpan.FromSeconds(140));
        await Settle();
        string acceptedTitle = f.Target.Title;
        var acceptedDuration = f.Target.Duration;
        var acceptedRemaining = f.Target.Remaining;
        int changes = 0;
        f.Session.Changed += () => changes++;
        f.Session.StructuralChanged += () => changes++;
        Click(Action(f.Panel, SaveTip));
        await Settle();

        Assert.Equal(acceptedTitle, f.Target.Title);
        Assert.Equal(acceptedDuration, f.Target.Duration);
        Assert.Equal(acceptedRemaining, f.Target.Remaining);
        Assert.Equal(0, changes);
        Assert.Equal(new[] { f.Active.Id, f.Target.Id, f.Tail.Id }, f.Session.Timers.Select(timer => timer.Id));
        AssertDraft(f.Panel, field == "title" ? "My title correction" : "Queued correction", "2", field == "duration" ? "30" : "05");
        var error = Assert.Single(f.Panel.GetVisualDescendants().OfType<TextBlock>(), text => Equals(text.Tag, "training-edit-error"));
        Assert.True(error.IsVisible);
        Assert.False(string.IsNullOrWhiteSpace(error.Text));
        Assert.Equal(0, f.Exits);
    }

    [AvaloniaTheory]
    [InlineData(SaveTip)]
    [InlineData(CancelTip)]
    public async Task EscapeFromCorrectionActionCancelsOnlyTheDraftAndReturnsToItsEditCommand(string tip)
    {
        using var f = new Fixture();
        await f.Open(f.Target);
        Inputs(f.Panel)[0].Text = "Abandoned draft";
        Inputs(f.Panel)[2].Text = "50";
        f.Session.Start();
        int changes = 0;
        f.Session.StructuralChanged += () => changes++;
        await RevealWithKeyboard(f.Window, Action(f.Panel, tip));
        Key(f.Window, PhysicalKey.Escape);
        await Settle();

        Assert.Equal(0, f.Exits);
        Assert.Equal(0, changes);
        Assert.Equal("Queued correction", f.Target.Title);
        Assert.Equal(TimeSpan.FromSeconds(125), f.Target.Duration);
        Assert.True(f.Session.IsRunning);
        Assert.Same(f.Active, f.Session.Active);
        Assert.DoesNotContain(f.Panel.GetVisualDescendants().OfType<Button>(), button => Equals(ToolTip.GetTip(button), SaveTip));
        Assert.True(RowAction(f.Panel, f.Target, EditTip).IsFocused);
    }

    [AvaloniaTheory]
    [InlineData(SaveTip, false)]
    [InlineData(CancelTip, false)]
    [InlineData(SaveTip, true)]
    [InlineData(CancelTip, true)]
    public async Task DetachedCorrectionCallbacksCannotMutateModelsOrClearTheNewEditor(string staleAction, bool rebind)
    {
        using var f = new Fixture();
        await f.Open(f.Target);
        Inputs(f.Panel)[0].Text = "Obsolete correction";
        Inputs(f.Panel)[2].Text = "50";
        var detached = Action(f.Panel, staleAction);
        Click(Action(f.Panel, CancelTip));
        await Settle();
        var current = f.Session;
        var currentTarget = f.Active;
        if (rebind)
        {
            current = new TrainingSession();
            currentTarget = new TrainingTimer("Replacement drill", TimeSpan.FromSeconds(70));
            current.AddTimer(currentTarget);
            f.Panel.Bind(current);
        }
        await f.Open(currentTarget);
        var inputs = Inputs(f.Panel);
        inputs[0].Text = "New editor must survive";
        inputs[1].Text = "03";
        inputs[2].Text = "007";
        int oldChanges = 0, currentChanges = 0;
        f.Session.StructuralChanged += () => oldChanges++;
        current.StructuralChanged += () => currentChanges++;
        Click(detached);
        await Settle();

        AssertDraft(f.Panel, "New editor must survive", "03", "007");
        Assert.Equal(0, oldChanges);
        Assert.Equal(0, currentChanges);
        Assert.Equal("Queued correction", f.Target.Title);
        Assert.Equal(TimeSpan.FromSeconds(125), f.Target.Duration);
        Assert.Equal(rebind ? "Replacement drill" : "Current drill", currentTarget.Title);
        Assert.Equal(TimeSpan.FromSeconds(rebind ? 70 : 180), currentTarget.Duration);
        Assert.Same(currentTarget, current.Active);
        Assert.Equal(0, f.Exits);
    }

    [AvaloniaTheory]
    [InlineData(380, 320)]
    [InlineData(420, 320)]
    public async Task CompactInvalidCorrectionKeepsBothActionsReachableAndKeyboardSaveAppliesOnce(int width, int height)
    {
        using var f = new Fixture(width, height);
        await f.Open(f.Target);
        var inputs = Inputs(f.Panel);
        inputs[0].Text = "Compact corrected drill";
        inputs[1].Text = "invalid pasted duration";
        inputs[1].Focus();
        int structural = 0;
        f.Session.StructuralChanged += () => structural++;
        Key(f.Window, PhysicalKey.Enter);
        await Settle();
        Assert.Equal(0, structural);
        Assert.Equal("Queued correction", f.Target.Title);
        Assert.Equal(TimeSpan.FromSeconds(125), f.Target.Duration);
        AssertDraft(f.Panel, "Compact corrected drill", "invalid pasted duration", "05");
        var error = Assert.Single(f.Panel.GetVisualDescendants().OfType<TextBlock>(), text => Equals(text.Tag, "training-edit-error"));
        AssertInside(f.Window, error, "Correction validation");
        foreach (string tip in new[] { CancelTip, SaveTip })
        {
            var button = Action(f.Panel, tip);
            await RevealWithKeyboard(f.Window, button);
            Assert.True(button.IsFocused);
            AssertInside(f.Window, button, tip);
        }
        inputs = Inputs(f.Panel);
        inputs[1].Text = "2";
        inputs[2].Text = "30";
        await RevealWithKeyboard(f.Window, Action(f.Panel, SaveTip));
        Key(f.Window, PhysicalKey.Space);
        await Settle();
        Assert.Equal(1, structural);
        Assert.Equal("Compact corrected drill", f.Target.Title);
        Assert.Equal(TimeSpan.FromSeconds(150), f.Target.Duration);
        Assert.Equal(0, f.Exits);
        Assert.False(f.Session.IsRunning);
        Assert.Same(f.Active, f.Session.Active);
    }
}

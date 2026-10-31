using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class OperatorRecoveryTests
{
    private static async Task Settle(int milliseconds = 250) { await Task.Delay(milliseconds); Dispatcher.UIThread.RunJobs(); }
    private static T Named<T>(Control view, string name) where T : Control => view.FindControl<T>(name)!;
    private static void Click(Control view, string name)
    { Named<Button>(view, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static void Capture(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        string path = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") ?? "/tmp/roche-batch-3-captures";
        Directory.CreateDirectory(path); using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(path, name + ".png"));
    }
    private static TrainingSession FinishingSession()
    {
        var session = new TrainingSession { AutoAdvance = false };
        session.AddTimer(new TrainingTimer("Finished interval", TimeSpan.Zero));
        session.AddTimer(new TrainingTimer("Next interval", TimeSpan.FromMinutes(2)));
        session.AddTimer(new TrainingTimer("Last interval", TimeSpan.FromMinutes(3)));
        return session;
    }

    [AvaloniaTheory]
    [InlineData("clear", "NO TIMERS")]
    [InlineData("rebind", "FRESH SESSION")]
    [InlineData("skip", "LAST INTERVAL")]
    [InlineData("remove", "LAST INTERVAL")]
    public void InterruptingFinishImmediatelyShowsTheCurrentQueue(string operation, string expected)
    {
        var session = FinishingSession(); var output = new TrainingScorebugControl(); output.Bind(session);
        var window = new NativeWindow { Width = 386, Height = 193, Content = output }; window.Show();
        try
        {
            session.Start(); session.Tick(); Assert.Equal("Next interval", session.Active!.Title);
            Assert.Equal("FINISHED INTERVAL", Named<TextBlock>(output, "TitleText").Text);
            if (operation == "clear") session.Clear();
            else if (operation == "skip") session.Skip();
            else if (operation == "remove") session.RemoveTimer(session.Active!);
            else { var fresh = new TrainingSession(); fresh.AddTimer(new TrainingTimer("Fresh session", TimeSpan.FromMinutes(1))); output.Bind(fresh); }
            Capture(window, "training-finish-" + operation);
            Assert.Equal(expected, Named<TextBlock>(output, "TitleText").Text);
            Assert.False(Named<Grid>(output, "StagedTimerHost").IsVisible);
            Assert.Equal(operation == "clear" ? "00:00" : operation == "rebind" ? "01:00" : "03:00", Named<TextBlock>(output, "TimerText").Text);
        }
        finally { output.ResetVisuals(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task ResetCancelsThePendingPromotionRatherThanFadingTheNewContentLater()
    {
        var session = FinishingSession(); var output = new TrainingScorebugControl(); output.Bind(session);
        var window = new NativeWindow { Width = 386, Height = 193, Content = output }; window.Show();
        try
        {
            session.Start(); session.Tick();
            typeof(TrainingScorebugControl).GetMethod("EndFlashAndPromoteStaged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(output, null);
            output.ResetVisuals();
            await Task.Delay(560); Dispatcher.UIThread.RunJobs();
            Assert.InRange(Named<StackPanel>(output, "ActiveTimerHost").Opacity, .999, 1);
        }
        finally { output.ResetVisuals(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("oops", "0")]
    [InlineData("-2", "0")]
    [InlineData("0", "0")]
    [InlineData("2147483647", "59")]
    public async Task InvalidDurationDoesNotCreateAnUnexpectedOneSecondTimer(string minutes, string seconds)
    {
        var session = new TrainingSession(); var panel = new TrainingControlPanel(); panel.Bind(session);
        var window = new NativeWindow { Width = 420, Height = 280, Content = panel }; window.Show();
        try
        {
            await Settle();
            Named<TextBox>(panel, "NewTitleBox").Text = "Long training name preserved for correction";
            Named<TextBox>(panel, "NewMinutesBox").Text = minutes; Named<TextBox>(panel, "NewSecondsBox").Text = seconds;
            var add = Named<Button>(panel, "AddTimerButton");
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None); add.Focus(NavigationMethod.Tab); add.BringIntoView();
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None); await Settle();
            Click(panel, "AddTimerButton");
            await Settle(); Capture(window, "training-invalid-" + minutes);
            Assert.Empty(session.Timers);
            var error = Named<TextBlock>(panel, "TrainingEntryError"); Assert.True(error.IsVisible); Assert.False(string.IsNullOrWhiteSpace(error.Text));
            Assert.Equal(minutes, Named<TextBox>(panel, "NewMinutesBox").Text);
            Named<TextBox>(panel, "NewMinutesBox").Text = "0"; Named<TextBox>(panel, "NewSecondsBox").Text = "90";
            Click(panel, "AddTimerButton");
            Assert.Equal(TimeSpan.FromSeconds(90), Assert.Single(session.Timers).Duration); Assert.False(error.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void UndoExplainsTheScorecardBoundaryAndBecomesAvailableAfterItIsReverted()
    {
        var match = CricketReliabilityChecks.CreateMatch(true); match.RecordDelivery(4, CricketDeliveryType.Four);
        var draft = match.CreateScorecardDraft(1); draft.TotalRuns = 5; draft.Batters[0].Runs = 5; draft.Bowlers[0].RunsConceded = 5;
        Assert.True(match.TryApplyScorecard(draft, out string error), error);
        var panel = new CricketControlPanel(); panel.SetMatch(match);
        var window = new NativeWindow { Width = 480, Height = 320, Content = panel }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); var undo = Named<Button>(panel, "UndoDeliveryButton");
            string before = JsonSerializer.Serialize(match.ToState());
            Capture(window, "cricket-undo-scorecard-boundary");
            Assert.False(undo.IsEnabled); Assert.Contains("scorecard", ToolTip.GetTip(undo)!.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.False(match.TryUndoLastScoringAction(match.WorkflowToken, out error)); Assert.Equal(before, JsonSerializer.Serialize(match.ToState()));
            Assert.True(match.TryRevertLatestScorecardCorrection(1, out error), error); Dispatcher.UIThread.RunJobs();
            Assert.True(undo.IsEnabled); Click(panel, "UndoDeliveryButton");
            Assert.Empty(match.CurrentInnings!.Deliveries); Assert.Equal(0, match.TotalRuns);
        }
        finally { panel.ClearMatch(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task DetachedOutputStopsFinishCallbacksAndReattachesExactlyOnce()
    {
        var session = FinishingSession(); var output = new TrainingScorebugControl(); output.Bind(session);
        var window = new NativeWindow { Title = "Training Display", Width = 386, Height = 193, Content = output }; window.Show();
        try
        {
            await Settle(); Assert.True(output.IsLoaded);
            session.Start(); session.Tick(); window.Content = null; window.UpdateLayout(); await Settle();
            Assert.Null(output.GetVisualRoot());
            Assert.False((bool)typeof(TrainingScorebugControl).GetField("_flashing", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(output)!);
            session.Active!.Remaining = TimeSpan.Zero; session.Start(); session.Tick();
            Assert.False((bool)typeof(TrainingScorebugControl).GetField("_flashing", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(output)!);
            Assert.Equal("Next interval", session.Active.Title); // No detached subscriber may advance the queue.
            session.Clear(); session.AddTimer(new TrainingTimer("Reattached", TimeSpan.Zero));
            session.AddTimer(new TrainingTimer("Exactly once", TimeSpan.FromMinutes(1)));
            window.Content = output; await Settle(); session.Start(); session.Tick();
            Assert.Equal("Exactly once", session.Active!.Title); Assert.Equal(1, session.ActiveIndex);
            session.Clear(); Assert.Equal("NO TIMERS", Named<TextBlock>(output, "TitleText").Text);
        }
        finally { output.ResetVisuals(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(700, 450)] [InlineData(420, 280)]
    public async Task InlineTimerEditRejectsInvalidInputAndKeepsCancelAndSaveReachable(int width, int height)
    {
        var session = new TrainingSession(); session.AddTimer(new TrainingTimer("Long interval name for correcting the duration without losing the original", TimeSpan.FromSeconds(125)));
        var panel = new TrainingControlPanel(); panel.Bind(session);
        var window = new NativeWindow { Width = width, Height = height, Content = panel }; window.Show();
        try
        {
            await Settle();
            var edit = panel.GetVisualDescendants().OfType<Button>().Single(button => Equals(ToolTip.GetTip(button), "Edit title and duration"));
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
            var inputs = panel.GetVisualDescendants().OfType<TextBox>().Where(input => string.IsNullOrEmpty(input.Name)).ToArray();
            Assert.Equal(3, inputs.Length); inputs[0].Text = "Corrected long interval title"; inputs[1].Text = "bad";
            inputs[1].Focus(); window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None); await Settle();
            Assert.Equal(TimeSpan.FromSeconds(125), session.Active!.Duration); Assert.StartsWith("Long interval", session.Active.Title);
            var error = panel.GetVisualDescendants().OfType<TextBlock>().Single(text => Equals(text.Tag, "training-edit-error"));
            Assert.True(error.IsVisible);
            var errorPoint = error.TranslatePoint(default, window)!.Value;
            Assert.True(new Rect(window.Bounds.Size).Contains(new Rect(errorPoint, error.Bounds.Size)), "Validation feedback must scroll into view after Enter");
            Capture(window, $"training-edit-validation-{width}x{height}");
            foreach (string tip in new[] { "Cancel (Esc)", "Save (Enter)" })
            {
                var button = panel.GetVisualDescendants().OfType<Button>().Single(b => Equals(ToolTip.GetTip(b), tip));
                window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None); button.Focus(NavigationMethod.Tab); button.BringIntoView();
                window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None); await Settle();
                var p = button.TranslatePoint(default, window)!.Value;
                Assert.True(new Rect(window.Bounds.Size).Contains(new Rect(p, button.Bounds.Size)), tip + " must remain reachable");
            }
            Capture(window, $"training-edit-error-{width}x{height}");
            inputs[1].Text = "2"; inputs[2].Text = "30"; inputs[1].Focus();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None); await Settle();
            Assert.Equal(TimeSpan.FromSeconds(150), session.Active.Duration); Assert.Equal("Corrected long interval title", session.Active.Title);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task GlobalCricketTabShortcutCannotNavigateBehindAnOpenDecision()
    {
        var app = (App)Application.Current!; app.IsSelfTestMode = true;
        var window = new Roche_Scoreboard.MainWindow(); var match = CricketReliabilityChecks.CreateMatch(true);
        var panel = window.FindControl<CricketControlPanel>("CricketControlPanelView")!;
        try
        {
            window.Show(); await Settle(1000);
            window.FindControl<SportSelectionScreen>("SportSelectionPanel")!.IsVisible = false;
            typeof(Roche_Scoreboard.MainWindow).GetField("_sportMode", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, SportMode.Cricket);
            panel.IsVisible = true; panel.SetMatch(match); await Settle();
            Click(panel, "MatchOptionsButton"); await Settle();
            panel.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "outcome")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
            var note = panel.GetVisualDescendants().OfType<TextBox>().Single(input => Equals(input.Tag, "outcome-note"));
            note.Text = "Keep this unfinished decision"; note.Focus();
            int beforeTab = Named<TabControl>(panel, "MainTabControl").SelectedIndex; string beforeState = JsonSerializer.Serialize(match.ToState());
            window.KeyPressQwerty(PhysicalKey.Digit3, RawInputModifiers.Control); window.KeyReleaseQwerty(PhysicalKey.Digit3, RawInputModifiers.None); await Settle();
            Assert.Equal(beforeTab, Named<TabControl>(panel, "MainTabControl").SelectedIndex);
            Assert.Equal("Keep this unfinished decision", note.Text); Assert.True(note.IsFocused);
            Capture(window, "cricket-decision-keyboard-guard");
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None); await Settle();
            Assert.Equal(beforeState, JsonSerializer.Serialize(match.ToState())); Assert.False(match.IsMatchFinalized);
            window.KeyPressQwerty(PhysicalKey.Digit3, RawInputModifiers.Control); window.KeyReleaseQwerty(PhysicalKey.Digit3, RawInputModifiers.None); await Settle();
            Assert.Equal(2, Named<TabControl>(panel, "MainTabControl").SelectedIndex);
        }
        finally { panel.ClearMatch(); window.Close(); app.IsSelfTestMode = false; }
    }

    [AvaloniaTheory]
    [InlineData(1280, 850)] [InlineData(420, 320)]
    public async Task AflCommandsRemainReachableAndKeyboardActivationDoesNotAlsoStartTheClock(int width, int height)
    {
        var app = (App)Application.Current!; app.IsSelfTestMode = true;
        var window = new Roche_Scoreboard.MainWindow();
        async Task Tap(Control target, int wait = 400)
        {
            target.BringIntoView(); await Settle(100);
            Assert.True(target.IsEffectivelyVisible, target.Name + " must be visible before pointer activation");
            target.BringIntoView(); Dispatcher.UIThread.RunJobs();
            var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); await Settle(wait);
        }
        try
        {
            window.Show(); window.Width = 1280; window.Height = 850; await Settle(1600);
            var launcher = window.FindControl<SportSelectionScreen>("SportSelectionPanel")!;
            await Tap(launcher.FindControl<Border>("AflCard")!, 1000);
            var wizard = window.FindControl<SetupWizard>("SetupWizardPanel")!;
            await Tap(wizard.FindControl<Control>("NextButton")!, 300); // Sport is step one of the unified wizard.
            wizard.FindControl<TextBox>("SetupHomeNameBox")!.Text = "Roche Display Club with a particularly long name";
            wizard.FindControl<TextBox>("SetupAwayNameBox")!.Text = "Visiting team with an equally long club name";
            await Tap(wizard.FindControl<Control>("NextButton")!, 700);
            await Tap(wizard.FindControl<Control>("NextButton")!, 700);
            wizard.FindControl<ComboBox>("SetupWeatherLocation")!.Text = "";
            await Tap(wizard.FindControl<Control>("NextButton")!, 900);
            window.Width = width; window.Height = height; await Settle();
            var match = (MatchManager)typeof(Roche_Scoreboard.MainWindow).GetField("_match", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            Assert.True(OperatorFeedback.IsOperatorWindow(window)); // A team name is not a window-role discriminator.
            foreach (string name in new[] { "HomeGoalButton", "UndoButton" })
            {
                var button = Named<Button>(window, name);
                window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None); button.Focus(NavigationMethod.Tab); button.BringIntoView();
                window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None); await Settle();
                var position = button.TranslatePoint(default, window)!.Value;
                Assert.True(new Rect(window.Bounds.Size).Contains(new Rect(position, button.Bounds.Size)), name + " must be reachable");
                window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None); await Settle();
                Assert.False(match.ClockRunning);
                Assert.Equal(name == "HomeGoalButton" ? "1.0.6" : "0.0.0", Named<TextBlock>(window, "HomeScoreDisplay").Text);
            }
            Capture(window, $"afl-keyboard-{width}x{height}");
        }
        finally { window.Close(); await Settle(150); app.IsSelfTestMode = false; }
    }
}

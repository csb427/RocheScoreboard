using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class ExceptionalCricketInteractionTests
{
    private static async Task Settle() { await Task.Delay(350); Dispatcher.UIThread.RunJobs(); }
    private static Button Tagged(Control root, string tag) => root.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Tag, tag));
    private static async Task Tap(Window window, Button button)
    {
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None); button.Focus(NavigationMethod.Tab);
        button.BringIntoView(); Dispatcher.UIThread.RunJobs(); window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        await Settle();
        var position = button.TranslatePoint(default, window)!.Value;
        Assert.True(new Rect(window.Bounds.Size).Contains(new Rect(position, button.Bounds.Size)), button.Content + " must be inside the window after focus/scroll");
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs(); await Settle();
    }
    private static async Task OpenOptions(Window window, CricketControlPanel panel)
        => await Tap(window, panel.FindControl<Button>("MatchOptionsButton")!);
    private static void Capture(Window window, string name)
    {
        string directory = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") ?? "/tmp/roche-batch-2-captures";
        Directory.CreateDirectory(directory); using var image = window.CaptureRenderedFrame(); image!.Save(Path.Combine(directory, name + ".png"));
    }

    [AvaloniaTheory]
    [InlineData(700, 450)] [InlineData(420, 280)]
    public async Task ExplicitOutcomeAndReopenWorkWithPointerKeyboardScrollingAndLongNotes(int width, int height)
    {
        var match = CricketReliabilityChecks.CreateMatch(true); var panel = new CricketControlPanel(); panel.SetMatch(match);
        var window = new NativeWindow { Width = width, Height = height, Content = panel }; window.Show();
        try
        {
            await Settle(); await OpenOptions(window, panel); await Tap(window, Tagged(panel, "outcome"));
            var note = panel.GetVisualDescendants().OfType<TextBox>().Single(input => Equals(input.Tag, "outcome-note"));
            note.Text = "Officials confirmed — " + new string('N', 200);
            var noResult = Tagged(panel, "no-result"); noResult.Focus(NavigationMethod.Tab); noResult.BringIntoView(); await Settle();
            Capture(window, $"outcome-form-{width}x{height}");
            await Tap(window, noResult); Assert.True(match.IsMatchFinalized); Assert.Equal("No result", match.MatchResult);
            Assert.Equal("NO RESULT", panel.FindControl<TextBlock>("WorkflowStatus")!.Text);
            Assert.False(panel.FindControl<Button>("UndoDeliveryButton")!.IsEnabled);
            await OpenOptions(window, panel); await Tap(window, Tagged(panel, "outcome")); await Tap(window, Tagged(panel, "reopen"));
            await Tap(window, Tagged(panel, "confirm-reopen")); Assert.False(match.IsMatchFinalized); Assert.Equal("", match.ResultNote);
        }
        finally { panel.ClearMatch(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task BeforeDeliveryActionAppearsInHistoryAndToolbarUndoRestoresItsCheckpoint()
    {
        var match = CricketReliabilityChecks.CreateMatch(true); var panel = new CricketControlPanel(); panel.SetMatch(match);
        var window = new NativeWindow { Width = 700, Height = 450, Content = panel }; window.Show();
        try
        {
            await Settle(); await OpenOptions(window, panel); await Tap(window, Tagged(panel, "before-delivery"));
            Capture(window, "before-delivery-confirmation"); await Tap(window, Tagged(panel, "record-before-delivery"));
            Assert.Equal(1, match.TotalWickets); Assert.Empty(match.CurrentInnings!.Deliveries);
            Assert.Contains(panel.GetVisualDescendants().OfType<StackPanel>(), node => Equals(node.Tag, "non-delivery-history"));
            match.SelectNewBatter(2); await Settle();
            await Tap(window, panel.FindControl<Button>("UndoDeliveryButton")!);
            Assert.Equal(0, match.TotalWickets); Assert.Equal(1, match.CurrentInnings.NonStrikerIndex);
        }
        finally { panel.ClearMatch(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task NonStrikerChangeDuringConfirmationRejectsTheOldActionWithoutMutation()
    {
        var match = CricketReliabilityChecks.CreateMatch(true); var panel = new CricketControlPanel(); panel.SetMatch(match);
        var window = new NativeWindow { Width = 700, Height = 450, Content = panel }; window.Show();
        try
        {
            await Settle(); await OpenOptions(window, panel); await Tap(window, Tagged(panel, "before-delivery"));
            match.SetNonStriker(2); string before = JsonSerializer.Serialize(match.ToState()); int revision = match.DeliveryRevision;
            await Tap(window, Tagged(panel, "record-before-delivery"));
            Assert.Equal(before, JsonSerializer.Serialize(match.ToState())); Assert.Equal(revision, match.DeliveryRevision);
            Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("match changed", StringComparison.OrdinalIgnoreCase) == true);
        }
        finally { panel.ClearMatch(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(CricketMatchResultKind.Abandoned, "ABANDONED")]
    [InlineData(CricketMatchResultKind.NoResult, "NO RESULT")]
    public async Task CompactOutputKeepsTheOutcomeVisibleEvenWhenTheTossWasPending(CricketMatchResultKind kind, string label)
    {
        var match = CricketReliabilityChecks.CreateMatch(true); match.TossPending = true; match.StartMatch();
        Assert.True(match.TrySetMatchOutcome(kind, "No play", match.WorkflowToken, out _));
        var output = new CricketScorebugControl(); output.SetOverlayCompact(true);
        var window = new NativeWindow { Width = 386, Height = 193, Content = output }; window.Show();
        try
        {
            output.UpdateFromMatch(match); await Task.Delay(800); Dispatcher.UIThread.RunJobs();
            Assert.Equal(("MATCH", label), CricketScorebugControl.SelectRailContext(match, 0));
            Assert.Equal(label, output.FindControl<TextBlock>("InfoValue")!.Text);
            Assert.False(output.FindControl<Grid>("PregameScreen")!.IsVisible);
            Capture(window, "compact-outcome-" + kind);
        }
        finally { output.Dispose(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task MainWindowQueuesAnEndedMatchIntoTheActualRecoveryWriter()
    {
        string directory = Path.Combine(Path.GetTempPath(), "roche-ended-recovery-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "ended.json"); var match = CricketReliabilityChecks.CreateMatch(true);
        Assert.True(match.TrySetMatchOutcome(CricketMatchResultKind.Abandoned, "Officials stopped play", match.WorkflowToken, out _));
        var host = new MainWindow(); var writer = new CricketRecoveryWriter(file);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        try
        {
            typeof(MainWindow).GetField("_cricketMatch", flags)!.SetValue(host, match);
            typeof(MainWindow).GetField("_cricketRecoveryWriter", flags)!.SetValue(host, writer);
            typeof(MainWindow).GetMethod("QueueCricketGameStateSave", flags)!.Invoke(host, null);
            Assert.True(await Task.Run(() => writer.Flush(TimeSpan.FromSeconds(4)))); Assert.True(File.Exists(file));
            var saved = (GameState)typeof(GameStateService).GetMethod("TryLoad", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { file })!;
            Assert.NotNull(saved);
            Assert.Equal(CricketMatchResultKind.Abandoned, saved.CricketMatchState!.ResultKind);
            Assert.NotNull(saved.CricketMatchState.ClosureBefore); Assert.Equal("Officials stopped play", saved.CricketMatchState.ResultNote);
            var restored = new CricketMatchManager(); restored.LoadState(saved.CricketMatchState);
            Assert.True(restored.TryReopenMatch(restored.WorkflowToken, out string error), error);
        }
        finally { host.Close(); writer.Clear(); Directory.Delete(directory, true); }
    }
}

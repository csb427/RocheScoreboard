using System.Text.Json;
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

public sealed class RefinementInteractionTests
{
    private static T Named<T>(Control view, string name) where T : Control => view.FindControl<T>(name)!;
    private static void Click(Control view, string name)
    { Named<Button>(view, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static void Choose(Control view, string name, int index)
    { Named<ComboBox>(view, name).SelectedIndex = index; Dispatcher.UIThread.RunJobs(); }

    [AvaloniaTheory]
    [InlineData(700, 450)]
    [InlineData(420, 280)]
    public async Task WideStumpingEntryShowsPenaltyAllowsPointerCommitAndKeepsCancelReachable(int width, int height)
    {
        var match = CricketReliabilityChecks.CreateMatch(true);
        match.CurrentInnings!.BattingOrder[0].FirstName = "A very long batter name for a small laptop window";
        var view = new CricketDeliveryDialog(match);
        var window = new NativeWindow { Width = width, Height = height, Content = view };
        window.Show();
        try
        {
            await Task.Delay(150); Dispatcher.UIThread.RunJobs();
            Choose(view, "DismissalChoice", 4); // Stumped
            Choose(view, "RunOutChoice", (int)CricketWicketRuns.Wide);
            await Task.Delay(150); Dispatcher.UIThread.RunJobs();
            Assert.True(Named<Button>(view, "ApplyButton").IsEnabled);
            Assert.Contains("+1 wide", Named<TextBlock>(view, "PreviewText").Text);
            Assert.False(Named<TextBlock>(view, "WicketRuleHint").IsVisible);
            foreach (string name in new[] { "CancelButton", "ApplyButton" })
            {
                var button = Named<Button>(view, name); var p = button.TranslatePoint(default, window)!.Value;
                Assert.True(button.IsEffectivelyVisible);
                Assert.True(new Rect(window.Bounds.Size).Contains(new Rect(p, button.Bounds.Size)), name + " must be fully reachable");
            }
            string directory = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") ?? "/tmp/roche-refinement-captures";
            Directory.CreateDirectory(directory);
            using (var image = window.CaptureRenderedFrame()) image!.Save(Path.Combine(directory, $"wide-stumping-{width}x{height}.png"));
            var commit = Named<Button>(view, "ApplyButton");
            var point = commit.TranslatePoint(new Point(commit.Bounds.Width / 2, commit.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.Single(match.CurrentInnings.Deliveries);
            Assert.Equal(DismissalType.Stumped, match.CurrentInnings.Deliveries[0].Dismissal);
            Assert.Equal(1, match.TotalRuns); Assert.Equal(1, match.TotalWickets);
        }
        finally { view.Cancel(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task RunOutCrossingIsKeyboardReachableAndUpdatesTheNextStrikerPreview()
    {
        var match = CricketReliabilityChecks.CreateMatch(true);
        var view = new CricketDeliveryDialog(match);
        var window = new NativeWindow { Width = 420, Height = 320, Content = view }; window.Show();
        try
        {
            await Task.Delay(150); Dispatcher.UIThread.RunJobs(); Choose(view, "DismissalChoice", 3);
            var crossed = Named<RadioButton>(view, "RunOutCrossed");
            Assert.True(crossed.IsEffectivelyVisible);
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            crossed.Focus(NavigationMethod.Tab); crossed.BringIntoView(); Dispatcher.UIThread.RunJobs();
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            var p = crossed.TranslatePoint(default, window)!.Value;
            Assert.True(new Rect(window.Bounds.Size).Intersects(new Rect(p, crossed.Bounds.Size)));
            crossed.IsChecked = true; Dispatcher.UIThread.RunJobs();
            Assert.Contains("Next on strike: Player 2", Named<TextBlock>(view, "PreviewText").Text);
            Click(view, "ApplyButton"); Assert.True(match.CurrentInnings!.Deliveries[0].RunOutCrossed);
            Assert.Equal(1, match.CurrentInnings.StrikerIndex);
            match.SelectNewBatter(2); Assert.Equal(1, match.CurrentInnings.StrikerIndex);
        }
        finally { view.Cancel(); window.Close(); }
    }

    [AvaloniaFact]
    public void NonStrikerChangeInvalidatesAnAlreadyOpenWicketEntry()
    {
        var match = CricketReliabilityChecks.CreateMatch(true); var view = new CricketDeliveryDialog(match);
        var window = new NativeWindow { Width = 700, Height = 450, Content = view }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); match.SetNonStriker(2); string before = JsonSerializer.Serialize(match.ToState());
            Click(view, "ApplyButton");
            Assert.Equal(before, JsonSerializer.Serialize(match.ToState()));
            Assert.Contains("match changed", Named<TextBlock>(view, "ErrorText").Text, StringComparison.OrdinalIgnoreCase);
        }
        finally { view.Cancel(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task CorrectionConfirmationRechecksStateAfterTheOperatorAnswers()
    {
        var match = CricketReliabilityChecks.CreateMatch(true); match.RecordDelivery(0, CricketDeliveryType.Dot);
        var view = new CricketDeliveryDialog(match, match.CurrentInnings!.Deliveries[0]);
        var window = new NativeWindow { Width = 700, Height = 450, Content = view }; window.Show();
        try
        {
            await Task.Delay(150); Dispatcher.UIThread.RunJobs(); Choose(view, "BatterChoice", 1);
            Click(view, "ApplyButton");
            var answer = view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Tag, "ball"));
            match.SetNonStriker(2); string before = JsonSerializer.Serialize(match.ToState());
            answer.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(500); Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, JsonSerializer.Serialize(match.ToState()));
            Assert.Contains("changed during confirmation", Named<TextBlock>(view, "ErrorText").Text);
        }
        finally { view.Cancel(); window.Close(); }
    }

    [AvaloniaFact]
    public void InvalidWideDismissalExplainsTheDisabledApplyAndFreeHitStillRestrictsStumping()
    {
        var match = CricketReliabilityChecks.CreateMatch(true); match.FreeHitsEnabled = true;
        match.RecordDelivery(1, CricketDeliveryType.NoBall);
        var view = new CricketDeliveryDialog(match);
        Choose(view, "RunOutChoice", (int)CricketWicketRuns.Wide);
        Choose(view, "DismissalChoice", 4);
        Assert.False(Named<Button>(view, "ApplyButton").IsEnabled);
        Assert.True(Named<TextBlock>(view, "WicketRuleHint").IsVisible);
        Assert.True(Named<TextBlock>(view, "FreeHitEntryHint").IsVisible);
        Choose(view, "DismissalChoice", 3);
        Assert.True(Named<Button>(view, "ApplyButton").IsEnabled);
        view.Cancel();
    }
}

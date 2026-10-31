using System.Collections.ObjectModel;
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
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class CricketWizardInterruptionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static object? Invoke(object owner, string name, params object?[] args) =>
        owner.GetType().GetMethod(name, Private)!.Invoke(owner, args);

    private static async Task Settle(int milliseconds = 100)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private sealed class Fixture : IDisposable
    {
        private readonly bool glass = OperatorAppearance.GlassEnabled;
        private readonly bool reduced = OperatorAppearance.ReduceMotion;
        private readonly bool? animation = OperatorMotion.AnimationOverride;
        private readonly byte[]? appearance = File.Exists(OperatorAppearance.StoragePath)
            ? File.ReadAllBytes(OperatorAppearance.StoragePath) : null;

        internal CricketSetupWizard Wizard { get; }
        internal NativeWindow Window { get; }
        internal int Cancellations { get; private set; }
        internal int Completions { get; private set; }

        internal Fixture(int width = 900, int height = 700)
        {
            OperatorMotion.AnimationOverride = null;
            OperatorAppearance.Configure(true, true);
            Wizard = new CricketSetupWizard();
            Wizard.Reset();
            Invoke(Wizard, "EnableUnifiedSetup");
            Wizard.CancelSetupRequested += () =>
            {
                ++Cancellations;
                // Match the real unified owner: whole-flow cancellation hides the wizard.
                Wizard.IsVisible = false;
            };
            Wizard.SetupCompleted += _ => ++Completions;
            Window = new NativeWindow { Width = width, Height = height, Content = Wizard };
            Window.Show();
        }

        internal T Named<T>(string name) where T : Control =>
            Assert.IsAssignableFrom<T>(Wizard.FindControl<T>(name));

        internal T Tagged<T>(object tag) where T : Control =>
            Assert.Single(Wizard.GetVisualDescendants().OfType<T>(), control => Equals(control.Tag, tag));

        internal ObservableCollection<CricketPlayer> Players(bool home) =>
            (ObservableCollection<CricketPlayer>)typeof(CricketSetupWizard)
                .GetField(home ? "_homePlayers" : "_awayPlayers", Private)!.GetValue(Wizard)!;

        internal void Step(int number) =>
            Named<RadioButton>($"Step{number}Navigation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        internal async Task<Button> OpenPlayer(bool home, bool edit = false)
        {
            Step(2);
            await Settle();
            var source = Tagged<Button>((edit ? "edit-" : "add-") + (home ? "home-player" : "away-player"));
            source.Focus(NavigationMethod.Tab);
            source.BringIntoView();
            await Settle(40);
            Click(source);
            await Settle();
            Assert.Single(Wizard.GetVisualDescendants().OfType<Border>(),
                border => Equals(border.Tag, "cricket-decision-sheet"));
            return source;
        }

        internal void Escape()
        {
            Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        }

        internal void AssertWithin(Control control, Control container)
        {
            var controlBounds = new Rect(control.TranslatePoint(default, Window)!.Value, control.Bounds.Size);
            var containerBounds = new Rect(container.TranslatePoint(default, Window)!.Value, container.Bounds.Size);
            Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0,
                $"{control.Name ?? control.Tag?.ToString()} must have a rendered target.");
            Assert.True(containerBounds.Contains(controlBounds),
                $"{control.Name ?? control.Tag?.ToString()} bounds {controlBounds} must fit inside {containerBounds}.");
        }

        public void Dispose()
        {
            Window.Close();
            OperatorMotion.AnimationOverride = animation;
            OperatorAppearance.Configure(glass, reduced);
            if (appearance is null) File.Delete(OperatorAppearance.StoragePath);
            else File.WriteAllBytes(OperatorAppearance.StoragePath, appearance);
        }
    }

    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task EscapeInAPlayerSheetCancelsOnlyThatDraftAndReturnsToItsSource(bool home, bool edit)
    {
        using var f = new Fixture();
        await Settle();
        f.Named<TextBox>("SetupHomeNameBox").Text = "Keep the home team";
        f.Named<TextBox>("SetupAwayNameBox").Text = "Keep the away team";
        var player = new CricketPlayer { FirstName = "Existing", LastName = "Player", IsCaptain = true };
        if (edit) f.Players(home).Add(player);
        string homeBefore = JsonSerializer.Serialize(f.Players(true));
        string awayBefore = JsonSerializer.Serialize(f.Players(false));
        var source = await f.OpenPlayer(home, edit);
        var first = f.Tagged<TextBox>("new-player-first");
        first.Focus(NavigationMethod.Tab);
        first.SelectAll();
        f.Window.KeyTextInput("Uncommitted");
        Assert.Equal("Uncommitted", first.Text);

        f.Escape();
        await Settle(250);

        Assert.Equal(0, f.Cancellations);
        Assert.True(f.Wizard.IsVisible);
        Assert.True(f.Named<Control>("Step3Panel").IsVisible);
        Assert.DoesNotContain(f.Wizard.GetVisualDescendants().OfType<Border>(),
            border => Equals(border.Tag, "cricket-decision-sheet"));
        Assert.Equal(homeBefore, JsonSerializer.Serialize(f.Players(true)));
        Assert.Equal(awayBefore, JsonSerializer.Serialize(f.Players(false)));
        Assert.Equal("Keep the home team", f.Named<TextBox>("SetupHomeNameBox").Text);
        Assert.Equal("Keep the away team", f.Named<TextBox>("SetupAwayNameBox").Text);
        Assert.True(source.IsKeyboardFocusWithin,
            $"Sheet cancellation must return to its source, not cancel setup: {f.Window.FocusManager?.GetFocusedElement()}.");
        Assert.Equal(0, f.Completions);
        Assert.Null(f.Wizard.Result);
    }

    [AvaloniaTheory]
    [InlineData("bad", "20")]
    [InlineData("0", "20")]
    [InlineData("37", "37")]
    public async Task EscapeInOversRestoresTheAcceptedValueWithoutCancelingSetup(string draft, string restored)
    {
        using var f = new Fixture();
        await Settle();
        f.Step(3);
        await Settle();
        var overs = f.Named<TextBox>("OversDisplay");
        overs.Focus(NavigationMethod.Tab);
        overs.BringIntoView();
        await Settle(40);
        overs.SelectAll();
        f.Window.KeyTextInput(draft);
        Assert.Equal(draft, overs.Text);

        f.Escape();
        await Settle();

        Assert.Equal(0, f.Cancellations);
        Assert.True(f.Wizard.IsVisible);
        Assert.True(f.Named<Control>("Step3bPanel").IsVisible);
        Assert.Equal(restored, overs.Text);
        Assert.Equal(0, f.Completions);
        Assert.Null(f.Wizard.Result);
    }

    [AvaloniaFact]
    public async Task EscapeOutsideALocalEditorStillCancelsTheUnifiedWizardOnce()
    {
        using var f = new Fixture();
        await Settle();
        var next = f.Named<Button>("NextButton");
        next.Focus(NavigationMethod.Tab);
        f.Escape();
        await Settle();

        Assert.Equal(1, f.Cancellations);
        Assert.False(f.Wizard.IsVisible);
        Assert.Equal(0, f.Completions);
        Assert.Null(f.Wizard.Result);
    }

    [AvaloniaTheory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task VisibleResetRejectsOutstandingPlayerApplyAndAllowsANewDraft(bool home, bool resetBeforeApply)
    {
        using var f = new Fixture();
        await Settle();
        f.Named<TextBox>("SetupHomeNameBox").Text = "Previous setup";
        var source = await f.OpenPlayer(home);
        f.Tagged<TextBox>("new-player-first").Text = "Stale player";
        await Settle(40);
        var staleApply = f.Tagged<Button>("apply");
        Assert.True(staleApply.IsEnabled);

        if (!resetBeforeApply) Click(staleApply);
        // Reset happens while visible, including the dispatcher gap after accepted Apply.
        f.Wizard.Reset();
        if (resetBeforeApply) Click(staleApply);
        await Settle(250);

        Assert.Empty(f.Players(true));
        Assert.Empty(f.Players(false));
        Assert.True(f.Wizard.IsVisible);
        Assert.True(f.Named<Control>("TeamsStepContainer").IsVisible);
        Assert.DoesNotContain(f.Wizard.GetVisualDescendants().OfType<Border>(),
            border => Equals(border.Tag, "cricket-decision-sheet"));
        Assert.Equal("", f.Named<TextBox>("SetupHomeNameBox").Text);
        Assert.Equal("20", f.Named<TextBox>("OversDisplay").Text);
        Assert.Equal(0, f.Cancellations);
        Assert.Equal(0, f.Completions);
        Assert.Null(f.Wizard.Result);
        Assert.False(source.IsKeyboardFocusWithin);

        await f.OpenPlayer(home);
        f.Tagged<TextBox>("new-player-first").Text = "Current player";
        await Settle(40);
        Click(f.Tagged<Button>("apply"));
        await Settle(250);
        Assert.Equal("Current player", Assert.Single(f.Players(home)).FirstName);
        Assert.Empty(f.Players(!home));
        Assert.DoesNotContain(f.Wizard.GetVisualDescendants().OfType<Border>(),
            border => Equals(border.Tag, "cricket-decision-sheet"));
    }

    [AvaloniaTheory]
    [InlineData(900, 700, "bad")]
    [InlineData(420, 320, "0")]
    [InlineData(420, 320, "101")]
    public async Task RejectedFinishRevealsAndExplainsTheHiddenOversDraftWithoutLosingSetup(
        int width, int height, string draft)
    {
        using var f = new Fixture(width, height);
        await Settle();
        f.Named<TextBox>("SetupHomeNameBox").Text = "Retained home draft";
        f.Named<TextBox>("SetupAwayNameBox").Text = "Retained away draft";
        f.Named<CheckBox>("ReplaceWideBallsToggle").IsChecked = false;
        f.Named<CheckBox>("FreeHitsToggle").IsChecked = true;
        f.Step(3);
        await Settle();
        var overs = f.Named<TextBox>("OversDisplay");
        overs.Focus(NavigationMethod.Tab);
        overs.BringIntoView();
        await Settle(40);
        overs.SelectAll();
        f.Window.KeyTextInput(draft);
        Assert.Equal(draft, overs.Text);
        f.Step(4);
        await Settle();
        Assert.False(f.Named<Control>("Step3bPanel").IsVisible);
        var next = f.Named<Button>("NextButton");
        next.Focus(NavigationMethod.Tab);
        Click(next);
        await Settle(250);

        Assert.Equal(0, f.Cancellations);
        Assert.Equal(0, f.Completions);
        Assert.Null(f.Wizard.Result);
        Assert.True(f.Wizard.IsVisible);
        Assert.True(f.Named<Control>("Step3bPanel").IsVisible);
        Assert.Equal(draft, overs.Text);
        Assert.True(overs.IsKeyboardFocusWithin,
            $"Rejected finish must focus the visible overs edit: {f.Window.FocusManager?.GetFocusedElement()}.");
        var error = Assert.Single(f.Wizard.GetVisualDescendants().OfType<TextBlock>(), text =>
            text.IsEffectivelyVisible && (text.Text?.Contains("whole number", StringComparison.OrdinalIgnoreCase) == true
                || text.Text?.Contains("1 to 100", StringComparison.OrdinalIgnoreCase) == true));
        var viewport = Assert.Single(overs.GetVisualAncestors().OfType<WizardViewport>());
        f.AssertWithin(overs, viewport);
        f.AssertWithin(error, viewport);
        f.AssertWithin(next, f.Wizard);
        Assert.Equal("Retained home draft", f.Named<TextBox>("SetupHomeNameBox").Text);
        Assert.Equal("Retained away draft", f.Named<TextBox>("SetupAwayNameBox").Text);
        Assert.False(f.Named<CheckBox>("ReplaceWideBallsToggle").IsChecked);
        Assert.True(f.Named<CheckBox>("FreeHitsToggle").IsChecked);

        overs.SelectAll();
        f.Window.KeyTextInput("37");
        f.Step(4);
        Click(next);
        await Settle(250);
        Assert.Equal(1, f.Completions);
        var result = Assert.IsType<CricketSetupResult>(f.Wizard.Result);
        Assert.Equal(37, result.TotalOvers);
        Assert.Equal("Retained home draft", result.TeamAName);
        Assert.Equal("Retained away draft", result.TeamBName);
        Assert.False(result.ReplaceWideBalls);
        Assert.True(result.FreeHitsEnabled);
        Assert.Equal("", result.TossWinner);
        Click(next);
        Assert.Equal(1, f.Completions);
    }
}

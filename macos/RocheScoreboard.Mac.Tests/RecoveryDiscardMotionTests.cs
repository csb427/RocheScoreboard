using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Roche_Scoreboard;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

// Prepared outside the repository. No production handler/timer/private state is
// invoked or forced. Reflection below reads the real confirmation timer only.
public sealed class RecoveryDiscardMotionTests
{
    private static async Task Settle(int milliseconds = 80)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task AtElapsed(Stopwatch watch, int milliseconds)
    {
        while (true)
        {
            long remaining = milliseconds - watch.ElapsedMilliseconds;
            if (remaining <= 0) return;
            await Settle((int)Math.Min(80, remaining));
        }
    }

    private sealed class AppearanceScope : IDisposable
    {
        private readonly bool glass = OperatorAppearance.GlassEnabled;
        private readonly bool reduced = OperatorAppearance.ReduceMotion;
        private readonly OperatorMotionStyle style = OperatorAppearance.MotionStyle;
        private readonly bool? animation = OperatorMotion.AnimationOverride;
        private readonly byte[]? contents = File.Exists(OperatorAppearance.StoragePath)
            ? File.ReadAllBytes(OperatorAppearance.StoragePath) : null;

        internal AppearanceScope(bool reduceMotion, OperatorMotionStyle motionStyle = OperatorMotionStyle.Expressive)
        {
            OperatorMotion.AnimationOverride = null;
            OperatorAppearance.ConfigureMotionStyle(motionStyle);
            OperatorAppearance.Configure(true, reduceMotion);
        }

        public void Dispose()
        {
            OperatorMotion.AnimationOverride = animation;
            OperatorAppearance.ConfigureMotionStyle(style);
            OperatorAppearance.Configure(glass, reduced);
            if (contents is null) File.Delete(OperatorAppearance.StoragePath);
            else File.WriteAllBytes(OperatorAppearance.StoragePath, contents);
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly GameState Saved = new()
        {
            Sport = "afl",
            MatchState = new MatchManager().ToState()
        };
        internal readonly ResumeMatchPrompt Prompt;
        internal readonly Grid Host = new();
        internal readonly NativeWindow Window;
        internal int Completions;
        private readonly string savedBefore;

        internal Fixture()
        {
            savedBefore = JsonSerializer.Serialize(Saved);
            Prompt = new ResumeMatchPrompt(Saved);
            Host.Children.Add(Prompt);
            // Match the actual owner's one-shot removal on Completed. No
            // persistence service is installed: this fixture observes choices.
            Prompt.Completed += () => { Completions++; Host.Children.Remove(Prompt); };
            Window = new NativeWindow { Width = 760, Height = 580, Content = Host };
            Window.Show();
        }

        internal Button Discard => Assert.IsType<Button>(Prompt.FindControl<Button>("DiscardButton"));
        internal Button Later => Assert.IsType<Button>(Prompt.FindControl<Button>("LaterButton"));
        internal Button Resume => Assert.IsType<Button>(Prompt.FindControl<Button>("ResumeButton"));
        internal ProgressBar Progress => Assert.IsType<ProgressBar>(Prompt.FindControl<ProgressBar>("DiscardConfirmationProgress"));
        internal DispatcherTimer Timer => Assert.IsType<DispatcherTimer>(typeof(ResumeMatchPrompt)
            .GetField("_discardConfirmationTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Prompt));

        internal async Task Ready()
        {
            await Settle(650);
            Assert.True(Prompt.IsLoaded);
            Assert.True(Discard.IsEffectivelyVisible);
            Assert.False(Timer.IsEnabled);
            Assert.Equal(TimeSpan.FromSeconds(5), Timer.Interval);
            AssertUndecided();
        }

        internal void Key(PhysicalKey key)
        {
            Window.KeyPressQwerty(key, RawInputModifiers.None);
            Window.KeyReleaseQwerty(key, RawInputModifiers.None);
        }

        internal void ArmByKeyboard()
        {
            Assert.True(Discard.Focus(NavigationMethod.Tab));
            Key(PhysicalKey.Space);
            AssertArmed();
        }

        internal async Task PointerClick(Button button)
        {
            button.BringIntoView();
            await Settle(20);
            var origin = button.TranslatePoint(default, Window);
            Assert.NotNull(origin);
            var point = origin.Value + new Vector(button.Bounds.Width / 2, button.Bounds.Height / 2);
            Assert.True(new Rect(Window.Bounds.Size).Contains(point));
            Window.MouseDown(point, MouseButton.Left);
            Window.MouseUp(point, MouseButton.Left);
        }

        internal void AssertUnchanged() => Assert.Equal(savedBefore, JsonSerializer.Serialize(Saved));

        internal void AssertUndecided()
        {
            Assert.Equal(0, Completions);
            Assert.False(Prompt.ResumeChosen);
            Assert.False(Prompt.DiscardChosen);
            Assert.False(Prompt.DeferredChosen);
            AssertUnchanged();
        }

        internal void AssertArmed()
        {
            Assert.Equal("CONFIRM DISCARD", Discard.Content);
            Assert.True(Timer.IsEnabled);
            Assert.Equal(TimeSpan.FromSeconds(5), Timer.Interval);
            Assert.True(Progress.IsVisible);
            Assert.Contains("within five seconds", AutomationProperties.GetHelpText(Discard) ?? "");
            AssertUndecided();
        }

        internal void AssertStaticArmed()
        {
            AssertArmed();
            Assert.False(Motion.IsAnimating(Progress));
            // Full means armed, not a fabricated time-remaining fraction. The
            // button/help explains the actual five-second acceptance window.
            Assert.Equal(1, Progress.Value, 6);
        }

        internal void AssertDisarmed()
        {
            Assert.Equal("DISCARD SAVE", Discard.Content);
            Assert.False(Timer.IsEnabled);
            Assert.False(Progress.IsVisible);
            Assert.False(Motion.IsAnimating(Progress));
            Assert.Contains("Confirmation is required", AutomationProperties.GetHelpText(Discard) ?? "");
            AssertUndecided();
        }

        internal async Task WaitForTimeout(Stopwatch sinceArm)
        {
            while (Timer.IsEnabled && sinceArm.ElapsedMilliseconds < 6200) await Settle(50);
            AssertDisarmed();
        }

        internal void RetainedActionsCannotChangeTheDecision(bool discarded)
        {
            // Retained routed actions model queued callbacks after the public
            // owner removed the completed view. They do not force private state.
            foreach (var button in new[] { Discard, Later, Resume, Discard })
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, Completions);
            Assert.Equal(discarded, Prompt.DiscardChosen);
            Assert.Equal(!discarded, Prompt.DeferredChosen);
            Assert.False(Prompt.ResumeChosen);
            Assert.False(Timer.IsEnabled);
            Assert.False(Motion.IsAnimating(Progress));
            AssertUnchanged();
        }

        public void Dispose()
        {
            Window.Content = null;
            Window.Close();
            // No cleanup before assertions; unload is the real timer-disarm
            // route. StopAll is only failure-path clock cleanup afterward.
            Motion.StopAll(Progress);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReducedEntryArmsAStaticIndicatorAndStillRequiresTheExplicitSecondActivation(bool calm)
    {
        using var appearance = new AppearanceScope(true, calm ? OperatorMotionStyle.Calm : OperatorMotionStyle.Expressive);
        using var f = new Fixture();
        await f.Ready();
        Assert.False(OperatorMotion.Enabled);
        f.ArmByKeyboard();
        f.AssertStaticArmed();
        await Settle(350);
        f.AssertStaticArmed();
        await f.PointerClick(f.Discard);
        await Settle(100);
        Assert.True(f.Prompt.DiscardChosen);
        Assert.False(f.Prompt.DeferredChosen);
        f.RetainedActionsCannotChangeTheDecision(discarded: true);
    }

    [AvaloniaFact]
    public async Task LiveCalmThenReducedKeepsTheOriginalFiveSecondWindowAndTimeoutNeverDiscards()
    {
        using var appearance = new AppearanceScope(false);
        using var f = new Fixture();
        await f.Ready();
        Assert.True(OperatorMotion.Enabled);
        f.ArmByKeyboard();
        var elapsed = Stopwatch.StartNew();
        Assert.True(Motion.IsAnimating(f.Progress));
        await AtElapsed(elapsed, 1400);
        double beforeCalm = f.Progress.Value;
        OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Calm);
        await Settle(250);
        f.AssertArmed();
        Assert.True(Motion.IsAnimating(f.Progress));
        Assert.InRange(f.Progress.Value, 0, beforeCalm + .015);
        OperatorAppearance.Configure(true, true);
        f.AssertStaticArmed();
        await AtElapsed(elapsed, 3900);
        f.AssertStaticArmed();
        await f.WaitForTimeout(elapsed);
        Assert.InRange(elapsed.ElapsedMilliseconds, 4800, 6200);
        // The timeout only disarmed: another first activation cannot discard.
        f.ArmByKeyboard();
        f.AssertStaticArmed();
        f.Key(PhysicalKey.Escape);
        await Settle(100);
        f.RetainedActionsCannotChangeTheDecision(discarded: false);
    }

    [AvaloniaFact]
    public async Task TimeoutAndRearmInTheSameLoadedPromptStillObserveTheNextLiveReduction()
    {
        using var appearance = new AppearanceScope(false);
        using var f = new Fixture();
        await f.Ready();
        f.ArmByKeyboard();
        var elapsed = Stopwatch.StartNew();
        OperatorAppearance.Configure(true, true);
        f.AssertStaticArmed();
        await f.WaitForTimeout(elapsed);
        Assert.InRange(elapsed.ElapsedMilliseconds, 4800, 6200);
        Assert.True(f.Prompt.IsLoaded);
        OperatorAppearance.Configure(true, false);
        f.ArmByKeyboard();
        Assert.True(Motion.IsAnimating(f.Progress));
        await Settle(180);
        OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Calm);
        OperatorAppearance.Configure(true, true);
        f.AssertStaticArmed();
        await Settle(250);
        f.AssertStaticArmed();
        await f.PointerClick(f.Later);
        await Settle(100);
        f.RetainedActionsCannotChangeTheDecision(discarded: false);
    }

    [AvaloniaFact]
    public async Task UnloadDisarmsAndReloadRearmGetsANewWindowWithoutTheOldTimeout()
    {
        using var appearance = new AppearanceScope(true);
        using var f = new Fixture();
        await f.Ready();
        f.ArmByKeyboard();
        var firstArm = Stopwatch.StartNew();
        await AtElapsed(firstArm, 700);
        f.Host.Children.Remove(f.Prompt);
        await Settle(100);
        Assert.False(f.Prompt.IsLoaded);
        f.AssertDisarmed();
        await AtElapsed(firstArm, 1600);
        f.Host.Children.Add(f.Prompt);
        await Settle(100);
        Assert.True(f.Prompt.IsLoaded);
        f.ArmByKeyboard();
        var secondArm = Stopwatch.StartNew();
        f.AssertStaticArmed();
        // Beyond the old deadline, safely before the new five-second deadline.
        await AtElapsed(firstArm, 5350);
        f.AssertStaticArmed();
        await f.WaitForTimeout(secondArm);
        Assert.InRange(secondArm.ElapsedMilliseconds, 4800, 6200);
        f.ArmByKeyboard();
        f.AssertStaticArmed();
        await f.PointerClick(f.Discard);
        await Settle(100);
        f.RetainedActionsCannotChangeTheDecision(discarded: true);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SafeCloseWhileArmedCanBeInterruptedByReducedMotionAndCompletesOnce(bool escape)
    {
        using var appearance = new AppearanceScope(false);
        using var f = new Fixture();
        await f.Ready();
        f.ArmByKeyboard();
        Assert.True(Motion.IsAnimating(f.Progress));
        if (escape) f.Key(PhysicalKey.Escape);
        else await f.PointerClick(f.Later);
        Assert.True(f.Prompt.DeferredChosen);
        Assert.False(f.Prompt.DiscardChosen);
        Assert.False(f.Timer.IsEnabled);
        Assert.False(Motion.IsAnimating(f.Progress));
        Assert.True(Motion.IsAnimating(f.Prompt), "Exercise reduction during the actual close, before the owner removes the view.");
        OperatorAppearance.Configure(true, true);
        await Settle(650);
        Assert.False(f.Prompt.IsLoaded);
        f.RetainedActionsCannotChangeTheDecision(discarded: false);
        await Settle(250);
        f.RetainedActionsCannotChangeTheDecision(discarded: false);
    }
    // Append inside RecoveryDiscardMotionTests. Reuses its existing Fixture,
    // AppearanceScope, Settle and single-read AtElapsed helper. No new imports.
    // All reflection is read-only; no private handler/state/timer is forced.
    private static void AssertRoundTripShellSettled(Fixture f)
    {
        var shell = Assert.IsType<OperatorGlassSurface>(f.Prompt.FindControl<OperatorGlassSurface>("DialogShell"));
        var transform = Assert.IsType<Avalonia.Media.TransformGroup>(shell.RenderTransform);
        var scale = Assert.Single(transform.Children.OfType<Avalonia.Media.ScaleTransform>());
        var travel = Assert.Single(transform.Children.OfType<Avalonia.Media.TranslateTransform>());
        Assert.False(Motion.IsAnimating(scale));
        Assert.False(Motion.IsAnimating(travel));
        Assert.Equal(1, scale.ScaleX, 6);
        Assert.Equal(1, scale.ScaleY, 6);
        Assert.Equal(0, travel.X, 6);
        Assert.Equal(0, travel.Y, 6);
        Assert.False(Motion.IsAnimating(f.Prompt));
        Assert.Equal(1, f.Prompt.Opacity, 6);
        Assert.False(Motion.IsAnimating(Assert.IsType<Grid>(f.Prompt.FindControl<Grid>("RecoveryHeader"))));
        Assert.False(Motion.IsAnimating(Assert.IsType<ScrollViewer>(f.Prompt.FindControl<ScrollViewer>("RecoveryDetails"))));
    }

    private static DispatcherTimer? RoundTripExitDeadline(Fixture f) =>
        (DispatcherTimer?)typeof(ResumeMatchPrompt).GetField("_recoveryExitDeadline",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Prompt);

    private static void AssertRoundTripExitIsActuallyPending(Fixture f)
    {
        Assert.True(f.Prompt.IsLoaded);
        Assert.Equal(0, f.Completions);
        Assert.False(f.Prompt.IsHitTestVisible);
        Assert.False(f.Timer.IsEnabled);
        Assert.False(Motion.IsAnimating(f.Progress));
        Assert.True(Motion.IsAnimating(f.Prompt), "The second reduction must interrupt a real pending exit clock.");
        var deadline = Assert.IsType<DispatcherTimer>(RoundTripExitDeadline(f));
        Assert.True(deadline.IsEnabled);
        Assert.Equal(OperatorMotion.SurfaceClose + TimeSpan.FromMilliseconds(150), deadline.Interval);
        f.AssertUnchanged();
    }

    [AvaloniaFact]
    public async Task LaterAfterAMotionRoundTripCompletesImmediatelyOnTheSecondReduction()
    {
        using var appearance = new AppearanceScope(false);
        using var f = new Fixture();
        await f.Ready();
        Assert.True(OperatorMotion.Enabled);
        OperatorAppearance.Configure(true, true);
        Assert.False(OperatorMotion.Enabled);
        AssertRoundTripShellSettled(f);
        f.AssertUndecided();

        // The prompt remains loaded. Re-enabling must preserve observation of
        // the next close; this is not a reload that would reattach the listener.
        OperatorAppearance.Configure(true, false);
        Assert.True(OperatorMotion.Enabled);
        Assert.True(f.Prompt.IsLoaded);
        f.ArmByKeyboard();
        var originalArm = Stopwatch.StartNew();
        Assert.True(Motion.IsAnimating(f.Progress));
        await f.PointerClick(f.Later);
        Assert.True(f.Prompt.DeferredChosen);
        Assert.False(f.Prompt.DiscardChosen);
        Assert.False(f.Prompt.ResumeChosen);
        AssertRoundTripExitIsActuallyPending(f);

        OperatorAppearance.Configure(true, true);
        // Deliberately before any await: the ordinary 150ms exit/300ms fallback
        // would hide the lost observer if this assertion waited for completion.
        Assert.Equal(1, f.Completions);
        Assert.Null(RoundTripExitDeadline(f));
        AssertRoundTripShellSettled(f);
        await Settle(80);
        Assert.False(f.Prompt.IsLoaded);
        f.RetainedActionsCannotChangeTheDecision(discarded: false);
        Assert.False(f.Progress.IsVisible);

        // The old five-second confirmation deadline cannot revive work or
        // replace this safe decision after the owner removed the prompt.
        await AtElapsed(originalArm, 5350);
        Assert.Null(RoundTripExitDeadline(f));
        AssertRoundTripShellSettled(f);
        f.RetainedActionsCannotChangeTheDecision(discarded: false);
        Assert.False(f.Progress.IsVisible);
    }

    [AvaloniaFact]
    public async Task ConfirmedDiscardAfterAMotionRoundTripCompletesImmediatelyOnTheSecondReduction()
    {
        using var appearance = new AppearanceScope(false);
        using var f = new Fixture();
        await f.Ready();
        Assert.True(OperatorMotion.Enabled);
        OperatorAppearance.Configure(true, true);
        Assert.False(OperatorMotion.Enabled);
        AssertRoundTripShellSettled(f);
        f.AssertUndecided();
        OperatorAppearance.Configure(true, false);
        Assert.True(OperatorMotion.Enabled);
        Assert.True(f.Prompt.IsLoaded);

        // Preserve the actual two-activation destructive workflow. The first
        // Space only arms; the real pointer activation explicitly confirms it.
        f.ArmByKeyboard();
        var originalArm = Stopwatch.StartNew();
        Assert.True(Motion.IsAnimating(f.Progress));
        await f.PointerClick(f.Discard);
        Assert.True(f.Prompt.DiscardChosen);
        Assert.False(f.Prompt.DeferredChosen);
        Assert.False(f.Prompt.ResumeChosen);
        AssertRoundTripExitIsActuallyPending(f);

        OperatorAppearance.Configure(true, true);
        Assert.Equal(1, f.Completions);
        Assert.Null(RoundTripExitDeadline(f));
        AssertRoundTripShellSettled(f);
        await Settle(80);
        Assert.False(f.Prompt.IsLoaded);
        f.RetainedActionsCannotChangeTheDecision(discarded: true);
        Assert.False(f.Progress.IsVisible);

        await AtElapsed(originalArm, 5350);
        Assert.Null(RoundTripExitDeadline(f));
        AssertRoundTripShellSettled(f);
        f.RetainedActionsCannotChangeTheDecision(discarded: true);
        Assert.False(f.Progress.IsVisible);
    }

}

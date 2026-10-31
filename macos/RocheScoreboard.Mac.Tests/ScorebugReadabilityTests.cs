// Real operator score/story routes; existing fixture setup is shared.
using System.Diagnostics;
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
public sealed partial class PresentationLifetimeTests
{
    private static T SBReadNamed<T>(Control root, string name) where T : Control
    { var value = root.FindControl<T>(name); Assert.NotNull(value); return value; }
    private static MatchManager SBReadMatch(Fixture f) => Field<MatchManager>(f.Window, "_match");
    private static ScorebugControl SBReadBug(Fixture f) => Field<ScorebugControl>(f.Window, "_scorebug");
    private static async Task SBReadPump(int ms = 25)
    { await Task.Delay(ms); Dispatcher.UIThread.RunJobs(); }
    private sealed class SBReadMotionScope : IDisposable
    {
        private readonly bool? previous = OperatorMotion.AnimationOverride;
        internal SBReadMotionScope(bool reduced) { OperatorMotion.AnimationOverride = !reduced; }
        public void Dispose() { OperatorMotion.AnimationOverride = previous; }
    }
    private static void SBReadClick(Button button)
    {
        Assert.True(button.IsEffectivelyVisible && button.IsEffectivelyEnabled,
            button.Name + " must be a loaded visible enabled operator action");
        Assert.NotNull(button.GetVisualRoot());
        // Routed button Click proves actual application state path, not physical
        // pointer targeting. No reflection invokes a scoring/story handler.
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static async Task SBReadReady(Fixture f, ScorebugLayout layout)
    {
        await f.Ready(); // existing scoped FinalizeAflSetup fixture setup
        f.Window.Width = 1280; f.Window.Height = 850;
        f.Output().Width = 386; f.Output().Height = 193;
        // Historical names: ClassicRadio -> Expanded enum; ExpandedRadio -> Distance.
        SBReadNamed<RadioButton>(f.Window, layout == ScorebugLayout.Distance
            ? "LayoutExpandedRadio" : "LayoutClassicRadio").IsChecked = true;
        SBReadNamed<RadioButton>(f.Window, "AnimBroadcastRadio").IsChecked = true;
        SBReadNamed<TabControl>(f.Window, "MainTabControl").SelectedItem =
            SBReadNamed<TabItem>(f.Window, "MatchControlTab");
        await SBReadPump(200);
        Assert.True(f.Output().IsVisible); Assert.Equal(layout, SBReadBug(f).CurrentLayout);
        Assert.Equal(GoalAnimationStyle.Broadcast, SBReadBug(f).GetGoalAnimationStyle());
        Assert.Equal(new[] { 0, 0, 0, 0 }, SBReadScore(SBReadMatch(f)));
    }
    private static async Task SBReadStoryFromKeyboard(Fixture f, string label)
    {
        var outputTab = SBReadNamed<TabItem>(f.Window, "OutputStudioTab");
        if (!outputTab.IsVisible) SBReadClick(SBReadNamed<Button>(f.Window, "GuidedModeButton"));
        Assert.True(outputTab.IsVisible);
        SBReadNamed<TabControl>(f.Window, "MainTabControl").SelectedItem = outputTab;
        await SBReadPump(350);
        var tile = f.Window.GetVisualDescendants().OfType<OperatorActionBorder>()
            .Single(action => action.GetVisualDescendants().OfType<TextBlock>()
                .Any(text => text.Text == label));
        tile.BringIntoView(); await SBReadPump(120);
        Assert.True(tile.IsEffectivelyVisible && tile.IsEffectivelyEnabled);
        Assert.True(tile.Focus(NavigationMethod.Tab));
        Assert.Same(tile, f.Window.FocusManager!.GetFocusedElement());
        // Actual native OnKeyDown -> ordinary operator tile -> current drought.
        f.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        f.Window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }
    private static string SBReadDirectory(string name)
    {
        string directory = Path.Combine(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")
            ?? Path.Combine(Path.GetTempPath(), "roche-scorebug-readability"), name);
        Directory.CreateDirectory(directory); return directory;
    }
    private static void SBReadFrame(Window output, string directory, string name)
    {
        using var frame = output.CaptureRenderedFrame();
        Assert.NotNull(frame); frame.Save(Path.Combine(directory, name + ".png"));
    }
    private static void SBReadReceipt(string directory, object value) =>
        File.WriteAllText(Path.Combine(directory, "timeline.json"),
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    private static int[] SBReadScore(MatchManager match) =>
        [match.HomeGoals, match.HomeBehinds, match.AwayGoals, match.AwayBehinds];
    private sealed record SBReadPose(double Milliseconds, string Goals, string Behinds, string Total,
        double GoalsOpacity, double BehindsOpacity, double TotalOpacity,
        string OtherGoals, string OtherBehinds, string OtherTotal, bool Animating);

    // 16 cases: both public layouts, operator reduced/full, four real score actions.
    // All assertions after receipt creation are intended post-fix contracts;
    // expected baseline failure is repeated opacity dips, not a passing-probe test.
    [AvaloniaTheory]
    [InlineData(ScorebugLayout.Expanded, false, true, true)]
    [InlineData(ScorebugLayout.Expanded, false, true, false)]
    [InlineData(ScorebugLayout.Expanded, false, false, true)]
    [InlineData(ScorebugLayout.Expanded, false, false, false)]
    [InlineData(ScorebugLayout.Expanded, true, true, true)]
    [InlineData(ScorebugLayout.Expanded, true, true, false)]
    [InlineData(ScorebugLayout.Expanded, true, false, true)]
    [InlineData(ScorebugLayout.Expanded, true, false, false)]
    [InlineData(ScorebugLayout.Distance, false, true, true)]
    [InlineData(ScorebugLayout.Distance, false, true, false)]
    [InlineData(ScorebugLayout.Distance, false, false, true)]
    [InlineData(ScorebugLayout.Distance, false, false, false)]
    [InlineData(ScorebugLayout.Distance, true, true, true)]
    [InlineData(ScorebugLayout.Distance, true, true, false)]
    [InlineData(ScorebugLayout.Distance, true, false, true)]
    [InlineData(ScorebugLayout.Distance, true, false, false)]
    public async Task AcceptedScoreRemainsReadableAfterItsRealBroadcastReveal(
        ScorebugLayout layout, bool reduced, bool home, bool goal)
    {
        using var motion = new SBReadMotionScope(reduced); using var f = new Fixture(); await SBReadReady(f, layout);
        string side = home ? "Home" : "Away", other = home ? "Away" : "Home";
        string name = $"score-{layout}-reduce-{reduced}-{side}-{(goal ? "Goal" : "Behind")}";
        string directory = SBReadDirectory(name);
        var g = SBReadNamed<TextBlock>(SBReadBug(f), "Exp" + side + "GoalsText");
        var b = SBReadNamed<TextBlock>(SBReadBug(f), "Exp" + side + "BehindsText");
        var t = SBReadNamed<TextBlock>(SBReadBug(f), "Exp" + side + "TotalText");
        var og = SBReadNamed<TextBlock>(SBReadBug(f), "Exp" + other + "GoalsText");
        var ob = SBReadNamed<TextBlock>(SBReadBug(f), "Exp" + other + "BehindsText");
        var ot = SBReadNamed<TextBlock>(SBReadBug(f), "Exp" + other + "TotalText");
        var events = new List<ScoreEvent>(); SBReadMatch(f).ScoreEventAdded += events.Add;
        SBReadFrame(f.Output(), directory, "00-before-input");
        var watch = Stopwatch.StartNew();
        SBReadClick(SBReadNamed<Button>(f.Window, side + (goal ? "GoalButton" : "BehindButton")));
        // Synchronous assertion BEFORE any Pump/await. Model and event accept now;
        // intentional old on-air pose is allowed until existing reveal time.
        var ev = Assert.Single(events); Assert.Same(ev, Assert.Single(SBReadMatch(f).Events));
        Assert.Equal(home ? TeamSide.Home : TeamSide.Away, ev.Team);
        Assert.Equal(goal ? ScoreType.Goal : ScoreType.Behind, ev.Type);
        int[] expected = home ? (goal ? [1, 0, 0, 0] : [0, 1, 0, 0])
                              : (goal ? [0, 0, 1, 0] : [0, 0, 0, 1]);
        Assert.Equal(expected, SBReadScore(SBReadMatch(f)));
        Assert.Equal(expected, new[] { ev.HomeGoals, ev.HomeBehinds, ev.AwayGoals, ev.AwayBehinds });
        Assert.Equal("0", t.Text); Assert.Equal("0", ot.Text);
        string expectedTotal = goal ? "6" : "1";
        var poses = new List<SBReadPose>(); double? reveal = null; double? stopped = null;
        bool capturedDip = false; double last = 0, maxGap = 0;
        while (watch.Elapsed.TotalMilliseconds < 8000)
        {
            await SBReadPump(12); double now = watch.Elapsed.TotalMilliseconds;
            maxGap = Math.Max(maxGap, now - last); last = now;
            poses.Add(new(now, g.Text ?? "", b.Text ?? "", t.Text ?? "",
                g.Opacity, b.Opacity, t.Opacity, og.Text ?? "", ob.Text ?? "", ot.Text ?? "", SBReadBug(f).IsScoreAnimating));
            if (t.Text == expectedTotal && reveal is null)
            { reveal = now; SBReadFrame(f.Output(), directory, "01-real-value-revealed"); }
            if (reveal is not null && t.Opacity < .6 && !capturedDip)
            { capturedDip = true; SBReadFrame(f.Output(), directory, "02-first-observed-opacity-dip"); }
            if (reveal is not null && !SBReadBug(f).IsScoreAnimating) stopped ??= now;
            // Retain BOTH the observation window and the sample minimum. A
            // slower renderer must not end the witness before enough samples.
            if (stopped is not null && now - stopped >= 450
                && reveal is not null && poses.Count(p => p.Milliseconds >= reveal.Value) >= 20) break;
        }
        SBReadFrame(f.Output(), directory, "03-final-readable-pose");
        List<SBReadPose> afterReveal = reveal is null ? [] : poses.Where(p => p.Milliseconds >= reveal.Value).ToList();
        SBReadReceipt(directory, new { scope = "Headless real native property timeline and PNGs; native desktop/hardware unverified",
            layout, reduced, home, goal, revealMilliseconds = reveal, stoppedMilliseconds = stopped,
            maximumSampleGapMilliseconds = maxGap, observedOpacityDip = capturedDip, poses });
        Assert.NotNull(reveal); Assert.NotNull(stopped);
        Assert.True(afterReveal.Count >= 20, "Missing adequate observed post-reveal sampling; inspect receipt before claiming a baseline.");
        Assert.All(poses, p => {
            Assert.True(p.Total is "0" || p.Total == expectedTotal, "Never show fabricated intermediate totals");
            Assert.Equal("0", p.OtherGoals); Assert.Equal("0", p.OtherBehinds); Assert.Equal("0", p.OtherTotal);
        });
        Assert.All(afterReveal, p => {
            Assert.Equal(expectedTotal, p.Total);
            Assert.InRange(p.TotalOpacity, .95, 1.0);
            Assert.InRange(goal ? p.GoalsOpacity : p.BehindsOpacity, .95, 1.0);
        });
        Assert.Equal(expected, SBReadScore(SBReadMatch(f))); Assert.Single(events); Assert.Single(SBReadMatch(f).Events);
        Assert.False(SBReadBug(f).IsScoreAnimating);
    }

    private sealed record SBReadDirectionPose(double Milliseconds, double Opacity, string DiagnosticSymbol, double RenderedScaleY, bool Visible);
    // Four cases: both team-specific actual manual tiles, operator full/reduced.
    [AvaloniaTheory]
    [InlineData(false, true)] [InlineData(false, false)]
    [InlineData(true, true)] [InlineData(true, false)]
    public async Task ManualTeamStoryHasAStableSemanticDirection(bool reduced, bool home)
    {
        using var motion = new SBReadMotionScope(reduced); using var f = new Fixture(); await SBReadReady(f, ScorebugLayout.Expanded);
        string before = JsonSerializer.Serialize(SBReadMatch(f).ToState());
        string directory = SBReadDirectory($"direction-reduce-{reduced}-{(home ? "home" : "away")}");
        await SBReadStoryFromKeyboard(f, home ? "HOME DROUGHT" : "AWAY DROUGHT");
        var host = SBReadNamed<Control>(SBReadBug(f), "ExpTeamStoryDirectionHost");
        var indicator = SBReadNamed<TextBlock>(SBReadBug(f), "ExpTeamStoryDirectionIndicator");
        // Indicator TextBlock is deliberately collapsed; actual three Path shapes
        // are mirrored by host scale. Check rendered geometry separately.
        Assert.Equal(3, host.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Count());
        // Reachability is a precondition. Failure here is a fixture/UI-route result,
        // not proof the actual direction animation is safe or broken.
        var wait = Stopwatch.StartNew();
        while (!SBReadBug(f).IsOverlayActive && wait.ElapsedMilliseconds < 2500) await SBReadPump();
        Assert.True(SBReadBug(f).IsOverlayActive, "Manual team-story route was not reached; inspect fixture.");
        var watch = Stopwatch.StartNew(); var poses = new List<SBReadDirectionPose>(); int frame = 0;
        while (watch.ElapsedMilliseconds < 1650)
        {
            await SBReadPump(20);
            poses.Add(new(watch.Elapsed.TotalMilliseconds, host.Opacity,
                indicator.Text ?? "", Assert.IsType<Avalonia.Media.ScaleTransform>(host.RenderTransform).ScaleY, host.IsEffectivelyVisible));
            if (watch.ElapsedMilliseconds >= frame * 180)
            { SBReadFrame(f.Output(), directory, $"direction-{frame:D2}"); frame++; }
        }
        SBReadReceipt(directory, new { scope = "Actual keyboard manual-drought route; real native properties and frames",
            reduced, home, poses });
        Assert.All(poses, p => {
            Assert.True(p.Visible); Assert.Equal(home ? "▲" : "▼", p.DiagnosticSymbol);
            Assert.Equal(home ? 1.0 : -1.0, p.RenderedScaleY);
            Assert.InRange(p.Opacity, .95, 1.0);
        });
        Assert.Equal(home ? Avalonia.Layout.VerticalAlignment.Top : Avalonia.Layout.VerticalAlignment.Bottom,
            host.VerticalAlignment);
        Assert.Equal(before, JsonSerializer.Serialize(SBReadMatch(f).ToState()));
    }
}

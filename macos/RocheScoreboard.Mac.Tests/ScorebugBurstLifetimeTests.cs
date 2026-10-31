// Real burst and close/reopen routes reuse current presentation fixtures.
using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class PresentationLifetimeTests
{
    // Read-only diagnostics supplement public final values; never clear a queue
    // or invoke a lifecycle/animation handler to satisfy these observations.
    private static int SBFollowQueueCount(Fixture f) =>
        Field<System.Collections.ICollection>(f.Window, "_pendingScoreAnimations").Count;
    private static int SBFollowActiveCount(Fixture f) => Field<int>(f.Window, "_activeScoreAnimations");
    private static int[] SBFollowEventSnapshot(ScoreEvent ev) =>
        [ev.HomeGoals, ev.HomeBehinds, ev.AwayGoals, ev.AwayBehinds];
    private sealed record SBFollowPose(double Milliseconds, string HomeGoals, string HomeBehinds,
        string HomeTotal, string AwayGoals, string AwayBehinds, string AwayTotal,
        double HomeOpacity, double AwayOpacity, bool PublicAnimating,
        int SourceQueueCountReadOnly, int SourceActiveCountReadOnly);
    private static SBFollowPose SBFollowObserve(Fixture f, double milliseconds)
    {
        var bug = SBReadBug(f);
        string Text(string name) => SBReadNamed<TextBlock>(bug, name).Text ?? "";
        return new(milliseconds, Text("ExpHomeGoalsText"), Text("ExpHomeBehindsText"), Text("ExpHomeTotalText"),
            Text("ExpAwayGoalsText"), Text("ExpAwayBehindsText"), Text("ExpAwayTotalText"),
            SBReadNamed<TextBlock>(bug, "ExpHomeTotalText").Opacity,
            SBReadNamed<TextBlock>(bug, "ExpAwayTotalText").Opacity,
            bug.IsScoreAnimating, SBFollowQueueCount(f), SBFollowActiveCount(f));
    }

    [AvaloniaTheory]
    [InlineData(ScorebugLayout.Expanded, false)]
    [InlineData(ScorebugLayout.Distance, true)]
    public async Task FourBackToBackScoreActionsAcceptNowAndDrainToExactSevenEach(
        ScorebugLayout layout, bool reduced)
    {
        using var motion = new SBReadMotionScope(reduced);
        using var f = new Fixture(); await SBReadReady(f, layout);
        string directory = SBReadDirectory($"burst-{layout}-reduce-{reduced}");
        var events = new List<ScoreEvent>(); var eventSnapshots = new List<int[]>();
        SBReadMatch(f).ScoreEventAdded += ev => { events.Add(ev); eventSnapshots.Add(SBFollowEventSnapshot(ev)); };
        SBReadFrame(f.Output(), directory, "00-before-burst");
        var watch = Stopwatch.StartNew();
        // Four actual loaded visible operator Buttons. No await/Pump between
        // input and the model/event/snapshot assertions below.
        foreach (string name in new[] { "HomeGoalButton", "AwayGoalButton", "HomeBehindButton", "AwayBehindButton" })
            SBReadClick(SBReadNamed<Button>(f.Window, name));
        Assert.Equal(new[] { 1, 1, 1, 1 }, SBReadScore(SBReadMatch(f)));
        Assert.Equal(7, SBReadMatch(f).HomeTotal); Assert.Equal(7, SBReadMatch(f).AwayTotal);
        Assert.Equal(4, events.Count); Assert.Equal(4, SBReadMatch(f).Events.Count);
        Assert.Equal(new[] { 1, 0, 0, 0 }, eventSnapshots[0]);
        Assert.Equal(new[] { 1, 0, 1, 0 }, eventSnapshots[1]);
        Assert.Equal(new[] { 1, 1, 1, 0 }, eventSnapshots[2]);
        Assert.Equal(new[] { 1, 1, 1, 1 }, eventSnapshots[3]);
        Assert.Equal(new[] { TeamSide.Home, TeamSide.Away, TeamSide.Home, TeamSide.Away }, events.Select(ev => ev.Team));
        Assert.Equal(new[] { ScoreType.Goal, ScoreType.Goal, ScoreType.Behind, ScoreType.Behind }, events.Select(ev => ev.Type));
        Assert.All(events, ev => Assert.Contains(ev, SBReadMatch(f).Events));
        // The live model is already7/7 while the first package retains an old
        // output pose. This explicitly protects acceptance from visual pacing.
        var immediate = SBFollowObserve(f, watch.Elapsed.TotalMilliseconds);
        Assert.Equal("0", immediate.HomeTotal); Assert.Equal("0", immediate.AwayTotal);
        var poses = new List<SBFollowPose> { immediate }; double? drained = null;
        while (watch.ElapsedMilliseconds < 25000)
        {
            await SBReadPump(20);
            var pose = SBFollowObserve(f, watch.Elapsed.TotalMilliseconds); poses.Add(pose);
            bool complete = pose.HomeTotal == "7" && pose.AwayTotal == "7" && !pose.PublicAnimating
                && pose.SourceQueueCountReadOnly == 0 && pose.SourceActiveCountReadOnly == 0;
            if (complete) drained ??= pose.Milliseconds; else drained = null;
            if (drained is not null && pose.Milliseconds - drained.Value >= 450) break;
        }
        SBReadFrame(f.Output(), directory, "01-drained-output");
        SBReadReceipt(directory, new { scope = "Actual four routed score Clicks; native properties, no cross-side reveal-order assertion",
            layout, reduced, eventSnapshots, drainedMilliseconds = drained, poses });
        Assert.NotNull(drained); // generous liveness timeout, not a performance benchmark
        Assert.All(poses, pose => {
            // Per-digit allowed values come from real initial/accepted score
            // snapshots. Deliberately do not demand atomic cross-side poses.
            Assert.True(pose.HomeGoals is "0" or "1"); Assert.True(pose.HomeBehinds is "0" or "1");
            Assert.True(pose.AwayGoals is "0" or "1"); Assert.True(pose.AwayBehinds is "0" or "1");
            Assert.True(pose.HomeTotal is "0" or "6" or "7"); Assert.True(pose.AwayTotal is "0" or "6" or "7");
        });
        var final = poses[^1]; Assert.Equal("7", final.HomeTotal); Assert.Equal("7", final.AwayTotal);
        Assert.Equal("1", final.HomeGoals); Assert.Equal("1", final.HomeBehinds);
        Assert.Equal("1", final.AwayGoals); Assert.Equal("1", final.AwayBehinds);
        Assert.False(SBReadBug(f).IsScoreAnimating); Assert.Equal(0, SBFollowQueueCount(f)); Assert.Equal(0, SBFollowActiveCount(f));
        Assert.Equal(new[] { 1, 1, 1, 1 }, SBReadScore(SBReadMatch(f)));
        Assert.Equal(4, events.Count); Assert.Equal(4, SBReadMatch(f).Events.Count);
    }

    // Tests the actual existing command surface: OpenCommandBar populates live
    // rows; select real enable-output row and send keyboard Enter through normal
    // command KeyDown. No direct EnableOperatorOutput/private handler invocation.
    private static async Task SBFollowShowOutputThroughCommandBar(Fixture f)
    {
        Assert.True(f.Window.OpenCommandBar(), "Existing command bar must open in the active AFL session");
        await SBReadPump(100);
        var results = SBReadNamed<ListBox>(f.Window, "CommandResults");
        var show = results.Items.OfType<ListBoxItem>().Single(row => Equals(row.Tag, "enable-output"));
        results.SelectedItem = show;
        Assert.True(show.IsEffectivelyVisible && show.IsEffectivelyEnabled);
        Assert.True(SBReadNamed<TextBox>(f.Window, "CommandSearch").Focus());
        f.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        f.Window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await SBReadPump(150);
        Assert.False(SBReadNamed<Control>(f.Window, "CommandOverlay").IsVisible);
        Assert.True(f.Output().IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(ScorebugLayout.Expanded, false, true, false)]
    [InlineData(ScorebugLayout.Distance, true, false, true)]
    public async Task ClosingDuringAnObservedRevealReopensAuthoritativeScoresWithoutOldWork(
        ScorebugLayout layout, bool reduced, bool home, bool goal)
    {
        using var motion = new SBReadMotionScope(reduced);
        using var f = new Fixture(); await SBReadReady(f, layout);
        string directory = SBReadDirectory($"reveal-close-{layout}-reduce-{reduced}");
        string side = home ? "Home" : "Away"; string total = goal ? "6" : "1";
        var events = new List<ScoreEvent>(); SBReadMatch(f).ScoreEventAdded += events.Add;
        var oldOutput = f.Output(); var oldBug = SBReadBug(f);
        var watch = Stopwatch.StartNew(); double? reveal = null;
        SBReadClick(SBReadNamed<Button>(f.Window, side + (goal ? "GoalButton" : "BehindButton")));
        Assert.Single(events); Assert.Single(SBReadMatch(f).Events);
        while (watch.ElapsedMilliseconds < 8000)
        {
            await SBReadPump(12);
            if (SBReadNamed<TextBlock>(oldBug, "Exp" + side + "TotalText").Text == total
                && oldBug.IsScoreAnimating)
            { reveal = watch.Elapsed.TotalMilliseconds; break; }
        }
        Assert.NotNull(reveal); Assert.True(oldBug.IsScoreAnimating);
        string stateBeforeClose = JsonSerializer.Serialize(SBReadMatch(f).ToState());
        SBReadFrame(oldOutput, directory, "00-observed-reveal-before-close");
        Assert.True(oldBug.IsScoreAnimating, "Frame capture must not move this test beyond the in-flight close boundary.");
        oldOutput.Close(); // actual output/window lifetime boundary
        await SBReadPump(80);
        Assert.False(oldOutput.IsVisible); Assert.False(oldBug.IsScoreAnimating);
        Assert.Equal(0, SBFollowQueueCount(f)); Assert.Equal(0, SBFollowActiveCount(f));
        await SBFollowShowOutputThroughCommandBar(f);
        Assert.NotSame(oldOutput, f.Output()); Assert.NotSame(oldBug, SBReadBug(f));
        var reopened = f.Output(); var reopenedBug = SBReadBug(f);
        var poses = new List<SBFollowPose>(); var after = Stopwatch.StartNew();
        // Long enough to observe beyond original reveal's remaining completion
        // work and a normal short following beat; no synthetic clock or ResetQueue.
        while (after.ElapsedMilliseconds < 1800)
        {
            await SBReadPump(25); poses.Add(SBFollowObserve(f, after.Elapsed.TotalMilliseconds));
        }
        SBReadFrame(reopened, directory, "01-command-reopened-current-output");
        SBReadReceipt(directory, new { scope = "Actual output Close during observed reveal, real enable-output command row plus Enter",
            layout, reduced, home, goal, revealMilliseconds = reveal, poses });
        Assert.All(poses, pose => {
            Assert.Equal(home ? total : "0", pose.HomeTotal); Assert.Equal(home ? "0" : total, pose.AwayTotal);
            Assert.False(pose.PublicAnimating); Assert.Equal(0, pose.SourceQueueCountReadOnly); Assert.Equal(0, pose.SourceActiveCountReadOnly);
            Assert.InRange(pose.HomeOpacity, .95, 1.0); Assert.InRange(pose.AwayOpacity, .95, 1.0);
        });
        Assert.Same(reopened, f.Output()); Assert.Same(reopenedBug, SBReadBug(f));
        Assert.False(oldBug.IsScoreAnimating); Assert.Single(events); Assert.Single(SBReadMatch(f).Events);
        Assert.Equal(stateBeforeClose, JsonSerializer.Serialize(SBReadMatch(f).ToState()));
    }
}

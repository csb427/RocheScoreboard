// External proposal only: uncompiled/unrun. Public mounted component routes,
// natural source-authored delta timers, no private Tick/animation invocation.
using System.Reflection;
using Path = System.IO.Path;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private static void DeltaOwnerPopulate(PresentationScoreBar bar, MatchManager match) =>
        bar.Populate(match, 1, "#C9483B", "#B68F12", "#FFFFFF", "#FFFFFF", null, null, showTimer: true);
    private static MatchManager DeltaOwnerMatch(bool replacement)
    {
        var match = new MatchManager();
        match.SetTeams(replacement ? "Replacement Home" : "Original Home", replacement ? "NEW" : "OLD",
            replacement ? "Replacement Away" : "Original Away", replacement ? "OTH" : "PRE");
        match.AddBehind(TeamSide.Home);
        if (replacement) { match.AddGoal(TeamSide.Away); match.AddGoal(TeamSide.Away); }
        match.SetElapsed(TimeSpan.FromSeconds(125)); // Live centre, stopped deterministic clock.
        return match;
    }
    private static string DeltaOwnerState(MatchManager match) => JsonSerializer.Serialize(match.ToState());
    private static string DeltaOwnerSide(bool home) => home ? "Home" : "Away";
    private static TranslateTransform DeltaOwnerTranslate(PresentationScoreBar bar, bool home) =>
        Assert.IsType<TranslateTransform>(Named<Border>(bar, DeltaOwnerSide(home) + "DeltaOverlay").RenderTransform);
    private static bool DeltaOwnerTimerRunning(PresentationScoreBar bar, bool home)
    {
        var field = bar.GetType().GetField(home ? "_homeDeltaTimer" : "_awayDeltaTimer",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field.GetValue(bar) is DispatcherTimer timer && timer.IsEnabled;
    }
    private static object DeltaOwnerPose(PresentationScoreBar bar, bool home) => new
    {
        side = DeltaOwnerSide(home), timer = DeltaOwnerTimerRunning(bar, home),
        delta = Named<TextBlock>(bar, DeltaOwnerSide(home) + "DeltaText").Text,
        overlayOpacity = Named<Border>(bar, DeltaOwnerSide(home) + "DeltaOverlay").Opacity,
        overlayClock = Motion.IsAnimating(Named<Border>(bar, DeltaOwnerSide(home) + "DeltaOverlay")),
        y = DeltaOwnerTranslate(bar, home).Y, translateClock = Motion.IsAnimating(DeltaOwnerTranslate(bar, home)),
        flashOpacity = Named<Rectangle>(bar, DeltaOwnerSide(home) + "GoalFlash").Opacity,
        flashClock = Motion.IsAnimating(Named<Rectangle>(bar, DeltaOwnerSide(home) + "GoalFlash")),
        total = Named<TextBlock>(bar, DeltaOwnerSide(home) + "TotalText").Text,
        totalOpacity = Named<TextBlock>(bar, DeltaOwnerSide(home) + "TotalText").Opacity
    };
    private static void DeltaOwnerGone(PresentationScoreBar bar, bool home)
    {
        string side = DeltaOwnerSide(home);
        Assert.False(DeltaOwnerTimerRunning(bar, home));
        Assert.Equal(0d, Named<Border>(bar, side + "DeltaOverlay").Opacity, 6);
        Assert.Equal(0d, Named<Rectangle>(bar, side + "GoalFlash").Opacity, 6);
        Assert.False(Motion.IsAnimating(Named<Border>(bar, side + "DeltaOverlay")));
        Assert.False(Motion.IsAnimating(DeltaOwnerTranslate(bar, home)));
        Assert.False(Motion.IsAnimating(Named<Rectangle>(bar, side + "GoalFlash")));
        Assert.False(Motion.IsAnimating(Named<TextBlock>(bar, side + "TotalText")));
        Assert.Equal(1d, Named<TextBlock>(bar, side + "TotalText").Opacity, 6);
    }
    private static void DeltaOwnerTotals(PresentationScoreBar bar, MatchManager match)
    {
        Assert.Equal(match.HomeTotal.ToString(), Named<TextBlock>(bar, "HomeTotalText").Text);
        Assert.Equal(match.AwayTotal.ToString(), Named<TextBlock>(bar, "AwayTotalText").Text);
        Assert.Equal(match.HomeAbbr.ToUpperInvariant(), Named<TextBlock>(bar, "HomeLogoFallback").Text);
        Assert.Equal(match.AwayAbbr.ToUpperInvariant(), Named<TextBlock>(bar, "AwayLogoFallback").Text);
    }
    private static void DeltaOwnerRecord(string name, object value)
    {
        if (Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"),
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }

    [AvaloniaTheory]
    [InlineData("different", true, true)]
    [InlineData("different", true, false)]
    [InlineData("different", false, true)]
    [InlineData("different", false, false)]
    [InlineData("load", true, true)]
    [InlineData("load", true, false)]
    [InlineData("load", false, true)]
    [InlineData("load", false, false)]
    [InlineData("reset", true, true)]
    [InlineData("reset", true, false)]
    [InlineData("reset", false, true)]
    [InlineData("reset", false, false)]
    public async Task ScoreBarDeltaRebindRetiresOnlyTheOldModelOrLifetimeWhileFreshFeedbackStillRuns(
        string boundary, bool home, bool goal)
    {
        // Existing rollback-safe storage fixture: QuarterLabelService may lazily
        // read/create its isolated directory during the real Populate route.
        await using var storage = await AflFixture.Create(1280, 850);
        var original = DeltaOwnerMatch(false); var replacement = DeltaOwnerMatch(true);
        var bar = new PresentationScoreBar { Height = 64 };
        var window = new Window { Width = 707, Height = 100, Content = bar };
        string name = $"scorebar-delta-{boundary}-{home}-{goal}";
        bool naturallyFinished = false;
        object? bindingEvidence = null;
        try
        {
            DeltaOwnerPopulate(bar, original); window.Show(); await Settle(120);
            Assert.True(bar.IsLoaded); Assert.True(bar.IsEffectivelyVisible);
            var oldEvent = goal ? original.AddGoal(home ? TeamSide.Home : TeamSide.Away)
                : original.AddBehind(home ? TeamSide.Home : TeamSide.Away);
            DeltaOwnerPopulate(bar, original);
            bar.PlayScoreAnimation(oldEvent); await Settle(100);
            Assert.Equal(goal ? "+6" : "+1", Named<TextBlock>(bar, DeltaOwnerSide(home) + "DeltaText").Text);
            Assert.True(DeltaOwnerTimerRunning(bar, home));
            Assert.True(Motion.IsAnimating(DeltaOwnerTranslate(bar, home)));
            Assert.Equal(1d, Named<Border>(bar, DeltaOwnerSide(home) + "DeltaOverlay").Opacity, 6);

            // Ordinary same-owner Populate is a common totals/resize refresh,
            // not a new game. This positive control rules out cancel-every-call.
            for (int i = 0; i < 3; i++) DeltaOwnerPopulate(bar, original);
            Assert.True(DeltaOwnerTimerRunning(bar, home));
            Assert.True(Motion.IsAnimating(DeltaOwnerTranslate(bar, home)));
            Assert.Equal(1d, Named<Border>(bar, DeltaOwnerSide(home) + "DeltaOverlay").Opacity, 6);
            DeltaOwnerTotals(bar, original);
            object before = DeltaOwnerPose(bar, home);
            string originalState = DeltaOwnerState(original);

            MatchManager current;
            if (boundary == "different") current = replacement;
            else if (boundary == "load")
            { original.LoadState(replacement.ToState()); current = original; }
            else
            {
                original.ResetForNewGame();
                original.SetTeams("Reset Home", "RST", "Reset Away", "NEW");
                original.AddBehind(TeamSide.Home); original.AddGoal(TeamSide.Away); original.AddGoal(TeamSide.Away);
                original.SetElapsed(TimeSpan.FromSeconds(125)); current = original;
            }
            string expected = DeltaOwnerState(current);
            DeltaOwnerPopulate(bar, current);
            bindingEvidence = new { before, home = DeltaOwnerPose(bar, true), away = DeltaOwnerPose(bar, false),
                expected, actual = DeltaOwnerState(current) };
            // FIRST negative oracle, before waiting out an old timer: source
            // currently leaves this old timer and visible delta over new totals.
            DeltaOwnerGone(bar, home); DeltaOwnerGone(bar, !home); DeltaOwnerTotals(bar, current);
            await Settle(2000); // Natural old1350/1050ms hold + authored exit budget.
            DeltaOwnerGone(bar, true); DeltaOwnerGone(bar, false); DeltaOwnerTotals(bar, current);
            Assert.Equal(expected, DeltaOwnerState(current));
            if (boundary == "different") Assert.Equal(originalState, DeltaOwnerState(original));

            // A fresh accepted score on this bound model keeps its own +1/+6
            // semantics and natural finish. No copied old callback may settle it.
            var fresh = goal ? current.AddBehind(home ? TeamSide.Home : TeamSide.Away)
                : current.AddGoal(home ? TeamSide.Home : TeamSide.Away);
            DeltaOwnerPopulate(bar, current); bar.PlayScoreAnimation(fresh);
            Assert.True(DeltaOwnerTimerRunning(bar, home));
            Assert.Equal(goal ? "+1" : "+6", Named<TextBlock>(bar, DeltaOwnerSide(home) + "DeltaText").Text);
            await Settle(100);
            DeltaOwnerPopulate(bar, current); // Repeat refresh while fresh cue is live.
            Assert.True(DeltaOwnerTimerRunning(bar, home));
            Assert.Equal(1d, Named<Border>(bar, DeltaOwnerSide(home) + "DeltaOverlay").Opacity, 6);
            DeltaOwnerGone(bar, !home); DeltaOwnerTotals(bar, current);
            string acceptedState = DeltaOwnerState(current);
            await Settle(1900);
            DeltaOwnerGone(bar, true); DeltaOwnerGone(bar, false); DeltaOwnerTotals(bar, current);
            Assert.Equal(acceptedState, DeltaOwnerState(current));
            DeltaOwnerRecord(name + "-settled", new { home = DeltaOwnerPose(bar, true), away = DeltaOwnerPose(bar, false),
                actual = DeltaOwnerState(current) });
            naturallyFinished = true;
        }
        finally
        {
            if (bindingEvidence is not null) DeltaOwnerRecord(name + "-binding", bindingEvidence);
            window.Close();
            // Original-source baseline may fail at the immediate binding oracle.
            // Let its authored hold/exit finish before the next fixture; never
            // invoke private Tick/Completed or substitute shortened durations.
            if (!naturallyFinished) await Settle(2000);
        }
    }

    [AvaloniaFact]
    public async Task ScoreBarDeltaRebindRetiresBothSidesOfAnAcceptedBurstAndKeepsFreshBurst()
    {
        await using var storage = await AflFixture.Create(1280, 850);
        var original = DeltaOwnerMatch(false); var replacement = DeltaOwnerMatch(true);
        var bar = new PresentationScoreBar { Height = 64 };
        var window = new Window { Width = 707, Height = 100, Content = bar };
        bool naturallyFinished = false;
        object? bindingEvidence = null;
        try
        {
            DeltaOwnerPopulate(bar, original); window.Show(); await Settle(120);
            var home = original.AddGoal(TeamSide.Home); var away = original.AddBehind(TeamSide.Away);
            DeltaOwnerPopulate(bar, original); bar.PlayScoreAnimation(home); bar.PlayScoreAnimation(away);
            await Settle(100); DeltaOwnerPopulate(bar, original);
            Assert.True(DeltaOwnerTimerRunning(bar, true)); Assert.True(DeltaOwnerTimerRunning(bar, false));
            Assert.Equal("+6", Named<TextBlock>(bar, "HomeDeltaText").Text);
            Assert.Equal("+1", Named<TextBlock>(bar, "AwayDeltaText").Text);
            Assert.Equal(1d, Named<Border>(bar, "HomeDeltaOverlay").Opacity, 6);
            Assert.Equal(1d, Named<Border>(bar, "AwayDeltaOverlay").Opacity, 6);
            string oldState = DeltaOwnerState(original), currentState = DeltaOwnerState(replacement);
            DeltaOwnerPopulate(bar, replacement);
            bindingEvidence = new { home = DeltaOwnerPose(bar, true), away = DeltaOwnerPose(bar, false), currentState };
            DeltaOwnerGone(bar, true); DeltaOwnerGone(bar, false); DeltaOwnerTotals(bar, replacement);
            // New two-side feedback immediately after rebind must remain owned
            // independently through the old source's natural timer budget.
            var currentHome = replacement.AddBehind(TeamSide.Home);
            var currentAway = replacement.AddGoal(TeamSide.Away);
            DeltaOwnerPopulate(bar, replacement);
            bar.PlayScoreAnimation(currentHome); bar.PlayScoreAnimation(currentAway);
            await Settle(100); DeltaOwnerPopulate(bar, replacement);
            Assert.True(DeltaOwnerTimerRunning(bar, true)); Assert.True(DeltaOwnerTimerRunning(bar, false));
            Assert.Equal("+1", Named<TextBlock>(bar, "HomeDeltaText").Text);
            Assert.Equal("+6", Named<TextBlock>(bar, "AwayDeltaText").Text);
            Assert.Equal(1d, Named<Border>(bar, "HomeDeltaOverlay").Opacity, 6);
            Assert.Equal(1d, Named<Border>(bar, "AwayDeltaOverlay").Opacity, 6);
            string accepted = DeltaOwnerState(replacement);
            await Settle(1900);
            DeltaOwnerGone(bar, true); DeltaOwnerGone(bar, false); DeltaOwnerTotals(bar, replacement);
            Assert.Equal(accepted, DeltaOwnerState(replacement)); Assert.Equal(oldState, DeltaOwnerState(original));
            naturallyFinished = true;
        }
        finally
        {
            if (bindingEvidence is not null) DeltaOwnerRecord("scorebar-delta-burst-binding", bindingEvidence);
            window.Close(); if (!naturallyFinished) await Settle(2000);
        }
    }
}

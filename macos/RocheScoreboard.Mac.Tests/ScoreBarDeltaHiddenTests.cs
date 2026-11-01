using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaTheory]
    [InlineData("self", true, true)]
    [InlineData("self", false, true)]
    [InlineData("owner", true, true)]
    [InlineData("owner", false, true)]
    [InlineData("self", true, false)]
    [InlineData("self", false, false)]
    [InlineData("owner", true, false)]
    [InlineData("owner", false, false)]
    [InlineData("detach", true, false)]
    [InlineData("detach", false, false)]
    public async Task ScoreBarHiddenOrDetachedDoesNotKeepOrRestartScoreDecoration(
        string boundary, bool home, bool alreadyPlaying)
    {
        await using var storage = await AflFixture.Create(1280, 850);
        var match = DeltaOwnerMatch(false);
        var bar = new PresentationScoreBar { Height = 64 };
        var window = new Window { Width = 707, Height = 100, Content = bar };
        try
        {
            DeltaOwnerPopulate(bar, match); window.Show(); await Settle(100);
            Assert.True(bar.IsLoaded); Assert.True(window.IsVisible); Assert.True(bar.IsVisible);
            var score = home ? match.AddGoal(TeamSide.Home) : match.AddBehind(TeamSide.Away);
            DeltaOwnerPopulate(bar, match);
            if (alreadyPlaying)
            {
                bar.PlayScoreAnimation(score); await Settle(80);
                Assert.True(DeltaOwnerTimerRunning(bar, home));
            }
            string state = DeltaOwnerState(match);
            if (boundary == "self") bar.IsVisible = false;
            else if (boundary == "owner") window.Hide();
            else window.Content = null;
            await Settle(40);
            if (boundary == "self") Assert.False(bar.IsVisible);
            else if (boundary == "owner") Assert.False(window.IsVisible);
            else Assert.False(bar.IsLoaded);
            if (!alreadyPlaying)
            {
                // A copied presentation callback may arrive after hide/detach.
                // Repeat it: neither call is permission to restart decoration.
                bar.PlayScoreAnimation(score); bar.PlayScoreAnimation(score);
            }
            DeltaOwnerRecord($"{boundary}-{home}-{alreadyPlaying}-retired", new
            {
                boundary, alreadyPlaying, home = DeltaOwnerPose(bar, true),
                away = DeltaOwnerPose(bar, false), before = state, after = DeltaOwnerState(match)
            });
            DeltaOwnerGone(bar, true); DeltaOwnerGone(bar, false);
            Assert.Equal(state, DeltaOwnerState(match));
            if (boundary == "self") bar.IsVisible = true;
            else if (boundary == "owner") window.Show();
            else window.Content = bar;
            await Settle(100); Assert.True(bar.IsLoaded);
            DeltaOwnerGone(bar, true); DeltaOwnerGone(bar, false); // No stale replay on return.
            var fresh = home ? match.AddBehind(TeamSide.Home) : match.AddGoal(TeamSide.Away);
            DeltaOwnerPopulate(bar, match); bar.PlayScoreAnimation(fresh);
            Assert.True(DeltaOwnerTimerRunning(bar, home));
            Assert.Equal(home ? "+1" : "+6", Named<TextBlock>(bar, home ? "HomeDeltaText" : "AwayDeltaText").Text);
            string accepted = DeltaOwnerState(match);
            await Settle(1900);
            DeltaOwnerGone(bar, true); DeltaOwnerGone(bar, false); DeltaOwnerTotals(bar, match);
            Assert.Equal(accepted, DeltaOwnerState(match));
        }
        finally { window.Close(); await Settle(2000); }
    }
}

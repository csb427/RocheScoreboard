using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ScoreBarUnloadingRetiresCapturedDeltaButReloadAcceptsFreshScore(bool home)
    {
        await using var storage = await AflFixture.Create(1280, 850);
        var match = DeltaOwnerMatch(false);
        var bar = new PresentationScoreBar { Height = 64 };
        var window = new Window { Width = 707, Height = 100, Content = bar };
        try
        {
            DeltaOwnerPopulate(bar, match); window.Show(); await Settle(100);
            Assert.True(bar.IsLoaded);
            var score = home ? match.AddGoal(TeamSide.Home) : match.AddBehind(TeamSide.Away);
            DeltaOwnerPopulate(bar, match); bar.PlayScoreAnimation(score); await Settle(80);
            Assert.True(DeltaOwnerTimerRunning(bar, home));
            string state = DeltaOwnerState(match);
            window.Content = null;
            await Until(() => !bar.IsLoaded, "Detach the actual public bar before checking its owned timers.");
            DeltaOwnerGone(bar, true); DeltaOwnerGone(bar, false);
            Assert.Equal(state, DeltaOwnerState(match));
            window.Content = bar; DeltaOwnerPopulate(bar, match);
            await Until(() => bar.IsLoaded, "The same actual bar must reload before fresh feedback.");
            var fresh = home ? match.AddBehind(TeamSide.Home) : match.AddGoal(TeamSide.Away);
            DeltaOwnerPopulate(bar, match); bar.PlayScoreAnimation(fresh);
            Assert.True(DeltaOwnerTimerRunning(bar, home));
            Assert.Equal(home ? "+1" : "+6", Named<TextBlock>(bar, home ? "HomeDeltaText" : "AwayDeltaText").Text);
            string accepted = DeltaOwnerState(match);
            await Settle(1900);
            DeltaOwnerGone(bar, true); DeltaOwnerGone(bar, false);
            DeltaOwnerTotals(bar, match); Assert.Equal(accepted, DeltaOwnerState(match));
        }
        finally
        {
            window.Close();
            await Settle(2000); // Preserve the original natural timer budget on a red baseline.
        }
    }
}

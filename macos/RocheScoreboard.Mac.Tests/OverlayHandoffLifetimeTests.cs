using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task ObsoleteOverlayHandoffCannotReleaseFreshWeatherAfterPublicResetInHiddenObserver(bool animate, bool fullReset)
    {
        bool? previous = OperatorMotion.AnimationOverride; OperatorMotion.AnimationOverride = animate;
        try
        {
            await ManualWeatherControlledScope.Run(async controlled =>
            {
                var f = controlled.Fixture; var bug = await ManualWeatherPrepare(f, controlled, false);
                string model = ManualWeatherModel(f); var fresh = ManualWeatherSnapshot(23); int hidden = 0; Exception? callbackError = null;
                void ResetOnHidden()
                {
                    if (++hidden != 1) return;
                    try
                    {
                        if (fullReset) bug.ResetAllOverlayState(); else bug.ClearOverlayQueue();
                        bug.ShowRainForecast(fresh);
                    }
                    catch (Exception error) { callbackError = error; }
                }
                bug.OverlayHidden += ResetOnHidden;
                try
                {
                    bug.ShowWeatherForecast(ManualWeatherSnapshot(17));
                    await Until(() => ManualWeatherOverlay(bug) == "Forecast", "Forecast must own the actual overlay lane first.");
                    Assert.True(bug.CancelCurrentOverlay());
                    await Until(() => hidden >= 1, "Actual exit must notify the observer without a synthetic private timer invocation.");
                    await Settle(350); // Cross the real110ms handoff; fresh rain's12s hold is untouched.
                    DeltaOwnerRecord("handoff-reset-" + animate + "-" + fullReset, new { animate, fullReset, hidden,
                        active = ManualWeatherOverlay(bug), bug.IsOverlayActive,
                        rainVisible = ManualWeatherRead<bool>(bug, "_rainVisible"),
                        rainHeight = Named<Border>(bug, "ExpRainBorder").Height });
                    Assert.Null(callbackError); Assert.Equal(1, hidden);
                    Assert.True(ManualWeatherRead<bool>(bug, "_rainVisible"));
                    Assert.Equal("RainForecast", ManualWeatherOverlay(bug)); Assert.True(bug.IsOverlayActive);
                    Assert.Same(fresh, bug.GetWeatherSnapshot()); Assert.Equal(model, ManualWeatherModel(f));
                }
                finally { bug.OverlayHidden -= ResetOnHidden; bug.ClearOverlayQueue(); }
            });
        }
        finally { OperatorMotion.AnimationOverride = previous; }
    }
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task OrdinaryManualOverlayHandoffStillDisplaysQueuedWeatherAndKeepsScoreState(bool animate)
    {
        bool? previous = OperatorMotion.AnimationOverride; OperatorMotion.AnimationOverride = animate;
        try
        {
            await ManualWeatherControlledScope.Run(async controlled =>
            {
                var f = controlled.Fixture; var bug = await ManualWeatherPrepare(f, controlled, false);
                string model = ManualWeatherModel(f); var fresh = ManualWeatherSnapshot(23);
                bug.ShowWeatherForecast(ManualWeatherSnapshot(17));
                await Until(() => ManualWeatherOverlay(bug) == "Forecast", "Forecast must own the actual lane.");
                bug.PrepareManualOverlayHandoff(); bug.ShowRainForecast(fresh);
                await Until(() => ManualWeatherOverlay(bug) == "RainForecast", "Ordinary queued rain must enter after actual exit/gap.");
                await Settle(350); Assert.True(bug.IsOverlayActive); Assert.Same(fresh, bug.GetWeatherSnapshot()); Assert.Equal(model, ManualWeatherModel(f));
                bug.ClearOverlayQueue();
            });
        }
        finally { OperatorMotion.AnimationOverride = previous; }
    }
}

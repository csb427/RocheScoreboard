using System.Collections;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Services.Cloud;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class LiveSettingsClockChoiceTests
{
    private static void VenueWeatherShow(ScorebugControl bug, string kind, WeatherSnapshot snapshot)
    {
        if (kind == "Forecast") bug.ShowWeatherForecast(snapshot);
        else if (kind == "WeatherStats") bug.ShowWeatherStats(snapshot);
        else bug.ShowRainForecast(snapshot);
    }

    private static string[] VenueWeatherQueue(ScorebugControl bug)
        => ManualWeatherRead<IEnumerable>(bug, "_overlayQueue").Cast<object>().Select(value => value.ToString()!).ToArray();

    private static void VenueWeatherAssertCleared(ScorebugControl bug)
    {
        foreach (string field in new[] { "_weatherStatsVisible", "_forecastVisible", "_rainVisible" })
            Assert.False(ManualWeatherRead<bool>(bug, field), field + " must not retain an obsolete venue.");
        foreach (string field in new[] { "_weatherStatsHideTimer", "_forecastHideTimer", "_rainHideTimer" })
            Assert.Null(ManualWeatherRead<object?>(bug, field));
        foreach (string name in new[] { "ExpWeatherStatsBorder", "ExpForecastBorder", "ExpRainBorder" })
        {
            var border = Named<Border>(bug, name);
            Assert.Equal(0d, border.Height); Assert.Equal(0d, border.Opacity);
        }
        Assert.Null(bug.GetWeatherSnapshot()); Assert.Equal("", Named<TextBlock>(bug, "ExpTopWeatherText").Text);
        Assert.DoesNotContain(VenueWeatherQueue(bug), kind => kind is "Forecast" or "WeatherStats" or "RainForecast");
    }

    [AvaloniaTheory]
    [InlineData("Forecast", false)] [InlineData("Forecast", true)]
    [InlineData("WeatherStats", false)] [InlineData("WeatherStats", true)]
    [InlineData("RainForecast", false)] [InlineData("RainForecast", true)]
    public async Task PublicVenueChangeRemovesVisibleWeatherAndAdvancesOnlyRetainedNonweather(string kind, bool disable)
    {
        await ManualWeatherControlledScope.Run(async controlled =>
        {
            var f = controlled.Fixture; var bug = await ManualWeatherPrepare(f, controlled, false);
            string model = ManualWeatherModel(f); int hidden = 0; void ObserveHidden() => ++hidden;
            bug.OverlayHidden += ObserveHidden;
            try
            {
                VenueWeatherShow(bug, kind, ManualWeatherSnapshot(17));
                await Until(() => ManualWeatherOverlay(bug) == kind, "The actual old-venue graphic must own the lane.");
                await Settle(650); // Finish the real broadcast entrance, not OperatorMotion's separate override.
                bug.ShowQuarterScores(1, 2, 1, 1, 3);
                VenueWeatherShow(bug, kind == "Forecast" ? "RainForecast" : "Forecast", ManualWeatherSnapshot(19));
                ((IRemoteCommandTarget)f.Window).SetWeatherLocation(disable ? "" : "Controlled venue B");
                await Settle(800); // Cross exit/layout/handoff clocks; an old location must never remain on air.
                DeltaOwnerRecord("venue-visible-" + kind + "-" + disable, new { kind, disable, hidden,
                    active = ManualWeatherOverlay(bug), queued = VenueWeatherQueue(bug),
                    weather = ManualWeatherRead<bool>(bug, "_weatherStatsVisible"),
                    forecast = ManualWeatherRead<bool>(bug, "_forecastVisible"), rain = ManualWeatherRead<bool>(bug, "_rainVisible") });
                VenueWeatherAssertCleared(bug);
                Assert.Equal("QuarterScores", ManualWeatherOverlay(bug)); Assert.Equal(1, hidden);
                Assert.Equal(model, ManualWeatherModel(f)); Assert.Empty(controlled.Pending);
            }
            finally { bug.OverlayHidden -= ObserveHidden; bug.ClearOverlayQueue(); }
        });
    }

    [AvaloniaTheory]
    [InlineData("Forecast")] [InlineData("WeatherStats")] [InlineData("RainForecast")]
    public async Task SameNormalizedVenuePreservesCurrentWeatherAndItsTimer(string kind)
    {
        await ManualWeatherControlledScope.Run(async controlled =>
        {
            var f = controlled.Fixture; var bug = await ManualWeatherPrepare(f, controlled, false);
            string model = ManualWeatherModel(f); var snapshot = ManualWeatherSnapshot(17);
            VenueWeatherShow(bug, kind, snapshot); await Settle(650);
            string timerField = kind == "Forecast" ? "_forecastHideTimer" : kind == "WeatherStats" ? "_weatherStatsHideTimer" : "_rainHideTimer";
            object timer = ManualWeatherRead<object>(bug, timerField); Assert.NotNull(timer);
            ((IRemoteCommandTarget)f.Window).SetWeatherLocation("  controlled VENUE a  ");
            await Settle(150);
            Assert.Equal(kind, ManualWeatherOverlay(bug)); Assert.Same(snapshot, bug.GetWeatherSnapshot());
            Assert.Same(timer, ManualWeatherRead<object>(bug, timerField)); Assert.Equal(model, ManualWeatherModel(f));
            bug.ClearOverlayQueue();
        });
    }

    [AvaloniaFact]
    public async Task PublicVenueChangePrunesOnlyQueuedWeatherAndKeepsCurrentNonweatherLifetime()
    {
        await ManualWeatherControlledScope.Run(async controlled =>
        {
            var f = controlled.Fixture; var bug = await ManualWeatherPrepare(f, controlled, false);
            string model = ManualWeatherModel(f); bug.ShowQuarterScores(1, 2, 1, 1, 3); await Settle(650);
            object timer = ManualWeatherRead<object>(bug, "_quarterScoresAutoHideTimer"); Assert.NotNull(timer);
            foreach (string kind in new[] { "Forecast", "WeatherStats", "RainForecast" }) VenueWeatherShow(bug, kind, ManualWeatherSnapshot(17));
            bug.ShowLeadChangesBar(2);
            Assert.Equal(new[] { "Forecast", "WeatherStats", "RainForecast", "LeadChanges" }, VenueWeatherQueue(bug));
            ((IRemoteCommandTarget)f.Window).SetWeatherLocation("Controlled venue B");
            VenueWeatherAssertCleared(bug); Assert.Equal("QuarterScores", ManualWeatherOverlay(bug));
            Assert.Same(timer, ManualWeatherRead<object>(bug, "_quarterScoresAutoHideTimer"));
            Assert.Equal(new[] { "LeadChanges" }, VenueWeatherQueue(bug)); Assert.Equal(model, ManualWeatherModel(f));
            bug.ClearOverlayQueue();
        });
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task VenueChangeDuringExitCannotLetOldCompletionReleaseFreshSameKind(bool disable)
    {
        await ManualWeatherControlledScope.Run(async controlled =>
        {
            var f = controlled.Fixture; var bug = await ManualWeatherPrepare(f, controlled, false);
            string model = ManualWeatherModel(f); bug.ShowWeatherForecast(ManualWeatherSnapshot(17)); await Settle(650);
            Assert.True(bug.CancelCurrentOverlay());
            ((IRemoteCommandTarget)f.Window).SetWeatherLocation(disable ? "" : "Controlled venue B");
            if (disable) ((IRemoteCommandTarget)f.Window).SetWeatherLocation("Controlled venue B");
            var fresh = ManualWeatherSnapshot(23); bug.ShowWeatherForecast(fresh);
            await Settle(850);
            Assert.Equal("Forecast", ManualWeatherOverlay(bug)); Assert.True(ManualWeatherRead<bool>(bug, "_forecastVisible"));
            Assert.True(Named<Border>(bug, "ExpForecastBorder").Height > 0);
            Assert.Same(fresh, bug.GetWeatherSnapshot()); Assert.Equal(model, ManualWeatherModel(f));
            bug.ClearOverlayQueue();
        });
    }
}

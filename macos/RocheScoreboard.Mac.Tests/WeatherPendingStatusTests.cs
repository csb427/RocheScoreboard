using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Services.Cloud;
using Xunit;
using Avalonia.VisualTree;
using Roche_Scoreboard.Views;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaTheory]
    [InlineData("cancel", "Weather overlay cancelled.")]
    [InlineData("location", "Weather set to Controlled venue B")]
    [InlineData("disabled", "Weather disabled")]
    public async Task WeatherPendingStatusFollowsOperatorIntentAndOldCompletionCannotRestoreLoading(string action, string expected)
    {
        await ManualWeatherControlledScope.Run(async controlled =>
        {
            var f = controlled.Fixture;
            await ManualWeatherPrepare(f, controlled, offAir: false);
            var pending = await ManualWeatherInvoke(f, controlled, "FORECAST");
            Assert.Equal("Loading weather for Controlled venue A…", Named<TextBlock>(f.Window, "WeatherLocationStatus").Text);
            if (action == "cancel") PresPlanProvider(Named<Button>(f.Window, "CancelOverlayButton")).Invoke();
            else ((IRemoteCommandTarget)f.Window).SetWeatherLocation(action == "location" ? "Controlled venue B" : "");
            Assert.Equal(expected, Named<TextBlock>(f.Window, "WeatherLocationStatus").Text);
            await ManualWeatherComplete(pending, null);
            Assert.Equal(expected, Named<TextBlock>(f.Window, "WeatherLocationStatus").Text);
            Assert.Empty(controlled.Feedback);
        });
    }
    [AvaloniaFact]
    public async Task WeatherPendingStatusCachedRetryReportsReadyAndStillShowsTheOriginalForecast()
    {
        await ManualWeatherControlledScope.Run(async controlled =>
        {
            var f = controlled.Fixture;
            var bug = await ManualWeatherPrepare(f, controlled, offAir: false);
            var pending = await ManualWeatherInvoke(f, controlled, "FORECAST");
            PresPlanProvider(Named<Button>(f.Window, "CancelOverlayButton")).Invoke();
            await ManualWeatherComplete(pending, null);
            bug.UpdateWeatherSnapshot(ManualWeatherSnapshot(17));
            var card = Assert.Single(f.Window.GetVisualDescendants().OfType<OperatorActionBorder>(),
                item => item.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "FORECAST"));
            PresPlanProvider(card).Invoke();
            await Until(() => ManualWeatherOverlay(bug) == "Forecast", "Cached retry must still show the actual requested overlay.");
            Assert.Equal("Weather ready for Controlled venue A", Named<TextBlock>(f.Window, "WeatherLocationStatus").Text);
            Assert.Single(controlled.Pending); Assert.Empty(controlled.Feedback);
        });
    }

    [AvaloniaTheory]
    [InlineData("home")]
    [InlineData("output-close")]
    public async Task WeatherPendingStatusRetiresAtNavigationOrOutputClose(string boundary)
    {
        await ManualWeatherControlledScope.Run(async controlled =>
        {
            var f = controlled.Fixture;
            var bug = await ManualWeatherPrepare(f, controlled, offAir: false);
            var pending = await ManualWeatherInvoke(f, controlled, "FORECAST");
            Assert.Equal("Loading weather for Controlled venue A…", Named<TextBlock>(f.Window, "WeatherLocationStatus").Text);
            if (boundary == "home")
            {
                Assert.True(f.Window.OpenCommandBar());
                Assert.True(f.Window.ExecuteCommand("home"));
            }
            else
            {
                PresPlanRead<Window>(f, "_displayWindow").Close();
                await Until(() => PresPlanRead<Window?>(f, "_displayWindow") is null, "Actual output close must finish.");
            }
            Assert.Equal("Weather overlay cancelled.", Named<TextBlock>(f.Window, "WeatherLocationStatus").Text);
            string model = ManualWeatherModel(f);
            await ManualWeatherComplete(pending, ManualWeatherSnapshot(21));
            Assert.Equal("Weather overlay cancelled.", Named<TextBlock>(f.Window, "WeatherLocationStatus").Text);
            Assert.Null(bug.GetWeatherSnapshot()); Assert.Empty(controlled.Feedback);
            Assert.Equal(model, ManualWeatherModel(f));
            Assert.True(PresPlanRead<bool>(f, "_outputActivationRequired"));
        });
    }

}

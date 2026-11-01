using System.Net;
using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Services.Cloud;
using System.Text.Json;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private sealed class LocalWeatherHandler : HttpMessageHandler
    {
        internal bool MissingLocation;
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            string payload = request.RequestUri!.AbsolutePath.Contains("search")
                ? MissingLocation ? "{\"results\":[]}" : "{\"results\":[{\"latitude\":-37.8,\"longitude\":145.0}]}"
                : """{"current":{"temperature_2m":17,"apparent_temperature":16,"weather_code":0},"daily":{"temperature_2m_min":[10],"temperature_2m_max":[22]},"hourly":{"time":[],"temperature_2m":[],"weather_code":[],"precipitation":[],"precipitation_probability":[]}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
        }
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WeatherCacheServiceStopOrUnresolvedReplacementDoesNotReturnPreviousVenue(bool replace)
    {
        using var handler = new LocalWeatherHandler(); using var client = new HttpClient(handler);
        using var service = new WeatherService(client);
        await service.StartAsync("Venue A");
        Assert.NotNull(service.LatestSnapshot); Assert.Equal(17d, service.LatestSnapshot.CurrentTemp); Assert.Equal(2, handler.Requests);
        if (replace) { handler.MissingLocation = true; await service.StartAsync("Venue B"); }
        else service.Stop();
        Assert.Null(service.LatestSnapshot); Assert.Equal("", service.CurrentWeather);
    }
    private static async Task WeatherCacheView(Action<ScorebugControl> check)
    {
        await using var storage = await AflFixture.Create(1280, 850);
        var bug = new ScorebugControl();
        var window = new Window { Width = 386, Height = 193, Content = bug };
        try { window.Show(); await Settle(80); Assert.True(bug.IsLoaded); check(bug); }
        finally { window.Close(); await Settle(80); }
    }
    [AvaloniaTheory]
    [InlineData("Venue B")]
    [InlineData(null)]
    public async Task WeatherCacheScorebugLocationChangeDiscardsPreviousSnapshotAndPendingWeather(string? replacement)
    {
        await WeatherCacheView(bug =>
        {
            bug.SetWeatherLocation("Venue A"); var snapshot = ManualWeatherSnapshot(17);
            bug.UpdateWeatherSnapshot(snapshot); Assert.Same(snapshot, bug.GetWeatherSnapshot());
            bug.SetWeatherLocation(replacement);
            Assert.Null(bug.GetWeatherSnapshot()); Assert.Equal("", Named<TextBlock>(bug, "ExpTopWeatherText").Text);
            bug.SetWeatherLocation("Venue A"); Assert.Null(bug.GetWeatherSnapshot());
            bug.UpdateWeatherSnapshot(snapshot); Assert.Same(snapshot, bug.GetWeatherSnapshot());
        });
    }
    [AvaloniaFact]
    public async Task WeatherCacheSameNormalizedLocationRetainsKnownWeather()
    {
        await WeatherCacheView(bug =>
        {
            bug.SetWeatherLocation("Venue A"); var snapshot = ManualWeatherSnapshot(17);
            bug.UpdateWeatherSnapshot(snapshot); bug.SetWeatherLocation("  venue a  ");
            Assert.Same(snapshot, bug.GetWeatherSnapshot());
        });
    }
    [AvaloniaTheory]
    [InlineData("Venue B")]
    [InlineData("")]
    public async Task WeatherCachePublicLocationApplyClearsOldOutputDataWithoutNavigationOrScoreChanges(string replacement)
    {
        var f = await AflFixture.Create(1280, 850);
        using var handler = new LocalWeatherHandler(); using var client = new HttpClient(handler);
        using var service = new WeatherService(client);
        try
        {
            Assert.Null(PresPlanRead<object?>(f, "_weatherService"));
            f.Window.WeatherServiceFactory = () => service;
            var remote = (IRemoteCommandTarget)f.Window;
            remote.SwitchScreen("scorebug");
            remote.SetWeatherLocation("Venue A");
            await f.Window.StartOrUpdateWeatherServiceAsync("Venue A");
            var bug = PresPlanRead<ScorebugControl>(f, "_scorebug");
            Assert.NotNull(bug.GetWeatherSnapshot()); Assert.Equal(17d, bug.GetWeatherSnapshot()!.CurrentTemp);
            if (!string.IsNullOrEmpty(replacement)) remote.SwitchScreen("weather");
            string model = JsonSerializer.Serialize(f.Match.ToState());
            string screen = PresPlanRead<string>(f, "_activeScreenKey"); bool open = PresWindowOpen(f);
            handler.MissingLocation = true;
            remote.SetWeatherLocation(replacement);
            await f.Window.StartOrUpdateWeatherServiceAsync(string.IsNullOrEmpty(replacement) ? null : replacement);
            Assert.Null(service.LatestSnapshot); Assert.Null(bug.GetWeatherSnapshot());
            Assert.Equal("", Named<TextBlock>(bug, "ExpTopWeatherText").Text);
            var weather = PresPlanRead<WeatherScreenControl>(f, "_weatherScreen");
            Assert.True(Named<Control>(weather, "WeatherUnavailablePanel").IsVisible);
            Assert.False(Named<Control>(weather, "WeatherDataPanel").IsVisible);
            Assert.Equal(screen, PresPlanRead<string>(f, "_activeScreenKey")); Assert.Equal(open, PresWindowOpen(f));
            Assert.Equal(model, JsonSerializer.Serialize(f.Match.ToState()));
        }
        finally { await f.DisposeAsync(); }
    }

    [AvaloniaFact]
    public async Task WeatherCacheStopDuringSummaryNotificationCannotPublishRetiredForecast()
    {
        using var handler = new LocalWeatherHandler(); using var client = new HttpClient(handler);
        using var service = new WeatherService(client); int forecasts = 0;
        service.WeatherUpdated += _ => service.Stop();
        service.ForecastUpdated += _ => forecasts++;
        await service.StartAsync("Venue A");
        Assert.Equal(0, forecasts); Assert.Null(service.LatestSnapshot); Assert.Equal("", service.CurrentWeather);
    }
    [AvaloniaFact]
    public async Task WeatherCacheCricketDisplayAndPanelClearOldReadoutsWithoutModelMutation()
    {
        await using var f = await AflFixture.Create(1280, 850);
        var panel = Named<CricketControlPanel>(f.Window, "CricketControlPanelView");
        var snapshot = ManualWeatherSnapshot(17); string before = JsonSerializer.Serialize(f.Match.ToState());
        panel.UpdateWeatherSnapshot(snapshot);
        Assert.Contains("17", Named<TextBlock>(panel, "CricketWeatherTemperature").Text);
        panel.ClearWeatherSnapshot();
        Assert.Equal("", Named<TextBlock>(panel, "CricketWeatherTemperature").Text);
        Assert.Equal("", Named<TextBlock>(panel, "CricketWeatherDescription").Text);
        var bug = new CricketScorebugControl(); var owner = new Window { Width = 386, Height = 193, Content = bug };
        try
        {
            owner.Show(); await Settle(80); bug.UpdateWeatherSnapshot(snapshot);
            Assert.Contains("17", Named<TextBlock>(bug, "TopWeatherText").Text);
            bug.ClearWeatherSnapshot(); Assert.Equal("", Named<TextBlock>(bug, "TopWeatherText").Text);
            Assert.Equal(before, JsonSerializer.Serialize(f.Match.ToState()));
        }
        finally { owner.Close(); await Settle(80); }
    }

}

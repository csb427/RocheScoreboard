using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private sealed class LocationStatusRequest(Uri uri)
    {
        internal readonly Uri Uri = uri;
        internal readonly TaskCompletionSource<HttpResponseMessage> Response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Geocode => Uri.AbsolutePath.Contains("search");
        internal void Reply(string payload) => Response.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
    }
    private sealed class LocationStatusTransport : HttpMessageHandler
    {
        internal readonly ConcurrentQueue<LocationStatusRequest> Requests = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var pending = new LocationStatusRequest(request.RequestUri!); Requests.Enqueue(pending);
            return await pending.Response.Task.WaitAsync(token);
        }
    }
    private sealed class LocationStatusScope : IAsyncDisposable
    {
        internal readonly AflFixture Fixture;
        internal readonly LocationStatusTransport Transport = new();
        internal readonly HttpClient Client;
        internal readonly WeatherService Service;
        internal readonly List<Task> Operations = new();
        internal readonly Func<WeatherService> PreviousFactory;
        internal readonly Action<Task>? PreviousObserver;
        internal LocationStatusScope(AflFixture fixture)
        {
            Assert.Null(PresPlanRead<object?>(fixture, "_weatherService"));
            Fixture = fixture; Client = new HttpClient(Transport); Service = new WeatherService(Client);
            PreviousFactory = fixture.Window.WeatherServiceFactory; PreviousObserver = fixture.Window.WeatherLocationOperationObserved;
            fixture.Window.WeatherServiceFactory = () => Service;
            fixture.Window.WeatherLocationOperationObserved = operation => Operations.Add(operation);
        }
        public async ValueTask DisposeAsync()
        {
            try
            {
                Service.Stop(); foreach (var request in Transport.Requests) request.Response.TrySetCanceled();
                await Task.WhenAll(Operations.ToArray()).WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                try { await Fixture.DisposeAsync(); }
                finally
                {
                    Fixture.Window.WeatherServiceFactory = PreviousFactory; Fixture.Window.WeatherLocationOperationObserved = PreviousObserver;
                    Service.Dispose(); Client.Dispose();
                    foreach (var request in Transport.Requests)
                        if (request.Response.Task.IsCompletedSuccessfully) request.Response.Task.Result.Dispose();
                }
            }
        }
    }
    private static async Task<Button> LocationStatusApplyButton(LocationStatusScope scope)
    {
        var f = scope.Fixture; var workspace = Assert.Single(f.Window.GetVisualDescendants().OfType<SettingsWorkspace>(), w => w.IsEffectivelyVisible);
        Key(f.Window, PhysicalKey.F, RawInputModifiers.Control);
        var search = Assert.Single(workspace.GetVisualDescendants().OfType<OperatorSearchBox>(), x => x.IsEffectivelyVisible);
        Assert.True(search.IsFocused); f.Window.KeyTextInput("Weather location"); await Settle(150);
        var editor = Named<ComboBox>(f.Window, "SettingsWeatherLocation");
        var row = Assert.IsAssignableFrom<Panel>(editor.Parent);
        return Assert.Single(row.Children.OfType<Button>(), button => Equals(button.Content, "Update"));
    }
    private static async Task LocationStatusApply(LocationStatusScope scope, Button apply, string location)
    {
        Named<ComboBox>(scope.Fixture.Window, "SettingsWeatherLocation").Text = location;
        int before = scope.Operations.Count;
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(apply)).Invoke();
        await Until(() => scope.Operations.Count == before + 1, "The original settings Update action must reach its observed real controller.");
    }
    private static async Task LocationStatusReply(LocationStatusScope scope, bool available)
    {
        await Until(() => scope.Transport.Requests.Count == 1, "Original provider must request the local controlled geocode.");
        var geocode = Assert.Single(scope.Transport.Requests); Assert.True(geocode.Geocode);
        geocode.Reply(available ? "{\"results\":[{\"latitude\":-37.8,\"longitude\":145.0}]}" : "{\"results\":[]}");
        if (!available) return;
        await Until(() => scope.Transport.Requests.Count == 2, "Only a resolved location must request its forecast.");
        var forecast = scope.Transport.Requests.Last(); Assert.False(forecast.Geocode);
        forecast.Reply("""{"current":{"temperature_2m":17,"apparent_temperature":16,"weather_code":0},"daily":{"temperature_2m_min":[10],"temperature_2m_max":[22]},"hourly":{"time":[],"temperature_2m":[],"weather_code":[],"precipitation":[],"precipitation_probability":[]}}""");
    }
    private static async Task LocationStatusReveal(LocationStatusScope scope, Button apply, TextBlock status)
    {
        var f = scope.Fixture; var workspace = Assert.Single(f.Window.GetVisualDescendants().OfType<SettingsWorkspace>(), w => w.IsEffectivelyVisible);
        var outer = Named<ScrollViewer>(f.Window, "AflSettingsPageViewport");
        var row = Assert.IsAssignableFrom<Panel>(apply.Parent);
        Avalonia.Rect Pair() => CropSliderBox(f.Window, row).Union(CropSliderBox(f.Window, status));
        bool Ready()
        {
            using var frame = f.Window.CaptureRenderedFrame();
            var visible = CropSliderBox(f.Window, workspace.Scroller).Intersect(CropSliderBox(f.Window, outer)).Intersect(new Avalonia.Rect(f.Window.ClientSize));
            return status.Bounds.Height > 0 && visible.Contains(Pair()) && Within(apply, f.Window.InputHitTest(CropSliderBox(f.Window, apply).Center));
        }
        double innerPrevious = 0, outerPrevious = 0, innerAmount = 1, outerAmount = 1;
        for (int i = 0; i < 45 && !Ready(); i++)
        {
            var inside = CropSliderBox(f.Window, workspace.Scroller); var pair = Pair();
            var viewport = CropSliderBox(f.Window, outer).Intersect(new Avalonia.Rect(f.Window.ClientSize));
            // The short page initially puts its entire inner form below the
            // outer viewport. Scroll the existing whole page first.
            bool useInner = (pair.Top < inside.Top || pair.Bottom > inside.Bottom)
                && viewport.Intersect(inside).Height > 0;
            if (useInner) viewport = viewport.Intersect(inside);
            Assert.True(viewport.Height > 0 && viewport.Width > 0);
            double direction = pair.Center.Y < (useInner ? inside.Center.Y : viewport.Center.Y) ? 1 : -1;
            if (useInner) { if (innerPrevious != 0 && innerPrevious != direction) innerAmount *= .5; innerPrevious = direction; }
            else { if (outerPrevious != 0 && outerPrevious != direction) outerAmount *= .5; outerPrevious = direction; }
            f.Window.MouseWheel(new Avalonia.Point(viewport.Left + 3, viewport.Center.Y), new Avalonia.Vector(0, direction * (useInner ? innerAmount : outerAmount)), RawInputModifiers.None);
            await Settle(50);
        }
        bool ready = Ready();
        if (!ready)
        {
            Capture(f.Window, "weather-location-layout-failed-" + f.Window.Width);
            DeltaOwnerRecord("weather-location-layout-failed-" + f.Window.Width, new { pair = Pair(),
                inner = CropSliderBox(f.Window, workspace.Scroller), outer = CropSliderBox(f.Window, outer),
                innerOffset = workspace.Scroller.Offset, outerOffset = outer.Offset });
        }
        Assert.True(ready, "Weather editor, Update action and full status must fit the real visible form together.");
    }
    [AvaloniaTheory]
    [InlineData("loading", 1280, 850)] [InlineData("available", 1280, 850)] [InlineData("unavailable", 1280, 850)]
    [InlineData("loading", 420, 320)] [InlineData("available", 420, 320)] [InlineData("unavailable", 420, 320)]
    public async Task OriginalSettingsWeatherApplyReportsActualProviderPhaseWithoutChangingMatchOrOutput(string phase, int width, int height)
    {
        await using var scope = new LocationStatusScope(await AflFixture.Create(width, height));
        var f = scope.Fixture; var apply = await LocationStatusApplyButton(scope);
        string model = ManualWeatherModel(f); bool open = PresWindowOpen(f);
        await LocationStatusApply(scope, apply, "Controlled venue A");
        var operation = Assert.Single(scope.Operations); var status = Named<TextBlock>(f.Window, "WeatherLocationStatus");
        if (phase == "loading")
        {
            Assert.False(operation.IsCompleted); Assert.Equal("Loading weather for Controlled venue A…", status.Text);
        }
        else
        {
            await LocationStatusReply(scope, phase == "available");
            await operation.WaitAsync(TimeSpan.FromSeconds(8)); await Settle(30);
            if (phase == "available") Assert.Equal("Weather data available for Controlled venue A", status.Text);
            else { Assert.Contains("unavailable", status.Text!, StringComparison.OrdinalIgnoreCase); Assert.Contains("Controlled venue A", status.Text!); }
        }
        Assert.Equal(model, ManualWeatherModel(f)); Assert.Equal(open, PresWindowOpen(f)); Assert.True(apply.IsEnabled);
        Assert.Equal("Controlled venue A", Named<ComboBox>(f.Window, "SettingsWeatherLocation").Text);
        await LocationStatusReveal(scope, apply, status);
        Capture(f.Window, "weather-location-status-" + phase + "-" + width);
        Assert.Equal(model, ManualWeatherModel(f)); Assert.Equal(open, PresWindowOpen(f));
        Assert.Equal("Update weather location", ControlAutomationPeer.CreatePeerForElement(apply)?.GetName());
        var editorPart = Assert.Single(Named<ComboBox>(f.Window, "SettingsWeatherLocation").GetVisualDescendants().OfType<TextBox>(), t => t.Name == "PART_EditableTextBox");
        Assert.True(editorPart.IsEffectivelyVisible); Assert.Equal("Controlled venue A", editorPart.Text);
        DeltaOwnerRecord("weather-location-status-" + phase + "-" + width, new { phase, width, height, status.Text, requests = scope.Transport.Requests.Count, operation.IsCompleted });
    }

    [AvaloniaTheory]
    [InlineData(1280, 850)] [InlineData(420, 320)]
    public async Task WeatherLocationNativeEditorTypingKeepsDraftUntilExplicitUpdate(int width, int height)
    {
        await using var scope = new LocationStatusScope(await AflFixture.Create(width, height));
        var f = scope.Fixture; var apply = await LocationStatusApplyButton(scope);
        await LocationStatusApply(scope, apply, "Controlled venue A");
        var status = Named<TextBlock>(f.Window, "WeatherLocationStatus");
        await LocationStatusReveal(scope, apply, status);
        var combo = Named<ComboBox>(f.Window, "SettingsWeatherLocation");
        var editor = Assert.Single(combo.GetVisualDescendants().OfType<TextBox>(), t => t.Name == "PART_EditableTextBox");
        Assert.Equal("Controlled venue A", editor.Text);
        Assert.True(editor.Focus(NavigationMethod.Tab));
        Key(f.Window, PhysicalKey.A, RawInputModifiers.Control);
        f.Window.KeyTextInput("Unapplied venue draft"); await Settle(50);
        Assert.Equal("Unapplied venue draft", editor.Text); Assert.Equal(editor.Text, combo.Text);
        Assert.Equal(editor.Text!.Length, editor.CaretIndex);
        _ = Assert.Single(scope.Operations); Assert.Single(scope.Transport.Requests);
        string model = ManualWeatherModel(f);
        await LocationStatusCompleteLatest(scope);
        await Task.WhenAll(scope.Operations.ToArray()).WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal("Unapplied venue draft", editor.Text); Assert.Equal(editor.Text, combo.Text);
        Assert.Equal("Weather data available for Controlled venue A", status.Text);
        Assert.Equal(model, ManualWeatherModel(f)); _ = Assert.Single(scope.Operations);
        Capture(f.Window, "weather-location-native-editor-draft-" + width);
    }

    private static async Task LocationStatusCompleteLatest(LocationStatusScope scope)
    {
        var geocode = scope.Transport.Requests.Last(); Assert.True(geocode.Geocode);
        int count = scope.Transport.Requests.Count;
        geocode.Reply("{\"results\":[{\"latitude\":-37.8,\"longitude\":145.0}]}");
        await Until(() => scope.Transport.Requests.Count == count + 1, "Current local forecast must follow its geocode.");
        var forecast = scope.Transport.Requests.Last(); Assert.False(forecast.Geocode);
        forecast.Reply("""{"current":{"temperature_2m":23,"apparent_temperature":22,"weather_code":0},"daily":{"temperature_2m_min":[10],"temperature_2m_max":[25]},"hourly":{"time":[],"temperature_2m":[],"weather_code":[],"precipitation":[],"precipitation_probability":[]}}""");
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task WeatherLocationNewApplyOwnsLoadingAndCompletionAfterOldRequestRetires(bool sameVenue)
    {
        await using var scope = new LocationStatusScope(await AflFixture.Create(1280, 850));
        var f = scope.Fixture; var apply = await LocationStatusApplyButton(scope);
        string model = ManualWeatherModel(f); var status = Named<TextBlock>(f.Window, "WeatherLocationStatus");
        await LocationStatusApply(scope, apply, "Controlled venue A");
        var old = Assert.Single(scope.Operations); string latest = sameVenue ? "Controlled venue A" : "Controlled venue B";
        await LocationStatusApply(scope, apply, latest);
        await old.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal("Loading weather for " + latest + "…", status.Text); Assert.True(apply.IsEnabled);
        await LocationStatusCompleteLatest(scope); await Task.WhenAll(scope.Operations.ToArray()).WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal("Weather data available for " + latest, status.Text); Assert.Equal(23d, scope.Service.LatestSnapshot?.CurrentTemp);
        Assert.Equal(model, ManualWeatherModel(f)); Assert.Equal(latest, Named<ComboBox>(f.Window, "SettingsWeatherLocation").Text);
    }

    [AvaloniaFact]
    public async Task WeatherLocationDisableRetiresPendingStatusAndDoesNotRestoreItAfterCancellation()
    {
        await using var scope = new LocationStatusScope(await AflFixture.Create(1280, 850));
        var f = scope.Fixture; var apply = await LocationStatusApplyButton(scope); string model = ManualWeatherModel(f);
        await LocationStatusApply(scope, apply, "Controlled venue A");
        await LocationStatusApply(scope, apply, ""); await Task.WhenAll(scope.Operations.ToArray()).WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal("Weather disabled", Named<TextBlock>(f.Window, "WeatherLocationStatus").Text);
        Assert.Null(scope.Service.LatestSnapshot); Assert.Equal(model, ManualWeatherModel(f));
    }

    [AvaloniaFact]
    public async Task WeatherLocationCompletionKeepsUnappliedEditorDraftAndOrdinaryScores()
    {
        await using var scope = new LocationStatusScope(await AflFixture.Create(1280, 850));
        var f = scope.Fixture; var apply = await LocationStatusApplyButton(scope);
        await LocationStatusApply(scope, apply, "Controlled venue A");
        Named<ComboBox>(f.Window, "SettingsWeatherLocation").Text = "Unapplied venue draft";
        f.Match.AddGoal(Roche_Scoreboard.Models.TeamSide.Home); string model = ManualWeatherModel(f);
        Assert.Single(scope.Transport.Requests);
        await LocationStatusCompleteLatest(scope); await Task.WhenAll(scope.Operations.ToArray()).WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal("Unapplied venue draft", Named<ComboBox>(f.Window, "SettingsWeatherLocation").Text);
        Assert.Equal("Weather data available for Controlled venue A", Named<TextBlock>(f.Window, "WeatherLocationStatus").Text);
        Assert.Equal(model, ManualWeatherModel(f));
    }

    [AvaloniaFact]
    public async Task ReentrantWeatherLocationCommandDuringLoadingCannotBeStoppedOrRepaintedByOldCaller()
    {
        await using var scope = new LocationStatusScope(await AflFixture.Create(1280, 850));
        var f = scope.Fixture; var apply = await LocationStatusApplyButton(scope); var status = Named<TextBlock>(f.Window, "WeatherLocationStatus");
        bool changed = false;
        void Redirect(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
        {
            if (changed || args.Property != TextBlock.TextProperty || status.Text != "Loading weather for Controlled venue A…") return;
            changed = true; ((Roche_Scoreboard.Services.Cloud.IRemoteCommandTarget)f.Window).SetWeatherLocation("Controlled venue B");
        }
        status.PropertyChanged += Redirect;
        try
        {
            Named<ComboBox>(f.Window, "SettingsWeatherLocation").Text = "Controlled venue A";
            Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(apply)).Invoke();
            await Until(() => scope.Operations.Count == 2 && scope.Transport.Requests.Count == 2, "Reentrant public B command and original A must both be observed without duplicating requests.");
            Assert.True(changed); Assert.Equal("Loading weather for Controlled venue B…", status.Text);
            await LocationStatusCompleteLatest(scope); await Task.WhenAll(scope.Operations.ToArray()).WaitAsync(TimeSpan.FromSeconds(8));
            Assert.Equal("Weather data available for Controlled venue B", status.Text); Assert.Equal(23d, scope.Service.LatestSnapshot?.CurrentTemp);
        }
        finally { status.PropertyChanged -= Redirect; }
    }

    [AvaloniaFact]
    public async Task ApplyingSameSettingsVenueRetiresAnOlderManualOverlayWithoutLateAirOrFeedback()
    {
        await ManualWeatherControlledScope.Run(async controlled =>
        {
            var f = controlled.Fixture; var bug = await ManualWeatherPrepare(f, controlled, false);
            var pending = await ManualWeatherInvoke(f, controlled, "FORECAST");
            Key(f.Window, PhysicalKey.Digit4, RawInputModifiers.Control); await Settle(150);
            Key(f.Window, PhysicalKey.F, RawInputModifiers.Control);
            var workspace = Assert.Single(f.Window.GetVisualDescendants().OfType<SettingsWorkspace>(), w => w.IsEffectivelyVisible);
            var search = Assert.Single(workspace.GetVisualDescendants().OfType<OperatorSearchBox>(), x => x.IsEffectivelyVisible);
            Assert.True(search.IsFocused); f.Window.KeyTextInput("Weather location"); await Settle(150);
            var editor = Named<ComboBox>(f.Window, "SettingsWeatherLocation"); Assert.True(editor.IsEffectivelyVisible);
            var row = Assert.IsAssignableFrom<Panel>(editor.Parent); var apply = Assert.Single(row.Children.OfType<Button>(), b => Equals(b.Content, "Update"));
            Assert.NotNull(ManualWeatherRead<object?>(f.Window, "_manualWeatherPendingRequest"));
            string model = ManualWeatherModel(f); editor.Text = "Controlled venue A";
            Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(apply)).Invoke();
            await Until(() => ManualWeatherRead<object?>(f.Window, "_manualWeatherPendingRequest") is null, "New settings intent must retire the older pending overlay.");
            string? status = Named<TextBlock>(f.Window, "WeatherLocationStatus").Text;
            await ManualWeatherComplete(pending, ManualWeatherSnapshot(17));
            Assert.Equal(status, Named<TextBlock>(f.Window, "WeatherLocationStatus").Text);
            Assert.Null(ManualWeatherOverlay(bug)); Assert.Null(bug.GetWeatherSnapshot()); Assert.Empty(controlled.Feedback);
            Assert.Equal(model, ManualWeatherModel(f));
        });
    }
}

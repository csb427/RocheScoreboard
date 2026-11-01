using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    private sealed class CricketWeatherHttp : HttpMessageHandler
    {
        internal sealed record Request(Uri Uri,TaskCompletionSource<HttpResponseMessage> Reply);
        internal readonly ConcurrentQueue<Request> Requests=new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var pending=new Request(request.RequestUri!,new(TaskCreationOptions.RunContinuationsAsynchronously));Requests.Enqueue(pending);
            return await pending.Reply.Task.WaitAsync(token);
        }
        internal void Reply(int index,string body)=>Requests.ElementAt(index).Reply.TrySetResult(new(HttpStatusCode.OK){Content=new StringContent(body)});
    }
    private sealed class CricketWeatherScope : IAsyncDisposable
    {
        internal readonly Scope Storage=new(true);
        internal readonly CricketWeatherHttp Transport=new();
        internal readonly HttpClient Client;
        internal readonly WeatherService Service;
        internal readonly List<Task> Operations=new();
        internal readonly Roche_Scoreboard.MainWindow Window;
        internal CricketControlPanel Panel=>Named<CricketControlPanel>(Window,"CricketControlPanelView");
        internal CricketMatchManager Match=>Read<CricketMatchManager>(Window,"_cricketMatch");
        internal CricketWeatherScope()
        {
            var seed=CricketReliabilityChecks.CreateMatch(true);seed.WeatherLocation="";
            GameStateService.Save(new GameState{Sport="cricket",CricketMatchState=seed.CapturePersistenceState()});
            Window=Storage.Main();Client=new HttpClient(Transport);Service=new WeatherService(Client);
            Window.WeatherServiceFactory=()=>Service;Window.WeatherLocationOperationObserved=operation=>Operations.Add(operation);
        }
        internal async Task Prepare(int width=1280,int height=850)
        {
            await Settle(350);var home=Named<SportSelectionScreen>(Window,"SportSelectionPanel");home.PrepareForDisplay();
            var resume=Named<Button>(home,"HomeResumeButton");await StableReachable(Window,resume);await Tap(Window,resume);
            var recovery=Assert.Single(Window.GetVisualDescendants().OfType<ResumeMatchPrompt>());var confirm=Named<Button>(recovery,"ResumeButton");await StableReachable(Window,confirm);await Tap(Window,confirm);
            await Until(()=>Panel.IsEffectivelyVisible&&!Named<Control>(Window,"RecoveryOverlay").IsVisible,"Actual Home recovery must reveal the real cricket operator.");
            Window.Width=width;Window.Height=height;await SelectOutput();
            Assert.Equal(SportMode.Cricket,Read<SportMode>(Window,"_sportMode"));Assert.Same(Match,Read<CricketMatchManager>(Panel,"_match"));Assert.False(Read<Window?>(Window,"_cricketDisplayWindow")?.IsVisible==true);Assert.True(Read<bool>(Window,"_outputActivationRequired"));
        }
        internal async Task SelectOutput()
        {var tabs=Named<TabControl>(Panel,"MainTabControl");tabs.SelectedItem=Assert.Single(tabs.Items.OfType<TabItem>(),x=>x.Name=="OutputTab");await Settle(350);}
        internal Button Update=>Assert.Single(Panel.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Content,"Update weather"));
        internal async Task UpdateVenue(string venue)
        {
            Named<TextBox>(Panel,"CricketWeatherLocation").Text=venue;await Reveal(Window,Update);await StableReachable(Window,Update);
            var app=Assert.IsType<RocheScoreboard.Mac.App>(Application.Current);bool selfTest=app.IsSelfTestMode;
            // Run the original event subscription; the existing factory uses
            // this deterministic transport, so no real HTTP is possible.
            try{app.IsSelfTestMode=false;TapNow(Window,Update);}finally{app.IsSelfTestMode=selfTest;}
        }
        internal async Task Resolve(int geocode,bool available)
        {
            Transport.Reply(geocode,available?"{\"results\":[{\"latitude\":-37.8,\"longitude\":145}]}":"{\"results\":[]}");
            if(!available)return;
            await Until(()=>Transport.Requests.Count>geocode+1,"Resolved venue must request its forecast.");
            Transport.Reply(geocode+1,"""{"current":{"temperature_2m":17,"apparent_temperature":16,"weather_code":0},"daily":{"temperature_2m_min":[10],"temperature_2m_max":[22]},"hourly":{"time":[],"temperature_2m":[],"weather_code":[],"precipitation":[],"precipitation_probability":[]}}""");
        }
        public async ValueTask DisposeAsync()
        {
            Service.Stop();foreach(var request in Transport.Requests)request.Reply.TrySetCanceled();
            try{await Task.WhenAll(Operations).WaitAsync(TimeSpan.FromSeconds(10));}
            finally{try{Storage.Dispose();}finally{Service.Dispose();Client.Dispose();foreach(var request in Transport.Requests)if(request.Reply.Task.IsCompletedSuccessfully)request.Reply.Task.Result.Dispose();}}
        }
    }
    private static async Task CricketWeatherReveal(CricketWeatherScope f)
    {
        var status=Named<TextBlock>(f.Panel,"CricketWeatherStatus");await Reveal(f.Window,f.Update);
        Rect Visible()=>status.GetVisualAncestors().OfType<ScrollViewer>().Select(x=>Painted(f.Window,x)).Aggregate(new Rect(f.Window.ClientSize),(visible,next)=>visible.Intersect(next));
        double previous=0,amount=1;
        for(int i=0;i<24;i++)
        {
            using var frame=f.Window.CaptureRenderedFrame();var visible=Visible();var pair=Painted(f.Window,status).Union(Painted(f.Window,f.Update));
            if(visible.Contains(pair))break;
            double direction=pair.Center.Y<visible.Center.Y?1:-1;if(previous!=0 && previous!=direction)amount*=.5;previous=direction;
            f.Window.MouseWheel(new Point(visible.Left+3,visible.Center.Y),new Vector(0,direction*amount),Avalonia.Input.RawInputModifiers.None);await Settle(60);
        }
        Assert.True(Visible().Contains(Painted(f.Window,status)));Assert.True(Visible().Contains(Painted(f.Window,f.Update)));await StableReachable(f.Window,f.Update);
    }
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketWeatherFeedback")]
    public async Task BlankCricketVenueExplainsItsRejectionWithoutChangingMatchOrStartingTransport(int width,int height)
    {
        await using var f=new CricketWeatherScope();await f.Prepare(width,height);string before=State(f.Match);int changes=0;f.Match.MatchChanged+=()=>++changes;
        await f.UpdateVenue("   ");await Settle(100);Assert.Equal(before,State(f.Match));Assert.Equal(0,changes);Assert.Empty(f.Operations);Assert.Empty(f.Transport.Requests);
        var status=Named<TextBlock>(f.Panel,"CricketWeatherStatus");Assert.Contains("Enter a venue",status.Text);Assert.Equal("Weather venue",ControlAutomationPeer.CreatePeerForElement(Named<TextBox>(f.Panel,"CricketWeatherLocation"))!.GetName());
        await CricketWeatherReveal(f);PromptCapture(f.Window,"cricket-weather-blank-"+width);Assert.False(Read<Window?>(f.Window,"_cricketDisplayWindow")?.IsVisible==true);Assert.True(Read<bool>(f.Window,"_outputActivationRequired"));
    }
    [AvaloniaTheory]
    [InlineData(true,1280,850)][InlineData(false,420,320)]
    [Trait("Evidence","CricketWeatherFeedback")]
    public async Task ActualCricketUpdateReportsTheRealProviderAndDoesNotBlockScoring(bool available,int width,int height)
    {
        await using var f=new CricketWeatherScope();await f.Prepare(width,height);int changes=0;f.Match.MatchChanged+=()=>++changes;
        await f.UpdateVenue("Controlled venue A");await Until(()=>f.Transport.Requests.Count==1,"Update must reach the existing controlled provider.");
        var status=Named<TextBlock>(f.Panel,"CricketWeatherStatus");Assert.Equal("Loading weather for Controlled venue A…",status.Text);Assert.False(Assert.Single(f.Operations).IsCompleted);Assert.Equal(1,changes);
        f.Match.RecordDelivery(1,CricketDeliveryType.Runs);Assert.Equal(1,f.Match.TotalRuns);string scored=State(f.Match);
        await f.Resolve(0,available);await Task.WhenAll(f.Operations).WaitAsync(TimeSpan.FromSeconds(5));await Settle(150);
        Assert.Equal(available?"Weather data available for Controlled venue A":"Weather unavailable for Controlled venue A. Check the venue or try Update again.",status.Text);
        Assert.Equal(scored,State(f.Match));Assert.Equal(2,changes);Assert.False(Read<Window?>(f.Window,"_cricketDisplayWindow")?.IsVisible==true);Assert.True(Read<bool>(f.Window,"_outputActivationRequired"));
        if(available){Assert.Contains("17",Named<TextBlock>(f.Panel,"CricketWeatherTemperature").Text);Assert.Equal("Weather for Controlled venue A",Named<TextBlock>(f.Panel,"CricketWeatherDataVenue").Text);}else Assert.Null(f.Service.LatestSnapshot);
        await CricketWeatherReveal(f);PromptCapture(f.Window,"cricket-weather-provider-"+available+"-"+width);
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketWeatherFeedback")]
    public async Task CompletedCricketWeatherCannotRelabelANewerUnappliedVenueDraft()
    {
        await using var f=new CricketWeatherScope();await f.Prepare();await f.UpdateVenue("Controlled venue A");await Until(()=>f.Transport.Requests.Count==1,"First venue request pending.");
        Named<TextBox>(f.Panel,"CricketWeatherLocation").Text="Draft venue B";var status=Named<TextBlock>(f.Panel,"CricketWeatherStatus");await Until(()=>status.Text?.Contains("Choose Update weather")==true,"The actual queued native TextChanged event must report unapplied venue intent.");string feedback=status.Text!;
        await f.Resolve(0,true);await Task.WhenAll(f.Operations).WaitAsync(TimeSpan.FromSeconds(5));await Settle(100);
        Assert.Equal(feedback,status.Text);Assert.Equal("Controlled venue A",f.Match.WeatherLocation);Assert.Equal("Draft venue B",Named<TextBox>(f.Panel,"CricketWeatherLocation").Text);Assert.Equal("Weather for Controlled venue A",Named<TextBlock>(f.Panel,"CricketWeatherDataVenue").Text);
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketWeatherFeedback")]
    public async Task ReplacedCricketPanelCannotReceiveTheOldRequestsFiguresOrAvailability()
    {
        await using var f=new CricketWeatherScope();await f.Prepare();await f.UpdateVenue("Controlled venue A");await Until(()=>f.Transport.Requests.Count==1,"First venue request pending.");
        var replacement=CricketReliabilityChecks.CreateMatch(true);replacement.WeatherLocation="Replacement venue";f.Panel.SetMatch(replacement);await f.SelectOutput();string before=State(replacement);string feedback=Named<TextBlock>(f.Panel,"CricketWeatherStatus").Text!;
        await f.Resolve(0,true);await Task.WhenAll(f.Operations).WaitAsync(TimeSpan.FromSeconds(5));await Settle(100);
        Assert.Equal(feedback,Named<TextBlock>(f.Panel,"CricketWeatherStatus").Text);Assert.Equal("—",Named<TextBlock>(f.Panel,"CricketWeatherTemperature").Text);Assert.Equal(before,State(replacement));Assert.Same(replacement,Read<CricketMatchManager>(f.Panel,"_match"));
    }

    [AvaloniaFact]
    [Trait("Evidence","CricketWeatherFeedback")]
    public async Task WeatherRetirementCannotClearANewMatchBoundByItsPublicTextObserver()
    {
        await using var f=new CricketWeatherScope();await f.Prepare();
        var temperature=Named<TextBlock>(f.Panel,"CricketWeatherTemperature");var replacement=CricketReliabilityChecks.CreateMatch(true);replacement.RecordDelivery(4,CricketDeliveryType.Four);replacement.WeatherLocation="New venue";
        string before=State(replacement);bool rebound=false;
        temperature.PropertyChanged+=(_,args)=>{if(rebound || args.Property!=TextBlock.TextProperty || args.NewValue is not "")return;rebound=true;f.Panel.SetMatch(replacement);};
        f.Panel.ClearMatch();await Settle(150);Assert.True(rebound);Assert.Same(replacement,Read<CricketMatchManager>(f.Panel,"_match"));Assert.Equal(before,State(replacement));
        Assert.Equal(1,Read<int>(f.Panel,"_loggedDeliveryCount"));await f.SelectOutput();Assert.Equal("—",Named<TextBlock>(f.Panel,"CricketWeatherTemperature").Text);Assert.Equal("Weather will update for your selected venue.",Named<TextBlock>(f.Panel,"CricketWeatherDescription").Text);
    }
}

using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Services.Cloud;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    // No transport/network/real credentials. Public host lifecycle still owns
    // generation, subscriptions, snapshots and UI refresh.
    private sealed class ShareCardSession(string code) : ICloudRemoteSession
    {
        public bool IsActive { get; private set; }
        public string? JoinCode => IsActive ? code : null;
        public Guid? SessionId => IsActive ? id : null;
        private readonly Guid id = Guid.NewGuid();
        public event Action<bool>? ConnectedChanged;
        public Task StartAsync(string sport,string home,string away,LiveStateSnapshot snapshot,CancellationToken ct)
        { IsActive=true; return Task.CompletedTask; }
        public void PublishSnapshot(LiveStateSnapshot snapshot) { }
        internal void Connection(bool connected)=>ConnectedChanged?.Invoke(connected);
        public ValueTask DisposeAsync() { IsActive=false; return ValueTask.CompletedTask; }
    }
    private static async Task OpenShareCard(AflFixture f)
    {
        Key(f.Window,PhysicalKey.F,RawInputModifiers.Control);
        var workspace=Assert.Single(f.Window.GetVisualDescendants().OfType<SettingsWorkspace>(), x=>x.IsEffectivelyVisible);
        var search=Assert.Single(workspace.GetVisualDescendants().OfType<OperatorSearchBox>(),x=>x.IsEffectivelyVisible);
        Assert.True(search.IsFocused);f.Window.KeyTextInput("Cloud remote");await Settle(160);
    }
    private static ShareCardSession ConfigureShareCard(AflFixture f,string code)
    {
        var session=new ShareCardSession(code);
        f.Window.SetCloudSessionTestConfiguration(new SupabaseConfig {
            Url="https://share-card.example.invalid",AnonKey="fixture-only-anon",ServiceRoleKey="fixture-only-host",
            RemoteAppBaseUrl="https://remote.example.invalid" });
        f.Window.CloudSessionFactory=(_,_,_)=>session;
        return session;
    }
    [AvaloniaTheory]
    [InlineData(1280,850,false)][InlineData(420,320,false)]
    [InlineData(1280,850,true)][InlineData(420,320,true)]
    [Trait("Evidence","AflCloudShareCard")]
    public async Task ActualAflSettingsCloudCardPreservesShareValuesAndScore(int width,int height,bool reduced)
    {
        await using var f=await AflFixture.Create(width,height);
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,reduced);OperatorMotion.AnimationOverride=null;
        var session=ConfigureShareCard(f,"K7HM-2QPR");
        string score=Scores(f.Match);
        await f.Window.StartCloudSessionAsync();await Settle(160);session.Connection(true);await Settle(80);
        await OpenShareCard(f);
        var copyCode=Named<Button>(f.Window,"CloudCopyCodeButton");
        var copyLink=Named<Button>(f.Window,"CloudCopyUrlButton");
        var receipt=Named<TextBlock>(f.Window,"CloudCopyFeedback");
        var copied=new List<string>();
        f.Window.CloudShareClipboardWriter=value=>{copied.Add(value);return true;};
        await Mr6Reveal(f,copyCode);Capture(f.Window,$"cloud-code-{width}-{reduced}");
        Assert.Equal("Copy cloud join code",ControlAutomationPeer.CreatePeerForElement(copyCode)?.GetName());
        Assert.True(copyCode.Bounds.Height>=44);
        await Tap(f.Window,copyCode);Assert.Equal("Join code copied.",receipt.Text);
        Assert.Equal(new[]{"K7HM-2QPR"},copied);
        // Keyboard equivalent and fast repeat leave share text authoritative.
        Assert.True(copyCode.Focus(NavigationMethod.Tab));Key(f.Window,PhysicalKey.Enter);await Settle(30);
        Assert.Equal(2,copied.Count);Assert.All(copied,x=>Assert.Equal("K7HM-2QPR",x));
        await Mr6Reveal(f,copyLink);Capture(f.Window,$"cloud-link-{width}-{reduced}");
        Assert.True(copyLink.Bounds.Height>=44);await Tap(f.Window,copyLink);
        Assert.Equal("Join link copied.",receipt.Text);
        Assert.Equal("https://remote.example.invalid/?code=K7HM-2QPR",copied.Last());
        f.Window.CloudShareClipboardWriter=_=>false;await Tap(f.Window,copyLink);
        Assert.StartsWith("Clipboard unavailable.",receipt.Text);
        Assert.Equal("K7HM-2QPR",Named<TextBlock>(f.Window,"CloudJoinCodeText").Text);
        Assert.Equal("https://remote.example.invalid/?code=K7HM-2QPR",Named<TextBlock>(f.Window,"CloudJoinUrlText").Text);
        Assert.Equal(score,Scores(f.Match));Assert.True(f.Window.IsCloudSessionActive);
        // Real End remains an owned confirmation (source contract/manual
        // WPF check); public fake lifecycle isolates old-copy restoration.
        await f.Window.EndCloudSessionAsync();await Settle(80);
        var next=ConfigureShareCard(f,"NEXT-7QPR");await f.Window.StartCloudSessionAsync();await Settle(160);
        Assert.Equal("",receipt.Text);
        Assert.Equal("Waiting for cloud connection",Named<TextBlock>(f.Window,"CloudConnectionLabel").Text);
        Assert.DoesNotContain("cloud connected",Named<TextBlock>(f.Window,"CloudStatusText").Text);
        await Settle(1600); // Beyond the old unconditional1.5s restore.
        Assert.Equal("NEXT-7QPR",Named<TextBlock>(f.Window,"CloudJoinCodeText").Text);
        Assert.Equal("https://remote.example.invalid/?code=NEXT-7QPR",Named<TextBlock>(f.Window,"CloudJoinUrlText").Text);
        next.Connection(true);await Settle(50);
        Assert.Equal("Cloud connected",Named<TextBlock>(f.Window,"CloudConnectionLabel").Text);
        await f.Window.EndCloudSessionAsync();await Settle(100);
        var start=Named<Button>(f.Window,"CloudStartButton");await Mr6Reveal(f,start);
        Assert.True(start.Bounds.Height>=44);Capture(f.Window,$"cloud-idle-{width}-{reduced}");
        Assert.Equal("",receipt.Text);Assert.Equal(score,Scores(f.Match));Assert.False(f.Window.IsCloudSessionActive);
    }

    [AvaloniaFact]
    [Trait("Evidence","AflCloudShareCard")]
    public async Task CloudShareReceiptRejectsSessionReplacementDuringClipboardWrite()
    {
        await using var f=await AflFixture.Create(1280,850);
        ConfigureShareCard(f,"OLD-2QPR");await f.Window.StartCloudSessionAsync();await Settle(150);await OpenShareCard(f);
        string score=Scores(f.Match);
        f.Window.CloudShareClipboardWriter=value=> {
            Assert.Equal("OLD-2QPR",value);
            var end=f.Window.EndCloudSessionAsync();Assert.True(end.IsCompletedSuccessfully);
            ConfigureShareCard(f,"NEW-2QPR");var start=f.Window.StartCloudSessionAsync();Assert.True(start.IsCompletedSuccessfully);
            return true;
        };
        var code=Named<Button>(f.Window,"CloudCopyCodeButton");await Mr6Reveal(f,code);await Tap(f.Window,code);
        Assert.Equal("NEW-2QPR",Named<TextBlock>(f.Window,"CloudJoinCodeText").Text);
        Assert.Equal("",Named<TextBlock>(f.Window,"CloudCopyFeedback").Text);
        Assert.Equal(score,Scores(f.Match));Assert.True(f.Window.IsCloudSessionActive);
        await f.Window.EndCloudSessionAsync();
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    [Trait("Evidence","AflCloudShareCard")]
    public async Task ActualCloudEndConfirmationDefaultsToDeclineAndCannotEndReplacement(bool replace)
    {
        await using var f=await AflFixture.Create(1280,850);
        ConfigureShareCard(f,"ORIGINAL");await f.Window.StartCloudSessionAsync();await Settle(100);await OpenShareCard(f);
        string score=Scores(f.Match);bool seen=false,noDefault=false;Exception? callbackError=null;
        var timer=new Avalonia.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};
        var elapsed=System.Diagnostics.Stopwatch.StartNew();
        timer.Tick+=(_,_)=> {
            var dialog=f.Window.OwnedWindows.SingleOrDefault(x=>x.Title=="Cloud Remote");
            if(dialog is null) {
                if(elapsed.Elapsed>TimeSpan.FromSeconds(10)) {callbackError=new TimeoutException("Cloud End confirmation did not appear.");timer.Stop();}
                return;
            }
            timer.Stop();seen=true;
            try {
                var buttons=dialog.GetVisualDescendants().OfType<Button>().ToArray();
                var no=Assert.Single(buttons,x=>Equals(x.Content,"No"));
                noDefault=no.IsDefault;
                if(replace) {
                    Assert.True(f.Window.EndCloudSessionAsync().IsCompletedSuccessfully);
                    ConfigureShareCard(f,"REPLACEMENT");Assert.True(f.Window.StartCloudSessionAsync().IsCompletedSuccessfully);
                }
                var action=replace?Assert.Single(buttons,x=>Equals(x.Content,"Yes")):no;
                action.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            }
            catch(Exception error){callbackError=error;dialog.Close();}
        };
        try {
            var end=Named<Button>(f.Window,"CloudEndButton");await Mr6Reveal(f,end);timer.Start();
            await Tap(f.Window,end);await Settle(100);
            Assert.Null(callbackError);Assert.True(seen);Assert.True(noDefault,"Decline must be the explicit default, never disconnection.");
            Assert.True(f.Window.IsCloudSessionActive,"An old Yes/decline cannot end the current replacement session.");
            Assert.Equal(replace?"REPLACEMENT":"ORIGINAL",f.Window.CloudJoinCode);Assert.Equal(score,Scores(f.Match));
        }
        finally {timer.Stop();foreach(var child in f.Window.OwnedWindows.Where(x=>x.Title=="Cloud Remote").ToArray())child.Close();await f.Window.EndCloudSessionAsync();}
    }

}

using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Services.Cloud;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private sealed class PendingShareCardSession(string code) : ICloudRemoteSession
    {
        private readonly TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource dispose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Action<bool>? connection;
        public bool IsActive { get; private set; }
        public string? JoinCode => IsActive ? code : null;
        public Guid? SessionId { get; } = Guid.NewGuid();
        internal int StartCalls, DisposeCalls;
        public event Action<bool>? ConnectedChanged { add => connection += value; remove => connection -= value; }
        public async Task StartAsync(string sport,string home,string away,LiveStateSnapshot snapshot,CancellationToken ct)
        { StartCalls++; await start.Task; IsActive=true; }
        public void PublishSnapshot(LiveStateSnapshot snapshot) { }
        public void Connection(bool connected)=>connection?.Invoke(connected);
        internal void StartSucceeds()=>start.TrySetResult();
        internal void StartFails(bool canceled=false)
        { if(canceled)start.TrySetCanceled();else start.TrySetException(new IOException("Controlled start failure.")); }
        internal void FinishDisposal()=>dispose.TrySetResult();
        public ValueTask DisposeAsync() { DisposeCalls++; return new ValueTask(DisposeCore()); }
        private async Task DisposeCore() { await dispose.Task; IsActive=false; }
    }
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

    private static PendingShareCardSession ConfigurePendingShareCard(AflFixture f,string code)
    {
        var session=new PendingShareCardSession(code);
        f.Window.SetCloudSessionTestConfiguration(new SupabaseConfig {
            Url="https://share-card.example.invalid",AnonKey="fixture-only-anon",ServiceRoleKey="fixture-only-host",
            RemoteAppBaseUrl="https://remote.example.invalid" });
        f.Window.CloudSessionFactory=(_,_,_)=>session;
        f.Window.CloudSessionFailureSink=_=>{};
        return session;
    }

    [AvaloniaTheory]
    [InlineData("success")][InlineData("failure")][InlineData("cancel")]
    [Trait("Evidence","AflCloudShareCard")]
    public async Task ActualCloudStartButtonHasSingleRecoverableInlineBusyLifecycle(string outcome)
    {
        await using var f=await AflFixture.Create(1280,850);
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);
        var session=ConfigurePendingShareCard(f,"BUSY-7QPR");
        await OpenShareCard(f);
        var button=Named<Button>(f.Window,"CloudStartButton");
        await Mr6Reveal(f,button);
        var before=button.Bounds.Size;
        await Tap(f.Window,button);
        Assert.Equal(1,session.StartCalls);
        Assert.False(button.IsEnabled);
        Assert.True(OperatorControlMotion.GetIsBusy(button));
        Assert.Equal(before,button.Bounds.Size);
        Assert.Equal("Creating cloud session",AutomationProperties.GetName(button));
        Assert.Equal("Creating session…",AutomationProperties.GetItemStatus(button));
        Assert.Equal("Creating session…",Named<TextBlock>(f.Window,"CloudStartButtonLabel").Text);
        Assert.True(Named<ProgressBar>(f.Window,"CloudStartBusyProgress").IsVisible
            || Named<TextBlock>(f.Window,"CloudStartButtonLabel").Text == "Creating session…");
        Capture(f.Window,$"cloud-settings-start-busy-{outcome}");
        Assert.Equal("Creating session…",Named<TextBlock>(f.Window,"CloudStatusText").Text);
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1,session.StartCalls);

        if(outcome=="success") session.StartSucceeds(); else session.StartFails(outcome=="cancel");
        if(outcome!="success") await Until(()=>session.DisposeCalls>0,"Failed or cancelled start must enter controlled cleanup before releasing the fake session.");
        if(outcome!="success") session.FinishDisposal();
        await Until(()=>!OperatorControlMotion.GetIsBusy(button),"The initiating action must recover after its real async operation terminates.");
        Assert.True(button.Bounds.Size.Width>0);
        Assert.False(Named<ProgressBar>(f.Window,"CloudStartBusyProgress").IsVisible);
        Assert.False(OperatorControlMotion.GetIsBusy(button));
        if(outcome=="success") Assert.True(f.Window.IsCloudSessionActive);
        else
        {
            Assert.False(f.Window.IsCloudSessionActive);
            Assert.True(button.IsEnabled);
            var retry=ConfigurePendingShareCard(f,"RETRY-7QPR");
            await Tap(f.Window,button);
            Assert.Equal(1,retry.StartCalls);
            retry.StartSucceeds();
            await Until(()=>f.Window.IsCloudSessionActive,"A new start must remain possible after failure/cancellation cleanup.");
            var end=f.Window.EndCloudSessionAsync();retry.FinishDisposal();await end;
        }
        if(outcome=="success") { var task=f.Window.EndCloudSessionAsync();session.FinishDisposal();await task; }
    }

    [AvaloniaFact]
    [Trait("Evidence","AflCloudShareCard")]
    public async Task ActualCloudEndButtonRemainsBusyUntilConfirmedDisposalCompletes()
    {
        await using var f=await AflFixture.Create(1280,850);
        var session=ConfigurePendingShareCard(f,"END-7QPR");
        var start=f.Window.StartCloudSessionAsync();session.StartSucceeds();await start;
        await OpenShareCard(f);
        var button=Named<Button>(f.Window,"CloudEndButton");await Mr6Reveal(f,button);
        bool confirmed=false;Exception? callbackError=null;
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};
        timer.Tick+=(_,_)=>{
            var dialog=f.Window.OwnedWindows.SingleOrDefault(x=>x.Title=="Cloud Remote");
            if(dialog is null)return;
            timer.Stop();
            try { var yes=Assert.Single(dialog.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Content,"Yes"));yes.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));confirmed=true; }
            catch(Exception error){callbackError=error;dialog.Close();}
        };
        try
        {
            timer.Start();await Tap(f.Window,button);
            await Until(()=>confirmed||callbackError is not null,"The owned confirmation must resolve before ending the session.");
            Assert.Null(callbackError);Assert.False(f.Window.IsCloudSessionActive);
            Assert.True(Named<StackPanel>(f.Window,"CloudLiveState").IsVisible,"Keep the initiating live action visible while teardown is pending.");
            Assert.False(button.IsEnabled);Assert.True(OperatorControlMotion.GetIsBusy(button));
            Assert.Equal("Ending cloud session",AutomationProperties.GetName(button));
            Assert.Equal("Ending session…",AutomationProperties.GetItemStatus(button));
            Assert.Equal("Ending session…",Named<TextBlock>(f.Window,"CloudEndButtonLabel").Text);
            Assert.True(Named<ProgressBar>(f.Window,"CloudEndBusyProgress").IsVisible
                || Named<TextBlock>(f.Window,"CloudEndButtonLabel").Text == "Ending session…");
            Capture(f.Window,"cloud-settings-end-busy");
            Assert.Equal("Ending cloud session…",Named<TextBlock>(f.Window,"CloudStatusText").Text);
            session.FinishDisposal();
            await Until(()=>!OperatorControlMotion.GetIsBusy(button),"End must restore its action when disposal is complete.");
            Assert.False(f.Window.IsCloudSessionActive);
            Assert.False(Named<StackPanel>(f.Window,"CloudLiveState").IsVisible);
            Assert.True(Named<StackPanel>(f.Window,"CloudIdleState").IsVisible);
        }
        finally { timer.Stop();foreach(var child in f.Window.OwnedWindows.Where(x=>x.Title=="Cloud Remote").ToArray())child.Close();session.FinishDisposal(); }
    }

    [AvaloniaTheory]
    [InlineData("success")][InlineData("failure")][InlineData("cancel")]
    [Trait("Evidence","AsyncCommandButton")]
    public async Task UpdateCheckButtonShowsInlineBusyAndRecoversWithoutNetwork(string outcome)
    {
        await using var f=await AflFixture.Create(1280,850);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls=0;
        f.Window.UpdateCheckAsyncOverride=()=>{calls++;return release.Task;};
        await OpenShareCard(f);
        var button=Named<Button>(f.Window,"CheckForUpdateButton");await Mr6Reveal(f,button);
        var before=button.Bounds.Size;
        await Tap(f.Window,button);
        Assert.Equal(1,calls);Assert.False(button.IsEnabled);Assert.True(OperatorControlMotion.GetIsBusy(button));
        Assert.Equal(before,button.Bounds.Size);
        Assert.Equal("Checking for updates",AutomationProperties.GetName(button));
        Assert.Equal("Checking for updates…",AutomationProperties.GetItemStatus(button));
        Assert.True(Named<ProgressBar>(f.Window,"CheckForUpdateBusyProgress").IsVisible
            || Named<TextBlock>(f.Window,"CheckForUpdateLabel").Text == "Checking…");
        Capture(f.Window,$"update-check-busy-{outcome}");
        bool oldReduceMotion=OperatorAppearance.ReduceMotion;
        try
        {
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
            Assert.False(Named<ProgressBar>(f.Window,"CheckForUpdateBusyProgress").IsVisible,
                "A live reduced-motion change removes the animated indicator while keeping the action label busy.");
            Assert.Equal("Checking…",Named<TextBlock>(f.Window,"CheckForUpdateLabel").Text);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);
            Assert.Equal(OperatorMotion.Enabled,Named<ProgressBar>(f.Window,"CheckForUpdateBusyProgress").IsVisible);
        }
        finally { OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,oldReduceMotion); }
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));Assert.Equal(1,calls);
        if(outcome=="failure")release.TrySetException(new IOException("Controlled check failure."));
        else if(outcome=="cancel")release.TrySetCanceled();
        else release.TrySetResult();
        await Until(()=>!OperatorControlMotion.GetIsBusy(button),"The update check action must recover after completion, failure or cancellation.");
        Assert.True(button.IsEnabled);Assert.Equal(before,button.Bounds.Size);
        Assert.False(Named<ProgressBar>(f.Window,"CheckForUpdateBusyProgress").IsVisible);
        Assert.False(Named<TextBlock>(f.Window,"CheckForUpdateBusyStatic").IsVisible);
        if(outcome!="success") Assert.Contains("Update check failed",Named<TextBlock>(f.Window,"UpdateStatusText").Text);
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

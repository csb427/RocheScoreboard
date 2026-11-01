// External draft: no real transport, credentials, network or private async
// invocation. Public MainWindow Start/End run with per-window controlled tasks.
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Roche_Scoreboard.Services.Cloud;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class AflResetScopeTests
{
    private static readonly SupabaseConfig CloudOwnerConfig = new()
    {
        Url = "https://cloud-lifecycle.example.invalid", AnonKey = "fixture-only-anon",
        ServiceRoleKey = "fixture-only-host", RemoteAppBaseUrl = "https://remote.example.invalid"
    };

    private sealed class ControlledCloudSession(string code) : ICloudRemoteSession
    {
        private readonly TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource dispose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Action<bool>? connection;
        private int disposing;
        private volatile bool active;
        internal int StartCalls, DisposeCalls, DisposeExecutions;
        internal string? StartedSport;
        internal LiveStateSnapshot? Initial;
        internal readonly List<LiveStateSnapshot> Published = [];
        internal readonly Guid Id = Guid.NewGuid();
        public bool IsActive => active;
        public string? JoinCode => active ? code : null;
        public Guid? SessionId => active ? Id : null;
        internal int Subscribers => connection?.GetInvocationList().Length ?? 0;
        public event Action<bool>? ConnectedChanged { add => connection += value; remove => connection -= value; }
        public Task StartAsync(string sport, string homeName, string awayName, LiveStateSnapshot initialState, CancellationToken ct)
        {
            StartCalls++; StartedSport = sport; Initial = initialState;
            return start.Task; // Intentionally held until this test permits completion.
        }
        public void PublishSnapshot(LiveStateSnapshot snapshot) => Published.Add(snapshot);
        internal void StartSucceeds()
        {
            active = Volatile.Read(ref disposing) == 0;
            start.TrySetResult();
        }
        internal void StartFails() => start.TrySetException(new IOException("Controlled start failed."));
        internal void FinishDisposal() => dispose.TrySetResult();
        internal void ReleaseForCleanup() { start.TrySetCanceled(); dispose.TrySetResult(); }
        internal void EmitConnection(bool connected) => connection?.Invoke(connected);
        internal Action<bool>? CopyConnectionInvocation() => connection;
        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            // Deliberately models an early-returning older provider so host
            // ownership is tested independently of the fixed real provider.
            if (Interlocked.Exchange(ref disposing, 1) != 0) return ValueTask.CompletedTask;
            DisposeExecutions++;
            return new ValueTask(DisposeCore());
        }
        private async Task DisposeCore() { await dispose.Task.ConfigureAwait(false); active = false; }
    }

    private sealed class CloudOwnerScope : IAsyncDisposable
    {
        internal readonly Fixture F;
        internal readonly List<ControlledCloudSession> Services = new();
        internal readonly List<string> Starts = new();
        internal readonly List<Exception> Failures = new();
        internal int Ends, FactoryCalls;
        private readonly Queue<ControlledCloudSession> next = new();
        private readonly List<Task> operations = new();
        private CloudOwnerScope(Fixture fixture) { F = fixture; }
        internal static async Task<CloudOwnerScope> Create(params string[] codes)
        {
            var fixture = await Fixture.Create(startMatch: false);
            try
            {
                var scope = new CloudOwnerScope(fixture);
                foreach (string code in codes)
                {
                    var service = new ControlledCloudSession(code);
                    scope.Services.Add(service); scope.next.Enqueue(service);
                }
                fixture.Window.SetCloudSessionTestConfiguration(CloudOwnerConfig);
                fixture.Window.CloudSessionFactory = (config, target, dispatcher) =>
                {
                    Assert.Same(CloudOwnerConfig, config);
                    Assert.Same(fixture.Window, target); Assert.Same(Dispatcher.UIThread, dispatcher);
                    scope.FactoryCalls++;
                    Assert.NotEmpty(scope.next); return scope.next.Dequeue();
                };
                fixture.Window.CloudSessionFailureSink = scope.Failures.Add;
                fixture.Window.CloudSessionStarted += scope.Starts.Add;
                fixture.Window.CloudSessionEnded += () => scope.Ends++;
                return scope;
            }
            catch { fixture.Dispose(); throw; }
        }
        private Task Track(Task operation) { operations.Add(operation); return operation; }
        internal Task Start() => Track(F.Window.StartCloudSessionAsync());
        internal Task End() => Track(F.Window.EndCloudSessionAsync());
        internal static async Task Terminal(Task task) => await task.WaitAsync(TimeSpan.FromSeconds(5));
        internal static async Task Progress(Func<bool> predicate)
        {
            for (int i = 0; i < 200 && !predicate(); i++) await Task.Delay(10);
            Assert.True(predicate(), "Controlled service operation must reach its observable terminal/dispose phase.");
        }
        internal object Pose() => new
        {
            F.Window.IsCloudSessionActive, F.Window.CloudJoinCode, F.Window.CloudSessionId,
            starts = Starts.ToArray(), ends = Ends, failures = Failures.Select(error => error.ToString()).ToArray(), FactoryCalls,
            services = Services.Select(service => new { service.Id, service.StartCalls, service.DisposeCalls,
                service.DisposeExecutions, service.Subscribers, service.IsActive }).ToArray()
        };
        internal void Record(string name, object value)
        {
            if (Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "cloud-owner-" + name + ".json"),
                JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        }
        public async ValueTask DisposeAsync()
        {
            var errors = new List<Exception>();
            // Keep factory/config/failure sink installed until EVERY accepted
            // public operation finishes, including an original-source failure
            // that has wrongly begun disposing B. No teardown modal can block.
            foreach (var service in Services) service.ReleaseForCleanup();
            foreach (Task operation in operations)
            {
                try { await Terminal(operation); }
                catch (Exception error) { errors.Add(error); }
            }
            try { await Terminal(F.Window.EndCloudSessionAsync()); }
            catch (Exception error) { errors.Add(error); }
            foreach (var service in Services)
            {
                try { await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (Exception error) { errors.Add(error); }
            }
            try { F.Dispose(); }
            catch (Exception error) { errors.Add(error); }
            if (errors.Count > 0) throw new AggregateException("Cloud owner terminal cleanup failed.", errors);
        }
    }

    [AvaloniaFact]
    public async Task CloudSessionOwnerCurrentStartStopUsesCapturedServiceAndNotifiesOnce()
    {
        await using var scope = await CloudOwnerScope.Create("CURRENT");
        string state = Snapshot(scope.F.Match); var a = scope.Services[0];
        Task start = scope.Start(); Assert.False(start.IsCompleted); Assert.Equal(1, a.StartCalls);
        a.StartSucceeds(); await CloudOwnerScope.Terminal(start);
        Assert.True(scope.F.Window.IsCloudSessionActive); Assert.Equal("CURRENT", scope.F.Window.CloudJoinCode);
        Assert.Equal(a.Id, scope.F.Window.CloudSessionId); Assert.Equal(new[] { "CURRENT" }, scope.Starts);
        Assert.Equal("afl", a.StartedSport); Assert.NotNull(a.Initial); Assert.Equal("lobby", a.Initial!.SessionStatus);
        Assert.Equal(1, a.Subscribers);
        Task end = scope.End(); Assert.Equal(1, a.DisposeCalls); Assert.Equal(0, a.Subscribers);
        a.FinishDisposal(); await CloudOwnerScope.Terminal(end);
        Assert.False(scope.F.Window.IsCloudSessionActive); Assert.Null(scope.F.Window.CloudJoinCode);
        Assert.Equal(1, scope.Ends); Assert.Empty(scope.Failures); Assert.Equal(state, Snapshot(scope.F.Match));
    }

    [AvaloniaFact]
    public async Task CloudSessionOwnerOrdinaryRestartUsesFreshServiceAndIndependentCodes()
    {
        // Constructor/default-factory contract only: an unstarted real service
        // allocates no HTTP/realtime session and its empty Dispose does no IO.
        using (var fixture = await Fixture.Create(startMatch: false))
        {
            await using var real = fixture.Window.CloudSessionFactory(CloudOwnerConfig,
                fixture.Window, Dispatcher.UIThread);
            Assert.IsType<RemoteControlService>(real); Assert.False(real.IsActive);
        }
        await using var scope = await CloudOwnerScope.Create("FIRST", "SECOND");
        foreach (var service in scope.Services)
        {
            Task start = scope.Start(); service.StartSucceeds(); await CloudOwnerScope.Terminal(start);
            Assert.True(scope.F.Window.IsCloudSessionActive); Assert.Equal(service.Id, scope.F.Window.CloudSessionId);
            Task end = scope.End(); service.FinishDisposal(); await CloudOwnerScope.Terminal(end);
            Assert.False(scope.F.Window.IsCloudSessionActive); Assert.Equal(1, service.DisposeExecutions);
            Assert.Equal(0, service.Subscribers);
        }
        Assert.Equal(new[] { "FIRST", "SECOND" }, scope.Starts); Assert.Equal(2, scope.Ends);
        Assert.Equal(2, scope.FactoryCalls); Assert.Empty(scope.Failures);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloudSessionOwnerRetiredStartCompletionCannotAnnounceOrDisposeReplacement(bool failure)
    {
        await using var scope = await CloudOwnerScope.Create("RETIRED", "REPLACEMENT");
        var a = scope.Services[0]; var b = scope.Services[1];
        Task oldStart = scope.Start(); Assert.Equal(1, a.StartCalls); Assert.False(oldStart.IsCompleted);
        Task oldEnd = scope.End(); a.FinishDisposal(); await CloudOwnerScope.Terminal(oldEnd);
        Assert.Equal(0, a.Subscribers);
        Task currentStart = scope.Start(); b.StartSucceeds(); await CloudOwnerScope.Terminal(currentStart);
        Assert.Equal(new[] { "REPLACEMENT" }, scope.Starts); Assert.True(scope.F.Window.IsCloudSessionActive);
        int ended = scope.Ends; string state = Snapshot(scope.F.Match);
        if (failure) a.StartFails(); else a.StartSucceeds();
        // The original failure path awaits wrongly requested B disposal. Reach
        // that concrete mutation and assert BEFORE waiting on its terminal gate.
        await CloudOwnerScope.Progress(() => oldStart.IsCompleted || b.DisposeCalls > 0);
        object observed = scope.Pose(); scope.Record("late-start-" + failure, observed);
        Assert.Equal(0, b.DisposeCalls); Assert.True(scope.F.Window.IsCloudSessionActive);
        Assert.Equal(b.Id, scope.F.Window.CloudSessionId); Assert.Equal("REPLACEMENT", scope.F.Window.CloudJoinCode);
        Assert.Equal(new[] { "REPLACEMENT" }, scope.Starts); Assert.Equal(ended, scope.Ends);
        Assert.Empty(scope.Failures); Assert.Equal(1, b.Subscribers);
        await CloudOwnerScope.Terminal(oldStart); Assert.Equal(state, Snapshot(scope.F.Match));
    }

    [AvaloniaFact]
    public async Task CloudSessionOwnerOlderEndCannotClearServiceStartedAfterASecondEndReturns()
    {
        await using var scope = await CloudOwnerScope.Create("OLD", "NEW");
        var a = scope.Services[0]; var b = scope.Services[1];
        Task startA = scope.Start(); a.StartSucceeds(); await CloudOwnerScope.Terminal(startA);
        Task firstEnd = scope.End(); Assert.False(firstEnd.IsCompleted); Assert.Equal(1, a.DisposeExecutions);
        Task secondEnd = scope.End(); await CloudOwnerScope.Terminal(secondEnd);
        Task startB = scope.Start(); Assert.Equal(1, b.StartCalls); b.StartSucceeds(); await CloudOwnerScope.Terminal(startB);
        Assert.Equal(b.Id, scope.F.Window.CloudSessionId); int ended = scope.Ends;
        a.FinishDisposal(); await CloudOwnerScope.Terminal(firstEnd);
        scope.Record("overlapping-end", scope.Pose());
        Assert.True(scope.F.Window.IsCloudSessionActive); Assert.Equal(b.Id, scope.F.Window.CloudSessionId);
        Assert.Equal("NEW", scope.F.Window.CloudJoinCode); Assert.Equal(0, b.DisposeCalls);
        Assert.Equal(ended, scope.Ends); Assert.Equal(new[] { "OLD", "NEW" }, scope.Starts);
        Assert.Equal(1, a.DisposeExecutions); Assert.Empty(scope.Failures);
    }

    [AvaloniaFact]
    public async Task CloudSessionOwnerCurrentFailureStillReportsAndDisposesBeforeSuccessfulRetry()
    {
        await using var scope = await CloudOwnerScope.Create("FAILED", "RETRY");
        var a = scope.Services[0]; var b = scope.Services[1];
        Task failed = scope.Start(); a.FinishDisposal(); a.StartFails(); await CloudOwnerScope.Terminal(failed);
        Assert.False(scope.F.Window.IsCloudSessionActive); Assert.Equal(1, a.DisposeExecutions);
        Assert.Equal(0, a.Subscribers); Assert.Contains("Controlled start failed.", Assert.Single(scope.Failures).Message);
        Assert.Empty(scope.Starts);
        Task retry = scope.Start(); b.StartSucceeds(); await CloudOwnerScope.Terminal(retry);
        Assert.True(scope.F.Window.IsCloudSessionActive); Assert.Equal("RETRY", scope.F.Window.CloudJoinCode);
        Assert.Equal(new[] { "RETRY" }, scope.Starts);
        Task end = scope.End(); b.FinishDisposal(); await CloudOwnerScope.Terminal(end);
        Assert.Equal(1, b.DisposeExecutions); Assert.Equal(1, scope.Ends);
    }

    [AvaloniaFact]
    public async Task CloudSessionOwnerCopiedOldConnectionNotificationCannotRepaintCurrentSession()
    {
        await using var scope = await CloudOwnerScope.Create("OLD", "CURRENT");
        var a = scope.Services[0]; var b = scope.Services[1];
        Task startA = scope.Start(); a.StartSucceeds(); await CloudOwnerScope.Terminal(startA);
        Action<bool>? copiedInvocation = a.CopyConnectionInvocation(); Assert.NotNull(copiedInvocation);
        Task endA = scope.End(); a.FinishDisposal(); await CloudOwnerScope.Terminal(endA);
        Task startB = scope.Start(); b.StartSucceeds(); await CloudOwnerScope.Terminal(startB);
        b.EmitConnection(true);
        Assert.Equal("Cloud connected", Named<TextBlock>(scope.F.Window, "CloudConnectionLabel").Text);
        string? status = Named<TextBlock>(scope.F.Window, "CloudStatusText").Text;
        copiedInvocation(false); // Copied real subscribed callback after unsubscribe; not a private method invocation.
        scope.Record("copied-connection", scope.Pose());
        Assert.Equal("Cloud connected", Named<TextBlock>(scope.F.Window, "CloudConnectionLabel").Text);
        Assert.Equal(status, Named<TextBlock>(scope.F.Window, "CloudStatusText").Text);
        Assert.Equal(b.Id, scope.F.Window.CloudSessionId); Assert.Equal(0, a.Subscribers); Assert.Equal(1, b.Subscribers);
        Assert.Empty(scope.Failures);
    }
    [AvaloniaFact]
    public async Task CloudSessionOwnerStartupPublishesFreshStateAfterAnInactiveConnectionWait()
    {
        await using var scope = await CloudOwnerScope.Create("FRESH");
        var service = scope.Services[0];
        Task start = scope.Start();
        Assert.NotNull(service.Initial); Assert.Equal(0, service.Initial!.HomeTotal);
        scope.F.Match.AddBehind(Roche_Scoreboard.Models.TeamSide.Home);
        string current = Snapshot(scope.F.Match);
        Assert.Empty(service.Published);
        service.StartSucceeds(); await CloudOwnerScope.Terminal(start);
        var published = Assert.Single(service.Published);
        Assert.Equal(1, published.HomeTotal); Assert.Equal(scope.F.Match.HomeName, published.HomeName);
        Assert.Equal(current, Snapshot(scope.F.Match)); Assert.Equal(new[] { "FRESH" }, scope.Starts);
        Task end = scope.End(); service.FinishDisposal(); await CloudOwnerScope.Terminal(end);
        Assert.Empty(scope.Failures); Assert.Equal(1, scope.Ends);
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task CloudSessionOwnerActualCloseRetiresActiveOrStartingServiceAndTracksCleanup(bool starting)
    {
        await using var scope = await CloudOwnerScope.Create("CLOSE");
        var service = scope.Services[0]; Task start = scope.Start();
        if (!starting) { service.StartSucceeds(); await CloudOwnerScope.Terminal(start); }
        var copied = service.CopyConnectionInvocation(); Assert.NotNull(copied);
        string state = Snapshot(scope.F.Match);
        scope.F.Window.Close();
        Assert.False(scope.F.Window.IsVisible);
        Assert.False(scope.F.Window.IsCloudSessionActive); Assert.Null(scope.F.Window.CloudJoinCode);
        Assert.Equal(0, service.Subscribers); Assert.Equal(1, service.DisposeExecutions);
        Task shutdown = Read<Task>(scope.F.Window, "_cloudOwnerShutdownTask");
        Assert.False(shutdown.IsCompleted);
        copied!(true); Dispatcher.UIThread.RunJobs();
        if (starting) service.StartSucceeds();
        service.FinishDisposal();
        await CloudOwnerScope.Terminal(shutdown); await CloudOwnerScope.Terminal(start);
        Assert.Equal(starting ? Array.Empty<string>() : new[] { "CLOSE" }, scope.Starts);
        Assert.Empty(scope.Failures); Assert.Equal(0, scope.Ends);
        Assert.Equal(state, Snapshot(scope.F.Match));
        await scope.F.Window.StartCloudSessionAsync(); Assert.Equal(1, scope.FactoryCalls);
        Assert.False(scope.F.Window.IsCloudSessionActive); Assert.Null(scope.F.Window.CloudSessionId);
    }

}

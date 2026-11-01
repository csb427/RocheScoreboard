using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Roche_Scoreboard.Services.Cloud;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

// Runs the actual RemoteControlService orchestrator. Only its two provider
// dependencies are controlled; no private methods, network or host UI fixture.
public sealed class CloudProviderLifetimeTests
{
    private static readonly SupabaseConfig Config = new()
    {
        Url = "https://provider-lifetime.example.invalid", AnonKey = "fixture-anon",
        ServiceRoleKey = "fixture-host"
    };
    private static readonly LiveStateSnapshot Initial = new() { HomeName = "Local Home", AwayName = "Local Away", HomeTotal = 17 };
    private enum Phase { Create, Subscribe, Publish }
    private sealed class ControlledStore : ICloudSessionStore
    {
        internal readonly GameSessionRow Row = new() { Id = Guid.NewGuid(), JoinCode = "SYNTHETIC" };
        internal readonly TaskCompletionSource<GameSessionRow> Creating = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Publishing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Updating = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Ending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object gate = new();
        private readonly List<LiveStateSnapshot> writes = [];
        internal int CreateCalls, PublishCalls, EndCalls, DisposeCalls;
        internal CancellationToken CreateToken, PublishToken;
        internal bool HoldUpdates;
        internal Exception? EndFailure, DisposeFailure;
        internal Guid? EndedId;
        internal LiveStateSnapshot[] Writes { get { lock (gate) return writes.ToArray(); } }
        public Task<GameSessionRow> CreateAsync(string sport, string homeName, string awayName, CancellationToken ct)
        {
            Assert.Equal("afl", sport); Assert.Equal(Initial.HomeName, homeName); Assert.Equal(Initial.AwayName, awayName);
            CreateToken = ct; Interlocked.Increment(ref CreateCalls); return Creating.Task;
        }
        public Task PublishStateAsync(Guid sessionId, LiveStateSnapshot snapshot, CancellationToken ct)
        {
            Assert.Equal(Row.Id, sessionId);
            lock (gate) writes.Add(snapshot);
            PublishToken = ct; int call = Interlocked.Increment(ref PublishCalls);
            if (call == 1) return Publishing.Task;
            return HoldUpdates && call == 2 ? Updating.Task : Task.CompletedTask;
        }
        public Task EndAsync(Guid sessionId, CancellationToken ct)
        {
            EndedId = sessionId; Interlocked.Increment(ref EndCalls);
            return EndFailure is { } error ? Task.FromException(error) : Ending.Task;
        }
        public void Dispose()
        {
            Interlocked.Increment(ref DisposeCalls);
            if (DisposeFailure is { } error) throw error;
        }
        internal void ReleaseAll()
        {
            Creating.TrySetResult(Row); Publishing.TrySetResult(); Updating.TrySetResult(); Ending.TrySetResult();
        }
    }
    private sealed class ControlledRealtime : ICloudRealtimeTransport
    {
        internal readonly TaskCompletionSource Subscribing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Disposing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int SubscribeCalls, DisposeCalls;
        internal CancellationToken SubscribeToken;
        internal Guid? SubscribedId;
        internal Exception? DisposeFailure;
        internal Action? OnSubscribe;
        private Action<bool>? connected;
        private Action<JsonElement>? received;
        internal int ConnectionSubscribers => connected?.GetInvocationList().Length ?? 0;
        internal int EventSubscribers => received?.GetInvocationList().Length ?? 0;
        public event Action<bool>? ConnectedChanged { add => connected += value; remove => connected -= value; }
        public event Action<JsonElement>? EventReceived { add => received += value; remove => received -= value; }
        public Task SubscribeToSessionEventsAsync(Guid sessionId, CancellationToken ct)
        {
            SubscribedId = sessionId; SubscribeToken = ct; Interlocked.Increment(ref SubscribeCalls);
            OnSubscribe?.Invoke(); return Subscribing.Task;
        }
        internal void EmitConnected(bool value) => connected?.Invoke(value);
        internal void EmitGoal() => received?.Invoke(Goal(SubscribedId ?? Guid.Empty));
        internal (Action<bool>? Connected, Action<JsonElement>? Received) CopyInvocation() => (connected, received);
        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref DisposeCalls);
            return DisposeFailure is { } error ? new ValueTask(Task.FromException(error)) : new ValueTask(Disposing.Task);
        }
        internal void ReleaseAll() { Subscribing.TrySetResult(); Disposing.TrySetResult(); }
    }
    private static JsonElement Goal(Guid sessionId) => JsonSerializer.SerializeToElement(new
    {
        id = Guid.NewGuid(), session_id = sessionId, event_type = RemoteEventTypes.HomeGoal,
        payload = new { }, created_at = DateTimeOffset.UtcNow
    });

    private sealed class Scope
    {
        private static readonly List<Scope> RetainedOutstanding = [];
        internal readonly ControlledStore Store = new();
        internal readonly ControlledRealtime Realtime = new();
        internal readonly RemoteControlService Service;
        internal readonly IRemoteCommandTarget Target = DispatchProxy.Create<IRemoteCommandTarget, RecordingRemoteTarget>();
        internal RecordingRemoteTarget Recorder => (RecordingRemoteTarget)Target;
        internal readonly List<bool> Connections = [];
        internal int StoreFactories, RealtimeFactories;
        internal Action? OnStoreFactory;
        private readonly List<Task> accepted = [];
        private readonly object acceptedGate = new();
        private readonly HashSet<Task> observed = [];
        private Scope()
        {
            Service = new RemoteControlService(Config, Target, Dispatcher.UIThread,
                settings => { Assert.Same(Config, settings); StoreFactories++; OnStoreFactory?.Invoke(); return Store; },
                settings => { Assert.Same(Config, settings); RealtimeFactories++; return Realtime; });
            Service.ConnectedChanged += Connections.Add;
        }
        private Task Track(Task task) { lock (acceptedGate) accepted.Add(task); return task; }
        internal Task Start(CancellationToken ct = default) => Track(Service.StartAsync("afl", Initial.HomeName, Initial.AwayName, Initial, ct));
        internal Task DisposeService() => Track(Service.DisposeAsync().AsTask());
        internal static async Task Progress(Func<bool> ready, string because)
        {
            for (int i = 0; i < 500 && !ready(); i++) await Task.Delay(10);
            Assert.True(ready(), because);
        }
        internal async Task Success(Task task) { await task.WaitAsync(TimeSpan.FromSeconds(5)); observed.Add(task); }
        internal async Task Cancelled(Task task)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
            observed.Add(task);
        }
        internal async Task<T> Fault<T>(Task task) where T : Exception
        {
            T error = await Assert.ThrowsAsync<T>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
            observed.Add(task); return error;
        }
        internal async Task Reach(Phase phase)
        {
            await Progress(() => Volatile.Read(ref Store.CreateCalls) == 1, "Create must have actually begun.");
            if (phase == Phase.Create) return;
            Store.Creating.TrySetResult(Store.Row);
            await Progress(() => Volatile.Read(ref Realtime.SubscribeCalls) == 1, "Subscribe must have actually begun.");
            if (phase == Phase.Subscribe) return;
            Realtime.Subscribing.TrySetResult();
            await Progress(() => Volatile.Read(ref Store.PublishCalls) == 1, "Initial Publish must have actually begun.");
        }
        internal void Complete(Phase phase, Exception? failure = null)
        {
            if (phase == Phase.Create) { if (failure is null) Store.Creating.TrySetResult(Store.Row); else Store.Creating.TrySetException(failure); }
            else if (phase == Phase.Subscribe) { if (failure is null) Realtime.Subscribing.TrySetResult(); else Realtime.Subscribing.TrySetException(failure); }
            else { if (failure is null) Store.Publishing.TrySetResult(); else Store.Publishing.TrySetException(failure); }
        }
        internal async Task Started()
        {
            Task start = Start(); await Reach(Phase.Publish); Store.Publishing.TrySetResult(); await Success(start);
            Assert.True(Service.IsActive); Assert.Equal(Store.Row.Id, Service.SessionId); Assert.Equal(Store.Row.JoinCode, Service.JoinCode);
        }
        internal void Capture(string name)
        {
            if (Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "cloud-provider-" + name + ".json"), JsonSerializer.Serialize(new
            {
                Service.IsActive, Service.SessionId, StoreFactories, RealtimeFactories,
                Store.CreateCalls, Store.PublishCalls, Store.EndCalls, Store.DisposeCalls,
                realtimeDisposes = Realtime.DisposeCalls, Realtime.SubscribeCalls,
                Realtime.ConnectionSubscribers, Realtime.EventSubscribers,
                createCancelled = Store.CreateToken.IsCancellationRequested,
                subscribeCancelled = Realtime.SubscribeToken.IsCancellationRequested,
                publishCancelled = Store.PublishToken.IsCancellationRequested,
                accepted = AcceptedTasks().Select(task => task.Status.ToString()).ToArray()
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        internal static async Task Run(Func<Scope, Task> body)
        {
            if (RetainedOutstanding.Count > 0)
                throw new InvalidOperationException("An earlier controlled provider fixture has nonterminal operations; stop this test process before starting another case.");
            var scope = new Scope(); Exception? failure = null;
            try { await body(scope); } catch (Exception error) { failure = error; }
            try { await scope.Cleanup(); }
            catch (Exception cleanup) { if (failure is null) failure = cleanup; else failure.Data["CloudProviderCleanup"] = cleanup; }
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
        private Task[] AcceptedTasks() { lock (acceptedGate) return accepted.Distinct().ToArray(); }
        private async Task Cleanup()
        {
            var errors = new List<Exception>();
            // Release every controlled provider before joining accepted public
            // tasks. This also terminates a baseline's wrongly orphaned work.
            Store.ReleaseAll(); Realtime.ReleaseAll();
            _ = DisposeService();
            var joined = new HashSet<Task>();
            for (int pass = 0; pass < 5; pass++)
            {
                foreach (Task task in AcceptedTasks().Where(task => joined.Add(task)))
                {
                    try { await task.WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (Exception error) { if (!observed.Contains(task)) errors.Add(error); }
                }
                if (AcceptedTasks().All(task => joined.Contains(task) && task.IsCompleted)) break;
            }
            if (AcceptedTasks().Any(task => !task.IsCompleted || !joined.Contains(task)))
            {
                RetainedOutstanding.Add(this);
                errors.Add(new InvalidOperationException("Accepted orchestrator tasks did not all reach terminal cleanup. Controlled resources are retained; no premature fallback disposal was attempted."));
                throw new AggregateException("Controlled provider fixture is retained with nonterminal work.", errors);
            }
            // Baseline can lose provider references; keep direct cleanup local
            // and bounded, after all real orchestrator operations are terminal.
            try { await Realtime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception error) { if (Realtime.DisposeFailure is null) errors.Add(error); }
            try { Store.Dispose(); } catch (Exception error) { if (Store.DisposeFailure is null) errors.Add(error); }
            if (errors.Count > 0) throw new AggregateException("Controlled provider terminal cleanup failed.", errors);
        }
    }

    [AvaloniaFact]
    public async Task NormalStartupAndHeldDisposeRetainRealOrchestratorOwnership()
    {
        await Scope.Run(async scope =>
        {
            await scope.Started();
            Assert.Equal(1, scope.StoreFactories); Assert.Equal(1, scope.RealtimeFactories);
            Assert.Equal(scope.Store.Row.Id, scope.Realtime.SubscribedId);
            Assert.Equal("lobby", Assert.Single(scope.Store.Writes).SessionStatus);
            scope.Realtime.EmitConnected(true); Assert.Equal(new[] { true }, scope.Connections);
            scope.Realtime.EmitGoal(); Dispatcher.UIThread.RunJobs(); Assert.Equal(1, scope.Recorder.Afl.HomeGoals);
            Task disposal = scope.DisposeService();
            Assert.False(disposal.IsCompleted);
            await Scope.Progress(() => Volatile.Read(ref scope.Store.EndCalls) == 1, "Dispose must attempt owned session End.");
            Assert.Equal(scope.Store.Row.Id, scope.Store.EndedId); scope.Store.Ending.TrySetResult();
            await Scope.Progress(() => Volatile.Read(ref scope.Realtime.DisposeCalls) == 1, "Realtime disposal must begin after End.");
            Assert.False(disposal.IsCompleted); scope.Realtime.Disposing.TrySetResult(); await scope.Success(disposal);
            Assert.Equal(1, scope.Store.DisposeCalls); Assert.Equal(0, scope.Realtime.ConnectionSubscribers); Assert.Equal(0, scope.Realtime.EventSubscribers);
            Assert.False(scope.Service.IsActive); Assert.Null(scope.Service.SessionId); Assert.Null(scope.Service.JoinCode);
            Assert.Equal(1, scope.Realtime.DisposeCalls); Assert.Equal(1, scope.Store.DisposeCalls);
        });
    }

    [AvaloniaFact]
    public async Task RepeatedStartupSharesThePublishedReceiptAndOneProviderOwner()
    {
        await Scope.Run(async scope =>
        {
            Task first = scope.Start(); Task second = scope.Start();
            Assert.Same(first, second); Assert.Equal(1, scope.StoreFactories); Assert.Equal(1, scope.Store.CreateCalls);
            await scope.Reach(Phase.Publish); scope.Store.Publishing.TrySetResult();
            await scope.Success(first); await scope.Success(second);
            Assert.Equal(1, scope.RealtimeFactories); Assert.Equal(1, scope.Realtime.SubscribeCalls);
            Task disposal = scope.DisposeService(); scope.Store.Ending.TrySetResult(); scope.Realtime.Disposing.TrySetResult(); await scope.Success(disposal);
            Assert.Equal(1, scope.Store.EndCalls); Assert.Equal(1, scope.Store.DisposeCalls); Assert.Equal(1, scope.Realtime.DisposeCalls);
        });
    }

    [AvaloniaTheory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, true)]
    public async Task RetiredStartupPhaseCannotPublishOrOutliveItsDispose(int heldPhase, bool fail)
    {
        await Scope.Run(async scope =>
        {
            Phase phase = (Phase)heldPhase; Task start = scope.Start(); await scope.Reach(phase);
            Task disposal = scope.DisposeService(); Task repeat = scope.DisposeService();
            scope.Capture("held-" + phase + "-" + fail);
            Assert.False(disposal.IsCompleted);
            Assert.Equal(0, scope.Store.EndCalls); // No End may race still-running startup writes/subscription.
            Assert.Equal(0, scope.Store.DisposeCalls); Assert.Equal(0, scope.Realtime.DisposeCalls);
            Assert.True(scope.Store.CreateToken.IsCancellationRequested);
            Assert.False(scope.Service.IsActive); Assert.Null(scope.Service.JoinCode); Assert.Null(scope.Service.SessionId);
            var expected = fail ? new IOException("Controlled late " + phase + " failure.") : null;
            scope.Complete(phase, expected);
            if (fail) Assert.Same(expected, await scope.Fault<IOException>(start)); else await scope.Cancelled(start);
            scope.Store.Ending.TrySetResult(); scope.Realtime.Disposing.TrySetResult();
            await scope.Success(disposal); await scope.Success(repeat); Assert.Same(disposal, repeat);
            Assert.False(scope.Service.IsActive); Assert.Null(scope.Service.SessionId);
            int created = phase == Phase.Create && fail ? 0 : 1;
            Assert.Equal(created, scope.Store.EndCalls);
            if (created == 1) Assert.Equal(scope.Store.Row.Id, scope.Store.EndedId);
            Assert.Equal(1, scope.Store.DisposeCalls);
            Assert.Equal(phase == Phase.Create ? 0 : 1, scope.Realtime.DisposeCalls);
            Assert.Equal(phase == Phase.Create ? 0 : 1, scope.RealtimeFactories);
            Assert.Equal(phase == Phase.Publish ? 1 : 0, scope.Store.PublishCalls);
            Assert.Equal(0, scope.Realtime.ConnectionSubscribers); Assert.Equal(0, scope.Realtime.EventSubscribers);
            Assert.Empty(scope.Connections); Assert.Equal(0, scope.Recorder.Applied);
            await scope.Fault<ObjectDisposedException>(scope.Start());
            Assert.Equal(1, scope.StoreFactories); Assert.Equal(phase == Phase.Create ? 0 : 1, scope.RealtimeFactories);
        });
    }

    [AvaloniaFact]
    public async Task SynchronousStoreFactoryCanRetirePublishedStartupWithoutSelfAwait()
    {
        await Scope.Run(async scope =>
        {
            Task? reentrant = null; scope.OnStoreFactory = () => reentrant = scope.DisposeService();
            Task start = scope.Start(); Assert.NotNull(reentrant); await scope.Cancelled(start); await scope.Success(reentrant!);
            Assert.Equal(1, scope.StoreFactories); Assert.Equal(0, scope.Store.CreateCalls); Assert.Equal(0, scope.RealtimeFactories);
            Assert.Equal(1, scope.Store.DisposeCalls); Assert.False(scope.Service.IsActive);
            Assert.Same(reentrant, scope.DisposeService());
        });
    }

    [AvaloniaFact]
    public async Task SynchronousConnectionObserverCanRetireStartupWithoutSelfAwait()
    {
        await Scope.Run(async scope =>
        {
            Task? reentrant = null;
            scope.Service.ConnectedChanged += _ => reentrant = scope.DisposeService();
            scope.Realtime.OnSubscribe = () => scope.Realtime.EmitConnected(true);
            Task start = scope.Start(); await scope.Reach(Phase.Subscribe);
            await Scope.Progress(() => reentrant is not null, "The actual connection observer must reenter Dispose.");
            Assert.NotNull(reentrant); Assert.False(reentrant!.IsCompleted);
            scope.Realtime.Subscribing.TrySetResult(); await scope.Cancelled(start);
            scope.Store.Ending.TrySetResult(); scope.Realtime.Disposing.TrySetResult(); await scope.Success(reentrant);
            Assert.Equal(new[] { true }, scope.Connections); Assert.Equal(0, scope.Store.PublishCalls);
            Assert.Equal(1, scope.Realtime.DisposeCalls); Assert.Equal(1, scope.Store.DisposeCalls);
        });
    }

    [AvaloniaFact]
    public async Task RetiredQueuedIngressAndCopiedTransportFeedbackCannotReachTarget()
    {
        await Scope.Run(async scope =>
        {
            await scope.Started(); var copied = scope.Realtime.CopyInvocation();
            Assert.NotNull(copied.Connected); Assert.NotNull(copied.Received);
            scope.Realtime.EmitGoal(); // Actual dispatcher queue accepted while live.
            Task disposal = scope.DisposeService();
            copied.Connected!(true); copied.Received!(Goal(scope.Store.Row.Id)); Dispatcher.UIThread.RunJobs();
            Assert.Empty(scope.Connections); Assert.Equal(0, scope.Recorder.Applied); Assert.Equal(0, scope.Recorder.Afl.HomeGoals);
            scope.Store.Ending.TrySetResult(); scope.Realtime.Disposing.TrySetResult(); await scope.Success(disposal);
            copied.Connected!(false); copied.Received!(Goal(scope.Store.Row.Id)); Dispatcher.UIThread.RunJobs();
            Assert.Empty(scope.Connections); Assert.Equal(0, scope.Recorder.Applied);
        });
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task CurrentPublishBurstRetainsNewestStateAndDisposeJoinsInFlightWrite(bool retireDuringWrite)
    {
        await Scope.Run(async scope =>
        {
            await scope.Started(); scope.Store.HoldUpdates = true;
            scope.Service.PublishSnapshot(Initial with { HomeTotal = 23 });
            await Scope.Progress(() => Volatile.Read(ref scope.Store.PublishCalls) == 2, "First live publish must be in flight.");
            scope.Service.PublishSnapshot(Initial with { HomeTotal = 29 }); scope.Service.PublishSnapshot(Initial with { HomeTotal = 35 });
            Task? heldDisposal = null;
            if (retireDuringWrite)
            {
                heldDisposal = scope.DisposeService(); Assert.False(heldDisposal.IsCompleted);
                Assert.Equal(0, scope.Store.EndCalls); Assert.Equal(0, scope.Store.DisposeCalls);
                Assert.True(scope.Store.PublishToken.IsCancellationRequested);
            }
            scope.Store.Updating.TrySetResult();
            if (!retireDuringWrite)
            {
                await Scope.Progress(() => Volatile.Read(ref scope.Store.PublishCalls) == 3, "Newest coalesced publish must reach the same store.");
                Assert.Equal(new[] { 17, 23, 35 }, scope.Store.Writes.Select(snapshot => snapshot.HomeTotal));
            }
            Task disposal = heldDisposal ?? scope.DisposeService();
            scope.Store.Ending.TrySetResult(); scope.Realtime.Disposing.TrySetResult(); await scope.Success(disposal);
            if (retireDuringWrite) Assert.Equal(new[] { 17, 23 }, scope.Store.Writes.Select(snapshot => snapshot.HomeTotal));
            int writes = scope.Store.PublishCalls; scope.Service.PublishSnapshot(Initial with { HomeTotal = 999 });
            await Task.Delay(150); Assert.Equal(writes, scope.Store.PublishCalls); Assert.Equal(1, scope.Store.EndCalls);
            Assert.Equal(1, scope.Store.DisposeCalls); Assert.Equal(1, scope.Realtime.DisposeCalls);
        });
    }

    [AvaloniaTheory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task CleanupFailureStillClosesEveryRemainingResourceAndRepeatsSameFault(int failingResource)
    {
        await Scope.Run(async scope =>
        {
            await scope.Started(); var expected = new IOException("Controlled cleanup failure.");
            if (failingResource == 0) scope.Store.EndFailure = expected;
            else if (failingResource == 1) scope.Realtime.DisposeFailure = expected;
            else scope.Store.DisposeFailure = expected;
            scope.Store.Ending.TrySetResult(); scope.Realtime.Disposing.TrySetResult();
            Task disposal = scope.DisposeService(); var failure = await scope.Fault<AggregateException>(disposal);
            Assert.Contains(expected, failure.InnerExceptions);
            Assert.Equal(1, scope.Store.EndCalls); Assert.Equal(1, scope.Realtime.DisposeCalls); Assert.Equal(1, scope.Store.DisposeCalls);
            Assert.Equal(0, scope.Realtime.ConnectionSubscribers); Assert.Equal(0, scope.Realtime.EventSubscribers);
            Task repeat = scope.DisposeService(); Assert.Same(disposal, repeat); Assert.Same(failure, await scope.Fault<AggregateException>(repeat));
            Assert.False(scope.Service.IsActive); Assert.Null(scope.Service.SessionId);
        });
    }
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task StartupQueuesOnlyNewestLiveStateBehindLobbyAndStopDropsQueuedState(bool stop)
    {
        await Scope.Run(async scope =>
        {
            Task start = scope.Start(); await scope.Reach(Phase.Subscribe);
            scope.Service.PublishSnapshot(Initial with { HomeTotal = 29 });
            await Task.Delay(150);
            Assert.Equal(0, scope.Store.PublishCalls);
            scope.Realtime.Subscribing.TrySetResult(); await scope.Reach(Phase.Publish);
            scope.Service.PublishSnapshot(Initial with { HomeTotal = 35 });
            await Task.Delay(150);
            Assert.Equal(1, scope.Store.PublishCalls);
            Task? disposal = stop ? scope.DisposeService() : null;
            scope.Store.Publishing.TrySetResult();
            if (stop)
            {
                await scope.Cancelled(start);
                scope.Store.Ending.TrySetResult(); scope.Realtime.Disposing.TrySetResult();
                await scope.Success(disposal!);
                Assert.Equal(new[] { 17 }, scope.Store.Writes.Select(snapshot => snapshot.HomeTotal));
            }
            else
            {
                await scope.Success(start);
                await Scope.Progress(() => Volatile.Read(ref scope.Store.PublishCalls) == 2, "Newest queued live state must publish after lobby startup.");
                Assert.Equal(new[] { 17, 35 }, scope.Store.Writes.Select(snapshot => snapshot.HomeTotal));
                disposal = scope.DisposeService(); scope.Store.Ending.TrySetResult(); scope.Realtime.Disposing.TrySetResult();
                await scope.Success(disposal);
            }
            Assert.Equal(1, scope.Store.EndCalls); Assert.Equal(1, scope.Store.DisposeCalls);
            Assert.Equal(1, scope.Realtime.DisposeCalls);
        });
    }

}

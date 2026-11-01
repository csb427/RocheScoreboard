using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Roche_Scoreboard.Services.Cloud;

// Host lifetime seam. Production still creates the exact transport service;
// local tests can control completion without connecting to Supabase.
internal interface ICloudRemoteSession : IAsyncDisposable
{
    bool IsActive { get; }
    string? JoinCode { get; }
    Guid? SessionId { get; }
    event Action<bool>? ConnectedChanged;
    Task StartAsync(string sport, string homeName, string awayName, LiveStateSnapshot initialState, CancellationToken ct);
    void PublishSnapshot(LiveStateSnapshot snapshot);
}

// Narrow provider contracts; the public constructor still uses the real clients.
internal interface ICloudSessionStore : IDisposable
{
    Task<GameSessionRow> CreateAsync(string sport, string homeName, string awayName, CancellationToken ct);
    Task PublishStateAsync(Guid sessionId, LiveStateSnapshot snapshot, CancellationToken ct);
    Task EndAsync(Guid sessionId, CancellationToken ct);
}

internal interface ICloudRealtimeTransport : IAsyncDisposable
{
    event Action<JsonElement>? EventReceived;
    event Action<bool>? ConnectedChanged;
    Task SubscribeToSessionEventsAsync(Guid sessionId, CancellationToken ct);
}

/// <summary>
/// Top-level orchestrator for the Supabase-backed cloud remote session.
/// <see cref="MainWindow"/> owns one of these and starts/stops it from the
/// Settings → Cloud Remote card.
///
/// Responsibilities:
///   - Create the game_sessions row (join code + host key).
///   - Subscribe to realtime events for that session and feed them into the
///     <see cref="RemoteCommandDispatcher"/>.
///   - Publish the rolling <see cref="LiveStateSnapshot"/> on every match
///     change, throttled to ~10/second to avoid flooding the REST endpoint.
/// </summary>
public sealed class RemoteControlService : IAsyncDisposable, ICloudRemoteSession
{
    private readonly SupabaseConfig _config;
    private readonly IRemoteCommandTarget _target;
    private readonly Dispatcher _uiDispatcher;

    private SupabaseRestClient? _rest;
    private ICloudSessionStore? _sessions;
    private ICloudRealtimeTransport? _realtime;
    private RemoteCommandDispatcher? _dispatcher;

    private readonly Func<SupabaseConfig, ICloudSessionStore> _sessionFactory;
    private readonly Func<SupabaseConfig, ICloudRealtimeTransport> _realtimeFactory;

    private GameSessionRow? _session;
    private DateTime _lastPublishUtc = DateTime.MinValue;
    private static readonly TimeSpan MinPublishInterval = TimeSpan.FromMilliseconds(100);
    private readonly object _publishGate = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private LiveStateSnapshot? _pendingSnapshot;
    private Task? _publishLoop;
    private bool _disposed;
    private Task? _startupTask;
    private Task? _disposeTask;
    private GameSessionRow? _startupSession;

    public RemoteControlService(SupabaseConfig config, IRemoteCommandTarget target, Dispatcher uiDispatcher)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(uiDispatcher);

        if (!config.IsValid)
            throw new ArgumentException("Supabase config is incomplete.", nameof(config));
        if (string.IsNullOrWhiteSpace(config.ServiceRoleKey))
            throw new ArgumentException(
                "supabase.config.json must include serviceRoleKey on the host scoreboard.",
                nameof(config));

        _config = config;
        _target = target;
        _uiDispatcher = uiDispatcher;
        _sessionFactory = settings =>
        {
            _rest = new SupabaseRestClient(settings);
            return new CloudGameSessionService(_rest);
        };
        _realtimeFactory = static settings => new SupabaseRealtimeClient(settings);
    }

    internal RemoteControlService(SupabaseConfig config, IRemoteCommandTarget target, Dispatcher uiDispatcher,
        Func<SupabaseConfig, ICloudSessionStore> sessionFactory,
        Func<SupabaseConfig, ICloudRealtimeTransport> realtimeFactory)
        : this(config, target, uiDispatcher)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        ArgumentNullException.ThrowIfNull(realtimeFactory);
        _sessionFactory = sessionFactory;
        _realtimeFactory = realtimeFactory;
    }

    public bool IsActive { get { lock (_publishGate) return !_disposed && _session is not null; } }

    /// <summary>Join code shown to the operator (e.g. <c>K7HM-2QPR</c>).</summary>
    public string? JoinCode { get { lock (_publishGate) return _disposed ? null : _session?.JoinCode; } }

    /// <summary>Server-assigned game id.</summary>
    public Guid? SessionId { get { lock (_publishGate) return _disposed ? null : _session?.Id; } }

    public event Action<bool>? ConnectedChanged;

    /// <summary>
    /// Boots a fresh session: creates the row, subscribes to events, and
    /// publishes the initial snapshot.
    /// </summary>
    public Task StartAsync(string sport, string homeName, string awayName, LiveStateSnapshot initialState, CancellationToken ct)
    {
        TaskCompletionSource completion;
        lock (_publishGate)
        {
            if (_disposed) return Task.FromException(new ObjectDisposedException(nameof(RemoteControlService)));
            if (_startupTask is not null) return _startupTask;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            // Publish the receipt before a synchronous factory/event can reenter.
            _startupTask = completion.Task;
        }
        _ = CompleteStartupAsync(sport, homeName, awayName, initialState, ct, completion);
        return completion.Task;
    }

    private async Task CompleteStartupAsync(string sport, string homeName, string awayName,
        LiveStateSnapshot initialState, CancellationToken ct, TaskCompletionSource completion)
    {
        try
        {
            await StartCoreAsync(sport, homeName, awayName, initialState, ct).ConfigureAwait(false);
            lock (_publishGate)
            {
                ct.ThrowIfCancellationRequested();
                if (_disposed) throw new OperationCanceledException(_lifetimeCts.Token);
                completion.TrySetResult();
                // Scores may change while subscribe/initial lobby publish is
                // pending. Flush their newest state only after startup has
                // finished writing, so the lobby snapshot cannot overwrite it.
                if (_pendingSnapshot is not null && (_publishLoop is null || _publishLoop.IsCompleted))
                    _publishLoop = Task.Run(PublishLoopAsync);
            }
        }
        catch (OperationCanceledException error) { completion.TrySetCanceled(error.CancellationToken); }
        catch (Exception error) { completion.TrySetException(error); }
    }

    private async Task StartCoreAsync(string sport, string homeName, string awayName,
        LiveStateSnapshot initialState, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeCts.Token);
        CancellationToken owned = linked.Token;
        ICloudSessionStore sessions;
        Task<GameSessionRow> creating;
        lock (_publishGate)
        {
            ThrowIfStartupRetired(owned);
            sessions = _sessionFactory(_config);
            _sessions = sessions; // Retain every acquired resource for terminal disposal.
            ThrowIfStartupRetired(owned);
            _dispatcher = CreateOwnedDispatcher();
            creating = sessions.CreateAsync(sport, homeName, awayName, owned);
        }
        GameSessionRow created = await creating.ConfigureAwait(false);
        Task subscribing;
        lock (_publishGate)
        {
            // Even a provider ignoring cancellation can return a created row.
            // Keep its identity for End, without exposing a retired session.
            _startupSession = created;
            ThrowIfStartupRetired(owned);
            _session = created;
            ICloudRealtimeTransport realtime = _realtimeFactory(_config);
            _realtime = realtime;
            ThrowIfStartupRetired(owned);
            realtime.ConnectedChanged += ForwardConnected;
            realtime.EventReceived += OnRealtimeEvent;
            subscribing = realtime.SubscribeToSessionEventsAsync(created.Id, owned);
        }
        await subscribing.ConfigureAwait(false);
        Task publishing;
        lock (_publishGate)
        {
            ThrowIfStartupRetired(owned);
            publishing = sessions.PublishStateAsync(created.Id, initialState with { SessionStatus = "lobby" }, owned);
        }
        await publishing.ConfigureAwait(false);
        lock (_publishGate) ThrowIfStartupRetired(owned);
    }

    // Called under the same gate as resource publication and retirement.
    private void ThrowIfStartupRetired(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_disposed) throw new OperationCanceledException(ct);
    }

    private RemoteCommandDispatcher CreateOwnedDispatcher()
    {
        RemoteCommandDispatcher? receipt = null;
        receipt = new RemoteCommandDispatcher(_target, callback =>
        {
            _ = _uiDispatcher.InvokeAsync(() =>
            {
                lock (_publishGate)
                    if (_disposed || !ReferenceEquals(_dispatcher, receipt)) return;
                callback();
            });
        });
        return receipt;
    }

    /// <summary>
    /// Pushes the latest <see cref="LiveStateSnapshot"/>. Safe to call when
    /// the service is inactive — returns immediately.
    /// </summary>
    public void PublishSnapshot(LiveStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_publishGate)
        {
            if (_disposed || _sessions is null || _session is null) return;
            // Always retain the newest state. Bursts are coalesced rather than
            // dropped, so the remote cannot remain on an intermediate score
            // merely because the final event landed inside the throttle window.
            _pendingSnapshot = snapshot;
            if (_startupTask?.IsCompletedSuccessfully == true && (_publishLoop is null || _publishLoop.IsCompleted))
                _publishLoop = Task.Run(PublishLoopAsync);
        }
    }

    private async Task PublishLoopAsync()
    {
        while (!_lifetimeCts.IsCancellationRequested)
        {
            LiveStateSnapshot? snapshot;
            ICloudSessionStore? sessions;
            Guid sessionId;
            lock (_publishGate)
            {
                snapshot = _pendingSnapshot;
                _pendingSnapshot = null;
                sessions = _sessions;
                sessionId = _session?.Id ?? Guid.Empty;
                if (snapshot is null || sessions is null || sessionId == Guid.Empty)
                {
                    _publishLoop = null;
                    return;
                }
            }

            TimeSpan wait = MinPublishInterval - (DateTime.UtcNow - _lastPublishUtc);
            if (wait > TimeSpan.Zero)
            {
                try { await Task.Delay(wait, _lifetimeCts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    Task publishing;
                    lock (_publishGate)
                    {
                        if (_disposed || !ReferenceEquals(_sessions, sessions) || _session?.Id != sessionId) return;
                        publishing = sessions.PublishStateAsync(sessionId, snapshot, timeout.Token);
                    }
                    await publishing.ConfigureAwait(false);
                    lock (_publishGate) if (!_disposed) _lastPublishUtc = DateTime.UtcNow;
                    break;
                }
                catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CloudRemote] publish attempt {attempt} failed: {ex.Message}");
                    if (attempt < 2)
                    {
                        try { await Task.Delay(TimeSpan.FromMilliseconds(500), _lifetimeCts.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) { return; }
                    }
                }
            }
        }
    }

    private void OnRealtimeEvent(JsonElement record)
    {
        lock (_publishGate) if (_disposed) return;
        try
        {
            // record shape:
            //   { id, session_id, event_type, payload, client_id, client_label, created_at }
            var ev = new RemoteEvent
            {
                Id          = GetGuid(record, "id"),
                SessionId   = GetGuid(record, "session_id"),
                EventType   = GetString(record, "event_type"),
                Payload     = record.TryGetProperty("payload", out var p) ? p.Clone()
                              : JsonDocument.Parse("{}").RootElement,
                ClientId    = TryGetGuid(record, "client_id"),
                ClientLabel = GetString(record, "client_label"),
                CreatedAt   = TryGetDateTime(record, "created_at") ?? DateTimeOffset.UtcNow,
            };

            lock (_publishGate) if (!_disposed) _dispatcher?.Dispatch(ev);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudRemote] bad event record: {ex.Message}");
        }
    }

    private void ForwardConnected(bool connected)
    {
        lock (_publishGate) if (!_disposed) ConnectedChanged?.Invoke(connected);
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        Task? startup, pendingPublish;
        lock (_publishGate)
        {
            if (_disposeTask is not null) return new ValueTask(_disposeTask);
            _disposed = true;
            _pendingSnapshot = null;
            startup = _startupTask;
            pendingPublish = _publishLoop;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            // Every caller observes the same actual terminal cleanup, including
            // callers reentering through cancellation or client callbacks.
            _disposeTask = completion.Task;
        }
        _ = CompleteDisposalAsync(startup, pendingPublish, completion);
        return new ValueTask(completion.Task);
    }

    private async Task CompleteDisposalAsync(Task? startup, Task? pendingPublish, TaskCompletionSource completion)
    {
        var errors = new List<Exception>();
        try
        {
            try { _lifetimeCts.Cancel(); } catch (Exception error) { errors.Add(error); }
            // Startup owns its public failure/cancellation receipt. Observe it
            // here to join ownership, then close all resources it acquired.
            try { if (startup is not null) await startup.ConfigureAwait(false); } catch (Exception) { }
            try { if (pendingPublish is not null) await pendingPublish.ConfigureAwait(false); }
            catch (Exception error) { errors.Add(error); }

            ICloudSessionStore? sessions;
            ICloudRealtimeTransport? realtime;
            SupabaseRestClient? rest;
            GameSessionRow? created;
            lock (_publishGate)
            {
                sessions = _sessions; realtime = _realtime; rest = _rest;
                created = _startupSession;
            }
            if (created is not null && sessions is not null)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await sessions.EndAsync(created.Id, timeout.Token).ConfigureAwait(false);
                }
                catch (Exception error) { errors.Add(error); }
            }
            if (realtime is not null)
            {
                try { realtime.EventReceived -= OnRealtimeEvent; } catch (Exception error) { errors.Add(error); }
                try { realtime.ConnectedChanged -= ForwardConnected; } catch (Exception error) { errors.Add(error); }
                try { await realtime.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
            }
            try { sessions?.Dispose(); } catch (Exception error) { errors.Add(error); }
            try { rest?.Dispose(); } catch (Exception error) { errors.Add(error); }
            lock (_publishGate)
            {
                _session = null; _startupSession = null; _sessions = null;
                _rest = null; _realtime = null; _dispatcher = null;
            }
            try { _lifetimeCts.Dispose(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count > 0) throw new AggregateException("Cloud provider cleanup failed.", errors);
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }

    // ── JSON helpers ──

    private static string GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? "")
            : "";

    private static Guid GetGuid(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
           && Guid.TryParse(v.GetString(), out var g)
            ? g
            : Guid.Empty;

    private static Guid? TryGetGuid(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
           && Guid.TryParse(v.GetString(), out var g)
            ? g
            : null;

    private static DateTimeOffset? TryGetDateTime(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
           && DateTimeOffset.TryParse(v.GetString(), out var dt)
            ? dt
            : null;
}

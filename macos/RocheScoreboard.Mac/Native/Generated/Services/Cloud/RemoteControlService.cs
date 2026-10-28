// Generated from Services/Cloud/RemoteControlService.cs; edit translator/adapters.
// Source SHA-256: 33f482768f22b53ceec5aa3a8b5be20cda5a28bfde906e7d35aa0422d6c0a4cc
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Roche_Scoreboard.Services.Cloud;

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
public sealed class RemoteControlService : IAsyncDisposable
{
    private readonly SupabaseConfig _config;
    private readonly IRemoteCommandTarget _target;
    private readonly Dispatcher _uiDispatcher;

    private SupabaseRestClient? _rest;
    private CloudGameSessionService? _sessions;
    private SupabaseRealtimeClient? _realtime;
    private RemoteCommandDispatcher? _dispatcher;

    private GameSessionRow? _session;
    private DateTime _lastPublishUtc = DateTime.MinValue;
    private static readonly TimeSpan MinPublishInterval = TimeSpan.FromMilliseconds(100);
    private readonly object _publishGate = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private LiveStateSnapshot? _pendingSnapshot;
    private Task? _publishLoop;
    private bool _disposed;

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
    }

    public bool IsActive => _session is not null;

    /// <summary>Join code shown to the operator (e.g. <c>K7HM-2QPR</c>).</summary>
    public string? JoinCode => _session?.JoinCode;

    /// <summary>Server-assigned game id.</summary>
    public Guid? SessionId => _session?.Id;

    public event Action<bool>? ConnectedChanged;

    /// <summary>
    /// Boots a fresh session: creates the row, subscribes to events, and
    /// publishes the initial snapshot.
    /// </summary>
    public async Task StartAsync(string sport, string homeName, string awayName, LiveStateSnapshot initialState, CancellationToken ct)
    {
        if (_session is not null) return;

        _rest = new SupabaseRestClient(_config);
        _sessions = new CloudGameSessionService(_rest);
        _dispatcher = new RemoteCommandDispatcher(_target, _uiDispatcher);

        _session = await _sessions.CreateAsync(sport, homeName, awayName, ct).ConfigureAwait(false);

        _realtime = new SupabaseRealtimeClient(_config);
        _realtime.ConnectedChanged += ForwardConnected;
        _realtime.EventReceived += OnRealtimeEvent;
        await _realtime.SubscribeToSessionEventsAsync(_session.Id, ct).ConfigureAwait(false);

        await _sessions.PublishStateAsync(_session.Id, initialState with { SessionStatus = "lobby" }, ct)
            .ConfigureAwait(false);
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
            if (_publishLoop is null || _publishLoop.IsCompleted)
                _publishLoop = Task.Run(PublishLoopAsync);
        }
    }

    private async Task PublishLoopAsync()
    {
        while (!_lifetimeCts.IsCancellationRequested)
        {
            LiveStateSnapshot? snapshot;
            CloudGameSessionService? sessions;
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
                    await sessions.PublishStateAsync(sessionId, snapshot, timeout.Token).ConfigureAwait(false);
                    _lastPublishUtc = DateTime.UtcNow;
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

            _dispatcher?.Dispatch(ev);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudRemote] bad event record: {ex.Message}");
        }
    }

    private void ForwardConnected(bool connected) => ConnectedChanged?.Invoke(connected);

    public async ValueTask DisposeAsync()
    {
        Task? pendingPublish;
        lock (_publishGate)
        {
            if (_disposed) return;
            _disposed = true;
            _pendingSnapshot = null;
            pendingPublish = _publishLoop;
        }
        try { _lifetimeCts.Cancel(); } catch { }
        try { if (pendingPublish is not null) await pendingPublish.ConfigureAwait(false); } catch { }

        if (_session is not null && _sessions is not null)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _sessions.EndAsync(_session.Id, cts.Token).ConfigureAwait(false);
            }
            catch { /* best effort on shutdown */ }
        }

        if (_realtime is not null)
        {
            _realtime.EventReceived -= OnRealtimeEvent;
            _realtime.ConnectedChanged -= ForwardConnected;
            await _realtime.DisposeAsync().ConfigureAwait(false);
        }

        _sessions?.Dispose();
        _rest?.Dispose();

        _session = null;
        _sessions = null;
        _rest = null;
        _realtime = null;
        _dispatcher = null;
        _lifetimeCts.Dispose();
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

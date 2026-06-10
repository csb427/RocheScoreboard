using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Roche_Scoreboard.Services.Cloud;

/// <summary>
/// Hand-rolled Supabase Realtime v2 client.
///
/// Realtime sits on top of Phoenix Channels over a WebSocket. We open a
/// single connection, subscribe to <c>realtime:public:game_events</c>
/// (filtered server-side to the active session), and surface each
/// <c>postgres_changes</c> INSERT through <see cref="EventReceived"/>.
///
/// A tiny heartbeat task pings the server every 25 seconds, which is the
/// upstream-recommended cadence for keeping the channel alive.
/// </summary>
public sealed class SupabaseRealtimeClient : IAsyncDisposable
{
    private readonly Uri _socketUri;
    private readonly string _apiKey;

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private Task? _heartbeatLoop;
    private long _ref;

    /// <summary>
    /// Raised on the receive task when a new <c>INSERT</c> arrives on the
    /// subscribed channel. The handler is called with the raw JSON payload
    /// of the new row (mirrors the <c>record</c> object returned by Supabase
    /// Realtime).
    /// </summary>
    public event Action<JsonElement>? EventReceived;

    /// <summary>Fires when the socket transitions to or from <c>Open</c>.</summary>
    public event Action<bool>? ConnectedChanged;

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public SupabaseRealtimeClient(SupabaseConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        // wss://<project-ref>.supabase.co/realtime/v1/websocket?apikey=...&vsn=1.0.0
        var http = new Uri(config.Url);
        var builder = new UriBuilder(http)
        {
            Scheme = http.Scheme == "https" ? "wss" : "ws",
            Path = "/realtime/v1/websocket",
            Query = $"apikey={Uri.EscapeDataString(config.AnonKey)}&vsn=1.0.0"
        };
        _socketUri = builder.Uri;
        _apiKey = config.AnonKey;
    }

    /// <summary>
    /// Opens the WebSocket and joins the
    /// <c>realtime:public:game_events:session_id=eq.&lt;sessionId&gt;</c>
    /// channel. Throws on initial connection failure; runtime hiccups are
    /// surfaced through <see cref="ConnectedChanged"/>.
    /// </summary>
    public async Task SubscribeToSessionEventsAsync(Guid sessionId, CancellationToken ct)
    {
        if (_socket is not null) throw new InvalidOperationException("Already subscribed.");

        _socket = new ClientWebSocket();
        await _socket.ConnectAsync(_socketUri, ct).ConfigureAwait(false);
        ConnectedChanged?.Invoke(true);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Join the postgres_changes channel filtered to this session id.
        string topic = $"realtime:public:game_events:session_id=eq.{sessionId}";
        var joinPayload = new
        {
            config = new
            {
                postgres_changes = new[]
                {
                    new
                    {
                        @event = "INSERT",
                        schema = "public",
                        table = "game_events",
                        filter = $"session_id=eq.{sessionId}"
                    }
                }
            }
        };

        await SendAsync(topic, "phx_join", joinPayload, _cts.Token).ConfigureAwait(false);

        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        _heartbeatLoop = Task.Run(() => HeartbeatLoopAsync(_cts.Token));
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var ms = new System.IO.MemoryStream();

        try
        {
            while (!ct.IsCancellationRequested && _socket is { State: WebSocketState.Open })
            {
                WebSocketReceiveResult result;
                ms.SetLength(0);

                do
                {
                    result = await _socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                ms.Position = 0;
                using var doc = await JsonDocument.ParseAsync(ms, cancellationToken: ct).ConfigureAwait(false);
                HandleIncoming(doc.RootElement);
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Realtime] receive loop error: {ex.Message}");
        }
        finally
        {
            ConnectedChanged?.Invoke(false);
        }
    }

    private void HandleIncoming(JsonElement root)
    {
        // Phoenix message shape: { topic, event, payload, ref }
        if (!root.TryGetProperty("event", out var evEl)) return;
        string evName = evEl.GetString() ?? "";
        if (evName != "postgres_changes") return;
        if (!root.TryGetProperty("payload", out var payloadEl)) return;
        if (!payloadEl.TryGetProperty("data", out var dataEl)) return;
        if (!dataEl.TryGetProperty("type", out var typeEl) || typeEl.GetString() != "INSERT") return;
        if (!dataEl.TryGetProperty("record", out var recordEl)) return;

        try
        {
            EventReceived?.Invoke(recordEl.Clone());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Realtime] EventReceived handler error: {ex.Message}");
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _socket is { State: WebSocketState.Open })
            {
                await Task.Delay(TimeSpan.FromSeconds(25), ct).ConfigureAwait(false);
                await SendAsync("phoenix", "heartbeat", new { }, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* graceful */ }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Realtime] heartbeat error: {ex.Message}");
        }
    }

    private async Task SendAsync(string topic, string @event, object payload, CancellationToken ct)
    {
        if (_socket is null) return;

        long refId = Interlocked.Increment(ref _ref);
        var message = new
        {
            topic,
            @event,
            payload,
            @ref = refId.ToString()
        };

        string json = JsonSerializer.Serialize(message);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts?.Cancel(); } catch { /* swallow */ }

        if (_socket is { State: WebSocketState.Open })
        {
            try
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "client closing", closeCts.Token)
                    .ConfigureAwait(false);
            }
            catch { /* best effort */ }
        }

        try { if (_receiveLoop is not null) await _receiveLoop.ConfigureAwait(false); } catch { }
        try { if (_heartbeatLoop is not null) await _heartbeatLoop.ConfigureAwait(false); } catch { }

        _socket?.Dispose();
        _cts?.Dispose();
        _socket = null;
        _cts = null;
    }
}

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Roche_Scoreboard.Services.Cloud;

/// <summary>
/// Owns the lifecycle of a single cloud remote session:
///   1. Creates the <c>game_sessions</c> row with a fresh join code and host key.
///   2. Writes the rolling <c>live_state</c> snapshot through the service-role key.
///   3. Marks the session <c>ended</c> on shutdown.
///
/// Realtime ingestion (i.e. listening for remote events) is delegated to
/// <see cref="SupabaseRealtimeClient"/> and orchestrated by
/// <see cref="RemoteControlService"/>.
/// </summary>
public sealed class CloudGameSessionService : IDisposable
{
    private readonly SupabaseRestClient _rest;

    public CloudGameSessionService(SupabaseRestClient rest)
    {
        ArgumentNullException.ThrowIfNull(rest);
        _rest = rest;
    }

    /// <summary>
    /// Inserts a new <c>game_sessions</c> row. The returned session contains
    /// the server-assigned id plus the join code / host key the operator
    /// should keep locally.
    /// </summary>
    public async Task<GameSessionRow> CreateAsync(string sport, string homeName, string awayName, CancellationToken ct)
    {
        var row = new
        {
            join_code = JoinCodeGenerator.Generate(),
            host_key  = JoinCodeGenerator.GenerateHostKey(),
            sport     = string.IsNullOrWhiteSpace(sport) ? "afl" : sport,
            status    = "lobby",
            home_name = string.IsNullOrWhiteSpace(homeName) ? "HOME" : homeName,
            away_name = string.IsNullOrWhiteSpace(awayName) ? "AWAY" : awayName,
        };

        GameSessionRow? created = await _rest.InsertAsync<GameSessionRow>(
            "game_sessions", row, useServiceRole: true, ct).ConfigureAwait(false);

        if (created is null)
            throw new InvalidOperationException("Supabase did not return the created session row.");
        return created;
    }

    /// <summary>
    /// Writes the latest <see cref="LiveStateSnapshot"/> + status back to the
    /// owning row. Uses the service-role key so the host bypasses RLS.
    /// </summary>
    public async Task PublishStateAsync(Guid sessionId, LiveStateSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var patch = new
        {
            live_state = snapshot,
            status = snapshot.SessionStatus,
        };

        string filter = $"id=eq.{sessionId}";
        await _rest.UpdateAsync("game_sessions", filter, patch, useServiceRole: true, ct)
            .ConfigureAwait(false);
    }

    /// <summary>Marks the session as <c>ended</c> and stamps <c>ended_at</c>.</summary>
    public async Task EndAsync(Guid sessionId, CancellationToken ct)
    {
        var patch = new { status = "ended", ended_at = DateTimeOffset.UtcNow };
        string filter = $"id=eq.{sessionId}";
        await _rest.UpdateAsync("game_sessions", filter, patch, useServiceRole: true, ct)
            .ConfigureAwait(false);
    }

    public void Dispose() => _rest.Dispose();
}

/// <summary>
/// PostgREST row representation of the <c>game_sessions</c> table. Field
/// names map to the snake_case columns used by Supabase.
/// </summary>
public sealed record GameSessionRow
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("join_code")]
    public string JoinCode { get; init; } = "";

    [JsonPropertyName("host_key")]
    public string HostKey { get; init; } = "";

    [JsonPropertyName("sport")]
    public string Sport { get; init; } = "afl";

    [JsonPropertyName("status")]
    public string Status { get; init; } = "lobby";

    [JsonPropertyName("home_name")]
    public string HomeName { get; init; } = "";

    [JsonPropertyName("away_name")]
    public string AwayName { get; init; } = "";

    [JsonPropertyName("live_state")]
    public JsonElement LiveState { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; init; }
}

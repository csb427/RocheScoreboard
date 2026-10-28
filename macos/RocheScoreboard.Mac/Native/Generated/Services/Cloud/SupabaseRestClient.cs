// Generated from Services/Cloud/SupabaseRestClient.cs; edit translator/adapters.
// Source SHA-256: 3c4534c14c8bcff3543153e75b3803b64b8e15f316ae3c637544085103191848
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Roche_Scoreboard.Services.Cloud;

/// <summary>
/// Thin REST wrapper around Supabase PostgREST + GoTrue used by the WPF host.
///
/// We deliberately avoid taking a hard dependency on every internal of the
/// <c>Supabase</c> NuGet package — the surface we need is small and using
/// <see cref="HttpClient"/> directly keeps the behaviour deterministic across
/// SDK upgrades. Realtime subscriptions live in
/// <see cref="SupabaseRealtimeClient"/>; this class only handles
/// CREATE / UPDATE / SELECT.
/// </summary>
public sealed class SupabaseRestClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _restBase;
    private readonly string _anonKey;
    private readonly string? _serviceRoleKey;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public SupabaseRestClient(SupabaseConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!config.IsValid) throw new ArgumentException("Supabase config is incomplete.", nameof(config));

        _restBase = config.Url.TrimEnd('/') + "/rest/v1";
        _anonKey = config.AnonKey;
        _serviceRoleKey = config.ServiceRoleKey;

        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>Inserts a row and returns the persisted representation.</summary>
    public async Task<T?> InsertAsync<T>(string table, object row, bool useServiceRole, CancellationToken ct)
    {
        using var req = NewRequest(HttpMethod.Post, $"/{Uri.EscapeDataString(table)}", useServiceRole);
        req.Headers.Add("Prefer", "return=representation");
        req.Content = JsonContent.Create(row, options: Json);

        using HttpResponseMessage resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp, ct).ConfigureAwait(false);

        var list = await resp.Content.ReadFromJsonAsync<List<T>>(Json, ct).ConfigureAwait(false);
        return list is { Count: > 0 } ? list[0] : default;
    }

    /// <summary>
    /// PATCHes the matching rows and returns the updated representation.
    /// <paramref name="filter"/> is a raw PostgREST filter such as
    /// <c>id=eq.&lt;uuid&gt;</c>.
    /// </summary>
    public async Task UpdateAsync(string table, string filter, object patch, bool useServiceRole, CancellationToken ct)
    {
        using var req = NewRequest(HttpMethod.Patch, $"/{Uri.EscapeDataString(table)}?{filter}", useServiceRole);
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(patch, options: Json);

        using HttpResponseMessage resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp, ct).ConfigureAwait(false);
    }

    /// <summary>SELECTs rows matching <paramref name="filter"/>.</summary>
    public async Task<List<T>> SelectAsync<T>(string table, string filter, bool useServiceRole, CancellationToken ct)
    {
        using var req = NewRequest(HttpMethod.Get, $"/{Uri.EscapeDataString(table)}?{filter}", useServiceRole);
        using HttpResponseMessage resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp, ct).ConfigureAwait(false);

        return await resp.Content.ReadFromJsonAsync<List<T>>(Json, ct).ConfigureAwait(false) ?? new();
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string pathAndQuery, bool useServiceRole)
    {
        var req = new HttpRequestMessage(method, _restBase + pathAndQuery);

        string apiKey = (useServiceRole && !string.IsNullOrEmpty(_serviceRoleKey))
            ? _serviceRoleKey
            : _anonKey;

        req.Headers.Add("apikey", apiKey);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return req;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new HttpRequestException(
            $"Supabase REST {resp.StatusCode}: {body}");
    }

    public void Dispose() => _http.Dispose();
}

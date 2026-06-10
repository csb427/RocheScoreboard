using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Roche_Scoreboard.Services.Cloud;

/// <summary>
/// Static configuration for the Supabase-backed cloud remote.
///
/// Loaded from a `supabase.config.json` file that sits next to the executable.
/// The file is created by the operator using the template documented in
/// <c>supabase/README.md</c>. When the file is missing the cloud remote is
/// disabled and the rest of the application keeps working unchanged.
/// </summary>
public sealed record SupabaseConfig
{
    [JsonPropertyName("url")]
    public string Url { get; init; } = "";

    [JsonPropertyName("anonKey")]
    public string AnonKey { get; init; } = "";

    /// <summary>
    /// Optional. Required only on the host scoreboard machine — it bypasses
    /// RLS so the WPF app can write the authoritative <c>live_state</c>
    /// snapshot back to <c>game_sessions</c>. Remote phones / tablets MUST
    /// never see this key.
    /// </summary>
    [JsonPropertyName("serviceRoleKey")]
    public string? ServiceRoleKey { get; init; }

    /// <summary>
    /// Optional friendly label for the operator console. Defaults to the
    /// machine name when omitted.
    /// </summary>
    [JsonPropertyName("hostLabel")]
    public string? HostLabel { get; init; }

    /// <summary>
    /// Optional. Public base URL of the deployed remote web app
    /// (e.g. <c>https://roche-remote.pages.dev</c>). When set, the desktop
    /// Cloud Remote card builds the join URL and QR as
    /// <c>{remoteAppBaseUrl}/?code={joinCode}</c> so phones land directly
    /// in the right session. When omitted, only the join code is shown
    /// and the operator can still type the URL manually on the remote
    /// device.
    /// </summary>
    [JsonPropertyName("remoteAppBaseUrl")]
    public string? RemoteAppBaseUrl { get; init; }

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Url) &&
        !string.IsNullOrWhiteSpace(AnonKey) &&
        Uri.TryCreate(Url, UriKind.Absolute, out _);

    /// <summary>
    /// Returns the absolute path the loader looks at — same folder as the
    /// running executable, then the user's app data folder as a fallback.
    /// </summary>
    public static string PrimaryConfigPath
    {
        get
        {
            string? exeDir = Path.GetDirectoryName(Environment.ProcessPath
                ?? AppContext.BaseDirectory);
            return Path.Combine(exeDir ?? AppContext.BaseDirectory, "supabase.config.json");
        }
    }

    public static string FallbackConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RocheScoreboard", "supabase.config.json");

    /// <summary>
    /// Loads the first available config file. Returns <c>null</c> when no
    /// valid config exists.
    /// </summary>
    public static SupabaseConfig? TryLoad()
    {
        foreach (string candidate in new[] { PrimaryConfigPath, FallbackConfigPath })
        {
            if (!File.Exists(candidate)) continue;

            try
            {
                string json = File.ReadAllText(candidate);
                var cfg = JsonSerializer.Deserialize<SupabaseConfig>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (cfg is not null && cfg.IsValid) return cfg;
            }
            catch (JsonException)
            {
                // Fall through and try the next location.
            }
            catch (IOException)
            {
                // File momentarily locked — skip silently.
            }
        }

        return null;
    }
}

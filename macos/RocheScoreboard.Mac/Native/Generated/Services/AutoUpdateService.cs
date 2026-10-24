// Generated from Services/AutoUpdateService.cs; edit translator/adapters.
// Source SHA-256: f2c6db8e1c90e1d9f1720220961c9b000cce80c0c1f953a0b8dd3078a3bfbfcb
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Roche_Scoreboard.Services;

/// <summary>
/// Checks GitHub Releases for new versions, downloads the update asset,
/// and launches an updater script that replaces the running executable.
/// </summary>
public sealed class AutoUpdateService
{
    private const string GitHubOwner = "csb427";
    private const string GitHubRepo = "RocheScoreboard";
    private const string ReleasesApiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
    private const string ReleasesPageUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/latest";

    private static readonly HttpClient s_http = CreateHttpClient();
    private static readonly object s_diagnosticGate = new();
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);
    private static readonly string UpdateDataDirectory = Path.Combine(
        NativeStoragePaths.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roche_Scoreboard");

    public static string DiagnosticsPath => Path.Combine(UpdateDataDirectory, "update.log");

    internal static void CleanupStaleStagingDirectories()
    {
        string tempDirectory = Path.GetTempPath();
        DateTime staleBeforeUtc = DateTime.UtcNow.AddDays(-2);
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(tempDirectory, "Roche_Scoreboard_Update_*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    string resolved = Path.GetFullPath(directory);
                    string expectedPrefix = Path.GetFullPath(Path.Combine(tempDirectory, "Roche_Scoreboard_Update_"));
                    if (!resolved.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (Directory.GetLastWriteTimeUtc(resolved) >= staleBeforeUtc) continue;
                    Directory.Delete(resolved, recursive: true);
                }
                catch { /* another updater may still own the directory */ }
            }
        }
        catch { /* temp enumeration is best-effort maintenance */ }
    }

    public UpdateCheckState LastCheckState { get; private set; } = UpdateCheckState.NotChecked;
    public string? LastError { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }

    /// <summary>
    /// Latest release info discovered by <see cref="CheckForUpdateAsync"/>.
    /// Null when no update is available or check has not run.
    /// </summary>
    public GitHubRelease? LatestRelease { get; private set; }

    /// <summary>
    /// Whether an update newer than the current version is available.
    /// </summary>
    public bool UpdateAvailable => LatestRelease is not null;

    public long LatestPackageSizeBytes => LatestRelease is null ? 0 : FindAsset(LatestRelease)?.Size ?? 0;

    public bool LatestPackageHasDigest => LatestRelease is not null &&
        FindAsset(LatestRelease)?.Digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Clears the cached update so prompts are not repeated in the same session.
    /// The next call to <see cref="CheckForUpdateAsync"/> will re-evaluate.
    /// </summary>
    public void DismissUpdate() => LatestRelease = null;

    /// <summary>
    /// Current application version from the assembly.
    /// </summary>
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    /// <summary>
    /// Queries the GitHub Releases API and sets <see cref="LatestRelease"/>
    /// if a newer version exists. Returns true when an update is available.
    /// </summary>
    public async Task<bool> CheckForUpdateAsync(CancellationToken ct = default)
    {
        await _checkGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
        LastCheckState = UpdateCheckState.Checking;
        LastError = null;
        LatestRelease = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CheckTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApiUrl);
            using var response = await s_http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: timeout.Token);

            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
                throw new InvalidDataException("GitHub returned a release without a version tag.");

            if (!TryParseReleaseVersion(release.TagName, out Version? remoteVersion) || remoteVersion is null)
                throw new InvalidDataException($"Release tag '{release.TagName}' is not a valid version.");

            if (remoteVersion > CurrentVersion)
            {
                LatestRelease = release;
                LastCheckState = FindAsset(release) is null
                    ? UpdateCheckState.UpdateAvailableWithoutCompatibleAsset
                    : UpdateCheckState.UpdateAvailable;
                LastCheckedAt = DateTimeOffset.Now;
                WriteDiagnostic($"Update check: {CurrentVersion.ToString(3)} -> {remoteVersion.ToString(3)}; state={LastCheckState}.");
                return true;
            }

            LastCheckState = UpdateCheckState.UpToDate;
            LastCheckedAt = DateTimeOffset.Now;
            WriteDiagnostic($"Update check: current={CurrentVersion.ToString(3)}, latest={remoteVersion.ToString(3)}.");
            return false;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            SetCheckFailure($"The update server did not respond within {CheckTimeout.TotalSeconds:0} seconds.", ex);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException or InvalidDataException)
        {
            SetCheckFailure(ex.Message, ex);
            return false;
        }
        }
        finally
        {
            _checkGate.Release();
        }
    }

    /// <summary>
    /// Downloads the complete portable ZIP from <see cref="LatestRelease"/>,
    /// stages it outside the application folder, and launches a native Mac
    /// updater that waits for this process to exit, replaces the application
    /// files (with retry on file-in-use), and re-launches the app.
    /// Writes a log in the per-user Roche_Scoreboard data directory so failures
    /// can be diagnosed if anything goes wrong.
    /// </summary>
    public Task<bool> DownloadAndApplyAsync(IProgress<double>? progress = null, CancellationToken ct = default)
        => DownloadAndApplyCoreAsync(progress is null ? null : new LegacyProgressAdapter(progress), ct);

    public Task<bool> DownloadAndApplyDetailedAsync(
        IProgress<UpdateProgressInfo>? progress = null,
        CancellationToken ct = default)
        => DownloadAndApplyCoreAsync(progress, ct);

    private async Task<bool> DownloadAndApplyCoreAsync(
        IProgress<UpdateProgressInfo>? progress,
        CancellationToken ct)
    {
        LastError = null;
        try { return await MacUpdater.ApplyAsync(LatestRelease is null ? null : FindAsset(LatestRelease), progress, ct); }
        catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException or System.Security.SecurityException)
        {
            LastError = ex.Message;
            WriteDiagnostic("Mac update failed: " + ex);
            return false;
        }
    }

    private static GitHubReleaseAsset? FindAsset(GitHubRelease release) => MacUpdater.FindAsset(release);

    private sealed class LegacyProgressAdapter(IProgress<double> inner) : IProgress<UpdateProgressInfo>
    {
        public void Report(UpdateProgressInfo value) => inner.Report(value.Progress);
    }

    public static string GetReleasePageUrl() => ReleasesPageUrl;

    public static void OpenReleasePageInBrowser()
    {
        OpenUrlInBrowser(ReleasesPageUrl);
    }

    public string? GetLatestAssetDownloadUrl()
    {
        if (LatestRelease is null)
            return null;

        return FindAsset(LatestRelease)?.BrowserDownloadUrl;
    }

    public static void OpenUrlInBrowser(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("No URL is available.");

        ProcessStartInfo psi = new()
        {
            FileName = url,
            UseShellExecute = true
        };

        Process.Start(psi);
    }

    private static HttpClient CreateHttpClient()
    {
        // Disable the per-request timeout entirely — large release assets
        // (84 MB+) easily exceed a 30s budget on a normal home connection,
        // and HttpClient.Timeout applies to the whole operation including
        // body read even with ResponseHeadersRead. We rely on the caller's
        // CancellationToken for cancellation instead.
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Roche-Scoreboard-Updater/1.0");
        // GitHub recommends an explicit Accept header and API version so the
        // response shape stays stable even if the default version changes.
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static bool TryParseReleaseVersion(string tag, out Version? version)
    {
        string value = tag.Trim().TrimStart('v', 'V');
        int suffix = value.IndexOfAny(['-', '+']);
        if (suffix >= 0) value = value[..suffix];
        return Version.TryParse(value, out version);
    }

    private void SetCheckFailure(string message, Exception exception)
    {
        LatestRelease = null;
        LastCheckState = UpdateCheckState.Failed;
        LastCheckedAt = DateTimeOffset.Now;
        LastError = message;
        WriteDiagnostic($"Update check failed: {exception}");
    }

    private static void WriteDiagnostic(string message)
    {
        lock (s_diagnosticGate)
        {
            try
            {
                Directory.CreateDirectory(UpdateDataDirectory);
                if (File.Exists(DiagnosticsPath) && new FileInfo(DiagnosticsPath).Length > 1024 * 1024)
                {
                    string backupPath = DiagnosticsPath + ".previous";
                    try
                    {
                        File.Copy(DiagnosticsPath, backupPath, overwrite: true);
                        File.WriteAllText(DiagnosticsPath, string.Empty);
                    }
                    catch { /* retain the existing log if rotation is blocked */ }
                }
                File.AppendAllText(DiagnosticsPath, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}

public enum UpdateProgressStage
{
    Preparing,
    Downloading,
    Verifying,
    Extracting,
    Staging,
    Ready
}

public sealed record UpdateProgressInfo(
    UpdateProgressStage Stage,
    double Progress,
    long BytesReceived,
    long TotalBytes,
    int Attempt,
    int MaximumAttempts,
    string Message);

public enum UpdateCheckState
{
    NotChecked,
    Checking,
    UpToDate,
    UpdateAvailable,
    UpdateAvailableWithoutCompatibleAsset,
    Failed
}

/// <summary>
/// Minimal representation of a GitHub API release response.
/// </summary>
public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("zipball_url")]
    public string? ZipballUrl { get; set; }

    [JsonPropertyName("assets")]
    public GitHubReleaseAsset[]? Assets { get; set; }
}

/// <summary>
/// Minimal representation of a GitHub release asset.
/// </summary>
public sealed class GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("digest")]
    public string? Digest { get; set; }
}

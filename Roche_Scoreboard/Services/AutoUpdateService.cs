using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
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
    private const string GitHubRepo = "Roche_Scoreboard";
    private const string ReleasesApiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

    private static readonly HttpClient s_http = CreateHttpClient();

    /// <summary>
    /// Latest release info discovered by <see cref="CheckForUpdateAsync"/>.
    /// Null when no update is available or check has not run.
    /// </summary>
    public GitHubRelease? LatestRelease { get; private set; }

    /// <summary>
    /// Whether an update newer than the current version is available.
    /// </summary>
    public bool UpdateAvailable => LatestRelease is not null;

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
        try
        {
            var release = await s_http.GetFromJsonAsync<GitHubRelease>(ReleasesApiUrl, ct);

            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
                return false;

            string tag = release.TagName.TrimStart('v', 'V');

            if (!Version.TryParse(tag, out Version? remoteVersion))
                return false;

            if (remoteVersion > CurrentVersion)
            {
                LatestRelease = release;
                return true;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or System.Text.Json.JsonException)
        {
            // Network or deserialization issues — silently ignore; the user can try later.
        }

        LatestRelease = null;
        return false;
    }

    /// <summary>
    /// Downloads the release asset (zip or exe) from <see cref="LatestRelease"/>,
    /// stages it outside the application folder, and launches a PowerShell
    /// updater that waits for this process to exit, replaces the application
    /// files (with retry on file-in-use), and re-launches the app.
    /// Writes a log to <c>%TEMP%\Roche_Scoreboard_Update.log</c> so failures
    /// can be diagnosed if anything goes wrong.
    /// </summary>
    public async Task<bool> DownloadAndApplyAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (LatestRelease is null)
            return false;

        var (assetUrl, isExe) = FindAssetUrl(LatestRelease);
        if (assetUrl is null)
            return false;

        string appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Stage the update OUTSIDE the app folder so the updater can freely
        // overwrite the app dir without trying to delete the script that's
        // currently executing from inside it.
        string tempRoot = Path.Combine(Path.GetTempPath(), "Roche_Scoreboard_Update_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        string logPath = Path.Combine(Path.GetTempPath(), "Roche_Scoreboard_Update.log");

        try
        {
            using var response = await s_http.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? -1;
            long bytesRead = 0;

            string currentExe = Environment.ProcessPath ?? Path.Combine(appDir, "Roche_Scoreboard.exe");
            string currentExeName = Path.GetFileName(currentExe);

            string sourceDir;

            if (isExe)
            {
                string downloadedExe = Path.Combine(tempRoot, currentExeName);
                await using (var fileStream = new FileStream(downloadedExe, FileMode.Create, FileAccess.Write, FileShare.None))
                await using (var downloadStream = await response.Content.ReadAsStreamAsync(ct))
                {
                    byte[] buffer = new byte[81920];
                    int read;
                    while ((read = await downloadStream.ReadAsync(buffer, ct)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                        bytesRead += read;
                        if (totalBytes > 0) progress?.Report((double)bytesRead / totalBytes);
                    }
                }
                progress?.Report(1.0);

                sourceDir = tempRoot;
            }
            else
            {
                string zipPath = Path.Combine(tempRoot, "update.zip");
                string extractDir = Path.Combine(tempRoot, "extracted");
                Directory.CreateDirectory(extractDir);

                await using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                await using (var downloadStream = await response.Content.ReadAsStreamAsync(ct))
                {
                    byte[] buffer = new byte[81920];
                    int read;
                    while ((read = await downloadStream.ReadAsync(buffer, ct)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                        bytesRead += read;
                        if (totalBytes > 0) progress?.Report((double)bytesRead / totalBytes);
                    }
                }
                progress?.Report(1.0);

                ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

                // The extract may have a single subfolder; detect it
                sourceDir = extractDir;
                string[] subDirs = Directory.GetDirectories(extractDir);
                if (subDirs.Length == 1 && Directory.GetFiles(extractDir).Length == 0)
                    sourceDir = subDirs[0];
            }

            // Sanity check: the staged source must contain the application exe
            // we expect to relaunch — otherwise the user ends up with a broken
            // install that can't reopen.
            string stagedExe = Path.Combine(sourceDir, currentExeName);
            if (!File.Exists(stagedExe))
            {
                // Try to find any exe with the same stem (handles renamed releases)
                string[] candidates = Directory.GetFiles(sourceDir, "*.exe", SearchOption.TopDirectoryOnly);
                if (candidates.Length == 1)
                {
                    stagedExe = candidates[0];
                }
                else
                {
                    return false;
                }
            }

            int currentPid = Environment.ProcessId;
            string scriptPath = Path.Combine(tempRoot, "apply_update.ps1");
            string script = BuildUpdaterScript(currentPid, currentExeName, currentExe, appDir, sourceDir, tempRoot, logPath);
            await File.WriteAllTextAsync(scriptPath, script, ct);

            // Launch PowerShell hidden, bypassing execution policy. Detach from
            // this process so it survives our shutdown.
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
                WorkingDirectory = tempRoot
            };
            var proc = Process.Start(psi);
            return proc is not null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            try { File.AppendAllText(logPath, $"[{DateTime.Now:O}] Download/stage failed: {ex}\r\n"); } catch { }
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch { }
            return false;
        }
    }

    /// <summary>
    /// Builds the PowerShell updater script. The script:
    /// 1. Waits for the running process (by PID) to exit with timeout fallback to name-based check.
    /// 2. Copies the staged update over the application directory, retrying on file-in-use errors.
    /// 3. Launches the new executable, verifies it started, and retries if not.
    /// 4. Schedules deletion of the staged temp directory so the script can self-clean.
    /// 5. Logs every step to <paramref name="logPath"/> for post-mortem diagnostics.
    /// </summary>
    private static string BuildUpdaterScript(int pid, string exeName, string currentExe, string appDir, string sourceDir, string tempRoot, string logPath)
    {
        // PowerShell escaping: single-quote string literals, double any embedded single quotes.
        static string Esc(string s) => s.Replace("'", "''");

        return $$"""
            $ErrorActionPreference = 'Continue'
            $log = '{{Esc(logPath)}}'
            function Write-Log($m) { try { Add-Content -Path $log -Value ("[{0:O}] {1}" -f (Get-Date), $m) } catch {} }

            Write-Log "Updater started. PID={{pid}} exe='{{Esc(currentExe)}}' app='{{Esc(appDir)}}' src='{{Esc(sourceDir)}}'"

            # 1. Wait for the running app to exit (by PID, with name fallback)
            try {
                $p = Get-Process -Id {{pid}} -ErrorAction SilentlyContinue
                if ($p) {
                    Write-Log "Waiting on PID {{pid}}..."
                    Wait-Process -Id {{pid}} -Timeout 30 -ErrorAction SilentlyContinue
                }
            } catch { Write-Log "PID wait error: $_" }

            $deadline = (Get-Date).AddSeconds(30)
            while ((Get-Date) -lt $deadline) {
                $still = Get-Process -Name '{{Esc(System.IO.Path.GetFileNameWithoutExtension(exeName))}}' -ErrorAction SilentlyContinue
                if (-not $still) { break }
                Start-Sleep -Milliseconds 500
            }
            # Last-resort: kill anything still holding files
            Get-Process -Name '{{Esc(System.IO.Path.GetFileNameWithoutExtension(exeName))}}' -ErrorAction SilentlyContinue | ForEach-Object {
                try { Write-Log "Force-stopping leftover process $($_.Id)"; Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue } catch {}
            }
            Start-Sleep -Milliseconds 750

            # 2. Copy files with retry (handles AV / locked files)
            $src = '{{Esc(sourceDir)}}'
            $dst = '{{Esc(appDir)}}'
            $maxAttempts = 8
            $copied = $false
            for ($i = 1; $i -le $maxAttempts; $i++) {
                try {
                    Write-Log "Copy attempt $i: '$src' -> '$dst'"
                    # /E recurse, /COPY:DAT, /R:3 retries, /W:1 wait, /NFL /NDL no per-file log,
                    # /NJH /NJS no header/summary, /XJ skip junctions, /IS include same.
                    $robo = Start-Process -FilePath 'robocopy.exe' -ArgumentList @($src, $dst, '/E', '/COPY:DAT', '/R:3', '/W:1', '/NFL', '/NDL', '/NJH', '/NJS', '/XJ', '/IS') -Wait -PassThru -WindowStyle Hidden
                    Write-Log "Robocopy exit code: $($robo.ExitCode)"
                    # Robocopy: 0-7 are success codes (8+ are failure)
                    if ($robo.ExitCode -lt 8) { $copied = $true; break }
                } catch {
                    Write-Log "Copy error: $_"
                }
                Start-Sleep -Seconds 2
            }

            if (-not $copied) {
                Write-Log "Robocopy failed; falling back to Copy-Item."
                try {
                    Copy-Item -Path (Join-Path $src '*') -Destination $dst -Recurse -Force -ErrorAction Stop
                    $copied = $true
                } catch { Write-Log "Copy-Item failed: $_" }
            }

            if (-not $copied) {
                Write-Log "Update apply FAILED. Attempting to relaunch original exe so the user is not left without an app."
            }

            # 3. Launch the (now-updated) application — retry to be absolutely sure
            $exe = '{{Esc(currentExe)}}'
            if (-not (Test-Path -LiteralPath $exe)) {
                # Fall back to first .exe in the app dir matching the stem
                $alt = Get-ChildItem -LiteralPath $dst -Filter '*.exe' -File -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($alt) { $exe = $alt.FullName }
            }

            $launched = $false
            for ($i = 1; $i -le 5; $i++) {
                try {
                    Write-Log "Launch attempt $i: '$exe'"
                    $proc = Start-Process -FilePath $exe -WorkingDirectory $dst -PassThru -ErrorAction Stop
                    Start-Sleep -Milliseconds 1500
                    if ($proc -and -not $proc.HasExited) {
                        Write-Log "Launched PID $($proc.Id)"
                        $launched = $true
                        break
                    }
                    Write-Log "Process exited immediately (HasExited=$($proc.HasExited))."
                } catch {
                    Write-Log "Launch error: $_"
                }
                Start-Sleep -Seconds 1
            }

            if (-not $launched) {
                Write-Log "Final fallback: cmd /c start"
                try { Start-Process -FilePath 'cmd.exe' -ArgumentList @('/c', 'start', '""', "`"$exe`"") -WindowStyle Hidden } catch { Write-Log "Fallback launch failed: $_" }
            }

            # 4. Self-clean staged temp dir AFTER this script ends, so we don't
            #    try to delete the directory we are running from.
            $temp = '{{Esc(tempRoot)}}'
            try {
                Start-Process -FilePath 'cmd.exe' -ArgumentList @('/c', 'ping', '127.0.0.1', '-n', '3', '>nul', '&', 'rmdir', '/s', '/q', "`"$temp`"") -WindowStyle Hidden
            } catch {}
            Write-Log "Updater finished. launched=$launched copied=$copied"
            """;
    }


    /// <summary>
    /// Finds the best asset URL from the release. Prefers .exe, then .zip, then zipball fallback.
    /// Returns the URL and whether it is a direct exe download.
    /// </summary>
    private static (string? Url, bool IsExe) FindAssetUrl(GitHubRelease release)
    {
        if (release.Assets is not null)
        {
            // Prefer .exe asset
            foreach (var asset in release.Assets)
            {
                if (asset.BrowserDownloadUrl is not null &&
                    asset.Name is not null &&
                    asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return (asset.BrowserDownloadUrl, true);
                }
            }

            // Fall back to .zip asset
            foreach (var asset in release.Assets)
            {
                if (asset.BrowserDownloadUrl is not null &&
                    asset.Name is not null &&
                    asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    return (asset.BrowserDownloadUrl, false);
                }
            }
        }

        // Fallback: GitHub auto-generated source zipball (not ideal but better than nothing)
        return (release.ZipballUrl, false);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Roche-Scoreboard-Updater/1.0");
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }
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
}

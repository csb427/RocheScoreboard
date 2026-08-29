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
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);
    private static readonly string UpdateDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roche_Scoreboard");

    public static string DiagnosticsPath => Path.Combine(UpdateDataDirectory, "update.log");

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

    /// <summary>
    /// Downloads the complete portable ZIP from <see cref="LatestRelease"/>,
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

        GitHubReleaseAsset? asset = FindAsset(LatestRelease);
        if (asset?.BrowserDownloadUrl is null)
        {
            LastError = "This release has no compatible Roche Scoreboard Windows x64 ZIP package.";
            WriteDiagnostic(LastError);
            return false;
        }

        string assetUrl = asset.BrowserDownloadUrl;
        bool isExe = asset.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true;

        // Always derive the install directory from the running process path.
        // This is correct for the current self-contained folder package and
        // remains safe if a future release returns to single-file extraction.
        string? exePath = Environment.ProcessPath;
        string appDir = !string.IsNullOrEmpty(exePath)
            ? Path.GetDirectoryName(exePath)!
            : AppContext.BaseDirectory;
        appDir = appDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Stage the update OUTSIDE the app folder so the updater can freely
        // overwrite the app dir without trying to delete the script that's
        // currently executing from inside it.
        string tempRoot = Path.Combine(Path.GetTempPath(), "Roche_Scoreboard_Update_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        string logPath = DiagnosticsPath;

        try
        {
            using var response = await s_http.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? -1;
            long bytesRead = 0;

            string currentExe = Environment.ProcessPath ?? Path.Combine(appDir, "Roche Scoreboard.exe");
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

                ValidateDownload(downloadedExe, bytesRead, totalBytes, asset);

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

                ValidateDownload(zipPath, bytesRead, totalBytes, asset);

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
                    string normalizedExe = Path.Combine(sourceDir, currentExeName);
                    File.Copy(stagedExe, normalizedExe, overwrite: true);
                    stagedExe = normalizedExe;
                }
                else
                {
                    return false;
                }
            }

            ValidatePortableExecutable(stagedExe);
            ValidateReleasePackage(sourceDir, currentExeName);

            if (!TryParseReleaseVersion(LatestRelease.TagName ?? string.Empty, out Version? expectedVersion) || expectedVersion is null)
                throw new InvalidDataException("The update release version could not be validated.");

            int currentPid = Environment.ProcessId;
            string scriptPath = Path.Combine(tempRoot, "apply_update.ps1");
            string script = BuildUpdaterScript(currentPid, currentExeName, currentExe, appDir, sourceDir,
                tempRoot, logPath, expectedVersion.ToString(3));
            await File.WriteAllTextAsync(scriptPath, script, ct);

            // Launch PowerShell hidden, bypassing execution policy. Detach from
            // this process so it survives our shutdown.
            bool launched = TryLaunchUpdaterScript(scriptPath, tempRoot);
            if (!launched)
            {
                try { File.AppendAllText(logPath, $"[{DateTime.Now:O}] Failed to launch updater script using all runners.\r\n"); } catch { }
            }
            return launched;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException or OperationCanceledException)
        {
            LastError = ex.Message;
            WriteDiagnostic($"Download/stage failed: {ex}");
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
    private static string BuildUpdaterScript(int pid, string exeName, string currentExe, string appDir,
        string sourceDir, string tempRoot, string logPath, string expectedVersion)
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

            # 2a. Rename the existing EXE out of the way. Windows permits
            #     renaming an open/locked executable even when it cannot be
            #     deleted or overwritten, so this guarantees the copy step
            #     below can write a fresh EXE in its place. Without this, a
            #     lingering file handle (AV scan, prefetch, taskbar pin) can
            #     cause robocopy to silently skip the EXE and still return
            #     success code 1, leaving the user on the old version.
            $exePath = '{{Esc(currentExe)}}'
            if (Test-Path -LiteralPath $exePath) {
                $oldExePath = "$exePath.old"
                for ($r = 1; $r -le 5; $r++) {
                    try {
                        if (Test-Path -LiteralPath $oldExePath) {
                            Remove-Item -LiteralPath $oldExePath -Force -ErrorAction SilentlyContinue
                        }
                        Rename-Item -LiteralPath $exePath -NewName ([System.IO.Path]::GetFileName($oldExePath)) -Force -ErrorAction Stop
                        Write-Log "Renamed existing EXE to '$oldExePath'"
                        break
                    } catch {
                        Write-Log "Rename attempt $r failed: $_"
                        Start-Sleep -Seconds 1
                    }
                }
            }

            # 2b. Copy files with retry (handles AV / locked files)
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

            # 2c. Verify the EXE was actually written. If the rename succeeded
            #     but the copy didn't drop a new EXE in its place, the user is
            #     now without an app — restore the old one so they aren't left
            #     in a broken state.
            if (-not (Test-Path -LiteralPath $exePath)) {
                Write-Log "Post-copy EXE missing at '$exePath' — attempting to restore from .old"
                if (Test-Path -LiteralPath "$exePath.old") {
                    try { Rename-Item -LiteralPath "$exePath.old" -NewName ([System.IO.Path]::GetFileName($exePath)) -Force } catch { Write-Log "Restore failed: $_" }
                }
                $copied = $false
            } else {
                try {
                    $writtenVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).ProductVersion
                    if ([string]::IsNullOrWhiteSpace($writtenVersion)) {
                        $writtenVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).FileVersion
                    }
                    Write-Log "New EXE present at '$exePath'; version='$writtenVersion', expected='{{Esc(expectedVersion)}}'."
                    if (-not $writtenVersion.StartsWith('{{Esc(expectedVersion)}}', [System.StringComparison]::OrdinalIgnoreCase)) {
                        Write-Log "Version verification failed — rolling back."
                        Remove-Item -LiteralPath $exePath -Force -ErrorAction SilentlyContinue
                        if (Test-Path -LiteralPath "$exePath.old") {
                            Rename-Item -LiteralPath "$exePath.old" -NewName ([System.IO.Path]::GetFileName($exePath)) -Force
                        }
                        $copied = $false
                    }
                } catch {
                    Write-Log "Version verification error: $_"
                    Remove-Item -LiteralPath $exePath -Force -ErrorAction SilentlyContinue
                    if (Test-Path -LiteralPath "$exePath.old") {
                        try { Rename-Item -LiteralPath "$exePath.old" -NewName ([System.IO.Path]::GetFileName($exePath)) -Force } catch {}
                    }
                    $copied = $false
                }
            }

            if (-not $copied) {
                Write-Log "Update apply FAILED. Attempting to relaunch original exe so the user is not left without an app."
            }

            # 2d. Strip Mark-of-the-Web from the freshly-written EXE so SmartScreen
            #     doesn't silently block the auto-relaunch. Files downloaded via
            #     HttpClient and then copied retain a Zone.Identifier ADS that can
            #     cause Start-Process to fail without a visible error.
            try {
                Unblock-File -LiteralPath $exePath -ErrorAction SilentlyContinue
                Get-ChildItem -LiteralPath $dst -Filter '*.exe' -File -ErrorAction SilentlyContinue | ForEach-Object {
                    try { Unblock-File -LiteralPath $_.FullName -ErrorAction SilentlyContinue } catch {}
                }
            } catch { Write-Log "Unblock-File failed: $_" }

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
                Write-Log "Updated app did not stay running — restoring the previous executable."
                if (Test-Path -LiteralPath "$exePath.old") {
                    try {
                        Remove-Item -LiteralPath $exePath -Force -ErrorAction SilentlyContinue
                        Rename-Item -LiteralPath "$exePath.old" -NewName ([System.IO.Path]::GetFileName($exePath)) -Force -ErrorAction Stop
                        Unblock-File -LiteralPath $exePath -ErrorAction SilentlyContinue
                        Start-Process -FilePath $exePath -WorkingDirectory $dst -ErrorAction Stop
                        Write-Log "Rollback succeeded and the previous version was relaunched."
                    } catch { Write-Log "Rollback launch failed: $_" }
                }
                $copied = $false
            }

            # 4. Self-clean staged temp dir AFTER this script ends, so we don't
            #    try to delete the directory we are running from. Also queues
            #    deletion of the .old EXE backup once it's no longer locked.
            $temp = '{{Esc(tempRoot)}}'
            $oldExeForCleanup = if ($copied -and $launched) { "$exePath.old" } else { '' }
            try {
                $cleanupCmd = if ($oldExeForCleanup) {
                    "ping 127.0.0.1 -n 5 >nul & del /f /q `"$oldExeForCleanup`" 2>nul & rmdir /s /q `"$temp`""
                } else {
                    "ping 127.0.0.1 -n 5 >nul & rmdir /s /q `"$temp`""
                }
                Start-Process -FilePath 'cmd.exe' -ArgumentList @('/c', $cleanupCmd) -WindowStyle Hidden
            } catch {}
            Write-Log "Updater finished. launched=$launched copied=$copied"
            """;
    }


    private static bool TryLaunchUpdaterScript(string scriptPath, string workingDirectory)
    {
        string escapedScript = scriptPath.Replace("\"", "\"\"");

        ProcessStartInfo[] candidates =
        [
            new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{escapedScript}\"",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory
            },
            new ProcessStartInfo
            {
                FileName = "pwsh.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{escapedScript}\"",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory
            },
            new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"\" powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{escapedScript}\"",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory
            }
        ];

        foreach (var psi in candidates)
        {
            try
            {
                var proc = Process.Start(psi);
                if (proc is not null)
                    return true;
            }
            catch
            {
                // try next launcher
            }
        }

        return false;
    }

    /// <summary>
    /// Selects a complete portable release package. GitHub's generated source
    /// archives and loose executables are deliberately excluded: this app needs
    /// its managed dependencies, self-contained .NET runtime and LibVLC plugin
    /// tree beside the executable.
    /// </summary>
    private static GitHubReleaseAsset? FindAsset(GitHubRelease release)
    {
        if (release.Assets is null) return null;

        static int Rank(GitHubReleaseAsset asset)
        {
            string name = asset.Name ?? "";
            if (string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl) || asset.Size <= 0) return int.MaxValue;
            bool appName = name.Contains("Roche", StringComparison.OrdinalIgnoreCase) &&
                           name.Contains("Scoreboard", StringComparison.OrdinalIgnoreCase);
            bool x64 = name.Contains("x64", StringComparison.OrdinalIgnoreCase) ||
                       !name.Contains("arm", StringComparison.OrdinalIgnoreCase);
            bool canonical = name.StartsWith("Roche-Scoreboard-v", StringComparison.OrdinalIgnoreCase) &&
                             name.Contains("Windows-x64", StringComparison.OrdinalIgnoreCase);
            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && appName && x64)
                return canonical ? 0 : 1;
            return int.MaxValue;
        }

        return release.Assets.OrderBy(Rank).FirstOrDefault(a => Rank(a) < int.MaxValue);
    }

    private static void ValidateReleasePackage(string sourceDir, string exeName)
    {
        string appStem = Path.GetFileNameWithoutExtension(exeName);
        string[] requiredFiles =
        [
            exeName,
            appStem + ".dll",
            appStem + ".deps.json",
            appStem + ".runtimeconfig.json",
            "coreclr.dll",
            "hostfxr.dll",
            Path.Combine("libvlc", "win-x64", "libvlc.dll"),
            Path.Combine("libvlc", "win-x64", "libvlccore.dll")
        ];

        string[] missing = requiredFiles
            .Where(relative => !File.Exists(Path.Combine(sourceDir, relative)))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException(
                "The downloaded release is incomplete and was not installed. Missing: " +
                string.Join(", ", missing));

        string pluginDirectory = Path.Combine(sourceDir, "libvlc", "win-x64", "plugins");
        int pluginCount = Directory.Exists(pluginDirectory)
            ? Directory.GetFiles(pluginDirectory, "*.dll", SearchOption.AllDirectories).Length
            : 0;
        if (pluginCount < 100)
            throw new InvalidDataException(
                $"The downloaded release contains an incomplete LibVLC plugin set ({pluginCount} files). It was not installed.");
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

    private static void ValidateDownload(string path, long downloadedBytes, long contentLength, GitHubReleaseAsset asset)
    {
        if (downloadedBytes <= 0 || !File.Exists(path))
            throw new InvalidDataException("The downloaded update is empty.");
        if (contentLength > 0 && downloadedBytes != contentLength)
            throw new InvalidDataException($"The download was incomplete ({downloadedBytes:N0} of {contentLength:N0} bytes).");
        if (asset.Size > 0 && downloadedBytes != asset.Size)
            throw new InvalidDataException($"The download size did not match GitHub ({downloadedBytes:N0} of {asset.Size:N0} bytes).");

        if (asset.Digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true)
        {
            string expected = asset.Digest[7..];
            using var stream = File.OpenRead(path);
            string actual = Convert.ToHexString(SHA256.HashData(stream));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update failed its SHA-256 integrity check.");
        }
    }

    private static void ValidatePortableExecutable(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 1024)
            throw new InvalidDataException("The staged application executable is missing or invalid.");
        using FileStream stream = File.OpenRead(path);
        if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
            throw new InvalidDataException("The staged application is not a valid Windows executable.");
    }

    private static void WriteDiagnostic(string message)
    {
        try
        {
            Directory.CreateDirectory(UpdateDataDirectory);
            File.AppendAllText(DiagnosticsPath, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}

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

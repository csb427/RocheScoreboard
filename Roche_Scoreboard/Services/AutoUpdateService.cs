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
    private static readonly TimeSpan DownloadIdleTimeout = TimeSpan.FromSeconds(45);
    private const int DownloadAttempts = 3;
    private const long MaximumExpandedPackageBytes = 2L * 1024 * 1024 * 1024;
    private static readonly string UpdateDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roche_Scoreboard");

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
    /// stages it outside the application folder, and launches a PowerShell
    /// updater that waits for this process to exit, replaces the application
    /// files (with retry on file-in-use), and re-launches the app.
    /// Writes a log to <c>%TEMP%\Roche_Scoreboard_Update.log</c> so failures
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
        if (LatestRelease is null)
        {
            LastError = "Check for updates before downloading.";
            return false;
        }

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

        string logPath = DiagnosticsPath;

        try
        {
            ct.ThrowIfCancellationRequested();
            EnsureInstallDirectoryWritable(appDir);
            EnsureSufficientDiskSpace(Path.GetTempPath(), Math.Max(512L * 1024 * 1024, asset.Size * 4));
            EnsureSufficientDiskSpace(appDir, Math.Max(256L * 1024 * 1024, asset.Size * 2));
            Directory.CreateDirectory(tempRoot);
            progress?.Report(new UpdateProgressInfo(UpdateProgressStage.Preparing, 0, 0, asset.Size, 0, DownloadAttempts,
                "Preparing a safe staging area…"));

            string currentExe = Environment.ProcessPath ?? Path.Combine(appDir, "Roche Scoreboard.exe");
            string currentExeName = Path.GetFileName(currentExe);

            string sourceDir;

            if (isExe)
            {
                string downloadedExe = Path.Combine(tempRoot, currentExeName);
                await DownloadFileWithRetryAsync(assetUrl, downloadedExe, asset, progress, ct).ConfigureAwait(false);

                sourceDir = tempRoot;
            }
            else
            {
                string zipPath = Path.Combine(tempRoot, "update.zip");
                string extractDir = Path.Combine(tempRoot, "extracted");
                Directory.CreateDirectory(extractDir);

                await DownloadFileWithRetryAsync(assetUrl, zipPath, asset, progress, ct).ConfigureAwait(false);

                progress?.Report(new UpdateProgressInfo(UpdateProgressStage.Verifying, 1, asset.Size, asset.Size, 0, DownloadAttempts,
                    "Checking package integrity and file paths…"));
                ValidateArchiveBeforeExtraction(zipPath);
                progress?.Report(new UpdateProgressInfo(UpdateProgressStage.Extracting, 1, asset.Size, asset.Size, 0, DownloadAttempts,
                    "Extracting the verified Windows release…"));
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
                    throw new InvalidDataException(
                        $"The release package does not contain the expected '{currentExeName}' executable.");
                }
            }

            ValidatePortableExecutable(stagedExe);
            ValidateReleasePackage(sourceDir, currentExeName);
            progress?.Report(new UpdateProgressInfo(UpdateProgressStage.Staging, 1, asset.Size, asset.Size, 0, DownloadAttempts,
                "Validating the app, .NET runtime and libVLC…"));

            if (!TryParseReleaseVersion(LatestRelease.TagName ?? string.Empty, out Version? expectedVersion) || expectedVersion is null)
                throw new InvalidDataException("The update release version could not be validated.");

            int currentPid = Environment.ProcessId;
            string scriptPath = Path.Combine(tempRoot, "apply_update.ps1");
            string script = BuildUpdaterScript(currentPid, currentExeName, currentExe, appDir, sourceDir,
                tempRoot, logPath, expectedVersion.ToString(3));
            await File.WriteAllTextAsync(scriptPath, script, ct).ConfigureAwait(false);

            // Launch PowerShell hidden, bypassing execution policy. Detach from
            // this process so it survives our shutdown.
            bool launched = TryLaunchUpdaterScript(scriptPath, tempRoot);
            if (!launched)
            {
                try { File.AppendAllText(logPath, $"[{DateTime.Now:O}] Failed to launch updater script using all runners.\r\n"); } catch { }
            }
            if (launched)
                progress?.Report(new UpdateProgressInfo(UpdateProgressStage.Ready, 1, asset.Size, asset.Size, 0, DownloadAttempts,
                    "Update verified. Restarting Roche Scoreboard…"));
            return launched;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException
                                      or TaskCanceledException or OperationCanceledException or TimeoutException)
        {
            LastError = ex is OperationCanceledException && ct.IsCancellationRequested
                ? "The update download was cancelled."
                : ex.Message;
            WriteDiagnostic($"Download/stage failed: {ex}");
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch { }
            return false;
        }
    }

    /// <summary>
    /// Builds the PowerShell updater script. The script:
    /// 1. Waits for the exact running process to exit without disturbing another scoreboard instance.
    /// 2. Takes a complete rollback copy, then installs the staged release with bounded retries.
    /// 3. Verifies and launches the new executable, restoring the complete prior release on failure.
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

            # Never terminate every process sharing the app name: a second
            # scoreboard instance may be running a different match or copy.
            # If the exact source PID ignored normal shutdown, stop only it.
            $still = Get-Process -Id {{pid}} -ErrorAction SilentlyContinue
            if ($still) {
                try {
                    Write-Log "Force-stopping source PID {{pid}} after the shutdown timeout."
                    Stop-Process -Id {{pid}} -Force -ErrorAction Stop
                } catch { Write-Log "Source PID stop error: $_" }
            }
            Start-Sleep -Milliseconds 750

            # Create a full rollback copy before changing any installed file.
            # Restoring only the old EXE is insufficient for a multi-file .NET
            # release because partially updated DLLs may be incompatible with it.
            $src = '{{Esc(sourceDir)}}'
            $dst = '{{Esc(appDir)}}'
            $backup = Join-Path '{{Esc(tempRoot)}}' 'previous-release'
            $backupReady = $false
            try {
                New-Item -ItemType Directory -Path $backup -Force | Out-Null
                $backupCopy = Start-Process -FilePath 'robocopy.exe' -ArgumentList @($dst, $backup, '/E', '/COPY:DAT', '/R:2', '/W:1', '/NFL', '/NDL', '/NJH', '/NJS', '/XJ') -Wait -PassThru -WindowStyle Hidden
                $backupReady = $backupCopy.ExitCode -lt 8 -and (Test-Path -LiteralPath (Join-Path $backup '{{Esc(exeName)}}'))
                Write-Log "Rollback backup exit=$($backupCopy.ExitCode) ready=$backupReady"
            } catch { Write-Log "Rollback backup failed: $_" }

            if (-not $backupReady) {
                Write-Log "Update aborted before installation because a complete rollback backup could not be created."
                try { Start-Process -FilePath '{{Esc(currentExe)}}' -WorkingDirectory $dst -ErrorAction SilentlyContinue } catch {}
                exit 20
            }

            function Restore-PreviousRelease {
                Write-Log "Restoring complete previous release from '$backup'."
                try {
                    $restore = Start-Process -FilePath 'robocopy.exe' -ArgumentList @($backup, $dst, '/E', '/COPY:DAT', '/R:3', '/W:1', '/NFL', '/NDL', '/NJH', '/NJS', '/XJ', '/IS') -Wait -PassThru -WindowStyle Hidden
                    Write-Log "Rollback restore exit code: $($restore.ExitCode)"
                    return $restore.ExitCode -lt 8
                } catch {
                    Write-Log "Rollback restore failed: $_"
                    return $false
                }
            }

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
            $maxAttempts = 8
            $copied = $false
            for ($i = 1; $i -le $maxAttempts; $i++) {
                try {
                    Write-Log "Copy attempt $($i): '$src' -> '$dst'"
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
                Restore-PreviousRelease | Out-Null
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
                        Restore-PreviousRelease | Out-Null
                        $copied = $false
                    }
                } catch {
                    Write-Log "Version verification error: $_"
                    Restore-PreviousRelease | Out-Null
                    $copied = $false
                }
            }

            if (-not $copied) {
                Write-Log "Update apply FAILED. Restoring the complete previous release before relaunch."
                Restore-PreviousRelease | Out-Null
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
                    Write-Log "Launch attempt $($i): '$exe'"
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
                try {
                    Restore-PreviousRelease | Out-Null
                    Unblock-File -LiteralPath $exePath -ErrorAction SilentlyContinue
                    Start-Process -FilePath $exePath -WorkingDirectory $dst -ErrorAction Stop
                    Write-Log "Full rollback succeeded and the previous version was relaunched."
                } catch { Write-Log "Rollback launch failed: $_" }
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

    private sealed class LegacyProgressAdapter(IProgress<double> inner) : IProgress<UpdateProgressInfo>
    {
        public void Report(UpdateProgressInfo value) => inner.Report(value.Progress);
    }

    internal static IReadOnlyDictionary<string, bool> RunSafetyChecks()
    {
        Dictionary<string, bool> results = new(StringComparer.OrdinalIgnoreCase);
        var release = new GitHubRelease
        {
            Assets =
            [
                new GitHubReleaseAsset { Name = "source.zip", Size = 100, BrowserDownloadUrl = "https://example.invalid/source" },
                new GitHubReleaseAsset { Name = "Roche-Scoreboard-v9.9.9-SHA256.txt", Size = 100, BrowserDownloadUrl = "https://example.invalid/hash" },
                new GitHubReleaseAsset { Name = "Roche-Scoreboard-v9.9.9-Windows-x64.zip", Size = 100, BrowserDownloadUrl = "https://example.invalid/app" }
            ]
        };
        results["canonical-windows-package-selected"] =
            FindAsset(release)?.Name == "Roche-Scoreboard-v9.9.9-Windows-x64.zip";
        var aliasOnlyRelease = new GitHubRelease
        {
            Assets =
            [
                new GitHubReleaseAsset { Name = "source.zip", Size = 100, BrowserDownloadUrl = "https://example.invalid/source" },
                new GitHubReleaseAsset { Name = "Roche-Scoreboard-Windows-x64.zip", Size = 100, BrowserDownloadUrl = "https://example.invalid/latest" }
            ]
        };
        results["stable-latest-download-alias-accepted"] =
            FindAsset(aliasOnlyRelease)?.Name == "Roche-Scoreboard-Windows-x64.zip";
        results["version-suffix-parses"] =
            TryParseReleaseVersion("v4.1.2+build.17", out Version? parsed) && parsed == new Version(4, 1, 2);

        string directory = Path.Combine(Path.GetTempPath(), "RocheUpdaterSafety", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            string safeZip = Path.Combine(directory, "safe.zip");
            using (ZipArchive archive = ZipFile.Open(safeZip, ZipArchiveMode.Create))
            {
                using StreamWriter writer = new(archive.CreateEntry("Roche Scoreboard.exe").Open());
                writer.Write("MZ-safe-test");
            }
            try
            {
                ValidateArchiveBeforeExtraction(safeZip);
                results["safe-archive-accepted"] = true;
            }
            catch { results["safe-archive-accepted"] = false; }

            string unsafeZip = Path.Combine(directory, "unsafe.zip");
            using (ZipArchive archive = ZipFile.Open(unsafeZip, ZipArchiveMode.Create))
            {
                using StreamWriter writer = new(archive.CreateEntry("../outside.txt").Open());
                writer.Write("must never extract");
            }
            try
            {
                ValidateArchiveBeforeExtraction(unsafeZip);
                results["path-traversal-rejected"] = false;
            }
            catch (InvalidDataException) { results["path-traversal-rejected"] = true; }

            string script = BuildUpdaterScript(4242, "Roche Scoreboard.exe",
                @"C:\Apps\Roche Scoreboard.exe", @"C:\Apps", @"C:\Stage", @"C:\Temp", @"C:\Logs\update.log", "9.9.9");
            results["stops-only-source-pid"] =
                script.Contains("Get-Process -Id 4242", StringComparison.Ordinal)
                && !script.Contains("Get-Process -Name", StringComparison.Ordinal);
            results["backs-up-and-restores-complete-release"] =
                script.Contains("'previous-release'", StringComparison.Ordinal)
                && script.Contains("function Restore-PreviousRelease", StringComparison.Ordinal)
                && script.Contains("@($backup, $dst, '/E'", StringComparison.Ordinal);
            results["script-avoids-ambiguous-variable-colons"] =
                !script.Contains("$i:", StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }

        return results;
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

    private static async Task DownloadFileWithRetryAsync(
        string assetUrl,
        string destinationPath,
        GitHubReleaseAsset asset,
        IProgress<UpdateProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;
        for (int attempt = 1; attempt <= DownloadAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new UpdateProgressInfo(UpdateProgressStage.Downloading, 0, 0, asset.Size,
                attempt, DownloadAttempts, attempt == 1 ? "Downloading the Windows release…" : $"Retrying download ({attempt}/{DownloadAttempts})…"));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, assetUrl);
                using HttpResponseMessage response = await s_http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? asset.Size;
                long bytesRead = 0;
                long lastReportedBytes = -1;
                long lastReportTimestamp = 0;
                double ticksPerSecond = Stopwatch.Frequency;

                await using Stream downloadStream = await response.Content
                    .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var fileStream = new FileStream(
                    destinationPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    bufferSize: 81920, useAsync: true);

                byte[] buffer = new byte[81920];
                while (true)
                {
                    int read = await downloadStream.ReadAsync(buffer, cancellationToken)
                        .AsTask()
                        .WaitAsync(DownloadIdleTimeout, cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0) break;

                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    bytesRead += read;

                    // At most ten progress dispatches per second. Updating for
                    // every 80 KB block generated thousands of WPF callbacks on
                    // fast connections and made the dialog feel less responsive.
                    long now = Stopwatch.GetTimestamp();
                    bool reportDue = lastReportTimestamp == 0 ||
                                     (now - lastReportTimestamp) / ticksPerSecond >= 0.1;
                    if (totalBytes > 0 && reportDue && bytesRead != lastReportedBytes)
                    {
                        double fraction = Math.Clamp((double)bytesRead / totalBytes, 0, 1);
                        progress?.Report(new UpdateProgressInfo(UpdateProgressStage.Downloading, fraction, bytesRead,
                            totalBytes, attempt, DownloadAttempts, "Downloading the Windows release…"));
                        lastReportedBytes = bytesRead;
                        lastReportTimestamp = now;
                    }
                }

                await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                ValidateDownload(destinationPath, bytesRead, totalBytes, asset);
                progress?.Report(new UpdateProgressInfo(UpdateProgressStage.Downloading, 1, bytesRead,
                    totalBytes > 0 ? totalBytes : asset.Size, attempt, DownloadAttempts, "Download complete."));
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TimeoutException or TaskCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);

                lastFailure = ex;
                WriteDiagnostic($"Download attempt {attempt}/{DownloadAttempts} failed: {ex.Message}");
                try { if (File.Exists(destinationPath)) File.Delete(destinationPath); } catch { }
                if (attempt < DownloadAttempts)
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken).ConfigureAwait(false);
            }
        }

        throw new IOException(
            $"The update download failed after {DownloadAttempts} attempts.", lastFailure);
    }

    private static void EnsureInstallDirectoryWritable(string appDirectory)
    {
        string probe = Path.Combine(appDirectory, $".roche-update-write-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probe, "update write check");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException(
                "Roche Scoreboard cannot update this folder. Move the extracted app folder to Documents or another writable location, then retry.", ex);
        }
        finally
        {
            try { if (File.Exists(probe)) File.Delete(probe); } catch { }
        }
    }

    private static void EnsureSufficientDiskSpace(string path, long requiredBytes)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(root)) return;
            var drive = new DriveInfo(root);
            if (drive.IsReady && drive.AvailableFreeSpace < requiredBytes)
            {
                long requiredMb = (long)Math.Ceiling(requiredBytes / 1024d / 1024d);
                throw new IOException($"At least {requiredMb:N0} MB of free disk space is required to install this update safely.");
            }
        }
        catch (ArgumentException) { /* UNC/unusual roots: continue with normal I/O checks */ }
        catch (NotSupportedException) { }
    }

    private static void ValidateArchiveBeforeExtraction(string zipPath)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count == 0)
            throw new InvalidDataException("The downloaded release archive is empty.");

        long expandedBytes = 0;
        string extractionRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(zipPath)!, "extracted"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry.Length < 0 || entry.Length > MaximumExpandedPackageBytes - expandedBytes)
                throw new InvalidDataException("The downloaded release expands beyond the safe package limit.");
            expandedBytes += entry.Length;

            string target = Path.GetFullPath(Path.Combine(extractionRoot, entry.FullName));
            if (!target.StartsWith(extractionRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded release contains an unsafe archive path.");
        }
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

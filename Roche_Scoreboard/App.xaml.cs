using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Application = System.Windows.Application;

namespace Roche_Scoreboard;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Shared update service so both startup and shutdown can access the same state.
    /// </summary>
    internal AutoUpdateService UpdateService { get; } = new();

    /// <summary>
    /// Shared theme service for runtime theme and density switching
    /// </summary>
    internal ThemeService ThemeService { get; } = new();

    /// <summary>
    /// Loaded UX preferences including theme and density
    /// </summary>
    internal UxPreferencesData UxPreferences { get; private set; } = new();

    /// <summary>
    /// The background update check kicked off in <see cref="OnStartup"/>.
    /// MainWindow awaits this before deciding whether to show the update
    /// prompt, otherwise the prompt is evaluated before the network call
    /// returns and never appears on first launch.
    /// </summary>
    internal Task UpdateCheckTask { get; private set; } = Task.CompletedTask;

    /// <summary>True only for the isolated command-line regression surfaces.</summary>
    internal bool IsSelfTestMode { get; private set; }

    /// <summary>
    /// Path to the crash log file. Each unhandled exception is appended with a
    /// timestamp so the next crash is diagnosable even if no WER dump is produced.
    /// </summary>
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roche_Scoreboard",
        "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        IsSelfTestMode = e.Args.Any(arg =>
            arg.StartsWith("--gallery-self-test-output=", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--startup-self-test-output=", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--preferences-self-test-output=", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--afl-presentation-snapshots=", StringComparison.OrdinalIgnoreCase));

        // Install global exception handlers up-front so any error from here on
        // is logged (and, where possible, non-fatal ones are swallowed) rather
        // than tearing the process down silently mid-game.
        InstallGlobalExceptionHandlers();

        // The AFL presentation gallery is a fully isolated visual QA surface.
        // Open it before production services so update, video, saved-state and
        // operator-preference initialisation cannot affect the regression view.
        string? aflSnapshotDirectory = GetArgumentValue(e.Args, "--afl-presentation-snapshots=");
        if (e.Args.Contains("--afl-presentation-gallery", StringComparer.OrdinalIgnoreCase) ||
            aflSnapshotDirectory is not null)
        {
            AflPresentationGalleryWindow presentationGallery = new();
            MainWindow = presentationGallery;
            presentationGallery.Show();
            if (aflSnapshotDirectory is not null)
            {
                presentationGallery.Dispatcher.InvokeAsync(async () =>
                {
                    try { await presentationGallery.CaptureSnapshotsAsync(aflSnapshotDirectory); }
                    finally
                    {
                        presentationGallery.Close();
                        Shutdown();
                    }
                }, DispatcherPriority.ApplicationIdle);
            }
            return;
        }

        // Persist the live game state when Windows is logging off / shutting
        // down / restarting. Without this hook the app is killed by the
        // session manager with no chance to flush the running clock.
        SessionEnding += (_, _) => TrySaveActiveMainWindowState();

        // Enable high DPI support for modern displays
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;

        // Load UX preferences early so theme/density can be initialized
        UxPreferences = Services.UxPreferences.Load();

        // Initialize theme service with persisted preferences
        ThemeService.Initialize(UxPreferences.Theme, UxPreferences.Density);

        // Validate LibVLC native runtime up-front. A missing or broken VLC
        // install should surface as a clear startup diagnostic instead of
        // becoming an unexplained black-screen state during the first goal
        // video. Failures are logged (via VideoDiagnostics) and shown to the
        // operator, but do NOT block app launch - the scoreboard itself is
        // still usable without video playback.
        try
        {
            VlcRuntime.ValidateOrThrow();
        }
        catch (Exception vlcEx)
        {
            VideoDiagnostics.Error("Startup",
                "LibVLC validation failed - video playback will be unavailable.", vlcEx);
            LogException("VlcRuntime.ValidateOrThrow", vlcEx);
            try
            {
                System.Windows.MessageBox.Show(
                    "LibVLC native libraries failed to initialise. Video playback features will be disabled for this session.\n\n"
                    + vlcEx.Message,
                    "Roche Scoreboard - Video subsystem unavailable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch { /* headless / no shell - already logged */ }
        }

        // Kick off the update check in the background. We do NOT await it
        // here — OnStartup must return promptly so the dispatcher can show
        // MainWindow. The task is parked on UpdateCheckTask so MainWindow's
        // deferred-alerts code can await it before deciding whether to show
        // the update prompt.
        UpdateCheckTask = Task.Run(async () =>
        {
            try { await UpdateService.CheckForUpdateAsync(); }
            catch { /* Never let an update check failure prevent the app from starting. */ }
        });

        // Determine which window to show based on command-line arguments
        bool isDesignGalleryMode = e.Args.Contains("--design-gallery", StringComparer.OrdinalIgnoreCase);
        string? gallerySelfTestPath = GetArgumentValue(e.Args, "--gallery-self-test-output=");
        string? startupSelfTestPath = GetArgumentValue(e.Args, "--startup-self-test-output=");
        string? preferencesSelfTestPath = GetArgumentValue(e.Args, "--preferences-self-test-output=");

        if (gallerySelfTestPath is not null)
        {
            RunGallerySelfTest(gallerySelfTestPath);
            return;
        }

        if (startupSelfTestPath is not null)
        {
            RunStartupSelfTest(startupSelfTestPath);
            return;
        }

        if (preferencesSelfTestPath is not null)
        {
            RunPreferencesSelfTest(preferencesSelfTestPath);
            return;
        }

        if (isDesignGalleryMode)
        {
            // Design gallery mode: show gallery only, do not load production screens
            Window galleryWindow = new DesignSystemGalleryWindow();
            galleryWindow.Show();
            MainWindow = galleryWindow;
        }
        else
        {
            // Normal production mode: show MainWindow
            Window mainWindow = new MainWindow();
            mainWindow.Show();
            MainWindow = mainWindow;
        }
    }

    /// <summary>
    /// Wires up Dispatcher, AppDomain, and TaskScheduler exception hooks.
    /// Everything is logged to <see cref="CrashLogPath"/>; UI-thread and
    /// unobserved-task exceptions are marked handled so the match continues.
    /// On a terminating crash the live game state is flushed to disk before
    /// the process dies so a restart can resume from the exact clock value.
    /// </summary>
    private void InstallGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            LogException("DispatcherUnhandledException", args.Exception);
            // Best-effort save before swallowing; cheap and safe.
            TrySaveActiveMainWindowState();
            args.Handled = true; // keep the app alive — match state is preserved
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                LogException("AppDomain.UnhandledException (Terminating=" + args.IsTerminating + ")", ex);

            // On a terminating exception this is our last chance to flush
            // the live clock; otherwise the saved state lags behind reality
            // by however long since the last MatchChanged event.
            if (args.IsTerminating)
                TrySaveActiveMainWindowState();
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogException("UnobservedTaskException", args.Exception);
            args.SetObserved(); // prevent process termination
        };
    }

    /// <summary>
    /// Best-effort flush of the active <see cref="MainWindow"/>'s live game
    /// state to disk. Marshals to the UI thread if necessary because both the
    /// MatchManager stopwatch and the WPF dispatcher demand it. Swallows
    /// every exception so a save attempt during teardown can never block or
    /// re-enter the crash handler.
    /// </summary>
    private void TrySaveActiveMainWindowState()
    {
        try
        {
            if (MainWindow is not MainWindow main) return;

            if (main.Dispatcher.CheckAccess())
            {
                main.TrySaveGameStateForShutdown();
            }
            else
            {
                // Synchronous Invoke so the save completes before this
                // shutdown-path method returns and the process is allowed to
                // terminate.
                main.Dispatcher.Invoke(main.TrySaveGameStateForShutdown);
            }
        }
        catch
        {
            // Best-effort during shutdown / crash; do not re-throw.
        }
    }

    private static void LogException(string source, Exception ex)
    {
        try
        {
            string dir = Path.GetDirectoryName(CrashLogPath)!;
            Directory.CreateDirectory(dir);
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{ex}\n\n";
            File.AppendAllText(CrashLogPath, entry);
        }
        catch
        {
            // If the logger itself fails there's nothing useful we can do.
        }
    }

    /// <summary>
    /// Shows the update prompt dialog. Returns true if the user chose to update
    /// and the update was applied (app should shut down).
    /// </summary>
    internal bool PromptForUpdate()
    {
        if (!UpdateService.UpdateAvailable)
            return false;

        var prompt = new UpdatePromptWindow(UpdateService)
        {
            Owner = MainWindow
        };

        bool? result = prompt.ShowDialog();

        if (prompt.UpdateApplied)
        {
            Shutdown();
            return true;
        }

        return false;
    }

    private static string? GetArgumentValue(string[] args, string prefix)
        => args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    private void RunGallerySelfTest(string outputPath)
    {
        string resolvedOutputPath = ResolveSelfTestOutputPath(outputPath, "gallery-self-test.json");
        DesignSystemGalleryWindow galleryWindow = new();
        MainWindow = galleryWindow;
        galleryWindow.Show();
        galleryWindow.UpdateLayout();

        galleryWindow.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                object result = galleryWindow.RunSelfTest();
                string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(resolvedOutputPath, json);
                TryWriteDiagnosticMirror("gallery-self-test.json", json);
            }
            catch (Exception ex)
            {
                string errorJson = JsonSerializer.Serialize(new
                {
                    error = ex.ToString(),
                }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(resolvedOutputPath, errorJson);
                TryWriteDiagnosticMirror("gallery-self-test.json", errorJson);
            }
            finally
            {
                galleryWindow.Close();
                Shutdown();
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private void RunStartupSelfTest(string outputPath)
    {
        string resolvedOutputPath = ResolveSelfTestOutputPath(outputPath, "startup-self-test.json");
        MainWindow mainWindow = new();
        Window startupWindow = mainWindow;
        startupWindow.Show();
        MainWindow = startupWindow;
        startupWindow.UpdateLayout();

        startupWindow.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                object result = new
                {
                    windowCount = Windows.Count,
                    mainWindowType = MainWindow?.GetType().FullName,
                    galleryVisible = Windows.OfType<DesignSystemGalleryWindow>().Any(),
                    startupException = false,
                    sportSelectionVisible = mainWindow.SportSelectionPanel.Visibility == Visibility.Visible,
                    mainContentVisible = mainWindow.MainContent.Visibility == Visibility.Visible,
                    setupVisible = mainWindow.SetupWizardPanel.Visibility == Visibility.Visible,
                    cricketSetupVisible = mainWindow.CricketSetupWizardPanel.Visibility == Visibility.Visible,
                    cricketControlVisible = mainWindow.CricketControlPanelView.Visibility == Visibility.Visible,
                    globalSettingsVisible = mainWindow.GlobalSettingsPanel.Visibility == Visibility.Visible,
                };

                string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(resolvedOutputPath, json);
                TryWriteDiagnosticMirror("startup-self-test.json", json);
            }
            catch (Exception ex)
            {
                string errorJson = JsonSerializer.Serialize(new
                {
                    error = ex.ToString(),
                }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(resolvedOutputPath, errorJson);
                TryWriteDiagnosticMirror("startup-self-test.json", errorJson);
            }
            finally
            {
                startupWindow.Close();
                Shutdown();
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private void RunPreferencesSelfTest(string outputPath)
    {
        string resolvedOutputPath = ResolveSelfTestOutputPath(outputPath, "preferences-self-test.json");
        string preferencesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Roche_Scoreboard",
            "ux_preferences.json");

        Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);

        string? originalContent = File.Exists(preferencesPath)
            ? File.ReadAllText(preferencesPath)
            : null;

        var cases = new[]
        {
            new { Name = "missing-properties", Json = "{ \"startupMode\": \"resumeIfAvailable\", \"lastMode\": \"training\", \"lastSettingsCategory\": \"audio\", \"lastMainTabIndex\": 7, \"enableQuickStartupHints\": false }" },
            new { Name = "valid-dark-compact", Json = "{ \"theme\": \"dark\", \"density\": \"compact\", \"startupMode\": \"lastMode\", \"lastMode\": \"globalSettings\", \"lastSettingsCategory\": \"general\", \"lastMainTabIndex\": 3, \"enableQuickStartupHints\": true }" },
            new { Name = "invalid-theme-valid-density", Json = "{ \"theme\": \"invalidTheme\", \"density\": \"compact\", \"startupMode\": \"sportSelection\", \"lastMode\": \"afl\", \"lastSettingsCategory\": \"display\", \"lastMainTabIndex\": 2, \"enableQuickStartupHints\": false }" },
            new { Name = "valid-theme-invalid-density", Json = "{ \"theme\": \"dark\", \"density\": \"invalidDensity\", \"startupMode\": \"resumeIfAvailable\", \"lastMode\": \"training\", \"lastSettingsCategory\": \"clock\", \"lastMainTabIndex\": 5, \"enableQuickStartupHints\": true }" },
            new { Name = "both-invalid", Json = "{ \"theme\": \"badTheme\", \"density\": \"badDensity\", \"startupMode\": \"lastMode\", \"lastMode\": \"globalSettings\", \"lastSettingsCategory\": \"media\", \"lastMainTabIndex\": 9, \"enableQuickStartupHints\": false }" },
            new { Name = "wrong-token-types", Json = "{ \"theme\": 12, \"density\": true, \"startupMode\": \"resumeIfAvailable\", \"lastMode\": \"training\", \"lastSettingsCategory\": \"network\", \"lastMainTabIndex\": 4, \"enableQuickStartupHints\": true }" },
            new { Name = "corrupt-json", Json = "{ \"theme\": \"dark\", \"density\": " },
        };

        var results = new List<object>();

        try
        {
            foreach (var @case in cases)
            {
                File.WriteAllText(preferencesPath, @case.Json);
                bool exceptionOccurred = false;
                string exceptionMessage = string.Empty;
                UxPreferencesData loaded;

                try
                {
                    loaded = Services.UxPreferences.Load();
                    Services.UxPreferences.Save(loaded);
                }
                catch (Exception ex)
                {
                    exceptionOccurred = true;
                    exceptionMessage = ex.ToString();
                    loaded = new UxPreferencesData();
                }

                string writtenBack = File.Exists(preferencesPath)
                    ? File.ReadAllText(preferencesPath)
                    : string.Empty;

                results.Add(new
                {
                    @case.Name,
                    loadedTheme = loaded.Theme.ToString(),
                    loadedDensity = loaded.Density.ToString(),
                    loadedStartupMode = loaded.StartupMode.ToString(),
                    loadedLastMode = loaded.LastMode.ToString(),
                    loadedLastSettingsCategory = loaded.LastSettingsCategory,
                    loadedLastMainTabIndex = loaded.LastMainTabIndex,
                    loadedEnableQuickStartupHints = loaded.EnableQuickStartupHints,
                    exceptionOccurred,
                    exceptionMessage,
                    writtenBack,
                });
            }
        }
        finally
        {
            if (originalContent is null)
            {
                if (File.Exists(preferencesPath))
                    File.Delete(preferencesPath);
            }
            else
            {
                File.WriteAllText(preferencesPath, originalContent);
            }
        }

        string json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(resolvedOutputPath, json);
        TryWriteDiagnosticMirror("preferences-self-test.json", json);
        Shutdown();
    }

    private static void TryWriteDiagnosticMirror(string fileName, string content)
    {
        try
        {
            string directory = Path.GetDirectoryName(CrashLogPath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, fileName), content);
        }
        catch
        {
            // The caller's requested output is authoritative. A locked or
            // restricted diagnostic mirror must never turn a passing self-test
            // into a failure or interfere with production startup.
        }
    }

    private static string ResolveSelfTestOutputPath(string outputPath, string fallbackFileName)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            string directory = Path.GetDirectoryName(outputPath) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            return outputPath;
        }

        string fallbackDirectory = Path.Combine(Path.GetTempPath(), "RocheScoreboardTask1");
        Directory.CreateDirectory(fallbackDirectory);
        return Path.Combine(fallbackDirectory, fallbackFileName);
    }
}

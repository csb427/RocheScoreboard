using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private bool _updatePromptOpen;
    private static int _diagnosticExceptionCount;
    private int _selfTestDiagnosticBaseline;
    internal static int DiagnosticExceptionCount => System.Threading.Volatile.Read(ref _diagnosticExceptionCount);

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
    /// Path to bounded, append-only diagnostics. Fatal evidence has a separate
    /// bounded file so an already-full recoverable-error log cannot hide it.
    /// </summary>
    private static string CrashLogPath => SelfTestStorage.IsEnabled
        ? SelfTestStorage.PathFor("diagnostics", "crash.log")
        : Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roche_Scoreboard",
        "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        OperatorFeedback.Install();

        IsSelfTestMode = e.Args.Any(arg =>
            DispatcherRecoveryProbeQa.IsProbeArgument(arg) ||
            arg.StartsWith("--gallery-self-test-output=", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--startup-self-test-output=", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--preferences-self-test-output=", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--afl-reliability-stress-output=", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--operator-self-test-output=", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--control-centre-self-test-output=", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--apple-interaction-self-test-output=", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--operator-controls-self-test-output=", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--operator-design-preview", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--cricket-motion-self-test-output=", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--cricket-self-test-output=", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--cricket-workflow-self-test-output=", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--vlc-self-test-output=", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--prompt-snapshots=", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--afl-presentation-snapshots=", StringComparison.OrdinalIgnoreCase));

        SelfTestStorage.Initialize(IsSelfTestMode);
        _selfTestDiagnosticBaseline = DiagnosticExceptionCount;

        // Install global exception handlers up-front so any error from here on
        // is logged (and, where possible, non-fatal ones are swallowed) rather
        // than tearing the process down silently mid-game.
        InstallGlobalExceptionHandlers();

        // Exercise the actual dispatcher policy in a separate isolated QA child,
        // before preferences, production services or an operator window exist.
        if (DispatcherRecoveryProbeQa.TryRun(this, e.Args)) return;

        // The controls-only route deliberately runs before operator preference
        // loading and production-window creation. SelfTestStorage is already
        // latched, so its fixtures can persist only below the isolated QA root.
        string? operatorControlsDirectory = GetArgumentValue(e.Args, "--operator-controls-self-test-output=");
        if (operatorControlsDirectory is not null)
        {
            int diagnosticBaseline = _selfTestDiagnosticBaseline;
            try
            {
                ThemeService.Initialize(UxPreferences.Theme, UxPreferences.Density);
                OperatorAccentPreferences.ApplyAccentResources();
                RunOperatorControlsSelfTest(operatorControlsDirectory, diagnosticBaseline);
            }
            catch (Exception error)
            {
                WriteOperatorControlsStartupFailure(operatorControlsDirectory, error);
            }
            return;
        }

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

        // A process-local QA comparison only. Every normal launch retains Default;
        // no persisted setting, driver, registry or session configuration is changed.
        var requestedRenderMode = SelfTestStorage.IsEnabled
            && e.Args.Contains("--operator-design-preview", StringComparer.OrdinalIgnoreCase)
            && e.Args.Contains("--operator-preview-diagnostics", StringComparer.OrdinalIgnoreCase)
            && e.Args.Contains("--operator-preview-software-only", StringComparer.OrdinalIgnoreCase)
                ? System.Windows.Interop.RenderMode.SoftwareOnly
                : System.Windows.Interop.RenderMode.Default;
        System.Windows.Media.RenderOptions.ProcessRenderMode = requestedRenderMode;

        // Load UX preferences early so theme/density can be initialized
        UxPreferences = Services.UxPreferences.Load();

        // Initialize theme service with persisted preferences
        ThemeService.Initialize(UxPreferences.Theme, UxPreferences.Density);
        OperatorAccentPreferences.ApplyAccentResources();

        // Validate LibVLC native runtime up-front. A missing or broken VLC
        // install should surface as a clear startup diagnostic instead of
        // becoming an unexplained black-screen state during the first goal
        // video. Failures are logged (via VideoDiagnostics) and shown to the
        // operator, but do NOT block app launch - the scoreboard itself is
        // still usable without video playback.
        try
        {
            if (!IsSelfTestMode)
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
        UpdateCheckTask = IsSelfTestMode
            ? Task.CompletedTask
            : Task.Run(async () =>
            {
                AutoUpdateService.CleanupStaleStagingDirectories();
                try { await UpdateService.CheckForUpdateAsync(); }
                catch { /* Never let an update check failure prevent the app from starting. */ }
            });

        // Determine which window to show based on command-line arguments
        bool isDesignGalleryMode = e.Args.Contains("--design-gallery", StringComparer.OrdinalIgnoreCase);
        string? gallerySelfTestPath = GetArgumentValue(e.Args, "--gallery-self-test-output=");
        string? startupSelfTestPath = GetArgumentValue(e.Args, "--startup-self-test-output=");
        string? preferencesSelfTestPath = GetArgumentValue(e.Args, "--preferences-self-test-output=");
        string? aflReliabilityStressPath = GetArgumentValue(e.Args, "--afl-reliability-stress-output=");
        string? cricketSelfTestPath = GetArgumentValue(e.Args, "--cricket-self-test-output=");
        string? operatorSelfTestPath = GetArgumentValue(e.Args, "--operator-self-test-output=");
        string? controlCentreSelfTestDirectory = GetArgumentValue(e.Args, "--control-centre-self-test-output=");
        string? appleInteractionDirectory = GetArgumentValue(e.Args, "--apple-interaction-self-test-output=");
        string? cricketMotionPath = GetArgumentValue(e.Args, "--cricket-motion-self-test-output=");
        string? cricketWorkflowPath = GetArgumentValue(e.Args, "--cricket-workflow-self-test-output=");
        string? vlcSelfTestPath = GetArgumentValue(e.Args, "--vlc-self-test-output=");
        string? promptSnapshotDirectory = GetArgumentValue(e.Args, "--prompt-snapshots=");

        if(e.Args.Contains("--operator-design-preview",StringComparer.OrdinalIgnoreCase))
        {
            MainWindow preview=new(); MainWindow=preview;
            if (e.Args.Contains("--operator-preview-diagnostics", StringComparer.OrdinalIgnoreCase))
                OperatorPreviewDiagnosticsQa.Attach(preview, requestedRenderMode);
            preview.Show(); return;
        }

        if (controlCentreSelfTestDirectory is not null) { RunControlCentreSelfTest(controlCentreSelfTestDirectory); return; }
        if (appleInteractionDirectory is not null) { RunControlCentreSelfTest(appleInteractionDirectory,true); return; }
        if (operatorSelfTestPath is not null) { RunCricketSelfTest(operatorSelfTestPath, operatorOnly: true); return; }
        if (cricketMotionPath is not null) { RunCricketSelfTest(cricketMotionPath, motionOnly: true); return; }
        if (cricketWorkflowPath is not null)
        {
            RunCricketSelfTest(cricketWorkflowPath, workflowsOnly: true);
            return;
        }
        if (cricketSelfTestPath is not null)
        {
            RunCricketSelfTest(cricketSelfTestPath);
            return;
        }

        if (promptSnapshotDirectory is not null)
        {
            RunPromptSnapshots(promptSnapshotDirectory);
            return;
        }

        if (vlcSelfTestPath is not null)
        {
            RunVlcSelfTest(vlcSelfTestPath);
            return;
        }

        if (aflReliabilityStressPath is not null)
        {
            RunAflReliabilityStressTest(aflReliabilityStressPath);
            return;
        }

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
    /// Resource exhaustion must remain unhandled: retrying a failed WPF render
    /// channel causes an exception/save storm rather than recovering the match.
    /// The first terminal notification owns one best-effort log and state save.
    /// </summary>
    private void InstallGlobalExceptionHandlers()
    {
        var policy = new UnhandledExceptionPolicy(LogException, TrySaveActiveMainWindowState);
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = policy.Handle("DispatcherUnhandledException", args.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                policy.Handle("AppDomain.UnhandledException", ex,
                    terminating: args.IsTerminating, saveState: args.IsTerminating);
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
    /// ordinary failures and limits dispatcher queue waiting during teardown.
    /// Resource failures propagate to the global policy so it cannot resume rendering.
    /// Once started, the existing save itself remains a best-effort operation.
    /// </summary>
    private void TrySaveActiveMainWindowState()
    {
        try
        {
            if (Dispatcher.CheckAccess())
            {
                SaveActiveMainWindowStateOnDispatcher();
            }
            else
            {
                // Do not wait indefinitely for an unresponsive render/UI thread.
                // Even reading Application.MainWindow belongs on its dispatcher.
                Dispatcher.Invoke(SaveActiveMainWindowStateOnDispatcher, DispatcherPriority.Send,
                    System.Threading.CancellationToken.None, TimeSpan.FromSeconds(1));
            }
        }
        catch (Exception error) when (!UnhandledExceptionPolicy.IsFatalResourceFailure(error))
        {
            // Ordinary shutdown / crash-save failures remain best-effort.
        }
    }

    private void SaveActiveMainWindowStateOnDispatcher()
    {
        if (MainWindow is MainWindow main)
            main.TrySaveGameStateForShutdown();
    }

    private static void LogException(string source, Exception ex, bool fatal = false)
    {
        System.Threading.Interlocked.Increment(ref _diagnosticExceptionCount);
        try
        {
            BoundedExceptionLog.TryAppend(fatal ? CrashLogPath + ".fatal" : CrashLogPath, source, ex);
        }
        catch (Exception error) when (!UnhandledExceptionPolicy.IsFatalResourceFailure(error))
        {
            // Ordinary diagnostic failures are best-effort; fatal ones reach the policy.
        }
    }

    /// <summary>
    /// Shows the update prompt dialog. Returns true if the user chose to update
    /// and the update was applied (app should shut down).
    /// </summary>
    internal bool PromptForUpdate()
    {
        if (!UpdateService.UpdateAvailable || _updatePromptOpen)
            return false;
        _updatePromptOpen = true;
        try
        {
            var prompt = new UpdatePromptWindow(UpdateService);
            if (MainWindow?.IsVisible == true)
                prompt.Owner = MainWindow;
            prompt.ShowDialog();

            if (!prompt.UpdateApplied) return false;
            Shutdown();
            return true;
        }
        catch (Exception ex)
        {
            LogException("PromptForUpdate", ex);
            return false;
        }
        finally
        {
            _updatePromptOpen = false;
        }
    }

    private void RunPromptSnapshots(string outputDirectory)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        string resolvedDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(Path.GetTempPath(), "RochePromptSnapshots")
            : outputDirectory);

        Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                Directory.CreateDirectory(resolvedDirectory);
                var recovery = new GameState
                {
                    Sport = "afl",
                    SavedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-7),
                    HomePrimaryColor = "#FF3338",
                    AwayPrimaryColor = "#FACC0D",
                    MatchState = new Roche_Scoreboard.Models.SerializableState
                    {
                        HomeName = "Xavier College",
                        AwayName = "Carey Grammar",
                        HomeGoals = 8,
                        HomeBehinds = 13,
                        AwayGoals = 7,
                        AwayBehinds = 9,
                        Quarter = 3,
                        ElapsedInQuarterTicks = TimeSpan.FromMinutes(16).Add(TimeSpan.FromSeconds(18)).Ticks
                    }
                };
                var resume = new ResumeGameDialog(recovery);
                MainWindow = resume;
                resume.Show();
                await Task.Delay(280);
                CaptureWindowPng(resume, Path.Combine(resolvedDirectory, "continue-match.png"));
                resume.Close();

                var release = new GitHubRelease
                {
                    TagName = "v4.1.3",
                    Body = "## New\n- Faster recovery and clearer operator prompts\n- Stage-aware update progress\n\n## Fixed\n- Safer cancellation and complete-release rollback",
                    HtmlUrl = AutoUpdateService.GetReleasePageUrl(),
                    Assets =
                    [
                        new GitHubReleaseAsset
                        {
                            Name = "Roche-Scoreboard-v4.1.3-Windows-x64.zip",
                            Size = 222_793_218,
                            Digest = "sha256:preview"
                        }
                    ]
                };
                var update = new UpdatePromptWindow(UpdateService, release);
                MainWindow = update;
                update.Show();
                await Task.Delay(280);
                CaptureWindowPng(update, Path.Combine(resolvedDirectory, "auto-update.png"));
                update.Close();

                File.WriteAllText(Path.Combine(resolvedDirectory, "prompt-snapshots.json"),
                    JsonSerializer.Serialize(new { success = true, windows = 2 }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory(resolvedDirectory);
                File.WriteAllText(Path.Combine(resolvedDirectory, "prompt-snapshots.json"),
                    JsonSerializer.Serialize(new { success = false, error = ex.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            }
            finally
            {
                Shutdown();
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private static void CaptureWindowPng(Window window, string path)
    {
        window.UpdateLayout();
        int width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
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
                    operatorModeChecks = mainWindow.RunOperatorModeSelfCheckForQa(),
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
        string preferencesPath = Services.UxPreferences.GetStoragePath();

        Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);

        string? originalContent = File.Exists(preferencesPath)
            ? File.ReadAllText(preferencesPath)
            : null;

        var cases = new[]
        {
            new { Name = "missing-properties", Json = "{ \"startupMode\": \"resumeIfAvailable\", \"lastMode\": \"training\", \"lastSettingsCategory\": \"audio\", \"lastMainTabIndex\": 7, \"enableQuickStartupHints\": false }" },
            new { Name = "advanced-mode-preserved", Json = "{ \"guidedOperatorMode\": false, \"lastMainTabIndex\": 2 }" },
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
                    loadedGuidedOperatorMode = loaded.GuidedOperatorMode,
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

    private void RunControlCentreSelfTest(string outputDirectory,bool focusedInteractions=false)
    {
        string directory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(SelfTestStorage.RootPath, "control-centre")
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, focusedInteractions ? "apple-interaction-self-test.json" : "control-centre-self-test.json");
        int diagnosticExceptionCount = _selfTestDiagnosticBaseline;
        MainWindow window;
        try { window = new(); MainWindow = window; window.Show(); }
        catch (Exception error)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new { error = error.ToString() }));
            Shutdown(1);
            return;
        }
        int exitCode = 1;
        window.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var checks = focusedInteractions ? await window.RunAppleInteractionChecksAsync(directory) : await window.RunControlCentreChecksAsync(directory);
                checks["qa-preferences-isolated"] = Services.UxPreferences.GetStoragePath().StartsWith(
                    SelfTestStorage.RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                checks["qa-recovery-isolated"] = GameStateService.RecoveryStatePath.StartsWith(
                    SelfTestStorage.RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                checks["qa-diagnostics-isolated"] = CrashLogPath.StartsWith(
                    SelfTestStorage.RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                checks["qa-no-unexpected-diagnostic-exceptions"] = DiagnosticExceptionCount == diagnosticExceptionCount;
                File.WriteAllText(path, JsonSerializer.Serialize(new
                {
                    failures = checks.Count(check => !check.Value),
                    checks,
                    selfTestStorageRoot = SelfTestStorage.RootPath
                }, new JsonSerializerOptions { WriteIndented = true }));
                exitCode = checks.Values.All(value => value) ? 0 : 1;
            }
            catch (Exception error)
            {
                File.WriteAllText(path, JsonSerializer.Serialize(new { error = error.ToString() }));
                exitCode = 1;
            }
            finally
            {
                window.Close();
                Shutdown(exitCode);
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private void RunOperatorControlsSelfTest(string outputDirectory, int diagnosticBaseline)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        string directory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(SelfTestStorage.RootPath, "operator-controls")
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "operator-controls-self-test.json");
        Dispatcher.InvokeAsync(async () =>
        {
            var stages = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, bool>>();
            int exitCode = 1;
            async Task RunStage(string name, Func<Task<System.Collections.Generic.Dictionary<string, bool>>> run)
            {
                try { stages[name] = await run(); }
                catch (Exception error)
                {
                    stages[name] = new System.Collections.Generic.Dictionary<string, bool> { [name + "-unexpected-exception"] = false };
                    try { File.WriteAllText(Path.Combine(directory, name + "-unexpected-exception.txt"), error.ToString()); }
                    catch { /* Failure remains represented in the staged JSON. */ }
                }
            }

            try
            {
                await RunStage("fatal-exception-and-bounded-logging", () => ExceptionSafetyChecks.RunAsync());
                await RunStage("live-glass-resource-lifetime", () => OperatorGlassSurfaceChecks.RunAsync());
                await RunStage("native-accent-and-settings-controls", () => OperatorControlsSelfTestChecks.RunAsync());
                await RunStage("setup-and-training-controls", () => OperatorSetupTrainingChecks.RunAsync());
                await RunStage("fallback-motion-controls", () => OperatorFallbackMotionChecks.RunAsync());
                await RunStage("shared-surface-controls", () => SharedSurfaceMotionChecks.RunAsync());

                var checks = stages.SelectMany(stage => stage.Value).ToDictionary(item => item.Key, item => item.Value);
                checks["qa-preferences-isolated"] = Services.UxPreferences.GetStoragePath().StartsWith(
                    SelfTestStorage.RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                checks["qa-recovery-isolated"] = GameStateService.RecoveryStatePath.StartsWith(
                    SelfTestStorage.RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                checks["qa-diagnostics-isolated"] = CrashLogPath.StartsWith(
                    SelfTestStorage.RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                checks["qa-no-unexpected-diagnostic-exceptions"] = DiagnosticExceptionCount == diagnosticBaseline;
                int failures = checks.Count(check => !check.Value);
                File.WriteAllText(path, JsonSerializer.Serialize(new
                {
                    status = failures == 0 ? "passed" : "failed",
                    failures,
                    diagnosticExceptions = DiagnosticExceptionCount - diagnosticBaseline,
                    checks,
                    stages,
                    selfTestStorageRoot = SelfTestStorage.RootPath
                }, new JsonSerializerOptions { WriteIndented = true }));
                exitCode = failures == 0 ? 0 : 1;
            }
            catch (Exception error)
            {
                try
                {
                    File.WriteAllText(path, JsonSerializer.Serialize(new
                    {
                        status = "failed",
                        error = error.ToString(),
                        stages,
                        diagnosticExceptions = DiagnosticExceptionCount - diagnosticBaseline,
                        selfTestStorageRoot = SelfTestStorage.RootPath
                    }, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch { /* Exit status still reports failure if the output target is unavailable. */ }
            }
            finally { Shutdown(exitCode); }
        }, DispatcherPriority.ApplicationIdle);
    }

    private void WriteOperatorControlsStartupFailure(string outputDirectory, Exception error)
    {
        try
        {
            string directory = string.IsNullOrWhiteSpace(outputDirectory)
                ? Path.Combine(SelfTestStorage.RootPath, "operator-controls")
                : Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "operator-controls-self-test.json"), JsonSerializer.Serialize(new
            {
                status = "failed",
                error = error.ToString(),
                failures = 1,
                selfTestStorageRoot = SelfTestStorage.RootPath
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { Shutdown(1); }
    }

    private void RunCricketSelfTest(string outputPath, bool workflowsOnly = false, bool operatorOnly = false, bool motionOnly = false)
    {
        string path = ResolveSelfTestOutputPath(outputPath, "cricket-self-test.json");
        MainWindow window;
        try { window=new(); MainWindow=window; window.Show(); }
        catch(Exception error) {File.WriteAllText(path,JsonSerializer.Serialize(new {error=error.ToString()}));Shutdown();return;}
        window.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var checks = operatorOnly || motionOnly ? new System.Collections.Generic.Dictionary<string,bool>() : workflowsOnly ? CricketWorkflowChecks.Run() : CricketReliabilityChecks.Run();
                var native = motionOnly ? await window.RunCricketMotionReviewChecksAsync(Path.GetDirectoryName(path)!) : operatorOnly ? await window.RunOperatorInteractionChecksAsync(Path.GetDirectoryName(path)!) : workflowsOnly
                    ? await window.RunCricketTargetedWorkflowChecksAsync(Path.GetDirectoryName(path)!)
                    : await window.RunCricketLifecycleChecksAsync(Path.GetDirectoryName(path)!);
                foreach (var check in native)
                    checks[check.Key] = check.Value;
                File.WriteAllText(path, JsonSerializer.Serialize(new
                {
                    failures = checks.Count(check => !check.Value),
                    checks
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                File.WriteAllText(path, JsonSerializer.Serialize(new { error = ex.ToString() }));
            }
            finally
            {
                window.Close();
                Shutdown();
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private void RunAflReliabilityStressTest(string outputPath)
    {
        string resolvedOutputPath = ResolveSelfTestOutputPath(outputPath, "afl-reliability-stress.json");
        try
        {
            object result = AflReliabilityStressSuite.Run();
            string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(resolvedOutputPath, json);
        }
        catch (Exception ex)
        {
            string json = JsonSerializer.Serialize(new { error = ex.ToString() }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(resolvedOutputPath, json);
        }
        finally
        {
            Shutdown();
        }
    }

    private void RunVlcSelfTest(string outputPath)
    {
        string resolvedOutputPath = ResolveSelfTestOutputPath(outputPath, "vlc-self-test.json");
        object result;
        try
        {
            VlcRuntime.ValidateOrThrow();
            result = new
            {
                available = VlcRuntime.IsAvailable,
                version = VlcRuntime.Instance.Version,
                initializationError = (string?)null,
            };
        }
        catch (Exception ex)
        {
            result = new
            {
                available = false,
                version = (string?)null,
                initializationError = ex.ToString(),
            };
        }

        File.WriteAllText(resolvedOutputPath,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
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
            outputPath = Path.GetFullPath(outputPath);
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

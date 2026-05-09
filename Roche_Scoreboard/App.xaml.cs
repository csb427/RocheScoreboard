using System;
using System.IO;
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
    /// Path to the crash log file. Each unhandled exception is appended with a
    /// timestamp so the next crash is diagnosable even if no WER dump is produced.
    /// </summary>
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roche_Scoreboard",
        "crash.log");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Install global exception handlers up-front so any error from here on
        // is logged (and, where possible, non-fatal ones are swallowed) rather
        // than tearing the process down silently mid-game.
        InstallGlobalExceptionHandlers();

        // Persist the live game state when Windows is logging off / shutting
        // down / restarting. Without this hook the app is killed by the
        // session manager with no chance to flush the running clock.
        SessionEnding += (_, _) => TrySaveActiveMainWindowState();

        // Enable high DPI support for modern displays
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;

        // Check for updates in the background; the prompt is deferred to MainWindow_Loaded
        try
        {
            await UpdateService.CheckForUpdateAsync();
        }
        catch
        {
            // Never let an update check failure prevent the app from starting.
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
}

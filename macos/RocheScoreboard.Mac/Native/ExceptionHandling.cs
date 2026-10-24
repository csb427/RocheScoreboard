using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using RocheScoreboard.Mac.Native;

namespace RocheScoreboard.Mac;
public sealed partial class App
{
    private bool _handlingFailure;
    private void InstallExceptionHandlers(IClassicDesktopStyleApplicationLifetime desktop)
    {
        Dispatcher.UIThread.UnhandledException += (_, failure) =>
        {
            failure.Handled = true;
            if (_handlingFailure) return;
            _handlingFailure = true;
            try
            {
                WriteCrashLog("UI", failure.Exception);
                TrySaveActiveSession();
            }
            finally { _handlingFailure = false; }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, failure) =>
        {
            if (failure.ExceptionObject is Exception error) WriteCrashLog("Process", error);
            if (failure.IsTerminating) TrySaveActiveSession();
        };
        TaskScheduler.UnobservedTaskException += (_, failure) => { WriteCrashLog("Background task", failure.Exception); failure.SetObserved(); };
        desktop.Exit += (_, _) => TrySaveActiveSession();
    }
    private static string CrashLogPath => Path.Combine(NativeStoragePaths.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roche_Scoreboard", "crash.log");
    private static void WriteCrashLog(string source, Exception failure)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath, $"{DateTimeOffset.Now:O} [{source}] {failure}\n\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private static void TrySaveActiveSession()
    {
        if (NativeDesktop.MainWindow is not Roche_Scoreboard.MainWindow window) return;
        try
        {
            if (Dispatcher.UIThread.CheckAccess()) window.TrySaveGameStateForShutdown();
            else Dispatcher.UIThread.Invoke(window.TrySaveGameStateForShutdown);
        }
        catch (Exception error) { WriteCrashLog("Recovery save", error); }
    }
}

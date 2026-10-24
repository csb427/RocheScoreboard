using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace RocheScoreboard.Mac.Native;

// Local diagnostics only: record input counts and UI state, never key values,
// typed text, team names, or network data. Keep a single bounded session log.
internal static class NativeInputDiagnostics
{
    private static bool _started;
    private static long _pointerEvents, _keyEvents;
    private static string _lastTarget = "none";
    private static readonly SemaphoreSlim LogGate = new(1, 1);
    internal static string LogPath => Path.Combine(NativeStoragePaths.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roche_Scoreboard", "input-diagnostics.log");

    internal static void Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_started) return;
        _started = true;
        InputElement.PointerPressedEvent.AddClassHandler<InputElement>((sender, e) =>
        {
            if (!ReferenceEquals(sender, e.Source)) return;
            _pointerEvents++;
            _lastTarget = sender is Control { Name: { } name } ? name : sender.GetType().Name;
        }, RoutingStrategies.Tunnel);
        InputElement.KeyDownEvent.AddClassHandler<InputElement>((sender, e) =>
        {
            if (ReferenceEquals(sender, e.Source)) _keyEvents++;
        }, RoutingStrategies.Tunnel);
        var watch = Stopwatch.StartNew();
        TimeSpan previous = watch.Elapsed;
        string session = Guid.NewGuid().ToString("N");
        _ = WriteAsync($"{DateTimeOffset.Now:O} start session={session} version={ProductionVersion.CurrentVersion}", reset: true);
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) =>
        {
            TimeSpan now = watch.Elapsed;
            double late = Math.Max(0, (now - previous).TotalMilliseconds - timer.Interval.TotalMilliseconds);
            previous = now;
            string windows = string.Join("; ", desktop.Windows.Where(w => w.IsVisible).Select(w =>
                $"{w.GetType().Name}[active={w.IsActive},enabled={w.IsEffectivelyEnabled},hitTest={w.IsHitTestVisible}]"));
            string panels = desktop.MainWindow is { } main
                ? string.Join("; ", new[] { "SportSelectionPanel", "SetupWizardPanel", "MainContent", "CricketControlPanelView", "TrainingControlPanelView" }
                    .Select(name => main.FindControl<Control>(name)).OfType<Control>().Where(c => c.IsVisible)
                    .Select(c => $"{c.Name}[enabled={c.IsEffectivelyEnabled},hitTest={c.IsHitTestVisible}]")) : "none";
            _ = WriteAsync($"{DateTimeOffset.Now:O} lateMs={late:0} mouse={_pointerEvents} keys={_keyEvents} target={_lastTarget} windows={windows} panels={panels}");
        };
        desktop.Exit += (_, _) => timer.Stop();
        timer.Start();
    }
    private static async Task WriteAsync(string line, bool reset = false)
    {
        await LogGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            if (reset || new FileInfo(LogPath) is { Exists: true, Length: > 1024 * 1024 })
                await File.WriteAllTextAsync(LogPath, line + Environment.NewLine).ConfigureAwait(false);
            else
                await File.AppendAllTextAsync(LogPath, line + Environment.NewLine).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        finally { LogGate.Release(); }
    }
}

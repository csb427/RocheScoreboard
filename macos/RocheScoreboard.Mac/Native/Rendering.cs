using Avalonia.Threading;
namespace RocheScoreboard.Mac.Native;
public static class NativeRendering
{
    private static EventHandler? _rendering;
    private static readonly DispatcherTimer Timer = new(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, e) => _rendering?.Invoke(null, e));
    public static event EventHandler Rendering
    {
        add { _rendering += value; Timer.Start(); }
        remove { _rendering -= value; if (_rendering is null) Timer.Stop(); }
    }
}
public static class NativeClipboard
{
    public static void SetText(string text)
    {
        var clipboard = NativeDesktop.MainWindow?.Clipboard ?? throw new InvalidOperationException("Clipboard requires an open operator window.");
        NativeAwait.Run(SetAsync());
        async Task<bool> SetAsync() { await clipboard.SetTextAsync(text); return true; }
    }
}

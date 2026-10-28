using Avalonia.Threading;
namespace RocheScoreboard.Mac.Native;
public static class NativeRendering
{
    private sealed class Clock
    {
        public EventHandler? Handlers;
        public readonly DispatcherTimer Timer;
        public Clock(){Timer=new DispatcherTimer(TimeSpan.FromMilliseconds(16),DispatcherPriority.Background,(_,e)=>Handlers?.Invoke(null,e));}
    }
    // A clock belongs to its dispatcher. This also keeps independent headless
    // sessions from retaining a timer associated with a disposed test dispatcher.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Dispatcher,Clock> Clocks=new();
    private static Clock Current=>Clocks.GetValue(Dispatcher.UIThread,_=>new Clock());
    internal static int SubscriberCount=>Current.Handlers?.GetInvocationList().Length??0;
    public static event EventHandler Rendering
    {
        add {var clock=Current;clock.Handlers+=value;clock.Timer.Start();}
        remove {var clock=Current;clock.Handlers-=value;if(clock.Handlers is null)clock.Timer.Stop();}
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

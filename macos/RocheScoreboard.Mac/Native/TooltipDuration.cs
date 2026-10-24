using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Threading;
namespace RocheScoreboard.Mac.Native;
public sealed class TooltipDuration
{
    private sealed class State { public int Duration; public IDisposable? Timer; }
    private static readonly ConditionalWeakTable<Control, State> States = new();
    public static int GetMilliseconds(Control target) => States.TryGetValue(target, out var state) ? state.Duration : 5000;
    public static void SetMilliseconds(Control target, int value)
    {
        var state = States.GetOrCreateValue(target); state.Duration = Math.Max(0, value);
        target.PropertyChanged += (_, change) =>
        {
            if (change.Property != ToolTip.IsOpenProperty) return;
            state.Timer?.Dispose(); state.Timer = null;
            if (ToolTip.GetIsOpen(target)) state.Timer = DispatcherTimer.RunOnce(() => ToolTip.SetIsOpen(target, false), TimeSpan.FromMilliseconds(state.Duration));
        };
        target.Unloaded += (_, _) => { state.Timer?.Dispose(); state.Timer = null; };
    }
}

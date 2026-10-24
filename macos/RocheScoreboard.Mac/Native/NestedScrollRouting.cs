using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;

namespace Roche_Scoreboard.Services;

internal static class NestedScrollRouting
{
    public static void RoutePreviewMouseWheel(ScrollViewer host, PointerWheelEventArgs e)
    {
        if (e.Handled) return;
        double maxX = Math.Max(0, host.Extent.Width - host.Viewport.Width);
        double maxY = Math.Max(0, host.Extent.Height - host.Viewport.Height);
        bool horizontal = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || maxY <= 0 || Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y);
        double delta = horizontal && Math.Abs(e.Delta.X) > .001 ? e.Delta.X : e.Delta.Y;
        if (Math.Abs(delta) < .001) return;
        double step = Math.Max(48, Math.Abs(delta) * 66) * Math.Sign(delta);
        var offset = horizontal
            ? new Vector(Math.Clamp(host.Offset.X - step, 0, maxX), host.Offset.Y)
            : new Vector(host.Offset.X, Math.Clamp(host.Offset.Y - step, 0, maxY));
        if ((offset - host.Offset).Length < .01) return;
        host.Offset = offset;
        e.Handled = true;
    }
}

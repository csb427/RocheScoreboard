using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Roche_Scoreboard.Services;

internal static class NestedScrollRouting
{
    public static void RoutePreviewMouseWheel(ScrollViewer host, MouseWheelEventArgs e)
    {
        ScrollViewer? nested = FindAncestor(e.OriginalSource as DependencyObject);
        if (nested is not null && !ReferenceEquals(nested, host))
        {
            bool nestedCanMove = e.Delta > 0
                ? nested.VerticalOffset > 0
                : nested.VerticalOffset < nested.ScrollableHeight;
            if (nestedCanMove) return;
        }

        double step = Math.Max(48, Math.Abs(e.Delta) * 0.55);
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 || host.ScrollableHeight <= 0)
        {
            host.ScrollToHorizontalOffset(Math.Clamp(
                host.HorizontalOffset + (e.Delta > 0 ? -step : step), 0, host.ScrollableWidth));
        }
        else
        {
            host.ScrollToVerticalOffset(Math.Clamp(
                host.VerticalOffset + (e.Delta > 0 ? -step : step), 0, host.ScrollableHeight));
        }
        e.Handled = true;
    }

    private static ScrollViewer? FindAncestor(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is ScrollViewer viewer) return viewer;
            try { current = VisualTreeHelper.GetParent(current); }
            catch { current = LogicalTreeHelper.GetParent(current); }
        }
        return null;
    }
}

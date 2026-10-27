using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using System.Runtime.CompilerServices;

namespace Roche_Scoreboard.Services;

/// <summary>Show quiet scroll handles only during real user scrolling, not during layout changes.</summary>
public static class ScrollActivity
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(ScrollActivity), new PropertyMetadata(false, OnEnabled));
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.RegisterAttached(
        "Active", typeof(bool), typeof(ScrollActivity), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    private static readonly ConditionalWeakTable<ScrollViewer, DispatcherTimer> Timers = new();
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetActive(DependencyObject target, bool value) => target.SetValue(ActiveProperty, value);
    public static bool GetActive(DependencyObject target) => (bool)target.GetValue(ActiveProperty);
    private static void OnEnabled(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ScrollViewer viewer) return;
        if ((bool)args.NewValue)
        {
            viewer.ScrollChanged += OnScrolled;
            viewer.PreviewMouseWheel += OnWheel;
            viewer.PreviewKeyDown += OnKey;
            viewer.Unloaded += OnUnloaded;
        }
        else
        {
            viewer.ScrollChanged -= OnScrolled;
            viewer.PreviewMouseWheel -= OnWheel;
            viewer.PreviewKeyDown -= OnKey;
            viewer.Unloaded -= OnUnloaded;
            OnUnloaded(viewer, new RoutedEventArgs());
        }
    }
    private static void OnScrolled(object sender, ScrollChangedEventArgs args)
    {
        if (args.OriginalSource == sender && args.ExtentHeightChange == 0 && args.ExtentWidthChange == 0
            && args.ViewportHeightChange == 0 && args.ViewportWidthChange == 0
            && (args.VerticalChange != 0 || args.HorizontalChange != 0)) Wake((ScrollViewer)sender);
    }
    private static void OnWheel(object sender, MouseWheelEventArgs args) => Wake((ScrollViewer)sender);
    private static void OnKey(object sender, KeyEventArgs args)
    {
        if (args.Key is Key.PageDown or Key.PageUp or Key.Home or Key.End or Key.Down or Key.Up) Wake((ScrollViewer)sender);
    }
    private static void Wake(ScrollViewer viewer)
    {
        SetActive(viewer, true);
        var timer = Timers.GetValue(viewer, key =>
        {
            var value = new DispatcherTimer(DispatcherPriority.Background, key.Dispatcher) { Interval = TimeSpan.FromMilliseconds(1300) };
            value.Tick += (_, _) =>
            {
                if (key.IsMouseCaptureWithin) return;
                value.Stop(); SetActive(key, false);
            };
            return value;
        });
        timer.Stop(); timer.Start();
    }
    private static void OnUnloaded(object sender, RoutedEventArgs args)
    {
        var viewer = (ScrollViewer)sender;
        if (Timers.TryGetValue(viewer, out var timer)) timer.Stop();
        SetActive(viewer, false);
    }
}

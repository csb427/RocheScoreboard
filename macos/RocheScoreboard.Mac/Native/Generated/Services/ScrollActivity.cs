// Generated from Services/ScrollActivity.cs; edit translator/adapters.
// Source SHA-256: 2a349900638327548ed98eae138bd03fd5bdf96d5d6025092e41df52c168d07c
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using KeyEventArgs = Avalonia.Input.KeyEventArgs;
using System.Runtime.CompilerServices;

namespace Roche_Scoreboard.Services;

/// <summary>Show quiet scroll handles only during real user scrolling, not during layout changes.</summary>
public static class ScrollActivity
{
    public static readonly AttachedProperty<bool> EnabledProperty = (AttachedProperty<bool>)NativeProperty.RegisterAttached("Enabled",typeof(bool), typeof(ScrollActivity), new PropertyMetadata(false, OnEnabled));
    public static readonly AttachedProperty<bool> ActiveProperty = (AttachedProperty<bool>)NativeProperty.RegisterAttached("Active",typeof(bool), typeof(ScrollActivity), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    private static readonly ConditionalWeakTable<ScrollViewer, DispatcherTimer> Timers = new();
    public static void SetEnabled(AvaloniaObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static bool GetEnabled(AvaloniaObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetActive(AvaloniaObject target, bool value) => target.SetValue(ActiveProperty, value);
    public static bool GetActive(AvaloniaObject target) => (bool)target.GetValue(ActiveProperty);
    private static void OnEnabled(AvaloniaObject target, AvaloniaPropertyChangedEventArgs args)
    {
        if (target is not ScrollViewer viewer) return;
        if ((bool)args.NewValue)
        {
            viewer.ScrollChanged += OnScrolled;
            viewer.PointerWheelChanged += OnWheel;
            viewer.KeyDown += OnKey;
            viewer.Unloaded += OnUnloaded;
        }
        else
        {
            viewer.ScrollChanged -= OnScrolled;
            viewer.PointerWheelChanged -= OnWheel;
            viewer.KeyDown -= OnKey;
            viewer.Unloaded -= OnUnloaded;
            OnUnloaded(viewer, new RoutedEventArgs());
        }
    }
    private static void OnScrolled(object? sender, ScrollChangedEventArgs args)
    {
        if (args.Source == sender && args.ExtentDelta.Y == 0 && args.ExtentDelta.X == 0
            && args.ViewportDelta.Y == 0 && args.ViewportDelta.X == 0
            && (args.OffsetDelta.Y != 0 || args.OffsetDelta.X != 0)) Wake((ScrollViewer)sender);
    }
    private static void OnWheel(object? sender, PointerWheelEventArgs args) => Wake((ScrollViewer)sender);
    private static void OnKey(object? sender, KeyEventArgs args)
    {
        if (args.Key is Key.PageDown or Key.PageUp or Key.Home or Key.End or Key.Down or Key.Up) Wake((ScrollViewer)sender);
    }
    private static void Wake(ScrollViewer viewer)
    {
        SetActive(viewer, true);
        var timer = Timers.GetValue(viewer, key =>
        {
            var value = new DispatcherTimer() { Interval = TimeSpan.FromMilliseconds(1300) };
            value.Tick += (_, _) =>
            {
                if (NativePointer.IsCaptureWithin(key)) return;
                value.Stop(); SetActive(key, false);
            };
            return value;
        });
        timer.Stop(); timer.Start();
    }
    private static void OnUnloaded(object? sender, RoutedEventArgs args)
    {
        var viewer = (ScrollViewer)sender;
        if (Timers.TryGetValue(viewer, out var timer)) timer.Stop();
        SetActive(viewer, false);
    }
}

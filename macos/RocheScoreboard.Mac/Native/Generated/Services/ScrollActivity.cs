// Generated from Services/ScrollActivity.cs; edit translator/adapters.
// Source SHA-256: fdabdcb0d5c1d9e4e544380cef9b87cb9d9eaf63d16ab065485e305c39b4a25a
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using KeyEventArgs = Avalonia.Input.KeyEventArgs;
using ScrollBar = Avalonia.Controls.Primitives.ScrollBar;
using System.Runtime.CompilerServices;

namespace Roche_Scoreboard.Services;

/// <summary>Show quiet scroll handles only during real user scrolling, not during layout changes.</summary>
public static class ScrollActivity
{
    public static readonly AttachedProperty<bool> EnabledProperty = (AttachedProperty<bool>)NativeProperty.RegisterAttached("Enabled",typeof(bool), typeof(ScrollActivity), new PropertyMetadata(false, OnEnabled));
    public static readonly AttachedProperty<bool> ActiveProperty = (AttachedProperty<bool>)NativeProperty.RegisterAttached("Active",typeof(bool), typeof(ScrollActivity), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, OnActive));
    public static readonly AttachedProperty<bool> FadeEnabledProperty = (AttachedProperty<bool>)NativeProperty.RegisterAttached("FadeEnabled",typeof(bool), typeof(ScrollActivity), new PropertyMetadata(false, OnFadeEnabled));
    private static readonly ConditionalWeakTable<ScrollViewer, DispatcherTimer> Timers = new();
    private static readonly ConditionalWeakTable<ScrollBar, Fade> Fades = new();
    public static void SetEnabled(AvaloniaObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static bool GetEnabled(AvaloniaObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetActive(AvaloniaObject target, bool value) => target.SetValue(ActiveProperty, value);
    public static bool GetActive(AvaloniaObject target) => (bool)target.GetValue(ActiveProperty);
    public static void SetFadeEnabled(AvaloniaObject target, bool value) => target.SetValue(FadeEnabledProperty, value);
    public static bool GetFadeEnabled(AvaloniaObject target) => (bool)target.GetValue(FadeEnabledProperty);
    private static void OnActive(AvaloniaObject target, AvaloniaPropertyChangedEventArgs args)
    { if (target is ScrollBar bar && Fades.TryGetValue(bar, out var fade)) fade.Refresh(); }
    private static void OnFadeEnabled(AvaloniaObject target, AvaloniaPropertyChangedEventArgs args)
    {
        if (target is not ScrollBar bar) return;
        if (args.NewValue is true) Fades.GetValue(bar, value => new Fade(value));
        else if (Fades.TryGetValue(bar, out var fade)) { fade.Dispose(); Fades.Remove(bar); }
    }
    // The style owns the resting opacity. This controller owns only the
    // temporary clock, so clearing it never overwrites a style or binding.
    private sealed class Fade
    {
        private readonly ScrollBar _bar;
        private bool _observing;
        private bool _active;
        internal Fade(ScrollBar bar)
        {
            _bar = bar;
            _active = GetActive(bar);
            bar.Loaded += Loaded;
            bar.Unloaded += Unloaded;
            NativeEvents.WatchProperty(bar,Avalonia.Controls.Control.IsVisibleProperty,true,AvailabilityChanged);
            NativeEvents.WatchProperty(bar,Avalonia.Controls.Control.IsEnabledProperty,true,AvailabilityChanged);
            if (bar.IsLoaded) Observe();
        }
        private void Loaded(object? sender, RoutedEventArgs args) { Observe(); Rest(); }
        private void Unloaded(object? sender, RoutedEventArgs args) { StopObserving(); Rest(); }
        private void AvailabilityChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            // The native event bridge also delivers other property changes.
            if (args.Property.Name is nameof(Control.IsVisible) or nameof(Control.IsEnabled)) Rest();
        }
        private void Observe()
        {
            if (_observing) return;
            _observing = true;
            OperatorAppearance.Changed += PreferenceChanged;
            NativeSystemPreferences.StaticPropertyChanged += SystemPreferenceChanged;
        }
        private void StopObserving()
        {
            if (!_observing) return;
            _observing = false;
            OperatorAppearance.Changed -= PreferenceChanged;
            NativeSystemPreferences.StaticPropertyChanged -= SystemPreferenceChanged;
        }
        private void SystemPreferenceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        { if (args.PropertyName == nameof(NativeSystemPreferences.ClientAreaAnimation)) PreferenceChanged(); }
        private void PreferenceChanged()
        {
            if (!_observing) return;
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.InvokeAsync(new Action(PreferenceChanged)); return; }
            if (_observing && !OperatorMotion.Enabled) Rest();
        }
        internal void Refresh()
        {
            bool active = GetActive(_bar);
            // The Active style trigger may already expose its new base value.
            // A live clock is the current pose; otherwise use the prior rest.
            double current = NativeValueSource.GetValueSource(_bar, Control.OpacityProperty).IsAnimated
                ? _bar.Opacity : (_active ? .85 : 0);
            _active = active;
            Rest();
            if (!_observing || !_bar.IsLoaded || !_bar.IsVisible || !_bar.IsEnabled || !OperatorMotion.Enabled) return;
            _bar.BeginAnimation(Control.OpacityProperty, new DoubleAnimation(current, active ? .85 : 0,
                TimeSpan.FromMilliseconds(active ? 120 : 300))
                { FillBehavior = FillBehavior.Stop });
        }
        private void Rest() => _bar.BeginAnimation(Control.OpacityProperty, null);
        internal void Dispose()
        {
            StopObserving(); Rest();
            _bar.Loaded -= Loaded;
            _bar.Unloaded -= Unloaded;
            NativeEvents.WatchProperty(_bar,Avalonia.Controls.Control.IsVisibleProperty,false,AvailabilityChanged);
            NativeEvents.WatchProperty(_bar,Avalonia.Controls.Control.IsEnabledProperty,false,AvailabilityChanged);
        }
    }
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

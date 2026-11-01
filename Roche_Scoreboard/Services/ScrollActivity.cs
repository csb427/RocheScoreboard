using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using System.Runtime.CompilerServices;

namespace Roche_Scoreboard.Services;

/// <summary>Show quiet scroll handles only during real user scrolling, not during layout changes.</summary>
public static class ScrollActivity
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(ScrollActivity), new PropertyMetadata(false, OnEnabled));
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.RegisterAttached(
        "Active", typeof(bool), typeof(ScrollActivity), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, OnActive));
    public static readonly DependencyProperty FadeEnabledProperty = DependencyProperty.RegisterAttached(
        "FadeEnabled", typeof(bool), typeof(ScrollActivity), new PropertyMetadata(false, OnFadeEnabled));
    private static readonly ConditionalWeakTable<ScrollViewer, DispatcherTimer> Timers = new();
    private static readonly ConditionalWeakTable<ScrollBar, Fade> Fades = new();
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetActive(DependencyObject target, bool value) => target.SetValue(ActiveProperty, value);
    public static bool GetActive(DependencyObject target) => (bool)target.GetValue(ActiveProperty);
    public static void SetFadeEnabled(DependencyObject target, bool value) => target.SetValue(FadeEnabledProperty, value);
    public static bool GetFadeEnabled(DependencyObject target) => (bool)target.GetValue(FadeEnabledProperty);
    private static void OnActive(DependencyObject target, DependencyPropertyChangedEventArgs args)
    { if (target is ScrollBar bar && Fades.TryGetValue(bar, out var fade)) fade.Refresh(); }
    private static void OnFadeEnabled(DependencyObject target, DependencyPropertyChangedEventArgs args)
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
            bar.IsVisibleChanged += AvailabilityChanged;
            bar.IsEnabledChanged += AvailabilityChanged;
            if (bar.IsLoaded) Observe();
        }
        private void Loaded(object sender, RoutedEventArgs args) { Observe(); Rest(); }
        private void Unloaded(object sender, RoutedEventArgs args) { StopObserving(); Rest(); }
        private void AvailabilityChanged(object sender, DependencyPropertyChangedEventArgs args)
        {
            // The native event bridge also delivers other property changes.
            if (args.Property.Name is nameof(UIElement.IsVisible) or nameof(UIElement.IsEnabled)) Rest();
        }
        private void Observe()
        {
            if (_observing) return;
            _observing = true;
            OperatorAppearance.Changed += PreferenceChanged;
            SystemParameters.StaticPropertyChanged += SystemPreferenceChanged;
        }
        private void StopObserving()
        {
            if (!_observing) return;
            _observing = false;
            OperatorAppearance.Changed -= PreferenceChanged;
            SystemParameters.StaticPropertyChanged -= SystemPreferenceChanged;
        }
        private void SystemPreferenceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        { if (args.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) PreferenceChanged(); }
        private void PreferenceChanged()
        {
            if (!_observing) return;
            if (!_bar.Dispatcher.CheckAccess()) { _bar.Dispatcher.BeginInvoke(new Action(PreferenceChanged)); return; }
            if (_observing && !OperatorMotion.Enabled) Rest();
        }
        internal void Refresh()
        {
            bool active = GetActive(_bar);
            // The Active style trigger may already expose its new base value.
            // A live clock is the current pose; otherwise use the prior rest.
            double current = DependencyPropertyHelper.GetValueSource(_bar, UIElement.OpacityProperty).IsAnimated
                ? _bar.Opacity : (_active ? .85 : 0);
            _active = active;
            Rest();
            if (!_observing || !_bar.IsLoaded || !_bar.IsVisible || !_bar.IsEnabled || !OperatorMotion.Enabled) return;
            _bar.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(current, active ? .85 : 0,
                TimeSpan.FromMilliseconds(active ? 120 : 300))
                { FillBehavior = FillBehavior.Stop });
        }
        private void Rest() => _bar.BeginAnimation(UIElement.OpacityProperty, null);
        internal void Dispose()
        {
            StopObserving(); Rest();
            _bar.Loaded -= Loaded;
            _bar.Unloaded -= Unloaded;
            _bar.IsVisibleChanged -= AvailabilityChanged;
            _bar.IsEnabledChanged -= AvailabilityChanged;
        }
    }
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

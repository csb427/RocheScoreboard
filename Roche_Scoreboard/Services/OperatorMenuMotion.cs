using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ContextMenu = System.Windows.Controls.ContextMenu;

namespace Roche_Scoreboard.Services;

/// <summary>One bounded material arrival for native command menus, with no delayed command execution.</summary>
public static class OperatorMenuMotion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(OperatorMenuMotion), new PropertyMetadata(false, Changed));
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    private static readonly ConditionalWeakTable<ContextMenu, State> States = new();
    // A class Opened handler runs before the instance handler. Resolve the
    // actual styled part here rather than relying on State.Open having run.
    internal static bool OwnsArrival(ContextMenu menu)
        => GetEnabled(menu) && States.TryGetValue(menu, out _) && Surface(menu) is not null;
    private static FrameworkElement? Surface(ContextMenu menu)
    {
        menu.ApplyTemplate();
        return menu.Template?.FindName("PART_MenuSurface", menu) as FrameworkElement;
    }
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ContextMenu menu) return;
        if (args.NewValue is true) States.GetValue(menu, value => new State(value));
        else if (States.TryGetValue(menu, out var state)) { state.Dispose(); States.Remove(menu); }
    }
    private sealed class State
    {
        private readonly ContextMenu _menu;
        private FrameworkElement? _surface;
        private readonly ScaleTransform _scale = new(1, 1);
        internal State(ContextMenu menu) { _menu = menu; menu.Opened += Open; menu.Closed += Close; }
        private void Open(object sender, RoutedEventArgs args)
        {
            Reset(); _surface = Surface(_menu);
            if (_surface is null) return;
            OperatorAppearance.Changed += PreferenceChanged;
            SystemParameters.StaticPropertyChanged += SystemPreferenceChanged;
            _surface.RenderTransformOrigin = Origin(); _surface.RenderTransform = _scale;
            if (!OperatorMotion.Enabled) return;
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1-.06*OperatorMotion.TravelScale, 1, OperatorMotion.MaterialArrival) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1-.32*OperatorMotion.TravelScale, 1, OperatorMotion.MaterialArrival) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
            _surface.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.65, 1, OperatorMotion.SurfaceOpen) { FillBehavior = FillBehavior.Stop });
        }
        private System.Windows.Point Origin()
        {
            if(_surface is not {ActualWidth:>0,ActualHeight:>0} || _menu.PlacementTarget is not FrameworkElement target)return new(.5,0);
            try
            {
                var source=target.PointToScreen(new System.Windows.Point(target.ActualWidth*.5,target.ActualHeight*.5));
                var corner=_surface.PointToScreen(new System.Windows.Point());var dpi=VisualTreeHelper.GetDpi(_surface);
                return new(Math.Clamp((source.X-corner.X)/(_surface.ActualWidth*dpi.DpiScaleX),0,1),Math.Clamp((source.Y-corner.Y)/(_surface.ActualHeight*dpi.DpiScaleY),0,1));
            }
            catch(InvalidOperationException){return new(.5,0);}
        }
        private void SystemPreferenceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        { if (args.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) PreferenceChanged(); }
        private void PreferenceChanged()
        {
            if (!_menu.Dispatcher.CheckAccess()) { _menu.Dispatcher.BeginInvoke(new Action(PreferenceChanged)); return; }
            if (!OperatorMotion.Enabled) Reset();
        }
        private void Close(object sender, RoutedEventArgs args) => Reset();
        private void Reset()
        {
            OperatorAppearance.Changed -= PreferenceChanged;
            SystemParameters.StaticPropertyChanged -= SystemPreferenceChanged;
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            if (_surface is not null) { _surface.BeginAnimation(UIElement.OpacityProperty, null); _surface.RenderTransform = Transform.Identity; }
            _surface = null;
        }
        internal void Dispose() { Reset(); _menu.Opened -= Open; _menu.Closed -= Close; }
    }
}

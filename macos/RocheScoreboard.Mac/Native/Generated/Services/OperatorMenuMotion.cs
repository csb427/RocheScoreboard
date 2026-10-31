// Generated from Services/OperatorMenuMotion.cs; edit translator/adapters.
// Source SHA-256: abc378bf20963d84eff833653c79f1cb8b352c79ce8f23bf0e757030658f61f8
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System.Runtime.CompilerServices;
using ContextMenu = Avalonia.Controls.ContextMenu;

namespace Roche_Scoreboard.Services;

/// <summary>One bounded material arrival for native command menus, with no delayed command execution.</summary>
public static class OperatorMenuMotion
{
    public static readonly AttachedProperty<bool> EnabledProperty = (AttachedProperty<bool>)NativeProperty.RegisterAttached("Enabled", typeof(bool), typeof(OperatorMenuMotion), new PropertyMetadata(false, Changed));
    public static void SetEnabled(AvaloniaObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static bool GetEnabled(AvaloniaObject target) => (bool)target.GetValue(EnabledProperty);
    private static readonly ConditionalWeakTable<ContextMenu, State> States = new();
    // A class Opened handler runs before the instance handler. Resolve the
    // actual styled part here rather than relying on State.Open having run.
    internal static bool OwnsArrival(ContextMenu menu)
        => GetEnabled(menu) && States.TryGetValue(menu, out _) && Surface(menu) is not null;
    private static Control? Surface(ContextMenu menu)
    {
        menu.ApplyTemplate();
        return NativeTemplates.FindObject(menu,"PART_MenuSurface") as Control;
    }
    private static void Changed(AvaloniaObject target, AvaloniaPropertyChangedEventArgs args)
    {
        if (target is not ContextMenu menu) return;
        if (args.NewValue is true) States.GetValue(menu, value => new State(value));
        else if (States.TryGetValue(menu, out var state)) { state.Dispose(); States.Remove(menu); }
    }
    private sealed class State
    {
        private readonly ContextMenu _menu;
        private Control? _surface;
        private readonly ScaleTransform _scale = new(1, 1);
        internal State(ContextMenu menu) { _menu = menu; menu.Opened += Open; menu.Closed += Close; }
        private void Open(object? sender, RoutedEventArgs args)
        {
            Reset(); _surface = Surface(_menu);
            if (_surface is null) return;
            OperatorAppearance.Changed += PreferenceChanged;
            NativeSystemPreferences.StaticPropertyChanged += SystemPreferenceChanged;
            _surface.RenderTransformOrigin = new RelativePoint(Origin(),RelativeUnit.Relative); _surface.RenderTransform = _scale;
            if (!OperatorMotion.Enabled) return;
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1-.06*OperatorMotion.TravelScale, 1, OperatorMotion.MaterialArrival) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1-.32*OperatorMotion.TravelScale, 1, OperatorMotion.MaterialArrival) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
            _surface.BeginAnimation(Control.OpacityProperty, new DoubleAnimation(.65, 1, OperatorMotion.SurfaceOpen) { FillBehavior = FillBehavior.Stop });
        }
        private Avalonia.Point Origin()
        {
            if(_surface is not {Bounds.Width:>0,Bounds.Height:>0} || _menu.PlacementTarget is not Control target)return new(.5,0);
            try
            {
                var source=target.PointToScreen(new Avalonia.Point(target.NativeWidth()*.5,target.NativeHeight()*.5));
                var corner=_surface.PointToScreen(new Avalonia.Point());var dpi=VisualTreeHelper.GetDpi(_surface);
                return new(Math.Clamp((source.X-corner.X)/(_surface.NativeWidth()*dpi.DpiScaleX),0,1),Math.Clamp((source.Y-corner.Y)/(_surface.NativeHeight()*dpi.DpiScaleY),0,1));
            }
            catch(InvalidOperationException){return new(.5,0);}
        }
        private void SystemPreferenceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        { if (args.PropertyName == nameof(NativeSystemPreferences.ClientAreaAnimation)) PreferenceChanged(); }
        private void PreferenceChanged()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.InvokeAsync(new Action(PreferenceChanged)); return; }
            if (!OperatorMotion.Enabled) Reset();
        }
        private void Close(object? sender, RoutedEventArgs args) => Reset();
        private void Reset()
        {
            OperatorAppearance.Changed -= PreferenceChanged;
            NativeSystemPreferences.StaticPropertyChanged -= SystemPreferenceChanged;
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            if (_surface is not null) { _surface.BeginAnimation(Control.OpacityProperty, null); _surface.RenderTransform = new MatrixTransform(Matrix.Identity); }
            _surface = null;
        }
        internal void Dispose() { Reset(); _menu.Opened -= Open; _menu.Closed -= Close; }
    }
}

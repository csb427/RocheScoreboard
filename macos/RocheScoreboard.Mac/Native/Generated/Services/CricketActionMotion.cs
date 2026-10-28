// Generated from Services/CricketActionMotion.cs; edit translator/adapters.
// Source SHA-256: 3ab18be53d03b3266eb9ccc8f7fcbf77520bdc27fd90531a59fa2a253edeaa3d
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using ButtonBase = Avalonia.Controls.Button;

namespace Roche_Scoreboard.Services;

/// <summary>Animate the surface inside a stable hit target; hidden controls always return to rest.</summary>
public static class CricketActionMotion
{
    public static readonly AttachedProperty<bool> EnabledProperty = (AttachedProperty<bool>)NativeProperty.RegisterAttached("Enabled", typeof(bool), typeof(CricketActionMotion), new PropertyMetadata(false, Changed));
    public static bool GetEnabled(AvaloniaObject value) => (bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(AvaloniaObject value, bool enabled) => value.SetValue(EnabledProperty, enabled);
    private static void Changed(AvaloniaObject source, AvaloniaPropertyChangedEventArgs args)
    {
        if (source is not ButtonBase button) return;
        button.PointerEntered -= Enter; button.PointerExited -= Leave; button.Click -= Click;
        button.GotFocus -= Focus; button.LostFocus -= Blur;
        button.PointerPressed -= Press; button.PointerReleased -= Release;
        button.Loaded -= Load; button.Unloaded -= Unload; NativeEvents.WatchProperty(button,Avalonia.Controls.Control.IsVisibleProperty,false,VisibilityChanged);
        if (args.NewValue is true)
        {
            button.PointerEntered += Enter; button.PointerExited += Leave; button.Click += Click;
            button.GotFocus += Focus; button.LostFocus += Blur;
            button.PointerPressed += Press; button.PointerReleased += Release;
            button.Loaded += Load; button.Unloaded += Unload; NativeEvents.WatchProperty(button,Avalonia.Controls.Control.IsVisibleProperty,true,VisibilityChanged);
        }
        else Reset(button);
    }
    private static T? Part<T>(ButtonBase button, string name) where T : class
    {
        try { return NativeTemplates.FindObject(button,name) as T; }
        catch (InvalidOperationException) { return null; }
    }
    private static void Enter(object? sender, Avalonia.Input.PointerEventArgs args) => Hover((ButtonBase)sender);
    private static void Leave(object? sender, Avalonia.Input.PointerEventArgs args) => Hover((ButtonBase)sender);
    private static void Focus(object? sender, Avalonia.Input.GotFocusEventArgs args) => Hover((ButtonBase)sender);
    private static void Blur(object? sender, RoutedEventArgs args) => Hover((ButtonBase)sender);
    private static void Load(object? sender, RoutedEventArgs args) => Reset((ButtonBase)sender);
    private static void Unload(object? sender, RoutedEventArgs args) => Reset((ButtonBase)sender);
    private static void VisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs args) { if (args.NewValue is false) Reset((ButtonBase)sender); }
    private static void Press(object? sender, PointerEventArgs args)
    {
        var button = (ButtonBase)sender;
        if (button.IsEnabled) Paint(button, .97, 0, .25, OperatorMotion.Press);
    }
    private static void Release(object? sender, PointerEventArgs args) => Hover((ButtonBase)sender);
    private static void Hover(ButtonBase button)
    {
        if (!button.IsLoaded || !button.IsVisible || !button.IsEnabled) { Reset(button); return; }
        bool active = button.IsPointerOver || button.IsKeyboardFocusWithin;
        Paint(button, active ? .99 : 1, active ? -1 : 0, active ? .20 : 0, active ? OperatorMotion.HoverIn : OperatorMotion.HoverOut);
    }
    private static void Click(object? sender, RoutedEventArgs args)
    {
        var button = (ButtonBase)sender;
        if (!button.IsLoaded || !button.IsVisible || !button.IsEnabled) { Reset(button); return; }
        bool active = button.IsPointerOver || button.IsKeyboardFocusWithin;
        Paint(button, active ? .99 : 1, active ? -1 : 0, active ? .20 : 0, OperatorMotion.SurfaceOpen,
            new BackEase { Amplitude = .35, EasingMode = EasingMode.EaseOut });
    }
    private static void Paint(ButtonBase button, double scale, double lift, double glow, TimeSpan duration, IEasingFunction? easing = null)
    {
        if (Part<ScaleTransform>(button, "TbSc") is { } transform)
        {
            Animate(transform, ScaleTransform.ScaleXProperty, scale, duration, easing);
            Animate(transform, ScaleTransform.ScaleYProperty, scale, duration, easing);
        }
        if (Part<TranslateTransform>(button, "TbLift") is { } shift) Animate(shift, TranslateTransform.YProperty, lift, duration, easing);
        if (Part<Control>(button, "ActionGlow") is { } highlight) Animate(highlight, Control.OpacityProperty, glow, duration, easing);
    }
    private static void Animate(AvaloniaObject target, AvaloniaProperty property, double value, TimeSpan duration, IEasingFunction? easing)
    {
        if (!OperatorMotion.Enabled) { target.BeginAnimation(property, null); target.SetValue(property, value); return; }
        target.BeginAnimation(property, new DoubleAnimation(value, duration) { EasingFunction = easing ?? OperatorMotion.SmoothOut });
    }
    private static void Animate(Control target, AvaloniaProperty property, double value, TimeSpan duration, IEasingFunction? easing)
    {
        if (!OperatorMotion.Enabled) { target.BeginAnimation(property, null); target.SetValue(property, value); return; }
        target.BeginAnimation(property, new DoubleAnimation(value, duration) { EasingFunction = easing ?? OperatorMotion.SmoothOut });
    }
    private static void Reset(ButtonBase button)
    {
        if (Part<ScaleTransform>(button, "TbSc") is { } scale) { scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); scale.ScaleX = scale.ScaleY = 1; }
        if (Part<TranslateTransform>(button, "TbLift") is { } shift) { shift.BeginAnimation(TranslateTransform.YProperty, null); shift.Y = 0; }
        if (Part<Control>(button, "ActionGlow") is { } glow) { glow.BeginAnimation(Control.OpacityProperty, null); glow.Opacity = 0; }
    }
}

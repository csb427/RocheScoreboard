using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace Roche_Scoreboard.Services;

/// <summary>Animate the surface inside a stable hit target; hidden controls always return to rest.</summary>
public static class CricketActionMotion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(CricketActionMotion), new PropertyMetadata(false, Changed));
    public static bool GetEnabled(DependencyObject value) => (bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value, bool enabled) => value.SetValue(EnabledProperty, enabled);
    private static void Changed(DependencyObject source, DependencyPropertyChangedEventArgs args)
    {
        if (source is not ButtonBase button) return;
        button.MouseEnter -= Enter; button.MouseLeave -= Leave; button.Click -= Click;
        button.GotKeyboardFocus -= Focus; button.LostKeyboardFocus -= Blur;
        button.PreviewMouseLeftButtonDown -= Press; button.PreviewMouseLeftButtonUp -= Release;
        button.Loaded -= Load; button.Unloaded -= Unload; button.IsVisibleChanged -= VisibilityChanged;
        if (args.NewValue is true)
        {
            button.MouseEnter += Enter; button.MouseLeave += Leave; button.Click += Click;
            button.GotKeyboardFocus += Focus; button.LostKeyboardFocus += Blur;
            button.PreviewMouseLeftButtonDown += Press; button.PreviewMouseLeftButtonUp += Release;
            button.Loaded += Load; button.Unloaded += Unload; button.IsVisibleChanged += VisibilityChanged;
        }
        else Reset(button);
    }
    private static T? Part<T>(ButtonBase button, string name) where T : class
    {
        try { return button.Template?.FindName(name, button) as T; }
        catch (InvalidOperationException) { return null; }
    }
    private static void Enter(object sender, System.Windows.Input.MouseEventArgs args) => Hover((ButtonBase)sender);
    private static void Leave(object sender, System.Windows.Input.MouseEventArgs args) => Hover((ButtonBase)sender);
    private static void Focus(object sender, KeyboardFocusChangedEventArgs args) => Hover((ButtonBase)sender);
    private static void Blur(object sender, KeyboardFocusChangedEventArgs args) => Hover((ButtonBase)sender);
    private static void Load(object sender, RoutedEventArgs args) => Reset((ButtonBase)sender);
    private static void Unload(object sender, RoutedEventArgs args) => Reset((ButtonBase)sender);
    private static void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) { if (args.NewValue is false) Reset((ButtonBase)sender); }
    private static void Press(object sender, MouseButtonEventArgs args)
    {
        var button = (ButtonBase)sender;
        if (button.IsEnabled) Paint(button, .97, 0, .25, OperatorMotion.Press);
    }
    private static void Release(object sender, MouseButtonEventArgs args) => Hover((ButtonBase)sender);
    private static void Hover(ButtonBase button)
    {
        if (!button.IsLoaded || !button.IsVisible || !button.IsEnabled) { Reset(button); return; }
        bool active = button.IsMouseOver || button.IsKeyboardFocusWithin;
        Paint(button, active ? .99 : 1, active ? -1 : 0, active ? .20 : 0, active ? OperatorMotion.HoverIn : OperatorMotion.HoverOut);
    }
    private static void Click(object sender, RoutedEventArgs args)
    {
        var button = (ButtonBase)sender;
        if (!button.IsLoaded || !button.IsVisible || !button.IsEnabled) { Reset(button); return; }
        bool active = button.IsMouseOver || button.IsKeyboardFocusWithin;
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
        if (Part<UIElement>(button, "ActionGlow") is { } highlight) Animate(highlight, UIElement.OpacityProperty, glow, duration, easing);
    }
    private static void Animate(Animatable target, DependencyProperty property, double value, TimeSpan duration, IEasingFunction? easing)
    {
        if (!OperatorMotion.Enabled) { target.BeginAnimation(property, null); target.SetValue(property, value); return; }
        target.BeginAnimation(property, new DoubleAnimation(value, duration) { EasingFunction = easing ?? OperatorMotion.SmoothOut });
    }
    private static void Animate(UIElement target, DependencyProperty property, double value, TimeSpan duration, IEasingFunction? easing)
    {
        if (!OperatorMotion.Enabled) { target.BeginAnimation(property, null); target.SetValue(property, value); return; }
        target.BeginAnimation(property, new DoubleAnimation(value, duration) { EasingFunction = easing ?? OperatorMotion.SmoothOut });
    }
    private static void Reset(ButtonBase button)
    {
        if (Part<ScaleTransform>(button, "TbSc") is { } scale) { scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); scale.ScaleX = scale.ScaleY = 1; }
        if (Part<TranslateTransform>(button, "TbLift") is { } shift) { shift.BeginAnimation(TranslateTransform.YProperty, null); shift.Y = 0; }
        if (Part<UIElement>(button, "ActionGlow") is { } glow) { glow.BeginAnimation(UIElement.OpacityProperty, null); glow.Opacity = 0; }
    }
}

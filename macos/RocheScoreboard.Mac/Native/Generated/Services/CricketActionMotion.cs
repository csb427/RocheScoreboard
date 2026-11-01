// Generated from Services/CricketActionMotion.cs; edit translator/adapters.
// Source SHA-256: dc890beb698b72e4623b1dd1bb75dead9edc3ebc4328d38d33a860f8459d0844
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System.Runtime.CompilerServices;
using Avalonia.Input;
using ButtonBase = Avalonia.Controls.Button;

namespace Roche_Scoreboard.Services;

/// <summary>Animate the surface inside a stable hit target; hidden controls always return to rest.</summary>
public static class CricketActionMotion
{
    private static readonly ConditionalWeakTable<ButtonBase, PreferenceWatch> Preferences = new();
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
        NativeEvents.WatchProperty(button,Avalonia.Controls.Control.IsEnabledProperty,false,VisibilityChanged);
        if (args.NewValue is true)
        {
            button.PointerEntered += Enter; button.PointerExited += Leave; button.Click += Click;
            button.GotFocus += Focus; button.LostFocus += Blur;
            button.PointerPressed += Press; button.PointerReleased += Release;
            button.Loaded += Load; button.Unloaded += Unload; NativeEvents.WatchProperty(button,Avalonia.Controls.Control.IsVisibleProperty,true,VisibilityChanged);
            NativeEvents.WatchProperty(button,Avalonia.Controls.Control.IsEnabledProperty,true,VisibilityChanged);
            Preferences.GetValue(button, target => new PreferenceWatch(target)).Update();
            if(button.IsLoaded)Reset(button);
        }
        else
        {
            if(Preferences.TryGetValue(button,out var watch))watch.Stop();
            Preferences.Remove(button);
            Reset(button);
        }
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
    private static void Load(object? sender, RoutedEventArgs args)
    {
        var button=(ButtonBase)sender;
        Preferences.GetValue(button,target=>new PreferenceWatch(target)).Update();
        Reset(button);
    }
    private static void Unload(object? sender, RoutedEventArgs args)
    {
        var button=(ButtonBase)sender;
        if(Preferences.TryGetValue(button,out var watch))watch.Stop();
        Reset(button);
    }
    private static void VisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        // Native adapters may forward general property notifications here.
        if(args.Property.Name is not ("IsVisible" or "IsEnabled"))return;
        var button=(ButtonBase)sender;
        if(Preferences.TryGetValue(button,out var watch))watch.Update();
        if(!button.IsLoaded || !button.IsVisible || !button.IsEnabled)Reset(button);
    }
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
        if(!OperatorMotion.Enabled){Settle(button,glow);return;}
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
    private sealed class PreferenceWatch
    {
        private readonly ButtonBase _button;
        private bool _watching;
        internal PreferenceWatch(ButtonBase button)=>_button=button;
        internal void Update()
        {
            bool watch=GetEnabled(_button) && _button.IsLoaded && _button.IsVisible && _button.IsEnabled;
            if(watch==_watching)return;
            if(watch){_watching=true;OperatorAppearance.Changed+=Changed;}
            else Stop();
        }
        internal void Stop()
        {
            if(!_watching)return;
            _watching=false;OperatorAppearance.Changed-=Changed;
        }
        private void Changed()
        {
            if(!_watching)return;
            Update();
            if(!_watching){Reset(_button);return;}
            if(OperatorMotion.Enabled)return;
            Settle(_button,_button.IsPressed ? .25 : (_button.IsPointerOver || _button.IsKeyboardFocusWithin) ? .20 : 0);
        }
    }
    private static void Settle(ButtonBase button,double glow)
    {
        Reset(button);
        if(Part<Control>(button,"ActionGlow") is { } highlight)highlight.Opacity=glow;
    }
    private static void Reset(ButtonBase button)
    {
        if (Part<ScaleTransform>(button, "TbSc") is { } scale) { scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); scale.ScaleX = scale.ScaleY = 1; }
        if (Part<TranslateTransform>(button, "TbLift") is { } shift) { shift.BeginAnimation(TranslateTransform.YProperty, null); shift.Y = 0; }
        if (Part<Control>(button, "ActionGlow") is { } glow) { glow.BeginAnimation(Control.OpacityProperty, null); glow.Opacity = 0; }
    }
}

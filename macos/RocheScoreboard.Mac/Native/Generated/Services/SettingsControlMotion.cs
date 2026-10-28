// Generated from Services/SettingsControlMotion.cs; edit translator/adapters.
// Source SHA-256: cd91fd933dad2ff636e2d49ddddc44e32573190e1626e8276b7dceba467b1d06
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using TemplatedControl = Avalonia.Controls.Primitives.TemplatedControl;
using CheckBox = Avalonia.Controls.CheckBox;
using ComboBox = Avalonia.Controls.ComboBox;

namespace Roche_Scoreboard.Services;

/// <summary>Switch and dropdown feedback without moving the control's hit target.</summary>
public static class SettingsControlMotion
{
    public static readonly AttachedProperty<bool> EnabledProperty = (AttachedProperty<bool>)NativeProperty.RegisterAttached("Enabled", typeof(bool), typeof(SettingsControlMotion), new PropertyMetadata(false, Changed));
    public static bool GetEnabled(AvaloniaObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(AvaloniaObject target, bool value) => target.SetValue(EnabledProperty, value);
    private static void Changed(AvaloniaObject source, AvaloniaPropertyChangedEventArgs args)
    {
        if (source is not TemplatedControl control) return;
        control.Loaded -= Loaded; control.Unloaded -= Unloaded; NativeEvents.WatchProperty(control,Avalonia.Controls.Control.IsVisibleProperty,false,VisibilityChanged);
        if (control is CheckBox check) { check.Checked -= Checked; check.Unchecked -= Checked; check.Indeterminate -= Checked; }
        if (control is ComboBox combo) { combo.DropDownOpened -= Opened; combo.DropDownClosed -= Closed; }
        if (args.NewValue is not true) return;
        control.Loaded += Loaded; control.Unloaded += Unloaded; NativeEvents.WatchProperty(control,Avalonia.Controls.Control.IsVisibleProperty,true,VisibilityChanged);
        if (control is CheckBox toggle) { toggle.Checked += Checked; toggle.Unchecked += Checked; toggle.Indeterminate += Checked; }
        if (control is ComboBox choice) { choice.DropDownOpened += Opened; choice.DropDownClosed += Closed; }
    }
    private static T? Part<T>(TemplatedControl target, string name) where T : class
    {
        try { return NativeTemplates.FindObject(target,name) as T; }
        catch (InvalidOperationException) { return null; }
    }
    private static void Loaded(object? sender, RoutedEventArgs args)
    {
        if (sender is CheckBox toggle) { toggle.ApplyTemplate(); SetThumb(toggle, false); }
    }
    private static void VisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (sender is not CheckBox toggle || args.NewValue is not true) return;
        Dispatcher.UIThread.InvokeAsync(new Action(() =>
        {
            if (!toggle.IsLoaded || !toggle.IsVisible) return;
            toggle.ApplyTemplate(); SetThumb(toggle, false);
        }), Avalonia.Threading.DispatcherPriority.Loaded);
    }
    private static void Unloaded(object? sender, RoutedEventArgs args) { if (sender is CheckBox toggle) SetThumb(toggle, false); }
    private static void Checked(object? sender, RoutedEventArgs args) => SetThumb((CheckBox)sender, true);
    private static void SetThumb(CheckBox toggle, bool animate)
    {
        if (Part<TranslateTransform>(toggle, "Thumb") is not { } thumb) return;
        double target = toggle.IsChecked == true ? 17 : 0;
        if (!animate || !toggle.IsLoaded || !toggle.IsVisible || !OperatorMotion.Enabled)
        { thumb.BeginAnimation(TranslateTransform.XProperty, null); thumb.X = target; return; }
        double start = thumb.X, direction = target >= start ? 1 : -1;
        var frames = new DoubleAnimationUsingKeyFrames { Duration = OperatorMotion.Toggle };
        frames.KeyFrames.Add(new LinearDoubleKeyFrame(start, KeyTime.FromPercent(0)));
        frames.KeyFrames.Add(new EasingDoubleKeyFrame(target + direction, KeyTime.FromPercent(.55), OperatorMotion.SmoothOut));
        frames.KeyFrames.Add(new EasingDoubleKeyFrame(target - direction * .35, KeyTime.FromPercent(.8), OperatorMotion.SmoothOut));
        frames.KeyFrames.Add(new EasingDoubleKeyFrame(target, KeyTime.FromPercent(1), OperatorMotion.SmoothOut));
        thumb.BeginAnimation(TranslateTransform.XProperty, frames, HandoffBehavior.SnapshotAndReplace);
    }
    private static void Opened(object? sender, EventArgs args)
    {
        if (sender is not ComboBox combo || Part<Popup>(combo, "PART_Popup")?.Child is not Control panel) return;
        var shift = new TranslateTransform(); panel.RenderTransform = shift;
        if (!OperatorMotion.Enabled) { panel.Opacity = 1; return; }
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, 0, OperatorMotion.SurfaceOpen) { FillBehavior = FillBehavior.Stop, EasingFunction = OperatorMotion.SmoothOut });
        panel.BeginAnimation(Control.OpacityProperty, new DoubleAnimation(.4, 1, OperatorMotion.SurfaceOpen) { FillBehavior = FillBehavior.Stop });
    }
    private static void Closed(object? sender, EventArgs args)
    {
        if (sender is not ComboBox combo || Part<Popup>(combo, "PART_Popup")?.Child is not Control panel) return;
        panel.BeginAnimation(Control.OpacityProperty, null); panel.Opacity = 1;
        if (panel.RenderTransform is TranslateTransform shift) { shift.BeginAnimation(TranslateTransform.YProperty, null); shift.Y = 0; }
    }
}

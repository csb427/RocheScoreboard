using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Control = System.Windows.Controls.Control;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;

namespace Roche_Scoreboard.Services;

/// <summary>Switch and dropdown feedback without moving the control's hit target.</summary>
public static class SettingsControlMotion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(SettingsControlMotion), new PropertyMetadata(false, Changed));
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    private static void Changed(DependencyObject source, DependencyPropertyChangedEventArgs args)
    {
        if (source is not Control control) return;
        control.Loaded -= Loaded; control.Unloaded -= Unloaded; control.IsVisibleChanged -= VisibilityChanged;
        if (control is CheckBox check) { check.Checked -= Checked; check.Unchecked -= Checked; check.Indeterminate -= Checked; }
        if (control is ComboBox combo) { combo.DropDownOpened -= Opened; combo.DropDownClosed -= Closed; }
        if (args.NewValue is not true) return;
        control.Loaded += Loaded; control.Unloaded += Unloaded; control.IsVisibleChanged += VisibilityChanged;
        if (control is CheckBox toggle) { toggle.Checked += Checked; toggle.Unchecked += Checked; toggle.Indeterminate += Checked; }
        if (control is ComboBox choice) { choice.DropDownOpened += Opened; choice.DropDownClosed += Closed; }
    }
    private static T? Part<T>(Control target, string name) where T : class
    {
        try { return target.Template?.FindName(name, target) as T; }
        catch (InvalidOperationException) { return null; }
    }
    private static void Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is CheckBox toggle) { toggle.ApplyTemplate(); SetThumb(toggle, false); }
    }
    private static void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not CheckBox toggle || args.NewValue is not true) return;
        toggle.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!toggle.IsLoaded || !toggle.IsVisible) return;
            toggle.ApplyTemplate(); SetThumb(toggle, false);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private static void Unloaded(object sender, RoutedEventArgs args) { if (sender is CheckBox toggle) SetThumb(toggle, false); }
    private static void Checked(object sender, RoutedEventArgs args) => SetThumb((CheckBox)sender, true);
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
        if (sender is not ComboBox combo || Part<Popup>(combo, "PART_Popup")?.Child is not FrameworkElement panel) return;
        var shift = new TranslateTransform(); panel.RenderTransform = shift;
        if (!OperatorMotion.Enabled) { panel.Opacity = 1; return; }
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, 0, OperatorMotion.SurfaceOpen) { FillBehavior = FillBehavior.Stop, EasingFunction = OperatorMotion.SmoothOut });
        panel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.4, 1, OperatorMotion.SurfaceOpen) { FillBehavior = FillBehavior.Stop });
    }
    private static void Closed(object? sender, EventArgs args)
    {
        if (sender is not ComboBox combo || Part<Popup>(combo, "PART_Popup")?.Child is not FrameworkElement panel) return;
        panel.BeginAnimation(UIElement.OpacityProperty, null); panel.Opacity = 1;
        if (panel.RenderTransform is TranslateTransform shift) { shift.BeginAnimation(TranslateTransform.YProperty, null); shift.Y = 0; }
    }
}

// Generated from Services/OperatorChoiceMotion.cs; edit translator/adapters.
// Source SHA-256: 90f8c722e1229c46cad4c967595771e7ea4dba5f3441dc5dfcef5784cab827ed
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using ButtonBase = Avalonia.Controls.Button;

namespace Roche_Scoreboard.Services;

/// <summary>One interruptible filled-selection signal for cards, switches and saved actions.</summary>
public static class OperatorChoiceMotion
{
    private static readonly Dictionary<Control, int> Active = [];
    private static int _generation;
    static OperatorChoiceMotion() => OperatorAppearance.Changed += () =>
    { if (!OperatorMotion.Enabled) foreach (var button in Active.Keys.ToArray()) Settle(button); };
    public static readonly AttachedProperty<double> ProgressProperty = (AttachedProperty<double>)NativeProperty.RegisterAttached("Progress", typeof(double), typeof(OperatorChoiceMotion), new PropertyMetadata(0d));
    public static double GetProgress(AvaloniaObject element) => (double)element.GetValue(ProgressProperty);
    public static void SetProgress(AvaloniaObject element, double value) => element.SetValue(ProgressProperty, value);
    // Ordinary action buttons may represent persisted state without becoming a
    // toggle: e.g. a failed Save must never visually claim the club was saved.
    public static readonly AttachedProperty<bool> SelectedProperty = (AttachedProperty<bool>)NativeProperty.RegisterAttached("Selected", typeof(bool), typeof(OperatorChoiceMotion), new PropertyMetadata(false, SelectionChanged));
    public static bool GetSelected(AvaloniaObject element) => (bool)element.GetValue(SelectedProperty);
    public static void SetSelected(AvaloniaObject element, bool value) => element.SetValue(SelectedProperty, value);
    public static readonly AttachedProperty<bool> EnabledProperty = (AttachedProperty<bool>)NativeProperty.RegisterAttached("Enabled", typeof(bool), typeof(OperatorChoiceMotion), new PropertyMetadata(false, Changed));
    public static bool GetEnabled(AvaloniaObject element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(AvaloniaObject element, bool value) => element.SetValue(EnabledProperty, value);
    private static bool Selected(Control button) => button is ToggleButton toggle ? toggle.IsChecked == true : GetSelected(button);
    private static void Changed(AvaloniaObject element, AvaloniaPropertyChangedEventArgs args)
    {
        if (element is not Control button) return;
        button.Loaded -= Loaded; button.Unloaded -= Unloaded; NativeEffectiveVisibility.WatchProperty(button,Avalonia.Controls.Control.IsVisibleProperty,false,VisibilityChanged);
        if (button is ToggleButton old) { old.Checked -= Toggled; old.Unchecked -= Toggled; old.Indeterminate -= Toggled; }
        if (args.NewValue is not true) { Settle(button); Active.Remove(button); return; }
        button.Loaded += Loaded; button.Unloaded += Unloaded; NativeEffectiveVisibility.WatchProperty(button,Avalonia.Controls.Control.IsVisibleProperty,true,VisibilityChanged);
        if (button is ToggleButton toggle) { toggle.Checked += Toggled; toggle.Unchecked += Toggled; toggle.Indeterminate += Toggled; }
        if (button.IsLoaded) { Active[button] = ++_generation; Settle(button); }
    }
    private static void Loaded(object? sender, RoutedEventArgs args) { if (sender is Control button) { Active[button] = ++_generation; Settle(button); } }
    private static void Unloaded(object? sender, RoutedEventArgs args) { if (sender is Control button) { Settle(button); Active.Remove(button); } }
    private static void VisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    { if (sender is Control button && !NativeEffectiveVisibility.IsVisible(button)) Settle(button); }
    private static void Settle(Control button)
    {
        if (Active.ContainsKey(button)) Active[button] = ++_generation;
        button.BeginAnimation(ProgressProperty, null); SetProgress(button, Selected(button) ? 1 : 0);
    }
    private static void SelectionChanged(AvaloniaObject target, AvaloniaPropertyChangedEventArgs args)
    { if (target is Control button && GetEnabled(button)) Animate(button); }
    private static void Toggled(object? sender, RoutedEventArgs args) { if (sender is Control button) Animate(button); }
    private static void Animate(Control button)
    {
        if (!button.IsLoaded || !NativeEffectiveVisibility.IsVisible(button) || !OperatorMotion.Enabled) { Settle(button); return; }
        int generation = ++_generation; Active[button] = generation;
        double target = Selected(button) ? 1 : 0;
        var motion = new DoubleAnimation(target, OperatorMotion.ChoiceArrival) { EasingFunction = OperatorMotion.SmoothOut };
        motion.Completed += (_, _) => { if (Active.TryGetValue(button, out int current) && current == generation) Settle(button); };
        button.BeginAnimation(ProgressProperty, motion);
    }
}

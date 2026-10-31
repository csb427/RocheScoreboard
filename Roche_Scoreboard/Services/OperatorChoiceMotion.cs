using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace Roche_Scoreboard.Services;

/// <summary>One interruptible filled-selection signal for cards, switches and saved actions.</summary>
public static class OperatorChoiceMotion
{
    private static readonly Dictionary<FrameworkElement, int> Active = [];
    private static int _generation;
    static OperatorChoiceMotion() => OperatorAppearance.Changed += () =>
    { if (!OperatorMotion.Enabled) foreach (var button in Active.Keys.ToArray()) Settle(button); };
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.RegisterAttached("Progress", typeof(double), typeof(OperatorChoiceMotion), new PropertyMetadata(0d));
    public static double GetProgress(DependencyObject element) => (double)element.GetValue(ProgressProperty);
    public static void SetProgress(DependencyObject element, double value) => element.SetValue(ProgressProperty, value);
    // Ordinary action buttons may represent persisted state without becoming a
    // toggle: e.g. a failed Save must never visually claim the club was saved.
    public static readonly DependencyProperty SelectedProperty = DependencyProperty.RegisterAttached("Selected", typeof(bool), typeof(OperatorChoiceMotion), new PropertyMetadata(false, SelectionChanged));
    public static bool GetSelected(DependencyObject element) => (bool)element.GetValue(SelectedProperty);
    public static void SetSelected(DependencyObject element, bool value) => element.SetValue(SelectedProperty, value);
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(OperatorChoiceMotion), new PropertyMetadata(false, Changed));
    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);
    private static bool Selected(FrameworkElement button) => button is ToggleButton toggle ? toggle.IsChecked == true : GetSelected(button);
    private static void Changed(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not FrameworkElement button) return;
        button.Loaded -= Loaded; button.Unloaded -= Unloaded; button.IsVisibleChanged -= VisibilityChanged;
        if (button is ToggleButton old) { old.Checked -= Toggled; old.Unchecked -= Toggled; old.Indeterminate -= Toggled; }
        if (args.NewValue is not true) { Settle(button); Active.Remove(button); return; }
        button.Loaded += Loaded; button.Unloaded += Unloaded; button.IsVisibleChanged += VisibilityChanged;
        if (button is ToggleButton toggle) { toggle.Checked += Toggled; toggle.Unchecked += Toggled; toggle.Indeterminate += Toggled; }
        if (button.IsLoaded) { Active[button] = ++_generation; Settle(button); }
    }
    private static void Loaded(object sender, RoutedEventArgs args) { if (sender is FrameworkElement button) { Active[button] = ++_generation; Settle(button); } }
    private static void Unloaded(object sender, RoutedEventArgs args) { if (sender is FrameworkElement button) { Settle(button); Active.Remove(button); } }
    private static void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
    { if (sender is FrameworkElement button && !button.IsVisible) Settle(button); }
    private static void Settle(FrameworkElement button)
    {
        if (Active.ContainsKey(button)) Active[button] = ++_generation;
        button.BeginAnimation(ProgressProperty, null); SetProgress(button, Selected(button) ? 1 : 0);
    }
    private static void SelectionChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    { if (target is FrameworkElement button && GetEnabled(button)) Animate(button); }
    private static void Toggled(object sender, RoutedEventArgs args) { if (sender is FrameworkElement button) Animate(button); }
    private static void Animate(FrameworkElement button)
    {
        if (!button.IsLoaded || !button.IsVisible || !OperatorMotion.Enabled) { Settle(button); return; }
        int generation = ++_generation; Active[button] = generation;
        double target = Selected(button) ? 1 : 0;
        var motion = new DoubleAnimation(target, OperatorMotion.ChoiceArrival) { EasingFunction = OperatorMotion.SmoothOut };
        motion.Completed += (_, _) => { if (Active.TryGetValue(button, out int current) && current == generation) Settle(button); };
        button.BeginAnimation(ProgressProperty, motion);
    }
}

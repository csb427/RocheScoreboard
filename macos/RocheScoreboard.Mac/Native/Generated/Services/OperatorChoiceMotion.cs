// Generated from Services/OperatorChoiceMotion.cs; edit translator/adapters.
// Source SHA-256: 7585814f81040b90f11ea28b5a0176f75eeebe31458b24d1c01ebd99a99bf9bc
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System.Runtime.CompilerServices;

namespace Roche_Scoreboard.Services;

/// <summary>One interruptible filled-selection signal for cards, switches and saved actions.</summary>
public static class OperatorChoiceMotion
{
    private static readonly ConditionalWeakTable<Control, State> States = new();
    private static readonly HashSet<State> Active = [];
    static OperatorChoiceMotion() => OperatorAppearance.Changed += () =>
    { if (!OperatorMotion.Enabled) foreach (var state in Active.ToArray()) state.Settle(); };
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
    internal static bool IsAnimating(AvaloniaObject element) => element is Control control
        && States.TryGetValue(control, out var state) && state.IsAnimating;

    private static void Changed(AvaloniaObject element, AvaloniaPropertyChangedEventArgs args)
    {
        if (element is not Control button) return;
        if (args.NewValue is true)
        {
            var state = States.GetValue(button, control => new State(control));
            state.Attach();
        }
        else if (States.TryGetValue(button, out var state))
        {
            state.Dispose(); States.Remove(button);
        }
    }

    private static void SelectionChanged(AvaloniaObject target, AvaloniaPropertyChangedEventArgs args)
    { if (target is Control button && GetEnabled(button) && States.TryGetValue(button, out var state)) state.Retarget(); }

    private sealed class State(Control button)
    {
        private Control? _button = button;
        private double _position, _velocity, _target, _last;
        private bool _running, _attached;
        internal bool IsAnimating => _running;

        internal void Attach()
        {
            var control = _button;
            if (control is null || _attached) return;
            _attached = true;
            control.Loaded += Loaded; control.Unloaded += Unloaded; NativeEffectiveVisibility.WatchProperty(control,Avalonia.Controls.Control.IsVisibleProperty,true,VisibilityChanged);
            if (control is ToggleButton toggle)
            { toggle.Checked += Toggled; toggle.Unchecked += Toggled; toggle.Indeterminate += Toggled; }
            if (control.IsLoaded) Loaded(control, new RoutedEventArgs());
        }

        private void Loaded(object? sender, RoutedEventArgs args)
        {
            if (_button is not { } control) return;
            Active.Add(this);
            Settle();
        }
        private void Unloaded(object? sender, RoutedEventArgs args)
        {
            Settle(); Active.Remove(this);
        }
        private void VisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (_button is not { } control) return;
            if (!NativeEffectiveVisibility.IsVisible(control)) { Settle(); Active.Remove(this); }
            else if (control.IsLoaded) { Active.Add(this); Settle(); }
        }
        private void Toggled(object? sender, RoutedEventArgs args) => Retarget();

        internal void Retarget()
        {
            if (_button is not { } control) return;
            _target = IsSelected(control) ? 1 : 0;
            if (!control.IsLoaded || !NativeEffectiveVisibility.IsVisible(control) || !OperatorMotion.Enabled)
            { Settle(); return; }
            Active.Add(this);
            if (_running) return; // Keep the current spring position and velocity.
            if (Math.Abs(_target - _position) < .0001 && Math.Abs(_velocity) < .001)
            { _position = _target; _velocity = 0; SetProgress(control, _target); return; }
            _last = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
            _running = true; NativeRendering.Rendering += Frame;
        }

        private void Frame(object? sender, EventArgs args)
        {
            if (_button is not { } control || !control.IsLoaded || !NativeEffectiveVisibility.IsVisible(control) || !OperatorMotion.Enabled)
            { Settle(); Active.Remove(this); return; }
            double now = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
            double elapsed = Math.Max(0, now - _last); _last = now;
            OperatorMotion.AdvanceCriticallyDamped(ref _position, ref _velocity, _target, 20 * OperatorMotion.SpringResponse, elapsed);
            // Choice fill is a discrete state indicator, not an overscrollable
            // surface. Keep the visible value bounded while preserving the
            // spring state for ordinary retargets inside the range.
            SetProgress(control, Math.Clamp(_position, 0, 1));
            if (Math.Abs(_position - _target) < .001 && Math.Abs(_velocity) < .02)
            { _position = _target; _velocity = 0; SetProgress(control, _target); Stop(); }
        }

        internal void Settle()
        {
            Stop();
            if (_button is not { } control) return;
            _target = _position = IsSelected(control) ? 1 : 0; _velocity = 0;
            SetProgress(control, _position);
        }

        private void Stop()
        { if (!_running) return; NativeRendering.Rendering -= Frame; _running = false; }

        internal void Dispose()
        {
            var control = _button;
            if (control is null) return;
            Settle(); Active.Remove(this);
            if (_attached)
            {
                control.Loaded -= Loaded; control.Unloaded -= Unloaded; NativeEffectiveVisibility.WatchProperty(control,Avalonia.Controls.Control.IsVisibleProperty,false,VisibilityChanged);
                if (control is ToggleButton toggle)
                { toggle.Checked -= Toggled; toggle.Unchecked -= Toggled; toggle.Indeterminate -= Toggled; }
                _attached = false;
            }
            _button = null;
        }
    }

    private static bool IsSelected(Control button) => button is ToggleButton toggle
        ? toggle.IsChecked == true : GetSelected(button);
}

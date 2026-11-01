using System.Runtime.CompilerServices;
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
    private static readonly ConditionalWeakTable<Control, State> States = new();
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(SettingsControlMotion), new PropertyMetadata(false, Changed));
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);

    private static void Changed(DependencyObject source, DependencyPropertyChangedEventArgs args)
    {
        if (source is not Control control || control is not (CheckBox or ComboBox)) return;
        if (args.NewValue is true) States.GetValue(control, target => new State(target));
        else if (States.TryGetValue(control, out var state))
        {
            state.Dispose();
            States.Remove(control);
        }
    }

    private static T? Part<T>(Control target, string name) where T : class
    {
        try { return target.Template?.FindName(name, target) as T; }
        catch (InvalidOperationException) { return null; }
    }

    private sealed class State
    {
        private readonly Control _control;
        private bool _watchingPreferences, _disposed;
        private TranslateTransform? _thumb;
        private FrameworkElement? _popupSurface;
        private TranslateTransform? _popupShift;
        private Transform? _original;
        private Transform? _installedPopupTransform;
        private double _popupOpacity = 1;

        internal State(Control control)
        {
            _control = control;
            control.Loaded += Loaded;
            control.Unloaded += Unloaded;
            control.IsVisibleChanged += AvailabilityChanged;
            control.IsEnabledChanged += AvailabilityChanged;
            if (control is CheckBox toggle)
            {
                toggle.Checked += Checked;
                toggle.Unchecked += Checked;
                toggle.Indeterminate += Checked;
            }
            if (control is ComboBox combo)
            {
                combo.DropDownOpened += Opened;
                combo.DropDownClosed += Closed;
            }
            // An attached-property change need not be followed by Loaded.
            if (control.IsLoaded) Loaded(control, new RoutedEventArgs());
        }

        private void Loaded(object sender, RoutedEventArgs args)
        {
            if (_disposed || !GetEnabled(_control)) return;
            _control.ApplyTemplate();
            UpdatePreferenceWatch();
            SetThumb(false);
            if (_control is ComboBox { IsDropDownOpen: true } combo) TrackPopup(combo, false);
        }

        private void AvailabilityChanged(object sender, DependencyPropertyChangedEventArgs args)
        {
            // Native adapters can route a general property notification here.
            if (args.Property.Name is not ("IsVisible" or "IsEnabled") || _disposed) return;
            UpdatePreferenceWatch();
            if (!_control.IsLoaded || !_control.IsVisible || !_control.IsEnabled)
            {
                SettlePopup();
                SetThumb(false);
                return;
            }
            _control.ApplyTemplate();
            SetThumb(false);
            if (_control is ComboBox { IsDropDownOpen: true } combo && _popupSurface is null)
                TrackPopup(combo, false);
        }

        private void UpdatePreferenceWatch()
        {
            bool watch = !_disposed && _control.IsLoaded && _control.IsVisible && _control.IsEnabled;
            if (watch == _watchingPreferences) return;
            _watchingPreferences = watch;
            if (watch) OperatorAppearance.Changed += PreferenceChanged;
            else OperatorAppearance.Changed -= PreferenceChanged;
        }

        private void PreferenceChanged()
        {
            if (OperatorMotion.Enabled) return;
            SetThumb(false);
            SettlePopup();
        }

        private void Unloaded(object sender, RoutedEventArgs args)
        {
            OperatorAppearance.Changed -= PreferenceChanged;
            _watchingPreferences = false;
            SetThumb(false);
            _thumb = null;
            SettlePopup();
        }

        private void Checked(object sender, RoutedEventArgs args) => SetThumb(true);

        private void SetThumb(bool animate)
        {
            if (_control is not CheckBox toggle) return;
            double target = toggle.IsChecked == true ? 17 : 0;
            var thumb = Part<TranslateTransform>(toggle, "Thumb") ?? _thumb;
            if (_thumb is not null && !ReferenceEquals(_thumb, thumb))
            {
                _thumb.BeginAnimation(TranslateTransform.XProperty, null);
                _thumb.X = target;
            }
            _thumb = thumb;
            if (thumb is null) return;
            if (!animate || _disposed || !toggle.IsLoaded || !toggle.IsVisible || !toggle.IsEnabled || !OperatorMotion.Enabled)
            {
                thumb.BeginAnimation(TranslateTransform.XProperty, null);
                thumb.X = target;
                return;
            }
            double start = thumb.X, direction = target >= start ? 1 : -1;
            var frames = new DoubleAnimationUsingKeyFrames { Duration = OperatorMotion.Toggle };
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(start, KeyTime.FromPercent(0)));
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(target + direction, KeyTime.FromPercent(.55), OperatorMotion.SmoothOut));
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(target - direction * .35, KeyTime.FromPercent(.8), OperatorMotion.SmoothOut));
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(target, KeyTime.FromPercent(1), OperatorMotion.SmoothOut));
            thumb.BeginAnimation(TranslateTransform.XProperty, frames, HandoffBehavior.SnapshotAndReplace);
        }

        private void Opened(object? sender, EventArgs args)
        {
            if (!_disposed && sender is ComboBox combo) TrackPopup(combo, true);
        }

        private void TrackPopup(ComboBox combo, bool animate)
        {
            SettlePopup();
            if (Part<Popup>(combo, "PART_Popup")?.Child is not FrameworkElement panel) return;
            _popupSurface = panel;
            _popupOpacity = panel.Opacity;
            _original = panel.RenderTransform;
            var shift = new TranslateTransform();
            _popupShift = shift;
            if (_original is null || _original.Value.IsIdentity) _installedPopupTransform = shift;
            else
            {
                // Own the local arrival, preserving an authored base transform.
                var original = _original;
                var group = new TransformGroup();
                group.Children.Add(original);
                group.Children.Add(shift);
                _installedPopupTransform = group;
            }
            panel.RenderTransform = _installedPopupTransform;
            if (!animate || !combo.IsLoaded || !combo.IsVisible || !combo.IsEnabled || !OperatorMotion.Enabled) return;
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, 0, OperatorMotion.SurfaceOpen) { FillBehavior = FillBehavior.Stop, EasingFunction = OperatorMotion.SmoothOut });
            panel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.4, _popupOpacity, OperatorMotion.SurfaceOpen) { FillBehavior = FillBehavior.Stop });
        }

        private void Closed(object? sender, EventArgs args) => SettlePopup();

        private void SettlePopup()
        {
            if (_popupShift is not null)
            {
                _popupShift.BeginAnimation(TranslateTransform.YProperty, null);
                _popupShift.Y = 0;
            }
            if (_popupSurface is not null)
            {
                _popupSurface.BeginAnimation(UIElement.OpacityProperty, null);
                _popupSurface.Opacity = _popupOpacity;
                if (ReferenceEquals(_popupSurface.RenderTransform, _installedPopupTransform))
                    _popupSurface.RenderTransform = _original ?? Transform.Identity;
            }
            _popupSurface = null;
            _popupShift = null;
            _original = null;
            _installedPopupTransform = null;
        }

        internal void Dispose()
        {
            _disposed = true;
            OperatorAppearance.Changed -= PreferenceChanged;
            _watchingPreferences = false;
            SetThumb(false);
            _thumb = null;
            SettlePopup();
            _control.Loaded -= Loaded;
            _control.Unloaded -= Unloaded;
            _control.IsVisibleChanged -= AvailabilityChanged;
            _control.IsEnabledChanged -= AvailabilityChanged;
            if (_control is CheckBox toggle)
            {
                toggle.Checked -= Checked;
                toggle.Unchecked -= Checked;
                toggle.Indeterminate -= Checked;
            }
            if (_control is ComboBox combo)
            {
                combo.DropDownOpened -= Opened;
                combo.DropDownClosed -= Closed;
            }
        }
    }
}

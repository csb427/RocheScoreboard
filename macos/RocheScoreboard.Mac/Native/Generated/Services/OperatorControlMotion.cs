// Generated from Services/OperatorControlMotion.cs; edit translator/adapters.
// Source SHA-256: e7c20a0fb28fee1593d28f2e73cfd5486273cc90e0d062205fd8f0d393804662
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System.Runtime.CompilerServices;
using Avalonia.Input;
using TemplatedControl = Avalonia.Controls.Primitives.TemplatedControl;
using ComboBox = Avalonia.Controls.ComboBox;

namespace Roche_Scoreboard.Services;

/// <summary>One feedback owner per new control; only template pixels move, never hit targets.</summary>
public static class OperatorControlMotion
{
    public static readonly AttachedProperty<bool> EnabledProperty = (AttachedProperty<bool>)NativeProperty.RegisterAttached("Enabled", typeof(bool), typeof(OperatorControlMotion), new PropertyMetadata(false, Changed));
    public static bool GetEnabled(AvaloniaObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(AvaloniaObject target, bool value) => target.SetValue(EnabledProperty, value);
    private static readonly ConditionalWeakTable<TemplatedControl, State> States = new();
    private static void Changed(AvaloniaObject target, AvaloniaPropertyChangedEventArgs args)
    {
        if (target is not TemplatedControl control) return;
        if (args.NewValue is true) States.GetValue(control, c => new State(c));
        else if (States.TryGetValue(control, out var state)) { state.Dispose(); States.Remove(control); }
    }
    private sealed class State
    {
        private readonly TemplatedControl _control;
        private bool _pressed, _running;
        private double _scale = 1, _velocity, _target = 1, _last;
        private ScaleTransform? _transform;
        private Control? _highlight;
        private double _highlightTarget;
        private TranslateTransform? _thumb;
        private Avalonia.Controls.Shapes.Path? _check;
        private double _thumbX, _thumbVelocity, _thumbTarget;
        private double _stretch = 1, _stretchVelocity, _stretchTarget = 1;
        private readonly NativePropertyDescriptor _templateDescriptor;
        private bool _watchingTemplate, _templateQueued, _disposed;
        private Control? _popupSurface;
        private readonly TranslateTransform _popupShift = new();
        internal State(TemplatedControl control)
        {
            _control = control;
            _templateDescriptor = NativePropertyDescriptor.FromProperty(TemplatedControl.TemplateProperty, typeof(TemplatedControl));
            control.Loaded += Loaded; control.Unloaded += Unloaded;
            control.PointerEntered += Refresh; control.PointerExited += Leave;
            control.PointerPressed += Down; control.PointerReleased += Up;
            control.PointerCaptureLost += LostCapture; control.KeyDown += KeyDown; control.KeyUp += KeyUp;
            control.LostFocus += LostFocus;
            NativeEvents.WatchProperty(control,Avalonia.Controls.Control.IsEnabledProperty,true,Availability); NativeEvents.WatchProperty(control,Avalonia.Controls.Control.IsVisibleProperty,true,Availability);
            control.AddHandler(ToggleButton.CheckedEvent, new EventHandler<RoutedEventArgs>(Toggle));
            control.AddHandler(ToggleButton.UncheckedEvent, new EventHandler<RoutedEventArgs>(Toggle));
            if(control is ComboBox choice) { choice.DropDownOpened += OpenChoice; choice.DropDownClosed += CloseChoice; }
            if (control.IsLoaded) Loaded(control, new RoutedEventArgs());
        }
        private void Loaded(object? sender, RoutedEventArgs e)
        {
            if(_disposed || !GetEnabled(_control))return;
            OperatorAppearance.Changed-=AppearanceChanged;OperatorAppearance.Changed+=AppearanceChanged;
            if (!_watchingTemplate) { _templateDescriptor.AddValueChanged(_control, Retemplate); _watchingTemplate = true; }
            _control.ApplyTemplate();
            _transform = NativeTemplates.FindObject(_control,"TbSc") as ScaleTransform;
            _highlight = NativeTemplates.FindObject(_control,"ActionGlow") as Control;
            _thumb = NativeTemplates.FindObject(_control,"Thumb") as TranslateTransform;
            _check = NativeTemplates.FindObject(_control,"PART_CheckPath") as Avalonia.Controls.Shapes.Path;
            _thumbX = _thumbTarget = _control is ToggleButton { IsChecked: true } ? 20 : 0;
            if (_thumb is not null) _thumb.X = _thumbX;
            if (_check is not null) { _check.BeginAnimation(Avalonia.Controls.Shapes.Shape.StrokeDashOffsetProperty,null); _check.StrokeDashOffset = _thumbTarget > 0 ? 0 : 22; }
            Update();
        }
        private void Retemplate(object? sender, EventArgs e)
        {
            if (_templateQueued) return;
            _templateQueued = true;
            Dispatcher.UIThread.InvokeAsync(new Action(() => { _templateQueued = false; if (!_disposed && GetEnabled(_control) && _control.IsLoaded) Loaded(_control, new RoutedEventArgs()); }), Avalonia.Threading.DispatcherPriority.Loaded);
        }
        private void Refresh(object? sender, RoutedEventArgs e) => Update();
        private void Leave(object? sender, Avalonia.Input.PointerEventArgs e) { _pressed = false; Update(); }
        private void Down(object? sender, PointerEventArgs e)
        {
            if(_control is Avalonia.Controls.TabItem)
            {var point=e.GetPosition(_control);if(point.X<0 || point.Y<0 || point.X>_control.NativeWidth() || point.Y>_control.NativeHeight())return;}
            _pressed = true; Update();
        }
        private void Up(object? sender, PointerEventArgs e) { _pressed = false; Update(); }
        private void LostCapture(object? sender, PointerCaptureLostEventArgs e) { _pressed = false; Update(); }
        private void KeyDown(object? sender, Avalonia.Input.KeyEventArgs e) { if(_control is Avalonia.Controls.TabItem && !_control.IsFocused)return; if (e.Key is Key.Space or Key.Enter) { _pressed = true; Update(); } }
        private void KeyUp(object? sender, Avalonia.Input.KeyEventArgs e) { _pressed = false; Update(); }
        private void LostFocus(object? sender, RoutedEventArgs e) { _pressed = false; Update(); }
        private void OpenChoice(object? sender, EventArgs e)
        {
            if(NativeTemplates.FindObject(_control,"PART_Popup") is not Popup { Child: Control surface })return;
            _popupSurface=surface; surface.RenderTransform=_popupShift;
            _popupShift.BeginAnimation(TranslateTransform.YProperty,null);surface.BeginAnimation(Control.OpacityProperty,null);
            _popupShift.Y=0;surface.Opacity=1;
            if(!OperatorMotion.Enabled)return;
            _popupShift.BeginAnimation(TranslateTransform.YProperty,new RocheScoreboard.Mac.Native.DoubleAnimation(-5,0,OperatorMotion.SurfaceOpen){EasingFunction=OperatorMotion.SmoothOut,FillBehavior=RocheScoreboard.Mac.Native.FillBehavior.Stop});
            surface.BeginAnimation(Control.OpacityProperty,new RocheScoreboard.Mac.Native.DoubleAnimation(.72,1,OperatorMotion.SurfaceOpen){FillBehavior=RocheScoreboard.Mac.Native.FillBehavior.Stop});
        }
        private void CloseChoice(object? sender,EventArgs e)
        {
            _popupShift.BeginAnimation(TranslateTransform.YProperty,null);_popupShift.Y=0;
            if(_popupSurface is not null){_popupSurface.BeginAnimation(Control.OpacityProperty,null);_popupSurface.Opacity=1;}_popupSurface=null;
        }
        private void Toggle(object? sender, RoutedEventArgs e)
        {
            _thumbTarget = _control is ToggleButton { IsChecked: true } ? 20 : 0;
            if (_check is not null)
            {
                double to = _thumbTarget > 0 ? 0 : 22;
                if (OperatorMotion.Enabled && _control.IsLoaded)
                    _check.BeginAnimation(Avalonia.Controls.Shapes.Shape.StrokeDashOffsetProperty, new RocheScoreboard.Mac.Native.DoubleAnimation(to, TimeSpan.FromMilliseconds(220)) { EasingFunction = OperatorMotion.SmoothOut });
                else { _check.BeginAnimation(Avalonia.Controls.Shapes.Shape.StrokeDashOffsetProperty, null); _check.StrokeDashOffset = to; }
            }
            Update();
        }
        private void Availability(object? sender, AvaloniaPropertyChangedEventArgs e) { if (!_control.IsEnabled || !_control.IsVisible) Reset(); else Update(); }
        private void Unloaded(object? sender, RoutedEventArgs e) { OperatorAppearance.Changed-=AppearanceChanged;Reset(); if (_watchingTemplate) { _templateDescriptor.RemoveValueChanged(_control, Retemplate); _watchingTemplate = false; } }
        private void AppearanceChanged()=>Update();
        private void Update()
        {
            if (!_control.IsEnabled || !_control.IsVisible) { Reset(); return; }
            _target = _pressed ? .945 : _control.IsPointerOver ? (_thumb is not null ? 1.075 : 1 + Math.Min(.018, 4 / Math.Max(1, _control.NativeWidth()))) : 1;
            _stretchTarget = _thumb is not null && _pressed ? 1.18 : 1;
            double highlight=_pressed?.075:_control.IsPointerOver?.035:0;
            if(_highlight is not null && Math.Abs(highlight-_highlightTarget)>.001)
            {
                _highlightTarget=highlight;
                if(!OperatorMotion.Enabled){_highlight.BeginAnimation(Control.OpacityProperty,null);_highlight.Opacity=highlight;}
                else _highlight.BeginAnimation(Control.OpacityProperty,new RocheScoreboard.Mac.Native.DoubleAnimation(highlight,TimeSpan.FromMilliseconds(110)){EasingFunction=OperatorMotion.SmoothOut});
            }
            if (_transform is null && _thumb is null) return;
            if (!OperatorMotion.Enabled) { _scale = _target = _stretch = _stretchTarget = 1; _velocity = _stretchVelocity = 0; _thumbX = _thumbTarget; Paint(); Stop(); return; }
            if (_running) return;
            // A loaded or re-entered idle control does not need a rendering loop.
            if(Math.Abs(_target-_scale)<.0002 && Math.Abs(_velocity)<.002 && Math.Abs(_thumbTarget-_thumbX)<.02 && Math.Abs(_thumbVelocity)<.05 && Math.Abs(_stretchTarget-_stretch)<.0002 && Math.Abs(_stretchVelocity)<.002)
            {Paint();return;}
            _last = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency; _running = true; NativeRendering.Rendering += Frame;
        }
        private void Frame(object? sender, EventArgs e)
        {
            double now = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
            double dt = Math.Max(0, now - _last); _last = now;
            Advance(ref _scale, ref _velocity, _target, 27, dt);
            Advance(ref _thumbX, ref _thumbVelocity, _thumbTarget, 26, dt);
            Advance(ref _stretch, ref _stretchVelocity, _stretchTarget, 29, dt);
            Paint();
            if (Math.Abs(_target - _scale) < .0002 && Math.Abs(_velocity) < .002 && Math.Abs(_thumbTarget - _thumbX) < .02 && Math.Abs(_thumbVelocity) < .05 && Math.Abs(_stretchTarget - _stretch) < .0002 && Math.Abs(_stretchVelocity) < .002)
            { _scale = _target; _thumbX = _thumbTarget; _stretch = _stretchTarget; Paint(); Stop(); }
        }
        private static void Advance(ref double value, ref double velocity, double target, double omega, double dt)
        {
            double offset = value - target, coefficient = velocity + omega * offset;
            double decay = Math.Exp(-omega * dt);
            value = target + (offset + coefficient * dt) * decay;
            velocity = (velocity - omega * coefficient * dt) * decay;
        }
        private void Paint()
        {
            if (_transform is not null) { _transform.ScaleX = _scale * _stretch; _transform.ScaleY = _scale * (1 - (_stretch - 1) * .35); }
            if (_thumb is not null) _thumb.X = _thumbX + (1 - Math.Clamp(_thumbX / 10, 0, 2)) * 12 * (_stretch - 1);
        }
        private void Stop() { if (!_running) return; NativeRendering.Rendering -= Frame; _running = false; }
        private void Reset() { Stop(); CloseChoice(null,EventArgs.Empty); _pressed = false; _scale = _target = _stretch = _stretchTarget = 1; _velocity = _thumbVelocity = _stretchVelocity = 0; _thumbX = _thumbTarget; if (_highlight is not null) {_highlight.BeginAnimation(Control.OpacityProperty,null);_highlight.Opacity=0;_highlightTarget=0;} if (_check is not null) { _check.BeginAnimation(Avalonia.Controls.Shapes.Shape.StrokeDashOffsetProperty, null); _check.StrokeDashOffset = _thumbTarget > 0 ? 0 : 22; } Paint(); }
        internal void Dispose()
        {
            _disposed = true;
            OperatorAppearance.Changed-=AppearanceChanged;
            Reset(); var c = _control;
            if (_watchingTemplate) { _templateDescriptor.RemoveValueChanged(_control, Retemplate); _watchingTemplate = false; }
            c.Loaded -= Loaded; c.Unloaded -= Unloaded; c.PointerEntered -= Refresh; c.PointerExited -= Leave;
            c.PointerPressed -= Down; c.PointerReleased -= Up; c.PointerCaptureLost -= LostCapture;
            c.KeyDown -= KeyDown; c.KeyUp -= KeyUp; NativeEvents.WatchProperty(c,Avalonia.Controls.Control.IsEnabledProperty,false,Availability); NativeEvents.WatchProperty(c,Avalonia.Controls.Control.IsVisibleProperty,false,Availability);
            c.LostFocus -= LostFocus;
            c.RemoveHandler(ToggleButton.CheckedEvent, new EventHandler<RoutedEventArgs>(Toggle)); c.RemoveHandler(ToggleButton.UncheckedEvent, new EventHandler<RoutedEventArgs>(Toggle));
            if(c is ComboBox choice){choice.DropDownOpened-=OpenChoice;choice.DropDownClosed-=CloseChoice;}
        }
    }
}

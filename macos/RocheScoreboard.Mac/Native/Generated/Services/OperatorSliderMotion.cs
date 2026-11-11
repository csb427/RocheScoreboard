// Generated from Services/OperatorSliderMotion.cs; edit translator/adapters.
// Source SHA-256: 35e89fac92463708179761bedd9518f891c43fc1204a7400fcb03d9c82c7ce25
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System.Runtime.CompilerServices;
using Slider = Avalonia.Controls.Slider;

namespace Roche_Scoreboard.Services;

/// <summary>Deform only a range control's grip; native tracking and keyboard input stay unchanged.</summary>
public static class OperatorSliderMotion
{
    public static readonly AttachedProperty<bool> EnabledProperty = (AttachedProperty<bool>)NativeProperty.RegisterAttached("Enabled", typeof(bool), typeof(OperatorSliderMotion), new PropertyMetadata(false, Changed));
    public static void SetEnabled(AvaloniaObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static bool GetEnabled(AvaloniaObject target) => (bool)target.GetValue(EnabledProperty);
    private static readonly ConditionalWeakTable<Slider, State> States = new();
    private static void Changed(AvaloniaObject target, AvaloniaPropertyChangedEventArgs args)
    {
        if (target is not Slider slider) return;
        if (args.NewValue is true) States.GetValue(slider, value => new State(value));
        else if (States.TryGetValue(slider, out var state)) { state.Dispose(); States.Remove(slider); }
    }
    private sealed class State
    {
        private readonly Slider _slider;
        private Thumb? _thumb;
        private ScaleTransform? _grip;
        private bool _dragging;
        private bool _running;
        private double _scaleX = 1, _velocityX, _targetX = 1;
        private double _scaleY = 1, _velocityY, _targetY = 1, _last;
        private bool _watchingTemplate, _templateQueued, _disposed;
        private readonly NativePropertyDescriptor _templateDescriptor;
        internal State(Slider slider)
        {
            _slider = slider; _templateDescriptor=NativePropertyDescriptor.FromProperty(Avalonia.Controls.Primitives.TemplatedControl.TemplateProperty,typeof(Slider));
            slider.Loaded += Loaded; slider.Unloaded += Unloaded;
            NativeEvents.WatchProperty(slider,Avalonia.Controls.Control.IsEnabledProperty,true,Availability); NativeEvents.WatchProperty(slider,Avalonia.Controls.Control.IsVisibleProperty,true,Availability);
            if (slider.IsLoaded) Loaded(slider, new RoutedEventArgs());
        }
        private void Loaded(object? sender, RoutedEventArgs args)
        {
            if(_disposed)return;
            if(!_watchingTemplate){_templateDescriptor.AddValueChanged(_slider,Retemplate);_watchingTemplate=true;}
            DetachThumb(); _slider.ApplyTemplate();
            _thumb = (NativeTemplates.FindObject(_slider,"PART_Track") as Track)?.Thumb;
            _thumb?.ApplyTemplate(); _grip = NativeTemplates.FindObject(_thumb,"PART_GripScale") as ScaleTransform;
            if (_thumb is null) return;
            _thumb.PointerEntered += Hover; _thumb.PointerExited += Hover; _thumb.DragStarted += DragStarted; _thumb.DragCompleted += DragCompleted;
            OperatorAppearance.Changed += AppearanceChanged;
        }
        private void Retemplate(object? sender,EventArgs args)
        {
            if(_templateQueued)return;_templateQueued=true;
            Dispatcher.UIThread.InvokeAsync(new Action(()=>{_templateQueued=false;if(!_disposed && _slider.IsLoaded && GetEnabled(_slider))Loaded(_slider,new RoutedEventArgs());}),Avalonia.Threading.DispatcherPriority.Loaded);
        }
        private void Hover(object? sender, Avalonia.Input.PointerEventArgs args) => Update();
        private void DragStarted(object? sender, Avalonia.Input.VectorEventArgs args) { _dragging = true; Update(); }
        private void DragCompleted(object? sender, Avalonia.Input.VectorEventArgs args) { _dragging = false; Update(); }
        private void Availability(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (_slider.IsEnabled && _slider.IsVisible) return;
            // A hidden/disabled slider can lose native thumb capture without a
            // DragCompleted event. Do not carry a stale pressed presentation
            // into its next visible interaction.
            _dragging = false;
            Reset();
        }
        private void AppearanceChanged() { if (!OperatorMotion.Enabled) Reset(); }
        private void Update()
        {
            if (_grip is null) return;
            _targetX = _dragging ? 1.22 : _thumb?.IsPointerOver == true ? 1.08 : 1;
            _targetY = _dragging ? .94 : _thumb?.IsPointerOver == true ? 1.08 : 1;
            if (_slider.Orientation == Avalonia.Layout.Orientation.Vertical) (_targetX, _targetY) = (_targetY, _targetX);
            if (!OperatorMotion.Enabled) { Reset(); return; }
            if (_running) return; // Retarget from the current grip pose and velocity.
            if (Math.Abs(_targetX - _scaleX) < .0002 && Math.Abs(_velocityX) < .002
                && Math.Abs(_targetY - _scaleY) < .0002 && Math.Abs(_velocityY) < .002)
            {
                _scaleX = _targetX; _scaleY = _targetY; Paint(); return;
            }
            _last = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
            _running = true; NativeRendering.Rendering += Frame;
        }
        private void Frame(object? sender, EventArgs args)
        {
            if (_grip is null || !_slider.IsLoaded || !_slider.IsVisible || !_slider.IsEnabled || !OperatorMotion.Enabled)
            { Reset(); return; }
            double now = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
            double elapsed = Math.Max(0, now - _last); _last = now;
            double omega = 42 * OperatorMotion.SpringResponse;
            OperatorMotion.AdvanceCriticallyDamped(ref _scaleX, ref _velocityX, _targetX, omega, elapsed);
            OperatorMotion.AdvanceCriticallyDamped(ref _scaleY, ref _velocityY, _targetY, omega, elapsed);
            Paint();
            if (Math.Abs(_targetX - _scaleX) < .0002 && Math.Abs(_velocityX) < .002
                && Math.Abs(_targetY - _scaleY) < .0002 && Math.Abs(_velocityY) < .002)
            {
                _scaleX = _targetX; _scaleY = _targetY; _velocityX = _velocityY = 0; Paint(); Stop();
            }
        }
        private void Reset()
        {
            Stop(); _scaleX = _scaleY = _targetX = _targetY = 1; _velocityX = _velocityY = 0; Paint();
        }
        private void Paint() { if (_grip is not null) { _grip.ScaleX = _scaleX; _grip.ScaleY = _scaleY; } }
        private void Stop() { if (!_running) return; NativeRendering.Rendering -= Frame; _running = false; }
        private void Unloaded(object? sender, RoutedEventArgs args)
        { DetachThumb();if(_watchingTemplate){_templateDescriptor.RemoveValueChanged(_slider,Retemplate);_watchingTemplate=false;} }
        private void DetachThumb()
        {
            OperatorAppearance.Changed -= AppearanceChanged; Reset();
            if (_thumb is not null) { _thumb.PointerEntered -= Hover; _thumb.PointerExited -= Hover; _thumb.DragStarted -= DragStarted; _thumb.DragCompleted -= DragCompleted; }
            _thumb = null; _grip = null; _dragging = false;
        }
        internal void Dispose()
        { _disposed=true;Unloaded(_slider,new RoutedEventArgs()); _slider.Loaded -= Loaded; _slider.Unloaded -= Unloaded; NativeEvents.WatchProperty(_slider,Avalonia.Controls.Control.IsEnabledProperty,false,Availability); NativeEvents.WatchProperty(_slider,Avalonia.Controls.Control.IsVisibleProperty,false,Availability); }
    }
}

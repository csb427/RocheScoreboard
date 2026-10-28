// Generated from Services/OperatorSliderMotion.cs; edit translator/adapters.
// Source SHA-256: 5212e58cea72f077080b83abf5e363dce33da7496981a3186103fda58511209a
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
        private int _generation;
        private bool _dragging;
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
        private void Availability(object? sender, AvaloniaPropertyChangedEventArgs args) { if (!_slider.IsEnabled || !_slider.IsVisible) Reset(); }
        private void AppearanceChanged() { if (!OperatorMotion.Enabled) Reset(); }
        private void Update()
        {
            if (_grip is null) return;
            int generation = ++_generation;
            double x = _dragging ? 1.22 : _thumb?.IsPointerOver == true ? 1.08 : 1;
            double y = _dragging ? .94 : _thumb?.IsPointerOver == true ? 1.08 : 1;
            if (_slider.Orientation == Avalonia.Layout.Orientation.Vertical) (x, y) = (y, x);
            if (!OperatorMotion.Enabled) { Reset(); return; }
            void Animate(AvaloniaProperty property, double value)
            {
                var grip = _grip;
                var animation = new DoubleAnimation(value, TimeSpan.FromMilliseconds(170)) { EasingFunction = OperatorMotion.SmoothOut };
                animation.Completed += (_, _) => { if (generation != _generation) return; grip.BeginAnimation(property, null); grip.SetValue(property, value); };
                grip.BeginAnimation(property, animation);
            }
            Animate(ScaleTransform.ScaleXProperty, x); Animate(ScaleTransform.ScaleYProperty, y);
        }
        private void Reset()
        {
            ++_generation; if (_grip is null) return;
            _grip.BeginAnimation(ScaleTransform.ScaleXProperty, null); _grip.BeginAnimation(ScaleTransform.ScaleYProperty, null); _grip.ScaleX = _grip.ScaleY = 1;
        }
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

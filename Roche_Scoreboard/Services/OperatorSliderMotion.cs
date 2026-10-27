using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Slider = System.Windows.Controls.Slider;

namespace Roche_Scoreboard.Services;

/// <summary>Deform only a range control's grip; native tracking and keyboard input stay unchanged.</summary>
public static class OperatorSliderMotion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(OperatorSliderMotion), new PropertyMetadata(false, Changed));
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    private static readonly ConditionalWeakTable<Slider, State> States = new();
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
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
        private readonly System.ComponentModel.DependencyPropertyDescriptor _templateDescriptor;
        internal State(Slider slider)
        {
            _slider = slider; _templateDescriptor=System.ComponentModel.DependencyPropertyDescriptor.FromProperty(System.Windows.Controls.Control.TemplateProperty,typeof(Slider));
            slider.Loaded += Loaded; slider.Unloaded += Unloaded;
            slider.IsEnabledChanged += Availability; slider.IsVisibleChanged += Availability;
            if (slider.IsLoaded) Loaded(slider, new RoutedEventArgs());
        }
        private void Loaded(object sender, RoutedEventArgs args)
        {
            if(_disposed)return;
            if(!_watchingTemplate){_templateDescriptor.AddValueChanged(_slider,Retemplate);_watchingTemplate=true;}
            DetachThumb(); _slider.ApplyTemplate();
            _thumb = (_slider.Template.FindName("PART_Track", _slider) as Track)?.Thumb;
            _thumb?.ApplyTemplate(); _grip = _thumb?.Template.FindName("PART_GripScale", _thumb) as ScaleTransform;
            if (_thumb is null) return;
            _thumb.MouseEnter += Hover; _thumb.MouseLeave += Hover; _thumb.DragStarted += DragStarted; _thumb.DragCompleted += DragCompleted;
            OperatorAppearance.Changed += AppearanceChanged;
        }
        private void Retemplate(object? sender,EventArgs args)
        {
            if(_templateQueued)return;_templateQueued=true;
            _slider.Dispatcher.BeginInvoke(new Action(()=>{_templateQueued=false;if(!_disposed && _slider.IsLoaded && GetEnabled(_slider))Loaded(_slider,new RoutedEventArgs());}),System.Windows.Threading.DispatcherPriority.Loaded);
        }
        private void Hover(object sender, System.Windows.Input.MouseEventArgs args) => Update();
        private void DragStarted(object sender, DragStartedEventArgs args) { _dragging = true; Update(); }
        private void DragCompleted(object sender, DragCompletedEventArgs args) { _dragging = false; Update(); }
        private void Availability(object sender, DependencyPropertyChangedEventArgs args) { if (!_slider.IsEnabled || !_slider.IsVisible) Reset(); }
        private void AppearanceChanged() { if (!OperatorMotion.Enabled) Reset(); }
        private void Update()
        {
            if (_grip is null) return;
            int generation = ++_generation;
            double x = _dragging ? 1.22 : _thumb?.IsMouseOver == true ? 1.08 : 1;
            double y = _dragging ? .94 : _thumb?.IsMouseOver == true ? 1.08 : 1;
            if (_slider.Orientation == System.Windows.Controls.Orientation.Vertical) (x, y) = (y, x);
            if (!OperatorMotion.Enabled) { Reset(); return; }
            void Animate(DependencyProperty property, double value)
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
        private void Unloaded(object sender, RoutedEventArgs args)
        { DetachThumb();if(_watchingTemplate){_templateDescriptor.RemoveValueChanged(_slider,Retemplate);_watchingTemplate=false;} }
        private void DetachThumb()
        {
            OperatorAppearance.Changed -= AppearanceChanged; Reset();
            if (_thumb is not null) { _thumb.MouseEnter -= Hover; _thumb.MouseLeave -= Hover; _thumb.DragStarted -= DragStarted; _thumb.DragCompleted -= DragCompleted; }
            _thumb = null; _grip = null; _dragging = false;
        }
        internal void Dispose()
        { _disposed=true;Unloaded(_slider,new RoutedEventArgs()); _slider.Loaded -= Loaded; _slider.Unloaded -= Unloaded; _slider.IsEnabledChanged -= Availability; _slider.IsVisibleChanged -= Availability; }
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TabControl = System.Windows.Controls.TabControl;

namespace Roche_Scoreboard.Services;

/// <summary>Two independently damped edges form an interruptible liquid selection lens.</summary>
public sealed class OperatorSelectionLens : FrameworkElement
{
    private TabControl? _tabs;
    private double _left, _right, _leftV, _rightV, _targetLeft, _targetRight, _last;
    private double _surfaceWidth,_surfaceWidthVelocity;
    private bool _running, _ready;
    public OperatorSelectionLens()
    {
        IsHitTestVisible = false;
        SetResourceReference(OperatorLiquidSelection.ContextProperty,"ControlCentre.CanvasBrush");
        Loaded += (_, _) => Attach(); Unloaded += (_, _) => Detach();
        SizeChanged += (_, _) => Update(false);
        IsVisibleChanged += (_, _) => { if (!IsVisible) Stop(); else Update(false); };
    }
    private void Attach()
    {
        DependencyObject? current = this;
        while (current is not null && current is not TabControl) current = VisualTreeHelper.GetParent(current);
        if (current is not TabControl tabs) return;
        if (!ReferenceEquals(_tabs, tabs)) { Detach(); _tabs = tabs; tabs.SelectionChanged += Selected; tabs.ItemContainerGenerator.StatusChanged += Generated; tabs.LayoutUpdated += LayoutChanged; }
        OperatorAppearance.Changed-=AppearanceChanged;OperatorAppearance.Changed+=AppearanceChanged;
        Dispatcher.BeginInvoke(new Action(() => Update(false)), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private void Selected(object sender, SelectionChangedEventArgs e) { if (ReferenceEquals(e.OriginalSource, _tabs)) Update(true); }
    private void Generated(object? sender, EventArgs e) {if(!_ready)Update(false);}
    private void LayoutChanged(object? sender, EventArgs e)
    {
        if (SelectedContainer() is not {ActualWidth:>0} selected) {Clear();return;}
        double x = selected.TranslatePoint(new System.Windows.Point(), this).X;
        if (!_ready || Math.Abs(x + 2 - _targetLeft) > .1 || Math.Abs(x + selected.ActualWidth - 2 - _targetRight) > .1) Update(_running);
    }
    internal bool IsAnimating => _running;
    internal Rect LensBounds => VisualShape.Bounds;
    internal OperatorLiquidSelection.Shape VisualShape => _ready?OperatorLiquidSelection.Calculate(RenderSize,_left,_right,_surfaceWidth,_leftV,_rightV,2,1.7,_running && OperatorMotion.Enabled):new(Rect.Empty,0,0);
    private TabItem? SelectedContainer()=>_tabs?.SelectedItem is not { } item?null:item as TabItem ?? _tabs.ItemContainerGenerator.ContainerFromItem(item) as TabItem;
    private void Clear(){if(!_ready && !_running)return;Stop();_ready=false;_leftV=_rightV=0;InvalidateVisual();}
    private void Update(bool animate)
    {
        if (_tabs is null || !IsVisible || ActualWidth < 1) return;
        var selected = SelectedContainer();
        if (selected is null || selected.ActualWidth < 1) {Clear();return;}
        var point = selected.TranslatePoint(new System.Windows.Point(), this);
        _targetLeft = Math.Clamp(point.X + 2, 0, ActualWidth);
        _targetRight = Math.Clamp(point.X + selected.ActualWidth - 2, _targetLeft, ActualWidth);
        if (!animate || !_ready || !OperatorMotion.Enabled)
        { Stop(); _left = _targetLeft; _right = _targetRight; _surfaceWidth=_targetRight-_targetLeft; _surfaceWidthVelocity=_leftV=_rightV=0; _ready = true; InvalidateVisual(); return; }
        if (_running) return;
        _last = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency; _running = true; CompositionTarget.Rendering += Frame;
    }
    private void Frame(object? sender, EventArgs args)
    {
        double now = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
        double dt = Math.Max(0, now - _last); _last = now;
        bool rightward = (_targetLeft + _targetRight) > (_left + _right);
        Step(ref _left, ref _leftV, _targetLeft, rightward ? 19 : 32, dt);
        Step(ref _right, ref _rightV, _targetRight, rightward ? 32 : 19, dt);
        Step(ref _surfaceWidth,ref _surfaceWidthVelocity,_targetRight-_targetLeft,28,dt);
        InvalidateVisual();
        if (Math.Abs(_left - _targetLeft) + Math.Abs(_right - _targetRight) < .18 && Math.Abs(_surfaceWidth-(_targetRight-_targetLeft)) < .08 && Math.Abs(_leftV) + Math.Abs(_rightV) < 4 && Math.Abs(_surfaceWidthVelocity) < 2)
        { _left = _targetLeft; _right = _targetRight; _surfaceWidth=_targetRight-_targetLeft; _leftV=_rightV=_surfaceWidthVelocity=0; InvalidateVisual(); Stop(); }
    }
    private static void Step(ref double value, ref double velocity, double target, double omega, double dt)
    {
        double offset = value - target, coefficient = velocity + omega * offset, decay = Math.Exp(-omega * dt);
        value = target + (offset + coefficient * dt) * decay;
        velocity = (velocity - omega * coefficient * dt) * decay;
    }
    private void Stop() { if (!_running) return; CompositionTarget.Rendering -= Frame; _running = false; }
    private void AppearanceChanged(){if(!OperatorMotion.Enabled)Update(false);InvalidateVisual();}
    private void Detach() { OperatorAppearance.Changed-=AppearanceChanged;Stop(); if (_tabs is not null) { _tabs.SelectionChanged -= Selected; _tabs.ItemContainerGenerator.StatusChanged -= Generated; _tabs.LayoutUpdated -= LayoutChanged; } _tabs = null; _ready = false; }
    protected override void OnRender(DrawingContext drawing)
    {
        if (!_ready || ActualHeight < 2) return;
        OperatorLiquidSelection.Draw(drawing,this,VisualShape,false);
    }
}

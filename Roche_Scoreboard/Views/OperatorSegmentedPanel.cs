using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Roche_Scoreboard.Services;
using RadioButton = System.Windows.Controls.RadioButton;
using Control = System.Windows.Controls.Control;

namespace Roche_Scoreboard.Views;

/// <summary>One continuous selection body under stationary native radio-button labels.</summary>
public sealed partial class OperatorSegmentedPanel : UniformGrid
{
    private double _left, _right, _leftVelocity, _rightVelocity, _targetLeft, _targetRight, _last;
    private double _surfaceWidth,_surfaceWidthVelocity;
    private bool _ready, _running;
    private readonly Dictionary<RadioButton,bool> _inkCoverage = new();
    public OperatorSegmentedPanel()
    {
        InstallScrubbing();
        SetResourceReference(OperatorLiquidSelection.ContextProperty,"ControlCentre.CanvasBrush");
        AddHandler(ToggleButton.CheckedEvent,new RoutedEventHandler((_,_)=>Update(true)));
        AddHandler(ToggleButton.UncheckedEvent,new RoutedEventHandler((_,_)=>Dispatcher.BeginInvoke(new Action(()=>{if(IsLoaded && Selected is null)Clear();}),System.Windows.Threading.DispatcherPriority.Loaded)));
        Loaded += (_,_)=>{OperatorAppearance.Changed-=AppearanceChanged;OperatorAppearance.Changed+=AppearanceChanged;Update(false);}; Unloaded += (_,_)=>{OperatorAppearance.Changed-=AppearanceChanged;Stop();_ready=false;_inkCoverage.Clear();};
        SizeChanged += (_,_)=>Update(false);
        IsVisibleChanged += (_,_)=>{if(IsVisible)Update(false);else Stop();};
        LayoutUpdated += (_,_)=>
        {
            if(!IsLoaded || _scrubbing)return;
            foreach(var removed in _inkCoverage.Keys.Where(button=>!Children.Contains(button)).ToArray())
            {removed.ClearValue(Control.ForegroundProperty);_inkCoverage.Remove(removed);}
            if(Selected is not {ActualWidth:>0} selected){Clear();return;}
            double x=selected.TranslatePoint(new System.Windows.Point(),this).X+3;
            if(!_ready || Math.Abs(x-_targetLeft)>.1 || Math.Abs(x+selected.ActualWidth-6-_targetRight)>.1)Update(false);
        };
    }
    private RadioButton? Selected => Children.OfType<RadioButton>().FirstOrDefault(button=>button.IsChecked == true);
    private void AppearanceChanged(){if(!OperatorMotion.Enabled)Update(false);InvalidateVisual();}
    private void Clear()
    {
        if(!_ready && !_running && _inkCoverage.Count==0)return;
        Stop();_ready=false;_leftVelocity=_rightVelocity=0;
        foreach(var button in _inkCoverage.Keys.Concat(Children.OfType<RadioButton>()).Distinct())
        {if(Children.Contains(button))button.SetResourceReference(Control.ForegroundProperty,"ControlCentre.TextBrush");else button.ClearValue(Control.ForegroundProperty);}
        _inkCoverage.Clear();InvalidateVisual();
    }
    private void Update(bool animate)
    {
        if(_scrubbing)return;
        if(!IsLoaded || !IsVisible)return;
        if(Selected is not {ActualWidth: > 0} selected){Clear();return;}
        _targetLeft=selected.TranslatePoint(new System.Windows.Point(),this).X+3;
        _targetRight=_targetLeft+selected.ActualWidth-6;
        if(!animate || !_ready || !OperatorMotion.Enabled)
        {Stop();_left=_targetLeft;_right=_targetRight;_surfaceWidth=_targetRight-_targetLeft;_surfaceWidthVelocity=_leftVelocity=_rightVelocity=0;_ready=true;Paint();return;}
        if(_running)return;
        _running=true;_last=System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency;
        CompositionTarget.Rendering+=Frame;
    }
    private void Frame(object? sender,EventArgs args)
    {
        double now=System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency,dt=Math.Max(0,now-_last);_last=now;
        bool rightward=_targetLeft>_left;
        Step(ref _left,ref _leftVelocity,_targetLeft,rightward?23:32,dt);
        Step(ref _right,ref _rightVelocity,_targetRight,rightward?32:23,dt);
        Step(ref _surfaceWidth,ref _surfaceWidthVelocity,_targetRight-_targetLeft,28,dt);
        if(Math.Abs(_left-_targetLeft)+Math.Abs(_right-_targetRight)+Math.Abs(_surfaceWidth-(_targetRight-_targetLeft))<.06 && Math.Abs(_leftVelocity)+Math.Abs(_rightVelocity)+Math.Abs(_surfaceWidthVelocity)<1.5)
        {_left=_targetLeft;_right=_targetRight;_surfaceWidth=_targetRight-_targetLeft;_leftVelocity=_rightVelocity=_surfaceWidthVelocity=0;Stop();}
        Paint();
    }
    private static void Step(ref double value,ref double velocity,double target,double omega,double dt)
    {omega*=OperatorMotion.SpringResponse;double offset=value-target,c=velocity+omega*offset,e=Math.Exp(-omega*dt);value=target+(offset+c*dt)*e;velocity=(velocity-omega*c*dt)*e;}
    private void Stop(){if(!_running)return;CompositionTarget.Rendering-=Frame;_running=false;}
    private void Paint()
    {
        // Switch ink when the arriving lens actually covers the label, never
        // make a newly selected dark label disappear ahead of the material.
        var bounds=VisualShape.Bounds;
        foreach(var button in Children.OfType<RadioButton>())
        {
            double x=button.TranslatePoint(new System.Windows.Point(),this).X;
            bool covered=!bounds.IsEmpty && x+button.ActualWidth*.5>=bounds.Left && x+button.ActualWidth*.5<=bounds.Right;
            if(!_inkCoverage.TryGetValue(button,out bool previous) || previous!=covered)
            {button.SetResourceReference(Control.ForegroundProperty,covered?"ControlCentre.SegmentSelectedInkBrush":"ControlCentre.TextBrush");_inkCoverage[button]=covered;}
        }
        InvalidateVisual();
    }
    internal bool IsAnimating=>_running;
    internal OperatorLiquidSelection.Shape VisualShape=>_ready?OperatorLiquidSelection.Calculate(RenderSize,_left,_right,_surfaceWidth,_leftVelocity,_rightVelocity,3,1.6,_running && OperatorMotion.Enabled):new(Rect.Empty,0,0);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);if(!_ready || ActualHeight<2)return;
        OperatorLiquidSelection.Draw(dc,this,VisualShape,true);
    }
}

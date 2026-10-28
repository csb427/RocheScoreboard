// Generated from Views/OperatorSegmentedPanel.cs; edit translator/adapters.
// Source SHA-256: eb16380dce6660e9449a634c6aa28b166ab995557b01dc9036dd32871a9ce464
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Roche_Scoreboard.Services;
using RadioButton = Avalonia.Controls.RadioButton;
using TemplatedControl = Avalonia.Controls.Primitives.TemplatedControl;

namespace Roche_Scoreboard.Views;

/// <summary>One continuous selection body under stationary native radio-button labels.</summary>
public sealed class OperatorSegmentedPanel : NativePaintGrid
{
    private double _left, _right, _leftVelocity, _rightVelocity, _targetLeft, _targetRight, _last;
    private double _surfaceWidth,_surfaceWidthVelocity;
    private bool _ready, _running;
    private readonly Dictionary<RadioButton,bool> _inkCoverage = new();
    public OperatorSegmentedPanel()
    {
        this.SetResourceReference(OperatorLiquidSelection.ContextProperty,"ControlCentre.CanvasBrush");
        AddHandler(ToggleButton.CheckedEvent,new EventHandler<RoutedEventArgs>((_,_)=>Update(true)));
        AddHandler(ToggleButton.UncheckedEvent,new EventHandler<RoutedEventArgs>((_,_)=>Dispatcher.UIThread.Post(new Action(()=>{if(IsLoaded && Selected is null)Clear();}),Avalonia.Threading.DispatcherPriority.Loaded)));
        Loaded += (_,_)=>{OperatorAppearance.Changed-=AppearanceChanged;OperatorAppearance.Changed+=AppearanceChanged;Update(false);}; Unloaded += (_,_)=>{OperatorAppearance.Changed-=AppearanceChanged;Stop();_ready=false;_inkCoverage.Clear();};
        SizeChanged += (_,_)=>Update(false);
        NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsVisibleProperty,true,(_,_)=>{if(IsVisible)Update(false);else Stop();});
        LayoutUpdated += (_,_)=>
        {
            if(!IsLoaded)return;
            foreach(var removed in _inkCoverage.Keys.Where(button=>!Children.Contains(button)).ToArray())
            {removed.ClearValue(TextBlock.ForegroundProperty);_inkCoverage.Remove(removed);}
            if(Selected is not {Bounds.Width:>0} selected){Clear();return;}
            double x=selected.NativeTranslatePoint(new Avalonia.Point(),this).X+3;
            if(!_ready || Math.Abs(x-_targetLeft)>.1 || Math.Abs(x+selected.NativeWidth()-6-_targetRight)>.1)Update(false);
        };
    }
    private RadioButton? Selected => Children.OfType<RadioButton>().FirstOrDefault(button=>button.IsChecked == true);
    private void AppearanceChanged(){if(!OperatorMotion.Enabled)Update(false);InvalidateVisual();}
    private void Clear()
    {
        if(!_ready && !_running && _inkCoverage.Count==0)return;
        Stop();_ready=false;_leftVelocity=_rightVelocity=0;
        foreach(var button in _inkCoverage.Keys.Concat(Children.OfType<RadioButton>()).Distinct())
        {if(Children.Contains(button))button.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");else button.ClearValue(TextBlock.ForegroundProperty);}
        _inkCoverage.Clear();InvalidateVisual();
    }
    private void Update(bool animate)
    {
        if(!IsLoaded || !IsVisible)return;
        if(Selected is not {Bounds.Width: > 0} selected){Clear();return;}
        _targetLeft=selected.NativeTranslatePoint(new Avalonia.Point(),this).X+3;
        _targetRight=_targetLeft+selected.NativeWidth()-6;
        if(!animate || !_ready || !OperatorMotion.Enabled)
        {Stop();_left=_targetLeft;_right=_targetRight;_surfaceWidth=_targetRight-_targetLeft;_surfaceWidthVelocity=_leftVelocity=_rightVelocity=0;_ready=true;Paint();return;}
        if(_running)return;
        _running=true;_last=System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency;
        NativeRendering.Rendering+=Frame;
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
    {double offset=value-target,c=velocity+omega*offset,e=Math.Exp(-omega*dt);value=target+(offset+c*dt)*e;velocity=(velocity-omega*c*dt)*e;}
    private void Stop(){if(!_running)return;NativeRendering.Rendering-=Frame;_running=false;}
    private void Paint()
    {
        // Switch ink when the arriving lens actually covers the label, never
        // make a newly selected dark label disappear ahead of the material.
        var bounds=VisualShape.Bounds;
        foreach(var button in Children.OfType<RadioButton>())
        {
            double x=button.NativeTranslatePoint(new Avalonia.Point(),this).X;
            bool covered=!(bounds.Width<=0 || bounds.Height<=0) && x+button.NativeWidth()*.5>=bounds.Left && x+button.NativeWidth()*.5<=bounds.Right;
            if(!_inkCoverage.TryGetValue(button,out bool previous) || previous!=covered)
            {button.SetResourceReference(TextBlock.ForegroundProperty,covered?"ControlCentre.SegmentSelectedInkBrush":"ControlCentre.TextBrush");_inkCoverage[button]=covered;}
        }
        InvalidateVisual();
    }
    internal bool IsAnimating=>_running;
    internal OperatorLiquidSelection.Shape VisualShape=>_ready?OperatorLiquidSelection.Calculate(Bounds.Size,_left,_right,_surfaceWidth,_leftVelocity,_rightVelocity,3,1.6,_running && OperatorMotion.Enabled):new(default(Rect),0,0);
    protected override void RenderNative(NativeDrawingContext dc)
    {
        NativeDrawingContext.RenderBase(dc);if(!_ready || Bounds.Height<2)return;
        OperatorLiquidSelection.Draw(dc,this,VisualShape,true);
    }
}

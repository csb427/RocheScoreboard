// Generated from Views/OperatorSegmentedPanel.Scrubbing.cs; edit translator/adapters.
// Source SHA-256: ae9887aa1f588bfb196b3220ed73eeb768bdde8f264c63a0e824a9073e84939e
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Roche_Scoreboard.Services;
using RadioButton=Avalonia.Controls.RadioButton;

namespace Roche_Scoreboard.Views;

public sealed partial class OperatorSegmentedPanel
{
    private bool _allowScrubbing, _scrubPressed, _scrubbing, _scrubDecayRunning;
    private Avalonia.Point _scrubStart;
    private RadioButton? _scrubCandidate;
    private double _scrubWidth, _scrubSampleAt, _scrubFrameAt;
    // Opt in for preferences only. Wizard steps keep their existing validation
    // and explicit navigation; they are not drag-to-navigate controls.
    public bool AllowScrubbing
    {
        get=>_allowScrubbing;
        set{_allowScrubbing=value;if(!value)CancelScrub();}
    }
    internal bool IsPreviewingSelection=>_scrubbing;
    private void InstallScrubbing()
    {
        this.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, ScrubDown, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        this.AddHandler(Avalonia.Input.InputElement.PointerMovedEvent, ScrubMove, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        this.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, ScrubUp, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        this.AddHandler(Avalonia.Input.InputElement.KeyDownEvent, ScrubKey, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        this.PointerCaptureLost += (_,args)=>{if(ReferenceEquals(args.Source,this))CancelScrub(true);};
        Unloaded += (_,_)=>CancelScrub();
        NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsVisibleProperty,true,(_,_)=>{if(!IsVisible)CancelScrub();});
        NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsEnabledProperty,true,(_,_)=>{if(!IsEnabled)CancelScrub();});
    }
    private RadioButton? ChoiceAt(Avalonia.Point point)
        =>Children.OfType<RadioButton>().Where(button=>button.IsEnabled && button.IsVisible && button.NativeWidth()>0)
            .FirstOrDefault(button=>new Rect(button.NativeTranslatePoint(new Avalonia.Point(),this),button.Bounds.Size).Contains(point));
    private void ScrubDown(object? sender,Avalonia.Input.PointerEventArgs args)
    {
        if(!AllowScrubbing || !IsEnabled || Selected is null)return;
        _scrubStart=args.GetPosition(this);_scrubCandidate=ChoiceAt(_scrubStart);
        _scrubPressed=_scrubCandidate is not null;
        if(_scrubPressed)
        {
            _scrubWidth=Math.Max(1,_scrubCandidate!.NativeWidth()-6);
            _scrubSampleAt=_scrubFrameAt=System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency;
            _leftVelocity=_rightVelocity=_surfaceWidthVelocity=0;
        }
    }
    private void ScrubMove(object? sender,Avalonia.Input.PointerEventArgs args)
    {
        if(!_scrubPressed)return;
        if(!args.GetCurrentPoint(null).Properties.IsLeftButtonPressed){CancelScrub(true);return;}
        var point=args.GetPosition(this);
        if(!_scrubbing)
        {
            if(Math.Abs(point.X-_scrubStart.X)<6 || Math.Abs(point.X-_scrubStart.X)<Math.Abs(point.Y-_scrubStart.Y))return;
            _scrubbing=true;Stop();NativePointer.Capture(this,args);
            if(!NativePointer.IsCaptured(this)){CancelScrub(true);return;}
        }
        _scrubCandidate=ChoiceAt(point);
        UpdateScrubPreview(point,System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency);
        args.Handled=true;
    }
    private void ScrubUp(object? sender,Avalonia.Input.PointerEventArgs args)
    {
        if(!_scrubbing){_scrubPressed=false;_scrubCandidate=null;return;}
        double now=System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency;
        var point=args.GetPosition(this);
        _scrubCandidate=ChoiceAt(point);
        UpdateScrubPreview(point,now);
        var selected=_scrubCandidate;
        _scrubbing=_scrubPressed=false;_scrubCandidate=null;
        StopScrubVelocityDecay();
        NativePointer.Release(this);args.Handled=true;
        if(selected is not null && selected.IsEnabled && selected.IsVisible)
        {selected.IsChecked=true;selected.Focus();}
        // The selected value changes once here. Update(true) retargets the
        // existing spring from the current pose and measured release velocity.
        Update(true);
    }
    private void ScrubKey(object? sender,Avalonia.Input.KeyEventArgs args)
    {
        if(!_scrubbing || args.Key!=Key.Escape)return;
        CancelScrub(true);args.Handled=true;Selected?.Focus();
    }
    private void UpdateScrubPreview(Avalonia.Point point,double now)
    {
        if(Bounds.Width<1 || _scrubWidth<1)return;
        double sampleDt=Math.Max(0,now-_scrubSampleAt);
        DecayScrubVelocity(now);
        double leftLimit=3,rightLimit=Math.Max(leftLimit,Bounds.Width-3);
        double maxWidth=Math.Min(_scrubWidth,rightLimit-leftLimit);
        if(maxWidth<1)return;
        double minCenter=leftLimit+maxWidth*.5,maxCenter=rightLimit-maxWidth*.5;
        double left,width;
        if(point.X<minCenter)
        {
            double excess=minCenter-point.X;
            // Use the measured segment width as the rubber-band extent. The
            // preview stays inside the panel and compresses asymptotically.
            width=Math.Max(1,maxWidth+RubberBandOffset(-excess,maxWidth));
            left=leftLimit;
        }
        else if(point.X>maxCenter)
        {
            double excess=point.X-maxCenter;
            width=Math.Max(1,maxWidth-RubberBandOffset(excess,maxWidth));
            left=rightLimit-width;
        }
        else
        {
            width=maxWidth;
            left=Math.Clamp(point.X-width*.5,leftLimit,rightLimit-width);
        }

        // Share the width spring's response so sampling, pause decay and
        // release all use the same time basis.
        double response=28*OperatorMotion.SpringResponse;
        if(sampleDt>0)
        {
            // A full panel traversal per spring time constant is the velocity
            // ceiling; the smooth bound only affects the visual spring.
            double limit=Math.Max(1,Bounds.Width*response);
            _leftVelocity=SmoothVelocity(_leftVelocity,left-_left,sampleDt,response,limit);
            _rightVelocity=SmoothVelocity(_rightVelocity,left+width-_right,sampleDt,response,limit);
            _surfaceWidthVelocity=_rightVelocity-_leftVelocity;
        }
        _left=left;_right=left+width;_surfaceWidth=width;_ready=true;
        _scrubSampleAt=_scrubFrameAt=now;
        if(OperatorMotion.Enabled && (Math.Abs(_leftVelocity)+Math.Abs(_rightVelocity))>.5)
            StartScrubVelocityDecay();
        Paint();
    }
    internal static double RubberBandOffset(double excess,double extent)
    {
        double distance=Math.Abs(excess);
        if(!double.IsFinite(distance) || !double.IsFinite(extent) || distance<=0 || extent<=0)return 0;
        return Math.CopySign(extent/(1+extent/distance),excess);
    }
    internal static double SmoothVelocity(double current,double displacement,double elapsed,double response,double limit)
    {
        if(!double.IsFinite(current))current=0;
        if(!double.IsFinite(limit) || limit<=0)return 0;
        if(!double.IsFinite(displacement) || !double.IsFinite(elapsed) || !double.IsFinite(response) || elapsed<=0 || response<=0)return current;
        double desired=BoundVelocity(displacement/elapsed,limit);
        double blend=1-Math.Exp(-response*elapsed);
        return Math.Clamp(current+(desired-current)*blend,-limit,limit);
    }
    private static double BoundVelocity(double value,double limit)
    {
        if(!double.IsFinite(limit) || limit<=0)return 0;
        if(!double.IsFinite(value))return Math.CopySign(limit,value);
        double magnitude=Math.Abs(value);
        return magnitude==0?0:Math.CopySign(limit/(1+limit/magnitude),value);
    }
    private void DecayScrubVelocity(double now)
    {
        double dt=Math.Max(0,now-_scrubFrameAt);
        if(dt<=0)return;
        double decay=Math.Exp(-28*OperatorMotion.SpringResponse*dt);
        _leftVelocity*=decay;_rightVelocity*=decay;_surfaceWidthVelocity=_rightVelocity-_leftVelocity;
        _scrubFrameAt=now;
    }
    private void ScrubVelocityFrame(object? sender,EventArgs args)
    {
        if(!_scrubbing || !IsLoaded || !IsVisible || !IsEnabled || !OperatorMotion.Enabled)
        {StopScrubVelocityDecay();return;}
        double now=System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency;
        double dt=Math.Max(0,now-_scrubFrameAt);_scrubFrameAt=now;
        double decay=Math.Exp(-28*OperatorMotion.SpringResponse*dt);
        _leftVelocity*=decay;_rightVelocity*=decay;_surfaceWidthVelocity=_rightVelocity-_leftVelocity;
        if(Math.Abs(_leftVelocity)+Math.Abs(_rightVelocity)<.5)
        { _leftVelocity=_rightVelocity=_surfaceWidthVelocity=0;StopScrubVelocityDecay(); }
        Paint();
    }
    private void StartScrubVelocityDecay()
    {if(_scrubDecayRunning)return;_scrubDecayRunning=true;NativeRendering.Rendering+=ScrubVelocityFrame;}
    private void StopScrubVelocityDecay()
    {if(!_scrubDecayRunning)return;NativeRendering.Rendering-=ScrubVelocityFrame;_scrubDecayRunning=false;}
    private void CancelScrub(bool animateReturn=false)
    {
        bool previewing=_scrubbing;_scrubbing=_scrubPressed=false;_scrubCandidate=null;
        StopScrubVelocityDecay();
        if(NativePointer.IsCaptured(this))NativePointer.Release(this);
        if(previewing)
        {
            if(animateReturn && IsLoaded && IsVisible && IsEnabled && OperatorMotion.Enabled)Update(true);
            else Update(false);
        }
    }
}

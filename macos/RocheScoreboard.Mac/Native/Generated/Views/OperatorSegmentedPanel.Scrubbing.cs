// Generated from Views/OperatorSegmentedPanel.Scrubbing.cs; edit translator/adapters.
// Source SHA-256: 2af23fc3f56a5157dbc45a847df2399849b829d32fd7d3f62661b8efec95fab9
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
    private bool _allowScrubbing, _scrubPressed, _scrubbing;
    private Avalonia.Point _scrubStart;
    private RadioButton? _scrubCandidate;
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
        this.PointerCaptureLost += (_,args)=>{if(ReferenceEquals(args.Source,this))CancelScrub();};
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
    }
    private void ScrubMove(object? sender,Avalonia.Input.PointerEventArgs args)
    {
        if(!_scrubPressed)return;
        if(!args.GetCurrentPoint(null).Properties.IsLeftButtonPressed){CancelScrub();return;}
        var point=args.GetPosition(this);
        if(!_scrubbing)
        {
            if(Math.Abs(point.X-_scrubStart.X)<6 || Math.Abs(point.X-_scrubStart.X)<Math.Abs(point.Y-_scrubStart.Y))return;
            _scrubbing=true;Stop();NativePointer.Capture(this,args);
            if(!NativePointer.IsCaptured(this)){CancelScrub();return;}
        }
        _scrubCandidate=ChoiceAt(point);
        if(_scrubCandidate is not null && OperatorMotion.Enabled)
        {
            // The live lens follows the hand. Checked values and preference
            // writes stay authoritative and unchanged until release.
            double width=_scrubCandidate.NativeWidth()-6;
            _left=Math.Clamp(point.X-width*.5,3,Math.Max(3,Bounds.Width-width-3));_right=_left+width;
            _surfaceWidth=width;_leftVelocity=_rightVelocity=_surfaceWidthVelocity=0;_ready=true;Paint();
        }
        args.Handled=true;
    }
    private void ScrubUp(object? sender,Avalonia.Input.PointerEventArgs args)
    {
        if(!_scrubbing){_scrubPressed=false;_scrubCandidate=null;return;}
        var selected=ChoiceAt(args.GetPosition(this));
        _scrubbing=_scrubPressed=false;_scrubCandidate=null;
        NativePointer.Release(this);args.Handled=true;
        if(selected is not null && selected.IsEnabled && selected.IsVisible)
        {selected.IsChecked=true;selected.Focus();}
        Update(true);
    }
    private void ScrubKey(object? sender,Avalonia.Input.KeyEventArgs args)
    {
        if(!_scrubbing || args.Key!=Key.Escape)return;
        CancelScrub();args.Handled=true;Selected?.Focus();
    }
    private void CancelScrub()
    {
        bool previewing=_scrubbing;_scrubbing=_scrubPressed=false;_scrubCandidate=null;
        if(NativePointer.IsCaptured(this))NativePointer.Release(this);
        if(previewing)Update(false);
    }
}

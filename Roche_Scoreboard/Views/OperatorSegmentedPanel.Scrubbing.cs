using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Roche_Scoreboard.Services;
using RadioButton=System.Windows.Controls.RadioButton;

namespace Roche_Scoreboard.Views;

public sealed partial class OperatorSegmentedPanel
{
    private bool _allowScrubbing, _scrubPressed, _scrubbing;
    private System.Windows.Point _scrubStart;
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
        this.PreviewMouseLeftButtonDown += ScrubDown;
        this.PreviewMouseMove += ScrubMove;
        this.PreviewMouseLeftButtonUp += ScrubUp;
        this.PreviewKeyDown += ScrubKey;
        this.LostMouseCapture += (_,args)=>{if(ReferenceEquals(args.OriginalSource,this))CancelScrub();};
        Unloaded += (_,_)=>CancelScrub();
        IsVisibleChanged += (_,_)=>{if(!IsVisible)CancelScrub();};
        IsEnabledChanged += (_,_)=>{if(!IsEnabled)CancelScrub();};
    }
    private RadioButton? ChoiceAt(System.Windows.Point point)
        =>Children.OfType<RadioButton>().Where(button=>button.IsEnabled && button.IsVisible && button.ActualWidth>0)
            .FirstOrDefault(button=>new Rect(button.TranslatePoint(new System.Windows.Point(),this),button.RenderSize).Contains(point));
    private void ScrubDown(object? sender,System.Windows.Input.MouseButtonEventArgs args)
    {
        if(!AllowScrubbing || !IsEnabled || Selected is null)return;
        _scrubStart=args.GetPosition(this);_scrubCandidate=ChoiceAt(_scrubStart);
        _scrubPressed=_scrubCandidate is not null;
    }
    private void ScrubMove(object? sender,System.Windows.Input.MouseEventArgs args)
    {
        if(!_scrubPressed)return;
        if(args.LeftButton != MouseButtonState.Pressed){CancelScrub();return;}
        var point=args.GetPosition(this);
        if(!_scrubbing)
        {
            if(Math.Abs(point.X-_scrubStart.X)<6 || Math.Abs(point.X-_scrubStart.X)<Math.Abs(point.Y-_scrubStart.Y))return;
            _scrubbing=true;Stop();this.CaptureMouse();
            if(!this.IsMouseCaptured){CancelScrub();return;}
        }
        _scrubCandidate=ChoiceAt(point);
        if(_scrubCandidate is not null && OperatorMotion.Enabled)
        {
            // The live lens follows the hand. Checked values and preference
            // writes stay authoritative and unchanged until release.
            double width=_scrubCandidate.ActualWidth-6;
            _left=Math.Clamp(point.X-width*.5,3,Math.Max(3,ActualWidth-width-3));_right=_left+width;
            _surfaceWidth=width;_leftVelocity=_rightVelocity=_surfaceWidthVelocity=0;_ready=true;Paint();
        }
        args.Handled=true;
    }
    private void ScrubUp(object? sender,System.Windows.Input.MouseButtonEventArgs args)
    {
        if(!_scrubbing){_scrubPressed=false;_scrubCandidate=null;return;}
        var selected=ChoiceAt(args.GetPosition(this));
        _scrubbing=_scrubPressed=false;_scrubCandidate=null;
        this.ReleaseMouseCapture();args.Handled=true;
        if(selected is not null && selected.IsEnabled && selected.IsVisible)
        {selected.IsChecked=true;selected.Focus();}
        Update(true);
    }
    private void ScrubKey(object? sender,System.Windows.Input.KeyEventArgs args)
    {
        if(!_scrubbing || args.Key!=Key.Escape)return;
        CancelScrub();args.Handled=true;Selected?.Focus();
    }
    private void CancelScrub()
    {
        bool previewing=_scrubbing;_scrubbing=_scrubPressed=false;_scrubCandidate=null;
        if(this.IsMouseCaptured)this.ReleaseMouseCapture();
        if(previewing)Update(false);
    }
}

// Generated from MainWindow.StoppageEditorMotion.cs; edit translator/adapters.
// Source SHA-256: 29c324d1b4b8eb633802da16c3199b91cb78c8852bdb9945f057cd22952dedfa
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Point=Avalonia.Point;
using Size=Avalonia.Size;
using Button=Avalonia.Controls.Button;
using KeyEventArgs=Avalonia.Input.KeyEventArgs;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private bool _stoppageMotionWired,_stoppageEditorClosing,_stoppageReturnFocus,_stoppagePlacementPending;
    private int _stoppageEditorQuarter,_stoppageEditorCompleted;
    private TimeSpan _stoppageEditorDuration;
    private ExpansionOrigin _stoppageOrigin=ExpansionOrigin.Default;
    private T? StoppagePart<T>(string name) where T:class=>NativeLookup.Find(this, name) as T;
    private Popup? StoppagePopup=>StoppagePart<Popup>("ControlPanelCountdownEditPopupHost");
    private Border? StoppageSurface=>StoppagePart<Border>("ControlPanelCountdownEditPopup");
    private ScaleTransform? StoppageScale=>StoppagePart<ScaleTransform>("ControlPanelCountdownEditPopupScale");
    private TranslateTransform? StoppageTravel=>StoppagePart<TranslateTransform>("ControlPanelCountdownEditPopupTravel");
    private bool StoppageEditorAvailable=>_aflMatchReady && _sportMode==SportMode.AFL && MainContent.IsVisible
        && _match.ClockMode==ClockMode.CountUp && _showControlPanelCountdown && !_match.IsFullTime;
    private bool StoppageContextCurrent=>StoppageEditorAvailable && _match.Quarter==_stoppageEditorQuarter
        && _match.CompletedQuarterCount==_stoppageEditorCompleted && _controlPanelCountdownDuration==_stoppageEditorDuration;

    private bool FocusExistingStoppageEditor()
    {
        if(StoppagePopup?.IsOpen!=true || _stoppageEditorClosing)return false;
        if(!StoppageContextCurrent){InvalidateStoppageEditor();return false;}
        StoppagePart<Avalonia.Controls.TextBox>("ControlPanelCountdownEditMinutesBox")?.Focus();return true;
    }
    private void EnsureStoppageMotion()
    {
        if(_stoppageMotionWired || StoppagePopup is not {} popup)return;
        _stoppageMotionWired=true;
        if(StoppagePart<OperatorSymbol>("ControlPanelCountdownEditSymbol") is {} symbol)
            symbol.Loaded+=(_,_)=>{if(popup.IsOpen && !_stoppageEditorClosing && !_stoppagePlacementPending)symbol.Activate();};
        popup.Closed+=(_,_)=>
        {
            ++_countdownEditorGeneration;StopStoppageClocks();OperatorAppearance.Changed-=StoppagePreferenceChanged;
            _stoppageEditorClosing=false;_stoppagePlacementPending=false;
            if(StoppageSurface is {} surface){surface.IsVisible=false;surface.Opacity=1;if(surface is OperatorGlassSurface glass)glass.GlassSource=null;}
            SetStoppagePose(new(1,1,0,0));
            if(_stoppageReturnFocus && StoppageEditorAvailable)StoppagePart<Button>("ControlPanelCountdownEditButton")?.Focus();
            _stoppageReturnFocus=false;
        };
        NativeEvents.WatchProperty(MainContent,Avalonia.Controls.Control.IsVisibleProperty,true,(_,args)=>{if(args.NewValue is false)InvalidateStoppageEditor();});
        Unloaded+=(_,_)=>InvalidateStoppageEditor();
    }
    private void StopStoppageClocks()
    {
        StoppageSurface?.BeginAnimation(Control.OpacityProperty,null);
        StoppageScale?.BeginAnimation(ScaleTransform.ScaleXProperty,null);StoppageScale?.BeginAnimation(ScaleTransform.ScaleYProperty,null);
        StoppageTravel?.BeginAnimation(TranslateTransform.XProperty,null);StoppageTravel?.BeginAnimation(TranslateTransform.YProperty,null);
        StoppagePart<Control>("ControlPanelCountdownEditHeader")?.BeginAnimation(Control.OpacityProperty,null);
        StoppagePart<TranslateTransform>("ControlPanelCountdownHeaderTravel")?.BeginAnimation(TranslateTransform.YProperty,null);
    }
    private void SetStoppagePose(ExpansionOrigin pose)
    {
        if(StoppageScale is {} scale){scale.ScaleX=pose.ScaleX;scale.ScaleY=pose.ScaleY;}
        if(StoppageTravel is {} travel){travel.X=pose.OffsetX;travel.Y=pose.OffsetY;}
        if(StoppagePart<Control>("ControlPanelCountdownEditHeader") is {} header)header.Opacity=1;
        if(StoppagePart<TranslateTransform>("ControlPanelCountdownHeaderTravel") is {} headerTravel)headerTravel.Y=0;
    }
    private ExpansionOrigin ReadStoppagePose()=>new(StoppageScale?.ScaleX??1,StoppageScale?.ScaleY??1,StoppageTravel?.X??0,StoppageTravel?.Y??0);
    private ExpansionOrigin MeasureStoppageOrigin(Border surface)
    {
        if(StoppagePart<Button>("ControlPanelCountdownEditButton") is not {} source)return ExpansionOrigin.Default.WithTravel(OperatorMotion.TravelScale);
        var size=new Size(surface.NativeWidth()>0?surface.NativeWidth():surface.DesiredSize.Width,surface.NativeHeight()>0?surface.NativeHeight():surface.DesiredSize.Height);
        ExpansionOrigin VisibleOrigin(Rect sourceRect)
        {
            double width=Math.Min(sourceRect.Width,size.Width),height=Math.Min(sourceRect.Height,size.Height);
            // Native popup windows clip to their own frame. If the owner moves
            // during editing, return toward the nearest visible source edge.
            var visible=new Rect(Math.Clamp(sourceRect.X,0,Math.Max(0,size.Width-width)),Math.Clamp(sourceRect.Y,0,Math.Max(0,size.Height-height)),width,height);
            return ExpansionOrigin.FromRect(visible,size).WithTravel(OperatorMotion.TravelScale);
        }
        try
        {
            if(NativeWindow.GetWindow(source) is {} owner && ReferenceEquals(owner,NativeWindow.GetWindow(surface)))
                return VisibleOrigin(new Rect(source.NativeTranslatePoint(new Point(),surface),source.NativeTranslatePoint(new Point(source.NativeWidth(),source.NativeHeight()),surface)));
            // Popup and owner have different visual roots. Screen conversion
            // preserves actual smart-placement and monitor scale on each root.
            var first=surface.PointFromScreen(source.PointToScreen(new Point()));
            var last=surface.PointFromScreen(source.PointToScreen(new Point(source.NativeWidth(),source.NativeHeight())));
            return VisibleOrigin(new Rect(first,last));
        }
        catch(InvalidOperationException)
        {
            // A disconnected root has no authoritative screen position. The
            // preferred centred placement keeps the source inside the host.
            return VisibleOrigin(new Rect((size.Width-source.NativeWidth())/2,(size.Height-source.NativeHeight())/2,source.NativeWidth(),source.NativeHeight()));
        }
    }
    private void OpenStoppageEditor()
    {
        if(!StoppageEditorAvailable || StoppageSurface is not {} surface || StoppagePopup is not {} popup)return;
        EnsureStoppageMotion();bool reversing=popup.IsOpen;var from=ReadStoppagePose();
        int generation=++_countdownEditorGeneration;StopStoppageClocks();_stoppageEditorClosing=false;_stoppageReturnFocus=false;
        _stoppageEditorQuarter=_match.Quarter;_stoppageEditorCompleted=_match.CompletedQuarterCount;_stoppageEditorDuration=_controlPanelCountdownDuration;
        SetStoppagePose(new(1,1,0,0));surface.IsVisible=true;surface.Opacity=1;
        if(surface is OperatorGlassSurface glass)glass.GlassSource=OperatorDesign.GetBackdrop(this);
        _stoppagePlacementPending=OperatorMotion.Enabled;surface.Opacity=_stoppagePlacementPending?0:1;
        popup.IsOpen=true;surface.UpdateLayout();
        OperatorAppearance.Changed-=StoppagePreferenceChanged;OperatorAppearance.Changed+=StoppagePreferenceChanged;
        if(OperatorMotion.Enabled)
        {
            // A native popup's position is not final inside IsOpen's setter.
            // Measure after layout, before the first visible animation pose.
            Dispatcher.UIThread.Post(DispatcherPriority.Loaded,new Action(()=>
            {
                if(generation!=_countdownEditorGeneration || !popup.IsOpen || _stoppageEditorClosing)return;
                _stoppagePlacementPending=false;surface.UpdateLayout();_stoppageOrigin=MeasureStoppageOrigin(surface);surface.Opacity=1;
                AnimateStoppagePose(reversing?from:_stoppageOrigin,new(1,1,0,0),OperatorMotion.PanelOpen,generation,false);
                if(StoppagePart<Control>("ControlPanelCountdownEditHeader") is {} header)
                    header.BeginAnimation(Control.OpacityProperty,new DoubleAnimation(.45,1,OperatorMotion.SurfaceOpen){FillBehavior=FillBehavior.Stop,EasingFunction=OperatorMotion.SmoothOut});
                if(StoppagePart<TranslateTransform>("ControlPanelCountdownHeaderTravel") is {} headTravel)
                    headTravel.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(7*OperatorMotion.TravelScale,0,OperatorMotion.SurfaceOpen){FillBehavior=FillBehavior.Stop,EasingFunction=OperatorMotion.SmoothOut});
                StoppagePart<OperatorSymbol>("ControlPanelCountdownEditSymbol")?.Activate();
            }));
        }
        else _stoppageOrigin=MeasureStoppageOrigin(surface);
        StoppagePart<Avalonia.Controls.TextBox>("ControlPanelCountdownEditMinutesBox")?.Focus();
    }
    private void AnimateStoppagePose(ExpansionOrigin from,ExpansionOrigin to,TimeSpan duration,int generation,bool closing)
    {
        SetStoppagePose(to);
        void Part(Action<DoubleAnimation?> begin,double start,double end)
            =>begin(new DoubleAnimation(start,end,duration){FillBehavior=FillBehavior.Stop,EasingFunction=OperatorMotion.SmoothOut});
        if(StoppageScale is {} scale){Part(a=>scale.BeginAnimation(ScaleTransform.ScaleXProperty,a),from.ScaleX,to.ScaleX);Part(a=>scale.BeginAnimation(ScaleTransform.ScaleYProperty,a),from.ScaleY,to.ScaleY);}
        if(StoppageTravel is {} travel){Part(a=>travel.BeginAnimation(TranslateTransform.XProperty,a),from.OffsetX,to.OffsetX);Part(a=>travel.BeginAnimation(TranslateTransform.YProperty,a),from.OffsetY,to.OffsetY);}
        if(StoppageSurface is not {} surface)return;
        double fromOpacity=surface.Opacity;surface.Opacity=closing?0:1;
        var fade=new DoubleAnimation(closing?fromOpacity:.75,closing?0:1,duration){FillBehavior=FillBehavior.Stop,EasingFunction=OperatorMotion.SmoothOut};
        fade.Completed+=(_,_)=>{if(generation!=_countdownEditorGeneration)return;if(closing)CompleteStoppageEditorClose(_stoppageReturnFocus);else{StopStoppageClocks();SetStoppagePose(new(1,1,0,0));surface.Opacity=1;}};
        surface.BeginAnimation(Control.OpacityProperty,fade);
    }
    private void BeginStoppageEditorClose()
    {
        if(StoppagePopup?.IsOpen!=true || _stoppageEditorClosing)return;
        _stoppageReturnFocus=true;_stoppageEditorClosing=true;
        if(!OperatorMotion.Enabled || _stoppagePlacementPending){CompleteStoppageEditorClose(true);return;}
        var from=ReadStoppagePose();double opacity=StoppageSurface?.Opacity??1;
        int generation=++_countdownEditorGeneration;StopStoppageClocks();if(StoppageSurface is {} surface)surface.Opacity=opacity;
        SetStoppagePose(new(1,1,0,0));
        if(StoppageSurface is {} positionedSurface)_stoppageOrigin=MeasureStoppageOrigin(positionedSurface);
        AnimateStoppagePose(from,_stoppageOrigin,OperatorMotion.SurfaceClose,generation,true);
    }
    private void StoppageEditor_KeyDown(object sender,KeyEventArgs e)
    {
        // Popup content has a separate native input root. Handle cancellation
        // on its surface so Escape works from both fields and footer actions.
        if(e.Key!=Key.Escape)return;
        e.Handled=true;
        BeginStoppageEditorClose();
    }
    private void CompleteStoppageEditorClose(bool returnFocus)
    {
        var popup=StoppagePopup;
        if(popup is null || !popup.IsOpen && !_stoppageEditorClosing && !_stoppageReturnFocus && StoppageSurface?.IsVisible!=true)return;
        ++_countdownEditorGeneration;_stoppageReturnFocus=returnFocus;StopStoppageClocks();
        if(popup.IsOpen)popup.IsOpen=false;
        else{_stoppageReturnFocus=false;OperatorAppearance.Changed-=StoppagePreferenceChanged;}
    }
    private void InvalidateStoppageEditor()=>CompleteStoppageEditorClose(false);
    private bool CanApplyStoppageEditor()
    {
        if(StoppagePopup?.IsOpen!=true || _stoppageEditorClosing)return false;
        if(StoppageContextCurrent)return true;
        InvalidateStoppageEditor();return false;
    }
    private void StoppagePreferenceChanged()
    {
        if(OperatorMotion.Enabled || StoppagePopup?.IsOpen!=true)return;
        if(_stoppageEditorClosing){CompleteStoppageEditorClose(_stoppageReturnFocus);return;}
        ++_countdownEditorGeneration;_stoppagePlacementPending=false;StopStoppageClocks();SetStoppagePose(new(1,1,0,0));if(StoppageSurface is {} surface)surface.Opacity=1;
    }
}

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Roche_Scoreboard.Services;

namespace Roche_Scoreboard.Views;

public partial class ResumeMatchPrompt
{
    private FrameworkElement? _recoverySource;
    private readonly ScaleTransform _recoveryScale=new(1,1);
    private readonly TranslateTransform _recoveryTravel=new();
    private ExpansionOrigin _recoveryOrigin=ExpansionOrigin.Default;
    private DispatcherTimer? _recoveryExitDeadline;
    internal void SetExpansionOrigin(FrameworkElement? source)=>_recoverySource=source;

    private void InitializeRecoveryMotion()
    {
        var transform=new TransformGroup();transform.Children.Add(_recoveryScale);transform.Children.Add(_recoveryTravel);
        DialogShell.RenderTransform=transform;DialogShell.RenderTransformOrigin=new System.Windows.Point(.5,.5);
        RecoverySymbol.Loaded+=(_,_)=>RecoverySymbol.Activate();
        Loaded+=(_,_)=>
        {
            OperatorAppearance.Changed-=RecoveryPreferencesChanged;OperatorAppearance.Changed+=RecoveryPreferencesChanged;
            if(_completed)return;
            DialogShell.UpdateLayout();_recoveryOrigin=ExpansionOrigin.From(DialogShell,_recoverySource).WithTravel(OperatorMotion.TravelScale);
            if(!OperatorMotion.Enabled){StopRecoveryMotion();return;}
            AnimateRecoveryShell(_recoveryOrigin,new ExpansionOrigin(1,1,0,0),OperatorMotion.PanelOpen);
            // Identity appears before saved team details. No fake progress or
            // whole-card blur; data remains readable on the existing glass.
            RecoveryHeader.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,OperatorMotion.SurfaceOpen){BeginTime=OperatorMotion.Stagger,FillBehavior=FillBehavior.Stop});
            RecoveryDetails.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,OperatorMotion.SurfaceOpen){BeginTime=TimeSpan.FromTicks(OperatorMotion.Stagger.Ticks*2),FillBehavior=FillBehavior.Stop});
        };
        Unloaded+=(_,_)=>
        {
            OperatorAppearance.Changed-=RecoveryPreferencesChanged;
            StopRecoveryMotion();
        };
    }
    private void AnimateRecoveryShell(ExpansionOrigin from,ExpansionOrigin to,TimeSpan duration)
    {
        _recoveryScale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(from.ScaleX,to.ScaleX,duration){EasingFunction=OperatorMotion.SmoothOut,FillBehavior=FillBehavior.Stop});
        _recoveryScale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(from.ScaleY,to.ScaleY,duration){EasingFunction=OperatorMotion.SmoothOut,FillBehavior=FillBehavior.Stop});
        _recoveryTravel.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(from.OffsetX,to.OffsetX,duration){EasingFunction=OperatorMotion.SmoothOut,FillBehavior=FillBehavior.Stop});
        _recoveryTravel.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(from.OffsetY,to.OffsetY,duration){EasingFunction=OperatorMotion.SmoothOut,FillBehavior=FillBehavior.Stop});
    }
    private void CloseRecoveryMotion()
    {
        var current=new ExpansionOrigin(_recoveryScale.ScaleX,_recoveryScale.ScaleY,_recoveryTravel.X,_recoveryTravel.Y);
        AnimateRecoveryShell(current,_recoveryOrigin,OperatorMotion.SurfaceClose);
        RecoveryDetails.BeginAnimation(OpacityProperty,new DoubleAnimation(0,OperatorMotion.Press));
        var fade=new DoubleAnimation(Opacity,0,OperatorMotion.SurfaceClose){EasingFunction=OperatorMotion.SmoothOut};
        fade.Completed+=(_,_)=>Complete();BeginAnimation(OpacityProperty,fade);
        // A hidden/occluded compositor must not retain the recovery input scope.
        _recoveryExitDeadline=new DispatcherTimer{Interval=OperatorMotion.SurfaceClose+TimeSpan.FromMilliseconds(150)};
        _recoveryExitDeadline.Tick+=(_,_)=>Complete();_recoveryExitDeadline.Start();
    }
    private void RecoveryPreferencesChanged()
    {if(!OperatorMotion.Enabled){if(_completed)Complete();else StopRecoveryMotion();}}
    private void StopRecoveryMotion()
    {
        _recoveryExitDeadline?.Stop();_recoveryExitDeadline=null;
        foreach(var property in new[]{ScaleTransform.ScaleXProperty,ScaleTransform.ScaleYProperty})_recoveryScale.BeginAnimation(property,null);
        _recoveryScale.ScaleX=_recoveryScale.ScaleY=1;
        foreach(var property in new[]{TranslateTransform.XProperty,TranslateTransform.YProperty})_recoveryTravel.BeginAnimation(property,null);
        _recoveryTravel.X=_recoveryTravel.Y=0;
        RecoveryHeader.BeginAnimation(OpacityProperty,null);RecoveryHeader.Opacity=1;
        RecoveryDetails.BeginAnimation(OpacityProperty,null);RecoveryDetails.Opacity=1;
    }
    private void RestoreRecoveryFocus()
    {
        if(!DeferredChosen || _recoverySource is not {} source)return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(()=>
        {
            if(source is not {IsLoaded:true,IsVisible:true,IsEnabled:true,Focusable:true} || Window.GetWindow(source) is not {IsActive:true})return;
            if(Keyboard.FocusedElement is UIElement {IsVisible:true,IsEnabled:true,Focusable:true} current
                && !ReferenceEquals(current,source) && !this.IsAncestorOf(current))return;
            Keyboard.Focus(source);
        }));
    }
}

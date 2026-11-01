// OUTSIDE CANDIDATE — UNCOMPILED/UNRUN. Operator-only schematic, never output content.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Services;
using Border=System.Windows.Controls.Border;
using Canvas=System.Windows.Controls.Canvas;
using Ellipse=System.Windows.Shapes.Ellipse;
using Point=System.Windows.Point;

namespace Roche_Scoreboard.Views;

/// <summary>A venue standby scene inside a fixed frame, not a playback/progress indicator.</summary>
internal sealed class AflScreensaverGlyph : Canvas
{
    public static readonly DependencyProperty ProgressProperty=DependencyProperty.Register(
        nameof(Progress),typeof(double),typeof(AflScreensaverGlyph),new PropertyMetadata(0d,ProgressChanged));
    public double Progress {get=>(double)GetValue(ProgressProperty);set=>SetValue(ProgressProperty,value);}
    internal OperatorChoiceSurface SelectionBridge {get;}=new(){Name="ScreensaverSelectionBridge",Width=29,Height=29,Opacity=0};
    private readonly Border _frame=new(){Name="ScreensaverSceneFrame",Width=25,Height=23,CornerRadius=new CornerRadius(3),BorderThickness=new Thickness(1.5),IsHitTestVisible=false};
    private readonly Ellipse _field=new(){Name="ScreensaverVenueOval",Width=17,Height=8,StrokeThickness=1.1,Opacity=.72,IsHitTestVisible=false};
    private readonly Border _center=new(){Name="ScreensaverVenueCenter",Width=1,Height=5,Opacity=.60,IsHitTestVisible=false};
    private readonly Border _horizon=new(){Name="ScreensaverHorizonAccent",Width=17,Height=1.4,IsHitTestVisible=false};
    private readonly ScaleTransform _horizonScale=new(.75,1);
    private readonly Border[] _lights=new Border[2];
    private readonly TranslateTransform[] _lightSettles=new TranslateTransform[2];
    internal double HorizonPhase=>Math.Clamp(Progress/.65,0,1);
    internal double AmbientPhase=>Math.Clamp((Progress-.30)/.70,0,1);
    public AflScreensaverGlyph()
    {
        Name="ScreensaverGlyph";Width=29;Height=29;Focusable=false;IsHitTestVisible=false;ClipToBounds=true;
        Children.Add(SelectionBridge);
        SetLeft(_frame,2);SetTop(_frame,3);Children.Add(_frame);
        SetLeft(_field,6);SetTop(_field,14);Children.Add(_field);
        SetLeft(_center,14);SetTop(_center,15.5);Children.Add(_center);
        _horizon.RenderTransform=_horizonScale;_horizon.RenderTransformOrigin=new Point(.5,.5);
        SetLeft(_horizon,6);SetTop(_horizon,11.5);Children.Add(_horizon);
        for(int i=0;i<2;i++)
        {
            _lightSettles[i]=new TranslateTransform();
            _lights[i]=new Border{Name="ScreensaverAmbientMark"+(i+1),Width=2,Height=i==0?4:5,CornerRadius=new CornerRadius(1),IsHitTestVisible=false,RenderTransform=_lightSettles[i]};
            SetLeft(_lights[i],i==0?8:20);SetTop(_lights[i],i==0?6:7);Children.Add(_lights[i]);
        }
        SetSelectedInk(false);ApplyProgress();
    }
    internal void SetSelectedInk(bool selected)
    {
        string key=selected?"ControlCentre.SelectedTextBrush":"ControlCentre.TextBrush";
        _frame.SetResourceReference(Border.BorderBrushProperty,key);
        _field.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,key);
        _center.SetResourceReference(Border.BackgroundProperty,key);_horizon.SetResourceReference(Border.BackgroundProperty,key);
        foreach(var light in _lights)light.SetResourceReference(Border.BackgroundProperty,key);
    }
    private static void ProgressChanged(DependencyObject target,DependencyPropertyChangedEventArgs args)=>((AflScreensaverGlyph)target).ApplyProgress();
    private void ApplyProgress()
    {
        // A fixed scene remains legible in both states. A short horizon accent
        // settles first, then two tiny ambient marks arrive locally. This is
        // finite selection feedback, not a live field, content thumbnail,
        // output-window activation, media readiness or playback progress.
        _horizonScale.ScaleX=1-.25*OperatorMotion.TravelScale*(1-HorizonPhase);
        _horizon.Opacity=.28+.62*HorizonPhase;
        for(int i=0;i<_lights.Length;i++)if(_lights[i] is not null)
        {
            _lights[i].Opacity=.18+.67*AmbientPhase;
            _lightSettles[i].Y=(i==0?-.9:.9)*OperatorMotion.TravelScale*(1-AmbientPhase);
        }
    }
}

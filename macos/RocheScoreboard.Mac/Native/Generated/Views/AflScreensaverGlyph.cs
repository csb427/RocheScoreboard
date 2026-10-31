// Generated from Views/AflScreensaverGlyph.cs; edit translator/adapters.
// Source SHA-256: 6c1b03ae37fe9f52620a69554b643a9add974139ce25ef7466d5cae652ea7680
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
// OUTSIDE CANDIDATE — UNCOMPILED/UNRUN. Operator-only schematic, never output content.
using Roche_Scoreboard.Services;
using Border=Avalonia.Controls.Border;
using Canvas=Avalonia.Controls.Canvas;
using Ellipse=Avalonia.Controls.Shapes.Ellipse;
using Point=Avalonia.Point;

namespace Roche_Scoreboard.Views;

/// <summary>A venue standby scene inside a fixed frame, not a playback/progress indicator.</summary>
internal sealed class AflScreensaverGlyph : Canvas
{
    public static readonly AttachedProperty<double> ProgressProperty=(AttachedProperty<double>)NativeProperty.Register(nameof(Progress),typeof(double),typeof(AflScreensaverGlyph),new PropertyMetadata(0d,ProgressChanged));
    public double Progress {get=>(double)GetValue(ProgressProperty);set=>SetValue(ProgressProperty,value);}
    internal OperatorChoiceSurface SelectionBridge {get;}=new(){Name="ScreensaverSelectionBridge",Width=29,Height=29,Opacity=0};
    private readonly Border _frame=new NativeBorder(){Name="ScreensaverSceneFrame",Width=25,Height=23,CornerRadius=new CornerRadius(3),BorderThickness=new Thickness(1.5),IsHitTestVisible=false};
    private readonly Ellipse _field=new(){Name="ScreensaverVenueOval",Width=17,Height=8,StrokeThickness=1.1,Opacity=.72,IsHitTestVisible=false};
    private readonly Border _center=new NativeBorder(){Name="ScreensaverVenueCenter",Width=1,Height=5,Opacity=.60,IsHitTestVisible=false};
    private readonly Border _horizon=new NativeBorder(){Name="ScreensaverHorizonAccent",Width=17,Height=1.4,IsHitTestVisible=false};
    private readonly ScaleTransform _horizonScale=new(.75,1);
    private readonly Border[] _lights=new NativeBorder[2];
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
        _horizon.RenderTransform=_horizonScale;_horizon.RenderTransformOrigin=new RelativePoint(.5,.5,RelativeUnit.Relative);
        SetLeft(_horizon,6);SetTop(_horizon,11.5);Children.Add(_horizon);
        for(int i=0;i<2;i++)
        {
            _lightSettles[i]=new TranslateTransform();
            _lights[i]=new NativeBorder{Name="ScreensaverAmbientMark"+(i+1),Width=2,Height=i==0?4:5,CornerRadius=new CornerRadius(1),IsHitTestVisible=false,RenderTransform=_lightSettles[i]};
            SetLeft(_lights[i],i==0?8:20);SetTop(_lights[i],i==0?6:7);Children.Add(_lights[i]);
        }
        SetSelectedInk(false);ApplyProgress();
    }
    internal void SetSelectedInk(bool selected)
    {
        string key=selected?"ControlCentre.SelectedTextBrush":"ControlCentre.TextBrush";
        _frame.SetResourceReference(Border.BorderBrushProperty,key);
        _field.SetResourceReference(Avalonia.Controls.Shapes.Shape.StrokeProperty,key);
        _center.SetResourceReference(Border.BackgroundProperty,key);_horizon.SetResourceReference(Border.BackgroundProperty,key);
        foreach(var light in _lights)light.SetResourceReference(Border.BackgroundProperty,key);
    }
    private static void ProgressChanged(AvaloniaObject target,AvaloniaPropertyChangedEventArgs args)=>((AflScreensaverGlyph)target).ApplyProgress();
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

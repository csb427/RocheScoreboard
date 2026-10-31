using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Services;
using Border=System.Windows.Controls.Border;
using Canvas=System.Windows.Controls.Canvas;
using ShapePath=System.Windows.Shapes.Path;
using Point=System.Windows.Point;

namespace Roche_Scoreboard.Views;

/// <summary>Conventional schematic weather symbol; never current conditions or service progress.</summary>
internal sealed class AflWeatherGlyph : Canvas
{
    public static readonly DependencyProperty ProgressProperty=DependencyProperty.Register(
        nameof(Progress),typeof(double),typeof(AflWeatherGlyph),new PropertyMetadata(0d,ProgressChanged));
    public double Progress {get=>(double)GetValue(ProgressProperty);set=>SetValue(ProgressProperty,value);}
    internal OperatorChoiceSurface SelectionBridge {get;}=new(){Name="WeatherSelectionBridge",Width=29,Height=29,Opacity=0};
    private readonly TranslateTransform _sunLift=new(),_rayLift=new();
    private readonly Border _sun=new(){Name="WeatherSchematicSun",Width=8,Height=8,CornerRadius=new CornerRadius(4),IsHitTestVisible=false};
    private readonly ShapePath _rays=new(){Name="WeatherSchematicRays",Width=29,Height=29,Stretch=Stretch.None,
        Data=Geometry.Parse("M19,2 L19,4 M24,4 L22.8,5.2 M26,9 L24,9 M24,14 L22.8,12.8 M14,4 L15.2,5.2 M12,9 L14,9"),StrokeThickness=1.3};
    private readonly ShapePath _cloud=new(){Name="WeatherSchematicCloud",Width=29,Height=29,Stretch=Stretch.None,
        Data=Geometry.Parse("M7,23 C4.8,23 3.5,21.5 3.5,19.5 C3.5,17.6 4.9,16.1 6.8,15.9 C7.2,12.9 9.4,11 12.2,11 C15,11 17.2,12.8 17.8,15.3 C18.6,14.9 19.4,14.7 20.3,14.7 C23.2,14.7 25.5,16.6 25.5,19.2 C25.5,21.4 23.7,23 21.4,23 Z"),StrokeThickness=1.6,StrokeLineJoin=PenLineJoin.Round};
    internal double SunPhase=>Math.Clamp(Progress/.65,0,1);
    internal double RayPhase=>Math.Clamp((Progress-.25)/.75,0,1);
    public AflWeatherGlyph()
    {
        Name="WeatherGlyph";Width=29;Height=29;ClipToBounds=true;Focusable=false;IsHitTestVisible=false;
        _sun.RenderTransform=_sunLift;_rays.RenderTransform=_rayLift;
        SetLeft(_sun,15);SetTop(_sun,5);
        Children.Add(SelectionBridge);Children.Add(_rays);Children.Add(_sun);Children.Add(_cloud);
        SetSelectedInk(false);ApplyProgress();
    }
    internal void SetSelectedInk(bool selected)
    {
        string ink=selected?"ControlCentre.SelectedTextBrush":"ControlCentre.TextBrush";
        _sun.SetResourceReference(Border.BackgroundProperty,ink);
        _rays.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,ink);
        _cloud.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,ink);
        // Same themed card material occludes the rear sun: no blur over text,
        // fixed shell and cloud, no service-owned colour or forecast binding.
        _cloud.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
            selected?"ControlCentre.SelectionBrush":"ControlCentre.RaisedBrush");
    }
    private static void ProgressChanged(DependencyObject target,DependencyPropertyChangedEventArgs args)=>((AflWeatherGlyph)target).ApplyProgress();
    private void ApplyProgress()
    {
        // Reversible selection geometry only. A short lift clears the fixed
        // cloud silhouette; rays brighten later. Calm reduces displacement;
        // owner/reduced-motion policy settles source progress immediately.
        double shift=2*OperatorMotion.TravelScale*(1-SunPhase);
        _sunLift.Y=shift;_rayLift.Y=shift;
        _sun.Opacity=.65+.35*SunPhase;_rays.Opacity=.30+.60*RayPhase;
    }
}

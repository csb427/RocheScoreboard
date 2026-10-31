// Generated from Views/AflLiveScorebugGlyph.cs; edit translator/adapters.
// Source SHA-256: 31110ad18ee09d980bdbe480263ee868378d131a0fd04a9830363e8a74af5c27
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Roche_Scoreboard.Services;
using Border=Avalonia.Controls.Border;
using Canvas=Avalonia.Controls.Canvas;
using ShapePath=Avalonia.Controls.Shapes.Path;

namespace Roche_Scoreboard.Views;

/// <summary>Two schematic team wells and a fixed-width timing row, never live match values.</summary>
internal sealed class AflLiveScorebugGlyph : Canvas
{
    public static readonly AttachedProperty<double> ProgressProperty=(AttachedProperty<double>)NativeProperty.Register(nameof(Progress),typeof(double),typeof(AflLiveScorebugGlyph),new PropertyMetadata(0d,ProgressChanged));
    public double Progress {get=>(double)GetValue(ProgressProperty);set=>SetValue(ProgressProperty,value);}
    internal double HomePhase=>Math.Clamp(Progress/.45,0,1);
    internal double AwayPhase=>Math.Clamp((Progress-.12)/.45,0,1);
    internal double TimingPhase=>Math.Clamp((Progress-.40)/.60,0,1);
    internal OperatorChoiceSurface SelectionSurface {get;}=new(){Name="LiveScorebugSelectionWell",Width=29,Height=29,OriginX=.5,Opacity=.10};
    private readonly Border _home=new NativeBorder(){Name="LiveScorebugHomeFill",Width=6,Height=5,Opacity=0};
    private readonly Border _away=new NativeBorder(){Name="LiveScorebugAwayFill",Width=6,Height=5,Opacity=0};
    private readonly Border _timing=new NativeBorder(){Name="LiveScorebugTimingStrip",Width=16,Height=1,Opacity=0};
    private readonly TranslateTransform _homeTravel=new(),_awayTravel=new(),_timingTravel=new();
    private readonly Border _homeWell=new NativeBorder(){Name="LiveScorebugHomeWell",Width=8,Height=7,BorderThickness=new Thickness(1),ClipToBounds=true};
    private readonly Border _awayWell=new NativeBorder(){Name="LiveScorebugAwayWell",Width=8,Height=7,BorderThickness=new Thickness(1),ClipToBounds=true};
    private readonly Border _timingRail=new NativeBorder(){Name="LiveScorebugTimingRail",Width=18,Height=3,BorderThickness=new Thickness(1),ClipToBounds=true};
    private readonly ShapePath _frame=new(){Name="LiveScorebugMonitorFrame",Width=29,Height=29,Stretch=Avalonia.Media.Stretch.None,Data=Geometry.Parse("M2,3 L27,3 27,22 2,22 Z M14.5,22 L14.5,27 M9,27 L20,27"),StrokeThickness=1.7,StrokeJoin=PenLineJoin.Round};
    public AflLiveScorebugGlyph()
    {
        Name="LiveScorebugGlyph";Width=29;Height=29;ClipToBounds=true;IsHitTestVisible=false;Focusable=false;
        SelectionSurface.SetResourceReference(OperatorChoiceSurface.FillBrushProperty,"ControlCentre.AccentBrush");Children.Add(SelectionSurface);
        _home.RenderTransform=_homeTravel;_away.RenderTransform=_awayTravel;_timing.RenderTransform=_timingTravel;
        _homeWell.Child=_home;_awayWell.Child=_away;_timingRail.Child=_timing;
        SetLeft(_homeWell,5);SetTop(_homeWell,11);SetLeft(_awayWell,16);SetTop(_awayWell,11);
        SetLeft(_timingRail,5);SetTop(_timingRail,6);
        Children.Add(_homeWell);Children.Add(_awayWell);Children.Add(_timingRail);Children.Add(_frame);
        SetSelectedInk(false);ApplyProgress();
    }
    internal void SetSelectedInk(bool selected)
    {
        string ink=selected?"ControlCentre.SelectedTextBrush":"ControlCentre.TextBrush";
        _frame.SetResourceReference(Avalonia.Controls.Shapes.Shape.StrokeProperty,ink);
        foreach(var well in new[]{_homeWell,_awayWell,_timingRail})well.SetResourceReference(Border.BorderBrushProperty,ink);
        foreach(var fill in new[]{_home,_away,_timing})fill.SetResourceReference(Border.BackgroundProperty,ink);
    }
    private static void ProgressChanged(AvaloniaObject target,AvaloniaPropertyChangedEventArgs args)=>((AflLiveScorebugGlyph)target).ApplyProgress();
    private void ApplyProgress()
    {
        // Parts hand off inside a stationary monitor. No width/count/progress
        // animation: the timing strip always reserves its full16-unit width.
        _home.Opacity=.50*HomePhase;_away.Opacity=.50*AwayPhase;_timing.Opacity=.85*TimingPhase;
        _homeTravel.X=-1.2*(1-HomePhase)*OperatorMotion.TravelScale;
        _awayTravel.X=1.2*(1-AwayPhase)*OperatorMotion.TravelScale;
        _timingTravel.Y=1.4*(1-TimingPhase)*OperatorMotion.TravelScale;
    }
}

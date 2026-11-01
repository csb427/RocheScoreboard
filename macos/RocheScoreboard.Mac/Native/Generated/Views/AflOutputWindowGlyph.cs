// Generated from Views/AflOutputWindowGlyph.cs; edit translator/adapters.
// Source SHA-256: 80d0ff5d3bff93d78041b15e48e855eb96895fe8d77f8d5ca069b5002ccfe00c
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Border = Avalonia.Controls.Border;
using Canvas = Avalonia.Controls.Canvas;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Point = Avalonia.Point;

namespace Roche_Scoreboard.Views;

/// <summary>An output window frame and opening pane; no on-air or playing indicator.</summary>
internal sealed class AflOutputWindowGlyph : Canvas
{
    public static readonly AttachedProperty<double> ThumbPositionProperty = (AttachedProperty<double>)NativeProperty.Register(nameof(ThumbPosition),typeof(double), typeof(AflOutputWindowGlyph), new PropertyMetadata(0d, PositionChanged));
    public double ThumbPosition { get => (double)GetValue(ThumbPositionProperty); set => SetValue(ThumbPositionProperty, value); }
    internal double PanePhase => Math.Clamp(ThumbPosition / 20 / .72, 0, 1);
    internal double GlintPhase => Math.Clamp((ThumbPosition / 20 - .72) / .28, 0, 1);
    private readonly Border _pane = new NativeBorder() { Name="OutputWindowOpenPane",Width=19,Height=12,CornerRadius=new CornerRadius(1),IsHitTestVisible=false };
    private readonly Border _glint = new NativeBorder() { Name="OutputWindowPaneEdge",Width=14,Height=1,IsHitTestVisible=false };
    private readonly ScaleTransform _paneScale = new(1,.45);
    private readonly TranslateTransform _paneTravel = new();
    public AflOutputWindowGlyph()
    {
        Name="OutputWindowGlyph";Width=29;Height=29;ClipToBounds=true;Focusable=false;IsHitTestVisible=false;
        var paneClip=new Canvas { Name="OutputWindowPaneClip",Width=21,Height=15,ClipToBounds=true,IsHitTestVisible=false };
        SetLeft(paneClip,4);SetTop(paneClip,5);SetLeft(_pane,1);SetTop(_pane,1);
        _pane.SetResourceReference(Border.BackgroundProperty,"ControlCentre.AccentBrush");
        var paneTransforms=new TransformGroup();paneTransforms.Children.Add(_paneScale);paneTransforms.Children.Add(_paneTravel);
        _pane.RenderTransform=paneTransforms;_pane.RenderTransformOrigin=new RelativePoint(.5,1,RelativeUnit.Relative);
        _glint.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SelectedTextBrush");SetLeft(_glint,3);SetTop(_glint,3);
        paneClip.Children.Add(_pane);paneClip.Children.Add(_glint);Children.Add(paneClip);
        var frame=new ShapePath { Name="OutputWindowMonitorFrame",Width=29,Height=29,Stretch=Avalonia.Media.Stretch.None,Data=Geometry.Parse("M2,3 L27,3 27,22 2,22 Z M14.5,22 L14.5,27 M9,27 L20,27"),
            StrokeThickness=1.7,StrokeJoin=PenLineJoin.Round,IsHitTestVisible=false };
        frame.SetResourceReference(Avalonia.Controls.Shapes.Shape.StrokeProperty,"ControlCentre.TextBrush");Children.Add(frame);ApplyPosition();
    }
    private static void PositionChanged(AvaloniaObject target,AvaloniaPropertyChangedEventArgs args)=>((AflOutputWindowGlyph)target).ApplyPosition();
    private void ApplyPosition()
    {
        // The already-owned thumb timeline supplies all reversal/Reduce state.
        // This canvas owns no clock, timer, ancestor selection or subscriptions.
        double pane=PanePhase;_pane.Opacity=.72*pane;_paneScale.ScaleY=.45+.55*pane;
        _paneTravel.Y=2*(1-pane)*Roche_Scoreboard.Services.OperatorMotion.TravelScale;
        _glint.Opacity=.72*GlintPhase;
    }
}

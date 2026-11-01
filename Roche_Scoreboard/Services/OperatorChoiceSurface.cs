using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace Roche_Scoreboard.Services;

/// <summary>Uses the existing tab lens geometry for a growing selection fill.
/// The control's checked state and hit target remain independent of this drawing.</summary>
public sealed class OperatorChoiceSurface : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double), typeof(OperatorChoiceSurface), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public static readonly DependencyProperty OriginXProperty = DependencyProperty.Register(nameof(OriginX), typeof(double), typeof(OperatorChoiceSurface), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double OriginX { get => (double)GetValue(OriginXProperty); set => SetValue(OriginXProperty, value); }
    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(nameof(FillBrush), typeof(Brush), typeof(OperatorChoiceSurface), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? FillBrush { get => (Brush?)GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }
    public OperatorChoiceSurface() { IsHitTestVisible = false; Focusable = false; SetResourceReference(OperatorLiquidSelection.ContextProperty, "ControlCentre.CanvasBrush"); }
    protected override void OnRender(DrawingContext context)
    {
        double p = Math.Clamp(Progress, 0, 1);
        if (p <= 0 || ActualWidth < 5 || ActualHeight < 5) return;
        double width = ActualWidth - 4, height = ActualHeight - 4;
        double fillWidth = width * (.08 + .92 * p), fillHeight = height * (.45 + .55 * p);
        double origin=double.IsFinite(OriginX)?Math.Clamp(OriginX,0,1):1;
        var shape = new OperatorLiquidSelection.Shape(new Rect(2 + (width - fillWidth)*origin, 2 + (height - fillHeight) / 2, fillWidth, fillHeight), Math.Sin(Math.PI * p) * .8, origin<.5?1:-1);
        context.PushOpacity(p);
        OperatorLiquidSelection.Draw(context, this, shape, false, FillBrush);
        context.Pop();
    }
}

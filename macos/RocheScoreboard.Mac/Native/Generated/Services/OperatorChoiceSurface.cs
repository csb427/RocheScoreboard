// Generated from Services/OperatorChoiceSurface.cs; edit translator/adapters.
// Source SHA-256: 60b4bf2118c8fa9cd89b0d4b2883289ee633a98ebbaf9b5e1c9f50a8f2c2d7ec
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Brush = Avalonia.Media.IBrush;

namespace Roche_Scoreboard.Services;

/// <summary>Uses the existing tab lens geometry for a growing selection fill.
/// The control's checked state and hit target remain independent of this drawing.</summary>
public sealed class OperatorChoiceSurface : Control
{
    public static readonly AttachedProperty<double> ProgressProperty = (AttachedProperty<double>)NativeProperty.Register(nameof(Progress), typeof(double), typeof(OperatorChoiceSurface), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public static readonly AttachedProperty<double> OriginXProperty = (AttachedProperty<double>)NativeProperty.Register(nameof(OriginX), typeof(double), typeof(OperatorChoiceSurface), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double OriginX { get => (double)GetValue(OriginXProperty); set => SetValue(OriginXProperty, value); }
    // Match the owning card/track's actual silhouette, rather than assuming
    // every selection is a capsule. Templates declare their own radius.
    public static readonly AttachedProperty<double> CornerRadiusProperty = (AttachedProperty<double>)NativeProperty.Register(nameof(CornerRadius), typeof(double), typeof(OperatorChoiceSurface), new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public static readonly AttachedProperty<Brush?> FillBrushProperty = (AttachedProperty<Brush?>)NativeProperty.Register(nameof(FillBrush), typeof(Brush), typeof(OperatorChoiceSurface), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? FillBrush { get => (Brush?)GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }
    public OperatorChoiceSurface() { IsHitTestVisible = false; Focusable = false; this.SetResourceReference(OperatorLiquidSelection.ContextProperty, "ControlCentre.CanvasBrush"); }
    public override void Render(Avalonia.Media.DrawingContext nativeContext) { using var context = new NativeDrawingContext(nativeContext,Bounds.Size);
        double p = Math.Clamp(Progress, 0, 1);
        if (p <= 0 || Bounds.Width < 5 || Bounds.Height < 5) return;
        double width = Bounds.Width - 4, height = Bounds.Height - 4;
        double fillWidth = width * (.08 + .92 * p), fillHeight = height * (.45 + .55 * p);
        double origin=double.IsFinite(OriginX)?Math.Clamp(OriginX,0,1):1;
        var shape = new OperatorLiquidSelection.Shape(new Rect(2 + (width - fillWidth)*origin, 2 + (height - fillHeight) / 2, fillWidth, fillHeight), Math.Sin(Math.PI * p) * .8, origin<.5?1:-1, Math.Max(0, CornerRadius-2));
        context.PushOpacity(p);
        OperatorLiquidSelection.Draw(context, this, shape, false, FillBrush);
        context.Pop();
    }
}

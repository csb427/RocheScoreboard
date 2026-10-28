// Generated from Services/OperatorChoiceSurface.cs; edit translator/adapters.
// Source SHA-256: beda71025254cd615ec08768c399b1b0bae5cde17fe7ade323c6df9daa2fed8b
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace Roche_Scoreboard.Services;

/// <summary>Uses the existing tab lens geometry for a growing selection fill.
/// The control's checked state and hit target remain independent of this drawing.</summary>
public sealed class OperatorChoiceSurface : Control
{
    public static readonly AttachedProperty<double> ProgressProperty = (AttachedProperty<double>)NativeProperty.Register(nameof(Progress), typeof(double), typeof(OperatorChoiceSurface), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public OperatorChoiceSurface() { IsHitTestVisible = false; Focusable = false; this.SetResourceReference(OperatorLiquidSelection.ContextProperty, "ControlCentre.CanvasBrush"); }
    public override void Render(Avalonia.Media.DrawingContext nativeContext) { using var context = new NativeDrawingContext(nativeContext,Bounds.Size);
        double p = Math.Clamp(Progress, 0, 1);
        if (p <= 0 || Bounds.Width < 5 || Bounds.Height < 5) return;
        double width = Bounds.Width - 4, height = Bounds.Height - 4;
        double fillWidth = width * (.08 + .92 * p), fillHeight = height * (.45 + .55 * p);
        var shape = new OperatorLiquidSelection.Shape(new Rect(2 + width - fillWidth, 2 + (height - fillHeight) / 2, fillWidth, fillHeight), Math.Sin(Math.PI * p) * .8, -1);
        context.PushOpacity(p);
        OperatorLiquidSelection.Draw(context, this, shape, false);
        context.Pop();
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
namespace RocheScoreboard.Mac.Native;
internal sealed class NativePaintLayer(Action<NativeDrawingContext> draw):Control
{
    public override void Render(DrawingContext context){using var paint=new NativeDrawingContext(context,Bounds.Size);draw(paint);}
}
public class NativePaintBorder:Border
{
    private readonly NativePaintLayer _paint;
    public NativePaintBorder(){_paint=new NativePaintLayer(RenderNative){IsHitTestVisible=false,ZIndex=-2};VisualChildren.Insert(0,_paint);}
    protected virtual void RenderNative(NativeDrawingContext context){}
    protected void AddVisualChild(Visual child){if(child is Control control)control.ZIndex=child.GetType().Name=="FocusEdge"?1:-1;VisualChildren.Add(child);}
    protected void RemoveVisualChild(Visual child)=>VisualChildren.Remove(child);
    protected override Size ArrangeOverride(Size size){var result=base.ArrangeOverride(size);_paint.Measure(size);_paint.Arrange(new Rect(size));return result;}
    public new void InvalidateVisual(){base.InvalidateVisual();_paint.InvalidateVisual();}
}
public class NativePaintGrid:UniformGrid
{
    private readonly NativePaintLayer _paint;
    public NativePaintGrid(){_paint=new NativePaintLayer(RenderNative){IsHitTestVisible=false,ZIndex=-2};VisualChildren.Insert(0,_paint);}
    protected virtual void RenderNative(NativeDrawingContext context){}
    protected override Size ArrangeOverride(Size size){var result=base.ArrangeOverride(size);_paint.Measure(size);_paint.Arrange(new Rect(size));return result;}
    public new void InvalidateVisual(){base.InvalidateVisual();_paint.InvalidateVisual();}
}

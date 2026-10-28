using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
namespace RocheScoreboard.Mac.Native;
public sealed class NativeDrawingContext(DrawingContext context,Size size=default) : IDisposable
{
    private readonly Stack<IDisposable> _states=[];
    public void DrawDrawing(Drawing drawing)=>drawing.Draw(context);
    public void DrawGeometry(IBrush? brush,IPen? pen,Geometry geometry)=>context.DrawGeometry(brush,pen,geometry);
    public void DrawRectangle(IBrush? brush,IPen? pen,Rect rect)=>context.DrawRectangle(brush,pen,rect);
    public void DrawRoundedRectangle(IBrush? brush,IPen? pen,Rect rect,double x,double y)=>context.DrawRectangle(brush,pen,rect,x,y);
    public void DrawEllipse(IBrush? brush,IPen? pen,Point point,double x,double y)=>context.DrawEllipse(brush,pen,point,x,y);
    public void DrawLine(IPen pen,Point a,Point b)=>context.DrawLine(pen,a,b);
    public void DrawText(NativeFormattedText text,Point point)=>context.DrawText(text.Text,point);
    public void DrawImage(IImage image,Rect rect)=>context.DrawImage(image,rect);
    public void PushClip(Geometry geometry)=>_states.Push(context.PushGeometryClip(geometry));
    public void PushClip(Rect rect)=>_states.Push(context.PushClip(rect));
    public void PushOpacity(double opacity)=>_states.Push(context.PushOpacity(opacity));
    public void PushOpacityMask(IBrush brush)=>_states.Push(context.PushOpacityMask(brush,new Rect(size.Width>0?size:new Size(10000,10000))));
    public void PushTransform(ITransform transform)=>_states.Push(context.PushTransform(transform.Value));
    public void Pop(){if(_states.Count>0)_states.Pop().Dispose();}
    public void Dispose(){while(_states.Count>0)Pop();}
    public static implicit operator NativeDrawingContext(DrawingContext context)=>new(context);
    public static void RenderBase(NativeDrawingContext context){}
}
public class NativeAdorner : Control
{
    public Control AdornedElement{get;}
    public NativeAdorner(Control target){AdornedElement=target;IsHitTestVisible=false;}
    protected override Size MeasureOverride(Size available)=>AdornedElement.Bounds.Size;
}
public sealed class NativeAdornerLayer(Avalonia.Controls.Primitives.AdornerLayer layer)
{
    public Avalonia.Controls.Controls Children=>layer.Children;
    public static NativeAdornerLayer? GetAdornerLayer(Control target)=>Avalonia.Controls.Primitives.AdornerLayer.GetAdornerLayer(target) is {} layer?new(layer):null;
    public void Add(NativeAdorner adorner){Avalonia.Controls.Primitives.AdornerLayer.SetAdornedElement(adorner,adorner.AdornedElement);layer.Children.Add(adorner);}
    public void Remove(NativeAdorner adorner)=>layer.Children.Remove(adorner);
}
public static class NativeSystemColors
{
    public static Color HighlightColor=>Color.Parse("#58A6FF");
    public static Color WindowColor=>Color.Parse("#161B22");
    public static Color WindowTextColor=>Colors.White;
    public static IBrush WindowBrush=>new SolidColorBrush(WindowColor);
    public static IBrush WindowTextBrush=>Brushes.White;
    public static IBrush HighlightTextBrush=>Brushes.White;
    public static IBrush HighlightBrush=>new SolidColorBrush(HighlightColor);
}
public static class NativeCursors
{
    public static Avalonia.Input.Cursor Hand{get;}=new(Avalonia.Input.StandardCursorType.Hand);
    public static Avalonia.Input.Cursor Arrow{get;}=new(Avalonia.Input.StandardCursorType.Arrow);
    public static Avalonia.Input.Cursor IBeam{get;}=new(Avalonia.Input.StandardCursorType.Ibeam);
}
public static class NativeDrawing
{
    public static GeometryDrawing Geometry(IBrush brush,IPen? pen,Geometry geometry)=>new(){Brush=brush,Pen=pen,Geometry=geometry};
}

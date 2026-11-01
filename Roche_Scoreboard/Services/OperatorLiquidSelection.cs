using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using SystemColors = System.Windows.SystemColors;

namespace Roche_Scoreboard.Services;

/// <summary>A single connected, paint-only liquid body shared by operator selection controls.</summary>
internal static class OperatorLiquidSelection
{
    // A dynamic resource dependency invalidates the surface when its theme changes.
    internal static readonly DependencyProperty ContextProperty=DependencyProperty.RegisterAttached("Context",typeof(Brush),typeof(OperatorLiquidSelection),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    internal readonly record struct Shape(Rect Bounds,double Deformation,double Direction);
    private static Brush Gradient(Color first,Color last)
    {var brush=new LinearGradientBrush(first,last,90);brush.Freeze();return brush;}
    private static Brush Solid(Color color){var brush=new SolidColorBrush(color);brush.Freeze();return brush;}
    private static Pen Rim(Brush brush){var pen=new Pen(brush,.8);pen.Freeze();return pen;}
    private static readonly Brush SegmentGlass=Gradient(Color.FromRgb(252,253,255),Color.FromRgb(197,210,228));
    private static readonly Brush SegmentSolid=Solid(Color.FromRgb(242,246,252));
    private static readonly Brush DarkGlass=Gradient(Color.FromArgb(110,211,232,253),Color.FromArgb(90,103,127,153));
    private static readonly Brush LightGlass=Gradient(Color.FromArgb(230,255,255,255),Color.FromArgb(220,186,204,225));
    private static readonly Brush DarkSolid=Solid(Color.FromRgb(57,76,96));
    private static readonly Brush LightSolid=Solid(Color.FromRgb(199,218,240));
    private static readonly Pen GlassRim=Rim(Gradient(Color.FromArgb(205,250,253,255),Color.FromArgb(65,158,190,224)));
    private static readonly Pen SolidRim=Rim(Solid(Color.FromRgb(147,169,192)));

    internal static Shape Calculate(Size viewport,double left,double right,double targetWidth,double leftVelocity,double rightVelocity,double inset,double stretchLimit,bool moving)
    {
        inset=Math.Max(0,inset);
        if(!double.IsFinite(viewport.Width+viewport.Height+left+right+targetWidth+leftVelocity+rightVelocity+inset+stretchLimit)
            || viewport.Width<2 || viewport.Height<=inset*2 || targetWidth<1)return new(Rect.Empty,0,0);
        double x=Math.Clamp(Math.Min(left,right),0,viewport.Width);
        double width=Math.Clamp(Math.Abs(right-left),0,viewport.Width-x);
        double maxWidth=targetWidth*Math.Max(1,stretchLimit);
        if(width>maxWidth){x+=(width-maxWidth)*.5;width=maxWidth;}
        double speed=(Math.Abs(leftVelocity)+Math.Abs(rightVelocity))/(targetWidth*22);
        double stretch=Math.Max(0,width/targetWidth-1)/Math.Max(.01,stretchLimit-1);
        double deformation=moving?Math.Clamp(speed*.65+stretch*.8,0,1):0;
        double baseHeight=viewport.Height-inset*2;
        double squeeze=Math.Min(3,baseHeight*.06)*deformation;
        double velocity=leftVelocity+rightVelocity;
        return new(new Rect(x,inset+squeeze,width,baseHeight-squeeze*2),deformation,Math.Clamp(velocity/(targetWidth*8),-1,1));
    }

    internal static Geometry Geometry(Shape shape)
    {
        var r=shape.Bounds;
        if(r.IsEmpty || r.Width<1 || r.Height<1 || !double.IsFinite(r.X+r.Y+r.Width+r.Height+shape.Deformation+shape.Direction))return System.Windows.Media.Geometry.Empty;
        double deform=Math.Clamp(shape.Deformation,0,1),direction=Math.Clamp(shape.Direction,-1,1);
        double ry=r.Height*.5,rx=Math.Min(r.Width*.5,ry);
        double leftRadius=rx*(1-deform*direction*.24),rightRadius=rx*(1+deform*direction*.24);
        double run=Math.Max(0,r.Width-leftRadius-rightRadius);
        double neck=Math.Min(8,r.Height*.16)*deform*Math.Min(1,run/Math.Max(1,ry));
        double middle=r.Left+leftRadius+run*(.5-direction*deform*.18),cy=r.Top+ry;
        const double k=.5522847498307936;
        var geometry=new StreamGeometry();
        using(var context=geometry.Open())
        {
            context.BeginFigure(new Point(r.Left+leftRadius,r.Top),true,true);
            context.BezierTo(new Point(r.Left+leftRadius+run*.20,r.Top),new Point(middle-run*.18,r.Top+neck),new Point(middle,r.Top+neck),true,false);
            context.BezierTo(new Point(middle+run*.18,r.Top+neck),new Point(r.Right-rightRadius-run*.20,r.Top),new Point(r.Right-rightRadius,r.Top),true,false);
            context.BezierTo(new Point(r.Right-rightRadius+rightRadius*k,r.Top),new Point(r.Right,cy-ry*k),new Point(r.Right,cy),true,false);
            context.BezierTo(new Point(r.Right,cy+ry*k),new Point(r.Right-rightRadius+rightRadius*k,r.Bottom),new Point(r.Right-rightRadius,r.Bottom),true,false);
            context.BezierTo(new Point(r.Right-rightRadius-run*.20,r.Bottom),new Point(middle+run*.18,r.Bottom-neck),new Point(middle,r.Bottom-neck),true,false);
            context.BezierTo(new Point(middle-run*.18,r.Bottom-neck),new Point(r.Left+leftRadius+run*.20,r.Bottom),new Point(r.Left+leftRadius,r.Bottom),true,false);
            context.BezierTo(new Point(r.Left+leftRadius-leftRadius*k,r.Bottom),new Point(r.Left,cy+ry*k),new Point(r.Left,cy),true,false);
            context.BezierTo(new Point(r.Left,cy-ry*k),new Point(r.Left+leftRadius-leftRadius*k,r.Top),new Point(r.Left+leftRadius,r.Top),true,false);
        }
        geometry.Freeze();return geometry;
    }

    internal static void Draw(DrawingContext context,FrameworkElement owner,Shape shape,bool segmented,Brush? fillOverride=null)
    {
        if(shape.Bounds.IsEmpty || shape.Bounds.Width<1)return;
        bool light=owner.GetValue(ContextProperty) is SolidColorBrush canvas && canvas.Color.R>150;
        bool contrast=SystemParameters.HighContrast;
        Brush fill=contrast?SystemColors.HighlightBrush:fillOverride??(segmented?(OperatorAppearance.GlassEnabled?SegmentGlass:SegmentSolid):OperatorAppearance.GlassEnabled?(light?LightGlass:DarkGlass):(light?LightSolid:DarkSolid));
        var pen=contrast?new Pen(SystemColors.HighlightTextBrush,1):OperatorAppearance.GlassEnabled?GlassRim:SolidRim;
        context.DrawGeometry(fill,pen,Geometry(shape));
    }
}

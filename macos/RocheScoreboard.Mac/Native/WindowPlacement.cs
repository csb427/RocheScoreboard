using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
namespace RocheScoreboard.Mac.Native;

/// <summary>Physical desktop coordinates and logical client dimensions remain separate.</summary>
public static class NativeWindowPlacement
{
    public readonly record struct Placement(PixelPoint Position,Size ClientSize);
    public static Placement Fit(PixelPoint position,Size client,PixelRect work,double scale,Size decorations)
    {
        scale=double.IsFinite(scale)&&scale>0?scale:1;
        double availableWidth=Math.Max(1,work.Width/scale-Math.Max(0,decorations.Width));
        double availableHeight=Math.Max(1,work.Height/scale-Math.Max(0,decorations.Height));
        double width=Math.Clamp(double.IsFinite(client.Width)&&client.Width>0?client.Width:900,1,availableWidth);
        double height=Math.Clamp(double.IsFinite(client.Height)&&client.Height>0?client.Height:700,1,availableHeight);
        int outerWidth=Math.Min(work.Width,Math.Max(1,(int)Math.Ceiling((width+decorations.Width)*scale)));
        int outerHeight=Math.Min(work.Height,Math.Max(1,(int)Math.Ceiling((height+decorations.Height)*scale)));
        return new(new PixelPoint(Math.Clamp(position.X,work.X,work.Right-outerWidth),Math.Clamp(position.Y,work.Y,work.Bottom-outerHeight)),new Size(width,height));
    }
}

/// <summary>Safety net for short displays; normal operator reflow and inner scrolling remain intact.</summary>
internal sealed class NativeAccessibleWindowContent:ScrollViewer
{
    protected override Type StyleKeyOverride=>typeof(ScrollViewer);
    private readonly Control _body;
    private readonly bool _operator;
    private readonly double _minimumWidth;
    public NativeAccessibleWindowContent(Control body,bool isOperator)
    {
        _body=body;_operator=isOperator;
        _minimumWidth=Math.Max(double.IsFinite(body.Width)?body.Width:0,Math.Max(body.MinWidth,isOperator?420:320));
        Content=body;HorizontalScrollBarVisibility=ScrollBarVisibility.Auto;VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
        HorizontalContentAlignment=Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalContentAlignment=Avalonia.Layout.VerticalAlignment.Stretch;
    }
    protected override Size MeasureOverride(Size available)
    {
        if(double.IsFinite(available.Width))_body.Width=Math.Max(_minimumWidth,available.Width);
        if((_operator||_body is ScrollViewer or UserControl)&&double.IsFinite(available.Height))_body.Height=Math.Max(0,available.Height);
        return base.MeasureOverride(available);
    }
}

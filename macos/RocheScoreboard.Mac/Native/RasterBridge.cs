using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
namespace RocheScoreboard.Mac.Native;
public sealed class NativeDrawingVisual:Control
{
    private readonly DrawingGroup _drawing=new();
    public NativeDrawingContext RenderOpen()=>new(_drawing.Open());
    public override void Render(DrawingContext context)=>_drawing.Draw(context);
}
public sealed class NativeRenderTargetBitmap:IImage,IDisposable
{
    private readonly RenderTargetBitmap _bitmap;
    public NativeRenderTargetBitmap(int width,int height,double x,double y,object? format=null)
    {_bitmap=new RenderTargetBitmap(new PixelSize(Math.Max(1,width),Math.Max(1,height)),new Vector(x,y));}
    public NativeRenderTargetBitmap(PixelSize size)=>_bitmap=new(size);
    internal IImageBrushSource NativeImage=>_bitmap;
    public int PixelWidth=>_bitmap.PixelSize.Width;
    public int PixelHeight=>_bitmap.PixelSize.Height;
    public Size Size=>_bitmap.Size;
    public void Render(Visual visual){if(visual is Control control&&(control.Bounds.Width<=0||control.Bounds.Height<=0)){control.Measure(_bitmap.Size);control.Arrange(new Rect(_bitmap.Size));}_bitmap.Render(visual);}
    public void Draw(DrawingContext context,Rect source,Rect destination)=>((IImage)_bitmap).Draw(context,source,destination);
    public void Save(Stream stream)=>_bitmap.Save(stream);
    public void Save(string path)=>_bitmap.Save(path);
    public void Dispose()=>_bitmap.Dispose();
}

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using Svg.Skia;

namespace RocheScoreboard.Mac.Native;

public enum BitmapCacheOption { OnLoad }
public static class PixelFormats { public static SKColorType Bgra32 => SKColorType.Bgra8888; }
public class NativeBitmap : IImage, IDisposable
{
    private Bitmap? _native;
    internal SKBitmap? Pixels;
    internal IImageBrushSource? NativeImage=>_native;
    public Uri? UriSource { get; set; }
    public Stream? StreamSource { get; set; }
    public BitmapCacheOption CacheOption { get; set; }
    public int DecodePixelHeight { get; set; }
    public int DecodePixelWidth { get; set; }
    public int PixelWidth => Pixels?.Width ?? 0;
    public int PixelHeight => Pixels?.Height ?? 0;
    public double Width => PixelWidth;
    public double Height => PixelHeight;
    public Size Size => new(Width, Height);
    public NativeBitmap() { }
    public NativeBitmap(Uri uri) { UriSource = uri; EndInit(); }
    internal NativeBitmap(SKBitmap pixels) => Assign(pixels);
    internal void Assign(SKBitmap pixels)
    {
        Pixels?.Dispose(); _native?.Dispose();
        Pixels = pixels;
        using var image = SKImage.FromBitmap(pixels);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = data.AsStream();
        _native = new Bitmap(stream);
    }
    public static NativeBitmap Create(int width, int height, double dpiX, double dpiY, SKColorType format, object? palette, Array pixels, int stride)
    {
        var bitmap = new SKBitmap(width, height, format, SKAlphaType.Unpremul);
        var bytes = new byte[Buffer.ByteLength(pixels)]; Buffer.BlockCopy(pixels, 0, bytes, 0, bytes.Length);
        for (int y = 0; y < height; y++) Marshal.Copy(bytes, y * stride, bitmap.GetPixels() + y * bitmap.RowBytes, width * 4);
        return new NativeBitmap(bitmap);
    }
    public void Save(Stream output)
    {
        using var image = SKImage.FromBitmap(Pixels!); using var data = image.Encode(SKEncodedImageFormat.Png, 100); data.SaveTo(output);
    }
    public void BeginInit() { }
    public void EndInit()
    {
        using Stream? file = StreamSource is null && UriSource is {} uri ? (uri.IsFile ? File.OpenRead(uri.LocalPath) : NativeAssets.Open(uri)) : null;
        Stream input = StreamSource ?? file ?? throw new InvalidOperationException("A local image path or input stream is required.");
        using var encoded = new MemoryStream(); input.CopyTo(encoded);
        byte[] bytes = encoded.ToArray();
        using var decoded = SKBitmap.Decode(bytes) ?? MacImageDecoder.Decode(bytes) ?? throw new InvalidDataException("Image could not be decoded by Skia or the Mac image codecs.");
        int width = decoded.Width, height = decoded.Height;
        if (DecodePixelHeight > 0) { width = Math.Max(1, (int)Math.Round(width * (double)DecodePixelHeight / height)); height = DecodePixelHeight; }
        else if (DecodePixelWidth > 0) { height = Math.Max(1, (int)Math.Round(height * (double)DecodePixelWidth / width)); width = DecodePixelWidth; }
        var pixels = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(pixels))
        using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
            canvas.DrawBitmap(decoded, new SKRect(0, 0, width, height), paint);
        Assign(pixels);
    }
    public void CopyPixels(byte[] buffer, int stride, int offset)
    {
        if (Pixels is null) throw new InvalidOperationException("Image has not been initialized.");
        for (int y = 0; y < PixelHeight; y++) Marshal.Copy(Pixels.GetPixels() + y * Pixels.RowBytes, buffer, offset + y * stride, PixelWidth * 4);
    }
    public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
        => ((IImage)(_native ?? throw new InvalidOperationException("Image not initialized."))).Draw(context, sourceRect, destRect);
    public void Dispose() { _native?.Dispose(); Pixels?.Dispose(); _native = null; Pixels = null; }
}
public sealed class NativeFormatConvertedBitmap : NativeBitmap
{
    public NativeFormatConvertedBitmap(NativeBitmap source, SKColorType format, object? palette, double threshold)
        : base(source.Pixels!.Copy(format)) { }
}
public sealed class NativeCroppedBitmap : NativeBitmap
{
    public NativeCroppedBitmap(NativeBitmap source, PixelRect crop)
    {
        using var subset = new SKBitmap();
        if (!source.Pixels!.ExtractSubset(subset, SKRectI.Create(crop.X, crop.Y, crop.Width, crop.Height)))
            throw new ArgumentOutOfRangeException(nameof(crop));
        Assign(subset.Copy());
    }
}
public static class NativeRaster
{
    public static byte[] RenderPixels(IImage source, int width, int height)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            return Avalonia.Threading.Dispatcher.UIThread.Invoke(() => RenderPixels(source, width, height));
        var image = new Avalonia.Controls.Image { Source = source, Width = width, Height = height, Stretch = Stretch.Fill };
        image.Measure(new Size(width, height)); image.Arrange(new Rect(0, 0, width, height));
        using var target = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        target.Render(image);
        using var stream = new MemoryStream(); target.Save(stream); stream.Position = 0;
        using var decoded = SKBitmap.Decode(stream);
        using var bitmap = decoded.Copy(SKColorType.Bgra8888);
        var bytes = new byte[width * height * 4];
        for (int row = 0; row < height; row++) Marshal.Copy(bitmap.GetPixels() + row * bitmap.RowBytes, bytes, row * width * 4, width * 4);
        return bytes;
    }
}
public static class NativeImageBrush
{
    private static IImageBrushSource Rasterize(IImage image)
    {
        if(!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            return Avalonia.Threading.Dispatcher.UIThread.Invoke(()=>Rasterize(image));
        double width=Math.Max(1,image.Size.Width),height=Math.Max(1,image.Size.Height);
        double scale=Math.Min(1,2048/Math.Max(width,height));
        int pixelsWide=Math.Max(1,(int)Math.Ceiling(width*scale)),pixelsHigh=Math.Max(1,(int)Math.Ceiling(height*scale));
        var visual=new Avalonia.Controls.Image{Source=image,Width=pixelsWide,Height=pixelsHigh,Stretch=Stretch.Fill};
        visual.Measure(new Size(pixelsWide,pixelsHigh));visual.Arrange(new Rect(0,0,pixelsWide,pixelsHigh));
        var raster=new RenderTargetBitmap(new PixelSize(pixelsWide,pixelsHigh));
        raster.Render(visual);return raster;
    }
    public static ImageBrush Create(IImage? image,Action<ImageBrush>? configure=null)
    {
        var brush=new ImageBrush(image switch{NativeBitmap b=>b.NativeImage,NativeRenderTargetBitmap b=>b.NativeImage,IImageBrushSource source=>source,null=>null,_=>Rasterize(image)});
        configure?.Invoke(brush);return brush;
    }
}
public static class NativeImmutability
{
    public static bool IsFrozen(object value)=>NativeFreezing.IsFrozen(value)||value is Avalonia.Media.IImmutableSolidColorBrush;
    public static SolidColorBrush Clone(this SolidColorBrush brush)=>new(brush.Color,brush.Opacity);
}

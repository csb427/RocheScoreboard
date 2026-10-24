using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using Svg.Skia;
using RocheScoreboard.Mac.Native;

namespace Roche_Scoreboard.Services;

internal static class ImageLoadHelper
{
    internal const string LogoFilter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tiff;*.tif;*.ico;*.wdp;*.svg;*.svgz|SVG Files|*.svg;*.svgz|All Files|*.*";
    internal static IImage? Load(string? path, int decodePixelHeight = 0)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            string ext = Path.GetExtension(path);
            if (ext.Equals(".svg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".svgz", StringComparison.OrdinalIgnoreCase))
            {
                var svg = new SKSvg();
                using var file = File.OpenRead(path);
                using var gzip = ext.Equals(".svgz", StringComparison.OrdinalIgnoreCase) ? new GZipStream(file, CompressionMode.Decompress) : null;
                svg.Load(gzip is null ? file : gzip);
                return svg.Picture is null ? null : new VectorImage(svg);
            }
            var image = new NativeBitmap { UriSource = new Uri(Path.GetFullPath(path)), DecodePixelHeight = decodePixelHeight };
            image.EndInit(); return image;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidDataException or InvalidOperationException)
        { System.Diagnostics.Debug.WriteLine($"Image load failed: {ex.Message}"); return null; }
    }
    internal static (int Width, int Height) GetTrimScanSize(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return (1, 1);
        double scale = Math.Min(256 / height, 512 / width);
        return (Math.Max(1, (int)Math.Min(512, width * scale)), Math.Max(1, (int)Math.Min(256, height * scale)));
    }
    internal static IImage? LoadTrimmed(string? path, int decodePixelHeight = 0)
    {
        var source = Load(path, decodePixelHeight);
        if (source is null) return null;
        try
        {
            var (width, height) = GetTrimScanSize(source.Size.Width, source.Size.Height);
            var image = new Image { Source = source, Width = width, Height = height, Stretch = Stretch.Fill };
            image.Measure(new Size(width, height)); image.Arrange(new Rect(0, 0, width, height));
            using var target = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
            target.Render(image);
            using var stream = new MemoryStream(); target.Save(stream); stream.Position = 0;
            using var bitmap = SKBitmap.Decode(stream);
            int minX = width, maxX = 0, minY = height, maxY = 0;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                if (bitmap.GetPixel(x, y).Alpha > 10)
                { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
            if (maxX < minX || maxY < minY) return source;
            double left = (double)minX / width, top = (double)minY / height;
            double right = (double)(width - 1 - maxX) / width, bottom = (double)(height - 1 - maxY) / height;
            if (left < .05 && top < .05 && right < .05 && bottom < .05) return source;
            return new ImageRegion(source, new Rect(left * source.Size.Width, top * source.Size.Height,
                (1 - left - right) * source.Size.Width, (1 - top - bottom) * source.Size.Height));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Optional logo crop failed: {ex.Message}"); return source; }
    }
    private sealed class ImageRegion(IImage source, Rect crop) : IImage
    {
        public Size Size => crop.Size;
        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
            => source.Draw(context, sourceRect.Translate(crop.Position - default(Point)), destRect);
    }
    private sealed class VectorImage(SKSvg svg) : IImage, IDisposable
    {
        public Size Size => new(svg.Picture!.CullRect.Width, svg.Picture.CullRect.Height);
        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
        {
            using (context.PushClip(destRect))
            using (context.PushTransform(Matrix.CreateTranslation(-sourceRect.X, -sourceRect.Y) *
                Matrix.CreateScale(destRect.Width / sourceRect.Width, destRect.Height / sourceRect.Height) * Matrix.CreateTranslation(destRect.X, destRect.Y)))
                context.Custom(new SvgOperation(svg.Picture!, new Rect(Size)));
        }
        public void Dispose() => svg.Dispose();
    }
    private sealed class SvgOperation(SKPicture picture, Rect bounds) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public void Dispose() { }
        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);
        public bool HitTest(Point point) => false;
        public void Render(ImmediateDrawingContext context)
        {
            using var lease = (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) as ISkiaSharpApiLeaseFeature)?.Lease();
            if (lease is null) throw new InvalidOperationException("SVG rendering requires the configured Skia renderer.");
            lease.SkCanvas.DrawPicture(picture);
        }
    }
}

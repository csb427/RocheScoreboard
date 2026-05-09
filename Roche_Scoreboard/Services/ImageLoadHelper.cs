using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace Roche_Scoreboard.Services;

/// <summary>
/// Centralised image loading for team logos, supporting raster formats and SVG.
/// </summary>
internal static class ImageLoadHelper
{
    /// <summary>
    /// File dialog filter covering all supported image types.
    /// </summary>
    internal const string LogoFilter =
        "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tiff;*.tif;*.ico;*.wdp;*.svg;*.svgz|" +
        "SVG Files|*.svg;*.svgz|" +
        "All Files|*.*";

    private static readonly string[] SvgExtensions = [".svg", ".svgz"];

    /// <summary>
    /// Loads any supported image file and returns a frozen <see cref="ImageSource"/>,
    /// or <see langword="null"/> if the path is invalid or the file cannot be decoded.
    /// </summary>
    /// <param name="path">Absolute path to the image file.</param>
    /// <param name="decodePixelHeight">
    /// Optional pixel height hint for raster images to limit memory.
    /// Ignored for SVG files. Pass 0 to decode at full resolution.
    /// </param>
    internal static ImageSource? Load(string? path, int decodePixelHeight = 0)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            string ext = Path.GetExtension(path);
            if (IsSvg(ext))
                return LoadSvg(path);

            return LoadRaster(path, decodePixelHeight);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSvg(string extension)
        => SvgExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    private static ImageSource? LoadRaster(string path, int decodePixelHeight)
    {
        BitmapImage bitmap = new();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (decodePixelHeight > 0)
            bitmap.DecodePixelHeight = decodePixelHeight;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static ImageSource? LoadSvg(string path)
    {
        WpfDrawingSettings settings = new()
        {
            IncludeRuntime = true,
            TextAsGeometry = false
        };

        using FileSvgReader reader = new(settings);
        DrawingGroup? drawing = reader.Read(path);
        if (drawing is null)
            return null;

        drawing.Freeze();
        DrawingImage image = new(drawing);
        image.Freeze();
        return image;
    }

    /// <summary>
    /// Loads an image and trims away transparent padding so the opaque content
    /// fills the available space. Returns <see langword="null"/> on failure.
    /// </summary>
    internal static ImageSource? LoadTrimmed(string? path, int decodePixelHeight = 0)
    {
        var source = Load(path, decodePixelHeight);
        if (source is null) return null;
        return TrimTransparent(source);
    }

    /// <summary>
    /// Crops an <see cref="ImageSource"/> to the tight bounding box of its
    /// non-transparent pixels. If the image is fully opaque or has no
    /// significant padding, it is returned unchanged.
    /// </summary>
    private static ImageSource TrimTransparent(ImageSource source)
    {
        // Render to a 256-pixel-high bitmap for scanning (small enough to be fast)
        const int scanHeight = 256;
        double aspect = source.Width / source.Height;
        int scanWidth = Math.Max(1, (int)(scanHeight * aspect));

        RenderTargetBitmap rtb = new(scanWidth, scanHeight, 96, 96, PixelFormats.Pbgra32);
        DrawingVisual dv = new();
        using (DrawingContext dc = dv.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, scanWidth, scanHeight));
        }
        rtb.Render(dv);

        byte[] pixels = new byte[scanWidth * scanHeight * 4];
        rtb.CopyPixels(pixels, scanWidth * 4, 0);

        // Find opaque bounding box (alpha > 10 threshold to ignore anti-alias fringes)
        const byte alphaThreshold = 10;
        int minX = scanWidth, maxX = 0, minY = scanHeight, maxY = 0;

        for (int y = 0; y < scanHeight; y++)
        {
            int rowOffset = y * scanWidth * 4;
            for (int x = 0; x < scanWidth; x++)
            {
                byte a = pixels[rowOffset + x * 4 + 3];
                if (a > alphaThreshold)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        // If no opaque pixels found or trimming is negligible (<5% each side), return original
        if (maxX < minX || maxY < minY)
            return source;

        double trimRatioL = (double)minX / scanWidth;
        double trimRatioR = (double)(scanWidth - 1 - maxX) / scanWidth;
        double trimRatioT = (double)minY / scanHeight;
        double trimRatioB = (double)(scanHeight - 1 - maxY) / scanHeight;

        if (trimRatioL < 0.03 && trimRatioR < 0.03 && trimRatioT < 0.03 && trimRatioB < 0.03)
            return source;

        // Add a tiny 2% margin so the content doesn't touch the clip edge
        const double margin = 0.02;
        double cropL = Math.Max(0, trimRatioL - margin);
        double cropT = Math.Max(0, trimRatioT - margin);
        double cropR = Math.Max(0, trimRatioR - margin);
        double cropB = Math.Max(0, trimRatioB - margin);

        // Build a cropped image using CroppedBitmap for raster, or a clipped DrawingImage
        Rect viewBox = new(
            cropL * source.Width,
            cropT * source.Height,
            source.Width * (1 - cropL - cropR),
            source.Height * (1 - cropT - cropB));

        DrawingVisual croppedVisual = new();
        using (DrawingContext dc = croppedVisual.RenderOpen())
        {
            dc.DrawImage(source, new Rect(-viewBox.X, -viewBox.Y, source.Width, source.Height));
        }

        DrawingGroup group = new();
        group.ClipGeometry = new RectangleGeometry(new Rect(0, 0, viewBox.Width, viewBox.Height));
        group.Children.Add(new ImageDrawing(source,
            new Rect(-viewBox.X, -viewBox.Y, source.Width, source.Height)));
        group.Freeze();

        DrawingImage result = new(group);
        result.Freeze();
        return result;
    }
}

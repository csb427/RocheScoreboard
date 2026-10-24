using System.Runtime.InteropServices;
using SkiaSharp;

namespace RocheScoreboard.Mac.Native;

// ImageIO supplies the system codecs that Skia does not include (notably TIFF).
internal static class MacImageDecoder
{
    private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string IO = "/System/Library/Frameworks/ImageIO.framework/ImageIO";
    private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    [StructLayout(LayoutKind.Sequential)] private readonly record struct Rect(double X, double Y, double Width, double Height);
    [DllImport(CF)] private static extern nint CFDataCreate(nint allocator, byte[] bytes, nint length);
    [DllImport(CF)] private static extern void CFRelease(nint value);
    [DllImport(IO)] private static extern nint CGImageSourceCreateWithData(nint data, nint options);
    [DllImport(IO)] private static extern nint CGImageSourceCreateImageAtIndex(nint source, nuint index, nint options);
    [DllImport(CG)] private static extern nuint CGImageGetWidth(nint image);
    [DllImport(CG)] private static extern nuint CGImageGetHeight(nint image);
    [DllImport(CG)] private static extern nint CGColorSpaceCreateDeviceRGB();
    [DllImport(CG)] private static extern nint CGBitmapContextCreate(nint data, nuint width, nuint height, nuint bits, nuint stride, nint colors, uint info);
    [DllImport(CG)] private static extern void CGContextTranslateCTM(nint context, double x, double y);
    [DllImport(CG)] private static extern void CGContextScaleCTM(nint context, double x, double y);
    [DllImport(CG)] private static extern void CGContextDrawImage(nint context, Rect rect, nint image);
    [DllImport(CG)] private static extern void CGContextRelease(nint context);
    [DllImport(CG)] private static extern void CGColorSpaceRelease(nint colors);
    [DllImport(CG)] private static extern void CGImageRelease(nint image);

    internal static SKBitmap? Decode(byte[] bytes)
    {
        if (!OperatingSystem.IsMacOS()) return null;
        nint data = 0, source = 0, image = 0, colors = 0, context = 0;
        SKBitmap? bitmap = null;
        try
        {
            data = CFDataCreate(0, bytes, bytes.Length);
            source = CGImageSourceCreateWithData(data, 0);
            if (source == 0) return null;
            image = CGImageSourceCreateImageAtIndex(source, 0, 0);
            if (image == 0) return null;
            int width = checked((int)CGImageGetWidth(image)), height = checked((int)CGImageGetHeight(image));
            bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            colors = CGColorSpaceCreateDeviceRGB();
            context = CGBitmapContextCreate(bitmap.GetPixels(), (nuint)width, (nuint)height, 8, (nuint)bitmap.RowBytes, colors, 0x2002);
            if (context == 0) { bitmap.Dispose(); return null; }
            CGContextTranslateCTM(context, 0, height);
            CGContextScaleCTM(context, 1, -1);
            CGContextDrawImage(context, new Rect(0, 0, width, height), image);
            var result = bitmap; bitmap = null; return result;
        }
        finally
        {
            bitmap?.Dispose();
            if (context != 0) CGContextRelease(context);
            if (colors != 0) CGColorSpaceRelease(colors);
            if (image != 0) CGImageRelease(image);
            if (source != 0) CFRelease(source);
            if (data != 0) CFRelease(data);
        }
    }
}

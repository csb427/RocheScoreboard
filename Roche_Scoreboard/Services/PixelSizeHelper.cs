using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Roche_Scoreboard.Services;

/// <summary>
/// Converts between WPF device-independent units and physical output pixels.
/// Scoreboard capture hardware is configured in pixels, so the operator-facing
/// dimension fields must not silently change meaning at 125%/150% Windows DPI.
/// </summary>
internal static class PixelSizeHelper
{
    private const double DefaultDpi = 96.0;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    internal static (double X, double Y) GetDpi(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            try
            {
                double dpi = GetDpiForWindow(hwnd);
                if (dpi > 0) return (dpi, dpi);
            }
            catch (EntryPointNotFoundException) { }
        }

        var dpiScale = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        return (dpiScale.PixelsPerInchX, dpiScale.PixelsPerInchY);
    }

    internal static void SetWindowPixels(Window window, int pixelWidth, int pixelHeight)
    {
        var (dpiX, dpiY) = GetDpi(window);
        window.Width = pixelWidth * DefaultDpi / dpiX;
        window.Height = pixelHeight * DefaultDpi / dpiY;
    }

    internal static (int Width, int Height) GetWindowPixels(Window window, double dipWidth, double dipHeight)
    {
        var (dpiX, dpiY) = GetDpi(window);
        return ((int)Math.Round(dipWidth * dpiX / DefaultDpi),
                (int)Math.Round(dipHeight * dpiY / DefaultDpi));
    }
}

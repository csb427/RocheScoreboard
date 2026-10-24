using Avalonia.Controls;
namespace Roche_Scoreboard.Services;
internal static class PixelSizeHelper
{
    internal static (double X, double Y) GetDpi(Window window) => (window.RenderScaling * 96, window.RenderScaling * 96);
    internal static void SetWindowPixels(Window window, int pixelWidth, int pixelHeight)
    { window.Width = pixelWidth / window.RenderScaling; window.Height = pixelHeight / window.RenderScaling; }
    internal static (int Width, int Height) GetWindowPixels(Window window, double dipWidth, double dipHeight)
        => ((int)Math.Round(dipWidth * window.RenderScaling), (int)Math.Round(dipHeight * window.RenderScaling));
}

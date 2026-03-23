using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace Roche_Scoreboard.Services;

/// <summary>
/// Provides luminance-based contrast detection to choose readable
/// foreground text (black or white) for any background colour.
/// Uses the WCAG 2.0 relative luminance formula.
/// </summary>
internal static class ContrastHelper
{
    private static readonly SolidColorBrush BlackBrush;
    private static readonly SolidColorBrush WhiteBrush;

    static ContrastHelper()
    {
        BlackBrush = CreateFrozenBrush(Colors.Black);
        WhiteBrush = CreateFrozenBrush(Colors.White);
    }

    /// <summary>
    /// Returns <see cref="Colors.Black"/> or <see cref="Colors.White"/>
    /// depending on whether <paramref name="background"/> is light or dark.
    /// </summary>
    internal static Color GetContrastForeground(Color background)
    {
        return GetRelativeLuminance(background) > 0.179 ? Colors.Black : Colors.White;
    }

    /// <summary>
    /// Returns a frozen <see cref="SolidColorBrush"/> (black or white)
    /// that contrasts with the given <paramref name="background"/>.
    /// </summary>
    internal static SolidColorBrush GetContrastBrush(Color background)
    {
        return GetRelativeLuminance(background) > 0.179 ? BlackBrush : WhiteBrush;
    }

    /// <summary>
    /// Computes the WCAG 2.0 relative luminance of a colour (0 = darkest, 1 = lightest).
    /// </summary>
    private static double GetRelativeLuminance(Color c)
    {
        double r = Linearize(c.R / 255.0);
        double g = Linearize(c.G / 255.0);
        double b = Linearize(c.B / 255.0);

        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    /// <summary>
    /// Converts an sRGB channel value (0–1) to linear RGB.
    /// </summary>
    private static double Linearize(double channel)
    {
        return channel <= 0.03928
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

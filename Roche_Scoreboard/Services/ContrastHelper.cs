using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace Roche_Scoreboard.Services;

/// <summary>
/// Provides luminance-based contrast detection to choose readable
/// foreground text for any background colour.
/// Uses the WCAG 2.0 relative luminance and contrast ratio formulae.
/// </summary>
internal static class ContrastHelper
{
    /// <summary>
    /// Minimum WCAG 2.0 contrast ratio for normal text readability (AA level).
    /// </summary>
    private const double MinContrastRatio = 4.5;

    private static readonly SolidColorBrush BlackBrush;
    private static readonly SolidColorBrush WhiteBrush;

    static ContrastHelper()
    {
        BlackBrush = CreateFrozenBrush(Colors.Black);
        WhiteBrush = CreateFrozenBrush(Colors.White);
    }

    /// <summary>
    /// Returns <paramref name="preferred"/> if it has sufficient contrast against
    /// <paramref name="background"/>; otherwise returns black or white, whichever
    /// provides the highest contrast ratio.
    /// </summary>
    internal static Color GetReadableTextColor(Color background, Color preferred)
    {
        if (ContrastRatio(background, preferred) >= MinContrastRatio)
            return preferred;

        return GetContrastForeground(background);
    }

    /// <summary>
    /// Brush-returning overload of <see cref="GetReadableTextColor"/>.
    /// </summary>
    internal static SolidColorBrush GetReadableTextBrush(Color background, Color preferred)
    {
        if (ContrastRatio(background, preferred) >= MinContrastRatio)
            return CreateFrozenBrush(preferred);

        return GetContrastBrush(background);
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
    /// Minimum contrast ratio for a border to be visibly distinguishable
    /// from its background. Lower than text readability — borders just need
    /// to read as a line, not be readable as glyphs.
    /// </summary>
    private const double MinBorderContrastRatio = 1.6;

    /// <summary>
    /// Picks a border colour that's visibly distinct from
    /// <paramref name="background"/>. If <paramref name="primary"/> already
    /// has enough contrast, it is returned. Otherwise <paramref name="secondary"/>
    /// is returned if it does. As a final fallback, returns black or white
    /// (whichever contrasts the background).
    /// <para>
    /// Solves the dark-team-on-dark-background problem: a Richmond/Collingwood
    /// black primary against the #0D1117 presentation background is invisible
    /// as a border, so we fall through to the team's secondary (typically
    /// yellow / white) which reads cleanly.
    /// </para>
    /// </summary>
    internal static Color GetVisibleBorderColor(Color background, Color primary, Color secondary)
    {
        if (ContrastRatio(background, primary) >= MinBorderContrastRatio)
            return primary;
        if (ContrastRatio(background, secondary) >= MinBorderContrastRatio)
            return secondary;
        return GetContrastForeground(background);
    }

    /// <summary>Brush-returning overload of <see cref="GetVisibleBorderColor"/>.</summary>
    internal static SolidColorBrush GetVisibleBorderBrush(Color background, Color primary, Color secondary)
        => CreateFrozenBrush(GetVisibleBorderColor(background, primary, secondary));

    /// <summary>
    /// Calculates the WCAG 2.0 contrast ratio between two colours (1:1 to 21:1).
    /// </summary>
    internal static double ContrastRatio(Color a, Color b)
    {
        double lumA = GetRelativeLuminance(a);
        double lumB = GetRelativeLuminance(b);

        double lighter = Math.Max(lumA, lumB);
        double darker = Math.Min(lumA, lumB);

        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// Computes the WCAG 2.0 relative luminance of a colour (0 = darkest, 1 = lightest).
    /// </summary>
    internal static double GetRelativeLuminance(Color c)
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

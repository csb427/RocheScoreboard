using System;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace Roche_Scoreboard.Services;

/// <summary>
/// Smart contrast helpers. Picks a readable foreground color (white or black,
/// optionally with an outline) for a given background, using the WCAG relative
/// luminance formula. Also supports analysing a gradient or image brush to
/// pick the worst-case foreground that still works across the whole area.
/// </summary>
public static class SmartContrast
{
    /// <summary>
    /// Returns a relative luminance value in [0, 1] for a sRGB color, per WCAG 2.x.
    /// </summary>
    public static double RelativeLuminance(Color c)
    {
        double R = Channel(c.R / 255.0);
        double G = Channel(c.G / 255.0);
        double B = Channel(c.B / 255.0);
        return 0.2126 * R + 0.7152 * G + 0.0722 * B;

        static double Channel(double v) =>
            v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    /// <summary>
    /// Returns the WCAG contrast ratio (1..21) between two colors.
    /// </summary>
    public static double ContrastRatio(Color a, Color b)
    {
        double la = RelativeLuminance(a);
        double lb = RelativeLuminance(b);
        double bright = Math.Max(la, lb);
        double dark = Math.Min(la, lb);
        return (bright + 0.05) / (dark + 0.05);
    }

    /// <summary>
    /// Picks white or black depending on which yields the higher contrast against
    /// the supplied background.
    /// </summary>
    public static Color PickForeground(Color background)
    {
        double whiteContrast = ContrastRatio(background, Colors.White);
        double blackContrast = ContrastRatio(background, Colors.Black);
        return whiteContrast >= blackContrast ? Colors.White : Colors.Black;
    }

    /// <summary>
    /// Picks the foreground whose worst contrast ratio is strongest across
    /// every supplied background. This is the safe choice for gradients,
    /// hover states and other surfaces whose colour is not constant.
    /// </summary>
    public static Color PickForeground(params Color[] backgrounds)
    {
        if (backgrounds is null || backgrounds.Length == 0)
            return Colors.White;

        double whiteMinimum = MinimumContrast(Colors.White, backgrounds);
        double blackMinimum = MinimumContrast(Colors.Black, backgrounds);
        return whiteMinimum >= blackMinimum ? Colors.White : Colors.Black;
    }

    /// <summary>
    /// Keeps a preferred brand colour only when it meets normal-text WCAG AA
    /// against every background; otherwise returns the safest black/white ink.
    /// </summary>
    public static Color PickReadableForeground(Color preferred, params Color[] backgrounds)
    {
        if (backgrounds is not null && backgrounds.Length > 0 &&
            MinimumContrast(preferred, backgrounds) >= 4.5)
        {
            return preferred;
        }

        return PickForeground(backgrounds ?? []);
    }

    /// <summary>
    /// Picks white or black for a brush; for gradient brushes the worst-case
    /// (lowest-contrast) midpoint is used so the result reads across the gradient.
    /// </summary>
    public static Color PickForeground(Brush? brush)
    {
        if (brush is null) return Colors.White;

        switch (brush)
        {
            case SolidColorBrush solid:
                return PickForeground(solid.Color);
            case GradientBrush grad when grad.GradientStops.Count > 0:
            {
                var stops = new Color[grad.GradientStops.Count];
                for (int i = 0; i < grad.GradientStops.Count; i++)
                    stops[i] = grad.GradientStops[i].Color;
                return PickForeground(stops);
            }
            default:
                return Colors.White;
        }
    }

    /// <summary>
    /// Returns true if the contrast ratio between fg and bg is below the WCAG AA
    /// threshold of 4.5:1 for normal text.
    /// </summary>
    public static bool IsLowContrast(Color fg, Color bg) => ContrastRatio(fg, bg) < 4.5;

    /// <summary>
    /// Convenience: returns a Brush wrapping <see cref="PickForeground(Brush?)"/>.
    /// </summary>
    public static SolidColorBrush PickForegroundBrush(Brush? background) =>
        new(PickForeground(background));

    /// <summary>
    /// Convenience: returns a Brush wrapping <see cref="PickForeground(Color)"/>.
    /// </summary>
    public static SolidColorBrush PickForegroundBrush(Color background) =>
        new(PickForeground(background));

    private static double MinimumContrast(Color foreground, Color[] backgrounds)
    {
        double minimum = double.MaxValue;
        foreach (Color background in backgrounds)
            minimum = Math.Min(minimum, ContrastRatio(foreground, background));
        return minimum;
    }
}

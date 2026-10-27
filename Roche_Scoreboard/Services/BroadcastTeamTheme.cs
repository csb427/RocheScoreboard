using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace Roche_Scoreboard.Services;

/// <summary>Keep decorative logo/gradient washes from defeating the selected text contrast.</summary>
internal static class BroadcastTeamTheme
{
    internal static double DecorationStrength(Color team)
    {
        Color text = ContrastHelper.GetContrastForeground(team);
        double strength = 1;
        while (strength > .001)
        {
            var (dark, light) = Extremes(team, strength);
            if (ContrastHelper.ContrastRatio(text, dark) >= 4.5 && ContrastHelper.ContrastRatio(text, light) >= 4.5)
                return strength;
            strength *= .65;
        }
        return 0;
    }

    internal static (Color Dark, Color Light) Extremes(Color team, double strength)
    {
        // Conservative bounds: cropped logo at 30%, plus 22% white / 15% black gradient.
        double dark = 1 - (1 - .3 * strength) * (1 - .15 * strength);
        double light = 1 - (1 - .3 * strength) * (1 - .22 * strength);
        return (Blend(team, Colors.Black, dark), Blend(team, Colors.White, light));
    }

    private static Color Blend(Color background, Color overlay, double opacity) => Color.FromRgb(
        (byte)Math.Round(background.R * (1 - opacity) + overlay.R * opacity),
        (byte)Math.Round(background.G * (1 - opacity) + overlay.G * opacity),
        (byte)Math.Round(background.B * (1 - opacity) + overlay.B * opacity));
}

using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace Roche_Scoreboard.Services;

/// <summary>Shared component-level finals colours used by every AFL presentation surface.</summary>
internal static class FinalsPalette
{
    public static readonly Color GoldLight = Color.FromRgb(0xF2, 0xD4, 0x7A);
    public static readonly Color GoldMid = Color.FromRgb(0xC9, 0x98, 0x2D);
    public static readonly Color GoldDark = Color.FromRgb(0xA9, 0x78, 0x1D);
    public static readonly Color Ink = Color.FromRgb(0x08, 0x06, 0x02);

    public static LinearGradientBrush PanelBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) };
        brush.GradientStops.Add(new GradientStop(GoldDark, 0));
        brush.GradientStops.Add(new GradientStop(GoldMid, 0.58));
        brush.GradientStops.Add(new GradientStop(GoldLight, 1));
        brush.Freeze();
        return brush;
    }

    public static LinearGradientBrush FrameBrush()
    {
        var brush = new LinearGradientBrush(GoldLight, GoldDark, 35);
        brush.Freeze();
        return brush;
    }
}

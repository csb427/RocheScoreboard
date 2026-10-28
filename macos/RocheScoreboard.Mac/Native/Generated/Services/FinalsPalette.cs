// Generated from Services/FinalsPalette.cs; edit translator/adapters.
// Source SHA-256: 2a9a27dbc74e1afe1e36e7b7774556708f7311e03bba39aa3a5aebb155a4122a
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Color = Avalonia.Media.Color;

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
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative) };
        brush.GradientStops.Add(new GradientStop(GoldDark, 0));
        brush.GradientStops.Add(new GradientStop(GoldMid, 0.58));
        brush.GradientStops.Add(new GradientStop(GoldLight, 1));
        brush.Freeze();
        return brush;
    }

    public static LinearGradientBrush FrameBrush()
    {
        var brush = NativeBrush.Linear(GoldLight, GoldDark, 35);
        brush.Freeze();
        return brush;
    }
}

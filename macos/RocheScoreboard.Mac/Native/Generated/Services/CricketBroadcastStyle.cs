// Generated from Services/CricketBroadcastStyle.cs; edit translator/adapters.
// Source SHA-256: e0d2f80e2062b5f3b0ca8f7d3f7a396888ac02df74155050277a550057ba7bac
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using FontFamily = Avalonia.Media.FontFamily;
using Color=Avalonia.Media.Color;
using ColorConverter=RocheScoreboard.Mac.Native.ColorConverter;

namespace Roche_Scoreboard.Services;

/// <summary>Cricket-only broadcast geometry and typography; operator/AFL styling stays independent.</summary>
internal static class CricketBroadcastStyle
{
    internal const double HeaderFraction = .65 / 5.65;
    internal const double OverlayFraction = .27;
    internal const double CompactTopFraction = OverlayFraction;
    internal static readonly TimeSpan OverlayMotionDuration=TimeSpan.FromMilliseconds(620);
    internal static IEasingFunction OverlayMotionEase()=>new SineEase {EasingMode=EasingMode.EaseInOut};
    internal static readonly FontFamily Typeface = new("Arial");
    internal static FontWeight Weight => FontWeight.Bold;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Primary, string Secondary), LinearGradientBrush> Gradients = new();
    internal static LinearGradientBrush Gradient(string? primary, string? secondary)
    {
        var key = (primary ?? "", secondary ?? "");
        if (Gradients.TryGetValue(key, out var existing)) return existing;
        // A club colour should remain that hue throughout the panel. Mixing a
        // blue primary with a red secondary created an unrelated purple wash.
        string colour=Usable(primary) ? primary! : Usable(secondary) ? secondary! : "#24486A";
        var brush = TeamVisuals.DarkBroadcast(colour, colour);
        if (Gradients.Count >= 64) Gradients.Clear();
        return Gradients.GetOrAdd(key, brush);
    }
    private static bool Usable(string? value)
    {
        try { return !string.IsNullOrWhiteSpace(value) && ColorConverter.ConvertFromString(value) is Color { A: >=128 }; }
        catch { return false; }
    }
    internal static ImageBrush? CenterLogo(IImage? source,double opacity=.16)
    {
        if(source is null) return null;
        var brush=NativeImageBrush.Create(source, brush => {brush.Stretch=Stretch.UniformToFill;brush.AlignmentX=AlignmentX.Center;brush.AlignmentY=AlignmentY.Center;brush.Opacity=opacity;});
        if(true) brush.Freeze(); return brush;
    }
}

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FontFamily = System.Windows.Media.FontFamily;
using Color=System.Windows.Media.Color;
using ColorConverter=System.Windows.Media.ColorConverter;

namespace Roche_Scoreboard.Services;

/// <summary>Cricket-only broadcast geometry and typography; operator/AFL styling stays independent.</summary>
internal static class CricketBroadcastStyle
{
    internal const double HeaderFraction = .65 / 5.65;
    internal const double OverlayFraction = .27;
    internal const double CompactTopFraction = OverlayFraction;
    internal static readonly TimeSpan OverlayMotionDuration=TimeSpan.FromMilliseconds(620);
    internal static IEasingFunction OverlayMotionEase()=>new SineEase {EasingMode=EasingMode.EaseInOut};
    internal static readonly FontFamily Typeface = new("Arial, Helvetica Neue, Helvetica, Liberation Sans");
    internal static FontWeight Weight => FontWeights.Bold;
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
    internal static ImageBrush? CenterLogo(ImageSource? source,double opacity=.16)
    {
        if(source is null) return null;
        var brush=new ImageBrush(source) { Stretch=Stretch.UniformToFill,AlignmentX=AlignmentX.Center,AlignmentY=AlignmentY.Center,Opacity=opacity };
        if(brush.CanFreeze) brush.Freeze(); return brush;
    }
}

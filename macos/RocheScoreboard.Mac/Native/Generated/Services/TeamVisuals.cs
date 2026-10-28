// Generated from Services/TeamVisuals.cs; edit translator/adapters.
// Source SHA-256: c0868a2b5ee1beb80e2300f92077212925dc15dc643fb5977ef0d5ecd841d585
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System.IO;
using Avalonia.Media.Imaging;
using Color = Avalonia.Media.Color;

namespace Roche_Scoreboard.Services;

internal static class TeamVisuals
{
    internal static Color Parse(string? hex, Color fallback)
    {
        try { return (Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString(hex ?? ""); }
        catch { return fallback; }
    }

    internal static LinearGradientBrush Panel(string? primary, string? secondary, double strength = .25)
    {
        Color shell = Color.FromRgb(0x16, 0x1B, 0x22);
        Color Blend(Color colour, double weight) => Color.FromRgb(
            (byte)(shell.R * (1 - weight) + colour.R * weight),
            (byte)(shell.G * (1 - weight) + colour.G * weight),
            (byte)(shell.B * (1 - weight) + colour.B * weight));
        return NativeBrush.Linear(Blend(Parse(primary, Colors.SteelBlue), strength),
            Blend(Parse(secondary, Colors.SteelBlue), strength * .55), 32);
    }

    internal static LinearGradientBrush Broadcast(string? primary, string? secondary = null)
    {
        Color team = Parse(primary, Colors.Navy);
        double strength = BroadcastTeamTheme.DecorationStrength(team);
        Color Mix(Color target, double amount) => Color.FromRgb(
            (byte)Math.Round(team.R * (1 - amount) + target.R * amount),
            (byte)Math.Round(team.G * (1 - amount) + target.G * amount),
            (byte)Math.Round(team.B * (1 - amount) + target.B * amount));
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative) };
        brush.GradientStops.Add(new GradientStop(Mix(Colors.White, .12 * strength), 0));
        brush.GradientStops.Add(new GradientStop(team, .4));
        Color end = Parse(secondary, Colors.Black);
        if (end == team) end = Colors.Black;
        brush.GradientStops.Add(new GradientStop(Mix(end, .16 * strength), 1));
        brush.Freeze();
        return brush;
    }

    // White foreground remains readable even where a white logo is painted at 22% opacity.
    internal static LinearGradientBrush DarkBroadcast(string? primary, string? secondary)
    {
        Color Safe(Color c)
        {
            Color Composite() => Color.FromRgb((byte)(c.R * .78 + 255 * .22), (byte)(c.G * .78 + 255 * .22), (byte)(c.B * .78 + 255 * .22));
            for (int pass = 0; pass < 80 && ContrastHelper.ContrastRatio(Colors.White, Composite()) < 4.5; pass++)
                c = Color.FromRgb((byte)(c.R * .94), (byte)(c.G * .94), (byte)(c.B * .94));
            return c;
        }
        Color first = Parse(primary, Colors.SteelBlue), second = Parse(secondary, Colors.Navy);
        Color end = Color.FromRgb((byte)(first.R * .55 + second.R * .2), (byte)(first.G * .55 + second.G * .2), (byte)(first.B * .55 + second.B * .2));
        var brush = NativeBrush.Linear(Safe(first), Safe(end), 30); brush.Freeze(); return brush;
    }

    internal static SolidColorBrush Accent(string? hex)
    {
        Color colour = Parse(hex, Colors.SteelBlue);
        return new SolidColorBrush(Color.FromRgb((byte)(colour.R * .7 + 76),
            (byte)(colour.G * .7 + 76), (byte)(colour.B * .7 + 76)));
    }

    internal static IImage? LoadLogo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var image = new NativeBitmap();
            image.BeginInit(); image.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 640;
            image.EndInit(); image.Freeze();
            // Trim transparent padding before centring/cropping a background logo.
            // Ignore near-transparent edge noise; never alter the source image on disk.
            var pixels = new NativeFormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int width = pixels.PixelWidth, height = pixels.PixelHeight, stride = width * 4;
            var buffer = new byte[stride * height]; pixels.CopyPixels(buffer, stride, 0);
            int left = width, right = -1, top = height, bottom = -1;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (buffer[y * stride + x * 4 + 3] > 20)
                    { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
            if (right < left || bottom < top) return image;
            left = Math.Max(0, left - 2); top = Math.Max(0, top - 2);
            right = Math.Min(width - 1, right + 2); bottom = Math.Min(height - 1, bottom + 2);
            var cropped = new NativeCroppedBitmap(image, new Avalonia.PixelRect(left, top, right - left + 1, bottom - top + 1));
            cropped.Freeze(); return cropped;
        }
        catch { return null; }
    }
}

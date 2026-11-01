using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using RocheScoreboard.Mac.Native;
using SkiaSharp;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private static double HeaderLuminance(double r, double g, double b)
    {
        static double Linear(double value) { value /= 255; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
        return .2126 * Linear(r) + .7152 * Linear(g) + .0722 * Linear(b);
    }
    private static async Task<double> HeaderBackdropContrast(NativeWindow window, TextBlock text, string path)
    {
        var ink = Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground);
        double alpha = ink.Opacity * ink.Color.A / 255d
            * text.GetVisualAncestors().Prepend(text).Aggregate(1d, (v, actor) => v * actor.Opacity);
        var rect = FinalsPainted(window, text);
        using (text.SetValue((AvaloniaProperty)Visual.OpacityProperty, 0d, BindingPriority.Animation))
        {
            await Settle(60);
            using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); frame.Save(path);
        }
        using var bitmap = SKBitmap.Decode(path); Assert.NotNull(bitmap);
        double sx = bitmap.Width / window.ClientSize.Width, sy = bitmap.Height / window.ClientSize.Height;
        double minimum = double.PositiveInfinity; int pixels = 0;
        for (int y = Math.Max(0, (int)Math.Ceiling(rect.Top * sy)); y < Math.Min(bitmap.Height, (int)Math.Floor(rect.Bottom * sy)); y++)
            for (int x = Math.Max(0, (int)Math.Ceiling(rect.Left * sx)); x < Math.Min(bitmap.Width, (int)Math.Floor(rect.Right * sx)); x++)
            {
                var bg = bitmap.GetPixel(x, y);
                double a = HeaderLuminance(bg.Red, bg.Green, bg.Blue);
                double b = HeaderLuminance(alpha * ink.Color.R + (1 - alpha) * bg.Red,
                    alpha * ink.Color.G + (1 - alpha) * bg.Green, alpha * ink.Color.B + (1 - alpha) * bg.Blue);
                minimum = Math.Min(minimum, (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05)); pixels++;
            }
        Assert.True(pixels > 0); return minimum;
    }
    [AvaloniaTheory]
    [InlineData(AppTheme.Dark, 1280, 850)] [InlineData(AppTheme.Light, 1280, 850)]
    [InlineData(AppTheme.Dark, 420, 320)] [InlineData(AppTheme.Light, 420, 320)]
    public async Task SetupTickerHeaderHasThemeReadableHierarchyAndSpecificDisplayGlyph(AppTheme theme, int width, int height)
    {
        await using var f = await AflFixture.Create(width, height);
        var app = Assert.IsType<App>(Application.Current); app.ThemeService.SetTheme(theme); await Settle(100);
        var wizard = await OpenActualAflMessageSetup(f);
        var title = Assert.Single(wizard.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Ticker messages");
        var description = Assert.Single(wizard.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Scroll across the bottom bar during the game");
        var header = title.GetVisualAncestors().OfType<StackPanel>().First(); header.BringIntoView(); await Settle(160);
        var viewport = Named<ScrollViewer>(wizard, "ResponsiveScrollHost");
        double amount = 1, previous = 0;
        for (int i = 0; i < 40 && !FinalsPainted(f.Window, viewport).Inflate(.01).Contains(FinalsPainted(f.Window, header)); i++)
        {
            var view = FinalsPainted(f.Window, viewport); var box = FinalsPainted(f.Window, header);
            double direction = box.Top < view.Top ? 1 : -1;
            if (previous != 0 && previous != direction) amount *= .5;
            previous = direction;
            var point = new Point(view.Left + 3, view.Center.Y);
            Assert.True(Within(viewport, f.Window.InputHitTest(point)), "Actual wheel input must reach the wizard viewport.");
            f.Window.MouseWheel(point, new Vector(0, direction * amount), RawInputModifiers.None); await Settle(60);
        }
        Assert.True(FinalsPainted(f.Window, viewport).Inflate(.01).Contains(FinalsPainted(f.Window, header)), "Whole header must fit the actual setup viewport.");
        string model = ManualWeatherModel(f);
        string dir = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") ?? Path.Combine(Path.GetTempPath(), "roche-ticker-header");
        Directory.CreateDirectory(dir); string prefix = Path.Combine(dir, $"header-{theme}-{width}-{height}");
        using (var frame = f.Window.CaptureRenderedFrame()) { Assert.NotNull(frame); frame.Save(prefix + ".png"); }
        double titleContrast = await HeaderBackdropContrast(f.Window, title, prefix + "-title-backing.png");
        double descriptionContrast = await HeaderBackdropContrast(f.Window, description, prefix + "-description-backing.png");
        var titleInk = Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground);
        var descriptionInk = Assert.IsAssignableFrom<ISolidColorBrush>(description.Foreground);
        var requiredTitle = Assert.IsAssignableFrom<ISolidColorBrush>(NativeResources.FindOrNull(title, "ControlCentre.TextBrush"));
        var requiredDescription = Assert.IsAssignableFrom<ISolidColorBrush>(NativeResources.FindOrNull(description, "ControlCentre.SecondaryTextBrush"));
        var icons = header.GetVisualDescendants().OfType<OperatorSymbol>().Where(s => Equals(s.Kind, "ticker")).ToArray();
        File.WriteAllText(prefix + ".json", JsonSerializer.Serialize(new { theme, width, height,
            titleContrast, descriptionContrast, titleInk = titleInk.Color.ToString(), descriptionInk = descriptionInk.Color.ToString(),
            expectedTitle = requiredTitle.Color.ToString(), expectedDescription = requiredDescription.Color.ToString(),
            description.FontSize, description.TextWrapping, iconCount = icons.Length,
            headerBounds = FinalsPainted(f.Window, header), viewport = FinalsPainted(f.Window, viewport),
            before = model, after = ManualWeatherModel(f) }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.Equal(model, ManualWeatherModel(f));
        Assert.True(titleContrast >= 3, $"Large heading contrast{titleContrast:F2} must reach3:1 over its actual backing.");
        Assert.True(descriptionContrast >= 4.5, $"Body description contrast{descriptionContrast:F2} must reach4.5:1 over actual backing.");
        Assert.Equal(requiredTitle.Color, titleInk.Color); Assert.Equal(requiredDescription.Color, descriptionInk.Color);
        Assert.True(description.FontSize >= 14); Assert.Equal(TextWrapping.Wrap, description.TextWrapping);
        var icon = Assert.Single(icons); Assert.False(icon.IsHitTestVisible); Assert.False(icon.Focusable);
        Assert.False((bool)icon.GetType().GetProperty("IsAnimating", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(icon)!);
    }

    [AvaloniaFact]
    public async Task TickerDisplayGlyphIsDrawnInsteadOfTheUnknownKindFallback()
    {
        var ticker = new OperatorSymbol { Kind = "ticker", Width = 28, Height = 28, Foreground = Brushes.White };
        var fallback = new OperatorSymbol { Kind = "zzz", Width = 28, Height = 28, Foreground = Brushes.White };
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 20, Background = Brushes.Black };
        row.Children.Add(ticker); row.Children.Add(fallback);
        var window = new NativeWindow { Width = 320, Height = 80, Content = row };
        try
        {
            window.Show(); await Settle(100);
            string dir = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") ?? Path.Combine(Path.GetTempPath(), "roche-ticker-glyph");
            Directory.CreateDirectory(dir); string path = Path.Combine(dir, "ticker-fallback.png");
            using (var frame = window.CaptureRenderedFrame()) { Assert.NotNull(frame); frame.Save(path); }
            using var bitmap = SKBitmap.Decode(path);
            byte[] Ink(OperatorSymbol symbol)
            {
                var box = FinalsPainted(window, symbol); var bytes = new List<byte>();
                double sx = bitmap.Width / window.ClientSize.Width, sy = bitmap.Height / window.ClientSize.Height;
                for (int y = (int)Math.Round(box.Top * sy); y < (int)Math.Round(box.Bottom * sy); y++)
                    for (int x = (int)Math.Round(box.Left * sx); x < (int)Math.Round(box.Right * sx); x++)
                    { var p = bitmap.GetPixel(x, y); bytes.Add(p.Red); bytes.Add(p.Green); bytes.Add(p.Blue); }
                return bytes.ToArray();
            }
            var actual = Ink(ticker); var reference = Ink(fallback);
            Assert.True(actual.Count(value => value > 128) > 20, "The ticker glyph must paint visible ink; blank output is not a distinct icon.");
            Assert.True(reference.Count(value => value > 128) > 20);
            Assert.NotEqual(Convert.ToHexString(SHA256.HashData(reference)), Convert.ToHexString(SHA256.HashData(actual)));
        }
        finally { window.Close(); }
    }
}

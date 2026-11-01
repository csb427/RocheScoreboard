using System.Text.Json;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class BreakRotationModeIsolationTests
{
    [AvaloniaTheory]
    [InlineData("Home", false, 1280, 850)] [InlineData("Home", true, 1280, 850)]
    [InlineData("Away", false, 1280, 850)] [InlineData("Away", true, 1280, 850)]
    [InlineData("Home", false, 420, 320)] [InlineData("Home", true, 420, 320)]
    [InlineData("Away", false, 420, 320)] [InlineData("Away", true, 420, 320)]
    public async Task ActualLiveLogoActionsHaveClearTeamNamesNativeIconsAndComfortableTargets(string side, bool clear, int width, int height)
    {
        await using var scope = new StorageScope(true, true); var f = await TeamSwatchOpenFromOwnedRecovery(scope);
        f.Main.Width = width; f.Main.Height = height; await Pump(100);
        string model = JsonSerializer.Serialize(f.Match.ToState()); var colours = TeamSwatchValues(f.Main);
        await TeamSwatchReachByOriginalTab(f.Main, side + "SecondaryColorPreview", "logo-actions");
        var row = Assert.IsType<StackPanel>(Named<TextBlock>(f.Main, side + "LogoStatusText").Parent);
        var target = Assert.Single(row.Children.OfType<Button>(), b => Equals(b.Content, clear ? "✕" : "📂 Logo"));
        // Original secondary swatch → sampler → Browse → Clear, real sequential Tab.
        for (int i = 0; i < (clear ? 3 : 2); i++) { RotationKey(f.Main, PhysicalKey.Tab); await Pump(20); }
        Assert.True(target.IsFocused); await RevealLiveUtility(f.Main, target);
        var bounds = SamplerPainted(f.Main, target); var peer = ControlAutomationPeer.CreatePeerForElement(target);
        string expected = $"{(clear ? "Remove" : "Choose")} {side.ToLowerInvariant()} team logo";
        TeamSwatchEvidence($"logo-action-{side}-{clear}-{width}-{height}", new { side, clear, width, height, bounds, name = peer?.GetName(), hasInvoke = peer is IInvokeProvider });
        TeamSwatchCapture(f.Main, $"logo-action-{side}-{clear}-{width}-{height}");
        Assert.True(bounds.Width >= 39.5 && bounds.Height >= 39.5, "Live logo action needs a40x40or larger comfortable target.");
        Assert.NotNull(peer); Assert.Equal(expected, peer.GetName()); Assert.IsAssignableFrom<IInvokeProvider>(peer);
        Assert.Contains(target.GetVisualDescendants().OfType<OperatorSymbol>(), icon => Equals(icon.Kind, clear ? "close" : "image"));
        Assert.Equal(model, JsonSerializer.Serialize(f.Match.ToState())); Assert.Equal(colours, TeamSwatchValues(f.Main));
        // File-dialog browsing is not invoked here. Actual Clear behavior has
        // separate owned-logo/crop isolation regressions in the final run.
    }
    [AvaloniaFact]
    public async Task LogoImageGlyphPaintsActualArtworkInsteadOfUnknownDocumentFallback()
    {
        var image = new OperatorSymbol { Kind = "image", Width = 32, Height = 32, Foreground = Avalonia.Media.Brushes.White };
        var unknown = new OperatorSymbol { Kind = "zzzzunknownzzzz", Width = 32, Height = 32, Foreground = Avalonia.Media.Brushes.White };
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 20, Background = Avalonia.Media.Brushes.Black };
        row.Children.Add(image); row.Children.Add(unknown);
        var window = new RocheScoreboard.Mac.Native.NativeWindow { Width = 240, Height = 80, Content = row };
        try
        {
            window.Show(); await Pump(100);
            string dir = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")!; Directory.CreateDirectory(dir); string path = Path.Combine(dir, "image-glyph-fallback.png");
            using (var frame = window.CaptureRenderedFrame()) { Assert.NotNull(frame); frame.Save(path); }
            using var bitmap = SkiaSharp.SKBitmap.Decode(path); Assert.NotNull(bitmap);
            byte[] Ink(Control icon)
            {
                var rect = SamplerPainted(window, icon); var bytes = new List<byte>();
                double sx = bitmap.Width / window.ClientSize.Width, sy = bitmap.Height / window.ClientSize.Height;
                for (int y = (int)Math.Round(rect.Top * sy); y < (int)Math.Round(rect.Bottom * sy); y++)
                    for (int x = (int)Math.Round(rect.Left * sx); x < (int)Math.Round(rect.Right * sx); x++)
                    { var pixel = bitmap.GetPixel(x, y); bytes.Add(pixel.Red); bytes.Add(pixel.Green); bytes.Add(pixel.Blue); }
                return bytes.ToArray();
            }
            var actual = Ink(image); var fallback = Ink(unknown);
            Assert.True(actual.Count(v => v > 128) > 20, "Image artwork must paint visible ink."); Assert.True(fallback.Count(v => v > 128) > 20);
            Assert.NotEqual(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fallback)), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(actual)));
        }
        finally { window.Close(); }
    }

}

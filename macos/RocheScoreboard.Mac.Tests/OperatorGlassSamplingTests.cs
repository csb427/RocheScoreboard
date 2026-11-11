using System.Reflection;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using SkiaSharp;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

/// <summary>Resolved live brush coordinates, separate from material source identity and rendered acceptance.</summary>
public sealed class OperatorGlassSamplingTests
{
    private static async Task Settle()
    {
        await Task.Delay(160);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class SamplingHost : IDisposable
    {
        internal readonly OperatorSceneBackdrop Scene = new();
        internal readonly OperatorGlassSurface Surface;
        internal readonly NativeWindow Window;

        internal SamplingHost(int width, int height, int left, int top, int sampleWidth, int sampleHeight)
        {
            Surface = new OperatorGlassSurface
            {
                Width = sampleWidth,
                Height = sampleHeight,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Margin = new Thickness(left, top, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(110, 20, 22, 28)),
                Material = GlassMaterial.Regular,
                GlassSource = Scene,
                Child = new TextBlock { Text = "Sharp foreground content" }
            };
            var stage = new Grid();
            stage.Children.Add(Scene);
            stage.Children.Add(Surface);
            Window = new NativeWindow { Width = width, Height = height, Content = stage };
            Window.Show();
        }

        internal VisualBrush AssertResolvedPixelSample()
        {
            Assert.True(Surface.IsEffectivelyVisible);
            Assert.True(Scene.Bounds.Width > Surface.Bounds.Width);
            Assert.True(Scene.Bounds.Height > Surface.Bounds.Height);
            Assert.False(Scene.IsHitTestVisible);
            var sample = Assert.IsType<VisualBrush>(typeof(OperatorGlassSurface)
                .GetField("_sample", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Surface));
            Assert.Same(Scene, sample.Visual);
            Assert.Equal(RelativeUnit.Absolute, sample.SourceRect.Unit);
            var location = Scene.PointToClient(Surface.PointToScreen(default));
            var expected = new Rect(location, Surface.Bounds.Size);
            Assert.True(expected.X > 1 && expected.Y > 1, "The real sample origin must exercise non-normalized pixel coordinates.");
            Assert.True(expected.Width > 1 && expected.Height > 1);
            Assert.Equal(expected, sample.SourceRect.Rect);
            return sample;
        }

        public void Dispose() => Window.Close();
    }

    private sealed class MovingContentHost : IDisposable
    {
        internal readonly NativeWindow Window;
        internal readonly Grid ContentLayer;
        internal readonly Border MovingObject;
        internal readonly OperatorGlassSurface Surface;

        internal MovingContentHost()
        {
            Window = new NativeWindow { Width = 340, Height = 220, Background = new SolidColorBrush(Color.Parse("#102030")) };
            ContentLayer = new Grid { Background = new SolidColorBrush(Color.Parse("#162B43")), IsHitTestVisible = false };
            MovingObject = new Border
            {
                Width = 60, Height = 44, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(50, 60, 0, 0),
                Background = new SolidColorBrush(Color.Parse("#F04424"))
            };
            ContentLayer.Children.Add(MovingObject);
            Surface = new OperatorGlassSurface
            {
                Width = 220, Height = 150, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(30, 30, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(74, 12, 18, 27)), CornerRadius = new CornerRadius(0),
                Material = GlassMaterial.Regular, GlassSource = ContentLayer
            };
            var stage = new Grid(); stage.Children.Add(ContentLayer); stage.Children.Add(Surface);
            Window.Content = stage; Window.Show();
        }

        internal SKColor Pixel(SKBitmap frame, double x, double y)
        {
            var point = Surface.TranslatePoint(new Point(x, y), Window);
            Assert.NotNull(point);
            double sx = frame.Width / Window.ClientSize.Width, sy = frame.Height / Window.ClientSize.Height;
            return frame.GetPixel(Math.Clamp((int)Math.Round(point.Value.X * sx), 0, frame.Width - 1),
                Math.Clamp((int)Math.Round(point.Value.Y * sy), 0, frame.Height - 1));
        }

        public void Dispose() => Window.Close();
    }

    private static SKBitmap Decode(WriteableBitmap frame)
    {
        string path = Path.Combine(Path.GetTempPath(), $"roche-glass-sample-{Guid.NewGuid():N}.png");
        try { frame.Save(path); return SKBitmap.Decode(path) ?? throw new InvalidOperationException("Could not decode the rendered sample frame."); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [AvaloniaTheory]
    [InlineData(680, 520, 43, 61, 180, 120)]
    [InlineData(420, 320, 29, 47, 260, 180)]
    public async Task ActualDecorativeBackdropSampleRetainsAbsolutePixelOriginAndExtent(
        int width, int height, int left, int top, int sampleWidth, int sampleHeight)
    {
        // This host does not configure global appearance, mutate preferences,
        // register a Cloud/session fixture, or require a capture directory.
        using var host = new SamplingHost(width, height, left, top, sampleWidth, sampleHeight);
        await Settle();
        host.AssertResolvedPixelSample();
    }

    [Fact]
    public void MissingGlassSampleUsesNeutralTransparentFallbackInsteadOfScalingWallpaper()
    {
        var fallback = Assert.IsAssignableFrom<ISolidColorBrush>(OperatorSceneBackdrop.FallbackBrush());
        Assert.Equal(Colors.Transparent, fallback.Color);
        Assert.True(fallback.Color.A == 0, "The compositor should reveal the actual local scene through its own tint when sampling is unavailable.");
    }

    [AvaloniaFact]
    public async Task ResizingAndRelocatingTheSurfaceUpdatesItsRetainedAbsoluteSampleRectangle()
    {
        using var host = new SamplingHost(680, 520, 43, 61, 180, 120);
        await Settle();
        var previous = host.AssertResolvedPixelSample();
        var previousRectangle = previous.SourceRect.Rect;
        int builds = host.Surface.LiveSampleBuildCount;
        int updates = host.Surface.LiveSampleUpdateCount;
        host.Surface.Margin = new Thickness(96, 102, 0, 0);
        host.Surface.Width = 214;
        host.Surface.Height = 142;
        await Settle();
        var current = host.AssertResolvedPixelSample();
        Assert.Same(previous, current);
        Assert.Equal(builds, host.Surface.LiveSampleBuildCount);
        Assert.True(host.Surface.LiveSampleUpdateCount > updates);
        Assert.NotEqual(previousRectangle, current.SourceRect.Rect);
        Assert.Equal(214, current.SourceRect.Rect.Width);
        Assert.Equal(142, current.SourceRect.Rect.Height);
    }

    [AvaloniaFact]
    public async Task MovingDistinctContentBehindGlassMovesTheRenderedSamplePatch()
    {
        Assert.False(NativeSystemPreferences.HighContrast, "Headless pixel evidence does not emulate Windows high contrast.");
        Assert.True(OperatorAppearance.GlassEnabled, "This pixel check exercises the translucent material path without changing preferences.");
        bool? previousMotion = OperatorMotion.AnimationOverride;
        OperatorMotion.AnimationOverride = false;
        using var host = new MovingContentHost();
        try
        {
            await Settle();
            using var beforeRendered = host.Window.CaptureRenderedFrame();
            Assert.NotNull(beforeRendered);
            using var beforeFrame = Decode(beforeRendered);
            string? captureDirectory = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(captureDirectory))
            {
                Directory.CreateDirectory(captureDirectory);
                beforeRendered.Save(Path.Combine(captureDirectory, "glass-underlay-before.png"));
            }
            SKColor beforeAtObject = host.Pixel(beforeFrame, 48, 50);
            SKColor beforeAtDestination = host.Pixel(beforeFrame, 148, 80);
            Assert.True(beforeAtObject.Red > beforeAtObject.Green + 35, $"Expected the moving red object under the sample patch, got {beforeAtObject}.");
            Assert.True(beforeAtDestination.Green > beforeAtDestination.Red + 8, $"Expected the blue content layer before the move, got {beforeAtDestination}.");

            host.MovingObject.Margin = new Thickness(150, 90, 0, 0);
            await Settle();
            using var afterRendered = host.Window.CaptureRenderedFrame();
            Assert.NotNull(afterRendered);
            using var afterFrame = Decode(afterRendered);
            if (!string.IsNullOrWhiteSpace(captureDirectory)) afterRendered.Save(Path.Combine(captureDirectory, "glass-underlay-after.png"));
            SKColor afterAtOldPosition = host.Pixel(afterFrame, 48, 50);
            SKColor afterAtNewPosition = host.Pixel(afterFrame, 148, 80);
            Assert.True(afterAtOldPosition.Green > afterAtOldPosition.Red + 8, $"The old local patch should now show the blue layer, got {afterAtOldPosition}.");
            Assert.True(afterAtNewPosition.Red > afterAtNewPosition.Green + 35, $"The new local patch should now contain the moved red object, got {afterAtNewPosition}.");
            Assert.False(host.Surface.HasCapturedBackdrop, "A dedicated sibling content layer should stay a live local brush, not a frozen whole-window snapshot.");
        }
        finally { OperatorMotion.AnimationOverride = previousMotion; }
    }
}

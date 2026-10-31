using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
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

    [AvaloniaFact]
    public async Task ResizingAndRelocatingTheSurfaceRebuildsItsActualAbsoluteSampleRectangle()
    {
        using var host = new SamplingHost(680, 520, 43, 61, 180, 120);
        await Settle();
        var previous = host.AssertResolvedPixelSample();
        var previousRectangle = previous.SourceRect.Rect;
        host.Surface.Margin = new Thickness(96, 102, 0, 0);
        host.Surface.Width = 214;
        host.Surface.Height = 142;
        await Settle();
        var current = host.AssertResolvedPixelSample();
        Assert.NotSame(previous, current);
        Assert.NotEqual(previousRectangle, current.SourceRect.Rect);
        Assert.Equal(214, current.SourceRect.Rect.Width);
        Assert.Equal(142, current.SourceRect.Rect.Height);
    }
}

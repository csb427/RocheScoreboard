using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using RocheScoreboard.Mac.Production;
using Xunit;
using System.Xml.Linq;

namespace RocheScoreboard.Mac.Tests;

public sealed class ProductionScorebugTests
{
    [AvaloniaFact]
    public void EveryNamedElementInTheProductionSourceIsRetained()
    {
        var view = new ProductionScorebug();
        var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "ProductionReferences", "ScorebugControl.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var names = source.Descendants().Select(e => e.Attribute(x + "Name")?.Value).Where(n => n is not null).ToArray();
        Assert.Equal(426, names.Length);
        foreach (string? name in names) Assert.NotNull(view.ProductionObject<AvaloniaObject>(name!));
    }

    [AvaloniaFact]
    public void OriginalNamedControlsBrushesTransformsAndColumnsRemainIndependent()
    {
        var first = new ProductionScorebug();
        var second = new ProductionScorebug();
        Assert.NotSame(first.ProductionObject<GradientStop>("ExpHomePanelGradStart"), second.ProductionObject<GradientStop>("ExpHomePanelGradStart"));
        Assert.NotSame(first.ProductionObject<TranslateTransform>("ExpHomeTotalFlip"), second.ProductionObject<TranslateTransform>("ExpHomeTotalFlip"));
        Assert.NotNull(first.ProductionObject<ColumnDefinition>("ExpClockColumn"));
        Assert.NotNull(first.ProductionObject<TextBlock>("ExpHomeTotalText"));
        Assert.NotNull(first.ProductionObject<Border>("ExpWarningBanner"));
        Assert.Same(((LinearGradientBrush)first.ProductionObject<Grid>("ExpHomePanel").Background!).GradientStops[0], first.ProductionObject<GradientStop>("ExpHomePanelGradStart"));
        Assert.False(first.ProductionObject<Grid>("ClassicLayout").IsVisible);
    }

    [AvaloniaFact]
    public void SharedEngineDataUpdatesTheProductionCellsAndPalette()
    {
        var session = new DemoSession(); session.Afl.AddGoal(Roche_Scoreboard.Models.TeamSide.Home);
        session.HomeColor = "#123456";
        var view = new ProductionScorebug(); view.RefreshFrom(session);
        Assert.Equal("6", view.ProductionObject<TextBlock>("ExpHomeTotalText").Text);
        Assert.Equal("1", view.ProductionObject<TextBlock>("ExpHomeGoalsText").Text);
        Assert.Equal("Q1", view.ProductionObject<TextBlock>("ExpQuarterNumberText").Text);
        Assert.Equal(Color.Parse("#123456"), view.ProductionObject<GradientStop>("ExpHomePanelGradStart").Color);
    }

    [AvaloniaFact]
    public void ProductionVisualTreeRendersAtTheOriginalOutputSizes()
    {
        var view = new ProductionScorebug();
        var window = new Window { Content = view, Width = 707, Height = 420 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            string dir = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") ?? Path.Combine(Path.GetTempPath(), "roche-mac-captures");
            Directory.CreateDirectory(dir);
            foreach (var size in new[] { new PixelSize(707, 420), new PixelSize(386, 193), new PixelSize(1280, 720) })
            {
                window.Width = size.Width; window.Height = size.Height;
                Dispatcher.UIThread.RunJobs();
                using var capture = window.CaptureRenderedFrame();
                Assert.NotNull(capture);
                capture.Save(Path.Combine(dir, $"production-scorebug-{size.Width}x{size.Height}.png"));
            }
        }
        finally { window.Close(); }
    }
}

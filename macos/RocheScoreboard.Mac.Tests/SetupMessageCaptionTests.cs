using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaTheory]
    [InlineData(AppTheme.Dark, 1280, 850)] [InlineData(AppTheme.Light, 1280, 850)]
    [InlineData(AppTheme.Dark, 420, 320)] [InlineData(AppTheme.Light, 420, 320)]
    public async Task SetupAnnouncementColourCaptionsStayReadableWithoutChangingCustomPreview(AppTheme theme, int width, int height)
    {
        await using var f = await AflFixture.Create(width, height);
        Assert.IsType<App>(Application.Current).ThemeService.SetTheme(theme); await Settle(100);
        var wizard = await OpenActualAflMessageSetup(f); AddSetupAnnouncement(wizard, "Original preview colour");
        var message = Assert.Single(MessageDraft(wizard)); message.TextColor = "#F0D020"; message.HighlightColor = "#310845";
        await Until(() => wizard.GetVisualDescendants().OfType<Button>().Any(b => ReferenceEquals(b.Tag, message)),
            "Actual added announcement row must be realized before measuring its captions.");
        string messages = TickerSlotState(MessageDraft(wizard)), model = ManualWeatherModel(f);
        var dir = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") ?? Path.Combine(Path.GetTempPath(), "roche-setup-captions");
        Directory.CreateDirectory(dir); var observations = new List<object>();
        foreach (bool saved in new[] { true, false })
            foreach (bool highlight in new[] { false, true })
            {
                Button button = saved ? MessageItemAction(wizard, message, highlight ? "highlight colour" : "text colour")
                    : Named<Button>(wizard, highlight ? "SetupNewMessageHighlightColorBtn" : "SetupNewMessageTextColorBtn");
                await RevealActualWizardCommand(f, wizard, button);
                var label = Assert.Single(button.GetVisualAncestors().OfType<StackPanel>().First().Children.OfType<TextBlock>());
                var viewport = Named<ScrollViewer>(wizard, "ResponsiveScrollHost");
                bool Ready()
                {
                    var visible = FinalsPainted(f.Window, viewport).Intersect(new Rect(f.Window.ClientSize));
                    return visible.Inflate(.01).Contains(FinalsPainted(f.Window, label))
                        && visible.Inflate(.01).Contains(FinalsPainted(f.Window, button))
                        && FinalsProbes(FinalsPainted(f.Window, button)).All(p => Within(button, f.Window.InputHitTest(p)));
                }
                double amount = 1, previous = 0;
                for (int i = 0; i < 40 && !Ready(); i++)
                {
                    var visible = FinalsPainted(f.Window, viewport).Intersect(new Rect(f.Window.ClientSize));
                    double direction = FinalsPainted(f.Window, label).Top < visible.Top ? 1 : -1;
                    if (previous != 0 && previous != direction) amount *= .5;
                    previous = direction; var point = new Point(visible.Left + 3, visible.Center.Y);
                    Assert.True(Within(viewport, f.Window.InputHitTest(point)));
                    f.Window.MouseWheel(point, new Vector(0, direction * amount), RawInputModifiers.None); await Settle(60);
                }
                Assert.True(Ready(), "Caption and all five command probes must share the actual unobscured wizard viewport.");
                var rect = FinalsPainted(f.Window, label);
                string prefix = Path.Combine(dir, $"caption-{theme}-{width}-{height}-{saved}-{highlight}");
                using (var frame = f.Window.CaptureRenderedFrame()) { Assert.NotNull(frame); frame.Save(prefix + ".png"); }
                double contrast = await HeaderBackdropContrast(f.Window, label, prefix + "-backing.png");
                observations.Add(new { saved, highlight, label.Text, label.FontSize, contrast, bounds = rect });
            }
        string record = Path.Combine(dir, $"captions-{theme}-{width}-{height}.json");
        File.WriteAllText(record, JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));
        using var recorded = JsonDocument.Parse(File.ReadAllText(record));
        foreach (var observation in recorded.RootElement.EnumerateArray())
        {
            Assert.True(observation.GetProperty("contrast").GetDouble() >= 4.5,
                $"{theme} {width}x{height} saved={observation.GetProperty("saved")} highlight={observation.GetProperty("highlight")}: caption contrast must reach 4.5:1.");
            Assert.True(observation.GetProperty("FontSize").GetDouble() >= 12);
        }
        var preview = Assert.Single(wizard.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == message.Text);
        Assert.Equal(Color.Parse("#F0D020"), Assert.IsAssignableFrom<ISolidColorBrush>(preview.Foreground).Color);
        Assert.Equal(Color.Parse("#310845"), Assert.IsAssignableFrom<ISolidColorBrush>(Assert.IsType<Border>(preview.Parent).Background).Color);
        Assert.Equal(messages, TickerSlotState(MessageDraft(wizard))); Assert.Equal(model, ManualWeatherModel(f));
    }
}

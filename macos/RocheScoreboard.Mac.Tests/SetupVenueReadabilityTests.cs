using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private static async Task RevealWizardReadingTarget(AflFixture f, UserControl wizard, Control target)
    {
        var viewport = Assert.Single(target.GetVisualAncestors().OfType<WizardViewport>());
        bool Ready()
        {
            using var frame = f.Window.CaptureRenderedFrame(); Assert.NotNull(frame);
            return FinalsPainted(f.Window, viewport).Intersect(new Rect(f.Window.ClientSize)).Inflate(.01)
                .Contains(FinalsPainted(f.Window, target));
        }
        double amount = 1, previous = 0;
        for (int i = 0; i < 50 && !Ready(); i++)
        {
            var visible = FinalsPainted(f.Window, viewport).Intersect(new Rect(f.Window.ClientSize));
            double direction = FinalsPainted(f.Window, target).Top < visible.Top ? 1 : -1;
            if (previous != 0 && previous != direction) amount *= .5;
            previous = direction;
            var candidates = new[] { new Point(visible.Left + 3, visible.Center.Y), new Point(visible.Right - 3, visible.Center.Y), visible.Center };
            var point = candidates.First(p => f.Window.InputHitTest(p) is Control hit && Within(viewport, hit)
                && hit is not ComboBox && !hit.GetVisualAncestors().OfType<ComboBox>().Any());
            f.Window.MouseWheel(point, new Vector(0, direction * amount), RawInputModifiers.None); await Settle(60);
        }
        Assert.True(Ready(), "The measured reading target must fit the actual unobscured wizard viewport.");
        await Settle(80);
    }
    [AvaloniaTheory]
    [InlineData(false, AppTheme.Dark, 1280, 850)] [InlineData(false, AppTheme.Light, 1280, 850)]
    [InlineData(false, AppTheme.Dark, 420, 320)] [InlineData(false, AppTheme.Light, 420, 320)]
    [InlineData(true, AppTheme.Dark, 1280, 850)] [InlineData(true, AppTheme.Light, 1280, 850)]
    [InlineData(true, AppTheme.Dark, 420, 320)] [InlineData(true, AppTheme.Light, 420, 320)]
    public async Task SetupVenueHeadingAndHelpHaveStableReadableBackingAndKeepOriginalEditor(bool cricket, AppTheme theme, int width, int height)
    {
        await using var f = await AflFixture.Create(width, height);
        Assert.IsType<App>(Application.Current).ThemeService.SetTheme(theme); await Settle(100);
        UserControl wizard;
        if (!cricket) wizard = await OpenActualAflMessageSetup(f);
        else
        {
            Assert.True(f.Window.OpenCommandBar()); Assert.True(f.Window.ExecuteCommand("home"));
            Assert.True(f.Window.OpenCommandBar()); Assert.True(f.Window.ExecuteCommand("start-game"));
            var chooser = Named<SetupWizard>(f.Window, "SetupWizardPanel");
            Named<RadioButton>(chooser, "CricketSportChoice").IsChecked = true;
            PresPlanProvider(Named<Button>(chooser, "NextButton")).Invoke();
            wizard = Named<CricketSetupWizard>(f.Window, "CricketSetupWizardPanel");
            await Until(() => wizard.IsEffectivelyVisible, "Sport choice must open actual cricket setup.");
            for (int step = 1; step < 4; step++)
            { PresPlanProvider(Named<Button>(wizard, "NextButton")).Invoke(); int expectedStep = step + 1;
                await Until(() => ManualWeatherRead<int>(wizard, "_currentStep") == expectedStep, "Cricket setup must reach Options through its original Next controls."); }
        }
        await Settle(300);
        await Until(() => wizard.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Weather at your venue"),
            "Loaded wizard must install its live weather section before reading measurements.");
        var title = Assert.Single(wizard.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Weather at your venue");
        var help = Assert.Single(wizard.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Search for the town or suburb nearest the ground.");
        var input = Named<ComboBox>(wizard, "SetupWeatherLocation");
        string model = ManualWeatherModel(f), originalText = input.Text ?? "";
        var prefix = Path.Combine(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") ?? Path.Combine(Path.GetTempPath(), "roche-venue-reading"), $"venue-{cricket}-{theme}-{width}-{height}");
        Directory.CreateDirectory(Path.GetDirectoryName(prefix)!);
        await RevealWizardReadingTarget(f, wizard, title);
        using (var frame = f.Window.CaptureRenderedFrame()) { Assert.NotNull(frame); frame.Save(prefix + ".png"); }
        double headingContrast = await HeaderBackdropContrast(f.Window, title, prefix + "-heading-backing.png");
        await RevealWizardReadingTarget(f, wizard, help);
        double helpContrast = await HeaderBackdropContrast(f.Window, help, prefix + "-help-backing.png");
        File.WriteAllText(prefix + ".json", JsonSerializer.Serialize(new { cricket, theme, width, height, headingContrast, helpContrast, title.FontSize, input.Text }));
        Assert.True(headingContrast >= 3, $"Venue heading contrast {headingContrast:F3} must reach 3:1.");
        Assert.True(helpContrast >= 4.5, $"Venue help contrast {helpContrast:F3} must reach 4.5:1.");
        var icon = Assert.Single(title.GetVisualAncestors().OfType<StackPanel>().First().Children.OfType<OperatorSymbol>());
        var expected = Assert.IsAssignableFrom<ISolidColorBrush>(NativeResources.FindOrNull(title, "ControlCentre.TextBrush"));
        Assert.Equal(expected.Color, Assert.IsAssignableFrom<ISolidColorBrush>(icon.Foreground).Color);
        await RevealWizardReadingTarget(f, wizard, input);
        Assert.True(input.Focus(NavigationMethod.Tab)); input.Text = "Pavilion 2 – eastern ground"; await Settle(60);
        Assert.Equal("Pavilion 2 – eastern ground", input.Text); Assert.Same(input, Named<ComboBox>(wizard, "SetupWeatherLocation"));
        Assert.Equal(model, ManualWeatherModel(f));
        input.Text = originalText;
    }
}

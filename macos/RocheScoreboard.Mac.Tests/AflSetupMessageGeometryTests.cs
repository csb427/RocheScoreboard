using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private static async Task<SetupWizard> OpenActualAflMessageSetup(AflFixture f)
    {
        Assert.True(f.Window.OpenCommandBar()); Assert.True(f.Window.ExecuteCommand("home"));
        Assert.True(f.Window.OpenCommandBar()); Assert.True(f.Window.ExecuteCommand("start-game"));
        var wizard = Named<SetupWizard>(f.Window, "SetupWizardPanel");
        Assert.True(Named<RadioButton>(wizard, "AflSportChoice").IsChecked);
        for (int step = 1; step <= 3; step++)
        {
            var next = Named<Button>(wizard, "NextButton");
            Assert.True(next.IsEffectivelyVisible && next.IsEffectivelyEnabled);
            PresPlanProvider(next).Invoke();
            int expected = step;
            await Until(() => ManualWeatherRead<int>(wizard, "_currentStep") == expected,
                "Actual universal Start Game must advance through its AFL setup steps.");
            if (step == 1)
            {
                Named<TextBox>(wizard, "SetupHomeNameBox").Text = "Geometry Home";
                Named<TextBox>(wizard, "SetupAwayNameBox").Text = "Geometry Away";
            }
        }
        await Until(() => Named<TextBox>(wizard, "SetupNewMessageBox").IsEffectivelyVisible,
            "Actual unified AFL Options must expose its message composer.");
        return wizard;
    }
    private static async Task RevealActualWizardCommand(AflFixture f, SetupWizard wizard, Button target)
    {
        Assert.True(target.IsEffectivelyVisible && target.IsEffectivelyEnabled);
        Assert.True(target.Focus(NavigationMethod.Tab)); target.BringIntoView(); await Settle(100);
        var viewport = Named<ScrollViewer>(wizard, "ResponsiveScrollHost");
        bool Ready()
        {
            using var frame = f.Window.CaptureRenderedFrame();
            var box = FinalsPainted(f.Window, target);
            return box.Width > 16 && box.Height > 16 && new Rect(f.Window.ClientSize).Contains(box)
                && FinalsProbes(box).All(p => Within(target, f.Window.InputHitTest(p)));
        }
        double amount = 1, previous = 0;
        for (int i = 0; i < 50 && !Ready(); i++)
        {
            var box = FinalsPainted(f.Window, target);
            var visible = FinalsPainted(f.Window, viewport).Intersect(new Rect(f.Window.ClientSize));
            var point = new Point(visible.Left + 3, visible.Center.Y);
            Assert.True(Within(viewport, f.Window.InputHitTest(point)), "Wheel probe must reach the actual wizard viewport.");
            bool top = Within(target, f.Window.InputHitTest(FinalsProbes(box)[3]));
            bool bottom = Within(target, f.Window.InputHitTest(FinalsProbes(box)[4]));
            double direction = !top && bottom ? 1 : top && !bottom ? -1 : box.Center.Y < point.Y ? 1 : -1;
            if (previous != 0 && previous != direction) amount *= .5;
            previous = direction;
            f.Window.MouseWheel(point, new Vector(0, direction * amount), RawInputModifiers.None); await Settle(80);
        }
        Assert.True(Ready(), "All five painted command probes must be reachable in the actual unified wizard.");
    }

    [AvaloniaTheory]
    [InlineData(1280, 850, "text colour", false, 0)]
    [InlineData(1280, 850, "highlight colour", false, 0)]
    [InlineData(1280, 850, "Remove this message", false, 0)]
    [InlineData(420, 320, "text colour", false, 0)]
    [InlineData(420, 320, "highlight colour", false, 0)]
    [InlineData(420, 320, "Remove this message", false, 0)]
    [InlineData(1280, 850, "SetupNewMessageTextColorBtn", true, 0)]
    [InlineData(1280, 850, "SetupNewMessageHighlightColorBtn", true, 0)]
    [InlineData(420, 320, "SetupNewMessageTextColorBtn", true, 0)]
    [InlineData(420, 320, "SetupNewMessageHighlightColorBtn", true, 0)]
    [InlineData(1280, 850, "Remove this message", false, 7)]
    [InlineData(420, 320, "Remove this message", false, 7)]
    public async Task AflSetupMessageCommandsFitActualUnifiedNormalAndCompactWindows(int width, int height, string purpose, bool composer, int index)
    {
        await using var f = await AflFixture.Create(width, height);
        var wizard = await OpenActualAflMessageSetup(f);
        var messages = MessageDraft(wizard);
        for (int i = 0; i <= index; i++) messages.Add(new MarqueeMessage($"Announcement {i + 1}"));
        await Settle(80);
        Button target = composer ? Named<Button>(wizard, purpose) : MessageItemAction(wizard, messages[index], purpose);
        string model = ManualWeatherModel(f), draft = TickerSlotState(messages);
        await RevealActualWizardCommand(f, wizard, target);
        var box = FinalsPainted(f.Window, target);
        if (Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir); string name = $"{width}-{height}-{index}-{purpose.Replace(' ', '-')}";
            using var frame = f.Window.CaptureRenderedFrame(); Assert.NotNull(frame); frame.Save(Path.Combine(dir, name + ".png"));
            File.WriteAllText(Path.Combine(dir, name + ".json"), JsonSerializer.Serialize(new
            {
                width, height, purpose, index, f.Window.ClientSize, Painted = box,
                Probes = FinalsProbes(box).Select(p => new { p, Hit = Within(target, f.Window.InputHitTest(p)) })
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Assert.True(box.Width >= 40 && box.Height >= 40,
            $"{purpose} needs at least 40x40 DIPs; actual {box.Width:F2}x{box.Height:F2}.");
        Assert.Equal(draft, TickerSlotState(messages)); Assert.Equal(model, ManualWeatherModel(f)); Assert.True(target.IsFocused);
    }
}

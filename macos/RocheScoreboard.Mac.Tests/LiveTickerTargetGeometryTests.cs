using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaTheory]
    [InlineData(1280, 850, "Text colour", 0)]
    [InlineData(1280, 850, "Highlight colour", 0)]
    [InlineData(1280, 850, "Clear this preset slot", 0)]
    [InlineData(420, 320, "Text colour", 0)]
    [InlineData(420, 320, "Highlight colour", 0)]
    [InlineData(420, 320, "Clear this preset slot", 0)]
    [InlineData(1280, 850, "Clear this preset slot", 7)]
    [InlineData(420, 320, "Clear this preset slot", 7)]
    public async Task LiveTickerSlotCommandsHaveComfortablePaintedTargetsAndScrollReachability(int width, int height, string purpose, int index)
    {
        await using var f = await AflFixture.Create(width, height);
        var slots = await PrepareLiveTicker(f);
        if (index == 7) { slots[index].Text = "Last saved announcement"; slots[index].IsEnabled = false; await Settle(80); }
        Button target = LiveTickerItemAction(f, slots[index], purpose);
        string state = ManualWeatherModel(f), messages = TickerSlotState(slots);
        await PresPlanReveal(f, target);
        var box = FinalsPainted(f.Window, target);
        string name = $"{width}-{height}-{index}-{purpose.Replace(' ', '-')}";
        if (Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            using var frame = f.Window.CaptureRenderedFrame(); Assert.NotNull(frame);
            frame.Save(Path.Combine(dir, name + ".png"));
            File.WriteAllText(Path.Combine(dir, name + ".json"), JsonSerializer.Serialize(new
            {
                width, height, index, purpose, f.Window.ClientSize, target.Bounds, Painted = box,
                Probes = FinalsProbes(box).Select(point => new { point, Hit = Within(target, f.Window.InputHitTest(point)) }),
                target.IsFocused, Slots = slots.Count
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Assert.True(box.Width >= 40 && box.Height >= 40,
            $"{purpose} needs at least40x40DIPs of painted input area; actual {box.Width:F2}x{box.Height:F2}.");
        Assert.Equal(messages, TickerSlotState(slots)); Assert.Equal(state, ManualWeatherModel(f));
        Assert.Equal(8, slots.Count); Assert.True(target.IsFocused);
    }
}

using System.Text.Json;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaTheory]
    [InlineData(0, 1280, 850)] [InlineData(1, 1280, 850)] [InlineData(2, 1280, 850)]
    [InlineData(0, 420, 320)] [InlineData(1, 420, 320)] [InlineData(2, 420, 320)]
    public async Task OriginalExactClockEntryHasNamedReachableTargetsAndStillAppliesOnlyOnSet(int index, int width, int height)
    {
        await using var f = await AflFixture.Create(width, height); await OpenClockSection(f, width == 420);
        f.Match.SetElapsed(TimeSpan.FromMinutes(2));
        var minutes = Named<TextBox>(f.Window, "ClockSetMinutesBox"); var seconds = Named<TextBox>(f.Window, "ClockSetSecondsBox");
        var expander = Assert.Single(minutes.GetLogicalAncestors().OfType<Expander>());
        expander.ApplyTemplate(); await Settle(80); Capture(f.Window, $"exact-clock-header-{width}-{height}");
        var header = Assert.Single(expander.GetVisualDescendants().OfType<ToggleButton>());
        Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(header)).Toggle();
        await Until(() => expander.IsExpanded && ReferenceEquals(TopLevel.GetTopLevel(minutes), f.Window)
            && minutes.IsEffectivelyVisible && minutes.Bounds.Height > 0,
            "Header expansion must actually attach and lay out the original entry, not report detached content as visible.");
        var apply = Assert.Single(expander.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "SET"));
        Control[] controls = [minutes, seconds, apply]; var target = controls[index];
        Assert.True(header.Focus(NavigationMethod.Tab));
        for (int n = 0; n < 35 && !target.IsFocused; n++) { Key(f.Window, PhysicalKey.Tab); await Settle(25); }
        Assert.True(target.IsFocused); await Settle(100); Capture(f.Window, $"exact-clock-{index}-{width}-{height}");
        var matrix = target.TransformToVisual(f.Window); Assert.NotNull(matrix);
        var bounds = new Rect(matrix.Value.Transform(default), matrix.Value.Transform(new Point(target.Bounds.Width, target.Bounds.Height)));
        var peer = ControlAutomationPeer.CreatePeerForElement(target);
        DeltaOwnerRecord($"exact-clock-{index}-{width}-{height}", new { index, width, height, bounds, name = peer?.GetName() });
        Assert.True(bounds.Width >= 39.5 && bounds.Height >= 39.5, "Exact-clock input and apply must have40px usable targets.");
        Assert.True(new Rect(f.Window.ClientSize).Contains(bounds));
        foreach (var p in new[] { bounds.Center, new Point(bounds.Left + 4, bounds.Center.Y), new Point(bounds.Right - 4, bounds.Center.Y), new Point(bounds.Center.X, bounds.Top + 4), new Point(bounds.Center.X, bounds.Bottom - 4) })
            Assert.True(f.Window.InputHitTest(p) is Visual hit && (ReferenceEquals(hit, target) || hit.GetVisualAncestors().Contains(target)), "The focused original target must own all5pointer probes.");
        string[] names = ["Scoreboard clock minutes", "Scoreboard clock seconds", "Set scoreboard clock"];
        Assert.Equal(names[index], peer?.GetName());
        string before = ManualWeatherModel(f); string history = JsonSerializer.Serialize(f.Match.Events); var duration = f.Match.QuarterDuration;
        minutes.Text = "02"; seconds.Text = "30"; Assert.Equal(before, ManualWeatherModel(f));
        for (int n = 0; n < 35 && !apply.IsFocused; n++) { Key(f.Window, PhysicalKey.Tab); await Settle(25); }
        Assert.True(apply.IsFocused); Key(f.Window, PhysicalKey.Enter);
        await Until(() => f.Match.DisplayClock == TimeSpan.FromSeconds(150), "Original SET Enter must commit the exact count-up clock value.");
        Assert.Equal(history, JsonSerializer.Serialize(f.Match.Events)); Assert.Equal(duration, f.Match.QuarterDuration);
        Assert.False(f.Match.ClockRunning); Assert.False(Named<TextBlock>(f.Window, "ClockSetError").IsVisible);
    }
}

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
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaTheory]
    [InlineData(0, 1280, 850)] [InlineData(1, 1280, 850)]
    [InlineData(2, 1280, 850)] [InlineData(3, 1280, 850)]
    [InlineData(0, 420, 320)] [InlineData(1, 420, 320)]
    [InlineData(2, 420, 320)] [InlineData(3, 420, 320)]
    public async Task LiveQuarterDurationStepperIsNamedReachableAndKeepsExistingUnitSemantics(int index, int width, int height)
    {
        await using var f = await AflFixture.Create(width, height);
        await OpenClockSection(f, width == 420);
        var down = VisibleChoice(f, "SettingsCountdownCard");
        Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(down)).Select();
        var panel = Named<StackPanel>(f.Window, "QuarterDurationPanel");
        await Until(() => panel.IsEffectivelyVisible, "Actual countdown selection must reveal its real duration editor.");
        var actions = panel.GetVisualDescendants().OfType<OperatorActionBorder>().ToArray(); Assert.Equal(4, actions.Length);
        var target = actions[index]; var minutes = Named<TextBox>(f.Window, "QuarterMinutesBox");
        var seconds = Named<TextBox>(f.Window, "QuarterSecondsBox");
        Assert.Equal("20", minutes.Text); Assert.Equal("00", seconds.Text);
        string history = JsonSerializer.Serialize(f.Match.Events); var elapsed = f.Match.ElapsedInQuarter;
        // Real Tab traverses the existing tree and triggers the viewport's
        // keyboard reveal. No forced visibility, private click or Apply call.
        Assert.True(down.Focus(NavigationMethod.Tab));
        for (int count = 0; count < 35 && !target.IsFocused; count++) { Key(f.Window, PhysicalKey.Tab); await Settle(25); }
        Assert.True(target.IsFocused, "Each existing unit stepper must be independently reachable by Tab.");
        await Settle(100); Capture(f.Window, $"quarter-step-{index}-{width}-{height}");
        var transform = target.TransformToVisual(f.Window); Assert.NotNull(transform);
        var bounds = new Rect(transform.Value.Transform(default), transform.Value.Transform(new Point(target.Bounds.Width, target.Bounds.Height)));
        var peer = ControlAutomationPeer.CreatePeerForElement(target);
        var probes = new[] { bounds.Center, new Point(bounds.Left + 3, bounds.Center.Y), new Point(bounds.Right - 3, bounds.Center.Y), new Point(bounds.Center.X, bounds.Top + 3), new Point(bounds.Center.X, bounds.Bottom - 3) };
        DeltaOwnerRecord($"quarter-step-{index}-{width}-{height}", new { index, width, height, bounds, name = peer?.GetName(),
            hits = probes.Select(p => new { point = p, hit = f.Window.InputHitTest(p)?.GetType().FullName,
                ancestors = f.Window.InputHitTest(p) is Visual v ? string.Join("/", v.GetVisualAncestors().OfType<Control>().Select(c => c.GetType().Name + ":" + c.Name)) : "none" }) });
        Assert.True(bounds.Width >= 39.5 && bounds.Height >= 39.5, "Minute/second stepper needs a40px accessible target.");
        Assert.True(new Rect(f.Window.ClientSize).Contains(bounds));
        foreach (var p in probes)
            Assert.True(f.Window.InputHitTest(p) is Visual hit && (ReferenceEquals(hit, target) || hit.GetVisualAncestors().Contains(target)), "All five target probes must reach the intended stepper, not a header/clip.");
        string[] names = ["Decrease quarter duration minutes", "Increase quarter duration minutes", "Decrease quarter duration seconds by five", "Increase quarter duration seconds by five"];
        Assert.Equal(names[index], peer?.GetName()); Assert.IsAssignableFrom<IInvokeProvider>(peer);
        Assert.Equal(new Thickness(2), target.BorderThickness); // Authored keyboard focus stays visible inside the hit target.
        Key(f.Window, PhysicalKey.Enter);
        int[] expected = [19 * 60, 21 * 60, 20 * 60 + 55, 20 * 60 + 5];
        await Until(() => f.Match.QuarterDuration == TimeSpan.FromSeconds(expected[index]), "Original keyboard action must apply the existing field-specific step, including seconds wrap.");
        Assert.Equal(history, JsonSerializer.Serialize(f.Match.Events)); Assert.Equal(elapsed, f.Match.ElapsedInQuarter);
        Assert.False(f.Match.ClockRunning); Assert.False(Named<TextBlock>(f.Window, "QuarterDurationError").IsVisible);
    }
}

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
    [InlineData(true)] [InlineData(false)]
    public async Task CompactExactClockRejectionKeepsFocusedDraftAndFullErrorReachableWithoutMutation(bool badMinutes)
    {
        await using var f = await AflFixture.Create(420, 320); await OpenClockSection(f, true);
        var minutes = Named<TextBox>(f.Window, "ClockSetMinutesBox"); var seconds = Named<TextBox>(f.Window, "ClockSetSecondsBox");
        var expander = Assert.Single(minutes.GetLogicalAncestors().OfType<Expander>()); expander.ApplyTemplate();
        var header = Assert.Single(expander.GetVisualDescendants().OfType<ToggleButton>());
        Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(header)).Toggle();
        await Until(() => expander.IsExpanded && ReferenceEquals(TopLevel.GetTopLevel(minutes), f.Window)
            && minutes.IsEffectivelyVisible && minutes.Bounds.Height > 0, "Original expanded entry must actually attach.");
        var apply = Assert.Single(expander.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "SET"));
        minutes.Text = badMinutes ? "bad" : "02"; seconds.Text = badMinutes ? "30" : "60";
        string before = ManualWeatherModel(f);
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(apply)).Invoke();
        var error = Named<TextBlock>(f.Window, "ClockSetError");
        await Until(() => error.IsEffectivelyVisible, "Real SET must reject the invalid draft with its inline explanation.");
        await Settle(120); Capture(f.Window, "exact-clock-rejected-compact-" + badMinutes);
        var field = badMinutes ? minutes : seconds;
        Rect Bounds(Control control)
        {
            var matrix = control.TransformToVisual(f.Window); Assert.NotNull(matrix);
            return new Rect(matrix.Value.Transform(default), matrix.Value.Transform(new Point(control.Bounds.Width, control.Bounds.Height)));
        }
        var box = Bounds(field); var message = Bounds(error);
        DeltaOwnerRecord("exact-clock-rejected-compact-" + badMinutes, new { badMinutes, box, message, errorText = error.Text, draft = field.Text });
        Assert.Equal(before, ManualWeatherModel(f)); Assert.True(field.IsFocused);
        Assert.Equal(badMinutes ? "bad" : "02", minutes.Text); Assert.Equal(badMinutes ? "30" : "60", seconds.Text);
        Assert.Contains("clock has not changed", error.Text!, StringComparison.OrdinalIgnoreCase);
        Assert.True(new Rect(f.Window.ClientSize).Contains(box)); Assert.True(new Rect(f.Window.ClientSize).Contains(message));
        foreach (var p in new[] { box.Center, new Point(box.Left + 4, box.Center.Y), new Point(box.Right - 4, box.Center.Y), new Point(box.Center.X, box.Top + 4), new Point(box.Center.X, box.Bottom - 4) })
            Assert.True(f.Window.InputHitTest(p) is Visual hit && (ReferenceEquals(hit, field) || hit.GetVisualAncestors().Contains(field)), "Rejected draft must stay reachable below the fixed header and above navigation.");
    }
}

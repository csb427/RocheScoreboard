using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class ColorPickerValidationCurrentDraftTests
{
    // Pure source-authored height calculation, not a private event handler or
    // a claim that the executor's physical monitor/work area was reconfigured.
    private static double MaximumPickerHeight(double workAreaHeight) => (double)typeof(ColorPickerWindow)
        .GetMethod("MaximumHeightForWorkArea", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [workAreaHeight])!;

    [AvaloniaTheory]
    [InlineData(320)] [InlineData(400)] [InlineData(560)] [InlineData(900)]
    public void ColourPickerLogicalHeightCalculationNeverExceedsAvailableWorkArea(double workAreaHeight)
    {
        double maximum = MaximumPickerHeight(workAreaHeight);
        Assert.Equal(Math.Max(1, workAreaHeight - 24), maximum);
        Assert.InRange(Math.Min(760, maximum), 1, Math.Max(1, workAreaHeight - 24));
    }

    [AvaloniaTheory]
    [InlineData(320, "USE COLOUR")] [InlineData(320, "CANCEL")]
    [InlineData(560, "USE COLOUR")] [InlineData(560, "CANCEL")]
    public async Task ColourPickerFitsSimulatedShortWorkAreaAndKeepsOriginalActionsReachable(int workAreaHeight, string exit)
    {
        using var storage = new PickerStorage();
        var owner = new NativeWindow { Width = 900, Height = 700 }; owner.Show();
        Color initial = Colors.CornflowerBlue; var preview = new List<Color>();
        var history = storage.Bytes();
        try
        {
            var result = await Pick(owner, initial, preview, async picker =>
            {
                // Drive the actual modal with its source-computed logical maximum.
                // The physical/headless screen remains unchanged; simulated sizing
                // and real painted reachability are distinct evidence contracts.
                double maximum = MaximumPickerHeight(workAreaHeight);
                picker.MaxHeight = maximum; picker.Height = Math.Min(760, maximum); await Pump(80);
                CapturePicker(picker, $"simulated-{workAreaHeight}-initial");
                Assert.True(picker.ClientSize.Height <= workAreaHeight - 24,
                    $"Logical work height{workAreaHeight} must fit its24DIP gutter; actual modal{picker.ClientSize.Height}.");
                var hex = Named<TextBox>(picker, "HexBox"); await Type(picker, hex, "#12AB34"); Onscreen(hex, picker);
                Assert.Equal(Color.FromRgb(0x12, 0xAB, 0x34), picker.SelectedColor);
                var action = Action(picker, exit);
                action.BringIntoView(); Assert.True(action.Focus(NavigationMethod.Tab)); await Pump(60);
                Onscreen(action, picker); Assert.True(action.IsFocused);
                var point = Assert.IsType<Point>(action.TranslatePoint(new Point(action.Bounds.Width / 2, action.Bounds.Height / 2), picker));
                var hit = picker.InputHitTest(point) as Visual;
                Assert.True(ReferenceEquals(hit, action) || (hit is not null && hit.GetVisualAncestors().Contains(action)),
                    "Scrolled original action must receive the actual centre hit, not merely exist in the tree.");
                CapturePicker(picker, $"simulated-{workAreaHeight}-{exit.Replace(' ', '-')}-reachable");
                Invoke(action);
                await Until(() => !picker.IsVisible, "Original Use/Cancel must close before infrastructure cleanup.");
            });
            Assert.Equal(exit == "USE COLOUR", result.accepted);
            Assert.Equal(exit == "USE COLOUR" ? Color.FromRgb(0x12, 0xAB, 0x34) : initial, result.selected);
            Assert.NotEmpty(preview); Assert.Equal(result.selected, preview[^1]);
            if (exit == "CANCEL") storage.AssertBytes(history);
        }
        finally { foreach (var child in owner.OwnedWindows.ToArray()) child.Close(); owner.Close(); }
    }
}

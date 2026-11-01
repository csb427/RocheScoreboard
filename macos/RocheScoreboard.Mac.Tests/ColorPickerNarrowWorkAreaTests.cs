using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class ColorPickerValidationCurrentDraftTests
{
    private static double PreferredPickerWidth(double workAreaWidth) => (double)typeof(ColorPickerWindow)
        .GetMethod("PreferredWidthForWorkArea", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [workAreaWidth])!;
    private static Rect PickerPainted(Control actor, Window window)
    {
        var p = Assert.IsType<Point>(actor.TranslatePoint(default, window));
        var q = Assert.IsType<Point>(actor.TranslatePoint(new Point(actor.Bounds.Width, actor.Bounds.Height), window));
        return new Rect(p, q);
    }
    [AvaloniaTheory]
    [InlineData(320)] [InlineData(420)] [InlineData(900)] [InlineData(1440)]
    public void ColourPickerLogicalWidthFitsNarrowWorkAreasAndRetainsNormalDesign(double workAreaWidth)
        => Assert.Equal(Math.Min(500, Math.Max(1, workAreaWidth - 24)), PreferredPickerWidth(workAreaWidth));

    [AvaloniaTheory]
    [InlineData(320, "USE COLOUR", 0)] [InlineData(320, "CANCEL", 0)]
    [InlineData(420, "USE COLOUR", 0)] [InlineData(420, "CANCEL", 0)]
    [InlineData(320, "×", 0)] [InlineData(420, "×", 0)]
    [InlineData(320, "USE COLOUR", 21)] [InlineData(320, "CANCEL", 21)]
    public async Task ColourPickerSimulatedNarrowWidthKeepsWholeWheelAndOriginalActionsReachable(int workAreaWidth, string exit, double fontSize)
    {
        using var storage = new PickerStorage();
        var owner = new NativeWindow { Width = 900, Height = 700 }; owner.Show();
        var preview = new List<Color>(); Color initial = Colors.CornflowerBlue;
        var history = storage.Bytes();
        try
        {
            var result = await Pick(owner, initial, preview, async picker =>
            {
                double width = PreferredPickerWidth(workAreaWidth);
                picker.Width = width; picker.MaxHeight = 536; picker.Height = 536; await Pump(80);
                CapturePicker(picker, $"simulated-width-{workAreaWidth}-initial");
                Assert.True(picker.ClientSize.Width <= workAreaWidth - 24,
                    $"Logical work width{workAreaWidth} needs its24DIP gutter; actual{picker.ClientSize.Width}.");
                Rect card = PickerPainted(Named<OperatorGlassSurface>(picker, "PickerSurface"), picker);
                Assert.True(card.Left >= -.01 && card.Right <= picker.ClientSize.Width + .01,
                    $"Responsive colour card must fit horizontally; card{card}, client{picker.ClientSize}.");
                var wheel = Named<Canvas>(picker, "ColourField");
                wheel.BringIntoView(); Assert.True(wheel.Focus(NavigationMethod.Tab)); await Pump(60);
                var region = wheel.GetLogicalAncestors().OfType<Grid>().First();
                Rect wheelBox = PickerPainted(wheel, picker), regionBox = PickerPainted(region, picker);
                Assert.True(regionBox.Inflate(.01).Contains(wheelBox),
                    $"The complete wheel must fit its content region: wheel{wheelBox}, region{regionBox}.");
                Assert.True(new Rect(picker.ClientSize).Contains(wheelBox));
                foreach (double x in new[] { 254d, 6d })
                {
                    var point = Assert.IsType<Point>(wheel.TranslatePoint(new Point(x, 130), picker));
                    var hit = picker.InputHitTest(point) as Visual;
                    Assert.True(ReferenceEquals(hit, wheel) || (hit is not null && hit.GetVisualAncestors().Contains(wheel)));
                    picker.MouseMove(point); picker.MouseDown(point, MouseButton.Left); picker.MouseUp(point, MouseButton.Left);
                    await Pump(40);
                    Color c = picker.SelectedColor;
                    if (x > 130) Assert.True(c.R > c.G + 100 && c.R > c.B + 100, "Right-edge logical wheel point must select red after fitting.");
                    else Assert.True(c.G > c.R + 100 && c.B > c.R + 100, "Left-edge logical wheel point must select cyan after fitting.");
                }
                var hex = Named<TextBox>(picker, "HexBox"); await Type(picker, hex, "#12AB34"); Onscreen(hex, picker);
                var action = Action(picker, exit);
                if (fontSize > 0) action.FontSize = fontSize;
                action.BringIntoView(); Assert.True(action.Focus(NavigationMethod.Tab)); await Pump(60);
                Onscreen(action, picker); Assert.True(action.IsFocused);
                var label = Assert.Single(action.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == exit);
                if (fontSize > 0) Assert.Equal(fontSize, label.FontSize);
                CapturePicker(picker, $"simulated-width-{workAreaWidth}-{exit.Replace(' ', '-')}-font-{fontSize}-reachable");
                if (Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is { Length: > 0 } dir)
                    File.WriteAllText(Path.Combine(dir, $"action-label-{workAreaWidth}-{exit.Replace(' ', '-')}-{fontSize}.json"),
                        System.Text.Json.JsonSerializer.Serialize(new { exit, workAreaWidth, requestedFontSize = fontSize, actualFontSize = label.FontSize,
                            label.Bounds, textHeight = label.TextLayout.Height,
                            lines = label.TextLayout.TextLines.Select(l => new { l.Width, l.HasCollapsed }) }));
                Assert.DoesNotContain(label.TextLayout.TextLines, line => line.HasCollapsed);
                Assert.All(label.TextLayout.TextLines, line => Assert.True(line.Width <= label.Bounds.Width + .01,
                    $"Whole action label must fit: {exit}, line{line.Width}, bounds{label.Bounds.Width}."));
                Assert.True(label.TextLayout.Height <= label.Bounds.Height + .01);
                var actionPoint = Assert.IsType<Point>(action.TranslatePoint(new Point(action.Bounds.Width / 2, action.Bounds.Height / 2), picker));
                var actionHit = picker.InputHitTest(actionPoint) as Visual;
                Assert.True(ReferenceEquals(actionHit, action) || (actionHit is not null && actionHit.GetVisualAncestors().Contains(action)));
                if (exit == "×")
                {
                    picker.MouseMove(actionPoint); picker.MouseDown(actionPoint, MouseButton.Left); picker.MouseUp(actionPoint, MouseButton.Left);
                }
                else Invoke(action);
                await Until(() => !picker.IsVisible, "Original fitted-modal action must close before cleanup.");
            });
            Assert.Equal(exit == "USE COLOUR", result.accepted);
            Assert.Equal(exit == "USE COLOUR" ? Color.FromRgb(0x12, 0xAB, 0x34) : initial, result.selected);
            Assert.NotEmpty(preview); Assert.Equal(result.selected, preview[^1]);
            if (exit != "USE COLOUR") storage.AssertBytes(history);
        }
        finally { foreach (var child in owner.OwnedWindows.ToArray()) child.Close(); owner.Close(); }
    }
}

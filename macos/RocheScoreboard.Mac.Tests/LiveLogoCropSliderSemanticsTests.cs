using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private static readonly string[] CropSliderNames = ["HomeLogoZoomSlider", "HomeLogoXSlider", "HomeLogoYSlider", "AwayLogoZoomSlider", "AwayLogoXSlider", "AwayLogoYSlider"];
    private static Rect CropSliderBox(Window window, Control control)
    {
        var m = control.TransformToVisual(window); Assert.NotNull(m);
        return new Rect(m.Value.Transform(default), m.Value.Transform(new Point(control.Bounds.Width, control.Bounds.Height)));
    }
    private static async Task CropSliderReveal(AflFixture f, SettingsWorkspace workspace, Slider slider)
    {
        var row = Assert.IsAssignableFrom<Panel>(slider.Parent);
        var caption = Assert.IsType<TextBlock>(row.Children[row.Children.IndexOf(slider) - 1]);
        var card = Assert.IsAssignableFrom<Control>(row.Parent);
        var cropBeforeReveal = CropSliderNames.Select(n => Named<Slider>(f.Window, n).Value).ToArray();
        var outer = Named<ScrollViewer>(f.Window, "AflSettingsPageViewport");
        Rect Pair() => CropSliderBox(f.Window, caption).Union(CropSliderBox(f.Window, slider));
        var captionPoint = CropSliderBox(f.Window, caption).Center;
        DeltaOwnerRecord("crop-caption-hit-" + slider.Name + "-" + f.Window.Width, new {
            slider.Name, captionBox = CropSliderBox(f.Window, caption), sliderBox = CropSliderBox(f.Window, slider),
            inner = CropSliderBox(f.Window, workspace.Scroller), outer = CropSliderBox(f.Window, outer),
            hit = f.Window.InputHitTest(captionPoint)?.GetType().FullName,
            inRow = Within(row, f.Window.InputHitTest(captionPoint)),
            inCard = Within(card, f.Window.InputHitTest(captionPoint)) });
        bool Ready()
        {
            using var frame = f.Window.CaptureRenderedFrame(); var r = CropSliderBox(f.Window, slider);
            var visible = CropSliderBox(f.Window, workspace.Scroller).Intersect(CropSliderBox(f.Window, outer)).Intersect(new Rect(f.Window.ClientSize));
            return r.Width > 0 && r.Height > 0 && visible.Contains(Pair())
                && Within(slider, f.Window.InputHitTest(r.Center))
                // A left-aligned read-only caption spans the whole card. Its
                // empty horizontal area correctly hits the backing card, not
                // the text or transparent StackPanel. Keep full viewport
                // containment and exclude the fixed header/navigation.
                && Within(card, f.Window.InputHitTest(CropSliderBox(f.Window, caption).Center));
        }
        double innerPrevious = 0, outerPrevious = 0, innerAmount = 1, outerAmount = 1;
        for (int i = 0; i < 45 && !Ready(); i++)
        {
            var inside = CropSliderBox(f.Window, workspace.Scroller); var pair = Pair();
            bool useInner = pair.Top < inside.Top || pair.Bottom > inside.Bottom;
            var viewport = CropSliderBox(f.Window, outer).Intersect(new Rect(f.Window.ClientSize));
            if (useInner) viewport = viewport.Intersect(inside);
            Assert.True(viewport.Width > 0 && viewport.Height > 0);
            double direction = pair.Center.Y < (useInner ? inside.Center.Y : viewport.Center.Y) ? 1 : -1;
            if (useInner) { if (innerPrevious != 0 && innerPrevious != direction) innerAmount *= .5; innerPrevious = direction; }
            else { if (outerPrevious != 0 && outerPrevious != direction) outerAmount *= .5; outerPrevious = direction; }
            // Blank left gutter routes the actual wheel to the intended
            // scroller, without changing a slider by wheeling over its thumb.
            var point = new Point(viewport.Left + 3, viewport.Center.Y);
            f.Window.MouseWheel(point, new Vector(0, direction * (useInner ? innerAmount : outerAmount)), RawInputModifiers.None); await Settle(50);
        }
        Assert.True(Ready(), "Caption and actual crop slider must be reachable together through both existing settings scrollers.");
        Assert.Equal(cropBeforeReveal, CropSliderNames.Select(n => Named<Slider>(f.Window, n).Value).ToArray());
    }
    private static void AssertCropPreviewMatrix(AflFixture f, double[] values)
    {
        foreach (var (side, start) in new[] { ("Home", 0), ("Away", 3) })
        {
            var image = Named<Image>(f.Window, side + "LogoCropPreviewImage"); Assert.NotNull(image.RenderTransform);
            var m = image.RenderTransform.Value;
            Assert.Equal(values[start], m.M11, 6); Assert.Equal(values[start], m.M22, 6);
            Assert.Equal(values[start + 1], m.M31, 6); Assert.Equal(values[start + 2], m.M32, 6);
        }
    }
    [AvaloniaTheory]
    [InlineData("HomeLogoZoomSlider", 1280, 850)] [InlineData("HomeLogoXSlider", 1280, 850)] [InlineData("HomeLogoYSlider", 1280, 850)]
    [InlineData("AwayLogoZoomSlider", 1280, 850)] [InlineData("AwayLogoXSlider", 1280, 850)] [InlineData("AwayLogoYSlider", 1280, 850)]
    [InlineData("HomeLogoZoomSlider", 420, 320)] [InlineData("HomeLogoXSlider", 420, 320)] [InlineData("HomeLogoYSlider", 420, 320)]
    [InlineData("AwayLogoZoomSlider", 420, 320)] [InlineData("AwayLogoXSlider", 420, 320)] [InlineData("AwayLogoYSlider", 420, 320)]
    public async Task OriginalLogoCropSliderNamesExplainUnitsAndKeepDirectRangeAndKeyboardPreview(string name, int width, int height)
    {
        await using var f = await AflFixture.Create(width, height);
        var workspace = Assert.Single(f.Window.GetVisualDescendants().OfType<SettingsWorkspace>(), w => w.IsEffectivelyVisible);
        Key(f.Window, PhysicalKey.F, RawInputModifiers.Control);
        var search = Assert.Single(workspace.GetVisualDescendants().OfType<OperatorSearchBox>(), x => x.IsEffectivelyVisible);
        Assert.True(search.IsFocused); f.Window.KeyTextInput("Adjust logo crop and positioning"); await Settle(180);
        Assert.True(Named<Control>(f.Window, "SettingsSection_Media").IsEffectivelyVisible);
        var sliders = CropSliderNames.Select(n => Named<Slider>(f.Window, n)).ToArray(); int index = Array.IndexOf(CropSliderNames, name);
        var target = sliders[index];
        for (int count = 0; count < 50 && !target.IsFocused; count++) { Key(f.Window, PhysicalKey.Tab); await Settle(25); }
        Assert.True(target.IsFocused); await CropSliderReveal(f, workspace, target);
        var peer = ControlAutomationPeer.CreatePeerForElement(target); var provider = Assert.IsAssignableFrom<IRangeValueProvider>(peer);
        string side = index < 3 ? "Home" : "Away"; int axis = index % 3;
        string purpose = axis == 0 ? "zoom" : axis == 1 ? "horizontal offset" : "vertical offset";
        var row = Assert.IsAssignableFrom<Panel>(target.Parent); var caption = Assert.IsType<TextBlock>(row.Children[row.Children.IndexOf(target) - 1]);
        Capture(f.Window, "crop-slider-" + name + "-" + width);
        DeltaOwnerRecord("crop-slider-" + name + "-" + width, new { name, width, height, peerName = peer?.GetName(), caption.Text, caption.FontSize, provider.Minimum, provider.Maximum, provider.Value });
        Assert.Equal(side + " logo " + purpose, peer?.GetName());
        Assert.Contains(axis == 0 ? "multiplier" : "layout units", AutomationProperties.GetHelpText(target)!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(axis == 0 ? "Zoom (×)" : axis == 1 ? "Horizontal offset" : "Vertical offset", caption.Text); Assert.True(caption.FontSize >= 12);
        Assert.False(provider.IsReadOnly); Assert.Equal(axis == 0 ? .8 : -50, provider.Minimum); Assert.Equal(axis == 0 ? 3 : 50, provider.Maximum);
        double[] expected = sliders.Select(s => s.Value).ToArray(); string model = ManualWeatherModel(f); bool open = PresWindowOpen(f);
        double next = axis == 0 ? 1.25 : 12; if (target.Value == next) next = axis == 0 ? 1.5 : 15;
        provider.SetValue(next); await Settle(30); expected[index] = next;
        for (int i = 0; i < sliders.Length; i++) Assert.Equal(expected[i], sliders[i].Value, 6);
        AssertCropPreviewMatrix(f, expected); Assert.Equal(model, ManualWeatherModel(f)); Assert.Equal(open, PresWindowOpen(f));
        Key(f.Window, PhysicalKey.End); await Until(() => target.Value == target.Maximum, "Original focused slider End must update the direct preview at its real maximum.");
        expected[index] = provider.Maximum; AssertCropPreviewMatrix(f, expected);
        for (int i = 0; i < sliders.Length; i++) Assert.Equal(expected[i], sliders[i].Value, 6);
        Assert.Equal(model, ManualWeatherModel(f)); Assert.Equal(open, PresWindowOpen(f));
        // Transform properties establish immediate preview state. Without an
        // image asset this does not establish bitmap crop/pixel correctness.
    }
}

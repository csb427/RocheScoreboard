// Regression checks for the real translated production dictionaries and motion owners.
// These use actual translated production dictionaries and motion owners.
// WPF startup's competing generic ContextMenu arrival is NATIVE-ONLY here;
// see the accompanying plan rather than a synthetic passing Mac assertion.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using RocheScoreboard.Mac.Native;
using Xunit;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace RocheScoreboard.Mac.Tests;

public sealed class SharedPreferenceMotionTests
{
    private sealed class AppearanceScope : IDisposable
    {
        private readonly bool glass = OperatorAppearance.GlassEnabled;
        private readonly bool reduced = OperatorAppearance.ReduceMotion;
        private readonly OperatorMotionStyle style = OperatorAppearance.MotionStyle;
        private readonly bool? animation = OperatorMotion.AnimationOverride;
        private readonly byte[]? bytes = File.Exists(OperatorAppearance.StoragePath)
            ? File.ReadAllBytes(OperatorAppearance.StoragePath) : null;

        internal void EnableAnimations()
        {
            // Exercise the actual preference event; an AnimationOverride=true
            // would make a Reduce Motion regression impossible to observe.
            OperatorMotion.AnimationOverride = null;
            OperatorAppearance.Configure(true, false);
            OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);
            Assert.True(OperatorMotion.Enabled);
        }

        internal void Reduce() => OperatorAppearance.Configure(true, true);

        public void Dispose()
        {
            OperatorMotion.AnimationOverride = animation;
            OperatorAppearance.ConfigureMotionStyle(style);
            OperatorAppearance.Configure(glass, reduced);
            if (bytes is null) File.Delete(OperatorAppearance.StoragePath);
            else File.WriteAllBytes(OperatorAppearance.StoragePath, bytes);
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly StackPanel Body = new() { Margin = new Thickness(24), Spacing = 12 };
        internal readonly NativeWindow Window;
        private readonly HashSet<ComboBox> combos = new();
        private readonly List<(Popup Popup, Control Surface, TranslateTransform Shift)> popupParts = new();

        internal Fixture(bool legacy = false)
        {
            Window = new NativeWindow
            {
                Title = "Shared preference motion fixture", Width = 520, Height = 420,
                Content = Body
            };
            // Explicitly select one real production dictionary. No substitute
            // template, artificial animation, or private owner invocation.
            Window.Resources.MergedDictionaries.Add(
                NativeAssets.Dictionary(legacy ? "SettingsControls" : "ControlCentre"));
        }

        internal async Task Show()
        {
            Window.Show();
            // Keep pointer hover/press springs out of the label-geometry
            // contract being tested. This is real input outside the form.
            Window.MouseMove(new Point(2, 2));
            await Settle(180);
        }

        internal void Add(Control control)
        {
            if (control is ComboBox combo) combos.Add(combo);
            Body.Children.Add(control);
        }

        internal void Remember(Popup popup, Control surface, TranslateTransform shift) =>
            popupParts.Add((popup, surface, shift));

        public void Dispose()
        {
            // Removed controls are deliberately retained in this teardown
            // registry. An assertion failure must not strand their popup.
            try
            {
                foreach (var combo in combos) combo.IsDropDownOpen = false;
            }
            finally
            {
                foreach (var parts in popupParts)
                {
                    parts.Popup.IsOpen = false;
                    // Test cleanup only, after all app-lifecycle assertions.
                    Motion.StopAll(parts.Surface);
                    Motion.StopAll(parts.Shift);
                }
                try { Window.Close(); }
                finally { Body.Children.Clear(); Dispatcher.UIThread.RunJobs(); }
            }
        }
    }

    private static async Task Settle(int milliseconds = 80)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Key(TopLevel inputRoot, PhysicalKey key)
    {
        inputRoot.KeyPressQwerty(key, RawInputModifiers.None);
        inputRoot.KeyReleaseQwerty(key, RawInputModifiers.None);
    }

    private static T Part<T>(Control control, string name) where T : class =>
        Assert.IsAssignableFrom<T>(NativeTemplates.FindObject(control, name));

    private sealed class SemanticEvents : IDisposable
    {
        private readonly ToggleButton? toggle;
        private readonly ComboBox? combo;
        internal int CheckedChanges { get; private set; }
        internal int SelectedChanges { get; private set; }
        internal (int Checked, int Selected) Counts => (CheckedChanges, SelectedChanges);
        internal SemanticEvents(Control control)
        {
            toggle = control as ToggleButton;
            combo = control as ComboBox;
            if (toggle is not null) toggle.IsCheckedChanged += Checked;
            if (combo is not null) combo.SelectionChanged += Selected;
        }
        private void Checked(object? sender, Avalonia.Interactivity.RoutedEventArgs args) => CheckedChanges++;
        private void Selected(object? sender, SelectionChangedEventArgs args) => SelectedChanges++;
        public void Dispose()
        {
            if (toggle is not null) toggle.IsCheckedChanged -= Checked;
            if (combo is not null) combo.SelectionChanged -= Selected;
        }
    }

    private static void AssertNoSemanticEvents(SemanticEvents events, (int Checked, int Selected) before)
    {
        Assert.Equal(before, events.Counts);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, events.Counts);
    }

    private readonly record struct RenderedQuad(Point TopLeft, Point TopRight, Point BottomRight, Point BottomLeft);
    private sealed record Geometry(Rect LayoutBounds, RenderedQuad Target, RenderedQuad Label, Point[] HitProbes);

    private static RenderedQuad Rendered(Control control, Window window)
    {
        Point Map(double x, double y)
        {
            var translated = control.TranslatePoint(new Point(x, y), window);
            Assert.NotNull(translated);
            return translated.Value;
        }
        return new(Map(0, 0), Map(control.Bounds.Width, 0),
            Map(control.Bounds.Width, control.Bounds.Height), Map(0, control.Bounds.Height));
    }

    private static Control ReadableLabel(Control control)
    {
        string expected = control switch
        {
            ComboBox combo => (combo.SelectedItem as ComboBoxItem)?.Content?.ToString()
                ?? throw new InvalidOperationException("A selected fixture item must have readable text."),
            CheckBox => "Enable this option",
            _ => "Use this choice"
        };
        return Assert.Single(control.GetVisualDescendants().OfType<TextBlock>(),
            text => text.IsEffectivelyVisible && text.Text == expected);
    }

    private static void AssertHitAt(Window window, Control target, Point point)
    {
        var hit = window.InputHitTest(point);
        Assert.True(ReferenceEquals(hit, target) ||
            hit is Visual visual && visual.GetVisualAncestors().Contains(target),
            $"The original physical probe {point} must hit {target.GetType().Name} {target.Name} "
            + $"or its template child; hit {hit?.GetType().Name ?? "null"}; bounds {target.Bounds}.");
    }

    private static Geometry GeometryOf(Window window, Control control)
    {
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        var probes = new[] { new Point(.5, .5), new Point(.25, .5), new Point(.75, .5),
            new Point(.5, .25), new Point(.5, .75) }.Select(fraction =>
        {
            var point = control.TranslatePoint(new Point(control.Bounds.Width * fraction.X,
                control.Bounds.Height * fraction.Y), window);
            Assert.NotNull(point);
            AssertHitAt(window, control, point.Value);
            return point.Value;
        }).ToArray();
        return new(control.Bounds, Rendered(control, window), Rendered(ReadableLabel(control), window), probes);
    }

    private static void AssertGeometry(Geometry before, Window window, Control control)
    {
        static void PointEqual(Point expected, Point actual)
        { Assert.Equal(expected.X, actual.X, 4); Assert.Equal(expected.Y, actual.Y, 4); }
        static void QuadEqual(RenderedQuad expected, RenderedQuad actual)
        {
            PointEqual(expected.TopLeft, actual.TopLeft); PointEqual(expected.TopRight, actual.TopRight);
            PointEqual(expected.BottomRight, actual.BottomRight); PointEqual(expected.BottomLeft, actual.BottomLeft);
        }
        window.UpdateLayout();
        // Hit testing uses the rendered scene, which must include a reattached
        // control; layout completion alone does not commit that scene.
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        Assert.Equal(before.LayoutBounds, control.Bounds);
        QuadEqual(before.Target, Rendered(control, window));
        QuadEqual(before.Label, Rendered(ReadableLabel(control), window));
        foreach (var point in before.HitProbes) AssertHitAt(window, control, point);
    }

    private static RadioButton Choice(bool selected = false)
    {
        var choice = new RadioButton
        {
            Content = "Use this choice", IsChecked = selected,
            Width = 280, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };
        choice.SetResourceReference(Control.ThemeProperty, "SettingsTile");
        return choice;
    }

    private static CheckBox LegacyToggle(bool selected = false)
    {
        var toggle = new CheckBox { Content = "Enable this option", IsChecked = selected, Width = 280 };
        toggle.SetResourceReference(Control.ThemeProperty, "SettingsToggle");
        return toggle;
    }

    private static ComboBox Combo()
    {
        var combo = new ComboBox { Width = 280, SelectedIndex = -1 };
        combo.Items.Add(new ComboBoxItem { Content = "First" });
        combo.Items.Add(new ComboBoxItem { Content = "Second" });
        combo.Items.Add(new ComboBoxItem { Content = "Third" });
        combo.SelectedIndex = 1;
        combo.SetResourceReference(Control.ThemeProperty, "SettingsChoice");
        return combo;
    }

    private static void AssertOwner(Control control, bool legacy)
    {
        Assert.Equal(!legacy, OperatorControlMotion.GetEnabled(control));
        Assert.Equal(legacy, SettingsControlMotion.GetEnabled(control));
    }

    private static ShapePath Check(RadioButton choice) => Part<ShapePath>(choice, "PART_CheckPath");

    private static (Popup Popup, Control Surface, TranslateTransform Shift) OpenChoice(
        Fixture fixture, ComboBox combo, bool legacy)
    {
        AssertOwner(combo, legacy);
        Assert.True(combo.Focus(NavigationMethod.Tab));
        Key(fixture.Window, PhysicalKey.F4);
        Assert.True(combo.IsDropDownOpen);
        var popup = Part<Popup>(combo, "PART_Popup");
        Assert.True(popup.IsOpen);
        var surface = Assert.IsAssignableFrom<Control>(popup.Child);
        var shift = Assert.IsType<TranslateTransform>(surface.RenderTransform);
        fixture.Remember(popup, surface, shift);
        // Establish actual helper-owned clocks before asserting interruption.
        Assert.True(Motion.IsAnimating(shift), "Opening the real production popup must start its local shift.");
        Assert.True(Motion.IsAnimating(surface), "Opening must start the real popup opacity clock.");
        Motion.SampleAt(shift, TimeSpan.FromMilliseconds(45));
        Motion.SampleAt(surface, TimeSpan.FromMilliseconds(45));
        Assert.True(shift.Y < 0);
        Assert.InRange(surface.Opacity, .001, .999);
        return (popup, surface, shift);
    }

    private static void AssertPopupRest(Control surface, TranslateTransform shift)
    {
        Assert.False(Motion.IsAnimating(shift));
        Assert.False(Motion.IsAnimating(surface));
        Assert.Equal(0, shift.Y, 6);
        Assert.Equal(1, surface.Opacity, 6);
        // A final-looking sample is insufficient if a clock is still attached.
        Motion.SampleAt(shift, TimeSpan.FromMilliseconds(180));
        Motion.SampleAt(surface, TimeSpan.FromMilliseconds(180));
        Assert.Equal(0, shift.Y, 6);
        Assert.Equal(1, surface.Opacity, 6);
    }

    [AvaloniaFact]
    public async Task LiveReduceSettlesAnUnchangedHoverGlowWithoutChangingTheChoice()
    {
        using var appearance = new AppearanceScope();
        appearance.EnableAnimations();
        using var f = new Fixture();
        var choice = Choice();
        f.Add(choice);
        await f.Show();
        using var events = new SemanticEvents(choice);
        f.Window.MouseMove(new Point(2, 2));
        var center = choice.TranslatePoint(new Point(choice.Bounds.Width / 2, choice.Bounds.Height / 2), f.Window);
        Assert.NotNull(center);
        f.Window.MouseMove(center.Value);
        Assert.True(choice.IsPointerOver);
        var glow = Assert.Single(choice.GetVisualDescendants().OfType<Border>(), item => item.Name == "ActionGlow");
        Assert.True(Motion.IsAnimating(glow));
        Motion.SampleAt(glow, TimeSpan.FromMilliseconds(20));
        var geometry = GeometryOf(f.Window, choice);
        var before = events.Counts;

        appearance.Reduce();

        Assert.False(Motion.IsAnimating(glow));
        Assert.Equal(.035, glow.Opacity, 6);
        Assert.False(choice.IsChecked == true);
        AssertNoSemanticEvents(events, before);
        AssertGeometry(geometry, f.Window, choice);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OperatorCheckDrawStopsImmediatelyOnLiveReduceWithoutChangingSelection(bool selected)
    {
        using var appearance = new AppearanceScope();
        appearance.EnableAnimations();
        using var f = new Fixture();
        var choice = Choice(!selected);
        f.Add(choice);
        await f.Show();
        AssertOwner(choice, legacy: false);
        var check = Check(choice);
        var bounds = choice.Bounds;
        using var events = new SemanticEvents(choice);
        choice.IsChecked = selected;
        Assert.Equal((1, 0), events.Counts);
        Assert.Equal(selected, choice.IsChecked == true);
        Assert.True(Motion.IsAnimating(check));
        Motion.SampleAt(check, TimeSpan.FromMilliseconds(50));
        Assert.InRange(check.StrokeDashOffset, .001, 21.999);
        var rendered = GeometryOf(f.Window, choice);
        var semanticBefore = events.Counts;

        appearance.Reduce();

        Assert.False(OperatorMotion.Enabled);
        Assert.False(Motion.IsAnimating(check));
        Assert.Equal(selected ? 0 : 22, check.StrokeDashOffset, 6);
        Assert.Equal(selected, choice.IsChecked == true);
        Assert.Equal(bounds, choice.Bounds);
        AssertGeometry(rendered, f.Window, choice);
        AssertNoSemanticEvents(events, semanticBefore);
        Motion.SampleAt(check, TimeSpan.FromMilliseconds(160));
        Assert.Equal(selected ? 0 : 22, check.StrokeDashOffset, 6);
        AssertNoSemanticEvents(events, semanticBefore);
    }

    [AvaloniaTheory]
    [InlineData("hide")]
    [InlineData("unload")]
    [InlineData("disable-motion")]
    public async Task OperatorCheckCleanupStopsItsClockAndReloadPreservesCheckedState(string retirement)
    {
        using var appearance = new AppearanceScope();
        appearance.EnableAnimations();
        using var f = new Fixture();
        var choice = Choice();
        f.Add(choice);
        await f.Show();
        var check = Check(choice);
        using var events = new SemanticEvents(choice);
        choice.IsChecked = true;
        Assert.Equal((1, 0), events.Counts);
        Assert.True(Motion.IsAnimating(check));
        var rendered = GeometryOf(f.Window, choice);
        var semanticBefore = events.Counts;

        if (retirement == "hide") choice.IsVisible = false;
        else if (retirement == "unload") f.Body.Children.Remove(choice);
        else OperatorControlMotion.SetEnabled(choice, false);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Motion.IsAnimating(check));
        Assert.Equal(0, check.StrokeDashOffset, 6);
        Assert.True(choice.IsChecked);
        AssertNoSemanticEvents(events, semanticBefore);
        if (retirement == "disable-motion") AssertGeometry(rendered, f.Window, choice);
        if (retirement == "hide") choice.IsVisible = true;
        else if (retirement == "unload") f.Add(choice);
        else OperatorControlMotion.SetEnabled(choice, true);
        await Settle();
        Assert.Equal(0, Check(choice).StrokeDashOffset, 6);
        Assert.False(Motion.IsAnimating(Check(choice)));
        Assert.True(choice.IsChecked);
        AssertNoSemanticEvents(events, semanticBefore);
        AssertGeometry(rendered, f.Window, choice);
    }

    [AvaloniaFact]
    public async Task OperatorCheckRapidReversalsKeepTheFinalValueAndDoNotLeaveAClock()
    {
        using var appearance = new AppearanceScope();
        appearance.EnableAnimations();
        using var f = new Fixture();
        var choice = Choice();
        f.Add(choice);
        await f.Show();
        var check = Check(choice);
        var bounds = choice.Bounds;
        using var events = new SemanticEvents(choice);
        foreach (bool selected in new[] { true, false, true, false, true })
        {
            choice.IsChecked = selected;
            Assert.Equal(selected, choice.IsChecked == true);
            Assert.True(Motion.IsAnimating(check));
            Motion.SampleAt(check, TimeSpan.FromMilliseconds(35));
        }
        Assert.Equal((5, 0), events.Counts);
        var rendered = GeometryOf(f.Window, choice);
        var semanticBefore = events.Counts;
        appearance.Reduce();
        Assert.True(choice.IsChecked);
        Assert.Equal(0, check.StrokeDashOffset, 6);
        Assert.False(Motion.IsAnimating(check));
        Assert.Equal(bounds, choice.Bounds);
        AssertGeometry(rendered, f.Window, choice);
        AssertNoSemanticEvents(events, semanticBefore);
        choice.IsChecked = false;
        Assert.False(choice.IsChecked);
        Assert.Equal(22, check.StrokeDashOffset, 6);
        Assert.False(Motion.IsAnimating(check));
        Assert.Equal((6, 0), events.Counts);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenComboSettlesBothLocalPartsImmediatelyOnLiveReduce(bool legacy)
    {
        using var appearance = new AppearanceScope();
        appearance.EnableAnimations();
        using var f = new Fixture(legacy);
        var combo = Combo();
        f.Add(combo);
        await f.Show();
        var bounds = combo.Bounds;
        using var events = new SemanticEvents(combo);
        var parts = OpenChoice(f, combo, legacy);
        Assert.Equal((0, 0), events.Counts);
        var rendered = GeometryOf(f.Window, combo);
        var semanticBefore = events.Counts;

        appearance.Reduce();

        AssertPopupRest(parts.Surface, parts.Shift);
        Assert.True(combo.IsDropDownOpen);
        Assert.True(parts.Popup.IsOpen);
        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal(bounds, combo.Bounds);
        AssertGeometry(rendered, f.Window, combo);
        AssertNoSemanticEvents(events, semanticBefore);
        combo.IsDropDownOpen = false;
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisablingComboMotionWhileOpenSettlesPartsWithoutClosingOrChangingValue(bool legacy)
    {
        using var appearance = new AppearanceScope();
        appearance.EnableAnimations();
        using var f = new Fixture(legacy);
        var combo = Combo();
        f.Add(combo);
        await f.Show();
        using var events = new SemanticEvents(combo);
        var parts = OpenChoice(f, combo, legacy);
        Assert.Equal((0, 0), events.Counts);
        var rendered = GeometryOf(f.Window, combo);
        var semanticBefore = events.Counts;

        if (legacy) SettingsControlMotion.SetEnabled(combo, false);
        else OperatorControlMotion.SetEnabled(combo, false);

        AssertPopupRest(parts.Surface, parts.Shift);
        Assert.True(combo.IsDropDownOpen);
        Assert.Equal(1, combo.SelectedIndex);
        AssertGeometry(rendered, f.Window, combo);
        AssertNoSemanticEvents(events, semanticBefore);
        combo.IsDropDownOpen = false;
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComboCloseAndRepeatedOpenRetireOldClocksAndRealKeysStillSelectImmediately(bool legacy)
    {
        using var appearance = new AppearanceScope();
        appearance.EnableAnimations();
        using var f = new Fixture(legacy);
        var combo = Combo();
        f.Add(combo);
        await f.Show();
        var bounds = combo.Bounds;
        using var events = new SemanticEvents(combo);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var parts = OpenChoice(f, combo, legacy);
            var rendered = GeometryOf(f.Window, combo);
            combo.IsDropDownOpen = false;
            Assert.False(parts.Popup.IsOpen);
            AssertPopupRest(parts.Surface, parts.Shift);
            Assert.Equal(1, combo.SelectedIndex);
            AssertGeometry(rendered, f.Window, combo);
            Assert.Equal((0, 0), events.Counts);
        }
        var semanticBefore = events.Counts;
        var beforeReduce = GeometryOf(f.Window, combo);
        appearance.Reduce();
        AssertNoSemanticEvents(events, semanticBefore);
        AssertGeometry(beforeReduce, f.Window, combo);
        Assert.True(combo.Focus(NavigationMethod.Tab));
        Key(f.Window, PhysicalKey.F4);
        Assert.True(combo.IsDropDownOpen);
        Key(f.Window, PhysicalKey.End);
        Key(f.Window, PhysicalKey.Enter);
        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(2, combo.SelectedIndex);
        Assert.Equal("Third", Assert.IsAssignableFrom<ComboBoxItem>(combo.SelectedItem).Content);
        Assert.Equal(bounds, combo.Bounds);
        Assert.Equal((0, 1), events.Counts);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovingAnOpenComboRetiresItsPopupPartsWithoutChangingItsDraft(bool legacy)
    {
        using var appearance = new AppearanceScope();
        appearance.EnableAnimations();
        using var f = new Fixture(legacy);
        var combo = Combo();
        f.Add(combo);
        await f.Show();
        using var events = new SemanticEvents(combo);
        var parts = OpenChoice(f, combo, legacy);
        var rendered = GeometryOf(f.Window, combo);
        var semanticBefore = events.Counts;
        f.Body.Children.Remove(combo);
        Dispatcher.UIThread.RunJobs();
        AssertPopupRest(parts.Surface, parts.Shift);
        Assert.False(parts.Popup.IsOpen);
        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(1, combo.SelectedIndex);
        AssertNoSemanticEvents(events, semanticBefore);
        f.Add(combo);
        await Settle();
        AssertGeometry(rendered, f.Window, combo);
        AssertNoSemanticEvents(events, semanticBefore);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyCheckboxLiveReduceSettlesItsActualThumbWithoutChangingValue(bool selected)
    {
        using var appearance = new AppearanceScope();
        appearance.EnableAnimations();
        using var f = new Fixture(legacy: true);
        var toggle = LegacyToggle(!selected);
        f.Add(toggle);
        await f.Show();
        AssertOwner(toggle, legacy: true);
        var thumb = Part<TranslateTransform>(toggle, "Thumb");
        var bounds = toggle.Bounds;
        using var events = new SemanticEvents(toggle);
        toggle.IsChecked = selected;
        Assert.Equal((1, 0), events.Counts);
        Assert.True(Motion.IsAnimating(thumb));
        Motion.SampleAt(thumb, TimeSpan.FromMilliseconds(80));
        Assert.True(Math.Abs(thumb.X - (selected ? 0 : 17)) > .001,
            "The actual thumb must have moved away from its initial endpoint.");
        var rendered = GeometryOf(f.Window, toggle);
        var semanticBefore = events.Counts;

        appearance.Reduce();

        Assert.False(Motion.IsAnimating(thumb));
        Assert.Equal(selected ? 17 : 0, thumb.X, 6);
        Assert.Equal(selected, toggle.IsChecked == true);
        Assert.Equal(bounds, toggle.Bounds);
        AssertGeometry(rendered, f.Window, toggle);
        AssertNoSemanticEvents(events, semanticBefore);
        Motion.SampleAt(thumb, TimeSpan.FromMilliseconds(200));
        Assert.Equal(selected ? 17 : 0, thumb.X, 6);
        AssertNoSemanticEvents(events, semanticBefore);
    }

    [AvaloniaTheory]
    [InlineData("hide")]
    [InlineData("unload")]
    [InlineData("disable-motion")]
    public async Task LegacyCheckboxRetirementStopsThumbAndReentryUsesCurrentValue(string retirement)
    {
        using var appearance = new AppearanceScope();
        appearance.EnableAnimations();
        using var f = new Fixture(legacy: true);
        var toggle = LegacyToggle();
        f.Add(toggle);
        await f.Show();
        AssertOwner(toggle, legacy: true);
        var thumb = Part<TranslateTransform>(toggle, "Thumb");
        using var events = new SemanticEvents(toggle);
        toggle.IsChecked = true;
        Assert.Equal((1, 0), events.Counts);
        Assert.True(Motion.IsAnimating(thumb));
        Motion.SampleAt(thumb, TimeSpan.FromMilliseconds(60));
        var rendered = GeometryOf(f.Window, toggle);
        var semanticBefore = events.Counts;

        if (retirement == "hide") toggle.IsVisible = false;
        else if (retirement == "unload") f.Body.Children.Remove(toggle);
        else SettingsControlMotion.SetEnabled(toggle, false);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Motion.IsAnimating(thumb));
        Assert.Equal(17, thumb.X, 6);
        Assert.True(toggle.IsChecked);
        AssertNoSemanticEvents(events, semanticBefore);
        if (retirement == "disable-motion") AssertGeometry(rendered, f.Window, toggle);
        if (retirement == "hide") toggle.IsVisible = true;
        else if (retirement == "unload") f.Add(toggle);
        else SettingsControlMotion.SetEnabled(toggle, true);
        await Settle();
        Assert.Equal(17, Part<TranslateTransform>(toggle, "Thumb").X, 6);
        Assert.True(toggle.IsChecked);
        AssertNoSemanticEvents(events, semanticBefore);
        AssertGeometry(rendered, f.Window, toggle);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyCheckboxKeyboardValueChangesImmediatelyAndReversalsStayStationary(bool reduced)
    {
        using var appearance = new AppearanceScope();
        appearance.EnableAnimations();
        if (reduced) appearance.Reduce();
        using var f = new Fixture(legacy: true);
        var toggle = LegacyToggle();
        f.Add(toggle);
        await f.Show();
        AssertOwner(toggle, legacy: true);
        var thumb = Part<TranslateTransform>(toggle, "Thumb");
        var bounds = toggle.Bounds;
        var rendered = GeometryOf(f.Window, toggle);
        int changes = 0;
        toggle.IsCheckedChanged += (_, _) => changes++;
        Assert.True(toggle.Focus(NavigationMethod.Tab));
        Key(f.Window, PhysicalKey.Space);
        Assert.True(toggle.IsChecked);
        Assert.Equal(1, changes);
        Assert.Equal(bounds, toggle.Bounds);
        AssertGeometry(rendered, f.Window, toggle);
        Key(f.Window, PhysicalKey.Space);
        Assert.False(toggle.IsChecked);
        Assert.Equal(2, changes);
        Assert.Equal(bounds, toggle.Bounds);
        AssertGeometry(rendered, f.Window, toggle);
        if (!reduced) appearance.Reduce();
        Assert.Equal(2, changes);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, changes);
        AssertGeometry(rendered, f.Window, toggle);
        Assert.False(Motion.IsAnimating(thumb));
        Assert.Equal(0, thumb.X, 6);
    }

    [AvaloniaFact]
    public async Task ReenablingLegacyCheckboxMotionWhileLoadedRestoresPreferenceOwnership()
    {
        using var appearance = new AppearanceScope();
        appearance.EnableAnimations();
        using var f = new Fixture(legacy: true);
        var toggle = LegacyToggle();
        f.Add(toggle);
        await f.Show();
        AssertOwner(toggle, legacy: true);
        using var events = new SemanticEvents(toggle);
        var rendered = GeometryOf(f.Window, toggle);
        SettingsControlMotion.SetEnabled(toggle, false);
        SettingsControlMotion.SetEnabled(toggle, true);
        Assert.Equal((0, 0), events.Counts);
        AssertGeometry(rendered, f.Window, toggle);
        var thumb = Part<TranslateTransform>(toggle, "Thumb");
        toggle.IsChecked = true;
        Assert.Equal((1, 0), events.Counts);
        Assert.True(Motion.IsAnimating(thumb));
        Motion.SampleAt(thumb, TimeSpan.FromMilliseconds(60));
        var semanticBefore = events.Counts;

        appearance.Reduce();

        Assert.True(toggle.IsChecked);
        Assert.False(Motion.IsAnimating(thumb));
        Assert.Equal(17, thumb.X, 6);
        AssertNoSemanticEvents(events, semanticBefore);
        AssertGeometry(rendered, f.Window, toggle);
    }

    [AvaloniaFact]
    public async Task SpecializedMenuOwnerSettlesReopensAndAcceptsReducedKeyboardCommand()
    {
        using var appearance = new AppearanceScope();
        appearance.EnableAnimations();
        using var f = new Fixture();
        var anchor = new Button { Content = "Menu", Width = 180 };
        var command = new MenuItem { Header = "Apply selected option" };
        var menu = new ContextMenu { ItemsSource = new[] { command } };
        menu.Resources.MergedDictionaries.Add(NativeAssets.Dictionary("ControlCentre"));
        menu.SetResourceReference(Control.ThemeProperty, typeof(ContextMenu));
        anchor.ContextMenu = menu;
        f.Add(anchor);
        await f.Show();
        int invoked = 0;
        command.Click += (_, _) => invoked++;
        try
        {
            menu.Open(anchor);
            Dispatcher.UIThread.RunJobs();
            Assert.True(menu.IsOpen);
            Assert.True(OperatorMenuMotion.GetEnabled(menu));
            var surface = Part<Control>(menu, "PART_MenuSurface");
            var scale = Assert.IsType<ScaleTransform>(surface.RenderTransform);
            Assert.True(Motion.IsAnimating(scale));
            Motion.SampleAt(scale, TimeSpan.FromMilliseconds(50));
            Assert.InRange(scale.ScaleY, .001, .999);
            Assert.Equal(0, invoked);

            appearance.Reduce();

            Assert.False(Motion.IsAnimating(scale));
            Assert.False(Motion.IsAnimating(surface));
            Assert.Equal(Matrix.Identity, surface.RenderTransform!.Value);
            Assert.Equal(1, surface.Opacity, 6);
            Assert.Equal(0, invoked);
            menu.Close();
            Assert.False(menu.IsOpen);
            menu.Open(anchor);
            Dispatcher.UIThread.RunJobs();
            Assert.True(menu.IsOpen);
            var reopened = Part<Control>(menu, "PART_MenuSurface");
            Assert.False(Motion.IsAnimating(reopened));
            Assert.False(Motion.IsAnimating(Assert.IsType<ScaleTransform>(reopened.RenderTransform)));
            Assert.Equal(Matrix.Identity, reopened.RenderTransform!.Value);
            Assert.Equal(0, invoked);
            Assert.True(command.Focus(NavigationMethod.Tab));
            var inputRoot = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(command));
            Key(inputRoot, PhysicalKey.Enter);
            Assert.Equal(1, invoked);
            Assert.False(menu.IsOpen);
        }
        finally
        {
            menu.Close();
            anchor.ContextMenu = null;
        }
    }
}

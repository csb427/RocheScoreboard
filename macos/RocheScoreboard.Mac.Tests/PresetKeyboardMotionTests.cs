// External authored draft, UNCOMPILED/UNRUN. Ten original-control cases.
// Existing exclusive typed-storage fixture; raw keyboard/pointer events only.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class ClubPresetStage2GatedTests
{
    private sealed class PresetReorderPolicyScope : IDisposable
    {
        private readonly bool glass = OperatorAppearance.GlassEnabled, reduce = OperatorAppearance.ReduceMotion;
        private readonly OperatorMotionStyle style = OperatorAppearance.MotionStyle;
        private readonly bool? animation = OperatorMotion.AnimationOverride;
        private readonly byte[]? bytes = File.Exists(OperatorAppearance.StoragePath) ? File.ReadAllBytes(OperatorAppearance.StoragePath) : null;
        internal void Enable()
        {
            OperatorMotion.AnimationOverride = null;
            OperatorAppearance.Configure(glass, false);
            OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);
            Assert.True(OperatorMotion.Enabled);
        }
        public void Dispose()
        {
            OperatorAppearance.ConfigureMotionStyle(style); OperatorAppearance.Configure(glass, reduce);
            OperatorMotion.AnimationOverride = animation;
            if (bytes is null) File.Delete(OperatorAppearance.StoragePath); else File.WriteAllBytes(OperatorAppearance.StoragePath, bytes);
        }
    }
    private static readonly string[] PresetReorderNames = ["First reorder club", "Second reorder club", "Third reorder club"];
    private static WrapPanel PresetReorderPanel(Control wizard, string side) => Named<WrapPanel>(wizard, side + "PresetPanel");
    private static Border PresetReorderTile(Control wizard, string side, string name) => Assert.Single(
        PresetReorderPanel(wizard, side).Children.OfType<Border>(), tile => tile.Tag is TeamPreset preset && preset.PresetName == name);
    private static string[] PresetReorderOrder(Control wizard, string side) => PresetReorderPanel(wizard, side).Children.OfType<Border>()
        .Select(tile => Assert.IsType<TeamPreset>(tile.Tag).PresetName).ToArray();
    private static string PresetReorderDraft(Control wizard) => string.Join("|", new[] { "Home", "Away" }
        .SelectMany(side => new[] { "NameBox", "AbbrBox", "ColorBox", "SecondaryBox" }.Select(suffix => Named<TextBox>(wizard, "Setup" + side + suffix).Text)));
    private static TextBlock PresetReorderStatus(Control wizard, string side) => Assert.Single(
        Named<Border>(wizard, side + "TeamCard").GetVisualDescendants().OfType<TextBlock>(), text => Equals(text.Tag, "team-preset-status:" + side));
    private static void PresetReorderKey(Window window, PhysicalKey key)
    { window.KeyPressQwerty(key, RawInputModifiers.Alt); window.KeyReleaseQwerty(key, RawInputModifiers.None); Dispatcher.UIThread.RunJobs(); }
    private static TranslateTransform PresetReorderShift(Border tile) => Assert.Single(Assert.IsType<TransformGroup>(tile.RenderTransform).Children.OfType<TranslateTransform>());
    private static async Task<(Control Wizard, NativeWindow Window)> PresetReorderOpen(StoreFixture fixture, bool cricket)
    {
        var sport = cricket ? Cricket : Afl;
        await fixture.Seed(sport, PresetReorderNames.Select(name => Other(name)).ToList());
        await fixture.Seed(cricket ? Afl : Cricket, [Other("Other sport sentinel")]);
        Control wizard = cricket ? new CricketSetupWizard() : new SetupWizard();
        NativeKeyboard.EnsureInitialized(); // Existing production bridge for a standalone native host.
        var owner = fixture.Track(new NativeWindow { Width = 1280, Height = 1000, Content = wizard }); owner.Show();
        await Until(() => wizard.IsLoaded && PresetReorderPanel(wizard, "Home").IsEffectivelyVisible
            && PresetReorderOrder(wizard, "Home").Length == 3 && PresetActions(wizard).All(button => button.IsEnabled),
            "Original wizard must publish the three confirmed isolated clubs before reorder input.");
        return (wizard, owner);
    }
    private static void PresetReorderNoSelection(Control wizard)
    {
        foreach (string side in new[] { "Home", "Away" })
            Assert.All(PresetReorderPanel(wizard, side).Children.OfType<Border>(), tile => Assert.Equal("Not selected", AutomationProperties.GetItemStatus(tile)));
    }

    [AvaloniaTheory]
    [InlineData(false, "Home")]
    [InlineData(false, "Away")]
    [InlineData(true, "Home")]
    [InlineData(true, "Away")]
    public async Task PresetKeyboardMotionAltReorderUsesAuthoritativeOrderBusyBoundaryAndStableSideFocus(bool cricket, string side)
    {
        await using var fixture = await StoreFixture.Create();
        var (wizard, window) = await PresetReorderOpen(fixture, cricket); var sport = cricket ? Cricket : Afl;
        string draft = PresetReorderDraft(wizard), otherSport = Json((await fixture.Read(cricket ? Afl : Cricket)).Presets);
        byte[]?[] sentinelBytes = new[] { fixture.PathFor(cricket ? Afl : Cricket), fixture.PathFor(cricket ? Afl : Cricket) + ".bak" }
            .Select(path => File.Exists(path) ? File.ReadAllBytes(path) : null).ToArray();
        var first = PresetReorderTile(wizard, side, PresetReorderNames[0]);
        first.BringIntoView(); Assert.True(first.Focus(NavigationMethod.Tab)); Dispatcher.UIThread.RunJobs();
        GateIo gate = fixture.GateWrites();
        PresetReorderKey(window, PhysicalKey.ArrowUp);
        Assert.Equal(0, gate.WriteCalls); Assert.Equal(PresetReorderNames, (await fixture.Read(sport)).Presets.Select(preset => preset.PresetName));
        PresetReorderKey(window, PhysicalKey.ArrowDown);
        await Until(() => gate.Entered.Task.IsCompleted, "The physical Alt+Down must enter one typed reorder write.");
        PresetReorderKey(window, PhysicalKey.ArrowDown); PresetReorderKey(window, PhysicalKey.ArrowDown);
        Assert.Equal(1, gate.WriteCalls); Assert.Equal(PresetReorderNames, PresetReorderOrder(wizard, side));
        Assert.Contains("Saving club order", PresetReorderStatus(wizard, side).Text);
        gate.Release(); var saved = await fixture.Read(sport);
        string[] expected = [PresetReorderNames[1], PresetReorderNames[0], PresetReorderNames[2]];
        Assert.Equal(expected, saved.Presets.Select(preset => preset.PresetName));
        await Until(() => PresetReorderOrder(wizard, side).SequenceEqual(expected) && PresetActions(wizard).All(button => button.IsEnabled),
            "Only the confirmed typed result may repaint both visible preset panels.");
        Assert.Equal(expected, PresetReorderOrder(wizard, side == "Home" ? "Away" : "Home"));
        var current = PresetReorderTile(wizard, side, PresetReorderNames[0]); Assert.True(current.IsFocused);
        Assert.Equal(PresetReorderNames[0] + " · " + side + " preset", AutomationProperties.GetName(current));
        Assert.Contains("Alt+Up", AutomationProperties.GetHelpText(current)); Assert.Contains("Alt+Down", AutomationProperties.GetHelpText(current));
        Assert.Equal(draft, PresetReorderDraft(wizard)); PresetReorderNoSelection(wizard); Assert.Equal(1, gate.WriteCalls);
        window.Width = 420; window.Height = 320;
        await Until(() =>
        {
            // A resize invalidates layout and the renderer's hit-test scene.
            // Request reveal against the current frame, then wait for the
            // exact compact viewport and every real target probe to agree.
            current.BringIntoView(); using var frame = window.CaptureRenderedFrame();
            if (frame is null || window.ClientSize != new Size(420, 320) || !current.IsFocused) return false;
            Point[] corners = [new(0, 0), new(current.Bounds.Width, 0), new(0, current.Bounds.Height), new(current.Bounds.Width, current.Bounds.Height)];
            var points = corners.Select(point => current.TranslatePoint(point, window)).ToArray();
            if (points.Any(point => point is null)) return false;
            double left = points.Min(point => point!.Value.X), top = points.Min(point => point!.Value.Y);
            var box = new Rect(left, top, points.Max(point => point!.Value.X) - left, points.Max(point => point!.Value.Y) - top);
            if (box.Width < 20 || box.Height < 20 || !new Rect(window.ClientSize).Contains(box)) return false;
            Point[] probes = [box.Center, new(box.Left + 4, box.Center.Y), new(box.Right - 4, box.Center.Y), new(box.Center.X, box.Top + 4), new(box.Center.X, box.Bottom - 4)];
            return probes.All(point => ReferenceEquals(window.InputHitTest(point), current)
                || window.InputHitTest(point) is Visual hit && hit.GetVisualAncestors().Contains(current));
        }, "The current identity must retain focus and all five actual input probes after compact reflow.");
        Point point = await PointerTarget(window, current); Assert.True(current.IsFocused); Assert.Equal(draft, PresetReorderDraft(wizard));
        Assert.True(point.X >= 0 && point.Y >= 0);
        Assert.Equal(otherSport, Json((await fixture.Read(cricket ? Afl : Cricket)).Presets));
        var sentinelAfter = new[] { fixture.PathFor(cricket ? Afl : Cricket), fixture.PathFor(cricket ? Afl : Cricket) + ".bak" }
            .Select(path => File.Exists(path) ? File.ReadAllBytes(path) : null).ToArray();
        for (int i = 0; i < sentinelBytes.Length; i++) Assert.Equal(sentinelBytes[i], sentinelAfter[i]);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PresetKeyboardMotionStaleTypedOrderRefreshesConflictThenExplicitRetryUsesFreshIdentity(bool cricket)
    {
        await using var fixture = await StoreFixture.Create();
        var (wizard, window) = await PresetReorderOpen(fixture, cricket); var sport = cricket ? Cricket : Afl;
        string draft = PresetReorderDraft(wizard); var first = PresetReorderTile(wizard, "Home", PresetReorderNames[0]);
        first.BringIntoView(); Assert.True(first.Focus(NavigationMethod.Tab));
        GateIo gate = fixture.GateWrites();
        var external = fixture.Watch(PresetStorage.ReorderAsync(sport, PresetReorderNames, 2, 0));
        await Until(() => gate.Entered.Task.IsCompleted, "A distinct typed writer must hold the store ahead of the keyboard request.");
        PresetReorderKey(window, PhysicalKey.ArrowDown);
        Assert.False(PresetActions(wizard).All(button => button.IsEnabled));
        gate.Release(); Assert.True((await external).Succeeded);
        var confirmed = await fixture.Read(sport);
        string[] changed = [PresetReorderNames[2], PresetReorderNames[0], PresetReorderNames[1]];
        await Until(() => PresetReorderOrder(wizard, "Home").SequenceEqual(changed)
            && (PresetReorderStatus(wizard, "Home").Text ?? "").Contains("arranging", StringComparison.OrdinalIgnoreCase)
            && PresetActions(wizard).All(button => button.IsEnabled), "The stale key intent must show the typed conflict and current snapshot, without writing stale indices.");
        Assert.Equal(changed, confirmed.Presets.Select(preset => preset.PresetName)); Assert.Equal(1, gate.WriteCalls);
        Assert.Equal(draft, PresetReorderDraft(wizard)); PresetReorderNoSelection(wizard);
        var current = PresetReorderTile(wizard, "Home", PresetReorderNames[0]); Assert.True(current.IsFocused);
        PresetReorderKey(window, PhysicalKey.ArrowDown); var retried = await fixture.Read(sport);
        string[] expected = [PresetReorderNames[2], PresetReorderNames[1], PresetReorderNames[0]];
        await Until(() => PresetReorderOrder(wizard, "Home").SequenceEqual(expected) && PresetActions(wizard).All(button => button.IsEnabled), "A fresh explicit key must use the refreshed identity and indices.");
        Assert.Equal(expected, retried.Presets.Select(preset => preset.PresetName)); Assert.Equal(2, gate.WriteCalls);
        Assert.True(PresetReorderTile(wizard, "Home", PresetReorderNames[0]).IsFocused);
        Assert.Equal(draft, PresetReorderDraft(wizard)); PresetReorderNoSelection(wizard);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PresetKeyboardMotionLiveReduceOrCalmCancelsWitnessedDragWithoutCommitAndFreshDropStillWorks(bool cricket, bool calm)
    {
        await using var fixture = await StoreFixture.Create(); using var policy = new PresetReorderPolicyScope();
        var (wizard, window) = await PresetReorderOpen(fixture, cricket); var sport = cricket ? Cricket : Afl;
        policy.Enable();
        string draft = PresetReorderDraft(wizard), original = Json((await fixture.Read(sport)).Presets);
        byte[] main = File.ReadAllBytes(fixture.PathFor(sport));
        var first = PresetReorderTile(wizard, "Home", PresetReorderNames[0]); var second = PresetReorderTile(wizard, "Home", PresetReorderNames[1]);
        Point from = await PointerTarget(window, first), to = await PointerTarget(window, second);
        GateIo gate = fixture.GateWrites(); gate.Release(); NativePointer.EnsureInitialized();
        try
        {
            window.MouseMove(from); window.MouseDown(from, MouseButton.Left); window.MouseMove(to);
            var sibling = PresetReorderShift(second);
            await Until(() => NativePointer.IsCaptured(first) && first.Opacity == .85 && Motion.IsAnimating(sibling)
                && Math.Abs(sibling.X) + Math.Abs(sibling.Y) > .01, "Require an actually progressing sibling clock and captured drag before changing live policy.");
            if (calm) OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Calm);
            else OperatorAppearance.Configure(OperatorAppearance.GlassEnabled, true);
            Assert.False(NativePointer.IsCaptureWithin(wizard));
            foreach (var tile in PresetReorderPanel(wizard, "Home").Children.OfType<Border>())
            { var shift = PresetReorderShift(tile); Assert.False(Motion.IsAnimating(shift)); Assert.Equal(0d, shift.X); Assert.Equal(0d, shift.Y); Assert.Equal(1d, tile.Opacity); }
            window.MouseUp(to, MouseButton.Left);
            Assert.Equal(original, Json((await fixture.Read(sport)).Presets)); Assert.Equal(0, gate.WriteCalls);
            Assert.Equal(main, File.ReadAllBytes(fixture.PathFor(sport))); Assert.Equal(draft, PresetReorderDraft(wizard)); PresetReorderNoSelection(wizard);
            first = PresetReorderTile(wizard, "Home", PresetReorderNames[0]); second = PresetReorderTile(wizard, "Home", PresetReorderNames[1]);
            from = await PointerTarget(window, first); to = await PointerTarget(window, second);
            window.MouseMove(from); window.MouseDown(from, MouseButton.Left); window.MouseMove(to);
            if (!calm) Assert.False(Motion.IsAnimating(PresetReorderShift(second)));
            window.MouseUp(to, MouseButton.Left); var accepted = await fixture.Read(sport);
            Assert.Equal(new[] { PresetReorderNames[1], PresetReorderNames[0], PresetReorderNames[2] }, accepted.Presets.Select(preset => preset.PresetName));
            Assert.Equal(1, gate.WriteCalls); Assert.Equal(draft, PresetReorderDraft(wizard)); PresetReorderNoSelection(wizard);
        }
        finally
        {
            // An early baseline assertion must cancel capture before releasing
            // the physical button; cleanup must not manufacture an accepted drop.
            NativePointer.Release(first); window.MouseUp(to, MouseButton.Left); Dispatcher.UIThread.RunJobs();
        }
    }
}

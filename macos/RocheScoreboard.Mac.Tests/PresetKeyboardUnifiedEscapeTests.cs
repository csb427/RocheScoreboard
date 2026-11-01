// External follow-up only, UNCOMPILED/UNRUN. The original ten-case file is
// unchanged. These two cases enable the real production unified component mode
// and route genuine Window Escape events through its XAML-wired handler.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class ClubPresetStage2GatedTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PresetKeyboardMotionUnifiedEscapeRetiresOnlyCapturedPreviewBeforeNormalSetupCancellation(bool cricket)
    {
        await using var fixture = await StoreFixture.Create();
        var (wizard, window) = await PresetReorderOpen(fixture, cricket);
        var sport = cricket ? Cricket : Afl;
        int cancellations = 0;
        if (wizard is CricketSetupWizard cricketWizard)
        {
            cricketWizard.EnableUnifiedSetup();
            Assert.True(cricketWizard.UnifiedSetupEnabled);
            cricketWizard.CancelSetupRequested += () => cancellations++;
        }
        else
        {
            var aflWizard = Assert.IsType<SetupWizard>(wizard);
            aflWizard.EnableUnifiedSetup();
            Assert.True(aflWizard.UnifiedSetupEnabled);
            aflWizard.CancelSetupRequested += () => cancellations++;
        }
        Assert.True(Named<Button>(wizard, "CancelSetupButton").IsVisible);
        string draft = PresetReorderDraft(wizard), order = Json((await fixture.Read(sport)).Presets);
        string other = Json((await fixture.Read(cricket ? Afl : Cricket)).Presets);
        byte[] main = File.ReadAllBytes(fixture.PathFor(sport));
        byte[]? backup = File.Exists(fixture.PathFor(sport) + ".bak") ? File.ReadAllBytes(fixture.PathFor(sport) + ".bak") : null;
        Border first = PresetReorderTile(wizard, "Home", PresetReorderNames[0]);
        Border second = PresetReorderTile(wizard, "Home", PresetReorderNames[1]);
        Point from = await PointerTarget(window, first), to = await PointerTarget(window, second);
        Assert.True(first.Focus(NavigationMethod.Tab));
        Assert.Same(first, window.FocusManager?.GetFocusedElement());
        GateIo gate = fixture.GateWrites(); gate.Release(); NativePointer.EnsureInitialized();
        void Escape()
        {
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }
        try
        {
            window.MouseMove(from); window.MouseDown(from, MouseButton.Left); window.MouseMove(to);
            Assert.True(NativePointer.IsCaptured(first)); Assert.Equal(.85, first.Opacity);
            Assert.Equal(order, Json((await fixture.Read(sport)).Presets));
            Escape();
            // First semantic oracle: an active drag's Escape must never invoke
            // the earlier XAML-wired whole-setup cancellation callback.
            Assert.Equal(0, cancellations);
            Assert.False(NativePointer.IsCaptureWithin(wizard));
            Assert.Equal(draft, PresetReorderDraft(wizard)); Assert.Equal(0, gate.WriteCalls);
            PresetReorderNoSelection(wizard);
            foreach (var tile in PresetReorderPanel(wizard, "Home").Children.OfType<Border>())
            {
                var shift = PresetReorderShift(tile);
                Assert.False(Motion.IsAnimating(shift)); Assert.Equal(0d, shift.X); Assert.Equal(0d, shift.Y); Assert.Equal(1d, tile.Opacity);
            }
            // Releasing the retired physical gesture must still be a no-op.
            window.MouseUp(to, MouseButton.Left);
            Assert.Equal(order, Json((await fixture.Read(sport)).Presets));
            Assert.Equal(main, File.ReadAllBytes(fixture.PathFor(sport)));
            Assert.Equal(backup, File.Exists(fixture.PathFor(sport) + ".bak") ? File.ReadAllBytes(fixture.PathFor(sport) + ".bak") : null);
            Assert.Equal(other, Json((await fixture.Read(cricket ? Afl : Cricket)).Presets));
            Assert.Equal(draft, PresetReorderDraft(wizard)); Assert.Equal(0, gate.WriteCalls); PresetReorderNoSelection(wizard);
            Escape();
            Assert.Equal(1, cancellations); // No drag now: preserve normal unified cancellation.
            Assert.False(NativePointer.IsCaptureWithin(wizard)); Assert.Equal(0, gate.WriteCalls);
            Assert.Equal(draft, PresetReorderDraft(wizard));
        }
        finally
        {
            NativePointer.Release(first); window.MouseUp(to, MouseButton.Left); Dispatcher.UIThread.RunJobs();
        }
    }
}

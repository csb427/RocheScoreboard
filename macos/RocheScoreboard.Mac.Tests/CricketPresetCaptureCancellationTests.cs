// Outside proposal only; uncompiled/unrun. Reuses existing nonparallel
// ClubPresetStage2GatedTests StoreFixture, actual pointer helpers and backend.
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class ClubPresetStage2GatedTests
{
    private static string CricketCaptureDraft(CricketSetupWizard wizard, string? side = null) =>
        string.Join("|", (side is null ? new[] { "Home", "Away" } : new[] { side })
            .SelectMany(team => new[] { "Name", "Abbr", "Color", "Secondary" }
                .Select(part => Named<TextBox>(wizard, "Setup" + team + part + "Box").Text)));
    private static byte[]?[] CricketCaptureFiles(StoreFixture fixture) =>
        new[] { Afl, Cricket }.SelectMany(sport => new[] { fixture.PathFor(sport), fixture.PathFor(sport) + ".bak", fixture.PathFor(sport) + ".tmp" })
            .Select(path => File.Exists(path) ? File.ReadAllBytes(path) : null).ToArray();
    private static void CricketCaptureFilesEqual(byte[]?[] before, StoreFixture fixture)
    {
        var after = CricketCaptureFiles(fixture); Assert.Equal(before.Length, after.Length);
        for (int i = 0; i < before.Length; i++) Assert.Equal(before[i], after[i]);
    }
    private static Border CricketCaptureTile(CricketSetupWizard wizard, string side, string name) =>
        Assert.Single(Named<WrapPanel>(wizard, side + "PresetPanel").Children.OfType<Border>(),
            tile => tile.Tag is TeamPreset preset && preset.PresetName == name);
    private static void CricketCaptureRecord(string side, bool preview, object value)
    {
        if (Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"cricket-capture-{side}-preview-{preview}.json"),
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }

    [AvaloniaTheory]
    [InlineData("Home", false)]
    [InlineData("Away", false)]
    [InlineData("Home", true)]
    [InlineData("Away", true)]
    public async Task CricketExternalCaptureTransferCancelsInsteadOfApplyingOrPersistingPreview(string side, bool preview)
    {
        await using var fixture = await StoreFixture.Create();
        await fixture.Seed(Afl, [Other("Unchanged AFL sentinel")]);
        await fixture.Seed(Cricket, [Other("First cricket club"), Other("Second cricket club"), Other("Third cricket club")]);
        var wizard = new CricketSetupWizard();
        var window = fixture.Track(new NativeWindow { Width = 1280, Height = 1000, Content = wizard });
        window.Show();
        var panel = Named<WrapPanel>(wizard, side + "PresetPanel");
        await Until(() => wizard.IsLoaded && panel.IsEffectivelyVisible
            && panel.Children.OfType<Border>().Count() == 3 && PresetActions(wizard).All(button => button.IsEnabled),
            "Original Cricket team preset panel must load confirmed isolated clubs.");
        Border tile = CricketCaptureTile(wizard, side, "First cricket club");
        Border next = CricketCaptureTile(wizard, side, "Second cricket club");
        Point start = await PointerTarget(window, tile), destination = await PointerTarget(window, next);
        Assert.True(Math.Sqrt(Math.Pow(destination.X-start.X,2)+Math.Pow(destination.Y-start.Y,2)) > 6);
        var sink = Named<TextBox>(wizard, "Setup" + side + "NameBox");
        string draft = CricketCaptureDraft(wizard), otherSide = side == "Home" ? "Away" : "Home";
        string opposite = CricketCaptureDraft(wizard, otherSide);
        string order = Json((await fixture.Read(Cricket)).Presets);
        string afl = Json((await fixture.Read(Afl)).Presets);
        byte[]?[] files = CricketCaptureFiles(fixture);
        var gate = fixture.GateWrites(); gate.Release(); // Pass-through actual backend, counted writes.
        IPointer? pointer = null; int losses = 0;
        tile.AddHandler(InputElement.PointerPressedEvent, (_, args) => pointer = args.Pointer,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        tile.PointerCaptureLost += (_, _) => losses++;
        NativePointer.EnsureInitialized();
        try
        {
            window.MouseMove(start); window.MouseDown(start, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.NotNull(pointer); Assert.Same(tile, pointer.Captured); Assert.True(NativePointer.IsCaptured(tile));
            if (preview)
            {
                window.MouseMove(destination); Dispatcher.UIThread.RunJobs();
                Assert.True(NativePointer.IsCaptured(tile));
                Assert.Equal(.85, tile.Opacity); // Existing real threshold/drag-preview branch reached.
            }
            // No accepted Up. Transfer the SAME real pointer captured by Down.
            pointer.Capture(sink);
            var after = await fixture.Read(Cricket); await Task.Delay(60); Dispatcher.UIThread.RunJobs();
            Assert.True(losses >= 1, "A real capture-loss event must be witnessed.");
            string afterOrder = Json(after.Presets), afterDraft = CricketCaptureDraft(wizard);
            CricketCaptureRecord(side, preview, new { side, preview, losses, writes = gate.WriteCalls,
                draft, afterDraft, order, afterOrder, captured = pointer.Captured?.GetType().FullName,
                tileCaptured = NativePointer.IsCaptured(tile), tile.Opacity });
            // Semantic/persistence oracles precede future visual/API assumptions.
            Assert.Equal(draft, afterDraft); Assert.Equal(order, afterOrder); Assert.Equal(0, gate.WriteCalls);
            Assert.Equal(afl, Json((await fixture.Read(Afl)).Presets)); CricketCaptureFilesEqual(files, fixture);
            Assert.Same(sink, pointer.Captured); Assert.False(NativePointer.IsCaptured(tile));
            pointer.Capture(null); window.MouseUp(preview ? destination : start, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            // Positive control: fresh explicit Up keeps the current intentional
            // selection/reorder behavior. Cancellation must not disable either.
            Border fresh = CricketCaptureTile(wizard, side, "First cricket club");
            Point from = await PointerTarget(window, fresh);
            if (!preview)
            {
                window.MouseMove(from); window.MouseDown(from, MouseButton.Left); window.MouseUp(from, MouseButton.Left);
                await Until(() => Named<TextBox>(wizard, "Setup" + side + "NameBox").Text == "First cricket club",
                    "Explicit original MouseUp must still select the intended Cricket side.");
                await fixture.Read(Cricket); Dispatcher.UIThread.RunJobs();
                Assert.Equal(opposite, CricketCaptureDraft(wizard, otherSide));
                Assert.Equal(order, Json((await fixture.Read(Cricket)).Presets));
                Assert.Equal(0, gate.WriteCalls); CricketCaptureFilesEqual(files, fixture);
            }
            else
            {
                Point to = await PointerTarget(window, CricketCaptureTile(wizard, side, "Second cricket club"));
                window.MouseMove(from); window.MouseDown(from, MouseButton.Left);
                window.MouseMove(to); window.MouseUp(to, MouseButton.Left);
                var accepted = await fixture.Read(Cricket);
                await Until(() => gate.WriteCalls == 1, "One accepted drag must perform one actual backend write.");
                Assert.Equal(new[] { "Second cricket club", "First cricket club", "Third cricket club" },
                    accepted.Presets.Select(preset => preset.PresetName));
                Assert.Equal(draft, CricketCaptureDraft(wizard));
                Assert.Equal(afl, Json((await fixture.Read(Afl)).Presets));
                await fixture.Read(Cricket); Assert.Equal(1, gate.WriteCalls);
            }
            Assert.False(NativePointer.IsCaptureWithin(wizard));
        }
        finally
        {
            pointer?.Capture(null); window.MouseUp(preview ? destination : start, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
    }
}

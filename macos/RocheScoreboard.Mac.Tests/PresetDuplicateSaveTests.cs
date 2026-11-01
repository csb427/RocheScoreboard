using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class BreakRotationModeIsolationTests
{
    private static List<TeamPreset> CollisionClubs(bool ambiguous) => [
        new() { PresetName = "Collision FC", TeamName = "Collision FC", Abbreviation = "ONE", PrimaryColor = "#123456", SecondaryColor = "#FFFFFF" },
        new() { PresetName = ambiguous ? "Collision FC" : "Other FC", TeamName = ambiguous ? "Collision FC" : "Other FC", Abbreviation = "TWO", PrimaryColor = "#654321", SecondaryColor = "#FFFFFF" },
        new() { PresetName = "Control FC", TeamName = "Control FC", Abbreviation = "CON", PrimaryColor = "#234567", SecondaryColor = "#FFFFFF" }];
    [AvaloniaTheory]
    [InlineData(false, "ambiguous")] [InlineData(true, "ambiguous")]
    [InlineData(false, "casefold")] [InlineData(true, "casefold")]
    [InlineData(false, "unique")] [InlineData(true, "unique")]
    [InlineData(false, "new")] [InlineData(true, "new")]
    public async Task ClubSaveCannotGuessDuplicateIdentityButKeepsUniqueUpsertAndNewAppend(bool cricket, string operation)
    {
        await using var f = await KbRoot.Create(); var sport = cricket ? KbCricket : KbAfl;
        var initial = CollisionClubs(operation is "ambiguous" or "casefold");
        if (operation == "casefold") initial[1].PresetName = "cOLLISION fC";
        if (cricket) await PresetStorage.SaveAllCricketAsync(initial); else await PresetStorage.SaveAllAsync(initial);
        var before = await PresetStorage.ReadAsync(sport); var bytes = f.Files.Bytes();
        var counted = new KbCountingIo(PresetStorage.CurrentIoForTests);
        using (PresetStorage.UseIoForTests(counted))
        {
            string name = operation == "new" ? "Brand new FC" : "Collision FC";
            var draft = new TeamPreset { PresetName = name, TeamName = name, Abbreviation = "NEW", PrimaryColor = "#ABCDEF", SecondaryColor = "#FEDCBA" };
            var result = await PresetStorage.SavePresetAsync(sport, draft, false);
            KbEvidence("duplicate-save-api-" + cricket + "-" + operation, new { cricket, operation, result.Status, counted.Writes,
                before = before.Presets.Select(p => new { p.PresetName, p.Abbreviation, p.PrimaryColor }),
                after = result.Snapshot.Presets.Select(p => new { p.PresetName, p.Abbreviation, p.PrimaryColor }) });
            if (operation is "ambiguous" or "casefold")
            {
                Assert.Equal(PresetCommitStatus.Conflict, result.Status); Assert.False(result.Succeeded); Assert.Equal(0, counted.Writes);
                Assert.Equal(before.Revision, result.Snapshot.Revision); Assert.True(PresetStorage.SameContents(initial, result.Snapshot.Presets));
                f.Files.AssertBytes(bytes); Assert.Contains("Multiple saved clubs", result.Error);
            }
            else
            {
                Assert.Equal(PresetCommitStatus.Committed, result.Status); Assert.Equal(1, counted.Writes);
                Assert.Equal(operation == "new" ? 4 : 3, result.Snapshot.Presets.Count);
                var actual = Assert.Single(result.Snapshot.Presets, p => p.PresetName == name);
                Assert.Equal("NEW", actual.Abbreviation); Assert.Equal("#ABCDEF", actual.PrimaryColor); Assert.Equal("#FEDCBA", actual.SecondaryColor);
                var retained = operation == "new" ? result.Snapshot.Presets.Take(3) : result.Snapshot.Presets.Skip(1);
                Assert.True(PresetStorage.SameContents(operation == "new" ? initial : initial.Skip(1), retained));
            }
        }
    }
    [AvaloniaTheory]
    [InlineData("Home")] [InlineData("Away")]
    public async Task ActualAflDuplicateSavePreservesDraftFilesAndCanRetryWithDistinctName(string side)
    {
        await using var f = await KbRoot.Create(); var initial = CollisionClubs(true);
        await PresetStorage.SaveAllAsync(initial); await PresetStorage.ReadAsync(KbAfl); f.Wizard.Reset();
        await Until(() => Named<WrapPanel>(f.Wizard, side + "PresetPanel").Children.OfType<Border>().Any(t => t.Tag is TeamPreset p && p.Abbreviation == "TWO"), "Confirmed duplicate records must be published.");
        var tile = Assert.Single(Named<WrapPanel>(f.Wizard, side + "PresetPanel").Children.OfType<Border>(), t => t.Tag is TeamPreset p && p.Abbreviation == "TWO");
        await KbTabTo(f, tile, side); KbProvider(tile, "Collision FC", side).Invoke(); await Pump(40);
        Named<TextBox>(f.Wizard, "Setup" + side + "ColorBox").Text = "#778899";
        string draft = KbDraft(f.Wizard, side); var bytes = f.Files.Bytes(); var recovery = f.Recovery();
        var surface = Named<TextBox>(f.Wizard, "Setup" + side + "NameBox").GetVisualAncestors().OfType<OperatorGlassSurface>().First();
        var save = Assert.Single(surface.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Save as preset"));
        var status = Assert.Single(surface.GetVisualDescendants().OfType<TextBlock>(), t => Equals(t.Tag, "team-preset-status:" + side));
        var counted = new KbCountingIo(PresetStorage.CurrentIoForTests);
        using (PresetStorage.UseIoForTests(counted))
        {
            await KbClick(f.Main, save);
            await Until(() => status.IsEffectivelyVisible && !status.Text!.StartsWith("Saving", StringComparison.Ordinal) && save.IsEffectivelyEnabled, "Original save must finish and expose feedback.");
            KbEvidence("duplicate-save-ui-" + side, new { side, status.Text, counted.Writes, beforeDraft = draft, afterDraft = KbDraft(f.Wizard, side) });
            Assert.Contains("Multiple saved clubs", status.Text); Assert.Equal(0, counted.Writes);
            Assert.Equal(draft, KbDraft(f.Wizard, side)); f.Files.AssertBytes(bytes); f.AssertRecovery(recovery);
            Named<TextBox>(f.Wizard, "Setup" + side + "NameBox").Text = "Distinct retry FC";
            await KbClick(f.Main, save); await Until(() => status.Text?.Contains("saved as a preset", StringComparison.Ordinal) == true && save.IsEffectivelyEnabled, "Distinct retry must use existing success workflow.");
            Assert.Equal(1, counted.Writes); var after = await PresetStorage.ReadAsync(KbAfl);
            Assert.Equal(4, after.Presets.Count); Assert.True(PresetStorage.SameContents(initial, after.Presets.Take(3)));
            var added = Assert.Single(after.Presets, p => p.PresetName == "Distinct retry FC"); Assert.Equal("#778899", added.PrimaryColor);
            f.AssertRecovery(recovery);
        }
    }
}

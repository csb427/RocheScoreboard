using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class BreakRotationModeIsolationTests
{
    private static async Task SeedSameNameClubs(KbRoot f)
    {
        await PresetStorage.SaveAllAsync([
            new() { PresetName = "Shared key", TeamName = "North identity club", Abbreviation = "NTH", PrimaryColor = "#123456", SecondaryColor = "#FFFFFF" },
            new() { PresetName = "Shared key", TeamName = "South identity club", Abbreviation = "STH", PrimaryColor = "#654321", SecondaryColor = "#FFFFFF" },
            new() { PresetName = "Unique key", TeamName = "Unique identity club", Abbreviation = "UNI", PrimaryColor = "#234567", SecondaryColor = "#FFFFFF" }]);
        await PresetStorage.ReadAsync(TeamLibrarySport.AustralianFootball);
        f.Wizard.Reset();
        await Until(() => Named<WrapPanel>(f.Wizard, "HomePresetPanel").Children.OfType<Border>().Any(t => t.Tag is TeamPreset p && p.TeamName == "South identity club"),
            "Confirmed same-name records must appear in the real saved-club panel.");
    }
    private static Border IdentityTile(KbRoot f, string side, string team)
        => Assert.Single(Named<WrapPanel>(f.Wizard, side + "PresetPanel").Children.OfType<Border>(), t => t.Tag is TeamPreset p && p.TeamName == team);
    private static Border[] SelectedIdentityTiles(KbRoot f, string side)
        => Named<WrapPanel>(f.Wizard, side + "PresetPanel").Children.OfType<Border>()
            .Where(t => AutomationProperties.GetItemStatus(t) == "Selected").ToArray();
    [AvaloniaTheory]
    [InlineData("Home", false)] [InlineData("Away", false)] [InlineData("Home", true)] [InlineData("Away", true)]
    public async Task SameNameSavedClubsShowOnlyTheActualInvokedIdentityAcrossRapidReversals(string side, bool animate)
    {
        bool? previous = OperatorMotion.AnimationOverride; OperatorMotion.AnimationOverride = animate;
        try
        {
            await using var f = await KbRoot.Create(); await SeedSameNameClubs(f);
            string other = side == "Home" ? "Away" : "Home", otherDraft = KbDraft(f.Wizard, other);
            var saved = f.Files.Bytes(); var recovery = f.Recovery();
            foreach (string team in new[] { "South identity club", "North identity club", "South identity club" })
            {
                var tile = IdentityTile(f, side, team); await KbTabTo(f, tile, side); KbProvider(tile, team, side).Invoke(); await Pump(40);
                KbEvidence("same-name-" + side + "-" + animate + "-" + team, new
                { side, animate, team, draft = KbDraft(f.Wizard, side), tile.IsLoaded, tile.IsEffectivelyVisible, selected = SelectedIdentityTiles(f, side).Select(t => ((TeamPreset)t.Tag!).TeamName).ToArray() });
                Assert.Same(tile, Assert.Single(SelectedIdentityTiles(f, side)));
                Assert.Equal(team, Named<TextBox>(f.Wizard, "Setup" + side + "NameBox").Text);
            }
            await Pump(60); Assert.Equal(otherDraft, KbDraft(f.Wizard, other));
            f.Files.AssertBytes(saved); f.AssertRecovery(recovery);
            KbScreenshot(f.Main, "same-name-selection-" + side + "-" + animate);
        }
        finally { OperatorMotion.AnimationOverride = previous; }
    }
    [AvaloniaTheory]
    [InlineData("ambiguous")] [InlineData("unique")] [InlineData("snapshot")] [InlineData("unique-snapshot")]
    public async Task RememberedClubKeyNeverGuessesBetweenDuplicateRecordsAndKeepsExactDraft(string context)
    {
        await using var f = await KbRoot.Create(); await SeedSameNameClubs(f);
        OperatorWorkflowPreferences.Update(p =>
        {
            p.LastHomePresetKey = context.StartsWith("unique", StringComparison.Ordinal) ? "preset:Unique key" : "preset:Shared key";
            p.LastHomeTeam = context.EndsWith("snapshot", StringComparison.Ordinal) ? new TeamPreset { PresetName = context == "unique-snapshot" ? "Unique key" : "Shared key", TeamName = "Exact remembered operator draft", Abbreviation = "EXA", PrimaryColor = "#456789", SecondaryColor = "#FEDCBA" } : null;
        });
        var saved = f.Files.Bytes(); var recovery = f.Recovery(); f.Wizard.Reset(); await Pump(100);
        string expected = context == "unique" ? "Unique identity club" : context.EndsWith("snapshot", StringComparison.Ordinal) ? "Exact remembered operator draft" : "";
        KbEvidence("remembered-identity-" + context, new { context, expected, actual = Named<TextBox>(f.Wizard, "SetupHomeNameBox").Text,
            selected = SelectedIdentityTiles(f, "Home").Select(t => ((TeamPreset)t.Tag!).TeamName).ToArray() });
        Assert.Equal(expected, Named<TextBox>(f.Wizard, "SetupHomeNameBox").Text);
        if (context.StartsWith("unique", StringComparison.Ordinal)) Assert.Equal("Unique identity club", Assert.IsType<TeamPreset>(Assert.Single(SelectedIdentityTiles(f, "Home")).Tag).TeamName);
        else Assert.Empty(SelectedIdentityTiles(f, "Home"));
        if (context.EndsWith("snapshot", StringComparison.Ordinal)) { Assert.Equal("EXA", Named<TextBox>(f.Wizard, "SetupHomeAbbrBox").Text); Assert.Equal("#456789", Named<TextBox>(f.Wizard, "SetupHomeColorBox").Text); }
        f.Files.AssertBytes(saved); f.AssertRecovery(recovery);
    }
}

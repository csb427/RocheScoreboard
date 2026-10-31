using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class LiveSettingsClockChoiceTests
{
    // Opt-in, test-only observation for the captured native synthetic-font
    // crash. Never resolves a glyph, changes a font, forces GC or edits state.
    private static void WriteNativeFontFixtureStage(Window owner, Control target, string stage)
    {
        if (Environment.GetEnvironmentVariable("ROCHE_FONT_DIAGNOSTICS") != "1") return;
        string directory = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")
            ?? throw new InvalidOperationException("Font diagnostics require an explicit fixture capture directory.");
        Directory.CreateDirectory(directory);
        var text = owner.GetVisualDescendants().OfType<TextBlock>()
            .Where(value => value.IsVisible && !string.IsNullOrEmpty(value.Text))
            .Select(value => new
            {
                value.Name, Family = value.FontFamily.ToString(),
                Weight = value.FontWeight.ToString(), Style = value.FontStyle.ToString(),
                Text = value.Text!.Length <= 100 ? value.Text : value.Text[..100]
            }).ToArray();
        var record = new
        {
            Stage = stage, Target = target.Name, TargetType = target.GetType().Name,
            FontManager = RuntimeHelpers.GetHashCode(FontManager.Current),
            Gen0 = GC.CollectionCount(0), Gen1 = GC.CollectionCount(1), Gen2 = GC.CollectionCount(2),
            RequestedFaces = text.GroupBy(value => (value.Family, value.Weight, value.Style))
                .Select(group => new { group.Key.Family, group.Key.Weight, group.Key.Style, Count = group.Count() }),
            NonAsciiText = text.Where(value => value.Text.Any(character => character > 127))
        };
        File.AppendAllText(Path.Combine(directory, "fixture-font-stages.jsonl"), JsonSerializer.Serialize(record) + "\n");
    }
}

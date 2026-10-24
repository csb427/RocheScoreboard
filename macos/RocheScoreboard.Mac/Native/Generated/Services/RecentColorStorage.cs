// Generated from Services/RecentColorStorage.cs; edit translator/adapters.
// Source SHA-256: 2bbaae07a5ee64ecaf26da57dc63bd32767583aba58872119f4f8ea1bfc8223c
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Media;
using Color = Avalonia.Media.Color;
using ColorConverter = RocheScoreboard.Mac.Native.ColorConverter;

namespace Roche_Scoreboard.Services;

internal static class RecentColorStorage
{
    private const int Capacity = 12;
    private static readonly object Sync = new();
    private static string StoragePath => Path.Combine(
        NativeStoragePaths.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Roche_Scoreboard", "recent_colours.json");

    public static IReadOnlyList<string> Load()
    {
        lock (Sync)
        {
            try
            {
                List<string>? stored = AtomicFile.TryReadJson<List<string>>(StoragePath);
                return stored is null ? Array.Empty<string>()
                    : stored.Where(IsValid).Distinct(StringComparer.OrdinalIgnoreCase).Take(Capacity).ToArray();
            }
            catch { return Array.Empty<string>(); }
        }
    }

    public static void Record(Color color)
    {
        string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        lock (Sync)
        {
            try
            {
                List<string> recent = Load().Where(value =>
                        !string.Equals(value, hex, StringComparison.OrdinalIgnoreCase))
                    .Prepend(hex).Take(Capacity).ToList();
                AtomicFile.WriteAllText(StoragePath, JsonSerializer.Serialize(recent));
            }
            catch { }
        }
    }

    private static bool IsValid(string value)
    {
        try
        {
            _ = (Color)ColorConverter.ConvertFromString(value);
            return value.Length == 7 && value[0] == '#';
        }
        catch { return false; }
    }
}

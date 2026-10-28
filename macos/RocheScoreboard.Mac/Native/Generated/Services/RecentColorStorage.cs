// Generated from Services/RecentColorStorage.cs; edit translator/adapters.
// Source SHA-256: e64947fdf46858405793a3d8c83a18cb3edc8b012f4dc0654a8c814f7beb4b0c
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Color = Avalonia.Media.Color;
using ColorConverter = RocheScoreboard.Mac.Native.ColorConverter;

namespace Roche_Scoreboard.Services;

internal static class RecentColorStorage
{
    private const int Capacity = 12;
    private static readonly object Sync = new();
    private static string StoragePath => Path.Combine(SelfTestStorage.DocumentsDirectory, "recent_colours.json");

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

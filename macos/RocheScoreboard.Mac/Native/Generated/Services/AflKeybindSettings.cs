// Generated from Services/AflKeybindSettings.cs; edit translator/adapters.
// Source SHA-256: 35f2e4417aefc97512e11010fd7fda59b49397e20c1d9dd891445c590f98d7d1
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
using System.Text.Json;
using Avalonia.Input;

namespace Roche_Scoreboard.Services;

internal static class AflKeybindSettings
{
    internal enum AflAction
    {
        ToggleClock,
        HomeGoal,
        HomeBehind,
        AwayGoal,
        AwayBehind,
        EndQuarter,
    }

    private sealed class SettingsData
    {
        public Dictionary<string, string> Bindings { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly Dictionary<AflAction, Key> DefaultBindings = new()
    {
        [AflAction.ToggleClock] = Key.Space,
        [AflAction.HomeGoal] = Key.A,
        [AflAction.HomeBehind] = Key.S,
        [AflAction.AwayGoal] = Key.K,
        [AflAction.AwayBehind] = Key.L,
        [AflAction.EndQuarter] = Key.N,
    };

    private static string GetStoragePath()
    {
        string dir = SelfTestStorage.DocumentsDirectory;
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "afl_keybind_settings.json");
    }

    private static SettingsData Load()
    {
        string path = GetStoragePath();
        try
        {
            return AtomicFile.TryReadJson<SettingsData>(path, JsonOptions) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static void Save(SettingsData data)
    {
        try
        {
            string json = JsonSerializer.Serialize(data, JsonOptions);
            AtomicFile.WriteAllText(GetStoragePath(), json);
        }
        catch
        {
        }
    }

    public static IReadOnlyDictionary<AflAction, Key> LoadBindings()
    {
        SettingsData data = Load();
        Dictionary<AflAction, Key> result = new(DefaultBindings);

        foreach (var pair in data.Bindings)
        {
            if (!Enum.TryParse<AflAction>(pair.Key, ignoreCase: true, out AflAction action))
                continue;
            if (!Enum.TryParse<Key>(pair.Value, ignoreCase: true, out Key key))
                continue;
            result[action] = key;
        }

        return result;
    }

    public static void SaveBindings(IReadOnlyDictionary<AflAction, Key> bindings)
    {
        SettingsData data = new();
        foreach (var pair in bindings)
        {
            data.Bindings[pair.Key.ToString()] = pair.Value.ToString();
        }
        Save(data);
    }

    public static IReadOnlyDictionary<AflAction, Key> GetDefaultBindings() => new Dictionary<AflAction, Key>(DefaultBindings);

    // The normal presentation router owns these keys before scoring bindings.
    // Keep loaded profiles untouched; this is availability for a new capture.
    internal static bool IsPresentationShortcut(Key key) => key is
        Key.D1 or Key.D2 or Key.D3 or Key.D4 or Key.D5 or Key.D6
        or Key.NumPad1 or Key.NumPad2 or Key.NumPad3 or Key.NumPad4 or Key.NumPad5 or Key.NumPad6;

    public static string FormatKey(Key key) => key switch
    {
        Key.Space => "Space",
        Key.NumPad0 => "Num 0",
        Key.NumPad1 => "Num 1",
        Key.NumPad2 => "Num 2",
        Key.NumPad3 => "Num 3",
        Key.NumPad4 => "Num 4",
        Key.NumPad5 => "Num 5",
        Key.NumPad6 => "Num 6",
        Key.NumPad7 => "Num 7",
        Key.NumPad8 => "Num 8",
        Key.NumPad9 => "Num 9",
        _ => key.ToString().ToUpperInvariant(),
    };
}

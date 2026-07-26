using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Input;

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
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Roche_Scoreboard");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "afl_keybind_settings.json");
    }

    private static SettingsData Load()
    {
        string path = GetStoragePath();
        if (!File.Exists(path)) return new();
        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SettingsData>(json, JsonOptions) ?? new();
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
            File.WriteAllText(GetStoragePath(), json);
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
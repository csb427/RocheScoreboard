// Generated from Services/PresetStorage.cs; edit translator/adapters.
// Source SHA-256: f2bc2e046bb56c9f09d4ef9e19f654e46ce13575d416637b6e194507ebda89c7
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
using System.Threading.Tasks;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

public static class PresetStorage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    private static string GetPresetsDirectory()
    {
        if (Avalonia.Application.Current is RocheScoreboard.Mac.App { IsSelfTestMode: true })
        {
            string isolated = Path.Combine(Path.GetTempPath(), "RocheScoreboard-SelfTests", Environment.ProcessId.ToString(), "presets");
            Directory.CreateDirectory(isolated); return isolated;
        }
        string dir = Path.Combine(
            NativeStoragePaths.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Roche_Scoreboard"
        );
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string GetPresetsPath()
        => Path.Combine(GetPresetsDirectory(), "team_presets.json");

    private static string GetCricketPresetsPath()
        => Path.Combine(GetPresetsDirectory(), "cricket_team_presets.json");

    private static List<TeamPreset> LoadFromFile(string path)
    {
        try
        {
            return AtomicFile.TryReadJson<List<TeamPreset>>(path, Options) ?? new List<TeamPreset>();
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to deserialize presets from {path}: {ex.Message}");
            return new List<TeamPreset>();
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to read presets file {path}: {ex.Message}");
            return new List<TeamPreset>();
        }
    }

    private static async Task<List<TeamPreset>> LoadFromFileAsync(string path)
    {
        try
        {
            return await AtomicFile.TryReadJsonAsync<List<TeamPreset>>(path, Options).ConfigureAwait(false)
                   ?? new List<TeamPreset>();
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to deserialize presets from {path}: {ex.Message}");
            return new List<TeamPreset>();
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to read presets file {path}: {ex.Message}");
            return new List<TeamPreset>();
        }
    }

    private static void SaveToFile(string path, List<TeamPreset> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);
        try
        {
            string json = JsonSerializer.Serialize(presets, Options);
            AtomicFile.WriteAllText(path, json);
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to serialize presets to {path}: {ex.Message}");
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to write presets file {path}: {ex.Message}");
        }
    }

    private static async Task SaveToFileAsync(string path, List<TeamPreset> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);
        try
        {
            string json = JsonSerializer.Serialize(presets, Options);
            await AtomicFile.WriteAllTextAsync(path, json).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to serialize presets to {path}: {ex.Message}");
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to write presets file asynchronously {path}: {ex.Message}");
        }
    }

    private static List<TeamPreset> MergePresets(List<TeamPreset> primary, List<TeamPreset> secondary)
    {
        Dictionary<string, TeamPreset> byName = new(StringComparer.OrdinalIgnoreCase);

        foreach (TeamPreset preset in primary.Where(static p => !string.IsNullOrWhiteSpace(p.PresetName)))
            byName[preset.PresetName] = preset;

        foreach (TeamPreset fallback in secondary.Where(static p => !string.IsNullOrWhiteSpace(p.PresetName)))
        {
            if (!byName.TryGetValue(fallback.PresetName, out TeamPreset? current))
            {
                byName[fallback.PresetName] = fallback;
                continue;
            }

            if ((current.CricketPlayers is null || current.CricketPlayers.Count == 0)
                && fallback.CricketPlayers is { Count: > 0 })
            {
                current.CricketPlayers = fallback.CricketPlayers;
            }
        }

        return byName.Values
            .OrderBy(static p => p.PresetName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<TeamPreset> LoadSharedPresetsWithLegacyCricketFallback()
    {
        List<TeamPreset> shared = LoadFromFile(GetPresetsPath());
        List<TeamPreset> legacyCricket = LoadFromFile(GetCricketPresetsPath());

        if (legacyCricket.Count == 0)
            return shared;

        List<TeamPreset> merged = MergePresets(shared, legacyCricket);

        if (merged.Count != shared.Count)
            SaveToFile(GetPresetsPath(), merged);

        return merged;
    }

    private static async Task<List<TeamPreset>> LoadSharedPresetsWithLegacyCricketFallbackAsync()
    {
        List<TeamPreset> shared = await LoadFromFileAsync(GetPresetsPath()).ConfigureAwait(false);
        List<TeamPreset> legacyCricket = await LoadFromFileAsync(GetCricketPresetsPath()).ConfigureAwait(false);

        if (legacyCricket.Count == 0)
            return shared;

        List<TeamPreset> merged = MergePresets(shared, legacyCricket);

        if (merged.Count != shared.Count)
            await SaveToFileAsync(GetPresetsPath(), merged).ConfigureAwait(false);

        return merged;
    }

    public static List<TeamPreset> LoadAll()
        => LoadFromFile(GetPresetsPath()).Where(p => !IsCricketLibraryPreset(p)).ToList();

    public static Task<List<TeamPreset>> LoadAllAsync()
        => LoadFootballPresetsAsync();

    private static async Task<List<TeamPreset>> LoadFootballPresetsAsync()
        => (await LoadFromFileAsync(GetPresetsPath()).ConfigureAwait(false)).Where(p => !IsCricketLibraryPreset(p)).ToList();

    private static bool IsCricketLibraryPreset(TeamPreset preset)
        => preset.LibraryTeamId?.StartsWith("cricket-", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsLegacyCricketPreset(TeamPreset preset)
        => IsCricketLibraryPreset(preset) || preset.CricketPlayers is { Count: > 0 };

    public static void SaveAll(List<TeamPreset> presets)
        => SaveToFile(GetPresetsPath(), presets);

    public static Task SaveAllAsync(List<TeamPreset> presets)
        => SaveToFileAsync(GetPresetsPath(), presets);

    public static List<TeamPreset> LoadAllCricket()
        => File.Exists(GetCricketPresetsPath()) ? LoadFromFile(GetCricketPresetsPath())
            : LoadFromFile(GetPresetsPath()).Where(IsLegacyCricketPreset).ToList();

    public static Task<List<TeamPreset>> LoadAllCricketAsync()
        => LoadCricketPresetsAsync();

    private static async Task<List<TeamPreset>> LoadCricketPresetsAsync()
        => File.Exists(GetCricketPresetsPath()) ? await LoadFromFileAsync(GetCricketPresetsPath()).ConfigureAwait(false)
            : (await LoadFromFileAsync(GetPresetsPath()).ConfigureAwait(false)).Where(IsLegacyCricketPreset).ToList();

    public static void SaveAllCricket(List<TeamPreset> presets)
        => SaveToFile(GetCricketPresetsPath(), presets);

    public static Task SaveAllCricketAsync(List<TeamPreset> presets)
        => SaveToFileAsync(GetCricketPresetsPath(), presets);
}

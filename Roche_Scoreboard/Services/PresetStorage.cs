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
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
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
        => LoadSharedPresetsWithLegacyCricketFallback();

    public static Task<List<TeamPreset>> LoadAllAsync() 
        => LoadSharedPresetsWithLegacyCricketFallbackAsync();

    public static void SaveAll(List<TeamPreset> presets) 
        => SaveToFile(GetPresetsPath(), presets);

    public static Task SaveAllAsync(List<TeamPreset> presets) 
        => SaveToFileAsync(GetPresetsPath(), presets);

    public static List<TeamPreset> LoadAllCricket() 
        => LoadSharedPresetsWithLegacyCricketFallback();

    public static Task<List<TeamPreset>> LoadAllCricketAsync() 
        => LoadSharedPresetsWithLegacyCricketFallbackAsync();

    public static void SaveAllCricket(List<TeamPreset> presets) 
        => SaveToFile(GetPresetsPath(), presets);

    public static Task SaveAllCricketAsync(List<TeamPreset> presets) 
        => SaveToFileAsync(GetPresetsPath(), presets);
}

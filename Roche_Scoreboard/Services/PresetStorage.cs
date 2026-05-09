using System;
using System.Collections.Generic;
using System.IO;
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
            if (!File.Exists(path))
                return new List<TeamPreset>();

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<TeamPreset>>(json, Options)
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

    private static async Task<List<TeamPreset>> LoadFromFileAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new List<TeamPreset>();

            string json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<TeamPreset>>(json, Options)
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
            File.WriteAllText(path, json);
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
            await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
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

    public static List<TeamPreset> LoadAll() 
        => LoadFromFile(GetPresetsPath());

    public static Task<List<TeamPreset>> LoadAllAsync() 
        => LoadFromFileAsync(GetPresetsPath());

    public static void SaveAll(List<TeamPreset> presets) 
        => SaveToFile(GetPresetsPath(), presets);

    public static Task SaveAllAsync(List<TeamPreset> presets) 
        => SaveToFileAsync(GetPresetsPath(), presets);

    public static List<TeamPreset> LoadAllCricket() 
        => LoadFromFile(GetCricketPresetsPath());

    public static Task<List<TeamPreset>> LoadAllCricketAsync() 
        => LoadFromFileAsync(GetCricketPresetsPath());

    public static void SaveAllCricket(List<TeamPreset> presets) 
        => SaveToFile(GetCricketPresetsPath(), presets);

    public static Task SaveAllCricketAsync(List<TeamPreset> presets) 
        => SaveToFileAsync(GetCricketPresetsPath(), presets);
}

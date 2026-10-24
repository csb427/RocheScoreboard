// Generated from Services/MatchStorage.cs; edit translator/adapters.
// Source SHA-256: f76ed4e486798cba5d81a7da0138c83a5350b7012681303e22ab6b0c8d7c60fb
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

public static class MatchStorage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    private static string GetAutoSavePath()
    {
        string dir = Path.Combine(
            NativeStoragePaths.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Roche_Scoreboard"
        );
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "autosave.json");
    }

    public static void SaveAuto(SerializableState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        try
        {
            string path = GetAutoSavePath();
            string json = JsonSerializer.Serialize(state, Options);
            File.WriteAllText(path, json);
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to serialize match state: {ex.Message}");
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to write match state file: {ex.Message}");
        }
    }

    public static async Task SaveAutoAsync(SerializableState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        try
        {
            string path = GetAutoSavePath();
            string json = JsonSerializer.Serialize(state, Options);
            await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to serialize match state: {ex.Message}");
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to write match state file asynchronously: {ex.Message}");
        }
    }

    public static SerializableState? LoadAuto()
    {
        try
        {
            string path = GetAutoSavePath();
            if (!File.Exists(path))
                return null;

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SerializableState>(json, Options);
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to deserialize match state: {ex.Message}");
            return null;
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to read match state file: {ex.Message}");
            return null;
        }
    }

    public static async Task<SerializableState?> LoadAutoAsync()
    {
        try
        {
            string path = GetAutoSavePath();
            if (!File.Exists(path))
                return null;

            string json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            return JsonSerializer.Deserialize<SerializableState>(json, Options);
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to deserialize match state: {ex.Message}");
            return null;
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to read match state file asynchronously: {ex.Message}");
            return null;
        }
    }
}

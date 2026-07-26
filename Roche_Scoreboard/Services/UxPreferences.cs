using System;
using System.IO;
using System.Text.Json;

namespace Roche_Scoreboard.Services;

internal enum StartupExperience
{
    SportSelection = 0,
    LastMode = 1,
    ResumeIfAvailable = 2,
}

internal enum PersistedMode
{
    SportSelection = 0,
    Afl = 1,
    Cricket = 2,
    Training = 3,
    GlobalSettings = 4,
}

internal sealed class UxPreferencesData
{
    public StartupExperience StartupMode { get; set; } = StartupExperience.ResumeIfAvailable;
    public PersistedMode LastMode { get; set; } = PersistedMode.SportSelection;
    public string LastSettingsCategory { get; set; } = "teams";
    public int LastMainTabIndex { get; set; } = 0;
    public bool EnableQuickStartupHints { get; set; } = true;
}

internal static class UxPreferences
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string GetStoragePath()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Roche_Scoreboard");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "ux_preferences.json");
    }

    public static UxPreferencesData Load()
    {
        string path = GetStoragePath();
        if (!File.Exists(path))
            return new UxPreferencesData();

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UxPreferencesData>(json, JsonOptions) ?? new UxPreferencesData();
        }
        catch
        {
            return new UxPreferencesData();
        }
    }

    public static void Save(UxPreferencesData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            string json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(GetStoragePath(), json);
        }
        catch
        {
            // Best effort persistence only.
        }
    }
}

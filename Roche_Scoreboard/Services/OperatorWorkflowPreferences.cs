using System;
using System.IO;
using System.Text.Json;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

/// <summary>
/// Remembers repeatable operator choices that are not part of a match result.
/// Kept separate from UxPreferences so setup-wizard and club-browser updates
/// cannot be overwritten by a stale MainWindow preference snapshot.
/// </summary>
internal sealed class OperatorWorkflowPreferencesData
{
    public bool AflCountdown { get; set; } = true;
    public int AflQuarterMinutes { get; set; } = 20;
    public int AflQuarterSeconds { get; set; }
    public bool AflControlPanelCountdown { get; set; } = true;
    public string AflWeatherLocation { get; set; } = "Melbourne, Australia";
    public string? LastHomePresetKey { get; set; }
    public string? LastAwayPresetKey { get; set; }
    public TeamPreset? LastHomeTeam { get; set; }
    public TeamPreset? LastAwayTeam { get; set; }

    public string TeamLibraryLeague { get; set; } = "All leagues";
    public string TeamLibraryState { get; set; } = "All states";
    public string TeamLibrarySort { get; set; } = "A–Z";
    public bool TeamLibraryFavouritesOnly { get; set; }
}

internal static class OperatorWorkflowPreferences
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static OperatorWorkflowPreferencesData Load()
    {
        lock (Sync)
            return LoadCore();
    }

    public static void Save(OperatorWorkflowPreferencesData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        lock (Sync)
            SaveCore(Normalize(data));
    }

    public static void Update(Action<OperatorWorkflowPreferencesData> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (Sync)
        {
            OperatorWorkflowPreferencesData data = LoadCore();
            update(data);
            SaveCore(Normalize(data));
        }
    }

    private static OperatorWorkflowPreferencesData LoadCore()
    {
        try
        {
            string path = GetStoragePath(createDirectory: false);
            if (!File.Exists(path))
                return new OperatorWorkflowPreferencesData();

            string json = File.ReadAllText(path);
            return Normalize(JsonSerializer.Deserialize<OperatorWorkflowPreferencesData>(json, JsonOptions)
                ?? new OperatorWorkflowPreferencesData());
        }
        catch
        {
            return new OperatorWorkflowPreferencesData();
        }
    }

    private static void SaveCore(OperatorWorkflowPreferencesData data)
    {
        try
        {
            string path = GetStoragePath(createDirectory: true);
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(data, JsonOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            // Workflow memory is a convenience. It must never block scoring.
        }
    }

    private static OperatorWorkflowPreferencesData Normalize(OperatorWorkflowPreferencesData data)
    {
        data.AflQuarterMinutes = Math.Clamp(data.AflQuarterMinutes, 1, 60);
        data.AflQuarterSeconds = Math.Clamp(data.AflQuarterSeconds, 0, 59);
        data.AflWeatherLocation = string.IsNullOrWhiteSpace(data.AflWeatherLocation)
            ? "Melbourne, Australia"
            : data.AflWeatherLocation.Trim();
        data.TeamLibraryLeague = NormalizeChoice(data.TeamLibraryLeague,
            ["All leagues", "AFL", "VAFA", "APS", "AGSV"], "All leagues");
        data.TeamLibraryState = NormalizeChoice(data.TeamLibraryState,
            ["All states", "VIC", "NSW", "QLD", "SA", "WA"], "All states");
        data.TeamLibrarySort = NormalizeChoice(data.TeamLibrarySort,
            ["A–Z", "Favourites", "League", "State"], "A–Z");
        return data;
    }

    private static string NormalizeChoice(string? value, string[] choices, string fallback)
    {
        foreach (string choice in choices)
        {
            if (string.Equals(value, choice, StringComparison.OrdinalIgnoreCase))
                return choice;
        }
        return fallback;
    }

    private static string GetStoragePath(bool createDirectory)
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Roche_Scoreboard");
        if (createDirectory)
            Directory.CreateDirectory(directory);
        return Path.Combine(directory, "operator_workflow.json");
    }
}

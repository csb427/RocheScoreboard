// Generated from Services/OperatorWorkflowPreferences.cs; edit translator/adapters.
// Source SHA-256: 89990623d79cf798575a4d64baac75b3d20a5aad0900c1f8e83a7ff04d0da111
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
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
    public string AflGoalAnimation { get; set; } = "broadcast";
    public int GoalAnimationPreferenceVersion { get; set; }
    public string AflWeatherLocation { get; set; } = "Melbourne, Australia";
    public bool OutputScreensaverEnabled { get; set; } = true;
    public string? OutputScreensaverMediaPath { get; set; }
    public bool OutputScreensaverShowClock { get; set; } = true;
    public bool AflBreakScreenEnabled { get; set; } = true;
    public bool AflAutoOverlaysEnabled { get; set; } = true;
    public bool CricketEventAnimations { get; set; } = true;
    public bool CricketRollingFigures { get; set; } = true;
    public bool CricketBowlerDetails { get; set; } = true;
    public int OutputWindowWidth { get; set; } = 386;
    public int OutputWindowHeight { get; set; } = 193;
    public string? LastHomePresetKey { get; set; }
    public string? LastAwayPresetKey { get; set; }
    public TeamPreset? LastHomeTeam { get; set; }
    public TeamPreset? LastAwayTeam { get; set; }

    public string TeamLibraryLeague { get; set; } = "All leagues";
    public string TeamLibraryState { get; set; } = "All states";
    public string TeamLibrarySort { get; set; } = "A–Z";
    public bool TeamLibraryFavouritesOnly { get; set; }
    public string CricketTeamLibraryLeague { get; set; } = "All leagues";
    public string CricketTeamLibraryState { get; set; } = "All states";
    public string CricketTeamLibrarySort { get; set; } = "A–Z";
    public bool CricketTeamLibraryFavouritesOnly { get; set; }
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
            return Normalize(AtomicFile.TryReadJson<OperatorWorkflowPreferencesData>(path, JsonOptions)
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
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptions));
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
        // Versions before this marker could overwrite TV Signal with the
        // Scoreboard preset during setup/restore. Migrate that polluted value
        // once; subsequent explicit choices are preserved normally.
        if (data.GoalAnimationPreferenceVersion < 1)
        {
            if (string.Equals(data.AflGoalAnimation, "classic", StringComparison.OrdinalIgnoreCase))
                data.AflGoalAnimation = "broadcast";
            data.GoalAnimationPreferenceVersion = 1;
        }
        data.AflGoalAnimation = NormalizeChoice(data.AflGoalAnimation,
            ["broadcast", "electric", "cinematic", "clean", "classic", "customVideo"], "broadcast");
        if (!string.IsNullOrWhiteSpace(data.OutputScreensaverMediaPath))
            data.OutputScreensaverMediaPath = data.OutputScreensaverMediaPath.Trim();
        data.OutputWindowWidth = Math.Clamp(data.OutputWindowWidth, 50, 7680);
        data.OutputWindowHeight = Math.Clamp(data.OutputWindowHeight, 50, 4320);
        data.TeamLibraryLeague = NormalizeChoice(data.TeamLibraryLeague,
            ["All leagues", "AFL", "VAFA", "APS", "AGSV"], "All leagues");
        data.TeamLibraryState = NormalizeChoice(data.TeamLibraryState,
            ["All states", "VIC", "NSW", "QLD", "SA", "WA"], "All states");
        data.TeamLibrarySort = NormalizeChoice(data.TeamLibrarySort,
            ["A–Z", "Favourites", "League", "State"], "A–Z");
        data.CricketTeamLibraryLeague = NormalizeChoice(data.CricketTeamLibraryLeague,
            ["All leagues", "Victorian Premier", "VSDCA"], "All leagues");
        data.CricketTeamLibraryState = NormalizeChoice(data.CricketTeamLibraryState,
            ["All states", "VIC"], "All states");
        data.CricketTeamLibrarySort = NormalizeChoice(data.CricketTeamLibrarySort,
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
        string directory = Avalonia.Application.Current is RocheScoreboard.Mac.App { IsSelfTestMode: true }
            ? Path.Combine(Path.GetTempPath(), "RocheScoreboard-SelfTests", Environment.ProcessId.ToString(), "workflow")
            : Path.Combine(
            NativeStoragePaths.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Roche_Scoreboard");
        if (createDirectory)
            Directory.CreateDirectory(directory);
        return Path.Combine(directory, "operator_workflow.json");
    }
}

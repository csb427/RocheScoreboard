using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

/// <summary>
/// Persists in-progress match state to a JSON file so the app can restore
/// the game if closed mid-match.
/// </summary>
internal static class GameStateService
{
    private static string StateFilePath => SelfTestStorage.IsEnabled
        ? SelfTestStorage.PathFor("recovery", "game_state.json")
        : Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roche_Scoreboard",
        "game_state.json");
    private static string BackupFilePath => StateFilePath + ".bak";
    private static string LegacyStateFilePath => SelfTestStorage.IsEnabled
        ? SelfTestStorage.PathFor("legacy", "game_state.json")
        : Path.Combine(AppContext.BaseDirectory, "game_state.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static string RecoveryStatePath => StateFilePath;

    // Cricket's background writer shares exactly the established recovery format.
    // The state supplied here is detached from the live match before queuing.
    internal static string SerializeRecoverySnapshot(GameState state)
    {
        state.SavedAtUtc=DateTimeOffset.UtcNow;
        return JsonSerializer.Serialize(state,JsonOptions);
    }

    /// <summary>
    /// Saves the current match state and UI configuration to local history and
    /// recovery. Returns true when either durable copy succeeded.
    /// </summary>
    internal static bool Save(GameState state, string? recoveryPath = null, string? checkpointDirectory = null)
    {
        // Archive first. A later reset can then retire recovery without losing
        // the last operator-visible state. A successful recovery write is also
        // sufficient: Clear archives that exact file before deleting it.
        bool archived = MatchCheckpointStore.Archive(state, checkpointDirectory);
        bool recovered = SaveTo(state, recoveryPath ?? StateFilePath);
        return archived || recovered;
    }

    private static bool SaveTo(GameState state, string path)
    {
        string temporaryPath = path + ".tmp";
        try
        {
            state.SavedAtUtc = DateTimeOffset.UtcNow;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string json = JsonSerializer.Serialize(state, JsonOptions);
            File.WriteAllText(temporaryPath, json);

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temporaryPath, path, path + ".bak", ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(path, path + ".bak", overwrite: true);
                    File.Move(temporaryPath, path, overwrite: true);
                }
            }
            else
            {
                File.Move(temporaryPath, path);
            }
            return true;
        }
        catch
        {
            // Report failure to explicit replacement/reset flows. Ordinary
            // autosaves may still ignore this result to keep input responsive.
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch { }
        }
    }

    /// <summary>
    /// Loads saved game state from disk. Returns <see langword="null"/> if
    /// no state file exists or it cannot be deserialised.
    /// </summary>
    internal static GameState? Load() => LoadFrom(StateFilePath, LegacyStateFilePath);

    internal static GameState? LoadFrom(string path, string? legacyPath = null)
    {
        GameState? state = TryLoad(path);
        if (state is not null) return state;

        state = TryLoad(path + ".bak");
        if (state is not null)
        {
            // Repair a corrupt or interrupted primary from its last valid
            // recovery copy so every subsequent launch does not repeatedly
            // parse the same damaged file before falling back.
            RepairPrimary(state, path);
            return state;
        }

        // Migrate releases that stored the active match beside the executable.
        state = legacyPath is null ? null : TryLoad(legacyPath);
        if (state is not null)
        {
            RepairPrimary(state, path);
            return state;
        }

        return null;
    }

    private static void RepairPrimary(GameState state, string path)
    {
        string temporary = path + ".repair." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, JsonOptions));
            // A normal save backs up its previous primary. During recovery that
            // primary is corrupt: never overwrite the known-valid backup with it.
            File.Move(temporary, path, overwrite: true);
        }
        catch { /* Leave the valid backup/legacy source available for retry. */ }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
    }

    private static GameState? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var state = JsonSerializer.Deserialize<GameState>(File.ReadAllText(path), JsonOptions);
            return RecoverySession.Describe(state) is null ? null : state;
        }
        catch { return null; }
    }

    /// <summary>
    /// Deletes the saved state file (called on explicit new game).
    /// </summary>
    internal static bool Clear(string? recoveryPath = null, string? legacyPath = null, string? checkpointDirectory = null)
    {
        string statePath = recoveryPath ?? StateFilePath;
        string backupPath = statePath + ".bak";
        string legacy = legacyPath ?? LegacyStateFilePath;
        // A failed archive must never turn a reset into data loss. Leave the
        // current recovery and its backup in place so the operator can retry.
        if (!MatchCheckpointStore.ArchiveCurrentRecovery(statePath, legacy, checkpointDirectory)) return false;
        try
        {
            if (File.Exists(statePath)) File.Delete(statePath);
            if (File.Exists(backupPath)) File.Delete(backupPath);
            if (!string.Equals(legacy, statePath, StringComparison.OrdinalIgnoreCase)
                && File.Exists(legacy)) File.Delete(legacy);
            return !File.Exists(statePath) && !File.Exists(backupPath)
                && (string.Equals(legacy, statePath, StringComparison.OrdinalIgnoreCase) || !File.Exists(legacy));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Returns true if a saved game state file exists.</summary>
    internal static bool HasSavedState() =>
        File.Exists(StateFilePath) || File.Exists(BackupFilePath) || File.Exists(LegacyStateFilePath);
}

/// <summary>
/// Serialisable snapshot of all match and UI state needed to restore a game.
/// </summary>
internal sealed class GameState
{
    public string Sport { get; set; } = "afl";

    /// <summary>UTC timestamp of the latest recovery snapshot.</summary>
    public DateTimeOffset? SavedAtUtc { get; set; }

    /// <summary>The match manager's serialisable state (scores, events, clock, quarter).</summary>
    public SerializableState? MatchState { get; set; }

    /// <summary>The Cricket engine state when <see cref="Sport"/> is cricket.</summary>
    public CricketSerializableState? CricketMatchState { get; set; }

    // Colours
    public string HomePrimaryColor { get; set; } = "";
    public string HomeSecondaryColor { get; set; } = "";
    public string AwayPrimaryColor { get; set; } = "";
    public string AwaySecondaryColor { get; set; } = "";

    // Logos
    public string? HomeLogoPath { get; set; }
    public string? AwayLogoPath { get; set; }

    // Goal videos
    public string? HomeGoalVideoPath { get; set; }
    public string? AwayGoalVideoPath { get; set; }

    // Settings
    public List<string> Messages { get; set; } = [];
    public List<Roche_Scoreboard.Models.MarqueeMessage> TickerSlots { get; set; } = [];
    public bool FinalsMode { get; set; }
    public string? WeatherLocation { get; set; }
    public bool ShowControlPanelCountdown { get; set; }
    public int ControlPanelCountdownDurationSeconds { get; set; }
    public int ControlPanelCountdownRemainingSeconds { get; set; }

    // Logo crop
    public double HomeLogoZoom { get; set; } = 1.0;
    public double HomeLogoOffsetX { get; set; }
    public double HomeLogoOffsetY { get; set; }
    public double AwayLogoZoom { get; set; } = 1.0;
    public double AwayLogoOffsetX { get; set; }
    public double AwayLogoOffsetY { get; set; }
}

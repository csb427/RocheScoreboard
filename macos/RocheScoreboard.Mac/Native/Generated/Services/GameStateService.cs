// Generated from Services/GameStateService.cs; edit translator/adapters.
// Source SHA-256: e114464a8afe4db736f23482fc76f01638680ecaf700bd4ed465a0c38f8d98e4
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
        NativeStoragePaths.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
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
    /// Saves the current match state and UI configuration to disk.
    /// Failures are silently ignored (best-effort persistence).
    /// </summary>
    internal static void Save(GameState state) => SaveTo(state, StateFilePath);

    private static void SaveTo(GameState state, string path)
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
        }
        catch
        {
            // Best-effort — don't crash the app for persistence failures
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
    internal static void Clear()
    {
        try
        {
            if (File.Exists(StateFilePath))
                File.Delete(StateFilePath);
            if (File.Exists(BackupFilePath))
                File.Delete(BackupFilePath);
            if (!string.Equals(LegacyStateFilePath, StateFilePath, StringComparison.OrdinalIgnoreCase)
                && File.Exists(LegacyStateFilePath))
                File.Delete(LegacyStateFilePath);
        }
        catch
        {
            // Best-effort
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

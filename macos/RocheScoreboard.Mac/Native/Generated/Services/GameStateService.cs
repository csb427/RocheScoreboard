// Generated from Services/GameStateService.cs; edit translator/adapters.
// Source SHA-256: 5ce45882cabbe4a2b7df17fd601d4545e77497137a9e23afa5d9b22cf8dcf9df
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Controls;
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
    private static readonly string StateFilePath = Path.Combine(
        NativeStoragePaths.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roche_Scoreboard",
        "game_state.json");
    private static readonly string BackupFilePath = StateFilePath + ".bak";
    private static readonly string LegacyStateFilePath = Path.Combine(AppContext.BaseDirectory, "game_state.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Saves the current match state and UI configuration to disk.
    /// Failures are silently ignored (best-effort persistence).
    /// </summary>
    internal static void Save(GameState state)
    {
        string temporaryPath = StateFilePath + ".tmp";
        try
        {
            state.SavedAtUtc = DateTimeOffset.UtcNow;
            Directory.CreateDirectory(Path.GetDirectoryName(StateFilePath)!);
            string json = JsonSerializer.Serialize(state, JsonOptions);
            File.WriteAllText(temporaryPath, json);

            if (File.Exists(StateFilePath))
            {
                try
                {
                    File.Replace(temporaryPath, StateFilePath, BackupFilePath, ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(StateFilePath, BackupFilePath, overwrite: true);
                    File.Move(temporaryPath, StateFilePath, overwrite: true);
                }
            }
            else
            {
                File.Move(temporaryPath, StateFilePath);
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
    internal static GameState? Load()
    {
        GameState? state = TryLoad(StateFilePath);
        if (state is not null) return state;

        state = TryLoad(BackupFilePath);
        if (state is not null)
        {
            // Repair a corrupt or interrupted primary from its last valid
            // recovery copy so every subsequent launch does not repeatedly
            // parse the same damaged file before falling back.
            Save(state);
            return state;
        }

        // Migrate releases that stored the active match beside the executable.
        state = TryLoad(LegacyStateFilePath);
        if (state is not null)
        {
            Save(state);
            return state;
        }

        return null;
    }

    private static GameState? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<GameState>(File.ReadAllText(path), JsonOptions);
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

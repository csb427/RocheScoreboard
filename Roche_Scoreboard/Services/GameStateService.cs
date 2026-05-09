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
        AppContext.BaseDirectory, "game_state.json");

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
        try
        {
            string json = JsonSerializer.Serialize(state, JsonOptions);
            File.WriteAllText(StateFilePath, json);
        }
        catch
        {
            // Best-effort — don't crash the app for persistence failures
        }
    }

    /// <summary>
    /// Loads saved game state from disk. Returns <see langword="null"/> if
    /// no state file exists or it cannot be deserialised.
    /// </summary>
    internal static GameState? Load()
    {
        try
        {
            if (!File.Exists(StateFilePath))
                return null;

            string json = File.ReadAllText(StateFilePath);
            return JsonSerializer.Deserialize<GameState>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
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
        }
        catch
        {
            // Best-effort
        }
    }

    /// <summary>Returns true if a saved game state file exists.</summary>
    internal static bool HasSavedState() => File.Exists(StateFilePath);
}

/// <summary>
/// Serialisable snapshot of all match and UI state needed to restore a game.
/// </summary>
internal sealed class GameState
{
    /// <summary>The match manager's serialisable state (scores, events, clock, quarter).</summary>
    public SerializableState? MatchState { get; set; }

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
    public bool FinalsMode { get; set; }
    public string? WeatherLocation { get; set; }

    // Logo crop
    public double HomeLogoZoom { get; set; } = 1.0;
    public double HomeLogoOffsetX { get; set; }
    public double HomeLogoOffsetY { get; set; }
    public double AwayLogoZoom { get; set; } = 1.0;
    public double AwayLogoOffsetX { get; set; }
    public double AwayLogoOffsetY { get; set; }
}

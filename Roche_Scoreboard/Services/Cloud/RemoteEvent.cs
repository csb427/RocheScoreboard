using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Roche_Scoreboard.Services.Cloud;

/// <summary>
/// Envelope used to ferry a remote command over Supabase. Matches the
/// contract documented in <c>docs/cloud-remote/EVENT_CATALOG.md</c>.
/// </summary>
public sealed record RemoteEvent
{
    /// <summary>Client-generated UUID v4 — used for idempotency.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Owning game session.</summary>
    public Guid SessionId { get; init; }

    /// <summary>
    /// Lower-snake-case command name. See the constants on
    /// <see cref="RemoteEventTypes"/>.
    /// </summary>
    public string EventType { get; init; } = "";

    /// <summary>Free-form JSON payload. <c>{}</c> for no-arg commands.</summary>
    public JsonElement Payload { get; init; } = JsonDocument.Parse("{}").RootElement;

    /// <summary>
    /// Optional opaque per-device identifier; used only for operator
    /// diagnostics ("Scorekeeper iPhone").
    /// </summary>
    public Guid? ClientId { get; init; }

    /// <summary>Human-readable label of the source device.</summary>
    public string? ClientLabel { get; init; }

    /// <summary>Server-assigned timestamp when persisted to Supabase.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// String constants for every supported remote command. Keeping them in one
/// place lets the dispatcher (WPF) and the web app (TypeScript) stay in lock
/// step with <c>docs/cloud-remote/EVENT_CATALOG.md</c>.
/// </summary>
public static class RemoteEventTypes
{
    // Scoring
    public const string HomeGoal     = "home_goal";
    public const string HomeBehind   = "home_behind";
    public const string AwayGoal     = "away_goal";
    public const string AwayBehind   = "away_behind";
    public const string Undo         = "undo";

    // Clock
    public const string ClockToggle  = "clock_toggle";
    public const string ClockSet     = "clock_set";
    public const string ClockModeSet = "clock_mode_set";
    public const string Siren        = "siren";

    // Quarter
    public const string EndQuarter        = "end_quarter";
    public const string ContinueQuarter   = "continue_quarter";
    public const string ResetQuarterLabel = "reset_quarter_label";

    // Match
    public const string MatchReset        = "match_reset";
    public const string SetTeams          = "set_teams";
    public const string SetColors         = "set_colors";
    public const string SetFinalsMode     = "set_finals_mode";
    public const string SetWeatherLocation = "set_weather_location";

    // Presentation
    public const string SwitchScreen       = "switch_screen";
    public const string ShowScoreboardWindow = "show_scoreboard_window";

    // Marquee
    public const string MessageAdd     = "message_add";
    public const string MessageRemove  = "message_remove";
    public const string MessagesSet    = "messages_set";

    // Layout / animation
    public const string SetLayout        = "set_layout";
    public const string SetGoalAnimation = "set_goal_animation";

    // Lifecycle
    public const string SessionStarted = "session_started";
    public const string SessionEnded   = "session_ended";
    public const string Heartbeat      = "heartbeat";
}

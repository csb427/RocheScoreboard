using System;
using System.Collections.Generic;

namespace Roche_Scoreboard.Services.Cloud;

/// <summary>
/// JSON shape persisted to <c>game_sessions.live_state</c>. Mirrors the
/// fields documented in <c>docs/cloud-remote/EVENT_CATALOG.md §1</c>.
///
/// Records are immutable; the host produces a new snapshot on every
/// <c>MatchChanged</c> event and writes it through the service-role key.
/// </summary>
public sealed record LiveStateSnapshot
{
    /// <summary>
    /// Which phone app the mobile shell should boot — <c>"afl"</c> or
    /// <c>"training"</c>. Defaults to AFL for backwards compatibility.
    /// </summary>
    public string Sport { get; init; } = "afl";

    // Teams
    public string HomeName { get; init; } = "";
    public string HomeAbbr { get; init; } = "";
    public string AwayName { get; init; } = "";
    public string AwayAbbr { get; init; } = "";

    // Scores
    public int HomeGoals   { get; init; }
    public int HomeBehinds { get; init; }
    public int HomeTotal   { get; init; }
    public int AwayGoals   { get; init; }
    public int AwayBehinds { get; init; }
    public int AwayTotal   { get; init; }
    public int Margin      { get; init; }

    // Quarter / clock
    public int    Quarter         { get; init; } = 1;
    public string Clock           { get; init; } = "00:00";
    public bool   ClockRunning    { get; init; }
    public string ClockMode       { get; init; } = "CountUp";
    public int    QuarterMinutes  { get; init; } = 20;
    public int    QuarterSeconds  { get; init; }
    public bool   Q4Ended         { get; init; }

    // Colours
    public string HomePrimaryColor   { get; init; } = "#000000";
    public string HomeSecondaryColor { get; init; } = "#FFFFFF";
    public string AwayPrimaryColor   { get; init; } = "#FFFFFF";
    public string AwaySecondaryColor { get; init; } = "#000000";

    // Display
    public string ActiveScreen { get; init; } = "scorebug";
    public string Layout       { get; init; } = "classic";
    public string GoalAnimation { get; init; } = "broadcast";

    // Context
    public int    EventCount { get; init; }
    public string LastEvent  { get; init; } = "";
    public string MatchResult { get; init; } = "";
    public bool   FinalsMode { get; init; }
    public string WeatherLocation { get; init; } = "";

    // Marquee
    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();

    // Quarter snapshots (cumulative end-of-quarter scores)
    public IReadOnlyList<QuarterSnapshotDto> QuarterSnapshots { get; init; } =
        Array.Empty<QuarterSnapshotDto>();

    // Session
    public string SessionStatus { get; init; } = "lobby";
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional training payload. Present only while the host is in
    /// Training mode. <c>null</c> for AFL sessions.
    /// </summary>
    public TrainingSnapshotDto? Training { get; init; }
}

public sealed record QuarterSnapshotDto
{
    public int Q  { get; init; }
    public int Hg { get; init; }
    public int Hb { get; init; }
    public int Ht { get; init; }
    public int Ag { get; init; }
    public int Ab { get; init; }
    public int At { get; init; }
}

/// <summary>
/// Snapshot of a <see cref="Roche_Scoreboard.Models.TrainingSession"/> in
/// a form the phone client can render directly.
/// </summary>
public sealed record TrainingSnapshotDto
{
    public bool   IsRunning              { get; init; }
    public bool   AutoAdvance            { get; init; } = true;
    public int    ActiveIndex            { get; init; } = -1;
    public string ActiveTitle            { get; init; } = "";
    public int    ActiveTotalSeconds     { get; init; }
    public int    ActiveRemainingSeconds { get; init; }
    public int    SessionTotalSeconds    { get; init; }
    public int    SessionRemainingSeconds { get; init; }

    public IReadOnlyList<TrainingTimerDto> Timers { get; init; } =
        Array.Empty<TrainingTimerDto>();
}

public sealed record TrainingTimerDto
{
    public string Id               { get; init; } = "";
    public string Title            { get; init; } = "";
    public int    TotalSeconds     { get; init; }
    public int    RemainingSeconds { get; init; }
    public bool   HasFinished      { get; init; }
}

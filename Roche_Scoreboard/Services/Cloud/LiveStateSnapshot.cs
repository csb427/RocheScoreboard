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

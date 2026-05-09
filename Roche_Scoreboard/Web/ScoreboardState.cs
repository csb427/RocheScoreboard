namespace Roche_Scoreboard.Web;

/// <summary>
/// Immutable snapshot of the current match state sent to all web clients via SignalR.
/// </summary>
public sealed record ScoreboardState
{
    // Teams
    public string HomeName { get; init; } = "";
    public string HomeAbbr { get; init; } = "";
    public string AwayName { get; init; } = "";
    public string AwayAbbr { get; init; } = "";

    // Scores
    public int HomeGoals { get; init; }
    public int HomeBehinds { get; init; }
    public int HomeTotal { get; init; }
    public int AwayGoals { get; init; }
    public int AwayBehinds { get; init; }
    public int AwayTotal { get; init; }
    public int Margin { get; init; }

    // Quarter & clock
    public int Quarter { get; init; } = 1;
    public string Clock { get; init; } = "00:00";
    public bool ClockRunning { get; init; }
    public string ClockMode { get; init; } = "CountUp";
    public bool Q4Ended { get; init; }

    // Team colours (hex)
    public string HomePrimaryColor { get; init; } = "#000000";
    public string HomeSecondaryColor { get; init; } = "#FFFFFF";
    public string AwayPrimaryColor { get; init; } = "#FFFFFF";
    public string AwaySecondaryColor { get; init; } = "#000000";

    // Display state
    public string ActiveScreen { get; init; } = "scorebug";

    // Match context — helps web control panel show relevant status
    public int EventCount { get; init; }
    public string LastEvent { get; init; } = "";
    public string MatchResult { get; init; } = "";
}

namespace Roche_Scoreboard.Models;

/// <summary>
/// Determines how team names are rendered in the scorebug.
/// The same mode is always applied to both teams simultaneously
/// to guarantee visual balance.
/// </summary>
public enum TeamNameDisplayMode
{
    /// <summary>Both names fit comfortably on a single line.</summary>
    SingleLine,

    /// <summary>At least one name is too long; both teams use a two-line layout.</summary>
    TwoLine,

    /// <summary>Names are too long even for two lines; both teams show their abbreviation.</summary>
    Abbreviation
}

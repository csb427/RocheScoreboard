namespace Roche_Scoreboard.Models;

/// <summary>The existing live ticker's slot and text limits, shared with match setup.</summary>
public static class TickerMessagePolicy
{
    public const int SlotCount = 8;
    public const int MaxTextLength = 140;

    // Keep ordinary/trailing spaces: live editing must not move the caret.
    public static string Flatten(string? value) => (value ?? string.Empty)
        .Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
}

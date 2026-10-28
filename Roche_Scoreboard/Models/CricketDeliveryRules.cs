namespace Roche_Scoreboard.Models;

public sealed partial class CricketMatchManager
{
    public bool ReplaceWideBalls { get; set; } = true;
    public bool ReplaceNoBalls { get; set; } = true;
    public bool FreeHitsEnabled { get; set; }
    public bool FreeHitPending => !IsMatchFinalized && FreeHitsEnabled && CurrentInnings is { IsComplete: false, FreeHitPending: true };

    public static bool IsDismissalAllowedOnFreeHit(DismissalType dismissal)
        => dismissal is DismissalType.RunOut or DismissalType.ObstructingField or DismissalType.HitBallTwice;

    /// <summary>MCC 2026 Laws 21.17 and 22.9. Extra-ball counting is a separate competition setting.</summary>
    public static bool IsDismissalAllowedForDelivery(DismissalType dismissal, CricketWicketRuns source, bool freeHit)
    {
        if (!Enum.IsDefined(dismissal) || !Enum.IsDefined(source)
            || dismissal is DismissalType.NotOut or DismissalType.RetiredHurt or DismissalType.RetiredOut)
            return false;
        if (freeHit && !IsDismissalAllowedOnFreeHit(dismissal)) return false;
        return source switch
        {
            CricketWicketRuns.NoBall => IsDismissalAllowedOnFreeHit(dismissal),
            CricketWicketRuns.Wide => dismissal is DismissalType.HitWicket or DismissalType.ObstructingField
                or DismissalType.RunOut or DismissalType.Stumped,
            CricketWicketRuns.Byes or CricketWicketRuns.LegByes => dismissal is DismissalType.RunOut or DismissalType.ObstructingField,
            _ => true
        };
    }

    private bool CountsInOver(CricketDeliveryType source) => source switch
    {
        CricketDeliveryType.Wide => !ReplaceWideBalls,
        CricketDeliveryType.NoBall => !ReplaceNoBalls,
        _ => true
    };
}

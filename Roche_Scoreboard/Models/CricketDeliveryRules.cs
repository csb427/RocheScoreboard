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

    /// <summary>MCC 2026 18.8: ordinary completed runs survive only run out/obstruction.
    /// Obstruction preventing a catch and runner cases require the umpire-confirmed allowance.</summary>
    public static bool DismissalAllowsCompletedRuns(DismissalType dismissal)
        => dismissal is DismissalType.RunOut or DismissalType.ObstructingField;

    private static string? WicketScoreProblem(DismissalType dismissal,CricketWicketRuns source,int runs,bool crossed)
    {
        if (DismissalAllowsCompletedRuns(dismissal)) return null;
        int penalty=source is CricketWicketRuns.Wide or CricketWicketRuns.NoBall ? 1 : 0;
        if (runs>penalty)
            return "Completed runs do not count for this dismissal. Use zero runs (or the one automatic wide/no-ball penalty). Record any applicable five-run penalty separately.";
        if (crossed) return "Crossing applies only to run out or obstructing the field. Review the dismissal and runs before applying.";
        return null;
    }

    private bool CountsInOver(CricketDeliveryType source) => source switch
    {
        CricketDeliveryType.Wide => !ReplaceWideBalls,
        CricketDeliveryType.NoBall => !ReplaceNoBalls,
        _ => true
    };
}

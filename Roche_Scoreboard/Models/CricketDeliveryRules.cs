namespace Roche_Scoreboard.Models;

public sealed partial class CricketMatchManager
{
    public bool ReplaceWideBalls { get; set; } = true;
    public bool ReplaceNoBalls { get; set; } = true;
    public bool FreeHitsEnabled { get; set; }
    public bool FreeHitPending => FreeHitsEnabled && CurrentInnings is { IsComplete: false, FreeHitPending: true };

    public static bool IsDismissalAllowedOnFreeHit(DismissalType dismissal)
        => dismissal is DismissalType.RunOut or DismissalType.ObstructingField or DismissalType.HitBallTwice;

    private bool CountsInOver(CricketDeliveryType source) => source switch
    {
        CricketDeliveryType.Wide => !ReplaceWideBalls,
        CricketDeliveryType.NoBall => !ReplaceNoBalls,
        _ => true
    };
}

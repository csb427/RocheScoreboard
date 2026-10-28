#if DEBUG
using Roche_Scoreboard.Models;
namespace Roche_Scoreboard.Views;

public partial class CricketScorebugControl
{
    internal bool PreviewBowlerDetails() => ShowBowlerDetails();
    internal void PreviewNextContext() { _infoRotationIndex++; if (_lastMatch is not null) UpdateInfoTile(_lastMatch); }
    internal void PreviewMoment(CricketDeliveryType moment) => BroadcastDelivery(new CricketDelivery { Type = moment, Runs = moment == CricketDeliveryType.Six ? 6 : moment == CricketDeliveryType.Four ? 4 : 0 });
    internal void StopPreviewMoment() => CancelBroadcast(true);
    internal void PreviewMilestone(int runs)
    {
        if(runs is not (50 or 100) || _broadcastMatch?.CurrentInnings is not {} innings || innings.StrikerIndex<0)return;
        var players=innings.BattingOrder.Select(player=>player.Clone()).ToList();
        players[innings.StrikerIndex].Runs=runs-1;players[innings.StrikerIndex].BallsFaced=runs==50?35:68;
        BroadcastDelivery(new CricketDelivery{Type=CricketDeliveryType.Runs,Runs=1,StrikerIndex=innings.StrikerIndex,
            StateBefore=new CricketInningsState{BattingOrder=players}});
    }
}
#endif

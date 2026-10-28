namespace Roche_Scoreboard.Models;

internal sealed record CricketMilestone(int Runs,string Player,int Balls)
{
    internal static CricketMilestone? FromDelivery(CricketDelivery delivery,CricketInnings? innings)
    {
        if(innings is null || delivery.StrikerIndex<0 || delivery.StrikerIndex>=innings.BattingOrder.Count)return null;
        int credited=delivery.ScoringType switch
        {
            CricketDeliveryType.Runs or CricketDeliveryType.Four or CricketDeliveryType.Six=>delivery.Runs,
            CricketDeliveryType.NoBall when delivery.NoBallRuns==CricketNoBallRuns.Bat=>Math.Max(0,delivery.Runs-1),
            _=>0
        };
        if(credited<=0)return null;
        var player=innings.BattingOrder[delivery.StrikerIndex];
        var before=delivery.StateBefore?.BattingOrder.ElementAtOrDefault(delivery.StrikerIndex);
        int previous=before?.Runs??Math.Max(0,player.Runs-credited),after=previous+credited;
        int milestone=previous<100 && after>=100?100:previous<50 && after>=50?50:0;
        if(milestone==0)return null;
        int faced=before is not null?before.BallsFaced+(delivery.ScoringType==CricketDeliveryType.Wide?0:1):player.BallsFaced;
        return new(milestone,player.FullName,faced);
    }
}

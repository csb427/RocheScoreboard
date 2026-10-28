namespace Roche_Scoreboard.Models;

/// <summary>Presentation order never mutates the indexed scorekeeping roster.</summary>
internal static class CricketPresentationOrder
{
    internal static List<(int Index,CricketPlayer Player)> Batters(CricketInnings innings)
    {
        var history=new List<int>();
        void Seen(int index){if(index>=0 && index<innings.BattingOrder.Count && !history.Contains(index))history.Add(index);}
        foreach(var ball in innings.Deliveries)
        {
            var pair=new[]{ball.StateBefore?.StrikerIndex??ball.StrikerIndex,ball.StateBefore?.NonStrikerIndex??ball.NonStrikerIndex};
            foreach(int actor in pair.Where(index=>index>=0 && index<innings.BattingOrder.Count)
                .OrderBy(index=>innings.BattingOrder[index].BattingEntryOrder>0?innings.BattingOrder[index].BattingEntryOrder:int.MaxValue))Seen(actor);
            Seen(ball.DismissedBatterIndex);
        }
        foreach(int actor in new[]{innings.StrikerIndex,innings.NonStrikerIndex}.Where(index=>index>=0 && index<innings.BattingOrder.Count)
            .OrderBy(index=>innings.BattingOrder[index].BattingEntryOrder>0?innings.BattingOrder[index].BattingEntryOrder:int.MaxValue))Seen(actor);
        // Retirements can occur before a player faces a ball. Retain that
        // appearance between the surrounding recorded entry positions.
        foreach(int index in Enumerable.Range(0,innings.BattingOrder.Count).Where(index=>!history.Contains(index)
            && (innings.BattingOrder[index].IsOut || innings.BattingOrder[index].IsRetired || innings.BattingOrder[index].Runs>0 || innings.BattingOrder[index].BallsFaced>0))
            .OrderBy(index=>innings.BattingOrder[index].BattingEntryOrder>0?innings.BattingOrder[index].BattingEntryOrder:int.MaxValue))
        {
            int entry=innings.BattingOrder[index].BattingEntryOrder;
            int before=entry>0?history.FindIndex(other=>innings.BattingOrder[other].BattingEntryOrder>entry):-1;
            if(before<0)history.Add(index);else history.Insert(before,index);
        }
        foreach(int index in Enumerable.Range(0,innings.BattingOrder.Count).OrderBy(index=>innings.BattingOrder[index].BattingPosition>0?innings.BattingOrder[index].BattingPosition:index+1))Seen(index);
        return history.Select(index=>(Index:index,Player:innings.BattingOrder[index])).ToList();
    }

    internal static List<(int Index,CricketPlayer Player)> Bowlers(CricketInnings innings)
    {
        var first=new Dictionary<int,int>();
        for(int ball=0;ball<innings.Deliveries.Count;ball++)
        {
            var delivery=innings.Deliveries[ball];int index=delivery.BowlerIndex;
            if(index<0)index=innings.BowlingAttack.FindIndex(player=>player.DisplayName==delivery.BowlerName);
            if(index>=0)first.TryAdd(index,ball);
        }
        return innings.BowlingAttack.Select((player,index)=>(Index:index,Player:player))
            .Where(item=>first.ContainsKey(item.Index) || item.Player.BallsBowled>0 || item.Player.NoBalls>0 || item.Player.Wides>0 || item.Player.RunsConceded>0 || item.Player.Wickets>0)
            .OrderBy(item=>first.GetValueOrDefault(item.Index,int.MaxValue))
            .ThenBy(item=>item.Player.BowlingEntryOrder>0 ? item.Player.BowlingEntryOrder : int.MaxValue).ThenBy(item=>item.Index).ToList();
    }
}

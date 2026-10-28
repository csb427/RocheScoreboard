namespace Roche_Scoreboard.Models;

internal sealed record CricketProgressPoint(int LegalBalls,int Runs,int Wickets,bool BreakBefore=false);
internal sealed record CricketOverPoint(int Over,int Runs,int Wickets);
internal sealed record CricketChartSeries(string Name,string Abbreviation,string Colour,string Secondary,int Innings,
    IReadOnlyList<CricketProgressPoint> Progress,IReadOnlyList<CricketOverPoint> Overs)
{
    internal int TeamInnings { get; init; }
    internal bool HistoryIncomplete { get; init; }
    internal int TotalRuns { get; init; }
    internal int LegalBalls { get; init; }
}
internal static class CricketChartData
{
    internal static IReadOnlyList<CricketChartSeries> Build(CricketMatchManager match)
    {
        if(match.CurrentInnings is not {} current)return [];
        return match.AllInnings.Where(innings=>innings.InningsNumber<=current.InningsNumber)
            .OrderBy(innings=>innings.BattingTeamName==match.TeamAName?0:1).ThenBy(innings=>innings.InningsNumber)
            .Select(innings=>BuildSeries(match,innings)).ToArray();
    }
    private static CricketChartSeries BuildSeries(CricketMatchManager match,CricketInnings innings)
    {
        int legal=0,index=0;var points=new List<CricketProgressPoint>{new(0,0,0)};
        foreach(var ball in innings.Deliveries)
        {
            foreach(var action in innings.ScorecardChanges.Where(change=>change.AfterDeliveryCount==index && change.NonDeliveryWicket is not null))
                points.Add(new(action.Before.LegalBallsBowled,action.Before.TotalRuns,1));
            if(ball.IsCountedBall)legal++;
            bool adjusted=innings.ScorecardChanges.Any(change=>change.AfterDeliveryCount==index && change.NonDeliveryWicket is null);
            points.Add(new(legal,Math.Max(0,ball.TotalAfter),ball.Type==CricketDeliveryType.Wicket?1:0,adjusted));index++;
        }
        foreach(var action in innings.ScorecardChanges.Where(change=>change.AfterDeliveryCount==index && change.NonDeliveryWicket is not null))
            points.Add(new(action.Before.LegalBallsBowled,action.Before.TotalRuns,1));
        bool incomplete=points[^1].Runs!=innings.TotalRuns || legal!=innings.LegalBallsBowled || innings.ScorecardChanges.Any(change=>change.NonDeliveryWicket is null);
        if(points[^1].Runs!=innings.TotalRuns || legal!=innings.LegalBallsBowled)
            points.Add(new(Math.Max(0,innings.LegalBallsBowled),Math.Max(0,innings.TotalRuns),0,true));
        var overs=innings.Deliveries.GroupBy(ball=>Math.Max(0,ball.OverNumber)).OrderBy(group=>group.Key)
            .Select(group=>new CricketOverPoint(group.Key+1,group.Sum(ball=>Math.Max(0,ball.Runs)),group.Count(ball=>ball.Type==CricketDeliveryType.Wicket))).ToArray();
        bool home=innings.BattingTeamName==match.TeamAName;
        return new(innings.BattingTeamName,home?match.TeamAAbbr:match.TeamBAbbr,home?match.TeamAPrimaryColor:match.TeamBPrimaryColor,
            home?match.TeamASecondaryColor:match.TeamBSecondaryColor,innings.InningsNumber,points,overs)
        {
            TeamInnings=match.AllInnings.Count(previous=>previous.InningsNumber<=innings.InningsNumber && previous.BattingTeamName==innings.BattingTeamName),
            HistoryIncomplete=incomplete,TotalRuns=innings.TotalRuns,LegalBalls=innings.LegalBallsBowled
        };
    }
}

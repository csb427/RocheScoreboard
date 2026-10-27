namespace Roche_Scoreboard.Models;

internal sealed record CricketProgressPoint(int LegalBalls,int Runs,int Wickets);
internal sealed record CricketOverPoint(int Over,int Runs,int Wickets);
internal sealed record CricketChartSeries(string Name,string Abbreviation,string Colour,string Secondary,int Innings,
    IReadOnlyList<CricketProgressPoint> Progress,IReadOnlyList<CricketOverPoint> Overs);
internal static class CricketChartData
{
    internal static IReadOnlyList<CricketChartSeries> Build(CricketMatchManager match)
    {
        if(match.CurrentInnings is not {} current)return [];
        var other=match.AllInnings.LastOrDefault(innings=>innings.InningsNumber<=current.InningsNumber && innings.BattingTeamName!=current.BattingTeamName);
        return new[]{other,current}.OfType<CricketInnings>().OrderBy(innings=>innings.BattingTeamName==match.TeamAName?0:1)
            .Select(innings=>BuildSeries(match,innings)).ToArray();
    }
    private static CricketChartSeries BuildSeries(CricketMatchManager match,CricketInnings innings)
    {
        int legal=0;var points=new List<CricketProgressPoint>{new(0,0,0)};
        foreach(var ball in innings.Deliveries)
        {
            if(ball.IsCountedBall)legal++;
            points.Add(new(legal,Math.Max(0,ball.TotalAfter),ball.Type==CricketDeliveryType.Wicket?1:0));
        }
        if(points.Count>1 && (points[^1].Runs!=innings.TotalRuns || legal!=innings.LegalBallsBowled))
            points.Add(new(Math.Max(legal,innings.LegalBallsBowled),Math.Max(0,innings.TotalRuns),0));
        var overs=innings.Deliveries.GroupBy(ball=>Math.Max(0,ball.OverNumber)).OrderBy(group=>group.Key)
            .Select(group=>new CricketOverPoint(group.Key+1,group.Sum(ball=>Math.Max(0,ball.Runs)),group.Count(ball=>ball.Type==CricketDeliveryType.Wicket))).ToArray();
        bool home=innings.BattingTeamName==match.TeamAName;
        return new(innings.BattingTeamName,home?match.TeamAAbbr:match.TeamBAbbr,home?match.TeamAPrimaryColor:match.TeamBPrimaryColor,
            home?match.TeamASecondaryColor:match.TeamBSecondaryColor,innings.InningsNumber,points,overs);
    }
}

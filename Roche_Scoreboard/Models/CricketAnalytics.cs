using System;
using System.Collections.Generic;
using System.Linq;

namespace Roche_Scoreboard.Models;

public sealed record CricketOverSummary(
    int Over,
    int Runs,
    int Wickets,
    int Dots,
    int Boundaries,
    string Sequence);

public sealed record CricketAnalyticsSnapshot
{
    public string Phase { get; init; } = "OPENING PHASE";
    public int ProjectedScore { get; init; }
    public int ConservativeProjection { get; init; }
    public int AttackingProjection { get; init; }
    public double RecentRunRate { get; init; }
    public double DotBallPercentage { get; init; }
    public int BoundaryDroughtBalls { get; init; }
    public int WicketsInHand { get; init; }
    public string ChaseEquation { get; init; } = "SETTING A TARGET";
    public string MomentumHeadline { get; init; } = "INNINGS SETTLING";
    public string MomentumDetail { get; init; } = "More deliveries are needed for a reliable trend.";
    public IReadOnlyList<CricketOverSummary> RecentOvers { get; init; } = [];
    public IReadOnlyList<string> FallOfWickets { get; init; } = [];
}

public static class CricketAnalyticsEngine
{
    public static CricketAnalyticsSnapshot Create(CricketMatchManager match)
    {
        ArgumentNullException.ThrowIfNull(match);
        CricketInnings? innings = match.CurrentInnings;
        if (innings is null) return new CricketAnalyticsSnapshot();

        List<CricketDelivery> legal = innings.Deliveries
            .Where(IsLegal)
            .ToList();
        List<CricketDelivery> recentLegal = legal.TakeLast(30).ToList();
        int recentStartIndex = recentLegal.Count == 0
            ? innings.Deliveries.Count
            : innings.Deliveries.IndexOf(recentLegal[0]);
        List<CricketDelivery> recentWindow = innings.Deliveries
            .Skip(Math.Max(0, recentStartIndex))
            .ToList();
        int recentRuns = recentWindow.Sum(d => d.Runs);
        double recentRate = recentLegal.Count > 0
            ? Math.Round(recentRuns * 6.0 / recentLegal.Count, 2)
            : innings.RunRate;

        int ballsRemaining = match.BallsRemaining ?? 0;
        int projection = innings.TotalRuns + (int)Math.Round(innings.RunRate * ballsRemaining / 6.0);
        int conservative = innings.TotalRuns + (int)Math.Round(Math.Min(innings.RunRate, recentRate) * ballsRemaining / 6.0);
        int attacking = innings.TotalRuns + (int)Math.Round(Math.Max(innings.RunRate, recentRate) * ballsRemaining / 6.0);
        if (match.Format != CricketFormat.LimitedOvers)
            projection = conservative = attacking = innings.TotalRuns;

        int wicketsInHand = Math.Max(0, innings.BattingOrder.Count - 1 - innings.TotalWickets);
        string chase = match.RunsRequired is int required
            ? required == 0
                ? "TARGET REACHED"
                : $"{required} RUNS FROM {match.BallsRemaining ?? 0} BALLS"
            : "SETTING A TARGET";

        string phase = GetPhase(match, innings);
        (string headline, string detail) = GetMomentum(match, innings, recentRate, wicketsInHand);

        return new CricketAnalyticsSnapshot
        {
            Phase = phase,
            ProjectedScore = projection,
            ConservativeProjection = conservative,
            AttackingProjection = attacking,
            RecentRunRate = recentRate,
            DotBallPercentage = innings.DotBallPercentage,
            BoundaryDroughtBalls = innings.BoundaryDroughtBalls,
            WicketsInHand = wicketsInHand,
            ChaseEquation = chase,
            MomentumHeadline = headline,
            MomentumDetail = detail,
            RecentOvers = BuildRecentOvers(innings),
            FallOfWickets = innings.ManualFallOfWickets is { } manual ? [.. manual] : innings.Deliveries
                .Where(d => d.Type == CricketDeliveryType.Wicket)
                .Select(d => $"{d.WicketsAfter}-{d.TotalAfter}  {d.BatsmanName}")
                .ToList()
        };
    }

    private static string GetPhase(CricketMatchManager match, CricketInnings innings)
    {
        if (match.Format != CricketFormat.LimitedOvers) return "BUILDING THE INNINGS";
        double progress = match.CurrentOverLimit <= 0 ? 0 : innings.CompletedOvers / (double)match.CurrentOverLimit;
        if (progress < 0.30) return match.IsInPowerplay ? "POWERPLAY" : "OPENING PHASE";
        if (progress < 0.75) return "MIDDLE OVERS";
        return "DEATH OVERS";
    }

    private static (string Headline, string Detail) GetMomentum(
        CricketMatchManager match,
        CricketInnings innings,
        double recentRate,
        int wicketsInHand)
    {
        if (innings.Deliveries.Count < 12)
            return ("INNINGS SETTLING", "More deliveries are needed for a reliable trend.");
        if (match.RequiredRunRate is double required)
        {
            double delta = recentRate - required;
            if (delta >= 1.5) return ("CHASE AHEAD OF THE RATE", $"Recent rate {recentRate:F2}; required {required:F2}.");
            if (delta <= -1.5) return ("PRESSURE BUILDING", $"Recent rate {recentRate:F2}; required {required:F2}.");
            return ("CHASE IN THE BALANCE", $"Recent rate {recentRate:F2}; required {required:F2}.");
        }
        if (innings.BoundaryDroughtBalls >= 18)
            return ("BOUNDARY PRESSURE", $"No boundary for {innings.BoundaryDroughtBalls} legal balls.");
        if (recentRate >= innings.RunRate + 1.5)
            return ("SCORING RATE RISING", $"Last five-over pace is {recentRate:F2} runs per over.");
        if (wicketsInHand <= 3)
            return ("WICKETS ARE THE STORY", $"Only {wicketsInHand} wicket{(wicketsInHand == 1 ? "" : "s")} in hand.");
        return ("CONTROLLED INNINGS", $"Current rate {innings.RunRate:F2}; recent rate {recentRate:F2}.");
    }

    private static IReadOnlyList<CricketOverSummary> BuildRecentOvers(CricketInnings innings)
    {
        return innings.Deliveries
            .GroupBy(d => d.OverNumber)
            .OrderBy(g => g.Key)
            .TakeLast(5)
            .Select(g => new CricketOverSummary(
                g.Key + 1,
                g.Sum(d => d.Runs),
                g.Count(d => d.Type == CricketDeliveryType.Wicket),
                g.Count(d => d.Type == CricketDeliveryType.Dot),
                g.Count(d => d.Type is CricketDeliveryType.Four or CricketDeliveryType.Six),
                string.Join(" ", g.Select(BallToken))))
            .ToList();
    }

    private static bool IsLegal(CricketDelivery delivery) => delivery.IsCountedBall;

    private static string BallToken(CricketDelivery delivery) => delivery.Type switch
    {
        CricketDeliveryType.Dot => "•",
        CricketDeliveryType.Wicket => "W",
        CricketDeliveryType.Four => "4",
        CricketDeliveryType.Six => "6",
        CricketDeliveryType.Wide => $"{delivery.Runs}wd",
        CricketDeliveryType.NoBall => delivery.NoBallRuns == CricketNoBallRuns.Bat || delivery.Runs == 1 ? $"{delivery.Runs}nb" : $"nb+{delivery.Runs - 1}{(delivery.NoBallRuns == CricketNoBallRuns.Byes ? "b" : "lb")}",
        CricketDeliveryType.Bye => $"{delivery.Runs}b",
        CricketDeliveryType.LegBye => $"{delivery.Runs}lb",
        _ => delivery.Runs.ToString()
    };
}

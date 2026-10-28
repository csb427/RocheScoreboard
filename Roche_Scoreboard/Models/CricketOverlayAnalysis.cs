using System.Globalization;

namespace Roche_Scoreboard.Models;

public enum CricketOverlayKind { LastOver, SinceScore, SinceWicket, SinceBoundary, Extras, LastWicket, ScoringWindow, Toss, Comparison, Projection, Boundaries }
public enum CricketOverlayGroup { Review, Pressure, Context, Trajectory }
public sealed record CricketOverlayMetric(string Label, string Value);
public sealed record CricketOverlayFact(CricketOverlayKind Kind, CricketOverlayGroup Group, string Title,
    string Detail, IReadOnlyList<CricketOverlayMetric> Metrics, bool Available, bool Automatic,
    int Priority, string Fingerprint, string? UnavailableReason = null);

/// <summary>Facts always come from the current delivery history, not increment-only counters.</summary>
public static class CricketOverlayAnalysis
{
    public static bool IsLegal(CricketDelivery d) => d.IsCountedBall;
    public static bool IsBoundary(CricketDelivery d) => d.Type is CricketDeliveryType.Four or CricketDeliveryType.Six
        || d.Type == CricketDeliveryType.NoBall && !d.CompletedRunsOnly && d.NoBallRuns == CricketNoBallRuns.Bat && d.Runs - 1 is 4 or 6;
    public static IReadOnlyList<CricketOverlayFact> Build(CricketMatchManager match)
    {
        var innings = match.CurrentInnings;
        if (innings is null) return [];
        var balls = innings.Deliveries; int legal = innings.LegalBallsBowled;
        string identity = $"{match.CurrentInningsNumber}:{match.DeliveryRevision}";
        var result = new List<CricketOverlayFact>();
        CricketOverlayFact Fact(CricketOverlayKind kind, CricketOverlayGroup group, string title, string detail,
            CricketOverlayMetric[] metrics, bool available = true, bool auto = false, int priority = 30, string? fingerprint = null, string? reason = null)
            => new(kind, group, title, detail, metrics, available, auto && !innings.IsComplete, priority, $"{identity}:{kind}:{fingerprint ?? legal.ToString(CultureInfo.InvariantCulture)}", reason);
        int completed = legal / 6 - 1;
        var lastOver = balls.Where(d => d.OverNumber == completed).ToArray();
        int overRuns = lastOver.Sum(d => d.Runs), overWickets = lastOver.Count(d => d.Type == CricketDeliveryType.Wicket);
        int overExtras=lastOver.Sum(d=>d.ScoringType switch {
            CricketDeliveryType.Wide or CricketDeliveryType.Bye or CricketDeliveryType.LegBye => d.Runs,
            CricketDeliveryType.NoBall => d.NoBallRuns==CricketNoBallRuns.Bat ? 1 : d.Runs, _ => 0 });
        var overMetrics=new List<CricketOverlayMetric> { new("RUNS",overRuns.ToString()) };
        if(overWickets>0) overMetrics.Add(new("WICKETS",overWickets.ToString()));
        if(overExtras>0) overMetrics.Add(new("EXTRAS",overExtras.ToString()));
        result.Add(Fact(CricketOverlayKind.LastOver, CricketOverlayGroup.Review, completed >= 0 ? $"LAST OVER · {completed+1}" : "LAST OVER",
            completed < 0 ? "NO COMPLETED OVER" : innings.BattingTeamName,
            overMetrics.ToArray(),
            auto: completed >= 0, priority: 38, fingerprint: completed.ToString()));
        AddDrought(CricketOverlayKind.SinceScore, "BALLS SINCE LAST SCORE", d => d.Runs > 0, 12, 55);
        AddDrought(CricketOverlayKind.SinceWicket, "BALLS SINCE LAST WICKET", d => d.Type == CricketDeliveryType.Wicket, 48, 36);
        AddDrought(CricketOverlayKind.SinceBoundary, "BALLS SINCE LAST BOUNDARY", IsBoundary, 24, 48);
        void AddDrought(CricketOverlayKind kind, string title, Func<CricketDelivery,bool> reset, int threshold, int priority)
        {
            int count = 0; bool found = false;
            for (int i = balls.Count-1; i >= 0; i--) { if (reset(balls[i])) { found = true; break; } if (IsLegal(balls[i])) count++; }
            bool showOvers = count >= 60;
            string value = showOvers ? count % 6 == 0 ? $"{count / 6}" : $"{count / 6}.{count % 6}" : count.ToString();
            result.Add(Fact(kind, CricketOverlayGroup.Pressure, showOvers ? title.Replace("BALLS", "OVERS") : title, found ? innings.BattingTeamName : $"{innings.BattingTeamName} · THIS INNINGS",
                [new(showOvers ? "OVERS" : "LEGAL BALLS", value)], auto: count >= threshold, priority: priority,
                fingerprint: $"{balls.Count - count}:{count / 6}"));
        }
        result.Add(Fact(CricketOverlayKind.Extras, CricketOverlayGroup.Context, "EXTRAS", innings.BattingTeamName,
            [new("NO-BALLS", $"{innings.NoBalls}"), new("WIDES", $"{innings.Wides}"), new("BYES", $"{innings.Byes}"), new("LEG-BYES", $"{innings.LegByes}")],
            auto: innings.TotalExtras >= 5 && legal >= 12, priority: 25));
        var boundaries=balls.Where(IsBoundary).ToArray();
        int fours=innings.BattingOrder.Sum(player=>player.Fours),sixes=innings.BattingOrder.Sum(player=>player.Sixes);
        result.Add(Fact(CricketOverlayKind.Boundaries,CricketOverlayGroup.Review,"INNINGS BOUNDARIES",innings.BattingTeamName,
            [new("FOURS",fours.ToString()),new("SIXES",sixes.ToString())],auto:boundaries.Length>0,priority:90,
            fingerprint:boundaries.LastOrDefault()?.Timestamp.Ticks.ToString()??"none"));
        var wicket = balls.LastOrDefault(d => d.Type == CricketDeliveryType.Wicket);
        if (wicket is null) result.Add(Fact(CricketOverlayKind.LastWicket, CricketOverlayGroup.Review, "LAST WICKET", "NO WICKET YET", [new("WICKETS", "0")]));
        else
        {
            int victim = wicket.DismissedBatterIndex >= 0 ? wicket.DismissedBatterIndex : wicket.StrikerIndex;
            var snapshot = wicket.StateBefore?.BattingOrder.ElementAtOrDefault(victim);
            var player = innings.BattingOrder.ElementAtOrDefault(victim);
            int batterRuns = wicket.ScoringType == CricketDeliveryType.Runs ? wicket.Runs
                : wicket.ScoringType == CricketDeliveryType.NoBall && wicket.NoBallRuns == CricketNoBallRuns.Bat ? Math.Max(0, wicket.Runs - 1) : 0;
            int runs = (snapshot?.Runs ?? player?.Runs ?? 0) + (snapshot != null && victim == wicket.StrikerIndex ? batterRuns : 0);
            int faced = (snapshot?.BallsFaced ?? player?.BallsFaced ?? 0) + (snapshot != null && victim == wicket.StrikerIndex && wicket.ScoringType != CricketDeliveryType.Wide ? 1 : 0);
            int index = balls.IndexOf(wicket);
            result.Add(Fact(CricketOverlayKind.LastWicket, CricketOverlayGroup.Review, "LAST WICKET", player?.DisplayName ?? wicket.BatsmanName ?? "BATTER",
                [new("RUNS", $"{runs}"), new("BALLS", $"{faced}")], auto: true, priority: 80, fingerprint: $"{index}:{wicket.Timestamp.Ticks}"));
        }
        bool tossKnown = match.TossWinner == match.TeamAName || match.TossWinner == match.TeamBName;
        bool first = match.CurrentInningsNumber == 1;
        result.Add(Fact(CricketOverlayKind.Toss, CricketOverlayGroup.Context, "TOSS RESULT",
            tossKnown ? $"{match.TossWinner} won the toss and will {(match.TossWinnerElectedToBat ? "bat" : "bowl")} first." : "TOSS NOT RECORDED",
            [new("ELECTED TO", match.TossWinnerElectedToBat ? "BAT" : "BOWL")], available: first && tossKnown,
            auto: first && tossKnown && legal <= 120, priority: legal < 12 ? 75 : legal < 36 ? 45 : 18,
            fingerprint: $"{match.TossWinner}:{match.TossWinnerElectedToBat}", reason: first ? "Record the toss in match setup." : "Toss applies only to the first innings of the game."));

        bool limited = match.Format == CricketFormat.LimitedOvers;
        bool chase = limited && match.CurrentInningsNumber == 2 && match.AllInnings.Count >= 2;
        int equivalentRuns = 0, equivalentWickets = 0, progress = 0;
        bool equivalent = chase && legal == 0;
        if (chase && legal > 0)
            foreach (var delivery in match.AllInnings[0].Deliveries)
            {
                if (IsLegal(delivery)) progress++;
                equivalentRuns = delivery.TotalAfter; equivalentWickets = delivery.WicketsAfter;
                if (progress == legal) { equivalent = true; break; }
            }
        result.Add(Fact(CricketOverlayKind.Comparison, CricketOverlayGroup.Context, $"AFTER {legal/6}.{legal%6} OVERS",
            chase ? match.AllInnings[0].BattingTeamName : "CHASE COMPARISON",
            [new("WAS", $"{equivalentWickets}/{equivalentRuns}")],
            available: chase && equivalent, auto: chase && equivalent && legal >= 12, priority: 40,
            reason: !chase ? "Available in a limited-overs second-innings chase." : "The first innings ended before this point."));
        int remaining = Math.Max(0, match.CurrentOverLimit * 6 - legal);
        double rate = legal > 0 ? innings.TotalRuns * 6.0 / legal : 0;
        var rates = new[] { Math.Max(0, rate == Math.Floor(rate) ? Math.Floor(rate)-1 : Math.Floor(rate)), Math.Ceiling(rate), Math.Ceiling(rate)+2 }.Distinct().ToArray();
        var orderedRates = rates.Where(candidate => Math.Abs(candidate - rate) > .000001)
            .Select(candidate => (Rate: candidate, Current: false)).Append((Rate: rate, Current: true)).OrderBy(candidate => candidate.Rate);
        var projected = orderedRates.Select(candidate => new CricketOverlayMetric(candidate.Current ? $"CURRENT {rate:F2}" : $"{candidate.Rate:0} RPO",
            candidate.Current && legal == 0 ? "—" : $"{innings.TotalRuns+(long)Math.Round(candidate.Rate*remaining/6.0)}")).ToArray();
        result.Add(Fact(CricketOverlayKind.Projection, CricketOverlayGroup.Trajectory, "PROJECTED SCORE", $"{remaining/6}.{remaining%6} OVERS REMAIN",
            projected, available: limited && remaining > 0 && !innings.IsComplete, auto: limited && legal >= 12 && remaining > 0,
            priority: 30, reason: "Available during limited-overs innings with balls remaining."));

        // Prefix totals make every candidate window O(1), including long multi-day histories.
        var runsAtBall = new int[legal+1]; var wicketsAtBall = new int[legal+1]; int recordedLegal = 0;
        foreach (var delivery in balls)
        {
            if (!IsLegal(delivery)) continue;
            if (++recordedLegal > legal) break;
            runsAtBall[recordedLegal] = delivery.TotalAfter; wicketsAtBall[recordedLegal] = delivery.WicketsAfter;
        }
        CricketOverlayFact? standout = null; double best = 0;
        // Fixed legal-ball windows include illegal deliveries inside that interval, not a fixed count of physical balls.
        for (int overs = 5; recordedLegal == legal && overs <= legal / 6; overs += 5)
        {
            int length = overs*6, beforeLegal = legal-length;
            int beforeRuns = runsAtBall[beforeLegal], beforeWickets = wicketsAtBall[beforeLegal];
            int runs = innings.TotalRuns-beforeRuns, wickets = innings.TotalWickets-beforeWickets;
            double windowRate = runs/(double)overs;
            double baseline = beforeLegal >= 12 ? beforeRuns*6.0/beforeLegal : 6;
            double severity = wickets >= 3 && windowRate <= Math.Max(3,baseline*.65) ? wickets*1.3 + Math.Max(0,baseline-windowRate)/2 : 0;
            if (windowRate >= Math.Max(10, baseline*1.4) && windowRate >= baseline+2) severity = Math.Max(severity,windowRate-baseline);
            if (severity <= best) continue;
            best = severity;
            standout = Fact(CricketOverlayKind.ScoringWindow, CricketOverlayGroup.Trajectory, $"LAST {overs} OVERS", innings.BattingTeamName,
                [new("RUNS", $"{runs}"), new("WICKETS", $"{wickets}"), new("RUN RATE", $"{windowRate:F2}")], auto: true, priority: 20, fingerprint: $"{legal}:{overs}");
        }
        result.Add(standout ?? Fact(CricketOverlayKind.ScoringWindow, CricketOverlayGroup.Trajectory, "SCORING WINDOW", "NO STANDOUT WINDOW YET",
            [new("OVERS", "5+")], available: false, reason: "Requires a significant surge or collapse over at least five overs."));
        return match.TossPending ? result.Select(f=>f with { Available=false,Automatic=false,UnavailableReason="Record the toss first." }).ToArray() : result;
    }
}

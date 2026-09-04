using System;
using System.Collections.Generic;
using System.Linq;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

/// <summary>
/// Finds the strongest current AFL goal-run story. Behinds are deliberately
/// excluded: broadcast goal runs describe majors in sequence, not points or
/// generic scoring events.
/// </summary>
internal static class AflGoalRunDetector
{
    private const int MinimumFeaturedGoals = 4;
    private const double MinimumDominanceShare = 0.75;

    internal sealed record Result(
        TeamSide Team,
        int TeamGoals,
        int WindowGoals,
        bool IsConsecutive,
        DateTime StartTimestamp,
        DateTime EndTimestamp)
    {
        public string StoryKey => $"{Team}:{StartTimestamp.Ticks}:{IsConsecutive}";
    }

    public static Result? Detect(
        IReadOnlyList<ScoreEvent> events,
        TeamSide featuredTeam,
        int minimumFeaturedGoals = MinimumFeaturedGoals,
        bool requireLatestGoal = true)
    {
        minimumFeaturedGoals = Math.Max(1, minimumFeaturedGoals);
        List<ScoreEvent> goals = events
            .Where(score => score.Type == ScoreType.Goal)
            .ToList();
        if (goals.Count < minimumFeaturedGoals || (requireLatestGoal && goals[^1].Team != featuredTeam))
            return null;

        int consecutive = 0;
        for (int index = goals.Count - 1; index >= 0 && goals[index].Team == featuredTeam; index--)
            consecutive++;

        Result? consecutiveResult = null;
        if (consecutive >= minimumFeaturedGoals)
        {
            ScoreEvent first = goals[goals.Count - consecutive];
            ScoreEvent last = goals[^1];
            consecutiveResult = new Result(
                featuredTeam, consecutive, consecutive, true, first.Timestamp, last.Timestamp);
        }

        Result? best = null;
        // Evaluate every suffix ending at the latest goal. There is no quarter
        // boundary or arbitrary goal-count cap: a legitimate run may begin
        // late in one quarter and continue into the next.
        for (int start = goals.Count - minimumFeaturedGoals - 1; start >= 0; start--)
        {
            int windowGoals = goals.Count - start;
            int teamGoals = 0;
            for (int index = start; index < goals.Count; index++)
                if (goals[index].Team == featuredTeam) teamGoals++;

            int oppositionGoals = windowGoals - teamGoals;
            if (teamGoals < minimumFeaturedGoals || oppositionGoals < 1)
                continue;

            double share = (double)teamGoals / windowGoals;
            if (share < MinimumDominanceShare)
                continue;

            var candidate = new Result(
                featuredTeam,
                teamGoals,
                windowGoals,
                false,
                goals[start].Timestamp,
                goals[^1].Timestamp);

            if (best is null
                || candidate.TeamGoals > best.TeamGoals
                || (candidate.TeamGoals == best.TeamGoals && candidate.WindowGoals < best.WindowGoals))
                best = candidate;
        }

        if (consecutiveResult is null)
            return best;
        if (best is null)
            return consecutiveResult;

        // A straight streak is the cleaner story when the alternatives are
        // close. Prefer the broader dominance story only when it adds at least
        // two meaningful goals (for example 6 of the last 7 over 4 straight,
        // or 11 of the last 14 over 5 straight).
        return best.TeamGoals >= consecutiveResult.TeamGoals + 2
            ? best
            : consecutiveResult;
    }

    /// <summary>
    /// Ranks both teams' current goal stories. This deliberately allows a
    /// dominant "N of the last M" sequence to survive one opposition reply;
    /// a straight streak still requires the latest goal to belong to that team.
    /// </summary>
    public static Result? DetectBest(IReadOnlyList<ScoreEvent> events, int minimumFeaturedGoals = MinimumFeaturedGoals)
    {
        Result? home = Detect(events, TeamSide.Home, minimumFeaturedGoals, requireLatestGoal: false);
        Result? away = Detect(events, TeamSide.Away, minimumFeaturedGoals, requireLatestGoal: false);
        if (home is null) return away;
        if (away is null) return home;

        double homeShare = (double)home.TeamGoals / home.WindowGoals;
        double awayShare = (double)away.TeamGoals / away.WindowGoals;
        if (home.TeamGoals != away.TeamGoals)
            return home.TeamGoals > away.TeamGoals ? home : away;
        if (home.IsConsecutive != away.IsConsecutive)
            return home.IsConsecutive ? home : away;
        if (Math.Abs(homeShare - awayShare) > 0.0001)
            return homeShare > awayShare ? home : away;
        return home.WindowGoals <= away.WindowGoals ? home : away;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

internal enum AflDroughtEventKind { Score, Goal }
internal enum AflDroughtScope { Match, Home, Away }

internal sealed record AflDroughtCandidate(
    AflDroughtScope Scope,
    AflDroughtEventKind Kind,
    TimeSpan Elapsed,
    DateTime AnchorTimestamp)
{
    // The event anchor makes each genuine drought a separate story. Without
    // it, a 20-minute drought shown earlier in the match could suppress the
    // 10-minute milestone of a completely new drought later in the match.
    public string Key => $"{Scope}:{Kind}:{AnchorTimestamp.Ticks}";
    public int TenMinuteMilestone => (int)Math.Floor(Elapsed.TotalMinutes / 10) * 10;
}

internal sealed record AflDroughtSnapshot(
    AflDroughtCandidate Match,
    AflDroughtCandidate Home,
    AflDroughtCandidate Away);

/// <summary>
/// Calculates AFL score and goal droughts exclusively from match playing time.
/// Wall-clock time and quarter breaks never enter the calculation.
/// </summary>
internal static class AflDroughtCalculator
{
    public static AflDroughtSnapshot Calculate(MatchManager match)
    {
        IReadOnlyList<ScoreEvent> events = match.Events;
        TimeSpan now = match.PlayingTimeAt(match.Quarter, match.ElapsedInQuarter);
        return new AflDroughtSnapshot(
            Select(match, events, now, AflDroughtScope.Match, null),
            Select(match, events, now, AflDroughtScope.Home, TeamSide.Home),
            Select(match, events, now, AflDroughtScope.Away, TeamSide.Away));
    }

    private static AflDroughtCandidate Select(
        MatchManager match,
        IReadOnlyList<ScoreEvent> events,
        TimeSpan now,
        AflDroughtScope scope,
        TeamSide? team)
    {
        IEnumerable<ScoreEvent> relevant = team is null
            ? events
            : events.Where(score => score.Team == team.Value);

        ScoreEvent? lastScore = relevant.LastOrDefault();
        ScoreEvent? lastGoal = relevant.LastOrDefault(score => score.Type == ScoreType.Goal);
        TimeSpan sinceScore = ElapsedSince(match, lastScore, now);
        TimeSpan sinceGoal = ElapsedSince(match, lastGoal, now);

        // SCORE takes precedence once it qualifies because a score drought
        // already includes a goal drought. Otherwise GOAL tells the useful
        // story when behinds have kept the scoreboard moving.
        AflDroughtEventKind kind = sinceScore.TotalMinutes >= 10
            ? AflDroughtEventKind.Score
            : sinceGoal > sinceScore
                ? AflDroughtEventKind.Goal
                : AflDroughtEventKind.Score;
        ScoreEvent? anchor = kind == AflDroughtEventKind.Score ? lastScore : lastGoal;
        return new AflDroughtCandidate(
            scope,
            kind,
            kind == AflDroughtEventKind.Score ? sinceScore : sinceGoal,
            anchor?.Timestamp ?? DateTime.MinValue);
    }

    private static TimeSpan ElapsedSince(
        MatchManager match,
        ScoreEvent? score,
        TimeSpan now)
    {
        if (score is null) return now;
        TimeSpan elapsed = now - match.PlayingTimeAt(score.Quarter, score.GameTime);
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }
}

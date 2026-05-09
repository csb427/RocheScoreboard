using System;
using System.Collections.Generic;

namespace Roche_Scoreboard.Models
{
    /// <summary>
    /// Auto-calculated match statistics derived from the event log.
    /// </summary>
    public sealed class MatchStats
    {
        // Scoring shots = goals + behinds for each team
        public int HomeScoringShots { get; init; }
        public int AwayScoringShots { get; init; }

        // Accuracy = goals / scoring shots (0-100%)
        public double HomeAccuracy { get; init; }
        public double AwayAccuracy { get; init; }

        // Time % in front (based on actual elapsed game seconds in each lead state)
        public double HomeTimePctInFront { get; init; }
        public double AwayTimePctInFront { get; init; }
        public double DrawPct { get; init; }

        // Largest lead (points)
        public int HomeLargestLead { get; init; }
        public int AwayLargestLead { get; init; }

        // Per-quarter goals scored (index 0 = Q1)
        public int[] HomeGoalsPerQuarter { get; init; } = new int[4];
        public int[] AwayGoalsPerQuarter { get; init; } = new int[4];
        public int[] HomeBehindsPerQuarter { get; init; } = new int[4];
        public int[] AwayBehindsPerQuarter { get; init; } = new int[4];

        // Best quarter (highest scoring quarter in total points)
        public int HomeBestQuarter { get; init; }
        public int AwayBestQuarter { get; init; }

        // Number of lead changes during the match
        public int LeadChanges { get; init; }

        /// <summary>
        /// Calculates match statistics. Pass the current quarter and elapsed time
        /// so the open segment from the last event to "now" is included in time-in-front.
        /// </summary>
        public static MatchStats Calculate(MatchManager match, int currentQuarter, TimeSpan currentElapsedInQuarter)
        {
            var events = match.Events;
            double quarterDurationSecs = match.QuarterDuration.TotalSeconds;

            int homeScoringShots = match.HomeGoals + match.HomeBehinds;
            int awayScoringShots = match.AwayGoals + match.AwayBehinds;

            double homeAccuracy = homeScoringShots > 0
                ? Math.Round(100.0 * match.HomeGoals / homeScoringShots, 1)
                : 0;
            double awayAccuracy = awayScoringShots > 0
                ? Math.Round(100.0 * match.AwayGoals / awayScoringShots, 1)
                : 0;

            int homeLargestLead = 0;
            int awayLargestLead = 0;
            double homeInFrontSecs = 0;
            double awayInFrontSecs = 0;
            double drawSecs = 0;
            int leadChanges = 0;
            int previousLeader = 0; // -1 = away leading, 0 = drawn/none, 1 = home leading

            // Per-quarter breakdown
            int[] hgQ = new int[4], abQ = new int[4], agQ = new int[4], hbQ = new int[4];

            // Cumulative game seconds for a given quarter + elapsed
            double CumulativeSecs(int quarter, TimeSpan elapsed) =>
                (quarter - 1) * quarterDurationSecs + elapsed.TotalSeconds;

            double previousTimestamp = 0; // game start
            int currentMargin = 0;        // margin at start of game = 0

            foreach (var ev in events)
            {
                double eventTimestamp = CumulativeSecs(ev.Quarter, ev.GameTime);
                double interval = Math.Max(0, eventTimestamp - previousTimestamp);

                // Accumulate time for the *previous* margin state
                if (currentMargin > 0)
                    homeInFrontSecs += interval;
                else if (currentMargin < 0)
                    awayInFrontSecs += interval;
                else
                    drawSecs += interval;

                // Update state to this event's margin
                currentMargin = ev.Margin;
                previousTimestamp = eventTimestamp;

                // Track largest lead
                if (currentMargin > homeLargestLead)
                    homeLargestLead = currentMargin;
                else if (-currentMargin > awayLargestLead)
                    awayLargestLead = -currentMargin;

                // Lead change detection
                int currentLeader = currentMargin > 0 ? 1 : currentMargin < 0 ? -1 : 0;
                if (currentLeader != 0 && previousLeader != 0 && currentLeader != previousLeader)
                    leadChanges++;
                if (currentLeader != 0)
                    previousLeader = currentLeader;

                // Per-quarter scoring breakdown
                int qi = Math.Clamp(ev.Quarter - 1, 0, 3);
                if (ev.Team == TeamSide.Home)
                {
                    if (ev.Type == ScoreType.Goal) hgQ[qi]++;
                    else hbQ[qi]++;
                }
                else
                {
                    if (ev.Type == ScoreType.Goal) agQ[qi]++;
                    else abQ[qi]++;
                }
            }

            // Open segment: last event to current game time
            double nowTimestamp = CumulativeSecs(currentQuarter, currentElapsedInQuarter);
            double tailInterval = Math.Max(0, nowTimestamp - previousTimestamp);
            if (currentMargin > 0)
                homeInFrontSecs += tailInterval;
            else if (currentMargin < 0)
                awayInFrontSecs += tailInterval;
            else
                drawSecs += tailInterval;

            double totalSecs = homeInFrontSecs + awayInFrontSecs + drawSecs;
            double homeTimePct = totalSecs > 0 ? Math.Round(100.0 * homeInFrontSecs / totalSecs, 1) : 0;
            double awayTimePct = totalSecs > 0 ? Math.Round(100.0 * awayInFrontSecs / totalSecs, 1) : 0;
            double drawPct = totalSecs > 0 ? Math.Round(100.0 * drawSecs / totalSecs, 1) : 0;

            // Best quarter by total points
            int homeBestQ = 0, awayBestQ = 0;
            int homeBestPts = 0, awayBestPts = 0;
            for (int q = 0; q < 4; q++)
            {
                int hPts = hgQ[q] * 6 + hbQ[q];
                int aPts = agQ[q] * 6 + abQ[q];
                if (hPts > homeBestPts) { homeBestPts = hPts; homeBestQ = q + 1; }
                if (aPts > awayBestPts) { awayBestPts = aPts; awayBestQ = q + 1; }
            }

            return new MatchStats
            {
                HomeScoringShots = homeScoringShots,
                AwayScoringShots = awayScoringShots,
                HomeAccuracy = homeAccuracy,
                AwayAccuracy = awayAccuracy,
                HomeTimePctInFront = homeTimePct,
                AwayTimePctInFront = awayTimePct,
                DrawPct = drawPct,
                HomeLargestLead = homeLargestLead,
                AwayLargestLead = awayLargestLead,
                HomeGoalsPerQuarter = hgQ,
                AwayGoalsPerQuarter = agQ,
                HomeBehindsPerQuarter = hbQ,
                AwayBehindsPerQuarter = abQ,
                HomeBestQuarter = homeBestQ,
                AwayBestQuarter = awayBestQ,
                LeadChanges = leadChanges
            };
        }
    }
}

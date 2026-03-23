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

        // Time % in front (based on proportion of scoring events where team was leading)
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

        public static MatchStats Calculate(MatchManager match)
        {
            var events = match.Events;

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
            int homeInFrontCount = 0;
            int awayInFrontCount = 0;
            int drawCount = 0;
            int leadChanges = 0;
            int previousLeader = 0; // -1 = away leading, 0 = drawn/none, 1 = home leading

            // Per-quarter breakdown
            int[] hgQ = new int[4], abQ = new int[4], agQ = new int[4], hbQ = new int[4];

            foreach (var ev in events)
            {
                int margin = ev.Margin; // positive = home leads

                int currentLeader = margin > 0 ? 1 : margin < 0 ? -1 : 0;

                // A lead change occurs when one team takes the lead from the other
                if (currentLeader != 0 && previousLeader != 0 && currentLeader != previousLeader)
                    leadChanges++;

                if (currentLeader != 0)
                    previousLeader = currentLeader;

                if (margin > 0)
                {
                    homeInFrontCount++;
                    if (margin > homeLargestLead) homeLargestLead = margin;
                }
                else if (margin < 0)
                {
                    awayInFrontCount++;
                    int awayLead = -margin;
                    if (awayLead > awayLargestLead) awayLargestLead = awayLead;
                }
                else
                {
                    drawCount++;
                }

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

            int totalEvents = events.Count;
            double homeTimePct = totalEvents > 0 ? Math.Round(100.0 * homeInFrontCount / totalEvents, 1) : 0;
            double awayTimePct = totalEvents > 0 ? Math.Round(100.0 * awayInFrontCount / totalEvents, 1) : 0;
            double drawPct = totalEvents > 0 ? Math.Round(100.0 * drawCount / totalEvents, 1) : 0;

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

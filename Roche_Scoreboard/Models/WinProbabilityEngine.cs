using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Roche_Scoreboard.Models
{
    /// <summary>
    /// Result of a win probability computation.
    /// </summary>
    public sealed class WinProbabilityResult
    {
        public double HomeWinPct { get; init; }
        public double AwayWinPct { get; init; }
        public double DrawPct { get; init; }
        public double ExpectedFinalMargin { get; init; }
        public double MarginStdDev { get; init; }
        /// <summary>0-1 confidence indicator — higher = more stable estimate.</summary>
        public double Confidence { get; init; }
        /// <summary>Change in home win probability since last computation.</summary>
        public double HomeDelta { get; init; }
    }

    /// <summary>
    /// Immutable snapshot of match state used for win probability computation.
    /// Thread-safe — capture once on the UI thread, then pass to background.
    /// </summary>
    public sealed class MatchSnapshot
    {
        public int HomeGoals { get; init; }
        public int HomeBehinds { get; init; }
        public int AwayGoals { get; init; }
        public int AwayBehinds { get; init; }
        public int HomeTotal => HomeGoals * 6 + HomeBehinds;
        public int AwayTotal => AwayGoals * 6 + AwayBehinds;
        public int CurrentMargin => HomeTotal - AwayTotal;
        public int Quarter { get; init; }
        /// <summary>Total elapsed match time in minutes across all completed quarters plus current.</summary>
        public double ElapsedMatchMinutes { get; init; }
        public double QuarterDurationMinutes { get; init; } = 20.0;
        public double TotalMatchMinutes => QuarterDurationMinutes * 4.0;
        public IReadOnlyList<ScoreEvent> RecentEvents { get; init; } = Array.Empty<ScoreEvent>();

        public static MatchSnapshot FromMatch(MatchManager match)
        {
            double qDur = match.QuarterDuration.TotalMinutes;
            double completedQuarterMinutes = (match.Quarter - 1) * qDur;
            double elapsedInCurrent = match.ElapsedInQuarter.TotalMinutes;

            // IMPORTANT: copy the event list. The engine runs Parallel.For on
            // worker threads and reads RecentEvents there; if the UI thread
            // mutates match.Events concurrently (operator scores, auto state
            // restore, etc.) iteration on a worker thread throws
            // InvalidOperationException and the Parallel.For AggregateException
            // tears the process down. An immutable array makes background
            // iteration safe. The copy itself happens on the UI thread (the
            // only writer) so it is race-free.
            ScoreEvent[] eventsCopy;
            var src = match.Events;
            int count = src?.Count ?? 0;
            if (count == 0)
            {
                eventsCopy = Array.Empty<ScoreEvent>();
            }
            else
            {
                eventsCopy = new ScoreEvent[count];
                for (int i = 0; i < count; i++)
                    eventsCopy[i] = src![i];
            }

            return new MatchSnapshot
            {
                HomeGoals = match.HomeGoals,
                HomeBehinds = match.HomeBehinds,
                AwayGoals = match.AwayGoals,
                AwayBehinds = match.AwayBehinds,
                Quarter = match.Quarter,
                ElapsedMatchMinutes = completedQuarterMinutes + elapsedInCurrent,
                QuarterDurationMinutes = qDur,
                RecentEvents = eventsCopy
            };
        }
    }

    // ── Regime states for momentum ──

    internal enum MatchRegime { Neutral, HomePressure, AwayPressure }

    /// <summary>
    /// Monte Carlo win probability engine for AFL matches.
    ///
    /// v2 model — significantly deeper than a naive "points + clock" model.
    /// It breaks down scoring into two decoupled processes and layers a set of
    /// match-shape priors on top:
    ///
    ///   1. <b>Scoring-shot intensity</b> λ (events/min) — how often each team
    ///      gets the ball into a shooting position. Estimated as a Bayesian
    ///      Gamma-Poisson posterior over <i>observed scoring shots</i>, not
    ///      just goals. A team with 5g 8b has 13 scoring shots and therefore
    ///      higher underlying λ than a team with 5g 2b, even though both sides
    ///      have 38 points. Recent shots are up-weighted via exponential decay
    ///      so current form drives the posterior more than early-match form.
    ///
    ///   2. <b>Conversion</b> θ (P(goal | scoring shot)) — estimated as a
    ///      Beta-Binomial posterior over the team's own accuracy, capped into
    ///      [0.30, 0.78]. A late-game "tightening" penalty shaves a little
    ///      accuracy off both sides when the margin is close, mimicking the
    ///      real pattern of rushed shots under pressure.
    ///
    ///   3. <b>Momentum regime</b> — short-window scoring dominance triggers
    ///      a HomePressure or AwayPressure state that lifts the dominant
    ///      team's λ and shaves the opponent's. Regime strength is damped
    ///      early and strengthens as the match progresses.
    ///
    ///   4. <b>Structural priors</b>:
    ///      • In-front inertia — whichever team has led for most of the
    ///        match so far gets a small λ boost (stability signal).
    ///      • Comeback drive — if the trailing team closed a sizeable
    ///        deficit, their λ gets a small late-game lift (resilience).
    ///      • Clutch regime — in the last 7 minutes with margin &lt;= 2 goals,
    ///        the trailing team picks up a desperation λ boost and a small
    ///        conversion penalty (rushed shots).
    ///      • Fatigue — global λ eases down slightly in the last 2 minutes
    ///        of the final quarter to reflect tiring legs.
    ///
    ///   5. <b>Monte Carlo rollout</b> — 3000 inhomogeneous Poisson samples.
    ///      Each event is thinned by a sinusoidal within-quarter phase (lulls
    ///      at quarter starts/ends) then converted to a goal with probability
    ///      θ (with in-sim late-game tightening).
    ///
    /// Win probability = fraction of sims where a team finishes ahead.
    /// </summary>
    public sealed class WinProbabilityEngine
    {
        // ── Structural constants ──
        private const int SimulationCount = 3000;

        // ── Bayesian prior for scoring-shot intensity (Gamma distribution) ──
        // Prior mean ≈ 0.22 scoring shots/min/team (one every ~4.5 min).
        // PriorBeta acts as "pseudo-minutes of observation" — the prior's weight.
        private const double PriorAlpha = 4.4;
        private const double PriorBeta = 20.0;

        // ── Conversion (goal | scoring shot) — Beta prior ──
        private const double ConvPriorA = 5.2;   // prior goals
        private const double ConvPriorB = 4.8;   // prior behinds  → mean ≈ 0.52
        private const double LateClosePenalty = 0.08;

        // ── Regime parameters ──
        private const double RegimeBoost = 0.30;
        private const double RegimeSuppress = 0.14;
        private const double RegimeWindowMinutes = 6.0;
        private const int RegimeMinDiff = 7;         // min point diff in window to activate

        // ── Secondary regime — scoring-shot pressure (volume, not points) ──
        // Rewards a team that's been getting into attacking positions a lot
        // even if they've been kicking behinds.
        private const double PressureWindowMinutes = 8.0;
        private const double PressureBoost = 0.12;

        // ── Structural priors ──
        private const double InFrontInertiaBoost = 0.06;  // max λ nudge for lead-time
        private const double ComebackBoost = 0.08;        // trailing team that has closed a deficit
        private const double ClutchBoostTrailing = 0.18;  // λ boost for trailing team in last 7 min, close game
        private const double ClutchPenaltyTrailing = 0.04;// conversion penalty for rushed shots
        private const double LateFatigueFactor = 0.92;    // global λ multiplier in last 2 min of Q4
        private const double RecentFormDecayMinutes = 12.0; // exponential decay time-constant for event weighting

        // ── Thread-safe RNG ──
        private static int _seedCounter = Environment.TickCount;
        private static readonly ThreadLocal<Random> Rng =
            new(() => new Random(Interlocked.Increment(ref _seedCounter)));

        private double _previousHomeWinPct = 0.5;

        /// <summary>
        /// Compute win probability from a match snapshot.
        /// Thread-safe — designed for background execution via Task.Run.
        /// </summary>
        public WinProbabilityResult Compute(MatchSnapshot snap)
        {
            var param = EstimateParameters(snap);

            // Accumulators (Parallel.For with thread-local state)
            int homeWins = 0, awayWins = 0, draws = 0;
            double marginSum = 0, marginSqSum = 0;

            Parallel.For(0, SimulationCount,
                () => new SimAccum(),
                (_, _, local) =>
                {
                    int margin = SimulateRemainder(snap, param, Rng.Value!);
                    local.MarginSum += margin;
                    local.MarginSqSum += (double)margin * margin;
                    if (margin > 0) local.HomeWins++;
                    else if (margin < 0) local.AwayWins++;
                    else local.Draws++;
                    return local;
                },
                local =>
                {
                    Interlocked.Add(ref homeWins, local.HomeWins);
                    Interlocked.Add(ref awayWins, local.AwayWins);
                    Interlocked.Add(ref draws, local.Draws);
                    AtomicAdd(ref marginSum, local.MarginSum);
                    AtomicAdd(ref marginSqSum, local.MarginSqSum);
                });

            double n = SimulationCount;
            double homePct = homeWins / n;
            double awayPct = awayWins / n;
            double drawPct = draws / n;
            double expMargin = marginSum / n;
            double variance = (marginSqSum / n) - (expMargin * expMargin);
            double stdDev = Math.Sqrt(Math.Max(0, variance));

            // Confidence: improves with time progress and stability
            double progress = Math.Clamp(snap.ElapsedMatchMinutes / snap.TotalMatchMinutes, 0, 1);
            double confidence = Math.Clamp(
                0.3 + 0.7 * progress - 0.12 * (stdDev / Math.Max(1, Math.Abs(expMargin) + 6)),
                0.1, 1.0);

            double delta = homePct - _previousHomeWinPct;
            _previousHomeWinPct = homePct;

            return new WinProbabilityResult
            {
                HomeWinPct = homePct,
                AwayWinPct = awayPct,
                DrawPct = drawPct,
                ExpectedFinalMargin = expMargin,
                MarginStdDev = stdDev,
                Confidence = confidence,
                HomeDelta = delta
            };
        }

        /// <summary>Reset for a new game.</summary>
        public void Reset() => _previousHomeWinPct = 0.5;

        // ── Parameter estimation ──

        private sealed class MatchParams
        {
            public double HomeLambda;           // scoring-shot rate events/min
            public double AwayLambda;
            public double HomeConversion;       // P(goal | scoring shot)
            public double AwayConversion;
            public MatchRegime Regime;
            public double RegimeStrength;
            public bool ClutchTrailingHome;     // home is the trailing team in clutch time
            public bool ClutchTrailingAway;
        }

        private MatchParams EstimateParameters(MatchSnapshot snap)
        {
            double elapsed = Math.Max(snap.ElapsedMatchMinutes, 0.5);

            // Observed scoring shots (goals + behinds). Using shots instead of
            // just goals means attacking pressure is captured even when the
            // opponent is kicking poorly (5.8 vs 5.2 signals same scoreboard
            // but very different momentum).
            int hShots = snap.HomeGoals + snap.HomeBehinds;
            int aShots = snap.AwayGoals + snap.AwayBehinds;

            // Recency-weighted effective shot count — recent events weigh more
            // via exponential decay. Falls back to raw count if no events.
            (double hShotsWeighted, double aShotsWeighted, double effectiveElapsed) =
                RecencyWeightedShots(snap, elapsed);

            // Bayesian posterior for scoring-shot intensity (Gamma-Poisson).
            double hLambda = (PriorAlpha + hShotsWeighted) / (PriorBeta + effectiveElapsed);
            double aLambda = (PriorAlpha + aShotsWeighted) / (PriorBeta + effectiveElapsed);

            // Conversion rates (Beta-Binomial posterior) based on raw shots —
            // accuracy is a slower-moving stat than intensity.
            double hConv = EstimateConversion(snap.HomeGoals, hShots, snap);
            double aConv = EstimateConversion(snap.AwayGoals, aShots, snap);

            // Momentum regime — short-window points dominance
            var (regime, strength) = DetectRegime(snap);

            // Apply regime adjustments
            if (regime == MatchRegime.HomePressure)
            {
                hLambda *= 1.0 + RegimeBoost * strength;
                aLambda *= 1.0 - RegimeSuppress * strength;
            }
            else if (regime == MatchRegime.AwayPressure)
            {
                aLambda *= 1.0 + RegimeBoost * strength;
                hLambda *= 1.0 - RegimeSuppress * strength;
            }

            // Scoring-shot pressure (volume-based) regime — captures a team
            // winning the field position battle even if inaccurate.
            double pressureDiff = RecentShotDiff(snap, PressureWindowMinutes);
            if (Math.Abs(pressureDiff) >= 2)
            {
                double pressureStrength = Math.Clamp((Math.Abs(pressureDiff) - 1) / 6.0, 0.0, 1.0);
                double progress = snap.ElapsedMatchMinutes / snap.TotalMatchMinutes;
                pressureStrength *= Math.Clamp(progress * 2.5, 0.2, 1.0);
                if (pressureDiff > 0)
                    hLambda *= 1.0 + PressureBoost * pressureStrength;
                else
                    aLambda *= 1.0 + PressureBoost * pressureStrength;
            }

            // In-front inertia — whichever team has been ahead for longer gets
            // a small λ boost as a stability signal.
            double leadTimeBoost = EstimateLeadTimeBoost(snap);
            if (leadTimeBoost > 0)
                hLambda *= 1.0 + leadTimeBoost;
            else if (leadTimeBoost < 0)
                aLambda *= 1.0 + Math.Abs(leadTimeBoost);

            // Comeback drive — if the trailing team has already closed a
            // sizeable deficit, give them a small late-game lift.
            double comebackHome, comebackAway;
            (comebackHome, comebackAway) = EstimateComebackBoost(snap);
            hLambda *= 1.0 + comebackHome;
            aLambda *= 1.0 + comebackAway;

            // Clutch regime — last 7 min with ≤ 2 goals margin
            bool clutchTrailingHome = false, clutchTrailingAway = false;
            double totalMin = snap.TotalMatchMinutes;
            double remaining = totalMin - snap.ElapsedMatchMinutes;
            int absMargin = Math.Abs(snap.CurrentMargin);
            if (remaining <= 7 && remaining > 0 && absMargin <= 12)
            {
                double clutchStrength = Math.Clamp((7.0 - remaining) / 7.0, 0.2, 1.0);
                if (snap.CurrentMargin < 0)
                {
                    hLambda *= 1.0 + ClutchBoostTrailing * clutchStrength;
                    hConv -= ClutchPenaltyTrailing * clutchStrength;
                    clutchTrailingHome = true;
                }
                else if (snap.CurrentMargin > 0)
                {
                    aLambda *= 1.0 + ClutchBoostTrailing * clutchStrength;
                    aConv -= ClutchPenaltyTrailing * clutchStrength;
                    clutchTrailingAway = true;
                }
            }

            hConv = Math.Clamp(hConv, 0.30, 0.78);
            aConv = Math.Clamp(aConv, 0.30, 0.78);

            return new MatchParams
            {
                HomeLambda = hLambda,
                AwayLambda = aLambda,
                HomeConversion = hConv,
                AwayConversion = aConv,
                Regime = regime,
                RegimeStrength = strength,
                ClutchTrailingHome = clutchTrailingHome,
                ClutchTrailingAway = clutchTrailingAway,
            };
        }

        /// <summary>
        /// Compute recency-weighted effective shot counts and an effective
        /// observation window. Recent events are up-weighted so current form
        /// drives the posterior more than first-quarter form.
        /// </summary>
        private static (double homeWeighted, double awayWeighted, double effectiveElapsed)
            RecencyWeightedShots(MatchSnapshot snap, double elapsed)
        {
            var events = snap.RecentEvents;
            if (events.Count == 0)
                return (snap.HomeGoals + snap.HomeBehinds, snap.AwayGoals + snap.AwayBehinds, elapsed);

            DateTime now = DateTime.Now;
            double hW = 0, aW = 0;
            double weightSum = 0;
            foreach (var ev in events)
            {
                double ageMin = Math.Max(0, (now - ev.Timestamp).TotalMinutes);
                double w = Math.Exp(-ageMin / RecentFormDecayMinutes);
                // Count both goals and behinds as 1 scoring shot.
                if (ev.Team == TeamSide.Home) hW += w;
                else aW += w;
                weightSum += w;
            }

            // Scale weighted counts up to match the raw count total so the
            // posterior keeps its magnitude — only the balance shifts.
            int totalShots = events.Count;
            double scale = weightSum > 0 ? totalShots / weightSum : 1.0;
            hW *= scale;
            aW *= scale;

            return (hW, aW, elapsed);
        }

        /// <summary>Scoring-shot difference (home − away) within the last <paramref name="windowMinutes"/>.</summary>
        private static double RecentShotDiff(MatchSnapshot snap, double windowMinutes)
        {
            var events = snap.RecentEvents;
            if (events.Count == 0) return 0;
            DateTime cutoff = DateTime.Now.AddMinutes(-windowMinutes);
            int h = 0, a = 0;
            for (int i = events.Count - 1; i >= 0; i--)
            {
                var ev = events[i];
                if (ev.Timestamp < cutoff) break;
                if (ev.Team == TeamSide.Home) h++;
                else a++;
            }
            return h - a;
        }

        /// <summary>
        /// Estimate a time-in-front boost. Positive = home has led longer.
        /// Magnitude peaks at <see cref="InFrontInertiaBoost"/> when one team
        /// has been ahead for nearly the whole match.
        /// </summary>
        private static double EstimateLeadTimeBoost(MatchSnapshot snap)
        {
            var events = snap.RecentEvents;
            if (events.Count == 0) return 0;

            double homeAhead = 0, awayAhead = 0;
            int hPts = 0, aPts = 0;
            DateTime prev = events[0].Timestamp;
            foreach (var ev in events)
            {
                double dt = Math.Max(0, (ev.Timestamp - prev).TotalMinutes);
                if (hPts > aPts) homeAhead += dt;
                else if (aPts > hPts) awayAhead += dt;
                int pts = ev.Type == ScoreType.Goal ? 6 : 1;
                if (ev.Team == TeamSide.Home) hPts += pts; else aPts += pts;
                prev = ev.Timestamp;
            }

            // Include tail up to "now"
            double tail = Math.Max(0, (DateTime.Now - prev).TotalMinutes);
            if (hPts > aPts) homeAhead += tail;
            else if (aPts > hPts) awayAhead += tail;

            double total = homeAhead + awayAhead;
            if (total <= 0.01) return 0;
            // Normalise to [-1, 1]
            double norm = (homeAhead - awayAhead) / total;
            // Dampen — prior inertia is subtle, not dominant.
            return norm * InFrontInertiaBoost;
        }

        /// <summary>
        /// If either team was trailing by a sizeable margin earlier but has
        /// since closed the gap, give them a small λ boost. Captures the
        /// "resilience" signal without overreacting.
        /// </summary>
        private static (double homeBoost, double awayBoost) EstimateComebackBoost(MatchSnapshot snap)
        {
            var events = snap.RecentEvents;
            if (events.Count < 6) return (0, 0);

            int hPts = 0, aPts = 0;
            int homeMaxDeficit = 0, awayMaxDeficit = 0;
            foreach (var ev in events)
            {
                int pts = ev.Type == ScoreType.Goal ? 6 : 1;
                if (ev.Team == TeamSide.Home) hPts += pts; else aPts += pts;
                int margin = hPts - aPts;
                if (-margin > homeMaxDeficit) homeMaxDeficit = -margin;
                if (margin > awayMaxDeficit) awayMaxDeficit = margin;
            }

            int currentMargin = hPts - aPts;
            double hB = 0, aB = 0;
            // Home has recovered from a 3+ goal deficit
            if (homeMaxDeficit >= 18 && -currentMargin < homeMaxDeficit - 6)
            {
                double recoverRatio = Math.Clamp(1.0 - Math.Max(0, -currentMargin) / (double)homeMaxDeficit, 0.0, 1.0);
                hB = ComebackBoost * recoverRatio;
            }
            if (awayMaxDeficit >= 18 && currentMargin < awayMaxDeficit - 6)
            {
                double recoverRatio = Math.Clamp(1.0 - Math.Max(0, currentMargin) / (double)awayMaxDeficit, 0.0, 1.0);
                aB = ComebackBoost * recoverRatio;
            }
            return (hB, aB);
        }

        private static double EstimateConversion(int goals, int totalEvents, MatchSnapshot snap)
        {
            double conv = (ConvPriorA + goals) / (ConvPriorA + ConvPriorB + totalEvents);

            // Late close-game pressure reduces accuracy
            double progress = snap.ElapsedMatchMinutes / snap.TotalMatchMinutes;
            double marginPts = Math.Abs(snap.CurrentMargin);
            if (progress > 0.6 && marginPts < 24)
            {
                double closeness = 1.0 - marginPts / 24.0;
                double lateness = (progress - 0.6) / 0.4;
                conv -= LateClosePenalty * closeness * lateness;
            }

            return Math.Clamp(conv, 0.30, 0.78);
        }

        private static (MatchRegime regime, double strength) DetectRegime(MatchSnapshot snap)
        {
            var events = snap.RecentEvents;
            if (events.Count < 3)
                return (MatchRegime.Neutral, 0.0);

            // Look at scoring in the recent window
            var cutoff = DateTime.Now.AddMinutes(-RegimeWindowMinutes);
            int homeRecent = 0, awayRecent = 0;
            for (int i = events.Count - 1; i >= 0; i--)
            {
                var ev = events[i];
                if (ev.Timestamp < cutoff) break;
                int pts = ev.Type == ScoreType.Goal ? 6 : 1;
                if (ev.Team == TeamSide.Home) homeRecent += pts;
                else awayRecent += pts;
            }

            int diff = homeRecent - awayRecent;
            if (Math.Abs(diff) < RegimeMinDiff)
                return (MatchRegime.Neutral, 0.0);

            // Strength scales with dominance magnitude, capped at 1
            double strength = Math.Clamp((Math.Abs(diff) - RegimeMinDiff) / 25.0, 0.0, 1.0);

            // Dampen regime effects early in the match
            double progress = snap.ElapsedMatchMinutes / snap.TotalMatchMinutes;
            strength *= Math.Clamp(progress * 2.5, 0.2, 1.0);

            return (diff > 0 ? MatchRegime.HomePressure : MatchRegime.AwayPressure, strength);
        }

        // ── Monte Carlo rollout ──

        private static int SimulateRemainder(MatchSnapshot snap, MatchParams param, Random rng)
        {
            int homePoints = snap.HomeTotal;
            int awayPoints = snap.AwayTotal;
            double remaining = snap.TotalMatchMinutes - snap.ElapsedMatchMinutes;
            if (remaining <= 0) return homePoints - awayPoints;

            double combinedLambda = param.HomeLambda + param.AwayLambda;
            if (combinedLambda <= 0) return homePoints - awayPoints;

            double homeRatio = param.HomeLambda / combinedLambda;
            double t = 0;

            while (t < remaining)
            {
                // Exponential inter-arrival time
                double u = rng.NextDouble();
                if (u < 1e-15) u = 1e-15;
                double dt = -Math.Log(u) / combinedLambda;
                t += dt;
                if (t >= remaining) break;

                double matchTime = snap.ElapsedMatchMinutes + t;

                // Within-quarter phase modifier (thinning acceptance probability)
                double inQuarter = matchTime % snap.QuarterDurationMinutes;
                double qFrac = inQuarter / snap.QuarterDurationMinutes;
                double phase = 0.78 + 0.22 * Math.Sin(Math.PI * qFrac);

                // Late-Q4 fatigue — global rate eases down slightly in the
                // last 2 minutes as players tire.
                double timeLeft = snap.TotalMatchMinutes - matchTime;
                if (timeLeft < 2.0) phase *= LateFatigueFactor;

                if (rng.NextDouble() > phase) continue;

                // Which team scores?
                bool isHome = rng.NextDouble() < homeRatio;

                // Goal or behind?
                double conv = isHome ? param.HomeConversion : param.AwayConversion;

                // Extra late-game tightening within the simulation
                if (matchTime > snap.TotalMatchMinutes - 15)
                {
                    int margin = Math.Abs(homePoints - awayPoints);
                    if (margin < 18)
                    {
                        double late = (matchTime - (snap.TotalMatchMinutes - 15)) / 15.0;
                        conv -= 0.04 * (1.0 - margin / 18.0) * late;
                    }
                }
                conv = Math.Clamp(conv, 0.30, 0.78);

                int points = rng.NextDouble() < conv ? 6 : 1;
                if (isHome) homePoints += points;
                else awayPoints += points;
            }

            return homePoints - awayPoints;
        }

        // ── Helpers ──

        private sealed class SimAccum
        {
            public int HomeWins;
            public int AwayWins;
            public int Draws;
            public double MarginSum;
            public double MarginSqSum;
        }

        private static void AtomicAdd(ref double location, double value)
        {
            while (true)
            {
                double current = Volatile.Read(ref location);
                double desired = current + value;
                if (Interlocked.CompareExchange(ref location, desired, current) == current)
                    return;
            }
        }
    }
}

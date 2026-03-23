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

            return new MatchSnapshot
            {
                HomeGoals = match.HomeGoals,
                HomeBehinds = match.HomeBehinds,
                AwayGoals = match.AwayGoals,
                AwayBehinds = match.AwayBehinds,
                Quarter = match.Quarter,
                ElapsedMatchMinutes = completedQuarterMinutes + elapsedInCurrent,
                QuarterDurationMinutes = qDur,
                RecentEvents = match.Events
            };
        }
    }

    // ── Regime states for momentum ──

    internal enum MatchRegime { Neutral, HomePressure, AwayPressure }

    /// <summary>
    /// Monte Carlo win probability engine for AFL matches.
    ///
    /// Model overview:
    ///   • Scoring is modeled as an inhomogeneous Poisson process. Each team has a
    ///     time-varying intensity λ(t) representing expected scoring events per minute.
    ///   • The baseline intensity is estimated via Bayesian updating of a Gamma prior
    ///     (conjugate to the Poisson likelihood) using the match's observed scoring tempo.
    ///     This means high-scoring games produce high-intensity simulations and vice versa.
    ///   • A regime-switching model captures momentum: the match moves through Neutral,
    ///     HomePressure, or AwayPressure phases based on recent scoring trends. The active
    ///     regime boosts the dominant team's intensity and suppresses the other's.
    ///     Regime strength is damped early in the game and strengthens late.
    ///   • Conversion probability (goal vs behind) uses a Beta-Binomial posterior,
    ///     with a pressure penalty in close late-game situations.
    ///   • Within-quarter scoring varies via a sinusoidal phase modifier (thinning),
    ///     producing realistic lulls at quarter starts and ends.
    ///   • Win probability = fraction of 3000 Monte Carlo rollouts where a team finishes ahead.
    ///
    /// Behavior by game phase:
    ///   Q1 early   → Prior dominates, probabilities stay near 50/50 unless margin is large.
    ///   Mid-game   → Bayesian posterior tightens around observed pace; regime effects strengthen.
    ///   Late Q4    → Very few simulated events remain, so current margin almost determines outcome.
    ///               A 3-goal lead with 2 minutes left → ~97%+ probability.
    /// </summary>
    public sealed class WinProbabilityEngine
    {
        // ── Structural constants ──
        private const int SimulationCount = 3000;

        // ── Bayesian prior for scoring intensity (Gamma distribution) ──
        // Prior mean = PriorAlpha / PriorBeta ≈ 0.22 events/min/team (one every ~4.5 min)
        // PriorBeta acts as "pseudo-minutes of observation" — the prior's weight
        private const double PriorAlpha = 4.4;
        private const double PriorBeta = 20.0;

        // ── Conversion (goal probability) — Beta prior ──
        private const double ConvPriorA = 5.2;   // prior goals
        private const double ConvPriorB = 4.8;   // prior behinds → mean ≈ 0.52
        private const double LateClosePenalty = 0.08;

        // ── Regime parameters ──
        private const double RegimeBoost = 0.28;
        private const double RegimeSuppress = 0.12;
        private const double RegimeWindowMinutes = 6.0;
        private const int RegimeMinDiff = 7;         // min point diff in window to activate

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
            public double HomeLambda;
            public double AwayLambda;
            public double HomeConversion;
            public double AwayConversion;
            public MatchRegime Regime;
            public double RegimeStrength;
        }

        private MatchParams EstimateParameters(MatchSnapshot snap)
        {
            double elapsed = Math.Max(snap.ElapsedMatchMinutes, 0.5);

            // Bayesian posterior intensity: Gamma(alpha + n, beta + T)
            int hEvents = snap.HomeGoals + snap.HomeBehinds;
            int aEvents = snap.AwayGoals + snap.AwayBehinds;
            double hLambda = (PriorAlpha + hEvents) / (PriorBeta + elapsed);
            double aLambda = (PriorAlpha + aEvents) / (PriorBeta + elapsed);

            // Conversion rates (Beta-Binomial posterior)
            double hConv = EstimateConversion(snap.HomeGoals, hEvents, snap);
            double aConv = EstimateConversion(snap.AwayGoals, aEvents, snap);

            // Regime detection
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

            return new MatchParams
            {
                HomeLambda = hLambda,
                AwayLambda = aLambda,
                HomeConversion = hConv,
                AwayConversion = aConv,
                Regime = regime,
                RegimeStrength = strength
            };
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

            return Math.Clamp(conv, 0.30, 0.75);
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

                // Within-quarter phase modifier (thinning acceptance probability)
                double matchTime = snap.ElapsedMatchMinutes + t;
                double inQuarter = matchTime % snap.QuarterDurationMinutes;
                double qFrac = inQuarter / snap.QuarterDurationMinutes;
                double phase = 0.78 + 0.22 * Math.Sin(Math.PI * qFrac);
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
                conv = Math.Clamp(conv, 0.30, 0.75);

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

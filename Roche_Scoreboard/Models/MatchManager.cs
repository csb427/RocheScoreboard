using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Roche_Scoreboard.Models
{
    public sealed class MatchManager
    {
        // Teams
        public string HomeName { get; private set; } = "HOME";
        public string HomeAbbr { get; private set; } = "HOM";
        public string AwayName { get; private set; } = "AWAY";
        public string AwayAbbr { get; private set; } = "AWY";

        // Score
        public int HomeGoals { get; private set; }
        public int HomeBehinds { get; private set; }
        public int AwayGoals { get; private set; }
        public int AwayBehinds { get; private set; }

        public int HomeTotal => HomeGoals * 6 + HomeBehinds;
        public int AwayTotal => AwayGoals * 6 + AwayBehinds;
        public int Margin => HomeTotal - AwayTotal;

        // Quarter/clock
        public int Quarter { get; private set; } = 1;

        public ClockMode ClockMode { get; private set; } = ClockMode.CountUp;
        public bool ClockRunning { get; private set; } = false;
        public TimeSpan QuarterDuration { get; private set; } = TimeSpan.FromMinutes(20);

        // Stopwatch-based game clock — immune to UI-thread delays
        private readonly Stopwatch _sw = new();
        private TimeSpan _pausedElapsed = TimeSpan.Zero;

        /// <summary>Elapsed time at the moment the last quarter was ended, for continue-quarter support.</summary>
        private TimeSpan _endedQuarterElapsed = TimeSpan.Zero;

        /// <summary>The quarter number that was most recently ended (0 if none).</summary>
        private int _endedQuarterNumber;

        /// <summary>Accurate elapsed time in the current quarter.</summary>
        public TimeSpan ElapsedInQuarter =>
            _pausedElapsed + (_sw.IsRunning ? _sw.Elapsed : TimeSpan.Zero);

        /// <summary>When in countdown mode, returns time remaining. Otherwise returns elapsed.</summary>
        public TimeSpan DisplayClock => ClockMode == ClockMode.Countdown
            ? TimeSpan.FromTicks(Math.Max(0, (QuarterDuration - ElapsedInQuarter).Ticks))
            : ElapsedInQuarter;

        public event Action? QuarterTimeExpired;

        // Display settings
        public bool DarkMode { get; private set; } = true;

        // Quarter-by-quarter cumulative snapshots (index 0 = Q1)
        private readonly QuarterSnapshot?[] _quarterSnapshots = new QuarterSnapshot?[4];

        public QuarterSnapshot? GetQuarterSnapshot(int quarter)
        {
            int idx = quarter - 1;
            if (idx < 0 || idx >= 4) return null;
            return _quarterSnapshots[idx];
        }

        // Log
        private readonly List<ScoreEvent> _events = [];
        public IReadOnlyList<ScoreEvent> Events => _events;

        public event Action? MatchChanged;
        public event Action<ScoreEvent>? ScoreEventAdded;

        public void SetTeams(string homeName, string homeAbbr, string awayName, string awayAbbr)
        {
            HomeName = string.IsNullOrWhiteSpace(homeName) ? "HOME" : homeName.Trim();
            HomeAbbr = string.IsNullOrWhiteSpace(homeAbbr) ? "HOM" : homeAbbr.Trim();
            AwayName = string.IsNullOrWhiteSpace(awayName) ? "AWAY" : awayName.Trim();
            AwayAbbr = string.IsNullOrWhiteSpace(awayAbbr) ? "AWY" : awayAbbr.Trim();
            MatchChanged?.Invoke();
        }

        public void SetDarkMode(bool enabled)
        {
            DarkMode = enabled;
            MatchChanged?.Invoke();
        }

        /// <summary>Called by the UI timer. Handles countdown expiry only.</summary>
        public void Tick()
        {
            if (!ClockRunning) return;

            // In countdown mode, fire event when time expires
            if (ClockMode == ClockMode.Countdown && ElapsedInQuarter >= QuarterDuration)
            {
                _sw.Stop();
                _pausedElapsed = QuarterDuration;
                ClockRunning = false;
                MatchChanged?.Invoke();
                QuarterTimeExpired?.Invoke();
            }
        }

        public void StartClock()
        {
            if (ClockRunning) return;
            // Don't allow clock to start after Q4 has ended
            if (Quarter == 4 && _quarterSnapshots[3] != null) return;
            ClockRunning = true;
            _sw.Restart();
            MatchChanged?.Invoke();
        }

        public void PauseClock()
        {
            if (!ClockRunning) return;
            _pausedElapsed += _sw.Elapsed;
            _sw.Reset();
            ClockRunning = false;
            MatchChanged?.Invoke();
        }

        public void SetClockMode(ClockMode mode)
        {
            ClockMode = mode;
            MatchChanged?.Invoke();
        }

        /// <summary>
        /// Adjusts the elapsed time in the current quarter by the given delta.
        /// The result is clamped to zero (cannot go negative).
        /// </summary>
        public void AdjustElapsed(TimeSpan delta)
        {
            if (ClockRunning)
            {
                _pausedElapsed += _sw.Elapsed;
                _sw.Restart();
            }

            _pausedElapsed += delta;
            if (_pausedElapsed < TimeSpan.Zero)
                _pausedElapsed = TimeSpan.Zero;

            MatchChanged?.Invoke();
        }

        /// <summary>
        /// Sets the elapsed time in the current quarter to the given value.
        /// The value is clamped to zero (cannot go negative).
        /// </summary>
        public void SetElapsed(TimeSpan elapsed)
        {
            if (ClockRunning)
                _sw.Restart();

            _pausedElapsed = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
            MatchChanged?.Invoke();
        }

        public void SetQuarterDuration(TimeSpan duration)
        {
            if (duration > TimeSpan.Zero)
                QuarterDuration = duration;
            MatchChanged?.Invoke();
        }

        public void ResetForNewGame()
        {
            HomeGoals = HomeBehinds = AwayGoals = AwayBehinds = 0;
            Quarter = 1;
            ClockRunning = false;
            _sw.Reset();
            _pausedElapsed = TimeSpan.Zero;
            _events.Clear();
            for (int i = 0; i < 4; i++) _quarterSnapshots[i] = null;
            MatchChanged?.Invoke();
        }

        /// <summary>
        /// Returns true if the current quarter was ended, false if Q4 already ended (no-op).
        /// </summary>
        public bool EndQuarter()
        {
            // Prevent repeated end-quarter calls once Q4 snapshot is recorded
            int idx = Quarter - 1;
            if (idx >= 0 && idx < 4 && _quarterSnapshots[idx] != null)
                return false;

            // Stop the clock if it's still running
            if (ClockRunning)
                _pausedElapsed += _sw.Elapsed;

            // Store elapsed time before resetting so ContinueQuarter can restore it
            _endedQuarterElapsed = _pausedElapsed;
            _endedQuarterNumber = Quarter;

            _sw.Reset();
            ClockRunning = false;
            _pausedElapsed = TimeSpan.Zero;

            // Snapshot cumulative scores at end of this quarter
            if (idx >= 0 && idx < 4)
            {
                _quarterSnapshots[idx] = new QuarterSnapshot
                {
                    HomeGoals = HomeGoals,
                    HomeBehinds = HomeBehinds,
                    AwayGoals = AwayGoals,
                    AwayBehinds = AwayBehinds
                };
            }

            if (Quarter < 4) Quarter++;

            MatchChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Returns true if the most recently ended quarter can be continued
        /// (i.e. it was ended and the clock has not yet started in the next quarter).
        /// </summary>
        public bool CanContinueQuarter =>
            _endedQuarterNumber > 0
            && _endedQuarterNumber < 4
            && !ClockRunning
            && _pausedElapsed == TimeSpan.Zero;

        /// <summary>
        /// Reverts to the quarter that was just ended, restoring the clock to where it left off.
        /// Returns true on success.
        /// </summary>
        public bool ContinueQuarter()
        {
            if (!CanContinueQuarter) return false;

            int prevQuarter = _endedQuarterNumber;
            int idx = prevQuarter - 1;

            // Remove the quarter snapshot
            if (idx >= 0 && idx < 4)
                _quarterSnapshots[idx] = null;

            Quarter = prevQuarter;
            _pausedElapsed = _endedQuarterElapsed;
            _endedQuarterNumber = 0;
            _endedQuarterElapsed = TimeSpan.Zero;

            MatchChanged?.Invoke();
            return true;
        }

        public ScoreEvent AddGoal(TeamSide team)
        {
            // Prevent scoring after Q4 has ended
            if (Quarter == 4 && _quarterSnapshots[3] != null)
                return CreateEvent(team, ScoreType.Goal);

            if (team == TeamSide.Home) HomeGoals++;
            else AwayGoals++;

            var ev = CreateEvent(team, ScoreType.Goal);
            _events.Add(ev);
            ScoreEventAdded?.Invoke(ev);
            MatchChanged?.Invoke();
            return ev;
        }

        public ScoreEvent AddBehind(TeamSide team)
        {
            // Prevent scoring after Q4 has ended
            if (Quarter == 4 && _quarterSnapshots[3] != null)
                return CreateEvent(team, ScoreType.Behind);

            if (team == TeamSide.Home) HomeBehinds++;
            else AwayBehinds++;

            var ev = CreateEvent(team, ScoreType.Behind);
            _events.Add(ev);
            ScoreEventAdded?.Invoke(ev);
            MatchChanged?.Invoke();
            return ev;
        }

        private ScoreEvent CreateEvent(TeamSide team, ScoreType type)
        {
            return new ScoreEvent
            {
                Quarter = Quarter,
                GameTime = ElapsedInQuarter,
                Team = team,
                Type = type,

                HomeGoals = HomeGoals,
                HomeBehinds = HomeBehinds,
                AwayGoals = AwayGoals,
                AwayBehinds = AwayBehinds
            };
        }

        public bool UndoLastScore()
        {
            if (_events.Count == 0) return false;
            _events.RemoveAt(_events.Count - 1);
            RebuildFromEvents();
            MatchChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Removes the event at <paramref name="index"/> and rebuilds all scores and snapshots.
        /// </summary>
        public bool RemoveEvent(int index)
        {
            if (index < 0 || index >= _events.Count) return false;
            _events.RemoveAt(index);
            RebuildFromEvents();
            MatchChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Changes the team, type, and/or time of the event at <paramref name="index"/>,
        /// then rebuilds all scores and snapshots from that point forward.
        /// </summary>
        public bool ModifyEvent(int index, TeamSide newTeam, ScoreType newType, TimeSpan newGameTime)
        {
            if (index < 0 || index >= _events.Count) return false;
            ScoreEvent ev = _events[index];
            ev.Team = newTeam;
            ev.Type = newType;
            ev.GameTime = newGameTime;
            RebuildFromEvents();
            MatchChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Recalculates cumulative scores and per-event snapshots from the event list.
        /// Also rebuilds quarter snapshots so scoreworm, stats, and break screens stay correct.
        /// </summary>
        private void RebuildFromEvents()
        {
            HomeGoals = HomeBehinds = AwayGoals = AwayBehinds = 0;

            // Clear quarter snapshots — they'll be rebuilt from event data
            for (int i = 0; i < 4; i++) _quarterSnapshots[i] = null;
            int lastSeenQuarter = 0;

            foreach (var ev in _events)
            {
                // If we've moved past a quarter boundary, snapshot the previous quarter(s)
                if (ev.Quarter > lastSeenQuarter && lastSeenQuarter > 0)
                {
                    int snapIdx = lastSeenQuarter - 1;
                    if (snapIdx >= 0 && snapIdx < 4 && _quarterSnapshots[snapIdx] == null)
                    {
                        _quarterSnapshots[snapIdx] = new QuarterSnapshot
                        {
                            HomeGoals = HomeGoals,
                            HomeBehinds = HomeBehinds,
                            AwayGoals = AwayGoals,
                            AwayBehinds = AwayBehinds
                        };
                    }
                }

                lastSeenQuarter = ev.Quarter;

                if (ev.Team == TeamSide.Home)
                {
                    if (ev.Type == ScoreType.Goal) HomeGoals++;
                    else HomeBehinds++;
                }
                else
                {
                    if (ev.Type == ScoreType.Goal) AwayGoals++;
                    else AwayBehinds++;
                }

                // Update the running snapshot on the event itself
                ev.HomeGoals = HomeGoals;
                ev.HomeBehinds = HomeBehinds;
                ev.AwayGoals = AwayGoals;
                ev.AwayBehinds = AwayBehinds;
            }
        }

        /// <summary>
        /// Validates the entire event sequence for impossible scoring patterns.
        /// Returns a list of human-readable warnings. An empty list means all is well.
        /// </summary>
        public List<ScoreValidationWarning> ValidateScoreSequence()
        {
            List<ScoreValidationWarning> warnings = [];
            int hg = 0, hb = 0, ag = 0, ab = 0;

            for (int i = 0; i < _events.Count; i++)
            {
                ScoreEvent ev = _events[i];

                // Apply this event
                if (ev.Team == TeamSide.Home)
                {
                    if (ev.Type == ScoreType.Goal) hg++;
                    else hb++;
                }
                else
                {
                    if (ev.Type == ScoreType.Goal) ag++;
                    else ab++;
                }

                // Check snapshot matches expected running totals
                if (ev.HomeGoals != hg || ev.HomeBehinds != hb || ev.AwayGoals != ag || ev.AwayBehinds != ab)
                {
                    warnings.Add(new ScoreValidationWarning(i, ev,
                        $"Event #{i + 1} snapshot mismatch — expected H {hg}.{hb}.{hg * 6 + hb} A {ag}.{ab}.{ag * 6 + ab}, " +
                        $"got H {ev.HomeGoals}.{ev.HomeBehinds}.{ev.HomeTotal} A {ev.AwayGoals}.{ev.AwayBehinds}.{ev.AwayTotal}"));
                }

                // Check total = goals*6 + behinds (should always hold, but verify)
                if (ev.HomeTotal != ev.HomeGoals * 6 + ev.HomeBehinds)
                {
                    warnings.Add(new ScoreValidationWarning(i, ev,
                        $"Event #{i + 1}: Home total {ev.HomeTotal} doesn't match {ev.HomeGoals}×6 + {ev.HomeBehinds} = {ev.HomeGoals * 6 + ev.HomeBehinds}"));
                }
                if (ev.AwayTotal != ev.AwayGoals * 6 + ev.AwayBehinds)
                {
                    warnings.Add(new ScoreValidationWarning(i, ev,
                        $"Event #{i + 1}: Away total {ev.AwayTotal} doesn't match {ev.AwayGoals}×6 + {ev.AwayBehinds} = {ev.AwayGoals * 6 + ev.AwayBehinds}"));
                }

                // Check scores never decrease from previous event
                if (i > 0)
                {
                    ScoreEvent prev = _events[i - 1];
                    if (ev.HomeGoals < prev.HomeGoals || ev.HomeBehinds < prev.HomeBehinds)
                    {
                        warnings.Add(new ScoreValidationWarning(i, ev,
                            $"Event #{i + 1}: Home score decreased — was {prev.HomeGoals}.{prev.HomeBehinds}, now {ev.HomeGoals}.{ev.HomeBehinds}"));
                    }
                    if (ev.AwayGoals < prev.AwayGoals || ev.AwayBehinds < prev.AwayBehinds)
                    {
                        warnings.Add(new ScoreValidationWarning(i, ev,
                            $"Event #{i + 1}: Away score decreased — was {prev.AwayGoals}.{prev.AwayBehinds}, now {ev.AwayGoals}.{ev.AwayBehinds}"));
                    }
                }

                // Check no negative values
                if (ev.HomeGoals < 0 || ev.HomeBehinds < 0 || ev.AwayGoals < 0 || ev.AwayBehinds < 0)
                {
                    warnings.Add(new ScoreValidationWarning(i, ev,
                        $"Event #{i + 1}: Negative score detected — H {ev.HomeGoals}.{ev.HomeBehinds} A {ev.AwayGoals}.{ev.AwayBehinds}"));
                }
            }

            // Check final state matches manager properties
            if (_events.Count > 0)
            {
                if (HomeGoals != hg || HomeBehinds != hb || AwayGoals != ag || AwayBehinds != ab)
                {
                    warnings.Add(new ScoreValidationWarning(-1, null,
                        $"Final score mismatch — manager shows H {HomeGoals}.{HomeBehinds} A {AwayGoals}.{AwayBehinds}, " +
                        $"events total H {hg}.{hb} A {ag}.{ab}"));
                }
            }

            return warnings;
        }

        // For saving/loading
        public SerializableState ToState() => new SerializableState
        {
            HomeName = HomeName,
            HomeAbbr = HomeAbbr,
            AwayName = AwayName,
            AwayAbbr = AwayAbbr,
            HomeGoals = HomeGoals,
            HomeBehinds = HomeBehinds,
            AwayGoals = AwayGoals,
            AwayBehinds = AwayBehinds,
            Quarter = Quarter,
            ClockRunning = ClockRunning,
            ElapsedInQuarterTicks = ElapsedInQuarter.Ticks,
            ClockMode = (int)ClockMode,
            QuarterDurationTicks = QuarterDuration.Ticks,
            DarkMode = DarkMode,
            Events = _events.ToList()
        };

        public void LoadState(SerializableState state)
        {
            HomeName = state.HomeName ?? "HOME";
            HomeAbbr = state.HomeAbbr ?? "HOM";
            AwayName = state.AwayName ?? "AWAY";
            AwayAbbr = state.AwayAbbr ?? "AWY";

            HomeGoals = Math.Max(0, state.HomeGoals);
            HomeBehinds = Math.Max(0, state.HomeBehinds);
            AwayGoals = Math.Max(0, state.AwayGoals);
            AwayBehinds = Math.Max(0, state.AwayBehinds);

            Quarter = Math.Clamp(state.Quarter, 1, 4);
            ClockMode = Enum.IsDefined(typeof(ClockMode), state.ClockMode)
                ? (ClockMode)state.ClockMode
                : ClockMode.CountUp;
            if (state.QuarterDurationTicks > 0)
                QuarterDuration = new TimeSpan(state.QuarterDurationTicks);
            _sw.Reset();
            _pausedElapsed = new TimeSpan(Math.Max(0, state.ElapsedInQuarterTicks));
            ClockRunning = false;
            if (state.ClockRunning)
                StartClock();
            DarkMode = state.DarkMode;

            _events.Clear();
            if (state.Events != null)
            {
                foreach (var ev in state.Events)
                {
                    if (ev != null) _events.Add(ev);
                }
            }

            // Rebuild quarter snapshots from event data so break screens,
            // scoreworm, stats, and full-time detection work after a restore.
            RebuildQuarterSnapshots();

            MatchChanged?.Invoke();
        }

        /// <summary>
        /// Rebuilds <see cref="_quarterSnapshots"/> from the event list.
        /// Each snapshot captures cumulative scores at the last event of a quarter
        /// whose successor belongs to a later quarter (i.e. the quarter boundary).
        /// If the current quarter matches the saved <see cref="Quarter"/> and no
        /// time has elapsed, the snapshot for that quarter is also written so that
        /// full-time detection works correctly for restored Q4-ended games.
        /// </summary>
        private void RebuildQuarterSnapshots()
        {
            for (int i = 0; i < 4; i++) _quarterSnapshots[i] = null;

            if (_events.Count == 0) return;

            int lastSeenQuarter = 0;
            int hg = 0, hb = 0, ag = 0, ab = 0;

            foreach (var ev in _events)
            {
                // When we cross into a new quarter, snapshot the previous one
                if (ev.Quarter > lastSeenQuarter && lastSeenQuarter > 0)
                {
                    int snapIdx = lastSeenQuarter - 1;
                    if (snapIdx >= 0 && snapIdx < 4 && _quarterSnapshots[snapIdx] == null)
                    {
                        _quarterSnapshots[snapIdx] = new QuarterSnapshot
                        {
                            HomeGoals = hg,
                            HomeBehinds = hb,
                            AwayGoals = ag,
                            AwayBehinds = ab
                        };
                    }
                }

                lastSeenQuarter = ev.Quarter;

                if (ev.Team == TeamSide.Home)
                {
                    if (ev.Type == ScoreType.Goal) hg++;
                    else hb++;
                }
                else
                {
                    if (ev.Type == ScoreType.Goal) ag++;
                    else ab++;
                }
            }

            // If the current quarter is beyond the last event's quarter,
            // snapshot the final event quarter (e.g. Q4 ended, clock stopped)
            if (Quarter > lastSeenQuarter || (Quarter == lastSeenQuarter && !ClockRunning && _pausedElapsed == TimeSpan.Zero))
            {
                int snapIdx = lastSeenQuarter - 1;
                if (snapIdx >= 0 && snapIdx < 4 && _quarterSnapshots[snapIdx] == null)
                {
                    _quarterSnapshots[snapIdx] = new QuarterSnapshot
                    {
                        HomeGoals = hg,
                        HomeBehinds = hb,
                        AwayGoals = ag,
                        AwayBehinds = ab
                    };
                }
            }
        }

        // Allow external callers to request the MatchChanged event be raised.
        // This avoids invoking the event directly from outside this type (CS0070).
        public void RaiseMatchChanged()
        {
            MatchChanged?.Invoke();
        }
    }

    public sealed class SerializableState
    {
        public string? HomeName { get; set; }
        public string? HomeAbbr { get; set; }
        public string? AwayName { get; set; }
        public string? AwayAbbr { get; set; }

        public int HomeGoals { get; set; }
        public int HomeBehinds { get; set; }
        public int AwayGoals { get; set; }
        public int AwayBehinds { get; set; }

        public int Quarter { get; set; }
        public bool ClockRunning { get; set; }
        public long ElapsedInQuarterTicks { get; set; }
        public int ClockMode { get; set; }
        public long QuarterDurationTicks { get; set; }

        public bool DarkMode { get; set; }

        public List<ScoreEvent>? Events { get; set; }
    }

    public sealed record QuarterSnapshot
    {
        public int HomeGoals { get; init; }
        public int HomeBehinds { get; init; }
        public int AwayGoals { get; init; }
        public int AwayBehinds { get; init; }
        public int HomeTotal => HomeGoals * 6 + HomeBehinds;
        public int AwayTotal => AwayGoals * 6 + AwayBehinds;
    }

    public enum ClockMode
    {
        CountUp,
        Countdown
    }

    /// <summary>
    /// Describes a single score validation issue detected in the event sequence.
    /// </summary>
    public sealed record ScoreValidationWarning(int EventIndex, ScoreEvent? Event, string Message);
}

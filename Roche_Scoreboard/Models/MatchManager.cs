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
        private readonly QuarterSnapshot[] _quarterSnapshots = new QuarterSnapshot[4];

        public QuarterSnapshot? GetQuarterSnapshot(int quarter)
        {
            int idx = quarter - 1;
            if (idx < 0 || idx >= 4) return null;
            return _quarterSnapshots[idx];
        }

        // Log
        private readonly List<ScoreEvent> _events = new List<ScoreEvent>();
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
            for (int i = 0; i < 4; i++) _quarterSnapshots[i] = null!;
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
            {
                _pausedElapsed += _sw.Elapsed;
                _sw.Reset();
            }
            else
            {
                _sw.Reset();
            }

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

            // Rebuild score from scratch for safety
            HomeGoals = HomeBehinds = AwayGoals = AwayBehinds = 0;

            foreach (var e in _events)
            {
                if (e.Team == TeamSide.Home)
                {
                    if (e.Type == ScoreType.Goal) HomeGoals++;
                    else HomeBehinds++;
                }
                else
                {
                    if (e.Type == ScoreType.Goal) AwayGoals++;
                    else AwayBehinds++;
                }
            }

            MatchChanged?.Invoke();
            return true;
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

            MatchChanged?.Invoke();
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

    public sealed class QuarterSnapshot
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
}

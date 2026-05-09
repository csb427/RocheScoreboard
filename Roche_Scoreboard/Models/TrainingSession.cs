using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;

namespace Roche_Scoreboard.Models;

/// <summary>
/// Owns a queue of <see cref="TrainingTimer"/> intervals and the live
/// stopwatch that drives the active one. The session is purely a model — it
/// raises events for the UI/scorebug to react to (countdown ticks, interval
/// finished, queue exhausted) and never touches WPF.
/// </summary>
public sealed class TrainingSession
{
    private readonly ObservableCollection<TrainingTimer> _timers = new();
    private readonly Stopwatch _sw = new();
    private TimeSpan _remainingAtLastStart;
    private int _activeIndex;

    public TrainingSession()
    {
        Timers = new ReadOnlyObservableCollection<TrainingTimer>(_timers);
    }

    /// <summary>The full ordered queue of timers (active + upcoming + completed).</summary>
    public ReadOnlyObservableCollection<TrainingTimer> Timers { get; }

    /// <summary>
    /// Index of the currently-active interval into <see cref="Timers"/>. When
    /// the queue is empty or fully exhausted this is -1.
    /// </summary>
    public int ActiveIndex => _timers.Count == 0 ? -1 : _activeIndex;

    /// <summary>The currently-active timer, or null if none.</summary>
    public TrainingTimer? Active =>
        _activeIndex >= 0 && _activeIndex < _timers.Count ? _timers[_activeIndex] : null;

    /// <summary>The next-up timer after <see cref="Active"/>, or null if none.</summary>
    public TrainingTimer? UpNext =>
        _activeIndex + 1 >= 0 && _activeIndex + 1 < _timers.Count ? _timers[_activeIndex + 1] : null;

    /// <summary>True while the active timer's stopwatch is running.</summary>
    public bool IsRunning => _sw.IsRunning;

    /// <summary>
    /// True once every queued timer has hit zero (i.e. the session is fully
    /// exhausted and the final flash should run).
    /// </summary>
    public bool IsExhausted => _timers.Count > 0 && _activeIndex >= _timers.Count;

    /// <summary>Fires every UI tick while running; useful for live remaining-time UI.</summary>
    public event Action? Changed;

    /// <summary>
    /// Fires only when the queue's structure changes (add / remove / reorder /
    /// clear / advance / skip). Use this to drive expensive rebuilds — it
    /// won't fire on the per-frame tick.
    /// </summary>
    public event Action? StructuralChanged;

    /// <summary>
    /// Fires the moment an active interval hits zero. The argument is the
    /// timer that just finished. The session does NOT auto-advance — the UI
    /// is expected to show its finish flash and then call
    /// <see cref="Skip"/> / <see cref="AdvanceAfterFinish"/> when ready.
    /// </summary>
    public event Action<TrainingTimer>? IntervalFinished;

    private void RaiseStructural()
    {
        StructuralChanged?.Invoke();
        Changed?.Invoke();
    }

    public void AddTimer(TrainingTimer t)
    {
        _timers.Add(t);
        RaiseStructural();
    }

    public void RemoveTimer(TrainingTimer t)
    {
        int idx = _timers.IndexOf(t);
        if (idx < 0) return;

        _timers.RemoveAt(idx);

        if (idx < _activeIndex)
            _activeIndex--;
        else if (idx == _activeIndex)
        {
            _sw.Reset();
            _remainingAtLastStart = TimeSpan.Zero;
            // _activeIndex now points at the next timer (which has shifted up)
            // but we don't auto-start it — the operator must explicitly press
            // Start. Clamp so we never sit beyond the end.
            if (_activeIndex >= _timers.Count) _activeIndex = Math.Max(0, _timers.Count);
        }
        RaiseStructural();
    }

    public void MoveTimer(TrainingTimer t, int newIndex)
    {
        int oldIndex = _timers.IndexOf(t);
        if (oldIndex < 0 || oldIndex == newIndex) return;
        if (newIndex < 0 || newIndex >= _timers.Count) return;
        _timers.Move(oldIndex, newIndex);
        // Don't touch _activeIndex semantically — if the active interval was
        // moved we still want it to point at the same TrainingTimer instance.
        if (oldIndex == _activeIndex) _activeIndex = newIndex;
        else if (oldIndex < _activeIndex && newIndex >= _activeIndex) _activeIndex--;
        else if (oldIndex > _activeIndex && newIndex <= _activeIndex) _activeIndex++;
        RaiseStructural();
    }

    public void Clear()
    {
        _sw.Reset();
        _timers.Clear();
        _activeIndex = 0;
        _remainingAtLastStart = TimeSpan.Zero;
        RaiseStructural();
    }

    /// <summary>Starts (or resumes) the active interval's countdown.</summary>
    public void Start()
    {
        if (Active is null) return;
        if (Active.HasFinished) return;
        if (_sw.IsRunning) return;
        _remainingAtLastStart = Active.Remaining;
        _sw.Restart();
        Changed?.Invoke();
    }

    /// <summary>Pauses the active interval, banking the elapsed into <see cref="TrainingTimer.Remaining"/>.</summary>
    public void Pause()
    {
        if (!_sw.IsRunning) return;
        if (Active is { } a)
        {
            TimeSpan elapsed = _sw.Elapsed;
            a.Remaining = TimeSpan.FromTicks(Math.Max(0, (_remainingAtLastStart - elapsed).Ticks));
        }
        _sw.Reset();
        Changed?.Invoke();
    }

    /// <summary>Resets the active interval back to its full configured duration.</summary>
    public void ResetActive()
    {
        if (Active is { } a)
        {
            a.Remaining = a.Duration;
            a.HasFinished = false;
            _remainingAtLastStart = a.Remaining;
            if (_sw.IsRunning) _sw.Restart();
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Adds extra time to the active timer. Works while the timer is paused
    /// AND while it's running — when running, the stopwatch baseline is
    /// extended so the next <see cref="Tick"/> doesn't wipe out the bonus.
    /// </summary>
    public void AddTimeToActive(TimeSpan extra)
    {
        if (extra <= TimeSpan.Zero) return;
        if (Active is not { } a) return;
        if (a.HasFinished) return;

        a.Duration += extra;
        if (_sw.IsRunning)
        {
            // Extend the baseline so the running clock keeps the new time.
            _remainingAtLastStart += extra;
            a.Remaining = ActiveRemaining;
        }
        else
        {
            a.Remaining += extra;
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Skips the active interval — banks it as finished and advances the
    /// pointer to the next one. Does NOT auto-start the next one.
    /// </summary>
    public void Skip()
    {
        if (Active is { } a)
        {
            a.Remaining = TimeSpan.Zero;
            a.HasFinished = true;
        }
        _sw.Reset();
        _activeIndex++;
        _remainingAtLastStart = TimeSpan.Zero;
        RaiseStructural();
    }

    /// <summary>
    /// Advances the active pointer past a just-finished timer (called by the
    /// scorebug after its post-finish hold so the next interval becomes
    /// active). If <paramref name="autoStart"/> is true, the next timer's
    /// stopwatch starts immediately.
    /// </summary>
    public void AdvanceAfterFinish(bool autoStart)
    {
        if (Active is { } a)
        {
            a.Remaining = TimeSpan.Zero;
            a.HasFinished = true;
        }
        _sw.Reset();
        _activeIndex++;
        _remainingAtLastStart = TimeSpan.Zero;

        if (autoStart && Active is { } next && !next.HasFinished)
        {
            _remainingAtLastStart = next.Remaining;
            _sw.Restart();
        }
        RaiseStructural();
    }

    /// <summary>Live remaining time of the active interval.</summary>
    public TimeSpan ActiveRemaining
    {
        get
        {
            if (Active is null) return TimeSpan.Zero;
            if (!_sw.IsRunning) return Active.Remaining;
            TimeSpan rem = _remainingAtLastStart - _sw.Elapsed;
            return rem < TimeSpan.Zero ? TimeSpan.Zero : rem;
        }
    }

    /// <summary>
    /// Called from a UI 33ms tick. Updates the active timer's
    /// <see cref="TrainingTimer.Remaining"/> and fires
    /// <see cref="IntervalFinished"/> exactly once when it hits zero.
    /// </summary>
    public void Tick()
    {
        if (!_sw.IsRunning) return;
        if (Active is not { } a) return;
        if (a.HasFinished) return;

        a.Remaining = ActiveRemaining;

        if (a.Remaining <= TimeSpan.Zero)
        {
            a.Remaining = TimeSpan.Zero;
            a.HasFinished = true;
            _sw.Reset();
            _remainingAtLastStart = TimeSpan.Zero;
            IntervalFinished?.Invoke(a);
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Total remaining time across the active + queued timers. Used by the
    /// optional session-total readout.
    /// </summary>
    public TimeSpan TotalRemaining
    {
        get
        {
            TimeSpan total = TimeSpan.Zero;
            for (int i = Math.Max(0, _activeIndex); i < _timers.Count; i++)
            {
                total += i == _activeIndex ? ActiveRemaining : _timers[i].Remaining;
            }
            return total;
        }
    }

    /// <summary>Sum of all original durations — for the progress-of-session bar.</summary>
    public TimeSpan TotalDuration => _timers.Aggregate(TimeSpan.Zero, (acc, t) => acc + t.Duration);
}

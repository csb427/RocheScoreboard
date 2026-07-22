// ─────────────────────────────────────────────────────────────────────────────
// MainWindow.TrainingCloudRemote.cs
//
// Training-mode counterpart to MainWindow.CloudRemote.cs. Owns:
//   • Building the Training payload for `live_state` (mirrored by the
//     phone web app under `web/remote/src/apps/training/*`).
//   • Forwarding incoming `training_*` remote events onto the existing
//     `TrainingSession` model so phone presses behave identically to
//     local clicks.
//   • Pushing a snapshot whenever the queue or the active timer changes
//     so the phone clock stays in lock-step with the operator's PC.
//
// All cross-thread work funnels through `Dispatcher.InvokeAsync` because
// the dispatcher already marshals events onto the UI thread but the
// `Changed` callback fires from the WPF dispatcher tick that is already
// on the UI thread.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services.Cloud;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    /// <summary>
    /// Subscribes the cloud publisher to a freshly-created
    /// <see cref="TrainingSession"/>. Safe to call multiple times — only
    /// the first attach for a given instance wires the handlers.
    /// </summary>
    internal void AttachTrainingCloudBridge(TrainingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        // Use a tagged-private-handler so a second attach call against the
        // same session does not double-subscribe.
        session.StructuralChanged -= OnTrainingStructuralChangedPublish;
        session.StructuralChanged += OnTrainingStructuralChangedPublish;

        // Push an immediate snapshot so the phone sees the empty queue /
        // initial state without waiting for the first user action.
        PublishCloudSnapshot();
    }

    /// <summary>
    /// Detaches the cloud publisher from a session being torn down so it
    /// can be garbage collected.
    /// </summary>
    internal void DetachTrainingCloudBridge(TrainingSession session)
    {
        if (session is null) return;
        session.StructuralChanged -= OnTrainingStructuralChangedPublish;
    }

    private void OnTrainingStructuralChangedPublish() => PublishCloudSnapshot();

    /// <summary>
    /// Builds the Training payload for `live_state`. Only called from
    /// <see cref="BuildLiveStateSnapshot"/> when a training session is
    /// active.
    /// </summary>
    private LiveStateSnapshot BuildTrainingLiveStateSnapshot(TrainingSession session)
    {
        var timers = new List<TrainingTimerDto>(session.Timers.Count);
        foreach (TrainingTimer t in session.Timers)
        {
            timers.Add(new TrainingTimerDto
            {
                Id = t.Id.ToString(),
                Title = string.IsNullOrWhiteSpace(t.Title) ? "Timer" : t.Title,
                TotalSeconds = (int)Math.Round(t.Duration.TotalSeconds),
                RemainingSeconds = (int)Math.Round(t.Remaining.TotalSeconds),
                HasFinished = t.HasFinished,
            });
        }

        var active = session.Active;
        var training = new TrainingSnapshotDto
        {
            IsRunning = session.IsRunning,
            AutoAdvance = session.AutoAdvance,
            ActiveIndex = session.ActiveIndex,
            ActiveTitle = active?.Title ?? "",
            ActiveTotalSeconds = active is null ? 0 : (int)Math.Round(active.Duration.TotalSeconds),
            ActiveRemainingSeconds = (int)Math.Round(session.ActiveRemaining.TotalSeconds),
            SessionTotalSeconds = (int)Math.Round(session.TotalDuration.TotalSeconds),
            SessionRemainingSeconds = (int)Math.Round(session.TotalRemaining.TotalSeconds),
            Timers = timers,
        };

        string status = session.IsRunning
            ? "live"
            : (session.IsExhausted ? "fullTime" : "lobby");

        return new LiveStateSnapshot
        {
            Sport = "training",
            HomeName = "Training",
            HomeAbbr = "TRN",
            AwayName = "Session",
            AwayAbbr = "SES",
            ActiveScreen = "scorebug",
            SessionStatus = status,
            Training = training,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    // ── IRemoteCommandTarget — training command surface ────────────────────

    void IRemoteCommandTarget.TrainingStartPause()
    {
        if (_trainingSession is null) return;
        if (_trainingSession.IsRunning) _trainingSession.Pause();
        else _trainingSession.Start();
        PublishCloudSnapshot();
    }

    void IRemoteCommandTarget.TrainingSkip()
    {
        if (_trainingSession is null) return;
        _trainingSession.Skip();
        PublishCloudSnapshot();
    }

    void IRemoteCommandTarget.TrainingResetActive()
    {
        if (_trainingSession is null) return;
        _trainingSession.ResetActive();
        PublishCloudSnapshot();
    }

    void IRemoteCommandTarget.TrainingClearQueue()
    {
        if (_trainingSession is null) return;
        _trainingSession.Clear();
        PublishCloudSnapshot();
    }

    void IRemoteCommandTarget.TrainingSetAutoAdvance(bool enabled)
    {
        if (_trainingSession is null) return;
        _trainingSession.AutoAdvance = enabled;
        PublishCloudSnapshot();
    }

    void IRemoteCommandTarget.TrainingAddTimer(string title, int seconds, TrainingIntervalSpec? interval)
    {
        if (_trainingSession is null) return;

        if (interval is { Rounds: > 0, WorkSeconds: > 0 } spec)
        {
            string prefix = string.IsNullOrWhiteSpace(spec.TitlePrefix) ? "Set" : spec.TitlePrefix.Trim();
            for (int i = 1; i <= spec.Rounds; i++)
            {
                _trainingSession.AddTimer(new TrainingTimer($"{prefix} {i}", TimeSpan.FromSeconds(spec.WorkSeconds)));
                if (spec.RestSeconds > 0 && i < spec.Rounds)
                {
                    _trainingSession.AddTimer(new TrainingTimer($"{prefix} {i} rest", TimeSpan.FromSeconds(spec.RestSeconds)));
                }
            }
        }
        else if (seconds > 0)
        {
            string label = string.IsNullOrWhiteSpace(title) ? "Timer" : title.Trim();
            _trainingSession.AddTimer(new TrainingTimer(label, TimeSpan.FromSeconds(seconds)));
        }

        PublishCloudSnapshot();
    }

    void IRemoteCommandTarget.TrainingRemoveTimer(Guid id)
    {
        if (_trainingSession is null) return;
        if (id == Guid.Empty) return;

        TrainingTimer? target = null;
        foreach (TrainingTimer t in _trainingSession.Timers)
        {
            if (t.Id == id) { target = t; break; }
        }
        if (target is not null)
        {
            _trainingSession.RemoveTimer(target);
            PublishCloudSnapshot();
        }
    }

    void IRemoteCommandTarget.TrainingUpdateTimer(Guid id, string title, int seconds)
    {
        if (_trainingSession is null) return;
        if (id == Guid.Empty) return;

        TrainingTimer? target = null;
        foreach (TrainingTimer t in _trainingSession.Timers)
        {
            if (t.Id == id) { target = t; break; }
        }
        if (target is null) return;

        string label = string.IsNullOrWhiteSpace(title) ? "Timer" : title.Trim();
        int total = Math.Max(1, seconds);
        _trainingSession.UpdateTimer(target, label, TimeSpan.FromSeconds(total));
        PublishCloudSnapshot();
    }

    void IRemoteCommandTarget.TrainingAdjustActive(int seconds)
    {
        if (_trainingSession is null) return;
        if (seconds == 0) return;
        _trainingSession.AddTimeToActive(TimeSpan.FromSeconds(seconds));
        PublishCloudSnapshot();
    }

    void IRemoteCommandTarget.TrainingReorder(Guid id, string direction)
    {
        if (_trainingSession is null) return;
        if (id == Guid.Empty) return;

        int currentIndex = -1;
        TrainingTimer? target = null;
        for (int i = 0; i < _trainingSession.Timers.Count; i++)
        {
            if (_trainingSession.Timers[i].Id == id)
            {
                currentIndex = i;
                target = _trainingSession.Timers[i];
                break;
            }
        }
        if (target is null || currentIndex < 0) return;

        int delta = string.Equals(direction, "up", StringComparison.OrdinalIgnoreCase) ? -1 : 1;
        int newIndex = currentIndex + delta;
        if (newIndex < 0 || newIndex >= _trainingSession.Timers.Count) return;

        _trainingSession.MoveTimer(target, newIndex);
        PublishCloudSnapshot();
    }

    void IRemoteCommandTarget.TrainingExit()
    {
        // Mirrors the local "exit training mode" button on the desktop
        // panel. Fires the same path so animations, window teardown, and
        // sport-selection return all run identically.
        TrainingControlPanelView?.RequestExit();
    }
}

// Generated from MainWindow.TrainingCloudRemote.cs; edit translator/adapters.
// Source SHA-256: 1278c52417a309fdfaa19e266f5deaa0b297012ce5fb33f49a5a01e4dc9469a0
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
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
// All cross-thread work funnels through `Dispatcher.UIThread.InvokeAsync` because
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
    private TrainingCloudBridgeReceipt? _trainingCloudBridge;
    private sealed class TrainingCloudBridgeReceipt(TrainingSession session)
    {
        internal readonly TrainingSession Session = session;
        internal Action Publish = null!;
    }

    /// <summary>Owns exactly one current Training model's structural publisher.</summary>
    internal void AttachTrainingCloudBridge(TrainingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!OwnsTrainingRemoteCommand(session)) return;
        if (_trainingCloudBridge is { } previous)
        {
            if (ReferenceEquals(previous.Session, session)) { PublishTrainingRemoteSnapshot(session); return; }
            DetachTrainingCloudBridge(previous.Session);
        }
        var receipt = new TrainingCloudBridgeReceipt(session);
        receipt.Publish = () =>
        {
            // Unsubscribe cannot remove a delegate from an event invocation
            // already copied by the old model. Its receipt must still be current.
            if (ReferenceEquals(_trainingCloudBridge, receipt)) PublishTrainingRemoteSnapshot(session);
        };
        _trainingCloudBridge = receipt;
        session.StructuralChanged += receipt.Publish;
        PublishTrainingRemoteSnapshot(session);
    }

    /// <summary>Retire before unsubscribe; copied old invocations then become no-ops.</summary>
    internal void DetachTrainingCloudBridge(TrainingSession session)
    {
        if (_trainingCloudBridge is not { } receipt || !ReferenceEquals(receipt.Session, session)) return;
        _trainingCloudBridge = null;
        session.StructuralChanged -= receipt.Publish;
    }

    /// <summary>
    /// Builds the Training payload for `live_state`. Only called from
    /// <see cref="BuildLiveStateSnapshot"/> when a training session is
    /// active.
    /// </summary>
    internal static LiveStateSnapshot BuildTrainingLiveStateSnapshot(TrainingSession session)
    {
        var timers = new List<TrainingTimerDto>(session.Timers.Count);
        foreach (TrainingTimer t in session.Timers)
        {
            timers.Add(new TrainingTimerDto
            {
                Id = t.Id.ToString(),
                Title = string.IsNullOrWhiteSpace(t.Title) ? "Timer" : t.Title,
                TotalSeconds = (long)Math.Round(t.Duration.TotalSeconds),
                RemainingSeconds = (long)Math.Round(t.Remaining.TotalSeconds),
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
            ActiveTotalSeconds = active is null ? 0 : (long)Math.Round(active.Duration.TotalSeconds),
            ActiveRemainingSeconds = (long)Math.Round(session.ActiveRemaining.TotalSeconds),
            SessionTotalSeconds = (long)Math.Round(session.TotalDuration.TotalSeconds),
            SessionRemainingSeconds = (long)Math.Round(session.TotalRemaining.TotalSeconds),
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

    // UI-thread authority only. Receive-thread code captures the reference,
    // then evaluates this predicate inside the existing dispatcher closure.
    // Queue edits are not a session replacement: preserve ordered remote input.
    private bool OwnsTrainingRemoteCommand(TrainingSession expectedSession) =>
        ReferenceEquals(_trainingSession, expectedSession) && _sportMode == SportMode.Training && !_remoteCommandOwnerClosed;

    private void PublishTrainingRemoteSnapshot(TrainingSession expectedSession)
    {
        // Model observers may have replaced/exited this owner synchronously.
        // Obsolete command continuations never publish a newer owner's state.
        if (OwnsTrainingRemoteCommand(expectedSession)) PublishCloudSnapshot();
    }

    void IRemoteCommandTarget.TrainingStartPause()
    {
        if (_trainingSession is not { } session || !OwnsTrainingRemoteCommand(session)) return;
        if (session.IsRunning) session.Pause();
        else session.Start();
        PublishTrainingRemoteSnapshot(session);
    }

    void IRemoteCommandTarget.TrainingSkip()
    {
        if (_trainingSession is not { } session || !OwnsTrainingRemoteCommand(session)) return;
        session.Skip();
        PublishTrainingRemoteSnapshot(session);
    }

    void IRemoteCommandTarget.TrainingResetActive()
    {
        if (_trainingSession is not { } session || !OwnsTrainingRemoteCommand(session)) return;
        session.ResetActive();
        PublishTrainingRemoteSnapshot(session);
    }

    void IRemoteCommandTarget.TrainingClearQueue()
    {
        if (_trainingSession is not { } session || !OwnsTrainingRemoteCommand(session)) return;
        session.Clear();
        PublishTrainingRemoteSnapshot(session);
    }

    void IRemoteCommandTarget.TrainingSetAutoAdvance(bool enabled)
    {
        if (_trainingSession is not { } session || !OwnsTrainingRemoteCommand(session)) return;
        session.AutoAdvance = enabled;
        PublishTrainingRemoteSnapshot(session);
    }

    // Reserve capacity before accepting a remote mutation. Snapshot seconds can
    // be Int64 while the model still has a finite TimeSpan aggregate. Keep
    // per-add owner checks: callbacks may legitimately replace/edit the queue.
    private static bool TrainingRemoteHasDurationCapacity(TrainingSession session, long additionalSeconds,
        TrainingTimer? replacing = null, TimeSpan replacementDuration = default)
    {
        if (additionalSeconds < 0) return false;
        long availableTicks = TimeSpan.MaxValue.Ticks;
        foreach (TrainingTimer timer in session.Timers)
        {
            if (timer.Duration < TimeSpan.Zero || timer.Remaining < TimeSpan.Zero
                || timer.Remaining > timer.Duration) return false;
            TimeSpan duration = ReferenceEquals(timer, replacing) ? replacementDuration : timer.Duration;
            if (duration < TimeSpan.Zero || duration.Ticks > availableTicks) return false;
            availableTicks -= duration.Ticks;
        }
        return additionalSeconds <= availableTicks / TimeSpan.TicksPerSecond;
    }

    void IRemoteCommandTarget.TrainingAddTimer(string title, int seconds, TrainingIntervalSpec? interval)
    {
        if (_trainingSession is not { } session || !OwnsTrainingRemoteCommand(session)) return;
        if (interval is not null ? !TrainingRemoteValidation.ValidInterval(interval)
            : !TrainingRemoteValidation.ValidTimer(title, seconds)) return;

        if (interval is { Rounds: > 0, WorkSeconds: > 0 } spec)
        {
            long requestedSeconds = (long)spec.Rounds * spec.WorkSeconds + (spec.Rounds - 1L) * spec.RestSeconds;
            if (!TrainingRemoteHasDurationCapacity(session, requestedSeconds)) return;
            string prefix = string.IsNullOrWhiteSpace(spec.TitlePrefix) ? "Set" : spec.TitlePrefix.Trim();
            for (int i = 1; i <= spec.Rounds; i++)
            {
                if (!OwnsTrainingRemoteCommand(session)
                    || !TrainingRemoteHasDurationCapacity(session, spec.WorkSeconds)) return;
                session.AddTimer(new TrainingTimer($"{prefix} {i}", TimeSpan.FromSeconds(spec.WorkSeconds)));
                if (spec.RestSeconds > 0 && i < spec.Rounds)
                {
                    if (!OwnsTrainingRemoteCommand(session)
                        || !TrainingRemoteHasDurationCapacity(session, spec.RestSeconds)) return;
                    session.AddTimer(new TrainingTimer($"{prefix} {i} rest", TimeSpan.FromSeconds(spec.RestSeconds)));
                }
            }
        }
        else if (seconds > 0)
        {
            if (!TrainingRemoteHasDurationCapacity(session, seconds)) return;
            string label = string.IsNullOrWhiteSpace(title) ? "Timer" : title.Trim();
            session.AddTimer(new TrainingTimer(label, TimeSpan.FromSeconds(seconds)));
        }

        PublishTrainingRemoteSnapshot(session);
    }

    void IRemoteCommandTarget.TrainingRemoveTimer(Guid id)
    {
        if (_trainingSession is not { } session || !OwnsTrainingRemoteCommand(session)) return;
        if (id == Guid.Empty) return;

        TrainingTimer? target = null;
        foreach (TrainingTimer t in session.Timers)
        {
            if (t.Id == id) { target = t; break; }
        }
        if (target is not null)
        {
            session.RemoveTimer(target);
            PublishTrainingRemoteSnapshot(session);
        }
    }

    void IRemoteCommandTarget.TrainingUpdateTimer(Guid id, string title, int seconds)
    {
        if (_trainingSession is not { } session || !OwnsTrainingRemoteCommand(session)) return;
        if (id == Guid.Empty || !TrainingRemoteValidation.ValidTimer(title, seconds)) return;

        TrainingTimer? target = null;
        foreach (TrainingTimer t in session.Timers)
        {
            if (t.Id == id) { target = t; break; }
        }
        if (target is null || !TrainingRemoteHasDurationCapacity(session, 0, target, TimeSpan.FromSeconds(seconds))) return;

        string label = string.IsNullOrWhiteSpace(title) ? "Timer" : title.Trim();
        int total = seconds;
        session.UpdateTimer(target, label, TimeSpan.FromSeconds(total));
        PublishTrainingRemoteSnapshot(session);
    }

    void IRemoteCommandTarget.TrainingAdjustActive(int seconds)
    {
        if (_trainingSession is not { } session || !OwnsTrainingRemoteCommand(session)) return;
        if (seconds == 0 || (seconds > 0 && !TrainingRemoteHasDurationCapacity(session, seconds))) return;
        session.AddTimeToActive(TimeSpan.FromSeconds(seconds));
        PublishTrainingRemoteSnapshot(session);
    }

    void IRemoteCommandTarget.TrainingReorder(Guid id, string direction)
    {
        if (_trainingSession is not { } session || !OwnsTrainingRemoteCommand(session)) return;
        if (id == Guid.Empty || !TrainingRemoteValidation.ValidDirection(direction)) return;

        int currentIndex = -1;
        TrainingTimer? target = null;
        for (int i = 0; i < session.Timers.Count; i++)
        {
            if (session.Timers[i].Id == id)
            {
                currentIndex = i;
                target = session.Timers[i];
                break;
            }
        }
        if (target is null || currentIndex < 0) return;

        int delta = string.Equals(direction, "up", StringComparison.OrdinalIgnoreCase) ? -1 : 1;
        int newIndex = currentIndex + delta;
        if (newIndex < 0 || newIndex >= session.Timers.Count) return;

        session.MoveTimer(target, newIndex);
        PublishTrainingRemoteSnapshot(session);
    }

    void IRemoteCommandTarget.TrainingExit()
    {
        if (_trainingSession is not { } session || !OwnsTrainingRemoteCommand(session)) return;
        // The phone already confirms Exit. Keep the expected-session commit
        // bypass immediate; do not open the new local desktop review again.
        TrainingControlPanelView?.RequestExit();
    }
}

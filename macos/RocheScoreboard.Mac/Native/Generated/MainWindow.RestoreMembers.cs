// Generated from MainWindow.RestoreMembers.cs; edit translator/adapters.
// Source SHA-256: e8d76de7850f7569cabbdce412688a3a2adc7e0537b8d95ff17343c3a99265cb
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using MediaBrushes = Avalonia.Media.Brushes;
using MediaColor = Avalonia.Media.Color;
using MediaColorConverter = RocheScoreboard.Mac.Native.ColorConverter;


namespace Roche_Scoreboard;

public partial class MainWindow
{
    // ── Scoring run auto-detection ──

    private TeamSide _lastShownRunTeam;
    private int _lastShownRunGoalCount;
    private TimeSpan _lastShownRunMatchClock = TimeSpan.MinValue; // match-clock position when last shown
    private DateTime _lastShownRunEndTimestamp = DateTime.MinValue;
    private static readonly TimeSpan RunCooldown = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan RunNewStoryCooldown = TimeSpan.FromSeconds(75);
    private static readonly TimeSpan DroughtOverlayCooldown = TimeSpan.FromMinutes(1);
    private DateTime _nextDroughtOverlayAllowedAtUtc = DateTime.MinValue;
    private readonly Dictionary<string, int> _shownDroughtMilestones = new();
    private int _previousLeadChanges;
    private const int LeadChangeOverlayThreshold = 1;
    private const int LeadChangeMinTotal = 5;
    // Lead changes are only a compelling story while the match is genuinely
    // close. Once one side pulls away (margin > ~4 goals), showing a historic
    // lead-change count is misleading — suppress the overlay in that case.
    private const int LeadChangeMaxCurrentMargin = 24;
    private DateTime _deferScorePushUntilUtc = DateTime.MinValue;

    private void UpdateControlPanelViewportConstraints(Avalonia.Size windowSize)
    {
        if (MatchLogPanel is null) return;

        // MainContent is hosted by an outer ScrollViewer and is therefore
        // measured with unlimited height. Explicitly cap the log to the live
        // viewport so event cards scroll inside the log rather than increasing
        // the desired height of the entire control panel.
        MatchLogPanel.MaxHeight = CalculateMatchLogPanelMaxHeight(Math.Max(windowSize.Height, MainContent.MinimumCanvasHeight));
    }

    internal static double CalculateMatchLogPanelMaxHeight(double windowHeight)
        => Math.Max(180, double.IsFinite(windowHeight) ? windowHeight - 310 : 180);

    // ── Score-animation completion gating ──
    //
    // _activeScoreAnimations counts in-flight goal/behind animations (both
    // default storyboards and custom-MP4 goal videos). PushAllToScoreboard
    // skips writing score digits while this is > 0 so the next clock tick or
    // overlay cycle cannot clobber a flip mid-animation. The previous
    // wall-clock deferral was a guess; this is the genuine signal.
    //
    // _pendingScoreAnimations queues follow-up goals/behinds that arrive
    // while one is still animating, so back-to-back scoring events play out
    // in the order they were entered instead of stomping each other.
    private int _activeScoreAnimations;
    private readonly Queue<Action> _pendingScoreAnimations = new();
    private const int MaxPendingScoreAnimations = 6;
    private DispatcherTimer? _scoreAnimationWatchdog;
    private int _scoreAnimationGeneration;
    private int _activeScoreAnimationToken;

    // ── Auto overlay delay
    private static readonly TimeSpan AutoOverlayDelay = TimeSpan.FromSeconds(2);
    // Scoring run is a contextual analysis overlay — it should land AFTER
    // the goal animation, "+6" swipe, and any goal video have all cleared.
    // Keep a short post-animation breath so the run feels
    // like a follow-up commentary graphic rather than an interruption.
    private static readonly TimeSpan ScoringRunOverlayDelay = TimeSpan.FromSeconds(2);
    private DispatcherTimer? _autoOverlayDelayTimer;
    private Action? _autoOverlayDelayCancellation;
    private DispatcherTimer? _scoringRunDelayTimer;
    private DispatcherTimer? _breakScreenDelayTimer;

    // ── Overlay queue — ensures each overlay gets its full display time ──
    private readonly Queue<(Action Show, bool AllowWhenClockStopped, Action? Cancel)> _overlayQueue = new();
    private bool _overlayQueueWired;
    private bool _periodicOverlayReserved;
    private int _overlayQueueGeneration;

    private void CancelQueuedOverlays()
    {
        // Also invalidates an item already dequeued into the inter-overlay gap
        // timer. Clearing only the Queue was not enough: that captured item
        // could otherwise appear after a quarter transition or match reset.
        _overlayQueueGeneration++;
        while (_overlayQueue.Count > 0)
            _overlayQueue.Dequeue().Cancel?.Invoke();
    }

    private void QueueOverlay(Action showAction, bool isPriority, Action? onCancelled)
    {
        var item = (Show: showAction, AllowWhenClockStopped: isPriority, Cancel: onCancelled);
        if (!isPriority || _overlayQueue.Count == 0)
        {
            _overlayQueue.Enqueue(item);
            return;
        }

        // Keep existing event-driven stories in their original order, but put
        // a new event-driven story ahead of weather/stats cards already waiting.
        // Live match events should never become stale behind scheduled content.
        var pending = _overlayQueue.ToArray();
        _overlayQueue.Clear();
        foreach (var queued in pending.Where(entry => entry.AllowWhenClockStopped))
            _overlayQueue.Enqueue(queued);
        _overlayQueue.Enqueue(item);
        foreach (var queued in pending.Where(entry => !entry.AllowWhenClockStopped))
            _overlayQueue.Enqueue(queued);
    }

    /// <summary>
    /// Wires the <see cref="ScorebugControl.OverlayHidden"/> event so queued
    /// overlays are dequeued automatically after the current one finishes.
    /// </summary>
    private void EnsureOverlayQueueWired()
    {
        if (_overlayQueueWired || _scorebug == null) return;
        _scorebug.OverlayHidden += OnOverlayHidden;
        _overlayQueueWired = true;
    }

    private void OnOverlayHidden()
    {
        // Notify the scheduler that the overlay zone is now free
        _overlayScheduler.NotifyOverlayHidden();

        // Block both queued and non-queued overlays for a short clean-air beat so
        // the viewer gets a clear beat of scorebug-only between them.
        _nextOverlayAllowedAtUtc = DateTime.UtcNow + OverlayCooldown;

        if (_overlayQueue.Count == 0) return;

        var next = _overlayQueue.Dequeue();
        int queueGeneration = _overlayQueueGeneration;

        // Solid cooldown between consecutive overlays so the operator (and the
        // viewer) gets a clear beat of scorebug-only between them.
        DispatcherTimer gap = new() { Interval = OverlayCooldown };
        gap.Tick += (_, _) =>
        {
            gap.Stop();
            if (queueGeneration != _overlayQueueGeneration)
            {
                next.Cancel?.Invoke();
                return;
            }
            if (_scorebug == null)
            {
                next.Cancel?.Invoke();
                return;
            }

            // Wait for score animations to finish before showing
            if (_scorebug.IsScoreAnimating)
            {
                DispatcherTimer poll = new() { Interval = TimeSpan.FromMilliseconds(200) };
                poll.Tick += (_, _) =>
                {
                    if (queueGeneration != _overlayQueueGeneration)
                    {
                        poll.Stop();
                        next.Cancel?.Invoke();
                        return;
                    }
                    if (_scorebug == null || !_scorebug.IsScoreAnimating)
                    {
                        poll.Stop();
                        if (!_autoOverlaysEnabled || (!next.AllowWhenClockStopped && !_match.ClockRunning))
                        {
                            next.Cancel?.Invoke();
                            return;
                        }
                        next.Show();
                    }
                };
                poll.Start();
            }
            else
            {
                if (!_autoOverlaysEnabled || (!next.AllowWhenClockStopped && !_match.ClockRunning))
                {
                    next.Cancel?.Invoke();
                    return;
                }
                next.Show();
            }
        };
        gap.Start();
    }

    // Short cooldown between the end of one overlay and the start
    // of the next. Applies to both queued and non-queued auto overlays.
    private static readonly TimeSpan OverlayCooldown = TimeSpan.FromSeconds(25);
    private DateTime _nextOverlayAllowedAtUtc = DateTime.MinValue;

    /// <summary>
    /// Enqueues an overlay action. If no overlay is currently active, shows it
    /// immediately after the standard delay. Otherwise queues it to play after
    /// the current overlay finishes.
    /// <para>
    /// When <paramref name="isPriority"/> is <c>true</c> (event-driven overlays
    /// like scoring runs and lead changes), the informational cooldown
    /// is bypassed so the overlay lands close to the moment that triggered it.
    /// The overlay still waits politely if another overlay is currently showing.
    /// </para>
    /// </summary>
    private void EnqueueAutoOverlay(Action showAction, bool isPriority = false, Action? onCancelled = null)
    {
        if (!_autoOverlaysEnabled)
        {
            onCancelled?.Invoke();
            return;
        }
        if (_scorebug?.IsTickerActive == true)
        {
            onCancelled?.Invoke();
            return;
        }

        // Suppress informational (non-priority) overlays while the clock isn't
        // running. This catches the "continuance" case where the operator has
        // paused the clock between plays — we never want a stat/weather/recent
        // overlay to drop in then. Priority (event-driven) overlays still go
        // through so things like a goal celebration aren't lost.
        if (!isPriority && !_match.ClockRunning)
        {
            onCancelled?.Invoke();
            return;
        }

        EnsureOverlayQueueWired();

        // If another overlay is on-screen, queue and let OnOverlayHidden drain.
        if (_scorebug != null && _scorebug.IsOverlayActive)
        {
            // Priority (event-driven) overlays take precedence over a scheduled
            // informational overlay that happens to be on-screen — fade the
            // informational one out smoothly so the event-driven one can show
            // close to the moment that triggered it. Other event-driven
            // overlays are left alone (we don't trample one event with another).
            if (isPriority && _scorebug.CancelInformationalOverlay())
            {
                // CancelInformationalOverlay raises OverlayHidden which queues
                // a cooldown drain — but for priority overlays we want
                // the new one to start now. Reset the cooldown gate and run
                // the standard short delay so the goal animation can settle.
                _nextOverlayAllowedAtUtc = DateTime.MinValue;
                CancelQueuedOverlays();
                ScheduleAutoOverlay(showAction, allowWhenClockStopped: true, onCancelled);
                return;
            }

            QueueOverlay(showAction, isPriority, onCancelled);
            return;
        }

        // Nothing active — but an overlay might have just finished. Enforce the
        // cooldown so the viewer gets a real beat of scorebug-only
        // between any two overlays. Priority overlays bypass this cooldown so
        // event-driven moments (scoring run, lead changes, drought, scoreless)
        // land close to the event that triggered them.
        DateTime now = DateTime.UtcNow;
        if (!isPriority && now < _nextOverlayAllowedAtUtc)
        {
            QueueOverlay(showAction, isPriority: false, onCancelled);
            TimeSpan remaining = _nextOverlayAllowedAtUtc - now;
            DispatcherTimer wait = new() { Interval = remaining };
            wait.Tick += (_, _) =>
            {
                wait.Stop();
                // Kick the queue only if nothing else has started in the meantime.
                if (_scorebug != null && !_scorebug.IsOverlayActive)
                    OnOverlayHidden();
            };
            wait.Start();
            return;
        }

        // Nothing active — show after the standard delay
        ScheduleAutoOverlay(showAction, allowWhenClockStopped: isPriority, onCancelled);
    }

    /// <summary>
    /// Schedules an auto-triggered overlay after <paramref name="delay"/>.
    /// If a score animation is still playing when the delay elapses, polls
    /// every 200 ms until the animation finishes before invoking the action.
    /// <paramref name="getTimer"/>/<paramref name="setTimer"/> access the timer
    /// slot so different overlay types don't cancel each other.
    /// </summary>
    private void ScheduleAutoOverlay(
        Action showAction,
        TimeSpan delay,
        Func<DispatcherTimer?> getTimer,
        Action<DispatcherTimer?> setTimer,
        bool allowWhenClockStopped = false,
        Action? onCancelled = null)
    {
        getTimer()?.Stop();
        DispatcherTimer timer = new() { Interval = delay };
        setTimer(timer);
        timer.Tick += (_, _) =>
        {
            timer.Stop();

            // An overlay should never trample a goal animation, "+6" swipe,
            // active goal video, normal mp4 playback, or another on-screen
            // overlay. Wait until everything has cleared. This keeps the
            // scoring-run graphic feeling like a follow-up commentary card
            // rather than something that interrupts live scoring excitement.
            bool ShouldKeepWaiting() =>
                (_scorebug != null && _scorebug.IsScoreAnimating) ||
                (_scorebug != null && _scorebug.IsOverlayActive) ||
                IsAnyVideoPlaying();

            if (ShouldKeepWaiting())
            {
                DispatcherTimer poll = new() { Interval = TimeSpan.FromMilliseconds(200) };
                setTimer(poll);
                poll.Tick += (_, _) =>
                {
                    if (!ShouldKeepWaiting())
                    {
                        poll.Stop();
                        // Give the restored scorebug one stable visual beat
                        // before an automatic overlay starts. Starting on the
                        // exact poll tick that the prior animation ended caused
                        // the abrupt handoff not seen with manual activation.
                        DispatcherTimer settle = new() { Interval = TimeSpan.FromMilliseconds(360) };
                        setTimer(settle);
                        settle.Tick += (_, _) =>
                        {
                            if (ShouldKeepWaiting())
                                return;

                            settle.Stop();
                            setTimer(null);
                            if (!_autoOverlaysEnabled || (!allowWhenClockStopped && !_match.ClockRunning))
                            {
                                onCancelled?.Invoke();
                                return;
                            }
                            showAction();
                        };
                        settle.Start();
                    }
                };
                poll.Start();
            }
            else
            {
                setTimer(null);
                if (!_autoOverlaysEnabled || (!allowWhenClockStopped && !_match.ClockRunning))
                {
                    onCancelled?.Invoke();
                    return;
                }
                showAction();
            }
        };
        timer.Start();
    }

    /// <summary>Schedules an auto overlay with the default 2-second delay.</summary>
    private void ScheduleAutoOverlay(Action showAction, bool allowWhenClockStopped = false, Action? onCancelled = null)
    {
        if (_autoOverlayDelayTimer is not null)
        {
            _autoOverlayDelayTimer.Stop();
            _autoOverlayDelayCancellation?.Invoke();
        }
        _autoOverlayDelayCancellation = onCancelled;
        ScheduleAutoOverlay(showAction, AutoOverlayDelay,
            () => _autoOverlayDelayTimer, t =>
            {
                _autoOverlayDelayTimer = t;
                if (t is null) _autoOverlayDelayCancellation = null;
            },
            allowWhenClockStopped, onCancelled);
    }

    // ── Automatic overlay toggle ──
    private bool _autoOverlaysEnabled = true;

    private void AutoOverlaysToggle_Changed(object? sender, RoutedEventArgs e)
    {
        _autoOverlaysEnabled = AutoOverlaysToggle.IsChecked == true;
        OperatorWorkflowPreferences.Update(data => data.AflAutoOverlaysEnabled = _autoOverlaysEnabled);
        if (!_autoOverlaysEnabled)
        {
            _autoOverlayDelayTimer?.Stop();
            _autoOverlayDelayTimer = null;
            _autoOverlayDelayCancellation?.Invoke();
            _autoOverlayDelayCancellation = null;
            CancelQueuedOverlays();
            _periodicOverlayReserved = false;
        }
        UpdateAutoOverlaysCardVisuals();
    }

    private void AutoOverlaysCard_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        AutoOverlaysToggle.IsChecked = AutoOverlaysToggle.IsChecked != true;
    }

    private void UpdateAutoOverlaysCardVisuals()
    {
        if (AutoOverlaysCard == null || AutoOverlaysGlow == null || AutoOverlaysCheck == null) return;

        bool enabled = AutoOverlaysToggle.IsChecked == true;

        AutoOverlaysCard.BorderBrush = enabled
            ? new SolidColorBrush((Avalonia.Media.Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString("#3FB950"))
            : new SolidColorBrush((Avalonia.Media.Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString("#2A2A40"));
        AutoOverlaysCard.BorderThickness = new Thickness(2);
        AutoOverlaysCard.Background = enabled
            ? new SolidColorBrush((Avalonia.Media.Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString("#0A1410"))
            : new SolidColorBrush((Avalonia.Media.Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString("#0E0E1A"));

        AutoOverlaysGlow.Opacity = enabled ? 0.12 : 0;
        AutoOverlaysCheck.IsVisible = enabled ? true : false;
    }

    // ── Finals mode toggle (settings tab) ──

    private void SettingsFinalsCard_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        SettingsFinalsToggle.IsChecked = SettingsFinalsToggle.IsChecked != true;
    }

    private void SettingsFinalsToggle_Changed(object? sender, RoutedEventArgs e)
    {
        _finalsMode = SettingsFinalsToggle.IsChecked == true;
        UpdateSettingsFinalsCardVisuals();
        ApplyFinalsModeToOutputs();
        SaveGameState();
        BroadcastWebState();
    }

    private void UpdateSettingsFinalsCardVisuals()
    {
        if (SettingsFinalsCard == null || SettingsFinalsGlow == null) return;

        bool on = SettingsFinalsToggle.IsChecked == true;

        SettingsFinalsCard.BorderBrush = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#D4A017")
            : (MediaColor)MediaColorConverter.ConvertFromString("#2A2020"));
        SettingsFinalsCard.BorderThickness = new Thickness(2);
        SettingsFinalsCard.Background = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#70521D")
            : (MediaColor)MediaColorConverter.ConvertFromString("#242428"));

        SettingsFinalsGlow.Opacity = 0;

        SettingsFinalsTitle.Foreground = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#D4A017")
            : (MediaColor)MediaColorConverter.ConvertFromString("#E1D2AF"));

        // Toggle track + thumb
        SettingsFinalsTrack.Background = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#2A2008")
            : (MediaColor)MediaColorConverter.ConvertFromString("#1A1610"));
        SettingsFinalsTrack.BorderBrush = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#D4A017")
            : (MediaColor)MediaColorConverter.ConvertFromString("#3A3018"));
        SettingsFinalsThumb.Background = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#D4A017")
            : (MediaColor)MediaColorConverter.ConvertFromString("#E1D2AF"));
        SettingsFinalsThumb.HorizontalAlignment = on
            ? Avalonia.Layout.HorizontalAlignment.Right
            : Avalonia.Layout.HorizontalAlignment.Left;
        SettingsFinalsThumb.Margin = on ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
    }

    // ── Hide team names toggle (settings tab) ──

    private bool _hideTeamNames;

    private void SettingsHideTeamNamesCard_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        SettingsHideTeamNamesToggle.IsChecked = SettingsHideTeamNamesToggle.IsChecked != true;
    }

    private void SettingsHideTeamNamesToggle_Changed(object? sender, RoutedEventArgs e)
    {
        _hideTeamNames = SettingsHideTeamNamesToggle.IsChecked == true;
        UpdateSettingsHideTeamNamesCardVisuals();
        _scorebug?.SetHideTeamNames(_hideTeamNames);
        _uxPreferences.HideTeamNames = _hideTeamNames;
        QueueUxPreferencesSave();
        BroadcastWebState();
    }

    private void UpdateSettingsHideTeamNamesCardVisuals()
    {
        if (SettingsHideTeamNamesCard == null) return;

        bool on = SettingsHideTeamNamesToggle.IsChecked == true;

        SettingsHideTeamNamesCard.BorderBrush = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#4A90D9")
            : (MediaColor)MediaColorConverter.ConvertFromString("#1C2030"));
        SettingsHideTeamNamesCard.BorderThickness = new Thickness(2);
        SettingsHideTeamNamesCard.Background = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#0A101C")
            : (MediaColor)MediaColorConverter.ConvertFromString("#080A12"));

        SettingsHideTeamNamesTitle.Foreground = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#7AB8E8")
            : (MediaColor)MediaColorConverter.ConvertFromString("#4A5A70"));

        SettingsHideTeamNamesTrack.Background = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#0A1828")
            : (MediaColor)MediaColorConverter.ConvertFromString("#131620"));
        SettingsHideTeamNamesTrack.BorderBrush = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#4A90D9")
            : (MediaColor)MediaColorConverter.ConvertFromString("#2A3040"));
        SettingsHideTeamNamesThumb.Background = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#4A90D9")
            : (MediaColor)MediaColorConverter.ConvertFromString("#3A4050"));
        SettingsHideTeamNamesThumb.HorizontalAlignment = on
            ? Avalonia.Layout.HorizontalAlignment.Right
            : Avalonia.Layout.HorizontalAlignment.Left;
        SettingsHideTeamNamesThumb.Margin = on ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
    }

    // ── Break screen toggle (settings tab) ──

    private bool _breakScreenEnabled = true;

    private void SettingsBreakScreenCard_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        SettingsBreakScreenToggle.IsChecked = SettingsBreakScreenToggle.IsChecked != true;
    }

    private void SettingsBreakScreenToggle_Changed(object? sender, RoutedEventArgs e)
    {
        _breakScreenEnabled = SettingsBreakScreenToggle.IsChecked == true;
        OperatorWorkflowPreferences.Update(data => data.AflBreakScreenEnabled = _breakScreenEnabled);
        UpdateSettingsBreakScreenCardVisuals();
    }

    private void UpdateSettingsBreakScreenCardVisuals()
    {
        if (SettingsBreakScreenCard == null || SettingsBreakScreenGlow == null) return;

        bool on = SettingsBreakScreenToggle.IsChecked == true;

        SettingsBreakScreenCard.BorderBrush = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#58A6FF")
            : (MediaColor)MediaColorConverter.ConvertFromString("#1A1A2A"));
        SettingsBreakScreenCard.BorderThickness = new Thickness(on ? 1 : 1);
        SettingsBreakScreenCard.Background = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#060A14")
            : (MediaColor)MediaColorConverter.ConvertFromString("#0A0A10"));

        SettingsBreakScreenGlow.Opacity = on ? 0.10 : 0;

        SettingsBreakScreenTitle.Foreground = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#58A6FF")
            : (MediaColor)MediaColorConverter.ConvertFromString("#2A3A50"));

        // Toggle track + thumb
        SettingsBreakScreenTrack.Background = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#0A1628")
            : (MediaColor)MediaColorConverter.ConvertFromString("#0E0E14"));
        SettingsBreakScreenTrack.BorderBrush = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#58A6FF")
            : (MediaColor)MediaColorConverter.ConvertFromString("#2A2A40"));
        SettingsBreakScreenThumb.Background = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#58A6FF")
            : (MediaColor)MediaColorConverter.ConvertFromString("#2A3A50"));
        SettingsBreakScreenThumb.HorizontalAlignment = on
            ? Avalonia.Layout.HorizontalAlignment.Right
            : Avalonia.Layout.HorizontalAlignment.Left;
        SettingsBreakScreenThumb.Margin = on ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
    }

    private void DeferScorePush(TimeSpan duration)
    {
        DateTime candidate = DateTime.UtcNow + duration;
        if (candidate > _deferScorePushUntilUtc)
        {
            _deferScorePushUntilUtc = candidate;
        }

        // Let the overlay scheduler know a goal animation is in progress
        _overlayScheduler.NotifyGoalAnimation(duration);
    }

    // ── Score-animation lifecycle helpers ──────────────────────────────────
    //
    // BeginScoreAnimation increments the in-flight counter and asks the
    // overlay scheduler to back off for the worst-case duration of the
    // current style. EndScoreAnimation is invoked from the storyboard /
    // goal-video Completed callback; it decrements the counter and, if any
    // follow-up scoring events were queued while this one was running,
    // drains the next one in FIFO order. PushAllToScoreboard checks
    // IsScoreAnimationActive (counter > 0) to know whether it's safe to
    // write the live score digits.

    private bool IsScoreAnimationActive => _activeScoreAnimations > 0;

    private int BeginScoreAnimation(TimeSpan worstCaseDuration)
    {
        int token = ++_scoreAnimationGeneration;
        _activeScoreAnimationToken = token;
        _activeScoreAnimations = 1;

        _scoreAnimationWatchdog?.Stop();
        _scoreAnimationWatchdog = new DispatcherTimer
        {
            // Storyboard and decoder callbacks are the normal completion path.
            // This is deliberately generous and exists only to recover from a
            // missing callback, unloaded control, or stalled media pipeline.
            Interval = worstCaseDuration + TimeSpan.FromSeconds(4)
        };
        _scoreAnimationWatchdog.Tick += (_, _) => RecoverTimedOutScoreAnimation(token);
        _scoreAnimationWatchdog.Start();

        if (worstCaseDuration > TimeSpan.Zero)
        {
            _overlayScheduler.NotifyGoalAnimation(worstCaseDuration);
        }
        return token;
    }

    private void EndScoreAnimation(int token)
    {
        // Ignore a late callback from an animation that was cancelled or
        // superseded. It must never complete the next queued score event.
        if (token != _activeScoreAnimationToken) return;

        _scoreAnimationWatchdog?.Stop();
        _scoreAnimationWatchdog = null;
        _activeScoreAnimationToken = 0;
        _activeScoreAnimations = 0;

        if (_pendingScoreAnimations.Count > 0)
        {
            // The model already contains every rapid click. Keep the
            // presentation on the event snapshot that just landed and
            // animate the next snapshot in FIFO order.
            Action next = _pendingScoreAnimations.Dequeue();
            try { next(); }
            catch
            {
                // A malformed optional animation must never strand later
                // scoring input. Continue draining on the dispatcher.
                Dispatcher.UIThread.Post(new Action(DrainScoreAnimationQueueAfterFailure));
            }
        }
        else
        {
            _scorebug?.SetScores(_match.HomeGoals, _match.HomeBehinds, _match.AwayGoals, _match.AwayBehinds);
            // MatchChanged already pushed team identity, colours, layout and
            // ticker state while the score animation was locked. Reapplying
            // the entire visual tree here caused the scorebug to remeasure at
            // the exact end of the final flash, producing a visible twitch.
            PushClockToScoreboard();
        }
    }

    private void RecoverTimedOutScoreAnimation(int token)
    {
        if (token != _activeScoreAnimationToken) return;

        Roche_Scoreboard.Services.VideoDiagnostics.Error("ScoreAnimation",
            "Animation completion watchdog expired; restoring the live score and continuing the queue.");
        _scoreAnimationWatchdog?.Stop();
        _scoreAnimationWatchdog = null;
        CancelGoalVideoPlayback();
        _scorebug?.CancelScoreAnimations();
        EndScoreAnimation(token);
    }

    private void DrainScoreAnimationQueueAfterFailure()
    {
        if (IsScoreAnimationActive) return;
        if (_pendingScoreAnimations.Count > 0)
        {
            Action next = _pendingScoreAnimations.Dequeue();
            try { next(); }
            catch { Dispatcher.UIThread.Post(new Action(DrainScoreAnimationQueueAfterFailure)); }
            return;
        }

        _scorebug?.SetScores(_match.HomeGoals, _match.HomeBehinds, _match.AwayGoals, _match.AwayBehinds);
        PushAllToScoreboard();
    }

    /// <summary>
    /// Cancels every queued/in-flight score animation. Called by Undo /
    /// reset / event editor paths so the new authoritative score lands
    /// immediately rather than waiting for a now-stale animation chain.
    /// </summary>
    private void ResetScoreAnimationQueue()
    {
        ++_scoreAnimationGeneration;
        _activeScoreAnimationToken = 0;
        _scoreAnimationWatchdog?.Stop();
        _scoreAnimationWatchdog = null;
        CancelGoalVideoPlayback();
        _scorebug?.CancelScoreAnimations();
        _pendingScoreAnimations.Clear();
        _activeScoreAnimations = 0;
        _deferScorePushUntilUtc = DateTime.MinValue;
    }

    private static TimeSpan GetGoalAnimationDuration(GoalAnimationStyle style)
    {
        return style switch
        {
            GoalAnimationStyle.Broadcast => TimeSpan.FromSeconds(3.5),
            GoalAnimationStyle.Electric => TimeSpan.FromSeconds(2.9),
            GoalAnimationStyle.Cinematic => TimeSpan.FromSeconds(5.0),
            GoalAnimationStyle.Clean => TimeSpan.FromSeconds(2.6),
            GoalAnimationStyle.Classic => TimeSpan.FromSeconds(2.4),
            _ => TimeSpan.FromSeconds(3.5)
        };
    }

    private Border CreateScoreLogBar(ScoreEvent ev)
    {
        bool isHome = ev.Team == TeamSide.Home;
        MediaColor teamColor = GetTeamColor(isHome);
        string teamName = isHome ? _match.HomeName : _match.AwayName;
        string type = ev.Type == ScoreType.Goal ? "GOAL" : "BEHIND";
        string clock = $"Q{ev.Quarter}  {(int)ev.GameTime.TotalMinutes:D2}:{ev.GameTime.Seconds:D2}";
        string points = ev.Type == ScoreType.Goal ? "+6" : "+1";

        MediaColor homeColor = GetTeamColor(true);
        MediaColor awayColor = GetTeamColor(false);

        // Colours
        // Make the whole event card belong to the scoring team, not just a
        // three-pixel stripe. The colour is mixed into a dark broadcast-safe
        // surface so even bright yellow/white club palettes retain contrast.
        MediaColor restBg = BlendMatchLogColor(MediaColor.FromRgb(0x0C, 0x13, 0x1D), teamColor, 0.30);
        MediaColor hoverBg = BlendMatchLogColor(MediaColor.FromRgb(0x10, 0x19, 0x25), teamColor, 0.42);
        MediaColor cardInk = SmartContrast.PickForeground(restBg, hoverBg);
        MediaColor restBorder = ContrastHelper.GetVisibleBorderColor(restBg, teamColor, cardInk);
        MediaColor hoverBorder = ContrastHelper.GetVisibleBorderColor(hoverBg, teamColor, cardInk);
        MediaColor typeColor = ev.Type == ScoreType.Goal
            ? MediaColor.FromRgb(0x3F, 0xB9, 0x50)
            : MediaColor.FromRgb(0xD2, 0x99, 0x22);
        MediaColor badgeBg = BlendMatchLogColor(restBg, typeColor, 0.22);
        MediaColor badgeInk = SmartContrast.PickReadableForeground(typeColor, badgeBg);
        MediaColor typeInk = SmartContrast.PickReadableForeground(typeColor, restBg, hoverBg);

        // ── Accent stripe (left edge, team colour) ──
        Border accentStripe = new NativeBorder()
        {
            Width = 3,
            Background = new SolidColorBrush(teamColor),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            CornerRadius = new CornerRadius(1.5)
        };

        // ── Points badge ──
        TextBlock pointsBadge = new()
        {
            Text = points,
            Foreground = new SolidColorBrush(badgeInk),
            FontSize = 10,
            FontWeight = FontWeight.Black,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Border pointsBorder = new NativeBorder()
        {
            Background = new SolidColorBrush(badgeBg),
            BorderBrush = new SolidColorBrush(ContrastHelper.GetVisibleBorderColor(badgeBg, typeColor, badgeInk)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1, 5, 1),
            Child = pointsBadge,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        // ── Header row: points badge + type + team name + clock ──
        TextBlock typeLabel = new()
        {
            Text = type,
            Foreground = new SolidColorBrush(typeInk),
            FontSize = 11,
            FontWeight = FontWeight.Black,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        TextBlock teamLabel = new()
        {
            Text = teamName.ToUpper(),
            Foreground = new SolidColorBrush(cardInk),
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        TextBlock clockLabel = new()
        {
            Text = clock,
            Foreground = new SolidColorBrush(cardInk),
            FontSize = 10,
            FontFamily = NativeFonts.Family("Consolas"),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };

        StackPanel headerLeft = new() { Orientation = Avalonia.Layout.Orientation.Horizontal };
        headerLeft.Children.Add(pointsBorder);
        headerLeft.Children.Add(typeLabel);
        headerLeft.Children.Add(teamLabel);

        Grid headerRow = new();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(headerLeft, 0);
        Grid.SetColumn(clockLabel, 1);
        headerRow.Children.Add(headerLeft);
        headerRow.Children.Add(clockLabel);

        // ── Scoreline row: HOME G.B.T — AWAY G.B.T ──
        var dimBrush = new SolidColorBrush(cardInk) { Opacity = 0.78 };
        var brightBrush = new SolidColorBrush(cardInk);

        TextBlock homeNameScore = new()
        {
            Text = _match.HomeName.ToUpper(),
            Foreground = isHome ? brightBrush : dimBrush,
            FontSize = 10,
            FontWeight = isHome ? FontWeight.Bold : FontWeight.Normal,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };

        TextBlock homeScoreLabel = new()
        {
            Text = $"{ev.HomeGoals}.{ev.HomeBehinds}.{ev.HomeTotal}",
            Foreground = isHome ? brightBrush : dimBrush,
            FontSize = 11,
            FontWeight = isHome ? FontWeight.ExtraBold : FontWeight.Normal,
            FontFamily = NativeFonts.Family("Bahnschrift"),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        TextBlock separator = new()
        {
            Text = "–",
            Foreground = new SolidColorBrush(cardInk) { Opacity = 0.65 },
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0)
        };

        TextBlock awayScoreLabel = new()
        {
            Text = $"{ev.AwayGoals}.{ev.AwayBehinds}.{ev.AwayTotal}",
            Foreground = !isHome ? brightBrush : dimBrush,
            FontSize = 11,
            FontWeight = !isHome ? FontWeight.ExtraBold : FontWeight.Normal,
            FontFamily = NativeFonts.Family("Bahnschrift"),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        TextBlock awayNameScore = new()
        {
            Text = _match.AwayName.ToUpper(),
            Foreground = !isHome ? brightBrush : dimBrush,
            FontSize = 10,
            FontWeight = !isHome ? FontWeight.Bold : FontWeight.Normal,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };

        StackPanel scoreLine = new()
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 0)
        };
        scoreLine.Children.Add(homeNameScore);
        scoreLine.Children.Add(homeScoreLabel);
        scoreLine.Children.Add(separator);
        scoreLine.Children.Add(awayScoreLabel);
        scoreLine.Children.Add(awayNameScore);

        // ── Edit hint (hidden by default, revealed on hover with panel expand) ──
        TextBlock editHint = new()
        {
            Text = "✎ Click to edit",
            Foreground = new SolidColorBrush(cardInk),
            FontSize = 9,
            FontWeight = FontWeight.SemiBold,
            Opacity = 0.0,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };

        Border editHintBar = new NativeBorder()
        {
            Background = new SolidColorBrush(MediaColor.FromArgb(0x00, 0x81, 0x8C, 0xF8)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x18, 0x81, 0x8C, 0xF8)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            CornerRadius = new CornerRadius(0, 0, 4, 4),
            Padding = new Thickness(0, 3, 0, 3),
            Margin = new Thickness(-2, 4, -2, -2),
            Child = editHint,
            MaxHeight = 22,
            Height = 0,
            ClipToBounds = true
        };

        // ── Assemble content ──
        StackPanel content = new() { Margin = new Thickness(10, 0, 0, 0) };
        content.Children.Add(headerRow);
        content.Children.Add(scoreLine);
        content.Children.Add(editHintBar);

        Grid inner = new();
        inner.Children.Add(accentStripe);
        inner.Children.Add(content);

        Border bar = new NativeBorder()
        {
            Background = new SolidColorBrush(restBg),
            BorderBrush = new SolidColorBrush(restBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(6, 6, 8, 6),
            Child = inner,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Tag = ev,
            ToolTip = "Click to edit this score event"
        };

        // ── Hover animations ──
        var bgRest = new SolidColorBrush(restBg);
        var bgHover = new SolidColorBrush(hoverBg);
        var borderRest = new SolidColorBrush(restBorder);
        var borderHover = new SolidColorBrush(hoverBorder);

        bar.PointerEntered += (_, _) =>
        {
            bar.Background = bgHover;
            bar.BorderBrush = borderHover;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            editHint.BeginAnimation(Control.OpacityProperty, fadeIn);
            editHintBar.BeginAnimation(Control.HeightProperty,
                new DoubleAnimation(editHintBar.NativeHeight(), 22, TimeSpan.FromMilliseconds(180))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });

            // Pop-out the edit hint bar background
            var bgAnim = new ColorAnimation(
                MediaColor.FromArgb(0x10, 0x81, 0x8C, 0xF8),
                TimeSpan.FromMilliseconds(200));
            ((SolidColorBrush)editHintBar.Background).BeginAnimation(SolidColorBrush.ColorProperty, bgAnim);

            var borderAnim = new ColorAnimation(
                MediaColor.FromArgb(0x30, 0x81, 0x8C, 0xF8),
                TimeSpan.FromMilliseconds(200));
            ((SolidColorBrush)editHintBar.BorderBrush).BeginAnimation(SolidColorBrush.ColorProperty, borderAnim);
        };

        bar.PointerExited += (_, _) =>
        {
            bar.Background = bgRest;
            bar.BorderBrush = borderRest;

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            editHint.BeginAnimation(Control.OpacityProperty, fadeOut);
            editHintBar.BeginAnimation(Control.HeightProperty,
                new DoubleAnimation(editHintBar.NativeHeight(), 0, TimeSpan.FromMilliseconds(160))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });

            // Fade out the edit hint bar background
            var bgAnim = new ColorAnimation(
                MediaColor.FromArgb(0x00, 0x81, 0x8C, 0xF8),
                TimeSpan.FromMilliseconds(200));
            ((SolidColorBrush)editHintBar.Background).BeginAnimation(SolidColorBrush.ColorProperty, bgAnim);

            var borderAnim = new ColorAnimation(
                MediaColor.FromArgb(0x18, 0x81, 0x8C, 0xF8),
                TimeSpan.FromMilliseconds(200));
            ((SolidColorBrush)editHintBar.BorderBrush).BeginAnimation(SolidColorBrush.ColorProperty, borderAnim);
        };

        bar.PointerReleased += ScoreLogBar_Click;
        return bar;
    }

    private static MediaColor BlendMatchLogColor(MediaColor surface, MediaColor team, double teamWeight)
    {
        teamWeight = Math.Clamp(teamWeight, 0, 1);
        double surfaceWeight = 1 - teamWeight;
        return MediaColor.FromRgb(
            (byte)Math.Round(surface.R * surfaceWeight + team.R * teamWeight),
            (byte)Math.Round(surface.G * surfaceWeight + team.G * teamWeight),
            (byte)Math.Round(surface.B * surfaceWeight + team.B * teamWeight));
    }

    /// <summary>
    /// Creates a styled separator bar for the match log that marks the start of a quarter.
    /// </summary>
    private static Border CreateQuarterSplitBar(int quarter)
    {
        string label = quarter switch
        {
            1 => "Q1",
            2 => "Q2",
            3 => "Q3",
            4 => "Q4",
            _ => $"Q{quarter}"
        };

        TextBlock text = new()
        {
            Text = label,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0x81, 0x8C, 0xF8)),
            FontSize = 10,
            FontWeight = FontWeight.Black,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        Border badge = new NativeBorder()
        {
            Background = new SolidColorBrush(MediaColor.FromArgb(0x18, 0x81, 0x8C, 0xF8)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x30, 0x81, 0x8C, 0xF8)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 2, 8, 2),
            Child = text
        };

        Grid content = new();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var lineBrush = new SolidColorBrush(MediaColor.FromRgb(0x21, 0x26, 0x2D));
        Border leftLine = new NativeBorder() { Height = 1, Background = lineBrush, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        Border rightLine = new NativeBorder() { Height = 1, Background = lineBrush, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };

        Grid.SetColumn(leftLine, 0);
        Grid.SetColumn(badge, 1);
        Grid.SetColumn(rightLine, 2);

        badge.Margin = new Thickness(8, 0, 8, 0);

        content.Children.Add(leftLine);
        content.Children.Add(badge);
        content.Children.Add(rightLine);

        return new NativeBorder
        {
            Margin = new Thickness(0, 8, 0, 8),
            Child = content
        };
    }

    private void RefreshVisibleScreens()
    {
        if (_displayWindow == null || !_displayWindow.IsVisible) return;

        if (_quarterBreakScorebug?.IsVisible == true)
        {
            PopulateQuarterBreakScorebug();
        }

        if (_breakScreen?.IsVisible == true)
        {
            PopulateBreakScreen();
        }

        if (_scoreworm?.IsVisible == true)
        {
            PopulateScoreworm();
        }

        if (_statsScreen?.IsVisible == true)
        {
            PopulateStatsScreen();
        }

        if (_weatherScreen?.IsVisible == true)
        {
            PopulateWeatherScreen();
        }
    }

    private void StartPause_Click(object? sender, RoutedEventArgs e)
    {
        EnsureScoreboardWindow();

        bool shouldControlStoppageCountdownOnly = _match.ClockMode == ClockMode.CountUp
            && _showControlPanelCountdown
            && _match.ClockRunning
            && _match.ElapsedInQuarter.TotalSeconds >= 2;

        if (shouldControlStoppageCountdownOnly)
        {
            ToggleControlPanelCountdown();
            UpdateLiveUI();
            return;
        }

        if (_match.ClockRunning)
        {
            _match.PauseClock();
            PauseControlPanelCountdown(forceAutoPausedFlag: false);
            UpdateLiveUI();
            return;
        }

        _controlPanelCountdownAutoPausedClock = false;

        bool startingQuarter = !_match.ClockRunning && _match.ElapsedInQuarter.TotalSeconds < 2;

        if (_showingBreakScreen || _isBreakRotating)
        {
            StopBreakRotation();
            if (startingQuarter && _scorebug is not null)
                StartQuarterFoldTransition(_scorebug, "Live Scorebug");
            else
                ShowScorebug();
            _scorebug?.ClearBreakGBLock();
            _scorebug?.ResumeScorelessFromBreak();
        }

        if (_breakScreenDelayTimer != null)
        {
            _breakScreenDelayTimer.Stop();
            _breakScreenDelayTimer = null;
            _scorebug?.ClearBreakGBLock();
            _scorebug?.ResumeScorelessFromBreak();
        }

        if (startingQuarter)
        {
            _overlayScheduler.NotifyQuarterStart();
            _scorebug?.SetQuarter(_match.Quarter);

            if (_match.ClockMode == ClockMode.CountUp && _showControlPanelCountdown)
            {
                if (_controlPanelCountdownRemaining == TimeSpan.Zero)
                {
                    _controlPanelCountdownRemaining = _controlPanelCountdownDuration;
                }

                _controlPanelCountdownRunning = true;
                _countdownNextTick = DateTime.UtcNow.AddSeconds(1);
            }
        }

        _match.StartClock();
        UpdateLiveUI();
    }

    private void ControlPanelCountdownStartPauseButton_Click(object? sender, RoutedEventArgs e)
    {
        ToggleControlPanelCountdown();
        UpdateLiveUI();
    }

    private void ToggleControlPanelCountdown()
    {
        if (!_showControlPanelCountdown || _match.ClockMode != ClockMode.CountUp)
        {
            return;
        }

        if (_controlPanelCountdownRemaining == TimeSpan.Zero)
        {
            _controlPanelCountdownRemaining = _controlPanelCountdownDuration;
            _controlPanelCountdownAutoPausedClock = false;
        }

        bool starting = !_controlPanelCountdownRunning;
        if (starting)
        {
            _controlPanelCountdownRunning = true;
            _countdownNextTick = DateTime.UtcNow.AddSeconds(1);
            return;
        }

        PauseControlPanelCountdown(forceAutoPausedFlag: false);
    }

    private void PauseControlPanelCountdown(bool forceAutoPausedFlag)
    {
        _controlPanelCountdownRunning = false;
        _countdownNextTick = DateTime.MinValue;
        if (forceAutoPausedFlag)
            _controlPanelCountdownAutoPausedClock = true;
    }

    private void ControlPanelCountdownResetButton_Click(object? sender, RoutedEventArgs e)
    {
        PauseControlPanelCountdown(forceAutoPausedFlag: false);
        _controlPanelCountdownRemaining = _controlPanelCountdownDuration;
        _controlPanelCountdownAutoPausedClock = false;
        UpdateControlPanelCountdownUi();
    }

    private void ControlPanelCountdownMinusMinuteButton_Click(object? sender, RoutedEventArgs e)
        => AdjustControlPanelCountdown(TimeSpan.FromMinutes(-1));

    private void ControlPanelCountdownMinus10Button_Click(object? sender, RoutedEventArgs e)
        => AdjustControlPanelCountdown(TimeSpan.FromSeconds(-10));

    private void ControlPanelCountdownPlus10Button_Click(object? sender, RoutedEventArgs e)
        => AdjustControlPanelCountdown(TimeSpan.FromSeconds(10));

    private void ControlPanelCountdownPlusMinuteButton_Click(object? sender, RoutedEventArgs e)
        => AdjustControlPanelCountdown(TimeSpan.FromMinutes(1));

    private void AdjustControlPanelCountdown(TimeSpan delta)
    {
        // Adjust the live value in place. A running stoppage clock remains running,
        // and the main scoreboard clock is deliberately never touched.
        _controlPanelCountdownRemaining = CalculateAdjustedStoppageTime(
            _controlPanelCountdownRemaining, delta);
        if (_controlPanelCountdownRemaining > TimeSpan.Zero)
        {
            _controlPanelCountdownDuration = _controlPanelCountdownRemaining;
            _controlPanelCountdownAutoPausedClock = false;
            if (_controlPanelCountdownRunning)
                _countdownNextTick = DateTime.UtcNow.AddSeconds(1);
        }
        else
        {
            PauseControlPanelCountdown(forceAutoPausedFlag: false);
        }

        UpdateControlPanelCountdownUi();
    }

    internal static TimeSpan CalculateAdjustedStoppageTime(TimeSpan current, TimeSpan delta)
    {
        double seconds = Math.Clamp(current.TotalSeconds + delta.TotalSeconds, 0, (99 * 60) + 59);
        return TimeSpan.FromSeconds(seconds);
    }

    internal static bool TryParseStoppageTime(string? minutesText, string? secondsText, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        int minutes = 0;
        int seconds = 0;
        bool valid = int.TryParse(minutesText, out minutes)
                     && int.TryParse(secondsText, out seconds)
                     && minutes is >= 0 and <= 99
                     && seconds is >= 0 and <= 59
                     && (minutes > 0 || seconds > 0);
        if (!valid) return false;
        value = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        return true;
    }

    private int _countdownEditorGeneration;

    private void ControlPanelCountdownEditButton_Click(object? sender, RoutedEventArgs e)
    {
        _countdownEditorGeneration++;
        var editPopup = this.FindName("ControlPanelCountdownEditPopup") as Avalonia.Controls.Border;
        var popupScale = this.FindName("ControlPanelCountdownEditPopupScale") as ScaleTransform;
        var editMinutesBox = this.FindName("ControlPanelCountdownEditMinutesBox") as Avalonia.Controls.TextBox;
        var editSecondsBox = this.FindName("ControlPanelCountdownEditSecondsBox") as Avalonia.Controls.TextBox;
        var editErrorText = this.FindName("ControlPanelCountdownEditErrorText") as Avalonia.Controls.TextBlock;

        if (editPopup is null)
            return;

        if (editMinutesBox is not null)
            editMinutesBox.Text = ((int)_controlPanelCountdownRemaining.TotalMinutes).ToString("D2");

        if (editSecondsBox is not null)
            editSecondsBox.Text = _controlPanelCountdownRemaining.Seconds.ToString("D2");

        if (editErrorText is not null)
        {
            editErrorText.Text = string.Empty;
            editErrorText.IsVisible = false;
        }

        editPopup.BeginAnimation(Control.OpacityProperty, null);
        popupScale?.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        popupScale?.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        var editPopupHost = this.FindName("ControlPanelCountdownEditPopupHost") as Avalonia.Controls.Primitives.Popup;
        if (editPopupHost is not null)
            editPopupHost.IsOpen = true;

        editPopup.IsVisible = true;
        editPopup.Opacity = 0;
        if (popupScale is not null)
        {
            popupScale.ScaleX = 0.96;
            popupScale.ScaleY = 0.96;
        }

        editPopup.BeginAnimation(Control.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });

        if (popupScale is not null)
        {
            popupScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            popupScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }

        editMinutesBox?.Focus();
        editMinutesBox?.SelectAll();
    }

    private void ControlPanelCountdownCancelEditButton_Click(object? sender, RoutedEventArgs e)
    {
        CloseControlPanelCountdownEditPopup();
    }

    private void ControlPanelCountdownApplyButton_Click(object? sender, RoutedEventArgs e)
    {
        var editPopup = this.FindName("ControlPanelCountdownEditPopup") as Avalonia.Controls.Border;
        var editMinutesBox = this.FindName("ControlPanelCountdownEditMinutesBox") as Avalonia.Controls.TextBox;
        var editSecondsBox = this.FindName("ControlPanelCountdownEditSecondsBox") as Avalonia.Controls.TextBox;
        var editErrorText = this.FindName("ControlPanelCountdownEditErrorText") as Avalonia.Controls.TextBlock;
        if (editMinutesBox is null || editSecondsBox is null)
            return;

        if (!TryParseStoppageTime((editMinutesBox.Text ?? string.Empty), (editSecondsBox.Text ?? string.Empty), out TimeSpan editedDuration))
        {
            if (editErrorText is not null)
            {
                editErrorText.Text = "Enter a time from 00:01 to 99:59.";
                editErrorText.IsVisible = true;
            }
            return;
        }

        // Preserve the run/pause state. This is a live correction of the independent
        // stoppage clock, never an implicit pause and never a match-clock edit.
        _controlPanelCountdownDuration = editedDuration;
        _controlPanelCountdownRemaining = editedDuration;
        _controlPanelCountdownAutoPausedClock = false;
        if (_controlPanelCountdownRunning)
            _countdownNextTick = DateTime.UtcNow.AddSeconds(1);

        _countdownEditorGeneration++;
        if (editPopup is not null)
            editPopup.IsVisible = false;

        var editPopupHost = this.FindName("ControlPanelCountdownEditPopupHost") as Avalonia.Controls.Primitives.Popup;
        if (editPopupHost is not null)
            editPopupHost.IsOpen = false;

        UpdateControlPanelCountdownUi();
    }

    private void ControlPanelCountdownEditBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if ((e.Key == Avalonia.Input.Key.Up || e.Key == Avalonia.Input.Key.Down)
            && sender is Avalonia.Controls.TextBox field)
        {
            int limit = field == ControlPanelCountdownEditMinutesBox ? 99 : 59;
            int step = (NativeKeyboard.Modifiers & Avalonia.Input.KeyModifiers.Shift) != 0 ? 10 : 1;
            if (e.Key == Avalonia.Input.Key.Down) step = -step;
            field.Text = AdjustStoppageEditorField((field.Text ?? string.Empty), step, limit);
            field.SelectAll();
            // This only edits the draft. Nothing reaches the live clock until Apply.
            e.Handled = true;
        }
        else if (e.Key == Avalonia.Input.Key.Enter)
        {
            ControlPanelCountdownApplyButton_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Avalonia.Input.Key.Escape)
        {
            CloseControlPanelCountdownEditPopup();
            e.Handled = true;
        }
    }

    internal static string AdjustStoppageEditorField(string? text, int step, int limit)
    {
        int.TryParse(text, out int current);
        return Math.Clamp((long)current + step, 0, limit).ToString("D2");
    }

    private void CloseControlPanelCountdownEditPopup()
    {
        var editPopup = this.FindName("ControlPanelCountdownEditPopup") as Avalonia.Controls.Border;
        var popupScale = this.FindName("ControlPanelCountdownEditPopupScale") as ScaleTransform;
        var cancelEditButton = this.FindName("ControlPanelCountdownCancelEditButton") as Avalonia.Controls.Button;
        var editErrorText = this.FindName("ControlPanelCountdownEditErrorText") as Avalonia.Controls.TextBlock;

        if (editPopup is null || editPopup.IsVisible != true)
            return;

        int generation = ++_countdownEditorGeneration;
        // Capture the rendered values before removing opening animations: their
        // base values are still zero/.96 and otherwise make Cancel snap shut.
        double visibleOpacity = editPopup.Opacity;
        double visibleScale = popupScale?.ScaleX ?? 1;
        editPopup.BeginAnimation(Control.OpacityProperty, null);
        popupScale?.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        popupScale?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        editPopup.Opacity = visibleOpacity;
        if (popupScale is not null)
            popupScale.ScaleX = popupScale.ScaleY = visibleScale;

        var fade = new DoubleAnimation(editPopup.Opacity, 0, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) =>
        {
            if (generation != _countdownEditorGeneration) return;
            editPopup.IsVisible = false;
            editPopup.Opacity = 0;
            var popupHostOnClose = this.FindName("ControlPanelCountdownEditPopupHost") as Avalonia.Controls.Primitives.Popup;
            if (popupHostOnClose is not null)
                popupHostOnClose.IsOpen = false;
            if (popupScale is not null)
            {
                popupScale.ScaleX = 0.96;
                popupScale.ScaleY = 0.96;
            }
        };
        editPopup.BeginAnimation(Control.OpacityProperty, fade);

        if (popupScale is not null)
        {
            popupScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(popupScale.ScaleX, 0.96, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
            popupScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(popupScale.ScaleY, 0.96, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
        }

        if (cancelEditButton is not null)
            cancelEditButton.IsVisible = false;
        if (editErrorText is not null)
        {
            editErrorText.Text = string.Empty;
            editErrorText.IsVisible = false;
        }
    }

    private void HomeGoal_Click(object? sender, RoutedEventArgs e)
    {
        EnsureScoreboardWindow();
        ApplyScoreImmediately(TeamSide.Home, ScoreType.Goal);
    }

    private void HomeBehind_Click(object? sender, RoutedEventArgs e)
    {
        EnsureScoreboardWindow();
        ApplyScoreImmediately(TeamSide.Home, ScoreType.Behind);
    }

    private void AwayGoal_Click(object? sender, RoutedEventArgs e)
    {
        EnsureScoreboardWindow();
        ApplyScoreImmediately(TeamSide.Away, ScoreType.Goal);
    }

    /// <summary>
    /// Applies the score to the match model immediately, then serialises only
    /// its visual presentation. The operator can therefore enter a rapid burst
    /// without waiting for a prior goal package, while the output still shows
    /// each event snapshot in the correct order.
    /// </summary>
    private void ApplyScoreImmediately(TeamSide team, ScoreType type)
    {
        bool isHome = team == TeamSide.Home;
        GoalAnimationStyle style = _scorebug?.GetGoalAnimationStyle() ?? GoalAnimationStyle.Broadcast;
        if (style == GoalAnimationStyle.CustomVideo && !BothTeamsHaveGoalVideos())
            style = GoalAnimationStyle.Broadcast;

        // Lock this side before MatchChanged fires so the model/UI updates at
        // once without skipping the event's output animation snapshot.
        _scorebug?.LockScoreUpdate(isHome);
        ScoreEvent score = ApplyScoreToModel(_match, team, type);

        Action animation = () => PlayAppliedScoreAnimation(score, isHome, type, style);
        if (_scorebug?.DeferScoreUntilTeamPanelsVisible(isHome, () => QueueScoreAnimation(animation)) == true)
            return;

        QueueScoreAnimation(animation);
    }

    internal static ScoreEvent ApplyScoreToModel(MatchManager match, TeamSide team, ScoreType type)
        => type == ScoreType.Goal ? match.AddGoal(team) : match.AddBehind(team);

    private void QueueScoreAnimation(Action animation)
    {
        if (IsScoreAnimationActive)
        {
            // Protect the live output from an unbounded visual backlog during
            // accidental double-clicks, remote retries, or a very rapid burst.
            // Match state has already updated immediately; preserve the newest
            // snapshot so the display catches up instead of animating stale
            // scores for tens of seconds.
            if (_pendingScoreAnimations.Count >= MaxPendingScoreAnimations)
            {
                _pendingScoreAnimations.Clear();
                Roche_Scoreboard.Services.VideoDiagnostics.Info("ScoreAnimation",
                    "Visual queue reached its safety limit; coalescing to the latest live score.");
            }
            _pendingScoreAnimations.Enqueue(animation);
        }
        else
            animation();
    }

    private void PlayAppliedScoreAnimation(ScoreEvent score, bool isHome, ScoreType type, GoalAnimationStyle style)
    {
        if (type == ScoreType.Behind)
        {
            int token = BeginScoreAnimation(TimeSpan.FromSeconds(2));
            if (_scorebug is null)
                EndScoreAnimation(token);
            else
                _scorebug.PlayScoreAnimation(isHome, false,
                    isHome ? score.HomeGoals : score.AwayGoals,
                    isHome ? score.HomeBehinds : score.AwayBehinds,
                    isHome ? score.HomeTotal : score.AwayTotal,
                    () => EndScoreAnimation(token));
            return;
        }

        TimeSpan expectedDuration = style == GoalAnimationStyle.CustomVideo
            ? TimeSpan.FromSeconds(45)
            : GetGoalAnimationDuration(style);
        int goalToken = BeginScoreAnimation(expectedDuration);
        if (style == GoalAnimationStyle.CustomVideo)
        {
            void afterVideo()
            {
                if (_scorebug is null)
                {
                    EndScoreAnimation(goalToken);
                    return;
                }
                _scorebug.PlayScoreFlipOnly(isHome,
                    isHome ? score.HomeGoals : score.AwayGoals,
                    isHome ? score.HomeBehinds : score.AwayBehinds,
                    isHome ? score.HomeTotal : score.AwayTotal,
                    () => EndScoreAnimation(goalToken));
            }
            PlayCustomGoalVideoIfConfigured(isHome, afterVideo);
            return;
        }

        GoalAnimationStyle previous = _scorebug?.GetGoalAnimationStyle() ?? style;
        if (previous != style) _scorebug?.SetGoalAnimationStyle(style);
        if (_scorebug is null)
            EndScoreAnimation(goalToken);
        else
            _scorebug.PlayScoreAnimation(isHome, true,
                isHome ? score.HomeGoals : score.AwayGoals,
                isHome ? score.HomeBehinds : score.AwayBehinds,
                isHome ? score.HomeTotal : score.AwayTotal,
                () => EndScoreAnimation(goalToken));
        if (previous != style) _scorebug?.SetGoalAnimationStyle(previous);
    }

    private bool BothTeamsHaveGoalVideos()
    {
        return !string.IsNullOrWhiteSpace(_homeGoalVideoPath)
            && System.IO.File.Exists(_homeGoalVideoPath)
            && !string.IsNullOrWhiteSpace(_awayGoalVideoPath)
            && System.IO.File.Exists(_awayGoalVideoPath);
    }

    private void AwayBehind_Click(object? sender, RoutedEventArgs e)
    {
        EnsureScoreboardWindow();
        ApplyScoreImmediately(TeamSide.Away, ScoreType.Behind);
    }

    private void Undo_Click(object? sender, RoutedEventArgs e)
    {
        int oldHomeGoals = _match.HomeGoals;
        int oldHomeBehinds = _match.HomeBehinds;
        int oldAwayGoals = _match.AwayGoals;
        int oldAwayBehinds = _match.AwayBehinds;

        if (_match.UndoLastScore())
        {
            // Clear any pending score animation defer so undo shows immediately
            ResetScoreAnimationQueue();
            _scorebug?.ResetAllOverlayState();
            _scorebug?.AnimateScoreCorrectionFrom(
                oldHomeGoals,
                oldHomeBehinds,
                oldAwayGoals,
                oldAwayBehinds,
                _match.HomeGoals,
                _match.HomeBehinds,
                _match.AwayGoals,
                _match.AwayBehinds);

            _previousLeadChanges = MatchStats.Calculate(_match, _match.Quarter, _match.ElapsedInQuarter).LeadChanges;

            RebuildMatchLog();

            // Reset the last-event text when all events are undone
            if (_match.Events.Count == 0)
                LastEventText.Text = string.Empty;

            UpdateLiveUI();
            PushAllToScoreboard();
            RefreshVisibleScreens();
        }
    }

    private void ScoreLogBar_Click(object? sender, PointerEventArgs e)
    {
        if (sender is not Border { Tag: ScoreEvent ev }) return;

        // Resolve live index at click time — safe even after prior edits
        int eventIndex = -1;
        for (int i = 0; i < _match.Events.Count; i++)
        {
            if (ReferenceEquals(_match.Events[i], ev)) { eventIndex = i; break; }
        }
        if (eventIndex < 0) return;

        ScoreEventEditorWindow editor = new(ev, _match.HomeName, _match.AwayName, (HomeColorBox.Text ?? string.Empty), (AwayColorBox.Text ?? string.Empty))
        {
            Owner = this
        };
        editor.ShowDialog();

        bool changed = editor.Result switch
        {
            ScoreEventEditorWindow.EditorResult.Deleted => _match.RemoveEvent(eventIndex),
            ScoreEventEditorWindow.EditorResult.Saved => _match.ModifyEvent(eventIndex, editor.SelectedTeam, editor.SelectedType, editor.SelectedGameTime, editor.SelectedQuarter),
            _ => false
        };

        if (!changed) return;

        // Run score validation after the edit
        var warnings = _match.ValidateScoreSequence();
        if (warnings.Count > 0)
        {
            string warningText = string.Join("\n", warnings.Select(w => w.Message));
            RocheScoreboard.Mac.Native.NativeMessageBox.Show(
                $"Score validation detected {warnings.Count} issue(s):\n\n{warningText}",
                "Score Validation Warning",
                NativeMessageBoxButton.OK,
                NativeMessageBoxImage.Warning);
        }

        ResetScoreAnimationQueue();
        _scorebug?.ResetAllOverlayState();
        // Historical edits are applied atomically. Do not run a live-score
        // animation over the rebuilt timeline or overlays can briefly observe
        // old and new event ordering at the same time.
        ResetPeriodicOverlayTracking();

        _previousLeadChanges = MatchStats.Calculate(_match, _match.Quarter, _match.ElapsedInQuarter).LeadChanges;

        RebuildMatchLog();
        UpdateLiveUI();
        PushAllToScoreboard();
        RefreshVisibleScreens();
    }

    private void AddMatchLogScore_Click(object? sender, RoutedEventArgs e)
    {
        var editor = new ScoreEventEditorWindow(
            _match.HomeName, _match.AwayName, _match.Quarter, _match.ElapsedInQuarter,
            (HomeColorBox.Text ?? string.Empty), (AwayColorBox.Text ?? string.Empty))
        {
            Owner = this
        };
        editor.ShowDialog();
        if (editor.Result != ScoreEventEditorWindow.EditorResult.Saved) return;

        ResetScoreAnimationQueue();
        _scorebug?.ResetAllOverlayState();
        ResetPeriodicOverlayTracking();
        _match.AddRecordedScore(editor.SelectedTeam, editor.SelectedType,
            editor.SelectedQuarter, editor.SelectedGameTime);
        _previousLeadChanges = MatchStats.Calculate(_match, _match.Quarter, _match.ElapsedInQuarter).LeadChanges;
        RebuildMatchLog();
        UpdateLiveUI();
        PushAllToScoreboard();
        RefreshVisibleScreens();
    }

    private void EndQuarter_Click(object? sender, RoutedEventArgs e)
    {
        if (!_match.EndQuarter()) return;

        _controlPanelCountdownRunning = false;
        _controlPanelCountdownRemaining = _controlPanelCountdownDuration;
        _controlPanelCountdownAutoPausedClock = false;
        _fiveMinWarningShown = false;
        _previousRemainingTime = TimeSpan.MaxValue;
        _scorebug?.PauseScorelessForBreak();

        // Dismiss the 5-minute warning if it was active
        _scorebug?.HideFiveMinuteWarning();

        // Dismiss any active overlays and cancel pending auto-overlay timers
        _scorebug?.ResetAllOverlayState();
        ResetPeriodicOverlayTracking();

        UpdateLiveUI();

        // Animate the quarter tile label to the break abbreviation (Q1→QT, Q2→HT, etc.)
        int endedQuarter = GetEndedQuarter();
        _scorebug?.AnimateQuarterToBreakLabel(endedQuarter);

        // Permanently show G/B columns for the duration of the break (Classic layout only)
        _scorebug?.ExpandGBColumnsForBreak();

        if (_breakScreenEnabled)
        {
            // The score-first break surface appears immediately through the
            // panel-fold transition. Detailed break presentations deliberately
            // wait so spectators can absorb the quarter result first.
            ShowQuarterBreakScorebug();
            _breakScreenDelayTimer?.Stop();
            _breakScreenDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
            _breakScreenDelayTimer.Tick += (_, _) =>
            {
                _breakScreenDelayTimer.Stop();
                _breakScreenDelayTimer = null;
                ShowBreakScreenRotation();
                StartBreakRotation();
            };
            _breakScreenDelayTimer.Start();
        }
    }

    private void ContinueQuarter_Click(object? sender, RoutedEventArgs e)
    {
        if (!_match.CanContinueQuarter) return;

        _controlPanelCountdownRunning = false;
        _controlPanelCountdownAutoPausedClock = false;

        int resumeQ = _match.Quarter - 1;
        NativeMessageBoxResult confirm = RocheScoreboard.Mac.Native.NativeMessageBox.Show(
            $"This will resume Quarter {resumeQ} from where it left off.\n\nThe next quarter will not start.",
            $"Resume Q{resumeQ}",
            NativeMessageBoxButton.OKCancel,
            NativeMessageBoxImage.Information);

        if (confirm != NativeMessageBoxResult.OK) return;

        if (!_match.ContinueQuarter()) return;

        // Cancel any pending break screen
        if (_breakScreenDelayTimer != null)
        {
            _breakScreenDelayTimer.Stop();
            _breakScreenDelayTimer = null;
        }

        if (_showingBreakScreen || _isBreakRotating)
        {
            StopBreakRotation();
            if (_scorebug is not null)
                StartQuarterFoldTransition(_scorebug, "Live Scorebug");
        }

        _scorebug?.ClearBreakGBLock();
        _scorebug?.ResumeScorelessFromBreak();
        _scorebug?.SetQuarter(_match.Quarter);

        UpdateLiveUI();
    }

    private void RebuildMatchLog()
    {
        if (EventList == null) return;

        EventList.Children.Clear();

        // Events are iterated oldest→newest and inserted at position 0,
        // so the visual order is newest-first. Insert quarter splits when
        // the quarter changes between consecutive events.
        int lastQuarter = -1;
        foreach (var ev in _match.Events)
        {
            if (lastQuarter != -1 && ev.Quarter != lastQuarter)
            {
                // Quarter boundary — insert a separator for the NEW quarter.
                // Because we Insert(0, ...), this separator ends up below the
                // events of the new quarter and above the events of the old one.
                EventList.Children.Insert(0, CreateQuarterSplitBar(ev.Quarter));
            }

            EventList.Children.Insert(0, CreateScoreLogBar(ev));
            lastQuarter = ev.Quarter;
        }

        // Update header count badge and empty-state hint
        int count = _match.Events.Count;
        if (MatchLogCountBadge is not null)
            MatchLogCountBadge.Text = count > 0 ? $"({count})" : "";
        if (MatchLogEmptyHint is not null)
            MatchLogEmptyHint.IsVisible = count == 0 ? true : false;
    }

    private void UpdateClockStatus()
    {
        var dc = _match.DisplayClock;
        ClockStatusText.Text = $"{(_match.ClockMode == ClockMode.Countdown ? "Countdown" : "Count Up")} · {(_match.ClockRunning ? "Running" : "Stopped")} · {(int)dc.TotalMinutes:D2}:{dc.Seconds:D2}";
        UpdateControlPanelCountdownUi();
    }

    private void UpdateControlPanelCountdownUi()
    {
        if (ControlPanelCountdownPanel is null)
        {
            return;
        }

        bool visible = _match.ClockMode == ClockMode.CountUp && _showControlPanelCountdown;
        ControlPanelCountdownPanel.IsVisible = visible ? true : false;
        if (!visible)
        {
            CloseControlPanelCountdownEditPopup();
            return;
        }

        ControlPanelCountdownText.Text = $"{(int)_controlPanelCountdownRemaining.TotalMinutes:D2}:{_controlPanelCountdownRemaining.Seconds:D2}";

        var editMinutesBox = this.FindName("ControlPanelCountdownEditMinutesBox") as Avalonia.Controls.TextBox;
        var editSecondsBox = this.FindName("ControlPanelCountdownEditSecondsBox") as Avalonia.Controls.TextBox;
        var editPopupHost = this.FindName("ControlPanelCountdownEditPopupHost") as Avalonia.Controls.Primitives.Popup;

        // Never overwrite a field while the operator is typing. Previously this ran
        // every clock tick and made live editing appear broken.
        if (editPopupHost?.IsOpen != true && editMinutesBox is not null)
            editMinutesBox.Text = ((int)_controlPanelCountdownDuration.TotalMinutes).ToString("D2");

        if (editPopupHost?.IsOpen != true && editSecondsBox is not null)
            editSecondsBox.Text = _controlPanelCountdownDuration.Seconds.ToString("D2");

        if (_controlPanelCountdownRemaining == TimeSpan.Zero)
        {
            ControlPanelCountdownStatusText.Text = _controlPanelCountdownAutoPausedClock
                ? "Finished — live clock paused"
                : "Finished";
            ControlPanelCountdownStatusText.Foreground = MediaBrushes.White;
        }
        else if (_controlPanelCountdownRunning)
        {
            ControlPanelCountdownStatusText.Text = "Running";
            ControlPanelCountdownStatusText.Foreground = this.FindResource("AccentBrush") as SolidColorBrush ?? new SolidColorBrush(MediaBrushes.White.Color);
        }
        else
        {
            ControlPanelCountdownStatusText.Text = _match.ClockRunning
                ? "Paused"
                : "Ready";
            ControlPanelCountdownStatusText.Foreground = this.FindResource("TextMidBrush") as SolidColorBrush ?? new SolidColorBrush(MediaBrushes.White.Color);
        }
    }

    private void StartBreakRotation()
    {
        StopBreakRotation();
        _rotationIndex = 0;
        _isBreakRotating = true;
        _rotationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(18) };
        _rotationTimer.Tick += (_, _) =>
        {
            // Build a dynamic rotation list — only include weather if data is available
            List<Action> screens =
            [
                ShowBreakScreenRotation,
                ShowScorewormRotation,
                ShowStatsScreenRotation
            ];

            if (_scorebug?.GetWeatherSnapshot() is not null)
                screens.Add(ShowWeatherScreenRotation);

            _rotationIndex = (_rotationIndex + 1) % screens.Count;
            screens[_rotationIndex]();
        };
        _rotationTimer.Start();
    }

    private void StopBreakRotation()
    {
        _rotationTimer?.Stop();
        _rotationTimer = null;
        _isBreakRotating = false;

        // Also cancel the delayed break-analysis rotation so that
        // switching to another screen (e.g. the custom video) isn't
        // overridden a few seconds later.
        if (_breakScreenDelayTimer != null)
        {
            _breakScreenDelayTimer.Stop();
            _breakScreenDelayTimer = null;
        }
    }

    private void ShowScorebug()
    {
        if (_scorebug == null) return;
        SwitchToScreen(_scorebug, "Live Scorebug");
    }

    private string? GetMatchStateBarTitle()
    {
        bool fullTime = _match.Quarter == 4 && _match.GetQuarterSnapshot(4) is not null;
        bool atQuarterBreak = !_match.ClockRunning && _match.ElapsedInQuarter <= TimeSpan.Zero
                              && (_match.Quarter > 1 || fullTime);
        return atQuarterBreak
            ? QuarterLabelService.GetBreakLabel(GetEndedQuarter())
            : QuarterLabelService.GetQuarterLabel(_match.Quarter);
    }

    private int GetEndedQuarter()
    {
        // After EndQuarter(), Quarter is already incremented (except Q4 which stays 4).
        // If clock is not running and no time has elapsed in the current quarter,
        // we're at a break — the quarter that just ended is Quarter-1.
        // Special case: Q4 stays at 4, check if Q4 snapshot exists for full time.
        if (!_match.ClockRunning && _match.ElapsedInQuarter <= TimeSpan.Zero && _match.Quarter > 1)
        {
            // If Q4 snapshot exists, all quarters are done → FT (endedQuarter=4)
            if (_match.GetQuarterSnapshot(4) != null)
            {
                return 4;
            }

            return _match.Quarter - 1;
        }

        return _match.Quarter;
    }

    private void ShowBreakScreen()
    {
        PopulateBreakScreen();
        SwitchToScreen(_breakScreen!, "Quarter Summary");
    }

    private void ShowQuarterBreakScorebug()
    {
        if (_quarterBreakScorebug is null) return;
        PopulateQuarterBreakScorebug();
        StartQuarterFoldTransition(_quarterBreakScorebug, "Quarter Break");
    }

    private void PopulateQuarterBreakScorebug()
    {
        if (_quarterBreakScorebug is null) return;
        bool showTopBar = _scorebug?.CurrentLayout != ScorebugLayout.Distance;
        _quarterBreakScorebug.SetTopBarVisible(showTopBar,
            showTopBar ? _scorebug?.GetTransitionTopBarHeight() : null);
        _quarterBreakScorebug.Populate(_match, GetEndedQuarter(),
            (HomeColorBox.Text ?? string.Empty), (AwayColorBox.Text ?? string.Empty),
            (HomeSecondaryColorBox.Text ?? string.Empty), (AwaySecondaryColorBox.Text ?? string.Empty),
            _homeLogoPath, _awayLogoPath, _scorebug?.GetWeatherSnapshot());
    }

    private void ShowBreakScreenRotation()
    {
        PopulateBreakScreen();
        SwitchToScreen(_breakScreen!, "Quarter Summary", fromRotation: true);
    }

    private void PopulateBreakScreen()
    {
        if (_breakScreen == null) return;

        _breakScreen.Populate(_match, GetEndedQuarter(),
            (HomeColorBox.Text ?? string.Empty), (AwayColorBox.Text ?? string.Empty),
            (HomeSecondaryColorBox.Text ?? string.Empty), (AwaySecondaryColorBox.Text ?? string.Empty),
            _homeLogoPath, _awayLogoPath, GetMatchStateBarTitle());
    }

    private void ShowScoreworm()
    {
        PopulateScoreworm();
        SwitchToScreen(_scoreworm!, "Score Worm");
    }

    private void ShowScorewormRotation()
    {
        PopulateScoreworm();
        SwitchToScreen(_scoreworm!, "Score Worm", fromRotation: true);
    }

    private void PopulateScoreworm()
    {
        if (_scoreworm == null) return;

        _scoreworm.Populate(_match, GetEndedQuarter(),
            (HomeColorBox.Text ?? string.Empty), (AwayColorBox.Text ?? string.Empty),
            (HomeSecondaryColorBox.Text ?? string.Empty), (AwaySecondaryColorBox.Text ?? string.Empty),
            _homeLogoPath, _awayLogoPath,
            GetMatchStateBarTitle());
    }

    private void ShowStatsScreen()
    {
        PopulateStatsScreen();
        SwitchToScreen(_statsScreen!, "Stats Screen");
    }

    private void ShowStatsScreenRotation()
    {
        PopulateStatsScreen();
        SwitchToScreen(_statsScreen!, "Stats Screen", fromRotation: true);
    }

    private void PopulateStatsScreen()
    {
        if (_statsScreen == null) return;
        var stats = MatchStats.Calculate(_match, _match.Quarter, _match.ElapsedInQuarter);

        _statsScreen.Populate(_match, stats, GetEndedQuarter(),
            (HomeColorBox.Text ?? string.Empty), (AwayColorBox.Text ?? string.Empty),
            (HomeSecondaryColorBox.Text ?? string.Empty), (AwaySecondaryColorBox.Text ?? string.Empty),
            _homeLogoPath, _awayLogoPath,
            GetMatchStateBarTitle());
    }

    private void ShowWeatherScreen()
    {
        PopulateWeatherScreen();
        SwitchToScreen(_weatherScreen!, "Weather");
    }

    private void ShowWeatherScreenRotation()
    {
        if (_weatherScreen == null || _scorebug?.GetWeatherSnapshot() == null)
        {
            ShowBreakScreenRotation();
            return;
        }

        PopulateWeatherScreen();
        SwitchToScreen(_weatherScreen, "Weather", fromRotation: true);
    }

    private void PopulateWeatherScreen()
    {
        if (_weatherScreen == null) return;

        var snapshot = _scorebug?.GetWeatherSnapshot();

        _weatherScreen.Populate(snapshot, _weatherLocation,
            _match, GetEndedQuarter(),
            (HomeColorBox.Text ?? string.Empty), (AwayColorBox.Text ?? string.Empty),
            (HomeSecondaryColorBox.Text ?? string.Empty), (AwaySecondaryColorBox.Text ?? string.Empty),
            _homeLogoPath, _awayLogoPath,
            GetMatchStateBarTitle());
    }

    private void ApplyCurrentTeamColorsToScorebug()
    {
        _scorebug?.SetTeamColors((HomeColorBox.Text ?? string.Empty), (AwayColorBox.Text ?? string.Empty), (HomeSecondaryColorBox.Text ?? string.Empty), (AwaySecondaryColorBox.Text ?? string.Empty));
    }

    private void ApplyTeamColorsToScoringPanels()
    {
        MediaColor homeColor = GetTeamColor(true);
        MediaColor awayColor = GetTeamColor(false);

        ApplyScoringPanelColors(homeColor,
            HomeScoringBorder, HomeScoringLabel, HomeScoreDisplay,
            HomeGoalButton, HomeBehindButton);

        ApplyScoringPanelColors(awayColor,
            AwayScoringBorder, AwayScoringLabel, AwayScoreDisplay,
            AwayGoalButton, AwayBehindButton);
    }

    /// <summary>
    /// Applies team-coloured theming to a scoring panel. Uses a minimum
    /// brightness floor so very dark team colours still produce visible
    /// backgrounds and buttons.
    /// </summary>
    private static void ApplyScoringPanelColors(
        MediaColor teamColor, Border panelBorder, Avalonia.Controls.TextBlock label,
        Avalonia.Controls.TextBlock scoreDisplay,
        Avalonia.Controls.Button goalButton, Avalonia.Controls.Button behindButton)
    {
        // Ensure minimum brightness for the team colour used in calculations
        // so very dark teams (e.g. black, dark navy) still produce visible panels
        byte minChannel = 30;
        MediaColor effective = MediaColor.FromRgb(
            Math.Max(teamColor.R, minChannel),
            Math.Max(teamColor.G, minChannel),
            Math.Max(teamColor.B, minChannel));

        // Panel background: blend team colour toward dark rather than raw division
        MediaColor bg = MediaColor.FromRgb(
            (byte)Math.Max(effective.R / 4, 10),
            (byte)Math.Max(effective.G / 4, 10),
            (byte)Math.Max(effective.B / 4, 12));
        MediaColor border = MediaColor.FromArgb(0xAA, effective.R, effective.G, effective.B);
        MediaColor labelFg = MediaColor.FromArgb(0xCC,
            (byte)Math.Min(effective.R + 100, 255),
            (byte)Math.Min(effective.G + 100, 255),
            (byte)Math.Min(effective.B + 100, 255));

        panelBorder.Background = new SolidColorBrush(bg);
        panelBorder.BorderBrush = new SolidColorBrush(border);
        label.Foreground = new SolidColorBrush(labelFg);
        goalButton.Background = new SolidColorBrush(teamColor);
        goalButton.BorderBrush = new SolidColorBrush(labelFg);
        goalButton.Foreground = ContrastHelper.GetContrastBrush(teamColor);

        // Behind button: darker tint with minimum brightness
        MediaColor behindBg = MediaColor.FromRgb(
            (byte)Math.Max(effective.R / 3, 14),
            (byte)Math.Max(effective.G / 3, 14),
            (byte)Math.Max(effective.B / 3, 16));
        behindButton.Background = new SolidColorBrush(behindBg);
        behindButton.BorderBrush = new SolidColorBrush(
            MediaColor.FromArgb(0x88, effective.R, effective.G, effective.B));

        scoreDisplay.Foreground = ContrastHelper.GetContrastBrush(bg);
    }

    // ── Unified MP4 takeover ───────────────────────────────────────────────
    //
    // Both goal-celebration videos and presentation videos play through the
    // same LibVLCSharp surface (_videoPlayer). EnterVideoTakeover hides every
    // sibling screen and reveals _videoPlayer; ExitVideoTakeover restores the
    // screen that was visible beforehand. This replaces the previous layered
    // MediaElement overlay, which was the root cause of black-screen and
    // global-lag issues during live operation.

    private Control? _preTakeoverScreen;
    private bool _videoTakeoverActive;
    private bool _scoreboardSuspendedForPlayback;
    private bool _breakRotationWasRunningPreTakeover;

    // Put the scoreboard ecosystem into a dormant state while a video owns
    // the output window. Stops the break-screen rotation timer and break
    // delay timer (both of which can otherwise fire mid-playback and force
    // a screen switch), and freezes any running storyboard animations on
    // the now-hidden sibling surfaces so they stop scheduling render-thread
    // work. The video surface itself is untouched.
    private void SuspendScoreboardForPlayback()
    {
        if (_scoreboardSuspendedForPlayback) return;
        _scoreboardSuspendedForPlayback = true;

        _breakRotationWasRunningPreTakeover = _isBreakRotating;
        try { StopBreakRotation(); } catch { /* best-effort */ }

        if (_breakScreenDelayTimer != null)
        {
            try { _breakScreenDelayTimer.Stop(); } catch { /* best-effort */ }
            _breakScreenDelayTimer = null;
        }

        // Hidden WPF children still cost render-thread work if they have
        // active storyboards. Collapsing them is already done by the
        // takeover, but explicitly halting any animations on them removes
        // the last bit of composition-thread chatter during playback.
        try { _scorebug?.BeginAnimation(Control.OpacityProperty, null); } catch { }
        try { _breakScreen?.BeginAnimation(Control.OpacityProperty, null); } catch { }
        try { _scoreworm?.BeginAnimation(Control.OpacityProperty, null); } catch { }
        try { _statsScreen?.BeginAnimation(Control.OpacityProperty, null); } catch { }
        try { _weatherScreen?.BeginAnimation(Control.OpacityProperty, null); } catch { }
    }

    private void ResumeScoreboardForPlayback()
    {
        if (!_scoreboardSuspendedForPlayback) return;
        _scoreboardSuspendedForPlayback = false;

        if (_breakRotationWasRunningPreTakeover)
        {
            try { StartBreakRotation(); } catch { /* best-effort */ }
        }
        _breakRotationWasRunningPreTakeover = false;
    }

    private void EnterVideoTakeover()
    {
        if (_videoPlayer is null || _displayContainer is null) return;

        if (!_videoTakeoverActive)
        {
            _preTakeoverScreen = GetCurrentlyVisibleScreen();
            _videoTakeoverActive = true;
            Roche_Scoreboard.Services.VideoDiagnostics.Info("Takeover",
                $"EnterVideoTakeover · preScreen={_preTakeoverScreen?.GetType().Name ?? "<none>"}");
        }

        // Hide every non-video child so the takeover is unambiguous.
        foreach (Control child in _displayContainer.Children)
        {
            if (ReferenceEquals(child, _videoPlayer)) continue;
            child.IsVisible = false;
        }

        _videoPlayer.IsVisible = true;
        _showingBreakScreen = true;

        // Put the rest of the scoreboard system into dormant mode so the
        // video surface owns the render pipeline.
        SuspendScoreboardForPlayback();

        if (VideoTabPreviewBadge != null)
            VideoTabPreviewBadge.Text = "VIDEO TAKEOVER · ON SCOREBOARD";
    }

    private void ExitVideoTakeover()
    {
        if (_videoPlayer is not null)
        {
            try { _videoPlayer.Pause(); } catch { /* control may be torn down */ }
            _videoPlayer.IsVisible = false;
        }

        // Always resume the scoreboard ecosystem, even if takeover wasn't
        // formally active — defensive against suspend/resume drift.
        ResumeScoreboardForPlayback();

        if (!_videoTakeoverActive) return;
        _videoTakeoverActive = false;
        Roche_Scoreboard.Services.VideoDiagnostics.Info("Takeover", "ExitVideoTakeover");

        Control? restore = _preTakeoverScreen ?? (Control?)_scorebug;
        _preTakeoverScreen = null;

        if (restore is not null)
        {
            restore.IsVisible = true;
            _showingBreakScreen = !ReferenceEquals(restore, _scorebug);
        }

        if (VideoTabPreviewBadge != null)
            VideoTabPreviewBadge.Text = "LIVE · MIRRORING SCOREBOARD";
    }

    /// <summary>
    /// Isolated playback diagnostic mode. Suspends the rest of the scoreboard
    /// ecosystem (rotation, animations, sibling screens) and plays a single
    /// MP4 on the persistent <see cref="VideoPlayerControl"/> with no
    /// overlays, no scorebug, no transitions. Used to isolate whether a
    /// playback issue originates from LibVLC itself or from scoreboard
    /// integration logic.
    /// </summary>
    /// <param name="filePath">Absolute path to an MP4 (or other VLC-readable) file.</param>
    /// <returns>True when playback was kicked off; false when the file is missing or VLC is unavailable.</returns>
    internal bool StartIsolatedPlaybackTest(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
        {
            Roche_Scoreboard.Services.VideoDiagnostics.Warn("TestMode",
                $"StartIsolatedPlaybackTest: file not found ({filePath}).");
            return false;
        }
        if (_videoPlayer is null || !Roche_Scoreboard.Services.VlcRuntime.IsAvailable)
        {
            Roche_Scoreboard.Services.VideoDiagnostics.Error("TestMode",
                "StartIsolatedPlaybackTest: video player or LibVLC runtime unavailable.");
            return false;
        }

        Roche_Scoreboard.Services.VideoDiagnostics.Info("TestMode",
            $"Entering isolated playback test mode for {filePath}.");
        try
        {
            _videoPlayer.Loop = true;
            _videoPlayer.SetVideoSource(filePath);
            EnterVideoTakeover();
            _videoPlayer.Play();
            return true;
        }
        catch (Exception ex)
        {
            Roche_Scoreboard.Services.VideoDiagnostics.Error("TestMode",
                "StartIsolatedPlaybackTest threw.", ex);
            return false;
        }
    }

    /// <summary>
    /// Stops the isolated playback test mode and returns the scoreboard to
    /// its pre-takeover state.
    /// </summary>
    internal void StopIsolatedPlaybackTest()
    {
        Roche_Scoreboard.Services.VideoDiagnostics.Info("TestMode",
            "Exiting isolated playback test mode.");
        try { _videoPlayer?.Stop(); } catch { /* best-effort */ }
        ExitVideoTakeover();
    }

    // Single live instance — emergency mode is mutually exclusive with the
    // embedded takeover. Only created on demand so its native player
    // resources don't sit idle.
    private Roche_Scoreboard.Views.EmergencyVideoWindow? _emergencyVideoWindow;

    /// <summary>
    /// Engages the emergency VLC-only borderless fallback window for the
    /// given source. Use only when the embedded takeover pipeline has
    /// repeatedly failed; this path bypasses the WPF compositor entirely.
    /// The window is pinned to the top-left and sized to match the
    /// scoreboard output dimensions.
    /// </summary>
    internal bool EngageEmergencyPlayback(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
        {
            Roche_Scoreboard.Services.VideoDiagnostics.Warn("Emergency",
                $"EngageEmergencyPlayback: file not found ({filePath}).");
            return false;
        }
        if (!Roche_Scoreboard.Services.VlcRuntime.IsAvailable)
        {
            Roche_Scoreboard.Services.VideoDiagnostics.Error("Emergency",
                "EngageEmergencyPlayback: LibVLC runtime unavailable.");
            return false;
        }

        // Mirror the scoreboard output region so the operator sees the same
        // on-air result. Fall back to the display container's render size.
        double w = _displayContainer?.NativeWidth() > 0 ? _displayContainer.NativeWidth() : 1280;
        double h = _displayContainer?.NativeHeight() > 0 ? _displayContainer.NativeHeight() : 720;

        try
        {
            if (_emergencyVideoWindow is null)
            {
                _emergencyVideoWindow = new Roche_Scoreboard.Views.EmergencyVideoWindow(
                    Roche_Scoreboard.Services.VlcRuntime.Instance, w, h);
                _emergencyVideoWindow.Closed += (_, _) => _emergencyVideoWindow = null;
                _emergencyVideoWindow.Show();
            }
            _emergencyVideoWindow.PlaySource(filePath);
            return true;
        }
        catch (Exception ex)
        {
            Roche_Scoreboard.Services.VideoDiagnostics.Error("Emergency",
                "EngageEmergencyPlayback threw.", ex);
            return false;
        }
    }

    /// <summary>Closes the emergency playback window, if one is currently visible.</summary>
    internal void DisengageEmergencyPlayback()
    {
        if (_emergencyVideoWindow is null) return;
        Roche_Scoreboard.Services.VideoDiagnostics.Info("Emergency", "DisengageEmergencyPlayback.");
        try { _emergencyVideoWindow.Close(); } catch { /* best-effort */ }
        _emergencyVideoWindow = null;
    }

    private void PlayCustomGoalVideoIfConfigured(bool isHome, Action fallbackAnimation)
    {
        if (_scorebug is null)
        {
            return;
        }

        if (_scorebug.GetGoalAnimationStyle() != GoalAnimationStyle.CustomVideo)
        {
            fallbackAnimation();
            return;
        }

        string? path = isHome ? _homeGoalVideoPath : _awayGoalVideoPath;
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path) || _videoPlayer is null)
        {
            fallbackAnimation();
            return;
        }

        // If a previous goal video is still on-screen, finish its pending
        // score-flip so the new event can take over cleanly.
        if (_goalVideoCompletionCallback is not null)
        {
            Action? prev = _goalVideoCompletionCallback;
            _goalVideoCompletionCallback = null;
            _videoPlayer.PlaybackEnded -= GoalVideo_OneShotEnded;
            _videoPlayer.MediaLoadFailed -= GoalVideo_LoadFailed;
            try { prev?.Invoke(); } catch { /* ignore */ }
        }

        try
        {
            _goalVideoCompletionCallback = fallbackAnimation;
            _videoPlayer.Loop = false;
            _videoPlayer.PlaybackEnded += GoalVideo_OneShotEnded;
            _videoPlayer.MediaLoadFailed += GoalVideo_LoadFailed;

            // Pause the previous clip (if any) before swapping the source
            // so VLC isn't actively decoding the old media while we replace
            // its Media instance. Without this, the away MP4 occasionally
            // never started rendering because the underlying MediaPlayer
            // was still mid-tick on the previous home clip.
            try { _videoPlayer.Pause(); } catch { /* best-effort */ }

            // VideoOptimizer produces a playback-friendly copy at import
            // time; using it here avoids decoding the original 4K source.
            string playbackPath = Roche_Scoreboard.Services.VideoOptimizer.GetPlaybackPath(path);
            _videoPlayer.SetVideoSource(playbackPath);
            EnterVideoTakeover();
            _videoPlayer.Play();
        }
        catch
        {
            FinishGoalVideoCallback();
        }
    }

    private void GoalVideo_OneShotEnded(object? sender, EventArgs e)
    {
        FinishGoalVideoCallback();
    }

    private void GoalVideo_LoadFailed(object? sender, VideoLoadFailedEventArgs e)
    {
        Roche_Scoreboard.Services.VideoDiagnostics.Error("GoalVideo",
            "Goal video load/playback failed - reverting to fallback animation.", e.ErrorException);
        FinishGoalVideoCallback();
    }

    private void FinishGoalVideoCallback()
    {
        if (_videoPlayer is not null)
        {
            _videoPlayer.PlaybackEnded -= GoalVideo_OneShotEnded;
            _videoPlayer.MediaLoadFailed -= GoalVideo_LoadFailed;
            _videoPlayer.Loop = true;
        }

        ExitVideoTakeover();

        Action? callback = _goalVideoCompletionCallback;
        _goalVideoCompletionCallback = null;
        try { callback?.Invoke(); } catch { /* ignore */ }
    }

    private void CancelGoalVideoPlayback()
    {
        if (_videoPlayer is not null)
        {
            _videoPlayer.PlaybackEnded -= GoalVideo_OneShotEnded;
            _videoPlayer.MediaLoadFailed -= GoalVideo_LoadFailed;
            try { _videoPlayer.Stop(); } catch { /* best-effort recovery */ }
            _videoPlayer.Loop = true;
        }

        _goalVideoCompletionCallback = null;
        ExitVideoTakeover();
    }

    private void StopStatsBarTimer()
    {
        _statsBarTimer?.Stop();
        _statsBarTimer = null;
        _statsBarHideTimer?.Stop();
        _statsBarHideTimer = null;
    }

    // ── Periodic overlay scheduling (driven by OverlayScheduler) ──

    /// <summary>
    /// Registers all informational overlay definitions with the scheduler.
    /// Called once from the constructor. The order here determines the default
    /// rotation: Stats → WinProb → Forecast → Weather → Rain.
    /// </summary>
    /// <summary>
    /// Registers all informational overlay definitions with the scheduler.
    /// Called once from the constructor.
    /// <para>
    /// All seven overlays share identical <see cref="OverlayDefinition.MinInterval"/>
    /// and <see cref="OverlayDefinition.Priority"/> values so the rotation is
    /// fair: each overlay has an equal chance of being picked at any given
    /// scheduling decision, and the scheduler's staleness-based selection then
    /// guarantees they spread evenly apart. Differences in display content are
    /// expressed via <see cref="OverlayDefinition.IsRelevant"/> so e.g. the
    /// Rain overlay is naturally skipped on dry days.
    /// </para>
    /// </summary>
    private void RegisterScheduledOverlays()
    {
        // Analysis overlays (StatsBar, WinProbability, QuarterScores, RecentScores)
        // surface the in-game story so they're favored over weather overlays:
        // shorter MinInterval = more eligible scheduling slots, higher priority
        // so they survive late-quarter filtering. Weather overlays still rotate
        // through, but at roughly half the cadence so they no longer dominate.
        TimeSpan analysisInterval = TimeSpan.FromMinutes(3);
        TimeSpan weatherInterval = TimeSpan.FromMinutes(8);
        TimeSpan sharedDuration = TimeSpan.FromSeconds(12);
        const int analysisPriority = 90;
        const int weatherPriority = 60;

        _overlayScheduler.RegisterOverlays(
        [
            new OverlayDefinition
            {
                Kind = OverlayKind.StatsBar,
                DisplayDuration = sharedDuration,
                MinInterval = analysisInterval,
                Priority = analysisPriority,
                Family = OverlayFamily.MatchStory,
                IsRelevant = () => _match.Events.Count > 0
            },
            new OverlayDefinition
            {
                Kind = OverlayKind.WinProbability,
                DisplayDuration = sharedDuration,
                MinInterval = analysisInterval,
                Priority = analysisPriority,
                Family = OverlayFamily.MatchStory,
                IsRelevant = () => _match.Events.Count >= 3
            },
            new OverlayDefinition
            {
                Kind = OverlayKind.Forecast,
                DisplayDuration = sharedDuration,
                MinInterval = weatherInterval,
                Priority = weatherPriority,
                Family = OverlayFamily.Conditions,
                IsRelevant = () => _scorebug?.GetWeatherSnapshot() is not null
            },
            new OverlayDefinition
            {
                Kind = OverlayKind.Weather,
                DisplayDuration = sharedDuration,
                MinInterval = weatherInterval,
                Priority = weatherPriority,
                Family = OverlayFamily.Conditions,
                IsRelevant = () => _scorebug?.GetWeatherSnapshot() is not null
            },
            new OverlayDefinition
            {
                Kind = OverlayKind.Rain,
                DisplayDuration = sharedDuration,
                MinInterval = weatherInterval,
                Priority = weatherPriority,
                Family = OverlayFamily.Conditions,
                IsRelevant = () =>
                {
                    var snap = _scorebug?.GetWeatherSnapshot();
                    if (snap is null) return false;
                    return snap.HourlyForecast.Any(h => h.PrecipitationProbability > 0);
                }
            },
            new OverlayDefinition
            {
                Kind = OverlayKind.QuarterScores,
                DisplayDuration = sharedDuration,
                MinInterval = analysisInterval,
                Priority = analysisPriority,
                Family = OverlayFamily.MatchStory,
                IsRelevant = () => _match.Events.Count > 0
            },
            new OverlayDefinition
            {
                Kind = OverlayKind.RecentScores,
                DisplayDuration = TimeSpan.FromSeconds(14),
                MinInterval = analysisInterval,
                Priority = analysisPriority,
                Family = OverlayFamily.MatchStory,
                IsRelevant = () => _match.Events.Count >= 3
            }
        ]);
    }

    /// <summary>
    /// Called every second from the frame-rendering loop. Asks the scheduler
    /// whether to show the next informational overlay and dispatches it.
    /// </summary>
    private void CheckPeriodicOverlays()
    {
        if (!_autoOverlaysEnabled) return;
        if (_periodicOverlayReserved) return;
        if (_scorebug == null || _showingBreakScreen) return;
        if (_scorebug.IsTickerActive) return;
        if (_match.Events.Count == 0) return;
        // Don't trigger periodic overlays while the clock is paused — the
        // operator is between plays / mid-continuance and the broadcast
        // shouldn't cover the live scorebug with a stat overlay then.
        if (!_match.ClockRunning) return;

        EnsureOverlayQueueWired();

        OverlayDefinition? next = _overlayScheduler.TryGetNextOverlay(
            _match.ElapsedInQuarter,
            _match.QuarterDuration,
            clockRunning: _match.ClockRunning,
            _showingBreakScreen);

        if (next is null) return;

        Action showAction = next.Kind switch
        {
            OverlayKind.StatsBar => () =>
            {
                ApplyCurrentTeamColorsToScorebug();
                var stats = MatchStats.Calculate(_match, _match.Quarter, _match.ElapsedInQuarter);
                _scorebug?.ShowStatsBar(stats);
            },
            OverlayKind.WinProbability => () =>
            {
                ApplyCurrentTeamColorsToScorebug();
                ComputeAndShowWinProbability();
            },
            OverlayKind.Forecast => () =>
            {
                var snapshot = _scorebug?.GetWeatherSnapshot();
                if (snapshot != null) _scorebug?.ShowWeatherForecast(snapshot);
            },
            OverlayKind.Weather => () =>
            {
                var snapshot = _scorebug?.GetWeatherSnapshot();
                if (snapshot != null) _scorebug?.ShowWeatherStats(snapshot);
            },
            OverlayKind.Rain => () =>
            {
                var snapshot = _scorebug?.GetWeatherSnapshot();
                if (snapshot != null) _scorebug?.ShowRainForecast(snapshot);
            },
            OverlayKind.QuarterScores => () =>
            {
                ApplyCurrentTeamColorsToScorebug();
                int q = _match.Quarter;
                var prev = q > 1 ? _match.GetQuarterSnapshot(q - 1) : null;
                int hg = _match.HomeGoals - (prev?.HomeGoals ?? 0);
                int hb = _match.HomeBehinds - (prev?.HomeBehinds ?? 0);
                int ag = _match.AwayGoals - (prev?.AwayGoals ?? 0);
                int ab = _match.AwayBehinds - (prev?.AwayBehinds ?? 0);
                _scorebug?.ShowQuarterScores(q, hg, hb, ag, ab);
            },
            OverlayKind.RecentScores => () =>
            {
                ApplyCurrentTeamColorsToScorebug();
                _scorebug?.ShowRecentScores(_match.Events, _match.Quarter);
            },
            _ => () => { }
        };

        // Reserve the single periodic slot while the card waits behind an
        // animation or another overlay. Only count it as shown when it really
        // reaches the output; cancelled reservations must not stall the
        // scheduler or consume a rotation entry.
        _periodicOverlayReserved = true;
        Action showReserved = () =>
        {
            _periodicOverlayReserved = false;
            if (_scorebug is null || _showingBreakScreen || _scorebug.IsTickerActive || !_match.ClockRunning)
                return;

            bool stillRelevant;
            try { stillRelevant = next.IsRelevant(); }
            catch { stillRelevant = false; }
            if (!stillRelevant)
                return;

            // Only consume rotation freshness once the card has passed its
            // final on-air checks. Weather/state can change while a periodic
            // card waits behind a goal animation or event-driven story.
            _overlayScheduler.NotifyOverlayShown(next.Kind);
            showAction();
        };
        Action cancelReservation = () => _periodicOverlayReserved = false;
        EnqueueAutoOverlay(showReserved, onCancelled: cancelReservation);
    }

    private void ResetPeriodicOverlayTracking()
    {
        _overlayScheduler.Reset();
        _previousLeadChanges = 0;
        _lastShownRunTeam = TeamSide.Home;
        _lastShownRunGoalCount = 0;
        _lastShownRunMatchClock = TimeSpan.MinValue;
        _lastShownRunEndTimestamp = DateTime.MinValue;
        _autoOverlayDelayTimer?.Stop();
        _autoOverlayDelayTimer = null;
        _autoOverlayDelayCancellation?.Invoke();
        _autoOverlayDelayCancellation = null;
        _scoringRunDelayTimer?.Stop();
        _scoringRunDelayTimer = null;
        CancelQueuedOverlays();
        _periodicOverlayReserved = false;
        _nextOverlayAllowedAtUtc = DateTime.MinValue;
        _nextDroughtOverlayAllowedAtUtc = DateTime.MinValue;
        _shownDroughtMilestones.Clear();
    }

    // ── Scoring run auto-detection ──

    private void CheckScoringRunAfterScore(ScoreEvent ev)
    {
        if (_scorebug == null || _showingBreakScreen) return;
        IReadOnlyList<ScoreEvent> events = _match.Events;
        if (ev.Type != ScoreType.Goal || events.Count == 0 || !ReferenceEquals(events[^1], ev)) return;

        // Automatic run stories must belong to the team that just kicked the
        // goal. DetectBest intentionally preserves an older dominant story for
        // manual display, but using it here could show the opposition's run in
        // direct response to this goal.
        AflGoalRunDetector.Result? run = AflGoalRunDetector.Detect(events, ev.Team);
        if (run is null) return;

        // Cooldowns use the same accumulated playing-time clock as droughts.
        // This keeps a run continuous across quarters without treating the
        // quarter break, or an early-ended quarter, as elapsed match time.
        TimeSpan currentMatchClock = _match.PlayingTimeAt(_match.Quarter, _match.ElapsedInQuarter);
        TimeSpan elapsedSinceLastShow = _lastShownRunMatchClock == TimeSpan.MinValue
            ? TimeSpan.MaxValue
            : currentMatchClock - _lastShownRunMatchClock;
        if (elapsedSinceLastShow < TimeSpan.Zero) elapsedSinceLastShow = TimeSpan.MaxValue;

        // A dominance window can legitimately reframe as goals are added (for
        // example 6/8 becoming 7/9). Treat it as continuation of the same team
        // story so a shifted start point cannot bypass the growth cooldown.
        bool overlappingTeamStory = run.Team == _lastShownRunTeam
                                    && _lastShownRunEndTimestamp != DateTime.MinValue
                                    && run.StartTimestamp <= _lastShownRunEndTimestamp;
        if (overlappingTeamStory)
        {
            bool grewByTwoGoals = run.TeamGoals >= _lastShownRunGoalCount + 2;
            if (!grewByTwoGoals && elapsedSinceLastShow < RunCooldown)
                return;
        }
        else if (elapsedSinceLastShow < RunNewStoryCooldown)
            return;

        _lastShownRunTeam = run.Team;
        _lastShownRunGoalCount = run.TeamGoals;
        _lastShownRunMatchClock = currentMatchClock;
        _lastShownRunEndTimestamp = run.EndTimestamp;

        _overlayScheduler.NotifyEventDrivenOverlay();

        string teamName = run.Team == TeamSide.Home ? _match.HomeName : _match.AwayName;
        var runColor = GetTeamColor(run.Team == TeamSide.Home);
        string colorHex = $"#{runColor.R:X2}{runColor.G:X2}{runColor.B:X2}";
        _scorebug.ShowGoalRun(teamName, run.TeamGoals, run.WindowGoals, run.IsConsecutive, colorHex);
    }

    private AflDroughtSnapshot CalculateDroughts()
        => AflDroughtCalculator.Calculate(_match);

    private bool TryConsumeDroughtMilestone(AflDroughtCandidate candidate)
    {
        int milestone = candidate.TenMinuteMilestone;
        if (milestone < 10) return false;
        if (_shownDroughtMilestones.TryGetValue(candidate.Key, out int shown) && shown >= milestone)
            return false;
        _shownDroughtMilestones[candidate.Key] = milestone;
        return true;
    }

    // ── Lead change auto-detection ──

    private void CheckLeadChangesAfterScore()
    {
        if (_scorebug == null || _showingBreakScreen) return;

        // Suppress when the match has drifted well away from close — the
        // lead-change count is a "closeness" story and stops being useful
        // once one team has a commanding lead.
        int currentMargin = Math.Abs(_match.HomeTotal - _match.AwayTotal);
        if (currentMargin > LeadChangeMaxCurrentMargin) return;

        var stats = MatchStats.Calculate(_match, _match.Quarter, _match.ElapsedInQuarter);
        if (stats.LeadChanges < LeadChangeMinTotal) return;

        int newChanges = stats.LeadChanges - _previousLeadChanges;
        if (newChanges >= LeadChangeOverlayThreshold)
        {
            _previousLeadChanges = stats.LeadChanges;
            _overlayScheduler.NotifyEventDrivenOverlay();
            ApplyCurrentTeamColorsToScorebug();
            _scorebug.ShowLeadChangesBar(stats.LeadChanges);
        }
    }

    private const int MIN_TRACK_PX = 50;

    private bool _isBreakRotating;

    private void UpdateActiveDisplayLabel(string label)
    {
        CurrentDisplayText.Text = string.IsNullOrWhiteSpace(label)
            ? "OFF AIR"
            : $"ON AIR · {label.ToUpperInvariant()}";
        HeaderOutputText.Text = string.IsNullOrWhiteSpace(label) ? "No active output" : label;
        InteractionMotion.Pulse(HeaderOutputBadge);
    }

    private void SwitchToScreen(Control target, string label, bool fromRotation = false)
    {
        EnsureScoreboardWindow();
        if (_displayWindow is { IsVisible: false })
        {
            _displayWindow.Show();
        }

        if (!fromRotation)
        {
            StopBreakRotation();
        }

        if (target != _videoPlayer)
        {
            // Pause the video when leaving the video screen so audio doesn't
            // keep playing in the background. The current frame is retained
            // so resuming via Play picks up where it left off.
            _videoPlayer?.Pause();
        }

        int transitionVersion = ++_screenTransitionVersion;
        CancelPresentationAnimations();

        var current = GetCurrentlyVisibleScreen();
        if (current == target)
        {
            NormalizePresentationSurfaces(target);
            _showingBreakScreen = target != _scorebug;
            UpdateActiveDisplayLabel(label);
            return;
        }

        // A rapid second selection can arrive before the previous transition
        // completes. Keep exactly one outgoing surface and one incoming surface
        // so stale animation clocks cannot leave duplicate screens visible.
        foreach (Control surface in GetPresentationSurfaces())
        {
            if (surface != current && surface != target)
                NormalizePresentationSurface(surface, visible: false);
        }
        if (current != null) NormalizePresentationSurface(current, visible: true);
        NormalizePresentationSurface(target, visible: false);

        if (current != null)
        {
            if (_scorebug != null && _scorebug.IsRetroLayout)
                RetroTransition(current, target, transitionVersion);
            else
                SlideTransition(current, target, true, transitionVersion);
        }
        else
        {
            target.IsVisible = true;
            target.RenderTransform = new TranslateTransform(0, 0);
        }

        _showingBreakScreen = target != _scorebug;
        UpdateActiveDisplayLabel(label);

        // Note: switching to the video surface intentionally does NOT auto-play
        // the clip. The operator loads a video paused on its first frame and
        // explicitly hits Play/Show on Scoreboard when they want broadcast.
        // Goal videos drive their own playback start via PlayCustomGoalVideoIfConfigured.
    }

    private void SlideTransition(Control from, Control to, bool slideLeft, int transitionVersion)
    {
        // Modern "blackout pulse" transition:
        //   1. Black overlay fades in (covers desktop / any compositor flash)
        //   2. Outgoing screen is hidden & incoming screen is shown while fully black
        //   3. Black overlay fades back out, revealing the new screen with a subtle scale-in
        // This guarantees the desktop is never visible mid-transition.
        if (_retroCrtOverlay == null)
        {
            // Fallback: instant swap
            from.IsVisible = false;
            from.RenderTransform = new TranslateTransform(0, 0);
            from.Opacity = 1;
            to.IsVisible = true;
            to.RenderTransform = new TranslateTransform(0, 0);
            to.Opacity = 1;
            return;
        }

        const int fadeInMs = 180;
        const int holdMs = 60;
        const int fadeOutMs = 220;

        _retroCrtOverlay.Background = Avalonia.Media.Brushes.Black;
        _retroCrtOverlay.IsVisible = true;
        _retroCrtOverlay.Opacity = 0;

        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var fadeToBlack = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(fadeInMs)) { EasingFunction = ease };
        fadeToBlack.Completed += (_, _) =>
        {
            if (transitionVersion != _screenTransitionVersion) return;

            // Swap behind the curtain
            from.IsVisible = false;
            from.Opacity = 1;
            from.RenderTransform = new TranslateTransform(0, 0);

            // Prep incoming with a subtle scale-up for a modern reveal
            var scale = new ScaleTransform(1.03, 1.03);
            to.RenderTransformOrigin = new RelativePoint(0.5, 0.5,RelativeUnit.Relative);
            to.RenderTransform = scale;
            to.Opacity = 1;
            to.IsVisible = true;

            var scaleEase = new CubicEase { EasingMode = EasingMode.EaseOut };
            var sx = new DoubleAnimation(1.03, 1.0, TimeSpan.FromMilliseconds(fadeOutMs + 80)) { EasingFunction = scaleEase };
            var sy = new DoubleAnimation(1.03, 1.0, TimeSpan.FromMilliseconds(fadeOutMs + 80)) { EasingFunction = scaleEase };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);

            var fadeFromBlack = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(fadeOutMs))
            {
                BeginTime = TimeSpan.FromMilliseconds(holdMs),
                EasingFunction = ease
            };
            fadeFromBlack.Completed += (_, _) =>
            {
                if (transitionVersion != _screenTransitionVersion) return;
                if (_retroCrtOverlay != null)
                {
                    _retroCrtOverlay.IsVisible = false;
                    _retroCrtOverlay.Opacity = 0;
                }
                to.RenderTransform = new TranslateTransform(0, 0);
            };
            _retroCrtOverlay!.BeginAnimation(Control.OpacityProperty, fadeFromBlack);
        };

        _retroCrtOverlay.BeginAnimation(Control.OpacityProperty, fadeToBlack);
    }

    private void StartQuarterFoldTransition(Control target, string label)
    {
        EnsureScoreboardWindow();
        StopBreakRotation();
        if (_displayWindow is { IsVisible: false }) _displayWindow.Show();

        int transitionVersion = ++_screenTransitionVersion;
        CancelPresentationAnimations();
        Control? current = GetCurrentlyVisibleScreen();
        if (current is null || ReferenceEquals(current, target) || _retroCrtOverlay is null
            || !NativeSystemPreferences.ClientAreaAnimation)
        {
            if (current is not null && !ReferenceEquals(current, target)) current.IsVisible = false;
            NormalizePresentationSurfaces(target);
            _showingBreakScreen = !ReferenceEquals(target, _scorebug);
            UpdateActiveDisplayLabel(label);
            return;
        }

        foreach (Control surface in GetPresentationSurfaces())
            if (!ReferenceEquals(surface, current)) surface.IsVisible = false;

        IReadOnlyList<Control> outgoing = GetQuarterFoldPanels(current);
        IReadOnlyList<Control> incoming = GetQuarterFoldPanels(target);
        current.UpdateLayout();
        PrepareFoldPanels(outgoing, folded: false);

        double topInset = GetTransitionTopInset(current);
        _retroCrtOverlay.Margin = new Thickness(0, topInset, 0, 0);
        _retroCrtOverlay.Background = Avalonia.Media.Brushes.Black;
        _retroCrtOverlay.IsVisible = true;
        _retroCrtOverlay.Opacity = 0;
        _retroCrtOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(520))
        {
            BeginTime = TimeSpan.FromMilliseconds(100),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        });

        void RevealIncoming()
        {
            if (transitionVersion != _screenTransitionVersion) return;

            current.IsVisible = false;
            ResetFoldPanels(outgoing);
            target.IsVisible = true;
            target.Opacity = 1;
            _displayContainer?.UpdateLayout();
            target.UpdateLayout();
            PrepareFoldPanels(incoming, folded: true);

            _retroCrtOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(560))
            {
                BeginTime = TimeSpan.FromMilliseconds(70),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

            for (int i = 0; i < incoming.Count; i++)
            {
                Control panel = incoming[i];
                if (panel.RenderTransform is not ScaleTransform scale) continue;
                panel.Opacity = 0;
                int delay = GetFoldDelay(i, incoming.Count);
                panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(360))
                {
                    BeginTime = TimeSpan.FromMilliseconds(delay),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
                var unfold = new DoubleAnimation(0.025, 1, TimeSpan.FromMilliseconds(500))
                {
                    BeginTime = TimeSpan.FromMilliseconds(delay),
                    EasingFunction = new BackEase { Amplitude = 0.10, EasingMode = EasingMode.EaseOut }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.98, 1, TimeSpan.FromMilliseconds(500))
                {
                    BeginTime = TimeSpan.FromMilliseconds(delay),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                }, HandoffBehavior.SnapshotAndReplace);
                if (i == incoming.Count - 1)
                {
                    unfold.Completed += (_, _) =>
                    {
                        if (transitionVersion != _screenTransitionVersion) return;
                        ResetFoldPanels(incoming);
                        if (_retroCrtOverlay is not null)
                        {
                            _retroCrtOverlay.BeginAnimation(OpacityProperty, null);
                            _retroCrtOverlay.Opacity = 0;
                            _retroCrtOverlay.IsVisible = false;
                            _retroCrtOverlay.Margin = new Thickness(0);
                        }
                    };
                }
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, unfold, HandoffBehavior.SnapshotAndReplace);
            }
        }

        for (int i = 0; i < outgoing.Count; i++)
        {
            Control panel = outgoing[i];
            if (panel.RenderTransform is not ScaleTransform scale) continue;
            int delay = GetFoldDelay(i, outgoing.Count);
            var fold = new DoubleAnimation(1, 0.025, TimeSpan.FromMilliseconds(440))
            {
                BeginTime = TimeSpan.FromMilliseconds(delay),
                EasingFunction = new PowerEase { Power = 3, EasingMode = EasingMode.EaseIn }
            };
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.08, TimeSpan.FromMilliseconds(420))
            {
                BeginTime = TimeSpan.FromMilliseconds(delay),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.98, TimeSpan.FromMilliseconds(440))
            {
                BeginTime = TimeSpan.FromMilliseconds(delay),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            }, HandoffBehavior.SnapshotAndReplace);
            if (i == outgoing.Count - 1) fold.Completed += (_, _) => RevealIncoming();
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, fold, HandoffBehavior.SnapshotAndReplace);
        }

        _showingBreakScreen = !ReferenceEquals(target, _scorebug);
        UpdateActiveDisplayLabel(label);
    }

    internal static int GetFoldDelay(int index, int count)
    {
        // Five scorebug panels read as home pair -> clock -> away pair.
        // Three break panels read as a steady left-to-right wave. Keeping the
        // pair offsets short makes each cell distinct without looking random.
        if (count == 5)
            return index switch { 0 => 0, 1 => 45, 2 => 120, 3 => 195, _ => 240 };
        return index * 75;
    }

    private double GetTransitionTopInset(Control current)
    {
        double measured = current switch
        {
            ScorebugControl scorebug => scorebug.GetTransitionTopBarHeight(),
            QuarterBreakScorebugControl quarterBreak => quarterBreak.GetTransitionTopBarHeight(),
            _ => 0
        };
        if (current is ScorebugControl or QuarterBreakScorebugControl)
            return Math.Max(0, measured);
        // Margin Flow, Match Stats, Weather and Quarter Breakdown no longer
        // reserve a title bar, so their transition blackout covers the full
        // presentation rather than leaving a stale strip at the top.
        return 0;
    }

    private IReadOnlyList<Control> GetQuarterFoldPanels(Control surface)
        => surface switch
        {
            ScorebugControl scorebug => scorebug.GetQuarterFoldPanels(),
            QuarterBreakScorebugControl quarterBreak => quarterBreak.GetFoldPanels(),
            ContentControl { Content: Grid root } => root.Children
                .OfType<Control>()
                .Where(child => Grid.GetRow(child) > 0 && child.IsVisible == true)
                .ToArray() is { Length: > 0 } panels ? panels : [(Control)surface],
            Control element => [element],
            _ => []
        };

    private static void PrepareFoldPanels(IReadOnlyList<Control> panels, bool folded)
    {
        for (int i = 0; i < panels.Count; i++)
        {
            Control panel = panels[i];
            panel.BeginAnimation(OpacityProperty, null);
            panel.Opacity = folded ? 0 : 1;
            // Every panel uses the same centre hinge; the stagger supplies the
            // direction. Mixed top/bottom hinges made the sequence look as if
            // neighbouring panels belonged to different transitions.
            panel.RenderTransformOrigin = new RelativePoint(0.5, 0.5,RelativeUnit.Relative);
            panel.RenderTransform = new ScaleTransform(1, folded ? 0.025 : 1);
        }
    }

    private static void ResetFoldPanels(IReadOnlyList<Control> panels)
    {
        foreach (Control panel in panels)
        {
            panel.BeginAnimation(OpacityProperty, null);
            panel.Opacity = 1;
            if (panel.RenderTransform is ScaleTransform scale)
            {
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = 1;
                scale.ScaleY = 1;
            }
            panel.RenderTransform = new MatrixTransform(Matrix.Identity);
        }
    }

    /// <summary>
    /// Retro CRT-style screen transition — fast blackout, scanline sweep, static noise flash,
    /// then the new screen resolves through a vertical-roll / brightness reveal.
    /// </summary>
    private void RetroTransition(Control from, Control to, int transitionVersion)
    {
        if (_retroCrtOverlay == null)
        {
            // Fallback: no overlay available, just swap
            from.IsVisible = false;
            to.IsVisible = true;
            to.RenderTransform = new TranslateTransform(0, 0);
            return;
        }

        double height = _displayWindow?.NativeHeight() ?? 440;

        // ── Phase 1: CRT blackout (fast collapse to horizontal line) ──
        _retroCrtOverlay.IsVisible = true;
        _retroCrtOverlay.Opacity = 0;
        _retroCrtOverlay.RenderTransformOrigin = new RelativePoint(0, .5, RelativeUnit.Relative);
        _retroCrtOverlay.RenderTransform = new ScaleTransform(1, 1);

        var sb = new Storyboard();

        // Black overlay fades in fast
        var blackIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(80));
        Storyboard.SetTarget(blackIn, _retroCrtOverlay);
        Storyboard.SetTargetProperty(blackIn, new PropertyPath(OpacityProperty));
        sb.Children.Add(blackIn);

        // Outgoing screen fades out
        var outFade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(60));
        Storyboard.SetTarget(outFade, from);
        Storyboard.SetTargetProperty(outFade, new PropertyPath(OpacityProperty));
        sb.Children.Add(outFade);

        // CRT pinch — squash to horizontal line then expand back
        var pinchY = new DoubleAnimation(1, 0.005, TimeSpan.FromMilliseconds(120))
        {
            BeginTime = TimeSpan.FromMilliseconds(60),
            EasingFunction = new PowerEase { Power = 3, EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(pinchY, _retroCrtOverlay);
        Storyboard.SetTargetProperty(pinchY, new PropertyPath("RenderTransform.ScaleY"));
        sb.Children.Add(pinchY);

        // Hold the line briefly then expand
        var expandY = new DoubleAnimation(0.005, 1, TimeSpan.FromMilliseconds(100))
        {
            BeginTime = TimeSpan.FromMilliseconds(220),
            EasingFunction = new PowerEase { Power = 2, EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(expandY, _retroCrtOverlay);
        Storyboard.SetTargetProperty(expandY, new PropertyPath("RenderTransform.ScaleY"));
        sb.Children.Add(expandY);

        // ── Phase 2: Static noise flash (brief white flash while black) ──
        var noiseFlash = new DoubleAnimation(1, 0.7, TimeSpan.FromMilliseconds(30))
        {
            BeginTime = TimeSpan.FromMilliseconds(180),
            AutoReverse = true
        };
        Storyboard.SetTarget(noiseFlash, _retroCrtOverlay);
        Storyboard.SetTargetProperty(noiseFlash, new PropertyPath(OpacityProperty));
        sb.Children.Add(noiseFlash);

        // ── Phase 3: New screen appears with vertical roll ──
        // Prepare the incoming screen off-screen (vertically shifted up)
        to.IsVisible = true;
        to.Opacity = 0;
        to.RenderTransform = new TranslateTransform(0, -height * 0.15);

        // Incoming fades in after the blackout
        var inFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
        {
            BeginTime = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(inFade, to);
        Storyboard.SetTargetProperty(inFade, new PropertyPath(OpacityProperty));
        sb.Children.Add(inFade);

        // Vertical roll — slides down from offset to 0 (CRT vertical hold settling)
        var rollIn = new DoubleAnimation(-height * 0.15, 0, TimeSpan.FromMilliseconds(200))
        {
            BeginTime = TimeSpan.FromMilliseconds(280),
            EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 8, EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(rollIn, to);
        Storyboard.SetTargetProperty(rollIn, new PropertyPath("RenderTransform.(TranslateTransform.Y)"));
        sb.Children.Add(rollIn);

        // ── Phase 4: Dismiss the CRT overlay ──
        var blackOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120))
        {
            BeginTime = TimeSpan.FromMilliseconds(340)
        };
        Storyboard.SetTarget(blackOut, _retroCrtOverlay);
        Storyboard.SetTargetProperty(blackOut, new PropertyPath(OpacityProperty));
        sb.Children.Add(blackOut);

        // Cleanup
        sb.Completed += (_, _) =>
        {
            if (transitionVersion != _screenTransitionVersion) return;
            if (ReferenceEquals(_presentationTransitionStoryboard, sb))
                _presentationTransitionStoryboard = null;
            from.IsVisible = false;
            from.Opacity = 1;
            from.RenderTransform = new TranslateTransform(0, 0);
            to.RenderTransform = new TranslateTransform(0, 0);
            if (_retroCrtOverlay != null)
            {
                _retroCrtOverlay.IsVisible = false;
                _retroCrtOverlay.Opacity = 0;
                _retroCrtOverlay.RenderTransform = new ScaleTransform(1, 1);
            }
        };

        _presentationTransitionStoryboard = sb;
        sb.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
    }

    private IEnumerable<Control> GetPresentationSurfaces()
    {
        Control?[] surfaces =
        [
            _scorebug, _quarterBreakScorebug, _breakScreen, _scoreworm, _statsScreen,
            _weatherScreen, _videoPlayer, _outputScreensaver
        ];
        foreach (Control? surface in surfaces)
        {
            if (surface != null) yield return surface;
        }
    }

    private void CancelPresentationAnimations()
    {
        if (_presentationTransitionStoryboard is not null)
        {
            try { _presentationTransitionStoryboard.Remove(this); } catch { }
            _presentationTransitionStoryboard = null;
        }
        if (_scorebug is not null) ResetFoldPanels(_scorebug.GetQuarterFoldPanels());
        if (_quarterBreakScorebug is not null) ResetFoldPanels(_quarterBreakScorebug.GetFoldPanels());
        foreach (Control surface in GetPresentationSurfaces())
        {
            surface.BeginAnimation(Control.OpacityProperty, null);
            surface.Opacity = 1;
            surface.RenderTransform = new TranslateTransform(0, 0);
        }

        if (_retroCrtOverlay != null)
        {
            _retroCrtOverlay.BeginAnimation(Control.OpacityProperty, null);
            _retroCrtOverlay.IsVisible = false;
            _retroCrtOverlay.Opacity = 0;
            _retroCrtOverlay.RenderTransform = new ScaleTransform(1, 1);
            _retroCrtOverlay.Margin = new Thickness(0);
        }
    }

    private void NormalizePresentationSurfaces(Control selected)
    {
        foreach (Control surface in GetPresentationSurfaces())
            NormalizePresentationSurface(surface, surface == selected);
    }

    private static void NormalizePresentationSurface(Control surface, bool visible)
    {
        surface.BeginAnimation(Control.OpacityProperty, null);
        surface.Opacity = 1;
        surface.RenderTransformOrigin = new RelativePoint(0.5, 0.5,RelativeUnit.Relative);
        surface.RenderTransform = new TranslateTransform(0, 0);
        surface.IsVisible = visible ? true : false;
    }

    private void PreviewSelectedGoalAnimation_Click(object? sender, RoutedEventArgs e)
    {
        SwitchToScorebug_Click(sender, new RoutedEventArgs());
        if (_scorebug is null) return;

        _scorebug.PlayScoreAnimation(
            isHome: true,
            isGoal: true,
            newGoals: _match.HomeGoals,
            newBehinds: _match.HomeBehinds,
            newTotal: _match.HomeTotal);
    }

    private void AnimationRadio_Changed(object? sender, RoutedEventArgs e)
    {
        if (AnimBroadcastRadio is null) return;

        GoalAnimationStyle style;
        string title;
        string desc;
        string duration;
        string intensity;

        if (AnimElectricRadio?.IsChecked == true)
        {
            style = GoalAnimationStyle.Electric;
            title = "Voltage";
            desc = "A lateral stadium charge: edge ignition, compressed GOAL wordmark, irregular strobes and an electric score cut.";
            duration = "~2.9s";
            intensity = "High";
        }
        else if (AnimCinematicRadio?.IsChecked == true)
        {
            style = GoalAnimationStyle.Cinematic;
            title = "Club Cinema";
            desc = "A crest-led identity moment: the GOAL wordmark blooms through the club colour before the score dissolves into view.";
            duration = "~5.0s";
            intensity = "Low";
        }
        else if (AnimCleanRadio?.IsChecked == true)
        {
            style = GoalAnimationStyle.Clean;
            title = "Clean Impact";
            desc = "Minimal and score-first: one calm GOAL wordmark, a restrained edge pulse and a fast total-score dissolve.";
            duration = "~2.6s";
            intensity = "Minimal";
        }
        else if (AnimClassicRadio?.IsChecked == true)
        {
            style = GoalAnimationStyle.Classic;
            title = "Flipboard";
            desc = "A mechanical scoreboard treatment: monospace GOAL display, stepped lamps, a discrete score flip and bulb-burnout exit.";
            duration = "~2.4s";
            intensity = "Medium";
        }
        else if (AnimCustomVideoRadio?.IsChecked == true)
        {
            style = GoalAnimationStyle.CustomVideo;
            title = "Custom Video";
            desc = "Team-specific goal video playback if configured.";
            duration = "Video";
            intensity = "Custom";
        }
        else
        {
            style = GoalAnimationStyle.Broadcast;
            title = "TV Signal";
            desc = "A premium television package: vertical wordmark reveal, two channel sweeps, broadcast score lift and clean close.";
            duration = "~3.5s";
            intensity = "Medium";
        }

        _scorebug?.SetGoalAnimationStyle(style);

        if (!_initializingGoalAnimation)
        {
            string persistedStyle = style == GoalAnimationStyle.CustomVideo
                ? "customVideo"
                : char.ToLowerInvariant(style.ToString()[0]) + style.ToString()[1..];
            OperatorWorkflowPreferences.Update(p => p.AflGoalAnimation = persistedStyle);
        }

        if (AnimPreviewTitle != null)
        {
            AnimPreviewTitle.Text = title;
            AnimPreviewDesc.Text = desc;
            AnimPreviewDuration.Text = duration;
            AnimPreviewIntensity.Text = intensity;
        }

        // Programmatic selections (restored preferences and cloud remote)
        // must paint the same selected card state as a direct card click.
        if (AnimCardBroadcast is not null && AnimCardCustomVideo is not null)
            UpdateAnimCards();
    }

    private void ApplyRememberedGoalAnimation(string? value)
    {
        GoalAnimationStyle style = value?.Trim().ToLowerInvariant() switch
        {
            "electric" => GoalAnimationStyle.Electric,
            "cinematic" => GoalAnimationStyle.Cinematic,
            "clean" => GoalAnimationStyle.Clean,
            "classic" => GoalAnimationStyle.Classic,
            "customvideo" => GoalAnimationStyle.CustomVideo,
            _ => GoalAnimationStyle.Broadcast
        };

        Avalonia.Controls.RadioButton target = style switch
        {
            GoalAnimationStyle.Electric => AnimElectricRadio,
            GoalAnimationStyle.Cinematic => AnimCinematicRadio,
            GoalAnimationStyle.Clean => AnimCleanRadio,
            GoalAnimationStyle.Classic => AnimClassicRadio,
            GoalAnimationStyle.CustomVideo => AnimCustomVideoRadio,
            _ => AnimBroadcastRadio
        };
        target.IsChecked = true;
    }

    private GoalAnimationStyle GetSelectedGoalAnimationStyle()
    {
        if (AnimElectricRadio?.IsChecked == true) return GoalAnimationStyle.Electric;
        if (AnimCinematicRadio?.IsChecked == true) return GoalAnimationStyle.Cinematic;
        if (AnimCleanRadio?.IsChecked == true) return GoalAnimationStyle.Clean;
        if (AnimClassicRadio?.IsChecked == true) return GoalAnimationStyle.Classic;
        if (AnimCustomVideoRadio?.IsChecked == true) return GoalAnimationStyle.CustomVideo;
        return GoalAnimationStyle.Broadcast;
    }

    private MediaColor GetTeamColor(bool home)
    {
        string hex = home ? (HomeColorBox.Text ?? string.Empty) : (AwayColorBox.Text ?? string.Empty);
        try
        {
            return (MediaColor)MediaColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return home ? MediaColor.FromRgb(0x2C, 0x66, 0xFF) : MediaColor.FromRgb(0xD3, 0x3D, 0x3D);
        }
        catch (NotSupportedException)
        {
            return home ? MediaColor.FromRgb(0x2C, 0x66, 0xFF) : MediaColor.FromRgb(0xD3, 0x3D, 0x3D);
        }
    }

    private Control? GetCurrentlyVisibleScreen()
    {
        if (_videoPlayer?.IsVisible == true) return _videoPlayer;
        if (_quarterBreakScorebug?.IsVisible == true) return _quarterBreakScorebug;
        if (_breakScreen?.IsVisible == true) return _breakScreen;
        if (_scoreworm?.IsVisible == true) return _scoreworm;
        if (_statsScreen?.IsVisible == true) return _statsScreen;
        if (_weatherScreen?.IsVisible == true) return _weatherScreen;
        if (_scorebug?.IsVisible == true) return _scorebug;
        if (_outputScreensaver?.IsVisible == true) return _outputScreensaver;
        return null;
    }

    private void ApplyLogoCropToScorebug()
    {
        _scorebug?.SetLogoCrop(
            _homeLogoZoom, _homeLogoOffsetX, _homeLogoOffsetY,
            _awayLogoZoom, _awayLogoOffsetX, _awayLogoOffsetY);
    }

    private void ComputeAndShowWinProbability()
    {
        if (_scorebug == null || _showingBreakScreen) return;

        var homeColor = GetTeamColor(true);
        var awayColor = GetTeamColor(false);
        string homeHex = $"#{homeColor.R:X2}{homeColor.G:X2}{homeColor.B:X2}";
        string awayHex = $"#{awayColor.R:X2}{awayColor.G:X2}{awayColor.B:X2}";
        _scorebug.SetWinProbColors(homeHex, awayHex);
        var snapshot = MatchSnapshot.FromMatch(_match);
        WinProbabilityResult result = _winProbEngine.Compute(snapshot);
        _scorebug.ShowWinProbability(result);
    }

    private void LogoCropSlider_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_syncingLogoCropControls) return;

        if (HomeLogoZoomSlider == null || HomeLogoXSlider == null || HomeLogoYSlider == null ||
            AwayLogoZoomSlider == null || AwayLogoXSlider == null || AwayLogoYSlider == null)
        {
            return;
        }

        _homeLogoZoom = HomeLogoZoomSlider.Value;
        _homeLogoOffsetX = HomeLogoXSlider.Value;
        _homeLogoOffsetY = HomeLogoYSlider.Value;
        _awayLogoZoom = AwayLogoZoomSlider.Value;
        _awayLogoOffsetX = AwayLogoXSlider.Value;
        _awayLogoOffsetY = AwayLogoYSlider.Value;

        ApplyLogoCropToScorebug();
        UpdateInlineLogoCropPreviews();
        if (LogoCropSummaryText != null)
        {
            LogoCropSummaryText.Text =
                $"Home: z {_homeLogoZoom:0.00}, x {_homeLogoOffsetX:0}, y {_homeLogoOffsetY:0}    |    " +
                $"Away: z {_awayLogoZoom:0.00}, x {_awayLogoOffsetX:0}, y {_awayLogoOffsetY:0}";
        }

        PushAllToScoreboard();
    }

    private void ResetHomeCrop_Click(object? sender, RoutedEventArgs e)
    {
        HomeLogoZoomSlider.Value = 1.0;
        HomeLogoXSlider.Value = 0;
        HomeLogoYSlider.Value = 0;
    }

    private void ResetAwayCrop_Click(object? sender, RoutedEventArgs e)
    {
        AwayLogoZoomSlider.Value = 1.0;
        AwayLogoXSlider.Value = 0;
        AwayLogoYSlider.Value = 0;
    }

    private void ApplyWindowDimensions_Click(object? sender, RoutedEventArgs e)
    {
        // Floor matches MIN_TRACK_PX (the WM_GETMINMAXINFO override) so the
        // operator can dial the display window all the way down to 50×50 to
        // match the physical scoreboard's tiny render slot. The master Viewbox
        // inside every screen (scorebug, break, scoreworm, stats, weather)
        // keeps the layout, animations and overlays pixel-identical to the
        // 960×540 design canvas — they just render at a much smaller scale.
        if (!int.TryParse((WindowWidthBox.Text ?? string.Empty), out int w) || w < 50 || w > 7680)
        {
            WindowDimensionsStatus.Text = "Width must be 50–7680.";
            return;
        }

        if (!int.TryParse((WindowHeightBox.Text ?? string.Empty), out int h) || h < 50 || h > 4320)
        {
            WindowDimensionsStatus.Text = "Height must be 50–4320.";
            return;
        }

        ApplySharedOutputDimensions(w, h);
        WindowDimensionsStatus.Text = $"All outputs set to {w} × {h} physical pixels";
    }

    private void ApplyTeamsAndStyling_Click(object? sender, RoutedEventArgs e) => ApplyTeamSettingsLive();

    private void TeamSettings_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        // Guard: TextChanged fires during XAML initialization before all controls exist
        if (!IsLoaded) return;
        // Guard: suppress during bulk sync (SyncControlPanelFields) to avoid cascading overwrites
        if (_suppressTeamSync) return;
        ApplyTeamSettingsLive();
    }

    private void ApplyTeamSettingsLive()
    {
        _match.SetTeams((HomeNameBox.Text ?? string.Empty), (HomeAbbrBox.Text ?? string.Empty), (AwayNameBox.Text ?? string.Empty), (AwayAbbrBox.Text ?? string.Empty));
        PushAllToScoreboard();
        ApplyTeamColorsToScoringPanels();
    }

    private void PickHomeColor_Click(object? sender, PointerEventArgs e) => PickColorInto(HomeColorBox, HomeColorPreview);
    private void PickAwayColor_Click(object? sender, PointerEventArgs e) => PickColorInto(AwayColorBox, AwayColorPreview);
    private void PickHomeSecondaryColor_Click(object? sender, PointerEventArgs e) => PickColorInto(HomeSecondaryColorBox, HomeSecondaryColorPreview);
    private void PickAwaySecondaryColor_Click(object? sender, PointerEventArgs e) => PickColorInto(AwaySecondaryColorBox, AwaySecondaryColorPreview);

    private void DropperHomeColor_Click(object? sender, PointerEventArgs e) => DropColorInto(HomeColorBox, HomeColorPreview);
    private void DropperAwayColor_Click(object? sender, PointerEventArgs e) => DropColorInto(AwayColorBox, AwayColorPreview);
    private void DropperHomeSecondaryColor_Click(object? sender, PointerEventArgs e) => DropColorInto(HomeSecondaryColorBox, HomeSecondaryColorPreview);
    private void DropperAwaySecondaryColor_Click(object? sender, PointerEventArgs e) => DropColorInto(AwaySecondaryColorBox, AwaySecondaryColorPreview);

    private void PickColorInto(Avalonia.Controls.TextBox target, Border preview)
    {
        MediaColor initial;
        try { initial = (MediaColor)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString((target.Text ?? string.Empty)); }
        catch { initial = Colors.White; }
        if (!ColorPickerWindow.TryPick(RocheScoreboard.Mac.Native.NativeWindow.GetWindow(this), initial, out MediaColor picked,
                previewColor => ApplyTeamColourPreview(target, preview, previewColor))) return;

        string hex = $"#{picked.R:X2}{picked.G:X2}{picked.B:X2}";
        target.Text = hex;
        preview.Background = new SolidColorBrush(picked);
        ApplyTeamSettingsLive();
        RefreshVisiblePresentationTeamStyle();
    }

    private void ApplyTeamColourPreview(Avalonia.Controls.TextBox target, Border preview, MediaColor color)
    {
        string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        _suppressTeamSync = true;
        try
        {
            if (!string.Equals((target.Text ?? string.Empty), hex, StringComparison.OrdinalIgnoreCase))
                target.Text = hex;
        }
        finally { _suppressTeamSync = false; }

        preview.Background = new SolidColorBrush(color);
        ApplyCurrentTeamColorsToScorebug();
        ApplyTeamColorsToScoringPanels();
        RefreshVisiblePresentationTeamStyle();
    }

    private void DropColorInto(Avalonia.Controls.TextBox target, Border preview)
    {
        MediaColor? picked = EyeDropper.Pick();
        if (picked is null) return;

        MediaColor c = picked.Value;
        RecentColorStorage.Record(c);
        string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        target.Text = hex;
        preview.Background = new SolidColorBrush(c);
        ApplyTeamSettingsLive();
        RefreshVisiblePresentationTeamStyle();
    }

    private void RefreshVisiblePresentationTeamStyle()
    {
        // Secondary colours are also consumed by full-screen presentation
        // controls. Refresh only the currently visible surface so a live colour
        // change is immediate without rebuilding every hidden presentation.
        if (_quarterBreakScorebug?.IsVisible == true) PopulateQuarterBreakScorebug();
        else if (_breakScreen?.IsVisible == true) PopulateBreakScreen();
        else if (_scoreworm?.IsVisible == true) PopulateScoreworm();
        else if (_statsScreen?.IsVisible == true) PopulateStatsScreen();
        else if (_weatherScreen?.IsVisible == true) PopulateWeatherScreen();
    }

    private void BrowseHomeLogo_Click(object? sender, RoutedEventArgs e)
    {
        string? path = BrowseLogoFile();
        if (path is null) return;

        _homeLogoPath = path;
        HomeLogoStatusText.Text = System.IO.Path.GetFileName(path);
        ResetLogoCropState();
        PushAllToScoreboard();
    }

    private void BrowseAwayLogo_Click(object? sender, RoutedEventArgs e)
    {
        string? path = BrowseLogoFile();
        if (path is null) return;

        _awayLogoPath = path;
        AwayLogoStatusText.Text = System.IO.Path.GetFileName(path);
        ResetLogoCropState();
        PushAllToScoreboard();
    }

    private void ClearHomeLogo_Click(object? sender, RoutedEventArgs e)
    {
        _homeLogoPath = null;
        HomeLogoStatusText.Text = "";
        ResetLogoCropState();
        PushAllToScoreboard();
    }

    private void ClearAwayLogo_Click(object? sender, RoutedEventArgs e)
    {
        _awayLogoPath = null;
        AwayLogoStatusText.Text = "";
        ResetLogoCropState();
        PushAllToScoreboard();
    }

    private static string? BrowseLogoFile()
    {
        NativeOpenFileDialog dlg = new()
        {
            Filter = ImageLoadHelper.LogoFilter,
            Title = "Select Team Logo"
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private const int TickerSlotCount = 8;
    private const int TickerMessageMaxLength = 140;
    private bool _normalizingTickerSlots;
    private bool _suppressTickerSync;
    private DispatcherTimer? _tickerStatusResetTimer;
    private (int Index, Roche_Scoreboard.Models.MarqueeMessage Message)? _lastRemovedTickerSlot;

    private void ClearTickerUndoState()
    {
        _lastRemovedTickerSlot = null;
        if (TickerUndoDeleteButton is not null)
            TickerUndoDeleteButton.IsVisible = false;
    }

    private void ResetWindowDimensions_Click(object? sender, RoutedEventArgs e)
    {
        WindowWidthBox.Text = "386";
        WindowHeightBox.Text = "193";
        ApplyWindowDimensions_Click(sender, e);
    }

    private void EnsureTickerSlots()
    {
        if (_normalizingTickerSlots) return;
        _normalizingTickerSlots = true;
        try
        {
            while (_styledMessages.Count > TickerSlotCount)
                _styledMessages.RemoveAt(_styledMessages.Count - 1);
            while (_styledMessages.Count < TickerSlotCount)
                _styledMessages.Add(new Roche_Scoreboard.Models.MarqueeMessage(string.Empty, isEnabled: false));

            foreach (Roche_Scoreboard.Models.MarqueeMessage slot in _styledMessages)
            {
                string normalized = NormalizeTickerText((slot.Text ?? string.Empty));
                if (!string.Equals((slot.Text ?? string.Empty), normalized, StringComparison.Ordinal)) slot.Text = normalized;
                string? textColor = NormalizeTickerColor(slot.TextColor);
                string? highlightColor = NormalizeTickerColor(slot.HighlightColor);
                if (!string.Equals(slot.TextColor, textColor, StringComparison.OrdinalIgnoreCase)) slot.TextColor = textColor;
                if (!string.Equals(slot.HighlightColor, highlightColor, StringComparison.OrdinalIgnoreCase)) slot.HighlightColor = highlightColor;
                if (normalized.Length == 0 && slot.IsEnabled) slot.IsEnabled = false;
            }
        }
        finally
        {
            _normalizingTickerSlots = false;
        }
    }

    private static string NormalizeTickerText(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        string flattened = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        // Preserve ordinary and trailing spaces while the operator is typing.
        // Removing a newly typed trailing space changed the bound value and
        // returned the caret to the start of the field.
        return flattened.Length <= TickerMessageMaxLength
            ? flattened
            : flattened[..TickerMessageMaxLength];
    }

    private static string? NormalizeTickerColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            MediaColor color = (MediaColor)MediaColorConverter.ConvertFromString(value);
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        catch
        {
            return null;
        }
    }

    private void AddMessage_Click(object? sender, RoutedEventArgs e)
    {
        string text = NormalizeTickerText((NewMessageBox.Text ?? string.Empty));
        if (string.IsNullOrWhiteSpace(text)) return;

        string? textColor = _newMessageTextColor;
        string? hi = _newMessageHighlightColor;

        EnsureTickerSlots();
        Roche_Scoreboard.Models.MarqueeMessage? existing = _styledMessages
            .FirstOrDefault(slot => string.Equals((slot.Text ?? string.Empty), text, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.IsEnabled = true;
            SyncTickerMessages();
            SetTickerEditorStatus("MESSAGE ALREADY SAVED · SWITCHED ON", isWarning: false);
            NewMessageBox.Text = string.Empty;
            return;
        }

        Roche_Scoreboard.Models.MarqueeMessage? msg = _styledMessages
            .FirstOrDefault(slot => string.IsNullOrWhiteSpace((slot.Text ?? string.Empty)));
        if (msg is null)
        {
            SetTickerEditorStatus("ALL 8 SLOTS ARE FULL · DELETE ONE TO ADD", isWarning: true);
            return;
        }
        msg.Text = text;
        msg.TextColor = textColor;
        msg.HighlightColor = hi;
        msg.IsEnabled = true;

        NewMessageBox.Text = string.Empty;
        // Keep selected colours for the next message — operators usually want
        // a consistent style across consecutive messages. They can change them
        // any time via the swatch buttons.

        SyncTickerMessages();
        NewMessageBox.Focus();
    }

    private DispatcherTimer? _tickerEditCommitTimer;

    private void SyncTickerMessages(bool persist = true)
    {
        if (_suppressTickerSync || _normalizingTickerSlots) return;
        EnsureTickerSlots();
        _messages.Clear();
        List<Roche_Scoreboard.Models.MarqueeMessage> activeMessages = GetActiveTickerMessages();
        foreach (Roche_Scoreboard.Models.MarqueeMessage message in activeMessages)
            _messages.Add((message.Text ?? string.Empty));

        if (activeMessages.Count > 0)
            CancelQueuedOverlays();
        string? outputError = null;
        try
        {
            _scorebug?.SetMarqueeMessages(activeMessages);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ticker output reset after render failure: {ex.Message}");
            try { _scorebug?.SetMarqueeMessages(Array.Empty<Roche_Scoreboard.Models.MarqueeMessage>()); }
            catch { /* Output fallback must never interfere with scoring. */ }
            outputError = "TICKER OUTPUT RESET · SCOREBOARD REMAINS LIVE";
        }
        UpdateTickerEditorState();
        if (outputError is not null)
            SetTickerEditorStatus(outputError, isWarning: true);
        if (persist)
        {
            try
            {
                PublishCloudSnapshot();
                SaveGameState();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ticker persistence skipped: {ex.Message}");
                SetTickerEditorStatus("LIVE TICKER UPDATED · SAVE WILL RETRY", isWarning: true);
            }
        }
    }

    private void ScheduleTickerEditCommit()
    {
        _tickerEditCommitTimer?.Stop();
        _tickerEditCommitTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _tickerEditCommitTimer.Tick -= TickerEditCommitTimer_Tick;
        _tickerEditCommitTimer.Tick += TickerEditCommitTimer_Tick;
        _tickerEditCommitTimer.Start();
    }

    private void TickerEditCommitTimer_Tick(object? sender, EventArgs e)
    {
        _tickerEditCommitTimer?.Stop();
        try
        {
            PublishCloudSnapshot();
            SaveGameState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ticker edit commit skipped: {ex.Message}");
            SetTickerEditorStatus("LIVE TICKER UPDATED · SAVE WILL RETRY", isWarning: true);
        }
    }

    private List<Roche_Scoreboard.Models.MarqueeMessage> GetActiveTickerMessages()
    {
        return _styledMessages
            .Where(message => message.IsEnabled && !string.IsNullOrWhiteSpace((message.Text ?? string.Empty)))
            .Select(message => new Roche_Scoreboard.Models.MarqueeMessage(
                (message.Text ?? string.Empty).Trim(), message.TextColor, message.HighlightColor, isEnabled: true))
            .ToList();
    }

    private void UpdateTickerEditorState()
    {
        int activeCount = _styledMessages.Count(message =>
            message.IsEnabled && !string.IsNullOrWhiteSpace((message.Text ?? string.Empty)));
        int savedCount = _styledMessages.Count(message => !string.IsNullOrWhiteSpace((message.Text ?? string.Empty)));
        bool hasMessages = activeCount > 0;
        if (TickerEmptyState is not null)
            TickerEmptyState.IsVisible = false;
        if (TickerMessageCountText is not null)
        {
            TickerMessageCountText.Text = $"{activeCount} ON AIR · {savedCount} SAVED";
            TickerMessageCountText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x75, 0xE1, 0x8A));
        }
        if (TickerClearAllButton is not null)
            TickerClearAllButton.IsEnabled = _styledMessages.Any(message => !string.IsNullOrWhiteSpace((message.Text ?? string.Empty)));
        if (TickerDisableAllButton is not null)
            TickerDisableAllButton.IsEnabled = hasMessages;
    }

    private void SetTickerEditorStatus(string message, bool isWarning)
    {
        if (TickerMessageCountText is null) return;
        _tickerStatusResetTimer?.Stop();
        TickerMessageCountText.Text = message;
        TickerMessageCountText.Foreground = new SolidColorBrush(isWarning
            ? MediaColor.FromRgb(0xFF, 0xBE, 0x67)
            : MediaColor.FromRgb(0x86, 0xEF, 0xAC));
        _tickerStatusResetTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _tickerStatusResetTimer.Tick -= TickerStatusResetTimer_Tick;
        _tickerStatusResetTimer.Tick += TickerStatusResetTimer_Tick;
        _tickerStatusResetTimer.Start();
    }

    private void TickerStatusResetTimer_Tick(object? sender, EventArgs e)
    {
        _tickerStatusResetTimer?.Stop();
        UpdateTickerEditorState();
    }

    private void TickerTemplate_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Button { Tag: string template }) return;
        EnsureTickerSlots();
        Roche_Scoreboard.Models.MarqueeMessage? existing = _styledMessages
            .FirstOrDefault(message => string.Equals((message.Text ?? string.Empty), template, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.IsEnabled = true;
            SyncTickerMessages();
            SetTickerEditorStatus("TEMPLATE ALREADY SAVED · SWITCHED ON", isWarning: false);
            return;
        }

        Roche_Scoreboard.Models.MarqueeMessage? slot = _styledMessages
            .FirstOrDefault(message => string.IsNullOrWhiteSpace((message.Text ?? string.Empty)));
        if (slot is null)
        {
            SetTickerEditorStatus("ALL 8 SLOTS ARE FULL · DELETE ONE TO ADD", isWarning: true);
            return;
        }
        slot.Text = NormalizeTickerText(template);
        slot.IsEnabled = true;
        SyncTickerMessages();
    }

    private void DisableAllTickerMessages_Click(object? sender, RoutedEventArgs e)
    {
        foreach (Roche_Scoreboard.Models.MarqueeMessage slot in _styledMessages)
            slot.IsEnabled = false;
        SyncTickerMessages();
    }

    private void NewMessageBox_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        if (TickerCharacterCountText is null || NewMessageBox is null) return;
        int count = (NewMessageBox.Text ?? string.Empty)?.Length ?? 0;
        TickerCharacterCountText.Text = $"{count} / 140 · ENTER TO ADD";
        TickerCharacterCountText.Foreground = count >= 126
            ? new SolidColorBrush(MediaColor.FromRgb(255, 185, 80))
            : new SolidColorBrush(MediaColor.FromRgb(115, 132, 151));
    }

    private void ClearTickerMessages_Click(object? sender, RoutedEventArgs e)
    {
        if (!_styledMessages.Any(message => !string.IsNullOrWhiteSpace((message.Text ?? string.Empty)))) return;

        NativeMessageBoxResult result = RocheScoreboard.Mac.Native.NativeMessageBox.Show(
            this,
            "Remove every ticker message from the live rotation?",
            "Clear ticker messages",
            NativeMessageBoxButton.YesNo,
            NativeMessageBoxImage.Question,
            NativeMessageBoxResult.No);
        if (result != NativeMessageBoxResult.Yes) return;

        foreach (Roche_Scoreboard.Models.MarqueeMessage slot in _styledMessages)
        {
            slot.Text = string.Empty;
            slot.TextColor = null;
            slot.HighlightColor = null;
            slot.IsEnabled = false;
        }
        ClearTickerUndoState();
        SyncTickerMessages();
    }

    // ── Colour-picker state for the "new message" row ──
    private string? _newMessageTextColor;       // null = default white
    private string? _newMessageHighlightColor;  // null = no highlight

    private static string ToHex(System.Drawing.Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static System.Drawing.Color ParseDrawingColor(string? hex, System.Drawing.Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try
        {
            var media = (MediaColor)MediaColorConverter.ConvertFromString(hex);
            return System.Drawing.Color.FromArgb(media.R, media.G, media.B);
        }
        catch
        {
            return fallback;
        }
    }

    private static bool TryPickColor(System.Drawing.Color initial, out System.Drawing.Color picked)
    {
        MediaColor mediaInitial = MediaColor.FromRgb(initial.R, initial.G, initial.B);
        bool accepted = ColorPickerWindow.TryPick(NativeDesktop.MainWindow,
            mediaInitial, out MediaColor mediaPicked);
        picked = accepted
            ? System.Drawing.Color.FromArgb(mediaPicked.R, mediaPicked.G, mediaPicked.B)
            : initial;
        return accepted;
    }

    private void PickNewTextColor_Click(object? sender, RoutedEventArgs e)
    {
        var initial = ParseDrawingColor(_newMessageTextColor, System.Drawing.Color.White);
        if (!TryPickColor(initial, out var picked)) return;
        _newMessageTextColor = ToHex(picked);
        if (NewMessageTextColorBtn != null)
            NewMessageTextColorBtn.Background = new SolidColorBrush(MediaColor.FromRgb(picked.R, picked.G, picked.B));
    }

    private void PickNewHighlightColor_Click(object? sender, RoutedEventArgs e)
    {
        var initial = ParseDrawingColor(_newMessageHighlightColor, System.Drawing.Color.FromArgb(255, 30, 64, 175));
        if (!TryPickColor(initial, out var picked)) return;
        _newMessageHighlightColor = ToHex(picked);
        if (NewMessageHighlightColorBtn != null)
            NewMessageHighlightColorBtn.Background = new SolidColorBrush(MediaColor.FromRgb(picked.R, picked.G, picked.B));
    }

    private void ClearNewHighlightColor_Click(object? sender, PointerEventArgs e)
    {
        _newMessageHighlightColor = null;
        if (NewMessageHighlightColorBtn != null)
            NewMessageHighlightColorBtn.Background = MediaBrushes.Transparent;
    }

    private void PickItemTextColor_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Button btn) return;
        if (btn.Tag is not Roche_Scoreboard.Models.MarqueeMessage msg) return;
        var initial = ParseDrawingColor(msg.TextColor, System.Drawing.Color.White);
        if (!TryPickColor(initial, out var picked)) return;
        msg.TextColor = ToHex(picked);
        SyncTickerMessages();
    }

    private void PickItemHighlightColor_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Button btn) return;
        if (btn.Tag is not Roche_Scoreboard.Models.MarqueeMessage msg) return;
        var initial = ParseDrawingColor(msg.HighlightColor, System.Drawing.Color.FromArgb(255, 30, 64, 175));
        if (!TryPickColor(initial, out var picked)) return;
        msg.HighlightColor = ToHex(picked);
        SyncTickerMessages();
    }

    private void ClearItemHighlightColor_Click(object? sender, PointerEventArgs e)
    {
        if (sender is not Avalonia.Controls.Button btn) return;
        if (btn.Tag is not Roche_Scoreboard.Models.MarqueeMessage msg) return;
        msg.HighlightColor = null;
        SyncTickerMessages();
    }

    private void RemoveMessageItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Button btn) return;
        if (btn.Tag is not Roche_Scoreboard.Models.MarqueeMessage msg) return;

        int index = _styledMessages.IndexOf(msg);
        if (index >= 0 && !string.IsNullOrWhiteSpace((msg.Text ?? string.Empty)))
        {
            _lastRemovedTickerSlot = (index, new Roche_Scoreboard.Models.MarqueeMessage(
                (msg.Text ?? string.Empty), msg.TextColor, msg.HighlightColor, msg.IsEnabled));
            if (TickerUndoDeleteButton is not null)
                TickerUndoDeleteButton.IsVisible = true;
        }

        msg.Text = string.Empty;
        msg.TextColor = null;
        msg.HighlightColor = null;
        msg.IsEnabled = false;
        SyncTickerMessages();
        SetTickerEditorStatus("MESSAGE DELETED · UNDO AVAILABLE", isWarning: false);
    }

    private void UndoTickerDelete_Click(object? sender, RoutedEventArgs e)
    {
        if (_lastRemovedTickerSlot is not { } removed ||
            removed.Index < 0 || removed.Index >= _styledMessages.Count)
            return;

        Roche_Scoreboard.Models.MarqueeMessage target = _styledMessages[removed.Index];
        if (!string.IsNullOrWhiteSpace((target.Text ?? string.Empty)))
        {
            SetTickerEditorStatus("UNDO SLOT IS NOW IN USE", isWarning: true);
            return;
        }

        target.Text = removed.Message.Text;
        target.TextColor = removed.Message.TextColor;
        target.HighlightColor = removed.Message.HighlightColor;
        target.IsEnabled = removed.Message.IsEnabled;
        _lastRemovedTickerSlot = null;
        TickerUndoDeleteButton.IsVisible = false;
        SyncTickerMessages();
        SetTickerEditorStatus("MESSAGE RESTORED", isWarning: false);
    }

    /// <summary>
    /// Legacy handler kept for backward compatibility (e.g. ✕ button bound by index).
    /// New UI uses <see cref="RemoveMessageItem_Click"/>.
    /// </summary>
    private void RemoveMessage_Click(object? sender, RoutedEventArgs e)
    {
        Roche_Scoreboard.Models.MarqueeMessage? last = _styledMessages
            .LastOrDefault(message => !string.IsNullOrWhiteSpace((message.Text ?? string.Empty)));
        if (last is null) return;
        last.Text = string.Empty;
        last.IsEnabled = false;
        SyncTickerMessages();
    }

    private void TickerSlotEnabled_Changed(object? sender, RoutedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Primitives.ToggleButton { DataContext: Roche_Scoreboard.Models.MarqueeMessage slot }) return;
        if (slot.IsEnabled && string.IsNullOrWhiteSpace((slot.Text ?? string.Empty)))
        {
            slot.IsEnabled = false;
            SetTickerEditorStatus("TYPE A MESSAGE BEFORE SWITCHING IT ON", isWarning: true);
            return;
        }
        SyncTickerMessages();
    }

    private void MessageItem_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        if (_suppressTickerSync || _normalizingTickerSlots) return;
        if (sender is not Avalonia.Controls.TextBox box) return;
        int caret = box.CaretIndex;
        if (box.DataContext is not Roche_Scoreboard.Models.MarqueeMessage slot) return;
        // TextChanged can run before the TwoWay binding has copied the new
        // target value into the model.  Synchronising at that point used the
        // previous word, pushed it back into the TextBox and moved the caret
        // to the front whenever Space was pressed.  Make the edit authoritative
        // before rebuilding the public ticker list.
        string editedText = NormalizeTickerText((box.Text ?? string.Empty));
        if (!string.Equals((slot.Text ?? string.Empty), editedText, StringComparison.Ordinal))
            slot.Text = editedText;
        if (string.IsNullOrWhiteSpace(editedText)) slot.IsEnabled = false;
        // Keep the plain-text mirror in sync with the styled collection so persistence
        // and any string-based callers see the latest text.
        SyncTickerMessages(persist: false);
        ScheduleTickerEditCommit();
        Dispatcher.UIThread.Post(new Action(() =>
        {
            if (box.IsKeyboardFocusWithin)
                box.CaretIndex = Math.Min(caret, (box.Text ?? string.Empty).Length);
        }), DispatcherPriority.Input);
    }

    private void NewMessageBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        AddMessage_Click(sender, new RoutedEventArgs());
        e.Handled = true;
    }

    private void QuarterDurationBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyQuarterDuration_Click(sender, new RoutedEventArgs());
        e.Handled = true;
    }

    private void ClockMode_Changed(object? sender, RoutedEventArgs e)
    {
        if (ClockModeCountdown == null || QuarterDurationPanel == null || ClockModeCountUp == null)
        {
            return;
        }

        bool wasRunning = _match.ClockRunning;
        bool wasCountUp = _match.ClockMode == ClockMode.CountUp;
        TimeSpan previousElapsed = _match.ElapsedInQuarter;
        TimeSpan previousQuarterDuration = _match.QuarterDuration;

        if (wasRunning)
        {
            _match.PauseClock();
        }

        if (ClockModeCountdown.IsChecked == true)
        {
            _match.SetClockMode(ClockMode.Countdown);
            QuarterDurationPanel.IsVisible = true;

            // Keep current game-time position where possible when switching mid-quarter.
            var currentDuration = TimeSpan.FromMinutes(int.TryParse((QuarterMinutesBox.Text ?? string.Empty), out int mins) ? Math.Clamp(mins, 0, 60) : previousQuarterDuration.Minutes)
                                + TimeSpan.FromSeconds(int.TryParse((QuarterSecondsBox.Text ?? string.Empty), out int secs) ? Math.Clamp(secs, 0, 59) : previousQuarterDuration.Seconds);
            if (currentDuration <= TimeSpan.Zero)
                currentDuration = previousQuarterDuration > TimeSpan.Zero ? previousQuarterDuration : TimeSpan.FromMinutes(20);

            _match.SetQuarterDuration(currentDuration);

            if (wasCountUp)
            {
                // Elapsed timeline can carry directly into countdown mode.
                var clampedElapsed = previousElapsed > currentDuration ? currentDuration : previousElapsed;
                _match.SetElapsed(clampedElapsed);
            }

            PauseControlPanelCountdown(forceAutoPausedFlag: false);
            _showControlPanelCountdown = false;
        }
        else
        {
            _match.SetClockMode(ClockMode.CountUp);
            QuarterDurationPanel.IsVisible = false;

            var settingsShowCountdownToggle = FindElement<Avalonia.Controls.CheckBox>("SettingsShowControlPanelCountdown");
            _showControlPanelCountdown = settingsShowCountdownToggle?.IsChecked == true;

            if (_controlPanelCountdownDuration <= TimeSpan.Zero)
            {
                _controlPanelCountdownDuration = previousQuarterDuration > TimeSpan.Zero ? previousQuarterDuration : TimeSpan.FromMinutes(20);
            }

            if (!wasCountUp)
            {
                // Preserve elapsed continuity when moving from countdown -> count up.
                _match.SetElapsed(previousElapsed);
                _controlPanelCountdownRemaining = _controlPanelCountdownDuration;
            }
            else if (!_controlPanelCountdownRunning)
            {
                // Safe fallback if count-up mode reselected mid-quarter.
                _controlPanelCountdownRemaining = _controlPanelCountdownDuration;
            }
        }

        if (wasRunning)
        {
            _match.StartClock();
            if (_match.ClockMode == ClockMode.CountUp && _showControlPanelCountdown && _controlPanelCountdownRemaining > TimeSpan.Zero)
            {
                _controlPanelCountdownRunning = true;
                _countdownNextTick = DateTime.UtcNow.AddSeconds(1);
                _controlPanelCountdownAutoPausedClock = false;
            }
        }

        if (ClockStatusText != null)
        {
            UpdateClockStatus();
        }

        UpdateSettingsClockCards();
        UpdateSettingsControlPanelCountdownOptionVisuals();
        UpdateControlPanelCountdownUi();
        PushAllToScoreboard();
        BroadcastWebState();
    }

    private void ApplyQuarterDuration_Click(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse((QuarterMinutesBox.Text ?? string.Empty), out int mins)) return;
        if (!int.TryParse((QuarterSecondsBox.Text ?? string.Empty), out int secs)) return;
        if (mins < 0 || mins > 60 || secs < 0 || secs > 59) return;
        if (mins == 0 && secs == 0) return;

        var duration = TimeSpan.FromMinutes(mins) + TimeSpan.FromSeconds(secs);
        _match.SetQuarterDuration(duration);
        _controlPanelCountdownDuration = duration;
        if (!_controlPanelCountdownRunning)
        {
            _controlPanelCountdownRemaining = duration;
            _controlPanelCountdownAutoPausedClock = false;
        }
        UpdateClockStatus();
        PushAllToScoreboard();
        BroadcastWebState();
    }

    private void BrowseVideo_Click(object? sender, RoutedEventArgs e)
    {
        // Legacy entry point. The dedicated VIDEO tab now owns the upload
        // workflow — route any external callers to the new flow.
        VideoTabBrowse_Click(sender, e);
    }

    private static string FormatFileSize(long bytes)
    {
        const double KB = 1024d;
        const double MB = KB * 1024;
        const double GB = MB * 1024;
        if (bytes >= GB) return $"{bytes / GB:F2} GB";
        if (bytes >= MB) return $"{bytes / MB:F1} MB";
        if (bytes >= KB) return $"{bytes / KB:F0} KB";
        return $"{bytes} B";
    }

    private void VideoPlayer_PlaybackStateChanged(object? sender, EventArgs e)
    {
        // Marshal in case event was raised from the media thread.
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(new Action(UpdateVideoTabPlayPauseButton));
            return;
        }
        UpdateVideoTabPlayPauseButton();
        SyncPreviewToScoreboard();
    }

    private void VideoPlayer_MediaLoadFailed(object? sender, Roche_Scoreboard.Views.VideoLoadFailedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(new Action(() => VideoPlayer_MediaLoadFailed(sender, e)));
            return;
        }

        if (VideoTabPlayPauseButton != null) VideoTabPlayPauseButton.IsEnabled = false;
        string reason = e.ErrorException?.Message ?? "unknown error";
        if (VideoTabStatusText != null) VideoTabStatusText.Text = "Failed to load: " + reason;
    }


    // ──────────────────────────────────────────────────────────────────────
    // Dedicated VIDEO tab handlers. These wrap the existing video workflow
    // with a modern step-by-step UI and an inline local preview.
    // ──────────────────────────────────────────────────────────────────────
    private void TryRestoreLastVideoPath()
    {
        try
        {
            string? saved = Services.DisplaySettings.LastVideoPath;
            if (string.IsNullOrWhiteSpace(saved) || !System.IO.File.Exists(saved))
                return;

            _customVideoPath = saved;
            VideoTabFilePathText.Text = System.IO.Path.GetFileName(_customVideoPath);
            VideoTabFilePathText.Foreground = (Avalonia.Media.IBrush)new NativeBrushConverter().ConvertFromString("#E6EDF3")!;

            try
            {
                var info = new System.IO.FileInfo(_customVideoPath);
                VideoTabFileMetaText.Text = $"{FormatFileSize(info.Length)} · {info.Extension.TrimStart('.').ToUpperInvariant()} · {info.Name}";
            }
            catch
            {
                VideoTabFileMetaText.Text = string.Empty;
            }

            if (VideoTabApplyButton != null) VideoTabApplyButton.IsEnabled = true;

            // Re-load the saved clip into the scoreboard player on startup so
            // every control button (Play, Restart, Show on Scoreboard, Unload)
            // is immediately usable. Previously only the hidden Apply button
            // was enabled here, which left the operator with disabled buttons
            // and no obvious way forward after an app restart.
            _ = Roche_Scoreboard.Services.VideoOptimizer.EnsureOptimisedAsync(_customVideoPath);

            // Mark as pending so the first activation of the VIDEO tab will
            // load it. We don't load it right now because Apply forces the
            // scoreboard window to open, which we don't want to do during
            // app start-up before the operator has picked a layout.
            _pendingVideoRestoreLoad = true;
            if (VideoTabUnloadButton != null) VideoTabUnloadButton.IsEnabled = true;
            VideoTabStatusText.Text = "Restored last video · open the VIDEO tab to load.";
        }
        catch
        {
            // Restoring the last video is best-effort; never block startup.
        }
    }

    private void VideoTabBrowse_Click(object? sender, RoutedEventArgs e)
    {
        string videoExts = "*.mp4;*.m4v;*.mov;*.mkv;*.webm;*.avi;*.wmv;*.flv;"
                         + "*.mpg;*.mpeg;*.ts;*.mts;*.m2ts;*.3gp;*.3g2;*.ogv;*.vob;*.asf";
        string imageExts = "*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp";

        if (_breakScreenDelayTimer != null)
        {
            _breakScreenDelayTimer.Stop();
            _breakScreenDelayTimer = null;
        }
        StopBreakRotation();

        NativeOpenFileDialog picker = new()
        {
            Title = "Select Media File",
            Filter = $"Media Files|{videoExts};{imageExts}|Video Files|{videoExts}|Image Files|{imageExts}|All Files|*.*",
            CheckFileExists = true,
            DereferenceLinks = true,
            Multiselect = false
        };

        if (picker.ShowDialog() != true) return;

        _customVideoPath = picker.FileName;
        string pickedExtension = System.IO.Path.GetExtension(_customVideoPath);
        _customMediaIsImage = !string.IsNullOrWhiteSpace(pickedExtension) &&
            (pickedExtension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || pickedExtension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || pickedExtension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || pickedExtension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || pickedExtension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || pickedExtension.Equals(".tif", StringComparison.OrdinalIgnoreCase)
            || pickedExtension.Equals(".tiff", StringComparison.OrdinalIgnoreCase)
            || pickedExtension.Equals(".webp", StringComparison.OrdinalIgnoreCase));

        VideoTabFilePathText.Text = System.IO.Path.GetFileName(_customVideoPath);
        VideoTabFilePathText.Foreground = (Avalonia.Media.IBrush)new NativeBrushConverter().ConvertFromString("#E6EDF3")!;

        // Persist so the VIDEO tab can restore this clip across app restarts.
        try { Services.DisplaySettings.LastVideoPath = _customVideoPath; } catch { /* best-effort */ }

        if (!_customMediaIsImage)
        {
            _ = Roche_Scoreboard.Services.VideoOptimizer.EnsureOptimisedAsync(_customVideoPath);
        }

        try
        {
            var info = new System.IO.FileInfo(_customVideoPath);
            string mediaType = _customMediaIsImage ? "IMAGE" : "VIDEO";
            VideoTabFileMetaText.Text = $"{FormatFileSize(info.Length)} · {mediaType} · {info.Extension.TrimStart('.').ToUpperInvariant()} · {info.Name}";
        }
        catch
        {
            VideoTabFileMetaText.Text = string.Empty;
        }

        VideoTabApplyButton.IsEnabled = true;
        VideoTabStatusText.Text = _customMediaIsImage
            ? "✓ Image loaded · ready to show on scoreboard."
            : "✓ Video loaded · ready to cue.";

        // Auto-load into the scoreboard player so the operator only has to
        // click "Show on Scoreboard" next. This collapses the old three-step
        // Browse → Load → Show workflow into a single linear path.
        VideoTabApply_Click(this, new RoutedEventArgs());
    }

    private void VideoTabApply_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_customVideoPath) || !System.IO.File.Exists(_customVideoPath))
        {
            VideoTabStatusText.Text = "File not found.";
            return;
        }

        EnsureScoreboardWindow();
        if (_videoPlayer == null) return;

        if (_breakScreenDelayTimer != null)
        {
            _breakScreenDelayTimer.Stop();
            _breakScreenDelayTimer = null;
        }
        StopBreakRotation();

        _videoPlayer.PlaybackStateChanged -= VideoPlayer_PlaybackStateChanged;
        _videoPlayer.PlaybackStateChanged += VideoPlayer_PlaybackStateChanged;
        _videoPlayer.MediaLoadFailed -= VideoPlayer_MediaLoadFailed;
        _videoPlayer.MediaLoadFailed += VideoPlayer_MediaLoadFailed;

        // SetVideoSource is deliberately followed by an explicit pause; the
        // operator remains in control of when media reaches the public output.
        _videoPlayer.SetVideoSource(_customVideoPath);
        if (_customMediaIsImage)
        {
            _videoPlayer.Pause();
        }

        VideoTabPlayPauseButton.IsEnabled = !_customMediaIsImage;
        VideoTabRestartButton.IsEnabled = !_customMediaIsImage;
        if (VideoTabShowOnScoreboardButton != null) VideoTabShowOnScoreboardButton.IsEnabled = true;
        if (VideoTabUnloadButton != null) VideoTabUnloadButton.IsEnabled = true;
        EnsureVideoTabPreviewSyncTimer();
        UpdateVideoTabPlayPauseButton();
        VideoTabStatusText.Text = _customMediaIsImage
            ? "Image loaded. Use \"Show on Scoreboard\" to display it."
            : "Loaded · paused on first frame. Use \"Show on Scoreboard\" to broadcast.";
    }

    private void VideoTabShowOnScoreboard_Click(object? sender, RoutedEventArgs e)
    {
        EnsureScoreboardWindow();
        if (_videoPlayer is null || !_videoPlayer.HasSource)
        {
            VideoTabStatusText.Text = "Load a video first.";
            return;
        }

        // Use the formal takeover entry so the previous screen is captured
        // and Unload / EndReached can restore it. SwitchToScreen alone
        // doesn't set _videoTakeoverActive, which used to leave the operator
        // stuck on the video surface after pressing Unload.
        EnterVideoTakeover();

        // Switching to the video surface intentionally does NOT auto-play.
        // The clip is shown paused on its current frame; the operator must
        // explicitly press Play (or Restart) to start broadcast playback.
        try
        {
            _videoPlayer.Pause();
        }
        catch { /* ignore — VLC may still be wiring up */ }

        UpdateActiveDisplayLabel("Custom Video");
        SetSelectedPreview("presentation:video");
        UpdateVideoTabPlayPauseButton();
        VideoTabStatusText.Text = _customMediaIsImage
            ? "Image is live on scoreboard."
            : "Showing on scoreboard · paused. Press Play to start.";
    }

    private void VideoTabPlayPause_Click(object? sender, RoutedEventArgs e)
    {
        if (_videoPlayer is null || !_videoPlayer.HasSource)
        {
            VideoTabStatusText.Text = "Load a media file first.";
            return;
        }

        if (_customMediaIsImage)
        {
            VideoTabStatusText.Text = "Images are static. Use Show on Scoreboard to display.";
            return;
        }

        _videoPlayer.TogglePlayPause();
        UpdateVideoTabPlayPauseButton();
    }

    private void VideoTabRestart_Click(object? sender, RoutedEventArgs e)
    {
        if (_videoPlayer is null || !_videoPlayer.HasSource)
        {
            VideoTabStatusText.Text = "Load a video first.";
            return;
        }

        try
        {
            _videoPlayer.Seek(TimeSpan.Zero);
            _videoPlayer.Play();
            VideoTabStatusText.Text = "Restarted from beginning.";
        }
        catch
        {
            VideoTabStatusText.Text = "Could not restart video.";
        }
        UpdateVideoTabPlayPauseButton();
    }

    private void VideoTabUnload_Click(object? sender, RoutedEventArgs e)
    {
        // If the scoreboard is currently broadcasting the video, drop back to
        // the previous screen before tearing the source down so the operator
        // doesn't see a frozen last frame.
        if (_videoTakeoverActive)
        {
            ExitVideoTakeover();
        }

        try { _videoPlayer?.Stop(); } catch { /* control may be unloading */ }

        StopVideoTabPreviewSyncTimer();

        _customVideoPath = null;
        try { Services.DisplaySettings.LastVideoPath = null; } catch { /* best-effort */ }

        if (VideoTabFilePathText != null)
        {
            VideoTabFilePathText.Text = "No file selected";
            VideoTabFilePathText.Foreground = (Avalonia.Media.IBrush)new NativeBrushConverter().ConvertFromString("#5080AA")!;
        }
        if (VideoTabFileMetaText != null)
        {
            VideoTabFileMetaText.Text = "Supported: mp4, mov, mkv, webm, avi, wmv, m4v, ts, mpg, mpeg, 3gp, flv, png, jpg, jpeg, bmp, gif, tif, tiff, webp and other Windows-supported formats.";
        }

        if (VideoTabApplyButton != null) VideoTabApplyButton.IsEnabled = false;
        if (VideoTabShowOnScoreboardButton != null) VideoTabShowOnScoreboardButton.IsEnabled = false;
        if (VideoTabPlayPauseButton != null) VideoTabPlayPauseButton.IsEnabled = false;
        if (VideoTabRestartButton != null) VideoTabRestartButton.IsEnabled = false;
        if (VideoTabUnloadButton != null) VideoTabUnloadButton.IsEnabled = false;

        if (VideoTabSeekSlider != null)
        {
            _videoTabSuppressSeekFeedback = true;
            VideoTabSeekSlider.Value = 0;
            VideoTabSeekSlider.Maximum = 1;
            _videoTabSuppressSeekFeedback = false;
        }
        if (VideoTabCurrentTimeText != null) VideoTabCurrentTimeText.Text = FormatTime(TimeSpan.Zero);
        if (VideoTabDurationText != null) VideoTabDurationText.Text = FormatTime(TimeSpan.Zero);

        if (VideoTabStatusText != null) VideoTabStatusText.Text = "Video unloaded.";
        UpdateVideoTabPlayPauseButton();
    }

    private void UpdateVideoTabPlayPauseButton()
    {
        if (VideoTabPlayPauseButton is null) return;

        bool playing = _videoPlayer?.IsPlaying == true;
        var conv = new NativeBrushConverter();
        if (playing)
        {
            VideoTabPlayPauseButton.Content = "⏸  Pause";
            VideoTabPlayPauseButton.BorderBrush = (Avalonia.Media.IBrush)conv.ConvertFromString("#7A4A1F")!;
            VideoTabPlayPauseButton.Background = (Avalonia.Media.IBrush)conv.ConvertFromString("#2A1A0E")!;
            VideoTabPlayPauseButton.Foreground = (Avalonia.Media.IBrush)conv.ConvertFromString("#F0C040")!;
            VideoTabStatusText.Text = "Playing.";
        }
        else
        {
            VideoTabPlayPauseButton.Content = "▶  Play";
            VideoTabPlayPauseButton.BorderBrush = (Avalonia.Media.IBrush)conv.ConvertFromString("#1F7A3F")!;
            VideoTabPlayPauseButton.Background = (Avalonia.Media.IBrush)conv.ConvertFromString("#0E2A18")!;
            VideoTabPlayPauseButton.Foreground = (Avalonia.Media.IBrush)conv.ConvertFromString("#7FE0A0")!;
            if (_videoPlayer?.HasSource == true)
                VideoTabStatusText.Text = "Paused.";
        }
    }

    // ── YouTube-style preview controls ──────────────────────────────────────

    private bool _videoTabSeekIsDragging;
    private bool _videoTabSuppressSeekFeedback;
    private bool _videoTabPositionHandlerHooked;
    // Set by TryRestoreLastVideoPath when a path was loaded from settings but
    // the actual VLC source load has been deferred until the operator first
    // visits the VIDEO tab (to avoid forcing the scoreboard window open on
    // app start-up). Consumed by MainTabControl_SelectionChanged.
    private bool _pendingVideoRestoreLoad;

    internal void HandleVideoTabActivated()
    {
        if (!_pendingVideoRestoreLoad) return;
        _pendingVideoRestoreLoad = false;
        if (string.IsNullOrWhiteSpace(_customVideoPath) || !System.IO.File.Exists(_customVideoPath)) return;
        try
        {
            VideoTabApply_Click(this, new RoutedEventArgs());
            if (VideoTabStatusText != null)
                VideoTabStatusText.Text = "Restored last video · paused on first frame.";
        }
        catch
        {
            // Leave the path visible so the operator can retry via Browse.
        }
    }

    // The seek/time UI is driven by VLC's TimeChanged event surfaced via
    // VideoPlayerControl.PositionChanged. That's 4 Hz of work *only* when
    // the decoder is actually advancing — no DispatcherTimer waking the UI
    // thread, no polling cost on idle/paused media. The handler is itself
    // coalesced inside the player, so bursts after a seek don't queue up.
    private void EnsureVideoTabPreviewSyncTimer()
    {
        if (_videoPlayer is null || _videoTabPositionHandlerHooked) return;
        _videoPlayer.PositionChanged += VideoPlayer_PositionChanged;
        _videoTabPositionHandlerHooked = true;
        // Push one immediate sync so the slider/clock reflect frame zero.
        SyncPreviewToScoreboard();
    }

    private void StopVideoTabPreviewSyncTimer()
    {
        if (_videoPlayer != null && _videoTabPositionHandlerHooked)
        {
            _videoPlayer.PositionChanged -= VideoPlayer_PositionChanged;
        }
        _videoTabPositionHandlerHooked = false;
    }

    // Throttle PositionChanged-driven UI sync. VLC's TimeChanged can fire
    // several times per second; the slider/clock only need ~4 Hz to feel
    // smooth to an operator. Anything tighter wastes UI-thread cycles
    // during active playback, which is precisely when we want WPF idle.
    private long _lastPositionSyncTicks;
    private const long PositionSyncThrottleTicks = 2_500_000; // 250 ms

    private void VideoPlayer_PositionChanged(object? sender, EventArgs e)
    {
        // Skip the UI refresh while the operator is dragging — their slider
        // position is the source of truth until they release.
        if (_videoTabSeekIsDragging) return;

        long now = DateTime.UtcNow.Ticks;
        if (now - _lastPositionSyncTicks < PositionSyncThrottleTicks) return;
        _lastPositionSyncTicks = now;

        SyncPreviewToScoreboard();
    }

    private void SyncPreviewToScoreboard()
    {
        if (_videoPlayer is null || !_videoPlayer.HasSource) return;

        TimeSpan duration = _videoPlayer.Duration;
        TimeSpan position = _videoPlayer.Position;

        if (VideoTabSeekSlider != null && duration > TimeSpan.Zero)
        {
            double totalSec = duration.TotalSeconds;
            if (Math.Abs(VideoTabSeekSlider.Maximum - totalSec) > 0.05)
            {
                VideoTabSeekSlider.Maximum = totalSec;
            }
            if (!_videoTabSeekIsDragging)
            {
                _videoTabSuppressSeekFeedback = true;
                VideoTabSeekSlider.Value = Math.Min(position.TotalSeconds, totalSec);
                _videoTabSuppressSeekFeedback = false;
            }
        }

        if (VideoTabCurrentTimeText != null)
            VideoTabCurrentTimeText.Text = FormatTime(position);
        if (VideoTabDurationText != null && duration > TimeSpan.Zero)
            VideoTabDurationText.Text = FormatTime(duration);

        UpdateVideoTabTelemetryText();
    }

    /// <summary>
    /// Refreshes the lightweight telemetry HUD overlaid on the VIDEO tab
    /// preview. Reads cheap state from the player (decoder mode, playback
    /// state, takeover state) so the operator can spot stalls or HW->SW
    /// fallbacks at a glance.
    /// </summary>
    private void UpdateVideoTabTelemetryText()
    {
        if (VideoTabTelemetryText is null || _videoPlayer is null) return;
        string mode = _videoPlayer.IsHardwareDecoding ? "HW DECODE" : "SW DECODE";
        string state = _videoPlayer.IsPlaying ? "PLAYING" : (_videoPlayer.HasSource ? "PAUSED" : "IDLE");
        string takeover = _videoTakeoverActive ? "TAKEOVER" : "PREVIEW";
        string fallback = _videoPlayer.SoftwareFallbackAttempted ? " · SW FALLBACK USED" : string.Empty;
        VideoTabTelemetryText.Text = $"{mode} · {state} · {takeover}{fallback}";

        if (VideoTabDiagnosticText is not null)
        {
            Roche_Scoreboard.Services.VideoDiagnosticEntry? last = Roche_Scoreboard.Services.VideoDiagnostics.Last;
            if (last is null)
            {
                VideoTabDiagnosticText.Text = string.Empty;
            }
            else if (last.Severity == Roche_Scoreboard.Services.VideoLogSeverity.Info)
            {
                VideoTabDiagnosticText.Text = string.Empty;
            }
            else
            {
                VideoTabDiagnosticText.Text = $"{last.Severity}: {last.Category} · {last.Message}";
            }
        }
    }

    private static string FormatTime(TimeSpan t)
    {
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}";
        return $"{t.Minutes}:{t.Seconds:D2}";
    }

    private void VideoTabSeek_DragStarted(object? sender, Avalonia.Input.VectorEventArgs e)
    {
        _videoTabSeekIsDragging = true;
    }

    private void VideoTabSeek_DragCompleted(object? sender, Avalonia.Input.VectorEventArgs e)
    {
        _videoTabSeekIsDragging = false;
        ApplySeekFromSlider();
    }

    private void VideoTabSeek_PreviewMouseLeftButtonUp(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        // Click-to-seek (IsMoveToPointEnabled) — apply the new position
        // immediately so it feels like YouTube's instant scrub.
        ApplySeekFromSlider();
    }

    private void VideoTabSeek_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_videoTabSuppressSeekFeedback) return;
        // While dragging we update *only* the readout label. The actual seek
        // is applied on DragCompleted / mouse-up. Calling _videoPlayer.Seek
        // on every value change would queue dozens of seeks per drag and on
        // slower machines the decoder visibly stalls while it works through
        // the queue. Coalescing inside the player helps, but skipping the
        // calls entirely is cheaper and feels just as responsive because the
        // operator is watching the slider thumb, not the live frame.
        if (_videoTabSeekIsDragging)
        {
            if (VideoTabCurrentTimeText != null)
                VideoTabCurrentTimeText.Text = FormatTime(TimeSpan.FromSeconds(e.NewValue));
        }
    }

    private void ApplySeekFromSlider()
    {
        if (VideoTabSeekSlider is null) return;
        TimeSpan target = TimeSpan.FromSeconds(VideoTabSeekSlider.Value);
        if (_videoPlayer is not null && _videoPlayer.HasSource)
        {
            _videoPlayer.Seek(target);
        }
    }

    private void VideoTabSpeed_Changed(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (VideoTabSpeedCombo?.SelectedItem is not Avalonia.Controls.ComboBoxItem item) return;
        if (item.Tag is not string tag) return;
        if (!double.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double rate))
            return;

        if (_videoPlayer != null) _videoPlayer.SpeedRatio = rate;
    }

    private double _lastAppliedVolume = -1.0;
    private void VideoTabVolume_Changed(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        // The volume slider raises ValueChanged on every sub-pixel move. VLC
        // accepts integer 0–100 internally, so anything below ~1% delta has
        // no audible effect — skipping those calls keeps the audio thread
        // quiet during fast drags.
        double v = Math.Clamp(e.NewValue, 0.0, 1.0);
        if (_lastAppliedVolume >= 0 && Math.Abs(v - _lastAppliedVolume) < 0.01) return;
        _lastAppliedVolume = v;
        if (_videoPlayer != null) _videoPlayer.Volume = v;
        UpdateVideoTabMuteButtonIcon();
    }

    private void VideoTabMute_Click(object? sender, RoutedEventArgs e)
    {
        if (_videoPlayer is null) return;
        _videoPlayer.IsMuted = !_videoPlayer.IsMuted;
        UpdateVideoTabMuteButtonIcon();
    }

    private void UpdateVideoTabMuteButtonIcon()
    {
        if (VideoTabMuteButton is null) return;
        bool muted = _videoPlayer?.IsMuted == true;
        double vol = _videoPlayer?.Volume ?? 1.0;
        VideoTabMuteButton.Content = muted || vol <= 0.001 ? "🔇" : vol < 0.5 ? "🔉" : "🔊";
    }

    // Connect the VIDEO-tab preview rectangle to a live VisualBrush of the
    // scoreboard's display container. The brush updates automatically as the
    // scoreboard repaints, so the right-hand pane always reflects the real
    // top-left output (scorebug, break, scoreworm, stats, weather). LibVLC's
    // native HWND is the one surface a VisualBrush can't sample, so during
    // an MP4 takeover the local MediaElement overlay (which uses the
    // optimised playback copy) is shown on top of the mirror.
    private void AttachLiveMirror()
    {
        if (VideoTabLiveMirror is null) return;
        if (_displayContainer is null)
        {
            VideoTabLiveMirror.Fill = null;
            if (VideoTabPreviewPlaceholder != null)
                VideoTabPreviewPlaceholder.IsVisible = true;
            return;
        }

        // Re-use an existing brush if we already attached one — there's no
        // need to rebuild it every tab activation, and recreating the brush
        // forces WPF to discard its cached intermediate render bitmap.
        if (VideoTabLiveMirror.Fill is not Avalonia.Media.VisualBrush)
        {
            var brush = new Avalonia.Media.VisualBrush(_displayContainer)
            {
                
                Stretch = Avalonia.Media.Stretch.Uniform,
                
                SourceRect = new RelativeRect(0, 0, 1, 1, RelativeUnit.Relative),
            };
            // Cache the intermediate bitmap so the brush only re-rasterises
            // when the scoreboard actually changes, instead of on every WPF
            // render tick. The cache scale is intentionally low (0.5×) — the
            // preview pane is small and the operator does not need pixel
            // accuracy here, so we trade visual sharpness for headroom on
            // lower-end devices.
            VideoTabLiveMirror.Fill = brush;
        }

        if (VideoTabPreviewPlaceholder != null)
            VideoTabPreviewPlaceholder.IsVisible = false;
        if (VideoTabPreviewBadge != null && !_videoTakeoverActive)
            VideoTabPreviewBadge.Text = "LIVE · MIRRORING SCOREBOARD";
    }

    private void DetachLiveMirror()
    {
        if (VideoTabLiveMirror != null) VideoTabLiveMirror.Fill = null;
        if (VideoTabPreviewPlaceholder != null)
            VideoTabPreviewPlaceholder.IsVisible = true;
    }

    private void VideoTabPreview_MediaOpened(object? sender, RoutedEventArgs e) { }
    private void VideoTabPreview_MediaFailed(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { }
    private void VideoTabPreview_MediaEnded(object? sender, RoutedEventArgs e) { }


    private void LayoutRadio_Changed(object? sender, RoutedEventArgs e)
    {
        ScorebugLayout layout = LayoutExpandedRadio?.IsChecked == true
            ? ScorebugLayout.Distance
            : ScorebugLayout.Expanded;

        _scorebug?.SetLayout(layout);
        _uxPreferences.ScorebugLayout = layout == ScorebugLayout.Distance ? "distance" : "classic";
        QueueUxPreferencesSave();
        if (LayoutCardClassic is not null && LayoutCardExpanded is not null)
        {
            UpdateLayoutCards();
            UpdateGBCountsButtonState(layout);
        }
    }

    private void ApplyRememberedScorebugLayout()
    {
        bool distance = string.Equals(_uxPreferences.ScorebugLayout, "distance", StringComparison.OrdinalIgnoreCase);
        LayoutExpandedRadio.IsChecked = distance;
        LayoutClassicRadio.IsChecked = !distance;
        _scorebug?.SetLayout(distance ? ScorebugLayout.Distance : ScorebugLayout.Expanded);
        UpdateLayoutCards();
    }

    private void NewGame_Click(object? sender, RoutedEventArgs e)
    {
        ShowResetOverlay();
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(true, nameof(_scorebug))]
    private bool TryEnsureOverlaySurface(bool allowTicker = false)
    {
        if (_sportMode != SportMode.AFL) return false;
        EnsureScoreboardWindow();
        if (_scorebug is null) return false;
        if (_showingBreakScreen || _scorebug.IsVisible != true) ShowScorebug();
        if (_scorebug.IsTickerActive && !allowTicker) return false;
        CancelQueuedOverlays();
        _periodicOverlayReserved = false;
        _autoOverlayDelayTimer?.Stop();
        _autoOverlayDelayTimer = null;
        _autoOverlayDelayCancellation?.Invoke();
        _autoOverlayDelayCancellation = null;
        _scoringRunDelayTimer?.Stop();
        _scoringRunDelayTimer = null;
        _scorebug.PrepareManualOverlayHandoff();
        return true;
    }

    private void ManualStatsBar_Click(object? sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:statsbar");
        if (!TryEnsureOverlaySurface()) return;
        ApplyCurrentTeamColorsToScorebug();
        var stats = MatchStats.Calculate(_match, _match.Quarter, _match.ElapsedInQuarter);
        _scorebug.ShowStatsBar(stats);
    }

    private void CancelCurrentOverlay_Click(object? sender, RoutedEventArgs e)
    {
        CancelQueuedOverlays();
        _periodicOverlayReserved = false;
        _autoOverlayDelayTimer?.Stop();
        _autoOverlayDelayTimer = null;
        _autoOverlayDelayCancellation?.Invoke();
        _autoOverlayDelayCancellation = null;
        _scoringRunDelayTimer?.Stop();
        _scoringRunDelayTimer = null;
        _scorebug?.CancelCurrentOverlay();
    }

    private void ManualWarning_Click(object? sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:warning");
        if (!TryEnsureOverlaySurface(allowTicker: true)) return;
        ApplyCurrentTeamColorsToScorebug();
        _overlayScheduler.NotifyEventDrivenOverlay();
        _scorebug.ShowFiveMinuteWarning();
    }

    private void ManualScoreless_Click(object? sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:lastscore");
        if (!TryEnsureOverlaySurface()) return;
        ApplyCurrentTeamColorsToScorebug();
        _overlayScheduler.NotifyEventDrivenOverlay();
        AflDroughtCandidate drought = CalculateDroughts().Match;
        _scorebug.ShowLastEventTime(drought.Kind.ToString(), drought.Elapsed);
    }

    private void ManualScoringRun_Click(object? sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:run");
        if (!TryEnsureOverlaySurface()) return;

        ApplyCurrentTeamColorsToScorebug();

        IReadOnlyList<ScoreEvent> events = _match.Events;
        ScoreEvent? latestGoal = events.LastOrDefault(score => score.Type == ScoreType.Goal);
        TeamSide team = latestGoal?.Team ?? TeamSide.Home;
        // Manual inspection shows the strongest current story even after the
        // first or second goal. Automatic airing deliberately keeps the
        // four-goal threshold in CheckScoringRunAfterScore.
        AflGoalRunDetector.Result? run = AflGoalRunDetector.DetectBest(events, minimumFeaturedGoals: 1);
        if (run is not null) team = run.Team;

        string teamName = team == TeamSide.Home ? _match.HomeName : _match.AwayName;
        var srColor = GetTeamColor(team == TeamSide.Home);
        string srHex = $"#{srColor.R:X2}{srColor.G:X2}{srColor.B:X2}";
        _overlayScheduler.NotifyEventDrivenOverlay();
        if (run is null)
            _scorebug.ShowGoalRunResting(teamName, srHex);
        else
            _scorebug.ShowGoalRun(teamName, run.TeamGoals, run.WindowGoals, run.IsConsecutive, srHex);
    }

    private void ManualHomeDrought_Click(object? sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:homedrought");
        if (!TryEnsureOverlaySurface()) return;
        ApplyCurrentTeamColorsToScorebug();
        AflDroughtCandidate drought = CalculateDroughts().Home;
        var homeColor = GetTeamColor(true);
        string homeHex = $"#{homeColor.R:X2}{homeColor.G:X2}{homeColor.B:X2}";
        _overlayScheduler.NotifyEventDrivenOverlay();
        _scorebug.ShowTeamDrought(_match.HomeName, _match.HomeAbbr,
            drought.Kind.ToString(), drought.Elapsed, homeHex);
    }

    private void ManualAwayDrought_Click(object? sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:awaydrought");
        if (!TryEnsureOverlaySurface()) return;
        ApplyCurrentTeamColorsToScorebug();
        AflDroughtCandidate drought = CalculateDroughts().Away;
        var awayColor = GetTeamColor(false);
        string awayHex = $"#{awayColor.R:X2}{awayColor.G:X2}{awayColor.B:X2}";
        _overlayScheduler.NotifyEventDrivenOverlay();
        _scorebug.ShowTeamDrought(_match.AwayName, _match.AwayAbbr,
            drought.Kind.ToString(), drought.Elapsed, awayHex);
    }

    private void ManualWinProb_Click(object? sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:winprob");
        if (!TryEnsureOverlaySurface()) return;
        ApplyCurrentTeamColorsToScorebug();
        ComputeAndShowWinProbability();
    }

    private void ManualGBCounts_Click(object? sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:gbcounts");
        if (!TryEnsureOverlaySurface()) return;
        _overlayScheduler.NotifyEventDrivenOverlay();
        _scorebug.ToggleGBColumns();
    }

    private async void ManualWeatherForecast_Click(object? sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:forecast");
        var snapshot = await GetWeatherForManualOverlayAsync();
        if (snapshot is null) return;
        if (!TryEnsureOverlaySurface()) return;
        _scorebug.ShowWeatherForecast(snapshot);
    }

    private async void ManualWeatherStats_Click(object? sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:weather");
        var snapshot = await GetWeatherForManualOverlayAsync();
        if (snapshot is null) return;
        if (!TryEnsureOverlaySurface()) return;
        _scorebug.ShowWeatherStats(snapshot);
    }

    private async void ManualRainForecast_Click(object? sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:rain");
        var snapshot = await GetWeatherForManualOverlayAsync();
        if (snapshot is null) return;
        if (!TryEnsureOverlaySurface()) return;
        _scorebug.ShowRainForecast(snapshot);
    }

    private async Task<WeatherSnapshot?> GetWeatherForManualOverlayAsync()
    {
        WeatherSnapshot? snapshot = _scorebug?.GetWeatherSnapshot();
        if (snapshot is not null) return snapshot;

        if (string.IsNullOrWhiteSpace(_weatherLocation))
        {
            WeatherLocationStatus.Text = "Choose a weather location before using weather overlays.";
            RocheScoreboard.Mac.Native.NativeMessageBox.Show(this,
                "Choose a weather location in Settings before showing a weather overlay.",
                "Weather location required", NativeMessageBoxButton.OK, NativeMessageBoxImage.Information);
            return null;
        }

        WeatherLocationStatus.Text = $"Loading weather for {_weatherLocation}…";
        await StartOrUpdateWeatherServiceAsync(_weatherLocation);
        snapshot = _scorebug?.GetWeatherSnapshot() ?? _weatherService?.LatestSnapshot;
        if (snapshot is not null)
        {
            _scorebug?.UpdateWeatherSnapshot(snapshot);
            WeatherLocationStatus.Text = $"Weather ready for {_weatherLocation}";
            return snapshot;
        }

        WeatherLocationStatus.Text = "Weather unavailable. Check the location and internet connection.";
        RocheScoreboard.Mac.Native.NativeMessageBox.Show(this,
            "Weather could not be loaded. Check the location and internet connection, then try again.",
            "Weather unavailable", NativeMessageBoxButton.OK, NativeMessageBoxImage.Warning);
        return null;
    }

    // ═══════════════════════════════════════════════════════
    //  SETTINGS: WEATHER LOCATION AUTOCOMPLETE
    // ═══════════════════════════════════════════════════════

    private bool _suppressSettingsWeatherFilter;
    private bool _settingsWeatherActivated;

    private void InitSettingsWeatherLocation()
    {
        SettingsWeatherLocation.AddHandler(
            TextBox.TextChangedEvent,
            new EventHandler<TextChangedEventArgs>(SettingsWeatherLocation_TextChanged));

        SettingsWeatherLocation.GotFocus += (_, _) => _settingsWeatherActivated = true;
        SettingsWeatherLocation.DropDownOpened += (_, _) => _settingsWeatherActivated = true;
    }

    private void SettingsWeatherLocation_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressSettingsWeatherFilter || !_settingsWeatherActivated) return;

        var combo = SettingsWeatherLocation;
        string input = (combo.Text ?? string.Empty) ?? "";

        if (string.IsNullOrWhiteSpace(input))
        {
            combo.IsDropDownOpen = false;
            return;
        }

        _suppressSettingsWeatherFilter = true;

        var matches = SetupWizard.AustralianCities
            .Where(c => c.Contains(input, StringComparison.OrdinalIgnoreCase))
            .ToList();

        combo.Items.Clear();
        foreach (string city in matches)
            combo.Items.Add(city);

        var editBox = NativeTemplates.FindObject(combo,"PART_EditableTextBox") as Avalonia.Controls.TextBox;
        int caretPos = editBox?.CaretIndex ?? input.Length;

        combo.IsDropDownOpen = matches.Count > 0;

        if (editBox is not null)
            editBox.CaretIndex = caretPos;

        _suppressSettingsWeatherFilter = false;
    }

    private void ApplyWeatherLocation_Click(object? sender, RoutedEventArgs e)
    {
        string location = (SettingsWeatherLocation.Text ?? string.Empty)?.Trim() ?? "";
        _weatherLocation = string.IsNullOrWhiteSpace(location) ? null : location;
        _scorebug?.SetWeatherLocation(_weatherLocation);
        WeatherLocationStatus.Text = string.IsNullOrWhiteSpace(location)
            ? "Weather disabled"
            : $"Weather set to {location}";
        _ = StartOrUpdateWeatherServiceAsync(_weatherLocation);
    }

    /// <summary>
    /// Ensures the live <see cref="WeatherService"/> is running for the
    /// currently configured location and pushes snapshots into the scorebug
    /// top bar so weather actually shows on the display.
    /// </summary>
    internal async Task StartOrUpdateWeatherServiceAsync(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            _weatherService?.Stop();
            _activeWeatherLocation = null;
            return;
        }

        if (_weatherService is null)
        {
            _weatherService = new WeatherService();
            _weatherService.ForecastUpdated += snap =>
            {
                if (snap is null) return;
                // Only refresh the cached snapshot + top weather bar from the
                // live feed. Don't trigger the forecast overlay here — that
                // belongs to the OverlayScheduler / manual buttons so it does
                // not pop up unexpectedly (e.g. when continuing a quarter).
                Dispatcher.UIThread.Invoke(() =>
                {
                    _scorebug?.UpdateWeatherSnapshot(snap);
                    _cricketScorebug?.UpdateWeatherSnapshot(snap);
                    CricketControlPanelView.UpdateWeatherSnapshot(snap);
                });
            };
        }

        // Avoid re-geocoding if the location hasn't changed.
        if (string.Equals(_activeWeatherLocation, location, StringComparison.OrdinalIgnoreCase)
            && _weatherService.LatestSnapshot is not null)
        {
            _scorebug?.UpdateWeatherSnapshot(_weatherService.LatestSnapshot);
            _cricketScorebug?.UpdateWeatherSnapshot(_weatherService.LatestSnapshot);
            CricketControlPanelView.UpdateWeatherSnapshot(_weatherService.LatestSnapshot);
            return;
        }

        _weatherService.Stop();
        _activeWeatherLocation = location;

        try { await _weatherService.StartAsync(location); }
        catch { /* network/parse failures are surfaced via empty snapshot */ }
    }

    // ═══════════════════════════════════════════════════════
    //  RESET MATCH — cinematic confirmation overlay
    // ═══════════════════════════════════════════════════════

    private void ResetMatch_Click(object? sender, RoutedEventArgs e)
    {
        ShowResetOverlay();
    }

    private void ShowResetOverlay()
    {
        ResetOverlay.IsHitTestVisible = true;
        ResetOverlay.IsVisible = true;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ResetOverlay.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void CancelReset_Click(object? sender, RoutedEventArgs e)
    {
        HideResetOverlay();
    }

    private void ConfirmReset_Click(object? sender, RoutedEventArgs e)
    {
        // The sport selector becomes visible during ExecuteMatchReset. Do not
        // leave a transparent, hit-testable reset layer above it while the old
        // fade completes.
        ResetOverlay.BeginAnimation(OpacityProperty, null);
        ResetOverlay.Opacity = 0;
        ResetOverlay.IsVisible = false;
        ResetOverlay.IsHitTestVisible = false;
        ExecuteMatchReset();
    }

    private void HideResetOverlay()
    {
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (_, _) =>
        {
            ResetOverlay.BeginAnimation(OpacityProperty, null);
            ResetOverlay.Opacity = 0;
            ResetOverlay.IsVisible = false;
            ResetOverlay.IsHitTestVisible = false;
        };
        ResetOverlay.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void ExecuteMatchReset()
    {
        _deferredGameStateSaveTimer?.Stop();
        GameStateService.Clear();
        _aflMatchReady = false;
        StopBreakRotation();
        StopStatsBarTimer();
        ExitVideoTakeover();

        _match.ResetForNewGame();
        _winProbEngine.Reset();
        _fiveMinWarningShown = false;
        _previousRemainingTime = TimeSpan.MaxValue;
        ResetScoreAnimationQueue();
        _showingBreakScreen = false;
        _lastDisplayedSecond = -1;
        _overlayQueueWired = false;
        ResetPeriodicOverlayTracking();

        EventList.Children.Clear();
        LastEventText.Text = string.Empty;
        UpdateActiveDisplayLabel("No active output");

        // Restore the match log empty state
        if (MatchLogCountBadge is not null)
            MatchLogCountBadge.Text = "";
        if (MatchLogEmptyHint is not null)
            MatchLogEmptyHint.IsVisible = true;

        _scorebug?.ResetAllOverlayState();
        _scorebug?.SetScores(0, 0, 0, 0);
        if (_scorebug != null) _scorebug.IsVisible = false;
        if (_quarterBreakScorebug != null) _quarterBreakScorebug.IsVisible = false;
        if (_breakScreen != null) _breakScreen.IsVisible = false;
        if (_scoreworm != null) _scoreworm.IsVisible = false;
        if (_statsScreen != null) _statsScreen.IsVisible = false;
        if (_videoPlayer != null) _videoPlayer.IsVisible = false;
        if (_weatherScreen != null) _weatherScreen.IsVisible = false;

        ShowOutputScreensaver(manualSelection: false);

        MainContent.BeginAnimation(OpacityProperty, null);
        SetupWizardPanel.BeginAnimation(OpacityProperty, null);
        MainContent.IsVisible = false;
        SetupWizardPanel.IsVisible = false;
        CricketSetupWizardPanel.IsVisible = false;
        CricketControlPanelView.IsVisible = false;
        MainTabControl.SelectedIndex = 0;
        SportSelectionPanel.PrepareForDisplay();
        SportSelectionPanel.IsHitTestVisible = true;
        SportSelectionPanel.Opacity = 1;
        SportSelectionPanel.IsVisible = true;
    }

    // ── Native window resize via custom grip ──


    private void ResizeGrip_MouseDown(object? sender, PointerEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;

        BeginResizeDrag(Avalonia.Controls.WindowEdge.SouthEast, (Avalonia.Input.PointerPressedEventArgs)e);
        e.Handled = true;
    }

    // ── Cloud remote bridge ──
    //
    // The legacy local web server + SignalR command bridge has been removed.
    // BroadcastWebState() is now a thin seam that forwards the current match
    // snapshot to the Supabase-backed cloud remote session (see
    // MainWindow.CloudRemote.cs). It is intentionally tolerant of "no
    // session active" so existing call sites do not need to know whether the
    // cloud session has been started.

    /// <summary>
    /// Builds the current match snapshot and pushes it to the cloud remote
    /// session if one is active. No-op when the operator has not started a
    /// cloud session.
    /// </summary>
    private void BroadcastWebState() => PublishCloudSnapshot();

    /// <summary>
    /// True if any of the on-screen MediaElements is actively displaying a
    /// video. Used by the overlay scheduler to wait for the video presenter
    /// to release the UI thread before queuing a new overlay.
    /// </summary>
    private bool IsAnyVideoPlaying()
    {
        if (_videoPlayer is { IsVisible: true }) return true;
        return false;
    }

    // ═══════════════════════════════════════════════════════════════
    // App update — manual check / install from the Settings tab.
    // ═══════════════════════════════════════════════════════════════

    private bool _updateCheckInFlight;

    private void RefreshUpdateStatusUi()
    {
        if (UpdateCurrentVersionText is null) return;

        UpdateCurrentVersionText.Text = $"Current version: v{ProductionVersion.CurrentVersion.ToString(3)}";

        if (Avalonia.Application.Current is not RocheScoreboard.Mac.App app) return;

        InstallUpdateButton.Content = "DOWNLOAD & INSTALL";
        string checkedSuffix = app.UpdateService.LastCheckedAt is DateTimeOffset checkedAt
            ? $" · Checked {checkedAt.LocalDateTime:h:mm tt}"
            : string.Empty;
        if (app.UpdateService.LastCheckState == UpdateCheckState.Checking)
        {
            UpdateStatusText.Text = "Checking GitHub for a compatible Mac release…";
            UpdateStatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x8A, 0x95, 0xA3));
            InstallUpdateButton.IsVisible = false;
        }
        else if (app.UpdateService.LastCheckState == UpdateCheckState.NotChecked)
        {
            UpdateStatusText.Text = "Updates have not been checked yet.";
            UpdateStatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x8A, 0x95, 0xA3));
            InstallUpdateButton.IsVisible = false;
        }
        else if (app.UpdateService.LastCheckState == UpdateCheckState.Failed)
        {
            UpdateStatusText.Text = $"Update check failed: {app.UpdateService.LastError}{checkedSuffix} · Diagnostic: {AutoUpdateService.DiagnosticsPath}";
            UpdateStatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0xFF, 0x8A, 0x80));
            InstallUpdateButton.IsVisible = true;
            InstallUpdateButton.IsEnabled = true;
            InstallUpdateButton.Content = "OPEN DOWNLOAD PAGE";
        }
        else if (app.UpdateService.LastCheckState == UpdateCheckState.UpdateAvailableWithoutCompatibleAsset)
        {
            UpdateStatusText.Text = "A newer release exists, but its complete Mac portable ZIP is missing.";
            UpdateStatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0xFF, 0xC8, 0x57));
            InstallUpdateButton.IsVisible = true;
            InstallUpdateButton.IsEnabled = true;
            InstallUpdateButton.Content = "OPEN DOWNLOAD PAGE";
        }
        else if (app.UpdateService.UpdateAvailable)
        {
            string? tag = app.UpdateService.LatestRelease?.TagName?.TrimStart('v', 'V');
            UpdateStatusText.Text = string.IsNullOrWhiteSpace(tag)
                ? "An update is available."
                : $"Update available: v{tag}{checkedSuffix}.";
            UpdateStatusText.Foreground = MediaBrushes.LightGreen;
            InstallUpdateButton.IsVisible = true;
            InstallUpdateButton.IsEnabled = true;
        }
        else
        {
            UpdateStatusText.Text = $"You're on the latest version{checkedSuffix}.";
            UpdateStatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x8A, 0x95, 0xA3));
            InstallUpdateButton.IsVisible = false;
        }
    }

    private async void CheckForUpdate_Click(object? sender, RoutedEventArgs e)
    {
        if (_updateCheckInFlight) return;
        if (Avalonia.Application.Current is not RocheScoreboard.Mac.App app) return;

        _updateCheckInFlight = true;
        try
        {
            CheckForUpdateButton.IsEnabled = false;
            UpdateStatusText.Text = "Checking for updates…";
            UpdateStatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x8A, 0x95, 0xA3));
            InstallUpdateButton.IsVisible = false;

            await app.UpdateService.CheckForUpdateAsync();
            RefreshUpdateStatusUi();
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"Update check failed: {ex.Message} Open the latest release in your browser.";
            UpdateStatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0xFF, 0x6B, 0x6B));
            InstallUpdateButton.IsVisible = true;
            InstallUpdateButton.IsEnabled = true;
            InstallUpdateButton.Content = "OPEN DOWNLOAD PAGE";
        }
        finally
        {
            CheckForUpdateButton.IsEnabled = true;
            _updateCheckInFlight = false;
        }
    }

    private void InstallUpdate_Click(object? sender, RoutedEventArgs e)
    {
        if (Avalonia.Application.Current is not RocheScoreboard.Mac.App app) return;
        if (!app.UpdateService.UpdateAvailable ||
            app.UpdateService.LastCheckState == UpdateCheckState.UpdateAvailableWithoutCompatibleAsset)
        {
            try
            {
                AutoUpdateService.OpenReleasePageInBrowser();
                UpdateStatusText.Text = "Opened latest download page in your browser.";
                UpdateStatusText.Foreground = MediaBrushes.LightGreen;
            }
            catch (InvalidOperationException ex)
            {
                UpdateStatusText.Text = $"Unable to open download page: {ex.Message}";
                UpdateStatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0xFF, 0x6B, 0x6B));
            }
            return;
        }

        if (app.PromptForUpdate()) return;
        RefreshUpdateStatusUi();
    }

    // ═══════════════════════════════════════════════════════════════
    // Presentation card handlers (Display & Messages tab)
    // ═══════════════════════════════════════════════════════════════

    private void PresCardScorebug_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        SwitchToScorebug_Click(sender, new RoutedEventArgs());
        UpdatePresentationCards("scorebug");
    }

    private void PresCardSummary_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        SwitchToBreakScreen_Click(sender, new RoutedEventArgs());
        UpdatePresentationCards("summary");
    }

    private void PresCardWorm_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        SwitchToScoreworm_Click(sender, new RoutedEventArgs());
        UpdatePresentationCards("worm");
    }

    private void PresCardStats_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        SwitchToStatsScreen_Click(sender, new RoutedEventArgs());
        UpdatePresentationCards("stats");
    }

    private void PresCardWeather_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        SwitchToWeatherScreen_Click(sender, new RoutedEventArgs());
        UpdatePresentationCards("weather");
    }

    private void PresCardQuarterBreak_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        SetSelectedPreview("presentation:quarterbreak");
        _activeScreenKey = "quarterbreak";
        EnsureScoreboardWindow();
        ShowQuarterBreakScorebug();
        UpdatePresentationCards("quarterbreak");
    }

    private void PresCardScreensaver_Click(object? sender, Avalonia.Input.PointerEventArgs e)
        => SwitchToScreensaver_Click(sender, new RoutedEventArgs());

    private void PresCardWindow_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        bool isOpen = _sportMode == SportMode.AFL
            ? _displayWindow is { IsVisible: true }
            : _cricketDisplayWindow is { IsVisible: true };

        if (isOpen)
        {
            if (_sportMode == SportMode.AFL)
                _displayWindow?.Close();
            else
                _cricketDisplayWindow?.Close();
        }
        else
        {
            ShowScoreboard_Click(sender, new RoutedEventArgs());
        }

        UpdateWindowToggleVisual();
    }

    /// <summary>Updates the window toggle pill to reflect current open/close state.</summary>
    private void UpdateWindowToggleVisual()
    {
        bool isOpen = _sportMode == SportMode.AFL
            ? _displayWindow is { IsVisible: true }
            : _cricketDisplayWindow is { IsVisible: true };

        // Animate the thumb position
        var thumbAnim = new DoubleAnimation(isOpen ? 20 : 0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        WindowToggleThumbTranslate.BeginAnimation(TranslateTransform.XProperty, thumbAnim);

        // Colour updates
        WindowToggleTrack.Background = new SolidColorBrush(isOpen
            ? (MediaColor)MediaColorConverter.ConvertFromString("#0D4429")
            : (MediaColor)MediaColorConverter.ConvertFromString("#18212C"));
        WindowToggleTrack.BorderBrush = new SolidColorBrush(isOpen
            ? (MediaColor)MediaColorConverter.ConvertFromString("#3FB950")
            : (MediaColor)MediaColorConverter.ConvertFromString("#304055"));
        WindowToggleThumb.Background = new SolidColorBrush(isOpen
            ? (MediaColor)MediaColorConverter.ConvertFromString("#3FB950")
            : (MediaColor)MediaColorConverter.ConvertFromString("#8191A4"));

        WindowToggleLabel.Text = isOpen ? "Close" : "Open";
        WindowToggleLabel.Foreground = new SolidColorBrush(isOpen
            ? (MediaColor)MediaColorConverter.ConvertFromString("#3FB950")
            : (MediaColor)MediaColorConverter.ConvertFromString("#8191A4"));

        PresCardWindow.BorderBrush = new SolidColorBrush(isOpen
            ? (MediaColor)MediaColorConverter.ConvertFromString("#3FB950")
            : (MediaColor)MediaColorConverter.ConvertFromString("#304055"));
    }

    private void UpdatePresentationCards(string active)
    {
        var cards = new (Border card, Border check, Border? glow, string key)[]
        {
            (PresCardScorebug, PresCheckScorebug, PresGlowScorebug, "scorebug"),
            (PresCardSummary,  PresCheckSummary,  null,              "summary"),
            (PresCardWorm,     PresCheckWorm,     null,              "worm"),
            (PresCardStats,    PresCheckStats,    null,              "stats"),
            (PresCardWeather,  PresCheckWeather,  null,              "weather"),
            (PresCardQuarterBreak, PresCheckQuarterBreak, null,       "quarterbreak"),
            (PresCardScreensaver, PresCheckScreensaver, null,         "screensaver"),
        };

        foreach (var (card, check, glow, key) in cards)
        {
            bool selected = key == active;
            card.Background = new SolidColorBrush(selected
                ? (MediaColor)MediaColorConverter.ConvertFromString("#0A1628")
                : (MediaColor)MediaColorConverter.ConvertFromString("#101721"));
            card.BorderBrush = new SolidColorBrush(selected
                ? (MediaColor)MediaColorConverter.ConvertFromString("#58A6FF")
                : (MediaColor)MediaColorConverter.ConvertFromString("#304055"));
            // Selection is paint-only. Changing border thickness or removing
            // the checkmark from measure shifted dense Output-tab cards under
            // the pointer after every presentation change.
            card.BorderThickness = new Thickness(2);
            check.IsVisible = true;
            check.Opacity = selected ? 1 : 0;
            if (glow != null)
                glow.Opacity = selected ? 0.12 : 0;
        }

        // Update text labels inside cards
        foreach (var (card, _, _, key) in cards)
        {
            bool selected = key == active;
            var stack = FindVisualChild<StackPanel>(card);
            if (stack == null) continue;
            foreach (var tb in FindVisualChildren<TextBlock>(stack))
            {
                if (tb.FontWeight == FontWeight.Black && tb.FontSize >= 10)
                    tb.Foreground = new SolidColorBrush(selected
                        ? Colors.White
                        : (MediaColor)MediaColorConverter.ConvertFromString("#8191A4"));
            }
        }
    }

    private static T? FindVisualChild<T>(AvaloniaObject parent) where T : AvaloniaObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            AvaloniaObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            T? found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(AvaloniaObject parent) where T : AvaloniaObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            AvaloniaObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) yield return typed;
            foreach (T nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Overlay card handlers (Display & Messages tab)
    // ═══════════════════════════════════════════════════════════════

    private void OverlayStatsBar_Click(object? sender, Avalonia.Input.PointerEventArgs e)
        => ManualStatsBar_Click(sender, new RoutedEventArgs());

    private void OverlayWinProb_Click(object? sender, Avalonia.Input.PointerEventArgs e)
        => ManualWinProb_Click(sender, new RoutedEventArgs());

    private void OverlayWarning_Click(object? sender, Avalonia.Input.PointerEventArgs e)
        => ManualWarning_Click(sender, new RoutedEventArgs());

    private void OverlayScoreless_Click(object? sender, Avalonia.Input.PointerEventArgs e)
        => ManualScoreless_Click(sender, new RoutedEventArgs());

    private void OverlayScoringRun_Click(object? sender, Avalonia.Input.PointerEventArgs e)
        => ManualScoringRun_Click(sender, new RoutedEventArgs());

    private void OverlayHomeDrought_Click(object? sender, Avalonia.Input.PointerEventArgs e)
        => ManualHomeDrought_Click(sender, new RoutedEventArgs());

    private void OverlayAwayDrought_Click(object? sender, Avalonia.Input.PointerEventArgs e)
        => ManualAwayDrought_Click(sender, new RoutedEventArgs());

    private void OverlayGBCounts_Click(object? sender, Avalonia.Input.PointerEventArgs e)
        => ManualGBCounts_Click(sender, new RoutedEventArgs());

    private void OverlayWeatherForecast_Click(object? sender, Avalonia.Input.PointerEventArgs e)
        => ManualWeatherForecast_Click(sender, new RoutedEventArgs());

    private void OverlayWeatherStats_Click(object? sender, Avalonia.Input.PointerEventArgs e)
        => ManualWeatherStats_Click(sender, new RoutedEventArgs());

    private void OverlayRainForecast_Click(object? sender, Avalonia.Input.PointerEventArgs e)
        => ManualRainForecast_Click(sender, new RoutedEventArgs());

    private void OverlayLeadChanges_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        SetSelectedPreview("overlay:leadchanges");
        if (!TryEnsureOverlaySurface()) return;
        _overlayScheduler.NotifyEventDrivenOverlay();
        var stats = MatchStats.Calculate(_match, _match.Quarter, _match.ElapsedInQuarter);
        _scorebug.ShowLeadChangesBar(stats.LeadChanges);
    }

    private void OverlayQtrScores_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        SetSelectedPreview("overlay:qtrscores");
        if (!TryEnsureOverlaySurface()) return;
        ApplyCurrentTeamColorsToScorebug();
        _overlayScheduler.NotifyEventDrivenOverlay();
        int q = _match.Quarter;
        var prev = q > 1 ? _match.GetQuarterSnapshot(q - 1) : null;
        int hg = _match.HomeGoals - (prev?.HomeGoals ?? 0);
        int hb = _match.HomeBehinds - (prev?.HomeBehinds ?? 0);
        int ag = _match.AwayGoals - (prev?.AwayGoals ?? 0);
        int ab = _match.AwayBehinds - (prev?.AwayBehinds ?? 0);
        _scorebug.ShowQuarterScores(q, hg, hb, ag, ab);
    }

    private void OverlayRecentScores_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        SetSelectedPreview("overlay:recentscores");
        if (!TryEnsureOverlaySurface()) return;
        ApplyCurrentTeamColorsToScorebug();
        _overlayScheduler.NotifyEventDrivenOverlay();
        _scorebug.ShowRecentScores(_match.Events, _match.Quarter);
    }

    // ═══════════════════════════════════════════════════════════════
    // Settings tab — Clock mode cards
    // ═══════════════════════════════════════════════════════════════

    private void SettingsCountUpCard_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        ArmNavigationPointerGuard();
        ClockModeCountUp.IsChecked = true;
        UpdateSettingsClockCards();
        UpdateSettingsControlPanelCountdownOptionVisuals();
    }

    private void SettingsCountdownCard_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        ArmNavigationPointerGuard();
        ClockModeCountdown.IsChecked = true;
        UpdateSettingsClockCards();
        UpdateSettingsControlPanelCountdownOptionVisuals();
    }

    // Tracks previous settings-card visibility state so entrance animation only plays on true re-entry.
    private bool _settingsCountdownOptionWasCollapsed = true;
    private DispatcherTimer? _settingsCountdownOptionHideTimer;

    private void UpdateSettingsClockCards()
    {
        bool countUp = ClockModeCountUp.IsChecked == true;

        SettingsCountUpCard.Background = new SolidColorBrush(
            (MediaColor)MediaColorConverter.ConvertFromString(countUp ? "#0A1628" : "#0E0C08"));
        SettingsCountUpCard.BorderBrush = new SolidColorBrush(
            (MediaColor)MediaColorConverter.ConvertFromString(countUp ? "#58A6FF" : "#2A2A40"));
        SettingsCountUpCard.BorderThickness = new Thickness(2);
        SettingsCountUpGlow.Opacity = countUp ? 0.12 : 0;
        SettingsCountUpCheck.IsVisible = countUp ? true : false;

        SettingsCountdownCard.Background = new SolidColorBrush(
            (MediaColor)MediaColorConverter.ConvertFromString(!countUp ? "#1A1408" : "#0E0C08"));
        SettingsCountdownCard.BorderBrush = new SolidColorBrush(
            (MediaColor)MediaColorConverter.ConvertFromString(!countUp ? "#6B5A00" : "#3A3018"));
        SettingsCountdownCard.BorderThickness = new Thickness(2);
        SettingsCountdownGlow.Opacity = !countUp ? 0.12 : 0;
        SettingsCountdownCheck.IsVisible = !countUp ? true : false;

        if(_aflSettingsWorkspace is not null)
        {
            SettingsCountUpCard.BorderThickness=SettingsCountdownCard.BorderThickness=new Thickness(0);
            SettingsCountUpCard.Background=SettingsCountdownCard.Background=Avalonia.Media.Brushes.Transparent;
            SettingsCountUpCheck.IsVisible=SettingsCountdownCheck.IsVisible=false;
            SettingsCountUpGlow.Opacity=SettingsCountdownGlow.Opacity=0;
        }

        UpdateSettingsControlPanelCountdownOptionVisuals();
    }

    private static void AnimateSettingsDouble(AvaloniaObject target, AvaloniaProperty property, double to, int durationMs, IEasingFunction? easing = null)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = easing
        };

        if (target is Control element)
        {
            element.BeginAnimation(property, animation);
            return;
        }

        if (target is AvaloniaObject animatable)
        {
            animatable.BeginAnimation(property, animation);
        }
    }

    private void SettingsControlPanelCountdownOptionCard_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (ClockModeCountUp.IsChecked != true)
            return;

        var settingsShowCountdownToggle = FindElement<Avalonia.Controls.CheckBox>("SettingsShowControlPanelCountdown");
        if (settingsShowCountdownToggle is null)
            return;

        settingsShowCountdownToggle.IsChecked = settingsShowCountdownToggle.IsChecked != true;
        _showControlPanelCountdown = settingsShowCountdownToggle.IsChecked == true;
        OperatorWorkflowPreferences.Update(p => p.AflControlPanelCountdown = _showControlPanelCountdown);
        if (!_showControlPanelCountdown)
            PauseControlPanelCountdown(forceAutoPausedFlag: false);
        UpdateSettingsControlPanelCountdownOptionVisuals();
        UpdateControlPanelCountdownUi();
    }

    private void UpdateSettingsControlPanelCountdownOptionVisuals()
    {
        var optionCard = FindElement<Avalonia.Controls.Border>("SettingsControlPanelCountdownOptionCard");
        var settingsShowCountdownToggle = FindElement<Avalonia.Controls.CheckBox>("SettingsShowControlPanelCountdown");
        var cardTranslate = FindElement<TranslateTransform>("SettingsControlPanelCountdownCardTranslate");
        var cardScale = FindElement<ScaleTransform>("SettingsControlPanelCountdownCardScale");
        var toggleTrack = FindElement<Avalonia.Controls.Border>("SettingsControlPanelCountdownToggleTrack");
        var thumbTranslate = FindElement<TranslateTransform>("SettingsControlPanelCountdownThumbTranslate");
        var toggleLabel = FindElement<Avalonia.Controls.TextBlock>("SettingsControlPanelCountdownToggleLabel");

        if (optionCard is null || settingsShowCountdownToggle is null || cardTranslate is null || cardScale is null)
            return;

        bool isVisible = ClockModeCountUp.IsChecked == true;
        bool isEnabled = settingsShowCountdownToggle.IsChecked == true;
        bool wasCollapsed = _settingsCountdownOptionWasCollapsed;
        _settingsCountdownOptionWasCollapsed = !isVisible;

        _settingsCountdownOptionHideTimer?.Stop();
        _settingsCountdownOptionHideTimer = null;

        var easeOut = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        if (isVisible)
        {
            optionCard.BeginAnimation(Control.OpacityProperty, null);
            cardTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            cardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            cardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            optionCard.IsVisible = true;

            if (wasCollapsed)
            {
                optionCard.Opacity = 0;
                cardTranslate.Y = 18;
                cardScale.ScaleX = 0.97;
                cardScale.ScaleY = 0.97;

                AnimateSettingsDouble(optionCard, Control.OpacityProperty, 1, 260, easeOut);
                AnimateSettingsDouble(cardTranslate, TranslateTransform.YProperty, 0, 280, easeOut);
                AnimateSettingsDouble(cardScale, ScaleTransform.ScaleXProperty, 1, 260, easeOut);
                AnimateSettingsDouble(cardScale, ScaleTransform.ScaleYProperty, 1, 260, easeOut);
            }
            else
            {
                AnimateSettingsDouble(optionCard, Control.OpacityProperty, 1, 180, easeOut);
                AnimateSettingsDouble(cardTranslate, TranslateTransform.YProperty, 0, 180, easeOut);
                AnimateSettingsDouble(cardScale, ScaleTransform.ScaleXProperty, 1, 180, easeOut);
                AnimateSettingsDouble(cardScale, ScaleTransform.ScaleYProperty, 1, 180, easeOut);
            }
        }
        else
        {
            optionCard.BeginAnimation(Control.OpacityProperty, null);
            cardTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            cardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            cardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            AnimateSettingsDouble(optionCard, Control.OpacityProperty, 0, 180, easeOut);
            AnimateSettingsDouble(cardTranslate, TranslateTransform.YProperty, 14, 180, easeOut);
            AnimateSettingsDouble(cardScale, ScaleTransform.ScaleXProperty, 0.98, 180, easeOut);
            AnimateSettingsDouble(cardScale, ScaleTransform.ScaleYProperty, 0.98, 180, easeOut);

            _settingsCountdownOptionHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(190) };
            _settingsCountdownOptionHideTimer.Tick += (_, _) =>
            {
                _settingsCountdownOptionHideTimer?.Stop();
                _settingsCountdownOptionHideTimer = null;
                if (ClockModeCountUp.IsChecked != true)
                {
                    optionCard.IsVisible = false;
                }
            };
            _settingsCountdownOptionHideTimer.Start();
        }

        optionCard.Background = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(isEnabled ? "#0E1B2B" : "#10161D"));
        optionCard.BorderBrush = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(isEnabled ? "#58A6FF" : "#30363D"));
        optionCard.BorderThickness = new Thickness(2);

        if (toggleTrack is not null)
            toggleTrack.Background = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(isEnabled ? "#1F6FEB" : "#2D333B"));

        if (thumbTranslate is not null)
            thumbTranslate.X = isEnabled ? 24 : 0;

        if (toggleLabel is not null)
        {
            toggleLabel.Text = isEnabled ? "ON" : "OFF";
            toggleLabel.Foreground = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(isEnabled ? "#58A6FF" : "#8B949E"));
        }

        // Rendering this settings card must be side-effect free. The live
        // preference changes only from an explicit operator click, setup
        // completion, clock-mode change or saved-state restore.
    }

    // ═══════════════════════════════════════════════════════════════
    // Settings tab — Quarter duration steppers
    // ═══════════════════════════════════════════════════════════════

    private void SettingsMinutesMinus_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (int.TryParse((QuarterMinutesBox.Text ?? string.Empty), out int mins))
        {
            QuarterMinutesBox.Text = Math.Max(1, mins - 1).ToString();
            ApplyQuarterDuration_Click(sender, new RoutedEventArgs());
        }
    }

    private void SettingsMinutesPlus_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (int.TryParse((QuarterMinutesBox.Text ?? string.Empty), out int mins))
        {
            QuarterMinutesBox.Text = Math.Clamp(mins + 1, 1, 60).ToString();
            ApplyQuarterDuration_Click(sender, new RoutedEventArgs());
        }
    }

    private void SettingsSecondsMinus_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (int.TryParse((QuarterSecondsBox.Text ?? string.Empty), out int secs))
        {
            secs -= 5;
            if (secs < 0) secs = 55;
            QuarterSecondsBox.Text = secs.ToString("D2");
            ApplyQuarterDuration_Click(sender, new RoutedEventArgs());
        }
    }

    private void SettingsSecondsPlus_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (int.TryParse((QuarterSecondsBox.Text ?? string.Empty), out int secs))
        {
            secs += 5;
            if (secs >= 60) secs = 0;
            QuarterSecondsBox.Text = secs.ToString("D2");
            ApplyQuarterDuration_Click(sender, new RoutedEventArgs());
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Live clock adjustment
    // ═══════════════════════════════════════════════════════════════

    private void ClockMinus10_Click(object? sender, RoutedEventArgs e)
    {
        _match.AdjustElapsed(TimeSpan.FromSeconds(-10));
        UpdateLiveUI();
    }

    private void ClockMinus1_Click(object? sender, RoutedEventArgs e)
    {
        _match.AdjustElapsed(TimeSpan.FromSeconds(-1));
        UpdateLiveUI();
    }

    private void ClockPlus1_Click(object? sender, RoutedEventArgs e)
    {
        _match.AdjustElapsed(TimeSpan.FromSeconds(1));
        UpdateLiveUI();
    }

    private void ClockPlus10_Click(object? sender, RoutedEventArgs e)
    {
        _match.AdjustElapsed(TimeSpan.FromSeconds(10));
        UpdateLiveUI();
    }

    private void ClockSet_Click(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse((ClockSetMinutesBox.Text ?? string.Empty), out int mins)) mins = 0;
        if (!int.TryParse((ClockSetSecondsBox.Text ?? string.Empty), out int secs)) secs = 0;

        TimeSpan target = TimeSpan.FromMinutes(mins) + TimeSpan.FromSeconds(secs);

        // In countdown mode, the user enters the desired display time (time remaining)
        if (_match.ClockMode == ClockMode.Countdown)
            target = _match.QuarterDuration - target;

        _match.SetElapsed(target);
        UpdateLiveUI();
    }

    // ═══════════════════════════════════════════════════════════════
    // Settings tab — Layout cards
    // ═══════════════════════════════════════════════════════════════

    private void ClassicLayoutCard_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        LayoutClassicRadio.IsChecked = true;
        UpdateLayoutCards();
    }

    private void ExpandedLayoutCard_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        LayoutExpandedRadio.IsChecked = true;
        UpdateLayoutCards();
    }

    private void RetroLayoutCard_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        LayoutRetroRadio.IsChecked = true;
        UpdateLayoutCards();
    }

    private void NoLogosLayoutCard_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        LayoutNoLogosRadio.IsChecked = true;
        UpdateLayoutCards();
    }

    private void UpdateLayoutCards()
    {
        // Radio buttons are declared before the visual cards in XAML, so a
        // Checked event can arrive during InitializeComponent.
        if (LayoutCardClassic is null || LayoutCardExpanded is null ||
            LayoutCheckClassic is null || LayoutCheckExpanded is null)
            return;

        bool classic = LayoutClassicRadio.IsChecked == true;
        bool expanded = LayoutExpandedRadio.IsChecked == true;

        var cards = new (Border card, Border check, Border? glow, bool selected)[]
        {
            (LayoutCardClassic,  LayoutCheckClassic,  LayoutGlowClassic, classic),
            (LayoutCardExpanded, LayoutCheckExpanded,  null,              expanded),
        };

        foreach (var (card, check, glow, selected) in cards)
        {
            card.Background = new SolidColorBrush(selected
                ? (MediaColor)MediaColorConverter.ConvertFromString("#0A1628")
                : (MediaColor)MediaColorConverter.ConvertFromString("#0E0E1A"));
            card.BorderBrush = new SolidColorBrush(selected
                ? (MediaColor)MediaColorConverter.ConvertFromString("#58A6FF")
                : (MediaColor)MediaColorConverter.ConvertFromString("#2A2A40"));
            card.BorderThickness = new Thickness(2);
            check.IsVisible = selected ? true : false;
            if (glow != null)
                glow.Opacity = selected ? 0.12 : 0;

            var stack = FindVisualChild<StackPanel>(card);
            if (stack == null) continue;
            foreach (var tb in FindVisualChildren<TextBlock>(stack))
            {
                if (tb.FontWeight == FontWeight.Black && tb.FontSize >= 11)
                    tb.Foreground = new SolidColorBrush(selected
                        ? Colors.White
                        : (MediaColor)MediaColorConverter.ConvertFromString("#6A6A80"));
            }
        }

        ScorebugLayout currentLayout = expanded ? ScorebugLayout.Distance : ScorebugLayout.Expanded;
        UpdateGBCountsButtonState(currentLayout);
    }

    private void UpdateGBCountsButtonState(ScorebugLayout layout)
    {
        // G/B Counts overlay removed — no-op
    }

    // ═══════════════════════════════════════════════════════════════
    // Settings tab — Animation cards
    // ═══════════════════════════════════════════════════════════════

    private void AnimBroadcastCard_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        AnimBroadcastRadio.IsChecked = true;
        UpdateAnimCards();
    }

    private void AnimElectricCard_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        AnimElectricRadio.IsChecked = true;
        UpdateAnimCards();
    }

    private void AnimCinematicCard_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        AnimCinematicRadio.IsChecked = true;
        UpdateAnimCards();
    }

    private void AnimCleanCard_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        AnimCleanRadio.IsChecked = true;
        UpdateAnimCards();
    }

    private void AnimClassicCard_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        AnimClassicRadio.IsChecked = true;
        UpdateAnimCards();
    }

    private void AnimCustomVideoCard_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        AnimCustomVideoRadio.IsChecked = true;
        UpdateAnimCards();
    }

    private void UpdateAnimCards()
    {
        var cards = new (Border card, Border check, Border? glow, bool selected, string idle, string accent)[]
        {
            (AnimCardBroadcast,   AnimCheckBroadcast,   null,                AnimBroadcastRadio.IsChecked == true,   "#0B1726", "#63B3FF"),
            (AnimCardElectric,    AnimCheckElectric,     null,                AnimElectricRadio.IsChecked == true,    "#091C21", "#59F3FF"),
            (AnimCardCinematic,   AnimCheckCinematic,    null,                AnimCinematicRadio.IsChecked == true,   "#171126", "#C6A5FF"),
            (AnimCardClean,       AnimCheckClean,        null,                AnimCleanRadio.IsChecked == true,       "#0B1D18", "#68E0B7"),
            (AnimCardClassic,     AnimCheckClassic,      null,                AnimClassicRadio.IsChecked == true,     "#201808", "#FFC857"),
            (AnimCardCustomVideo, AnimCheckCustomVideo,  AnimGlowCustomVideo, AnimCustomVideoRadio.IsChecked == true, "#1A1408", "#D29922"),
        };

        foreach (var (card, check, glow, selected, idle, accent) in cards)
        {
            card.Background = new SolidColorBrush(selected
                ? (MediaColor)MediaColorConverter.ConvertFromString(idle)
                : (MediaColor)MediaColorConverter.ConvertFromString("#0C1119"));
            card.BorderBrush = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(
                selected ? accent : "#2D3948"));
            // Selection is paint-only. Changing border thickness remeasured
            // the UniformGrid and could shift a different card under the
            // pointer between mouse-down and mouse-up.
            card.BorderThickness = new Thickness(2);
            check.IsVisible = selected ? true : false;
            if (glow != null)
                glow.Opacity = selected ? 0.12 : 0;

            var stack = FindVisualChild<StackPanel>(card);
            if (stack == null) continue;
            foreach (var tb in FindVisualChildren<TextBlock>(stack))
            {
                if (tb.FontWeight == FontWeight.Black && tb.FontSize >= 8)
                    tb.Foreground = new SolidColorBrush(selected
                        ? (MediaColor)MediaColorConverter.ConvertFromString(accent)
                        : (MediaColor)MediaColorConverter.ConvertFromString("#8191A3"));
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Settings tab — NativeWindow dimension steppers
    // ═══════════════════════════════════════════════════════════════

    private void WidthMinus_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (int.TryParse((WindowWidthBox.Text ?? string.Empty), out int w))
            WindowWidthBox.Text = Math.Max(200, w - 1).ToString();
    }

    private void WidthPlus_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (int.TryParse((WindowWidthBox.Text ?? string.Empty), out int w))
            WindowWidthBox.Text = Math.Min(7680, w + 1).ToString();
    }

    private void HeightMinus_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (int.TryParse((WindowHeightBox.Text ?? string.Empty), out int h))
            WindowHeightBox.Text = Math.Max(100, h - 1).ToString();
    }

    private void HeightPlus_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (int.TryParse((WindowHeightBox.Text ?? string.Empty), out int h))
            WindowHeightBox.Text = Math.Min(4320, h + 1).ToString();
    }

    // ═══════════════════════════════════════════════════════════════
    // Settings tab — Reset card
    // ═══════════════════════════════════════════════════════════════

    private void ResetCardSettings_Click(object? sender, Avalonia.Input.PointerEventArgs e)
        => ResetMatch_Click(sender, new RoutedEventArgs());

    // ═══════════════════════════════════════════════════════════════
    // Settings tab — Navigation signpost handlers
    // ═══════════════════════════════════════════════════════════════

    private void SettingsNav_Teams_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        ApplySettingsCategoryFilter("match");
    }

    private void SettingsNav_Match_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        ApplySettingsCategoryFilter("match");
    }

    private void SettingsNav_Keybinds_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        ApplySettingsCategoryFilter("controls");
    }

    private void SettingsNav_Display_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        ApplySettingsCategoryFilter("scoreboard");
    }

    private void SettingsNav_Overlays_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        ApplySettingsCategoryFilter("automation");
    }

    private void SettingsNav_Media_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        ApplySettingsCategoryFilter("assets");
    }

    private void SettingsNav_Advanced_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        ApplySettingsCategoryFilter("system");
    }

    // ═══════════════════════════════════════════════════════════════
    // Settings tab — Search & category filtering
    // ═══════════════════════════════════════════════════════════════

    private string _settingsActiveCategory = "match";

    private void ApplySettingsCategoryFilter(string category)
    {
        ArmNavigationPointerGuard();
        string requested = string.IsNullOrWhiteSpace(category) ? "match" : category.ToLowerInvariant();
        string normalized = requested switch
        {
            "teams" => "match",
            "keybinds" => "controls",
            "display" => "scoreboard",
            "overlays" => "automation",
            "media" => "assets",
            "advanced" => "system",
            _ => requested
        };
        if (_uxPreferences.GuidedOperatorMode && normalized is not ("match" or "scoreboard"))
            normalized = "match";
        bool changed = !string.Equals(_settingsActiveCategory, normalized, System.StringComparison.OrdinalIgnoreCase);
        _settingsActiveCategory = normalized;
        _uxPreferences.LastSettingsCategory = _settingsActiveCategory;
        if (changed)
        {
            QueueUxPreferencesSave();
            SettingsScrollViewer?.ScrollToHome();
        }
        UpdateSettingsCategoryPillStyles();
        ApplySettingsFilter();
    }

    private void UpdateSettingsCategoryPillStyles()
    {
        var pills = new (Avalonia.Controls.Border? Pill, string Key)[]
        {
            (SettingsNavBtn_Teams, "match"),
            (FindElement<Avalonia.Controls.Border>("SettingsNavBtn_Keybinds"), "controls"),
            (SettingsNavBtn_Display, "scoreboard"),
            (SettingsNavBtn_Overlays, "automation"),
            (SettingsNavBtn_Media, "assets"),
            (SettingsNavBtn_Advanced, "system"),
        };
        foreach (var (pill, key) in pills)
        {
            if (pill is null) continue;
            bool active = string.Equals(key, _settingsActiveCategory, System.StringComparison.OrdinalIgnoreCase);
            pill.Background = this.TryFindResource(active ? "ControlCentre.GlassBrush" : "ControlCentre.SurfaceBrush") as Avalonia.Media.IBrush;
            pill.BorderBrush = this.TryFindResource(active ? "ControlCentre.AccentBrush" : "ControlCentre.BorderBrush") as Avalonia.Media.IBrush;
            pill.BorderThickness = new Thickness(1);
            pill.CornerRadius = new CornerRadius(14);
            pill.Effect = null;
            pill.BeginAnimation(OpacityProperty, null);
            pill.Opacity = 1;
            var label = pill.Child as Avalonia.Controls.TextBlock ?? (pill.Child as Avalonia.Controls.Grid)?.Children.OfType<Avalonia.Controls.TextBlock>().FirstOrDefault();
            if (label is not null)
            {
                label.FontSize = 15;
                label.FontWeight = FontWeight.SemiBold;
                label.Foreground = this.TryFindResource(active ? "ControlCentre.TextBrush" : "ControlCentre.SecondaryTextBrush") as Avalonia.Media.IBrush;
            }
        }
        if (AflSettingsCompactCaption is not null)
            AflSettingsCompactCaption.Text = _settingsActiveCategory switch { "controls" => "Interaction", "scoreboard" => "Scoreboard", "automation" => "Automation", "assets" => "Assets", "system" => "Application", _ => "Match" };
        if (_aflSettingsCategoryMenu is not null) _aflSettingsCategoryMenu.IsOpen = false;
    }

    private void SettingsSearch_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        if (SettingsSearchPlaceholder is not null && SettingsSearchBox is not null)
            SettingsSearchPlaceholder.IsVisible = string.IsNullOrEmpty((SettingsSearchBox.Text ?? string.Empty))
                ? true
                : false;
        if (SettingsSearchClear is not null && SettingsSearchBox is not null)
            SettingsSearchClear.IsVisible = string.IsNullOrEmpty((SettingsSearchBox.Text ?? string.Empty))
                ? false
                : true;
        ApplySettingsFilter();
    }

    private void SettingsSearchClear_Click(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (SettingsSearchBox is not null)
            SettingsSearchBox.Text = string.Empty;
    }

    private void ApplySettingsFilter()
    {
        if(_aflSettingsWorkspace is not null){_aflSettingsWorkspace.NavigateCategory(_settingsActiveCategory);return;}
        string query = SettingsSearchBox?.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        string category = string.IsNullOrWhiteSpace(_settingsActiveCategory) ? "match" : _settingsActiveCategory;

        var sections = new (Avalonia.Controls.Border? Section, string Key)[]
        {
            (SettingsSection_Teams,    "match"),
            (SettingsSection_Match,    "match"),
            (FindElement<Avalonia.Controls.Border>("SettingsSection_Keybinds"), "controls"),
            (SettingsSection_Display,  "scoreboard"),
            (SettingsSection_Overlays, "automation"),
            (SettingsSection_Media,    "assets"),
            (SettingsSection_Advanced, "system"),
        };

        bool searching = !string.IsNullOrEmpty(query);
        int visibleCount = 0;
        foreach (var (section, key) in sections)
        {
            if (section is null) continue;
            bool wasVisible = section.IsVisible == true;
            bool categoryMatch = string.Equals(category, key, System.StringComparison.OrdinalIgnoreCase);
            bool queryMatch = !searching || SectionMatchesQuery(section, query);
            bool show = searching ? queryMatch : categoryMatch;
            section.IsVisible = show ? true : false;
            if (show)
            {
                if (!wasVisible)
                    AnimateSettingsSectionIn(section, visibleCount);
                else
                {
                    section.BeginAnimation(OpacityProperty, null);
                    section.Opacity = 1;
                    if (section.RenderTransform is TranslateTransform stableTranslate)
                    {
                        stableTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                        stableTranslate.Y = 0;
                    }
                }
                visibleCount++;
            }
        }

        if (SettingsNoResults is not null)
            SettingsNoResults.IsVisible = visibleCount == 0
                ? true
                : false;
    }

    private static void AnimateSettingsSectionIn(Control section, int order)
    {
        if (!NativeSystemPreferences.ClientAreaAnimation)
        {
            section.Opacity = 1;
            return;
        }

        section.BeginAnimation(OpacityProperty, null);
        section.Opacity = 0;
        var translate = section.RenderTransform as TranslateTransform;
        if (translate is null)
        {
            translate = new TranslateTransform();
            section.RenderTransform = translate;
        }
        translate.Y = 8;

        var delay = TimeSpan.FromMilliseconds(Math.Min(order, 4) * 45);
        section.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            BeginTime = delay,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(200))
        {
            BeginTime = delay,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private static bool SectionMatchesQuery(Avalonia.AvaloniaObject root, string query)
    {
        // Match the section's own Tag keywords first
        if (root is Avalonia.Controls.Control fe && fe.Tag is string tag &&
            tag.ToLowerInvariant().Contains(query))
            return true;

        // Then any descendant TextBlock / TextBox / Button content
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            switch (child)
            {
                case Avalonia.Controls.TextBlock tb when !string.IsNullOrEmpty((tb.Text ?? string.Empty)) &&
                    (tb.Text ?? string.Empty).ToLowerInvariant().Contains(query):
                    return true;
                case Avalonia.Controls.TextBox tx when !string.IsNullOrEmpty((tx.Text ?? string.Empty)) &&
                    (tx.Text ?? string.Empty).ToLowerInvariant().Contains(query):
                    return true;
                case Avalonia.Controls.ContentControl cc when cc.Content is string s &&
                    s.ToLowerInvariant().Contains(query):
                    return true;
            }
            if (SectionMatchesQuery(child, query))
                return true;
        }
        return false;
    }
}

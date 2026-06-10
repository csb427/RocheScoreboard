using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using WinForms = System.Windows.Forms;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    // ── Scoring run auto-detection ──

    private TeamSide _lastShownRunTeam;
    private int _lastShownRunMargin;
    private DateTime _lastShownRunTime;          // wall-clock time of last show (kept for backward compatibility)
    private TimeSpan _lastShownRunMatchClock = TimeSpan.MinValue; // match-clock position when last shown
    private RunTier _lastShownRunTier = RunTier.None;
    // Per-team cooldown is generous (a re-show needs the run to grow), but
    // when momentum flips to the OTHER team a much shorter cross-team cooldown
    // applies — a momentum swap is itself the story.
    private static readonly TimeSpan RunCooldown = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RunCrossTeamCooldown = TimeSpan.FromSeconds(45);
    // Same-team escalation — to re-show inside the cooldown the run must have
    // grown by at least this many points (2 goals) AND maintained dominance.
    private const int RunSameTeamGrowthPoints = 12;

    /// <summary>
    /// Identifies which scoring-run tier qualified a result. Used by the
    /// caller to rank competing runs and decide whether a re-show is
    /// warranted (a tier escalation overrides the same-team cooldown).
    /// Ordered so higher numeric value = more significant run.
    /// </summary>
    private enum RunTier
    {
        None = 0,
        Burst = 1,
        Standard = 2,
        Sustained = 3,
        Major = 4,
        Mega = 5
    }

    // ── Scoring-run thresholds (tiered, multi-window) ─────────────────────
    //
    // A scoring run is recognised across SEVERAL rolling windows simultaneously
    // (short event windows, medium event windows, long event windows, plus
    // short/medium/long time windows). For each window we compute the run
    // team's points, opposition points, span, and dominance ratio. Each window
    // is then evaluated against the tier table — the best qualifying window
    // wins.
    //
    // Tier definitions (highest qualifying tier wins per window):
    //
    //   BURST     18 pts (3 goals), ratio ≥ 3.0, span ≤ 4 match-min
    //             — short flurry of momentum the viewer just saw (18-0 / 18-1).
    //
    //   STANDARD  24 pts, ratio ≥ 2.5, opp ≤ 9 (1 goal + 3 behinds), span ≤ 12 min
    //             — the classic 24-6 / 24-3 / 30-6 stretch.
    //
    //   SUSTAINED 36 pts, ratio ≥ 2.5, opp ≤ 12, span ≤ 22 min
    //             — long, evenly-paced dominance (36-12 / 42-12).
    //
    //   MAJOR     42 pts, ratio ≥ 2.3, opp ≤ 18, span ≤ 32 min
    //             — quarter-plus of clear control (42-18).
    //
    //   MEGA      48 pts (8 goals), ratio ≥ 2.3, opp ≤ 18, no span limit
    //             — overwhelming, multi-quarter dominance (48-12 / 60-18).
    //
    // Important: opposition scoring is TOLERATED. A run is not killed by an
    // occasional behind or even a goal as long as the windowed dominance still
    // holds. The system fades a run only when the opposition meaningfully
    // responds and the dominance ratio drops below the tier minimum.

    private const int RunBurstMinPoints = 18;            // 3 unanswered-ish goals
    private const double RunBurstMinRatio = 3.0;
    private const int RunBurstMaxOppPoints = 1;          // tolerate at most 1 behind
    private static readonly TimeSpan RunBurstMaxSpan = TimeSpan.FromMinutes(4);

    private const int RunStandardMinPoints = 24;         // 4 goals
    private const double RunStandardMinRatio = 2.5;
    private const int RunStandardMaxOppPoints = 9;       // 1 goal + 3 behinds
    private static readonly TimeSpan RunStandardMaxSpan = TimeSpan.FromMinutes(12);

    private const int RunSustainedMinPoints = 36;        // 6 goals
    private const double RunSustainedMinRatio = 2.5;
    private const int RunSustainedMaxOppPoints = 12;
    private static readonly TimeSpan RunSustainedMaxSpan = TimeSpan.FromMinutes(22);

    private const int RunMajorMinPoints = 42;            // 7 goals
    private const double RunMajorMinRatio = 2.3;
    private const int RunMajorMaxOppPoints = 18;
    private static readonly TimeSpan RunMajorMaxSpan = TimeSpan.FromMinutes(32);

    private const int RunMegaMinPoints = 48;             // 8 goals
    private const double RunMegaMinRatio = 2.3;
    private const int RunMegaMaxOppPoints = 18;

    // Outer scan caps — the multi-window evaluator never looks past this much
    // history, regardless of how many events the match has accumulated.
    private const int RunMaxEventWindow = 60;
    private static readonly TimeSpan RunMaxTimeWindow = TimeSpan.FromMinutes(35);

    // Candidate window sizes (in events). The evaluator scores each of these
    // back from the most recent event and qualifies against the tier table.
    private static readonly int[] RunCandidateEventWindows = { 4, 6, 8, 10, 12, 16, 20, 24, 30 };

    // Candidate window sizes (in match minutes). Same idea, time-based.
    private static readonly TimeSpan[] RunCandidateTimeWindows =
    {
        TimeSpan.FromMinutes(4),
        TimeSpan.FromMinutes(8),
        TimeSpan.FromMinutes(12),
        TimeSpan.FromMinutes(18),
        TimeSpan.FromMinutes(25),
        TimeSpan.FromMinutes(35)
    };

    private const double RunRedundancyRatio = 0.92;      // suppress if run net >= this fraction of overall margin
    private static readonly TimeSpan RunStalenessLimit = TimeSpan.FromMinutes(5);
    // Recent-tail guard: of the most recent RunRecentTailSize events that fell
    // within RunRecentTailWindow match-minutes, at least RunRecentTailDominance
    // fraction of the points must go to the run team. This is a tight,
    // momentum-focused window — independent of the much longer scan window —
    // so a 30 min sustained run has to still be HOT in the last few minutes
    // to qualify for re-display.
    private const int RunRecentTailSize = 4;
    private const double RunRecentTailDominance = 0.7;
    private static readonly TimeSpan RunRecentTailWindow = TimeSpan.FromMinutes(4);

    // Minimum events required to count as a run. Single-event "runs" are
    // nonsense — a run is a SEQUENCE of dominant scoring.
    private const int RunMinEventCount = 2;
    private int _previousLeadChanges;
    private const int LeadChangeOverlayThreshold = 1;
    private const int LeadChangeMinTotal = 5;
    // Lead changes are only a compelling story while the match is genuinely
    // close. Once one side pulls away (margin > ~4 goals), showing a historic
    // lead-change count is misleading — suppress the overlay in that case.
    private const int LeadChangeMaxCurrentMargin = 24;
    private DateTime _deferScorePushUntilUtc = DateTime.MinValue;

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

    // ── Auto overlay delay
    private static readonly TimeSpan AutoOverlayDelay = TimeSpan.FromSeconds(2);
    // Scoring run is a contextual analysis overlay — it should land AFTER
    // the goal animation, "+6" swipe, and any goal video have all cleared.
    // Per spec: an intentional ~10s post-animation breath so the run feels
    // like a follow-up commentary graphic rather than an interruption.
    private static readonly TimeSpan ScoringRunOverlayDelay = TimeSpan.FromSeconds(10);
    private DispatcherTimer? _autoOverlayDelayTimer;
    private DispatcherTimer? _scoringRunDelayTimer;
    private DispatcherTimer? _breakScreenDelayTimer;

    // ── Overlay queue — ensures each overlay gets its full display time ──
    private readonly Queue<Action> _overlayQueue = new();
    private bool _overlayQueueWired;

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

        // Block both queued and non-queued overlays for the next 90 seconds so
        // the viewer gets a clear beat of scorebug-only between them.
        _nextOverlayAllowedAtUtc = DateTime.UtcNow + OverlayCooldown;

        if (_overlayQueue.Count == 0) return;

        Action next = _overlayQueue.Dequeue();

        // Solid cooldown between consecutive overlays so the operator (and the
        // viewer) gets a clear beat of scorebug-only between them.
        DispatcherTimer gap = new() { Interval = OverlayCooldown };
        gap.Tick += (_, _) =>
        {
            gap.Stop();
            if (_scorebug == null) return;

            // Wait for score animations to finish before showing
            if (_scorebug.IsScoreAnimating)
            {
                DispatcherTimer poll = new() { Interval = TimeSpan.FromMilliseconds(200) };
                poll.Tick += (_, _) =>
                {
                    if (_scorebug == null || !_scorebug.IsScoreAnimating)
                    {
                        poll.Stop();
                        if (!_autoOverlaysEnabled || !_match.ClockRunning) return;
                        next();
                    }
                };
                poll.Start();
            }
            else
            {
                if (!_autoOverlaysEnabled || !_match.ClockRunning) return;
                next();
            }
        };
        gap.Start();
    }

    // Solid 90-second cooldown between the end of one overlay and the start
    // of the next. Applies to both queued and non-queued auto overlays.
    private static readonly TimeSpan OverlayCooldown = TimeSpan.FromSeconds(90);
    private DateTime _nextOverlayAllowedAtUtc = DateTime.MinValue;

    /// <summary>
    /// Enqueues an overlay action. If no overlay is currently active, shows it
    /// immediately after the standard delay. Otherwise queues it to play after
    /// the current overlay finishes.
    /// <para>
    /// When <paramref name="isPriority"/> is <c>true</c> (event-driven overlays
    /// like scoring runs and lead changes), the 90-second post-overlay cooldown
    /// is bypassed so the overlay lands close to the moment that triggered it.
    /// The overlay still waits politely if another overlay is currently showing.
    /// </para>
    /// </summary>
    private void EnqueueAutoOverlay(Action showAction, bool isPriority = false)
    {
        if (!_autoOverlaysEnabled) return;

        // Suppress informational (non-priority) overlays while the clock isn't
        // running. This catches the "continuance" case where the operator has
        // paused the clock between plays — we never want a stat/weather/recent
        // overlay to drop in then. Priority (event-driven) overlays still go
        // through so things like a goal celebration aren't lost.
        if (!isPriority && !_match.ClockRunning) return;

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
                // a 90-second gap drain — but for priority overlays we want
                // the new one to start now. Reset the cooldown gate and run
                // the standard short delay so the goal animation can settle.
                _nextOverlayAllowedAtUtc = DateTime.MinValue;
                _overlayQueue.Clear();
                ScheduleAutoOverlay(showAction);
                return;
            }

            _overlayQueue.Enqueue(showAction);
            return;
        }

        // Nothing active — but an overlay might have just finished. Enforce the
        // 90-second cooldown so the viewer gets a real beat of scorebug-only
        // between any two overlays. Priority overlays bypass this cooldown so
        // event-driven moments (scoring run, lead changes, drought, scoreless)
        // land close to the event that triggered them.
        DateTime now = DateTime.UtcNow;
        if (!isPriority && now < _nextOverlayAllowedAtUtc)
        {
            _overlayQueue.Enqueue(showAction);
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
        ScheduleAutoOverlay(showAction);
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
        Action<DispatcherTimer?> setTimer)
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
                        setTimer(null);
                        // Final pause check — the clock may have stopped while
                        // we were waiting for the score animation/video/overlay.
                        if (!_autoOverlaysEnabled || !_match.ClockRunning) return;
                        showAction();
                    }
                };
                poll.Start();
            }
            else
            {
                setTimer(null);
                if (!_autoOverlaysEnabled || !_match.ClockRunning) return;
                showAction();
            }
        };
        timer.Start();
    }

    /// <summary>Schedules an auto overlay with the default 2-second delay.</summary>
    private void ScheduleAutoOverlay(Action showAction)
    {
        ScheduleAutoOverlay(showAction, AutoOverlayDelay,
            () => _autoOverlayDelayTimer, t => _autoOverlayDelayTimer = t);
    }

    // ── Automatic overlay toggle ──
    private bool _autoOverlaysEnabled = true;

    private void AutoOverlaysToggle_Changed(object sender, RoutedEventArgs e)
    {
        _autoOverlaysEnabled = AutoOverlaysToggle.IsChecked == true;
        UpdateAutoOverlaysCardVisuals();
    }

    private void AutoOverlaysCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        AutoOverlaysToggle.IsChecked = AutoOverlaysToggle.IsChecked != true;
    }

    private void UpdateAutoOverlaysCardVisuals()
    {
        if (AutoOverlaysCard == null || AutoOverlaysGlow == null || AutoOverlaysCheck == null) return;

        bool enabled = AutoOverlaysToggle.IsChecked == true;

        AutoOverlaysCard.BorderBrush = enabled
            ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3FB950"))
            : new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2A2A40"));
        AutoOverlaysCard.BorderThickness = enabled ? new Thickness(2) : new Thickness(1);
        AutoOverlaysCard.Background = enabled
            ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0A1410"))
            : new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0E0E1A"));

        AutoOverlaysGlow.Opacity = enabled ? 0.12 : 0;
        AutoOverlaysCheck.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Finals mode toggle (settings tab) ──

    private void SettingsFinalsCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SettingsFinalsToggle.IsChecked = SettingsFinalsToggle.IsChecked != true;
    }

    private void SettingsFinalsToggle_Changed(object sender, RoutedEventArgs e)
    {
        _finalsMode = SettingsFinalsToggle.IsChecked == true;
        UpdateSettingsFinalsCardVisuals();
        _scorebug?.SetFinalsMode(_finalsMode);
    }

    private void UpdateSettingsFinalsCardVisuals()
    {
        if (SettingsFinalsCard == null || SettingsFinalsGlow == null) return;

        bool on = SettingsFinalsToggle.IsChecked == true;

        SettingsFinalsCard.BorderBrush = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#D4A017")
            : (MediaColor)MediaColorConverter.ConvertFromString("#2A2020"));
        SettingsFinalsCard.BorderThickness = new Thickness(on ? 2 : 1);
        SettingsFinalsCard.Background = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#1A1408")
            : (MediaColor)MediaColorConverter.ConvertFromString("#0A0806"));

        SettingsFinalsGlow.Opacity = on ? 0.15 : 0;

        SettingsFinalsTitle.Foreground = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#D4A017")
            : (MediaColor)MediaColorConverter.ConvertFromString("#5A4A20"));

        // Toggle track + thumb
        SettingsFinalsTrack.Background = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#2A2008")
            : (MediaColor)MediaColorConverter.ConvertFromString("#1A1610"));
        SettingsFinalsTrack.BorderBrush = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#D4A017")
            : (MediaColor)MediaColorConverter.ConvertFromString("#3A3018"));
        SettingsFinalsThumb.Background = new SolidColorBrush(on
            ? (MediaColor)MediaColorConverter.ConvertFromString("#D4A017")
            : (MediaColor)MediaColorConverter.ConvertFromString("#5A4A20"));
        SettingsFinalsThumb.HorizontalAlignment = on
            ? System.Windows.HorizontalAlignment.Right
            : System.Windows.HorizontalAlignment.Left;
        SettingsFinalsThumb.Margin = on ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
    }

    // ── Break screen toggle (settings tab) ──

    private bool _breakScreenEnabled = true;

    private void SettingsBreakScreenCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SettingsBreakScreenToggle.IsChecked = SettingsBreakScreenToggle.IsChecked != true;
    }

    private void SettingsBreakScreenToggle_Changed(object sender, RoutedEventArgs e)
    {
        _breakScreenEnabled = SettingsBreakScreenToggle.IsChecked == true;
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
            ? System.Windows.HorizontalAlignment.Right
            : System.Windows.HorizontalAlignment.Left;
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

    private void BeginScoreAnimation(TimeSpan worstCaseDuration)
    {
        _activeScoreAnimations++;
        if (worstCaseDuration > TimeSpan.Zero)
        {
            _overlayScheduler.NotifyGoalAnimation(worstCaseDuration);
        }
    }

    private void EndScoreAnimation()
    {
        if (_activeScoreAnimations > 0) _activeScoreAnimations--;

        if (_activeScoreAnimations == 0)
        {
            // Animation chain finished — make sure the displayed digits match
            // the live model in case PushAllToScoreboard was throttled while
            // the animation was running.
            _scorebug?.SetScores(_match.HomeGoals, _match.HomeBehinds, _match.AwayGoals, _match.AwayBehinds);

            if (_pendingScoreAnimations.Count > 0)
            {
                Action next = _pendingScoreAnimations.Dequeue();
                try { next(); } catch { /* never let a queued event crash the chain */ }
            }
            else
            {
                PushAllToScoreboard();
            }
        }
    }

    /// <summary>
    /// Cancels every queued/in-flight score animation. Called by Undo /
    /// reset / event editor paths so the new authoritative score lands
    /// immediately rather than waiting for a now-stale animation chain.
    /// </summary>
    private void ResetScoreAnimationQueue()
    {
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
            GoalAnimationStyle.Clean => TimeSpan.FromSeconds(2.9),
            GoalAnimationStyle.Classic => TimeSpan.FromSeconds(2.7),
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
        MediaColor restBg = MediaColor.FromRgb(0x14, 0x1A, 0x24);
        MediaColor hoverBg = MediaColor.FromArgb(0xFF, 0x1C, 0x24, 0x33);
        MediaColor restBorder = MediaColor.FromArgb(0x30, teamColor.R, teamColor.G, teamColor.B);
        MediaColor hoverBorder = MediaColor.FromArgb(0x80, teamColor.R, teamColor.G, teamColor.B);
        MediaColor typeColor = ev.Type == ScoreType.Goal
            ? MediaColor.FromRgb(0x3F, 0xB9, 0x50)
            : MediaColor.FromRgb(0xD2, 0x99, 0x22);

        // ── Accent stripe (left edge, team colour) ──
        Border accentStripe = new()
        {
            Width = 3,
            Background = new SolidColorBrush(teamColor),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
            CornerRadius = new CornerRadius(1.5)
        };

        // ── Points badge ──
        TextBlock pointsBadge = new()
        {
            Text = points,
            Foreground = new SolidColorBrush(typeColor),
            FontSize = 10,
            FontWeight = FontWeights.Black,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        Border pointsBorder = new()
        {
            Background = new SolidColorBrush(MediaColor.FromArgb(0x20, typeColor.R, typeColor.G, typeColor.B)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x40, typeColor.R, typeColor.G, typeColor.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1, 5, 1),
            Child = pointsBadge,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };

        // ── Header row: points badge + type + team name + clock ──
        TextBlock typeLabel = new()
        {
            Text = type,
            Foreground = new SolidColorBrush(typeColor),
            FontSize = 11,
            FontWeight = FontWeights.Black,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };

        TextBlock teamLabel = new()
        {
            Text = teamName.ToUpper(),
            Foreground = MediaBrushes.White,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };

        TextBlock clockLabel = new()
        {
            Text = clock,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0x58, 0x65, 0x78)),
            FontSize = 10,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };

        StackPanel headerLeft = new() { Orientation = System.Windows.Controls.Orientation.Horizontal };
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
        var dimBrush = new SolidColorBrush(MediaColor.FromRgb(0x50, 0x5E, 0x70));
        var brightBrush = new SolidColorBrush(MediaColor.FromRgb(0xC9, 0xD1, 0xD9));

        TextBlock homeNameScore = new()
        {
            Text = _match.HomeName.ToUpper(),
            Foreground = isHome ? new SolidColorBrush(ContrastHelper.GetReadableTextColor(restBg, homeColor)) : dimBrush,
            FontSize = 10,
            FontWeight = isHome ? FontWeights.Bold : FontWeights.Normal,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };

        TextBlock homeScoreLabel = new()
        {
            Text = $"{ev.HomeGoals}.{ev.HomeBehinds}.{ev.HomeTotal}",
            Foreground = isHome ? brightBrush : dimBrush,
            FontSize = 11,
            FontWeight = isHome ? FontWeights.ExtraBold : FontWeights.Normal,
            FontFamily = new System.Windows.Media.FontFamily("Bahnschrift"),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };

        TextBlock separator = new()
        {
            Text = "–",
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0x3A, 0x44, 0x52)),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0)
        };

        TextBlock awayScoreLabel = new()
        {
            Text = $"{ev.AwayGoals}.{ev.AwayBehinds}.{ev.AwayTotal}",
            Foreground = !isHome ? brightBrush : dimBrush,
            FontSize = 11,
            FontWeight = !isHome ? FontWeights.ExtraBold : FontWeights.Normal,
            FontFamily = new System.Windows.Media.FontFamily("Bahnschrift"),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };

        TextBlock awayNameScore = new()
        {
            Text = _match.AwayName.ToUpper(),
            Foreground = !isHome ? new SolidColorBrush(ContrastHelper.GetReadableTextColor(restBg, awayColor)) : dimBrush,
            FontSize = 10,
            FontWeight = !isHome ? FontWeights.Bold : FontWeights.Normal,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };

        StackPanel scoreLine = new()
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
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
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0x81, 0x8C, 0xF8)),
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.0,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };

        Border editHintBar = new()
        {
            Background = new SolidColorBrush(MediaColor.FromArgb(0x00, 0x81, 0x8C, 0xF8)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x18, 0x81, 0x8C, 0xF8)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            CornerRadius = new CornerRadius(0, 0, 4, 4),
            Padding = new Thickness(0, 3, 0, 3),
            Margin = new Thickness(-2, 4, -2, -2),
            Child = editHint,
            MaxHeight = 0,
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

        Border bar = new()
        {
            Background = new SolidColorBrush(restBg),
            BorderBrush = new SolidColorBrush(restBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(6, 6, 8, 6),
            Child = inner,
            Cursor = System.Windows.Input.Cursors.Hand,
            Tag = ev,
            ToolTip = "Click to edit this score event"
        };

        // ── Hover animations ──
        var bgRest = new SolidColorBrush(restBg);
        var bgHover = new SolidColorBrush(hoverBg);
        var borderRest = new SolidColorBrush(restBorder);
        var borderHover = new SolidColorBrush(hoverBorder);

        bar.MouseEnter += (_, _) =>
        {
            bar.Background = bgHover;
            bar.BorderBrush = borderHover;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            editHint.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            // Expand the edit hint bar
            var expandAnim = new DoubleAnimation(22, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            editHintBar.BeginAnimation(FrameworkElement.MaxHeightProperty, expandAnim);

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

        bar.MouseLeave += (_, _) =>
        {
            bar.Background = bgRest;
            bar.BorderBrush = borderRest;

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            editHint.BeginAnimation(UIElement.OpacityProperty, fadeOut);

            // Collapse the edit hint bar
            var collapseAnim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            editHintBar.BeginAnimation(FrameworkElement.MaxHeightProperty, collapseAnim);

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

        bar.MouseLeftButtonUp += ScoreLogBar_Click;
        return bar;
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
            FontWeight = FontWeights.Black,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };

        Border badge = new()
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
        Border leftLine = new() { Height = 1, Background = lineBrush, VerticalAlignment = System.Windows.VerticalAlignment.Center };
        Border rightLine = new() { Height = 1, Background = lineBrush, VerticalAlignment = System.Windows.VerticalAlignment.Center };

        Grid.SetColumn(leftLine, 0);
        Grid.SetColumn(badge, 1);
        Grid.SetColumn(rightLine, 2);

        badge.Margin = new Thickness(8, 0, 8, 0);

        content.Children.Add(leftLine);
        content.Children.Add(badge);
        content.Children.Add(rightLine);

        return new Border
        {
            Margin = new Thickness(0, 8, 0, 8),
            Child = content
        };
    }

    private void RefreshVisibleScreens()
    {
        if (_displayWindow == null || !_displayWindow.IsVisible) return;

        if (_breakScreen?.Visibility == Visibility.Visible)
        {
            PopulateBreakScreen();
        }

        if (_scoreworm?.Visibility == Visibility.Visible)
        {
            PopulateScoreworm();
        }

        if (_statsScreen?.Visibility == Visibility.Visible)
        {
            PopulateStatsScreen();
        }
    }

    private void StartPause_Click(object sender, RoutedEventArgs e)
    {
        EnsureScoreboardWindow();

        if (_match.ClockRunning)
        {
            _match.PauseClock();
            // Don't reset overlays on pause — let current overlay finish naturally
            UpdateLiveUI();
            return;
        }

        if (_showingBreakScreen || _isBreakRotating)
        {
            StopBreakRotation();
            ShowScorebug();
            _scorebug?.ClearBreakGBLock();
            _scorebug?.ResumeScorelessFromBreak();
        }

        // Cancel the pending break screen delay if the quarter was started before it triggered
        if (_breakScreenDelayTimer != null)
        {
            _breakScreenDelayTimer.Stop();
            _breakScreenDelayTimer = null;
            _scorebug?.ClearBreakGBLock();
            _scorebug?.ResumeScorelessFromBreak();
        }

        // Notify the scheduler at the start of a new quarter (first start or after break).
        // Only fire when elapsed is near zero to avoid resetting mid-quarter on pause/resume.
        if (!_match.ClockRunning && _match.ElapsedInQuarter.TotalSeconds < 2)
        {
            _overlayScheduler.NotifyQuarterStart();
            _scorebug?.SetQuarter(_match.Quarter);
        }

        _match.StartClock();
        UpdateLiveUI();
    }

    private void HomeGoal_Click(object sender, RoutedEventArgs e)
    {
        EnsureScoreboardWindow();
        EnqueueScoringEvent(PerformHomeGoal);
    }

    private void HomeBehind_Click(object sender, RoutedEventArgs e)
    {
        EnsureScoreboardWindow();
        EnqueueScoringEvent(PerformHomeBehind);
    }

    private void AwayGoal_Click(object sender, RoutedEventArgs e)
    {
        EnsureScoreboardWindow();
        EnqueueScoringEvent(PerformAwayGoal);
    }

    /// <summary>
    /// Either runs the scoring action immediately or queues it behind the
    /// currently-animating event, so back-to-back operator clicks render
    /// in the order they were entered. PushAllToScoreboard is gated on the
    /// real animation/video completion signal rather than a wall-clock
    /// guess, which is what previously caused score digits to flip out of
    /// order during longer goal videos.
    /// </summary>
    private void EnqueueScoringEvent(Action action)
    {
        if (IsScoreAnimationActive)
        {
            _pendingScoreAnimations.Enqueue(action);
        }
        else
        {
            action();
        }
    }

    private void PerformHomeGoal()
    {
        GoalAnimationStyle style = _scorebug?.GetGoalAnimationStyle() ?? GoalAnimationStyle.Broadcast;
        if (style == GoalAnimationStyle.CustomVideo && !BothTeamsHaveGoalVideos())
        {
            style = GoalAnimationStyle.Classic;
        }

        // CRITICAL: lock the score display BEFORE mutating the model.
        // _match.AddGoal raises MatchChanged synchronously, which calls
        // PushAllToScoreboard → SetScores. If the lock isn't up first the
        // new digits would land on the scoreboard immediately, the goal
        // animation would have nothing to flip from, and the +6 swipe
        // would be suppressed because (current text == new text).
        BeginScoreAnimation(GetGoalAnimationDuration(style));
        _scorebug?.LockScoreUpdate(true);

        ScoreEvent ev = _match.AddGoal(TeamSide.Home);

        if (style == GoalAnimationStyle.CustomVideo)
        {
            void afterVideo()
            {
                if (_scorebug is null)
                {
                    EndScoreAnimation();
                    return;
                }
                _scorebug.PlayScoreFlipOnly(true, ev.HomeGoals, ev.HomeBehinds, ev.HomeTotal, EndScoreAnimation);
            }
            PlayCustomGoalVideoIfConfigured(true, afterVideo);
            return;
        }

        GoalAnimationStyle previous = _scorebug?.GetGoalAnimationStyle() ?? style;
        if (previous != style) _scorebug?.SetGoalAnimationStyle(style);
        if (_scorebug is null)
        {
            EndScoreAnimation();
        }
        else
        {
            _scorebug.PlayScoreAnimation(true, true, ev.HomeGoals, ev.HomeBehinds, ev.HomeTotal, EndScoreAnimation);
        }
        if (previous != style) _scorebug?.SetGoalAnimationStyle(previous);
    }

    private void PerformHomeBehind()
    {
        BeginScoreAnimation(TimeSpan.FromMilliseconds(900));
        _scorebug?.LockScoreUpdate(true);

        ScoreEvent ev = _match.AddBehind(TeamSide.Home);
        if (_scorebug is null)
        {
            EndScoreAnimation();
        }
        else
        {
            _scorebug.PlayScoreAnimation(true, false, ev.HomeGoals, ev.HomeBehinds, ev.HomeTotal, EndScoreAnimation);
        }
    }

    private void PerformAwayGoal()
    {
        GoalAnimationStyle style = _scorebug?.GetGoalAnimationStyle() ?? GoalAnimationStyle.Broadcast;
        if (style == GoalAnimationStyle.CustomVideo && !BothTeamsHaveGoalVideos())
        {
            style = GoalAnimationStyle.Classic;
        }

        BeginScoreAnimation(GetGoalAnimationDuration(style));
        _scorebug?.LockScoreUpdate(false);

        ScoreEvent ev = _match.AddGoal(TeamSide.Away);

        if (style == GoalAnimationStyle.CustomVideo)
        {
            void afterVideo()
            {
                if (_scorebug is null)
                {
                    EndScoreAnimation();
                    return;
                }
                _scorebug.PlayScoreFlipOnly(false, ev.AwayGoals, ev.AwayBehinds, ev.AwayTotal, EndScoreAnimation);
            }
            PlayCustomGoalVideoIfConfigured(false, afterVideo);
            return;
        }

        GoalAnimationStyle previous = _scorebug?.GetGoalAnimationStyle() ?? style;
        if (previous != style) _scorebug?.SetGoalAnimationStyle(style);
        if (_scorebug is null)
        {
            EndScoreAnimation();
        }
        else
        {
            _scorebug.PlayScoreAnimation(false, true, ev.AwayGoals, ev.AwayBehinds, ev.AwayTotal, EndScoreAnimation);
        }
        if (previous != style) _scorebug?.SetGoalAnimationStyle(previous);
    }

    private bool BothTeamsHaveGoalVideos()
    {
        return !string.IsNullOrWhiteSpace(_homeGoalVideoPath)
            && System.IO.File.Exists(_homeGoalVideoPath)
            && !string.IsNullOrWhiteSpace(_awayGoalVideoPath)
            && System.IO.File.Exists(_awayGoalVideoPath);
    }

    private void AwayBehind_Click(object sender, RoutedEventArgs e)
    {
        EnsureScoreboardWindow();
        EnqueueScoringEvent(PerformAwayBehind);
    }

    private void PerformAwayBehind()
    {
        BeginScoreAnimation(TimeSpan.FromMilliseconds(900));
        _scorebug?.LockScoreUpdate(false);

        ScoreEvent ev = _match.AddBehind(TeamSide.Away);
        if (_scorebug is null)
        {
            EndScoreAnimation();
        }
        else
        {
            _scorebug.PlayScoreAnimation(false, false, ev.AwayGoals, ev.AwayBehinds, ev.AwayTotal, EndScoreAnimation);
        }
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_match.UndoLastScore())
        {
            // Clear any pending score animation defer so undo shows immediately
            ResetScoreAnimationQueue();
            _scorebug?.ResetAllOverlayState();
            _scorebug?.SetScores(_match.HomeGoals, _match.HomeBehinds, _match.AwayGoals, _match.AwayBehinds);

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

    private void ScoreLogBar_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: ScoreEvent ev }) return;

        // Resolve live index at click time — safe even after prior edits
        int eventIndex = -1;
        for (int i = 0; i < _match.Events.Count; i++)
        {
            if (ReferenceEquals(_match.Events[i], ev)) { eventIndex = i; break; }
        }
        if (eventIndex < 0) return;

        ScoreEventEditorWindow editor = new(ev, _match.HomeName, _match.AwayName)
        {
            Owner = this
        };
        editor.ShowDialog();

        bool changed = editor.Result switch
        {
            ScoreEventEditorWindow.EditorResult.Deleted => _match.RemoveEvent(eventIndex),
            ScoreEventEditorWindow.EditorResult.Saved => _match.ModifyEvent(eventIndex, editor.SelectedTeam, editor.SelectedType, editor.SelectedGameTime),
            _ => false
        };

        if (!changed) return;

        // Run score validation after the edit
        var warnings = _match.ValidateScoreSequence();
        if (warnings.Count > 0)
        {
            string warningText = string.Join("\n", warnings.Select(w => w.Message));
            System.Windows.MessageBox.Show(
                $"Score validation detected {warnings.Count} issue(s):\n\n{warningText}",
                "Score Validation Warning",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        ResetScoreAnimationQueue();
        _scorebug?.ResetAllOverlayState();
        _scorebug?.SetScores(_match.HomeGoals, _match.HomeBehinds, _match.AwayGoals, _match.AwayBehinds);

        _previousLeadChanges = MatchStats.Calculate(_match, _match.Quarter, _match.ElapsedInQuarter).LeadChanges;

        RebuildMatchLog();
        UpdateLiveUI();
        PushAllToScoreboard();
        RefreshVisibleScreens();
    }

    private void EndQuarter_Click(object sender, RoutedEventArgs e)
    {
        if (!_match.EndQuarter()) return;

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
            _breakScreenDelayTimer?.Stop();
            _breakScreenDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            _breakScreenDelayTimer.Tick += (_, _) =>
            {
                _breakScreenDelayTimer.Stop();
                _breakScreenDelayTimer = null;
                ShowBreakScreen();
                StartBreakRotation();
            };
            _breakScreenDelayTimer.Start();
        }
    }

    private void ContinueQuarter_Click(object sender, RoutedEventArgs e)
    {
        if (!_match.CanContinueQuarter) return;

        int resumeQ = _match.Quarter - 1;
        MessageBoxResult confirm = System.Windows.MessageBox.Show(
            $"This will resume Quarter {resumeQ} from where it left off.\n\nThe next quarter will not start.",
            $"Resume Q{resumeQ}",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);

        if (confirm != MessageBoxResult.OK) return;

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
            ShowScorebug();
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
            MatchLogEmptyHint.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateClockStatus()
    {
        var dc = _match.DisplayClock;
        ClockStatusText.Text = $"{(_match.ClockMode == ClockMode.Countdown ? "Countdown" : "Count Up")} · {(_match.ClockRunning ? "Running" : "Stopped")} · {(int)dc.TotalMinutes:D2}:{dc.Seconds:D2}";
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

        // Also cancel any pending "show break screen in 8s" timer so that
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
        if (_match.ClockRunning)
        {
            return $"Q{_match.Quarter}";
        }

        return null;
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

    private void ShowBreakScreenRotation()
    {
        PopulateBreakScreen();
        SwitchToScreen(_breakScreen!, "Quarter Summary", fromRotation: true);
    }

    private void PopulateBreakScreen()
    {
        if (_breakScreen == null) return;

        _breakScreen.Populate(_match, GetEndedQuarter(),
            HomeColorBox.Text, AwayColorBox.Text,
            HomeSecondaryColorBox.Text, AwaySecondaryColorBox.Text,
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
            HomeColorBox.Text, AwayColorBox.Text,
            HomeSecondaryColorBox.Text, AwaySecondaryColorBox.Text,
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
            HomeColorBox.Text, AwayColorBox.Text,
            HomeSecondaryColorBox.Text, AwaySecondaryColorBox.Text,
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

        var snapshot = _scorebug?.GetWeatherSnapshot()
            ?? new WeatherSnapshot(0, 0, 0, 0, 0, string.Empty, string.Empty, Array.Empty<HourlyForecast>());

        _weatherScreen.Populate(snapshot, _weatherLocation,
            _match, GetEndedQuarter(),
            HomeColorBox.Text, AwayColorBox.Text,
            HomeSecondaryColorBox.Text, AwaySecondaryColorBox.Text,
            _homeLogoPath, _awayLogoPath,
            GetMatchStateBarTitle());
    }

    private void ApplyCurrentTeamColorsToScorebug()
    {
        _scorebug?.SetTeamColors(HomeColorBox.Text, AwayColorBox.Text, HomeSecondaryColorBox.Text, AwaySecondaryColorBox.Text);
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
        MediaColor teamColor, Border panelBorder, System.Windows.Controls.TextBlock label,
        System.Windows.Controls.TextBlock scoreDisplay,
        System.Windows.Controls.Button goalButton, System.Windows.Controls.Button behindButton)
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

    private UIElement? _preTakeoverScreen;
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
        try { _scorebug?.BeginAnimation(UIElement.OpacityProperty, null); } catch { }
        try { _breakScreen?.BeginAnimation(UIElement.OpacityProperty, null); } catch { }
        try { _scoreworm?.BeginAnimation(UIElement.OpacityProperty, null); } catch { }
        try { _statsScreen?.BeginAnimation(UIElement.OpacityProperty, null); } catch { }
        try { _weatherScreen?.BeginAnimation(UIElement.OpacityProperty, null); } catch { }
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
        foreach (UIElement child in _displayContainer.Children)
        {
            if (ReferenceEquals(child, _videoPlayer)) continue;
            child.Visibility = Visibility.Collapsed;
        }

        _videoPlayer.Visibility = Visibility.Visible;
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
            _videoPlayer.Visibility = Visibility.Collapsed;
        }

        // Always resume the scoreboard ecosystem, even if takeover wasn't
        // formally active — defensive against suspend/resume drift.
        ResumeScoreboardForPlayback();

        if (!_videoTakeoverActive) return;
        _videoTakeoverActive = false;
        Roche_Scoreboard.Services.VideoDiagnostics.Info("Takeover", "ExitVideoTakeover");

        UIElement? restore = _preTakeoverScreen ?? (UIElement?)_scorebug;
        _preTakeoverScreen = null;

        if (restore is not null)
        {
            restore.Visibility = Visibility.Visible;
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
        double w = _displayContainer?.ActualWidth > 0 ? _displayContainer.ActualWidth : 1280;
        double h = _displayContainer?.ActualHeight > 0 ? _displayContainer.ActualHeight : 720;

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
                IsRelevant = () => _match.Events.Count > 0
            },
            new OverlayDefinition
            {
                Kind = OverlayKind.WinProbability,
                DisplayDuration = sharedDuration,
                MinInterval = analysisInterval,
                Priority = analysisPriority,
                IsRelevant = () => _match.Events.Count >= 3
            },
            new OverlayDefinition
            {
                Kind = OverlayKind.Forecast,
                DisplayDuration = sharedDuration,
                MinInterval = weatherInterval,
                Priority = weatherPriority,
                IsRelevant = () => _scorebug?.GetWeatherSnapshot() is not null
            },
            new OverlayDefinition
            {
                Kind = OverlayKind.Weather,
                DisplayDuration = sharedDuration,
                MinInterval = weatherInterval,
                Priority = weatherPriority,
                IsRelevant = () => _scorebug?.GetWeatherSnapshot() is not null
            },
            new OverlayDefinition
            {
                Kind = OverlayKind.Rain,
                DisplayDuration = sharedDuration,
                MinInterval = weatherInterval,
                Priority = weatherPriority,
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
                IsRelevant = () => _match.Events.Count > 0
            },
            new OverlayDefinition
            {
                Kind = OverlayKind.RecentScores,
                DisplayDuration = TimeSpan.FromSeconds(14),
                MinInterval = analysisInterval,
                Priority = analysisPriority,
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
        if (_scorebug == null || _showingBreakScreen) return;
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

        // Mark overlay as shown in the scheduler immediately
        _overlayScheduler.NotifyOverlayShown(next.Kind);

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

        EnqueueAutoOverlay(showAction);
    }

    private void ResetPeriodicOverlayTracking()
    {
        _overlayScheduler.Reset();
        _previousLeadChanges = 0;
        _lastShownRunTeam = TeamSide.Home;
        _lastShownRunTime = DateTime.MinValue;
        _lastShownRunMargin = 0;
        _lastShownRunMatchClock = TimeSpan.MinValue;
        _lastShownRunTier = RunTier.None;
        _autoOverlayDelayTimer?.Stop();
        _autoOverlayDelayTimer = null;
        _scoringRunDelayTimer?.Stop();
        _scoringRunDelayTimer = null;
        _overlayQueue.Clear();
        _nextOverlayAllowedAtUtc = DateTime.MinValue;
    }

    // ── Scoring run auto-detection ──

    private sealed record ScoringRunResult(
        TeamSide Team, int RunPoints, int OpponentPoints,
        int EventCount, DateTime Start, DateTime NewestTimestamp,
        ScoreEvent? Newest, ScoreEvent? Oldest);

    /// <summary>
    /// Scans recent events backwards from <paramref name="runTeam"/> to detect a
    /// scoring run. Returns null if there are no events.
    /// <para>
    /// The main scan stays inside <see cref="RunMaxTimeWindow"/> and the event cap
    /// <see cref="RunMaxEventWindow"/>. If the run already qualifies by the end of
    /// that scan (strong dominance + minimum points), a follow-up extension pass
    /// continues absorbing contiguous earlier events that still satisfy the
    /// dominance constraints. This picks up "big" runs like a 40-2 spanning the
    /// start of Q1 all the way into Q2.
    /// </para>
    /// <para>
    /// A "recent-tail" guard also applies: the most recent few events in the
    /// window must be dominated by the run team, otherwise the run is cooling
    /// and the overlay would mislead the viewer.
    /// </para>
    /// </summary>
    /// <summary>
    /// Multi-window scoring-run scan. Evaluates the candidate event-count
    /// windows and time windows defined by <see cref="RunCandidateEventWindows"/>
    /// and <see cref="RunCandidateTimeWindows"/> against the tier table, then
    /// returns the highest-tier window. Returns null if no window qualifies.
    /// <para>
    /// Unlike the older "consecutive scoring" detector, this engine TOLERATES
    /// occasional opposition scoring. Each window is judged on overall
    /// dominance (points share, ratio, span) — a 24-3 stretch with two
    /// opposition behinds in the middle still qualifies, while a 24-18
    /// stretch does not. The engine therefore correctly recognises both
    /// short flurries and slow-burn long-term dominance.
    /// </para>
    /// </summary>
    private RunEvaluation EvaluateBestRunForTeam(TeamSide team, TimeSpan quarterDuration)
    {
        var events = _match.Events;
        var empty = new ScoringRunResult(team, 0, 0, 0, DateTime.MinValue, DateTime.MinValue, null, null);
        if (events.Count == 0)
            return new RunEvaluation(empty, RunTier.None, false, TimeSpan.Zero, 0.0, TimeSpan.MaxValue);

        // Anchor every window on the most recent event in the match (not the
        // most recent for the run team — momentum is about the live tail).
        TimeSpan newestMatchClock = MatchClockOfEvent(events[^1], quarterDuration);

        RunEvaluation? best = null;

        // Event-count windows.
        foreach (int win in RunCandidateEventWindows)
        {
            int upper = Math.Min(win, RunMaxEventWindow);
            int startIdx = Math.Max(0, events.Count - upper);
            var candidate = ScoreWindow(team, startIdx, events.Count - 1, quarterDuration, newestMatchClock);
            if (candidate is null) continue;
            best = PickBetter(best, candidate);
        }

        // Time-windowed scans.
        foreach (TimeSpan win in RunCandidateTimeWindows)
        {
            TimeSpan capped = win < RunMaxTimeWindow ? win : RunMaxTimeWindow;
            int startIdx = -1;
            for (int i = events.Count - 1; i >= 0; i--)
            {
                TimeSpan emc = MatchClockOfEvent(events[i], quarterDuration);
                if (newestMatchClock - emc > capped) break;
                startIdx = i;
            }
            if (startIdx < 0) continue;
            var candidate = ScoreWindow(team, startIdx, events.Count - 1, quarterDuration, newestMatchClock);
            if (candidate is null) continue;
            best = PickBetter(best, candidate);
        }

        return best ?? new RunEvaluation(empty, RunTier.None, false, TimeSpan.Zero, 0.0, TimeSpan.MaxValue);
    }

    /// <summary>
    /// Evaluates a single contiguous window <c>[startIdx..endIdx]</c> of the
    /// score event list against the tier table. Returns the highest-tier
    /// qualifying evaluation for that window, or null if it doesn't qualify.
    /// </summary>
    private RunEvaluation? ScoreWindow(TeamSide team, int startIdx, int endIdx, TimeSpan quarterDuration, TimeSpan newestMatchClock)
    {
        var events = _match.Events;
        if (startIdx < 0 || endIdx < startIdx || endIdx >= events.Count) return null;

        int runPts = 0, oppPts = 0, eventCount = 0;
        ScoreEvent? oldest = null;
        ScoreEvent? newest = null;
        DateTime runStart = DateTime.MinValue;
        DateTime newestTs = DateTime.MinValue;

        // Trim leading opposition-only history: the run "starts" at the first
        // event from the run team inside the window. This prevents an empty
        // 5-min stretch of opposition scoring from inflating opp pts.
        int firstRunIdx = -1;
        for (int i = startIdx; i <= endIdx; i++)
        {
            if (events[i].Team == team) { firstRunIdx = i; break; }
        }
        if (firstRunIdx < 0) return null;

        for (int i = firstRunIdx; i <= endIdx; i++)
        {
            var e = events[i];
            int pts = e.Type == ScoreType.Goal ? 6 : 1;
            if (e.Team == team) runPts += pts;
            else oppPts += pts;
            oldest ??= e;
            newest = e;
            runStart = oldest.Timestamp;
            newestTs = e.Timestamp;
            eventCount++;
        }
        if (eventCount == 0 || newest is null || oldest is null) return null;

        TimeSpan span = MatchClockOfEvent(newest, quarterDuration) - MatchClockOfEvent(oldest, quarterDuration);
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;

        double ratio = (double)runPts / Math.Max(1, oppPts);
        TimeSpan since = newestMatchClock - MatchClockOfEvent(newest, quarterDuration);
        if (since < TimeSpan.Zero) since = TimeSpan.Zero;

        // Tier qualification — pick highest tier that accepts the window.
        // Order matters: bigger tiers tested first.
        RunTier tier = RunTier.None;
        if (runPts >= RunMegaMinPoints && ratio >= RunMegaMinRatio && oppPts <= RunMegaMaxOppPoints)
            tier = RunTier.Mega;
        else if (runPts >= RunMajorMinPoints && ratio >= RunMajorMinRatio && oppPts <= RunMajorMaxOppPoints && span <= RunMajorMaxSpan)
            tier = RunTier.Major;
        else if (runPts >= RunSustainedMinPoints && ratio >= RunSustainedMinRatio && oppPts <= RunSustainedMaxOppPoints && span <= RunSustainedMaxSpan)
            tier = RunTier.Sustained;
        else if (runPts >= RunStandardMinPoints && ratio >= RunStandardMinRatio && oppPts <= RunStandardMaxOppPoints && span <= RunStandardMaxSpan)
            tier = RunTier.Standard;
        else if (runPts >= RunBurstMinPoints && ratio >= RunBurstMinRatio && oppPts <= RunBurstMaxOppPoints && span <= RunBurstMaxSpan)
            tier = RunTier.Burst;

        if (tier == RunTier.None) return null;

        var result = new ScoringRunResult(team, runPts, oppPts, eventCount, runStart, newestTs, newest, oldest);
        return new RunEvaluation(result, tier, true, span, ratio, since);
    }

    private static RunEvaluation PickBetter(RunEvaluation? current, RunEvaluation candidate)
    {
        if (current is null) return candidate;
        return CompareRuns(candidate, current) > 0 ? candidate : current;
    }

    /// <summary>
    /// Backward-compat shim used by the manual "show scoring run" button —
    /// returns the run details from the best qualifying window, or a synthesised
    /// minimal run from the most recent run-team event when nothing qualifies.
    /// </summary>
    private ScoringRunResult? CalculateScoringRun(TeamSide runTeam)
    {
        var events = _match.Events;
        if (events.Count == 0) return null;
        var eval = EvaluateBestRunForTeam(runTeam, _match.QuarterDuration);
        if (eval.Qualifies) return eval.Run;
        // Synthesize a "small run" from the most recent run-team event so the
        // manual overlay button still produces a sensible value.
        for (int i = events.Count - 1; i >= 0; i--)
        {
            if (events[i].Team == runTeam)
            {
                int pts = events[i].Type == ScoreType.Goal ? 6 : 1;
                return new ScoringRunResult(runTeam, pts, 0, 1, events[i].Timestamp, events[i].Timestamp, events[i], events[i]);
            }
        }
        return null;
    }

    /// <summary>
    /// Computes the match-clock position of <paramref name="ev"/> as
    /// (Quarter-1) × QuarterDuration + GameTime. This gives a single
    /// monotonically-increasing time axis across all four quarters that is
    /// independent of wall-clock time, so scoring-run windows survive paused
    /// matches, quarter breaks, saved-game restores, and app restarts.
    /// </summary>
    private static TimeSpan MatchClockOfEvent(ScoreEvent ev, TimeSpan quarterDuration)
    {
        int q = Math.Clamp(ev.Quarter, 1, 4);
        return TimeSpan.FromTicks(quarterDuration.Ticks * (q - 1)) + ev.GameTime;
    }

    /// <summary>
    /// Computes the match-clock distance from <paramref name="ev"/> to the
    /// current live match position ((Quarter-1) × QuarterDuration + ElapsedInQuarter).
    /// Returns <see cref="TimeSpan.Zero"/> if the event is somehow ahead of
    /// the live position (defensive — shouldn't happen).
    /// </summary>
    private TimeSpan ComputeMatchClockSinceEvent(ScoreEvent? ev, TimeSpan quarterDuration)
    {
        if (ev is null) return TimeSpan.Zero;
        TimeSpan eventClock = MatchClockOfEvent(ev, quarterDuration);
        TimeSpan liveClock = TimeSpan.FromTicks(quarterDuration.Ticks * (Math.Clamp(_match.Quarter, 1, 4) - 1)) + _match.ElapsedInQuarter;
        return liveClock > eventClock ? liveClock - eventClock : TimeSpan.Zero;
    }

    /// <summary>Convenience overload for the most recent event in the window.</summary>
    private TimeSpan ComputeMatchClockSinceLatest(ScoreEvent ev, TimeSpan quarterDuration)
        => ComputeMatchClockSinceEvent(ev, quarterDuration);

    private void CheckScoringRunAfterScore(ScoreEvent ev)
    {
        if (_scorebug == null || _showingBreakScreen) return;

        var events = _match.Events;
        if (events.Count < 3) return;

        TimeSpan quarterDuration = _match.QuarterDuration;

        // Evaluate both teams' candidate runs, then rank them by (tier,
        // freshness, ratio). The team that triggered the score does NOT
        // automatically win — what matters is which run currently has the
        // momentum.
        var homeEval = EvaluateRunForTeam(TeamSide.Home, quarterDuration);
        var awayEval = EvaluateRunForTeam(TeamSide.Away, quarterDuration);

        // Pick the better-ranked qualifying run.
        RunEvaluation? best = null;
        if (homeEval.Qualifies && awayEval.Qualifies)
            best = CompareRuns(homeEval, awayEval) >= 0 ? homeEval : awayEval;
        else if (homeEval.Qualifies)
            best = homeEval;
        else if (awayEval.Qualifies)
            best = awayEval;

        if (best is null) return;

        var run = best.Run;
        var tier = best.Tier;
        int runPoints = run.RunPoints;
        int opponentPoints = run.OpponentPoints;
        DateTime newestTimestamp = run.NewestTimestamp;
        DateTime now = DateTime.Now;

        // Minimum-event guard — a "run" needs at least 2 dominant scores.
        if (run.EventCount < RunMinEventCount) return;

        // Staleness — match-clock only. The most recent run event must be
        // within RunStalenessLimit match-minutes of the live match position.
        // This is the same yardstick used everywhere else in the run logic
        // and avoids wall-clock surprises after saved-game restores.
        TimeSpan stalenessMatchClock = ComputeMatchClockSinceEvent(run.Newest, quarterDuration);
        if (stalenessMatchClock > RunStalenessLimit) return;

        // Redundancy filter — see the inline comments below for the exception
        // table. A run that just restates the overall match margin adds no
        // information beyond the scorebug.
        int overallMargin = Math.Abs(_match.HomeTotal - _match.AwayTotal);
        int runNet = runPoints - opponentPoints;
        if (overallMargin > 0 && runNet >= overallMargin * RunRedundancyRatio)
        {
            // Exceptions — the run is still worth showing when any of these hold:
            //   1. Burst (clean & concentrated) — pure momentum the viewer just saw.
            //   2. Strong dominance ratio (e.g. 18-1, 21-1) — the score gap alone
            //      doesn't convey how lopsided the recent play has been.
            //   3. Sustained or Mega tier — sheer volume / duration is itself the story.
            double dominanceRatio = (double)runPoints / Math.Max(1, opponentPoints);
            bool isBurst = tier == RunTier.Burst;
            bool isDominant = runPoints >= RunStandardMinPoints && dominanceRatio >= 4.0;
            bool isLargeRun = tier >= RunTier.Sustained;
            if (!isBurst && !isDominant && !isLargeRun) return;
        }

        // Cooldown — match-clock-based, with three escape hatches:
        //   1. Different team than last shown (momentum has flipped) — fresh
        //      story, only the short cross-team cooldown applies.
        //   2. Tier has escalated (e.g. Standard → Sustained) — the run grew
        //      from "good" to "remarkable", re-show even within cooldown.
        //   3. Same team, same tier — run must have grown by ≥6 pts (1 goal)
        //      to merit re-display, or cooldown must have elapsed.
        TimeSpan currentMatchClock = TimeSpan.FromTicks(quarterDuration.Ticks * (Math.Clamp(_match.Quarter, 1, 4) - 1)) + _match.ElapsedInQuarter;
        TimeSpan elapsedSinceLastShow = _lastShownRunMatchClock == TimeSpan.MinValue
            ? TimeSpan.MaxValue
            : currentMatchClock - _lastShownRunMatchClock;
        if (elapsedSinceLastShow < TimeSpan.Zero) elapsedSinceLastShow = TimeSpan.MaxValue;

        if (run.Team == _lastShownRunTeam)
        {
            // Same-team re-show: allow if tier escalated, or run has grown
            // by ≥1 goal, or the per-team cooldown has elapsed.
            bool tierEscalated = tier > _lastShownRunTier;
            bool grewMeaningfully = runPoints >= _lastShownRunMargin + RunSameTeamGrowthPoints;
            bool cooldownElapsed = elapsedSinceLastShow >= RunCooldown;
            if (!tierEscalated && !grewMeaningfully && !cooldownElapsed)
                return;
        }
        else
        {
            // Cross-team re-show: short cooldown so a momentum flip lands
            // close to the moment it happened, without trampling a freshly
            // shown overlay.
            if (elapsedSinceLastShow < RunCrossTeamCooldown)
                return;
        }

        _lastShownRunTeam = run.Team;
        _lastShownRunMargin = runPoints;
        _lastShownRunTime = now;
        _lastShownRunMatchClock = currentMatchClock;
        _lastShownRunTier = tier;

        _overlayScheduler.NotifyEventDrivenOverlay();

        string teamName = run.Team == TeamSide.Home ? _match.HomeName : _match.AwayName;
        var runColor = GetTeamColor(run.Team == TeamSide.Home);
        string colorHex = $"#{runColor.R:X2}{runColor.G:X2}{runColor.B:X2}";
        _scorebug.ShowScoringRun(teamName, runPoints, opponentPoints, run.Start, colorHex);
    }

    /// <summary>
    /// Result of evaluating a single team's candidate scoring run against the
    /// tier table. <see cref="Qualifies"/> is true only when at least one
    /// tier accepts the run AND the recent-tail guard is satisfied. The tier
    /// is the <em>highest</em> qualifying tier (so a run that satisfies both
    /// Standard and Sustained reports as Sustained).
    /// </summary>
    private sealed record RunEvaluation(
        ScoringRunResult Run,
        RunTier Tier,
        bool Qualifies,
        TimeSpan MatchClockSpan,
        double DominanceRatio,
        TimeSpan SinceLatest);

    /// <summary>
    /// Calculates a candidate run for <paramref name="team"/> and evaluates
    /// it against the tier table. Returns the evaluation regardless of
    /// qualification — callers inspect <see cref="RunEvaluation.Qualifies"/>.
    /// </summary>
    private RunEvaluation EvaluateRunForTeam(TeamSide team, TimeSpan quarterDuration)
        => EvaluateBestRunForTeam(team, quarterDuration);

    /// <summary>
    /// Compares two qualifying runs to decide which to display. Returns
    /// &gt; 0 when <paramref name="a"/> should win, &lt; 0 when <paramref name="b"/>
    /// should win, 0 if they're equivalent.
    /// <para>
    /// Ranking criteria, in order:
    ///   1. Higher tier always wins (a Mega run beats a Burst even if the
    ///      Burst is fresher — sheer dominance is the bigger story).
    ///   2. Within the same tier, the FRESHER run wins (smaller "since
    ///      latest event" match-clock distance) — momentum is about now.
    ///   3. As a final tiebreaker, the run with the higher dominance ratio
    ///      wins — more lopsided is more interesting.
    /// </para>
    /// </summary>
    private static int CompareRuns(RunEvaluation a, RunEvaluation b)
    {
        int tierCmp = a.Tier.CompareTo(b.Tier);
        if (tierCmp != 0) return tierCmp;

        // Within the same tier, fresher wins (smaller "since latest" is better).
        int freshCmp = b.SinceLatest.CompareTo(a.SinceLatest);
        if (freshCmp != 0) return freshCmp;

        return a.DominanceRatio.CompareTo(b.DominanceRatio);
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

    private IntPtr DisplayWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            OverrideMinTrackSize(lParam);
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WM_NCHITTEST && _displayWindow is not null)
        {
            IntPtr result = HitTestBorderlessResize(_displayWindow, lParam);
            if (result != IntPtr.Zero)
            {
                handled = true;
                return result;
            }
        }

        return IntPtr.Zero;
    }

    private IntPtr CricketDisplayWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            OverrideMinTrackSize(lParam);
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WM_NCHITTEST && _cricketDisplayWindow is not null)
        {
            IntPtr result = HitTestBorderlessResize(_cricketDisplayWindow, lParam);
            if (result != IntPtr.Zero)
            {
                handled = true;
                return result;
            }
        }

        return IntPtr.Zero;
    }

    private IntPtr TrainingDisplayWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            OverrideMinTrackSize(lParam);
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WM_NCHITTEST && _trainingDisplayWindow is not null)
        {
            IntPtr result = HitTestBorderlessResize(_trainingDisplayWindow, lParam);
            if (result != IntPtr.Zero)
            {
                handled = true;
                return result;
            }
        }

        return IntPtr.Zero;
    }

    // ── Native min/max info structs for borderless-window minimum-size override ──
    //
    // WPF borderless windows (WindowStyle=None + AllowsTransparency=true) inherit
    // Windows' default minimum tracking size of roughly 120×39 px, which prevents
    // the operator from shrinking the display window below that even though the
    // scorebug is authored inside a master 960×540 Viewbox that scales perfectly
    // down to a few pixels. Intercepting WM_GETMINMAXINFO and stamping a tiny
    // ptMinTrackSize lets the window resize all the way down to 50×50, where the
    // Viewbox keeps the entire layout (overlays, animations, break/weather/stats
    // screens, transitions) pixel-identical to the 960×540 design — just at a
    // very small visual scale, which is exactly what the physical scoreboard
    // hardware needs.
    private const int WM_GETMINMAXINFO = 0x0024;

    // Absolute minimum window dimension in physical pixels. Matches the
    // operator-console dimension-box validation floor below so both paths agree.
    private const int MIN_TRACK_PX = 50;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    private static void OverrideMinTrackSize(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero) return;

        var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(lParam);
        mmi.ptMinTrackSize.X = MIN_TRACK_PX;
        mmi.ptMinTrackSize.Y = MIN_TRACK_PX;
        System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, fDeleteOld: true);
    }

    // ── Native hit-test constants for borderless window resize ──
    private const int WM_NCHITTEST = 0x0084;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    /// <summary>
    /// Returns the appropriate resize hit-test code when the cursor is within
    /// RESIZE_BORDER pixels of any edge of a borderless window.
    /// </summary>
    private static IntPtr HitTestBorderlessResize(Window window, IntPtr lParam)
    {
        int screenX = (short)(lParam.ToInt64() & 0xFFFF);
        int screenY = (short)(lParam.ToInt64() >> 16);

        System.Windows.Point pt = window.PointFromScreen(new System.Windows.Point(screenX, screenY));
        double w = window.ActualWidth;
        double h = window.ActualHeight;

        bool left = pt.X < RESIZE_BORDER;
        bool right = pt.X >= w - RESIZE_BORDER;
        bool top = pt.Y < RESIZE_BORDER;
        bool bottom = pt.Y >= h - RESIZE_BORDER;

        if (top && left) return (IntPtr)HTTOPLEFT;
        if (top && right) return (IntPtr)HTTOPRIGHT;
        if (bottom && left) return (IntPtr)HTBOTTOMLEFT;
        if (bottom && right) return (IntPtr)HTBOTTOMRIGHT;
        if (left) return (IntPtr)HTLEFT;
        if (right) return (IntPtr)HTRIGHT;
        if (top) return (IntPtr)HTTOP;
        if (bottom) return (IntPtr)HTBOTTOM;

        return IntPtr.Zero;
    }

    private bool _isBreakRotating;

    private void SwitchToScreen(UIElement target, string label, bool fromRotation = false)
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

        var current = GetCurrentlyVisibleScreen();
        if (current == target)
        {
            CurrentDisplayText.Text = label;
            return;
        }

        if (current != null)
        {
            if (_scorebug != null && _scorebug.IsRetroLayout)
                RetroTransition(current, target);
            else
                SlideTransition(current, target, true);
        }
        else
        {
            target.Visibility = Visibility.Visible;
            target.RenderTransform = new TranslateTransform(0, 0);
        }

        _showingBreakScreen = target != _scorebug;
        CurrentDisplayText.Text = label;

        // Note: switching to the video surface intentionally does NOT auto-play
        // the clip. The operator loads a video paused on its first frame and
        // explicitly hits Play/Show on Scoreboard when they want broadcast.
        // Goal videos drive their own playback start via PlayCustomGoalVideoIfConfigured.
    }

    private void SlideTransition(UIElement from, UIElement to, bool slideLeft)
    {
        // Modern "blackout pulse" transition:
        //   1. Black overlay fades in (covers desktop / any compositor flash)
        //   2. Outgoing screen is hidden & incoming screen is shown while fully black
        //   3. Black overlay fades back out, revealing the new screen with a subtle scale-in
        // This guarantees the desktop is never visible mid-transition.
        if (_retroCrtOverlay == null)
        {
            // Fallback: instant swap
            from.Visibility = Visibility.Collapsed;
            from.RenderTransform = new TranslateTransform(0, 0);
            from.Opacity = 1;
            to.Visibility = Visibility.Visible;
            to.RenderTransform = new TranslateTransform(0, 0);
            to.Opacity = 1;
            return;
        }

        const int fadeInMs = 180;
        const int holdMs = 60;
        const int fadeOutMs = 220;

        _retroCrtOverlay.Background = System.Windows.Media.Brushes.Black;
        _retroCrtOverlay.Visibility = Visibility.Visible;
        _retroCrtOverlay.Opacity = 0;

        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var fadeToBlack = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(fadeInMs)) { EasingFunction = ease };
        fadeToBlack.Completed += (_, _) =>
        {
            // Swap behind the curtain
            from.Visibility = Visibility.Collapsed;
            from.Opacity = 1;
            from.RenderTransform = new TranslateTransform(0, 0);

            // Prep incoming with a subtle scale-up for a modern reveal
            var scale = new ScaleTransform(1.03, 1.03);
            to.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            to.RenderTransform = scale;
            to.Opacity = 1;
            to.Visibility = Visibility.Visible;

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
                if (_retroCrtOverlay != null)
                {
                    _retroCrtOverlay.Visibility = Visibility.Collapsed;
                    _retroCrtOverlay.Opacity = 0;
                }
                to.RenderTransform = new TranslateTransform(0, 0);
            };
            _retroCrtOverlay!.BeginAnimation(UIElement.OpacityProperty, fadeFromBlack);
        };

        _retroCrtOverlay.BeginAnimation(UIElement.OpacityProperty, fadeToBlack);
    }

    /// <summary>
    /// Retro CRT-style screen transition — fast blackout, scanline sweep, static noise flash,
    /// then the new screen resolves through a vertical-roll / brightness reveal.
    /// </summary>
    private void RetroTransition(UIElement from, UIElement to)
    {
        if (_retroCrtOverlay == null)
        {
            // Fallback: no overlay available, just swap
            from.Visibility = Visibility.Collapsed;
            to.Visibility = Visibility.Visible;
            to.RenderTransform = new TranslateTransform(0, 0);
            return;
        }

        double height = _displayWindow?.ActualHeight ?? 440;

        // ── Phase 1: CRT blackout (fast collapse to horizontal line) ──
        _retroCrtOverlay.Visibility = Visibility.Visible;
        _retroCrtOverlay.Opacity = 0;
        _retroCrtOverlay.RenderTransform = new ScaleTransform(1, 1, 0, height / 2);

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
        to.Visibility = Visibility.Visible;
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
            from.Visibility = Visibility.Collapsed;
            from.Opacity = 1;
            from.RenderTransform = new TranslateTransform(0, 0);
            to.RenderTransform = new TranslateTransform(0, 0);
            if (_retroCrtOverlay != null)
            {
                _retroCrtOverlay.Visibility = Visibility.Collapsed;
                _retroCrtOverlay.Opacity = 0;
                _retroCrtOverlay.RenderTransform = new ScaleTransform(1, 1);
            }
        };

        sb.Begin();
    }

    private void AnimationRadio_Changed(object sender, RoutedEventArgs e)
    {
        if (_scorebug == null) return;

        GoalAnimationStyle style;
        string title;
        string desc;
        string duration;
        string intensity;

        if (AnimElectricRadio?.IsChecked == true)
        {
            style = GoalAnimationStyle.Electric;
            title = "Electric";
            desc = "Stadium hype — pre-charge edge zaps, horizontal surge entry, 5-beat strobe rhythm, reverse-direction gradient dashes, crackling exit.";
            duration = "~2.9s";
            intensity = "High";
        }
        else if (AnimCinematicRadio?.IsChecked == true)
        {
            style = GoalAnimationStyle.Cinematic;
            title = "Cinematic";
            desc = "Champions League drama — slow slide entry, three breathing glow cycles, team logo gently zooms forward, slow slide exit.";
            duration = "~5.0s";
            intensity = "Low";
        }
        else if (AnimCleanRadio?.IsChecked == true)
        {
            style = GoalAnimationStyle.Clean;
            title = "Clean";
            desc = "Modern minimalist — no overlay, no text. Two-stage heartbeat on the score area and an elastic swell on the total digit.";
            duration = "~2.2s";
            intensity = "Minimal";
        }
        else if (AnimClassicRadio?.IsChecked == true)
        {
            style = GoalAnimationStyle.Classic;
            title = "Classic";
            desc = "Retro arcade — overlay stutters on like 1980s scoreboard bulbs warming up, irregular mid-hold flicker, bulb-burnout exit.";
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
            title = "Broadcast";
            desc = "Premium TV package — vertical-unfurl entry, signature double-tap score glow, dual shimmer passes, controlled fade.";
            duration = "~3.5s";
            intensity = "Medium";
        }

        _scorebug.SetGoalAnimationStyle(style);

        if (AnimPreviewTitle != null)
        {
            AnimPreviewTitle.Text = title;
            AnimPreviewDesc.Text = desc;
            AnimPreviewDuration.Text = duration;
            AnimPreviewIntensity.Text = intensity;
        }
    }

    private MediaColor GetTeamColor(bool home)
    {
        string hex = home ? HomeColorBox.Text : AwayColorBox.Text;
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

    private UIElement? GetCurrentlyVisibleScreen()
    {
        if (_videoPlayer?.Visibility == Visibility.Visible) return _videoPlayer;
        if (_breakScreen?.Visibility == Visibility.Visible) return _breakScreen;
        if (_scoreworm?.Visibility == Visibility.Visible) return _scoreworm;
        if (_statsScreen?.Visibility == Visibility.Visible) return _statsScreen;
        if (_weatherScreen?.Visibility == Visibility.Visible) return _weatherScreen;
        if (_scorebug?.Visibility == Visibility.Visible) return _scorebug;
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

    private void LogoCropSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
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

    private void ResetHomeCrop_Click(object sender, RoutedEventArgs e)
    {
        HomeLogoZoomSlider.Value = 1.0;
        HomeLogoXSlider.Value = 0;
        HomeLogoYSlider.Value = 0;
    }

    private void ResetAwayCrop_Click(object sender, RoutedEventArgs e)
    {
        AwayLogoZoomSlider.Value = 1.0;
        AwayLogoXSlider.Value = 0;
        AwayLogoYSlider.Value = 0;
    }

    private void ApplyWindowDimensions_Click(object sender, RoutedEventArgs e)
    {
        // Floor matches MIN_TRACK_PX (the WM_GETMINMAXINFO override) so the
        // operator can dial the display window all the way down to 50×50 to
        // match the physical scoreboard's tiny render slot. The master Viewbox
        // inside every screen (scorebug, break, scoreworm, stats, weather)
        // keeps the layout, animations and overlays pixel-identical to the
        // 960×540 design canvas — they just render at a much smaller scale.
        if (!int.TryParse(WindowWidthBox.Text, out int w) || w < 50 || w > 7680)
        {
            WindowDimensionsStatus.Text = "Width must be 50–7680.";
            return;
        }

        if (!int.TryParse(WindowHeightBox.Text, out int h) || h < 50 || h > 4320)
        {
            WindowDimensionsStatus.Text = "Height must be 50–4320.";
            return;
        }

        if (_displayWindow is { IsVisible: true })
        {
            _displayWindow.Width = w;
            _displayWindow.Height = h;
            WindowDimensionsStatus.Text = $"Resized to {w} × {h}";
        }
        else if (_cricketDisplayWindow is { IsVisible: true })
        {
            _cricketDisplayWindow.Width = w;
            _cricketDisplayWindow.Height = h;
            WindowDimensionsStatus.Text = $"Resized to {w} × {h}";
        }
        else
        {
            WindowDimensionsStatus.Text = "No display window is open.";
        }
    }

    private void ApplyTeamsAndStyling_Click(object sender, RoutedEventArgs e) => ApplyTeamSettingsLive();

    private void TeamSettings_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        // Guard: TextChanged fires during XAML initialization before all controls exist
        if (!IsLoaded) return;
        // Guard: suppress during bulk sync (SyncControlPanelFields) to avoid cascading overwrites
        if (_suppressTeamSync) return;
        ApplyTeamSettingsLive();
    }

    private void ApplyTeamSettingsLive()
    {
        _match.SetTeams(HomeNameBox.Text, HomeAbbrBox.Text, AwayNameBox.Text, AwayAbbrBox.Text);
        PushAllToScoreboard();
        ApplyTeamColorsToScoringPanels();
    }

    private void PickHomeColor_Click(object sender, MouseButtonEventArgs e) => PickColorInto(HomeColorBox, HomeColorPreview);
    private void PickAwayColor_Click(object sender, MouseButtonEventArgs e) => PickColorInto(AwayColorBox, AwayColorPreview);
    private void PickHomeSecondaryColor_Click(object sender, MouseButtonEventArgs e) => PickColorInto(HomeSecondaryColorBox, HomeSecondaryColorPreview);
    private void PickAwaySecondaryColor_Click(object sender, MouseButtonEventArgs e) => PickColorInto(AwaySecondaryColorBox, AwaySecondaryColorPreview);

    private void DropperHomeColor_Click(object sender, MouseButtonEventArgs e) => DropColorInto(HomeColorBox, HomeColorPreview);
    private void DropperAwayColor_Click(object sender, MouseButtonEventArgs e) => DropColorInto(AwayColorBox, AwayColorPreview);
    private void DropperHomeSecondaryColor_Click(object sender, MouseButtonEventArgs e) => DropColorInto(HomeSecondaryColorBox, HomeSecondaryColorPreview);
    private void DropperAwaySecondaryColor_Click(object sender, MouseButtonEventArgs e) => DropColorInto(AwaySecondaryColorBox, AwaySecondaryColorPreview);

    private void PickColorInto(System.Windows.Controls.TextBox target, Border preview)
    {
        using WinForms.ColorDialog dialog = new() { FullOpen = true, AnyColor = true };
        if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;

        string hex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        target.Text = hex;
        preview.Background = new SolidColorBrush(MediaColor.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
        ApplyTeamSettingsLive();
    }

    private void DropColorInto(System.Windows.Controls.TextBox target, Border preview)
    {
        MediaColor? picked = EyeDropper.Pick();
        if (picked is null) return;

        MediaColor c = picked.Value;
        string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        target.Text = hex;
        preview.Background = new SolidColorBrush(c);
        ApplyTeamSettingsLive();
    }

    private void BrowseHomeLogo_Click(object sender, RoutedEventArgs e)
    {
        string? path = BrowseLogoFile();
        if (path is null) return;

        _homeLogoPath = path;
        HomeLogoStatusText.Text = System.IO.Path.GetFileName(path);
        ResetLogoCropState();
        PushAllToScoreboard();
    }

    private void BrowseAwayLogo_Click(object sender, RoutedEventArgs e)
    {
        string? path = BrowseLogoFile();
        if (path is null) return;

        _awayLogoPath = path;
        AwayLogoStatusText.Text = System.IO.Path.GetFileName(path);
        ResetLogoCropState();
        PushAllToScoreboard();
    }

    private void ClearHomeLogo_Click(object sender, RoutedEventArgs e)
    {
        _homeLogoPath = null;
        HomeLogoStatusText.Text = "";
        ResetLogoCropState();
        PushAllToScoreboard();
    }

    private void ClearAwayLogo_Click(object sender, RoutedEventArgs e)
    {
        _awayLogoPath = null;
        AwayLogoStatusText.Text = "";
        ResetLogoCropState();
        PushAllToScoreboard();
    }

    private static string? BrowseLogoFile()
    {
        Microsoft.Win32.OpenFileDialog dlg = new()
        {
            Filter = ImageLoadHelper.LogoFilter,
            Title = "Select Team Logo"
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private void AddMessage_Click(object sender, RoutedEventArgs e)
    {
        string text = NewMessageBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return;

        string? textColor = _newMessageTextColor;
        string? hi = _newMessageHighlightColor;

        var msg = new Roche_Scoreboard.Models.MarqueeMessage(text, textColor, hi);
        _styledMessages.Add(msg);
        _messages.Add(text);
        if (MessageList.ItemsSource is null)
            MessageList.ItemsSource = _styledMessages;

        NewMessageBox.Text = string.Empty;
        // Keep selected colours for the next message — operators usually want
        // a consistent style across consecutive messages. They can change them
        // any time via the swatch buttons.

        _scorebug?.SetMarqueeMessages((System.Collections.Generic.IList<Roche_Scoreboard.Models.MarqueeMessage>)_styledMessages);
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
        using var dlg = new WinForms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = initial
        };
        if (dlg.ShowDialog() == WinForms.DialogResult.OK)
        {
            picked = dlg.Color;
            return true;
        }
        picked = initial;
        return false;
    }

    private void PickNewTextColor_Click(object sender, RoutedEventArgs e)
    {
        var initial = ParseDrawingColor(_newMessageTextColor, System.Drawing.Color.White);
        if (!TryPickColor(initial, out var picked)) return;
        _newMessageTextColor = ToHex(picked);
        if (NewMessageTextColorBtn != null)
            NewMessageTextColorBtn.Background = new SolidColorBrush(MediaColor.FromRgb(picked.R, picked.G, picked.B));
    }

    private void PickNewHighlightColor_Click(object sender, RoutedEventArgs e)
    {
        var initial = ParseDrawingColor(_newMessageHighlightColor, System.Drawing.Color.FromArgb(255, 30, 64, 175));
        if (!TryPickColor(initial, out var picked)) return;
        _newMessageHighlightColor = ToHex(picked);
        if (NewMessageHighlightColorBtn != null)
            NewMessageHighlightColorBtn.Background = new SolidColorBrush(MediaColor.FromRgb(picked.R, picked.G, picked.B));
    }

    private void ClearNewHighlightColor_Click(object sender, MouseButtonEventArgs e)
    {
        _newMessageHighlightColor = null;
        if (NewMessageHighlightColorBtn != null)
            NewMessageHighlightColorBtn.Background = MediaBrushes.Transparent;
    }

    private void PickItemTextColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (btn.Tag is not Roche_Scoreboard.Models.MarqueeMessage msg) return;
        var initial = ParseDrawingColor(msg.TextColor, System.Drawing.Color.White);
        if (!TryPickColor(initial, out var picked)) return;
        msg.TextColor = ToHex(picked);
        _scorebug?.SetMarqueeMessages((System.Collections.Generic.IList<Roche_Scoreboard.Models.MarqueeMessage>)_styledMessages);
    }

    private void PickItemHighlightColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (btn.Tag is not Roche_Scoreboard.Models.MarqueeMessage msg) return;
        var initial = ParseDrawingColor(msg.HighlightColor, System.Drawing.Color.FromArgb(255, 30, 64, 175));
        if (!TryPickColor(initial, out var picked)) return;
        msg.HighlightColor = ToHex(picked);
        _scorebug?.SetMarqueeMessages((System.Collections.Generic.IList<Roche_Scoreboard.Models.MarqueeMessage>)_styledMessages);
    }

    private void ClearItemHighlightColor_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (btn.Tag is not Roche_Scoreboard.Models.MarqueeMessage msg) return;
        msg.HighlightColor = null;
        _scorebug?.SetMarqueeMessages((System.Collections.Generic.IList<Roche_Scoreboard.Models.MarqueeMessage>)_styledMessages);
    }

    private void RemoveMessageItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (btn.Tag is not Roche_Scoreboard.Models.MarqueeMessage msg) return;

        int index = _styledMessages.IndexOf(msg);
        if (index < 0) return;

        _styledMessages.RemoveAt(index);
        if (index < _messages.Count) _messages.RemoveAt(index);

        _scorebug?.SetMarqueeMessages((System.Collections.Generic.IList<Roche_Scoreboard.Models.MarqueeMessage>)_styledMessages);
    }

    /// <summary>
    /// Legacy handler kept for backward compatibility (e.g. ✕ button bound by index).
    /// New UI uses <see cref="RemoveMessageItem_Click"/>.
    /// </summary>
    private void RemoveMessage_Click(object sender, RoutedEventArgs e)
    {
        if (_styledMessages.Count == 0) return;
        int index = _styledMessages.Count - 1;
        _styledMessages.RemoveAt(index);
        if (index < _messages.Count) _messages.RemoveAt(index);
        _scorebug?.SetMarqueeMessages((System.Collections.Generic.IList<Roche_Scoreboard.Models.MarqueeMessage>)_styledMessages);
    }

    private void MessageItem_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        // Keep the plain-text mirror in sync with the styled collection so persistence
        // and any string-based callers see the latest text.
        _messages.Clear();
        foreach (var m in _styledMessages)
            _messages.Add(m.Text ?? string.Empty);

        _scorebug?.SetMarqueeMessages((System.Collections.Generic.IList<Roche_Scoreboard.Models.MarqueeMessage>)_styledMessages);
    }

    private void NewMessageBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        AddMessage_Click(sender, new RoutedEventArgs());
        e.Handled = true;
    }

    private void QuarterDurationBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyQuarterDuration_Click(sender, new RoutedEventArgs());
        e.Handled = true;
    }

    private void ClockMode_Changed(object sender, RoutedEventArgs e)
    {
        if (ClockModeCountdown == null || QuarterDurationPanel == null || ClockModeCountUp == null)
        {
            return;
        }

        if (ClockModeCountdown.IsChecked == true)
        {
            _match.SetClockMode(ClockMode.Countdown);
            QuarterDurationPanel.Visibility = Visibility.Visible;

            // Apply the current duration so the clock resets to the configured countdown value
            ApplyQuarterDuration_Click(sender, new RoutedEventArgs());
        }
        else
        {
            _match.SetClockMode(ClockMode.CountUp);
            QuarterDurationPanel.Visibility = Visibility.Collapsed;
        }

        if (ClockStatusText != null)
        {
            UpdateClockStatus();
        }

        UpdateSettingsClockCards();
        PushAllToScoreboard();
        BroadcastWebState();
    }

    private void ApplyQuarterDuration_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(QuarterMinutesBox.Text, out int mins)) return;
        if (!int.TryParse(QuarterSecondsBox.Text, out int secs)) return;
        if (mins < 0 || mins > 60 || secs < 0 || secs > 59) return;
        if (mins == 0 && secs == 0) return;

        _match.SetQuarterDuration(TimeSpan.FromMinutes(mins) + TimeSpan.FromSeconds(secs));
        UpdateClockStatus();
        PushAllToScoreboard();
        BroadcastWebState();
    }

    private void BrowseVideo_Click(object sender, RoutedEventArgs e)
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
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(UpdateVideoTabPlayPauseButton));
            return;
        }
        UpdateVideoTabPlayPauseButton();
        SyncPreviewToScoreboard();
    }

    private void VideoPlayer_MediaLoadFailed(object? sender, Roche_Scoreboard.Views.VideoLoadFailedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => VideoPlayer_MediaLoadFailed(sender, e)));
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
            VideoTabFilePathText.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#E6EDF3")!;

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

    private void VideoTabBrowse_Click(object sender, RoutedEventArgs e)
    {
        string videoExts = "*.mp4;*.m4v;*.mov;*.mkv;*.webm;*.avi;*.wmv;*.flv;"
                         + "*.mpg;*.mpeg;*.ts;*.mts;*.m2ts;*.3gp;*.3g2;*.ogv;*.vob;*.asf";

        if (_breakScreenDelayTimer != null)
        {
            _breakScreenDelayTimer.Stop();
            _breakScreenDelayTimer = null;
        }
        StopBreakRotation();

        Microsoft.Win32.OpenFileDialog picker = new()
        {
            Title = "Select Video File",
            Filter = $"Video Files|{videoExts}|All Files|*.*",
            CheckFileExists = true,
            DereferenceLinks = true,
            Multiselect = false
        };

        if (picker.ShowDialog() != true) return;

        _customVideoPath = picker.FileName;
        VideoTabFilePathText.Text = System.IO.Path.GetFileName(_customVideoPath);
        VideoTabFilePathText.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#E6EDF3")!;

        // Persist so the VIDEO tab can restore this clip across app restarts.
        try { Services.DisplaySettings.LastVideoPath = _customVideoPath; } catch { /* best-effort */ }

        _ = Roche_Scoreboard.Services.VideoOptimizer.EnsureOptimisedAsync(_customVideoPath);

        try
        {
            var info = new System.IO.FileInfo(_customVideoPath);
            VideoTabFileMetaText.Text = $"{FormatFileSize(info.Length)} · {info.Extension.TrimStart('.').ToUpperInvariant()} · {info.Name}";
        }
        catch
        {
            VideoTabFileMetaText.Text = string.Empty;
        }

        VideoTabApplyButton.IsEnabled = true;
        VideoTabStatusText.Text = "✓ Upload successful · ready to load.";

        // Auto-load into the scoreboard player so the operator only has to
        // click "Show on Scoreboard" next. This collapses the old three-step
        // Browse → Load → Show workflow into a single linear path.
        VideoTabApply_Click(this, new RoutedEventArgs());
    }

    private void VideoTabApply_Click(object sender, RoutedEventArgs e)
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

        _suppressVideoAutoPlay = true;
        try
        {
            // Load the scoreboard's player paused on frame zero so the
            // operator can scrub/cue in the preview before switching to it.
            _videoPlayer.SetVideoSource(_customVideoPath);
        }
        finally
        {
            _suppressVideoAutoPlay = false;
        }

        VideoTabPlayPauseButton.IsEnabled = true;
        VideoTabRestartButton.IsEnabled = true;
        if (VideoTabShowOnScoreboardButton != null) VideoTabShowOnScoreboardButton.IsEnabled = true;
        if (VideoTabUnloadButton != null) VideoTabUnloadButton.IsEnabled = true;
        EnsureVideoTabPreviewSyncTimer();
        UpdateVideoTabPlayPauseButton();
        VideoTabStatusText.Text = "Loaded · paused on first frame. Use \"Show on Scoreboard\" to broadcast.";
    }

    private void VideoTabShowOnScoreboard_Click(object sender, RoutedEventArgs e)
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
        _suppressVideoAutoPlay = true;
        try
        {
            _videoPlayer.Pause();
        }
        catch { /* ignore — VLC may still be wiring up */ }

        CurrentDisplayText.Text = "Custom Video";
        SetSelectedPreview("presentation:video");
        UpdateVideoTabPlayPauseButton();
        VideoTabStatusText.Text = "Showing on scoreboard · paused. Press Play to start.";
    }

    private void VideoTabPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_videoPlayer is null || !_videoPlayer.HasSource)
        {
            VideoTabStatusText.Text = "Load a video first.";
            return;
        }

        _videoPlayer.TogglePlayPause();
        UpdateVideoTabPlayPauseButton();
    }

    private void VideoTabRestart_Click(object sender, RoutedEventArgs e)
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

    private void VideoTabUnload_Click(object sender, RoutedEventArgs e)
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
            VideoTabFilePathText.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#5080AA")!;
        }
        if (VideoTabFileMetaText != null)
        {
            VideoTabFileMetaText.Text = "Supported: mp4, mov, mkv, webm, avi, wmv, m4v, ts, mpg, mpeg, 3gp, flv and other Windows-supported formats.";
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
        var conv = new System.Windows.Media.BrushConverter();
        if (playing)
        {
            VideoTabPlayPauseButton.Content = "⏸  Pause";
            VideoTabPlayPauseButton.BorderBrush = (System.Windows.Media.Brush)conv.ConvertFromString("#7A4A1F")!;
            VideoTabPlayPauseButton.Background = (System.Windows.Media.Brush)conv.ConvertFromString("#2A1A0E")!;
            VideoTabPlayPauseButton.Foreground = (System.Windows.Media.Brush)conv.ConvertFromString("#F0C040")!;
            VideoTabStatusText.Text = "Playing.";
        }
        else
        {
            VideoTabPlayPauseButton.Content = "▶  Play";
            VideoTabPlayPauseButton.BorderBrush = (System.Windows.Media.Brush)conv.ConvertFromString("#1F7A3F")!;
            VideoTabPlayPauseButton.Background = (System.Windows.Media.Brush)conv.ConvertFromString("#0E2A18")!;
            VideoTabPlayPauseButton.Foreground = (System.Windows.Media.Brush)conv.ConvertFromString("#7FE0A0")!;
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

    private void VideoTabSeek_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        _videoTabSeekIsDragging = true;
    }

    private void VideoTabSeek_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _videoTabSeekIsDragging = false;
        ApplySeekFromSlider();
    }

    private void VideoTabSeek_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Click-to-seek (IsMoveToPointEnabled) — apply the new position
        // immediately so it feels like YouTube's instant scrub.
        ApplySeekFromSlider();
    }

    private void VideoTabSeek_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
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

    private void VideoTabSpeed_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (VideoTabSpeedCombo?.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
        if (item.Tag is not string tag) return;
        if (!double.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double rate))
            return;

        if (_videoPlayer != null) _videoPlayer.SpeedRatio = rate;
    }

    private double _lastAppliedVolume = -1.0;
    private void VideoTabVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
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

    private void VideoTabMute_Click(object sender, RoutedEventArgs e)
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
                VideoTabPreviewPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        // Re-use an existing brush if we already attached one — there's no
        // need to rebuild it every tab activation, and recreating the brush
        // forces WPF to discard its cached intermediate render bitmap.
        if (VideoTabLiveMirror.Fill is not System.Windows.Media.VisualBrush)
        {
            var brush = new System.Windows.Media.VisualBrush(_displayContainer)
            {
                AutoLayoutContent = false,
                Stretch = System.Windows.Media.Stretch.Uniform,
                ViewboxUnits = System.Windows.Media.BrushMappingMode.RelativeToBoundingBox,
                Viewbox = new Rect(0, 0, 1, 1),
            };
            // Cache the intermediate bitmap so the brush only re-rasterises
            // when the scoreboard actually changes, instead of on every WPF
            // render tick. The cache scale is intentionally low (0.5×) — the
            // preview pane is small and the operator does not need pixel
            // accuracy here, so we trade visual sharpness for headroom on
            // lower-end devices.
            System.Windows.Media.RenderOptions.SetCachingHint(brush, System.Windows.Media.CachingHint.Cache);
            System.Windows.Media.RenderOptions.SetCacheInvalidationThresholdMinimum(brush, 0.5);
            System.Windows.Media.RenderOptions.SetCacheInvalidationThresholdMaximum(brush, 1.0);
            VideoTabLiveMirror.Fill = brush;
        }

        if (VideoTabPreviewPlaceholder != null)
            VideoTabPreviewPlaceholder.Visibility = Visibility.Collapsed;
        if (VideoTabPreviewBadge != null && !_videoTakeoverActive)
            VideoTabPreviewBadge.Text = "LIVE · MIRRORING SCOREBOARD";
    }

    private void DetachLiveMirror()
    {
        if (VideoTabLiveMirror != null) VideoTabLiveMirror.Fill = null;
        if (VideoTabPreviewPlaceholder != null)
            VideoTabPreviewPlaceholder.Visibility = Visibility.Visible;
    }

    private void VideoTabPreview_MediaOpened(object sender, RoutedEventArgs e) { }
    private void VideoTabPreview_MediaFailed(object sender, System.Windows.ExceptionRoutedEventArgs e) { }
    private void VideoTabPreview_MediaEnded(object sender, RoutedEventArgs e) { }


    private void LayoutRadio_Changed(object sender, RoutedEventArgs e)
    {
        if (_scorebug == null) return;

        ScorebugLayout layout = LayoutExpandedRadio?.IsChecked == true
            ? ScorebugLayout.Expanded
            : LayoutRetroRadio?.IsChecked == true
                ? ScorebugLayout.Retro
                : LayoutNoLogosRadio?.IsChecked == true
                    ? ScorebugLayout.NoLogos
                    : ScorebugLayout.Classic;

        _scorebug.SetLayout(layout);
        UpdateGBCountsButtonState(layout);
    }

    private void NewGame_Click(object sender, RoutedEventArgs e)
    {
        ShowResetOverlay();
    }

    private void ManualStatsBar_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:statsbar");
        if (_scorebug == null) return;
        if (_showingBreakScreen) ShowScorebug();
        ApplyCurrentTeamColorsToScorebug();
        var stats = MatchStats.Calculate(_match, _match.Quarter, _match.ElapsedInQuarter);
        _scorebug.ShowStatsBar(stats);
    }

    private void ManualWarning_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:warning");
        if (_scorebug == null) return;
        if (_showingBreakScreen) ShowScorebug();
        ApplyCurrentTeamColorsToScorebug();
        _overlayScheduler.NotifyEventDrivenOverlay();
        _scorebug.ShowFiveMinuteWarning();
    }

    private void ManualScoreless_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:lastscore");
        if (_scorebug == null) return;
        if (_showingBreakScreen) ShowScorebug();
        ApplyCurrentTeamColorsToScorebug();
        _overlayScheduler.NotifyEventDrivenOverlay();
        _scorebug.ShowLastScoreTime();
    }

    private void ManualScoringRun_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:run");
        if (_scorebug == null) return;
        if (_showingBreakScreen) ShowScorebug();

        ApplyCurrentTeamColorsToScorebug();

        // Pick a team to feature: the latest scorer if any events exist,
        // otherwise default to Home so the manual button still produces a
        // visible overlay (operators expect immediate feedback).
        var events = _match.Events;
        TeamSide team = events.Count > 0 ? events[^1].Team : TeamSide.Home;

        int runPoints;
        int opponentPoints;
        DateTime runStart;
        var run = events.Count > 0 ? CalculateScoringRun(team) : null;
        if (run is not null && run.RunPoints > 0)
        {
            runPoints = run.RunPoints;
            opponentPoints = run.OpponentPoints;
            runStart = run.Start;
        }
        else
        {
            runPoints = 1;
            opponentPoints = 0;
            runStart = DateTime.Now;
        }

        string teamName = team == TeamSide.Home ? _match.HomeName : _match.AwayName;
        var srColor = GetTeamColor(team == TeamSide.Home);
        string srHex = $"#{srColor.R:X2}{srColor.G:X2}{srColor.B:X2}";
        _overlayScheduler.NotifyEventDrivenOverlay();
        // Manual click should always (re-)show. Clear any active/queued overlay
        // first so EnqueueOverlay doesn't early-return when ScoringRun is
        // already the active or queued overlay.
        _scorebug.ClearOverlayQueue();
        _scorebug.ShowScoringRun(teamName, runPoints, opponentPoints, runStart, srHex);
    }

    private void ManualHomeDrought_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:homedrought");
        if (_scorebug == null) return;
        if (_showingBreakScreen) ShowScorebug();
        ApplyCurrentTeamColorsToScorebug();
        var since = _scorebug.HomeLastScoreTime;
        if (since == DateTime.MinValue) since = DateTime.Now - _match.ElapsedInQuarter;
        var homeColor = GetTeamColor(true);
        string homeHex = $"#{homeColor.R:X2}{homeColor.G:X2}{homeColor.B:X2}";
        _overlayScheduler.NotifyEventDrivenOverlay();
        _scorebug.ShowTeamDrought(_match.HomeName, since, homeHex, HomeSecondaryColorBox.Text);
    }

    private void ManualAwayDrought_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:awaydrought");
        if (_scorebug == null) return;
        if (_showingBreakScreen) ShowScorebug();
        ApplyCurrentTeamColorsToScorebug();
        var since = _scorebug.AwayLastScoreTime;
        if (since == DateTime.MinValue) since = DateTime.Now - _match.ElapsedInQuarter;
        var awayColor = GetTeamColor(false);
        string awayHex = $"#{awayColor.R:X2}{awayColor.G:X2}{awayColor.B:X2}";
        _overlayScheduler.NotifyEventDrivenOverlay();
        _scorebug.ShowTeamDrought(_match.AwayName, since, awayHex, AwaySecondaryColorBox.Text);
    }

    private void ManualWinProb_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:winprob");
        if (_scorebug == null) return;
        if (_showingBreakScreen) ShowScorebug();
        ApplyCurrentTeamColorsToScorebug();
        ComputeAndShowWinProbability();
    }

    private void ManualGBCounts_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:gbcounts");
        if (_scorebug == null) return;
        if (_showingBreakScreen) ShowScorebug();
        _overlayScheduler.NotifyEventDrivenOverlay();
        _scorebug.ToggleGBColumns();
    }

    private void ManualWeatherForecast_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:forecast");
        if (_scorebug == null) return;
        if (_showingBreakScreen) ShowScorebug();
        var snapshot = _scorebug.GetWeatherSnapshot();
        if (snapshot == null) return;
        _scorebug.ShowWeatherForecast(snapshot);
    }

    private void ManualWeatherStats_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:weather");
        if (_scorebug == null) return;
        if (_showingBreakScreen) ShowScorebug();
        var snapshot = _scorebug.GetWeatherSnapshot();
        if (snapshot == null) return;
        _scorebug.ShowWeatherStats(snapshot);
    }

    private void ManualRainForecast_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:rain");
        if (_scorebug == null) return;
        if (_showingBreakScreen) ShowScorebug();
        var snapshot = _scorebug.GetWeatherSnapshot();
        if (snapshot == null) return;
        _scorebug.ShowRainForecast(snapshot);
    }

    // ═══════════════════════════════════════════════════════
    //  SETTINGS: WEATHER LOCATION AUTOCOMPLETE
    // ═══════════════════════════════════════════════════════

    private bool _suppressSettingsWeatherFilter;
    private bool _settingsWeatherActivated;

    private void InitSettingsWeatherLocation()
    {
        SettingsWeatherLocation.AddHandler(
            System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(SettingsWeatherLocation_TextChanged));

        SettingsWeatherLocation.GotFocus += (_, _) => _settingsWeatherActivated = true;
        SettingsWeatherLocation.DropDownOpened += (_, _) => _settingsWeatherActivated = true;
    }

    private void SettingsWeatherLocation_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSettingsWeatherFilter || !_settingsWeatherActivated) return;

        var combo = SettingsWeatherLocation;
        string input = combo.Text ?? "";

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

        var editBox = combo.Template.FindName("PART_EditableTextBox", combo) as System.Windows.Controls.TextBox;
        int caretPos = editBox?.CaretIndex ?? input.Length;

        combo.IsDropDownOpen = matches.Count > 0;

        if (editBox is not null)
            editBox.CaretIndex = caretPos;

        _suppressSettingsWeatherFilter = false;
    }

    private void ApplyWeatherLocation_Click(object sender, RoutedEventArgs e)
    {
        string location = SettingsWeatherLocation.Text?.Trim() ?? "";
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
                Dispatcher.Invoke(() => _scorebug?.UpdateWeatherSnapshot(snap));
            };
        }

        // Avoid re-geocoding if the location hasn't changed.
        if (string.Equals(_activeWeatherLocation, location, StringComparison.OrdinalIgnoreCase)
            && _weatherService.LatestSnapshot is not null)
        {
            _scorebug?.UpdateWeatherSnapshot(_weatherService.LatestSnapshot);
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

    private void ResetMatch_Click(object sender, RoutedEventArgs e)
    {
        ShowResetOverlay();
    }

    private void ShowResetOverlay()
    {
        ResetOverlay.Visibility = Visibility.Visible;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ResetOverlay.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void CancelReset_Click(object sender, RoutedEventArgs e)
    {
        HideResetOverlay();
    }

    private void ConfirmReset_Click(object sender, RoutedEventArgs e)
    {
        HideResetOverlay();
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
            ResetOverlay.Visibility = Visibility.Collapsed;
        };
        ResetOverlay.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void ExecuteMatchReset()
    {
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
        CurrentDisplayText.Text = "";

        // Restore the match log empty state
        if (MatchLogCountBadge is not null)
            MatchLogCountBadge.Text = "";
        if (MatchLogEmptyHint is not null)
            MatchLogEmptyHint.Visibility = Visibility.Visible;

        _scorebug?.ResetAllOverlayState();
        _scorebug?.SetScores(0, 0, 0, 0);
        if (_scorebug != null) _scorebug.Visibility = Visibility.Collapsed;
        if (_breakScreen != null) _breakScreen.Visibility = Visibility.Collapsed;
        if (_scoreworm != null) _scoreworm.Visibility = Visibility.Collapsed;
        if (_statsScreen != null) _statsScreen.Visibility = Visibility.Collapsed;
        if (_videoPlayer != null) _videoPlayer.Visibility = Visibility.Collapsed;
        if (_weatherScreen != null) _weatherScreen.Visibility = Visibility.Collapsed;

        _displayWindow?.Hide();

        MainContent.BeginAnimation(OpacityProperty, null);
        SetupWizardPanel.BeginAnimation(OpacityProperty, null);
        MainContent.Visibility = Visibility.Collapsed;
        SetupWizardPanel.Visibility = Visibility.Collapsed;
        CricketSetupWizardPanel.Visibility = Visibility.Collapsed;
        CricketControlPanelView.Visibility = Visibility.Collapsed;
        MainTabControl.SelectedIndex = 0;
        SportSelectionPanel.Opacity = 1;
        SportSelectionPanel.Visibility = Visibility.Visible;
    }

    // ── Native window resize via custom grip ──

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_NCLBUTTONDOWN = 0x00A1;

    private void ResizeGrip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTBOTTOMRIGHT, IntPtr.Zero);
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
        if (_videoPlayer is { Visibility: Visibility.Visible }) return true;
        return false;
    }

    // ═══════════════════════════════════════════════════════════════
    // App update — manual check / install from the Settings tab.
    // ═══════════════════════════════════════════════════════════════

    private bool _updateCheckInFlight;

    private void RefreshUpdateStatusUi()
    {
        if (UpdateCurrentVersionText is null) return;

        UpdateCurrentVersionText.Text = $"Current version: v{AutoUpdateService.CurrentVersion.ToString(3)}";

        if (System.Windows.Application.Current is App app && app.UpdateService.UpdateAvailable)
        {
            string? tag = app.UpdateService.LatestRelease?.TagName?.TrimStart('v', 'V');
            UpdateStatusText.Text = string.IsNullOrWhiteSpace(tag)
                ? "An update is available."
                : $"Update available: v{tag}.";
            UpdateStatusText.Foreground = MediaBrushes.LightGreen;
            InstallUpdateButton.Visibility = Visibility.Visible;
            InstallUpdateButton.IsEnabled = true;
        }
        else
        {
            UpdateStatusText.Text = "No updates available — you're on the latest version.";
            UpdateStatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x8A, 0x95, 0xA3));
            InstallUpdateButton.Visibility = Visibility.Collapsed;
        }
    }

    private async void CheckForUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_updateCheckInFlight) return;
        if (System.Windows.Application.Current is not App app) return;

        _updateCheckInFlight = true;
        try
        {
            CheckForUpdateButton.IsEnabled = false;
            UpdateStatusText.Text = "Checking for updates…";
            UpdateStatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x8A, 0x95, 0xA3));
            InstallUpdateButton.Visibility = Visibility.Collapsed;

            bool available = await app.UpdateService.CheckForUpdateAsync();

            if (available)
            {
                string? tag = app.UpdateService.LatestRelease?.TagName?.TrimStart('v', 'V');
                UpdateStatusText.Text = string.IsNullOrWhiteSpace(tag)
                    ? "An update is available."
                    : $"Update available: v{tag}.";
                UpdateStatusText.Foreground = MediaBrushes.LightGreen;
                InstallUpdateButton.Visibility = Visibility.Visible;
                InstallUpdateButton.IsEnabled = true;
            }
            else
            {
                UpdateStatusText.Text = "No updates available — you're on the latest version.";
                UpdateStatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x8A, 0x95, 0xA3));
                InstallUpdateButton.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"Update check failed: {ex.Message}";
            UpdateStatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0xFF, 0x6B, 0x6B));
            InstallUpdateButton.Visibility = Visibility.Collapsed;
        }
        finally
        {
            CheckForUpdateButton.IsEnabled = true;
            _updateCheckInFlight = false;
        }
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is not App app) return;
        if (!app.UpdateService.UpdateAvailable)
        {
            UpdateStatusText.Text = "No update available.";
            return;
        }

        try
        {
            CheckForUpdateButton.IsEnabled = false;
            InstallUpdateButton.IsEnabled = false;
            UpdateProgressBar.Visibility = Visibility.Visible;
            UpdateProgressBar.Value = 0;
            UpdateStatusText.Text = "Downloading update…";

            var progress = new Progress<double>(p =>
            {
                UpdateProgressBar.Value = Math.Clamp(p * 100.0, 0, 100);
            });

            bool ok = await app.UpdateService.DownloadAndApplyAsync(progress);

            if (ok)
            {
                UpdateStatusText.Text = "Update downloaded — relaunching…";
                // Give the external updater a moment to start waiting on our PID
                // before we shut down, so it doesn't miss the exit event.
                await Task.Delay(750);
                System.Windows.Application.Current.Shutdown();
            }
            else
            {
                UpdateStatusText.Text = "Update failed. Please try again later.";
                UpdateProgressBar.Visibility = Visibility.Collapsed;
                CheckForUpdateButton.IsEnabled = true;
                InstallUpdateButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"Update failed: {ex.Message}";
            UpdateProgressBar.Visibility = Visibility.Collapsed;
            CheckForUpdateButton.IsEnabled = true;
            InstallUpdateButton.IsEnabled = true;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Presentation card handlers (Display & Messages tab)
    // ═══════════════════════════════════════════════════════════════

    private void PresCardScorebug_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SwitchToScorebug_Click(sender, new RoutedEventArgs());
        UpdatePresentationCards("scorebug");
    }

    private void PresCardSummary_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SwitchToBreakScreen_Click(sender, new RoutedEventArgs());
        UpdatePresentationCards("summary");
    }

    private void PresCardWorm_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SwitchToScoreworm_Click(sender, new RoutedEventArgs());
        UpdatePresentationCards("worm");
    }

    private void PresCardStats_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SwitchToStatsScreen_Click(sender, new RoutedEventArgs());
        UpdatePresentationCards("stats");
    }

    private void PresCardWeather_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SwitchToWeatherScreen_Click(sender, new RoutedEventArgs());
        UpdatePresentationCards("weather");
    }

    private void PresCardWindow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
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
            : (MediaColor)MediaColorConverter.ConvertFromString("#21262D"));
        WindowToggleTrack.BorderBrush = new SolidColorBrush(isOpen
            ? (MediaColor)MediaColorConverter.ConvertFromString("#3FB950")
            : (MediaColor)MediaColorConverter.ConvertFromString("#30363D"));
        WindowToggleThumb.Background = new SolidColorBrush(isOpen
            ? (MediaColor)MediaColorConverter.ConvertFromString("#3FB950")
            : (MediaColor)MediaColorConverter.ConvertFromString("#6A6A80"));

        WindowToggleLabel.Text = isOpen ? "Close" : "Open";
        WindowToggleLabel.Foreground = new SolidColorBrush(isOpen
            ? (MediaColor)MediaColorConverter.ConvertFromString("#3FB950")
            : (MediaColor)MediaColorConverter.ConvertFromString("#4A4A60"));

        PresCardWindow.BorderBrush = new SolidColorBrush(isOpen
            ? (MediaColor)MediaColorConverter.ConvertFromString("#3FB950")
            : (MediaColor)MediaColorConverter.ConvertFromString("#2A2A40"));
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
        };

        foreach (var (card, check, glow, key) in cards)
        {
            bool selected = key == active;
            card.Background = new SolidColorBrush(selected
                ? (MediaColor)MediaColorConverter.ConvertFromString("#0A1628")
                : (MediaColor)MediaColorConverter.ConvertFromString("#0E0E1A"));
            card.BorderBrush = new SolidColorBrush(selected
                ? (MediaColor)MediaColorConverter.ConvertFromString("#58A6FF")
                : (MediaColor)MediaColorConverter.ConvertFromString("#2A2A40"));
            card.BorderThickness = new Thickness(selected ? 2 : 1);
            check.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
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
                if (tb.FontWeight == FontWeights.Black && tb.FontSize >= 10)
                    tb.Foreground = new SolidColorBrush(selected
                        ? Colors.White
                        : (MediaColor)MediaColorConverter.ConvertFromString("#6A6A80"));
            }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            T? found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) yield return typed;
            foreach (T nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Overlay card handlers (Display & Messages tab)
    // ═══════════════════════════════════════════════════════════════

    private void OverlayStatsBar_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ManualStatsBar_Click(sender, new RoutedEventArgs());

    private void OverlayWinProb_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ManualWinProb_Click(sender, new RoutedEventArgs());

    private void OverlayWarning_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ManualWarning_Click(sender, new RoutedEventArgs());

    private void OverlayScoreless_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ManualScoreless_Click(sender, new RoutedEventArgs());

    private void OverlayScoringRun_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ManualScoringRun_Click(sender, new RoutedEventArgs());

    private void OverlayHomeDrought_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ManualHomeDrought_Click(sender, new RoutedEventArgs());

    private void OverlayAwayDrought_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ManualAwayDrought_Click(sender, new RoutedEventArgs());

    private void OverlayGBCounts_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ManualGBCounts_Click(sender, new RoutedEventArgs());

    private void OverlayWeatherForecast_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ManualWeatherForecast_Click(sender, new RoutedEventArgs());

    private void OverlayWeatherStats_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ManualWeatherStats_Click(sender, new RoutedEventArgs());

    private void OverlayRainForecast_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ManualRainForecast_Click(sender, new RoutedEventArgs());

    private void OverlayLeadChanges_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetSelectedPreview("overlay:leadchanges");
        if (_scorebug == null) return;
        if (_showingBreakScreen) ShowScorebug();
        _overlayScheduler.NotifyEventDrivenOverlay();
        var stats = MatchStats.Calculate(_match, _match.Quarter, _match.ElapsedInQuarter);
        _scorebug.ShowLeadChangesBar(stats.LeadChanges);
    }

    private void OverlayQtrScores_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetSelectedPreview("overlay:qtrscores");
        if (_scorebug == null) return;
        if (_showingBreakScreen) ShowScorebug();
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

    private void OverlayRecentScores_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetSelectedPreview("overlay:recentscores");
        if (_scorebug == null) return;
        if (_showingBreakScreen) ShowScorebug();
        ApplyCurrentTeamColorsToScorebug();
        _overlayScheduler.NotifyEventDrivenOverlay();
        // Manual click should always (re-)show. Clear any active/queued overlay
        // first so EnqueueOverlay doesn't early-return when RecentScores is
        // already the active or queued overlay.
        _scorebug.ClearOverlayQueue();
        _scorebug.ShowRecentScores(_match.Events, _match.Quarter);
    }

    // ═══════════════════════════════════════════════════════════════
    // Settings tab — Clock mode cards
    // ═══════════════════════════════════════════════════════════════

    private void SettingsCountUpCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ClockModeCountUp.IsChecked = true;
        UpdateSettingsClockCards();
    }

    private void SettingsCountdownCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ClockModeCountdown.IsChecked = true;
        UpdateSettingsClockCards();
    }

    private void UpdateSettingsClockCards()
    {
        bool countUp = ClockModeCountUp.IsChecked == true;

        SettingsCountUpCard.Background = new SolidColorBrush(
            (MediaColor)MediaColorConverter.ConvertFromString(countUp ? "#0A1628" : "#0E0C08"));
        SettingsCountUpCard.BorderBrush = new SolidColorBrush(
            (MediaColor)MediaColorConverter.ConvertFromString(countUp ? "#58A6FF" : "#2A2A40"));
        SettingsCountUpCard.BorderThickness = new Thickness(countUp ? 2 : 1);
        SettingsCountUpGlow.Opacity = countUp ? 0.12 : 0;
        SettingsCountUpCheck.Visibility = countUp ? Visibility.Visible : Visibility.Collapsed;

        SettingsCountdownCard.Background = new SolidColorBrush(
            (MediaColor)MediaColorConverter.ConvertFromString(!countUp ? "#1A1408" : "#0E0C08"));
        SettingsCountdownCard.BorderBrush = new SolidColorBrush(
            (MediaColor)MediaColorConverter.ConvertFromString(!countUp ? "#6B5A00" : "#3A3018"));
        SettingsCountdownCard.BorderThickness = new Thickness(!countUp ? 2 : 1);
        SettingsCountdownGlow.Opacity = !countUp ? 0.12 : 0;
        SettingsCountdownCheck.Visibility = !countUp ? Visibility.Visible : Visibility.Collapsed;
    }

    // ═══════════════════════════════════════════════════════════════
    // Settings tab — Quarter duration steppers
    // ═══════════════════════════════════════════════════════════════

    private void SettingsMinutesMinus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (int.TryParse(QuarterMinutesBox.Text, out int mins))
        {
            QuarterMinutesBox.Text = Math.Max(1, mins - 1).ToString();
            ApplyQuarterDuration_Click(sender, new RoutedEventArgs());
        }
    }

    private void SettingsMinutesPlus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (int.TryParse(QuarterMinutesBox.Text, out int mins))
        {
            QuarterMinutesBox.Text = Math.Clamp(mins + 1, 1, 60).ToString();
            ApplyQuarterDuration_Click(sender, new RoutedEventArgs());
        }
    }

    private void SettingsSecondsMinus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (int.TryParse(QuarterSecondsBox.Text, out int secs))
        {
            secs -= 5;
            if (secs < 0) secs = 55;
            QuarterSecondsBox.Text = secs.ToString("D2");
            ApplyQuarterDuration_Click(sender, new RoutedEventArgs());
        }
    }

    private void SettingsSecondsPlus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (int.TryParse(QuarterSecondsBox.Text, out int secs))
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

    private void ClockMinus10_Click(object sender, RoutedEventArgs e)
    {
        _match.AdjustElapsed(TimeSpan.FromSeconds(-10));
        UpdateLiveUI();
    }

    private void ClockMinus1_Click(object sender, RoutedEventArgs e)
    {
        _match.AdjustElapsed(TimeSpan.FromSeconds(-1));
        UpdateLiveUI();
    }

    private void ClockPlus1_Click(object sender, RoutedEventArgs e)
    {
        _match.AdjustElapsed(TimeSpan.FromSeconds(1));
        UpdateLiveUI();
    }

    private void ClockPlus10_Click(object sender, RoutedEventArgs e)
    {
        _match.AdjustElapsed(TimeSpan.FromSeconds(10));
        UpdateLiveUI();
    }

    private void ClockSet_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ClockSetMinutesBox.Text, out int mins)) mins = 0;
        if (!int.TryParse(ClockSetSecondsBox.Text, out int secs)) secs = 0;

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

    private void ClassicLayoutCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        LayoutClassicRadio.IsChecked = true;
        UpdateLayoutCards();
    }

    private void ExpandedLayoutCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        LayoutExpandedRadio.IsChecked = true;
        UpdateLayoutCards();
    }

    private void RetroLayoutCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        LayoutRetroRadio.IsChecked = true;
        UpdateLayoutCards();
    }

    private void NoLogosLayoutCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        LayoutNoLogosRadio.IsChecked = true;
        UpdateLayoutCards();
    }

    private void UpdateLayoutCards()
    {
        bool classic = LayoutClassicRadio.IsChecked == true;
        bool expanded = LayoutExpandedRadio.IsChecked == true;
        bool retro = LayoutRetroRadio.IsChecked == true;
        bool noLogos = LayoutNoLogosRadio.IsChecked == true;

        var cards = new (Border card, Border check, Border? glow, bool selected)[]
        {
            (LayoutCardClassic,  LayoutCheckClassic,  LayoutGlowClassic, classic),
            (LayoutCardExpanded, LayoutCheckExpanded,  null,              expanded),
            (LayoutCardRetro,    LayoutCheckRetro,     null,              retro),
            (LayoutCardNoLogos,  LayoutCheckNoLogos,   null,              noLogos),
        };

        foreach (var (card, check, glow, selected) in cards)
        {
            card.Background = new SolidColorBrush(selected
                ? (MediaColor)MediaColorConverter.ConvertFromString("#0A1628")
                : (MediaColor)MediaColorConverter.ConvertFromString("#0E0E1A"));
            card.BorderBrush = new SolidColorBrush(selected
                ? (MediaColor)MediaColorConverter.ConvertFromString("#58A6FF")
                : (MediaColor)MediaColorConverter.ConvertFromString("#2A2A40"));
            card.BorderThickness = new Thickness(selected ? 2 : 1);
            check.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            if (glow != null)
                glow.Opacity = selected ? 0.12 : 0;

            var stack = FindVisualChild<StackPanel>(card);
            if (stack == null) continue;
            foreach (var tb in FindVisualChildren<TextBlock>(stack))
            {
                if (tb.FontWeight == FontWeights.Black && tb.FontSize >= 11)
                    tb.Foreground = new SolidColorBrush(selected
                        ? Colors.White
                        : (MediaColor)MediaColorConverter.ConvertFromString("#6A6A80"));
            }
        }

        // G/B counts overlay only works on Classic layout
        ScorebugLayout currentLayout = expanded ? ScorebugLayout.Expanded
            : retro ? ScorebugLayout.Retro
            : noLogos ? ScorebugLayout.NoLogos
            : ScorebugLayout.Classic;
        UpdateGBCountsButtonState(currentLayout);
    }

    private void UpdateGBCountsButtonState(ScorebugLayout layout)
    {
        // G/B Counts overlay removed — no-op
    }

    // ═══════════════════════════════════════════════════════════════
    // Settings tab — Animation cards
    // ═══════════════════════════════════════════════════════════════

    private void AnimBroadcastCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        AnimBroadcastRadio.IsChecked = true;
        UpdateAnimCards();
    }

    private void AnimElectricCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        AnimElectricRadio.IsChecked = true;
        UpdateAnimCards();
    }

    private void AnimCinematicCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        AnimCinematicRadio.IsChecked = true;
        UpdateAnimCards();
    }

    private void AnimCleanCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        AnimCleanRadio.IsChecked = true;
        UpdateAnimCards();
    }

    private void AnimClassicCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        AnimClassicRadio.IsChecked = true;
        UpdateAnimCards();
    }

    private void AnimCustomVideoCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        AnimCustomVideoRadio.IsChecked = true;
        UpdateAnimCards();
    }

    private void UpdateAnimCards()
    {
        var cards = new (Border card, Border check, Border? glow, bool selected)[]
        {
            (AnimCardBroadcast,   AnimCheckBroadcast,   null,                AnimBroadcastRadio.IsChecked == true),
            (AnimCardElectric,    AnimCheckElectric,     null,                AnimElectricRadio.IsChecked == true),
            (AnimCardCinematic,   AnimCheckCinematic,    null,                AnimCinematicRadio.IsChecked == true),
            (AnimCardClean,       AnimCheckClean,        null,                AnimCleanRadio.IsChecked == true),
            (AnimCardClassic,     AnimCheckClassic,      null,                AnimClassicRadio.IsChecked == true),
            (AnimCardCustomVideo, AnimCheckCustomVideo,  AnimGlowCustomVideo, AnimCustomVideoRadio.IsChecked == true),
        };

        foreach (var (card, check, glow, selected) in cards)
        {
            bool isCustom = card == AnimCardCustomVideo;
            card.Background = new SolidColorBrush(selected
                ? (MediaColor)MediaColorConverter.ConvertFromString(isCustom ? "#1A1408" : "#0A1628")
                : (MediaColor)MediaColorConverter.ConvertFromString("#0E0E1A"));
            card.BorderBrush = new SolidColorBrush(selected
                ? (MediaColor)MediaColorConverter.ConvertFromString(isCustom ? "#6B5A00" : "#D29922")
                : (MediaColor)MediaColorConverter.ConvertFromString("#2A2A40"));
            card.BorderThickness = new Thickness(selected ? 2 : 1);
            check.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            if (glow != null)
                glow.Opacity = selected ? 0.12 : 0;

            var stack = FindVisualChild<StackPanel>(card);
            if (stack == null) continue;
            foreach (var tb in FindVisualChildren<TextBlock>(stack))
            {
                if (tb.FontWeight == FontWeights.Black && tb.FontSize >= 8)
                    tb.Foreground = new SolidColorBrush(selected
                        ? (MediaColor)MediaColorConverter.ConvertFromString(isCustom ? "#D29922" : "White")
                        : (MediaColor)MediaColorConverter.ConvertFromString("#6A6A80"));
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Settings tab — Window dimension steppers
    // ═══════════════════════════════════════════════════════════════

    private void WidthMinus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (int.TryParse(WindowWidthBox.Text, out int w))
            WindowWidthBox.Text = Math.Max(200, w - 1).ToString();
    }

    private void WidthPlus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (int.TryParse(WindowWidthBox.Text, out int w))
            WindowWidthBox.Text = Math.Min(7680, w + 1).ToString();
    }

    private void HeightMinus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (int.TryParse(WindowHeightBox.Text, out int h))
            WindowHeightBox.Text = Math.Max(100, h - 1).ToString();
    }

    private void HeightPlus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (int.TryParse(WindowHeightBox.Text, out int h))
            WindowHeightBox.Text = Math.Min(4320, h + 1).ToString();
    }

    // ═══════════════════════════════════════════════════════════════
    // Settings tab — Reset card
    // ═══════════════════════════════════════════════════════════════

    private void ResetCardSettings_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ResetMatch_Click(sender, new RoutedEventArgs());

    // ═══════════════════════════════════════════════════════════════
    // Settings tab — Navigation signpost handlers
    // ═══════════════════════════════════════════════════════════════

    private void SettingsNav_Teams_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ApplySettingsCategoryFilter("teams");
        SettingsSection_Teams.BringIntoView();
    }

    private void SettingsNav_Match_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ApplySettingsCategoryFilter("match");
        SettingsSection_Match.BringIntoView();
    }

    private void SettingsNav_Display_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ApplySettingsCategoryFilter("display");
        SettingsSection_Display.BringIntoView();
    }

    private void SettingsNav_Overlays_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ApplySettingsCategoryFilter("overlays");
        SettingsSection_Overlays.BringIntoView();
    }

    private void SettingsNav_Media_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ApplySettingsCategoryFilter("media");
        SettingsSection_Media.BringIntoView();
    }

    private void SettingsNav_Advanced_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ApplySettingsCategoryFilter("advanced");
        SettingsSection_Advanced.BringIntoView();
    }

    private void SettingsNav_All_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ApplySettingsCategoryFilter("all");

    // ═══════════════════════════════════════════════════════════════
    // Settings tab — Search & category filtering
    // ═══════════════════════════════════════════════════════════════

    private string _settingsActiveCategory = "all";

    private void ApplySettingsCategoryFilter(string category)
    {
        _settingsActiveCategory = string.IsNullOrWhiteSpace(category) ? "all" : category.ToLowerInvariant();
        UpdateSettingsCategoryPillStyles();
        ApplySettingsFilter();
    }

    private void UpdateSettingsCategoryPillStyles()
    {
        var pills = new (System.Windows.Controls.Border Pill, string Key)[]
        {
            (SettingsNavBtn_All, "all"),
            (SettingsNavBtn_Teams, "teams"),
            (SettingsNavBtn_Match, "match"),
            (SettingsNavBtn_Display, "display"),
            (SettingsNavBtn_Overlays, "overlays"),
            (SettingsNavBtn_Media, "media"),
            (SettingsNavBtn_Advanced, "advanced"),
        };
        foreach (var (pill, key) in pills)
        {
            if (pill is null) continue;
            bool active = string.Equals(key, _settingsActiveCategory, System.StringComparison.OrdinalIgnoreCase);
            pill.Background = active
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F, 0x29, 0x37))
                : System.Windows.Media.Brushes.Transparent;
            pill.BorderBrush = active
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x58, 0xA6, 0xFF))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x21, 0x26, 0x2D));
        }
    }

    private void SettingsSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (SettingsSearchPlaceholder is not null && SettingsSearchBox is not null)
            SettingsSearchPlaceholder.Visibility = string.IsNullOrEmpty(SettingsSearchBox.Text)
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
        if (SettingsSearchClear is not null && SettingsSearchBox is not null)
            SettingsSearchClear.Visibility = string.IsNullOrEmpty(SettingsSearchBox.Text)
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;
        ApplySettingsFilter();
    }

    private void SettingsSearchClear_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SettingsSearchBox is not null)
            SettingsSearchBox.Text = string.Empty;
    }

    private void ApplySettingsFilter()
    {
        string query = SettingsSearchBox?.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        string category = _settingsActiveCategory ?? "all";

        var sections = new (System.Windows.Controls.Border Section, string Key)[]
        {
            (SettingsSection_Teams,    "teams"),
            (SettingsSection_Match,    "match"),
            (SettingsSection_Display,  "display"),
            (SettingsSection_Overlays, "overlays"),
            (SettingsSection_Media,    "media"),
            (SettingsSection_Advanced, "advanced"),
        };

        int visibleCount = 0;
        foreach (var (section, key) in sections)
        {
            if (section is null) continue;
            bool categoryMatch = category == "all" || string.Equals(category, key, System.StringComparison.OrdinalIgnoreCase);
            bool queryMatch = string.IsNullOrEmpty(query) || SectionMatchesQuery(section, query);
            bool show = categoryMatch && queryMatch;
            section.Visibility = show ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            if (show) visibleCount++;
        }

        if (SettingsNoResults is not null)
            SettingsNoResults.Visibility = visibleCount == 0
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
    }

    private static bool SectionMatchesQuery(System.Windows.DependencyObject root, string query)
    {
        // Match the section's own Tag keywords first
        if (root is System.Windows.FrameworkElement fe && fe.Tag is string tag &&
            tag.ToLowerInvariant().Contains(query))
            return true;

        // Then any descendant TextBlock / TextBox / Button content
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            switch (child)
            {
                case System.Windows.Controls.TextBlock tb when !string.IsNullOrEmpty(tb.Text) &&
                    tb.Text.ToLowerInvariant().Contains(query):
                    return true;
                case System.Windows.Controls.TextBox tx when !string.IsNullOrEmpty(tx.Text) &&
                    tx.Text.ToLowerInvariant().Contains(query):
                    return true;
                case System.Windows.Controls.ContentControl cc when cc.Content is string s &&
                    s.ToLowerInvariant().Contains(query):
                    return true;
            }
            if (SectionMatchesQuery(child, query))
                return true;
        }
        return false;
    }
}

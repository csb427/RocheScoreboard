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
using Roche_Scoreboard.Web;
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

    private static TimeSpan GetGoalAnimationDuration(GoalAnimationStyle style)
    {
        return style switch
        {
            GoalAnimationStyle.Broadcast => TimeSpan.FromSeconds(1.8),
            GoalAnimationStyle.Electric => TimeSpan.FromSeconds(1.0),
            GoalAnimationStyle.Cinematic => TimeSpan.FromSeconds(3.2),
            GoalAnimationStyle.Clean => TimeSpan.FromSeconds(1.0),
            GoalAnimationStyle.Classic => TimeSpan.FromSeconds(1.7),
            _ => TimeSpan.FromSeconds(1.8)
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

        GoalAnimationStyle style = _scorebug?.GetGoalAnimationStyle() ?? GoalAnimationStyle.Broadcast;
        DeferScorePush(GetGoalAnimationDuration(style));

        ScoreEvent ev = _match.AddGoal(TeamSide.Home);

        if (style == GoalAnimationStyle.CustomVideo)
        {
            void afterVideo() => _scorebug?.PlayScoreFlipOnly(true, ev.HomeGoals, ev.HomeBehinds, ev.HomeTotal);
            PlayCustomGoalVideoIfConfigured(true, afterVideo);
            return;
        }

        _scorebug?.PlayScoreAnimation(true, true, ev.HomeGoals, ev.HomeBehinds, ev.HomeTotal);
    }

    private void HomeBehind_Click(object sender, RoutedEventArgs e)
    {
        EnsureScoreboardWindow();

        DeferScorePush(TimeSpan.FromMilliseconds(900));
        ScoreEvent ev = _match.AddBehind(TeamSide.Home);
        _scorebug?.PlayScoreAnimation(true, false, ev.HomeGoals, ev.HomeBehinds, ev.HomeTotal);
    }

    private void AwayGoal_Click(object sender, RoutedEventArgs e)
    {
        EnsureScoreboardWindow();

        GoalAnimationStyle style = _scorebug?.GetGoalAnimationStyle() ?? GoalAnimationStyle.Broadcast;
        DeferScorePush(GetGoalAnimationDuration(style));

        ScoreEvent ev = _match.AddGoal(TeamSide.Away);

        if (style == GoalAnimationStyle.CustomVideo)
        {
            void afterVideo() => _scorebug?.PlayScoreFlipOnly(false, ev.AwayGoals, ev.AwayBehinds, ev.AwayTotal);
            PlayCustomGoalVideoIfConfigured(false, afterVideo);
            return;
        }

        _scorebug?.PlayScoreAnimation(false, true, ev.AwayGoals, ev.AwayBehinds, ev.AwayTotal);
    }

    private void AwayBehind_Click(object sender, RoutedEventArgs e)
    {
        EnsureScoreboardWindow();

        DeferScorePush(TimeSpan.FromMilliseconds(900));
        ScoreEvent ev = _match.AddBehind(TeamSide.Away);
        _scorebug?.PlayScoreAnimation(false, false, ev.AwayGoals, ev.AwayBehinds, ev.AwayTotal);
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_match.UndoLastScore())
        {
            // Clear any pending score animation defer so undo shows immediately
            _deferScorePushUntilUtc = DateTime.MinValue;
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

        _deferScorePushUntilUtc = DateTime.MinValue;
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

        var snapshot = _scorebug?.GetWeatherSnapshot();
        if (snapshot == null) return;

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

    private void PlayCustomGoalVideoIfConfigured(bool isHome, Action fallbackAnimation)
    {
        if (_scorebug == null)
        {
            return;
        }

        if (_scorebug.GetGoalAnimationStyle() != GoalAnimationStyle.CustomVideo)
        {
            fallbackAnimation();
            return;
        }

        string? path = isHome ? _homeGoalVideoPath : _awayGoalVideoPath;
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path) || _goalVideoOverlay == null)
        {
            fallbackAnimation();
            return;
        }

        // If a goal video is already playing, stop it and fire its pending callback
        // so the previous score flip completes before starting the new one
        if (_goalVideoOverlay.Source != null && _goalVideoCompletionCallback != null)
        {
            _goalVideoOverlay.Stop();
            _goalVideoOverlay.Visibility = Visibility.Collapsed;
            Action? prev = _goalVideoCompletionCallback;
            _goalVideoCompletionCallback = null;
            prev.Invoke();
        }

        try
        {
            _goalVideoCompletionCallback = fallbackAnimation;
            _goalVideoOverlay.Source = new Uri(path, UriKind.Absolute);
            _goalVideoOverlay.Visibility = Visibility.Visible;
            _goalVideoOverlay.Position = TimeSpan.Zero;
            _goalVideoOverlay.Play();
        }
        catch (UriFormatException)
        {
            _goalVideoCompletionCallback = null;
            _goalVideoOverlay.Visibility = Visibility.Collapsed;
            fallbackAnimation();
        }
    }

    private void GoalVideo_MediaEnded(object? sender, RoutedEventArgs e)
    {
        _goalVideoOverlay?.Stop();
        if (_goalVideoOverlay != null)
        {
            _goalVideoOverlay.Visibility = Visibility.Collapsed;
        }

        Action? callback = _goalVideoCompletionCallback;
        _goalVideoCompletionCallback = null;
        callback?.Invoke();
    }

    private void GoalVideo_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        _goalVideoOverlay?.Stop();
        if (_goalVideoOverlay != null)
        {
            _goalVideoOverlay.Visibility = Visibility.Collapsed;
        }

        Action? callback = _goalVideoCompletionCallback;
        _goalVideoCompletionCallback = null;
        callback?.Invoke();
    }

    private void StopStatsBarTimer()
    {
        _statsBarTimer?.Stop();
        _statsBarTimer = null;
        _statsBarHideTimer?.Stop();
        _statsBarHideTimer = null;
    }

    private void StopGoalVideo()
    {
        if (_goalVideoOverlay == null) return;

        _goalVideoOverlay.Stop();
        _goalVideoOverlay.Source = null;
        _goalVideoOverlay.Visibility = Visibility.Collapsed;
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
        // Shared cadence for every scheduled overlay. The interval is set just
        // below the quarter length so any single overlay can appear at most
        // about once per quarter, leaving room for the other six to also rotate
        // through. Priority is also uniform so the late-quarter high-priority
        // filter treats them all the same way.
        TimeSpan sharedInterval = TimeSpan.FromMinutes(4);
        TimeSpan sharedDuration = TimeSpan.FromSeconds(12);
        const int sharedPriority = 80;

        _overlayScheduler.RegisterOverlays(
        [
            new OverlayDefinition
            {
                Kind = OverlayKind.StatsBar,
                DisplayDuration = sharedDuration,
                MinInterval = sharedInterval,
                Priority = sharedPriority,
                IsRelevant = () => _match.Events.Count > 0
            },
            new OverlayDefinition
            {
                Kind = OverlayKind.WinProbability,
                DisplayDuration = sharedDuration,
                MinInterval = sharedInterval,
                Priority = sharedPriority,
                IsRelevant = () => _match.Events.Count >= 3
            },
            new OverlayDefinition
            {
                Kind = OverlayKind.Forecast,
                DisplayDuration = sharedDuration,
                MinInterval = sharedInterval,
                Priority = sharedPriority,
                IsRelevant = () => _scorebug?.GetWeatherSnapshot() is not null
            },
            new OverlayDefinition
            {
                Kind = OverlayKind.Weather,
                DisplayDuration = sharedDuration,
                MinInterval = sharedInterval,
                Priority = sharedPriority,
                IsRelevant = () => _scorebug?.GetWeatherSnapshot() is not null
            },
            new OverlayDefinition
            {
                Kind = OverlayKind.Rain,
                DisplayDuration = sharedDuration,
                MinInterval = sharedInterval,
                Priority = sharedPriority,
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
                MinInterval = sharedInterval,
                Priority = sharedPriority,
                IsRelevant = () => _match.Events.Count > 0
            },
            new OverlayDefinition
            {
                Kind = OverlayKind.RecentScores,
                DisplayDuration = TimeSpan.FromSeconds(14),
                MinInterval = sharedInterval,
                Priority = sharedPriority,
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
            VideoPickerPrompt.Visibility = Visibility.Collapsed;
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

        if (target == _videoPlayer)
        {
            _videoPlayer?.Play();
        }
    }

    private void SlideTransition(UIElement from, UIElement to, bool slideLeft)
    {
        double width = _displayWindow?.ActualWidth ?? 752;
        double direction = slideLeft ? -1 : 1;

        to.Visibility = Visibility.Visible;
        to.Opacity = 0;
        to.RenderTransform = new TranslateTransform(-direction * width, 0);

        if (from.RenderTransform is not TranslateTransform)
        {
            from.RenderTransform = new TranslateTransform(0, 0);
        }

        CubicEase ease = new() { EasingMode = EasingMode.EaseInOut };
        TimeSpan duration = TimeSpan.FromMilliseconds(360);

        DoubleAnimation fromAnim = new(0, direction * width, duration) { EasingFunction = ease };
        DoubleAnimation toAnim = new(-direction * width, 0, duration) { EasingFunction = ease };

        // Crossfade: outgoing fades out, incoming fades in
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
        { BeginTime = TimeSpan.FromMilliseconds(80), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

        fromAnim.Completed += (_, _) =>
        {
            from.Visibility = Visibility.Collapsed;
            from.RenderTransform = new TranslateTransform(0, 0);
            from.Opacity = 1;
        };

        ((TranslateTransform)from.RenderTransform).BeginAnimation(TranslateTransform.XProperty, fromAnim);
        ((TranslateTransform)to.RenderTransform).BeginAnimation(TranslateTransform.XProperty, toAnim);
        from.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        to.BeginAnimation(UIElement.OpacityProperty, fadeIn);
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
            desc = "Lightning strobe flashes, instant text snap with micro-vibration, reversed shimmer discharge.";
            duration = "~1.2s";
            intensity = "High";
        }
        else if (AnimCinematicRadio?.IsChecked == true)
        {
            style = GoalAnimationStyle.Cinematic;
            title = "Cinematic";
            desc = "Epic title drop — text falls from 3× with elastic bounce, slow glow build, dual shimmer.";
            duration = "~3.2s";
            intensity = "Low";
        }
        else if (AnimCleanRadio?.IsChecked == true)
        {
            style = GoalAnimationStyle.Clean;
            title = "Clean";
            desc = "Organic heartbeat pulse of light. No overlay or text — just a breath of score glow.";
            duration = "~0.8s";
            intensity = "Minimal";
        }
        else if (AnimClassicRadio?.IsChecked == true)
        {
            style = GoalAnimationStyle.Classic;
            title = "Classic";
            desc = "Vintage flicker — overlay stutters on like old bulbs warming up, then flickers off.";
            duration = "~1.8s";
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
            desc = "TV graphics package — sharp snap-in with double-tap glow and horizontal text unfurl.";
            duration = "~2.0s";
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
        Microsoft.Win32.OpenFileDialog picker = new()
        {
            Title = "Select Video File",
            Filter = "Video Files|*.mp4;*.avi;*.wmv;*.mov;*.mkv|All Files|*.*"
        };

        if (picker.ShowDialog() != true) return;

        _customVideoPath = picker.FileName;
        VideoFilePathText.Text = System.IO.Path.GetFileName(_customVideoPath);
        ApplyVideoButton.IsEnabled = true;
    }

    private void ApplyVideo_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_customVideoPath) || !System.IO.File.Exists(_customVideoPath)) return;

        EnsureScoreboardWindow();
        if (_videoPlayer == null) return;

        _videoPlayer.SetVideoSource(_customVideoPath);
        SwitchToScreen(_videoPlayer, "Custom Video");
        VideoPickerPrompt.Visibility = Visibility.Collapsed;
    }

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
        StopGoalVideo();

        _match.ResetForNewGame();
        _winProbEngine.Reset();
        _fiveMinWarningShown = false;
        _previousRemainingTime = TimeSpan.MaxValue;
        _deferScorePushUntilUtc = DateTime.MinValue;
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

    // ── Web server: state broadcast + command bridge ──

    /// <summary>
    /// Builds a ScoreboardState snapshot and broadcasts it to all connected web clients.
    /// </summary>
    private async void BroadcastWebState()
    {
        if (_webHost is null) return;

        var dc = _match.DisplayClock;
        bool q4Ended = _match.Quarter == 4 && _match.GetQuarterSnapshot(4) != null;

        var state = new ScoreboardState
        {
            HomeName = _match.HomeName,
            HomeAbbr = _match.HomeAbbr,
            AwayName = _match.AwayName,
            AwayAbbr = _match.AwayAbbr,
            HomeGoals = _match.HomeGoals,
            HomeBehinds = _match.HomeBehinds,
            HomeTotal = _match.HomeTotal,
            AwayGoals = _match.AwayGoals,
            AwayBehinds = _match.AwayBehinds,
            AwayTotal = _match.AwayTotal,
            Margin = _match.Margin,
            Quarter = _match.Quarter,
            Clock = $"{(int)dc.TotalMinutes:D2}:{dc.Seconds:D2}",
            ClockRunning = _match.ClockRunning,
            ClockMode = _match.ClockMode.ToString(),
            Q4Ended = q4Ended,
            HomePrimaryColor = HomeColorBox.Text,
            HomeSecondaryColor = HomeSecondaryColorBox.Text,
            AwayPrimaryColor = AwayColorBox.Text,
            AwaySecondaryColor = AwaySecondaryColorBox.Text,
            ActiveScreen = _activeScreenKey,
            EventCount = _match.Events.Count,
            LastEvent = _match.Events.Count > 0
                ? _match.Events[^1].FormatLog(_match.HomeName, _match.AwayName)
                : "",
            MatchResult = MatchResultText.Text
        };

        try
        {
            await _webHost.BroadcastStateAsync(state);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Web broadcast error: {ex.Message}");
        }
    }

    // ── Live view: frame capture + streaming ──

    private DispatcherTimer? _frameCaptureTimer;
    private bool _isCapturingFrame;

    // Capture target dimensions are capped so the per-frame software render
    // stays cheap. The display window can be 1080p+, but the live web preview
    // doesn't need anywhere near that resolution.
    private const int FrameCaptureMaxWidth = 640;
    private const int FrameCaptureJpegQuality = 60;

    // Normal capture cadence and a much slower cadence used while a video is
    // playing on the display window. Capturing video frames at 30fps via
    // RenderTargetBitmap on the UI thread starves the video presenter and
    // causes the entire app (and the video itself) to drop to ~5fps.
    private static readonly TimeSpan FrameCaptureIntervalIdle = TimeSpan.FromMilliseconds(66);   // ~15 fps
    private static readonly TimeSpan FrameCaptureIntervalVideo = TimeSpan.FromMilliseconds(500); //  ~2 fps

    /// <summary>
    /// Starts a timer that periodically captures the display window content
    /// and streams it to live-view web clients as JPEG frames. The cadence
    /// auto-drops while video is playing so the MediaElement keeps its full
    /// frame budget on the UI thread.
    /// </summary>
    private void StartFrameCapture()
    {
        if (_frameCaptureTimer is not null) return;

        // DispatcherPriority.Background so the capture never preempts video
        // rendering or input handling — the live preview is a nice-to-have,
        // smooth playback is critical.
        _frameCaptureTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = FrameCaptureIntervalIdle
        };
        _frameCaptureTimer.Tick += FrameCapture_Tick;
        _frameCaptureTimer.Start();
    }

    private void StopFrameCapture()
    {
        _frameCaptureTimer?.Stop();
        _frameCaptureTimer = null;
    }

    /// <summary>
    /// True if any of the on-screen MediaElements is actively displaying a
    /// video. Used to throttle frame capture so we don't fight the video
    /// presenter for UI-thread time.
    /// </summary>
    private bool IsAnyVideoPlaying()
    {
        if (_videoPlayer is { Visibility: Visibility.Visible }) return true;
        if (_goalVideoOverlay is { Visibility: Visibility.Visible, Source: not null }) return true;
        return false;
    }

    private async void FrameCapture_Tick(object? sender, EventArgs e)
    {
        if (_webHost is null || _isCapturingFrame) return;

        // Adapt cadence based on whether a video is playing. This is the
        // single most important fix for video lag while the scoreboard is
        // mirroring — RenderTargetBitmap.Render() forces the MediaElement
        // into a software-render path, which collapses video to ~5fps and
        // drags the rest of the UI down with it. While video is active we
        // throttle the capture interval AND skip the actual capture work
        // so the GPU video presenter keeps full UI-thread time.
        bool videoPlaying = IsAnyVideoPlaying();
        if (_frameCaptureTimer is not null)
        {
            var desiredInterval = videoPlaying ? FrameCaptureIntervalVideo : FrameCaptureIntervalIdle;
            if (_frameCaptureTimer.Interval != desiredInterval)
                _frameCaptureTimer.Interval = desiredInterval;
        }

        if (videoPlaying)
            return;

        // Determine which visual to capture — AFL or Cricket display
        FrameworkElement? target = _displayContainer as FrameworkElement;
        if (target is null && _cricketDisplayWindow?.Content is FrameworkElement cricketContent)
            target = cricketContent;
        if (target is null) return;

        _isCapturingFrame = true;
        try
        {
            // 1. Render to a frozen bitmap on the UI thread (this part *must*
            //    run on the UI thread). We deliberately render at 96 DPI and
            //    cap the output width so the software render is cheap even on
            //    a 1080p display window.
            var rtb = RenderVisualForCapture(target);
            if (rtb is null) return;

            // 2. Encode the JPEG on a background thread so we don't tie up
            //    the UI dispatcher while the video presenter wants to draw.
            string base64 = await Task.Run(() => EncodeJpegBase64(rtb, FrameCaptureJpegQuality));

            if (!string.IsNullOrEmpty(base64))
            {
                await _webHost.BroadcastFrameAsync(base64);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Frame capture error: {ex.Message}");
        }
        finally
        {
            _isCapturingFrame = false;
        }
    }

    /// <summary>
    /// Renders the given visual to a frozen <see cref="System.Windows.Media.Imaging.RenderTargetBitmap"/>
    /// suitable for cross-thread JPEG encoding. The output is downscaled so
    /// it never exceeds <see cref="FrameCaptureMaxWidth"/> pixels wide.
    /// </summary>
    private static System.Windows.Media.Imaging.RenderTargetBitmap? RenderVisualForCapture(FrameworkElement visual)
    {
        double width = visual.ActualWidth;
        double height = visual.ActualHeight;
        if (width < 1 || height < 1) return null;

        // Downscale so the software render is fast regardless of the display
        // window's actual size. Web preview clients don't need full-res frames.
        double scale = 1.0;
        if (width > FrameCaptureMaxWidth)
            scale = FrameCaptureMaxWidth / width;

        int pixelWidth = Math.Max(1, (int)(width * scale));
        int pixelHeight = Math.Max(1, (int)(height * scale));

        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
            pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);

        if (scale < 1.0)
        {
            // Render through a scaling DrawingVisual so we don't render at
            // full resolution and then downscale (which would defeat the
            // performance win).
            var dv = new System.Windows.Media.DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                var brush = new System.Windows.Media.VisualBrush(visual)
                {
                    Stretch = System.Windows.Media.Stretch.None,
                    AlignmentX = System.Windows.Media.AlignmentX.Left,
                    AlignmentY = System.Windows.Media.AlignmentY.Top
                };
                ctx.PushTransform(new System.Windows.Media.ScaleTransform(scale, scale));
                ctx.DrawRectangle(brush, null, new Rect(0, 0, width, height));
                ctx.Pop();
            }
            rtb.Render(dv);
        }
        else
        {
            rtb.Render(visual);
        }

        rtb.Freeze();
        return rtb;
    }

    /// <summary>
    /// Encodes a frozen bitmap as a base64 JPEG string. Safe to call from a
    /// background thread because the bitmap has been frozen.
    /// </summary>
    private static string EncodeJpegBase64(System.Windows.Media.Imaging.BitmapSource bitmap, int quality)
    {
        var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));

        using var ms = new System.IO.MemoryStream();
        encoder.Save(ms);
        return Convert.ToBase64String(ms.GetBuffer(), 0, (int)ms.Length);
    }

    /// <summary>
    /// Handles commands received from web control panel clients.
    /// Dispatches to the WPF UI thread since SignalR calls arrive on a thread-pool thread.
    /// </summary>
    private void OnWebCommand(string command, string? parameter)
    {
        Dispatcher.BeginInvoke(() =>
        {
            bool q4Ended = _match.Quarter == 4 && _match.GetQuarterSnapshot(4) != null;

            switch (command)
            {
                case "homeGoal":
                    if (!q4Ended) HomeGoal_Click(this, new RoutedEventArgs());
                    break;
                case "homeBehind":
                    if (!q4Ended) HomeBehind_Click(this, new RoutedEventArgs());
                    break;
                case "awayGoal":
                    if (!q4Ended) AwayGoal_Click(this, new RoutedEventArgs());
                    break;
                case "awayBehind":
                    if (!q4Ended) AwayBehind_Click(this, new RoutedEventArgs());
                    break;
                case "startPause":
                    if (!q4Ended) StartPause_Click(this, new RoutedEventArgs());
                    break;
                case "endQuarter":
                    if (!q4Ended) EndQuarter_Click(this, new RoutedEventArgs());
                    break;
                case "undo":
                    Undo_Click(this, new RoutedEventArgs());
                    break;
                case "showWindow":
                    ShowScoreboard_Click(this, new RoutedEventArgs());
                    break;
                case "switchScreen":
                    HandleWebScreenSwitch(parameter);
                    break;
                case "setTeams":
                    HandleWebSetTeams(parameter);
                    break;
                case "setColors":
                    HandleWebSetColors(parameter);
                    break;
                case "setClockMode":
                    HandleWebSetClockMode(parameter);
                    break;
                case "addMessage":
                    HandleWebAddMessage(parameter);
                    break;
                case "resetMatch":
                    ResetMatch_Click(this, new RoutedEventArgs());
                    break;
            }
        });
    }

    private void HandleWebScreenSwitch(string? screen)
    {
        switch (screen)
        {
            case "scorebug":
                SwitchToScorebug_Click(this, new RoutedEventArgs());
                break;
            case "summary":
                SwitchToBreakScreen_Click(this, new RoutedEventArgs());
                break;
            case "worm":
                SwitchToScoreworm_Click(this, new RoutedEventArgs());
                break;
            case "stats":
                SwitchToStatsScreen_Click(this, new RoutedEventArgs());
                break;
            case "weather":
                SwitchToWeatherScreen_Click(this, new RoutedEventArgs());
                break;
            case "video":
                SwitchToVideo_Click(this, new RoutedEventArgs());
                break;
        }
    }

    private void HandleWebSetTeams(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            string homeName = root.GetProperty("homeName").GetString() ?? "";
            string homeAbbr = root.GetProperty("homeAbbr").GetString() ?? "";
            string awayName = root.GetProperty("awayName").GetString() ?? "";
            string awayAbbr = root.GetProperty("awayAbbr").GetString() ?? "";

            _match.SetTeams(homeName, homeAbbr, awayName, awayAbbr);
            SyncControlPanelFields();
            ApplyTeamColorsToScoringPanels();
            PushAllToScoreboard();
        }
        catch (System.Text.Json.JsonException)
        {
            // Ignore malformed JSON from web client
        }
    }

    private void HandleWebSetColors(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            HomeColorBox.Text = root.GetProperty("homePrimary").GetString() ?? HomeColorBox.Text;
            HomeSecondaryColorBox.Text = root.GetProperty("homeSecondary").GetString() ?? HomeSecondaryColorBox.Text;
            AwayColorBox.Text = root.GetProperty("awayPrimary").GetString() ?? AwayColorBox.Text;
            AwaySecondaryColorBox.Text = root.GetProperty("awaySecondary").GetString() ?? AwaySecondaryColorBox.Text;

            TrySetPreview(HomeColorPreview, HomeColorBox.Text);
            TrySetPreview(HomeSecondaryColorPreview, HomeSecondaryColorBox.Text);
            TrySetPreview(AwayColorPreview, AwayColorBox.Text);
            TrySetPreview(AwaySecondaryColorPreview, AwaySecondaryColorBox.Text);

            ApplyTeamColorsToScoringPanels();
            PushAllToScoreboard();
            BroadcastWebState();
        }
        catch (System.Text.Json.JsonException)
        {
            // Ignore malformed JSON from web client
        }
    }

    private void HandleWebSetClockMode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            string mode = root.GetProperty("mode").GetString() ?? "CountUp";
            int minutes = root.TryGetProperty("minutes", out var minEl) ? minEl.GetInt32() : 20;
            int seconds = root.TryGetProperty("seconds", out var secEl) ? secEl.GetInt32() : 0;
            seconds = Math.Clamp(seconds, 0, 59);

            if (mode == "Countdown")
            {
                ClockModeCountdown.IsChecked = true;
                _match.SetClockMode(ClockMode.Countdown);
                _match.SetQuarterDuration(TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds));
                QuarterMinutesBox.Text = minutes.ToString();
                QuarterSecondsBox.Text = seconds.ToString("D2");
            }
            else
            {
                ClockModeCountUp.IsChecked = true;
                _match.SetClockMode(ClockMode.CountUp);
            }

            PushAllToScoreboard();
            BroadcastWebState();
        }
        catch (System.Text.Json.JsonException)
        {
            // Ignore malformed JSON from web client
        }
    }

    private void HandleWebAddMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var styled = new Roche_Scoreboard.Models.MarqueeMessage(message);
        _styledMessages.Add(styled);
        _messages.Add(message);
        if (MessageList.ItemsSource is null)
            MessageList.ItemsSource = _styledMessages;

        _scorebug?.SetMarqueeMessages((System.Collections.Generic.IList<Roche_Scoreboard.Models.MarqueeMessage>)_styledMessages);
        BroadcastWebState();
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

    private void PresCardVideo_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SwitchToVideo_Click(sender, new RoutedEventArgs());
        UpdatePresentationCards("video");
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
            (PresCardVideo,    PresCheckVideo,    null,              "video"),
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
        => SettingsSection_Teams.BringIntoView();

    private void SettingsNav_Match_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => SettingsSection_Match.BringIntoView();

    private void SettingsNav_Display_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => SettingsSection_Display.BringIntoView();

    private void SettingsNav_Overlays_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => SettingsSection_Overlays.BringIntoView();

    private void SettingsNav_Media_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => SettingsSection_Media.BringIntoView();

    private void SettingsNav_Advanced_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => SettingsSection_Advanced.BringIntoView();
}

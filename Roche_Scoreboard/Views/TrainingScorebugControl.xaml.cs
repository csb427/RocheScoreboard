using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Color = System.Windows.Media.Color;
using UserControl = System.Windows.Controls.UserControl;

namespace Roche_Scoreboard.Views;

/// <summary>
/// Display-window content for Training mode. Renders the active timer huge,
/// the title above and an "UP NEXT" strip below; acknowledges completion with
/// a steady success cue and a smooth slide/scale transition to the next
/// timer once the post-finish hold elapses.
/// </summary>
public partial class TrainingScorebugControl : UserControl
{
    private TrainingSession? _session;
    private readonly DispatcherTimer _uiTimer;

    private static readonly CubicEase EaseOut = new() { EasingMode = EasingMode.EaseOut };
    private static readonly CubicEase EaseInOut = new() { EasingMode = EasingMode.EaseInOut };

    /// <summary>How long the finished timer stays on screen after hitting zero.</summary>
    private static readonly TimeSpan FinishHold = TimeSpan.FromMilliseconds(7500);

    /// <summary>Total length of the final session-complete presentation.</summary>
    private static readonly TimeSpan SessionCompleteHold = TimeSpan.FromSeconds(10);

    private DispatcherTimer? _holdTimer;
    private DispatcherTimer? _sessionCompleteTimer;
    private DispatcherTimer? _promotionTimer;
    private bool _flashing;
    private bool _advancingAfterFinish;
    private Guid? _flashActiveId;
    private int _visualGeneration;

    public TrainingScorebugControl()
    {
        InitializeComponent();
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _uiTimer.Tick += (_, _) =>
        {
            // Always tick the staged "NOW STARTING" preview if it's visible —
            // even when the main scorebug is locked during a flash.
            if (_flashing && _session is not null && StagedTimerHost.Visibility == Visibility.Visible)
            {
                StagedTimerText.Text = FormatTime(_session.ActiveRemaining);
            }
            UpdateLive();
        };
        Loaded += (_, _) => { AttachSessionEvents(); UpdateLive(); UpdateTitleScroll(); UpdateUpNext(); _uiTimer.Start(); };
        Unloaded += (_, _) => { _uiTimer.Stop(); DetachSessionEvents(); ResetVisuals(); StopTitleScroll(); };
    }

    public void Bind(TrainingSession session)
    {
        DetachSessionEvents();
        ResetVisuals();
        _session = session;
        AttachSessionEvents();

        OnStructuralChanged();
        OnSessionChanged();
    }

    private void DetachSessionEvents()
    {
        if (_session is null) return;
        _session.Changed -= OnSessionChanged;
        _session.StructuralChanged -= OnStructuralChanged;
        _session.IntervalFinished -= OnIntervalFinished;
    }

    private void AttachSessionEvents()
    {
        DetachSessionEvents();
        if (_session is null) return;
        _session.Changed += OnSessionChanged;
        _session.StructuralChanged += OnStructuralChanged;
        _session.IntervalFinished += OnIntervalFinished;
    }

    private void CancelInterruptedFinish()
    {
        // Our own advance stages the next timer. A later Clear/Skip/Remove
        // replaces that timer and must cancel every pending visual callback.
        if (_flashing && !_advancingAfterFinish
            && (_session?.Active?.Id != _flashActiveId || _session?.Timers.Count == 0)) ResetVisuals();
    }

    /// <summary>Light-weight refresh: live values only, no expensive layout work.</summary>
    private void OnSessionChanged()
    {
        CancelInterruptedFinish();
        if (_flashing)
        {
            // While the finish flash is running, the visible scorebug stays
            // locked on the FINISHED panel. The only thing we live-update is
            // the "NOW STARTING" preview at the bottom — its clock ticks down
            // in real time as the next timer runs in the background.
            if (_session is not null && StagedTimerHost.Visibility == Visibility.Visible)
            {
                StagedTitleText.Text = _session.Active?.Title.ToUpperInvariant() ?? "";
                StagedTimerText.Text = FormatTime(_session.ActiveRemaining);
            }
            return;
        }
        UpdateLive();
    }

    /// <summary>Heavy refresh: only fires when the queue actually changes structure.</summary>
    private void OnStructuralChanged()
    {
        CancelInterruptedFinish();
        if (_flashing) return; // up-next visibility is owned by the finish flow during a flash
        UpdateUpNext();
    }

    private void UpdateLive()
    {
        if (_session is null) return;
        if (_flashing) return;

        var active = _session.Active;
        if (active is null)
        {
            SetTitleText("NO TIMERS");
            SetActiveDurationReadout(TimeSpan.Zero, TimeSpan.Zero);
            UpdateProgressSegments(0);
            return;
        }

        SetTitleText(active.Title.ToUpperInvariant());

        TimeSpan rem = _session.ActiveRemaining;
        SetActiveDurationReadout(rem, active.Duration);

        // Progress fills FROM zero TO full as time elapses
        double frac = active.Duration.TotalSeconds <= 0
            ? 0
            : Math.Clamp(1 - (rem.TotalSeconds / active.Duration.TotalSeconds), 0, 1);
        UpdateProgressSegments(frac);

        // Pulse the timer glow when in the final 10 seconds for urgency cue.
        if (!active.HasFinished && _session.IsRunning && rem.TotalSeconds <= 10)
        {
            PulseTimerGlow();
        }
    }

    private DateTime _lastPulseUtc = DateTime.MinValue;
    private void PulseTimerGlow()
    {
        // Throttle so we only kick off a pulse once per second
        if ((DateTime.UtcNow - _lastPulseUtc).TotalSeconds < 1) return;
        _lastPulseUtc = DateTime.UtcNow;

        var anim = new DoubleAnimation(0.7, 1.0, TimeSpan.FromMilliseconds(220))
        {
            AutoReverse = true,
            EasingFunction = EaseInOut
        };
        TimerGlow.BeginAnimation(DropShadowEffect.OpacityProperty, anim);
    }

    private string _lastUpNextTitle = string.Empty;
    private void UpdateUpNext()
    {
        if (_session is null) return;
        var next = _session.UpNext;
        if (next is null)
        {
            ShowUpNext(false);
            return;
        }
        UpNextDuration.Text = FormatTime(next.Duration);
        if (!string.Equals(_lastUpNextTitle, next.Title, StringComparison.Ordinal))
        {
            _lastUpNextTitle = next.Title;
            UpNextTitle.Text = next.Title;
        }
        ShowUpNext(true);
    }

    private bool _upNextVisible;
    /// <summary>
    /// Toggles the up-next bar via Visibility + opacity fade. We do NOT
    /// animate Height any more — starting at Height=0 with ClipToBounds=True
    /// was causing WPF to skip the child measure pass on first show, leaving
    /// the title text invisible.
    /// </summary>
    private void ShowUpNext(bool show)
    {
        if (_upNextVisible == show) return;
        _upNextVisible = show;

        UpNextBar.BeginAnimation(OpacityProperty, null);

        if (show)
        {
            UpNextBar.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(UpNextBar.Opacity, 1, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = EaseOut
            };
            UpNextBar.BeginAnimation(OpacityProperty, fadeIn);
        }
        else
        {
            var fadeOut = new DoubleAnimation(UpNextBar.Opacity, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = EaseOut
            };
            fadeOut.Completed += (_, _) =>
            {
                if (!_upNextVisible) UpNextBar.Visibility = Visibility.Collapsed;
            };
            UpNextBar.BeginAnimation(OpacityProperty, fadeOut);
        }
    }

    /// <summary>
    /// Legacy entry point preserved for the finish-flash flow which used to
    /// drive the bar via height. Now just maps to Visibility — any non-zero
    /// target means "show".
    /// </summary>
    private void AnimateUpNext(double targetHeight) => ShowUpNext(targetHeight > 0);

    private string _lastTitleText = string.Empty;
    private bool _titleScrollNeedsRefresh;
    private void SetTitleText(string text)
    {
        if (string.Equals(_lastTitleText, text, StringComparison.Ordinal))
        {
            // Hide does not necessarily unload the output. An owned finish
            // retirement may have stopped this same title while it was hidden.
            // Retry only that invalidated layout, never measure every UI tick.
            if (_titleScrollNeedsRefresh && CanPresentTitle()) UpdateTitleScroll();
            return;
        }
        _lastTitleText = text;
        TitleText.Text = text;
        UpdateTitleScroll();
    }

    private bool CanPresentTitle() => IsLoaded && IsVisible
        && Window.GetWindow(this) is { IsVisible: true };

    /// <summary>
    /// Measures the title against the visible clip width. If it overflows,
    /// kick off a back-and-forth (reverse) translate animation; otherwise
    /// hold it centred (Canvas.Left mid-position).
    /// </summary>
    private void UpdateTitleScroll()
    {
        int generation = _visualGeneration;
        string cachedTitle = _lastTitleText, displayedTitle = TitleText.Text;
        bool Current() => generation == _visualGeneration
            && string.Equals(cachedTitle, _lastTitleText, StringComparison.Ordinal)
            && string.Equals(displayedTitle, TitleText.Text, StringComparison.Ordinal);
        // Stop any previous scroll animation.
        StopTitleScroll();
        if (!Current() || !CanPresentTitle()) return;

        // Force the TextBlock to measure now so DesiredSize is current.
        TitleText.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        if (!Current() || !CanPresentTitle()) return;
        double textWidth = TitleText.DesiredSize.Width;
        double clipWidth = TitleClip.Width;

        if (textWidth <= clipWidth - 8)
        {
            // Fits — centre it within the clip canvas.
            Canvas.SetLeft(TitleText, (clipWidth - textWidth) / 2);
            if (Current() && CanPresentTitle()) _titleScrollNeedsRefresh = false;
            return;
        }

        // Overflows — anchor to left edge of clip, then animate back and forth.
        Canvas.SetLeft(TitleText, 0);
        if (!Current() || !CanPresentTitle()) return;
        double travel = textWidth - clipWidth + 24; // padding so last char clears edge
        // Speed: ~140 px/sec
        double seconds = Math.Max(2.5, travel / 140.0);
        var anim = new DoubleAnimation(0, -travel, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        TitleScrollTransform.BeginAnimation(TranslateTransform.XProperty, anim);
        if (Current() && CanPresentTitle()) _titleScrollNeedsRefresh = false;
    }

    private void StopTitleScroll()
    {
        _titleScrollNeedsRefresh = true;
        TitleScrollTransform.BeginAnimation(TranslateTransform.XProperty, null);
        TitleScrollTransform.X = 0;
    }

    private void UpdateProgressSegments(double fraction)
    {
        if (_progressSegmentCache.Count == 0)
        {
            BuildProgressSegments();
        }
        int segmentCount = _progressSegmentCache.Count;
        int filled = (int)Math.Round(Math.Clamp(fraction, 0, 1) * segmentCount);
        for (int i = 0; i < segmentCount; i++)
        {
            var seg = _progressSegmentCache[i];
            bool on = i < filled;
            seg.Background = on ? _segOnBrush : _segOffBrush;
            seg.Opacity = on ? 1.0 : 0.55;
        }
    }

    private readonly List<Border> _progressSegmentCache = new();
    private static readonly SolidColorBrush _segOnBrush =
        new(((Color)System.Windows.Media.ColorConverter.ConvertFromString("#58A6FF")!));
    private static readonly SolidColorBrush _segOffBrush =
        new(((Color)System.Windows.Media.ColorConverter.ConvertFromString("#1F2937")!));

    private void BuildProgressSegments()
    {
        const int count = 20;
        ProgressSegments.Items.Clear();
        _progressSegmentCache.Clear();
        for (int i = 0; i < count; i++)
        {
            var seg = new Border
            {
                Height = 38,
                Margin = new Thickness(4, 0, 4, 0),
                Background = _segOffBrush,
                Opacity = 0.55,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 14,
                    ShadowDepth = 0,
                    Color = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#58A6FF")!,
                    Opacity = 0.45
                }
            };
            _progressSegmentCache.Add(seg);
            ProgressSegments.Items.Add(seg);
        }
    }

    /// <summary>
    /// The primary clock always uses the same MM:SS glyph size. Large total
    /// hours have their own fixed-size, labelled lane; configured hour-sized
    /// intervals keep that lane while counting below one hour. These are real
    /// visible components, not an abbreviated or hidden full-duration readout.
    /// </summary>
    private void SetActiveDurationReadout(TimeSpan remaining, TimeSpan configuredDuration)
    {
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        bool showHours = configuredDuration >= TimeSpan.FromHours(1) || remaining >= TimeSpan.FromHours(1);
        long hours = remaining.Ticks / TimeSpan.TicksPerHour;
        string hoursText = hours.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string minuteSeconds = $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";
        var hoursVisibility = showHours ? Visibility.Visible : Visibility.Collapsed;
        bool changed = TimerText.Text != minuteSeconds || TimerHoursText.Text != hoursText
            || TimerHoursLane.Visibility != hoursVisibility
            || string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(TimerReadout));
        TimerText.Text = minuteSeconds;
        TimerHoursText.Text = hoursText;
        TimerHoursLane.Visibility = hoursVisibility;
        TimerHoursSpacer.Visibility = hoursVisibility;
        if (changed)
            System.Windows.Automation.AutomationProperties.SetName(TimerReadout, showHours
                ? $"Time remaining: {hoursText} hours, {remaining.Minutes} minutes, {remaining.Seconds} seconds"
                : $"Time remaining: {remaining.Minutes} minutes, {remaining.Seconds} seconds");
    }

    private static string FormatTime(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        // MM:SS for under an hour, H:MM:SS otherwise
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}";
        return $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}";
    }

    // ─────────── Finish flow ───────────

    private string? _lockedFinishedTitle;
    private static readonly Color CompletionBaseColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#102A21")!;
    private static readonly Color NeutralBaseColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#080C12")!;

    private void OnIntervalFinished(TrainingTimer finished)
    {
        // Show() makes the output visible before Loaded is dispatched. It must
        // already consume a finish then; detached/hidden views leave progression
        // to the operator controller's existing off-screen fallback.
        if (!IsVisible || Window.GetWindow(this) is not { IsVisible: true }
            || _session is null || !ReferenceEquals(_session.Active, finished) || !finished.HasFinished) return;
        ResetVisuals();
        var session = _session;
        var expectedNext = session.UpNext;
        int advanceGeneration = _visualGeneration;

        // 1) Lock the visible scorebug on the just-finished timer.
        _lockedFinishedTitle = finished.Title.ToUpperInvariant();
        SetTitleText(_lockedFinishedTitle);
        SetActiveDurationReadout(TimeSpan.Zero, finished.Duration);
        UpdateProgressSegments(1);
        _flashing = true;

        // 2) Hide the up-next bar — its slot will be taken by the staged
        //    preview of the next interval.
        AnimateUpNext(0);

        // 3) Advance the model NOW — the next timer's stopwatch starts
        //    running immediately in the background, unless the operator has
        //    disabled auto-advance (in which case the next timer is staged
        //    paused and waits for a manual Start press).
        bool sessionFinishing = expectedNext is null;
        bool autoStart = !sessionFinishing && session.AutoAdvance;
        _advancingAfterFinish = true;
        try { session.AdvanceAfterFinish(autoStart: autoStart); }
        finally { _advancingAfterFinish = false; }

        // Advance notifies synchronously. A subscriber may rebind/close this
        // output or change the queue while that notification is on the stack.
        // Never let the obsolete continuation rearm work over its replacement.
        if (advanceGeneration != _visualGeneration || !ReferenceEquals(_session, session)) return;
        if (!IsVisible || Window.GetWindow(this) is not { IsVisible: true })
        {
            ResetVisuals();
            StopTitleScroll();
            return;
        }
        if (!ReferenceEquals(session.Active, expectedNext))
        {
            ResetVisuals();
            return;
        }
        _flashActiveId = session.Active?.Id;

        // 4) Acknowledge completion once without flashing the public display.
        // The next stopwatch already runs independently of this decoration.
        bool CurrentCueOwner() => advanceGeneration == _visualGeneration && ReferenceEquals(_session, session)
            && ReferenceEquals(session.Active, expectedNext) && IsVisible
            && Window.GetWindow(this) is { IsVisible: true };
        if (!StartCompletionCue(showMark: !sessionFinishing, isCurrent: CurrentCueOwner))
        {
            RetireCompletionVisualsIfOwned(advanceGeneration, session);
            return;
        }

        if (sessionFinishing)
        {
            // No next timer — show the session-complete banner for the full
            // 10-second completion window then fall back to ready state.
            ShowSessionComplete();
            return;
        }

        // 5) Reveal the staged "NOW STARTING" bar at the bottom showing the
        //    next timer with its live ticking clock. The UI tick will update
        //    StagedTimerText every frame while _flashing is true.
        ShowStagedNowStarting();

        // 6) After FinishHold, return the accent and animate the swap: the
        //    finished panel slides up & out, the staged bar grows up to
        //    centre as the new active timer.
        _holdTimer?.Stop();
        var hold = new DispatcherTimer { Interval = FinishHold };
        _holdTimer = hold;
        int generation = _visualGeneration;
        hold.Tick += (_, _) =>
        {
            hold.Stop();
            if (generation != _visualGeneration || !ReferenceEquals(_holdTimer, hold)) return;
            _holdTimer = null;
            EndFlashAndPromoteStaged();
        };
        _holdTimer.Start();
    }

    /// <summary>
    /// Shows the bottom "NOW STARTING" bar populated with the (already
    /// running) next timer. Its remaining time is refreshed by the UI tick.
    /// </summary>
    private void ShowStagedNowStarting()
    {
        if (_session?.Active is not { } next) return;

        StagedTitleText.Text = next.Title.ToUpperInvariant();
        StagedTimerText.Text = FormatTime(_session.ActiveRemaining);
        StagedTimerHost.Visibility = Visibility.Visible;
        StagedTimerHost.Opacity = 0;
        StagedScale.ScaleX = 1;
        StagedScale.ScaleY = 1;
        StagedTranslate.Y = 40; // start nudged down + faded so it slides in

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = EaseOut };
        var slide = new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(500)) { EasingFunction = EaseOut };
        StagedTimerHost.BeginAnimation(OpacityProperty, fade);
        StagedTranslate.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    /// <summary>
    /// End-of-flash transition: smooth in-place crossfade. The finished
    /// panel fades out and shrinks slightly; the staged "NOW STARTING" bar
    /// fades out where it is; the new active panel fades in pre-populated
    /// with the live next timer's values. No big translates across the
    /// canvas — feels like a clean cut, not a janky slide.
    /// </summary>
    private bool OwnsCompletionVisuals(int generation, TrainingSession? session, TrainingTimer? active) =>
        generation == _visualGeneration && ReferenceEquals(_session, session)
        && ReferenceEquals(session?.Active, active) && IsVisible
        && Window.GetWindow(this) is { IsVisible: true };

    private void RetireCompletionVisualsIfOwned(int generation, TrainingSession? session)
    {
        // A hidden host can still own this phase. Retire it without touching
        // a replacement which may have rebound during an observable write.
        if (generation != _visualGeneration || !ReferenceEquals(_session, session)) return;
        int resetGeneration = unchecked(_visualGeneration + 1);
        ResetVisuals();
        if (resetGeneration == _visualGeneration && ReferenceEquals(_session, session)) StopTitleScroll();
    }

    private void EndFlashAndPromoteStaged()
    {
        int generation = _visualGeneration;
        var session = _session;
        var active = session?.Active;
        bool IsCurrent() => OwnsCompletionVisuals(generation, session, active);
        if (!StopCompletionCue(IsCurrent)) { RetireCompletionVisualsIfOwned(generation, session); return; }

        const int crossMs = 450;

        // These setters may synchronously replace the output's owner. Every
        // later write and the delayed promotion still belong to this phase.
        ActiveTimerHost.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(crossMs)) { EasingFunction = EaseOut });
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
        TimerScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 0.95, TimeSpan.FromMilliseconds(crossMs)) { EasingFunction = EaseOut });
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
        TimerScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 0.95, TimeSpan.FromMilliseconds(crossMs)) { EasingFunction = EaseOut });
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }

        StagedTimerHost.BeginAnimation(OpacityProperty,
            new DoubleAnimation(StagedTimerHost.Opacity, 0, TimeSpan.FromMilliseconds(crossMs))
            { EasingFunction = EaseOut });
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }

        _promotionTimer?.Stop();
        var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(crossMs + 20) };
        _promotionTimer = settle;
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            if (!ReferenceEquals(_promotionTimer, settle)) return;
            if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
            _promotionTimer = null;
            FinalizeSwap();
        };
        settle.Start();
    }

    private void FinalizeSwap()
    {
        var session = _session;
        if (session is null) return;
        var active = session.Active;
        int generation = _visualGeneration;
        bool IsCurrent() => OwnsCompletionVisuals(generation, session, active);
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }

        // Settle the old presentation only while it still owns this output.
        ActiveTimerHost.BeginAnimation(OpacityProperty, null);
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
        TimerScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
        TimerScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
        TimerTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
        ActiveTimerHost.Opacity = 0;
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
        TimerScale.ScaleX = 1;
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
        TimerScale.ScaleY = 1;
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
        TimerTranslate.Y = 0;
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }

        StagedTimerHost.BeginAnimation(OpacityProperty, null);
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
        StagedScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
        StagedScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
        StagedTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
        StagedScale.ScaleX = 1;
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
        StagedScale.ScaleY = 1;
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
        StagedTranslate.Y = 0;
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
        StagedTimerHost.Visibility = Visibility.Collapsed;
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
        StagedTimerHost.Opacity = 1;
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }

        _flashing = false;
        _lockedFinishedTitle = null;
        UpdateLive();
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
        UpdateUpNext();
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
        ActiveTimerHost.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = EaseOut });
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
        AnimateTitlePop();
    }

    private bool StartCompletionCue(bool showMark, Func<bool> isCurrent)
    {
        // Setting a visual property may synchronously notify another owner.
        // Revalidate between writes so an interrupted cue cannot repaint it.
        if (!isCurrent()) return false;
        RootBg.BeginAnimation(SolidColorBrush.ColorProperty, null);
        if (!isCurrent()) return false;
        RootBg.Color = CompletionBaseColor;
        if (!isCurrent()) return false;
        GradientBg.BeginAnimation(OpacityProperty, null);
        if (!isCurrent()) return false;
        GradientBg.Opacity = .35;
        if (!isCurrent()) return false;

        CompletionMark.BeginAnimation(OpacityProperty, null);
        if (!isCurrent()) return false;
        CompletionMark.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null);
        if (!isCurrent()) return false;
        CompletionMark.Opacity = showMark ? 1 : 0;
        if (!isCurrent()) return false;
        CompletionMark.StrokeDashOffset = 0;
        if (!isCurrent()) return false;
        if (!showMark) return true; // The final phase has its own completion banner.
        CompletionMark.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty,
            new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(260))
            { EasingFunction = EaseOut, FillBehavior = FillBehavior.Stop });
        return isCurrent();
    }

    private bool StopCompletionCue(Func<bool> isCurrent)
    {
        if (!isCurrent()) return false;
        CompletionMark.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null);
        if (!isCurrent()) return false;
        CompletionMark.StrokeDashOffset = 0;
        if (!isCurrent()) return false;
        CompletionMark.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)) { EasingFunction = EaseOut });
        if (!isCurrent()) return false;
        // The one finite return belongs to the existing phase handoff.
        RootBg.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(NeutralBaseColor, TimeSpan.FromMilliseconds(260)) { EasingFunction = EaseOut });
        if (!isCurrent()) return false;
        GradientBg.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(260)) { EasingFunction = EaseOut });
        return isCurrent();
    }

    private void AnimateTitlePop()
    {
        var pop = new DoubleAnimationUsingKeyFrames();
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.08, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150)),
            new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }));
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(400)),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        TitleScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        TitleScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop.Clone());
    }

    private void ShowSessionComplete()
    {
        int generation = _visualGeneration;
        var session = _session;
        var active = session?.Active;
        bool IsCurrent() => OwnsCompletionVisuals(generation, session, active);
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
        SetTitleText("SESSION COMPLETE");
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
        SetActiveDurationReadout(TimeSpan.Zero, TimeSpan.Zero);
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }

        SessionCompleteOverlay.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)) { EasingFunction = EaseOut });
        if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }

        _sessionCompleteTimer?.Stop();
        var complete = new DispatcherTimer { Interval = SessionCompleteHold };
        _sessionCompleteTimer = complete;
        complete.Tick += (_, _) =>
        {
            complete.Stop();
            if (!ReferenceEquals(_sessionCompleteTimer, complete)) return;
            if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
            _sessionCompleteTimer = null;
            if (!StopCompletionCue(IsCurrent)) { RetireCompletionVisualsIfOwned(generation, session); return; }
            SessionCompleteOverlay.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(600)) { EasingFunction = EaseOut });
            if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
            _flashing = false;
            _lockedFinishedTitle = null;
            UpdateLive();
            if (!IsCurrent()) { RetireCompletionVisualsIfOwned(generation, session); return; }
            UpdateUpNext();
        };
        complete.Start();
    }

    /// <summary>
    /// Cancel any pending cue animations and hold timers (e.g. when the operator
    /// resets or exits the session).
    /// </summary>
    public void ResetVisuals()
    {
        int generation = ++_visualGeneration;
        _holdTimer?.Stop();
        _holdTimer = null;
        _sessionCompleteTimer?.Stop();
        _sessionCompleteTimer = null;
        _promotionTimer?.Stop();
        _promotionTimer = null;
        RootBg.BeginAnimation(SolidColorBrush.ColorProperty, null);
        if (generation != _visualGeneration) return;
        RootBg.Color = NeutralBaseColor;
        if (generation != _visualGeneration) return;
        GradientBg.BeginAnimation(OpacityProperty, null);
        if (generation != _visualGeneration) return;
        GradientBg.Opacity = 1;
        if (generation != _visualGeneration) return;
        CompletionMark.BeginAnimation(OpacityProperty, null);
        if (generation != _visualGeneration) return;
        CompletionMark.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null);
        if (generation != _visualGeneration) return;
        CompletionMark.Opacity = 0;
        if (generation != _visualGeneration) return;
        CompletionMark.StrokeDashOffset = 6;
        if (generation != _visualGeneration) return;
        SessionCompleteOverlay.BeginAnimation(OpacityProperty, null);
        if (generation != _visualGeneration) return;
        SessionCompleteOverlay.Opacity = 0;
        if (generation != _visualGeneration) return;
        StagedTimerHost.BeginAnimation(OpacityProperty, null);
        if (generation != _visualGeneration) return;
        StagedScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        if (generation != _visualGeneration) return;
        StagedScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        if (generation != _visualGeneration) return;
        StagedTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        if (generation != _visualGeneration) return;
        StagedTimerHost.Visibility = Visibility.Collapsed;
        if (generation != _visualGeneration) return;
        StagedTimerHost.Opacity = 1;
        if (generation != _visualGeneration) return;
        StagedScale.ScaleX = 1;
        if (generation != _visualGeneration) return;
        StagedScale.ScaleY = 1;
        if (generation != _visualGeneration) return;
        StagedTranslate.Y = 0;
        if (generation != _visualGeneration) return;
        ActiveTimerHost.BeginAnimation(OpacityProperty, null);
        if (generation != _visualGeneration) return;
        ActiveTimerHost.Opacity = 1;
        if (generation != _visualGeneration) return;
        TimerScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        if (generation != _visualGeneration) return;
        TimerScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        if (generation != _visualGeneration) return;
        TimerTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        if (generation != _visualGeneration) return;
        TitleScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        if (generation != _visualGeneration) return;
        TitleScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        if (generation != _visualGeneration) return;
        TitleScale.ScaleX = 1;
        if (generation != _visualGeneration) return;
        TitleScale.ScaleY = 1;
        if (generation != _visualGeneration) return;
        TimerScale.ScaleX = 1;
        if (generation != _visualGeneration) return;
        TimerScale.ScaleY = 1;
        if (generation != _visualGeneration) return;
        TimerTranslate.Y = 0;
        if (generation != _visualGeneration) return;
        _flashing = false;
        _lockedFinishedTitle = null;
        _flashActiveId = null;
        UpdateLive();
        if (generation != _visualGeneration) return;
        UpdateUpNext();
    }
}

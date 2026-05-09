using System;
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
/// the title above and an "UP NEXT" strip below; flashes red/black when an
/// interval finishes and runs a smooth slide/scale transition to the next
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

    /// <summary>Total length of the final session-complete strobe.</summary>
    private static readonly TimeSpan SessionCompleteFlash = TimeSpan.FromSeconds(10);

    /// <summary>Strobe period (red on / off) during finish flashes.</summary>
    private static readonly TimeSpan StrobePeriod = TimeSpan.FromMilliseconds(420);

    private DispatcherTimer? _strobeTimer;
    private DispatcherTimer? _holdTimer;
    private DispatcherTimer? _sessionCompleteTimer;
    private bool _flashing;

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
        Loaded += (_, _) => _uiTimer.Start();
        Unloaded += (_, _) => _uiTimer.Stop();
    }

    public void Bind(TrainingSession session)
    {
        if (_session is not null)
        {
            _session.Changed -= OnSessionChanged;
            _session.StructuralChanged -= OnStructuralChanged;
            _session.IntervalFinished -= OnIntervalFinished;
        }

        _session = session;
        _session.Changed += OnSessionChanged;
        _session.StructuralChanged += OnStructuralChanged;
        _session.IntervalFinished += OnIntervalFinished;

        OnStructuralChanged();
        OnSessionChanged();
    }

    /// <summary>Light-weight refresh: live values only, no expensive layout work.</summary>
    private void OnSessionChanged()
    {
        if (_flashing)
        {
            // While the finish flash is running, the visible scorebug stays
            // locked on the FINISHED panel. The only thing we live-update is
            // the "NOW STARTING" preview at the bottom — its clock ticks down
            // in real time as the next timer runs in the background.
            if (_session is not null && StagedTimerHost.Visibility == Visibility.Visible)
            {
                StagedTimerText.Text = FormatTime(_session.ActiveRemaining);
            }
            return;
        }
        UpdateLive();
    }

    /// <summary>Heavy refresh: only fires when the queue actually changes structure.</summary>
    private void OnStructuralChanged()
    {
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
            TitleText.Text = "NO TIMERS";
            TimerText.Text = "00:00";
            ProgressFillCol.Width = new GridLength(0, GridUnitType.Star);
            ProgressEmptyCol.Width = new GridLength(1, GridUnitType.Star);
            SetStatus("READY", "#58A6FF");
            return;
        }

        TitleText.Text = active.Title.ToUpperInvariant();

        TimeSpan rem = _session.ActiveRemaining;
        TimerText.Text = FormatTime(rem);

        // Progress fills FROM zero TO full as time elapses
        double frac = active.Duration.TotalSeconds <= 0
            ? 0
            : Math.Clamp(1 - (rem.TotalSeconds / active.Duration.TotalSeconds), 0, 1);
        ProgressFillCol.Width = new GridLength(Math.Max(0.0001, frac), GridUnitType.Star);
        ProgressEmptyCol.Width = new GridLength(Math.Max(0.0001, 1 - frac), GridUnitType.Star);

        // Status pill colour shifts blue -> amber -> red as the timer winds down
        if (active.HasFinished)
        {
            SetStatus("FINISHED", "#FF4444");
        }
        else if (!_session.IsRunning)
        {
            SetStatus("PAUSED", "#FFA657");
        }
        else if (rem.TotalSeconds <= 10)
        {
            SetStatus("LAST 10s", "#FF6B6B");
            // Pulse the timer glow when in the final 10 seconds
            PulseTimerGlow();
        }
        else
        {
            SetStatus("RUNNING", "#3FB950");
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

    private void UpdateUpNext()
    {
        if (_session is null) return;
        var next = _session.UpNext;
        if (next is null)
        {
            AnimateUpNext(0);
            return;
        }
        UpNextTitle.Text = next.Title;
        UpNextDuration.Text = FormatTime(next.Duration);
        AnimateUpNext(140);
    }

    private double _currentUpNextHeight;
    private void AnimateUpNext(double targetHeight)
    {
        if (Math.Abs(_currentUpNextHeight - targetHeight) < 0.5) return;
        _currentUpNextHeight = targetHeight;
        var anim = new DoubleAnimation(targetHeight, TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = EaseOut
        };
        UpNextBar.BeginAnimation(HeightProperty, anim);
    }

    private void SetStatus(string text, string colorHex)
    {
        StatusPillText.Text = text;
        if (System.Windows.Media.ColorConverter.ConvertFromString(colorHex) is Color c)
        {
            StatusPillBrush.Color = c;
            StatusPillBorder.Color = c;
        }
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
    private static readonly Color FlashOnColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#B30000")!;
    private static readonly Color FlashOffColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#080C12")!;

    private void OnIntervalFinished(TrainingTimer finished)
    {
        if (_session is null) return;

        // 1) Lock the visible scorebug on the just-finished timer.
        _lockedFinishedTitle = finished.Title.ToUpperInvariant();
        TitleText.Text = _lockedFinishedTitle;
        TimerText.Text = "00:00";
        ProgressFillCol.Width = new GridLength(1, GridUnitType.Star);
        ProgressEmptyCol.Width = new GridLength(0.0001, GridUnitType.Star);
        SetStatus("FINISHED", "#FF4444");
        _flashing = true;

        // 2) Hide the up-next bar — its slot will be taken by the staged
        //    preview of the next interval.
        AnimateUpNext(0);

        // 3) Advance the model NOW — the next timer's stopwatch starts
        //    running immediately in the background.
        bool sessionFinishing = _session.UpNext is null;
        _session.AdvanceAfterFinish(autoStart: !sessionFinishing);

        // 4) Start the strobe (background colour change, no overlay).
        StartStrobe();

        if (sessionFinishing)
        {
            // No next timer — show the session-complete banner for the full
            // 10-second strobe window then fall back to ready state.
            ShowSessionComplete();
            return;
        }

        // 5) Reveal the staged "NOW STARTING" bar at the bottom showing the
        //    next timer with its live ticking clock. The UI tick will update
        //    StagedTimerText every frame while _flashing is true.
        ShowStagedNowStarting();

        // 6) After FinishHold, end the strobe and animate the swap: the
        //    finished panel slides up & out, the staged bar grows up to
        //    centre as the new active timer.
        _holdTimer?.Stop();
        _holdTimer = new DispatcherTimer { Interval = FinishHold };
        _holdTimer.Tick += (_, _) =>
        {
            _holdTimer!.Stop();
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
    private void EndFlashAndPromoteStaged()
    {
        StopStrobe();

        const int crossMs = 450;

        // Finished panel: fade out + tiny scale-down so it doesn't feel jumpy.
        ActiveTimerHost.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(crossMs)) { EasingFunction = EaseOut });
        TimerScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 0.95, TimeSpan.FromMilliseconds(crossMs)) { EasingFunction = EaseOut });
        TimerScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 0.95, TimeSpan.FromMilliseconds(crossMs)) { EasingFunction = EaseOut });

        // Staged "NOW STARTING" bar: fade out where it sits — its job
        // (showing the next clock during the flash) is done.
        StagedTimerHost.BeginAnimation(OpacityProperty,
            new DoubleAnimation(StagedTimerHost.Opacity, 0, TimeSpan.FromMilliseconds(crossMs))
            { EasingFunction = EaseOut });

        // Hand off after the fade completes.
        var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(crossMs + 20) };
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            FinalizeSwap();
        };
        settle.Start();
    }

    private void FinalizeSwap()
    {
        if (_session is null) return;

        // Clear active host transforms; we're about to fade it back in
        // populated with the new timer's data.
        ActiveTimerHost.BeginAnimation(OpacityProperty, null);
        TimerScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        TimerScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        TimerTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        ActiveTimerHost.Opacity = 0; // start invisible — we just faded out
        TimerScale.ScaleX = 1;
        TimerScale.ScaleY = 1;
        TimerTranslate.Y = 0;

        StagedTimerHost.BeginAnimation(OpacityProperty, null);
        StagedScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        StagedScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        StagedTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        StagedScale.ScaleX = 1;
        StagedScale.ScaleY = 1;
        StagedTranslate.Y = 0;
        StagedTimerHost.Visibility = Visibility.Collapsed;
        StagedTimerHost.Opacity = 1;

        _flashing = false;
        _lockedFinishedTitle = null;

        // Populate the active panel with the new running timer's values, then
        // smoothly fade it in. The up-next bar grows in the same animation
        // window so the operator sees both arrive together.
        UpdateLive();
        UpdateUpNext();

        ActiveTimerHost.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = EaseOut });

        // Brief title-pop animation when the new timer takes over.
        AnimateTitlePop();
    }

    private void StartStrobe()
    {
        StopStrobe();
        bool on = false;
        _strobeTimer = new DispatcherTimer { Interval = StrobePeriod };
        _strobeTimer.Tick += (_, _) =>
        {
            on = !on;
            // Animate the ROOT background colour (no overlay layer). On the
            // "on" frame the gradient layer fades down so the red dominates;
            // on the "off" frame the gradient fades back in.
            var bgAnim = new ColorAnimation(on ? FlashOnColor : FlashOffColor, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = EaseInOut
            };
            RootBg.BeginAnimation(SolidColorBrush.ColorProperty, bgAnim);

            var gradAnim = new DoubleAnimation(on ? 0.0 : 1.0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = EaseInOut
            };
            GradientBg.BeginAnimation(OpacityProperty, gradAnim);
        };
        // Kick off immediately so the first frame strobes red.
        RootBg.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(FlashOnColor, TimeSpan.FromMilliseconds(120)));
        GradientBg.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120)));
        _strobeTimer.Start();
    }

    private void StopStrobe()
    {
        _strobeTimer?.Stop();
        _strobeTimer = null;
        // Restore the dark base colour and the gradient overlay smoothly.
        RootBg.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(FlashOffColor, TimeSpan.FromMilliseconds(260)) { EasingFunction = EaseOut });
        GradientBg.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(260)) { EasingFunction = EaseOut });
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
        TitleText.Text = "SESSION COMPLETE";
        TimerText.Text = "00:00";
        SetStatus("DONE", "#FF4444");

        SessionCompleteOverlay.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)) { EasingFunction = EaseOut });

        _sessionCompleteTimer?.Stop();
        _sessionCompleteTimer = new DispatcherTimer { Interval = SessionCompleteFlash };
        _sessionCompleteTimer.Tick += (_, _) =>
        {
            _sessionCompleteTimer!.Stop();
            _sessionCompleteTimer = null;
            StopStrobe();
            SessionCompleteOverlay.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(600)) { EasingFunction = EaseOut });
            _flashing = false;
            _lockedFinishedTitle = null;
            UpdateLive();
            UpdateUpNext();
        };
        _sessionCompleteTimer.Start();
    }

    /// <summary>
    /// Cancel any pending strobe / hold timers (e.g. when the operator
    /// resets or exits the session).
    /// </summary>
    public void ResetVisuals()
    {
        _strobeTimer?.Stop();
        _strobeTimer = null;
        _holdTimer?.Stop();
        _holdTimer = null;
        _sessionCompleteTimer?.Stop();
        _sessionCompleteTimer = null;
        RootBg.BeginAnimation(SolidColorBrush.ColorProperty, null);
        RootBg.Color = FlashOffColor;
        GradientBg.BeginAnimation(OpacityProperty, null);
        GradientBg.Opacity = 1;
        SessionCompleteOverlay.BeginAnimation(OpacityProperty, null);
        SessionCompleteOverlay.Opacity = 0;
        StagedTimerHost.BeginAnimation(OpacityProperty, null);
        StagedScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        StagedScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        StagedTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        StagedTimerHost.Visibility = Visibility.Collapsed;
        StagedTimerHost.Opacity = 1;
        StagedScale.ScaleX = 1;
        StagedScale.ScaleY = 1;
        StagedTranslate.Y = 0;
        ActiveTimerHost.BeginAnimation(OpacityProperty, null);
        ActiveTimerHost.Opacity = 1;
        TimerScale.ScaleX = 1;
        TimerScale.ScaleY = 1;
        TimerTranslate.Y = 0;
        _flashing = false;
        _lockedFinishedTitle = null;
    }
}

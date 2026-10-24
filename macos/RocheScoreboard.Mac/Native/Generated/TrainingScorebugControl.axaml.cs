using RocheScoreboard.Mac.Production;
// Generated from Views/TrainingScorebugControl.xaml.cs; edit translator/adapters.
// Source SHA-256: 39d2f25f73532db6ce2c2b8d5e97994342d853947667dda9374fdd6e9bcd1e7b
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Input;
using RocheScoreboard.Mac.Native;
using System;
using System.Collections.Generic;
using Roche_Scoreboard.Models;
using Color = Avalonia.Media.Color;
using UserControl = Avalonia.Controls.UserControl;

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
        NativePointer.EnsureInitialized(); InitializeComponent(); ProductionNames.CompleteLoad(this);
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _uiTimer.Tick += (_, _) =>
        {
            // Always tick the staged "NOW STARTING" preview if it's visible —
            // even when the main scorebug is locked during a flash.
            if (_flashing && _session is not null && StagedTimerHost.IsVisible == true)
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
            if (_session is not null && StagedTimerHost.IsVisible == true)
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
            SetTitleText("NO TIMERS");
            TimerText.Text = "00:00";
            UpdateProgressSegments(0);
            return;
        }

        SetTitleText(active.Title.ToUpperInvariant());

        TimeSpan rem = _session.ActiveRemaining;
        TimerText.Text = FormatTime(rem);

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
            UpNextBar.IsVisible = true;
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
                if (!_upNextVisible) UpNextBar.IsVisible = false;
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
    private void SetTitleText(string text)
    {
        if (string.Equals(_lastTitleText, text, StringComparison.Ordinal))
            return;
        _lastTitleText = text;
        TitleText.Text = text;
        UpdateTitleScroll();
    }

    /// <summary>
    /// Measures the title against the visible clip width. If it overflows,
    /// kick off a back-and-forth (reverse) translate animation; otherwise
    /// hold it centred (Canvas.Left mid-position).
    /// </summary>
    private void UpdateTitleScroll()
    {
        // Stop any previous scroll animation.
        TitleScrollTransform.BeginAnimation(TranslateTransform.XProperty, null);
        TitleScrollTransform.X = 0;

        // Force the TextBlock to measure now so DesiredSize is current.
        TitleText.Measure(new Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity));
        double textWidth = TitleText.DesiredSize.Width;
        double clipWidth = TitleClip.Width;

        if (textWidth <= clipWidth - 8)
        {
            // Fits — centre it within the clip canvas.
            Canvas.SetLeft(TitleText, (clipWidth - textWidth) / 2);
            return;
        }

        // Overflows — anchor to left edge of clip, then animate back and forth.
        Canvas.SetLeft(TitleText, 0);
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
        new(((Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString("#58A6FF")!));
    private static readonly SolidColorBrush _segOffBrush =
        new(((Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString("#1F2937")!));

    private void BuildProgressSegments()
    {
        const int count = 20;
        ProgressSegments.Items.Clear();
        _progressSegmentCache.Clear();
        for (int i = 0; i < count; i++)
        {
            var seg = new NativeBorder
            {
                Height = 38,
                Margin = new Thickness(4, 0, 4, 0),
                Background = _segOffBrush,
                Opacity = 0.55,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 14,
                    OffsetX = 0, OffsetY = 0,
                    Color = (Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString("#58A6FF")!,
                    Opacity = 0.45
                }
            };
            _progressSegmentCache.Add(seg);
            ProgressSegments.Items.Add(seg);
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
    private static readonly Color FlashOnColor = (Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString("#B30000")!;
    private static readonly Color FlashOffColor = (Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString("#080C12")!;

    private void OnIntervalFinished(TrainingTimer finished)
    {
        if (_session is null) return;

        // 1) Lock the visible scorebug on the just-finished timer.
        _lockedFinishedTitle = finished.Title.ToUpperInvariant();
        SetTitleText(_lockedFinishedTitle);
        TimerText.Text = "00:00";
        UpdateProgressSegments(1);
        _flashing = true;

        // 2) Hide the up-next bar — its slot will be taken by the staged
        //    preview of the next interval.
        AnimateUpNext(0);

        // 3) Advance the model NOW — the next timer's stopwatch starts
        //    running immediately in the background, unless the operator has
        //    disabled auto-advance (in which case the next timer is staged
        //    paused and waits for a manual Start press).
        bool sessionFinishing = _session.UpNext is null;
        bool autoStart = !sessionFinishing && _session.AutoAdvance;
        _session.AdvanceAfterFinish(autoStart: autoStart);

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
        StagedTimerHost.IsVisible = true;
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
        StagedTimerHost.IsVisible = false;
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
        SetTitleText("SESSION COMPLETE");
        TimerText.Text = "00:00";

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
        StagedTimerHost.IsVisible = false;
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

public partial class TrainingScorebugControl
{
    private SolidColorBrush RootBg => (SolidColorBrush)ProductionNames.RegistryFor(this)["RootBg"];
    private ScaleTransform TitleScale => (ScaleTransform)ProductionNames.RegistryFor(this)["TitleScale"];
    private TranslateTransform TitleTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["TitleTranslate"];
    private DropShadowEffect TitleGlow => (DropShadowEffect)ProductionNames.RegistryFor(this)["TitleGlow"];
    private TranslateTransform TitleScrollTransform => (TranslateTransform)ProductionNames.RegistryFor(this)["TitleScrollTransform"];
    private ScaleTransform TimerScale => (ScaleTransform)ProductionNames.RegistryFor(this)["TimerScale"];
    private TranslateTransform TimerTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["TimerTranslate"];
    private DropShadowEffect TimerGlow => (DropShadowEffect)ProductionNames.RegistryFor(this)["TimerGlow"];
    private ScaleTransform StagedScale => (ScaleTransform)ProductionNames.RegistryFor(this)["StagedScale"];
    private TranslateTransform StagedTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["StagedTranslate"];
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

namespace Roche_Scoreboard.Views
{
    /// <summary>
    /// A lively, animated pre-match loading screen that runs while
    /// match resources spin up. Designed to be hosted as a full-window
    /// overlay (Panel.ZIndex high) and torn down once loading finishes.
    /// </summary>
    public partial class MatchLoadingScreen : UserControl
    {
        private readonly List<StepRow> _steps = new();
        private double _displayPercent;
        private double _targetPercent;
        private bool _completed;
        private bool _finishedRaised;
        private bool _stopped;
        private readonly Random _rng = new();
        private System.Windows.Threading.DispatcherTimer? _progressTimer;
        private System.Windows.Threading.DispatcherTimer? _watchdogTimer;

        public MatchLoadingScreen()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            SizeChanged += (_, _) => RebuildBackdrop();
        }

        /// <summary>
        /// Fired when the orchestrated loading sequence finishes.
        /// </summary>
        public event Action? LoadingFinished;

        // ----- Public API -----

        public void ConfigureTeams(string homeName, string homeAbbr, string? homeLogo, string? homePrimaryColor,
                                   string awayName, string awayAbbr, string? awayLogo, string? awayPrimaryColor)
        {
            HomeNameText.Text = string.IsNullOrWhiteSpace(homeName) ? "HOME" : homeName.ToUpperInvariant();
            AwayNameText.Text = string.IsNullOrWhiteSpace(awayName) ? "AWAY" : awayName.ToUpperInvariant();

            HomeLogoFallback.Text = string.IsNullOrWhiteSpace(homeAbbr) ? "HOM" : homeAbbr.ToUpperInvariant();
            AwayLogoFallback.Text = string.IsNullOrWhiteSpace(awayAbbr) ? "AWA" : awayAbbr.ToUpperInvariant();

            ApplyTeamColor(homePrimaryColor, HomeRing, HomeLogoSlot, HomeRingGlow, Color.FromRgb(0x1F, 0x6F, 0xEB));
            ApplyTeamColor(awayPrimaryColor, AwayRing, AwayLogoSlot, AwayRingGlow, Color.FromRgb(0xFF, 0x7B, 0x72));

            TrySetLogo(HomeLogoImage, HomeLogoFallback, homeLogo);
            TrySetLogo(AwayLogoImage, AwayLogoFallback, awayLogo);
        }

        public void SetBrandSubtitle(string text) => BrandSubtitle.Text = text ?? string.Empty;

        /// <summary>
        /// Begins the loading sequence. <paramref name="stepLabels"/> are the
        /// human-readable stages displayed in the ticker. The screen drives a
        /// fake-progress timeline that finishes after <paramref name="totalDuration"/>.
        /// Call <see cref="Finish"/> early if real work completes sooner.
        /// </summary>
        public void StartLoading(IReadOnlyList<string> stepLabels, TimeSpan totalDuration)
        {
            // Reset state so the panel is safe to reuse across matches.
            _completed = false;
            _finishedRaised = false;
            _stopped = false;
            _displayPercent = 0;
            _targetPercent = 0;
            PercentText.Text = "0%";
            StatusText.Text = "INITIALISING";
            ProgressFill.BeginAnimation(WidthProperty, null);
            ProgressFill.Width = 0;

            BuildSteps(stepLabels);
            BeginProgressTimeline(totalDuration);

            // Watchdog: no matter what happens, force completion after a
            // generous timeout so the loader can never deadlock the app.
            _watchdogTimer?.Stop();
            var watchdogMs = Math.Max(4000, totalDuration.TotalMilliseconds * 2 + 1500);
            _watchdogTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(watchdogMs)
            };
            _watchdogTimer.Tick += (_, _) =>
            {
                _watchdogTimer?.Stop();
                if (!_finishedRaised)
                {
                    System.Diagnostics.Debug.WriteLine("[MatchLoading] watchdog forced completion");
                    ForceComplete();
                }
            };
            _watchdogTimer.Start();
        }

        /// <summary>
        /// Snaps progress to completion. Idempotent. Safe to call mid-flight
        /// or after the sequence has already finished.
        /// </summary>
        public void Finish() => ForceComplete();

        private void ForceComplete()
        {
            if (_finishedRaised) return;

            _completed = true;
            _progressTimer?.Stop();
            _progressTimer = null;

            foreach (var step in _steps) step.MarkComplete();
            StatusText.Text = "READY";
            PercentText.Text = "100%";
            AnimateProgressTo(100, TimeSpan.FromMilliseconds(180));

            RaiseFinishedSoon(TimeSpan.FromMilliseconds(200));
        }

        private void RaiseFinishedSoon(TimeSpan delay)
        {
            if (_finishedRaised) return;

            // Use a one-shot DispatcherTimer for the visual beat; if it never
            // ticks for some reason the watchdog still guarantees the fire.
            var t = new System.Windows.Threading.DispatcherTimer
            {
                Interval = delay <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : delay
            };
            t.Tick += (_, _) =>
            {
                t.Stop();
                RaiseFinishedNow();
            };
            t.Start();
        }

        private void RaiseFinishedNow()
        {
            if (_finishedRaised) return;
            _finishedRaised = true;
            _watchdogTimer?.Stop();
            _watchdogTimer = null;
            try { LoadingFinished?.Invoke(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MatchLoading] LoadingFinished handler error: {ex.Message}");
            }
        }

        // ----- Lifecycle -----

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RebuildBackdrop();
            StartAmbientAnimations();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _stopped = true;
            _progressTimer?.Stop();
            _progressTimer = null;
            _watchdogTimer?.Stop();
            _watchdogTimer = null;
            GridCanvas.Children.Clear();
            BeamCanvas.Children.Clear();
            ParticleCanvas.Children.Clear();
        }

        // ----- Steps ticker -----

        private void BuildSteps(IReadOnlyList<string> stepLabels)
        {
            StepStack.Children.Clear();
            _steps.Clear();
            foreach (var label in stepLabels)
            {
                var row = new StepRow(label);
                _steps.Add(row);
                StepStack.Children.Add(row.Root);
            }
        }

        // ----- Progress orchestration -----

        private void BeginProgressTimeline(TimeSpan totalDuration)
        {
            _progressTimer?.Stop();

            if (_steps.Count == 0)
            {
                AnimateProgressTo(100, totalDuration);
                RaiseFinishedSoon(totalDuration);
                return;
            }

            double per = 100.0 / _steps.Count;
            double tickMs = Math.Max(40, totalDuration.TotalMilliseconds / 100.0);

            int currentStep = 0;
            double elapsedMs = 0;
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(tickMs)
            };
            _progressTimer = timer;
            _steps[0].MarkActive();
            StatusText.Text = _steps[0].Label.ToUpperInvariant();
            timer.Tick += (_, _) =>
            {
                if (_stopped || _completed) { timer.Stop(); return; }
                elapsedMs += tickMs;
                double progress = Math.Min(100, elapsedMs / totalDuration.TotalMilliseconds * 100);

                int idx = Math.Min(_steps.Count - 1, (int)(progress / per));
                if (idx != currentStep)
                {
                    _steps[currentStep].MarkComplete();
                    currentStep = idx;
                    _steps[currentStep].MarkActive();
                    StatusText.Text = _steps[currentStep].Label.ToUpperInvariant();
                }

                _targetPercent = progress;
                AnimateProgressTo(progress, TimeSpan.FromMilliseconds(tickMs * 1.4));

                if (progress >= 100)
                {
                    timer.Stop();
                    if (_progressTimer == timer) _progressTimer = null;
                    ForceComplete();
                }
            };
            timer.Start();
        }

        private void AnimateProgressTo(double percent, TimeSpan duration)
        {
            _displayPercent = percent;
            PercentText.Text = ((int)Math.Round(percent)).ToString(CultureInfo.InvariantCulture) + "%";

            double trackWidth = 540 - 2; // account for border
            double targetWidth = Math.Max(0, Math.Min(trackWidth, trackWidth * percent / 100.0));
            var anim = new DoubleAnimation(targetWidth, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ProgressFill.BeginAnimation(WidthProperty, anim);
        }

        // ----- Ambient animations -----

        private void StartAmbientAnimations()
        {
            // Orbit rotations (continuous).
            var outerOrbit = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(4.5))
            { RepeatBehavior = RepeatBehavior.Forever };
            OuterOrbitRotate.BeginAnimation(RotateTransform.AngleProperty, outerOrbit);

            var innerOrbit = new DoubleAnimation(360, 0, TimeSpan.FromSeconds(3.2))
            { RepeatBehavior = RepeatBehavior.Forever };
            InnerOrbitRotate.BeginAnimation(RotateTransform.AngleProperty, innerOrbit);

            // VS chip pulse.
            var vsPulse = new DoubleAnimation(1.0, 1.12, TimeSpan.FromSeconds(0.55))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            VsChipScale.BeginAnimation(ScaleTransform.ScaleXProperty, vsPulse);
            VsChipScale.BeginAnimation(ScaleTransform.ScaleYProperty, vsPulse);

            // Brand dot pulse.
            var brandPulse = new DoubleAnimation(0.4, 1.0, TimeSpan.FromSeconds(0.9))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            BrandPulse.BeginAnimation(OpacityProperty, brandPulse);

            // Ring glow breathe.
            AnimateRingGlow(HomeRingGlow);
            AnimateRingGlow(AwayRingGlow);

            // Progress shine sweep loop.
            var shine = new DoubleAnimation(-80, 540, TimeSpan.FromSeconds(1.8))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            ShineTranslate.BeginAnimation(TranslateTransform.XProperty, shine);

            SpawnParticles();
            SpawnBeams();
        }

        private static void AnimateRingGlow(DropShadowEffect glow)
        {
            var anim = new DoubleAnimation(0.55, 1.0, TimeSpan.FromSeconds(1.4))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            glow.BeginAnimation(DropShadowEffect.OpacityProperty, anim);
        }

        private void RebuildBackdrop()
        {
            GridCanvas.Children.Clear();
            BeamCanvas.Children.Clear();

            double w = Math.Max(ActualWidth, 1);
            double h = Math.Max(ActualHeight, 1);

            // Diagonal grid lines.
            var stroke = new SolidColorBrush(Color.FromArgb(0x80, 0x1F, 0x6F, 0xEB));
            for (double x = -h; x < w + h; x += 64)
            {
                var line = new Line
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x + h,
                    Y2 = h,
                    Stroke = stroke,
                    StrokeThickness = 1
                };
                GridCanvas.Children.Add(line);
            }
            for (double x = -h; x < w + h; x += 64)
            {
                var line = new Line
                {
                    X1 = x,
                    Y1 = h,
                    X2 = x + h,
                    Y2 = 0,
                    Stroke = stroke,
                    StrokeThickness = 1,
                    Opacity = 0.5
                };
                GridCanvas.Children.Add(line);
            }
        }

        private void SpawnBeams()
        {
            double w = Math.Max(ActualWidth, 1280);
            double h = Math.Max(ActualHeight, 720);

            for (int i = 0; i < 6; i++)
            {
                var beam = new Border
                {
                    Width = 2,
                    Height = h * 1.6,
                    Background = new LinearGradientBrush(
                        Color.FromArgb(0x00, 0x58, 0xA6, 0xFF),
                        Color.FromArgb(0xAA, 0x58, 0xA6, 0xFF),
                        new Point(0, 0), new Point(0, 1)),
                    Opacity = 0.5
                };
                Canvas.SetLeft(beam, -50);
                Canvas.SetTop(beam, -h * 0.3);
                beam.RenderTransform = new RotateTransform(20);
                BeamCanvas.Children.Add(beam);

                double startX = -100 + i * (w / 4.0);
                double endX = startX + w + 200;
                var anim = new DoubleAnimation(startX, endX, TimeSpan.FromSeconds(4.5 + _rng.NextDouble() * 3))
                {
                    BeginTime = TimeSpan.FromSeconds(i * 0.4),
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                beam.BeginAnimation(Canvas.LeftProperty, anim);
            }
        }

        private void SpawnParticles()
        {
            ParticleCanvas.Children.Clear();
            double w = Math.Max(ActualWidth, 1280);
            double h = Math.Max(ActualHeight, 720);

            var palette = new[]
            {
                Color.FromRgb(0x58, 0xA6, 0xFF),
                Color.FromRgb(0xFF, 0xD3, 0x3D),
                Color.FromRgb(0x3F, 0xB9, 0x50),
                Color.FromRgb(0xFF, 0x7B, 0x72),
                Color.FromRgb(0x1F, 0x6F, 0xEB)
            };

            for (int i = 0; i < 36; i++)
            {
                double size = 2 + _rng.NextDouble() * 4;
                var color = palette[_rng.Next(palette.Length)];
                var dot = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = new SolidColorBrush(color),
                    Opacity = 0.35 + _rng.NextDouble() * 0.5,
                    Effect = new DropShadowEffect { Color = color, BlurRadius = 10, ShadowDepth = 0 }
                };
                double startX = _rng.NextDouble() * w;
                double startY = _rng.NextDouble() * h;
                Canvas.SetLeft(dot, startX);
                Canvas.SetTop(dot, startY);
                ParticleCanvas.Children.Add(dot);

                double driftY = -120 - _rng.NextDouble() * 200;
                var moveY = new DoubleAnimation(startY, startY + driftY, TimeSpan.FromSeconds(4 + _rng.NextDouble() * 5))
                {
                    BeginTime = TimeSpan.FromMilliseconds(_rng.Next(2500)),
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                dot.BeginAnimation(Canvas.TopProperty, moveY);

                var fade = new DoubleAnimation(0.15, 0.9, TimeSpan.FromSeconds(1.6 + _rng.NextDouble() * 2))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                dot.BeginAnimation(OpacityProperty, fade);
            }
        }

        // ----- Helpers -----

        private static void TrySetLogo(Image target, TextBlock fallback, string? path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(path, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();
                    target.Source = bmp;
                    fallback.Visibility = Visibility.Collapsed;
                    target.Visibility = Visibility.Visible;
                    return;
                }
            }
            catch
            {
                // fall through to fallback
            }
            target.Source = null;
            target.Visibility = Visibility.Collapsed;
            fallback.Visibility = Visibility.Visible;
        }

        private static void ApplyTeamColor(string? hex, Ellipse ring, Border slot, DropShadowEffect glow, Color fallback)
        {
            Color color = fallback;
            if (!string.IsNullOrWhiteSpace(hex))
            {
                try
                {
                    var obj = ColorConverter.ConvertFromString(hex);
                    if (obj is Color c) color = c;
                }
                catch
                {
                    // keep fallback
                }
            }
            ring.Stroke = new SolidColorBrush(color);
            slot.BorderBrush = new SolidColorBrush(color);
            glow.Color = color;
        }

        // ----- Step row -----

        private sealed class StepRow
        {
            public string Label { get; }
            public Grid Root { get; }
            private readonly Ellipse _dot;
            private readonly TextBlock _text;

            public StepRow(string label)
            {
                Label = label;
                Root = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(Color.FromRgb(0x30, 0x36, 0x3D)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                Grid.SetColumn(_dot, 0);
                _text = new TextBlock
                {
                    Text = label,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E))
                };
                Grid.SetColumn(_text, 1);
                Root.Children.Add(_dot);
                Root.Children.Add(_text);
            }

            public void MarkActive()
            {
                _dot.Fill = new SolidColorBrush(Color.FromRgb(0x58, 0xA6, 0xFF));
                _dot.Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0x58, 0xA6, 0xFF),
                    BlurRadius = 14,
                    ShadowDepth = 0
                };
                _text.Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xED, 0xF3));
                var pulse = new DoubleAnimation(0.4, 1.0, TimeSpan.FromSeconds(0.6))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                _dot.BeginAnimation(UIElement.OpacityProperty, pulse);
            }

            public void MarkComplete()
            {
                _dot.BeginAnimation(UIElement.OpacityProperty, null);
                _dot.Opacity = 1;
                _dot.Fill = new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50));
                _dot.Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0x3F, 0xB9, 0x50),
                    BlurRadius = 10,
                    ShadowDepth = 0
                };
                _text.Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xD1, 0xD9));
            }
        }
    }
}

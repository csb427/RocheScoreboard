using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Color = System.Windows.Media.Color;

namespace Roche_Scoreboard.Views
{
    public partial class ScorebugControl : System.Windows.Controls.UserControl
    {
        private Color _homeColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#0A2A6A");
        private Color _awayColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#7A1A1A");

        private const double MarqueePixelsPerSecond = 100;
        private readonly DispatcherTimer _wallClockTimer;

        private bool _homeAnimating;
        private bool _awayAnimating;
        private Storyboard? _homeScoreSb;
        private Storyboard? _awayScoreSb;
        private readonly List<DispatcherTimer> _homeTimers = new();
        private readonly List<DispatcherTimer> _awayTimers = new();
        private ScorebugLayout _currentLayout = ScorebugLayout.Classic;
        private const double ExpandedOverlayHeight = 130;

        // Goal/Behind column expand/collapse state
        private string _homeFullName = "HOME TEAM";
        private string _homeAbbr = "HOM";
        private string _awayFullName = "AWAY TEAM";
        private string _awayAbbr = "AWA";
        private bool _gbColumnsExpanded;
        private DispatcherTimer? _gbCollapseTimer;
        private DispatcherTimer? _gbExpandDelayTimer;
        private const double GBColumnWidth = 80;
        private static readonly TimeSpan GBExpandDuration = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan GBCollapseDuration = TimeSpan.FromMilliseconds(550);
        private static readonly TimeSpan GBCollapseDelay = TimeSpan.FromSeconds(15);

        // Auto-show G/B columns after an idle period with no scoring
        private DateTime _lastScoreEventTime = DateTime.MinValue;
        private bool _gbAutoShowTriggered;
        private const double GBAutoShowIdleMinutes = 2.0;

        public ScorebugLayout CurrentLayout => _currentLayout;

        public ScorebugControl()
        {
            InitializeComponent();
            ApplyGradient();

            var now = DateTime.Now.ToString("h:mm tt");
            TopTimeText.Text = now;
            ExpTopTimeText.Text = now;

            _wallClockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _wallClockTimer.Tick += (_, __) =>
            {
                var t = DateTime.Now.ToString("h:mm tt");
                TopTimeText.Text = t;
                ExpTopTimeText.Text = t;
            };
            _wallClockTimer.Start();

            MarqueeCanvas.SizeChanged += (_, __) => RestartMarqueeIfNeeded();
            ExpMarqueeCanvas.SizeChanged += (_, __) => RestartMarqueeIfNeeded();
            Loaded += (_, __) => StartActiveMarquee();
        }

        // ---- Layout switching ----

        public void SetLayout(ScorebugLayout layout)
        {
            if (_currentLayout == layout) return;
            ClearOverlayQueue();

            _currentLayout = layout;
            ClassicLayout.Visibility = layout == ScorebugLayout.Classic ? Visibility.Visible : Visibility.Collapsed;
            ExpandedLayout.Visibility = layout == ScorebugLayout.Expanded ? Visibility.Visible : Visibility.Collapsed;

            ApplyGradient();
            RestartMarqueeIfNeeded();
        }

        private bool IsExpanded => _currentLayout == ScorebugLayout.Expanded;

        // ---- Data setters (update BOTH layouts so switching mid-game is seamless) ----

        public void SetTeams(string homeName, string homeAbbr, string awayName, string awayAbbr)
        {
            _homeFullName = (string.IsNullOrWhiteSpace(homeName) ? "Home Team" : homeName).ToUpperInvariant();
            _awayFullName = (string.IsNullOrWhiteSpace(awayName) ? "Away Team" : awayName).ToUpperInvariant();
            _homeAbbr = (string.IsNullOrWhiteSpace(homeAbbr) ? "HOM" : homeAbbr).ToUpperInvariant();
            _awayAbbr = (string.IsNullOrWhiteSpace(awayAbbr) ? "AWA" : awayAbbr).ToUpperInvariant();

            // Classic layout: always show full name, scaling handles fit
            HomeNameText.Text = _homeFullName;
            AwayNameText.Text = _awayFullName;
            HomeLogoFallbackText.Text = _homeAbbr; AwayLogoFallbackText.Text = _awayAbbr;

            // Expanded layout: full name with wrapping enabled in XAML
            ExpHomeNameText.Text = _homeFullName; ExpAwayNameText.Text = _awayFullName;
            ExpHomeLogoFallback.Text = _homeAbbr; ExpAwayLogoFallback.Text = _awayAbbr;

            SyncClassicNameScales();
        }

        /// <summary>
        /// Measures both classic team name TextBlocks using the full team names
        /// and applies a single uniform scale so both rows render at the same visual height.
        /// Always measures full names so abbreviations use the same scale factor.
        /// </summary>
        private void SyncClassicNameScales(bool animate = false, TimeSpan? duration = null, IEasingFunction? easing = null)
        {
            string homeText = _homeFullName;
            string awayText = _awayFullName;
            if (string.IsNullOrEmpty(homeText) || string.IsNullOrEmpty(awayText)) return;

            double pixelsPerDip = 1.0;
            try
            {
                var source = PresentationSource.FromVisual(HomeNameText);
                if (source?.CompositionTarget != null)
                    pixelsPerDip = source.CompositionTarget.TransformToDevice.M11;
            }
            catch { /* pre-load fallback */ }

            var typeface = new Typeface(HomeNameText.FontFamily, HomeNameText.FontStyle, HomeNameText.FontWeight, HomeNameText.FontStretch);
            double fontSize = HomeNameText.FontSize; // 200

            var homeFormatted = new FormattedText(homeText, System.Globalization.CultureInfo.CurrentCulture,
                (System.Windows.FlowDirection)0, typeface, fontSize, System.Windows.Media.Brushes.White, pixelsPerDip);
            var awayFormatted = new FormattedText(awayText, System.Globalization.CultureInfo.CurrentCulture,
                (System.Windows.FlowDirection)0, typeface, fontSize, System.Windows.Media.Brushes.White, pixelsPerDip);

            // Available space: row height ~150px, column width depends on G/B state
            double rowHeight = HomeRowGrid.ActualHeight;
            if (rowHeight <= 0) rowHeight = 146; // fallback: (420 - 20 - 100) / 2 minus margins

            // Column 1 available width = total - logo - goals - behinds - total_score - margins
            double gbWidth = _gbColumnsExpanded ? GBColumnWidth * 2 : 0;
            double totalColWidth = HomeTotalBorder.ActualWidth;
            if (totalColWidth <= 0) totalColWidth = 160;
            double availableWidth = 707 - rowHeight - gbWidth - totalColWidth - 20; // ~logo, ~margins

            double homeNatW = homeFormatted.Width;
            double awayNatW = awayFormatted.Width;
            double natH = homeFormatted.Height; // same for both (same font/size)

            // Scale each to fit both width and height, take min
            double homeScaleW = availableWidth / homeNatW;
            double homeScaleH = rowHeight / natH;
            double homeScale = Math.Min(homeScaleW, homeScaleH);

            double awayScaleW = availableWidth / awayNatW;
            double awayScaleH = rowHeight / natH;
            double awayScale = Math.Min(awayScaleW, awayScaleH);

            // Use the smaller scale for both so they match
            double uniformScale = Math.Min(homeScale, awayScale);
            uniformScale = Math.Min(uniformScale, 1.0); // never exceed natural size

            if (animate)
            {
                AnimateNameScale(HomeNameText, uniformScale, duration, easing);
                AnimateNameScale(AwayNameText, uniformScale, duration, easing);
            }
            else
            {
                HomeNameText.LayoutTransform = new ScaleTransform(uniformScale, uniformScale);
                AwayNameText.LayoutTransform = new ScaleTransform(uniformScale, uniformScale);
            }
        }

        /// <summary>
        /// Smoothly animates a TextBlock's LayoutTransform ScaleTransform to a new uniform scale.
        /// </summary>
        private static void AnimateNameScale(TextBlock tb, double targetScale, TimeSpan? duration = null, IEasingFunction? easing = null)
        {
            // Ensure we have a ScaleTransform to animate
            if (tb.LayoutTransform is not ScaleTransform st)
            {
                st = new ScaleTransform(targetScale, targetScale);
                tb.LayoutTransform = st;
                return;
            }

            easing ??= new CubicEase { EasingMode = EasingMode.EaseInOut };
            var dur = duration ?? GBExpandDuration;

            var animX = new DoubleAnimation(targetScale, dur) { EasingFunction = easing };
            var animY = new DoubleAnimation(targetScale, dur) { EasingFunction = easing };

            st.BeginAnimation(ScaleTransform.ScaleXProperty, animX);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, animY);
        }

        public void SetScores(int homeGoals, int homeBehinds, int awayGoals, int awayBehinds)
        {
            if (!_homeAnimating)
            {
                int ht = homeGoals * 6 + homeBehinds;
                HomeTotalText.Text = ht.ToString(); HomeGoalsText.Text = homeGoals.ToString(); HomeBehindsText.Text = homeBehinds.ToString();
                ExpHomeTotalText.Text = ht.ToString(); ExpHomeGoalsText.Text = homeGoals.ToString(); ExpHomeBehindsText.Text = homeBehinds.ToString();
            }
            if (!_awayAnimating)
            {
                int at = awayGoals * 6 + awayBehinds;
                AwayTotalText.Text = at.ToString(); AwayGoalsText.Text = awayGoals.ToString(); AwayBehindsText.Text = awayBehinds.ToString();
                ExpAwayTotalText.Text = at.ToString(); ExpAwayGoalsText.Text = awayGoals.ToString(); ExpAwayBehindsText.Text = awayBehinds.ToString();
            }
        }

        public void LockScoreUpdate(bool isHome) { if (isHome) _homeAnimating = true; else _awayAnimating = true; }

        private bool _quarterAnimating;

        public void SetQuarter(int quarter)
        {
            if (_quarterAnimating) return;
            quarter = Math.Clamp(quarter, 1, 4);
            QuarterNumberText.Text = $"Q{quarter}";
            ExpQuarterNumberText.Text = $"Q{quarter}";
        }

        /// <summary>
        /// Vertical flip transition: the quarter label flips down to reveal the break abbreviation.
        /// </summary>
        public void AnimateQuarterToBreakLabel(int endedQuarter)
        {
            string breakLabel = endedQuarter switch
            {
                1 => "QT",
                2 => "HT",
                3 => "3QT",
                _ => "FT"
            };

            // Set the text to the pre-break quarter so the flip starts from the right label
            string fromLabel = $"Q{endedQuarter}";
            QuarterNumberText.Text = fromLabel;
            ExpQuarterNumberText.Text = fromLabel;

            _quarterAnimating = true;

            int completed = 0;
            void OnFlipDone()
            {
                if (System.Threading.Interlocked.Increment(ref completed) >= 2)
                    _quarterAnimating = false;
            }

            FlipQuarterText(QuarterNumberText, breakLabel, OnFlipDone);
            FlipQuarterText(ExpQuarterNumberText, breakLabel, OnFlipDone);
        }

        private static void FlipQuarterText(TextBlock textBlock, string newText, Action onDone)
        {
            TimeSpan halfDuration = TimeSpan.FromMilliseconds(250);
            CubicEase easeIn = new() { EasingMode = EasingMode.EaseIn };
            CubicEase easeOut = new() { EasingMode = EasingMode.EaseOut };

            // Ensure a ScaleTransform is available for the vertical flip
            if (textBlock.RenderTransform is not ScaleTransform)
            {
                textBlock.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
                textBlock.RenderTransform = new ScaleTransform(1, 1);
            }

            ScaleTransform scale = (ScaleTransform)textBlock.RenderTransform;

            // Phase 1: collapse vertically (flip down)
            DoubleAnimation flipOut = new(1, 0, halfDuration) { EasingFunction = easeIn };

            flipOut.Completed += (_, _) =>
            {
                textBlock.Text = newText;

                // Phase 2: expand vertically (flip up with new text)
                DoubleAnimation flipIn = new(0, 1, halfDuration) { EasingFunction = easeOut };
                flipIn.Completed += (_, _) => onDone();

                scale.BeginAnimation(ScaleTransform.ScaleYProperty, flipIn);
            };

            scale.BeginAnimation(ScaleTransform.ScaleYProperty, flipOut);
        }

        public void SetClock(string mmss)
        {
            string v = string.IsNullOrWhiteSpace(mmss) ? "00:00" : mmss;
            ClockText.Text = v;
            ExpClockText.Text = v;
        }

        private Color _homeSecondaryColor = Colors.White;
        private Color _awaySecondaryColor = Colors.White;
        private double _homeLogoZoom = 1.0;
        private double _homeLogoOffsetX;
        private double _homeLogoOffsetY;
        private double _awayLogoZoom = 1.0;
        private double _awayLogoOffsetX;
        private double _awayLogoOffsetY;

        public void SetTeamColors(string homeHex, string awayHex,
                                   string homeSecondaryHex = "#FFFFFF", string awaySecondaryHex = "#FFFFFF")
        {
            _homeColor = SafeColor(homeHex, "#0A2A6A");
            _awayColor = SafeColor(awayHex, "#7A1A1A");
            _homeSecondaryColor = SafeColor(homeSecondaryHex, "#FFFFFF");
            _awaySecondaryColor = SafeColor(awaySecondaryHex, "#FFFFFF");

            var hBrush = new SolidColorBrush(_homeColor);
            var aBrush = new SolidColorBrush(_awayColor);

            // Team secondary colour for all team-row text (names, scores, G/B digits)
            var hSecBrush = new SolidColorBrush(_homeSecondaryColor);
            var aSecBrush = new SolidColorBrush(_awaySecondaryColor);

            // Classic — single continuous gradient across the entire row (including logo area)
            HomeRowGrid.Background = BuildRowGradient(_homeColor);
            AwayRowGrid.Background = BuildRowGradient(_awayColor);
            HomeLogoSquare.Background = System.Windows.Media.Brushes.Transparent;
            AwayLogoSquare.Background = System.Windows.Media.Brushes.Transparent;
            HomeTotalBorder.Background = new SolidColorBrush(Mix(_homeColor, Colors.Black, 0.45));
            AwayTotalBorder.Background = new SolidColorBrush(Mix(_awayColor, Colors.Black, 0.45));
            HomeNameText.Foreground = hSecBrush; AwayNameText.Foreground = aSecBrush;
            HomeTotalText.Foreground = hSecBrush; AwayTotalText.Foreground = aSecBrush;
            HomeGoalsText.Foreground = hSecBrush; HomeBehindsText.Foreground = hSecBrush;
            AwayGoalsText.Foreground = aSecBrush; AwayBehindsText.Foreground = aSecBrush;

            // Expanded
            ExpHomePanelBrush.Color = _homeColor; ExpAwayPanelBrush.Color = _awayColor;
            ExpHomeLogoSquare.Background = BuildLogoBackground(_homeColor);
            ExpAwayLogoSquare.Background = BuildLogoBackground(_awayColor);
            ExpHomeNameText.Foreground = hSecBrush; ExpAwayNameText.Foreground = aSecBrush;
            ExpHomeTotalText.Foreground = hSecBrush; ExpAwayTotalText.Foreground = aSecBrush;
            ExpHomeGoalsText.Foreground = hSecBrush; ExpHomeBehindsText.Foreground = hSecBrush;
            ExpAwayGoalsText.Foreground = aSecBrush; ExpAwayBehindsText.Foreground = aSecBrush;

            ApplyGradient();
        }

        public void SetLogos(string? homeLogoPath, string? awayLogoPath)
        {
            ApplyLogo(HomeLogoImage, HomeLogoFallbackText, homeLogoPath);
            ApplyLogo(AwayLogoImage, AwayLogoFallbackText, awayLogoPath);
            ApplyLogoExpanded(ExpHomeLogoImage, ExpHomeLogoFallback, ExpHomeLogoSquare, homeLogoPath);
            ApplyLogoExpanded(ExpAwayLogoImage, ExpAwayLogoFallback, ExpAwayLogoSquare, awayLogoPath);
            ApplyLogoTransforms();
        }

        public void SetLogoCrop(
            double homeZoom, double homeOffsetX, double homeOffsetY,
            double awayZoom, double awayOffsetX, double awayOffsetY)
        {
            _homeLogoZoom = Math.Clamp(homeZoom, 0.8, 4.0);
            _homeLogoOffsetX = Math.Clamp(homeOffsetX, -50, 50);
            _homeLogoOffsetY = Math.Clamp(homeOffsetY, -50, 50);
            _awayLogoZoom = Math.Clamp(awayZoom, 0.8, 4.0);
            _awayLogoOffsetX = Math.Clamp(awayOffsetX, -50, 50);
            _awayLogoOffsetY = Math.Clamp(awayOffsetY, -50, 50);

            ApplyLogoTransforms();
        }

        private void ApplyLogoTransforms()
        {
            ApplyLogoTransform(HomeLogoImage, _homeLogoZoom, _homeLogoOffsetX, _homeLogoOffsetY);
            ApplyLogoTransform(ExpHomeLogoImage, _homeLogoZoom, _homeLogoOffsetX, _homeLogoOffsetY);
            ApplyLogoTransform(AwayLogoImage, _awayLogoZoom, _awayLogoOffsetX, _awayLogoOffsetY);
            ApplyLogoTransform(ExpAwayLogoImage, _awayLogoZoom, _awayLogoOffsetX, _awayLogoOffsetY);
        }

        private static void ApplyLogoTransform(System.Windows.Controls.Image image, double zoom, double offsetX, double offsetY)
        {
            image.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            var group = new TransformGroup();
            group.Children.Add(new ScaleTransform(zoom, zoom));
            group.Children.Add(new TranslateTransform(offsetX, offsetY));
            image.RenderTransform = group;
        }

        private void ApplyGradient()
        {
            var mid = Color.FromRgb(16, 24, 39);
            TeamGradient.GradientStops.Clear();
            if (_currentLayout == ScorebugLayout.Classic)
            {
                TeamGradient.GradientStops.Add(new GradientStop(Mix(mid, _homeColor, 0.75), 0.0));
                TeamGradient.GradientStops.Add(new GradientStop(mid, 0.50));
                TeamGradient.GradientStops.Add(new GradientStop(Mix(mid, _awayColor, 0.75), 1.0));
            }
            else
            {
                var dark = Color.FromRgb(10, 14, 37);
                TeamGradient.GradientStops.Add(new GradientStop(dark, 0.0));
                TeamGradient.GradientStops.Add(new GradientStop(dark, 1.0));
            }
        }

        private static void ApplyLogo(System.Windows.Controls.Image image, TextBlock fallback, string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            { image.Source = null; image.Visibility = Visibility.Collapsed; fallback.Visibility = Visibility.Visible; return; }
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(path, UriKind.Absolute); bmp.EndInit(); bmp.Freeze();
                image.Source = bmp; image.Visibility = Visibility.Visible; fallback.Visibility = Visibility.Collapsed;
            }
            catch { image.Source = null; image.Visibility = Visibility.Collapsed; fallback.Visibility = Visibility.Visible; }
        }

        private static void ApplyLogoExpanded(System.Windows.Controls.Image image, TextBlock fallback, FrameworkElement placeholder, string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            { image.Source = null; image.Visibility = Visibility.Collapsed; fallback.Visibility = Visibility.Visible; placeholder.Visibility = Visibility.Visible; return; }
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(path, UriKind.Absolute); bmp.EndInit(); bmp.Freeze();
                image.Source = bmp; image.Visibility = Visibility.Visible; fallback.Visibility = Visibility.Collapsed; placeholder.Visibility = Visibility.Collapsed;
            }
            catch { image.Source = null; image.Visibility = Visibility.Collapsed; fallback.Visibility = Visibility.Visible; placeholder.Visibility = Visibility.Visible; }
        }

        private static System.Windows.Media.Brush BuildLogoBackground(Color baseColor)
        {
            var darker = Mix(baseColor, Colors.Black, 0.62);
            var lessDark = Mix(baseColor, Colors.Black, 0.38);
            return new LinearGradientBrush(darker, lessDark, new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
        }

        /// <summary>
        /// Bold horizontal gradient for a team row: starts very dark at the logo edge,
        /// blends through the base colour, and ends noticeably lighter at the score end.
        /// </summary>
        private static System.Windows.Media.Brush BuildRowGradient(Color baseColor)
        {
            Color dark = Mix(baseColor, Colors.Black, 0.65);
            Color lighter = Mix(baseColor, Colors.White, 0.10);

            var grad = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0.5),
                EndPoint = new System.Windows.Point(1, 0.5)
            };
            grad.GradientStops.Add(new GradientStop(dark, 0.0));
            grad.GradientStops.Add(new GradientStop(baseColor, 0.30));
            grad.GradientStops.Add(new GradientStop(lighter, 1.0));
            return grad;
        }

        private static Color SafeColor(string? hex, string fallbackHex)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) hex = fallbackHex;
                if (!hex.StartsWith("#")) hex = "#" + hex.Trim();
                return (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            }
            catch { return (Color)System.Windows.Media.ColorConverter.ConvertFromString(fallbackHex); }
        }

        private static Color Mix(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        // ==== Goal/Behind column expand/collapse ====

        /// <summary>
        /// Smoothly expands the G/B columns with a pop/bounce overshoot.
        /// The full team name stays visible but scales down to fit the reduced width.
        /// Handles re-entry: if already expanded, just resets the collapse timer.
        /// Defers if a goal overlay animation is still playing to avoid layout glitches.
        /// </summary>
        private void ExpandGBColumns()
        {
            // Defer if a goal overlay is still animating — schedule a retry
            if (_homeAnimating || _awayAnimating)
            {
                _gbExpandDelayTimer?.Stop();
                _gbExpandDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                _gbExpandDelayTimer.Tick += (_, _) =>
                {
                    _gbExpandDelayTimer!.Stop();
                    _gbExpandDelayTimer = null;
                    ExpandGBColumns();
                };
                _gbExpandDelayTimer.Start();
                return;
            }

            if (_gbColumnsExpanded)
            {
                ResetGBCollapseTimer();
                return;
            }
            _gbColumnsExpanded = true;

            _gbCollapseTimer?.Stop();

            // Shared easing so column expansion and name condensing move as one
            var popEase = new BackEase { Amplitude = 0.2, EasingMode = EasingMode.EaseOut };

            // Animate name scale down (full name compressed to fit reduced width)
            SyncClassicNameScales(animate: true, GBExpandDuration, popEase);

            // Column expansion — To-only so interrupted collapses resume smoothly
            var expand = new DoubleAnimation { To = GBColumnWidth, Duration = GBExpandDuration, EasingFunction = popEase };
            HomeGoalsContainer.BeginAnimation(WidthProperty, expand.Clone() as DoubleAnimation);
            HomeBehindsContainer.BeginAnimation(WidthProperty, expand.Clone() as DoubleAnimation);
            AwayGoalsContainer.BeginAnimation(WidthProperty, expand.Clone() as DoubleAnimation);
            AwayBehindsContainer.BeginAnimation(WidthProperty, expand);

            // Schedule auto-collapse
            _gbCollapseTimer = new DispatcherTimer { Interval = GBCollapseDelay };
            _gbCollapseTimer.Tick += (_, _) =>
            {
                _gbCollapseTimer.Stop();
                CollapseGBColumns();
            };
            _gbCollapseTimer.Start();
        }

        /// <summary>
        /// Smoothly collapses the G/B columns.
        /// The full team name scales back up to fill the recovered width.
        /// Defers if a goal overlay animation is still playing.
        /// </summary>
        private void CollapseGBColumns()
        {
            if (!_gbColumnsExpanded) return;

            // Defer if a goal overlay is still animating
            if (_homeAnimating || _awayAnimating)
            {
                _gbCollapseTimer?.Stop();
                _gbCollapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                _gbCollapseTimer.Tick += (_, _) =>
                {
                    _gbCollapseTimer!.Stop();
                    CollapseGBColumns();
                };
                _gbCollapseTimer.Start();
                return;
            }

            _gbColumnsExpanded = false;

            _gbCollapseTimer?.Stop();
            _gbCollapseTimer = null;

            // Shared easing so column collapse and name expansion move as one
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            // Animate name scale up (full name expanding to fill recovered width)
            SyncClassicNameScales(animate: true, GBCollapseDuration, ease);

            // Column collapse — To-only so interrupted expands resume smoothly
            var collapse = new DoubleAnimation { To = 0, Duration = GBCollapseDuration, EasingFunction = ease };
            HomeGoalsContainer.BeginAnimation(WidthProperty, collapse.Clone() as DoubleAnimation);
            HomeBehindsContainer.BeginAnimation(WidthProperty, collapse.Clone() as DoubleAnimation);
            AwayGoalsContainer.BeginAnimation(WidthProperty, collapse.Clone() as DoubleAnimation);
            AwayBehindsContainer.BeginAnimation(WidthProperty, collapse);
        }

        /// <summary>
        /// Resets the collapse timer so columns stay visible while scores keep coming.
        /// </summary>
        private void ResetGBCollapseTimer()
        {
            if (!_gbColumnsExpanded) return;
            _gbCollapseTimer?.Stop();
            _gbCollapseTimer = new DispatcherTimer { Interval = GBCollapseDelay };
            _gbCollapseTimer.Tick += (_, _) =>
            {
                _gbCollapseTimer.Stop();
                CollapseGBColumns();
            };
            _gbCollapseTimer.Start();
        }

        /// <summary>
        /// Resets the idle timer that auto-shows G/B columns when neither team has scored for a while.
        /// Call on every score event.
        /// </summary>
        public void ResetAutoShowGBTimer()
        {
            _lastScoreEventTime = DateTime.Now;
            _gbAutoShowTriggered = false;
        }

        /// <summary>
        /// Called periodically from the clock tick path (classic layout only).
        /// If neither team has scored for GBAutoShowIdleMinutes, auto-expands G/B columns.
        /// </summary>
        public void CheckAutoShowGBColumns()
        {
            if (IsExpanded) return;
            if (_lastScoreEventTime == DateTime.MinValue || _gbAutoShowTriggered) return;
            if (_gbColumnsExpanded) return;
            if (_homeAnimating || _awayAnimating) return;

            if ((DateTime.Now - _lastScoreEventTime).TotalMinutes >= GBAutoShowIdleMinutes)
            {
                _gbAutoShowTriggered = true;
                ExpandGBColumns();
            }
        }

        // ==== Score animation routing ====

        private GoalAnimationStyle _animationStyle = GoalAnimationStyle.Broadcast;

        public GoalAnimationStyle AnimationStyle => _animationStyle;

        public void SetGoalAnimationStyle(GoalAnimationStyle style) => _animationStyle = style;
        public GoalAnimationStyle GetGoalAnimationStyle() => _animationStyle;

        public void PlayScoreAnimation(bool isHome, bool isGoal, int newGoals, int newBehinds, int newTotal)
        {
            bool expanded = IsExpanded;
            if (expanded) CancelExpandedTeamAnimation(isHome);
            else CancelClassicTeamAnimation(isHome);

            var sb = new Storyboard();
            double flipStart;

            if (isGoal)
            {
                flipStart = _animationStyle switch
                {
                    GoalAnimationStyle.Broadcast => BuildBroadcastGoal(sb, isHome, expanded),
                    GoalAnimationStyle.Electric => BuildElectricGoal(sb, isHome, expanded),
                    GoalAnimationStyle.Cinematic => BuildCinematicGoal(sb, isHome, expanded),
                    GoalAnimationStyle.Clean => BuildCleanGoal(sb, isHome, expanded),
                    _ => BuildClassicGoal(sb, isHome, expanded),
                };
            }
            else
            {
                flipStart = BuildBehindAnimation(sb, isHome, expanded);
            }

            AddFlipAnimations(sb, isHome, flipStart, newGoals, newBehinds, newTotal, expanded);
            if (isHome) _homeScoreSb = sb; else _awayScoreSb = sb;
            sb.Begin();

            // Delay G/B column expansion until the total score has landed
            _gbExpandDelayTimer?.Stop();
            if (!expanded)
            {
                double gbDelaySec = flipStart + 0.80;
                _gbExpandDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(gbDelaySec) };
                _gbExpandDelayTimer.Tick += (_, _) => { _gbExpandDelayTimer!.Stop(); _gbExpandDelayTimer = null; ExpandGBColumns(); };
                _gbExpandDelayTimer.Start();
            }
            else
            {
                ResetGBCollapseTimer();
            }
        }

        /// <summary>
        /// Plays only the score digit flip animation (no goal overlay).
        /// Used after custom video playback completes.
        /// </summary>
        public void PlayScoreFlipOnly(bool isHome, int newGoals, int newBehinds, int newTotal)
        {
            bool expanded = IsExpanded;
            if (expanded) CancelExpandedTeamAnimation(isHome);
            else CancelClassicTeamAnimation(isHome);

            var sb = new Storyboard();
            AddFlipAnimations(sb, isHome, 0.0, newGoals, newBehinds, newTotal, expanded);
            if (isHome) _homeScoreSb = sb; else _awayScoreSb = sb;
            sb.Begin();

            // Delay G/B column expansion until the total score has landed
            _gbExpandDelayTimer?.Stop();
            if (!expanded)
            {
                _gbExpandDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.80) };
                _gbExpandDelayTimer.Tick += (_, _) => { _gbExpandDelayTimer!.Stop(); _gbExpandDelayTimer = null; ExpandGBColumns(); };
                _gbExpandDelayTimer.Start();
            }
            else
            {
                ResetGBCollapseTimer();
            }
        }

        // ---- Helper accessors for overlay elements ----

        private Border GetOverlay(bool isHome, bool expanded) =>
            expanded ? (isHome ? ExpHomeOverlay : ExpAwayOverlay) : (isHome ? HomeOverlay : AwayOverlay);
        private TextBlock GetOverlayText(bool isHome, bool expanded) =>
            expanded ? (isHome ? ExpHomeOverlayText : ExpAwayOverlayText) : (isHome ? HomeOverlayText : AwayOverlayText);
        private SolidColorBrush GetOverlayBg(bool isHome, bool expanded) =>
            expanded ? (isHome ? ExpHomeOverlayBg : ExpAwayOverlayBg) : (isHome ? HomeOverlayBg : AwayOverlayBg);
        private System.Windows.Shapes.Rectangle GetGradBar(bool isHome, bool expanded) =>
            expanded ? (isHome ? ExpHomeGradBar : ExpAwayGradBar) : (isHome ? HomeGradientBar : AwayGradientBar);
        private TranslateTransform GetGradScroll(bool isHome, bool expanded) =>
            expanded ? (isHome ? ExpHomeGradScroll : ExpAwayGradScroll) : (isHome ? HomeGradientScroll : AwayGradientScroll);
        private System.Windows.Shapes.Rectangle GetShimmer(bool isHome, bool expanded) =>
            expanded ? (isHome ? ExpHomeShimmer : ExpAwayShimmer) : (isHome ? HomeShimmer : AwayShimmer);
        private TranslateTransform GetShimmerScroll(bool isHome, bool expanded) =>
            expanded ? (isHome ? ExpHomeShimmerScroll : ExpAwayShimmerScroll) : (isHome ? HomeShimmerScroll : AwayShimmerScroll);
        private Border GetScoreGlow(bool isHome, bool expanded) =>
            expanded ? (isHome ? ExpHomeScoreGlow : ExpAwayScoreGlow) : (isHome ? HomeScoreGlow : AwayScoreGlow);
        private System.Windows.Shapes.Rectangle GetEdgeGlow(bool isHome, bool expanded) =>
            expanded ? (isHome ? ExpHomeEdgeGlow : ExpAwayEdgeGlow) : (isHome ? HomeEdgeGlow : AwayEdgeGlow);
        private RadialGradientBrush GetScoreGlowBrush(bool isHome, bool expanded) =>
            (RadialGradientBrush)GetScoreGlow(isHome, expanded).Background;

        private void PrepareOverlay(bool isHome, bool expanded, Color primaryColor, Color secondaryColor)
        {
            var overlay = GetOverlay(isHome, expanded);
            var overlayText = GetOverlayText(isHome, expanded);
            var overlayBg = GetOverlayBg(isHome, expanded);
            var gradScroll = GetGradScroll(isHome, expanded);
            var shimmer = GetShimmer(isHome, expanded);
            var shimmerScroll = GetShimmerScroll(isHome, expanded);
            var gradBar = GetGradBar(isHome, expanded);

            overlayBg.Color = Color.FromArgb(0xFF, primaryColor.R, primaryColor.G, primaryColor.B);
            overlayText.Text = "GOAL!";
            overlayText.Foreground = new SolidColorBrush(secondaryColor);
            overlayText.FontSize = expanded ? 100 : 72;

            if (expanded)
            {
                var scale = isHome ? ExpHomeOverlayScale : ExpAwayOverlayScale;
                scale.ScaleX = 0; scale.ScaleY = 0;
            }
            else
            {
                overlayText.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
                overlayText.RenderTransform = new ScaleTransform(0, 0);
            }

            double gradReset = expanded ? -2000 : -1400;
            gradScroll.X = gradReset;
            shimmerScroll.X = expanded ? -250 : -200;
            shimmer.Opacity = 0;
            gradBar.Opacity = expanded ? 0.55 : 0.7;

            if (overlayText.Effect is System.Windows.Media.Effects.DropShadowEffect glow)
            { glow.Color = secondaryColor; glow.BlurRadius = 40; glow.Opacity = 0.9; }
        }

        private void PrepareScoreGlow(bool isHome, bool expanded, Color teamColor)
        {
            var brush = GetScoreGlowBrush(isHome, expanded);
            brush.GradientStops[0].Color = Color.FromArgb(0xCC, teamColor.R, teamColor.G, teamColor.B);
            brush.GradientStops[1].Color = Color.FromArgb(0x00, teamColor.R, teamColor.G, teamColor.B);
        }

        // ==================================================================
        // BROADCAST — AFL / Premier League TV
        // ------------------------------------------------------------------
        // Team-colour overlay (NO rainbow) → giant text slam at 1.5× snapping
        // to 1× → bright score-area glow radiates → single fast shimmer →
        // overlay fades leaving a score glow pulse on the digits.
        // The gradient bar uses a WHITE-to-TEAM colour wash, not rainbow.
        // ==================================================================

        private double BuildBroadcastGoal(Storyboard sb, bool isHome, bool expanded)
        {
            var primaryColor = isHome ? _homeColor : _awayColor;
            var secondaryColor = isHome ? _homeSecondaryColor : _awaySecondaryColor;
            PrepareOverlay(isHome, expanded, primaryColor, secondaryColor);
            PrepareScoreGlow(isHome, expanded, primaryColor);

            var overlay = GetOverlay(isHome, expanded);
            var overlayText = GetOverlayText(isHome, expanded);
            var gradBar = GetGradBar(isHome, expanded);
            var shimmer = GetShimmer(isHome, expanded);
            var scoreGlow = GetScoreGlow(isHome, expanded);

            // Use full-opacity team colour overlay (not 0xEE)
            GetOverlayBg(isHome, expanded).Color = Color.FromArgb(0xFF, primaryColor.R, primaryColor.G, primaryColor.B);

            // Tint the gradient bar to team secondary (white→team instead of rainbow)
            var gradBrush = (LinearGradientBrush)gradBar.Fill;
            gradBrush.GradientStops[0].Color = Colors.White;
            gradBrush.GradientStops[1].Color = secondaryColor;
            gradBrush.GradientStops[2].Color = Colors.White;
            gradBrush.GradientStops[3].Color = secondaryColor;
            gradBrush.GradientStops[4].Color = Colors.White;
            gradBrush.GradientStops[5].Color = secondaryColor;
            gradBrush.GradientStops[6].Color = Colors.White;
            gradBrush.GradientStops[7].Color = secondaryColor;

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            var backEase = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut };
            var decelEase = new CubicEase { EasingMode = EasingMode.EaseOut };
            double gradFrom = expanded ? -2000 : -1400;
            double shimFrom = expanded ? -250 : -200;

            // Score glow radiates FIRST (0–0.4s) — visible beneath overlay
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 0, 1, 0.1, 0.0));
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 1, 0, 1.2, 1.6, ease));

            // Overlay slams in instantly
            sb.Children.Add(MakeDA(overlay, OpacityProperty, 0, 1, 0.04));

            // Team-colour gradient wash (0–1.5s, slower sweep)
            sb.Children.Add(MakeDA(gradBar, "RenderTransform.(TranslateTransform.X)", gradFrom, 900, 1.5, 0.0, ease));

            // GOAL text: starts at 1.5× and snaps DOWN to 1× (overshoot inward)
            overlayText.FontSize = expanded ? 120 : 86;
            if (expanded)
            { var sc = isHome ? ExpHomeOverlayScale : ExpAwayOverlayScale; sc.ScaleX = 1.5; sc.ScaleY = 1.5; }
            else
            { overlayText.RenderTransform = new ScaleTransform(1.5, 1.5); }
            sb.Children.Add(MakeDA(overlayText, "RenderTransform.ScaleX", 1.5, 1, 0.4, 0.02, backEase));
            sb.Children.Add(MakeDA(overlayText, "RenderTransform.ScaleY", 1.5, 1, 0.4, 0.02, backEase));

            // Glow burst: starts massive, contracts
            sb.Children.Add(MakeDA(overlayText, "(UIElement.Effect).(DropShadowEffect.BlurRadius)", 120, 30, 0.5, 0.02, ease));

            // Fast shimmer wipe (0.4–0.9s)
            sb.Children.Add(MakeDA(shimmer, OpacityProperty, 0, 1, 0.06, 0.4));
            sb.Children.Add(MakeDA(shimmer, "RenderTransform.(TranslateTransform.X)", shimFrom, 900, 0.5, 0.4, ease));
            sb.Children.Add(MakeDA(shimmer, OpacityProperty, 1, 0, 0.08, 0.82));

            // Hold, then crisp fade (1.4–1.8s)
            sb.Children.Add(MakeDA(overlay, OpacityProperty, 1, 0, 0.4, 1.4, ease));

            return 1.6;
        }

        // ==================================================================
        // ELECTRIC — NBA / high-energy
        // ------------------------------------------------------------------
        // NO gradient bar at all. Instead: rapid white FLASH (overlay goes
        // full white → team colour), text SLAMS from 2× to 1× instantly,
        // edge glow STROBES 3× rapidly, overlay flickers ON-OFF-ON-OFF-ON
        // before holding steady, then snaps off. Very fast (<1.2s).
        // ==================================================================

        private double BuildElectricGoal(Storyboard sb, bool isHome, bool expanded)
        {
            var primaryColor = isHome ? _homeColor : _awayColor;
            var secondaryColor = isHome ? _homeSecondaryColor : _awaySecondaryColor;
            PrepareOverlay(isHome, expanded, primaryColor, secondaryColor);

            var overlay = GetOverlay(isHome, expanded);
            var overlayText = GetOverlayText(isHome, expanded);
            var overlayBg = GetOverlayBg(isHome, expanded);
            var edgeGlow = GetEdgeGlow(isHome, expanded);
            var gradBar = GetGradBar(isHome, expanded);

            // Hide gradient bar for Electric — we use white flash instead
            gradBar.Opacity = 0;

            // Start overlay as WHITE
            overlayBg.Color = Colors.White;
            overlayText.FontSize = expanded ? 130 : 90;
            overlayText.Foreground = new SolidColorBrush(secondaryColor);
            if (overlayText.Effect is System.Windows.Media.Effects.DropShadowEffect g)
            { g.Color = secondaryColor; g.BlurRadius = 10; g.Opacity = 0.9; }

            if (expanded)
            { var sc = isHome ? ExpHomeOverlayScale : ExpAwayOverlayScale; sc.ScaleX = 2.5; sc.ScaleY = 2.5; }
            else
            { overlayText.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5); overlayText.RenderTransform = new ScaleTransform(2.5, 2.5); }

            var snapEase = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            var decelEase = new CubicEase { EasingMode = EasingMode.EaseOut };

            // Edge glow triple strobe (0–0.3s)
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 0, 1, 0.02, 0.0));
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 1, 0, 0.04, 0.02));
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 0, 1, 0.02, 0.08));
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 1, 0, 0.04, 0.10));
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 0, 1, 0.02, 0.16));
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 1, 0, 0.12, 0.18, decelEase));

            // Overlay: white flash ON-OFF-ON-OFF-ON (stutter)
            sb.Children.Add(MakeDA(overlay, OpacityProperty, 0, 1, 0.02, 0.0));
            sb.Children.Add(MakeDA(overlay, OpacityProperty, 1, 0, 0.02, 0.02));
            sb.Children.Add(MakeDA(overlay, OpacityProperty, 0, 1, 0.02, 0.06));
            sb.Children.Add(MakeDA(overlay, OpacityProperty, 1, 0, 0.02, 0.08));
            sb.Children.Add(MakeDA(overlay, OpacityProperty, 0, 1, 0.02, 0.12));

            // White flash transitions to team colour via timer
            var colorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.14) };
            var timers = isHome ? _homeTimers : _awayTimers;
            var teamCol = Color.FromArgb(0xFF, primaryColor.R, primaryColor.G, primaryColor.B);
            colorTimer.Tick += (_, __) => { colorTimer.Stop(); timers.Remove(colorTimer); overlayBg.Color = teamCol; };
            timers.Add(colorTimer); colorTimer.Start();

            // Text SLAMS from 2.5× → 1× instantly (0.02–0.2s)
            sb.Children.Add(MakeDA(overlayText, "RenderTransform.ScaleX", 2.5, 1, 0.18, 0.02, snapEase));
            sb.Children.Add(MakeDA(overlayText, "RenderTransform.ScaleY", 2.5, 1, 0.18, 0.02, snapEase));

            // Glow burst on impact
            sb.Children.Add(MakeDA(overlayText, "(UIElement.Effect).(DropShadowEffect.BlurRadius)", 10, 80, 0.08, 0.02));
            sb.Children.Add(MakeDA(overlayText, "(UIElement.Effect).(DropShadowEffect.BlurRadius)", 80, 10, 0.15, 0.12, decelEase));

            // Snap OFF (0.9–1.0s)
            sb.Children.Add(MakeDA(overlay, OpacityProperty, 1, 0, 0.1, 0.9, snapEase));

            return 0.8;
        }

        // ==================================================================
        // CINEMATIC — F1 / Champions League
        // ------------------------------------------------------------------
        // Overlay SLIDES IN from left (TranslateTransform), text fades in at
        // normal scale (NO pop, NO scale), glow breathes slowly, entire overlay
        // SLIDES OUT to the right. Very long hold (3.5s+). No shimmer.
        // ==================================================================

        private double BuildCinematicGoal(Storyboard sb, bool isHome, bool expanded)
        {
            var primaryColor = isHome ? _homeColor : _awayColor;
            var secondaryColor = isHome ? _homeSecondaryColor : _awaySecondaryColor;
            PrepareOverlay(isHome, expanded, primaryColor, secondaryColor);
            PrepareScoreGlow(isHome, expanded, primaryColor);

            var overlay = GetOverlay(isHome, expanded);
            var overlayText = GetOverlayText(isHome, expanded);
            var gradBar = GetGradBar(isHome, expanded);
            var scoreGlow = GetScoreGlow(isHome, expanded);

            GetOverlayBg(isHome, expanded).Color = Color.FromArgb(0xFF, primaryColor.R, primaryColor.G, primaryColor.B);
            overlayText.FontSize = expanded ? 110 : 78;

            // Set text to full scale immediately (no pop effect)
            if (expanded)
            { var sc = isHome ? ExpHomeOverlayScale : ExpAwayOverlayScale; sc.ScaleX = 1; sc.ScaleY = 1; }
            else
            { overlayText.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5); overlayText.RenderTransform = new ScaleTransform(1, 1); }

            // Hide gradient bar — cinematic uses no rainbow sweep
            gradBar.Opacity = 0;

            // Text starts invisible (fades in)
            overlayText.Opacity = 0;

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            var decelEase = new CubicEase { EasingMode = EasingMode.EaseOut };
            var gentleEase = new SineEase { EasingMode = EasingMode.EaseInOut };
            double panelWidth = expanded ? 543 : 707;

            // SLIDE overlay in from the left via ClipToBounds + translate
            // We animate the overlay's own position using RenderTransform
            overlay.RenderTransform = new TranslateTransform(-panelWidth, 0);
            overlay.Opacity = 1;

            // Slide in (0–0.6s)
            sb.Children.Add(MakeDA(overlay, "RenderTransform.(TranslateTransform.X)", -panelWidth, 0, 0.6, 0.0, decelEase));

            // Score glow slow build
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 0, 0.8, 0.8, 0.0, decelEase));
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 0.8, 0, 2.0, 2.5, ease));

            // Text fades in (0.3–0.8s) — NO scale animation
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 0, 1, 0.5, 0.3, decelEase));

            // Glow breathes (0.3–3.5s)
            sb.Children.Add(MakeDA(overlayText, "(UIElement.Effect).(DropShadowEffect.BlurRadius)", 20, 100, 0.8, 0.3, ease));
            sb.Children.Add(MakeDA(overlayText, "(UIElement.Effect).(DropShadowEffect.BlurRadius)", 100, 40, 1.0, 1.1, ease));
            sb.Children.Add(MakeDA(overlayText, "(UIElement.Effect).(DropShadowEffect.BlurRadius)", 40, 80, 0.8, 2.1, ease));
            sb.Children.Add(MakeDA(overlayText, "(UIElement.Effect).(DropShadowEffect.BlurRadius)", 80, 20, 0.6, 2.9, ease));

            // SLIDE overlay out to the right (3.2–3.8s)
            sb.Children.Add(MakeDA(overlay, "RenderTransform.(TranslateTransform.X)", 0, panelWidth, 0.6, 3.2, ease));

            return 3.6;
        }

        // ==================================================================
        // CLEAN — Minimal / modern
        // ------------------------------------------------------------------
        // NO overlay panel at all. Score area glows in team colour, the TOTAL
        // score text scales up 1.2× with elastic ease then settles, and all
        // score digits flash bright white foreground momentarily. Completely
        // non-intrusive — the scoreboard stays fully readable.
        // ==================================================================

        private double BuildCleanGoal(Storyboard sb, bool isHome, bool expanded)
        {
            var primaryColor = isHome ? _homeColor : _awayColor;
            PrepareScoreGlow(isHome, expanded, primaryColor);

            var scoreGlow = GetScoreGlow(isHome, expanded);
            var edgeGlow = GetEdgeGlow(isHome, expanded);

            // Get the total text element to animate scale
            var totalText = expanded
                ? (isHome ? ExpHomeTotalText : ExpAwayTotalText)
                : (isHome ? HomeTotalText : AwayTotalText);

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            var elasticEase = new ElasticEase { Oscillations = 1, Springiness = 5, EasingMode = EasingMode.EaseOut };
            var decelEase = new CubicEase { EasingMode = EasingMode.EaseOut };

            // Big score glow pulse (0–1.5s)
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 0, 1, 0.08, 0.0));
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 1, 0.5, 0.2, 0.08, ease));
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 0.5, 0.8, 0.15, 0.28, ease));
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 0.8, 0, 1.0, 0.43, ease));

            // Edge glow accent (0–0.5s)
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 0, 1, 0.04, 0.0));
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 1, 0, 0.45, 0.04, decelEase));

            // Total text scales up 1.2× with elastic spring (0–0.8s)
            totalText.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            var existingFlip = totalText.RenderTransform as TranslateTransform;
            // We need a TransformGroup to combine flip + scale
            var scaleXform = new ScaleTransform(1, 1);
            if (existingFlip != null)
            {
                var grp = new TransformGroup();
                grp.Children.Add(existingFlip);
                grp.Children.Add(scaleXform);
                totalText.RenderTransform = grp;
                sb.Children.Add(MakeDA(scaleXform, ScaleTransform.ScaleXProperty, 1, 1.2, 0.6, 0.0, elasticEase));
                sb.Children.Add(MakeDA(scaleXform, ScaleTransform.ScaleYProperty, 1, 1.2, 0.6, 0.0, elasticEase));
                sb.Children.Add(MakeDA(scaleXform, ScaleTransform.ScaleXProperty, 1.2, 1, 0.4, 0.6, decelEase));
                sb.Children.Add(MakeDA(scaleXform, ScaleTransform.ScaleYProperty, 1.2, 1, 0.4, 0.6, decelEase));
            }

            // No overlay used — flip starts immediately
            return 0.0;
        }

        // ==================================================================
        // CLASSIC — original animation preserved as-is
        // ------------------------------------------------------------------
        // Rainbow gradient sweep + back-ease text pop + shimmer + pulse + fade.
        // This is the ONLY preset that uses the rainbow gradient bar.
        // ==================================================================

        private double BuildClassicGoal(Storyboard sb, bool isHome, bool expanded)
        {
            var primaryColor = isHome ? _homeColor : _awayColor;
            var secondaryColor = isHome ? _homeSecondaryColor : _awaySecondaryColor;
            PrepareOverlay(isHome, expanded, primaryColor, secondaryColor);

            var overlay = GetOverlay(isHome, expanded);
            var overlayText = GetOverlayText(isHome, expanded);
            var gradBar = GetGradBar(isHome, expanded);
            var shimmer = GetShimmer(isHome, expanded);

            // Restore rainbow gradient (in case another preset changed it)
            var gradBrush = (LinearGradientBrush)gradBar.Fill;
            gradBrush.GradientStops[0].Color = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFF");
            gradBrush.GradientStops[1].Color = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFD700");
            gradBrush.GradientStops[2].Color = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF6B00");
            gradBrush.GradientStops[3].Color = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF0080");
            gradBrush.GradientStops[4].Color = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#8000FF");
            gradBrush.GradientStops[5].Color = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#00BFFF");
            gradBrush.GradientStops[6].Color = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#00FF88");
            gradBrush.GradientStops[7].Color = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFF");

            overlayText.FontSize = expanded ? 100 : 72;

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            var backEase = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut };
            double gradFrom = expanded ? -2000 : -1400;
            double shimFrom = expanded ? -250 : -200;

            sb.Children.Add(MakeDA(overlay, OpacityProperty, 0, 1, 0.12));
            sb.Children.Add(MakeDA(gradBar, "RenderTransform.(TranslateTransform.X)", gradFrom, 800, expanded ? 1.2 : 1.8, ease: ease));
            sb.Children.Add(MakeDA(overlayText, "RenderTransform.ScaleX", 0, 1, 0.35, 0.08, backEase));
            sb.Children.Add(MakeDA(overlayText, "RenderTransform.ScaleY", 0, 1, 0.35, 0.08, backEase));
            sb.Children.Add(MakeDA(overlayText, "(UIElement.Effect).(DropShadowEffect.BlurRadius)", 40, expanded ? 100 : 80, expanded ? 0.2 : 0.3, expanded ? 0.08 : 0.4, ease));
            sb.Children.Add(MakeDA(overlayText, "(UIElement.Effect).(DropShadowEffect.BlurRadius)", expanded ? 100 : 80, 30, expanded ? 0.4 : 0.5, expanded ? 0.35 : 0.7, ease));
            sb.Children.Add(MakeDA(shimmer, OpacityProperty, 0, expanded ? 0.85 : 0.8, expanded ? 0.12 : 0.15, 0.5));
            sb.Children.Add(MakeDA(shimmer, "RenderTransform.(TranslateTransform.X)", shimFrom, expanded ? 800 : 900, 0.7, 0.5, ease));
            sb.Children.Add(MakeDA(shimmer, OpacityProperty, expanded ? 0.85 : 0.8, 0, expanded ? 0.12 : 0.15, expanded ? 1.1 : 1.05));
            sb.Children.Add(MakeDA(overlayText, "RenderTransform.ScaleX", 1, 1.12, expanded ? 0.18 : 0.2, 1.0, ease));
            sb.Children.Add(MakeDA(overlayText, "RenderTransform.ScaleY", 1, 1.12, expanded ? 0.18 : 0.2, 1.0, ease));
            sb.Children.Add(MakeDA(overlayText, "RenderTransform.ScaleX", 1.12, 1, expanded ? 0.22 : 0.25, expanded ? 1.18 : 1.2, ease));
            sb.Children.Add(MakeDA(overlayText, "RenderTransform.ScaleY", 1.12, 1, expanded ? 0.22 : 0.25, expanded ? 1.18 : 1.2, ease));
            sb.Children.Add(MakeDA(overlay, OpacityProperty, 1, 0, 0.5, 1.8, ease));

            return 2.1;
        }

        // ==================================================================
        // BEHIND animation (shared across all presets)
        // ------------------------------------------------------------------
        // Team-colour score area glow pulse. No overlay panel.
        // ==================================================================

        private double BuildBehindAnimation(Storyboard sb, bool isHome, bool expanded)
        {
            var primaryColor = isHome ? _homeColor : _awayColor;
            PrepareScoreGlow(isHome, expanded, primaryColor);

            var scoreGlow = GetScoreGlow(isHome, expanded);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            var decelEase = new CubicEase { EasingMode = EasingMode.EaseOut };

            // Subtle score area glow (0–0.8s)
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 0, 0.5, 0.1, 0.0, decelEase));
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 0.5, 0, 0.7, 0.1, ease));

            return 0.0;
        }

        // ---- Shared flip builder ----

        private void AddFlipAnimations(Storyboard sb, bool isHome, double flipStart,
            int newGoals, int newBehinds, int newTotal, bool expanded)
        {
            var goalsText = expanded ? (isHome ? ExpHomeGoalsText : ExpAwayGoalsText) : (isHome ? HomeGoalsText : AwayGoalsText);
            var behindsText = expanded ? (isHome ? ExpHomeBehindsText : ExpAwayBehindsText) : (isHome ? HomeBehindsText : AwayBehindsText);
            var totalText = expanded ? (isHome ? ExpHomeTotalText : ExpAwayTotalText) : (isHome ? HomeTotalText : AwayTotalText);
            // Also keep the other layout in sync
            var altG = expanded ? (isHome ? HomeGoalsText : AwayGoalsText) : (isHome ? ExpHomeGoalsText : ExpAwayGoalsText);
            var altB = expanded ? (isHome ? HomeBehindsText : AwayBehindsText) : (isHome ? ExpHomeBehindsText : ExpAwayBehindsText);
            var altT = expanded ? (isHome ? HomeTotalText : AwayTotalText) : (isHome ? ExpHomeTotalText : ExpAwayTotalText);

            // Detect which components actually changed for blink highlighting
            bool goalsChanged = goalsText.Text != newGoals.ToString();
            bool behindsChanged = behindsText.Text != newBehinds.ToString();

            var snapEase = new CubicEase { EasingMode = EasingMode.EaseIn };
            var bounceEase = new CubicEase { EasingMode = EasingMode.EaseOut };
            var popEase = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut };
            double slideDist = 60;
            var slideOutDur = TimeSpan.FromSeconds(0.18);
            var slideInDur = TimeSpan.FromSeconds(0.28);
            var timers = isHome ? _homeTimers : _awayTimers;

            void AddSlide(TextBlock tb, string val, double delay, TextBlock alt, bool pop = false)
            {
                var begin = TimeSpan.FromSeconds(flipStart + delay);
                // Slide old value out to the left
                var so = new DoubleAnimation(0, -slideDist, slideOutDur) { BeginTime = begin, EasingFunction = snapEase };
                Storyboard.SetTarget(so, tb); Storyboard.SetTargetProperty(so, new PropertyPath("RenderTransform.(TranslateTransform.X)")); sb.Children.Add(so);
                // Slide new value in from the right — G/B values pop with overshoot
                var inEase = pop ? (IEasingFunction)popEase : bounceEase;
                var si = new DoubleAnimation(slideDist, 0, slideInDur) { BeginTime = begin + slideOutDur, EasingFunction = inEase };
                Storyboard.SetTarget(si, tb); Storyboard.SetTargetProperty(si, new PropertyPath("RenderTransform.(TranslateTransform.X)")); sb.Children.Add(si);
                // Update text mid-transition
                var ut = new DispatcherTimer { Interval = begin + slideOutDur };
                ut.Tick += (_, __) => { ut.Stop(); timers.Remove(ut); tb.Text = val; alt.Text = val; };
                timers.Add(ut); ut.Start();
            }

            // Simple synced blink: all changed digits blink together at even intervals
            void AddBlink(TextBlock tb, double startAt)
            {
                const double offDur = 0.06;
                const double onDur = 0.08;
                const int blinkCount = 4;
                for (int i = 0; i < blinkCount; i++)
                {
                    double t = startAt + i * (offDur + onDur);
                    sb.Children.Add(MakeDA(tb, OpacityProperty, 1, 0.1, offDur, t));
                    sb.Children.Add(MakeDA(tb, OpacityProperty, 0.1, 1, onDur, t + offDur));
                }
            }

            // Slide order: total FIRST, then goals and behinds pop outward
            AddSlide(totalText, newTotal.ToString(), 0.0, altT);
            AddSlide(goalsText, newGoals.ToString(), 0.10, altG, pop: true);
            AddSlide(behindsText, newBehinds.ToString(), 0.16, altB, pop: true);

            // After all slides complete, blink ALL changed digits at the same time
            double blinkTime = flipStart + 0.16 + 0.18 + 0.28 + 0.05;
            if (goalsChanged) AddBlink(goalsText, blinkTime);
            if (behindsChanged) AddBlink(behindsText, blinkTime);
            AddBlink(totalText, blinkTime);

            double totalDur = blinkTime + 4 * (0.06 + 0.08) + 0.1;
            var guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(totalDur) };
            guard.Tick += (_, __) => { guard.Stop(); timers.Remove(guard); if (isHome) _homeAnimating = false; else _awayAnimating = false; };
            timers.Add(guard); guard.Start();
        }

        // ---- Cancel helpers ----

        private void CancelClassicTeamAnimation(bool isHome)
        {
            var sb = isHome ? _homeScoreSb : _awayScoreSb;
            var timers = isHome ? _homeTimers : _awayTimers;
            foreach (var t in timers) t.Stop(); timers.Clear(); sb?.Stop();
            ResetFlipTransforms(isHome, false);
            var overlay = isHome ? HomeOverlay : AwayOverlay;
            overlay.BeginAnimation(OpacityProperty, null); overlay.Opacity = 0;
            overlay.RenderTransform = null; // reset Cinematic slide
            var overlayText = isHome ? HomeOverlayText : AwayOverlayText;
            overlayText.BeginAnimation(OpacityProperty, null); overlayText.Opacity = 1;
            overlayText.Foreground = System.Windows.Media.Brushes.White;
            var gradBar = isHome ? HomeGradientBar : AwayGradientBar;
            gradBar.Opacity = 0.6; // restore default
            var scoreGlow = isHome ? HomeScoreGlow : AwayScoreGlow;
            scoreGlow.BeginAnimation(OpacityProperty, null); scoreGlow.Opacity = 0;
            var edgeGlow = isHome ? HomeEdgeGlow : AwayEdgeGlow;
            edgeGlow.BeginAnimation(OpacityProperty, null); edgeGlow.Opacity = 0;
            if (isHome) _homeScoreSb = null; else _awayScoreSb = null;
        }

        private void CancelExpandedTeamAnimation(bool isHome)
        {
            var sb = isHome ? _homeScoreSb : _awayScoreSb;
            var timers = isHome ? _homeTimers : _awayTimers;
            foreach (var t in timers) t.Stop(); timers.Clear(); sb?.Stop();
            ResetFlipTransforms(isHome, true);

            var overlay = isHome ? ExpHomeOverlay : ExpAwayOverlay;
            overlay.BeginAnimation(OpacityProperty, null); overlay.Opacity = 0;
            overlay.RenderTransform = null; // reset Cinematic slide
            var overlayText = isHome ? ExpHomeOverlayText : ExpAwayOverlayText;
            overlayText.BeginAnimation(OpacityProperty, null); overlayText.Opacity = 1;
            overlayText.Foreground = System.Windows.Media.Brushes.White;
            var gradBar = isHome ? ExpHomeGradBar : ExpAwayGradBar;
            gradBar.Opacity = 0.55; // restore default

            var shimmer = isHome ? ExpHomeShimmer : ExpAwayShimmer;
            shimmer.BeginAnimation(OpacityProperty, null); shimmer.Opacity = 0;

            var scoreGlow = isHome ? ExpHomeScoreGlow : ExpAwayScoreGlow;
            scoreGlow.BeginAnimation(OpacityProperty, null); scoreGlow.Opacity = 0;
            var edgeGlow = isHome ? ExpHomeEdgeGlow : ExpAwayEdgeGlow;
            edgeGlow.BeginAnimation(OpacityProperty, null); edgeGlow.Opacity = 0;

            if (isHome) _homeScoreSb = null; else _awayScoreSb = null;
        }

        private void ResetFlipTransforms(bool isHome, bool expanded)
        {
            TranslateTransform gf, bf, tf;
            TextBlock totalText;
            if (expanded)
            {
                gf = isHome ? ExpHomeGoalsFlip : ExpAwayGoalsFlip;
                bf = isHome ? ExpHomeBehindsFlip : ExpAwayBehindsFlip;
                tf = isHome ? ExpHomeTotalFlip : ExpAwayTotalFlip;
                totalText = isHome ? ExpHomeTotalText : ExpAwayTotalText;
            }
            else
            {
                gf = isHome ? HomeGoalsFlip : AwayGoalsFlip;
                bf = isHome ? HomeBehindsFlip : AwayBehindsFlip;
                tf = isHome ? HomeTotalFlip : AwayTotalFlip;
                totalText = isHome ? HomeTotalText : AwayTotalText;
            }
            gf.BeginAnimation(TranslateTransform.YProperty, null); gf.Y = 0;
            bf.BeginAnimation(TranslateTransform.YProperty, null); bf.Y = 0;
            tf.BeginAnimation(TranslateTransform.YProperty, null); tf.Y = 0;
            gf.BeginAnimation(TranslateTransform.XProperty, null); gf.X = 0;
            bf.BeginAnimation(TranslateTransform.XProperty, null); bf.X = 0;
            tf.BeginAnimation(TranslateTransform.XProperty, null); tf.X = 0;

            // Reset opacity from highlight animations
            var goalsText = expanded ? (isHome ? ExpHomeGoalsText : ExpAwayGoalsText) : (isHome ? HomeGoalsText : AwayGoalsText);
            var behindsText = expanded ? (isHome ? ExpHomeBehindsText : ExpAwayBehindsText) : (isHome ? HomeBehindsText : AwayBehindsText);
            goalsText.BeginAnimation(OpacityProperty, null); goalsText.Opacity = 1;
            behindsText.BeginAnimation(OpacityProperty, null); behindsText.Opacity = 1;
            totalText.BeginAnimation(OpacityProperty, null); totalText.Opacity = 1;
            // Restore transforms if highlight/Clean replaced them with TransformGroup
            if (goalsText.RenderTransform is not TranslateTransform)
                goalsText.RenderTransform = gf;
            if (behindsText.RenderTransform is not TranslateTransform)
                behindsText.RenderTransform = bf;
            if (totalText.RenderTransform is not TranslateTransform)
                totalText.RenderTransform = tf;
        }

        // ---- Storyboard helper ----

        private static DoubleAnimation MakeDA(DependencyObject target, DependencyProperty dp, double from, double to, double durSec, double beginSec = 0, IEasingFunction? ease = null)
        {
            var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(durSec)) { EasingFunction = ease };
            if (beginSec > 0) a.BeginTime = TimeSpan.FromSeconds(beginSec);
            Storyboard.SetTarget(a, target); Storyboard.SetTargetProperty(a, new PropertyPath(dp)); return a;
        }

        private static DoubleAnimation MakeDA(DependencyObject target, string path, double from, double to, double durSec, double beginSec = 0, IEasingFunction? ease = null)
        {
            var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(durSec)) { EasingFunction = ease };
            if (beginSec > 0) a.BeginTime = TimeSpan.FromSeconds(beginSec);
            Storyboard.SetTarget(a, target); Storyboard.SetTargetProperty(a, new PropertyPath(path)); return a;
        }

        // ==== Overlay routing helpers ====

        private Border AStatsBar => IsExpanded ? ExpStatsBarBorder : StatsBarBorder;
        private Grid AStatsContent => IsExpanded ? ExpStatsBarContent : StatsBarContent;
        private TranslateTransform AStatsContentTr => IsExpanded ? ExpStatsBarContentTranslate : StatsBarContentTranslate;
        private TextBlock AStatsLabel => IsExpanded ? ExpStatsLabelText : StatsLabelText;
        private TextBlock AStatsHomeVal => IsExpanded ? ExpStatsHomeValueText : StatsHomeValueText;
        private TextBlock AStatsAwayVal => IsExpanded ? ExpStatsAwayValueText : StatsAwayValueText;
        private SolidColorBrush AStatsHomeBrush => IsExpanded ? ExpStatsHomeBarBrush : StatsHomeBarBrush;
        private SolidColorBrush AStatsAwayBrush => IsExpanded ? ExpStatsAwayBarBrush : StatsAwayBarBrush;
        private ColumnDefinition AStatsHomeCol => IsExpanded ? ExpStatsBarHomeCol : StatsBarHomeCol;
        private ColumnDefinition AStatsAwayCol => IsExpanded ? ExpStatsBarAwayCol : StatsBarAwayCol;
        private Border AScorelessBar => IsExpanded ? ExpScorelessBarBorder : ScorelessBarBorder;
        private TextBlock AScorelessTime => IsExpanded ? ExpScorelessTimeText : ScorelessTimeText;
        private Border AScoringRunBar => IsExpanded ? ExpScoringRunBarBorder : ScoringRunBarBorder;
        private LinearGradientBrush AScoringRunGrad => IsExpanded ? ExpScoringRunGradient : ScoringRunGradient;
        private TextBlock AScoringRunLabel => IsExpanded ? ExpScoringRunLabelText : ScoringRunLabelText;
        private TextBlock AScoringRunDetail => IsExpanded ? ExpScoringRunDetailText : ScoringRunDetailText;
        private TextBlock AScoringRunValue => IsExpanded ? ExpScoringRunValueText : ScoringRunValueText;
        private Border ADroughtBar => IsExpanded ? ExpTeamDroughtBarBorder : TeamDroughtBarBorder;
        private LinearGradientBrush ADroughtGrad => IsExpanded ? ExpTeamDroughtGradient : TeamDroughtGradient;
        private TextBlock ADroughtLabel => IsExpanded ? ExpTeamDroughtLabelText : TeamDroughtLabelText;
        private TextBlock ADroughtTime => IsExpanded ? ExpTeamDroughtTimeText : TeamDroughtTimeText;
        private Border ALeadChangesBar => IsExpanded ? ExpLeadChangesBarBorder : LeadChangesBarBorder;
        private TextBlock ALeadChangesLabel => IsExpanded ? ExpLeadChangesLabelText : LeadChangesLabelText;
        private TextBlock ALeadChangesValue => IsExpanded ? ExpLeadChangesValueText : LeadChangesValueText;
        private Border AWinProbBar => IsExpanded ? ExpWinProbBarBorder : WinProbBarBorder;
        private TextBlock AWinProbHomePct => IsExpanded ? ExpWinProbHomePctText : WinProbHomePctText;
        private TextBlock AWinProbAwayPct => IsExpanded ? ExpWinProbAwayPctText : WinProbAwayPctText;
        private TextBlock AWinProbLabel => IsExpanded ? ExpWinProbLabelText : WinProbLabelText;
        private SolidColorBrush AWinProbHomeBrush => IsExpanded ? ExpWinProbHomeBarBrush : WinProbHomeBarBrush;
        private SolidColorBrush AWinProbAwayBrush => IsExpanded ? ExpWinProbAwayBarBrush : WinProbAwayBarBrush;
        private ColumnDefinition AWinProbHomeCol => IsExpanded ? ExpWinProbHomeCol : WinProbHomeCol;
        private ColumnDefinition AWinProbAwayCol => IsExpanded ? ExpWinProbAwayCol : WinProbAwayCol;
        private Border AWarningOverlay => IsExpanded ? ExpWarningOverlay : WarningOverlay;
        private TextBlock AWarningText => IsExpanded ? ExpWarningText : WarningText;
        private TranslateTransform AWarningStripes => IsExpanded ? ExpWarningStripesTranslate : WarningStripesTranslate;
        private Border AWarningGlow => IsExpanded ? ExpWarningGlowBorder : WarningGlowBorder;
        private double OverlayH(double classicH) => IsExpanded ? ExpandedOverlayHeight : classicH;

        // ==== Stats bar ====

        private bool _statsBarVisible;
        private MatchStats? _currentStats;
        private int _statsRotationIndex;
        private DispatcherTimer? _statsRotationTimer;
        private const double StatsBarHeight = 50;
        private const double StatsRotationSeconds = 4.0;

        private enum OverlayKind { StatsBar, FiveMinWarning, ScorelessTimer, ScoringRun, TeamDrought, WinProbability, LeadChanges }
        private readonly Queue<OverlayKind> _overlayQueue = new();
        private OverlayKind? _activeOverlay;

        private void EnqueueOverlay(OverlayKind kind)
        {
            if (_activeOverlay == kind || _overlayQueue.Contains(kind)) return;
            _overlayQueue.Enqueue(kind);
            if (_activeOverlay == null) ProcessNextOverlay();
        }

        private void ProcessNextOverlay()
        {
            if (_overlayQueue.Count == 0) { _activeOverlay = null; return; }
            var next = _overlayQueue.Dequeue(); _activeOverlay = next;
            switch (next)
            {
                case OverlayKind.StatsBar: DoShowStatsBar(); break;
                case OverlayKind.FiveMinWarning: DoShowFiveMinuteWarning(); break;
                case OverlayKind.ScorelessTimer: DoShowScorelessBar(); break;
                case OverlayKind.ScoringRun: DoShowScoringRunBar(); break;
                case OverlayKind.TeamDrought: DoShowTeamDroughtBar(); break;
                case OverlayKind.WinProbability: DoShowWinProbabilityBar(); break;
                case OverlayKind.LeadChanges: DoShowLeadChangesBar(); break;
            }
        }

        private void OnOverlayHidden() { _activeOverlay = null; ProcessNextOverlay(); }

        private struct StatEntry { public string Label; public string HomeValue; public string AwayValue; public double HomePct; }

        private StatEntry[] BuildStatEntries(MatchStats stats)
        {
            double st = stats.HomeScoringShots + stats.AwayScoringShots;
            double at2 = stats.HomeAccuracy + stats.AwayAccuracy;
            double tt = stats.HomeTimePctInFront + stats.AwayTimePctInFront;
            double lt = stats.HomeLargestLead + stats.AwayLargestLead;
            return new[]
            {
                new StatEntry { Label = "SCORING SHOTS", HomeValue = stats.HomeScoringShots.ToString(), AwayValue = stats.AwayScoringShots.ToString(), HomePct = st > 0 ? stats.HomeScoringShots / st : 0.5 },
                new StatEntry { Label = "ACCURACY", HomeValue = $"{stats.HomeAccuracy:0}%", AwayValue = $"{stats.AwayAccuracy:0}%", HomePct = at2 > 0 ? stats.HomeAccuracy / at2 : 0.5 },
                new StatEntry { Label = "TIME IN FRONT", HomeValue = $"{stats.HomeTimePctInFront:0}%", AwayValue = $"{stats.AwayTimePctInFront:0}%", HomePct = tt > 0 ? stats.HomeTimePctInFront / tt : 0.5 },
                new StatEntry { Label = "LARGEST LEAD", HomeValue = stats.HomeLargestLead > 0 ? stats.HomeLargestLead.ToString() : "–", AwayValue = stats.AwayLargestLead > 0 ? stats.AwayLargestLead.ToString() : "–", HomePct = lt > 0 ? (double)stats.HomeLargestLead / lt : 0.5 }
            };
        }

        private void ApplyStat(StatEntry e, bool animate)
        {
            AStatsLabel.Text = e.Label; AStatsHomeVal.Text = e.HomeValue; AStatsAwayVal.Text = e.AwayValue;
            AStatsHomeBrush.Color = _homeColor; AStatsAwayBrush.Color = _awayColor;
            double hp = Math.Clamp(e.HomePct, 0.05, 0.95);
            AStatsHomeCol.Width = new GridLength(hp, GridUnitType.Star);
            AStatsAwayCol.Width = new GridLength(1.0 - hp, GridUnitType.Star);
            if (animate)
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                AStatsContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25)) { EasingFunction = ease });
                AStatsContentTr.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(40, 0, TimeSpan.FromSeconds(0.3)) { EasingFunction = ease });
            }
        }

        private MatchStats? _pendingStats;
        public void ShowStatsBar(MatchStats stats) { _pendingStats = stats; EnqueueOverlay(OverlayKind.StatsBar); }

        /// <summary>
        /// If the stats bar is currently visible, smoothly update the displayed
        /// stat entry with fresh data (e.g. after a scoring event).
        /// </summary>
        public void UpdateStatsIfVisible(MatchStats stats)
        {
            if (!_statsBarVisible) return;
            _currentStats = stats;
            var entries = BuildStatEntries(stats);
            int idx = Math.Clamp(_statsRotationIndex, 0, entries.Length - 1);
            var entry = entries[idx];

            // Animate a quick pulse: fade out → update → fade in
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var fadeOut = new DoubleAnimation(1, 0.4, TimeSpan.FromSeconds(0.15)) { EasingFunction = ease };
            fadeOut.Completed += (_, __) =>
            {
                AStatsLabel.Text = entry.Label;
                AStatsHomeVal.Text = entry.HomeValue;
                AStatsAwayVal.Text = entry.AwayValue;
                AStatsHomeBrush.Color = _homeColor;
                AStatsAwayBrush.Color = _awayColor;
                double hp = Math.Clamp(entry.HomePct, 0.05, 0.95);
                AStatsHomeCol.Width = new GridLength(hp, GridUnitType.Star);
                AStatsAwayCol.Width = new GridLength(1.0 - hp, GridUnitType.Star);
                AStatsContent.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0.4, 1, TimeSpan.FromSeconds(0.25)) { EasingFunction = ease });
            };
            AStatsContent.BeginAnimation(OpacityProperty, fadeOut);
        }

        private void DoShowStatsBar()
        {
            if (_statsBarVisible) return; _statsBarVisible = true;
            _currentStats = _pendingStats ?? _currentStats;
            if (_currentStats == null) { OnOverlayHidden(); return; }
            _statsRotationIndex = 0;
            ApplyStat(BuildStatEntries(_currentStats)[0], false);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            AStatsBar.BeginAnimation(HeightProperty, new DoubleAnimation(0, OverlayH(StatsBarHeight), TimeSpan.FromSeconds(0.4)) { EasingFunction = ease });
            _statsRotationTimer?.Stop();
            _statsRotationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(StatsRotationSeconds) };
            _statsRotationTimer.Tick += StatsRotation_Tick; _statsRotationTimer.Start();
        }

        private void StatsRotation_Tick(object? sender, EventArgs e)
        {
            if (_currentStats == null) return;
            var entries = BuildStatEntries(_currentStats);
            int ni = _statsRotationIndex + 1;
            if (ni >= entries.Length) { _statsRotationTimer?.Stop(); _statsRotationTimer = null; HideStatsBar(); return; }
            var easeOut = new CubicEase { EasingMode = EasingMode.EaseIn };
            var fo = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.2)) { EasingFunction = easeOut };
            fo.Completed += (_, __) => { _statsRotationIndex = ni; ApplyStat(entries[_statsRotationIndex], true); };
            AStatsContent.BeginAnimation(OpacityProperty, fo);
            AStatsContentTr.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -40, TimeSpan.FromSeconds(0.2)) { EasingFunction = easeOut });
        }

        public void HideStatsBar()
        {
            if (!_statsBarVisible) return; _statsBarVisible = false;
            _statsRotationTimer?.Stop(); _statsRotationTimer = null;
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var a = new DoubleAnimation(OverlayH(StatsBarHeight), 0, TimeSpan.FromSeconds(0.35)) { EasingFunction = ease };
            a.Completed += (_, __) => OnOverlayHidden();
            AStatsBar.BeginAnimation(HeightProperty, a);
        }

        // ==== Marquee ====

        private List<string> _marqueeMessages = new();
        private int _marqueeIndex;
        private bool _marqueeRunning;

        public void SetMarqueeMessages(IList<string> messages)
        {
            var incoming = messages == null ? new List<string>() : new List<string>(messages);

            // Immediately interrupt current scroll and apply — new messages show ASAP
            ApplyMarqueeMessages(incoming);
        }

        private void ApplyMarqueeMessages(List<string> messages)
        {
            StopAllMarquees();

            // Find the first truly new message (not in the previous list) and start there
            int startIndex = 0;
            if (messages.Count > 0 && _marqueeMessages.Count > 0)
            {
                for (int i = 0; i < messages.Count; i++)
                {
                    if (!_marqueeMessages.Contains(messages[i]))
                    {
                        startIndex = i;
                        break;
                    }
                }
            }

            _marqueeMessages = messages;
            _marqueeIndex = startIndex;

            if (_marqueeMessages.Count > 0)
                ScrollNextMessage();
        }

        private void ScrollNextMessage()
        {
            if (_marqueeMessages.Count == 0) return;
            if (_marqueeIndex >= _marqueeMessages.Count) _marqueeIndex = 0;

            string msg = _marqueeMessages[_marqueeIndex];
            MarqueeText.Text = msg;
            ExpMarqueeText.Text = msg;

            if (IsExpanded)
                RunSingleMarquee(ExpMarqueeText, ExpMarqueeCanvas, ExpMarqueeTranslate);
            else
                RunSingleMarquee(MarqueeText, MarqueeCanvas, MarqueeTranslate);
        }

        private void RunSingleMarquee(TextBlock text, Canvas canvas, TranslateTransform tr)
        {
            if (string.IsNullOrWhiteSpace(text.Text)) { _marqueeRunning = false; return; }
            tr.BeginAnimation(TranslateTransform.XProperty, null);
            text.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            double tw = text.DesiredSize.Width, th = text.DesiredSize.Height;
            double cw = canvas.ActualWidth > 0 ? canvas.ActualWidth : 600;
            double ch = canvas.ActualHeight > 0 ? canvas.ActualHeight : 100;
            Canvas.SetTop(text, Math.Max(0, (ch - th) / 2));
            Canvas.SetLeft(text, 0);

            double from = cw, to = -tw, dist = from - to;
            double seconds = dist / MarqueePixelsPerSecond;

            _marqueeRunning = true;
            var anim = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(TimeSpan.FromSeconds(seconds))
            };
            anim.Completed += OnMarqueeScrollCompleted;
            tr.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        private void OnMarqueeScrollCompleted(object? sender, EventArgs e)
        {
            _marqueeRunning = false;

            // Advance to the next message and scroll it
            if (_marqueeMessages.Count > 0)
            {
                _marqueeIndex = (_marqueeIndex + 1) % _marqueeMessages.Count;
                ScrollNextMessage();
            }
        }

        private void StartActiveMarquee()
        {
            if (_marqueeMessages.Count == 0) return;
            if (_marqueeRunning) return;
            ScrollNextMessage();
        }

        private void StopAllMarquees()
        {
            _marqueeRunning = false;
            MarqueeTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            ExpMarqueeTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            MarqueeText.Text = "";
            ExpMarqueeText.Text = "";
        }

        private void RestartMarqueeIfNeeded() { if (_marqueeMessages.Count > 0 && !_marqueeRunning) StartActiveMarquee(); }

        // ==== 5-minute warning ====

        private bool _warningVisible;
        private Storyboard? _warningStoryboard;
        private DispatcherTimer? _warningAutoHideTimer;

        public void ShowFiveMinuteWarning() => EnqueueOverlay(OverlayKind.FiveMinWarning);

        private void DoShowFiveMinuteWarning()
        {
            if (_warningVisible) return; _warningVisible = true;
            _warningStoryboard?.Stop(); _warningAutoHideTimer?.Stop();
            var wo = AWarningOverlay; var wt = AWarningText; var ws = AWarningStripes; var wg = AWarningGlow;
            var sb = new Storyboard();
            sb.Children.Add(MakeDA(wo, OpacityProperty, 0, 1, 0.4, ease: new CubicEase { EasingMode = EasingMode.EaseOut }));
            var sx = new DoubleAnimationUsingKeyFrames();
            sx.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            sx.KeyFrames.Add(new EasingDoubleKeyFrame(1.25, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.25)), new CubicEase { EasingMode = EasingMode.EaseOut }));
            sx.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5)), new ElasticEase { Oscillations = 1, Springiness = 5, EasingMode = EasingMode.EaseOut }));
            Storyboard.SetTarget(sx, wt); Storyboard.SetTargetProperty(sx, new PropertyPath("RenderTransform.ScaleX")); sb.Children.Add(sx);
            var sy = sx.Clone(); Storyboard.SetTarget(sy, wt); Storyboard.SetTargetProperty(sy, new PropertyPath("RenderTransform.ScaleY")); sb.Children.Add(sy);
            ws.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 56, TimeSpan.FromSeconds(0.8)) { RepeatBehavior = RepeatBehavior.Forever });
            var gp = new DoubleAnimation(0.3, 0.8, TimeSpan.FromSeconds(0.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            Storyboard.SetTarget(gp, wg); Storyboard.SetTargetProperty(gp, new PropertyPath(OpacityProperty)); sb.Children.Add(gp);
            var tg = new DoubleAnimation(15, 40, TimeSpan.FromSeconds(0.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            Storyboard.SetTarget(tg, wt); Storyboard.SetTargetProperty(tg, new PropertyPath("Effect.BlurRadius")); sb.Children.Add(tg);
            _warningStoryboard = sb; sb.Begin(this, true);
            _warningAutoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _warningAutoHideTimer.Tick += (_, __) => { _warningAutoHideTimer.Stop(); HideFiveMinuteWarning(); };
            _warningAutoHideTimer.Start();
        }

        public void HideFiveMinuteWarning()
        {
            if (!_warningVisible) return; _warningVisible = false;
            _warningAutoHideTimer?.Stop(); _warningAutoHideTimer = null;
            _warningStoryboard?.Stop(); _warningStoryboard = null;
            AWarningStripes.BeginAnimation(TranslateTransform.XProperty, null);
            AWarningGlow.BeginAnimation(OpacityProperty, null); AWarningGlow.Opacity = 0;
            var fo = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.35)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fo.Completed += (_, __) => OnOverlayHidden();
            AWarningOverlay.BeginAnimation(OpacityProperty, fo);
        }

        // ==== Scoreless timer ====

        private bool _scorelessBarVisible;
        private DispatcherTimer? _scorelessUpdateTimer;
        private DateTime _lastScoreTime = DateTime.MinValue;
        private bool _scorelessTriggered;
        private const double ScorelessBarHeight = 50;
        private const double ScorelessShowMinutes = 10.0;

        public void ResetScorelessTimer() { _lastScoreTime = DateTime.Now; _scorelessTriggered = false; if (_scorelessBarVisible) HideScorelessBar(); }

        public void CheckScorelessTimer()
        {
            if (_lastScoreTime == DateTime.MinValue || _scorelessTriggered) return;
            if ((DateTime.Now - _lastScoreTime).TotalMinutes >= ScorelessShowMinutes) { _scorelessTriggered = true; EnqueueOverlay(OverlayKind.ScorelessTimer); }
        }

        public void ShowScorelessBar() { if (_lastScoreTime == DateTime.MinValue) _lastScoreTime = DateTime.Now.AddMinutes(-1); EnqueueOverlay(OverlayKind.ScorelessTimer); }

        private void DoShowScorelessBar()
        {
            if (_scorelessBarVisible) return; _scorelessBarVisible = true;
            UpdateScorelessText();
            AScorelessBar.BeginAnimation(HeightProperty, new DoubleAnimation(0, OverlayH(ScorelessBarHeight), TimeSpan.FromSeconds(0.45)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            _scorelessUpdateTimer?.Stop(); _scorelessUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _scorelessUpdateTimer.Tick += (_, __) => UpdateScorelessText(); _scorelessUpdateTimer.Start();
            var ht = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) }; ht.Tick += (_, __) => { ht.Stop(); HideScorelessBar(); }; ht.Start();
        }

        public void HideScorelessBar()
        {
            if (!_scorelessBarVisible) return; _scorelessBarVisible = false;
            var a = new DoubleAnimation(OverlayH(ScorelessBarHeight), 0, TimeSpan.FromSeconds(0.35)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            a.Completed += (_, __) => OnOverlayHidden();
            AScorelessBar.BeginAnimation(HeightProperty, a);
        }

        private void UpdateScorelessText()
        {
            if (_lastScoreTime == DateTime.MinValue) return;
            var el = DateTime.Now - _lastScoreTime;
            var t = $"{(int)el.TotalMinutes:D2}:{el.Seconds:D2}";
            ScorelessTimeText.Text = t; ExpScorelessTimeText.Text = t;
        }

        // ==== Clear overlay queue ====

        public void ClearOverlayQueue()
        {
            _overlayQueue.Clear();
            if (_statsBarVisible) { _statsBarVisible = false; _statsRotationTimer?.Stop(); _statsRotationTimer = null; ClearHeight(StatsBarBorder); ClearHeight(ExpStatsBarBorder); }
            if (_warningVisible)
            {
                _warningVisible = false; _warningAutoHideTimer?.Stop(); _warningAutoHideTimer = null; _warningStoryboard?.Stop(); _warningStoryboard = null;
                WarningStripesTranslate.BeginAnimation(TranslateTransform.XProperty, null); WarningGlowBorder.BeginAnimation(OpacityProperty, null); WarningGlowBorder.Opacity = 0;
                WarningOverlay.BeginAnimation(OpacityProperty, null); WarningOverlay.Opacity = 0;
                ExpWarningStripesTranslate.BeginAnimation(TranslateTransform.XProperty, null); ExpWarningGlowBorder.BeginAnimation(OpacityProperty, null); ExpWarningGlowBorder.Opacity = 0;
                ExpWarningOverlay.BeginAnimation(OpacityProperty, null); ExpWarningOverlay.Opacity = 0;
            }
            if (_scorelessBarVisible) { _scorelessBarVisible = false; _scorelessUpdateTimer?.Stop(); _scorelessUpdateTimer = null; ClearHeight(ScorelessBarBorder); ClearHeight(ExpScorelessBarBorder); }
            if (_scoringRunBarVisible) { _scoringRunBarVisible = false; _scoringRunUpdateTimer?.Stop(); _scoringRunUpdateTimer = null; ClearHeight(ScoringRunBarBorder); ClearHeight(ExpScoringRunBarBorder); }
            if (_teamDroughtBarVisible) { _teamDroughtBarVisible = false; _teamDroughtUpdateTimer?.Stop(); _teamDroughtUpdateTimer = null; ClearHeight(TeamDroughtBarBorder); ClearHeight(ExpTeamDroughtBarBorder); }
            if (_winProbBarVisible) { _winProbBarVisible = false; ClearHeight(WinProbBarBorder); ClearHeight(ExpWinProbBarBorder); }
            if (_leadChangesBarVisible) { _leadChangesBarVisible = false; _leadChangesAutoHideTimer?.Stop(); _leadChangesAutoHideTimer = null; ClearHeight(LeadChangesBarBorder); ClearHeight(ExpLeadChangesBarBorder); }
            _activeOverlay = null;
        }

        private static void ClearHeight(Border b) { b.BeginAnimation(HeightProperty, null); b.Height = 0; }

        // ==== Scoring run bar ====

        private bool _scoringRunBarVisible;
        private DispatcherTimer? _scoringRunUpdateTimer;
        private const double ScoringRunBarHeight = 50;
        private string _pendingScoringRunTeam = "";
        private DateTime _pendingScoringRunSince;
        private string _pendingScoringRunValue = "";

        public void ShowScoringRun(string teamName, int runPoints, int concededPoints, DateTime runStartTime, Color teamColor)
        {
            _pendingScoringRunTeam = $"{teamName.ToUpper()} SCORING RUN";
            _pendingScoringRunSince = runStartTime;
            _pendingScoringRunValue = $"{runPoints}-{concededPoints}";
            var dark = Color.FromRgb((byte)(teamColor.R / 8), (byte)(teamColor.G / 8), (byte)(teamColor.B / 8));
            var mid = Color.FromRgb((byte)(teamColor.R / 4), (byte)(teamColor.G / 4), (byte)(teamColor.B / 4));
            AScoringRunGrad.GradientStops[0].Color = dark; AScoringRunGrad.GradientStops[1].Color = mid; AScoringRunGrad.GradientStops[2].Color = dark;
            AScoringRunLabel.Foreground = new SolidColorBrush(Color.FromRgb((byte)Math.Min(255, teamColor.R + 80), (byte)Math.Min(255, teamColor.G + 80), (byte)Math.Min(255, teamColor.B + 80)));
            EnqueueOverlay(OverlayKind.ScoringRun);
        }

        private void DoShowScoringRunBar()
        {
            if (_scoringRunBarVisible) return; _scoringRunBarVisible = true;
            AScoringRunLabel.Text = _pendingScoringRunTeam; AScoringRunValue.Text = _pendingScoringRunValue;
            UpdateScoringRunText();
            AScoringRunBar.BeginAnimation(HeightProperty, new DoubleAnimation(0, OverlayH(ScoringRunBarHeight), TimeSpan.FromSeconds(0.45)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            _scoringRunUpdateTimer?.Stop(); _scoringRunUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _scoringRunUpdateTimer.Tick += (_, __) => UpdateScoringRunText(); _scoringRunUpdateTimer.Start();
            var ht = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) }; ht.Tick += (_, __) => { ht.Stop(); HideScoringRunBar(); }; ht.Start();
        }

        private void UpdateScoringRunText()
        {
            var el = DateTime.Now - _pendingScoringRunSince;
            var t = $"in the last {(int)el.TotalMinutes:D2}:{el.Seconds:D2}";
            ScoringRunDetailText.Text = t; ExpScoringRunDetailText.Text = t;
        }

        public void HideScoringRunBar()
        {
            if (!_scoringRunBarVisible) return; _scoringRunBarVisible = false;
            _scoringRunUpdateTimer?.Stop(); _scoringRunUpdateTimer = null;
            var a = new DoubleAnimation(OverlayH(ScoringRunBarHeight), 0, TimeSpan.FromSeconds(0.35)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            a.Completed += (_, __) => OnOverlayHidden();
            AScoringRunBar.BeginAnimation(HeightProperty, a);
        }

        // ==== Team drought bar ====

        private bool _teamDroughtBarVisible;
        private DispatcherTimer? _teamDroughtUpdateTimer;
        private const double TeamDroughtBarHeight = 50;
        private const double TeamDroughtShowMinutes = 6.0;
        private DateTime _homeLastScoreTime = DateTime.MinValue;
        private DateTime _awayLastScoreTime = DateTime.MinValue;
        private bool _homeDroughtTriggered, _awayDroughtTriggered;
        public DateTime HomeLastScoreTime => _homeLastScoreTime;
        public DateTime AwayLastScoreTime => _awayLastScoreTime;
        private string _pendingDroughtLabel = "";
        private DateTime _pendingDroughtSince;
        private Color _pendingDroughtTeamColor;

        public void ResetTeamDroughtTimer(bool isHome)
        {
            if (isHome) { _homeLastScoreTime = DateTime.Now; _homeDroughtTriggered = false; } else { _awayLastScoreTime = DateTime.Now; _awayDroughtTriggered = false; }
            if (_teamDroughtBarVisible) HideTeamDroughtBar();
        }

        public void CheckTeamDrought(string homeName, string awayName, Color homeColor, Color awayColor)
        {
            var now = DateTime.Now;
            if (_homeLastScoreTime != DateTime.MinValue && !_homeDroughtTriggered && (now - _homeLastScoreTime).TotalMinutes >= TeamDroughtShowMinutes)
            { _homeDroughtTriggered = true; ShowTeamDrought(homeName, _homeLastScoreTime, homeColor); return; }
            if (_awayLastScoreTime != DateTime.MinValue && !_awayDroughtTriggered && (now - _awayLastScoreTime).TotalMinutes >= TeamDroughtShowMinutes)
            { _awayDroughtTriggered = true; ShowTeamDrought(awayName, _awayLastScoreTime, awayColor); }
        }

        public void ShowTeamDrought(string teamName, DateTime lastScoreTime, Color teamColor)
        {
            _pendingDroughtLabel = $"{teamName.ToUpper()} SCORELESS"; _pendingDroughtSince = lastScoreTime; _pendingDroughtTeamColor = teamColor;
            EnqueueOverlay(OverlayKind.TeamDrought);
        }

        private void DoShowTeamDroughtBar()
        {
            if (_teamDroughtBarVisible) return; _teamDroughtBarVisible = true;
            ADroughtLabel.Text = _pendingDroughtLabel;
            var dark = Color.FromRgb((byte)(_pendingDroughtTeamColor.R / 8), (byte)(_pendingDroughtTeamColor.G / 8), (byte)(_pendingDroughtTeamColor.B / 8));
            var mid = Color.FromRgb((byte)(_pendingDroughtTeamColor.R / 4), (byte)(_pendingDroughtTeamColor.G / 4), (byte)(_pendingDroughtTeamColor.B / 4));
            ADroughtGrad.GradientStops[0].Color = dark; ADroughtGrad.GradientStops[1].Color = mid; ADroughtGrad.GradientStops[2].Color = dark;
            ADroughtLabel.Foreground = new SolidColorBrush(Color.FromRgb((byte)Math.Min(255, _pendingDroughtTeamColor.R + 100), (byte)Math.Min(255, _pendingDroughtTeamColor.G + 100), (byte)Math.Min(255, _pendingDroughtTeamColor.B + 100)));
            UpdateTeamDroughtText();
            ADroughtBar.BeginAnimation(HeightProperty, new DoubleAnimation(0, OverlayH(TeamDroughtBarHeight), TimeSpan.FromSeconds(0.45)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            _teamDroughtUpdateTimer?.Stop(); _teamDroughtUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _teamDroughtUpdateTimer.Tick += (_, __) => UpdateTeamDroughtText(); _teamDroughtUpdateTimer.Start();
            var ht = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) }; ht.Tick += (_, __) => { ht.Stop(); HideTeamDroughtBar(); }; ht.Start();
        }

        public void HideTeamDroughtBar()
        {
            if (!_teamDroughtBarVisible) return; _teamDroughtBarVisible = false;
            _teamDroughtUpdateTimer?.Stop(); _teamDroughtUpdateTimer = null;
            var a = new DoubleAnimation(OverlayH(TeamDroughtBarHeight), 0, TimeSpan.FromSeconds(0.35)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            a.Completed += (_, __) => OnOverlayHidden();
            ADroughtBar.BeginAnimation(HeightProperty, a);
        }

        private void UpdateTeamDroughtText()
        {
            if (_pendingDroughtSince == DateTime.MinValue) return;
            var el = DateTime.Now - _pendingDroughtSince;
            var t = $"{(int)el.TotalMinutes:D2}:{el.Seconds:D2}";
            TeamDroughtTimeText.Text = t; ExpTeamDroughtTimeText.Text = t;
        }

        // ==== Win probability bar ====

        private bool _winProbBarVisible;
        private const double WinProbBarHeight = 50;
        private WinProbabilityResult? _pendingWinProb;

        public void ShowWinProbability(WinProbabilityResult result)
        {
            _pendingWinProb = result;
            EnqueueOverlay(OverlayKind.WinProbability);
        }

        public void SetWinProbColors(Color homeColor, Color awayColor)
        {
            WinProbHomeBarBrush.Color = homeColor;
            ExpWinProbHomeBarBrush.Color = homeColor;
            WinProbAwayBarBrush.Color = awayColor;
            ExpWinProbAwayBarBrush.Color = awayColor;
        }

        /// <summary>
        /// Silently update the probability values if the bar is already visible,
        /// without re-triggering the show animation or overlay queue.
        /// </summary>
        public void UpdateWinProbabilityIfVisible(WinProbabilityResult result)
        {
            _pendingWinProb = result;
            if (_winProbBarVisible)
                ApplyWinProbValues(result);
        }

        private void DoShowWinProbabilityBar()
        {
            if (_winProbBarVisible) return;
            _winProbBarVisible = true;
            var result = _pendingWinProb;
            if (result == null) { OnOverlayHidden(); return; }
            ApplyWinProbValues(result);
            AWinProbBar.BeginAnimation(HeightProperty,
                new DoubleAnimation(0, OverlayH(WinProbBarHeight), TimeSpan.FromSeconds(0.45))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            var ht = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            ht.Tick += (_, __) => { ht.Stop(); HideWinProbabilityBar(); };
            ht.Start();
        }

        public void HideWinProbabilityBar()
        {
            if (!_winProbBarVisible) return;
            _winProbBarVisible = false;
            var a = new DoubleAnimation(OverlayH(WinProbBarHeight), 0, TimeSpan.FromSeconds(0.35))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            a.Completed += (_, __) => OnOverlayHidden();
            AWinProbBar.BeginAnimation(HeightProperty, a);
        }

        private void ApplyWinProbValues(WinProbabilityResult r)
        {
            int hPct = (int)Math.Round(r.HomeWinPct * 100);
            int aPct = (int)Math.Round(r.AwayWinPct * 100);
            // Ensure they add to 100 (absorb draw into rounding)
            if (hPct + aPct < 100) { if (r.HomeWinPct >= r.AwayWinPct) hPct = 100 - aPct; else aPct = 100 - hPct; }
            if (hPct + aPct > 100) { if (r.HomeWinPct >= r.AwayWinPct) aPct = 100 - hPct; else hPct = 100 - aPct; }
            hPct = Math.Clamp(hPct, 1, 99);
            aPct = Math.Clamp(aPct, 1, 99);

            WinProbHomePctText.Text = $"{hPct}%"; ExpWinProbHomePctText.Text = $"{hPct}%";
            WinProbAwayPctText.Text = $"{aPct}%"; ExpWinProbAwayPctText.Text = $"{aPct}%";

            // Animate the split bar column widths
            var homeWidth = new GridLength(hPct, GridUnitType.Star);
            var awayWidth = new GridLength(aPct, GridUnitType.Star);
            WinProbHomeCol.Width = homeWidth; ExpWinProbHomeCol.Width = homeWidth;
            WinProbAwayCol.Width = awayWidth; ExpWinProbAwayCol.Width = awayWidth;

            // Delta label
            double absDelta = Math.Abs(r.HomeDelta) * 100;
            if (absDelta >= 0.5)
            {
                string arrow = r.HomeDelta > 0 ? "▲" : "▼";
                string deltaText = $"WIN PROBABILITY   {arrow} {absDelta:F0}%";
                WinProbLabelText.Text = deltaText; ExpWinProbLabelText.Text = deltaText;
            }
            else
            {
                WinProbLabelText.Text = "WIN PROBABILITY"; ExpWinProbLabelText.Text = "WIN PROBABILITY";
            }
        }

        // ==== Lead changes bar ====

        private bool _leadChangesBarVisible;
        private const double LeadChangesBarHeight = 50;
        private int _pendingLeadChanges;
        private DispatcherTimer? _leadChangesAutoHideTimer;

        public void ShowLeadChangesBar(int leadChanges)
        {
            _pendingLeadChanges = leadChanges;
            EnqueueOverlay(OverlayKind.LeadChanges);
        }

        private void DoShowLeadChangesBar()
        {
            if (_leadChangesBarVisible) return; _leadChangesBarVisible = true;
            ALeadChangesValue.Text = _pendingLeadChanges.ToString();
            ALeadChangesLabel.Text = "LEAD CHANGES:";
            ALeadChangesBar.BeginAnimation(HeightProperty,
                new DoubleAnimation(0, OverlayH(LeadChangesBarHeight), TimeSpan.FromSeconds(0.45))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            _leadChangesAutoHideTimer?.Stop();
            _leadChangesAutoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            _leadChangesAutoHideTimer.Tick += (_, __) => { _leadChangesAutoHideTimer?.Stop(); HideLeadChangesBar(); };
            _leadChangesAutoHideTimer.Start();
        }

        public void HideLeadChangesBar()
        {
            if (!_leadChangesBarVisible) return; _leadChangesBarVisible = false;
            _leadChangesAutoHideTimer?.Stop(); _leadChangesAutoHideTimer = null;
            var a = new DoubleAnimation(OverlayH(LeadChangesBarHeight), 0, TimeSpan.FromSeconds(0.35))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            a.Completed += (_, __) => OnOverlayHidden();
            ALeadChangesBar.BeginAnimation(HeightProperty, a);
        }

        /// <summary>
        /// Manually toggles the G/B columns next to the total score:
        /// expands if currently collapsed, collapses if currently expanded.
        /// </summary>
        public void ToggleGBColumns()
        {
            if (_gbColumnsExpanded)
                CollapseGBColumns();
            else
                ExpandGBColumns();
        }

        /// <summary>
        /// Full reset for new game — clears all visible overlays AND resets
        /// the underlying timer/trigger state so nothing carries over.
        /// </summary>
        public void ResetAllOverlayState()
        {
            ClearOverlayQueue();

            // Scoreless timer
            _lastScoreTime = DateTime.MinValue;
            _scorelessTriggered = false;

            // Per-team drought timers
            _homeLastScoreTime = DateTime.MinValue;
            _awayLastScoreTime = DateTime.MinValue;
            _homeDroughtTriggered = false;
            _awayDroughtTriggered = false;

            // Scoring run pending state
            _pendingScoringRunTeam = "";
            _pendingScoringRunValue = "";
            _pendingScoringRunSince = DateTime.MinValue;

            // Drought pending state
            _pendingDroughtLabel = "";
            _pendingDroughtSince = DateTime.MinValue;

            // Win probability
            _pendingWinProb = null;

            // Auto-show G/B idle timer
            _lastScoreEventTime = DateTime.MinValue;
            _gbAutoShowTriggered = false;
        }
    }
}

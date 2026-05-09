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
using System.Windows.Media.Effects;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using HAlign = System.Windows.HorizontalAlignment;

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
        private ScorebugLayout _currentLayout = ScorebugLayout.Expanded;
        private const double ExpandedOverlayHeight = 40;

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
        private bool _finalsMode;
        private string? _weatherLocation;
        private WeatherSnapshot? _weatherSnapshot;

        public event Action? OverlayHidden;
        public bool IsScoreAnimating => _homeAnimating || _awayAnimating;
        public bool IsOverlayActive => _activeOverlay is not null;
        public bool IsRetroLayout => _currentLayout == ScorebugLayout.Retro;

        public void NotifyClockStarted() { }
        public void NotifyClockStopped() { }

        public ScorebugLayout CurrentLayout => _currentLayout;

        public ScorebugControl()
        {
            InitializeComponent();
            ApplyGradient();

            var now = DateTime.Now.ToString("h:mm tt");
            TopTimeText.Text = now;
            ExpTopTimeText.Text = now;
            ExpTopClockText.Text = now;
            ExpTopDateText.Text = DateTime.Now.ToString("ddd d MMM");
            ExpTopWeatherText.Text = "--°";

            _wallClockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _wallClockTimer.Tick += (_, __) =>
            {
                var t = DateTime.Now.ToString("h:mm tt");
                TopTimeText.Text = t;
                ExpTopTimeText.Text = t;
                ExpTopClockText.Text = t;
                ExpTopDateText.Text = DateTime.Now.ToString("ddd d MMM");
            };
            _wallClockTimer.Start();

            MarqueeCanvas.SizeChanged += (_, __) => RestartMarqueeIfNeeded();
            ExpMarqueeCanvas.SizeChanged += (_, __) => RestartMarqueeIfNeeded();
            ExpandedLayout.SizeChanged += (_, __) =>
            {
                UpdateCompactClockMode();
                UpdateExpOverlayBleed();
            };
            Loaded += (_, __) =>
            {
                StartActiveMarquee();
                UpdateCompactClockMode();
                UpdateExpOverlayBleed();
            };
        }

        // The expanded GOAL/LEAD-CHANGE overlays bleed left across the logo
        // column so the team-colour wash visually unifies the score and logo
        // areas. The bleed must equal the logo column's actual width — when the
        // output window resizes the column changes width and a static -240
        // margin would leave a gap or overshoot. Recompute on layout changes.
        private void UpdateExpOverlayBleed()
        {
            double logoW = ExpLogoColumn.ActualWidth;
            if (logoW <= 0) return;
            var bleed = new Thickness(-logoW, 0, 0, 0);
            ExpHomeOverlay.Margin = bleed;
            ExpAwayOverlay.Margin = bleed;
        }

        // When the output window aspect ratio (W/H) drops below this threshold,
        // the dedicated left clock/quarter column collapses and a compact bar
        // takes over the ticker area. When the ratio rises back above the
        // threshold (plus a small hysteresis margin), the original layout
        // returns. Threshold chosen so ~16:9 output is wide-mode but a roughly
        // square or portrait window switches to compact mode.
        private const double CompactAspectThreshold = 1.30;
        private const double CompactAspectHysteresis = 0.05;
        private bool _compactClockActive;

        private void UpdateCompactClockMode()
        {
            double w = ExpandedLayout.ActualWidth;
            double h = ExpandedLayout.ActualHeight;
            if (w <= 0 || h <= 0) return;

            double ratio = w / h;
            bool shouldBeCompact = _compactClockActive
                ? ratio < (CompactAspectThreshold + CompactAspectHysteresis)
                : ratio < CompactAspectThreshold;

            if (shouldBeCompact == _compactClockActive) return;
            _compactClockActive = shouldBeCompact;

            if (shouldBeCompact)
            {
                ExpClockColumn.MinWidth = 0;
                ExpClockColumn.Width = new GridLength(0);
                ExpClockPanel.Visibility = Visibility.Collapsed;
                ExpMarqueeCanvas.Visibility = Visibility.Collapsed;
                ExpCompactClockBar.Visibility = Visibility.Visible;
                StopAllMarquees();
            }
            else
            {
                ExpClockColumn.MinWidth = 120;
                ExpClockColumn.Width = new GridLength(210, GridUnitType.Star);
                ExpClockPanel.Visibility = Visibility.Visible;
                ExpCompactClockBar.Visibility = Visibility.Collapsed;
                ExpMarqueeCanvas.Visibility = Visibility.Visible;
                StartActiveMarquee();
            }
        }

        // ---- Layout switching ----

        public void SetLayout(ScorebugLayout layout)
        {
            // Only the modern (Expanded) layout is supported now — flexible, no Viewbox, no stretching.
            layout = ScorebugLayout.Expanded;
            if (_currentLayout == layout) return;
            ClearOverlayQueue();

            _currentLayout = layout;
            ClassicLayout.Visibility = Visibility.Collapsed;
            ExpandedLayout.Visibility = Visibility.Visible;

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
            ExpCompactQuarterText.Text = $"Q{quarter}";
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
            ExpCompactQuarterText.Text = breakLabel;

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
            ExpCompactClockText.Text = v;
        }

        private Color _homeSecondaryColor = Colors.White;
        private Color _awaySecondaryColor = Colors.White;
        private double _homeLogoZoom = 1.0;
        private double _homeLogoOffsetX;
        private double _homeLogoOffsetY;
        private double _awayLogoZoom = 1.0;
        private double _awayLogoOffsetX;
        private double _awayLogoOffsetY;
        private ImageSource? _homeLogoSource;
        private ImageSource? _awayLogoSource;

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

            // Expanded — apply team gradients across logo cell and score panel.
            // The right-side end stops are kept relatively bright so the gradient
            // remains noticeable without going muddy / overly dark.
            var hDark = Mix(_homeColor, Colors.Black, 0.30);
            var aDark = Mix(_awayColor, Colors.Black, 0.30);
            ExpHomeLogoCellGradStart.Color = hDark;
            ExpHomeLogoCellGradEnd.Color = _homeColor;
            ExpHomePanelGradStart.Color = _homeColor;
            ExpHomePanelGradEnd.Color = hDark;
            ExpAwayLogoCellGradStart.Color = aDark;
            ExpAwayLogoCellGradEnd.Color = _awayColor;
            ExpAwayPanelGradStart.Color = _awayColor;
            ExpAwayPanelGradEnd.Color = aDark;
            ExpHomeLogoSquare.Background = BuildLogoBackground(_homeColor);
            ExpAwayLogoSquare.Background = BuildLogoBackground(_awayColor);
            ExpHomeNameText.Foreground = hSecBrush; ExpAwayNameText.Foreground = aSecBrush;
            ExpHomeTotalText.Foreground = hSecBrush; ExpAwayTotalText.Foreground = aSecBrush;
            ExpHomeGoalsText.Foreground = hSecBrush; ExpHomeBehindsText.Foreground = hSecBrush;
            ExpAwayGoalsText.Foreground = aSecBrush; ExpAwayBehindsText.Foreground = aSecBrush;

            // Swipe overlays use team secondary as background and team primary as text colour
            ExpHomeSwipeBg.Color = _homeSecondaryColor; ExpHomeSwipeFg.Color = _homeColor;
            ExpAwaySwipeBg.Color = _awaySecondaryColor; ExpAwaySwipeFg.Color = _awayColor;

            // Tint overlay border gradients with team primary colours
            ApplyOverlayBorderColors();

            ApplyGradient();
        }

        /// <summary>
        /// Overlay borders are intentionally fixed neon colours that do not
        /// follow team primaries. This method now only applies team accents to
        /// the recent-scores header (which still benefits from a per-match
        /// home/away cue), leaving every other overlay border untouched.
        /// </summary>
        private void ApplyOverlayBorderColors()
        {
            var hC = _homeColor;
            var aC = _awayColor;
            if (ExpRecentScoresHeaderHomeStop is not null) ExpRecentScoresHeaderHomeStop.Color = hC;
            if (ExpRecentScoresHeaderAwayStop is not null) ExpRecentScoresHeaderAwayStop.Color = aC;

            // Quarter-scores boxed layout
            if (ExpQuarterScoresHomeGoalsBg is not null) ExpQuarterScoresHomeGoalsBg.Color = hC;
            if (ExpQuarterScoresHomeBehindsBg is not null) ExpQuarterScoresHomeBehindsBg.Color = hC;
            if (ExpQuarterScoresHomeTotalBg is not null) ExpQuarterScoresHomeTotalBg.Color = hC;
            if (ExpQuarterScoresAwayGoalsBg is not null) ExpQuarterScoresAwayGoalsBg.Color = aC;
            if (ExpQuarterScoresAwayBehindsBg is not null) ExpQuarterScoresAwayBehindsBg.Color = aC;
            if (ExpQuarterScoresAwayTotalBg is not null) ExpQuarterScoresAwayTotalBg.Color = aC;
            var hSec = _homeSecondaryColor;
            var aSec = _awaySecondaryColor;
            if (ExpQuarterScoresHomeGoalsBorder is not null) ExpQuarterScoresHomeGoalsBorder.Color = hSec;
            if (ExpQuarterScoresHomeBehindsBorder is not null) ExpQuarterScoresHomeBehindsBorder.Color = hSec;
            if (ExpQuarterScoresAwayGoalsBorder is not null) ExpQuarterScoresAwayGoalsBorder.Color = aSec;
            if (ExpQuarterScoresAwayBehindsBorder is not null) ExpQuarterScoresAwayBehindsBorder.Color = aSec;
        }

        public void SetLogos(string? homeLogoPath, string? awayLogoPath)
        {
            ApplyLogo(HomeLogoImage, HomeLogoFallbackText, homeLogoPath);
            ApplyLogo(AwayLogoImage, AwayLogoFallbackText, awayLogoPath);
            ApplyLogoExpanded(ExpHomeLogoImage, ExpHomeLogoFallback, ExpHomeLogoSquare, homeLogoPath);
            ApplyLogoExpanded(ExpAwayLogoImage, ExpAwayLogoFallback, ExpAwayLogoSquare, awayLogoPath);
            _homeLogoSource = ExpHomeLogoImage.Source;
            _awayLogoSource = ExpAwayLogoImage.Source;
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

                // Auto-crop transparent / near-uniform borders so small logos visually fill the cell.
                var trimmed = TrimLogoTransparentBorders(bmp);
                image.Source = trimmed; image.Visibility = Visibility.Visible; fallback.Visibility = Visibility.Collapsed; placeholder.Visibility = Visibility.Collapsed;
            }
            catch { image.Source = null; image.Visibility = Visibility.Collapsed; fallback.Visibility = Visibility.Visible; placeholder.Visibility = Visibility.Visible; }
        }

        /// <summary>
        /// Detects fully-transparent or near-transparent border rows/columns around a logo
        /// and returns a tightly-cropped <see cref="BitmapSource"/>. This makes the logo
        /// visually fill the available cell when the source image has lots of empty space.
        /// Falls back to the original bitmap if no crop is possible or the format isn't BGRA.
        /// </summary>
        private static BitmapSource TrimLogoTransparentBorders(BitmapImage src)
        {
            try
            {
                var converted = new FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                int w = converted.PixelWidth;
                int h = converted.PixelHeight;
                if (w <= 4 || h <= 4) return src;

                int stride = w * 4;
                byte[] pixels = new byte[stride * h];
                converted.CopyPixels(pixels, stride, 0);

                const byte alphaThreshold = 16;
                int top = 0, bottom = h - 1, left = 0, right = w - 1;

                bool RowEmpty(int y)
                {
                    int row = y * stride;
                    for (int x = 0; x < w; x++) if (pixels[row + x * 4 + 3] > alphaThreshold) return false;
                    return true;
                }
                bool ColEmpty(int x)
                {
                    int xi = x * 4 + 3;
                    for (int y = 0; y < h; y++) if (pixels[y * stride + xi] > alphaThreshold) return false;
                    return true;
                }

                while (top < bottom && RowEmpty(top)) top++;
                while (bottom > top && RowEmpty(bottom)) bottom--;
                while (left < right && ColEmpty(left)) left++;
                while (right > left && ColEmpty(right)) right--;

                int cropW = right - left + 1;
                int cropH = bottom - top + 1;
                if (cropW <= 0 || cropH <= 0) return src;
                // No meaningful trim — return original
                if (top == 0 && left == 0 && cropW == w && cropH == h) return src;

                var cropped = new CroppedBitmap(converted, new Int32Rect(left, top, cropW, cropH));
                cropped.Freeze();
                return cropped;
            }
            catch
            {
                return src;
            }
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

        // Derives the goal/lead-change gradient sweep range from the overlay's
        // actual width so the wash always travels off-screen-to-off-screen no
        // matter how the operator has resized the output window. Falls back to
        // the original 960-design values if width isn't available yet.
        private static (double from, double to) GetGradSweep(FrameworkElement overlay, bool expanded)
        {
            double w = overlay.ActualWidth;
            if (w <= 0)
                return (expanded ? -2000 : -1400, 900);

            // Original ratios at 960 design width: from=-2000 (~-2.08w), to=900 (~0.94w),
            // collapsed: from=-1400 (~-1.46w), to=900. Reproduce them container-relative.
            double fromRatio = expanded ? -2.08 : -1.46;
            double toRatio = 0.94;
            return (w * fromRatio, w * toRatio);
        }

        private static double GetShimmerFrom(FrameworkElement overlay, bool expanded)
        {
            double w = overlay.ActualWidth;
            if (w <= 0) return expanded ? -250 : -200;
            return w * (expanded ? -0.26 : -0.21);
        }

        private static double GetShimmerTo(FrameworkElement overlay, bool expanded)
        {
            double w = overlay.ActualWidth;
            if (w <= 0) return expanded ? 800 : 900;
            return w * (expanded ? 0.84 : 0.94);
        }

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
        // Team-colour overlay (NO rainbow) ? giant text slam at 1.5× snapping
        // to 1× ? bright score-area glow radiates ? single fast shimmer ?
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

            // Tint the gradient bar to team secondary (white?team instead of rainbow)
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
            var (gradFrom, gradTo) = GetGradSweep(overlay, expanded);
            double shimFrom = GetShimmerFrom(overlay, expanded);
            double shimTo = GetShimmerTo(overlay, expanded);

            // Score glow radiates FIRST (0–0.4s) — visible beneath overlay
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 0, 1, 0.1, 0.0));
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 1, 0, 1.2, 1.6, ease));

            // Overlay slams in instantly
            sb.Children.Add(MakeDA(overlay, OpacityProperty, 0, 1, 0.04));

            // Team-colour gradient wash (0–1.5s, slower sweep)
            sb.Children.Add(MakeDA(gradBar, "RenderTransform.(TranslateTransform.X)", gradFrom, gradTo, 1.5, 0.0, ease));

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
            sb.Children.Add(MakeDA(shimmer, "RenderTransform.(TranslateTransform.X)", shimFrom, shimTo, 0.5, 0.4, ease));
            sb.Children.Add(MakeDA(shimmer, OpacityProperty, 1, 0, 0.08, 0.82));

            // Hold, then crisp fade (1.4–1.8s)
            sb.Children.Add(MakeDA(overlay, OpacityProperty, 1, 0, 0.4, 1.4, ease));

            return 1.6;
        }

        // ==================================================================
        // ELECTRIC — NBA / high-energy
        // ------------------------------------------------------------------
        // NO gradient bar at all. Instead: rapid white FLASH (overlay goes
        // full white ? team colour), text SLAMS from 2× to 1× instantly,
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

            // Text SLAMS from 2.5× ? 1× instantly (0.02–0.2s)
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
            var (gradFrom, gradTo) = GetGradSweep(overlay, expanded);
            double shimFrom = GetShimmerFrom(overlay, expanded);
            double shimTo = GetShimmerTo(overlay, expanded);

            sb.Children.Add(MakeDA(overlay, OpacityProperty, 0, 1, 0.12));
            sb.Children.Add(MakeDA(gradBar, "RenderTransform.(TranslateTransform.X)", gradFrom, gradTo, expanded ? 1.2 : 1.8, ease: ease));
            sb.Children.Add(MakeDA(overlayText, "RenderTransform.ScaleX", 0, 1, 0.35, 0.08, backEase));
            sb.Children.Add(MakeDA(overlayText, "RenderTransform.ScaleY", 0, 1, 0.35, 0.08, backEase));
            sb.Children.Add(MakeDA(overlayText, "(UIElement.Effect).(DropShadowEffect.BlurRadius)", 40, expanded ? 100 : 80, expanded ? 0.2 : 0.3, expanded ? 0.08 : 0.4, ease));
            sb.Children.Add(MakeDA(overlayText, "(UIElement.Effect).(DropShadowEffect.BlurRadius)", expanded ? 100 : 80, 30, expanded ? 0.4 : 0.5, expanded ? 0.35 : 0.7, ease));
            sb.Children.Add(MakeDA(shimmer, OpacityProperty, 0, expanded ? 0.85 : 0.8, expanded ? 0.12 : 0.15, 0.5));
            sb.Children.Add(MakeDA(shimmer, "RenderTransform.(TranslateTransform.X)", shimFrom, shimTo, 0.7, 0.5, ease));
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
            // Digit slide travels ~0.6 of the cell's actual rendered height so the
            // flip stays visually consistent at any output-window size.
            double cellH = totalText.ActualHeight;
            if (cellH <= 0) cellH = goalsText.ActualHeight;
            if (cellH <= 0) cellH = behindsText.ActualHeight;
            double slideDist = cellH > 0 ? cellH * 0.6 : ScalePx(60);
            var slideOutDur = TimeSpan.FromSeconds(0.18);
            var slideInDur = TimeSpan.FromSeconds(0.28);
            var timers = isHome ? _homeTimers : _awayTimers;

            void AddSlide(TextBlock tb, string val, double delay, TextBlock alt, bool pop = false)
            {
                var begin = TimeSpan.FromSeconds(flipStart + delay);
                // Slide old value UP and out (off the top of the cell)
                var so = new DoubleAnimation(0, -slideDist, slideOutDur) { BeginTime = begin, EasingFunction = snapEase };
                Storyboard.SetTarget(so, tb); Storyboard.SetTargetProperty(so, new PropertyPath("RenderTransform.(TranslateTransform.Y)")); sb.Children.Add(so);
                // Slide new value UP into place from below — G/B values pop with overshoot
                var inEase = pop ? (IEasingFunction)popEase : bounceEase;
                var si = new DoubleAnimation(slideDist, 0, slideInDur) { BeginTime = begin + slideOutDur, EasingFunction = inEase };
                Storyboard.SetTarget(si, tb); Storyboard.SetTargetProperty(si, new PropertyPath("RenderTransform.(TranslateTransform.Y)")); sb.Children.Add(si);
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

            // For the expanded layout, we wait for any goal preset to finish playing
            // (`flipStart` seconds), THEN play the +N swipe coming UP from the bottom of
            // the total-score cell. At the swipe's reveal moment the total / goals /
            // behinds digits all slide up to their new values together, then the swipe
            // exits upward.
            int delta = goalsChanged ? 6 : (behindsChanged ? 1 : 0);
            bool deferTotalForSwipe = expanded && delta > 0;

            // Reveal moment inside PlayScoreSwipe: rise (0.38s) + hold (~0.40s)
            const double swipeRevealOffset = 0.78;
            double totalDur;

            if (deferTotalForSwipe)
            {
                // 1) Schedule the swipe to start AFTER the goal preset has finished.
                // 2) When the swipe reveals, slide G/B/Total to their new values together
                //    so the digits change in sync with the swipe pulling away.
                ScheduleScoreSwipeWithUpdate(isHome, delta, /*delaySec*/ flipStart, newTotal, totalText, altT);

                double slideAt = swipeRevealOffset; // relative to flipStart
                AddSlide(totalText, newTotal.ToString(), slideAt, altT);
                if (goalsChanged) AddSlide(goalsText, newGoals.ToString(), slideAt, altG, pop: true);
                if (behindsChanged) AddSlide(behindsText, newBehinds.ToString(), slideAt, altB, pop: true);

                // Synced blink right after the slide-in completes
                double blinkTime = flipStart + slideAt + 0.18 + 0.28 + 0.05;
                if (goalsChanged) AddBlink(goalsText, blinkTime);
                if (behindsChanged) AddBlink(behindsText, blinkTime);
                AddBlink(totalText, blinkTime);

                totalDur = blinkTime + 4 * (0.06 + 0.08) + 0.4; // include swipe slide-out
            }
            else
            {
                AddSlide(totalText, newTotal.ToString(), 0.0, altT);
                if (goalsChanged) AddSlide(goalsText, newGoals.ToString(), 0.10, altG, pop: true);
                if (behindsChanged) AddSlide(behindsText, newBehinds.ToString(), 0.10, altB, pop: true);

                double blinkTime = flipStart + 0.16 + 0.18 + 0.28 + 0.05;
                if (goalsChanged) AddBlink(goalsText, blinkTime);
                if (behindsChanged) AddBlink(behindsText, blinkTime);
                AddBlink(totalText, blinkTime);

                totalDur = blinkTime + 4 * (0.06 + 0.08) + 0.1;
            }

            var guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(totalDur) };
            guard.Tick += (_, __) => { guard.Stop(); timers.Remove(guard); if (isHome) _homeAnimating = false; else _awayAnimating = false; };
            timers.Add(guard); guard.Start();
        }

        // ---- Score swipe (+1 / +6) ----

        private void ScheduleScoreSwipe(bool isHome, int delta, double delaySec)
        {
            var timers = isHome ? _homeTimers : _awayTimers;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(delaySec) };
            t.Tick += (_, __) => { t.Stop(); timers.Remove(t); PlayScoreSwipe(isHome, delta); };
            timers.Add(t); t.Start();
        }

        private void ScheduleScoreSwipeWithUpdate(bool isHome, int delta, double delaySec, int newTotal, TextBlock totalText, TextBlock altTotal)
        {
            var timers = isHome ? _homeTimers : _awayTimers;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(delaySec) };
            t.Tick += (_, __) =>
            {
                t.Stop(); timers.Remove(t);
                // Reveal callback is intentionally null: the digit slide animations
                // (Goals/Behinds/Total) are scheduled in AddFlipAnimations to fire at
                // the swipe's reveal moment, so they update in sync visually.
                PlayScoreSwipe(isHome, delta);
            };
            timers.Add(t); t.Start();
        }

        private void PlayScoreSwipe(bool isHome, int delta, Action? onReveal = null)
        {
            var overlay = isHome ? ExpHomeSwipeOverlay : ExpAwaySwipeOverlay;
            var translate = isHome ? ExpHomeSwipeTranslate : ExpAwaySwipeTranslate;
            var swipeText = isHome ? ExpHomeSwipeText : ExpAwaySwipeText;

            swipeText.Text = "+" + delta;

            double h = overlay.ActualHeight > 0 ? overlay.ActualHeight : 200;

            // Reset any prior render transform / opacity animation.
            overlay.RenderTransform = translate;
            overlay.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            overlay.BeginAnimation(OpacityProperty, null);
            overlay.Opacity = 1;
            translate.BeginAnimation(TranslateTransform.YProperty, null);
            translate.X = 0;
            translate.Y = h;

            // Smooth easing curves — soft entry, gentle exit. No sway, scale,
            // or tilt: just a clean upward swipe that feels fluid.
            var entryEase = new CubicEase { EasingMode = EasingMode.EaseOut };
            var exitEase = new CubicEase { EasingMode = EasingMode.EaseIn };

            // 1) Slide up from below to centred.
            var rise = new DoubleAnimation(h, 0, TimeSpan.FromSeconds(0.38)) { EasingFunction = entryEase };
            translate.BeginAnimation(TranslateTransform.YProperty, rise);

            // 2) After a short hold, swipe up off the top and reveal the new score.
            var holdAndExitDelay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.78) };
            holdAndExitDelay.Tick += (_, __) =>
            {
                holdAndExitDelay.Stop();
                onReveal?.Invoke();

                var slideOut = new DoubleAnimation(0, -h, TimeSpan.FromSeconds(0.32)) { EasingFunction = exitEase };
                slideOut.Completed += (_, __) =>
                {
                    overlay.Opacity = 0;
                    translate.BeginAnimation(TranslateTransform.YProperty, null);
                    translate.X = 0;
                    translate.Y = h;
                };
                translate.BeginAnimation(TranslateTransform.YProperty, slideOut);

                var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.30)) { EasingFunction = exitEase };
                overlay.BeginAnimation(OpacityProperty, fade);
            };
            holdAndExitDelay.Start();
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

            // Reset score-change swipe overlay
            var swipeOverlay = isHome ? ExpHomeSwipeOverlay : ExpAwaySwipeOverlay;
            var swipeTr = isHome ? ExpHomeSwipeTranslate : ExpAwaySwipeTranslate;
            swipeOverlay.BeginAnimation(OpacityProperty, null); swipeOverlay.Opacity = 0;
            swipeTr.BeginAnimation(TranslateTransform.YProperty, null); swipeTr.Y = 0;

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
        private System.Windows.Media.Brush AScoringRunGrad => IsExpanded ? (System.Windows.Media.Brush)ExpScoringRunGradient : (System.Windows.Media.Brush)ScoringRunGradient;
        private TextBlock AScoringRunLabel => IsExpanded ? ExpScoringRunLabelText : ScoringRunLabelText;
        private TextBlock AScoringRunDetail => IsExpanded ? ExpScoringRunDetailText : ScoringRunDetailText;
        private TextBlock AScoringRunValue => IsExpanded ? ExpScoringRunValueText : ScoringRunValueText;
        private Border ADroughtBar => IsExpanded ? ExpTeamDroughtBarBorder : TeamDroughtBarBorder;
        private System.Windows.Media.Brush ADroughtGrad => IsExpanded ? (System.Windows.Media.Brush)ExpTeamDroughtGradient : (System.Windows.Media.Brush)TeamDroughtGradient;
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
        private double ScaleUnit => ScoreboardScaleHelper.GetScale(ActualWidth, ActualHeight);
        private double ScalePx(double designValue) => ScoreboardScaleHelper.Scale(designValue, ScaleUnit);
        private double OverlayH(double classicH) => ScalePx(IsExpanded ? ExpandedOverlayHeight : classicH);

        // ==== Stats bar ====

        private bool _statsBarVisible;
        private MatchStats? _currentStats;
        private int _statsRotationIndex;
        private DispatcherTimer? _statsRotationTimer;
        private const double StatsBarHeight = 50;
        private const double StatsRotationSeconds = 5.0;

        private enum OverlayKind { StatsBar, FiveMinWarning, ScorelessTimer, ScoringRun, TeamDrought, WinProbability, LeadChanges, QuarterScores, RecentScores, WeatherStats, Forecast, RainForecast }
        private readonly Queue<OverlayKind> _overlayQueue = new();
        private OverlayKind? _activeOverlay;

        private void EnqueueOverlay(OverlayKind kind)
        {
            if (_activeOverlay == kind || _overlayQueue.Contains(kind)) return;
            _overlayQueue.Enqueue(kind);
            if (_activeOverlay == null)
            {
                ProcessNextOverlay();
            }
            else
            {
                // A new overlay was requested while one is already showing.
                // Smoothly hide the current one — OnOverlayHidden will pick the
                // next one off the queue, giving an exit-before-enter transition.
                HideActiveOverlay();
            }
        }

        private void HideActiveOverlay()
        {
            switch (_activeOverlay)
            {
                case OverlayKind.StatsBar: HideStatsBar(); break;
                case OverlayKind.FiveMinWarning: HideFiveMinuteWarning(); break;
                case OverlayKind.ScorelessTimer: HideScorelessBar(); break;
                case OverlayKind.ScoringRun: HideScoringRunBar(); break;
                case OverlayKind.TeamDrought: HideTeamDroughtBar(); break;
                case OverlayKind.WinProbability: HideWinProbabilityBar(); break;
                case OverlayKind.LeadChanges: HideLeadChangesBar(); break;
                case OverlayKind.QuarterScores: HideQuarterScoresBar(); break;
                case OverlayKind.RecentScores: HideRecentScores(); break;
                case OverlayKind.WeatherStats: HideWeatherStats(); break;
                case OverlayKind.Forecast: HideForecast(); break;
                case OverlayKind.RainForecast: HideRain(); break;
            }
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
                case OverlayKind.QuarterScores: DoShowQuarterScoresBar(); break;
                case OverlayKind.RecentScores: DoShowRecentScores(_pendingRecentScores.events, _pendingRecentScores.quarter); break;
                case OverlayKind.WeatherStats: DoShowWeatherStats(); break;
                case OverlayKind.Forecast: DoShowForecast(); break;
                case OverlayKind.RainForecast: DoShowRain(); break;
            }
        }

        private void OnOverlayHidden()
        {
            OverlayHidden?.Invoke();
            _activeOverlay = null;
            ProcessNextOverlay();
        }

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
                AStatsContentTr.BeginAnimation(TranslateTransform.XProperty, null);
                AStatsContentTr.X = 0;
                AStatsContentTr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(ScalePx(20), 0, TimeSpan.FromSeconds(0.3)) { EasingFunction = ease });
            }
            else
            {
                AStatsContentTr.BeginAnimation(TranslateTransform.XProperty, null);
                AStatsContentTr.BeginAnimation(TranslateTransform.YProperty, null);
                AStatsContentTr.X = 0;
                AStatsContentTr.Y = 0;
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

            // Animate a quick pulse: fade out ? update ? fade in
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.15)) { EasingFunction = ease };
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
            AStatsContentTr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -ScalePx(20), TimeSpan.FromSeconds(0.2)) { EasingFunction = ease });
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
            AStatsContentTr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -ScalePx(20), TimeSpan.FromSeconds(0.2)) { EasingFunction = easeOut });
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

            // No-op if the list is identical to the active one. Routine state
            // refreshes (score adds, clock pauses, periodic pushes) call this
            // method on every tick — restarting the scroll there would reset
            // the marquee position constantly. Only apply when something
            // actually changed.
            if (MarqueeListEquals(_marqueeMessages, incoming))
                return;

            ApplyMarqueeMessages(incoming);
        }

        public void SetMarqueeMessages(IList<MarqueeMessage> messages)
        {
            var incoming = messages == null
                ? new List<MarqueeMessage>()
                : new List<MarqueeMessage>(messages);

            if (MarqueeStyledListEquals(_marqueeStyled, incoming))
                return;

            _marqueeStyled = incoming;
            var asText = new List<string>(incoming.Count);
            foreach (var m in incoming) asText.Add(m.Text ?? string.Empty);

            if (MarqueeListEquals(_marqueeMessages, asText))
            {
                // Text identical — just refresh styling on the active scroll
                ApplyCurrentMarqueeStyle();
                return;
            }

            ApplyMarqueeMessages(asText);
        }

        private List<MarqueeMessage> _marqueeStyled = new();

        private static bool MarqueeListEquals(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            return true;
        }

        private static bool MarqueeStyledListEquals(List<MarqueeMessage> a, List<MarqueeMessage> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!a[i].ContentEquals(b[i])) return false;
            }
            return true;
        }

        private void ApplyCurrentMarqueeStyle()
        {
            if (_marqueeStyled.Count == 0 || _marqueeIndex >= _marqueeStyled.Count) return;
            var m = _marqueeStyled[_marqueeIndex];
            ApplyMarqueeStyle(MarqueeText, MarqueeHighlight, m);
            ApplyMarqueeStyle(ExpMarqueeText, ExpMarqueeHighlight, m);
        }

        private static void ApplyMarqueeStyle(TextBlock text, System.Windows.Shapes.Rectangle? highlight, MarqueeMessage m)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(m.TextColor))
                {
                    var c = (Color)System.Windows.Media.ColorConverter.ConvertFromString(m.TextColor);
                    text.Foreground = new SolidColorBrush(c);
                }
                else
                {
                    text.Foreground = System.Windows.Media.Brushes.White;
                }
            }
            catch { text.Foreground = System.Windows.Media.Brushes.White; }

            if (highlight != null)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(m.HighlightColor))
                    {
                        var c = (Color)System.Windows.Media.ColorConverter.ConvertFromString(m.HighlightColor);
                        highlight.Fill = new SolidColorBrush(c);
                        highlight.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        highlight.Visibility = Visibility.Collapsed;
                    }
                }
                catch { highlight.Visibility = Visibility.Collapsed; }
            }
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

            ApplyCurrentMarqueeStyle();

            if (IsExpanded)
                RunSingleMarquee(ExpMarqueeText, ExpMarqueeCanvas, ExpMarqueeTranslate, ExpMarqueeHighlight, ExpMarqueeHighlightTranslate);
            else
                RunSingleMarquee(MarqueeText, MarqueeCanvas, MarqueeTranslate, MarqueeHighlight, MarqueeHighlightTranslate);
        }

        private void RunSingleMarquee(TextBlock text, Canvas canvas, TranslateTransform tr, System.Windows.Shapes.Rectangle? highlight = null, TranslateTransform? highlightTr = null)
        {
            if (string.IsNullOrWhiteSpace(text.Text)) { _marqueeRunning = false; return; }
            tr.BeginAnimation(TranslateTransform.XProperty, null);
            highlightTr?.BeginAnimation(TranslateTransform.XProperty, null);

            // Scale font size to ~70% of the canvas height so the marquee fits
            // the bottom bar at any output-window size (instead of overflowing
            // when the bar shrinks below the design 34px).
            double canvasH = canvas.ActualHeight;
            if (canvasH > 0)
            {
                double targetFs = Math.Max(8, canvasH * 0.7);
                if (Math.Abs(text.FontSize - targetFs) > 0.5)
                    text.FontSize = targetFs;
            }

            text.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            double tw = text.DesiredSize.Width, th = text.DesiredSize.Height;
            double cw = canvas.ActualWidth > 0 ? canvas.ActualWidth : ScalePx(600);
            double ch = canvas.ActualHeight > 0 ? canvas.ActualHeight : ScalePx(100);
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

            if (highlight != null && highlightTr != null && highlight.Visibility == Visibility.Visible)
            {
                double pad = Math.Max(4, th * 0.1);
                highlight.Width = tw + pad * 2;
                highlight.Height = th + pad * 0.4;
                Canvas.SetTop(highlight, Math.Max(0, (ch - highlight.Height) / 2));
                Canvas.SetLeft(highlight, -pad);
                var hAnim = new DoubleAnimation
                {
                    From = from,
                    To = to,
                    Duration = new Duration(TimeSpan.FromSeconds(seconds))
                };
                highlightTr.BeginAnimation(TranslateTransform.XProperty, hAnim);
            }
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
            MarqueeHighlightTranslate?.BeginAnimation(TranslateTransform.XProperty, null);
            ExpMarqueeHighlightTranslate?.BeginAnimation(TranslateTransform.XProperty, null);
            MarqueeText.Text = "";
            ExpMarqueeText.Text = "";
            // Ensure the highlight bars don't leave residual colour behind
            // when the message that owned them is removed.
            if (MarqueeHighlight is not null)
            {
                MarqueeHighlight.Visibility = Visibility.Collapsed;
                MarqueeHighlight.Width = 0;
            }
            if (ExpMarqueeHighlight is not null)
            {
                ExpMarqueeHighlight.Visibility = Visibility.Collapsed;
                ExpMarqueeHighlight.Width = 0;
            }
            MarqueeText.Foreground = System.Windows.Media.Brushes.White;
            ExpMarqueeText.Foreground = System.Windows.Media.Brushes.White;
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
            ws.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, ScalePx(56), TimeSpan.FromSeconds(0.8)) { RepeatBehavior = RepeatBehavior.Forever });
            var gp = new DoubleAnimation(0.3, 0.8, TimeSpan.FromSeconds(0.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            Storyboard.SetTarget(gp, wg); Storyboard.SetTargetProperty(gp, new PropertyPath(OpacityProperty)); sb.Children.Add(gp);
            var tg = new DoubleAnimation(ScalePx(15), ScalePx(40), TimeSpan.FromSeconds(0.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            Storyboard.SetTarget(tg, wt); Storyboard.SetTargetProperty(tg, new PropertyPath("Effect.BlurRadius")); sb.Children.Add(tg);
            _warningStoryboard = sb; sb.Begin(this, true);
            _warningAutoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _warningAutoHideTimer.Tick += (_, __) => { _warningAutoHideTimer.Stop(); HideFiveMinuteWarning(); };
            _warningAutoHideTimer.Start();
        }

        public void HideFiveMinuteWarning()
        {
            if (!_warningVisible) return;
            _warningVisible = false;
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

        public bool CheckScorelessTimer()
        {
            if (_lastScoreTime == DateTime.MinValue || _scorelessTriggered) return false;
            if ((DateTime.Now - _lastScoreTime).TotalMinutes >= ScorelessShowMinutes)
            {
                _scorelessTriggered = true;
                EnqueueOverlay(OverlayKind.ScorelessTimer);
                return true;
            }

            return false;
        }

        public void ShowScorelessBar() { if (_lastScoreTime == DateTime.MinValue) _lastScoreTime = DateTime.Now.AddMinutes(-1); EnqueueOverlay(OverlayKind.ScorelessTimer); }

        private void DoShowScorelessBar()
        {
            if (_scorelessBarVisible) return;
            _scorelessBarVisible = true;
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

        // ==== Scoring run bar ====

        private bool _scoringRunBarVisible;
        private DispatcherTimer? _scoringRunUpdateTimer;
        private const double ScoringRunBarHeight = 50;
        private string _pendingScoringRunTeam = "";
        private DateTime _pendingScoringRunSince;
        private string _pendingScoringRunValue = "";

        public void ShowScoringRun(string teamName, int runPoints, int concededPoints, DateTime runStartTime, Color teamColor)
        {
            _pendingScoringRunTeam = teamName;
            _pendingScoringRunSince = runStartTime;
            // Show team's run points alongside the opposition score during the run
            // so viewers see both halves of the run (e.g. "24-3").
            _pendingScoringRunValue = $"{runPoints}-{concededPoints}";
            AScoringRunLabel.Text = $"{teamName.ToUpperInvariant()} SCORING RUN";
            AScoringRunValue.Text = _pendingScoringRunValue;
            AScoringRunDetail.Text = "CURRENT SCORING RUN";

            // Tint the bar background and text with the team colour so the run
            // visually belongs to the team that's on it.
            byte r = (byte)Math.Clamp(teamColor.R / 4, 6, 255);
            byte g = (byte)Math.Clamp(teamColor.G / 4, 6, 255);
            byte b = (byte)Math.Clamp(teamColor.B / 4, 6, 255);
            var dark = Color.FromRgb(r, g, b);
            switch (AScoringRunGrad)
            {
                case SolidColorBrush solid:
                    solid.Color = dark;
                    break;
                case LinearGradientBrush lg:
                    var midR = (byte)Math.Clamp(teamColor.R / 2, 12, 255);
                    var midG = (byte)Math.Clamp(teamColor.G / 2, 12, 255);
                    var midB = (byte)Math.Clamp(teamColor.B / 2, 12, 255);
                    var mid = Color.FromRgb(midR, midG, midB);
                    if (lg.GradientStops.Count >= 3)
                    {
                        lg.GradientStops[0].Color = dark;
                        lg.GradientStops[1].Color = mid;
                        lg.GradientStops[lg.GradientStops.Count - 1].Color = dark;
                    }
                    break;
            }
            AScoringRunLabel.Foreground = new SolidColorBrush(teamColor);
            AScoringRunValue.Foreground = new SolidColorBrush(Colors.White);

            EnqueueOverlay(OverlayKind.ScoringRun);
        }

        public void ShowScoringRun(string teamName, int runPoints, int concededPoints, DateTime runStartTime, string colorHex)
        {
            var hex = colorHex.StartsWith("#") ? colorHex : $"#{colorHex}";
            var teamColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            ShowScoringRun(teamName, runPoints, concededPoints, runStartTime, teamColor);
        }

        private void DoShowScoringRunBar()
        {
            if (_scoringRunBarVisible) return;
            _scoringRunBarVisible = true;

            UpdateScoringRunText();
            AScoringRunBar.BeginAnimation(HeightProperty,
                new DoubleAnimation(0, OverlayH(ScoringRunBarHeight), TimeSpan.FromSeconds(0.45))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

            _scoringRunUpdateTimer?.Stop();
            _scoringRunUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _scoringRunUpdateTimer.Tick += (_, __) => UpdateScoringRunText();
            _scoringRunUpdateTimer.Start();

            var ht = new DispatcherTimer { Interval = TimeSpan.FromSeconds(16) };
            ht.Tick += (_, __) => { ht.Stop(); HideScoringRunBar(); };
            ht.Start();
        }

        public void HideScoringRunBar()
        {
            if (!_scoringRunBarVisible) return;
            _scoringRunBarVisible = false;
            _scoringRunUpdateTimer?.Stop();
            _scoringRunUpdateTimer = null;

            var a = new DoubleAnimation(OverlayH(ScoringRunBarHeight), 0, TimeSpan.FromSeconds(0.35))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            a.Completed += (_, __) => OnOverlayHidden();
            AScoringRunBar.BeginAnimation(HeightProperty, a);
        }

        private void UpdateScoringRunText()
        {
            if (_pendingScoringRunSince == DateTime.MinValue) return;
            var elapsed = DateTime.Now - _pendingScoringRunSince;
            var t = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
            AScoringRunDetail.Text = $"OVER {t}";
        }

        // ====  Team Drought bar  ====

        private bool _teamDroughtBarVisible;
        private DispatcherTimer? _teamDroughtUpdateTimer;
        private const double TeamDroughtBarHeight = 50;
        private const double TeamDroughtShowMinutes = 6.0;
        private DateTime _homeLastScoreTime = DateTime.MinValue;
        private DateTime _awayLastScoreTime = DateTime.MinValue;
        private bool _homeDroughtTriggered, _awayDroughtTriggered;
        private string _pendingDroughtLabel = "";
        private DateTime _pendingDroughtSince;
        private Color _pendingDroughtTeamColor;

        public void ResetTeamDroughtTimer(bool isHome)
        {
            if (isHome) { _homeLastScoreTime = DateTime.Now; _homeDroughtTriggered = false; } else { _awayLastScoreTime = DateTime.Now; _awayDroughtTriggered = false; }
            if (_teamDroughtBarVisible) HideTeamDroughtBar();
        }

        public void CheckTeamDrought(string homeName, string awayName, string homeColorHex, string awayColorHex)
        {
            var homeColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString(homeColorHex.StartsWith("#") ? homeColorHex : $"#{homeColorHex}");
            var awayColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString(awayColorHex.StartsWith("#") ? awayColorHex : $"#{awayColorHex}");
            CheckTeamDrought(homeName, awayName, homeColor, awayColor);
        }

        public void ShowTeamDrought(string teamName, DateTime lastScoreTime, string teamColorHex, string _)
        {
            var teamColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString(teamColorHex.StartsWith("#") ? teamColorHex : $"#{teamColorHex}");
            ShowTeamDrought(teamName, lastScoreTime, teamColor);
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
            ADroughtLabel.Foreground = new SolidColorBrush(Colors.White);

            // Tint the drought bar background with the team's colour so the bar
            // visually belongs to the team that is on the drought.
            var teamColor = _pendingDroughtTeamColor;
            byte r = (byte)Math.Clamp(teamColor.R / 4, 6, 255);
            byte g = (byte)Math.Clamp(teamColor.G / 4, 6, 255);
            byte b = (byte)Math.Clamp(teamColor.B / 4, 6, 255);
            var dark = Color.FromRgb(r, g, b);
            switch (ADroughtGrad)
            {
                case SolidColorBrush solid:
                    solid.Color = dark;
                    break;
                case LinearGradientBrush lg:
                    var midR = (byte)Math.Clamp(teamColor.R / 2, 12, 255);
                    var midG = (byte)Math.Clamp(teamColor.G / 2, 12, 255);
                    var midB = (byte)Math.Clamp(teamColor.B / 2, 12, 255);
                    var mid = Color.FromRgb(midR, midG, midB);
                    if (lg.GradientStops.Count >= 3)
                    {
                        lg.GradientStops[0].Color = dark;
                        lg.GradientStops[1].Color = mid;
                        lg.GradientStops[lg.GradientStops.Count - 1].Color = dark;
                    }
                    break;
            }
            UpdateTeamDroughtText();
            ADroughtBar.BeginAnimation(HeightProperty, new DoubleAnimation(0, OverlayH(TeamDroughtBarHeight), TimeSpan.FromSeconds(0.45)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            _teamDroughtUpdateTimer?.Stop(); _teamDroughtUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _teamDroughtUpdateTimer.Tick += (_, __) => UpdateTeamDroughtText();
            _teamDroughtUpdateTimer.Start();

            var ht = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            ht.Tick += (_, __) => { ht.Stop(); HideTeamDroughtBar(); };
            ht.Start();
        }

        public void HideTeamDroughtBar()
        {
            if (!_teamDroughtBarVisible) return;
            _teamDroughtBarVisible = false;
            _teamDroughtUpdateTimer?.Stop(); _teamDroughtUpdateTimer = null;

            var a = new DoubleAnimation(OverlayH(TeamDroughtBarHeight), 0, TimeSpan.FromSeconds(0.35))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
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

        private bool _leadChangesBarVisible;
        private const double LeadChangesBarHeight = 50;
        private int _pendingLeadChanges;
        private DispatcherTimer? _leadChangesAutoHideTimer;

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

        public void SetWinProbColors(string homeHex, string awayHex)
        {
            var home = (Color)System.Windows.Media.ColorConverter.ConvertFromString(homeHex.StartsWith("#") ? homeHex : $"#{homeHex}");
            var away = (Color)System.Windows.Media.ColorConverter.ConvertFromString(awayHex.StartsWith("#") ? awayHex : $"#{awayHex}");
            SetWinProbColors(home, away);
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

        public void ShowLeadChangesBar(int leadChanges)
        {
            _pendingLeadChanges = leadChanges;
            EnqueueOverlay(OverlayKind.LeadChanges);
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
                string arrow = r.HomeDelta > 0 ? "?" : "?";
                string deltaText = $"WIN PROBABILITY   {arrow} {absDelta:F0}%";
                WinProbLabelText.Text = deltaText; ExpWinProbLabelText.Text = deltaText;
            }
            else
            {
                WinProbLabelText.Text = "WIN PROBABILITY"; ExpWinProbLabelText.Text = "WIN PROBABILITY";
            }
        }

        // ==================================================================
        // LEAD CHANGES — AFL / NRL
        // ------------------------------------------------------------------
        // Simple team-colour wash overlay (NO rainbow) ? "LEAD CHANGES:" label
        // fades in ? numerical value scales up with bounce effect ? overlay
        // holds for a bit, then crisply fades out.
        // ==================================================================

        private double BuildLeadChanges(Storyboard sb, bool isHome, bool expanded)
        {
            var primaryColor = isHome ? _homeColor : _awayColor;
            var secondaryColor = isHome ? _homeSecondaryColor : _awaySecondaryColor;
            PrepareOverlay(isHome, expanded, primaryColor, secondaryColor);

            var overlay = GetOverlay(isHome, expanded);
            var overlayText = GetOverlayText(isHome, expanded);
            var gradBar = GetGradBar(isHome, expanded);

            // Use full-opacity team colour overlay (not 0xEE)
            GetOverlayBg(isHome, expanded).Color = Color.FromArgb(0xFF, primaryColor.R, primaryColor.G, primaryColor.B);

            // Tint the gradient bar to team secondary (white?team instead of rainbow)
            var gradBrush = (LinearGradientBrush)gradBar.Fill;
            gradBrush.GradientStops[0].Color = Colors.White;
            gradBrush.GradientStops[1].Color = secondaryColor;
            gradBrush.GradientStops[2].Color = Colors.White;
            gradBrush.GradientStops[3].Color = secondaryColor;
            gradBrush.GradientStops[4].Color = Colors.White;
            gradBrush.GradientStops[5].Color = secondaryColor;
            gradBrush.GradientStops[6].Color = Colors.White;
            gradBrush.GradientStops[7].Color = secondaryColor;

            overlayText.FontSize = expanded ? 100 : 72;
            overlayText.Text = "LEAD CHANGES:";

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            var backEase = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut };
            var decelEase = new CubicEase { EasingMode = EasingMode.EaseOut };
            var (gradFrom, gradTo) = GetGradSweep(overlay, expanded);

            // Overlay slams in instantly
            sb.Children.Add(MakeDA(overlay, OpacityProperty, 0, 1, 0.04));

            // Team-colour gradient wash (0–1.5s, slower sweep)
            sb.Children.Add(MakeDA(gradBar, "RenderTransform.(TranslateTransform.X)", gradFrom, gradTo, 1.5, 0.0, ease));

            // Value: starts at 0 and bounces to target
            double targetValue = _pendingLeadChanges;
            if (targetValue <= 0) targetValue = 1;
            var valueEase = new QuarticEase { EasingMode = EasingMode.EaseOut };
            sb.Children.Add(MakeDA(overlayText, "RenderTransform.ScaleX", 0, 1, 0.4, 0.08, valueEase));
            sb.Children.Add(MakeDA(overlayText, "RenderTransform.ScaleY", 0, 1, 0.4, 0.08, valueEase));
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 1, 0, 0.4, 1.4, ease));

            return 1.8;
        }

        private void DoShowLeadChangesBar()
        {
            if (_leadChangesBarVisible) return;
            _leadChangesBarVisible = true;
            ALeadChangesValue.Text = _pendingLeadChanges.ToString();
            ALeadChangesLabel.Text = "LEAD CHANGES:";
            ALeadChangesBar.BeginAnimation(HeightProperty,
                new DoubleAnimation(0, OverlayH(LeadChangesBarHeight), TimeSpan.FromSeconds(0.45))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            var ht = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            ht.Tick += (_, __) => { ht.Stop(); HideLeadChangesBar(); };
            ht.Start();
        }

        public void HideLeadChangesBar()
        {
            if (!_leadChangesBarVisible) return; _leadChangesBarVisible = false;
            var a = new DoubleAnimation(OverlayH(LeadChangesBarHeight), 0, TimeSpan.FromSeconds(0.35))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            a.Completed += (_, __) => OnOverlayHidden();
            ALeadChangesBar.BeginAnimation(HeightProperty, a);
        }

        // ==================================================================
        // QUARTER SCORES — neon overlay showing each team's tally for the quarter.
        // ==================================================================

        private bool _quarterScoresBarVisible;
        private const double QuarterScoresBarHeight = 80;
        private (int Quarter, int HomeGoals, int HomeBehinds, int AwayGoals, int AwayBehinds)? _pendingQuarterScores;
        private DispatcherTimer? _quarterScoresAutoHideTimer;

        private static string FormatQuarterLabel(int q) => q switch
        {
            1 => "Q1",
            2 => "Q2",
            3 => "Q3",
            4 => "Q4",
            _ => $"Q{q}"
        };

        private void DoShowQuarterScoresBar()
        {
            if (_quarterScoresBarVisible) return;
            var p = _pendingQuarterScores;
            if (p is null) { OnOverlayHidden(); return; }
            _quarterScoresBarVisible = true;

            int hTotal = p.Value.HomeGoals * 6 + p.Value.HomeBehinds;
            int aTotal = p.Value.AwayGoals * 6 + p.Value.AwayBehinds;

            ExpQuarterScoresQtrLabelText.Text = FormatQuarterLabel(p.Value.Quarter);
            ExpQuarterScoresHomeNameText.Text = _homeFullName;
            ExpQuarterScoresAwayNameText.Text = _awayFullName;
            ExpQuarterScoresHomeGoalsText.Text = p.Value.HomeGoals.ToString();
            ExpQuarterScoresHomeBehindsText.Text = p.Value.HomeBehinds.ToString();
            ExpQuarterScoresHomeTotalText.Text = hTotal.ToString();
            ExpQuarterScoresAwayGoalsText.Text = p.Value.AwayGoals.ToString();
            ExpQuarterScoresAwayBehindsText.Text = p.Value.AwayBehinds.ToString();
            ExpQuarterScoresAwayTotalText.Text = aTotal.ToString();

            ExpQuarterScoresBarBorder.BeginAnimation(HeightProperty,
                new DoubleAnimation(0, OverlayH(QuarterScoresBarHeight), TimeSpan.FromSeconds(0.45))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

            _quarterScoresAutoHideTimer?.Stop();
            _quarterScoresAutoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _quarterScoresAutoHideTimer.Tick += (_, __) =>
            {
                _quarterScoresAutoHideTimer?.Stop(); _quarterScoresAutoHideTimer = null;
                HideQuarterScoresBar();
            };
            _quarterScoresAutoHideTimer.Start();
        }

        public void HideQuarterScoresBar()
        {
            if (!_quarterScoresBarVisible) return; _quarterScoresBarVisible = false;
            _quarterScoresAutoHideTimer?.Stop(); _quarterScoresAutoHideTimer = null;
            var a = new DoubleAnimation(OverlayH(QuarterScoresBarHeight), 0, TimeSpan.FromSeconds(0.32))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            a.Completed += (_, __) => OnOverlayHidden();
            ExpQuarterScoresBarBorder.BeginAnimation(HeightProperty, a);
        }

        // ==================================================================
        // RECENT SCORES — right-pushing mask overlay showing only the last
        // five score events as "GOAL" / "BEHIND" with optional quarter splits.
        // The total-score and logo columns remain visible behind it.
        // ==================================================================

        private bool _recentScoresVisible;
        private DispatcherTimer? _recentScoresAutoHideTimer;
        private double _recentScoresTargetWidth;
        private (IReadOnlyList<ScoreEvent>? events, int quarter) _pendingRecentScores;

        private void DoShowRecentScores(IReadOnlyList<ScoreEvent>? events, int currentQuarter)
        {
            // Build the inner list (newest at top). Take last 5 in chronological
            // order, then iterate top?bottom newest-first. Insert quarter split
            // separators wherever adjacent entries (in display order) span a
            // different quarter.
            ExpRecentScoresList.Children.Clear();
            ExpRecentScoresList.RowDefinitions.Clear();

            var src = events is null ? new List<ScoreEvent>() : new List<ScoreEvent>(events);
            int take = Math.Min(5, src.Count);
            var lastN = take == 0
                ? new List<ScoreEvent>()
                : src.GetRange(src.Count - take, take);
            // Newest on top
            lastN.Reverse();

            int? prevQuarter = null;
            int rowIdx = 0;
            foreach (var ev in lastN)
            {
                if (prevQuarter.HasValue && prevQuarter.Value != ev.Quarter)
                {
                    ExpRecentScoresList.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var split = BuildQuarterSplitRow(prevQuarter.Value, ev.Quarter);
                    Grid.SetRow(split, rowIdx++);
                    ExpRecentScoresList.Children.Add(split);
                }
                ExpRecentScoresList.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                var row = BuildRecentScoreRow(ev);
                Grid.SetRow(row, rowIdx++);
                ExpRecentScoresList.Children.Add(row);
                prevQuarter = ev.Quarter;
            }

            // Width: covers the name/goals/behinds portion of the score column.
            // The score cells slide LEFT (toward the logo) so the Total cell
            // ends up pressed against the logo column and stays visible.
            double targetW = ExpScoreColumnWidthEstimate();
            _recentScoresTargetWidth = targetW;

            ExpRecentScoresOverlay.BeginAnimation(WidthProperty, null);

            // Fade name + goal + behind text out so the area behind the overlay
            // is empty — only the Total cell and team logo remain visible
            // (Total is pushed left to sit beside the logo).
            SetScoreCellsHiddenForRecentScores(true);

            if (_recentScoresVisible)
            {
                // Already visible — just refresh content; no slide-in.
                ExpRecentScoresOverlay.Width = targetW;
                AnimatePanelPush(-targetW, TimeSpan.FromSeconds(0.0));
            }
            else
            {
                _recentScoresVisible = true;
                ExpRecentScoresOverlay.Width = 0;
                var dur = TimeSpan.FromSeconds(0.42);
                var slideIn = new DoubleAnimation(0, targetW, dur)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                ExpRecentScoresOverlay.BeginAnimation(WidthProperty, slideIn);
                AnimatePanelPush(-targetW, dur);
            }

            _recentScoresAutoHideTimer?.Stop();
            _recentScoresAutoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            _recentScoresAutoHideTimer.Tick += (_, __) =>
            {
                _recentScoresAutoHideTimer?.Stop(); _recentScoresAutoHideTimer = null;
                HideRecentScores();
            };
            _recentScoresAutoHideTimer.Start();
        }

        public void HideRecentScores()
        {
            if (!_recentScoresVisible) return;
            _recentScoresVisible = false;
            _recentScoresAutoHideTimer?.Stop(); _recentScoresAutoHideTimer = null;
            double from = _recentScoresTargetWidth > 0 ? _recentScoresTargetWidth : ExpRecentScoresOverlay.ActualWidth;
            var dur = TimeSpan.FromSeconds(0.32);
            var slideOut = new DoubleAnimation(from, 0, dur)
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            slideOut.Completed += (_, __) =>
            {
                ExpRecentScoresOverlay.Width = 0;
                if (_activeOverlay == OverlayKind.RecentScores) OnOverlayHidden();
            };
            ExpRecentScoresOverlay.BeginAnimation(WidthProperty, slideOut);
            AnimatePanelPush(0, dur);
            // Restore name/goals/behinds visibility on the same easing as the slide.
            SetScoreCellsHiddenForRecentScores(false, dur);
        }

        private void SetScoreCellsHiddenForRecentScores(bool hidden, TimeSpan? dur = null)
        {
            UIElement[] targets =
            {
                ExpHomeNameText, ExpHomeGoalsText, ExpHomeBehindsText,
                ExpAwayNameText, ExpAwayGoalsText, ExpAwayBehindsText,
                ExpHomeNameSeparator, ExpHomeGoalsBehindsSep, ExpHomeBehindsTotalSep,
                ExpAwayNameSeparator, ExpAwayGoalsBehindsSep, ExpAwayBehindsTotalSep
            };
            double to = hidden ? 0.0 : 1.0;
            if (dur is null || dur.Value <= TimeSpan.Zero)
            {
                foreach (var t in targets)
                {
                    t.BeginAnimation(OpacityProperty, null);
                    t.Opacity = to;
                }
                return;
            }
            var anim = new DoubleAnimation(to, dur.Value)
            { EasingFunction = new CubicEase { EasingMode = hidden ? EasingMode.EaseOut : EasingMode.EaseIn } };
            foreach (var t in targets)
                t.BeginAnimation(OpacityProperty, anim);
        }

        private void AnimatePanelPush(double toX, TimeSpan dur)
        {
            // The recent-scores overlay enters from the right edge of the
            // score column. Every score cell — including Totals — translates
            // LEFT by the overlay width, so the Total ends up sitting against
            // the logo column on the left while the overlay covers the rest.
            TranslateTransform[] targets =
            {
                ExpHomeNameSlide, ExpHomeGoalsSlide, ExpHomeBehindsSlide, ExpHomeTotalSlide,
                ExpAwayNameSlide, ExpAwayGoalsSlide, ExpAwayBehindsSlide, ExpAwayTotalSlide
            };
            if (dur <= TimeSpan.Zero)
            {
                foreach (var t in targets)
                {
                    t.BeginAnimation(TranslateTransform.XProperty, null);
                    t.X = toX;
                }
                return;
            }
            var ease = new CubicEase { EasingMode = toX < 0 ? EasingMode.EaseOut : EasingMode.EaseIn };
            foreach (var t in targets)
            {
                double from = t.X;
                var anim = new DoubleAnimation(from, toX, dur) { EasingFunction = ease };
                t.BeginAnimation(TranslateTransform.XProperty, anim);
            }
        }

        private double ExpScoreColumnWidthEstimate()
        {
            // Width covers everything to the LEFT of the total cell: team-name +
            // Goals + Behinds. The Total cell remains visible to the right of
            // the recent-scores overlay so the running total is never hidden.
            // Score-cells row uses *, 2, *, 2, 2.1*  (~7.3 star units total).
            double scoresColW = ExpScoresColumn.ActualWidth;
            if (scoresColW <= 0)
            {
                double clockW = ExpClockColumn.ActualWidth;
                double logoW = ExpLogoColumn.ActualWidth;
                scoresColW = ExpandedLayout.ActualWidth - clockW - logoW;
            }
            if (scoresColW <= 0)
            {
                scoresColW = ExpandedLayout.ActualWidth * (550.0 / 960.0);
            }
            if (scoresColW <= 0) scoresColW = 700;
            // Leave the total cell visible. Score row star units: 1.15 + 1.15 + 1.7.
            double totalFraction = 1.7 / (1.15 + 1.15 + 1.7);
            double mask = scoresColW * (1.0 - totalFraction);
            return Math.Max(110, mask);
        }

        private FrameworkElement BuildRecentScoreRow(ScoreEvent ev)
        {
            bool isGoal = ev.Type == ScoreType.Goal;
            bool isHome = ev.Team == TeamSide.Home;
            var teamColor = isHome ? _homeColor : _awayColor;
            string label = isGoal ? "GOAL" : "BEHIND";
            var logoSource = isHome ? _homeLogoSource : _awayLogoSource;

            // Darker version of team color for the box background
            var darkBg = Color.FromArgb(
                0xFF,
                (byte)(teamColor.R * 0.18),
                (byte)(teamColor.G * 0.18),
                (byte)(teamColor.B * 0.18));

            var box = new Border
            {
                Margin = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(0),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(teamColor),
                Background = new SolidColorBrush(darkBg),
                HorizontalAlignment = HAlign.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                MinWidth = 0,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 8,
                    ShadowDepth = 0,
                    Color = teamColor,
                    Opacity = 0.85
                }
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.4, GridUnitType.Star) });

            // Left: team logo (or fallback colored block)
            var logoHost = new Border
            {
                Margin = new Thickness(2),
                ClipToBounds = true,
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF))
            };
            if (logoSource is not null)
            {
                logoHost.Child = new System.Windows.Controls.Image
                {
                    Source = logoSource,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HAlign.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
            }
            else
            {
                logoHost.Child = new TextBlock
                {
                    Text = isHome ? "H" : "A",
                    Foreground = Brushes.White,
                    FontFamily = new System.Windows.Media.FontFamily("Stolzl Bold"),
                    FontWeight = FontWeights.Black,
                    FontSize = 14,
                    HorizontalAlignment = HAlign.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
            }
            Grid.SetColumn(logoHost, 0);
            grid.Children.Add(logoHost);

            // Right: GOAL / BEHIND label, scaled to fill
            var viewbox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both,
                Margin = new Thickness(2),
                HorizontalAlignment = HAlign.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch
            };
            var text = new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontFamily = new System.Windows.Media.FontFamily("Stolzl Bold"),
                FontWeight = FontWeights.Black,
                FontSize = 14,
                TextAlignment = TextAlignment.Center,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 6,
                    ShadowDepth = 0,
                    Color = teamColor,
                    Opacity = 0.95
                }
            };
            viewbox.Child = text;
            Grid.SetColumn(viewbox, 1);
            grid.Children.Add(viewbox);

            box.Child = grid;
            return box;
        }

        private static string FormatQuarterBreakLabel(int olderQuarter) => olderQuarter switch
        {
            1 => "QT",
            2 => "HT",
            3 => "3QT",
            4 => "FT",
            _ => $"END Q{olderQuarter}"
        };

        private FrameworkElement BuildQuarterSplitRow(int olderQuarter, int newerQuarter)
        {
            // Display says "newer ? | older" so it reads naturally with newest at top.
            var border = new Border
            {
                Margin = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(2, 0, 2, 0),
                BorderThickness = new Thickness(0, 1, 0, 1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFB, 0xBF, 0x24)) // amber
            };
            var tb = new TextBlock
            {
                Text = $"— {FormatQuarterBreakLabel(olderQuarter)} —",
                Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)),
                FontFamily = new System.Windows.Media.FontFamily("Stolzl Bold"),
                FontWeight = FontWeights.Black,
                FontSize = 9,
                HorizontalAlignment = HAlign.Center,
                TextAlignment = TextAlignment.Center
            };
            border.Child = tb;
            return border;
        }

        // ---- Miscellanous public API for MainWindow integration ----

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

        public void SetFinalsMode(bool enabled) => _finalsMode = enabled;
        public void SetWeatherLocation(string? location) => _weatherLocation = location;

        public void PauseScorelessForBreak() { }
        public void ResumeScorelessFromBreak() { }
        public void ClearBreakGBLock() { }
        public void ExpandGBColumnsForBreak() => ExpandGBColumns();

        public bool CheckAutoTeamDrought() => false;
        public bool GetDroughtTeamIsHome() => true;
        public DateTime GetDroughtSince(bool isHome) => isHome ? _homeLastScoreTime : _awayLastScoreTime;
        public DateTime HomeLastScoreTime => _homeLastScoreTime;
        public DateTime AwayLastScoreTime => _awayLastScoreTime;
        public void ShowLastScoreTime() => ShowScorelessBar();

        public bool CancelInformationalOverlay()
        {
            if (_activeOverlay is null)
                return false;

            ClearOverlayQueue();
            return true;
        }

        public bool CancelScorelessOrDroughtOverlay()
        {
            bool cancelled = false;
            if (_scorelessBarVisible)
            {
                HideScorelessBar();
                cancelled = true;
            }

            if (_teamDroughtBarVisible)
            {
                HideTeamDroughtBar();
                cancelled = true;
            }

            return cancelled;
        }

        public WeatherSnapshot? GetWeatherSnapshot() => _weatherSnapshot;

        /// <summary>
        /// Refreshes the cached weather snapshot and the persistent top-bar
        /// readout WITHOUT enqueuing an overlay. Used by the live weather
        /// service so background updates don't trigger forecast pop-ups.
        /// </summary>
        public void UpdateWeatherSnapshot(WeatherSnapshot snapshot)
        {
            if (snapshot is null) return;
            _weatherSnapshot = snapshot;
            UpdateTopWeather(snapshot);
        }

        public void ShowWeatherForecast(WeatherSnapshot snapshot)
        {
            _weatherSnapshot = snapshot;
            UpdateTopWeather(snapshot);
            _pendingForecast = snapshot;
            EnqueueOverlay(OverlayKind.Forecast);
        }
        public void ShowWeatherStats(WeatherSnapshot snapshot)
        {
            _weatherSnapshot = snapshot;
            UpdateTopWeather(snapshot);
            _pendingWeatherStats = snapshot;
            EnqueueOverlay(OverlayKind.WeatherStats);
        }
        public void ShowRainForecast(WeatherSnapshot snapshot)
        {
            _weatherSnapshot = snapshot;
            UpdateTopWeather(snapshot);
            _pendingRain = snapshot;
            EnqueueOverlay(OverlayKind.RainForecast);
        }

        private WeatherSnapshot? _pendingWeatherStats;
        private WeatherSnapshot? _pendingForecast;
        private WeatherSnapshot? _pendingRain;
        private bool _weatherStatsVisible, _forecastVisible, _rainVisible;
        private DispatcherTimer? _weatherStatsHideTimer, _forecastHideTimer, _rainHideTimer;
        // Weather overlays use the same compact bar size as before; the
        // inner Viewbox content was enlarged so the text fills the bar
        // without leaving negative space.
        private const double WeatherBarHeight = 60;
        private double WeatherOverlayH() => OverlayH(WeatherBarHeight);

        private void DoShowWeatherStats()
        {
            if (_pendingWeatherStats is null) { OnOverlayHidden(); return; }
            var s = _pendingWeatherStats;
            ExpWeatherStatsIcon.Text = string.IsNullOrWhiteSpace(s.Icon) ? "?" : s.Icon;
            ExpWeatherStatsTemp.Text = $"{Math.Round(s.CurrentTemp)}°";
            ExpWeatherStatsDesc.Text = (s.Description ?? "").ToUpperInvariant();
            ExpWeatherStatsFeels.Text = $"{Math.Round(s.FeelsLike)}°";
            ExpWeatherStatsMin.Text = $"{Math.Round(s.DayMin)}°";
            ExpWeatherStatsMax.Text = $"{Math.Round(s.DayMax)}°";

            if (_weatherStatsVisible) return;
            _weatherStatsVisible = true;
            ExpWeatherStatsBorder.BeginAnimation(HeightProperty,
                new DoubleAnimation(0, WeatherOverlayH(), TimeSpan.FromSeconds(0.4))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

            _weatherStatsHideTimer?.Stop();
            _weatherStatsHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _weatherStatsHideTimer.Tick += (_, __) => { _weatherStatsHideTimer?.Stop(); HideWeatherStats(); };
            _weatherStatsHideTimer.Start();
        }

        private void HideWeatherStats()
        {
            if (!_weatherStatsVisible) return;
            _weatherStatsVisible = false;
            _weatherStatsHideTimer?.Stop(); _weatherStatsHideTimer = null;
            var a = new DoubleAnimation(WeatherOverlayH(), 0, TimeSpan.FromSeconds(0.32))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            a.Completed += (_, __) => { if (_activeOverlay == OverlayKind.WeatherStats) OnOverlayHidden(); };
            ExpWeatherStatsBorder.BeginAnimation(HeightProperty, a);
        }

        private void DoShowForecast()
        {
            if (_pendingForecast is null) { OnOverlayHidden(); return; }
            var s = _pendingForecast;
            ExpForecastTiles.Children.Clear();
            int count = Math.Min(4, s.HourlyForecast?.Count ?? 0);
            for (int i = 0; i < count; i++)
            {
                var h = s.HourlyForecast![i];
                var tile = new Border
                {
                    Margin = new Thickness(2, 0, 2, 0),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x67, 0xE8, 0xF9)),
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(Color.FromArgb(0x44, 0x06, 0x1A, 0x2C)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(3, 0, 3, 0),
                    MinWidth = 50
                };
                var sp = new StackPanel { HorizontalAlignment = HAlign.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center };
                sp.Children.Add(new TextBlock
                {
                    Text = h.Time.ToString("h tt", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant(),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                    FontFamily = new System.Windows.Media.FontFamily("Stolzl Bold"),
                    FontWeight = FontWeights.Black,
                    FontSize = 8,
                    LineHeight = 10,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                    HorizontalAlignment = HAlign.Center,
                    Margin = new Thickness(0)
                });
                sp.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(h.Icon) ? "·" : h.Icon,
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.Black,
                    LineHeight = 13,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                    HorizontalAlignment = HAlign.Center,
                    Margin = new Thickness(0)
                });
                sp.Children.Add(new TextBlock
                {
                    Text = $"{Math.Round(h.Temperature)}°",
                    Foreground = Brushes.White,
                    FontFamily = new System.Windows.Media.FontFamily("Stolzl Bold"),
                    FontWeight = FontWeights.Black,
                    FontSize = 12,
                    LineHeight = 14,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                    HorizontalAlignment = HAlign.Center,
                    Margin = new Thickness(0)
                });
                tile.Child = sp;
                ExpForecastTiles.Children.Add(tile);
            }

            if (_forecastVisible) return;
            _forecastVisible = true;
            ExpForecastBorder.BeginAnimation(HeightProperty,
                new DoubleAnimation(0, WeatherOverlayH(), TimeSpan.FromSeconds(0.4))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

            _forecastHideTimer?.Stop();
            _forecastHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            _forecastHideTimer.Tick += (_, __) => { _forecastHideTimer?.Stop(); HideForecast(); };
            _forecastHideTimer.Start();
        }

        private void HideForecast()
        {
            if (!_forecastVisible) return;
            _forecastVisible = false;
            _forecastHideTimer?.Stop(); _forecastHideTimer = null;
            var a = new DoubleAnimation(WeatherOverlayH(), 0, TimeSpan.FromSeconds(0.32))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            a.Completed += (_, __) => { if (_activeOverlay == OverlayKind.Forecast) OnOverlayHidden(); };
            ExpForecastBorder.BeginAnimation(HeightProperty, a);
        }

        private void DoShowRain()
        {
            if (_pendingRain is null) { OnOverlayHidden(); return; }
            var s = _pendingRain;
            ExpRainBars.Children.Clear();
            int count = Math.Min(6, s.HourlyForecast?.Count ?? 0);
            int peak = 0;
            for (int i = 0; i < count; i++)
            {
                var h = s.HourlyForecast![i];
                if (h.PrecipitationProbability > peak) peak = h.PrecipitationProbability;

                var col = new Grid { Margin = new Thickness(3, 0, 3, 0) };
                col.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var trackContainer = new Grid { VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 1, 0, 2) };
                var track = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x33, 0x38, 0xBD, 0xF8)),
                    CornerRadius = new CornerRadius(2)
                };
                trackContainer.Children.Add(track);

                double pct = Math.Clamp(h.PrecipitationProbability / 100.0, 0.0, 1.0);
                var fillRow = new Grid { VerticalAlignment = VerticalAlignment.Bottom };
                fillRow.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0 - pct, GridUnitType.Star) });
                fillRow.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(pct, 0.0001), GridUnitType.Star) });
                var fill = new Border
                {
                    CornerRadius = new CornerRadius(2),
                    Background = new LinearGradientBrush(
                        Color.FromRgb(0x38, 0xBD, 0xF8), Color.FromRgb(0x06, 0x6E, 0xC9), 90)
                };
                Grid.SetRow(fill, 1);
                fillRow.Children.Add(fill);
                trackContainer.Children.Add(fillRow);

                Grid.SetRow(trackContainer, 0);
                col.Children.Add(trackContainer);

                var stack = new StackPanel { HorizontalAlignment = HAlign.Center, Margin = new Thickness(0, 1, 0, 0) };
                stack.Children.Add(new TextBlock
                {
                    Text = $"{h.PrecipitationProbability}%",
                    Foreground = Brushes.White,
                    FontFamily = new System.Windows.Media.FontFamily("Stolzl Bold"),
                    FontSize = 11,
                    FontWeight = FontWeights.Black,
                    HorizontalAlignment = HAlign.Center
                });
                stack.Children.Add(new TextBlock
                {
                    Text = h.Time.ToString("htt", System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant(),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                    FontFamily = new System.Windows.Media.FontFamily("Stolzl Bold"),
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HAlign.Center
                });
                Grid.SetRow(stack, 1);
                col.Children.Add(stack);

                ExpRainBars.Children.Add(col);
            }
            ExpRainPeakValue.Text = $"{peak}%";

            if (_rainVisible) return;
            _rainVisible = true;
            ExpRainBorder.BeginAnimation(HeightProperty,
                new DoubleAnimation(0, WeatherOverlayH(), TimeSpan.FromSeconds(0.4))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

            _rainHideTimer?.Stop();
            _rainHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            _rainHideTimer.Tick += (_, __) => { _rainHideTimer?.Stop(); HideRain(); };
            _rainHideTimer.Start();
        }

        private void HideRain()
        {
            if (!_rainVisible) return;
            _rainVisible = false;
            _rainHideTimer?.Stop(); _rainHideTimer = null;
            var a = new DoubleAnimation(WeatherOverlayH(), 0, TimeSpan.FromSeconds(0.32))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            a.Completed += (_, __) => { if (_activeOverlay == OverlayKind.RainForecast) OnOverlayHidden(); };
            ExpRainBorder.BeginAnimation(HeightProperty, a);
        }

        private void UpdateTopWeather(WeatherSnapshot s)
        {
            if (s is null) return;
            string icon = string.IsNullOrWhiteSpace(s.Icon) ? string.Empty : s.Icon + " ";
            ExpTopWeatherText.Text = $"{icon}{Math.Round(s.CurrentTemp)}°";
        }

        public void ShowQuarterScores(int quarter, int homeGoals, int homeBehinds, int awayGoals, int awayBehinds)
        {
            _pendingQuarterScores = (quarter, homeGoals, homeBehinds, awayGoals, awayBehinds);
            EnqueueOverlay(OverlayKind.QuarterScores);
        }

        public void ShowRecentScores(IReadOnlyList<ScoreEvent> events, int quarter)
        {
            _pendingRecentScores = (events, quarter);
            EnqueueOverlay(OverlayKind.RecentScores);
        }

        public void ResetAllOverlayState()
        {
            ClearOverlayQueue();
            _lastScoreTime = DateTime.MinValue;
            _scorelessTriggered = false;
            _homeLastScoreTime = DateTime.MinValue;
            _awayLastScoreTime = DateTime.MinValue;
            _homeDroughtTriggered = false;
            _awayDroughtTriggered = false;
            _pendingScoringRunTeam = string.Empty;
            _pendingScoringRunValue = string.Empty;
            _pendingScoringRunSince = DateTime.MinValue;
            _pendingDroughtLabel = string.Empty;
            _pendingDroughtSince = DateTime.MinValue;
            _pendingWinProb = null;
            _pendingLeadChanges = 0;
            _lastScoreEventTime = DateTime.MinValue;
            _gbAutoShowTriggered = false;

            // Clear any +1/+6 score-change swipe that may still be visible
            ExpHomeSwipeOverlay.BeginAnimation(OpacityProperty, null); ExpHomeSwipeOverlay.Opacity = 0;
            ExpAwaySwipeOverlay.BeginAnimation(OpacityProperty, null); ExpAwaySwipeOverlay.Opacity = 0;
            ExpHomeSwipeTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            ExpAwaySwipeTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        }

        public void ClearOverlayQueue()
        {
            _overlayQueue.Clear();
            _activeOverlay = null;

            _statsBarVisible = false;
            _scorelessBarVisible = false;
            _scoringRunBarVisible = false;
            _teamDroughtBarVisible = false;
            _winProbBarVisible = false;
            _leadChangesBarVisible = false;
            _quarterScoresBarVisible = false;
            _recentScoresVisible = false;

            _statsRotationTimer?.Stop();
            _statsRotationTimer = null;
            _scorelessUpdateTimer?.Stop();
            _scorelessUpdateTimer = null;
            _scoringRunUpdateTimer?.Stop();
            _scoringRunUpdateTimer = null;
            _teamDroughtUpdateTimer?.Stop();
            _teamDroughtUpdateTimer = null;
            _leadChangesAutoHideTimer?.Stop();
            _leadChangesAutoHideTimer = null;
            _quarterScoresAutoHideTimer?.Stop();
            _quarterScoresAutoHideTimer = null;
            _recentScoresAutoHideTimer?.Stop();
            _recentScoresAutoHideTimer = null;

            ClearHeight(StatsBarBorder); ClearHeight(ExpStatsBarBorder);
            ClearHeight(ScorelessBarBorder); ClearHeight(ExpScorelessBarBorder);
            ClearHeight(ScoringRunBarBorder); ClearHeight(ExpScoringRunBarBorder);
            ClearHeight(TeamDroughtBarBorder); ClearHeight(ExpTeamDroughtBarBorder);
            ClearHeight(WinProbBarBorder); ClearHeight(ExpWinProbBarBorder);
            ClearHeight(LeadChangesBarBorder); ClearHeight(ExpLeadChangesBarBorder);
            ClearHeight(ExpQuarterScoresBarBorder);
            ExpRecentScoresOverlay.BeginAnimation(WidthProperty, null);
            ExpRecentScoresOverlay.Width = 0;
            AnimatePanelPush(0, TimeSpan.Zero);
        }

        private static void ClearHeight(Border b)
        {
            b.BeginAnimation(HeightProperty, null);
            b.Height = 0;
        }
    }
}


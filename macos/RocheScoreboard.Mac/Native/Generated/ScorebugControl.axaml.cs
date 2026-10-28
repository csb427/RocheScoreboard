using RocheScoreboard.Mac.Production;
// Generated from Views/ScorebugControl.xaml.cs; edit translator/adapters.
// Source SHA-256: 4c418415313f01557dccb49f629633c718c36c2f3522503706ee2ff02423c325
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
using System.IO;
using Avalonia.Controls.Documents;
using Avalonia.Media.Imaging;
using Avalonia.Controls.Shapes;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Color = Avalonia.Media.Color;
using Brushes = Avalonia.Media.Brushes;
using HAlign = Avalonia.Layout.HorizontalAlignment;

namespace Roche_Scoreboard.Views
{
    public partial class ScorebugControl : Avalonia.Controls.UserControl
    {
        private Color _homeColor = (Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString("#0A2A6A");
        private Color _awayColor = (Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString("#7A1A1A");

        private const double MarqueePixelsPerSecond = 82;
        private readonly DispatcherTimer _wallClockTimer;
        private bool _quarterLabelsSubscribed;

        private bool _homeAnimating;
        private bool _awayAnimating;
        private Storyboard? _homeScoreSb;
        private Storyboard? _awayScoreSb;
        private readonly List<DispatcherTimer> _homeTimers = new();
        private readonly List<DispatcherTimer> _awayTimers = new();
        private ScorebugLayout _currentLayout = ScorebugLayout.Expanded;
        private const double ExpandedWarningHeight = 46;

        // Goal/Behind column expand/collapse state
        private string _homeFullName = "HOME TEAM";
        private bool _teamsApplied;
        private bool _teamColoursApplied;
        private bool _logosApplied;
        private bool _layoutApplied;
        private bool _finalsModeApplied;
        private bool _hideTeamNamesApplied;
        private string? _appliedHomeLogoPath;
        private string? _appliedAwayLogoPath;
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
        private bool _hideTeamNames;
        private double _teamNameRevealProgress = 1;
        private DispatcherTimer? _teamNamePanelAnimationTimer;
        private DateTime _teamNamePanelAnimationStarted;
        private double _teamNamePanelAnimationFrom;
        private double _teamNamePanelAnimationTo = 1;
        private static readonly TimeSpan OverlayTransitionDuration = BroadcastMotion.OverlayDuration;
        private static readonly TimeSpan TeamStoryTransitionDuration = TimeSpan.FromMilliseconds(860);
        private static readonly TimeSpan TeamNamePanelAnimationDuration = OverlayTransitionDuration;
        private bool _overlayLayoutMotionActive;
        private bool _overlayResponsiveRefreshPending;
        private int _overlayLayoutMotionGeneration;
        private string? _weatherLocation;
        private WeatherSnapshot? _weatherSnapshot;

        public event Action? OverlayHidden;
        public bool IsScoreAnimating => _homeAnimating || _awayAnimating;
        public bool IsOverlayActive => _activeOverlay is not null;
        public bool IsTickerActive => _marqueeMessages.Count > 0;
        public bool IsRetroLayout => _currentLayout == ScorebugLayout.Retro;

        private DateTime? _overlayClockPausedAt;
        private DateTime OverlayClockNow => _overlayClockPausedAt ?? DateTime.Now;

        public void NotifyClockStopped()
        {
            _overlayClockPausedAt ??= DateTime.Now;
        }

        public void NotifyClockStarted()
        {
            // A team can be scoreless from the opening bounce; waiting for its
            // first score before starting the drought clock meant that the most
            // meaningful droughts were never detected.
            _homeLastScoreTime = _homeLastScoreTime == DateTime.MinValue ? DateTime.Now : _homeLastScoreTime;
            _awayLastScoreTime = _awayLastScoreTime == DateTime.MinValue ? DateTime.Now : _awayLastScoreTime;
            _lastScoreTime = _lastScoreTime == DateTime.MinValue ? DateTime.Now : _lastScoreTime;
            if (_overlayClockPausedAt is not DateTime pausedAt) return;

            TimeSpan pausedFor = DateTime.Now - pausedAt;
            ShiftOverlayTimestamp(ref _lastScoreTime, pausedFor);
            ShiftOverlayTimestamp(ref _homeLastScoreTime, pausedFor);
            ShiftOverlayTimestamp(ref _awayLastScoreTime, pausedFor);
            ShiftOverlayTimestamp(ref _lastScoreEventTime, pausedFor);
            ShiftOverlayTimestamp(ref _pendingDroughtSince, pausedFor);
            _overlayClockPausedAt = null;
        }

        private static void ShiftOverlayTimestamp(ref DateTime value, TimeSpan by)
        {
            if (value != DateTime.MinValue) value = value.Add(by);
        }

        public ScorebugLayout CurrentLayout => _currentLayout;

        internal (double GoalsBehinds, double Total) GetScoreFontSizesForQa()
            => (ExpHomeGoalsText.FontSize, ExpHomeTotalText.FontSize);

        internal (double Home, double Away) GetTeamNamePanelHeightsForQa()
            => (ExpHomeNameRow.NativeHeight(), ExpAwayNameRow.NativeHeight());

        internal string GetCoreStateDiagnosticsForQa()
            => $"layout={_currentLayout}; top={ExpTopInfoRow.NativeHeight():0.##}; " +
               $"teams={ExpHomeNameText.Text}/{ExpAwayNameText.Text}; " +
               $"score={ExpHomeGoalsText.Text}.{ExpHomeBehindsText.Text}.{ExpHomeTotalText.Text}/" +
               $"{ExpAwayGoalsText.Text}.{ExpAwayBehindsText.Text}.{ExpAwayTotalText.Text}; " +
               $"quarter={ExpQuarterNumberText.Text}; clock={ExpClockText.Text}; " +
               $"wall={ExpPanelWallTimeText.Text}{ExpPanelWallTimeSuffixText.Text}; " +
               $"ticker={IsTickerActive}; overlay={IsOverlayActive}; compact={_compactClockActive}";

        internal string GetOverlayStateDiagnosticsForQa()
            => $"active={_activeOverlay?.ToString() ?? "none"}; queued={_overlayQueue.Count}; " +
               $"exiting={_overlayIsExiting}; gap={_overlayTransitionGap}; warning={_warningVisible}; " +
               $"warning-height={ExpWarningBanner.NativeHeight():0.##}; warning-opacity={ExpWarningBanner.Opacity:0.##}; " +
               $"team-story={_teamStoryVisible}/{(_teamStoryIsHome ? "home" : "away")}; " +
               $"story-scale={ExpTeamStoryReveal.ScaleY:0.###}; opponent-y={GetTeamStoryOpponentOffset():0.##}; " +
               $"story-direction={ExpTeamStoryDirectionIndicator.Text}/{ExpTeamStoryDirectionHost.VerticalAlignment}; " +
               $"drought-label={ExpTeamDroughtLabelText.Text}; " +
               $"drought-lane/text={ExpTeamDroughtLabelLane.NativeWidth():0.##}/{ExpTeamDroughtLabelText.NativeWidth():0.##}; " +
               $"home-name-lane/text={ExpHomeNameHost.NativeWidth():0.##}/{ExpHomeNameText.NativeWidth():0.##}; " +
               $"away-name-lane/text={ExpAwayNameHost.NativeWidth():0.##}/{ExpAwayNameText.NativeWidth():0.##}";

        internal void FreezeWallClockForQa(DateTime value)
        {
            _wallClockTimer.Stop();
            string time = value.ToString("h:mm tt");
            TopTimeText.Text = time;
            ExpTopTimeText.Text = time;
            ExpTopClockText.Text = time;
            SetIntegratedWallTime(value);
            ExpCompactWallTimeText.Text = time;
            ExpTopDateText.Text = value.ToString("ddd d MMM");
        }

        public ScorebugControl()
        {
            NativePointer.EnsureInitialized(); ProductionNames.BeginLoad(this); try { InitializeComponent(); } finally { ProductionNames.CompleteLoad(this); }
            ApplyGradient();

            var now = BroadcastClock.WallTime();
            TopTimeText.Text = now;
            ExpTopTimeText.Text = now;
            ExpTopClockText.Text = now;
            SetIntegratedWallTime(DateTime.Now);
            ExpCompactWallTimeText.Text = now;
            ExpTopDateText.Text = DateTime.Now.ToString("ddd d MMM");
            ExpTopWeatherText.Text = "--°";

            _wallClockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _wallClockTimer.Tick += (_, __) =>
            {
                var t = BroadcastClock.WallTime();
                TopTimeText.Text = t;
                ExpTopTimeText.Text = t;
                ExpTopClockText.Text = t;
                SetIntegratedWallTime(DateTime.Now);
                ExpCompactWallTimeText.Text = t;
                ExpTopDateText.Text = DateTime.Now.ToString("ddd d MMM");
            };
            _wallClockTimer.Start();

            MarqueeCanvas.SizeChanged += (_, __) => RestartMarqueeIfNeeded();
            ExpMarqueeCanvas.SizeChanged += (_, __) => RestartMarqueeIfNeeded();
            ExpHomeScoreGrid.SizeChanged += (_, __) => QueueExpandedResponsiveLayout();
            ExpAwayScoreGrid.SizeChanged += (_, __) => QueueExpandedResponsiveLayout();
            ExpandedLayout.SizeChanged += (_, __) => QueueExpandedResponsiveLayout();
            NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsVisibleProperty,true,(_, __) =>
            {
                if (IsVisible)
                {
                    QueueExpandedResponsiveLayout();
                    RestartMarqueeIfNeeded();
                }
            });
            Loaded += (_, __) =>
            {
                _wallClockTimer.Start();
                if (!_quarterLabelsSubscribed)
                {
                    QuarterLabelService.LabelsChanged += OnQuarterLabelsChanged;
                    _quarterLabelsSubscribed = true;
                }
                StartActiveMarquee();
                QueueExpandedResponsiveLayout();
            };

            // Live custom quarter / break label refresh: when the operator
            // changes a label in settings the scorebug should reflect it
            // immediately without waiting for the next quarter change.
            QuarterLabelService.LabelsChanged += OnQuarterLabelsChanged;
            _quarterLabelsSubscribed = true;
            Unloaded += (_, __) =>
            {
                _wallClockTimer.Stop();
                if (_quarterLabelsSubscribed)
                {
                    QuarterLabelService.LabelsChanged -= OnQuarterLabelsChanged;
                    _quarterLabelsSubscribed = false;
                }
            };
        }

        private void SetIntegratedWallTime(DateTime value)
        {
            ExpPanelWallTimeText.Text = value.ToString("h:mm");
            ExpPanelWallTimeSuffixText.Text = value.ToString("tt");
        }

        /// <summary>
        /// Fits against supported maximum values, never the current value, so
        /// one- and two-digit G/B values share a size and 1/99/999 totals do too.
        /// </summary>
        private void UpdateExpandedScoreTypography()
        {
            if (!IsLoaded) return;

            double gbSize = Math.Min(
                FitScoreFont(ExpHomeGoalsCell, "88", ExpHomeGoalsText),
                FitScoreFont(ExpAwayGoalsCell, "88", ExpAwayGoalsText));
            gbSize = Math.Min(gbSize, Math.Min(
                FitScoreFont(ExpHomeBehindsCell, "88", ExpHomeBehindsText),
                FitScoreFont(ExpAwayBehindsCell, "88", ExpAwayBehindsText)));
            double totalSize = Math.Min(
                FitScoreFont(ExpHomeTotalCell, "888", ExpHomeTotalText),
                FitScoreFont(ExpAwayTotalCell, "888", ExpAwayTotalText));

            if (gbSize > 0)
                ExpHomeGoalsText.FontSize = ExpHomeBehindsText.FontSize = ExpAwayGoalsText.FontSize = ExpAwayBehindsText.FontSize = gbSize;
            if (totalSize > 0)
                ExpHomeTotalText.FontSize = ExpAwayTotalText.FontSize = totalSize;
        }

        private static double FitScoreFont(Control cell, string worstCase, TextBlock sample)
        {
            double width = Math.Max(0, cell.NativeWidth() - 6);
            double height = Math.Max(0, cell.NativeHeight() - 4);
            if (width <= 0 || height <= 0) return 0;

            const double probeSize = 100;
            Typeface typeface = new(sample.FontFamily, sample.FontStyle, sample.FontWeight, sample.FontStretch);
            NativeFormattedText measured = new(
                worstCase,
                System.Globalization.CultureInfo.InvariantCulture,
                Avalonia.Media.FlowDirection.LeftToRight,
                typeface,
                probeSize,
                Brushes.White,
                1);
            return Math.Floor(Math.Min(width / measured.WidthIncludingTrailingWhitespace,
                                       height / measured.Height) * probeSize * 0.985);
        }


        private void OnQuarterLabelsChanged()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(new Action(OnQuarterLabelsChanged));
                return;
            }
            RefreshQuarterLabel();
        }

        // The expanded GOAL/LEAD-CHANGE overlays bleed left across the logo
        // column so the team-colour wash visually unifies the score and logo
        // areas. The bleed must equal the logo column's actual width ° when the
        // output window resizes the column changes width and a static -240
        // margin would leave a gap or overshoot. Recompute on layout changes.
        private void UpdateExpOverlayBleed()
        {
            double logoW = ExpLogoColumn.NativeWidth();
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
        private bool _expandedResponsiveLayoutQueued;

        private void QueueExpandedResponsiveLayout()
        {
            if (_overlayLayoutMotionActive)
            {
                _overlayResponsiveRefreshPending = true;
                return;
            }
            if (_expandedResponsiveLayoutQueued) return;
            _expandedResponsiveLayoutQueued = true;
            Dispatcher.UIThread.Post(new Action(() =>
            {
                _expandedResponsiveLayoutQueued = false;
                if (!IsLoaded || !IsVisible || ExpandedLayout.NativeWidth() <= 0 || ExpandedLayout.NativeHeight() <= 0)
                    return;

                ApplyExpandedResponsiveProportions();
                UpdateCompactClockMode();
                UpdateBottomBarVisibility();
                UpdateCompactOverlayDetails();
                UpdateExpOverlayBleed();
                UpdateExpandedScoreTypography();
                UpdateExpandedTeamNamePanning();
            }), DispatcherPriority.Background);
        }

        private void ApplyExpandedResponsiveProportions()
        {
            double width = ExpandedLayout.NativeWidth();
            double height = ExpandedLayout.NativeHeight();

            // On physically narrow outputs the date is the least important
            // top-bar item and forces both weather and wall time to shrink.
            // Remove it below 320px and reuse every pixel for the two values
            // spectators actually need to read. Wider/default layouts retain
            // the complete three-part information bar.
            bool simplifyTopInfo = !IsDistance && width < 320;
            ExpTopWeatherColumn.Width = new GridLength(simplifyTopInfo ? 0.85 : 1, GridUnitType.Star);
            ExpTopClockColumn.Width = new GridLength(simplifyTopInfo ? 1.15 : 1, GridUnitType.Star);
            ExpTopDateColumn.Width = simplifyTopInfo ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            ExpTopDateHost.IsVisible = simplifyTopInfo ? false : true;

            // Keep fixed design floors from consuming a disproportionate share
            // of compact outputs. The star ratios preserve a substantial clock
            // and logo while reserving most of the width for score numerals.
            ExpClockColumn.MinWidth = _compactClockActive ? 0 : Math.Clamp(width * 0.12, 38, 80);
            ExpLogoColumn.MinWidth = Math.Clamp(width * 0.09, 30, 50);
            bool narrowDistance = IsDistance && width < 340;
            ExpClockColumn.Width = _compactClockActive
                ? new GridLength(0)
                : new GridLength(IsDistance ? (narrowDistance ? 230 : 225) : 225, GridUnitType.Star);
            ExpLogoColumn.Width = new GridLength(IsDistance ? (narrowDistance ? 160 : 170) : 165, GridUnitType.Star);
            ExpScoresColumn.Width = new GridLength(IsDistance ? (narrowDistance ? 620 : 625) : 610, GridUnitType.Star);
            // Preserve a large drought clock without letting it consume the
            // explanatory phrase on compact physical scoreboards.
            ExpTeamStoryTimerHost.Width = Math.Clamp(width * 0.28, 84, 116);
            if (_recentScoresDrawerProgress > 0.001)
            {
                if (!_compactClockActive && _recentScoresProtectedClockWidth > 1)
                    ExpClockColumn.Width = new GridLength(_recentScoresProtectedClockWidth);
                if (_recentScoresProtectedLogoWidth > 1)
                    ExpLogoColumn.Width = new GridLength(_recentScoresProtectedLogoWidth);
            }

            // A 28px minimum is useful at full size but used almost a quarter of
            // a 120px-high scoreboard. Scale that safety floor with height.
            ExpTopInfoRow.MinHeight = IsDistance ? 0 : Math.Clamp(height * 0.145, 16, 32);

            // Short screens devote more of each team panel to the score line.
            // Both teams use identical weights, so typography remains stable.
            double heightProgress = Math.Clamp((height - 110) / 120, 0, 1);
            double nameWeight = (IsDistance ? 1.08 : 0.97) + (0.50 * heightProgress);
            double scoreWeight = 2.85 - (0.35 * heightProgress);
            double revealProgress = _hideTeamNames ? 0 : _teamNameRevealProgress;
            ExpHomeNameRow.Height = revealProgress <= 0.001
                ? new GridLength(0)
                : new GridLength(nameWeight * revealProgress, GridUnitType.Star);
            ExpAwayNameRow.Height = revealProgress <= 0.001
                ? new GridLength(0)
                : new GridLength(nameWeight * revealProgress, GridUnitType.Star);
            ExpHomeScoreRow.Height = new GridLength(scoreWeight, GridUnitType.Star);
            ExpAwayScoreRow.Height = new GridLength(scoreWeight, GridUnitType.Star);
            ExpHomeNameSeparator.Opacity = revealProgress;
            ExpAwayNameSeparator.Opacity = revealProgress;
            ExpHomeNameSeparator.IsVisible = revealProgress <= 0.001
                ? false
                : true;
            ExpAwayNameSeparator.IsVisible = revealProgress <= 0.001
                ? false
                : true;

            // Fixed inner padding becomes expensive at small widths. Scale it
            // continuously rather than jumping at a breakpoint.
            double scorePadding = Math.Clamp(width / 280, 1, 4);
            var scoreMargin = new Thickness(scorePadding);
            ExpHomeGoalsCell.Margin = scoreMargin;
            ExpHomeBehindsCell.Margin = scoreMargin;
            ExpAwayGoalsCell.Margin = scoreMargin;
            ExpAwayBehindsCell.Margin = scoreMargin;

            double logoPadding = Math.Clamp(width / 160, 2, 6);
            var logoMargin = new Thickness(logoPadding);
            ExpHomeLogoHost.Margin = logoMargin;
            ExpAwayLogoHost.Margin = logoMargin;
        }

        private void UpdateCompactClockMode()
        {
            double w = ExpandedLayout.NativeWidth();
            double h = ExpandedLayout.NativeHeight();
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
                ExpClockPanel.IsVisible = false;
                ExpMarqueeCanvas.IsVisible = false;
                ExpCompactClockBar.IsVisible = true;
                StopAllMarquees();
            }
            else
            {
                double width = ExpandedLayout.NativeWidth();
                ExpClockColumn.MinWidth = Math.Clamp(width * 0.12, 38, 80);
                bool narrowDistance = IsDistance && width < 340;
                ExpClockColumn.Width = new GridLength(IsDistance ? (narrowDistance ? 230 : 225) : 225, GridUnitType.Star);
                ExpClockPanel.IsVisible = true;
                ExpCompactClockBar.IsVisible = false;
                ExpMarqueeCanvas.IsVisible = true;
                StartActiveMarquee();
            }

            UpdateBottomBarVisibility();
        }

        private void UpdateCompactOverlayDetails()
        {
            ExpRecentScoresRecentText.IsVisible = true;
            ExpRecentScoresRecentText.FontSize = 14;
            ExpRecentScoresTitleText.FontSize = 14;

            bool narrowOutput = ExpandedLayout.NativeWidth() > 0 && ExpandedLayout.NativeWidth() < 340;
            ExpRecentScoresHeader.IsVisible = true;
            ExpRecentScoresSpacerColumn.Width = narrowOutput
                ? new GridLength(0)
                : new GridLength(14);
            double valueColumnWidth = Math.Clamp(ExpandedLayout.NativeWidth() * 0.18, 40, 70);
            ExpStatsHomeValueColumn.Width = new GridLength(valueColumnWidth);
            ExpStatsAwayValueColumn.Width = new GridLength(valueColumnWidth);
            ExpWinProbHomeValueColumn.Width = new GridLength(valueColumnWidth);
            ExpWinProbAwayValueColumn.Width = new GridLength(valueColumnWidth);
            ExpStatsBarContent.Margin = narrowOutput ? new Thickness(6, 2, 6, 2) : new Thickness(12, 2, 12, 2);
            ExpWinProbContent.Margin = narrowOutput ? new Thickness(6, 2, 6, 2) : new Thickness(12, 2, 12, 2);
            ExpScoringRunContent.Margin = narrowOutput ? new Thickness(7, 3, 7, 3) : new Thickness(14, 5, 14, 5);
            ExpRecentScoresContent.Margin = narrowOutput ? new Thickness(6, 3, 6, 3) : new Thickness(12, 4, 12, 4);
        }

        // The bottom message bar is hidden by default to reclaim vertical space
        // for the scores/names/clock on outdoor scoreboards. It is only made
        // visible when a contextual ticker message is queued, or when compact
        // clock mode needs the row to host the quarter+clock fallback.
        private bool _bottomBarVisible;
        private bool _bottomBarAnimating;
        private bool _bottomBarTargetVisible;

        private void UpdateBottomBarVisibility()
        {
            if (ExpBottomBarRow is null || ExpBottomBarBorder is null || ExpBottomBarSlide is null) return;
            bool hasMessages = _marqueeMessages.Count > 0;
            bool needsRow = hasMessages || _compactClockActive;
            if (needsRow == _bottomBarTargetVisible && (_bottomBarAnimating || needsRow == _bottomBarVisible))
            {
                ExpBottomBarRow.Height = GridLength.Auto;
                ExpBottomBarRow.MinHeight = 0;
                return;
            }

            _bottomBarTargetVisible = needsRow;

            if (needsRow)
            {
                ExpBottomBarRow.Height = GridLength.Auto;
                ExpBottomBarRow.MinHeight = 0;
            }

            AnimateBottomBar(show: needsRow, onCompleted: () =>
            {
                _bottomBarVisible = needsRow;
                _bottomBarAnimating = false;
                if (!needsRow)
                {
                    ExpBottomBarBorder.Height = 0;
                    ExpBottomBarRow.Height = GridLength.Auto;
                    ExpBottomBarRow.MinHeight = 0;
                }

                if (_bottomBarTargetVisible != _bottomBarVisible)
                {
                    UpdateBottomBarVisibility();
                }
                else if (needsRow && _marqueeMessages.Count > 0)
                {
                    // Measure ticker typography only after the row reaches its
                    // final height. Measuring halfway through the entrance was
                    // pinning small text to the temporary top edge.
                    Dispatcher.UIThread.Post(new Action(StartActiveMarquee), DispatcherPriority.Background);
                }
            });
        }

        private void AnimateBottomBar(bool show, Action? onCompleted = null)
        {
            if (ExpBottomBarBorder is null || ExpBottomBarSlide is null) return;

            const double slideDistance = 34;
            TimeSpan dur = OverlayTransitionDuration;
            IEasingFunction ease = CreateOverlayTransitionEase();
            double currentOpacity = ExpBottomBarBorder.Opacity;
            double currentY = ExpBottomBarSlide.Y;
            double targetOpacity = show ? 1.0 : 0.0;
            double targetY = show ? 0.0 : slideDistance;
            double currentHeight = double.IsNaN(ExpBottomBarBorder.Height)
                ? ExpBottomBarBorder.NativeHeight()
                : ExpBottomBarBorder.Height;
            if (!show && currentHeight <= 0.001)
                currentHeight = 46;
            double targetHeight = show ? 46.0 : 0.0;

            ExpBottomBarBorder.BeginAnimation(Control.OpacityProperty, null);
            ExpBottomBarBorder.BeginAnimation(Control.HeightProperty, null);
            ExpBottomBarSlide.BeginAnimation(TranslateTransform.YProperty, null);
            _bottomBarAnimating = true;

            if (show && currentOpacity <= 0.001)
            {
                ExpBottomBarBorder.Opacity = 0;
                ExpBottomBarBorder.Height = 0;
                ExpBottomBarSlide.Y = slideDistance;
                currentOpacity = 0;
                currentY = slideDistance;
                currentHeight = 0;
            }

            var fade = new DoubleAnimation
            {
                From = currentOpacity,
                To = targetOpacity,
                Duration = dur,
                EasingFunction = ease
            };
            var slide = new DoubleAnimation
            {
                From = currentY,
                To = targetY,
                Duration = dur,
                EasingFunction = ease
            };
            var resize = new DoubleAnimation
            {
                From = currentHeight,
                To = targetHeight,
                Duration = dur,
                EasingFunction = ease,
                FillBehavior = FillBehavior.Stop
            };

            slide.Completed += (_, _) =>
            {
                ExpBottomBarBorder.BeginAnimation(Control.HeightProperty, null);
                ExpBottomBarBorder.Height = targetHeight;
                onCompleted?.Invoke();
            };

            ExpBottomBarBorder.BeginAnimation(Control.OpacityProperty, fade);
            ExpBottomBarBorder.BeginAnimation(Control.HeightProperty, resize);
            ExpBottomBarSlide.BeginAnimation(TranslateTransform.YProperty, slide);
        }

        // ---- Layout switching ----

        public void SetLayout(ScorebugLayout layout)
        {
            // Both public choices share the stable responsive scorebug engine.
            // Distance removes the information bar and reinvests its height in
            // team names and scores; legacy enum values map safely to Classic.
            layout = layout == ScorebugLayout.Distance
                ? ScorebugLayout.Distance
                : ScorebugLayout.Expanded;
            if (_layoutApplied && _currentLayout == layout) return;
            ClearOverlayQueue();

            _currentLayout = layout;
            _layoutApplied = true;
            ClassicLayout.IsVisible = false;
            ExpandedLayout.IsVisible = true;

            ApplyDistanceLayoutMode();
            ApplyGradient();
            RestartMarqueeIfNeeded();
            QueueExpandedResponsiveLayout();
        }

        private void ApplyDistanceLayoutMode()
        {
            bool distance = IsDistance;
            ExpTopInfoBar.IsVisible = distance ? false : true;
            ExpTopInfoRow.Height = distance ? new GridLength(0) : new GridLength(58, GridUnitType.Star);
            ExpTopInfoRow.MinHeight = distance ? 0 : 28;
            ExpPanelWallTimeRow.Height = distance ? new GridLength(0.48, GridUnitType.Star) : new GridLength(0);
            ExpPanelWallTimeView.IsVisible = distance ? true : false;
            ExpCompactWallTimeColumn.Width = distance ? new GridLength(0.8, GridUnitType.Star) : new GridLength(0);
        }

        private bool IsDistance => _currentLayout == ScorebugLayout.Distance;
        private bool IsExpanded => _currentLayout is ScorebugLayout.Expanded or ScorebugLayout.Distance;

        // ---- Data setters (update BOTH layouts so switching mid-game is seamless) ----

        public void SetTeams(string homeName, string homeAbbr, string awayName, string awayAbbr)
        {
            string nextHomeName = (string.IsNullOrWhiteSpace(homeName) ? "Home Team" : homeName).ToUpperInvariant();
            string nextAwayName = (string.IsNullOrWhiteSpace(awayName) ? "Away Team" : awayName).ToUpperInvariant();
            string nextHomeAbbr = (string.IsNullOrWhiteSpace(homeAbbr) ? "HOM" : homeAbbr).ToUpperInvariant();
            string nextAwayAbbr = (string.IsNullOrWhiteSpace(awayAbbr) ? "AWA" : awayAbbr).ToUpperInvariant();
            if (_teamsApplied && nextHomeName == _homeFullName && nextAwayName == _awayFullName &&
                nextHomeAbbr == _homeAbbr && nextAwayAbbr == _awayAbbr) return;
            _teamsApplied = true;
            _homeFullName = nextHomeName;
            _awayFullName = nextAwayName;
            _homeAbbr = nextHomeAbbr;
            _awayAbbr = nextAwayAbbr;

            // Classic layout: always show full name, scaling handles fit
            HomeNameText.Text = _homeFullName;
            AwayNameText.Text = _awayFullName;
            HomeLogoFallbackText.Text = _homeAbbr; AwayLogoFallbackText.Text = _awayAbbr;

            // Expanded layout: full name with wrapping enabled in XAML
            ExpHomeNameText.Text = _homeFullName; ExpAwayNameText.Text = _awayFullName;
            ExpHomeLogoFallback.Text = _homeAbbr; ExpAwayLogoFallback.Text = _awayAbbr;

            // Reset the previous name's transform immediately. If team setup
            // changes while the output is hidden or mid-animation, retaining
            // that offset can leave the new name permanently clipped.
            ExpHomeNameSlide.BeginAnimation(TranslateTransform.XProperty, null);
            ExpAwayNameSlide.BeginAnimation(TranslateTransform.XProperty, null);
            ExpHomeNameSlide.X = ExpAwayNameSlide.X = 0;
            ExpHomeNameText.HorizontalAlignment = HAlign.Left;
            ExpAwayNameText.HorizontalAlignment = HAlign.Left;
            ExpHomeNameHost.InvalidateMeasure();
            ExpAwayNameHost.InvalidateMeasure();

            SyncClassicNameScales();
            Dispatcher.UIThread.Post(new Action(UpdateExpandedTeamNamePanning), DispatcherPriority.Loaded);
            Dispatcher.UIThread.Post(new Action(UpdateExpandedTeamNamePanning), DispatcherPriority.Background);
        }

        private void ExpandedTeamNameHost_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            Dispatcher.UIThread.Post(new Action(UpdateExpandedTeamNamePanning), DispatcherPriority.Background);
        }

        private void UpdateExpandedTeamNamePanning()
        {
            UpdateExpandedTeamNamePanning(ExpHomeNameHost, ExpHomeNameText, ExpHomeNameSlide);
            UpdateExpandedTeamNamePanning(ExpAwayNameHost, ExpAwayNameText, ExpAwayNameSlide);
        }

        private static void UpdateExpandedTeamNamePanning(
            Control host, TextBlock text, TranslateTransform translate)
        {
            translate.BeginAnimation(TranslateTransform.XProperty, null);
            translate.X = 0;
            text.Width = double.NaN;
            if (host.NativeWidth() <= 1 || host.NativeHeight() <= 1)
                return;

            // Use the complete name lane. Long names are intentionally clipped
            // with an ellipsis instead of shrinking every club identity to an
            // unreadable size; short names retain the full available height.
            text.FontSize = Math.Clamp(Math.Floor(host.NativeHeight() * 0.90), 10, 44);
            text.HorizontalAlignment = HAlign.Stretch;
            text.TextAlignment = TextAlignment.Center;
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
                var source = TopLevel.GetTopLevel(HomeNameText);
                if (source != null)
                    pixelsPerDip = source.RenderScaling;
            }
            catch { /* pre-load fallback */ }

            var typeface = new Typeface(HomeNameText.FontFamily, HomeNameText.FontStyle, HomeNameText.FontWeight, HomeNameText.FontStretch);
            double fontSize = HomeNameText.FontSize; // 200

            var homeFormatted = new NativeFormattedText(homeText, System.Globalization.CultureInfo.CurrentCulture,
                (Avalonia.Media.FlowDirection)0, typeface, fontSize, Avalonia.Media.Brushes.White, pixelsPerDip);
            var awayFormatted = new NativeFormattedText(awayText, System.Globalization.CultureInfo.CurrentCulture,
                (Avalonia.Media.FlowDirection)0, typeface, fontSize, Avalonia.Media.Brushes.White, pixelsPerDip);

            // Available space: row height ~150px, column width depends on G/B state
            double rowHeight = HomeRowGrid.NativeHeight();
            if (rowHeight <= 0) rowHeight = 146; // fallback: (420 - 20 - 100) / 2 minus margins

            // Column 1 available width = total - logo - goals - behinds - total_score - margins
            double gbWidth = _gbColumnsExpanded ? GBColumnWidth * 2 : 0;
            double totalColWidth = HomeTotalBorder.NativeWidth();
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

            // Smart marquee fallback: if scale would drop below readability threshold,
            // clamp at threshold and animate text horizontally inside the available width.
            const double MinReadableScale = 0.45;
            bool homeOverflow = false, awayOverflow = false;
            if (uniformScale < MinReadableScale)
            {
                uniformScale = MinReadableScale;
                double scaledHome = homeNatW * uniformScale;
                double scaledAway = awayNatW * uniformScale;
                homeOverflow = scaledHome > availableWidth + 0.5;
                awayOverflow = scaledAway > availableWidth + 0.5;
            }

            if (animate)
            {
                AnimateNameScale(HomeNameText, uniformScale, duration, easing);
                AnimateNameScale(AwayNameText, uniformScale, duration, easing);
            }
            else
            {
                NativeLayoutTransform.Set(HomeNameText, new ScaleTransform(uniformScale, uniformScale));
                NativeLayoutTransform.Set(AwayNameText, new ScaleTransform(uniformScale, uniformScale));
            }

            ApplyNameMarquee(HomeNameText, homeOverflow, availableWidth, homeNatW * uniformScale);
            ApplyNameMarquee(AwayNameText, awayOverflow, availableWidth, awayNatW * uniformScale);
        }

        private static void ApplyNameMarquee(TextBlock tb, bool overflow, double availableWidth, double scaledWidth)
        {
            if (!overflow)
            {
                // Stop any existing marquee
                if (tb.RenderTransform is TranslateTransform existing)
                {
                    existing.BeginAnimation(TranslateTransform.XProperty, null);
                    tb.RenderTransform = new MatrixTransform(Matrix.Identity);
                }
                return;
            }

            var tt = tb.RenderTransform as TranslateTransform;
            if (tt is null)
            {
                tt = new TranslateTransform(0, 0);
                tb.RenderTransform = tt;
            }
            double overshoot = scaledWidth - availableWidth + 16; // small margin
            double seconds = Math.Max(3.0, overshoot / 25.0);
            var anim = new DoubleAnimation
            {
                From = 0,
                To = -overshoot,
                Duration = TimeSpan.FromSeconds(seconds),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            tt.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        /// <summary>
        /// Smoothly animates a TextBlock's LayoutTransform ScaleTransform to a new uniform scale.
        /// </summary>
        private static void AnimateNameScale(TextBlock tb, double targetScale, TimeSpan? duration = null, IEasingFunction? easing = null)
        {
            // Ensure we have a ScaleTransform to animate
            if (NativeLayoutTransform.Get(tb) is not ScaleTransform st)
            {
                st = new ScaleTransform(targetScale, targetScale);
                NativeLayoutTransform.Set(tb, st);
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

        /// <summary>
        /// Smoothly transitions score digits to a corrected state after undo/history edits.
        /// Uses a lightweight fade/slide animation (no goal/behind swipe overlays).
        /// </summary>
        public void AnimateScoreCorrectionFrom(int oldHomeGoals, int oldHomeBehinds, int oldAwayGoals, int oldAwayBehinds,
            int homeGoals, int homeBehinds, int awayGoals, int awayBehinds, Action? onCompleted = null)
        {
            HomeGoalsText.Text = oldHomeGoals.ToString();
            HomeBehindsText.Text = oldHomeBehinds.ToString();
            HomeTotalText.Text = (oldHomeGoals * 6 + oldHomeBehinds).ToString();
            ExpHomeGoalsText.Text = oldHomeGoals.ToString();
            ExpHomeBehindsText.Text = oldHomeBehinds.ToString();
            ExpHomeTotalText.Text = (oldHomeGoals * 6 + oldHomeBehinds).ToString();

            AwayGoalsText.Text = oldAwayGoals.ToString();
            AwayBehindsText.Text = oldAwayBehinds.ToString();
            AwayTotalText.Text = (oldAwayGoals * 6 + oldAwayBehinds).ToString();
            ExpAwayGoalsText.Text = oldAwayGoals.ToString();
            ExpAwayBehindsText.Text = oldAwayBehinds.ToString();
            ExpAwayTotalText.Text = (oldAwayGoals * 6 + oldAwayBehinds).ToString();

            AnimateScoreCorrection(homeGoals, homeBehinds, awayGoals, awayBehinds, onCompleted);
        }

        public void AnimateScoreCorrection(int homeGoals, int homeBehinds, int awayGoals, int awayBehinds, Action? onCompleted = null)
        {
            bool homeChanged = (HomeGoalsText.Text ?? string.Empty) != homeGoals.ToString() || (HomeBehindsText.Text ?? string.Empty) != homeBehinds.ToString();
            bool awayChanged = (AwayGoalsText.Text ?? string.Empty) != awayGoals.ToString() || (AwayBehindsText.Text ?? string.Empty) != awayBehinds.ToString();

            if (!homeChanged && !awayChanged)
            {
                SetScores(homeGoals, homeBehinds, awayGoals, awayBehinds);
                onCompleted?.Invoke();
                return;
            }

            if (homeChanged)
                _homeAnimating = true;
            if (awayChanged)
                _awayAnimating = true;

            int homeTotal = homeGoals * 6 + homeBehinds;
            int awayTotal = awayGoals * 6 + awayBehinds;

            if (homeChanged)
            {
                AnimateScoreTextSwap(HomeGoalsText, homeGoals.ToString());
                AnimateScoreTextSwap(HomeBehindsText, homeBehinds.ToString());
                AnimateScoreTextSwap(HomeTotalText, homeTotal.ToString());
                AnimateScoreTextSwap(ExpHomeGoalsText, homeGoals.ToString());
                AnimateScoreTextSwap(ExpHomeBehindsText, homeBehinds.ToString());
                AnimateScoreTextSwap(ExpHomeTotalText, homeTotal.ToString());
            }

            if (awayChanged)
            {
                AnimateScoreTextSwap(AwayGoalsText, awayGoals.ToString());
                AnimateScoreTextSwap(AwayBehindsText, awayBehinds.ToString());
                AnimateScoreTextSwap(AwayTotalText, awayTotal.ToString());
                AnimateScoreTextSwap(ExpAwayGoalsText, awayGoals.ToString());
                AnimateScoreTextSwap(ExpAwayBehindsText, awayBehinds.ToString());
                AnimateScoreTextSwap(ExpAwayTotalText, awayTotal.ToString());
            }

            var settleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            settleTimer.Tick += (_, _) =>
            {
                settleTimer.Stop();
                if (homeChanged) _homeAnimating = false;
                if (awayChanged) _awayAnimating = false;
                onCompleted?.Invoke();
            };
            settleTimer.Start();
        }

        private static void AnimateScoreTextSwap(TextBlock target, string newValue)
        {
            if (target.RenderTransform is not TranslateTransform translate)
            {
                translate = new TranslateTransform();
                target.RenderTransform = translate;
            }

            target.BeginAnimation(OpacityProperty, null);
            translate.BeginAnimation(TranslateTransform.YProperty, null);

            var fadeOut = new DoubleAnimation(1, 0.15, TimeSpan.FromMilliseconds(110))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var liftOut = new DoubleAnimation(0, -8, TimeSpan.FromMilliseconds(110))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            fadeOut.Completed += (_, _) =>
            {
                target.Text = newValue;
                target.Opacity = 0.15;
                translate.Y = 8;

                var fadeIn = new DoubleAnimation(0.15, 1, TimeSpan.FromMilliseconds(170))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var settleIn = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(170))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                target.BeginAnimation(OpacityProperty, fadeIn);
                translate.BeginAnimation(TranslateTransform.YProperty, settleIn);
            };

            target.BeginAnimation(OpacityProperty, fadeOut);
            translate.BeginAnimation(TranslateTransform.YProperty, liftOut);
        }

        private bool _quarterAnimating;
        private int _currentQuarter = 1;
        private int? _lastBreakQuarter; // when set, we're displaying the break label

        public void SetQuarter(int quarter)
        {
            if (_quarterAnimating) return;
            quarter = Math.Clamp(quarter, 1, 4);
            bool stateChanged = _currentQuarter != quarter || _lastBreakQuarter is not null;
            _currentQuarter = quarter;
            _lastBreakQuarter = null;
            // Transitioning into a new quarter clears any live operator override
            // so the new default Q-label is shown until the operator re-customises.
            if (stateChanged) QuarterLabelService.ClearCurrentOverride();
            string label = QuarterLabelService.CurrentOverride ?? QuarterLabelService.GetQuarterLabel(quarter);
            QuarterNumberText.Text = label;
            ExpQuarterNumberText.Text = label;
            ExpCompactQuarterText.Text = label;
        }

        /// <summary>
        /// Re-applies the current quarter / break label without animating. Used
        /// when the operator edits a custom label in settings and we want the
        /// scorebug to update live.
        /// </summary>
        internal void RefreshQuarterLabel()
        {
            if (_quarterAnimating) return;
            string baseLabel = _lastBreakQuarter is int eq
                ? QuarterLabelService.GetBreakLabel(eq)
                : QuarterLabelService.GetQuarterLabel(_currentQuarter);
            string label = QuarterLabelService.CurrentOverride ?? baseLabel;
            QuarterNumberText.Text = label;
            ExpQuarterNumberText.Text = label;
            ExpCompactQuarterText.Text = label;
        }

        /// <summary>
        /// Vertical flip transition: the quarter label flips down to reveal the break abbreviation.
        /// </summary>
        public void AnimateQuarterToBreakLabel(int endedQuarter)
        {
            // Transitioning to a break clears any live override.
            QuarterLabelService.ClearCurrentOverride();
            string breakLabel = QuarterLabelService.GetBreakLabel(endedQuarter);
            _lastBreakQuarter = endedQuarter;

            // Set the text to the pre-break quarter so the flip starts from the right label
            string fromLabel = QuarterLabelService.GetQuarterLabel(endedQuarter);
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
                textBlock.RenderTransformOrigin = new RelativePoint(0.5, 0.5,RelativeUnit.Relative);
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
        private IImage? _homeLogoSource;
        private IImage? _awayLogoSource;

        public void SetTeamColors(string homeHex, string awayHex,
                                   string homeSecondaryHex = "#FFFFFF", string awaySecondaryHex = "#FFFFFF")
        {
            Color nextHome = SafeColor(homeHex, "#0A2A6A");
            Color nextAway = SafeColor(awayHex, "#7A1A1A");
            Color nextHomeSecondary = SafeColor(homeSecondaryHex, "#FFFFFF");
            Color nextAwaySecondary = SafeColor(awaySecondaryHex, "#FFFFFF");
            if (_teamColoursApplied && nextHome == _homeColor && nextAway == _awayColor &&
                nextHomeSecondary == _homeSecondaryColor && nextAwaySecondary == _awaySecondaryColor) return;
            _teamColoursApplied = true;
            _homeColor = nextHome;
            _awayColor = nextAway;
            _homeSecondaryColor = nextHomeSecondary;
            _awaySecondaryColor = nextAwaySecondary;

            var hBrush = new SolidColorBrush(_homeColor);
            var aBrush = new SolidColorBrush(_awayColor);

            // Team secondary colour for all team-row text (names, scores, G/B digits)
            // Smart contrast: if the secondary colour fails WCAG against the team primary,
            // fall back to a high-contrast white/black so text remains readable.
            Color hTextColor = _homeSecondaryColor;
            Color aTextColor = _awaySecondaryColor;
            if (Services.SmartContrast.IsLowContrast(hTextColor, _homeColor))
                hTextColor = Services.SmartContrast.PickForeground(_homeColor);
            if (Services.SmartContrast.IsLowContrast(aTextColor, _awayColor))
                aTextColor = Services.SmartContrast.PickForeground(_awayColor);
            var hSecBrush = new SolidColorBrush(hTextColor);
            var aSecBrush = new SolidColorBrush(aTextColor);

            // Classic ° single continuous gradient across the entire row (including logo area)
            HomeRowGrid.Background = BuildRowGradient(_homeColor);
            AwayRowGrid.Background = BuildRowGradient(_awayColor);
            HomeLogoSquare.Background = Avalonia.Media.Brushes.Transparent;
            AwayLogoSquare.Background = Avalonia.Media.Brushes.Transparent;
            HomeTotalBorder.Background = new SolidColorBrush(Mix(_homeColor, Colors.Black, 0.45));
            AwayTotalBorder.Background = new SolidColorBrush(Mix(_awayColor, Colors.Black, 0.45));
            // Team names use each team's secondary colour so the scorebug is fully team-themed.
            // Both TextBlocks share identical font/weight/size in XAML, so they render with
            // the same visual treatment ? only the colour differs per team.
            HomeNameText.Foreground = hSecBrush; AwayNameText.Foreground = aSecBrush;
            HomeTotalText.Foreground = hSecBrush; AwayTotalText.Foreground = aSecBrush;
            HomeGoalsText.Foreground = hSecBrush; HomeBehindsText.Foreground = hSecBrush;
            AwayGoalsText.Foreground = aSecBrush; AwayBehindsText.Foreground = aSecBrush;

            // Expanded ° apply team gradients across logo cell and score panel.
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

            // The score-change card always covers the active total cell. Its
            // foreground is chosen independently so +1/+6 cannot disappear on
            // low-contrast team colour combinations.
            Color homeSwipeText = Services.SmartContrast.IsLowContrast(_homeColor, _homeSecondaryColor)
                ? Services.SmartContrast.PickForeground(_homeSecondaryColor) : _homeColor;
            Color awaySwipeText = Services.SmartContrast.IsLowContrast(_awayColor, _awaySecondaryColor)
                ? Services.SmartContrast.PickForeground(_awaySecondaryColor) : _awayColor;
            HomeSwipeBg.Color = ExpHomeSwipeBg.Color = _homeSecondaryColor;
            HomeSwipeFg.Color = ExpHomeSwipeFg.Color = homeSwipeText;
            AwaySwipeBg.Color = ExpAwaySwipeBg.Color = _awaySecondaryColor;
            AwaySwipeFg.Color = ExpAwaySwipeFg.Color = awaySwipeText;

            // Tint overlay border gradients with team primary colours
            ApplyOverlayBorderColors();

            ApplyGradient();
        }

        /// <summary>
        /// Applies the team palette to overlays whose values are explicitly
        /// split into home and away sections.
        /// </summary>
        private void ApplyOverlayBorderColors()
        {
            var hC = _homeColor;
            var aC = _awayColor;
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
            var hQuarterText = new SolidColorBrush(Services.SmartContrast.PickReadableForeground(hSec, hC));
            var aQuarterText = new SolidColorBrush(Services.SmartContrast.PickReadableForeground(aSec, aC));
            ExpQuarterScoresHomeNameText.Foreground = hQuarterText;
            ExpQuarterScoresHomeTotalText.Foreground = hQuarterText;
            ExpQuarterScoresAwayNameText.Foreground = aQuarterText;
            ExpQuarterScoresAwayTotalText.Foreground = aQuarterText;
        }

        public void SetLogos(string? homeLogoPath, string? awayLogoPath)
        {
            if (_logosApplied && string.Equals(homeLogoPath, _appliedHomeLogoPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(awayLogoPath, _appliedAwayLogoPath, StringComparison.OrdinalIgnoreCase)) return;
            _logosApplied = true;
            _appliedHomeLogoPath = homeLogoPath;
            _appliedAwayLogoPath = awayLogoPath;
            ApplyLogo(HomeLogoImage, HomeLogoFallbackText, homeLogoPath);
            ApplyLogo(AwayLogoImage, AwayLogoFallbackText, awayLogoPath);
            ApplyLogoExpanded(ExpHomeLogoImage, ExpHomeLogoFallback, ExpHomeLogoSquare, homeLogoPath);
            ApplyLogoExpanded(ExpAwayLogoImage, ExpAwayLogoFallback, ExpAwayLogoSquare, awayLogoPath);
            _homeLogoSource = ExpHomeLogoImage.Source;
            _awayLogoSource = ExpAwayLogoImage.Source;
            ExpQuarterScoresHomeLogo.Source = _homeLogoSource;
            ExpQuarterScoresAwayLogo.Source = _awayLogoSource;
            ExpSharedOverlayHomeLogo.Source = _homeLogoSource;
            ExpSharedOverlayAwayLogo.Source = _awayLogoSource;
            ApplyLogoTransforms();
        }

        public void SetLogoCrop(
            double homeZoom, double homeOffsetX, double homeOffsetY,
            double awayZoom, double awayOffsetX, double awayOffsetY)
        {
            double nextHomeZoom = Math.Clamp(homeZoom, 0.8, 4.0);
            double nextHomeX = Math.Clamp(homeOffsetX, -50, 50);
            double nextHomeY = Math.Clamp(homeOffsetY, -50, 50);
            double nextAwayZoom = Math.Clamp(awayZoom, 0.8, 4.0);
            double nextAwayX = Math.Clamp(awayOffsetX, -50, 50);
            double nextAwayY = Math.Clamp(awayOffsetY, -50, 50);
            if (Math.Abs(nextHomeZoom - _homeLogoZoom) < 0.0001 && Math.Abs(nextHomeX - _homeLogoOffsetX) < 0.0001 &&
                Math.Abs(nextHomeY - _homeLogoOffsetY) < 0.0001 && Math.Abs(nextAwayZoom - _awayLogoZoom) < 0.0001 &&
                Math.Abs(nextAwayX - _awayLogoOffsetX) < 0.0001 && Math.Abs(nextAwayY - _awayLogoOffsetY) < 0.0001) return;
            _homeLogoZoom = nextHomeZoom;
            _homeLogoOffsetX = nextHomeX;
            _homeLogoOffsetY = nextHomeY;
            _awayLogoZoom = nextAwayZoom;
            _awayLogoOffsetX = nextAwayX;
            _awayLogoOffsetY = nextAwayY;

            ApplyLogoTransforms();
        }

        private void ApplyLogoTransforms()
        {
            ApplyLogoTransform(HomeLogoImage, _homeLogoZoom, _homeLogoOffsetX, _homeLogoOffsetY);
            ApplyLogoTransform(ExpHomeLogoImage, _homeLogoZoom, _homeLogoOffsetX, _homeLogoOffsetY);
            ApplyLogoTransform(AwayLogoImage, _awayLogoZoom, _awayLogoOffsetX, _awayLogoOffsetY);
            ApplyLogoTransform(ExpAwayLogoImage, _awayLogoZoom, _awayLogoOffsetX, _awayLogoOffsetY);
        }

        private static void ApplyLogoTransform(Avalonia.Controls.Image image, double zoom, double offsetX, double offsetY)
        {
            image.RenderTransformOrigin = new RelativePoint(0.5, 0.5,RelativeUnit.Relative);
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

        private static void ApplyLogo(Avalonia.Controls.Image image, TextBlock fallback, string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            { image.Source = null; image.IsVisible = false; fallback.IsVisible = true; return; }
            try
            {
                var bmp = new NativeBitmap();
                bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(path, UriKind.Absolute); bmp.EndInit(); bmp.Freeze();
                image.Source = bmp; image.IsVisible = true; fallback.IsVisible = false;
            }
            catch { image.Source = null; image.IsVisible = false; fallback.IsVisible = true; }
        }

        private static void ApplyLogoExpanded(Avalonia.Controls.Image image, TextBlock fallback, Control placeholder, string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            { image.Source = null; image.IsVisible = false; fallback.IsVisible = true; placeholder.IsVisible = true; return; }
            try
            {
                var bmp = new NativeBitmap();
                bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(path, UriKind.Absolute); bmp.EndInit(); bmp.Freeze();

                // Auto-crop transparent / near-uniform borders so small logos visually fill the cell.
                var trimmed = TrimLogoTransparentBorders(bmp);
                image.Source = trimmed; image.IsVisible = true; fallback.IsVisible = false; placeholder.IsVisible = false;
            }
            catch { image.Source = null; image.IsVisible = false; fallback.IsVisible = true; placeholder.IsVisible = true; }
        }

        /// <summary>
        /// Detects fully-transparent or near-transparent border rows/columns around a logo
        /// and returns a tightly-cropped <see cref="NativeBitmap"/>. This makes the logo
        /// visually fill the available cell when the source image has lots of empty space.
        /// Falls back to the original bitmap if no crop is possible or the format isn't BGRA.
        /// </summary>
        private static NativeBitmap TrimLogoTransparentBorders(NativeBitmap src)
        {
            try
            {
                var converted = new NativeFormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
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
                // No meaningful trim ° return original
                if (top == 0 && left == 0 && cropW == w && cropH == h) return src;

                var cropped = new NativeCroppedBitmap(converted, new PixelRect(left, top, cropW, cropH));
                cropped.Freeze();
                return cropped;
            }
            catch
            {
                return src;
            }
        }

        private static Avalonia.Media.IBrush BuildLogoBackground(Color baseColor)
        {
            var darker = Mix(baseColor, Colors.Black, 0.62);
            var lessDark = Mix(baseColor, Colors.Black, 0.38);
            return NativeBrush.Linear(darker, lessDark, new Avalonia.Point(0, 0), new Avalonia.Point(1, 1));
        }

        /// <summary>
        /// Bold horizontal gradient for a team row: starts very dark at the logo edge,
        /// blends through the base colour, and ends noticeably lighter at the score end.
        /// </summary>
        private static Avalonia.Media.IBrush BuildRowGradient(Color baseColor)
        {
            Color dark = Mix(baseColor, Colors.Black, 0.65);
            Color lighter = Mix(baseColor, Colors.White, 0.10);

            var grad = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative)
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
                return (Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString(hex);
            }
            catch { return (Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString(fallbackHex); }
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
            // Defer if a goal overlay is still animating ° schedule a retry
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

            // Column expansion ° To-only so interrupted collapses resume smoothly
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

            // Column collapse ° To-only so interrupted expands resume smoothly
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
            _lastScoreEventTime = OverlayClockNow;
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

            if ((OverlayClockNow - _lastScoreEventTime).TotalMinutes >= GBAutoShowIdleMinutes)
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

        public void PlayScoreAnimation(bool isHome, bool isGoal, int newGoals, int newBehinds, int newTotal, Action? onCompleted = null)
        {
            bool expanded = IsExpanded;
            if (expanded) CancelExpandedTeamAnimation(isHome);
            else CancelClassicTeamAnimation(isHome);

            // Lock score-text writes for this side until the entire sequence
            // (entry overlay + digit flip + +N swipe) has finished. Without
            // this, SetScores from PushAllToScoreboard can overwrite digits
            // mid-flight and the swipe text falls out of sync with the value
            // the operator actually clicked.
            if (isHome) _homeAnimating = true; else _awayAnimating = true;

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

            // onCompleted is fired from the guard timer inside AddFlipAnimations
            // so it lines up with the END of the +N swipe slide-out, not the
            // earlier moment when the storyboard portion finishes.
            AddFlipAnimations(sb, isHome, flipStart, newGoals, newBehinds, newTotal, expanded,
                isGoal ? _animationStyle : GoalAnimationStyle.Clean, onCompleted);
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
        public void PlayScoreFlipOnly(bool isHome, int newGoals, int newBehinds, int newTotal, Action? onCompleted = null)
        {
            bool expanded = IsExpanded;
            if (expanded) CancelExpandedTeamAnimation(isHome);
            else CancelClassicTeamAnimation(isHome);

            // Lock score-text writes until the flip + swipe sequence completes.
            if (isHome) _homeAnimating = true; else _awayAnimating = true;

            var sb = new Storyboard();
            AddFlipAnimations(sb, isHome, 0.0, newGoals, newBehinds, newTotal, expanded,
                GoalAnimationStyle.Clean, onCompleted);
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
        private static (double from, double to) GetGradSweep(Control overlay, bool expanded)
        {
            double w = overlay.NativeWidth();
            if (w <= 0)
                return (expanded ? -2000 : -1400, 900);

            // Original ratios at 960 design width: from=-2000 (~-2.08w), to=900 (~0.94w),
            // collapsed: from=-1400 (~-1.46w), to=900. Reproduce them container-relative.
            double fromRatio = expanded ? -2.08 : -1.46;
            double toRatio = 0.94;
            return (w * fromRatio, w * toRatio);
        }

        private static double GetShimmerFrom(Control overlay, bool expanded)
        {
            double w = overlay.NativeWidth();
            if (w <= 0) return expanded ? -250 : -200;
            return w * (expanded ? -0.26 : -0.21);
        }

        private static double GetShimmerTo(Control overlay, bool expanded)
        {
            double w = overlay.NativeWidth();
            if (w <= 0) return expanded ? 800 : 900;
            return w * (expanded ? 0.84 : 0.94);
        }

        private Border GetOverlay(bool isHome, bool expanded) =>
            expanded ? (isHome ? ExpHomeOverlay : ExpAwayOverlay) : (isHome ? HomeOverlay : AwayOverlay);
        private TextBlock GetOverlayText(bool isHome, bool expanded) =>
            expanded ? (isHome ? ExpHomeOverlayText : ExpAwayOverlayText) : (isHome ? HomeOverlayText : AwayOverlayText);
        private SolidColorBrush GetOverlayBg(bool isHome, bool expanded) =>
            expanded ? (isHome ? ExpHomeOverlayBg : ExpAwayOverlayBg) : (isHome ? HomeOverlayBg : AwayOverlayBg);
        private Avalonia.Controls.Shapes.Rectangle GetGradBar(bool isHome, bool expanded) =>
            expanded ? (isHome ? ExpHomeGradBar : ExpAwayGradBar) : (isHome ? HomeGradientBar : AwayGradientBar);
        private TranslateTransform GetGradScroll(bool isHome, bool expanded) =>
            expanded ? (isHome ? ExpHomeGradScroll : ExpAwayGradScroll) : (isHome ? HomeGradientScroll : AwayGradientScroll);
        private Avalonia.Controls.Shapes.Rectangle GetShimmer(bool isHome, bool expanded) =>
            expanded ? (isHome ? ExpHomeShimmer : ExpAwayShimmer) : (isHome ? HomeShimmer : AwayShimmer);
        private TranslateTransform GetShimmerScroll(bool isHome, bool expanded) =>
            expanded ? (isHome ? ExpHomeShimmerScroll : ExpAwayShimmerScroll) : (isHome ? HomeShimmerScroll : AwayShimmerScroll);
        private Border GetScoreGlow(bool isHome, bool expanded) =>
            expanded ? (isHome ? ExpHomeScoreGlow : ExpAwayScoreGlow) : (isHome ? HomeScoreGlow : AwayScoreGlow);
        private Avalonia.Controls.Shapes.Rectangle GetEdgeGlow(bool isHome, bool expanded) =>
            expanded ? (isHome ? ExpHomeEdgeGlow : ExpAwayEdgeGlow) : (isHome ? HomeEdgeGlow : AwayEdgeGlow);
        private RadialGradientBrush GetScoreGlowBrush(bool isHome, bool expanded) =>
            NativeBrush.Require<RadialGradientBrush>(GetScoreGlow(isHome, expanded).Background);

        private void EnsureOverlayContrast(bool isHome, bool expanded, Color preferred)
        {
            Color background = GetOverlayBg(isHome, expanded).Color;
            Color foreground = Services.SmartContrast.IsLowContrast(preferred, background)
                ? Services.SmartContrast.PickForeground(background) : preferred;
            TextBlock text = GetOverlayText(isHome, expanded);
            text.Foreground = new SolidColorBrush(foreground);
            if (text.Effect is Avalonia.Media.DropShadowEffect glow)
                glow.Color = Services.SmartContrast.PickForeground(foreground);
        }

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
            overlayText.Text = "GOAL";
            Color readableText = Services.SmartContrast.IsLowContrast(secondaryColor, overlayBg.Color)
                ? Services.SmartContrast.PickForeground(overlayBg.Color) : secondaryColor;
            overlayText.Foreground = new SolidColorBrush(readableText);
            overlayText.FontSize = expanded ? 100 : 72;
            overlayText.FontFamily = NativeFonts.Family("Arial Black");
            overlayText.FontStyle = FontStyle.Normal;
            overlayText.FontStretch = FontStretch.Normal;
            overlayText.TextAlignment = TextAlignment.Center;
            overlayText.LineHeight = double.NaN;

            if (expanded)
            {
                // When the ticker or a team story has already reclaimed the
                // name lane, let GOAL use that available panel area instead of
                // retaining the normal presentation margins.
                bool constrainedState = IsTickerActive || _activeOverlay is not null;
                (isHome ? ExpHomeGoalTextViewbox : ExpAwayGoalTextViewbox).Margin = constrainedState
                    ? new Thickness(10, 4, 10, 4)
                    : new Thickness(32, 12, 32, 12);
            }

            // Reset overlay-level transform/opacity so a previous style cannot
            // leak a leftover TranslateTransform or stale opacity into the next
            // animation. Each style configures its own entry transform.
            overlay.RenderTransform = new TranslateTransform(0, 0);
            overlay.RenderTransformOrigin = new RelativePoint(0.5, 0.5,RelativeUnit.Relative);
            overlay.Opacity = 0;
            overlayText.Opacity = 1;

            if (expanded)
            {
                var scale = isHome ? ExpHomeOverlayScale : ExpAwayOverlayScale;
                scale.ScaleX = 0; scale.ScaleY = 0;
            }
            else
            {
                overlayText.RenderTransformOrigin = new RelativePoint(0.5, 0.5,RelativeUnit.Relative);
                overlayText.RenderTransform = new ScaleTransform(0, 0);
            }

            double gradReset = expanded ? -2000 : -1400;
            gradScroll.X = gradReset;
            shimmerScroll.X = expanded ? -250 : -200;
            shimmer.Opacity = 0;
            gradBar.Opacity = expanded ? 0.55 : 0.7;

            // Reset edge glow too — Electric strobes it, Clean uses it as an accent.
            GetEdgeGlow(isHome, expanded).Opacity = 0;

            if (overlayText.Effect is Avalonia.Media.DropShadowEffect glow)
            { glow.Color = readableText; glow.BlurRadius = 40; glow.Opacity = 0.9; }
        }

        private void PrepareScoreGlow(bool isHome, bool expanded, Color teamColor)
        {
            var brush = GetScoreGlowBrush(isHome, expanded);
            brush.GradientStops[0].Color = Color.FromArgb(0xCC, teamColor.R, teamColor.G, teamColor.B);
            brush.GradientStops[1].Color = Color.FromArgb(0x00, teamColor.R, teamColor.G, teamColor.B);
        }

        private void ApplyExpandedGoalChrome(bool isHome, bool expanded, GoalAnimationStyle style)
        {
            if (!expanded) return;
            Control[] chrome = isHome
                ? [ExpHomeBroadcastChrome, ExpHomeElectricChrome, ExpHomeCinematicChrome, ExpHomeCleanChrome, ExpHomeClassicChrome]
                : [ExpAwayBroadcastChrome, ExpAwayElectricChrome, ExpAwayCinematicChrome, ExpAwayCleanChrome, ExpAwayClassicChrome];
            foreach (Control element in chrome)
            {
                element.BeginAnimation(OpacityProperty, null);
                element.Opacity = 0;
            }
            // The earlier preset frames/rails/patterns read as decoration at
            // scoreboard scale. Presets now derive their identities entirely
            // from timing, direction, light and wordmark motion.
        }

        // ==================================================================
        // BROADCAST — Premium TV graphics package
        // ------------------------------------------------------------------
        // Structural fingerprint: VERTICAL reveal entry, multi-beat hold with
        // two distinct shimmer passes, signature double-tap score glow, then
        // controlled fade. Feels like a top-tier sports broadcaster lower-third.
        // Entry: scaleY 0→1 with overshoot (vertical unfurl).
        // Hold:  team-colour gradient wash; text grows then settles; two
        //        shimmers separated by a silent beat; second glow pulse.
        // Exit:  smooth opacity fade (no slide).
        // Duration ~3.8s.
        // ==================================================================

        private double BuildBroadcastGoal(Storyboard sb, bool isHome, bool expanded)
        {
            var primaryColor = isHome ? _homeColor : _awayColor;
            var secondaryColor = isHome ? _homeSecondaryColor : _awaySecondaryColor;
            PrepareOverlay(isHome, expanded, primaryColor, secondaryColor);
            ApplyExpandedGoalChrome(isHome, expanded, GoalAnimationStyle.Broadcast);
            PrepareScoreGlow(isHome, expanded, primaryColor);

            var overlay = GetOverlay(isHome, expanded);
            var overlayText = GetOverlayText(isHome, expanded);
            var gradBar = GetGradBar(isHome, expanded);
            var shimmer = GetShimmer(isHome, expanded);
            var scoreGlow = GetScoreGlow(isHome, expanded);
            var edgeGlow = GetEdgeGlow(isHome, expanded);

            overlayText.Text = "GOAL";
            overlayText.FontFamily = NativeFonts.Family("Arial Black");
            overlayText.FontStretch = FontStretch.Expanded;

            GetOverlayBg(isHome, expanded).Color = Color.FromArgb(0xFF, primaryColor.R, primaryColor.G, primaryColor.B);
            EnsureOverlayContrast(isHome, expanded, secondaryColor);

            // White ↔ team-secondary wash on the gradient bar
            var gradBrush = NativeBrush.Require<LinearGradientBrush>(gradBar.Fill);
            gradBrush.GradientStops[0].Color = Colors.White;
            gradBrush.GradientStops[1].Color = secondaryColor;
            gradBrush.GradientStops[2].Color = Colors.White;
            gradBrush.GradientStops[3].Color = secondaryColor;
            gradBrush.GradientStops[4].Color = Colors.White;
            gradBrush.GradientStops[5].Color = secondaryColor;
            gradBrush.GradientStops[6].Color = Colors.White;
            gradBrush.GradientStops[7].Color = secondaryColor;

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            var decelEase = new CubicEase { EasingMode = EasingMode.EaseOut };
            var backEase = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut };
            var (gradFrom, gradTo) = GetGradSweep(overlay, expanded);
            double shimFrom = GetShimmerFrom(overlay, expanded);
            double shimTo = GetShimmerTo(overlay, expanded);

            // Signature VERTICAL feel: overlay snaps in at full coverage so the
            // logos/scores can never peek through, but the GOAL text *itself*
            // unfurls vertically (scaleY 0→1 with overshoot) giving the
            // "lower-third reveal" character without exposing the row beneath.
            overlayText.FontSize = expanded ? 124 : 90;
            overlay.RenderTransform = new TranslateTransform(0, 0);
            overlay.Opacity = 0;
            sb.Children.Add(MakeDA(overlay, OpacityProperty, 0, 1, 0.10, 0.0, decelEase));

            // GOAL text unfurls vertically with a confident overshoot — this
            // carries the broadcast "reveal" signature now that the panel
            // itself is always fully opaque.
            if (expanded)
            { var sc = isHome ? ExpHomeOverlayScale : ExpAwayOverlayScale; sc.ScaleX = 1; sc.ScaleY = 0; }
            else
            { overlayText.RenderTransform = new ScaleTransform(1, 0); }
            overlayText.Opacity = 0;
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 0, 1, 0.12, 0.10, decelEase));
            sb.Children.Add(MakeDA(overlayText, "RenderTransform.ScaleY", 0, 1, 0.42, 0.10, backEase));
            sb.Children.Add(MakeDA(overlayText, "RenderTransform.ScaleX", 1, 1, 0.01, 0.0));

            // First gradient wash (0.2–1.5s) — slow, authoritative
            sb.Children.Add(MakeDA(gradBar, "RenderTransform.(TranslateTransform.X)", gradFrom, gradTo, 1.3, 0.2, ease));

            // Score glow: double-tap signature (tap → ease → second tap → fade)
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 0, 1, 0.08, 0.0));
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 1, 0.25, 0.35, 0.08, ease));
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 0.25, 0.95, 0.18, 1.4, ease));
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 0.95, 0, 1.6, 1.6, ease));

            // Text glow follows the double-tap rhythm
            sb.Children.Add(MakeDA(overlayText, "(Control.Effect).(DropShadowEffect.BlurRadius)", 30, 90, 0.25, 0.0, ease));
            sb.Children.Add(MakeDA(overlayText, "(Control.Effect).(DropShadowEffect.BlurRadius)", 90, 30, 0.5, 0.25, ease));
            sb.Children.Add(MakeDA(overlayText, "(Control.Effect).(DropShadowEffect.BlurRadius)", 30, 75, 0.15, 1.4, ease));
            sb.Children.Add(MakeDA(overlayText, "(Control.Effect).(DropShadowEffect.BlurRadius)", 75, 25, 0.7, 1.55, ease));

            // Edge glow accent during hold (subtle horizontal energy line)
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 0, 0.85, 0.4, 0.6, ease));
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 0.85, 0, 0.8, 2.4, ease));

            // First shimmer (0.6–1.1s) — quick pass
            sb.Children.Add(MakeDA(shimmer, OpacityProperty, 0, 0.9, 0.06, 0.6));
            sb.Children.Add(MakeDA(shimmer, "RenderTransform.(TranslateTransform.X)", shimFrom, shimTo, 0.55, 0.6, ease));
            sb.Children.Add(MakeDA(shimmer, OpacityProperty, 0.9, 0, 0.08, 1.07));

            // Second shimmer (2.0–2.6s) — slower, signature double-pass
            sb.Children.Add(MakeDA(shimmer, OpacityProperty, 0, 0.7, 0.08, 2.0));
            sb.Children.Add(MakeDA(shimmer, "RenderTransform.(TranslateTransform.X)", shimFrom, shimTo, 0.7, 2.0, ease));
            sb.Children.Add(MakeDA(shimmer, OpacityProperty, 0.7, 0, 0.1, 2.6));

            // Exit: GOAL text closes vertically while the panel opacity holds
            // until the very last moment so the row stays masked while the
            // wordmark collapses — then both fade together.
            sb.Children.Add(MakeDA(overlayText, "RenderTransform.ScaleY", 1, 0, 0.35, 3.20, ease));
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 1, 0, 0.30, 3.25, ease));
            sb.Children.Add(MakeDA(overlay, OpacityProperty, 1, 0, 0.30, 3.40, ease));

            return 3.5;
        }

        // ==================================================================
        // ELECTRIC — Stadium hype / high-energy
        // ------------------------------------------------------------------
        // Structural fingerprint: HORIZONTAL energy injection (no central
        // scale-slam), rhythmic crackle hold, jagged exit. Edge glows lead
        // the eye in from both sides; the overlay surges in like a charge.
        // Entry: edge double-zap → horizontal compress/expand snap (scaleX
        //        from 1.6 → 1 with overshoot), text vibrates micro-jitter.
        // Hold:  5-beat strobe rhythm on the overlay & edge glow at uneven
        //        intervals; gradient bar dashes across in reverse direction.
        // Exit:  rapid edge-glow tail + horizontal collapse + cut.
        // Duration ~3.0s.
        // ==================================================================

        private double BuildElectricGoal(Storyboard sb, bool isHome, bool expanded)
        {
            var primaryColor = isHome ? _homeColor : _awayColor;
            var secondaryColor = isHome ? _homeSecondaryColor : _awaySecondaryColor;
            PrepareOverlay(isHome, expanded, primaryColor, secondaryColor);
            ApplyExpandedGoalChrome(isHome, expanded, GoalAnimationStyle.Electric);

            var overlay = GetOverlay(isHome, expanded);
            var overlayText = GetOverlayText(isHome, expanded);
            var overlayBg = GetOverlayBg(isHome, expanded);
            var edgeGlow = GetEdgeGlow(isHome, expanded);
            var gradBar = GetGradBar(isHome, expanded);

            overlayText.Text = "GOAL";
            overlayText.FontFamily = NativeFonts.Family("Arial Black");
            overlayText.FontStyle = FontStyle.Oblique;
            overlayText.FontStretch = FontStretch.Expanded;

            // Hot white core wrapped in team colour
            overlayBg.Color = Color.FromRgb(
                (byte)(primaryColor.R * 0.30),
                (byte)(primaryColor.G * 0.30),
                (byte)(primaryColor.B * 0.30));
            overlayText.FontSize = expanded ? 128 : 92;
            overlayText.Foreground = new SolidColorBrush(Colors.White);
            if (overlayText.Effect is Avalonia.Media.DropShadowEffect g)
            {
                // The electrical wash deliberately crosses bright secondary
                // colours. A compact black halo keeps GOAL legible throughout
                // the pass, including white-team palettes.
                g.Color = Colors.Black;
                g.BlurRadius = 6;
                g.OffsetX = g.OffsetY = 0;
                g.Opacity = 1.0;
            }

            // Reverse-direction gradient pass (right-to-left) — distinct from Broadcast/Classic
            var gradBrush = NativeBrush.Require<LinearGradientBrush>(gradBar.Fill);
            gradBrush.GradientStops[0].Color = secondaryColor;
            gradBrush.GradientStops[1].Color = Colors.White;
            gradBrush.GradientStops[2].Color = secondaryColor;
            gradBrush.GradientStops[3].Color = Colors.White;
            gradBrush.GradientStops[4].Color = secondaryColor;
            gradBrush.GradientStops[5].Color = Colors.White;
            gradBrush.GradientStops[6].Color = secondaryColor;
            gradBrush.GradientStops[7].Color = Colors.White;
            gradBar.Opacity = 0.62;

            // Text: horizontal compress (1.6×, 1×) snapping wide — NO uniform scale-slam
            if (expanded)
            { var sc = isHome ? ExpHomeOverlayScale : ExpAwayOverlayScale; sc.ScaleX = 1.6; sc.ScaleY = 1; }
            else
            { overlayText.RenderTransform = new ScaleTransform(1.6, 1); }

            var snapEase = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var decelEase = new CubicEase { EasingMode = EasingMode.EaseOut };
            var backEase = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut };
            var (gradFrom, gradTo) = GetGradSweep(overlay, expanded);

            // 0.00 – 0.30s: pre-charge edge zaps before overlay appears
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 0, 1, 0.03, 0.0));
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 1, 0, 0.05, 0.04));
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 0, 1, 0.03, 0.12));
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 1, 0, 0.05, 0.16));

            // 0.20s: overlay surges in with horizontal expansion of the text.
            // The panel itself snaps to full opacity instantly so the row
            // never bleeds through during the energy beats.
            sb.Children.Add(MakeDA(overlay, OpacityProperty, 0, 1, 0.04, 0.20));
            sb.Children.Add(MakeDA(overlayText, "RenderTransform.ScaleX", 1.6, 1, 0.28, 0.20, backEase));

            // Reverse gradient dash (right→left) very fast
            sb.Children.Add(MakeDA(gradBar, "RenderTransform.(TranslateTransform.X)", gradTo, gradFrom, 0.45, 0.22, snapEase));

            // Hold beat 1: 5-step strobe at uneven cadence — strobing now
            // hits the GOAL TEXT and SHIMMER (a white flash layer) instead
            // of the panel, so the underlying row stays fully covered.
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 1, 0.35, 0.04, 0.55));
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 0.35, 1, 0.05, 0.59));
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 1, 0.5, 0.03, 0.78));
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 0.5, 1, 0.04, 0.81));
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 1, 0.7, 0.04, 1.05));
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 0.7, 1, 0.05, 1.09));

            // Edge glow rides the strobes
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 0, 1, 0.05, 0.55));
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 1, 0.2, 0.25, 0.60));
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 0.2, 1, 0.05, 1.05));
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 1, 0.3, 0.4, 1.10));

            // Glow burst pulses in sync (NOT a smooth fade — sharp)
            sb.Children.Add(MakeDA(overlayText, "(Control.Effect).(DropShadowEffect.BlurRadius)", 30, 95, 0.06, 0.20));
            sb.Children.Add(MakeDA(overlayText, "(Control.Effect).(DropShadowEffect.BlurRadius)", 95, 25, 0.30, 0.26, decelEase));
            sb.Children.Add(MakeDA(overlayText, "(Control.Effect).(DropShadowEffect.BlurRadius)", 25, 70, 0.06, 1.05));
            sb.Children.Add(MakeDA(overlayText, "(Control.Effect).(DropShadowEffect.BlurRadius)", 70, 30, 0.4, 1.11, decelEase));

            // Second reverse gradient dash (1.6s) — confirms the energy
            sb.Children.Add(MakeDA(gradBar, "RenderTransform.(TranslateTransform.X)", gradTo, gradFrom, 0.5, 1.6, snapEase));

            // Sustained crackle on edge glow (1.8 – 2.5s)
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 0.3, 1, 0.03, 1.85));
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 1, 0.4, 0.06, 1.88));
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 0.4, 1, 0.03, 2.10));
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 1, 0.3, 0.10, 2.13));
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 0.3, 0, 0.4, 2.30, decelEase));

            // Exit (2.5 – 3.0s): text collapses horizontally while the panel
            // stays fully opaque, then a quick cut at the very end.
            sb.Children.Add(MakeDA(overlayText, "RenderTransform.ScaleX", 1, 0.7, 0.18, 2.50, snapEase));
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 1, 0, 0.12, 2.65, snapEase));
            sb.Children.Add(MakeDA(overlay, OpacityProperty, 1, 0, 0.08, 2.78, snapEase));

            return 2.9;
        }

        // ==================================================================
        // CINEMATIC — Champions League / atmospheric drama
        // ------------------------------------------------------------------
        // Structural fingerprint: SLOW BUILD, long breath-hold, atmospheric
        // exit. The only style with horizontal slide ENTRY and EXIT — but
        // they are slow and intentional, not flashy. Team logo participates:
        // it slowly zooms forward during the buildup as an identity beat.
        // Entry: overlay slides in from left over 0.9s (slow). Text fades
        //        in from 0 with a faint upward drift; no scale snap.
        // Hold:  long, glow breathes through three full inhale/exhale
        //        cycles; team logo gently zoom-in (1.0 → 1.08) reinforcing
        //        identity; subtle score glow rises and lingers.
        // Exit:  slow slide out to right + opacity fade together (1.0s).
        // Duration ~5.2s.
        // ==================================================================

        private double BuildCinematicGoal(Storyboard sb, bool isHome, bool expanded)
        {
            var primaryColor = isHome ? _homeColor : _awayColor;
            var secondaryColor = isHome ? _homeSecondaryColor : _awaySecondaryColor;
            PrepareOverlay(isHome, expanded, primaryColor, secondaryColor);
            ApplyExpandedGoalChrome(isHome, expanded, GoalAnimationStyle.Cinematic);
            PrepareScoreGlow(isHome, expanded, primaryColor);

            var overlay = GetOverlay(isHome, expanded);
            var overlayText = GetOverlayText(isHome, expanded);
            var gradBar = GetGradBar(isHome, expanded);
            var scoreGlow = GetScoreGlow(isHome, expanded);

            overlayText.Text = "GOAL";
            overlayText.FontFamily = NativeFonts.Family("Arial Black");
            overlayText.FontStretch = FontStretch.Normal;
            overlayText.TextAlignment = TextAlignment.Center;
            overlayText.LineHeight = expanded ? 76 : 56;

            GetOverlayBg(isHome, expanded).Color = Color.FromRgb(
                (byte)(primaryColor.R * 0.22),
                (byte)(primaryColor.G * 0.22),
                (byte)(primaryColor.B * 0.22));
            EnsureOverlayContrast(isHome, expanded, secondaryColor);
            overlayText.FontSize = expanded ? 70 : 52;

            // Text at full scale, but starts invisible — pure opacity reveal
            if (expanded)
            { var sc = isHome ? ExpHomeOverlayScale : ExpAwayOverlayScale; sc.ScaleX = 1; sc.ScaleY = 1; }
            else
            { overlayText.RenderTransformOrigin = new RelativePoint(0.5, 0.5,RelativeUnit.Relative); overlayText.RenderTransform = new ScaleTransform(1, 1); }

            // No gradient sweep in cinematic
            gradBar.Opacity = 0;
            overlayText.Opacity = 0;

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            var decelEase = new CubicEase { EasingMode = EasingMode.EaseOut };
            var slowDecel = new QuarticEase { EasingMode = EasingMode.EaseOut };
            double panelWidth = expanded ? 543 : 707;

            // Cinematic entry: the panel itself fades up to full coverage
            // (so the row is never partially exposed), while the gradient
            // bar slowly slides across to carry the "horizontal cinematic
            // motion" signature on top of the masked backdrop.
            overlay.RenderTransform = new TranslateTransform(0, 0);
            overlay.Opacity = 0;
            sb.Children.Add(MakeDA(overlay, OpacityProperty, 0, 1, 0.45, 0.0, slowDecel));

            // Slow horizontal accent bar drift gives the cinematic feel
            // without exposing the row.
            gradBar.Opacity = 0.25;
            var (cinGradFrom, cinGradTo) = GetGradSweep(overlay, expanded);
            sb.Children.Add(MakeDA(gradBar, "RenderTransform.(TranslateTransform.X)", cinGradFrom, cinGradTo, 4.0, 0.2, ease));

            // Score glow slow rise (peaks around 1.5s, holds, decays)
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 0, 0.85, 1.2, 0.3, decelEase));
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 0.85, 0.6, 1.5, 1.5, ease));
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 0.6, 0, 1.8, 3.0, ease));

            // Text fades in slowly (0.6 – 1.4s)
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 0, 1, 0.8, 0.6, decelEase));

            // Glow breathes — 3 full inhale/exhale cycles across the hold
            sb.Children.Add(MakeDA(overlayText, "(Control.Effect).(DropShadowEffect.BlurRadius)", 25, 95, 0.8, 0.6, ease));
            sb.Children.Add(MakeDA(overlayText, "(Control.Effect).(DropShadowEffect.BlurRadius)", 95, 35, 0.8, 1.4, ease));
            sb.Children.Add(MakeDA(overlayText, "(Control.Effect).(DropShadowEffect.BlurRadius)", 35, 90, 0.7, 2.2, ease));
            sb.Children.Add(MakeDA(overlayText, "(Control.Effect).(DropShadowEffect.BlurRadius)", 90, 30, 0.8, 2.9, ease));
            sb.Children.Add(MakeDA(overlayText, "(Control.Effect).(DropShadowEffect.BlurRadius)", 30, 70, 0.5, 3.7, ease));

            // Exit: text fades first, then the panel fades — no slide-off
            // so the row underneath is never half-exposed.
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 1, 0, 0.6, 4.20, ease));
            sb.Children.Add(MakeDA(overlay, OpacityProperty, 1, 0, 0.7, 4.50, ease));

            return 5.0;
        }

        // ==================================================================
        // CLEAN — Modern minimalist / understated
        // ------------------------------------------------------------------
        // Structural fingerprint: a calm, fully opaque team-colour panel
        // fades up over the scoring row; a single restrained "GOAL!"
        // wordmark fades in centred at full size (NO scaling, NO sweeps,
        // NO shimmer) — the typography itself carries the moment. A
        // gentle score-glow heartbeat and total-digit swell run beneath
        // so the score still reacts. Nothing strobes, nothing sweeps.
        // Entry: panel opacity fade in, text opacity fade in.
        // Hold:  GOAL holds steady, score heartbeat, total digit swell.
        // Exit:  text fades, then panel fades — no slide, no flicker.
        // Duration ~2.8s.
        // ==================================================================

        private double BuildCleanGoal(Storyboard sb, bool isHome, bool expanded)
        {
            var primaryColor = isHome ? _homeColor : _awayColor;
            var secondaryColor = isHome ? _homeSecondaryColor : _awaySecondaryColor;
            PrepareOverlay(isHome, expanded, primaryColor, secondaryColor);
            ApplyExpandedGoalChrome(isHome, expanded, GoalAnimationStyle.Clean);
            PrepareScoreGlow(isHome, expanded, primaryColor);

            var overlay = GetOverlay(isHome, expanded);
            var overlayText = GetOverlayText(isHome, expanded);
            var gradBar = GetGradBar(isHome, expanded);
            var shimmer = GetShimmer(isHome, expanded);
            var scoreGlow = GetScoreGlow(isHome, expanded);
            var edgeGlow = GetEdgeGlow(isHome, expanded);

            overlayText.Text = "GOAL";
            overlayText.FontFamily = NativeFonts.Family("Arial Black");
            overlayText.FontStretch = FontStretch.Normal;

            GetOverlayBg(isHome, expanded).Color = Color.FromArgb(0xFF, primaryColor.R, primaryColor.G, primaryColor.B);
            EnsureOverlayContrast(isHome, expanded, secondaryColor);
            overlayText.FontSize = expanded ? 116 : 84;

            // No sweep, no shimmer — clean is defined by their absence.
            gradBar.Opacity = 0;
            shimmer.Opacity = 0;

            // A single decisive camera punch differentiates this preset from
            // Broadcast's vertical fold and Electric's horizontal charge.
            if (expanded)
            { var sc = isHome ? ExpHomeOverlayScale : ExpAwayOverlayScale; sc.ScaleX = 1.34; sc.ScaleY = 1.34; }
            else
            { overlayText.RenderTransformOrigin = new RelativePoint(0.5, 0.5,RelativeUnit.Relative); overlayText.RenderTransform = new ScaleTransform(1.34, 1.34); }
            overlayText.Opacity = 0;

            // Restrained glow — soft, slow, no pulsing.
            if (overlayText.Effect is Avalonia.Media.DropShadowEffect g)
            { g.Color = secondaryColor; g.BlurRadius = 30; g.Opacity = 0.6; }

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            var elasticEase = new ElasticEase { Oscillations = 1, Springiness = 5, EasingMode = EasingMode.EaseOut };
            var decelEase = new CubicEase { EasingMode = EasingMode.EaseOut };

            // Entry: panel fades up to full coverage, then text fades in.
            overlay.RenderTransform = new TranslateTransform(0, 0);
            overlay.Opacity = 0;
            sb.Children.Add(MakeDA(overlay, OpacityProperty, 0, 1, 0.18, 0.00, decelEase));
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 0, 1, 0.16, 0.04, decelEase));
            sb.Children.Add(MakeDA(overlayText, "RenderTransform.ScaleX", 1.34, 1, 0.34, 0.02, decelEase));
            sb.Children.Add(MakeDA(overlayText, "RenderTransform.ScaleY", 1.34, 1, 0.34, 0.02, decelEase));

            // Single slow glow breath on the text (no pulses).
            sb.Children.Add(MakeDA(overlayText, "(Control.Effect).(DropShadowEffect.BlurRadius)", 30, 55, 0.8, 0.5, ease));
            sb.Children.Add(MakeDA(overlayText, "(Control.Effect).(DropShadowEffect.BlurRadius)", 55, 35, 0.8, 1.3, ease));

            // Soft edge accent — single sweep, no strobing.
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 0, 0.6, 0.20, 0.10, decelEase));
            sb.Children.Add(MakeDA(edgeGlow, OpacityProperty, 0.6, 0, 1.2, 0.50, ease));

            // Heartbeat on the score area (subtler than overlay-less version).
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 0, 0.85, 0.10, 0.00, decelEase));
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 0.85, 0.35, 0.25, 0.10, ease));
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 0.35, 0.75, 0.15, 0.50, ease));
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 0.75, 0, 1.6, 0.70, ease));

            // Elastic swell on the total digit — preserves any existing flip transform.
            var totalText = expanded
                ? (isHome ? ExpHomeTotalText : ExpAwayTotalText)
                : (isHome ? HomeTotalText : AwayTotalText);
            totalText.RenderTransformOrigin = new RelativePoint(0.5, 0.5,RelativeUnit.Relative);
            var swell = new ScaleTransform(1, 1);
            var swellGroup = new TransformGroup();
            if (totalText.RenderTransform is TransformGroup existing)
            {
                foreach (var child in existing.Children) swellGroup.Children.Add(child);
            }
            else if (totalText.RenderTransform is Transform t && t != new MatrixTransform(Matrix.Identity))
            {
                swellGroup.Children.Add(t);
            }
            swellGroup.Children.Add(swell);
            totalText.RenderTransform = swellGroup;

            sb.Children.Add(MakeDA(swell, ScaleTransform.ScaleXProperty, 1, 1.18, 0.55, 0.0, elasticEase));
            sb.Children.Add(MakeDA(swell, ScaleTransform.ScaleYProperty, 1, 1.18, 0.55, 0.0, elasticEase));
            sb.Children.Add(MakeDA(swell, ScaleTransform.ScaleXProperty, 1.18, 1, 0.45, 0.6, decelEase));
            sb.Children.Add(MakeDA(swell, ScaleTransform.ScaleYProperty, 1.18, 1, 0.45, 0.6, decelEase));

            // Exit: text first, then panel — no slide, no flicker.
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 1, 0, 0.35, 2.20, ease));
            sb.Children.Add(MakeDA(overlay, OpacityProperty, 1, 0, 0.30, 2.50, ease));

            return 2.6;
        }

        // ==================================================================
        // CLASSIC — Retro arcade / vintage flicker
        // ------------------------------------------------------------------
        // Structural fingerprint: Looks like a 1980s scoreboard bulb display.
        // Overlay stutters on like incandescent bulbs warming up, holds with
        // an irregular flicker (random-feeling but deterministic), then
        // flickers off. No gradient sweep, no shimmer — that is intentional
        // and is what makes it READ as "old hardware" instead of "modern TV".
        // Warm amber/secondary tint on the text. Score digits get a single
        // settled glow (no double-tap — too modern).
        // Duration ~2.6s.
        // ==================================================================

        private double BuildClassicGoal(Storyboard sb, bool isHome, bool expanded)
        {
            var primaryColor = isHome ? _homeColor : _awayColor;
            var secondaryColor = isHome ? _homeSecondaryColor : _awaySecondaryColor;
            PrepareOverlay(isHome, expanded, primaryColor, secondaryColor);
            ApplyExpandedGoalChrome(isHome, expanded, GoalAnimationStyle.Classic);
            PrepareScoreGlow(isHome, expanded, primaryColor);

            var overlay = GetOverlay(isHome, expanded);
            var overlayText = GetOverlayText(isHome, expanded);
            var gradBar = GetGradBar(isHome, expanded);
            var shimmer = GetShimmer(isHome, expanded);
            var scoreGlow = GetScoreGlow(isHome, expanded);

            overlayText.Text = "GOAL";
            overlayText.FontFamily = NativeFonts.Family("Arial Black");
            overlayText.FontStretch = FontStretch.Expanded;

            GetOverlayBg(isHome, expanded).Color = Color.FromArgb(0xFF, primaryColor.R, primaryColor.G, primaryColor.B);
            EnsureOverlayContrast(isHome, expanded, secondaryColor);
            overlayText.FontSize = expanded ? 108 : 78;

            // Vintage hardware: no gradient sweep, no shimmer
            gradBar.Opacity = 0;
            shimmer.Opacity = 0;

            // Text starts at full scale (bulb either lit or unlit, no scaling)
            if (expanded)
            { var sc = isHome ? ExpHomeOverlayScale : ExpAwayOverlayScale; sc.ScaleX = 1; sc.ScaleY = 1; }
            else
            { overlayText.RenderTransformOrigin = new RelativePoint(0.5, 0.5,RelativeUnit.Relative); overlayText.RenderTransform = new ScaleTransform(1, 1); }

            // Warm bulb-glow on the text
            if (overlayText.Effect is Avalonia.Media.DropShadowEffect g)
            { g.Color = secondaryColor; g.BlurRadius = 25; g.Opacity = 0.95; }

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            var decelEase = new CubicEase { EasingMode = EasingMode.EaseOut };

            // Bulb warm-up: the PANEL snaps to full coverage immediately so
            // the row is masked; the bulb stutter happens on the TEXT only,
            // which reads as the lit bulb "warming up" against the housing.
            sb.Children.Add(MakeDA(overlay, OpacityProperty, 0, 1, 0.04, 0.00, decelEase));

            // Text bulb warm-up: off, on, off, on, dim, on full (0.06 – 0.40s)
            overlayText.Opacity = 0;
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 0, 1, 0.02, 0.06));
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 1, 0, 0.03, 0.09));
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 0, 1, 0.02, 0.17));
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 1, 0.4, 0.04, 0.21));
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 0.4, 1, 0.10, 0.30, decelEase));

            // Bulb glow pulses irregularly during the hold (0.6 – 2.0s)
            sb.Children.Add(MakeDA(overlayText, "(Control.Effect).(DropShadowEffect.BlurRadius)", 25, 55, 0.20, 0.50));
            sb.Children.Add(MakeDA(overlayText, "(Control.Effect).(DropShadowEffect.BlurRadius)", 55, 30, 0.35, 0.70, ease));
            sb.Children.Add(MakeDA(overlayText, "(Control.Effect).(DropShadowEffect.BlurRadius)", 30, 60, 0.18, 1.10));
            sb.Children.Add(MakeDA(overlayText, "(Control.Effect).(DropShadowEffect.BlurRadius)", 60, 35, 0.40, 1.28, ease));
            sb.Children.Add(MakeDA(overlayText, "(Control.Effect).(DropShadowEffect.BlurRadius)", 35, 45, 0.20, 1.70));

            // Mid-hold flicker dip (1.05s) — on the bulb (text) only, the
            // housing (panel) stays fully opaque so the row stays masked.
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 1, 0.55, 0.03, 1.05));
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 0.55, 1, 0.05, 1.08));

            // Single warm settled score glow — no double-tap
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 0, 0.7, 0.4, 0.30, decelEase));
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 0.7, 0, 1.4, 1.00, ease));

            // Bulb burnout exit (2.1 – 2.5s): the BULB (text) flickers and
            // dies; the housing (panel) only fades after the bulb is fully
            // out so the row underneath is never exposed mid-flicker.
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 1, 0.3, 0.03, 2.10));
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 0.3, 0.9, 0.03, 2.13));
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 0.9, 0.15, 0.04, 2.18));
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 0.15, 0.55, 0.03, 2.22));
            sb.Children.Add(MakeDA(overlayText, OpacityProperty, 0.55, 0, 0.15, 2.25, decelEase));
            sb.Children.Add(MakeDA(overlay, OpacityProperty, 1, 0, 0.20, 2.42, decelEase));

            return 2.4;
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

            // Subtle score area glow (0°0.8s)
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 0, 0.5, 0.1, 0.0, decelEase));
            sb.Children.Add(MakeDA(scoreGlow, OpacityProperty, 0.5, 0, 0.7, 0.1, ease));

            return 0.0;
        }

        // ---- Shared flip builder ----

        private void AddFlipAnimations(Storyboard sb, bool isHome, double flipStart,
            int newGoals, int newBehinds, int newTotal, bool expanded,
            GoalAnimationStyle revealStyle, Action? onCompleted = null)
        {
            var goalsText = expanded ? (isHome ? ExpHomeGoalsText : ExpAwayGoalsText) : (isHome ? HomeGoalsText : AwayGoalsText);
            var behindsText = expanded ? (isHome ? ExpHomeBehindsText : ExpAwayBehindsText) : (isHome ? HomeBehindsText : AwayBehindsText);
            var totalText = expanded ? (isHome ? ExpHomeTotalText : ExpAwayTotalText) : (isHome ? HomeTotalText : AwayTotalText);
            // Also keep the other layout in sync
            var altG = expanded ? (isHome ? HomeGoalsText : AwayGoalsText) : (isHome ? ExpHomeGoalsText : ExpAwayGoalsText);
            var altB = expanded ? (isHome ? HomeBehindsText : AwayBehindsText) : (isHome ? ExpHomeBehindsText : ExpAwayBehindsText);
            var altT = expanded ? (isHome ? HomeTotalText : AwayTotalText) : (isHome ? ExpHomeTotalText : ExpAwayTotalText);

            // Keep glyph rasterisation stable while numerals travel through
            // fractional positions; otherwise ClearType can briefly shimmer.
            TextBlock[] animatedDigits = [goalsText, behindsText, totalText];
            foreach (TextBlock digit in animatedDigits)
                NativeCache.Set(digit, new NativeBitmapCache(VisualTreeHelper.GetDpi(digit).PixelsPerDip));

            // Detect which components actually changed for blink highlighting
            bool goalsChanged = (goalsText.Text ?? string.Empty) != newGoals.ToString();
            bool behindsChanged = (behindsText.Text ?? string.Empty) != newBehinds.ToString();

            var snapEase = new CubicEase { EasingMode = EasingMode.EaseIn };
            var bounceEase = new CubicEase { EasingMode = EasingMode.EaseOut };
            var popEase = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut };
            // Digit slide travels ~0.6 of the cell's actual rendered height so the
            // flip stays visually consistent at any output-window size.
            double cellH = totalText.NativeHeight();
            if (cellH <= 0) cellH = goalsText.NativeHeight();
            if (cellH <= 0) cellH = behindsText.NativeHeight();
            double slideDist = cellH > 0 ? cellH * 0.6 : ScalePx(60);
            var slideOutDur = TimeSpan.FromSeconds(0.10);
            var slideInDur = TimeSpan.FromSeconds(0.16);
            var timers = isHome ? _homeTimers : _awayTimers;

            void AddSlide(TextBlock tb, string val, double delay, TextBlock alt, bool pop = false)
            {
                var begin = TimeSpan.FromSeconds(flipStart + delay);
                // Slide old value UP and out (off the top of the cell)
                var so = new DoubleAnimation(0, -slideDist, slideOutDur) { BeginTime = begin, EasingFunction = snapEase };
                Storyboard.SetTarget(so, tb); Storyboard.SetTargetProperty(so, new PropertyPath("RenderTransform.(TranslateTransform.Y)")); sb.Children.Add(so);
                // Slide new value UP into place from below ° G/B values pop with overshoot
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
                const double offDur = 0.055;
                const double onDur = 0.075;
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
            double swipeRevealOffset = GetScoreRevealOffset(revealStyle);
            double totalDur;

            if (deferTotalForSwipe)
            {
                // 1) Schedule the swipe to start AFTER the goal preset has finished.
                // 2) When the swipe reveals, slide G/B/Total to their new values together
                //    so the digits change in sync with the swipe pulling away.
                ScheduleScoreSwipeWithUpdate(isHome, delta, /*delaySec*/ flipStart, revealStyle);

                double slideAt = swipeRevealOffset; // relative to flipStart
                AddSlide(totalText, newTotal.ToString(), slideAt, altT);
                if (goalsChanged) AddSlide(goalsText, newGoals.ToString(), slideAt, altG, pop: true);
                if (behindsChanged) AddSlide(behindsText, newBehinds.ToString(), slideAt, altB, pop: true);

                // Synced blink right after the slide-in completes
                double blinkTime = flipStart + slideAt + 0.10 + 0.16 + 0.10;
                if (goalsChanged) AddBlink(goalsText, blinkTime);
                if (behindsChanged) AddBlink(behindsText, blinkTime);
                AddBlink(totalText, blinkTime);

                totalDur = blinkTime + 4 * (0.055 + 0.075) + 0.18;
            }
            else
            {
                AddSlide(totalText, newTotal.ToString(), 0.0, altT);
                if (goalsChanged) AddSlide(goalsText, newGoals.ToString(), 0.10, altG, pop: true);
                if (behindsChanged) AddSlide(behindsText, newBehinds.ToString(), 0.10, altB, pop: true);

                double blinkTime = flipStart + 0.10 + 0.10 + 0.16 + 0.10;
                if (goalsChanged) AddBlink(goalsText, blinkTime);
                if (behindsChanged) AddBlink(behindsText, blinkTime);
                AddBlink(totalText, blinkTime);

                totalDur = blinkTime + 4 * (0.055 + 0.075) + 0.12;
            }

            var guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(totalDur) };
            guard.Tick += (_, __) =>
            {
                guard.Stop(); timers.Remove(guard);
                // Retain the stable glyph cache after motion. Switching back
                // to live ClearType at the final flash produced a one-frame
                // change in numeral edges that looked like a panel twitch.
                if (isHome) _homeAnimating = false; else _awayAnimating = false;
                // Fire the host-supplied completion callback now — after the
                // swipe slide-out has finished — so the next queued goal
                // doesn't cancel an in-flight +N swipe.
                if (onCompleted is not null)
                {
                    try { onCompleted(); } catch { /* never let host crash storyboard */ }
                }
            };
            timers.Add(guard); guard.Start();
        }

        // ---- Score swipe (+1 / +6) ----

        private void ScheduleScoreSwipe(bool isHome, int delta, double delaySec, GoalAnimationStyle revealStyle)
        {
            var timers = isHome ? _homeTimers : _awayTimers;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(delaySec) };
            t.Tick += (_, __) => { t.Stop(); timers.Remove(t); PlayScoreSwipe(isHome, delta, revealStyle); };
            timers.Add(t); t.Start();
        }

        private void ScheduleScoreSwipeWithUpdate(bool isHome, int delta, double delaySec, GoalAnimationStyle revealStyle)
        {
            var timers = isHome ? _homeTimers : _awayTimers;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(delaySec) };
            t.Tick += (_, __) =>
            {
                t.Stop(); timers.Remove(t);
                // Reveal callback is intentionally null: the digit slide animations
                // (Goals/Behinds/Total) are scheduled in AddFlipAnimations to fire at
                // the swipe's reveal moment, so they update in sync visually.
                PlayScoreSwipe(isHome, delta, revealStyle);
            };
            timers.Add(t); t.Start();
        }

        private static double GetScoreRevealOffset(GoalAnimationStyle style) => style switch
        {
            GoalAnimationStyle.Electric => 0.72,
            GoalAnimationStyle.Cinematic => 0.88,
            GoalAnimationStyle.Clean => 0.68,
            GoalAnimationStyle.Classic => 0.76,
            _ => 0.80
        };

        private void PlayScoreSwipe(bool isHome, int delta, GoalAnimationStyle revealStyle, Action? onReveal = null)
        {
            var timers = isHome ? _homeTimers : _awayTimers;
            var overlay = IsExpanded
                ? (isHome ? ExpHomeSwipeOverlay : ExpAwaySwipeOverlay)
                : (isHome ? HomeSwipeOverlay : AwaySwipeOverlay);
            var translate = IsExpanded
                ? (isHome ? ExpHomeSwipeTranslate : ExpAwaySwipeTranslate)
                : (isHome ? HomeSwipeTranslate : AwaySwipeTranslate);
            var swipeText = IsExpanded
                ? (isHome ? ExpHomeSwipeText : ExpAwaySwipeText)
                : (isHome ? HomeSwipeText : AwaySwipeText);

            swipeText.Text = delta == 6 ? "+6" : "+1";
            swipeText.IsVisible = true;
            swipeText.BeginAnimation(OpacityProperty, null);
            swipeText.Opacity = 1;

            // Resolve contrast at display time, after the current team palette has
            // been applied. This guarantees a visible numeral even for unusual
            // primary/secondary combinations imported from the club library.
            swipeText.Foreground = Services.SmartContrast.PickForegroundBrush(overlay.Background);

            double h = overlay.NativeHeight() > 0 ? overlay.NativeHeight() : 200;
            double w = overlay.NativeWidth() > 0 ? overlay.NativeWidth() : 220;
            swipeText.FontSize = Math.Max(34, Math.Floor(Math.Min(h * 0.72, w * 0.42)));
            swipeText.ZIndex = 101;

            // Reset any prior render transform / opacity animation.
            overlay.RenderTransform = translate;
            overlay.RenderTransformOrigin = new RelativePoint(0.5, 0.5,RelativeUnit.Relative);
            overlay.BeginAnimation(OpacityProperty, null);
            overlay.Opacity = revealStyle is GoalAnimationStyle.Cinematic or GoalAnimationStyle.Clean ? 0 : 1;
            swipeText.RenderTransformOrigin = new RelativePoint(0.5, 0.5,RelativeUnit.Relative);
            swipeText.RenderTransform = new ScaleTransform(1, 1);
            translate.BeginAnimation(TranslateTransform.YProperty, null);
            translate.BeginAnimation(TranslateTransform.XProperty, null);
            translate.X = 0;
            translate.Y = 0;

            // The score delta always travels upward. Goal-animation presets may
            // still differ before this moment, but +6/+1 has one predictable
            // direction, duration and reveal language throughout the app.
            var entryEase = new CubicEase { EasingMode = EasingMode.EaseOut };
            var exitEase = new CubicEase { EasingMode = EasingMode.EaseIn };
            overlay.Opacity = 1;
            translate.Y = h;
            translate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(h, 0, TimeSpan.FromSeconds(0.34)) { EasingFunction = entryEase });

            // 2) After a short hold, swipe up off the top and reveal the new score.
            var holdAndExitDelay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(GetScoreRevealOffset(revealStyle)) };
            timers.Add(holdAndExitDelay);
            holdAndExitDelay.Tick += (_, __) =>
            {
                holdAndExitDelay.Stop();
                timers.Remove(holdAndExitDelay);
                onReveal?.Invoke();

                DoubleAnimation exitMotion = new DoubleAnimation(0, -h, TimeSpan.FromSeconds(0.32)) { EasingFunction = exitEase };
                AvaloniaProperty exitProperty = TranslateTransform.YProperty;

                exitMotion.Completed += (_, __) =>
                {
                    overlay.Opacity = 0;
                    translate.BeginAnimation(TranslateTransform.YProperty, null);
                    translate.BeginAnimation(TranslateTransform.XProperty, null);
                    translate.X = 0;
                    translate.Y = 0;
                };
                if (exitProperty == Control.OpacityProperty)
                    overlay.BeginAnimation(OpacityProperty, (DoubleAnimation)exitMotion);
                else
                    translate.BeginAnimation(exitProperty, (DoubleAnimation)exitMotion);

                // Keep the panel fully opaque while it leaves. Fading during
                // the upward motion made the exit feel unlike the solid entry
                // and exposed the score underneath before the wipe had cleared.
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
            overlayText.Foreground = Avalonia.Media.Brushes.White;
            var gradBar = isHome ? HomeGradientBar : AwayGradientBar;
            gradBar.Opacity = 0.6; // restore default
            var scoreGlow = isHome ? HomeScoreGlow : AwayScoreGlow;
            scoreGlow.BeginAnimation(OpacityProperty, null); scoreGlow.Opacity = 0;
            var edgeGlow = isHome ? HomeEdgeGlow : AwayEdgeGlow;
            edgeGlow.BeginAnimation(OpacityProperty, null); edgeGlow.Opacity = 0;
            var swipeOverlay = isHome ? HomeSwipeOverlay : AwaySwipeOverlay;
            var swipeTr = isHome ? HomeSwipeTranslate : AwaySwipeTranslate;
            swipeOverlay.BeginAnimation(OpacityProperty, null); swipeOverlay.Opacity = 0;
            swipeTr.BeginAnimation(TranslateTransform.YProperty, null); swipeTr.Y = 0;
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
            overlayText.Foreground = Avalonia.Media.Brushes.White;
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

        public void CancelScoreAnimations()
        {
            // Corrections, undo and match reset must invalidate both layout
            // variants. Otherwise a completion timer from an old score can
            // write its stale snapshot back after the authoritative correction.
            CancelClassicTeamAnimation(isHome: true);
            CancelClassicTeamAnimation(isHome: false);
            CancelExpandedTeamAnimation(isHome: true);
            CancelExpandedTeamAnimation(isHome: false);
            _homeAnimating = false;
            _awayAnimating = false;
            _gbExpandDelayTimer?.Stop();
            _gbExpandDelayTimer = null;
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
            NativeCache.Set(goalsText, null);
            NativeCache.Set(behindsText, null);
            NativeCache.Set(totalText, null);
            // Restore transforms if highlight/Clean replaced them with TransformGroup
            if (goalsText.RenderTransform is not TranslateTransform)
                goalsText.RenderTransform = gf;
            if (behindsText.RenderTransform is not TranslateTransform)
                behindsText.RenderTransform = bf;
            if (totalText.RenderTransform is not TranslateTransform)
                totalText.RenderTransform = tf;
        }

        // ---- Storyboard helper ----

        private static DoubleAnimation MakeDA(AvaloniaObject target, AvaloniaProperty dp, double from, double to, double durSec, double beginSec = 0, IEasingFunction? ease = null)
        {
            var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(durSec)) { EasingFunction = ease };
            if (beginSec > 0) a.BeginTime = TimeSpan.FromSeconds(beginSec);
            Storyboard.SetTarget(a, target); Storyboard.SetTargetProperty(a, new PropertyPath(dp)); return a;
        }

        private static DoubleAnimation MakeDA(AvaloniaObject target, string path, double from, double to, double durSec, double beginSec = 0, IEasingFunction? ease = null)
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
        private TextBlock AScorelessLabel => IsExpanded ? ExpScorelessLabelText : ScorelessLabelText;
        private TextBlock AScorelessTime => IsExpanded ? ExpScorelessTimeText : ScorelessTimeText;
        private Border AScoringRunBar => IsExpanded ? ExpScoringRunBarBorder : ScoringRunBarBorder;
        private Avalonia.Media.IBrush AScoringRunGrad => IsExpanded ? (Avalonia.Media.IBrush)ExpScoringRunGradient : (Avalonia.Media.IBrush)ScoringRunGradient;
        private TextBlock AScoringRunLabel => IsExpanded ? ExpScoringRunLabelText : ScoringRunLabelText;
        private Border ARecentScoresBar => ExpRecentScoresBarBorder;
        private Border ADroughtBar => IsExpanded ? ExpTeamDroughtBarBorder : TeamDroughtBarBorder;
        private Avalonia.Media.IBrush ADroughtGrad => IsExpanded ? (Avalonia.Media.IBrush)ExpTeamDroughtGradient : (Avalonia.Media.IBrush)TeamDroughtGradient;
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
        private Border AWarningOverlay => IsExpanded ? ExpWarningBanner : WarningOverlay;
        private StackPanel AWarningLetterHost => IsExpanded ? ExpWarningBannerLetterHost : WarningLetterHost;
        private TranslateTransform AWarningStripes => IsExpanded ? ExpWarningBannerStripesTranslate : WarningStripesTranslate;
        private Border AWarningGlow => IsExpanded ? ExpWarningBannerGlowBorder : WarningGlowBorder;
        private TranslateTransform AWarningScanline => IsExpanded ? ExpWarningBannerScanlineTranslate : WarningScanlineTranslate;
        private Avalonia.Controls.Shapes.Rectangle AWarningScanlineRect => IsExpanded ? ExpWarningBannerScanline : WarningScanline;
        private Avalonia.Controls.Shapes.Rectangle AWarningFlash => IsExpanded ? ExpWarningBannerFlash : WarningFlash;
        private ScaleTransform AWarningPulse => IsExpanded ? ExpWarningBannerLetterPulse : WarningLetterPulse;
        private TextBlock AQuarterText => IsExpanded ? ExpQuarterNumberText : QuarterNumberText;
        private TextBlock AClockText => IsExpanded ? ExpClockText : ClockText;
        private ScaleTransform AQuarterTextScale => IsExpanded ? ExpQuarterNumberTextScale : QuarterNumberTextScale;
        private ScaleTransform AClockTextScale => IsExpanded ? ExpClockTextScale : ClockTextScale;
        private double ScaleUnit => ScoreboardScaleHelper.GetScale(Bounds.Width, Bounds.Height);
        private double ScalePx(double designValue) => ScoreboardScaleHelper.Scale(designValue, ScaleUnit);
        private double OverlayH(double designHeight)
        {
            // Overlay constants are authored for the real 386x193 operating
            // canvas, not the old 960x540 mock-up canvas. Scale from that real
            // height, while reserving at least three quarters of the output for
            // the information bar and live score panels.
            double defaultScale = Math.Clamp(Bounds.Height / 193.0, 0.62, 1.60);
            double scaled = designHeight * defaultScale;
            double heightBudget = Math.Clamp(Bounds.Height * 0.26, 22, 92);
            return Math.Min(scaled, heightBudget);
        }

        private static double EaseOverlayTransition(double progress)
        {
            progress = Math.Clamp(progress, 0, 1);
            return progress * progress * (3 - (2 * progress));
        }

        private static IEasingFunction CreateOverlayTransitionEase()
            => BroadcastMotion.CreateOverlayEase();

        private static IEasingFunction CreateTeamStoryTransitionEase()
            => new SineEase { EasingMode = EasingMode.EaseInOut };

        private int BeginOverlayLayoutMotion()
        {
            _overlayLayoutMotionActive = true;
            _overlayResponsiveRefreshPending = false;
            return ++_overlayLayoutMotionGeneration;
        }

        private void EndOverlayLayoutMotion(int generation)
        {
            if (generation != _overlayLayoutMotionGeneration) return;
            _overlayLayoutMotionActive = false;
            if (_overlayResponsiveRefreshPending)
            {
                _overlayResponsiveRefreshPending = false;
                QueueExpandedResponsiveLayout();
            }
        }

        private void AnimateOverlayIn(Border bar, double designHeight)
        {
            double target = OverlayH(designHeight);
            bool carriesSharedIdentity = ReferenceEquals(bar, AStatsBar)
                || ReferenceEquals(bar, AWinProbBar)
                || ReferenceEquals(bar, ALeadChangesBar)
                || ReferenceEquals(bar, ExpQuarterScoresBarBorder);
            int motionGeneration = BeginOverlayLayoutMotion();
            bar.BeginAnimation(HeightProperty, null);
            bar.BeginAnimation(OpacityProperty, null);
            NativeCache.Set(bar, new NativeBitmapCache(1.0));
            bar.UseLayoutRounding = false;
            bar.UseLayoutRounding = false;
            bar.Height = target;
            bar.Opacity = 1;
            var translate = bar.RenderTransform as TranslateTransform ?? new TranslateTransform();
            bar.RenderTransform = translate;
            translate.BeginAnimation(TranslateTransform.YProperty, null);
            translate.Y = 0;
            var heightAnimation = new DoubleAnimation(0, target, OverlayTransitionDuration)
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = CreateOverlayTransitionEase()
            };
            heightAnimation.Completed += (_, _) =>
            {
                NativeCache.Set(bar, null);
                bar.UseLayoutRounding = true;
                bar.UseLayoutRounding = true;
                EndOverlayLayoutMotion(motionGeneration);
            };
            bar.BeginAnimation(HeightProperty, heightAnimation, HandoffBehavior.SnapshotAndReplace);
            bar.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, OverlayTransitionDuration)
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = CreateOverlayTransitionEase()
            });
            translate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-Math.Max(8, target * 0.28), 0, OverlayTransitionDuration)
                {
                    FillBehavior = FillBehavior.Stop,
                    EasingFunction = CreateOverlayTransitionEase()
                });
            if (carriesSharedIdentity)
            {
                ExpSharedOverlayLogoLayer.BeginAnimation(HeightProperty, null);
                ExpSharedOverlayLogoLayer.BeginAnimation(OpacityProperty, null);
                var identityTranslate = ExpSharedOverlayLogoLayer.RenderTransform as TranslateTransform
                    ?? new TranslateTransform();
                ExpSharedOverlayLogoLayer.RenderTransform = identityTranslate;
                identityTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                ExpSharedOverlayLogoLayer.Height = target;
                ExpSharedOverlayLogoLayer.Opacity = 1;
                identityTranslate.Y = 0;
                ExpSharedOverlayLogoLayer.BeginAnimation(HeightProperty,
                    new DoubleAnimation(0, target, OverlayTransitionDuration)
                    {
                        FillBehavior = FillBehavior.Stop,
                        EasingFunction = CreateOverlayTransitionEase()
                    }, HandoffBehavior.SnapshotAndReplace);
                ExpSharedOverlayLogoLayer.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, OverlayTransitionDuration)
                    {
                        FillBehavior = FillBehavior.Stop,
                        EasingFunction = CreateOverlayTransitionEase()
                    }, HandoffBehavior.SnapshotAndReplace);
                identityTranslate.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(-Math.Max(8, target * 0.28), 0, OverlayTransitionDuration)
                    {
                        FillBehavior = FillBehavior.Stop,
                        EasingFunction = CreateOverlayTransitionEase()
                    }, HandoffBehavior.SnapshotAndReplace);
            }
        }

        private void AnimateOverlayOut(Border bar, double designHeight, Action completed)
        {
            double from = bar.NativeHeight() > 0 ? bar.NativeHeight() : OverlayH(designHeight);
            bool carriesSharedIdentity = ReferenceEquals(bar, AStatsBar)
                || ReferenceEquals(bar, AWinProbBar)
                || ReferenceEquals(bar, ALeadChangesBar)
                || ReferenceEquals(bar, ExpQuarterScoresBarBorder);
            int motionGeneration = BeginOverlayLayoutMotion();
            bar.BeginAnimation(HeightProperty, null);
            bar.BeginAnimation(OpacityProperty, null);
            NativeCache.Set(bar, new NativeBitmapCache(1.0));
            bar.UseLayoutRounding = false;
            bar.UseLayoutRounding = false;
            bar.Height = 0;
            bar.Opacity = 0;
            var translate = bar.RenderTransform as TranslateTransform ?? new TranslateTransform();
            bar.RenderTransform = translate;
            translate.BeginAnimation(TranslateTransform.YProperty, null);
            translate.Y = -Math.Max(8, from * 0.28);
            var animation = new DoubleAnimation(from, 0, OverlayTransitionDuration)
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = CreateOverlayTransitionEase()
            };
            animation.Completed += (_, _) =>
            {
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.Y = 0;
                NativeCache.Set(bar, null);
                bar.UseLayoutRounding = true;
                bar.UseLayoutRounding = true;
                EndOverlayLayoutMotion(motionGeneration);
                completed();
            };
            bar.BeginAnimation(HeightProperty, animation);
            bar.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, OverlayTransitionDuration)
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = CreateOverlayTransitionEase()
            });
            translate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, -Math.Max(8, from * 0.28), OverlayTransitionDuration)
                {
                    FillBehavior = FillBehavior.Stop,
                    EasingFunction = CreateOverlayTransitionEase()
                });
            if (carriesSharedIdentity)
            {
                ExpSharedOverlayLogoLayer.BeginAnimation(HeightProperty, null);
                ExpSharedOverlayLogoLayer.BeginAnimation(OpacityProperty, null);
                var identityTranslate = ExpSharedOverlayLogoLayer.RenderTransform as TranslateTransform
                    ?? new TranslateTransform();
                ExpSharedOverlayLogoLayer.RenderTransform = identityTranslate;
                identityTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                ExpSharedOverlayLogoLayer.Height = 0;
                ExpSharedOverlayLogoLayer.Opacity = 0;
                identityTranslate.Y = -Math.Max(8, from * 0.28);
                ExpSharedOverlayLogoLayer.BeginAnimation(HeightProperty,
                    new DoubleAnimation(from, 0, OverlayTransitionDuration)
                    {
                        FillBehavior = FillBehavior.Stop,
                        EasingFunction = CreateOverlayTransitionEase()
                    }, HandoffBehavior.SnapshotAndReplace);
                ExpSharedOverlayLogoLayer.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(1, 0, OverlayTransitionDuration)
                    {
                        FillBehavior = FillBehavior.Stop,
                        EasingFunction = CreateOverlayTransitionEase()
                    }, HandoffBehavior.SnapshotAndReplace);
                identityTranslate.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(0, -Math.Max(8, from * 0.28), OverlayTransitionDuration)
                    {
                        FillBehavior = FillBehavior.Stop,
                        EasingFunction = CreateOverlayTransitionEase()
                    }, HandoffBehavior.SnapshotAndReplace);
            }
        }

        // ==== Stats bar ====

        private bool _statsBarVisible;
        private MatchStats? _currentStats;
        private int _statsRotationIndex;
        private DispatcherTimer? _statsRotationTimer;
        private const double StatsBarHeight = 46;
        private const double StatsRotationSeconds = 5.0;

        private enum OverlayKind { StatsBar, FiveMinWarning, ScorelessTimer, ScoringRun, TeamDrought, WinProbability, LeadChanges, QuarterScores, RecentScores, WeatherStats, Forecast, RainForecast }
        private readonly Queue<OverlayKind> _overlayQueue = new();
        private OverlayKind? _activeOverlay;
        private bool _overlayIsExiting;
        private bool _overlayTransitionGap;
        private int _overlayAutoHideGeneration;
        private bool _teamStoryVisible;
        private bool _teamStoryIsHome;
        private int _teamStoryAnimationGeneration;
        private readonly Queue<Action> _scoreSurfaceReadyCallbacks = new();
        private static readonly TimeSpan OverlayHandoffGap = BroadcastMotion.OverlayHandoffGap;

        private DispatcherTimer StartOverlayAutoHide(TimeSpan delay, Action callback)
        {
            int generation = ++_overlayAutoHideGeneration;
            DispatcherTimer timer = new() { Interval = delay };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (generation != _overlayAutoHideGeneration) return;
                callback();
            };
            timer.Start();
            return timer;
        }

        private void EnqueueOverlay(OverlayKind kind, bool priority = false, bool restartIfActive = false)
        {
            // The ticker owns the lower information lane while any preset is
            // on air. Do not queue stale overlays behind it; they would appear
            // unexpectedly when an operator later unticks the final message.
            if (IsTickerActive && !priority) return;
            if (priority) _overlayQueue.Clear();
            if (_activeOverlay == kind && restartIfActive && !_overlayIsExiting)
            {
                // A refreshed instance of the same card (most notably quickly
                // switching Home/Away droughts) is a replacement, not a second
                // twenty-second item. Preserve the common exit/gap/entrance
                // choreography while ensuring the newly prepared content wins.
                _overlayQueue.Clear();
                _overlayQueue.Enqueue(kind);
                HideActiveOverlay();
                return;
            }
            if (_activeOverlay == kind && !restartIfActive && !_overlayIsExiting) return;
            if (_overlayQueue.Contains(kind)) return;
            _overlayQueue.Enqueue(kind);
            if (_activeOverlay == null && !_overlayTransitionGap)
            {
                ProcessNextOverlay();
            }
            else if (priority && _activeOverlay is not null && !_overlayIsExiting)
            {
                // Only urgent alerts replace an active card. Normal overlays
                // wait their turn instead of repeatedly chopping each other's
                // display time; urgent replacements still use the full shared
                // exit → clean-air gap → entrance choreography.
                HideActiveOverlay();
            }
        }

        public void PrepareManualOverlayHandoff()
        {
            // An operator click is an intentional replacement, not another
            // background rotation item. Remove stale queued cards and let the
            // active one complete the same smooth exit before the clicked card
            // enters.
            _overlayQueue.Clear();
            if (_activeOverlay is not null && !_overlayIsExiting)
                HideActiveOverlay();
        }

        public bool CancelCurrentOverlay()
        {
            _overlayQueue.Clear();
            if (_activeOverlay is null)
                return false;

            if (!_overlayIsExiting)
                HideActiveOverlay();
            return true;
        }

        /// <summary>
        /// Team takeovers deliberately cover both clubs' ordinary score rows.
        /// A scoring command received in that state is accepted by the control
        /// panel immediately, but its on-air animation is held until the
        /// takeover has completed the shared exit transition.
        /// </summary>
        public bool DeferScoreUntilTeamPanelsVisible(bool scoringTeamIsHome, Action scoreAction)
        {
            if (scoreAction is null || _activeOverlay is not (OverlayKind.ScoringRun or OverlayKind.TeamDrought))
                return false;

            // The featured club's ordinary score row remains visible beneath
            // its story, so its score can animate immediately. Only a score for
            // the displaced opponent needs the takeover to finish exiting.
            if (scoringTeamIsHome == _teamStoryIsHome)
                return false;

            _scoreSurfaceReadyCallbacks.Enqueue(scoreAction);
            _overlayQueue.Clear();
            if (!_overlayIsExiting)
                HideActiveOverlay();
            return true;
        }

        private void HideActiveOverlay()
        {
            if (_overlayIsExiting || _activeOverlay is null) return;
            _overlayIsExiting = true;
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
            if (_overlayQueue.Count == 0)
            {
                _activeOverlay = null;
                ExpSharedOverlayLogoLayer.IsVisible = false;
                AnimateTeamNamePanelsForOverlay(hide: false);
                return;
            }
            var next = _overlayQueue.Dequeue(); _activeOverlay = next;
            bool showSharedTeamIdentity = next is OverlayKind.StatsBar
                or OverlayKind.WinProbability or OverlayKind.LeadChanges
                or OverlayKind.QuarterScores;
            ExpSharedOverlayLogoLayer.BeginAnimation(HeightProperty, null);
            ExpSharedOverlayLogoLayer.BeginAnimation(OpacityProperty, null);
            ExpSharedOverlayLogoLayer.Height = 0;
            ExpSharedOverlayLogoLayer.Opacity = 0;
            ExpSharedOverlayLogoLayer.IsVisible = showSharedTeamIdentity
                ? true
                : false;
            bool isTeamTakeover = next is OverlayKind.ScoringRun or OverlayKind.TeamDrought;
            AnimateTeamNamePanelsForOverlay(hide: !isTeamTakeover);
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
            // Observers use IsOverlayActive to decide whether the output lane
            // is free. Clear the state before raising the event; previously the
            // event claimed the overlay was hidden while IsOverlayActive still
            // returned true for the old card.
            bool releasedTeamTakeover = _activeOverlay is OverlayKind.ScoringRun or OverlayKind.TeamDrought;
            _activeOverlay = null;
            _overlayIsExiting = false;
            _overlayTransitionGap = true;
            OverlayHidden?.Invoke();

            if (releasedTeamTakeover)
            {
                while (_scoreSurfaceReadyCallbacks.Count > 0)
                {
                    Action ready = _scoreSurfaceReadyCallbacks.Dequeue();
                    try { ready(); } catch { /* an operator score must not break overlay recovery */ }
                }
            }
            DispatcherTimer handoff = new() { Interval = OverlayHandoffGap };
            handoff.Tick += (_, _) =>
            {
                handoff.Stop();
                _overlayTransitionGap = false;
                ProcessNextOverlay();
            };
            handoff.Start();
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
            Color labelInk = SmartContrast.PickSplitBarForeground(_homeColor, _awayColor);
            AStatsLabel.Foreground = new SolidColorBrush(labelInk);
            if (AStatsLabel.Effect is DropShadowEffect labelShadow)
            {
                labelShadow.Color = labelInk == Colors.Black ? Colors.White : Colors.Black;
                labelShadow.Opacity = 0.92;
            }
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
            if (_statsBarVisible) return;
            _currentStats = _pendingStats ?? _currentStats;
            if (_currentStats == null) { OnOverlayHidden(); return; }
            _statsBarVisible = true;
            _statsRotationIndex = 0;
            ApplyStat(BuildStatEntries(_currentStats)[0], false);
            AnimateOverlayIn(AStatsBar, StatsBarHeight);
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
            AnimateOverlayOut(AStatsBar, StatsBarHeight, OnOverlayHidden);
        }

        // ==== Marquee ====

        private List<string> _marqueeMessages = new();
        private int _marqueeIndex;
        private bool _marqueeRunning;
        private int _marqueeRunGeneration;
        private bool _marqueeLayoutRetryQueued;

        public void SetMarqueeMessages(IList<string> messages)
        {
            var incoming = messages == null
                ? new List<string>()
                : messages.Select(SanitizeMarqueeText)
                    .Where(text => text.Length > 0)
                    .Take(8)
                    .ToList();
            List<MarqueeMessage> incomingStyled = incoming
                .Select(text => new MarqueeMessage(text, "#FFFFFFFF", null, isEnabled: true))
                .ToList();
            if (_tickerSuspendedForWarning)
            {
                _tickerMessagesWhileWarning = incomingStyled;
                return;
            }

            // No-op if the list is identical to the active one. Routine state
            // refreshes (score adds, clock pauses, periodic pushes) call this
            // method on every tick ° restarting the scroll there would reset
            // the marquee position constantly. Only apply when something
            // actually changed.
            if (MarqueeListEquals(_marqueeMessages, incoming) &&
                MarqueeStyledListEquals(_marqueeStyled, incomingStyled))
                return;

            _marqueeStyled = incomingStyled;
            if (MarqueeListEquals(_marqueeMessages, incoming))
            {
                ApplyCurrentMarqueeStyle();
                return;
            }

            ApplyMarqueeMessages(incoming);
        }

        public void SetMarqueeMessages(IList<MarqueeMessage> messages)
        {
            var incoming = messages == null
                ? new List<MarqueeMessage>()
                : messages
                    .Where(message => message.IsEnabled && !string.IsNullOrWhiteSpace((message.Text ?? string.Empty)))
                    .Select(message => new MarqueeMessage(
                        SanitizeMarqueeText((message.Text ?? string.Empty)),
                        SanitizeMarqueeColor(message.TextColor),
                        SanitizeMarqueeColor(message.HighlightColor),
                        isEnabled: true))
                    .Where(message => (message.Text ?? string.Empty).Length > 0)
                    .Take(8)
                    .ToList();

            if (_tickerSuspendedForWarning)
            {
                _tickerMessagesWhileWarning = incoming;
                return;
            }

            if (MarqueeStyledListEquals(_marqueeStyled, incoming))
                return;

            _marqueeStyled = incoming;
            var asText = new List<string>(incoming.Count);
            foreach (var m in incoming) asText.Add((m.Text ?? string.Empty) ?? string.Empty);

            if (MarqueeListEquals(_marqueeMessages, asText))
            {
                // Text identical ° just refresh styling on the active scroll
                ApplyCurrentMarqueeStyle();
                return;
            }

            ApplyMarqueeMessages(asText);
        }

        private List<MarqueeMessage> _marqueeStyled = new();
        private bool _tickerSuspendedForWarning;
        private List<MarqueeMessage> _tickerMessagesWhileWarning = new();

        private void SuspendTickerForWarning()
        {
            if (_tickerSuspendedForWarning) return;
            _tickerSuspendedForWarning = true;
            _tickerMessagesWhileWarning = _marqueeStyled
                .Select(message => new MarqueeMessage(
                    (message.Text ?? string.Empty), message.TextColor, message.HighlightColor, isEnabled: true))
                .ToList();
            _marqueeMessages = new List<string>();
            _marqueeStyled = new List<MarqueeMessage>();
            StopAllMarquees();
            UpdateBottomBarVisibility();
        }

        private void ResumeTickerAfterWarning()
        {
            if (!_tickerSuspendedForWarning) return;
            List<MarqueeMessage> restore = _tickerMessagesWhileWarning;
            _tickerMessagesWhileWarning = new List<MarqueeMessage>();
            _tickerSuspendedForWarning = false;
            SetMarqueeMessages(restore);
        }

        private static bool MarqueeListEquals(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            return true;
        }

        private static string SanitizeMarqueeText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string flattened = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            string normalized = string.Join(' ', flattened.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return normalized.Length <= 140 ? normalized : normalized[..140].TrimEnd();
        }

        private static string? SanitizeMarqueeColor(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            try
            {
                Color color = (Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString(value);
                return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            }
            catch
            {
                return null;
            }
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

        private static void ApplyMarqueeStyle(TextBlock text, Avalonia.Controls.Shapes.Rectangle? highlight, MarqueeMessage m)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(m.TextColor))
                {
                    var c = (Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString(m.TextColor);
                    text.Foreground = new SolidColorBrush(c);
                }
                else
                {
                    text.Foreground = Avalonia.Media.Brushes.White;
                }
            }
            catch { text.Foreground = Avalonia.Media.Brushes.White; }

            if (highlight != null)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(m.HighlightColor))
                    {
                        var c = (Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString(m.HighlightColor);
                        highlight.Fill = new SolidColorBrush(c);
                        highlight.IsVisible = true;
                    }
                    else
                    {
                        highlight.IsVisible = false;
                    }
                }
                catch { highlight.IsVisible = false; }
            }
        }

        private void ApplyMarqueeMessages(List<string> messages)
        {
            if (messages.Count > 0)
            {
                _overlayQueue.Clear();
                if (_activeOverlay is not null)
                    HideActiveOverlay();
            }

            // Special-case: the operator just removed the last message while a
            // scroll is in progress. Cutting the bar off mid-text looks abrupt,
            // so let the active message finish its travel and the natural
            // animation-complete handler will hide the bar smoothly.
            if (messages.Count == 0 && _marqueeRunning && _marqueeMessages.Count > 0)
            {
                _marqueeMessages = messages;
                _marqueeIndex = 0;
                // Do NOT call StopAllMarquees / UpdateBottomBarVisibility here.
                // OnMarqueeScrollCompleted will see the empty list and tear
                // down + animate the bar out cleanly.
                return;
            }

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

            // Populate and measure the first message before the bar moves into
            // view. This removes the one-frame text/font hitch at the end of
            // the entrance without sacrificing the smooth bar animation.
            if (_marqueeMessages.Count > 0)
            {
                string first = _marqueeMessages[_marqueeIndex];
                MarqueeText.Text = first;
                ExpMarqueeText.Text = first;
                ApplyCurrentMarqueeStyle();
                MarqueeText.Measure(new Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity));
                ExpMarqueeText.Measure(new Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity));
            }

            UpdateBottomBarVisibility();

            if (_marqueeMessages.Count > 0 && !_bottomBarAnimating && _bottomBarVisible)
                ScrollNextMessage();
            else if (_activeOverlay is null && !_tickerSuspendedForWarning)
                AnimateTeamNamePanelsForOverlay(hide: false);
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

        private void RunSingleMarquee(TextBlock text, Canvas canvas, TranslateTransform tr, Avalonia.Controls.Shapes.Rectangle? highlight = null, TranslateTransform? highlightTr = null)
        {
            if (string.IsNullOrWhiteSpace((text.Text ?? string.Empty))) { _marqueeRunning = false; return; }

            // The bottom bar may have only just been made visible; if the canvas
            // hasn't measured yet we'd compute a zero-size layout and the text
            // ends up tiny and off-centre. Defer one layout pass in that case.
            if (_bottomBarAnimating || canvas.NativeHeight() < 40 || canvas.NativeWidth() <= 0)
            {
                _marqueeRunning = false;
                if (!_marqueeLayoutRetryQueued)
                {
                    _marqueeLayoutRetryQueued = true;
                    Dispatcher.UIThread.Post(new Action(() =>
                    {
                        _marqueeLayoutRetryQueued = false;
                        if (!_bottomBarAnimating && IsVisible && ExpMarqueeCanvas.NativeWidth() > 0 && _marqueeMessages.Count > 0)
                            StartActiveMarquee();
                    }), DispatcherPriority.Background);
                }
                return;
            }

            tr.BeginAnimation(TranslateTransform.XProperty, null);
            highlightTr?.BeginAnimation(TranslateTransform.XProperty, null);

            // Scale font size to ~70% of the canvas height so the marquee fits
            // the bottom bar at any output-window size (instead of overflowing
            // when the bar shrinks below the design 34px).
            double canvasH = canvas.NativeHeight();
            double targetFs = Math.Clamp(canvasH * 0.76, 22, 35);
            if (Math.Abs(text.FontSize - targetFs) > 0.5)
                text.FontSize = targetFs;

            // Re-measure AFTER FontSize change so the centering math uses the
            // final size (previously we measured against the stale font size
            // which made the text look off-centre vertically).
            text.Measure(new Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity));
            double tw = text.DesiredSize.Width, th = text.DesiredSize.Height;
            double cw = canvas.NativeWidth();
            double ch = canvas.NativeHeight();
            Canvas.SetTop(text, Math.Max(0, (ch - th) / 2));
            Canvas.SetLeft(text, 0);

            double from = cw, to = -tw, dist = from - to;
            double seconds = dist / MarqueePixelsPerSecond;

            _marqueeRunning = true;
            int generation = ++_marqueeRunGeneration;
            var anim = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new NativeDuration(TimeSpan.FromSeconds(seconds))
            };
            anim.Completed += (_, _) =>
            {
                if (generation == _marqueeRunGeneration)
                    OnMarqueeScrollCompleted(this, EventArgs.Empty);
            };
            tr.BeginAnimation(TranslateTransform.XProperty, anim);

            if (highlight != null && highlightTr != null && highlight.IsVisible == true)
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
                    Duration = new NativeDuration(TimeSpan.FromSeconds(seconds))
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
            else
            {
                // The message list was cleared while this scroll was in flight.
                // Now that the text has travelled fully off the bar we can
                // animate the bottom bar out smoothly.
                MarqueeText.Text = string.Empty;
                ExpMarqueeText.Text = string.Empty;
                if (MarqueeHighlight is not null)
                {
                    MarqueeHighlight.IsVisible = false;
                    MarqueeHighlight.Width = 0;
                }
                if (ExpMarqueeHighlight is not null)
                {
                    ExpMarqueeHighlight.IsVisible = false;
                    ExpMarqueeHighlight.Width = 0;
                }
                UpdateBottomBarVisibility();
                if (_activeOverlay is null)
                    AnimateTeamNamePanelsForOverlay(hide: false);
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
            _marqueeRunGeneration++;
            _marqueeRunning = false;
            _marqueeLayoutRetryQueued = false;
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
                MarqueeHighlight.IsVisible = false;
                MarqueeHighlight.Width = 0;
            }
            if (ExpMarqueeHighlight is not null)
            {
                ExpMarqueeHighlight.IsVisible = false;
                ExpMarqueeHighlight.Width = 0;
            }
            MarqueeText.Foreground = Avalonia.Media.Brushes.White;
            ExpMarqueeText.Foreground = Avalonia.Media.Brushes.White;
            UpdateBottomBarVisibility();
        }

        private void RestartMarqueeIfNeeded() { if (_marqueeMessages.Count > 0 && !_marqueeRunning) StartActiveMarquee(); }

        // ==== 5-minute warning ====

        private bool _warningVisible;
        private DispatcherTimer? _warningAutoHideTimer;
        private static readonly string[] WarningLines = new[] { "5 MINUTE WARNING" };
        private static readonly TimeSpan WarningLetterStagger = TimeSpan.FromMilliseconds(35);
        private static readonly TimeSpan WarningLetterDuration = TimeSpan.FromMilliseconds(280);
        private static readonly TimeSpan WarningHoldDuration = TimeSpan.FromSeconds(10);

        public void ShowFiveMinuteWarning()
        {
            SuspendTickerForWarning();
            EnqueueOverlay(OverlayKind.FiveMinWarning, priority: true);
        }

        private static IEnumerable<TextBlock> EnumerateLetters(StackPanel host)
        {
            foreach (var child in host.Children)
            {
                if (child is StackPanel line)
                {
                    foreach (var inner in line.Children)
                        if (inner is TextBlock tb) yield return tb;
                }
                else if (child is TextBlock direct)
                {
                    yield return direct;
                }
            }
        }

        private void BuildWarningLetters(StackPanel host, IReadOnlyList<string> lines, Color color, double fontSize, bool foldedOut)
        {
            host.Children.Clear();
            // Primary line (index 0) renders ~1.85x larger than subtitles. The
            // subtitle uses an amber tint with extra letter spacing and is
            // flanked by ⚠ glyphs for broadcast-caption polish.
            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                string line = lines[lineIndex];
                bool isPrimary = lineIndex == 0;
                double size = isPrimary ? fontSize * 1.85 : fontSize * 0.78;
                Color lineColor = isPrimary ? color : Color.FromRgb(0xFF, 0xC8, 0x4A);
                double letterPad = isPrimary ? 1 : 5;
                var row = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = HAlign.Center,
                    Margin = isPrimary ? new Thickness(0, 0, 0, 2) : new Thickness(0, 6, 0, 0)
                };

                // Subtitle gets a leading ⚠ chevron.
                if (!isPrimary)
                    row.Children.Add(MakeWarningGlyph(size, lineColor, leftMargin: 0, rightMargin: 14));

                foreach (var ch in line)
                {
                    var tb = new NativeTextBlock
                    {
                        Text = ch == ' ' ? "\u00A0\u00A0" : ch.ToString(),
                        Foreground = new SolidColorBrush(lineColor),
                        FontFamily = NativeFonts.Family("Stolzl Bold"),
                        FontWeight = FontWeight.Black,
                        FontSize = size,
                        Margin = new Thickness(letterPad, 0, letterPad, 0),
                        RenderTransformOrigin = new RelativePoint(0.5, 0.5,RelativeUnit.Relative),
                        Effect = new DropShadowEffect
                        {
                            BlurRadius = isPrimary ? 34 : 18,
                            OffsetX = 0, OffsetY = 0,
                            Color = isPrimary ? Colors.Black : Color.FromRgb(0x55, 0x00, 0x00),
                            Opacity = 0.95
                        }
                    };
                    var st = new ScaleTransform(1, foldedOut ? 0.001 : 1);
                    tb.RenderTransform = st;
                    row.Children.Add(tb);
                }

                // Subtitle gets a trailing ⚠ chevron.
                if (!isPrimary)
                    row.Children.Add(MakeWarningGlyph(size, lineColor, leftMargin: 14, rightMargin: 0));

                host.Children.Add(row);
            }
        }

        private static TextBlock MakeWarningGlyph(double size, Color color, double leftMargin, double rightMargin)
        {
            var tb = new NativeTextBlock
            {
                Text = "\u26A0", // ⚠
                Foreground = new SolidColorBrush(color),
                FontFamily = NativeFonts.Family("Segoe UI Symbol"),
                FontWeight = FontWeight.Black,
                FontSize = size * 1.15,
                Margin = new Thickness(leftMargin, 0, rightMargin, 0),
                RenderTransformOrigin = new RelativePoint(0.5, 0.5,RelativeUnit.Relative),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 18,
                    OffsetX = 0, OffsetY = 0,
                    Color = Color.FromRgb(0x55, 0x00, 0x00),
                    Opacity = 0.95
                }
            };
            tb.RenderTransform = new ScaleTransform(1, 1);
            return tb;
        }

        // Animates a ScaleTransform.ScaleY between values to mimic a letter "folding".
        private static void AnimateFold(ScaleTransform st, double from, double to, TimeSpan duration, TimeSpan beginAfter, Action? completed = null)
        {
            var anim = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = duration,
                BeginTime = beginAfter,
                EasingFunction = to < from
                    ? new CubicEase { EasingMode = EasingMode.EaseIn }
                    : new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }
            };
            if (completed != null) anim.Completed += (_, __) => completed();
            st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        private void FoldOutPanelText(Action onAllFolded)
        {
            var quarter = AQuarterTextScale;
            var clock = AClockTextScale;
            // Fold quarter and clock together (they're already single TextBlocks, so we
            // collapse them in a single quick fold to make room for the warning).
            int pending = 2;
            void OneDone() { if (--pending == 0) onAllFolded(); }
            AnimateFold(quarter, 1.0, 0.001, TimeSpan.FromMilliseconds(280), TimeSpan.Zero, OneDone);
            AnimateFold(clock, 1.0, 0.001, TimeSpan.FromMilliseconds(280), TimeSpan.FromMilliseconds(60), OneDone);
        }

        private void FoldInPanelText()
        {
            var quarter = AQuarterTextScale;
            var clock = AClockTextScale;
            AnimateFold(quarter, 0.001, 1.0, TimeSpan.FromMilliseconds(320), TimeSpan.Zero);
            AnimateFold(clock, 0.001, 1.0, TimeSpan.FromMilliseconds(320), TimeSpan.FromMilliseconds(80));
        }

        private void FoldInWarningLetters(StackPanel host, Action onComplete)
        {
            var letters = new List<TextBlock>(EnumerateLetters(host));
            if (letters.Count == 0) { onComplete(); return; }
            int pending = letters.Count;
            for (int i = 0; i < letters.Count; i++)
            {
                if (letters[i].RenderTransform is not ScaleTransform st) { if (--pending == 0) onComplete(); continue; }
                AnimateFold(st, 0.001, 1.0, WarningLetterDuration, TimeSpan.FromMilliseconds(i * WarningLetterStagger.TotalMilliseconds),
                    () => { if (--pending == 0) onComplete(); });
            }
        }

        private void FoldOutWarningLetters(StackPanel host, Action onComplete)
        {
            var letters = new List<TextBlock>(EnumerateLetters(host));
            if (letters.Count == 0) { onComplete(); return; }
            int pending = letters.Count;
            for (int i = 0; i < letters.Count; i++)
            {
                if (letters[i].RenderTransform is not ScaleTransform st) { if (--pending == 0) onComplete(); continue; }
                AnimateFold(st, 1.0, 0.001, WarningLetterDuration, TimeSpan.FromMilliseconds(i * WarningLetterStagger.TotalMilliseconds),
                    () => { if (--pending == 0) onComplete(); });
            }
        }

        private void DoShowFiveMinuteWarning()
        {
            if (_warningVisible) return;
            _warningVisible = true;

            if (IsExpanded)
            {
                AnimateOverlayIn(ExpWarningBanner, ExpandedWarningHeight);

                ExpWarningAlertGlow.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(900))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                    });
                ExpWarningBannerStripesTranslate.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(-64, 0, TimeSpan.FromMilliseconds(900))
                    {
                        RepeatBehavior = RepeatBehavior.Forever
                    });
                var alertPulse = new DoubleAnimation(1, 1.008, TimeSpan.FromMilliseconds(980))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                ExpWarningAlertScale.BeginAnimation(ScaleTransform.ScaleXProperty, alertPulse);
                ExpWarningAlertScale.BeginAnimation(ScaleTransform.ScaleYProperty, alertPulse);
                var warningFlash = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
                warningFlash.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                warningFlash.KeyFrames.Add(new LinearDoubleKeyFrame(0.75, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
                warningFlash.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(OverlayTransitionDuration)));
                ExpWarningAlertFlash.BeginAnimation(OpacityProperty, warningFlash);

                _warningAutoHideTimer = StartOverlayAutoHide(WarningHoldDuration, () =>
                {
                    _warningAutoHideTimer = null;
                    BeginHideFiveMinuteWarning();
                });
                return;
            }

            var wo = AWarningOverlay;
            var ws = AWarningStripes;
            var wg = AWarningGlow;
            var host = AWarningLetterHost;

            // Prep host with letters in folded-out state (invisible vertically).
            // White text reads cleanly against the new red alert backdrop; the
            // base size feeds the multiplier in BuildWarningLetters so the
            // primary "5 MIN" line ends up significantly larger than the
            // subtitle while the Viewbox still scales to fit available space.
            BuildWarningLetters(host, WarningLines, Colors.White,
                IsExpanded ? 110 : 60, foldedOut: true);

            void RevealWarning()
            {
                wo.Opacity = 0;
                if (IsExpanded)
                {
                    wo.Height = 0;
                    ExpWarningBannerSlide.Y = -ScalePx(ExpandedWarningHeight);
                    wo.BeginAnimation(Control.HeightProperty,
                        new DoubleAnimation(0, ScalePx(ExpandedWarningHeight), TimeSpan.FromMilliseconds(360))
                        { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } });
                    ExpWarningBannerSlide.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(-ScalePx(ExpandedWarningHeight), 0, TimeSpan.FromMilliseconds(420))
                        { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.22 } });
                }
                wo.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

                // Bright entry flash – white wipe that fades immediately.
                var flash = AWarningFlash;
                if (flash != null)
                {
                    var fa = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
                    fa.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                    fa.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))));
                    fa.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420))));
                    flash.BeginAnimation(OpacityProperty, fa);
                }

                // Stripe scroll + glow pulse (ambient hazard animations).
                ws.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(0, ScalePx(56), TimeSpan.FromSeconds(0.8))
                    { RepeatBehavior = RepeatBehavior.Forever });
                wg.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0.35, 1.0, TimeSpan.FromSeconds(0.55))
                    { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                      EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });

                // Scanline sweep across the overlay.
                var scan = AWarningScanline;
                var scanRect = AWarningScanlineRect;
                if (scan != null && scanRect != null)
                {
                    double width = Math.Max(AWarningOverlay.NativeWidth(), 200);
                    var scanAnim = new DoubleAnimation
                    {
                        From = -scanRect.Width,
                        To = width + scanRect.Width,
                        Duration = TimeSpan.FromSeconds(1.6),
                        BeginTime = TimeSpan.FromMilliseconds(420),
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                    };
                    var scanOp = new DoubleAnimation
                    {
                        From = 0.0,
                        To = 0.55,
                        Duration = TimeSpan.FromMilliseconds(300),
                        BeginTime = TimeSpan.FromMilliseconds(420)
                    };
                    scan.BeginAnimation(TranslateTransform.XProperty, scanAnim);
                    scanRect.BeginAnimation(OpacityProperty, scanOp);
                }

                // Heartbeat: very subtle scale pulse on the letter group so
                // motion is felt without making the text wobble or jitter.
                var pulse = AWarningPulse;
                if (pulse != null)
                {
                    var pa = new DoubleAnimation
                    {
                        From = 1.0,
                        To = 1.025,
                        Duration = TimeSpan.FromMilliseconds(820),
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                    };
                    pulse.BeginAnimation(ScaleTransform.ScaleXProperty, pa);
                    pulse.BeginAnimation(ScaleTransform.ScaleYProperty, pa);
                }

                FoldInWarningLetters(host, () =>
                {
                    _warningAutoHideTimer = StartOverlayAutoHide(WarningHoldDuration, () =>
                    {
                        _warningAutoHideTimer = null;
                        BeginHideFiveMinuteWarning();
                    });
                });
            }

            // The active expanded scorebug uses a dedicated full-width slide-down
            // banner. The deprecated classic layout keeps its compact fold-over.
            if (IsExpanded)
                RevealWarning();
            else
                FoldOutPanelText(RevealWarning);
        }

        public void HideFiveMinuteWarning()
        {
            if (!_warningVisible) return;
            _warningAutoHideTimer?.Stop();
            _warningAutoHideTimer = null;
            BeginHideFiveMinuteWarning();
        }

        private void BeginHideFiveMinuteWarning()
        {
            var wo = AWarningOverlay;
            var ws = AWarningStripes;
            var wg = AWarningGlow;
            var host = AWarningLetterHost;

            ExpWarningAlertGlow.BeginAnimation(OpacityProperty, null);
            ExpWarningAlertGlow.Opacity = 0.35;
            ExpWarningBannerStripesTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            ExpWarningBannerStripesTranslate.X = 0;
            ExpWarningAlertScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            ExpWarningAlertScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            ExpWarningAlertScale.ScaleX = ExpWarningAlertScale.ScaleY = 1;
            ExpWarningAlertFlash.BeginAnimation(OpacityProperty, null);
            ExpWarningAlertFlash.Opacity = 0;

            if (IsExpanded)
            {
                AnimateOverlayOut(ExpWarningBanner, ExpandedWarningHeight, () =>
                {
                    _warningVisible = false;
                    OnOverlayHidden();
                    ResumeTickerAfterWarning();
                });
                return;
            }

            FoldOutWarningLetters(host, () =>
            {
                // Stop ambient hazard animations.
                ws.BeginAnimation(TranslateTransform.XProperty, null);
                wg.BeginAnimation(OpacityProperty, null);
                wg.Opacity = 0;

                // Stop scanline + heartbeat.
                var scan = AWarningScanline;
                var scanRect = AWarningScanlineRect;
                scan?.BeginAnimation(TranslateTransform.XProperty, null);
                if (scanRect != null)
                {
                    scanRect.BeginAnimation(OpacityProperty, null);
                    scanRect.Opacity = 0;
                }
                var pulse = AWarningPulse;
                if (pulse != null)
                {
                    pulse.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    pulse.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    pulse.ScaleX = 1.0;
                    pulse.ScaleY = 1.0;
                }

                var fo = new DoubleAnimation(1, 0, OverlayTransitionDuration)
                { EasingFunction = CreateOverlayTransitionEase() };
                if (IsExpanded)
                {
                    wo.BeginAnimation(Control.HeightProperty,
                        new DoubleAnimation(wo.NativeHeight(), 0, OverlayTransitionDuration)
                        { EasingFunction = CreateOverlayTransitionEase() });
                    ExpWarningBannerSlide.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(0, -ScalePx(ExpandedWarningHeight), OverlayTransitionDuration)
                        { EasingFunction = CreateOverlayTransitionEase() });
                }
                fo.Completed += (_, __) =>
                {
                    wo.BeginAnimation(OpacityProperty, null);
                    wo.Opacity = 0;
                    if (IsExpanded)
                    {
                        wo.BeginAnimation(Control.HeightProperty, null);
                        wo.Height = 0;
                        ExpWarningBannerSlide.BeginAnimation(TranslateTransform.YProperty, null);
                        ExpWarningBannerSlide.Y = -ScalePx(ExpandedWarningHeight);
                    }
                    host.Children.Clear();

                    if (!IsExpanded)
                        FoldInPanelText();

                    _warningVisible = false;
                    OnOverlayHidden();
                    ResumeTickerAfterWarning();
                };
                wo.BeginAnimation(OpacityProperty, fo);
            });
        }

        // ==== Scoreless timer ====

        private bool _scorelessBarVisible;
        private DispatcherTimer? _scorelessUpdateTimer;
        private DispatcherTimer? _scorelessAutoHideTimer;
        private DateTime _lastScoreTime = DateTime.MinValue;
        private int _lastScoreMilestoneMinutes;
        private string _pendingLastEventKind = "SCORE";
        private const double ScorelessBarHeight = 46;

        public void ResetScorelessTimer()
        {
            _lastScoreTime = OverlayClockNow;
            _lastScoreMilestoneMinutes = 0;
            if (_scorelessBarVisible) HideScorelessBar();
        }

        public bool CheckScorelessTimer()
        {
            if (_lastScoreTime == DateTime.MinValue) return false;
            int nextMilestone = NextTenMinuteMilestone(_lastScoreMilestoneMinutes);
            if ((OverlayClockNow - _lastScoreTime).TotalMinutes >= nextMilestone)
            {
                _lastScoreMilestoneMinutes = nextMilestone;
                // The window-level scheduler owns when the overlay is shown.
                // Enqueuing here as well used to create a duplicate overlay in
                // both queues and could make LAST SCORE appear twice.
                return true;
            }

            return false;
        }

        public void ShowScorelessBar() { if (_lastScoreTime == DateTime.MinValue) _lastScoreTime = OverlayClockNow.AddMinutes(-1); EnqueueOverlay(OverlayKind.ScorelessTimer); }

        public void ShowLastEventTime(string eventKind, TimeSpan elapsed)
        {
            _pendingLastEventKind = string.Equals(eventKind, "Goal", StringComparison.OrdinalIgnoreCase)
                ? "GOAL"
                : "SCORE";
            _lastScoreTime = OverlayClockNow - (elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed);
            EnqueueOverlay(OverlayKind.ScorelessTimer, restartIfActive: true);
        }

        private void DoShowScorelessBar()
        {
            if (_scorelessBarVisible) return;
            _scorelessBarVisible = true;
            AScorelessLabel.Text = $"TIME SINCE LAST {_pendingLastEventKind}";
            UpdateScorelessText();
            AnimateOverlayIn(AScorelessBar, ScorelessBarHeight);
            _scorelessUpdateTimer?.Stop(); _scorelessUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _scorelessUpdateTimer.Tick += (_, __) => UpdateScorelessText(); _scorelessUpdateTimer.Start();
            _scorelessAutoHideTimer?.Stop();
            _scorelessAutoHideTimer = StartOverlayAutoHide(TimeSpan.FromSeconds(20), () =>
            {
                _scorelessAutoHideTimer = null;
                HideScorelessBar();
            });
        }

        public void HideScorelessBar()
        {
            if (!_scorelessBarVisible) return; _scorelessBarVisible = false;
            _scorelessAutoHideTimer?.Stop(); _scorelessAutoHideTimer = null;
            AnimateOverlayOut(AScorelessBar, ScorelessBarHeight, OnOverlayHidden);
        }

        private void UpdateScorelessText()
        {
            if (_lastScoreTime == DateTime.MinValue) return;
            var el = OverlayClockNow - _lastScoreTime;
            var t = FormatElapsedOverlayTime(el);
            ScorelessTimeText.Text = t; ExpScorelessTimeText.Text = t;
        }

        // ==== Directional team-story takeover ============================

        private void ConfigureTeamStoryPalette(bool isHome, Color primary)
        {
            Color strong = Mix(primary, Colors.Black, 0.42);
            Color middle = Mix(primary, Colors.Black, 0.66);
            Color dark = Mix(primary, Colors.Black, 0.88);
            ExpTeamStoryGradientStrong.Color = strong;
            ExpTeamStoryGradientMid.Color = middle;
            ExpTeamStoryGradientDark.Color = dark;

            Color preferred = isHome ? _homeSecondaryColor : _awaySecondaryColor;
            Color ink = SmartContrast.PickReadableForeground(preferred, strong, middle, dark);
            var inkBrush = new SolidColorBrush(ink);
            ExpTeamStoryTeamName.Foreground = inkBrush;
            ExpTeamStoryHeadline.Foreground = inkBrush;
            ExpTeamStoryTimer.Foreground = inkBrush;
            ExpTeamStoryKicker.Foreground = new SolidColorBrush(Color.FromArgb(0xE8, ink.R, ink.G, ink.B));
            ExpTeamStoryFooter.Foreground = new SolidColorBrush(Color.FromArgb(0xD0, ink.R, ink.G, ink.B));
            ExpTeamStoryDirectionIndicator.Foreground = inkBrush;
            ExpTeamStoryPushEdge.Background = new SolidColorBrush(
                SmartContrast.IsLowContrast(preferred, strong) ? ink : preferred);
            ExpTeamStoryLogo.Source = isHome ? _homeLogoSource : _awayLogoSource;
            ExpTeamStoryLogo.IsVisible = ExpTeamStoryLogo.Source is null
                ? false
                : true;
        }

        private void ConfigureScoringRunTakeover()
        {
            bool isHome = _pendingScoringRunIsHome;
            ConfigureTeamStoryPalette(isHome, isHome ? _homeColor : _awayColor);
            ExpTeamStoryTeamName.Text = isHome ? _homeFullName : _awayFullName;
            // Reserve against two digits, not the current run count. The
            // metric therefore stays dominant and never changes size at 9→10.
            ExpTeamStoryMetricColumn.Width = new GridLength(44, GridUnitType.Star);
            ExpTeamStoryDescriptorColumn.Width = new GridLength(56, GridUnitType.Star);
            ExpTeamStoryHeadline.Width = 172;
            ExpTeamStoryHeadline.Height = double.NaN;
            ExpTeamStoryHeadline.FontSize = 112;
            ExpTeamStoryHeadline.FontFamily = NativeFonts.Family("Arial Black");
            ExpTeamStoryKicker.FontSize = 42;
            ExpTeamStoryKicker.LineHeight = 37;
            ExpTeamStoryTimerHost.IsVisible = false;
            ExpTeamStoryContent.Margin = new Thickness(5, 8, 5, 8);
            ExpTeamStoryHeadline.Text = (_pendingGoalRunResting ? 0 : _pendingGoalRunGoals).ToString();
            ExpTeamStoryHeadline.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xE3, 0x67));
            if (_pendingGoalRunConsecutive)
            {
                ExpTeamStoryKicker.Text = "GOALS IN\nA ROW";
            }
            else
            {
                ExpTeamStoryKicker.Text = $"OF LAST\n{_pendingGoalRunWindow} GOALS";
            }
        }

        private void ConfigureDroughtTakeover()
        {
            bool isHome = _pendingAutoDroughtIsHome;
            ConfigureTeamStoryPalette(isHome, _pendingDroughtTeamColor);
            string fullName = isHome ? _homeFullName : _awayFullName;
            ExpTeamStoryTeamName.Text = string.IsNullOrWhiteSpace(fullName)
                ? _pendingDroughtAbbreviation
                : fullName;
            ExpTeamStoryMetricColumn.Width = new GridLength(65, GridUnitType.Star);
            ExpTeamStoryDescriptorColumn.Width = new GridLength(35, GridUnitType.Star);
            // 88:88 is the width contract. The previous 250-unit box clipped
            // double-digit minutes internally before its Viewbox could scale.
            ExpTeamStoryHeadline.Width = 380;
            ExpTeamStoryHeadline.Height = double.NaN;
            ExpTeamStoryHeadline.FontFamily = NativeFonts.Family("Arial Black");
            ExpTeamStoryHeadline.FontSize = 112;
            ExpTeamStoryKicker.FontSize = 54;
            ExpTeamStoryKicker.LineHeight = 46;
            ExpTeamStoryKicker.Text = $"LAST\n{_pendingDroughtEventKind}";
            ExpTeamStoryHeadline.Text = "00:00";
            ExpTeamStoryTimerHost.IsVisible = false;
            ExpTeamStoryContent.Margin = new Thickness(5, 8, 5, 8);
        }

        private void ConfigureTeamStoryDirection(bool isHome)
        {
            ExpTeamStoryDirectionHost.BeginAnimation(OpacityProperty, null);
            ExpTeamStoryDirectionIndicator.Text = isHome ? "▲" : "▼";
            ExpTeamStoryDirectionFlip.ScaleY = isHome ? 1 : -1;
            ExpTeamStoryDirectionHost.VerticalAlignment = isHome
                ? VerticalAlignment.Top
                : VerticalAlignment.Bottom;
            ExpTeamStoryDirectionHost.Opacity = 1;

            var flash = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(720),
                RepeatBehavior = RepeatBehavior.Forever
            };
            flash.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(0)));
            flash.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0.5)));
            ExpTeamStoryDirectionHost.BeginAnimation(OpacityProperty, flash,
                HandoffBehavior.SnapshotAndReplace);
        }

        private void StopTeamStoryDirection()
        {
            ExpTeamStoryDirectionHost.BeginAnimation(OpacityProperty, null);
            ExpTeamStoryDirectionHost.Opacity = 0;
        }

        private void AnimateTeamStoryIn(bool isHome)
        {
            _teamStoryVisible = true;
            _teamStoryIsHome = isHome;
            int generation = ++_teamStoryAnimationGeneration;
            double travel = Math.Max(1, ExpMainContentGrid.NativeHeight() / 2);
            double panelTarget = isHome ? travel : -travel;

            ExpTeamStoryTakeover.IsVisible = true;
            ExpTeamStoryTakeover.Height = travel;
            ExpTeamStoryTakeover.VerticalAlignment = isHome
                ? VerticalAlignment.Bottom
                : VerticalAlignment.Top;
            ExpTeamStoryTakeover.RenderTransformOrigin = new RelativePoint(0.5, isHome ? 0 : 1,RelativeUnit.Relative);
            ExpTeamStoryPushEdge.VerticalAlignment = isHome
                ? VerticalAlignment.Bottom
                : VerticalAlignment.Top;
            ConfigureTeamStoryDirection(isHome);
            NativeCache.Set(ExpTeamStoryTakeover, new NativeBitmapCache(1.0));

            ExpTeamStoryReveal.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            ExpTeamStoryReveal.ScaleY = 1;
            var reveal = new DoubleAnimation(0, 1, TeamStoryTransitionDuration)
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = CreateTeamStoryTransitionEase()
            };
            reveal.Completed += (_, _) =>
            {
                if (generation == _teamStoryAnimationGeneration)
                    NativeCache.Set(ExpTeamStoryTakeover, null);
            };
            ExpTeamStoryReveal.BeginAnimation(ScaleTransform.ScaleYProperty, reveal,
                HandoffBehavior.SnapshotAndReplace);

            ExpTeamStoryContent.BeginAnimation(OpacityProperty, null);
            ExpTeamStoryContent.Opacity = 1;
            ExpTeamStoryContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1,
                TimeSpan.FromMilliseconds(620))
            {
                BeginTime = TimeSpan.FromMilliseconds(110),
                FillBehavior = FillBehavior.Stop,
                EasingFunction = CreateTeamStoryTransitionEase()
            }, HandoffBehavior.SnapshotAndReplace);
            ExpTeamStoryContentTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            ExpTeamStoryContentTranslate.Y = 0;
            ExpTeamStoryContentTranslate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(isHome ? 14 : -14, 0, TeamStoryTransitionDuration)
                {
                    BeginTime = TimeSpan.FromMilliseconds(80),
                    FillBehavior = FillBehavior.Stop,
                    EasingFunction = CreateTeamStoryTransitionEase()
                }, HandoffBehavior.SnapshotAndReplace);

            ResetTeamStoryPanelOffsets(featuredHome: isHome);
            AnimateOpponentTeamPanels(isHome, 0, panelTarget, TeamStoryTransitionDuration);
        }

        private void AnimateTeamStoryOut(Action completed)
        {
            if (!_teamStoryVisible)
            {
                completed();
                return;
            }

            int generation = ++_teamStoryAnimationGeneration;
            double currentScale = ExpTeamStoryReveal.ScaleY;
            double currentLogoY = GetOpponentLogoTranslate(_teamStoryIsHome).Y;
            double currentScoreY = GetOpponentScoreTranslate(_teamStoryIsHome).Y;
            double currentContentOpacity = ExpTeamStoryContent.Opacity;
            ExpTeamStoryReveal.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            ExpTeamStoryContent.BeginAnimation(OpacityProperty, null);
            NativeCache.Set(ExpTeamStoryTakeover, new NativeBitmapCache(1.0));

            ExpTeamStoryReveal.ScaleY = 0;
            ExpTeamStoryContent.Opacity = 0;
            var hide = new DoubleAnimation(currentScale, 0, TeamStoryTransitionDuration)
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = CreateTeamStoryTransitionEase()
            };
            hide.Completed += (_, _) =>
            {
                if (generation != _teamStoryAnimationGeneration) return;
                ExpTeamStoryTakeover.IsVisible = false;
                NativeCache.Set(ExpTeamStoryTakeover, null);
                ExpTeamStoryReveal.ScaleY = 0;
                ExpTeamStoryContentTranslate.Y = 0;
                StopTeamStoryDirection();
                _teamStoryVisible = false;
                completed();
            };
            ExpTeamStoryReveal.BeginAnimation(ScaleTransform.ScaleYProperty, hide,
                HandoffBehavior.SnapshotAndReplace);
            ExpTeamStoryContent.BeginAnimation(OpacityProperty,
                new DoubleAnimation(currentContentOpacity, 0, TimeSpan.FromMilliseconds(620))
                {
                    FillBehavior = FillBehavior.Stop,
                    EasingFunction = CreateTeamStoryTransitionEase()
                }, HandoffBehavior.SnapshotAndReplace);
            AnimateOpponentTeamPanels(_teamStoryIsHome, currentLogoY, 0,
                TeamStoryTransitionDuration, currentScoreY);
        }

        private TranslateTransform GetOpponentLogoTranslate(bool featuredHome)
            => featuredHome ? ExpAwayLogoStoryTranslate : ExpHomeLogoStoryTranslate;

        private TranslateTransform GetOpponentScoreTranslate(bool featuredHome)
            => featuredHome ? ExpAwayScoreStoryTranslate : ExpHomeScoreStoryTranslate;

        private double GetTeamStoryOpponentOffset()
            => GetOpponentScoreTranslate(_teamStoryIsHome).Y;

        private void ResetTeamStoryPanelOffsets(bool featuredHome)
        {
            // The featured club always remains visible. Only its opponent is
            // displaced into the clipped edge as the story grows from the
            // shared boundary between the two score rows.
            TranslateTransform featuredLogo = featuredHome
                ? ExpHomeLogoStoryTranslate : ExpAwayLogoStoryTranslate;
            TranslateTransform featuredScore = featuredHome
                ? ExpHomeScoreStoryTranslate : ExpAwayScoreStoryTranslate;
            featuredLogo.BeginAnimation(TranslateTransform.YProperty, null);
            featuredScore.BeginAnimation(TranslateTransform.YProperty, null);
            featuredLogo.Y = featuredScore.Y = 0;
        }

        private void AnimateOpponentTeamPanels(bool featuredHome, double from, double to, TimeSpan duration,
            double? scoreFrom = null)
        {
            TranslateTransform logo = GetOpponentLogoTranslate(featuredHome);
            TranslateTransform score = GetOpponentScoreTranslate(featuredHome);
            logo.BeginAnimation(TranslateTransform.YProperty, null);
            score.BeginAnimation(TranslateTransform.YProperty, null);
            logo.Y = to;
            score.Y = to;
            logo.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(from, to, duration)
                {
                    FillBehavior = FillBehavior.Stop,
                    EasingFunction = CreateTeamStoryTransitionEase()
                }, HandoffBehavior.SnapshotAndReplace);
            score.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(scoreFrom ?? from, to, duration)
                {
                    FillBehavior = FillBehavior.Stop,
                    EasingFunction = CreateTeamStoryTransitionEase()
                }, HandoffBehavior.SnapshotAndReplace);
        }

        // ==== Scoring run bar ====

        private bool _scoringRunBarVisible;
        private DispatcherTimer? _scoringRunAutoHideTimer;
        private const double ScoringRunBarHeight = 54;
        private string _pendingScoringRunTeam = "";
        private int _pendingGoalRunGoals;
        private int _pendingGoalRunWindow;
        private bool _pendingGoalRunConsecutive;
        private bool _pendingGoalRunResting;
        private bool _pendingScoringRunIsHome;

        public void ShowGoalRun(string teamName, int teamGoals, int windowGoals,
            bool consecutive, Color teamColor)
        {
            _pendingScoringRunTeam = teamName;
            _pendingGoalRunGoals = Math.Max(1, teamGoals);
            _pendingGoalRunWindow = Math.Max(_pendingGoalRunGoals, windowGoals);
            _pendingGoalRunConsecutive = consecutive;
            _pendingGoalRunResting = false;
            UpdateGoalRunHeadline();
            bool isHomeTeam = string.Equals(teamName, _homeFullName, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(teamName, _homeAbbr, StringComparison.OrdinalIgnoreCase);
            _pendingScoringRunIsHome = isHomeTeam;
            IImage? runLogo = isHomeTeam ? _homeLogoSource : _awayLogoSource;
            ExpScoringRunLogoBackdrop.Source = runLogo;
            ExpScoringRunLogoBackdrop.IsVisible = runLogo is null ? false : true;

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
            Color scoringRunInk = SmartContrast.PickReadableForeground(teamColor, dark,
                Color.FromRgb(
                    (byte)Math.Clamp(teamColor.R / 2, 12, 255),
                    (byte)Math.Clamp(teamColor.G / 2, 12, 255),
                    (byte)Math.Clamp(teamColor.B / 2, 12, 255)));
            AScoringRunLabel.Foreground = new SolidColorBrush(scoringRunInk);
            ExpScoringRunAccent.Background = new SolidColorBrush(teamColor);

            EnqueueOverlay(OverlayKind.ScoringRun, restartIfActive: true);
        }

        public void ShowGoalRunResting(string teamName, string colorHex)
        {
            var hex = colorHex.StartsWith("#") ? colorHex : $"#{colorHex}";
            Color teamColor = (Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString(hex);
            _pendingScoringRunTeam = teamName;
            _pendingGoalRunResting = true;
            _pendingGoalRunGoals = 0;
            _pendingGoalRunWindow = 0;
            _pendingGoalRunConsecutive = false;

            bool isHomeTeam = string.Equals(teamName, _homeFullName, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(teamName, _homeAbbr, StringComparison.OrdinalIgnoreCase);
            _pendingScoringRunIsHome = isHomeTeam;
            IImage? runLogo = isHomeTeam ? _homeLogoSource : _awayLogoSource;
            ExpScoringRunLogoBackdrop.Source = runLogo;
            ExpScoringRunLogoBackdrop.IsVisible = runLogo is null ? false : true;
            ExpScoringRunAccent.Background = new SolidColorBrush(teamColor);
            UpdateGoalRunHeadline();
            EnqueueOverlay(OverlayKind.ScoringRun, restartIfActive: true);
        }

        public void ShowGoalRun(string teamName, int teamGoals, int windowGoals,
            bool consecutive, string colorHex)
        {
            var hex = colorHex.StartsWith("#") ? colorHex : $"#{colorHex}";
            var teamColor = (Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString(hex);
            ShowGoalRun(teamName, teamGoals, windowGoals, consecutive, teamColor);
        }

        // Compatibility for older callers and saved automation commands. Point
        // runs are converted to the closest goal-only story; new code calls
        // ShowGoalRun directly.
        public void ShowScoringRun(string teamName, int runPoints, int concededPoints,
            TimeSpan _, int __, string colorHex)
        {
            int teamGoals = Math.Max(1, runPoints / 6);
            int oppositionGoals = Math.Max(0, concededPoints / 6);
            ShowGoalRun(teamName, teamGoals, teamGoals + oppositionGoals, oppositionGoals == 0, colorHex);
        }

        private void DoShowScoringRunBar()
        {
            if (_scoringRunBarVisible) return;
            _scoringRunBarVisible = true;

            ConfigureScoringRunTakeover();
            AnimateTeamStoryIn(_pendingScoringRunIsHome);

            _scoringRunAutoHideTimer?.Stop();
            _scoringRunAutoHideTimer = StartOverlayAutoHide(TimeSpan.FromSeconds(14), () =>
            {
                _scoringRunAutoHideTimer = null;
                HideScoringRunBar();
            });
        }

        public void HideScoringRunBar()
        {
            if (!_scoringRunBarVisible) return;
            _scoringRunBarVisible = false;
            _scoringRunAutoHideTimer?.Stop();
            _scoringRunAutoHideTimer = null;

            AnimateTeamStoryOut(OnOverlayHidden);
        }

        private void UpdateGoalRunHeadline()
        {
            TextBlock headline = AScoringRunLabel;
            NativeText.ClearInlines(headline);
            Avalonia.Media.IBrush numberBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD3, 0x5A));
            if (_pendingGoalRunResting)
            {
                headline.Inlines!.Add(new Run("NO QUALIFYING GOAL RUN YET"));
                return;
            }
            headline.Inlines!.Add(new Run(_pendingScoringRunTeam.ToUpperInvariant() + " "));
            headline.Inlines!.Add(new Run(_pendingGoalRunGoals.ToString())
            {
                Foreground = numberBrush,
                FontSize = 36,
                FontWeight = FontWeight.Black
            });
            if (_pendingGoalRunConsecutive)
            {
                headline.Inlines!.Add(new Run(" GOALS IN A ROW"));
            }
            else
            {
                headline.Inlines!.Add(new Run(" OF THE LAST "));
                headline.Inlines!.Add(new Run(_pendingGoalRunWindow.ToString())
                {
                    Foreground = numberBrush,
                    FontSize = 36,
                    FontWeight = FontWeight.Black
                });
                headline.Inlines!.Add(new Run(" GOALS"));
            }
        }

        // ====  Team Drought bar  ====

        private bool _teamDroughtBarVisible;
        private DispatcherTimer? _teamDroughtUpdateTimer;
        private DispatcherTimer? _teamDroughtAutoHideTimer;
        private const double TeamDroughtBarHeight = 46;
        private DateTime _homeLastScoreTime = DateTime.MinValue;
        private DateTime _awayLastScoreTime = DateTime.MinValue;
        private int _homeDroughtMilestoneMinutes, _awayDroughtMilestoneMinutes;
        private bool _pendingAutoDroughtIsHome;
        private string _pendingDroughtLabel = "";
        private string _pendingDroughtAbbreviation = "";
        private string _pendingDroughtEventKind = "SCORE";
        private DateTime _pendingDroughtSince;
        private Color _pendingDroughtTeamColor;

        public void ResetTeamDroughtTimer(bool isHome)
        {
            if (isHome)
            {
                _homeLastScoreTime = OverlayClockNow;
                _homeDroughtMilestoneMinutes = 0;
            }
            else
            {
                _awayLastScoreTime = OverlayClockNow;
                _awayDroughtMilestoneMinutes = 0;
            }
            if (_teamDroughtBarVisible) HideTeamDroughtBar();
        }

        private static int NextTenMinuteMilestone(int lastMilestone)
            => Math.Max(10, ((Math.Max(0, lastMilestone) / 10) + 1) * 10);

        public void CheckTeamDrought(string homeName, string awayName, string homeColorHex, string awayColorHex)
        {
            var homeColor = (Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString(homeColorHex.StartsWith("#") ? homeColorHex : $"#{homeColorHex}");
            var awayColor = (Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString(awayColorHex.StartsWith("#") ? awayColorHex : $"#{awayColorHex}");
            CheckTeamDrought(homeName, awayName, homeColor, awayColor);
        }

        public void ShowTeamDrought(string teamName, DateTime lastScoreTime, string teamColorHex, string _)
        {
            var teamColor = (Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString(teamColorHex.StartsWith("#") ? teamColorHex : $"#{teamColorHex}");
            ShowTeamDrought(teamName, lastScoreTime, teamColor);
        }

        public void CheckTeamDrought(string homeName, string awayName, Color homeColor, Color awayColor)
        {
            if (!CheckAutoTeamDrought()) return;
            if (_pendingAutoDroughtIsHome)
                ShowTeamDrought(homeName, _homeLastScoreTime, homeColor);
            else
                ShowTeamDrought(awayName, _awayLastScoreTime, awayColor);
        }

        public void ShowTeamDrought(string teamName, DateTime lastScoreTime, Color teamColor)
        {
            ShowTeamDrought(teamName, teamName, "Score",
                OverlayClockNow - lastScoreTime, teamColor);
        }

        public void ShowTeamDrought(string teamName, string teamAbbreviation,
            string eventKind, TimeSpan elapsed, string teamColorHex)
        {
            var hex = teamColorHex.StartsWith("#") ? teamColorHex : $"#{teamColorHex}";
            var teamColor = (Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString(hex);
            ShowTeamDrought(teamName, teamAbbreviation, eventKind, elapsed, teamColor);
        }

        public void ShowTeamDrought(string teamName, string teamAbbreviation,
            string eventKind, TimeSpan elapsed, Color teamColor)
        {
            string fullName = teamName.ToUpperInvariant();
            _pendingDroughtLabel = fullName;
            _pendingDroughtAbbreviation = string.IsNullOrWhiteSpace(teamAbbreviation)
                ? fullName
                : teamAbbreviation.Trim().ToUpperInvariant();
            _pendingDroughtEventKind = string.Equals(eventKind, "Goal", StringComparison.OrdinalIgnoreCase)
                ? "GOAL"
                : "SCORE";
            TimeSpan safeElapsed = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
            DateTime lastScoreTime = OverlayClockNow - safeElapsed;
            _pendingDroughtSince = lastScoreTime;
            _pendingDroughtTeamColor = teamColor;
            bool isHomeTeam = string.Equals(teamName, _homeFullName, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(teamName, _homeAbbr, StringComparison.OrdinalIgnoreCase);
            _pendingAutoDroughtIsHome = isHomeTeam;
            IImage? droughtLogo = isHomeTeam ? _homeLogoSource : _awayLogoSource;
            ExpTeamDroughtLogoBackdrop.Source = droughtLogo;
            ExpTeamDroughtLogoBackdrop.IsVisible = droughtLogo is null ? false : true;
            ExpTeamDroughtAccent.Background = new SolidColorBrush(teamColor);
            EnqueueOverlay(OverlayKind.TeamDrought, restartIfActive: true);
        }

        private void DoShowTeamDroughtBar()
        {
            if (_teamDroughtBarVisible) return; _teamDroughtBarVisible = true;
            ConfigureDroughtTakeover();
            UpdateTeamDroughtText();
            AnimateTeamStoryIn(_pendingAutoDroughtIsHome);
            _teamDroughtUpdateTimer?.Stop(); _teamDroughtUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _teamDroughtUpdateTimer.Tick += (_, __) => UpdateTeamDroughtText();
            _teamDroughtUpdateTimer.Start();

            _teamDroughtAutoHideTimer?.Stop();
            _teamDroughtAutoHideTimer = StartOverlayAutoHide(TimeSpan.FromSeconds(20), () =>
            {
                _teamDroughtAutoHideTimer = null;
                HideTeamDroughtBar();
            });
        }

        public void HideTeamDroughtBar()
        {
            if (!_teamDroughtBarVisible) return;
            _teamDroughtBarVisible = false;
            _teamDroughtUpdateTimer?.Stop(); _teamDroughtUpdateTimer = null;
            _teamDroughtAutoHideTimer?.Stop(); _teamDroughtAutoHideTimer = null;
            AnimateTeamStoryOut(OnOverlayHidden);
        }

        private void StartTeamDroughtLabelPan()
        {
            ExpTeamDroughtLabelTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            ExpTeamDroughtLabelTranslate.X = 0;
            ExpTeamDroughtLabelText.Width = double.NaN;
        }

        private void UpdateTeamDroughtText()
        {
            if (_pendingDroughtSince == DateTime.MinValue) return;
            var el = OverlayClockNow - _pendingDroughtSince;
            var t = FormatElapsedOverlayTime(el);
            TeamDroughtTimeText.Text = t; ExpTeamDroughtTimeText.Text = t;
            ExpTeamStoryTimer.Text = t;
            if (_teamDroughtBarVisible)
                ExpTeamStoryHeadline.Text = t;
        }

        private static string FormatElapsedOverlayTime(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            return $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";
        }

        // ==== Win probability bar ====

        private bool _winProbBarVisible;
        private DispatcherTimer? _winProbabilityAutoHideTimer;
        private const double WinProbBarHeight = 46;
        private WinProbabilityResult? _pendingWinProb;

        private bool _leadChangesBarVisible;
        private const double LeadChangesBarHeight = 46;
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
            Color ink = SmartContrast.PickSplitBarForeground(homeColor, awayColor);
            WinProbLabelText.Foreground = new SolidColorBrush(ink);
            ExpWinProbInsideLabelText.Foreground = new SolidColorBrush(ink);
            foreach (TextBlock label in new[] { WinProbLabelText, ExpWinProbInsideLabelText })
            {
                if (label.Effect is DropShadowEffect shadow)
                {
                    shadow.Color = ink == Colors.Black ? Colors.White : Colors.Black;
                    shadow.Opacity = 0.92;
                }
            }
        }

        public void SetWinProbColors(string homeHex, string awayHex)
        {
            var home = (Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString(homeHex.StartsWith("#") ? homeHex : $"#{homeHex}");
            var away = (Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString(awayHex.StartsWith("#") ? awayHex : $"#{awayHex}");
            SetWinProbColors(home, away);
        }

        private void DoShowWinProbabilityBar()
        {
            if (_winProbBarVisible) return;
            var result = _pendingWinProb;
            if (result == null) { OnOverlayHidden(); return; }
            _winProbBarVisible = true;
            ApplyWinProbValues(result);
            AnimateOverlayIn(AWinProbBar, WinProbBarHeight);
            _winProbabilityAutoHideTimer?.Stop();
            _winProbabilityAutoHideTimer = StartOverlayAutoHide(TimeSpan.FromSeconds(10), () =>
            {
                _winProbabilityAutoHideTimer = null;
                HideWinProbabilityBar();
            });
        }

        public void HideWinProbabilityBar()
        {
            if (!_winProbBarVisible) return;
            _winProbBarVisible = false;
            _winProbabilityAutoHideTimer?.Stop(); _winProbabilityAutoHideTimer = null;
            AnimateOverlayOut(AWinProbBar, WinProbBarHeight, OnOverlayHidden);
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
        // LEAD CHANGES ° AFL / NRL
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
            EnsureOverlayContrast(isHome, expanded, secondaryColor);

            // Tint the gradient bar to team secondary (white?team instead of rainbow)
            var gradBrush = NativeBrush.Require<LinearGradientBrush>(gradBar.Fill);
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

            // Team-colour gradient wash (0°1.5s, slower sweep)
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
            AnimateOverlayIn(ALeadChangesBar, LeadChangesBarHeight);
            _leadChangesAutoHideTimer?.Stop();
            _leadChangesAutoHideTimer = StartOverlayAutoHide(TimeSpan.FromSeconds(12), () =>
            {
                _leadChangesAutoHideTimer = null;
                HideLeadChangesBar();
            });
        }

        public void HideLeadChangesBar()
        {
            if (!_leadChangesBarVisible) return; _leadChangesBarVisible = false;
            _leadChangesAutoHideTimer?.Stop(); _leadChangesAutoHideTimer = null;
            AnimateOverlayOut(ALeadChangesBar, LeadChangesBarHeight, OnOverlayHidden);
        }

        // ==================================================================
        // QUARTER SCORES ° neon overlay showing each team's tally for the quarter.
        // ==================================================================

        private bool _quarterScoresBarVisible;
        private const double QuarterScoresBarHeight = 64;
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
            // At scoreboard distance the quarter total is the useful number.
            // Abbreviations identify each side without forcing totals smaller.
            ExpQuarterScoresHomeNameText.Text = _homeAbbr;
            ExpQuarterScoresAwayNameText.Text = _awayAbbr;
            ExpQuarterScoresHomeGoalsText.Text = p.Value.HomeGoals.ToString();
            ExpQuarterScoresHomeBehindsText.Text = p.Value.HomeBehinds.ToString();
            ExpQuarterScoresHomeTotalText.Text = hTotal.ToString();
            ExpQuarterScoresAwayGoalsText.Text = p.Value.AwayGoals.ToString();
            ExpQuarterScoresAwayBehindsText.Text = p.Value.AwayBehinds.ToString();
            ExpQuarterScoresAwayTotalText.Text = aTotal.ToString();

            AnimateOverlayIn(ExpQuarterScoresBarBorder, QuarterScoresBarHeight);

            _quarterScoresAutoHideTimer?.Stop();
            _quarterScoresAutoHideTimer = StartOverlayAutoHide(TimeSpan.FromSeconds(10), () =>
            {
                _quarterScoresAutoHideTimer = null;
                HideQuarterScoresBar();
            });
        }

        public void HideQuarterScoresBar()
        {
            if (!_quarterScoresBarVisible) return; _quarterScoresBarVisible = false;
            _quarterScoresAutoHideTimer?.Stop(); _quarterScoresAutoHideTimer = null;
            AnimateOverlayOut(ExpQuarterScoresBarBorder, QuarterScoresBarHeight, OnOverlayHidden);
        }

        // ==================================================================
        // RECENT SCORES — shared drop-down lane showing the last three events.
        // The live score rows, totals and logos never move or fade.
        // ==================================================================

        private bool _recentScoresVisible;
        private DispatcherTimer? _recentScoresAutoHideTimer;
        private DispatcherTimer? _recentScoresDrawerAnimationTimer;
        private DateTime _recentScoresDrawerAnimationStarted;
        private double _recentScoresDrawerAnimationFrom;
        private double _recentScoresDrawerAnimationTo;
        private double _recentScoresDrawerProgress;
        private double _recentScoresProtectedClockWidth;
        private double _recentScoresProtectedLogoWidth;
        private static readonly TimeSpan RecentScoresDrawerDuration = OverlayTransitionDuration;
        private (IReadOnlyList<ScoreEvent>? events, int quarter) _pendingRecentScores;

        private void DoShowRecentScores(IReadOnlyList<ScoreEvent>? events, int currentQuarter)
        {
            _ = currentQuarter; // retained in the public contract for callers
            ExpRecentScoresCards.Children.Clear();

            var src = events is null ? new List<ScoreEvent>() : new List<ScoreEvent>(events);
            int cardLimit = 3;
            int take = Math.Min(cardLimit, src.Count);
            var lastN = take == 0
                ? new List<ScoreEvent>()
                : src.GetRange(src.Count - take, take);
            // Newest card first at the top of the drawer.
            lastN.Reverse();

            bool firstRow = true;
            if (lastN.Count == 0)
            {
                var emptyState = new NativeTextBlock
                {
                    Text = "NO SCORES RECORDED",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xB6, 0xC6)),
                    FontFamily = NativeFonts.Family("Stolzl Bold"),
                    FontWeight = FontWeight.Black,
                    FontSize = 24,
                    HorizontalAlignment = HAlign.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetRowSpan(emptyState, 3);
                ExpRecentScoresCards.Children.Add(emptyState);
            }
            else
            {
                for (int index = 0; index < lastN.Count; index++)
                {
                    Control card = BuildRecentScoreRow(lastN[index], firstRow);
                    Grid.SetRow(card, index);
                    ExpRecentScoresCards.Children.Add(card);
                    firstRow = false;
                }
            }

            if (_recentScoresVisible) return;
            _recentScoresVisible = true;
            _recentScoresProtectedClockWidth = Math.Max(ExpClockPanel.NativeWidth(), ExpClockColumn.NativeWidth());
            _recentScoresProtectedLogoWidth = Math.Max(ExpLogoColumn.NativeWidth(), 1);
            AnimateRecentScoresDrawer(show: true);

            _recentScoresAutoHideTimer?.Stop();
            _recentScoresAutoHideTimer = StartOverlayAutoHide(TimeSpan.FromSeconds(12), () =>
            {
                _recentScoresAutoHideTimer = null;
                HideRecentScores();
            });
        }

        public void HideRecentScores()
        {
            if (!_recentScoresVisible) return;
            _recentScoresVisible = false;
            _recentScoresAutoHideTimer?.Stop(); _recentScoresAutoHideTimer = null;
            AnimateRecentScoresDrawer(show: false, onCompleted: () =>
            {
                if (_activeOverlay == OverlayKind.RecentScores) OnOverlayHidden();
            });
        }

        private void AnimateRecentScoresDrawer(bool show, Action? onCompleted = null, bool animate = true)
        {
            _recentScoresDrawerAnimationTimer?.Stop();
            _recentScoresDrawerAnimationTimer = null;
            double target = show ? 1 : 0;
            if (!animate || !IsLoaded || Math.Abs(_recentScoresDrawerProgress - target) < 0.001)
            {
                _recentScoresDrawerProgress = target;
                ApplyRecentScoresDrawerLayout();
                onCompleted?.Invoke();
                return;
            }

            _recentScoresDrawerAnimationFrom = _recentScoresDrawerProgress;
            _recentScoresDrawerAnimationTo = target;
            _recentScoresDrawerAnimationStarted = DateTime.UtcNow;
            _recentScoresDrawerAnimationTimer = new DispatcherTimer()
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _recentScoresDrawerAnimationTimer.Tick += (_, _) =>
            {
                double linear = Math.Clamp(
                    (DateTime.UtcNow - _recentScoresDrawerAnimationStarted).TotalMilliseconds /
                    RecentScoresDrawerDuration.TotalMilliseconds,
                    0,
                    1);
                double eased = EaseOverlayTransition(linear);
                _recentScoresDrawerProgress = _recentScoresDrawerAnimationFrom
                    + ((_recentScoresDrawerAnimationTo - _recentScoresDrawerAnimationFrom) * eased);
                ApplyRecentScoresDrawerLayout();
                if (linear < 1) return;
                _recentScoresDrawerAnimationTimer?.Stop();
                _recentScoresDrawerAnimationTimer = null;
                _recentScoresDrawerProgress = target;
                ApplyRecentScoresDrawerLayout();
                QueueExpandedResponsiveLayout();
                onCompleted?.Invoke();
            };
            _recentScoresDrawerAnimationTimer.Start();
        }

        private void ApplyRecentScoresDrawerLayout()
        {
            double progress = Math.Clamp(_recentScoresDrawerProgress, 0, 1);
            // This is a public-display information panel, not a tiny utility
            // drawer. Give it enough width for an unmistakable team badge,
            // score type and quarter while preserving clock/logo columns.
            double drawerWidth = Math.Clamp(ExpandedLayout.NativeWidth() * 0.36, 104, 150) * progress;
            ExpRecentScoresDrawerColumn.Width = new GridLength(drawerWidth);
            ExpRecentScoresBarBorder.Opacity = progress;

            if (progress > 0.001)
            {
                if (!_compactClockActive && _recentScoresProtectedClockWidth > 1)
                    ExpClockColumn.Width = new GridLength(_recentScoresProtectedClockWidth);
                if (_recentScoresProtectedLogoWidth > 1)
                    ExpLogoColumn.Width = new GridLength(_recentScoresProtectedLogoWidth);
            }

            double detailWeight = 1.25 * (1 - progress);
            double separatorWidth = 2 * (1 - progress);
            ExpHomeGoalsColumn.Width = new GridLength(detailWeight, GridUnitType.Star);
            ExpHomeBehindsColumn.Width = new GridLength(detailWeight, GridUnitType.Star);
            ExpAwayGoalsColumn.Width = new GridLength(detailWeight, GridUnitType.Star);
            ExpAwayBehindsColumn.Width = new GridLength(detailWeight, GridUnitType.Star);
            ExpHomeGoalsSeparatorColumn.Width = new GridLength(separatorWidth);
            ExpHomeTotalSeparatorColumn.Width = new GridLength(separatorWidth);
            ExpAwayGoalsSeparatorColumn.Width = new GridLength(separatorWidth);
            ExpAwayTotalSeparatorColumn.Width = new GridLength(separatorWidth);
            ExpHomeGoalsCell.Opacity = ExpHomeBehindsCell.Opacity = 1 - progress;
            ExpAwayGoalsCell.Opacity = ExpAwayBehindsCell.Opacity = 1 - progress;
        }

        private Control BuildRecentScoreRow(ScoreEvent ev, bool isMostRecent = false)
        {
            bool isGoal = ev.Type == ScoreType.Goal;
            bool isHome = ev.Team == TeamSide.Home;
            var teamColor = isHome ? _homeColor : _awayColor;
            string label = isGoal ? "G" : "B";

            Color darkBg = Mix(teamColor, Colors.Black, 0.76);
            Color midBg = Mix(teamColor, Colors.Black, 0.48);
            Color cardInk = SmartContrast.PickReadableForeground(
                isHome ? _homeSecondaryColor : _awaySecondaryColor, darkBg, midBg);
            var cardBackground = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new(midBg, 0),
                    new(darkBg, 0.72),
                    new(Mix(teamColor, Colors.Black, 0.86), 1)
                }
            };

            var box = new NativeBorder
            {
                Margin = new Thickness(3, 1, 3, 1),
                Padding = new Thickness(3, 2, 5, 2),
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Color.FromArgb(isMostRecent ? (byte)0xFF : (byte)0xA0,
                    teamColor.R, teamColor.G, teamColor.B)),
                Background = cardBackground,
                HorizontalAlignment = HAlign.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                MinWidth = 0,
                ClipToBounds = true
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            IImage? eventLogo = isHome ? _homeLogoSource : _awayLogoSource;
            if (eventLogo is not null)
            {
                var watermark = new Avalonia.Controls.Image
                {
                    Source = eventLogo,
                    Stretch = Stretch.UniformToFill,
                    Opacity = 0.12,
                    Effect = new BlurEffect { Radius = 7 },
                    IsHitTestVisible = false
                };
                Grid.SetColumnSpan(watermark, 3);
                grid.Children.Add(watermark);

                var crest = new Avalonia.Controls.Image
                {
                    Source = eventLogo,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(2),
                    IsHitTestVisible = false
                };
                Grid.SetColumn(crest, 1);
                grid.Children.Add(crest);
            }
            else
            {
                var identityDot = new Ellipse
                {
                    Width = 18,
                    Height = 18,
                    Fill = new SolidColorBrush(teamColor),
                    Stroke = new SolidColorBrush(cardInk),
                    StrokeThickness = 2,
                    HorizontalAlignment = HAlign.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(identityDot, 1);
                grid.Children.Add(identityDot);
            }

            var accent = new NativeBorder { Background = new SolidColorBrush(teamColor), Margin = new Thickness(0, 0, 4, 0) };
            Grid.SetColumn(accent, 0);
            grid.Children.Add(accent);

            var scoreType = new NativeTextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(cardInk),
                FontFamily = NativeFonts.Family("Arial"),
                FontWeight = FontWeight.Black,
                FontSize = 38,
                HorizontalAlignment = HAlign.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(scoreType, 2);
            grid.Children.Add(scoreType);

            if (isMostRecent)
            {
                // Pulse the complete event card—not just its letter—so the
                // newest score is unmistakable at scoreboard distance.
                box.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.62,
                    TimeSpan.FromMilliseconds(640))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
            }

            box.Child = grid;
            return box;
        }

        public string GetRecentScoresDiagnosticsForQa()
        {
            string childMetrics = string.Join(", ", ExpRecentScoresCards.Children
                .OfType<Control>()
                .Select((child, index) => $"card{index + 1}={child.NativeWidth():0.##}x{child.NativeHeight():0.##}/{child.IsVisible}"));
            return $"visible={_recentScoresVisible}; progress={_recentScoresDrawerProgress:0.###}; " +
                   $"drawer={ExpRecentScoresBarBorder.NativeWidth():0.##}x{ExpRecentScoresBarBorder.NativeHeight():0.##}; " +
                   $"cards-host={ExpRecentScoresCards.NativeWidth():0.##}x{ExpRecentScoresCards.NativeHeight():0.##}; " +
                   $"children={ExpRecentScoresCards.Children.Count}; {childMetrics}";
        }

        // ---- Miscellanous public API for MainWindow integration ----

        public IReadOnlyList<Control> GetQuarterFoldPanels()
            // Top pair, centre anchor, then bottom pair gives the transition a
            // deliberate reading order instead of folding unrelated cells.
            => [ExpHomeLogoCell, ExpHomePanel, ExpClockPanel, ExpAwayLogoCell, ExpAwayPanel];

        public double GetTransitionTopBarHeight()
            => Math.Max(0, ExpTopInfoRow.NativeHeight());

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

        public void SetFinalsMode(bool enabled)
        {
            if (_finalsModeApplied && _finalsMode == enabled) return;
            _finalsModeApplied = true;
            _finalsMode = enabled;
            ApplyFinalsModeVisuals();
        }

        private void ApplyFinalsModeVisuals()
        {
            if (_finalsMode)
            {
                SetGradientStopColor("ExpClockPanelGradTop", "#F2D47A");
                SetGradientStopColor("ExpClockPanelGradMid", "#C9982D");
                SetGradientStopColor("ExpClockPanelGradBot", "#A9781D");
                ExpQuarterNumberText.Foreground = new SolidColorBrush(Services.FinalsPalette.Ink);
                ExpClockText.Foreground = new SolidColorBrush(Services.FinalsPalette.Ink);
                ExpPanelWallTimeText.Foreground = new SolidColorBrush(Services.FinalsPalette.Ink);
                ExpPanelWallTimeSuffixText.Foreground = new SolidColorBrush(Color.FromRgb(0x3A, 0x2A, 0x08));
                // Let the clean gold gradient remain the visible surface. The
                // normal-mode dark half-panels would otherwise muddy the gold
                // and put finals ink over a dark background.
                ExpClockQuarterSurface.Opacity = 0;
                ExpClockTimerSurface.Opacity = 0;
                ExpClockDivider.Background = new SolidColorBrush(Services.FinalsPalette.Ink);
                ExpClockDivider.Opacity = 0.58;
            }
            else
            {
                SetGradientStopColor("ExpClockPanelGradTop", "#1A1F2E");
                SetGradientStopColor("ExpClockPanelGradMid", "#0A0D14");
                SetGradientStopColor("ExpClockPanelGradBot", "#000000");
                ExpQuarterNumberText.Foreground = Brushes.White;
                ExpClockText.Foreground = Brushes.White;
                ExpPanelWallTimeText.Foreground = Brushes.White;
                ExpPanelWallTimeSuffixText.Foreground = new SolidColorBrush(Color.FromRgb(0xBF, 0xD0, 0xE2));
                ExpClockQuarterSurface.Background = new SolidColorBrush(Color.FromRgb(0x12, 0x1A, 0x2A));
                ExpClockQuarterSurface.Opacity = 0.72;
                ExpClockTimerSurface.Background = new SolidColorBrush(Color.FromRgb(0x02, 0x04, 0x09));
                ExpClockTimerSurface.Opacity = 0.38;
                ExpClockDivider.Background = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF));
                ExpClockDivider.Opacity = 1;
            }
        }

        private void SetGradientStopColor(string name, string hex)
        {
            if (NativeLookup.Find(this, name) is GradientStop stop)
                stop.Color = (Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString(hex);
        }

        /// <summary>
        /// Shows or hides the team name rows in the expanded scorebug.
        /// When hidden the score cells (goals / behinds / total) expand to fill the
        /// available panel height while keeping digit-safe typography stable.
        /// </summary>
        public void SetHideTeamNames(bool hide)
        {
            if (_hideTeamNamesApplied && _hideTeamNames == hide) return;
            _hideTeamNamesApplied = true;
            _hideTeamNames = hide;
            AnimateTeamNamePanelsForOverlay(_activeOverlay is not null);
        }

        private void AnimateTeamNamePanelsForOverlay(bool hide, bool animate = true)
        {
            double target = _hideTeamNames || hide ? 0 : 1;
            _teamNamePanelAnimationTimer?.Stop();
            _teamNamePanelAnimationTimer = null;

            if (!animate || !IsLoaded || Math.Abs(_teamNameRevealProgress - target) < 0.001)
            {
                _teamNameRevealProgress = target;
                ApplyExpandedResponsiveProportions();
                QueueExpandedResponsiveLayout();
                return;
            }

            _teamNamePanelAnimationFrom = _teamNameRevealProgress;
            _teamNamePanelAnimationTo = target;
            _teamNamePanelAnimationStarted = DateTime.UtcNow;
            _teamNamePanelAnimationTimer = new DispatcherTimer()
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _teamNamePanelAnimationTimer.Tick += TeamNamePanelAnimationTick;
            _teamNamePanelAnimationTimer.Start();
        }

        private void TeamNamePanelAnimationTick(object? sender, EventArgs e)
        {
            double linear = Math.Clamp(
                (DateTime.UtcNow - _teamNamePanelAnimationStarted).TotalMilliseconds /
                TeamNamePanelAnimationDuration.TotalMilliseconds,
                0,
                1);
            // Smoothstep keeps both the initial collapse and final settle calm,
            // while the changing star ratio continuously gives space to scores.
            double eased = EaseOverlayTransition(linear);
            _teamNameRevealProgress = _teamNamePanelAnimationFrom
                + ((_teamNamePanelAnimationTo - _teamNamePanelAnimationFrom) * eased);
            ApplyExpandedResponsiveProportions();

            if (linear < 1) return;

            _teamNamePanelAnimationTimer?.Stop();
            _teamNamePanelAnimationTimer = null;
            _teamNameRevealProgress = _teamNamePanelAnimationTo;
            ApplyExpandedResponsiveProportions();
            QueueExpandedResponsiveLayout();
        }
        public void SetWeatherLocation(string? location) => _weatherLocation = location;

        public void PauseScorelessForBreak() => NotifyClockStopped();
        public void ResumeScorelessFromBreak() => NotifyClockStarted();
        public void ClearBreakGBLock() { }
        public void ExpandGBColumnsForBreak() => ExpandGBColumns();

        public bool CheckAutoTeamDrought()
        {
            DateTime now = OverlayClockNow;
            double homeElapsed = _homeLastScoreTime == DateTime.MinValue
                ? 0 : (now - _homeLastScoreTime).TotalMinutes;
            double awayElapsed = _awayLastScoreTime == DateTime.MinValue
                ? 0 : (now - _awayLastScoreTime).TotalMinutes;

            // When both clubs are droughting, the match-wide LAST SCORE story
            // is the only useful graphic. Advance both team milestones so they
            // cannot backlog and fire as soon as that overlay closes.
            if (homeElapsed >= 10 && awayElapsed >= 10)
            {
                _homeDroughtMilestoneMinutes = (int)Math.Floor(homeElapsed / 10) * 10;
                _awayDroughtMilestoneMinutes = (int)Math.Floor(awayElapsed / 10) * 10;
                return false;
            }

            int homeMilestone = (int)Math.Floor(homeElapsed / 10) * 10;
            if (homeMilestone >= 10 && homeMilestone > _homeDroughtMilestoneMinutes)
            {
                _homeDroughtMilestoneMinutes = homeMilestone;
                _pendingAutoDroughtIsHome = true;
                return true;
            }

            int awayMilestone = (int)Math.Floor(awayElapsed / 10) * 10;
            if (awayMilestone >= 10 && awayMilestone > _awayDroughtMilestoneMinutes)
            {
                _awayDroughtMilestoneMinutes = awayMilestone;
                _pendingAutoDroughtIsHome = false;
                return true;
            }

            return false;
        }

        public bool GetDroughtTeamIsHome() => _pendingAutoDroughtIsHome;
        public DateTime GetDroughtSince(bool isHome) => isHome ? _homeLastScoreTime : _awayLastScoreTime;
        public DateTime HomeLastScoreTime => _homeLastScoreTime;
        public DateTime AwayLastScoreTime => _awayLastScoreTime;
        public void ShowLastScoreTime() => ShowScorelessBar();

        public bool CancelInformationalOverlay()
        {
            if (_activeOverlay is not (OverlayKind.StatsBar or OverlayKind.WinProbability
                or OverlayKind.QuarterScores or OverlayKind.RecentScores
                or OverlayKind.WeatherStats or OverlayKind.Forecast or OverlayKind.RainForecast))
                return false;

            _overlayQueue.Clear();
            HideActiveOverlay();
            return true;
        }

        public bool CancelScorelessOrDroughtOverlay(bool scoringTeamIsHome)
        {
            bool cancelled = false;
            if (_scorelessBarVisible)
            {
                HideScorelessBar();
                cancelled = true;
            }

            // A featured team's score row remains on air and can animate in
            // place. Cancel only when the scoring team is the displaced club.
            if (_teamDroughtBarVisible && scoringTeamIsHome != _teamStoryIsHome)
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
        private const double WeatherBarHeight = 54;

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
            AnimateOverlayIn(ExpWeatherStatsBorder, WeatherBarHeight);

            _weatherStatsHideTimer?.Stop();
            _weatherStatsHideTimer = StartOverlayAutoHide(TimeSpan.FromSeconds(10), () =>
            {
                _weatherStatsHideTimer = null;
                HideWeatherStats();
            });
        }

        private void HideWeatherStats()
        {
            if (!_weatherStatsVisible) return;
            _weatherStatsVisible = false;
            _weatherStatsHideTimer?.Stop(); _weatherStatsHideTimer = null;
            AnimateOverlayOut(ExpWeatherStatsBorder, WeatherBarHeight,
                () => { if (_activeOverlay == OverlayKind.WeatherStats) OnOverlayHidden(); });
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
                var tile = new NativeBorder
                {
                    Margin = new Thickness(1, 0, 1, 0),
                    BorderThickness = new Thickness(0),
                    Background = new SolidColorBrush(Color.FromArgb(0x44, 0x06, 0x1A, 0x2C)),
                    CornerRadius = new CornerRadius(0),
                    Padding = new Thickness(2, 0, 2, 0)
                };
                var sp = new StackPanel { HorizontalAlignment = HAlign.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                sp.Children.Add(new NativeTextBlock
                {
                    Text = h.Time.ToString("h tt", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant(),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                    FontFamily = NativeFonts.Family("Stolzl Bold"),
                    FontWeight = FontWeight.Black,
                    FontSize = 17,
                    LineHeight = 19,
                    HorizontalAlignment = HAlign.Center,
                    Margin = new Thickness(0)
                });
                // Icon removed for clarity; keep tile content to time + temp only.
                // Icon line removed for clarity; keep tile content to time + temp only.
                sp.Children.Add(new NativeTextBlock
                {
                    Text = $"{Math.Round(h.Temperature)}\u00B0",
                    Foreground = Brushes.White,
                    FontFamily = NativeFonts.Family("Stolzl Bold"),
                    FontWeight = FontWeight.Black,
                    FontSize = 30,
                    LineHeight = 32,
                    HorizontalAlignment = HAlign.Center,
                    Margin = new Thickness(0)
                });
                // Wrap in a Viewbox so the time/icon/temp shrink to fit the
                // narrow tile width when there are four cells side-by-side.
                // DownOnly prevents oversized text on very wide layouts.
                tile.Child = new Viewbox
                {
                    Stretch = Avalonia.Media.Stretch.Uniform,
                    StretchDirection = Avalonia.Media.StretchDirection.DownOnly,
                    Margin = new Thickness(1),
                    Child = sp
                };
                ExpForecastTiles.Children.Add(tile);
            }

            if (_forecastVisible) return;
            _forecastVisible = true;
            AnimateOverlayIn(ExpForecastBorder, WeatherBarHeight);

            _forecastHideTimer?.Stop();
            _forecastHideTimer = StartOverlayAutoHide(TimeSpan.FromSeconds(12), () =>
            {
                _forecastHideTimer = null;
                HideForecast();
            });
        }

        private void HideForecast()
        {
            if (!_forecastVisible) return;
            _forecastVisible = false;
            _forecastHideTimer?.Stop(); _forecastHideTimer = null;
            AnimateOverlayOut(ExpForecastBorder, WeatherBarHeight,
                () => { if (_activeOverlay == OverlayKind.Forecast) OnOverlayHidden(); });
        }

        private void DoShowRain()
        {
            if (_pendingRain is null) { OnOverlayHidden(); return; }
            var s = _pendingRain;
            ExpRainBars.Children.Clear();
            int count = Math.Min(4, s.HourlyForecast?.Count ?? 0);
            int peak = 0;
            for (int i = 0; i < count; i++)
            {
                var h = s.HourlyForecast![i];
                if (h.PrecipitationProbability > peak) peak = h.PrecipitationProbability;

                var col = new Grid { Margin = new Thickness(3, 0, 3, 0) };
                col.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var trackContainer = new Grid { VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 1, 0, 2) };
                var track = new NativeBorder
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x33, 0x38, 0xBD, 0xF8)),
                    CornerRadius = new CornerRadius(0)
                };
                trackContainer.Children.Add(track);

                double pct = Math.Clamp(h.PrecipitationProbability / 100.0, 0.0, 1.0);
                var fillRow = new Grid { VerticalAlignment = VerticalAlignment.Bottom };
                fillRow.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0 - pct, GridUnitType.Star) });
                fillRow.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(pct, 0.0001), GridUnitType.Star) });
                var fill = new NativeBorder
                {
                    CornerRadius = new CornerRadius(0),
                    Background = NativeBrush.Linear(
                        Color.FromRgb(0x38, 0xBD, 0xF8), Color.FromRgb(0x06, 0x6E, 0xC9), 90)
                };
                Grid.SetRow(fill, 1);
                fillRow.Children.Add(fill);
                trackContainer.Children.Add(fillRow);

                Grid.SetRow(trackContainer, 0);
                col.Children.Add(trackContainer);

                var stack = new StackPanel { HorizontalAlignment = HAlign.Center, Margin = new Thickness(0, 1, 0, 0) };
                stack.Children.Add(new NativeTextBlock
                {
                    Text = $"{h.PrecipitationProbability}%",
                    Foreground = Brushes.White,
                    FontFamily = NativeFonts.Family("Stolzl Bold"),
                    FontSize = 16,
                    FontWeight = FontWeight.Black,
                    HorizontalAlignment = HAlign.Center
                });
                stack.Children.Add(new NativeTextBlock
                {
                    Text = h.Time.ToString("htt", System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant(),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                    FontFamily = NativeFonts.Family("Stolzl Bold"),
                    FontSize = 13,
                    FontWeight = FontWeight.Black,
                    HorizontalAlignment = HAlign.Center
                });
                Grid.SetRow(stack, 1);
                col.Children.Add(stack);

                ExpRainBars.Children.Add(col);
            }
            ExpRainPeakValue.Text = $"{peak}%";

            if (_rainVisible) return;
            _rainVisible = true;
            AnimateOverlayIn(ExpRainBorder, WeatherBarHeight);

            _rainHideTimer?.Stop();
            _rainHideTimer = StartOverlayAutoHide(TimeSpan.FromSeconds(12), () =>
            {
                _rainHideTimer = null;
                HideRain();
            });
        }

        private void HideRain()
        {
            if (!_rainVisible) return;
            _rainVisible = false;
            _rainHideTimer?.Stop(); _rainHideTimer = null;
            AnimateOverlayOut(ExpRainBorder, WeatherBarHeight,
                () => { if (_activeOverlay == OverlayKind.RainForecast) OnOverlayHidden(); });
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
            _lastScoreMilestoneMinutes = 0;
            _homeLastScoreTime = DateTime.MinValue;
            _awayLastScoreTime = DateTime.MinValue;
            _homeDroughtMilestoneMinutes = 0;
            _awayDroughtMilestoneMinutes = 0;
            _pendingScoringRunTeam = string.Empty;
            _pendingGoalRunGoals = 0;
            _pendingGoalRunWindow = 0;
            _pendingGoalRunConsecutive = false;
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
            _overlayAutoHideGeneration++;
            _activeOverlay = null;
            _overlayIsExiting = false;
            _overlayTransitionGap = false;
            _overlayLayoutMotionActive = false;
            _overlayResponsiveRefreshPending = false;
            _overlayLayoutMotionGeneration++;
            _scoreSurfaceReadyCallbacks.Clear();
            ExpSharedOverlayLogoLayer.IsVisible = false;
            AnimateTeamNamePanelsForOverlay(hide: false, animate: false);
            AnimateRecentScoresDrawer(show: false, animate: false);

            _statsBarVisible = false;
            _scorelessBarVisible = false;
            _scoringRunBarVisible = false;
            _teamDroughtBarVisible = false;
            _winProbBarVisible = false;
            _leadChangesBarVisible = false;
            _quarterScoresBarVisible = false;
            _recentScoresVisible = false;
            _weatherStatsVisible = false;
            _forecastVisible = false;
            _rainVisible = false;
            _warningVisible = false;
            _teamStoryVisible = false;
            _teamStoryAnimationGeneration++;
            ExpTeamStoryReveal.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            ExpTeamStoryReveal.ScaleY = 0;
            ExpTeamStoryContent.BeginAnimation(OpacityProperty, null);
            ExpTeamStoryContent.Opacity = 0;
            ExpTeamStoryTakeover.IsVisible = false;
            NativeCache.Set(ExpTeamStoryTakeover, null);
            StopTeamStoryDirection();
            foreach (TranslateTransform translate in new[]
                     {
                         ExpHomeLogoStoryTranslate, ExpAwayLogoStoryTranslate,
                         ExpHomeScoreStoryTranslate, ExpAwayScoreStoryTranslate
                     })
            {
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.Y = 0;
            }

            _statsRotationTimer?.Stop();
            _statsRotationTimer = null;
            _scorelessUpdateTimer?.Stop();
            _scorelessUpdateTimer = null;
            _scorelessAutoHideTimer?.Stop();
            _scorelessAutoHideTimer = null;
            _scoringRunAutoHideTimer?.Stop();
            _scoringRunAutoHideTimer = null;
            _teamDroughtUpdateTimer?.Stop();
            _teamDroughtUpdateTimer = null;
            _teamDroughtAutoHideTimer?.Stop();
            _teamDroughtAutoHideTimer = null;
            _winProbabilityAutoHideTimer?.Stop();
            _winProbabilityAutoHideTimer = null;
            _leadChangesAutoHideTimer?.Stop();
            _leadChangesAutoHideTimer = null;
            _quarterScoresAutoHideTimer?.Stop();
            _quarterScoresAutoHideTimer = null;
            _recentScoresAutoHideTimer?.Stop();
            _recentScoresAutoHideTimer = null;
            _weatherStatsHideTimer?.Stop(); _weatherStatsHideTimer = null;
            _forecastHideTimer?.Stop(); _forecastHideTimer = null;
            _rainHideTimer?.Stop(); _rainHideTimer = null;
            _warningAutoHideTimer?.Stop(); _warningAutoHideTimer = null;

            ClearHeight(StatsBarBorder); ClearHeight(ExpStatsBarBorder);
            ClearHeight(ScorelessBarBorder); ClearHeight(ExpScorelessBarBorder);
            ClearHeight(ScoringRunBarBorder); ClearHeight(ExpScoringRunBarBorder);
            ClearHeight(TeamDroughtBarBorder); ClearHeight(ExpTeamDroughtBarBorder);
            ClearHeight(WinProbBarBorder); ClearHeight(ExpWinProbBarBorder);
            ClearHeight(LeadChangesBarBorder); ClearHeight(ExpLeadChangesBarBorder);
            ClearHeight(ExpQuarterScoresBarBorder);
            ExpRecentScoresBarBorder.BeginAnimation(HeightProperty, null);
            ExpRecentScoresBarBorder.Height = double.NaN;
            ClearHeight(ExpWeatherStatsBorder);
            ClearHeight(ExpForecastBorder);
            ClearHeight(ExpRainBorder);
            ClearHeight(ExpWarningBanner);
            ExpWarningBanner.Opacity = 0;
            WarningOverlay.BeginAnimation(OpacityProperty, null);
            WarningOverlay.Opacity = 0;
            ResumeTickerAfterWarning();
        }

        private static void ClearHeight(Border b)
        {
            b.BeginAnimation(HeightProperty, null);
            b.BeginAnimation(OpacityProperty, null);
            b.Height = 0;
            b.Opacity = 0;
            NativeCache.Set(b, null);
            if (b.RenderTransform is TranslateTransform translate)
            {
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.Y = 0;
            }
        }
    }
}


namespace Roche_Scoreboard.Views
{
public partial class ScorebugControl
{
    private LinearGradientBrush TeamGradient => (LinearGradientBrush)ProductionNames.RegistryFor(this)["TeamGradient"];
    private TranslateTransform StatsBarContentTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["StatsBarContentTranslate"];
    private ColumnDefinition StatsBarHomeCol => (ColumnDefinition)ProductionNames.RegistryFor(this)["StatsBarHomeCol"];
    private ColumnDefinition StatsBarAwayCol => (ColumnDefinition)ProductionNames.RegistryFor(this)["StatsBarAwayCol"];
    private SolidColorBrush StatsHomeBarBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["StatsHomeBarBrush"];
    private SolidColorBrush StatsAwayBarBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["StatsAwayBarBrush"];
    private LinearGradientBrush ScoringRunGradient => (LinearGradientBrush)ProductionNames.RegistryFor(this)["ScoringRunGradient"];
    private LinearGradientBrush TeamDroughtGradient => (LinearGradientBrush)ProductionNames.RegistryFor(this)["TeamDroughtGradient"];
    private ColumnDefinition WinProbHomeCol => (ColumnDefinition)ProductionNames.RegistryFor(this)["WinProbHomeCol"];
    private ColumnDefinition WinProbAwayCol => (ColumnDefinition)ProductionNames.RegistryFor(this)["WinProbAwayCol"];
    private SolidColorBrush WinProbHomeBarBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["WinProbHomeBarBrush"];
    private SolidColorBrush WinProbAwayBarBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["WinProbAwayBarBrush"];
    private TranslateTransform HomeGoalsFlip => (TranslateTransform)ProductionNames.RegistryFor(this)["HomeGoalsFlip"];
    private TranslateTransform HomeBehindsFlip => (TranslateTransform)ProductionNames.RegistryFor(this)["HomeBehindsFlip"];
    private TranslateTransform HomeTotalFlip => (TranslateTransform)ProductionNames.RegistryFor(this)["HomeTotalFlip"];
    private SolidColorBrush HomeSwipeBg => (SolidColorBrush)ProductionNames.RegistryFor(this)["HomeSwipeBg"];
    private TranslateTransform HomeSwipeTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["HomeSwipeTranslate"];
    private SolidColorBrush HomeSwipeFg => (SolidColorBrush)ProductionNames.RegistryFor(this)["HomeSwipeFg"];
    private SolidColorBrush HomeOverlayBg => (SolidColorBrush)ProductionNames.RegistryFor(this)["HomeOverlayBg"];
    private LinearGradientBrush HomeGradientBarBrush => (LinearGradientBrush)ProductionNames.RegistryFor(this)["HomeGradientBarBrush"];
    private TranslateTransform HomeGradientScroll => (TranslateTransform)ProductionNames.RegistryFor(this)["HomeGradientScroll"];
    private TranslateTransform HomeShimmerScroll => (TranslateTransform)ProductionNames.RegistryFor(this)["HomeShimmerScroll"];
    private TranslateTransform AwayGoalsFlip => (TranslateTransform)ProductionNames.RegistryFor(this)["AwayGoalsFlip"];
    private TranslateTransform AwayBehindsFlip => (TranslateTransform)ProductionNames.RegistryFor(this)["AwayBehindsFlip"];
    private TranslateTransform AwayTotalFlip => (TranslateTransform)ProductionNames.RegistryFor(this)["AwayTotalFlip"];
    private SolidColorBrush AwaySwipeBg => (SolidColorBrush)ProductionNames.RegistryFor(this)["AwaySwipeBg"];
    private TranslateTransform AwaySwipeTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["AwaySwipeTranslate"];
    private SolidColorBrush AwaySwipeFg => (SolidColorBrush)ProductionNames.RegistryFor(this)["AwaySwipeFg"];
    private SolidColorBrush AwayOverlayBg => (SolidColorBrush)ProductionNames.RegistryFor(this)["AwayOverlayBg"];
    private LinearGradientBrush AwayGradientBarBrush => (LinearGradientBrush)ProductionNames.RegistryFor(this)["AwayGradientBarBrush"];
    private TranslateTransform AwayGradientScroll => (TranslateTransform)ProductionNames.RegistryFor(this)["AwayGradientScroll"];
    private TranslateTransform AwayShimmerScroll => (TranslateTransform)ProductionNames.RegistryFor(this)["AwayShimmerScroll"];
    private ScaleTransform QuarterNumberTextScale => (ScaleTransform)ProductionNames.RegistryFor(this)["QuarterNumberTextScale"];
    private ScaleTransform ClockTextScale => (ScaleTransform)ProductionNames.RegistryFor(this)["ClockTextScale"];
    private TranslateTransform WarningStripesTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["WarningStripesTranslate"];
    private TranslateTransform WarningScanlineTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["WarningScanlineTranslate"];
    private SkewTransform WarningLetterSkew => (SkewTransform)ProductionNames.RegistryFor(this)["WarningLetterSkew"];
    private ScaleTransform WarningLetterPulse => (ScaleTransform)ProductionNames.RegistryFor(this)["WarningLetterPulse"];
    private TranslateTransform MarqueeHighlightTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["MarqueeHighlightTranslate"];
    private TranslateTransform MarqueeTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["MarqueeTranslate"];
    private RowDefinition ExpTopInfoRow => (RowDefinition)ProductionNames.RegistryFor(this)["ExpTopInfoRow"];
    private RowDefinition ExpBottomBarRow => (RowDefinition)ProductionNames.RegistryFor(this)["ExpBottomBarRow"];
    private ColumnDefinition ExpTopWeatherColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpTopWeatherColumn"];
    private ColumnDefinition ExpTopClockColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpTopClockColumn"];
    private ColumnDefinition ExpTopDateColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpTopDateColumn"];
    private TranslateTransform ExpBottomBarSlide => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpBottomBarSlide"];
    private TranslateTransform ExpMarqueeHighlightTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpMarqueeHighlightTranslate"];
    private TranslateTransform ExpMarqueeTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpMarqueeTranslate"];
    private ColumnDefinition ExpCompactWallTimeColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpCompactWallTimeColumn"];
    private TranslateTransform ExpStatsBarContentTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpStatsBarContentTranslate"];
    private ColumnDefinition ExpStatsHomeValueColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpStatsHomeValueColumn"];
    private ColumnDefinition ExpStatsAwayValueColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpStatsAwayValueColumn"];
    private ColumnDefinition ExpStatsBarHomeCol => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpStatsBarHomeCol"];
    private ColumnDefinition ExpStatsBarAwayCol => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpStatsBarAwayCol"];
    private SolidColorBrush ExpStatsHomeBarBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["ExpStatsHomeBarBrush"];
    private SolidColorBrush ExpStatsAwayBarBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["ExpStatsAwayBarBrush"];
    private LinearGradientBrush ExpScoringRunGradient => (LinearGradientBrush)ProductionNames.RegistryFor(this)["ExpScoringRunGradient"];
    private SolidColorBrush ExpTeamDroughtGradient => (SolidColorBrush)ProductionNames.RegistryFor(this)["ExpTeamDroughtGradient"];
    private TranslateTransform ExpTeamDroughtLabelTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpTeamDroughtLabelTranslate"];
    private ColumnDefinition ExpWinProbHomeValueColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpWinProbHomeValueColumn"];
    private ColumnDefinition ExpWinProbAwayValueColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpWinProbAwayValueColumn"];
    private ColumnDefinition ExpWinProbHomeCol => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpWinProbHomeCol"];
    private ColumnDefinition ExpWinProbAwayCol => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpWinProbAwayCol"];
    private SolidColorBrush ExpWinProbHomeBarBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["ExpWinProbHomeBarBrush"];
    private SolidColorBrush ExpWinProbAwayBarBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["ExpWinProbAwayBarBrush"];
    private SolidColorBrush ExpQuarterScoresHomeTotalBg => (SolidColorBrush)ProductionNames.RegistryFor(this)["ExpQuarterScoresHomeTotalBg"];
    private SolidColorBrush ExpQuarterScoresHomeTotalBorder => (SolidColorBrush)ProductionNames.RegistryFor(this)["ExpQuarterScoresHomeTotalBorder"];
    private SolidColorBrush ExpQuarterScoresAwayTotalBg => (SolidColorBrush)ProductionNames.RegistryFor(this)["ExpQuarterScoresAwayTotalBg"];
    private SolidColorBrush ExpQuarterScoresAwayTotalBorder => (SolidColorBrush)ProductionNames.RegistryFor(this)["ExpQuarterScoresAwayTotalBorder"];
    private SolidColorBrush ExpQuarterScoresHomeGoalsBg => (SolidColorBrush)ProductionNames.RegistryFor(this)["ExpQuarterScoresHomeGoalsBg"];
    private SolidColorBrush ExpQuarterScoresHomeGoalsBorder => (SolidColorBrush)ProductionNames.RegistryFor(this)["ExpQuarterScoresHomeGoalsBorder"];
    private SolidColorBrush ExpQuarterScoresHomeBehindsBg => (SolidColorBrush)ProductionNames.RegistryFor(this)["ExpQuarterScoresHomeBehindsBg"];
    private SolidColorBrush ExpQuarterScoresHomeBehindsBorder => (SolidColorBrush)ProductionNames.RegistryFor(this)["ExpQuarterScoresHomeBehindsBorder"];
    private SolidColorBrush ExpQuarterScoresAwayGoalsBg => (SolidColorBrush)ProductionNames.RegistryFor(this)["ExpQuarterScoresAwayGoalsBg"];
    private SolidColorBrush ExpQuarterScoresAwayGoalsBorder => (SolidColorBrush)ProductionNames.RegistryFor(this)["ExpQuarterScoresAwayGoalsBorder"];
    private SolidColorBrush ExpQuarterScoresAwayBehindsBg => (SolidColorBrush)ProductionNames.RegistryFor(this)["ExpQuarterScoresAwayBehindsBg"];
    private SolidColorBrush ExpQuarterScoresAwayBehindsBorder => (SolidColorBrush)ProductionNames.RegistryFor(this)["ExpQuarterScoresAwayBehindsBorder"];
    private TranslateTransform ExpWarningBannerSlide => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpWarningBannerSlide"];
    private TranslateTransform ExpWarningBannerStripesTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpWarningBannerStripesTranslate"];
    private ScaleTransform ExpWarningAlertScale => (ScaleTransform)ProductionNames.RegistryFor(this)["ExpWarningAlertScale"];
    private ScaleTransform ExpWarningBannerLetterPulse => (ScaleTransform)ProductionNames.RegistryFor(this)["ExpWarningBannerLetterPulse"];
    private TranslateTransform ExpWarningBannerScanlineTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpWarningBannerScanlineTranslate"];
    private ColumnDefinition ExpClockColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpClockColumn"];
    private ColumnDefinition ExpLogoColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpLogoColumn"];
    private ColumnDefinition ExpScoresColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpScoresColumn"];
    private ColumnDefinition ExpRecentScoresDrawerColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpRecentScoresDrawerColumn"];
    private GradientStop ExpClockPanelGradTop => (GradientStop)ProductionNames.RegistryFor(this)["ExpClockPanelGradTop"];
    private GradientStop ExpClockPanelGradMid => (GradientStop)ProductionNames.RegistryFor(this)["ExpClockPanelGradMid"];
    private GradientStop ExpClockPanelGradBot => (GradientStop)ProductionNames.RegistryFor(this)["ExpClockPanelGradBot"];
    private RowDefinition ExpPanelWallTimeRow => (RowDefinition)ProductionNames.RegistryFor(this)["ExpPanelWallTimeRow"];
    private ScaleTransform ExpQuarterNumberTextScale => (ScaleTransform)ProductionNames.RegistryFor(this)["ExpQuarterNumberTextScale"];
    private ScaleTransform ExpClockTextScale => (ScaleTransform)ProductionNames.RegistryFor(this)["ExpClockTextScale"];
    private TranslateTransform ExpWarningStripesTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpWarningStripesTranslate"];
    private TranslateTransform ExpWarningScanlineTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpWarningScanlineTranslate"];
    private SkewTransform ExpWarningLetterSkew => (SkewTransform)ProductionNames.RegistryFor(this)["ExpWarningLetterSkew"];
    private ScaleTransform ExpWarningLetterPulse => (ScaleTransform)ProductionNames.RegistryFor(this)["ExpWarningLetterPulse"];
    private TranslateTransform ExpLogoPanelsTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpLogoPanelsTranslate"];
    private TranslateTransform ExpHomeLogoStoryTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpHomeLogoStoryTranslate"];
    private GradientStop ExpHomeLogoCellGradStart => (GradientStop)ProductionNames.RegistryFor(this)["ExpHomeLogoCellGradStart"];
    private GradientStop ExpHomeLogoCellGradEnd => (GradientStop)ProductionNames.RegistryFor(this)["ExpHomeLogoCellGradEnd"];
    private TranslateTransform ExpAwayLogoStoryTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpAwayLogoStoryTranslate"];
    private GradientStop ExpAwayLogoCellGradStart => (GradientStop)ProductionNames.RegistryFor(this)["ExpAwayLogoCellGradStart"];
    private GradientStop ExpAwayLogoCellGradEnd => (GradientStop)ProductionNames.RegistryFor(this)["ExpAwayLogoCellGradEnd"];
    private TranslateTransform ExpScorePanelsTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpScorePanelsTranslate"];
    private TranslateTransform ExpHomeScoreStoryTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpHomeScoreStoryTranslate"];
    private GradientStop ExpHomePanelGradStart => (GradientStop)ProductionNames.RegistryFor(this)["ExpHomePanelGradStart"];
    private GradientStop ExpHomePanelGradEnd => (GradientStop)ProductionNames.RegistryFor(this)["ExpHomePanelGradEnd"];
    private RowDefinition ExpHomeNameRow => (RowDefinition)ProductionNames.RegistryFor(this)["ExpHomeNameRow"];
    private RowDefinition ExpHomeScoreRow => (RowDefinition)ProductionNames.RegistryFor(this)["ExpHomeScoreRow"];
    private TranslateTransform ExpHomeNameSlide => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpHomeNameSlide"];
    private ColumnDefinition ExpHomeGoalsColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpHomeGoalsColumn"];
    private ColumnDefinition ExpHomeGoalsSeparatorColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpHomeGoalsSeparatorColumn"];
    private ColumnDefinition ExpHomeBehindsColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpHomeBehindsColumn"];
    private ColumnDefinition ExpHomeTotalSeparatorColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpHomeTotalSeparatorColumn"];
    private ColumnDefinition ExpHomeTotalColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpHomeTotalColumn"];
    private TranslateTransform ExpHomeGoalsSlide => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpHomeGoalsSlide"];
    private TranslateTransform ExpHomeGoalsFlip => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpHomeGoalsFlip"];
    private TranslateTransform ExpHomeBehindsSlide => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpHomeBehindsSlide"];
    private TranslateTransform ExpHomeBehindsFlip => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpHomeBehindsFlip"];
    private TranslateTransform ExpHomeTotalSlide => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpHomeTotalSlide"];
    private TranslateTransform ExpHomeTotalFlip => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpHomeTotalFlip"];
    private SolidColorBrush ExpHomeSwipeBg => (SolidColorBrush)ProductionNames.RegistryFor(this)["ExpHomeSwipeBg"];
    private TranslateTransform ExpHomeSwipeTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpHomeSwipeTranslate"];
    private SolidColorBrush ExpHomeSwipeFg => (SolidColorBrush)ProductionNames.RegistryFor(this)["ExpHomeSwipeFg"];
    private SolidColorBrush ExpHomeOverlayBg => (SolidColorBrush)ProductionNames.RegistryFor(this)["ExpHomeOverlayBg"];
    private LinearGradientBrush ExpHomeGradBarBrush => (LinearGradientBrush)ProductionNames.RegistryFor(this)["ExpHomeGradBarBrush"];
    private TranslateTransform ExpHomeGradScroll => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpHomeGradScroll"];
    private TranslateTransform ExpHomeShimmerScroll => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpHomeShimmerScroll"];
    private DropShadowEffect ExpHomeOverlayGlow => (DropShadowEffect)ProductionNames.RegistryFor(this)["ExpHomeOverlayGlow"];
    private ScaleTransform ExpHomeOverlayScale => (ScaleTransform)ProductionNames.RegistryFor(this)["ExpHomeOverlayScale"];
    private TranslateTransform ExpAwayScoreStoryTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpAwayScoreStoryTranslate"];
    private GradientStop ExpAwayPanelGradStart => (GradientStop)ProductionNames.RegistryFor(this)["ExpAwayPanelGradStart"];
    private GradientStop ExpAwayPanelGradEnd => (GradientStop)ProductionNames.RegistryFor(this)["ExpAwayPanelGradEnd"];
    private RowDefinition ExpAwayNameRow => (RowDefinition)ProductionNames.RegistryFor(this)["ExpAwayNameRow"];
    private RowDefinition ExpAwayScoreRow => (RowDefinition)ProductionNames.RegistryFor(this)["ExpAwayScoreRow"];
    private TranslateTransform ExpAwayNameSlide => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpAwayNameSlide"];
    private ColumnDefinition ExpAwayGoalsColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpAwayGoalsColumn"];
    private ColumnDefinition ExpAwayGoalsSeparatorColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpAwayGoalsSeparatorColumn"];
    private ColumnDefinition ExpAwayBehindsColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpAwayBehindsColumn"];
    private ColumnDefinition ExpAwayTotalSeparatorColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpAwayTotalSeparatorColumn"];
    private ColumnDefinition ExpAwayTotalColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpAwayTotalColumn"];
    private TranslateTransform ExpAwayGoalsSlide => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpAwayGoalsSlide"];
    private TranslateTransform ExpAwayGoalsFlip => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpAwayGoalsFlip"];
    private TranslateTransform ExpAwayBehindsSlide => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpAwayBehindsSlide"];
    private TranslateTransform ExpAwayBehindsFlip => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpAwayBehindsFlip"];
    private TranslateTransform ExpAwayTotalSlide => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpAwayTotalSlide"];
    private TranslateTransform ExpAwayTotalFlip => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpAwayTotalFlip"];
    private SolidColorBrush ExpAwaySwipeBg => (SolidColorBrush)ProductionNames.RegistryFor(this)["ExpAwaySwipeBg"];
    private TranslateTransform ExpAwaySwipeTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpAwaySwipeTranslate"];
    private SolidColorBrush ExpAwaySwipeFg => (SolidColorBrush)ProductionNames.RegistryFor(this)["ExpAwaySwipeFg"];
    private SolidColorBrush ExpAwayOverlayBg => (SolidColorBrush)ProductionNames.RegistryFor(this)["ExpAwayOverlayBg"];
    private LinearGradientBrush ExpAwayGradBarBrush => (LinearGradientBrush)ProductionNames.RegistryFor(this)["ExpAwayGradBarBrush"];
    private TranslateTransform ExpAwayGradScroll => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpAwayGradScroll"];
    private TranslateTransform ExpAwayShimmerScroll => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpAwayShimmerScroll"];
    private DropShadowEffect ExpAwayOverlayGlow => (DropShadowEffect)ProductionNames.RegistryFor(this)["ExpAwayOverlayGlow"];
    private ScaleTransform ExpAwayOverlayScale => (ScaleTransform)ProductionNames.RegistryFor(this)["ExpAwayOverlayScale"];
    private ScaleTransform ExpTeamStoryReveal => (ScaleTransform)ProductionNames.RegistryFor(this)["ExpTeamStoryReveal"];
    private LinearGradientBrush ExpTeamStoryGradient => (LinearGradientBrush)ProductionNames.RegistryFor(this)["ExpTeamStoryGradient"];
    private GradientStop ExpTeamStoryGradientStrong => (GradientStop)ProductionNames.RegistryFor(this)["ExpTeamStoryGradientStrong"];
    private GradientStop ExpTeamStoryGradientMid => (GradientStop)ProductionNames.RegistryFor(this)["ExpTeamStoryGradientMid"];
    private GradientStop ExpTeamStoryGradientDark => (GradientStop)ProductionNames.RegistryFor(this)["ExpTeamStoryGradientDark"];
    private ScaleTransform ExpTeamStoryDirectionFlip => (ScaleTransform)ProductionNames.RegistryFor(this)["ExpTeamStoryDirectionFlip"];
    private TranslateTransform ExpTeamStoryContentTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["ExpTeamStoryContentTranslate"];
    private ColumnDefinition ExpTeamStoryMetricColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpTeamStoryMetricColumn"];
    private ColumnDefinition ExpTeamStoryDescriptorColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpTeamStoryDescriptorColumn"];
    private ColumnDefinition ExpRecentScoresSpacerColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ExpRecentScoresSpacerColumn"];
}
}

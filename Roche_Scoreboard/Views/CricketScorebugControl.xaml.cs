using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using UserControl = System.Windows.Controls.UserControl;

namespace Roche_Scoreboard.Views
{
    public partial class CricketScorebugControl : UserControl
    {
        private DispatcherTimer? _marqueeTimer;
        private int _marqueeIndex;
        private List<string> _messages = new();
        private double _marqueeWidth;
        private bool _marqueeAnimating;

        // Info tile rotation (partnership / run rate)
        private DispatcherTimer? _infoRotationTimer;
        private int _infoRotationIndex;
        private CricketMatchManager? _lastMatch;

        // Stats bar periodic popup
        private DispatcherTimer? _statsBarTimer;
        private DispatcherTimer? _statsBarHideTimer;
        private bool _statsBarVisible;

        // Over tracker overlay
        private DispatcherTimer? _overTrackerHideTimer;
        private bool _overTrackerVisible;

        // Overlay queue system
        private readonly Queue<Action> _overlayQueue = new();
        private bool _overlayBusy;
        private DateTime _lastOverlayEnd = DateTime.MinValue;
        private const double OverlayCooldownSec = 3.0;
        private const double OverlayDisplaySec = 5.0;

        /// <summary>Fires when queue count changes so control panel can show indicator.</summary>
        public event Action<int>? OverlayQueueChanged;

        // Batter display rows: track which player index occupies top vs bottom
        private int _topBatterIndex = -1;
        private int _bottomBatterIndex = -1;
        private int _lastInningsNumber = -1;
        private int _lastDisplayedScore = -1;
        private int _lastDisplayedWickets = -1;
        private int _lastTopRuns = -1;
        private int _lastBottomRuns = -1;
        private int _lastTopBalls = -1;
        private int _lastBottomBalls = -1;
        private string _lastBowlerFigures = "";
        private string _lastBowlerOvers = "";
        private string _lastOversDisplay = "";

        // Name/abbreviation rotation
        private DispatcherTimer? _nameRotationTimer;
        private bool _showingFullName;
        private bool _isDisposed;

        // Height-animated overlay bars (AFL-style)
        private DispatcherTimer? _overlayBarHideTimer;
        private string? _activeOverlayBar;
        private const double OverlayBarHeight = 50.0;
        private const double OverlayBarDisplaySec = 6.0;

        // Overs remaining warning
        private DispatcherTimer? _oversWarningStripeTimer;
        private int _lastOversWarningThreshold;

        // Powerplay
        private bool _powerplayBarShownThisInnings;
        private int _lastPowerplayOver = -1;

        // Cached brushes to avoid repeated allocations
        private static readonly SolidColorBrush WhiteBrush = new(Colors.White);
        private static readonly SolidColorBrush GreyOutBrush = new(Color.FromRgb(0x47, 0x55, 0x69));

        static CricketScorebugControl()
        {
            WhiteBrush.Freeze();
            GreyOutBrush.Freeze();
        }

        public CricketScorebugControl()
        {
            InitializeComponent();
            StartTimeUpdater();
            StartInfoRotation();
            StartStatsBarTimer();
            StartNameRotation();

            _overTrackerHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _overTrackerHideTimer.Tick += OnOverTrackerHideTimer;

            _overlayBarHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(OverlayBarDisplaySec) };
            _overlayBarHideTimer.Tick += OnOverlayBarHideTimer;

            _oversWarningStripeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            _oversWarningStripeTimer.Tick += OnOversWarningStripeTick;

            Unloaded += (_, _) => Cleanup();

            // Broadcast performance mode: pause cricket marquee/info rotation
            // and remove expensive drop-shadow effects while any MP4 video
            // surface is playing so the renderer can focus on the video.
            PlaybackPerformanceMode.StateChanged += OnPlaybackPerformanceStateChanged;
            Unloaded += (_, _) => PlaybackPerformanceMode.StateChanged -= OnPlaybackPerformanceStateChanged;
        }

        private bool _perfModeApplied;
        private readonly Dictionary<UIElement, System.Windows.Media.Effects.Effect?> _suspendedEffects = new();

        private void OnPlaybackPerformanceStateChanged(object? sender, bool active)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnPlaybackPerformanceStateChanged(sender, active)));
                return;
            }

            if (active)
            {
                if (_perfModeApplied) return;
                _perfModeApplied = true;
                _marqueeTimer?.Stop();
                _infoRotationTimer?.Stop();
                SuspendExpensiveEffects(this);
            }
            else
            {
                if (!_perfModeApplied) return;
                _perfModeApplied = false;
                RestoreExpensiveEffects();
                if (_messages.Count > 0 && _marqueeTimer != null) _marqueeTimer.Start();
                _infoRotationTimer?.Start();
            }
        }

        private void SuspendExpensiveEffects(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is UIElement element &&
                    element.Effect is System.Windows.Media.Effects.DropShadowEffect or System.Windows.Media.Effects.BlurEffect)
                {
                    _suspendedEffects[element] = element.Effect;
                    element.Effect = null;
                }
                SuspendExpensiveEffects(child);
            }
        }

        private void RestoreExpensiveEffects()
        {
            foreach (var kvp in _suspendedEffects)
            {
                try { kvp.Key.Effect = kvp.Value; } catch { }
            }
            _suspendedEffects.Clear();
        }

        private void OnOverTrackerHideTimer(object? sender, EventArgs e)
        {
            _overTrackerHideTimer?.Stop();
            HideOverTracker();
        }

        private void Cleanup()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            _marqueeTimer?.Stop();
            _infoRotationTimer?.Stop();
            _statsBarTimer?.Stop();
            _statsBarHideTimer?.Stop();
            _overTrackerHideTimer?.Stop();
            _nameRotationTimer?.Stop();
            _overlayBarHideTimer?.Stop();
            _oversWarningStripeTimer?.Stop();
        }

        // ---- Time ----

        private void StartTimeUpdater()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            timer.Tick += (_, __) => TopTimeText.Text = DateTime.Now.ToString("h:mm tt");
            timer.Start();
            TopTimeText.Text = DateTime.Now.ToString("h:mm tt");
        }

        // ---- Update from match state ----

        public void UpdateFromMatch(CricketMatchManager match)
        {
            _lastMatch = match;
            var inn = match.CurrentInnings;
            if (inn == null) return;

            // Reset batter row tracking on new innings
            if (match.CurrentInningsNumber != _lastInningsNumber)
            {
                _lastInningsNumber = match.CurrentInningsNumber;
                _topBatterIndex = -1;
                _bottomBatterIndex = -1;
                _lastDisplayedScore = -1;
                _lastDisplayedWickets = -1;
                _lastTopRuns = -1;
                _lastBottomRuns = -1;
                _lastTopBalls = -1;
                _lastBottomBalls = -1;
                _lastBowlerFigures = "";
                _lastBowlerOvers = "";
                _lastOversDisplay = "";
                _showingFullName = false;
                _powerplayBarShownThisInnings = false;
                _lastPowerplayOver = -1;
                _lastOversWarningThreshold = 0;
            }

            // Score — independent scroll for wickets and runs
            string wktsStr = inn.TotalWickets.ToString();
            string runsStr = inn.TotalRuns.ToString();
            string oversStr = inn.OversDisplay;

            if (inn.TotalRuns != _lastDisplayedScore && _lastDisplayedScore >= 0)
            {
                // Glow pulse on score change
                var glowUp = new DoubleAnimation(0.3, 0.9, TimeSpan.FromSeconds(0.15));
                var glowDown = new DoubleAnimation(0.9, 0.3, TimeSpan.FromSeconds(0.6))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                glowUp.Completed += (_, __) => ScoreGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, glowDown);
                ScoreGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, glowUp);

                AnimateScoreRoll(ScoreRuns, ScoreRunsTranslate, _lastDisplayedScore, inn.TotalRuns, 60);
            }
            else
            {
                ScoreRuns.Text = runsStr;
            }

            if (inn.TotalWickets != _lastDisplayedWickets && _lastDisplayedWickets >= 0)
                ScrollValue(ScoreWickets, ScoreWicketsTranslate, wktsStr, 60);
            else
                ScoreWickets.Text = wktsStr;

            if (oversStr != _lastOversDisplay && !string.IsNullOrEmpty(_lastOversDisplay))
                ScrollValue(OversText, OversTranslate, oversStr, 38);
            else
                OversText.Text = oversStr;

            _lastDisplayedScore = inn.TotalRuns;
            _lastDisplayedWickets = inn.TotalWickets;
            _lastOversDisplay = oversStr;

            // Team name/abbreviation — measure and apply uniform scale
            MeasureTeamNameScale(match);
            BattingAbbrText.Text = _showingFullName
                ? match.BattingTeamName.ToUpper()
                : match.BattingTeamAbbr;

            // Top bar innings info
            string ordinal = match.CurrentInningsNumber switch { 1 => "1ST", 2 => "2ND", 3 => "3RD", _ => $"{match.CurrentInningsNumber}TH" };
            TopInningsText.Text = match.Format == CricketFormat.LimitedOvers
                ? $"{ordinal} INNINGS  •  {match.TotalOvers} OVERS"
                : $"{ordinal} INNINGS";

            // Colours
            TrySetColor(BattingTeamBg, match.BattingTeamPrimaryColor);
            TrySetColor(BattingHeaderBg, match.BattingTeamPrimaryColor);
            TrySetColor(StrikerBorder, match.BattingTeamPrimaryColor);
            TrySetColor(NonStrikerBorder, match.BattingTeamPrimaryColor);
            TrySetColor(BattingIndicatorBrush, match.BattingTeamPrimaryColor);
            TrySetColor(NonStrikerIndicatorBrush, match.BattingTeamPrimaryColor);
            TrySetColor(BowlingHeaderBg, match.BowlingTeamPrimaryColor);
            TrySetColor(BowlerBorder, match.BowlingTeamPrimaryColor);
            TrySetColor(BowlingIndicatorBrush, match.BowlingTeamPrimaryColor);
            TrySetColor(PrevBowlerBorderBrush, match.BowlingTeamPrimaryColor);

            // Team text colours — auto-detect from primary background luminance
            var battingColor = SafeColor(match.BattingTeamPrimaryColor, "#FFFFFF");
            var bowlingColor = SafeColor(match.BowlingTeamPrimaryColor, "#FFFFFF");
            var battingTextBrush = ContrastHelper.GetContrastBrush(battingColor);
            var bowlingTextBrush = ContrastHelper.GetContrastBrush(bowlingColor);
            BattingAbbrText.Foreground = battingTextBrush;
            ScoreWickets.Foreground = battingTextBrush;
            ScoreRuns.Foreground = battingTextBrush;
            BattingHeaderText.Foreground = battingTextBrush;
            BowlingHeaderText.Foreground = bowlingTextBrush;

            // Headers
            BattingHeaderText.Text = $"{match.BattingTeamName.ToUpper()} BATTING";
            BowlingHeaderText.Text = $"{match.BowlingTeamName.ToUpper()} BOWLING";

            // Batter display — keep rows stable, only move the indicator
            int si = inn.StrikerIndex;
            int nsi = inn.NonStrikerIndex;

            // Assign rows on first display or when a new batter replaces a dismissed one
            if (_topBatterIndex < 0 || _bottomBatterIndex < 0)
            {
                _topBatterIndex = si;
                _bottomBatterIndex = nsi;
            }
            else
            {
                // If a new batter has replaced one of the tracked positions, update that slot
                bool topStillActive = _topBatterIndex == si || _topBatterIndex == nsi;
                bool bottomStillActive = _bottomBatterIndex == si || _bottomBatterIndex == nsi;

                if (!topStillActive && bottomStillActive)
                    _topBatterIndex = (_bottomBatterIndex == si) ? nsi : si;
                else if (topStillActive && !bottomStillActive)
                    _bottomBatterIndex = (_topBatterIndex == si) ? nsi : si;
                else if (!topStillActive && !bottomStillActive)
                {
                    _topBatterIndex = si;
                    _bottomBatterIndex = nsi;
                }
            }

            var topPlayer = (_topBatterIndex >= 0 && _topBatterIndex < inn.BattingOrder.Count)
                ? inn.BattingOrder[_topBatterIndex] : null;
            var bottomPlayer = (_bottomBatterIndex >= 0 && _bottomBatterIndex < inn.BattingOrder.Count)
                ? inn.BattingOrder[_bottomBatterIndex] : null;

            bool topIsStriker = _topBatterIndex == si;
            bool topGreyOut = topPlayer != null && topPlayer.IsOut && inn.NeedsBatterSelection;
            bool bottomGreyOut = bottomPlayer != null && bottomPlayer.IsOut && inn.NeedsBatterSelection;

            // Top row
            if (topPlayer != null)
            {
                StrikerName.Text = topPlayer.DisplayName.ToUpper();

                string topRunsStr = topPlayer.Runs.ToString();
                string topBallsStr = topPlayer.BallsFaced.ToString();

                if (topPlayer.Runs != _lastTopRuns && _lastTopRuns >= 0)
                {
                    ScrollValue(StrikerRuns, StrikerRunsTranslate, topRunsStr, 28);
                    FlashElement(StrikerRuns);
                }
                else
                    StrikerRuns.Text = topRunsStr;

                if (topPlayer.BallsFaced != _lastTopBalls && _lastTopBalls >= 0)
                    ScrollValue(StrikerBalls, StrikerBallsTranslate, topBallsStr, 18);
                else
                    StrikerBalls.Text = topBallsStr;

                StrikerName.Foreground = topGreyOut ? GreyOutBrush : WhiteBrush;
                StrikerRuns.Foreground = StrikerName.Foreground;
                StrikerIndicator.Text = topGreyOut ? "  " : (topIsStriker ? "▶" : "   ");

                _lastTopRuns = topPlayer.Runs;
                _lastTopBalls = topPlayer.BallsFaced;
            }
            else
            {
                StrikerName.Text = "";
                StrikerRuns.Text = "";
                StrikerBalls.Text = "";
                StrikerIndicator.Text = "  ";
            }

            // Bottom row
            if (bottomPlayer != null)
            {
                NonStrikerName.Text = bottomPlayer.DisplayName.ToUpper();

                string botRunsStr = bottomPlayer.Runs.ToString();
                string botBallsStr = bottomPlayer.BallsFaced.ToString();

                if (bottomPlayer.Runs != _lastBottomRuns && _lastBottomRuns >= 0)
                {
                    ScrollValue(NonStrikerRuns, NonStrikerRunsTranslate, botRunsStr, 28);
                    FlashElement(NonStrikerRuns);
                }
                else
                    NonStrikerRuns.Text = botRunsStr;

                if (bottomPlayer.BallsFaced != _lastBottomBalls && _lastBottomBalls >= 0)
                    ScrollValue(NonStrikerBalls, NonStrikerBallsTranslate, botBallsStr, 18);
                else
                    NonStrikerBalls.Text = botBallsStr;

                NonStrikerName.Foreground = bottomGreyOut ? GreyOutBrush : WhiteBrush;
                NonStrikerRuns.Foreground = NonStrikerName.Foreground;
                NonStrikerIndicator.Text = bottomGreyOut ? "  " : (!topIsStriker ? "▶" : "   ");

                _lastBottomRuns = bottomPlayer.Runs;
                _lastBottomBalls = bottomPlayer.BallsFaced;
            }
            else
            {
                NonStrikerName.Text = "";
                NonStrikerRuns.Text = "";
                NonStrikerBalls.Text = "";
                NonStrikerIndicator.Text = "  ";
            }

            // Current bowler
            var bowler = inn.CurrentBowler;
            if (bowler != null)
            {
                BowlerName.Text = bowler.DisplayName.ToUpper();

                string figStr = bowler.BowlingFigures;
                string bOvStr = bowler.OversDisplay;

                if (figStr != _lastBowlerFigures && !string.IsNullOrEmpty(_lastBowlerFigures))
                    ScrollValue(BowlerFigures, BowlerFiguresTranslate, figStr, 26);
                else
                    BowlerFigures.Text = figStr;

                if (bOvStr != _lastBowlerOvers && !string.IsNullOrEmpty(_lastBowlerOvers))
                    ScrollValue(BowlerOvers, BowlerOversTranslate, bOvStr, 18);
                else
                    BowlerOvers.Text = bOvStr;

                _lastBowlerFigures = figStr;
                _lastBowlerOvers = bOvStr;
            }
            else
            {
                BowlerName.Text = "";
                BowlerFigures.Text = "";
                BowlerOvers.Text = "";
            }

            // Previous/interchanging bowler
            if (inn.PreviousBowlerIndex >= 0
                && inn.PreviousBowlerIndex < inn.BowlingAttack.Count
                && inn.PreviousBowlerIndex != inn.CurrentBowlerIndex)
            {
                var prev = inn.BowlingAttack[inn.PreviousBowlerIndex];
                PrevBowlerName.Text = prev.DisplayName.ToUpper();
                PrevBowlerFigures.Text = prev.BowlingFigures;
                PrevBowlerOvers.Text = prev.OversDisplay;
            }
            else
            {
                PrevBowlerName.Text = "";
                PrevBowlerFigures.Text = "";
                PrevBowlerOvers.Text = "";
            }

            // Update stats bar content
            UpdateStatsBarContent(match);

            // This over ball tracker
            UpdateOverBallsDisplay(inn);

            // Logo
            SetLogo(match);

            // Info tile
            UpdateInfoTile(match);

            // Auto-trigger overlay bars (powerplay, drought, overs warning)
            CheckAutoOverlays(match);
        }

        // ---- Info tile (left panel, rotates every ~8s with crossfade) ----

        private void UpdateInfoTile(CricketMatchManager match)
        {
            var inn = match.CurrentInnings;
            if (inn == null) return;

            string newLabel, newValue;

            // After 1st innings: focus on chase/lead stats
            if (match.CurrentInningsNumber > 1)
            {
                if (match.Format == CricketFormat.LimitedOvers)
                {
                    // Limited overs 2nd innings: alternate target and runs needed
                    if (_infoRotationIndex % 2 == 0)
                    {
                        newLabel = "TARGET";
                        newValue = match.Target?.ToString() ?? "—";
                    }
                    else
                    {
                        newLabel = "RUNS NEEDED";
                        newValue = match.RunsRequired?.ToString() ?? "—";
                    }
                }
                else
                {
                    // Multi-day: alternate lead/trail and target (if final innings)
                    if (match.Target != null)
                    {
                        switch (_infoRotationIndex % 3)
                        {
                            case 0:
                                newLabel = match.LeadTrailRuns >= 0 ? "LEAD BY" : "TRAIL BY";
                                newValue = Math.Abs(match.LeadTrailRuns ?? 0).ToString();
                                break;
                            case 1:
                                newLabel = "TARGET";
                                newValue = match.Target.Value.ToString();
                                break;
                            default:
                                newLabel = "RUNS NEEDED";
                                newValue = match.RunsRequired?.ToString() ?? "—";
                                break;
                        }
                    }
                    else
                    {
                        // Non-final innings: alternate lead/trail and opponent previous innings score
                        var oppScore = match.OpponentPreviousInningsScore;
                        int states = oppScore != null ? 3 : 1;
                        switch (_infoRotationIndex % states)
                        {
                            case 0:
                                if (match.LeadTrailRuns == 0) { newLabel = "SCORES"; newValue = "LEVEL"; }
                                else
                                {
                                    newLabel = match.LeadTrailRuns >= 0 ? "LEAD BY" : "TRAIL BY";
                                    newValue = Math.Abs(match.LeadTrailRuns ?? 0).ToString();
                                }
                                break;
                            case 1:
                                newLabel = oppScore!.Value.Label;
                                newValue = oppScore!.Value.Score;
                                break;
                            default:
                                newLabel = "RUN RATE";
                                newValue = inn.RunRate.ToString("F2");
                                break;
                        }
                    }
                }
            }
            else
            {
                // First innings: rotate between run rate, partnership, extras
                switch (_infoRotationIndex % 3)
                {
                    case 0:
                        newLabel = "RUN RATE";
                        newValue = inn.RunRate.ToString("F2");
                        break;
                    case 1:
                        newLabel = "PARTNERSHIP";
                        newValue = $"{inn.PartnershipRuns}({inn.PartnershipBalls})";
                        break;
                    default:
                        newLabel = "EXTRAS";
                        newValue = inn.TotalExtras.ToString();
                        break;
                }
            }

            // Crossfade animation if text changed
            if (InfoLabel.Text != newLabel || InfoValue.Text != newValue)
            {
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.2));
                fadeOut.Completed += (_, __) =>
                {
                    InfoLabel.Text = newLabel;
                    InfoValue.Text = newValue;
                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    InfoLabel.BeginAnimation(OpacityProperty, fadeIn);
                    InfoValue.BeginAnimation(OpacityProperty, fadeIn);
                };
                InfoLabel.BeginAnimation(OpacityProperty, fadeOut);
                InfoValue.BeginAnimation(OpacityProperty, fadeOut);
            }
        }

        private void StartInfoRotation()
        {
            _infoRotationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _infoRotationTimer.Tick += (_, __) =>
            {
                _infoRotationIndex++;
                if (_lastMatch != null) UpdateInfoTile(_lastMatch);
            };
            _infoRotationTimer.Start();
        }

        // ---- Team name / abbreviation rotation ----

        private void StartNameRotation()
        {
            _nameRotationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            _nameRotationTimer.Tick += (_, __) =>
            {
                if (_lastMatch != null) RotateNameDisplay();
            };
            _nameRotationTimer.Start();
        }

        /// <summary>
        /// Measures the full team name using FormattedText and applies a uniform
        /// LayoutTransform scale so both the full name and abbreviation render at
        /// the same visual size, fitting the available left-panel width.
        /// Mirrors the AFL SyncClassicNameScales pattern.
        /// </summary>
        private void MeasureTeamNameScale(CricketMatchManager match)
        {
            string fullName = match.BattingTeamName.ToUpper();
            if (string.IsNullOrEmpty(fullName)) return;

            double pixelsPerDip = 1.0;
            try
            {
                var source = PresentationSource.FromVisual(BattingAbbrText);
                if (source?.CompositionTarget != null)
                    pixelsPerDip = source.CompositionTarget.TransformToDevice.M11;
            }
            catch { }

            var typeface = new Typeface(
                BattingAbbrText.FontFamily, BattingAbbrText.FontStyle,
                BattingAbbrText.FontWeight, BattingAbbrText.FontStretch);
            double fontSize = BattingAbbrText.FontSize;

            var formatted = new FormattedText(
                fullName, System.Globalization.CultureInfo.CurrentCulture,
                (System.Windows.FlowDirection)0, typeface, fontSize,
                System.Windows.Media.Brushes.White, pixelsPerDip);

            // Left panel is 165px; account for padding/margins
            const double availableWidth = 150.0;
            double scale = Math.Min(1.0, availableWidth / formatted.Width);

            if (BattingAbbrText.LayoutTransform is not ScaleTransform st)
            {
                BattingAbbrText.LayoutTransform = new ScaleTransform(scale, scale);
            }
            else if (Math.Abs(st.ScaleX - scale) > 0.001)
            {
                AnimateTeamNameScale(scale);
            }
        }

        /// <summary>
        /// Smoothly animates the BattingAbbrText LayoutTransform to a new uniform scale.
        /// Mirrors the AFL AnimateNameScale pattern.
        /// </summary>
        private void AnimateTeamNameScale(double targetScale)
        {
            if (BattingAbbrText.LayoutTransform is not ScaleTransform st)
            {
                st = new ScaleTransform(targetScale, targetScale);
                BattingAbbrText.LayoutTransform = st;
                return;
            }

            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var dur = TimeSpan.FromSeconds(0.5);

            st.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(targetScale, dur) { EasingFunction = ease });
            st.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(targetScale, dur) { EasingFunction = ease });
        }

        private void RotateNameDisplay()
        {
            if (_lastMatch == null) return;
            _showingFullName = !_showingFullName;

            string newText = _showingFullName
                ? _lastMatch.BattingTeamName.ToUpper()
                : _lastMatch.BattingTeamAbbr;

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.5))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (_, __) =>
            {
                BattingAbbrText.Text = newText;
                if (_lastMatch != null) MeasureTeamNameScale(_lastMatch);
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.55))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                BattingAbbrText.BeginAnimation(OpacityProperty, fadeIn);
            };
            BattingAbbrText.BeginAnimation(OpacityProperty, fadeOut);
        }

        // ---- Stats bar (periodic popup in bottom bar) ----

        private void StartStatsBarTimer()
        {
            _statsBarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
            _statsBarTimer.Tick += (_, __) =>
            {
                if (_lastMatch != null) EnqueueOverlay(() => ShowStatsBarInternal());
            };
            _statsBarTimer.Start();

            _statsBarHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(OverlayDisplaySec) };
            _statsBarHideTimer.Tick += (_, __) =>
            {
                _statsBarHideTimer!.Stop();
                HideStatsBar();
            };
        }

        public void TriggerStatsBar()
        {
            if (_lastMatch != null) EnqueueOverlay(() => ShowStatsBarInternal());
        }

        // ---- Overlay queue ----

        private void EnqueueOverlay(Action showAction)
        {
            _overlayQueue.Enqueue(showAction);
            OverlayQueueChanged?.Invoke(_overlayQueue.Count);
            TryProcessQueue();
        }

        private void TryProcessQueue()
        {
            if (_overlayBusy || _overlayQueue.Count == 0) return;

            double sinceLast = (DateTime.Now - _lastOverlayEnd).TotalSeconds;
            if (sinceLast < OverlayCooldownSec)
            {
                var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(OverlayCooldownSec - sinceLast) };
                delay.Tick += (_, __) => { delay.Stop(); TryProcessQueue(); };
                delay.Start();
                return;
            }

            _overlayBusy = true;
            var action = _overlayQueue.Dequeue();
            OverlayQueueChanged?.Invoke(_overlayQueue.Count);
            action();
        }

        private void OnOverlayFinished()
        {
            _overlayBusy = false;
            _lastOverlayEnd = DateTime.Now;
            TryProcessQueue();
        }

        private void ShowStatsBarInternal()
        {
            _statsBarVisible = true;
            if (_overTrackerVisible) HideOverTrackerImmediate();
            if (_lastMatch != null) UpdateStatsBarContent(_lastMatch);

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.35)) { EasingFunction = ease };
            var slideIn = new DoubleAnimation(30, 0, TimeSpan.FromSeconds(0.35)) { EasingFunction = ease };
            StatsBarBg.BeginAnimation(OpacityProperty, fadeIn);
            StatsBarTranslate.BeginAnimation(TranslateTransform.YProperty, slideIn);

            _statsBarHideTimer?.Stop();
            _statsBarHideTimer?.Start();
        }

        private void HideStatsBar()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.4)) { EasingFunction = ease };
            var slideOut = new DoubleAnimation(0, -20, TimeSpan.FromSeconds(0.4)) { EasingFunction = ease };
            fadeOut.Completed += (_, __) =>
            {
                _statsBarVisible = false;
                StatsBarTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                StatsBarTranslate.Y = 30;
                OnOverlayFinished();
            };
            StatsBarBg.BeginAnimation(OpacityProperty, fadeOut);
            StatsBarTranslate.BeginAnimation(TranslateTransform.YProperty, slideOut);
        }

        private void UpdateStatsBarContent(CricketMatchManager match)
        {
            var inn = match.CurrentInnings;
            if (inn == null) return;

            // After 1st innings, stats bar carries partnership & run rate
            // since the info tile focuses on target/chase
            if (match.CurrentInningsNumber > 1)
            {
                Stat1Label.Text = "P'SHIP";
                Stat1Value.Text = $"{inn.PartnershipRuns}({inn.PartnershipBalls})";

                Stat2Label.Text = "RUN RATE";
                Stat2Value.Text = inn.RunRate.ToString("F2");

                Stat3Label.Text = "EXTRAS";
                Stat3Value.Text = inn.TotalExtras.ToString();

                if (match.RequiredRunRate != null)
                {
                    Stat4Label.Text = "REQ. RATE";
                    Stat4Value.Text = match.RequiredRunRate.Value.ToString("F2");
                }
                else if (match.LeadTrailRuns != null)
                {
                    Stat4Label.Text = match.LeadTrailRuns >= 0 ? "LEAD" : "TRAIL";
                    Stat4Value.Text = Math.Abs(match.LeadTrailRuns.Value).ToString();
                }
                else
                {
                    Stat4Label.Text = "";
                    Stat4Value.Text = "";
                }
            }
            else
            {
                Stat1Label.Text = "EXTRAS";
                Stat1Value.Text = inn.TotalExtras.ToString();

                Stat2Label.Text = "P'SHIP";
                Stat2Value.Text = $"{inn.PartnershipRuns}({inn.PartnershipBalls})";

                Stat3Label.Text = "RUN RATE";
                Stat3Value.Text = inn.RunRate.ToString("F2");

                Stat4Label.Text = "";
                Stat4Value.Text = "";
            }

            // Stagger each stat pair visibility
            AnimateStatPair(Stat1Label, Stat1Value, 0);
            AnimateStatPair(Stat2Label, Stat2Value, 0.08);
            AnimateStatPair(Stat3Label, Stat3Value, 0.16);
            if (!string.IsNullOrEmpty(Stat4Label.Text))
                AnimateStatPair(Stat4Label, Stat4Value, 0.24);
        }

        private static void AnimateStatPair(TextBlock label, TextBlock value, double delaySec)
        {
            label.Opacity = 0;
            value.Opacity = 0;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25))
            {
                BeginTime = TimeSpan.FromSeconds(delaySec),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            label.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            value.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        // ---- Overlay triggers (AFL-style) ----

        public void ShowPartnershipOverlay(int runs, int balls)
        {
            EnqueueOverlay(() =>
            {
                Stat1Label.Text = "PARTNERSHIP";
                Stat1Value.Text = runs.ToString();

                Stat2Label.Text = "BALLS";
                Stat2Value.Text = balls.ToString();

                double sr = balls > 0 ? Math.Round(100.0 * runs / balls, 1) : 0;
                Stat3Label.Text = "STRIKE RATE";
                Stat3Value.Text = sr.ToString("F1");

                Stat4Label.Text = "";
                Stat4Value.Text = "";

                ShowStatsBarInternal();
            });
        }

        public void ShowExtrasOverlay(int wides, int noBalls, int byes, int legByes, int total)
        {
            EnqueueOverlay(() =>
            {
                Stat1Label.Text = "WIDES";
                Stat1Value.Text = wides.ToString();

                Stat2Label.Text = "NO BALLS";
                Stat2Value.Text = noBalls.ToString();

                Stat3Label.Text = "BYES";
                Stat3Value.Text = $"{byes}+{legByes}lb";

                Stat4Label.Text = "TOTAL";
                Stat4Value.Text = total.ToString();

                ShowStatsBarInternal();
            });
        }

        // ---- Over ball tracker overlay ----

        private int _lastBallCount = -1;

        private void UpdateOverBallsDisplay(CricketInnings inn)
        {
            // Only update when delivery count changes
            int currentBallCount = inn.Deliveries.Count;
            if (currentBallCount == _lastBallCount) return;

            bool isNewBall = currentBallCount > _lastBallCount && _lastBallCount >= 0;
            _lastBallCount = currentBallCount;

            OverBallsPanel.Children.Clear();
            OverTrackerLabel.Text = $"OVER {inn.CompletedOvers + 1}";

            // Calculate runs scored this over for the total display
            int overRuns = 0;
            int ballIndex = 0;
            int totalBalls = inn.CurrentOverBalls.Count;
            foreach (var ball in inn.CurrentOverBalls)
            {
                Color bgColor;
                if (ball == "W") bgColor = Color.FromRgb(0xDC, 0x26, 0x26);
                else if (ball == "4") bgColor = Color.FromRgb(0x15, 0x80, 0x3D);
                else if (ball == "6") bgColor = Color.FromRgb(0x7C, 0x3A, 0xED);
                else if (ball == "•") bgColor = Color.FromRgb(0x33, 0x41, 0x55);
                else if (ball.Contains("wd") || ball.Contains("nb")) bgColor = Color.FromRgb(0xD9, 0x77, 0x06);
                else bgColor = Color.FromRgb(0x1E, 0x40, 0xAF);

                // Tally runs for the over total
                if (ball != "•" && ball != "W")
                {
                    if (int.TryParse(ball, out int runs)) overRuns += runs;
                    else if (ball.Contains("wd") || ball.Contains("nb")) overRuns += 1;
                }

                bool isLastBall = isNewBall && ballIndex == totalBalls - 1;

                var pip = new Border
                {
                    Background = new SolidColorBrush(bgColor),
                    CornerRadius = new CornerRadius(6),
                    MinWidth = 36,
                    Height = 36,
                    Margin = new Thickness(2, 0, 2, 0),
                    Padding = new Thickness(4, 0, 4, 0),
                    RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                    RenderTransform = new ScaleTransform(isLastBall ? 0 : 1, isLastBall ? 0 : 1),
                    Child = new TextBlock
                    {
                        Text = ball,
                        Foreground = System.Windows.Media.Brushes.White,
                        FontSize = 16,
                        FontWeight = FontWeights.Black,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    }
                };
                OverBallsPanel.Children.Add(pip);

                // Bounce-in animation for the newest pip
                if (isLastBall)
                {
                    AnimatePipEntrance(pip, bgColor, ball);
                }

                ballIndex++;
            }

            // Update over runs total display
            if (totalBalls > 0)
            {
                OverRunsLabel.Text = "RUNS";
                OverRunsValue.Text = overRuns.ToString();
            }
            else
            {
                OverRunsLabel.Text = "";
                OverRunsValue.Text = "";
            }

            // Show overlay only every 3rd legal ball, on wickets, boundaries, or at end of over
            int ballsThisOver = inn.CurrentOverBalls.Count;
            if (ballsThisOver > 0)
            {
                string lastBall = inn.CurrentOverBalls[^1];
                bool endOfOver = inn.BallsInCurrentOver == 0 && inn.LegalBallsBowled > 0;
                bool isSignificant = lastBall == "W" || lastBall == "4" || lastBall == "6";
                bool isThirdBall = ballsThisOver % 3 == 0;

                if (endOfOver || isSignificant || isThirdBall)
                    ShowOverTracker();
            }
        }

        /// <summary>Scale-in bounce with a brief colour flash for the newest ball pip.</summary>
        private static void AnimatePipEntrance(Border pip, Color bgColor, string ball)
        {
            var st = (ScaleTransform)pip.RenderTransform;
            var backEase = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromSeconds(0.3);

            // Scale from 0 → 1 with overshoot bounce
            var scaleX = new DoubleAnimation(0, 1, dur) { EasingFunction = backEase };
            var scaleY = new DoubleAnimation(0, 1, dur) { EasingFunction = backEase };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);

            // Brief flash: lighten the background then settle back
            Color flashColor = Color.FromArgb(0xFF,
                (byte)Math.Min(bgColor.R + 80, 255),
                (byte)Math.Min(bgColor.G + 80, 255),
                (byte)Math.Min(bgColor.B + 80, 255));

            var brush = (SolidColorBrush)pip.Background;
            var flash = new ColorAnimation(flashColor, bgColor, TimeSpan.FromSeconds(0.5))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            brush.BeginAnimation(SolidColorBrush.ColorProperty, flash);

            // For boundaries and wickets, add a second pop-scale pulse
            if (ball == "4" || ball == "6" || ball == "W")
            {
                var pulse = new DoubleAnimation(1, 1.15, TimeSpan.FromSeconds(0.15))
                {
                    BeginTime = TimeSpan.FromSeconds(0.3),
                    AutoReverse = true,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
            }
        }

        public void ShowOverTracker()
        {
            EnqueueOverlay(() => ShowOverTrackerInternal());
        }

        private void ShowOverTrackerInternal()
        {
            if (_statsBarVisible) HideStatsBar();
            _overTrackerHideTimer?.Stop();
            _overTrackerVisible = true;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)) { EasingFunction = ease };
            var slideIn = new DoubleAnimation(30, 0, TimeSpan.FromSeconds(0.3)) { EasingFunction = ease };
            OverTrackerBg.BeginAnimation(OpacityProperty, fadeIn);
            OverTrackerTranslate.BeginAnimation(TranslateTransform.YProperty, slideIn);

            _overTrackerHideTimer?.Start();
        }

        private void HideOverTracker()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.35)) { EasingFunction = ease };
            var slideOut = new DoubleAnimation(0, -15, TimeSpan.FromSeconds(0.35)) { EasingFunction = ease };
            fadeOut.Completed += (_, __) =>
            {
                _overTrackerVisible = false;
                OverTrackerTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                OverTrackerTranslate.Y = 30;
                OnOverlayFinished();
            };
            OverTrackerBg.BeginAnimation(OpacityProperty, fadeOut);
            OverTrackerTranslate.BeginAnimation(TranslateTransform.YProperty, slideOut);
        }

        private void HideOverTrackerImmediate()
        {
            _overTrackerHideTimer?.Stop();
            _overTrackerVisible = false;
            OverTrackerBg.BeginAnimation(OpacityProperty, null);
            OverTrackerBg.Opacity = 0;
            OverTrackerTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            OverTrackerTranslate.Y = 30;
        }

        // ---- Logo ----

        private void SetLogo(CricketMatchManager match)
        {
            string? logoPath = match.BattingTeamName == match.TeamAName
                ? match.TeamALogoPath : match.TeamBLogoPath;

            if (!string.IsNullOrWhiteSpace(logoPath) && System.IO.File.Exists(logoPath))
            {
                try
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(logoPath, UriKind.Absolute);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.DecodePixelHeight = 100;
                    bmp.EndInit();
                    bmp.Freeze();
                    BattingLogo.Source = bmp;
                }
                catch { BattingLogo.Source = null; }
            }
            else
            {
                BattingLogo.Source = null;
            }
        }

        // ---- Marquee ----

        public void SetMarqueeMessages(List<string> messages)
        {
            _messages = messages ?? new();
            _marqueeIndex = 0;
            if (_messages.Count > 0 && _marqueeTimer == null)
                StartMarquee();
        }

        private void StartMarquee()
        {
            _marqueeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.5) };
            _marqueeTimer.Tick += MarqueeTick;
            _marqueeTimer.Start();
        }

        private void MarqueeTick(object? sender, EventArgs e)
        {
            if (_messages.Count == 0 || _marqueeAnimating) return;

            _marqueeAnimating = true;
            MarqueeText.Text = _messages[_marqueeIndex % _messages.Count];
            MarqueeText.UpdateLayout();
            _marqueeWidth = MarqueeText.ActualWidth;

            double startX = ActualWidth > 0 ? ActualWidth + 50 : 800;
            double endX = -_marqueeWidth - 50;
            double distance = startX - endX;
            double speed = 100;
            var duration = TimeSpan.FromSeconds(distance / speed);

            MarqueeTranslate.X = startX;
            var anim = new DoubleAnimation(startX, endX, duration);
            anim.Completed += (_, __) =>
            {
                _marqueeAnimating = false;
                _marqueeIndex++;
            };
            MarqueeTranslate.BeginAnimation(TranslateTransform.XProperty, anim);

            _marqueeTimer!.Interval = duration + TimeSpan.FromSeconds(1);
        }

        // ---- Event overlay animations (4/6/W) — AFL Broadcast style ----

        private bool _eventBannerShowing;
        private Storyboard? _eventSb;

        public void ShowEventBanner(CricketDeliveryType type, string teamColorHex)
        {
            if (_eventBannerShowing) return;

            Color teamColor;
            try { teamColor = (Color)ColorConverter.ConvertFromString(teamColorHex); }
            catch { teamColor = Color.FromRgb(0x33, 0x41, 0x55); }

            Color secondaryColor = Color.FromArgb(0xFF,
                (byte)Math.Min(teamColor.R + 80, 255),
                (byte)Math.Min(teamColor.G + 80, 255),
                (byte)Math.Min(teamColor.B + 80, 255));

            string text;
            Color overlayColor;

            switch (type)
            {
                case CricketDeliveryType.Four:
                    text = "FOUR!";
                    overlayColor = Color.FromRgb(0x15, 0x80, 0x3D); // green
                    secondaryColor = Colors.White;
                    break;
                case CricketDeliveryType.Six:
                    text = "SIX!";
                    overlayColor = Color.FromRgb(0x7C, 0x3A, 0xED); // purple
                    secondaryColor = Colors.Gold;
                    break;
                case CricketDeliveryType.Wicket:
                    text = "WICKET!";
                    overlayColor = Color.FromRgb(0xDC, 0x26, 0x26); // red
                    secondaryColor = Colors.White;
                    break;
                default:
                    return;
            }

            _eventBannerShowing = true;

            // Cancel any running animation
            if (_eventSb != null) { _eventSb.Stop(); _eventSb = null; }

            // Reset elements
            EventOverlayBg.Color = Color.FromArgb(0xFF, overlayColor.R, overlayColor.G, overlayColor.B);
            EventOverlayText.Text = text;
            EventOverlayText.Foreground = new SolidColorBrush(secondaryColor);
            EventOverlayText.FontSize = type == CricketDeliveryType.Six ? 86 : 72;

            EventTextScale.ScaleX = 1.5;
            EventTextScale.ScaleY = 1.5;

            EventGradientScroll.X = -1400;
            EventShimmerScroll.X = -200;
            EventShimmer.Opacity = 0;
            EventGradientBar.Opacity = 0.6;
            EventEdgeGlow.Opacity = 0;

            EventOverlayGlow.Color = secondaryColor;
            EventOverlayGlow.BlurRadius = 120;
            EventOverlayGlow.Opacity = 0.9;

            // Set score glow colours
            EventScoreGlowInner.Color = Color.FromArgb(0xCC, overlayColor.R, overlayColor.G, overlayColor.B);
            EventScoreGlowOuter.Color = Color.FromArgb(0x00, overlayColor.R, overlayColor.G, overlayColor.B);

            // Tint the gradient bar: white→secondary team colour wash (Broadcast style)
            var gradBrush = (LinearGradientBrush)EventGradientBar.Fill;
            gradBrush.GradientStops[0].Color = Colors.White;
            gradBrush.GradientStops[1].Color = secondaryColor;
            gradBrush.GradientStops[2].Color = Colors.White;
            gradBrush.GradientStops[3].Color = secondaryColor;
            gradBrush.GradientStops[4].Color = Colors.White;
            gradBrush.GradientStops[5].Color = secondaryColor;
            gradBrush.GradientStops[6].Color = Colors.White;
            gradBrush.GradientStops[7].Color = secondaryColor;

            var sb = new Storyboard();
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            var backEase = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut };
            var decelEase = new CubicEase { EasingMode = EasingMode.EaseOut };

            // Score glow radiates FIRST (0–0.4s)
            sb.Children.Add(MakeDA(EventScoreGlow, UIElement.OpacityProperty, 0, 1, 0.1, 0.0));
            sb.Children.Add(MakeDA(EventScoreGlow, UIElement.OpacityProperty, 1, 0, 1.2, 1.6, ease));

            // Overlay slams in instantly
            sb.Children.Add(MakeDA(EventOverlay, UIElement.OpacityProperty, 0, 1, 0.04));

            // Team-colour gradient wash (0–1.5s)
            sb.Children.Add(MakeDA(EventGradientBar, "RenderTransform.(TranslateTransform.X)", -1400, 900, 1.5, 0.0, ease));

            // Text: starts at 1.5× and snaps DOWN to 1× (overshoot inward)
            sb.Children.Add(MakeDA(EventOverlayText, "RenderTransform.ScaleX", 1.5, 1, 0.4, 0.02, backEase));
            sb.Children.Add(MakeDA(EventOverlayText, "RenderTransform.ScaleY", 1.5, 1, 0.4, 0.02, backEase));

            // Glow burst: starts massive, contracts
            sb.Children.Add(MakeDA(EventOverlayText, "(UIElement.Effect).(DropShadowEffect.BlurRadius)", 120, 30, 0.5, 0.02, ease));

            // Edge glow flash (for SIX and WICKET)
            if (type == CricketDeliveryType.Six || type == CricketDeliveryType.Wicket)
            {
                sb.Children.Add(MakeDA(EventEdgeGlow, UIElement.OpacityProperty, 0, 1, 0.06, 0.0));
                sb.Children.Add(MakeDA(EventEdgeGlow, UIElement.OpacityProperty, 1, 0, 0.3, 0.2, ease));
                sb.Children.Add(MakeDA(EventEdgeGlow, UIElement.OpacityProperty, 0, 0.7, 0.05, 0.5));
                sb.Children.Add(MakeDA(EventEdgeGlow, UIElement.OpacityProperty, 0.7, 0, 0.2, 0.6, ease));
            }

            // Fast shimmer wipe (0.4–0.9s)
            sb.Children.Add(MakeDA(EventShimmer, UIElement.OpacityProperty, 0, 1, 0.06, 0.4));
            sb.Children.Add(MakeDA(EventShimmer, "RenderTransform.(TranslateTransform.X)", -200, 900, 0.5, 0.4, ease));
            sb.Children.Add(MakeDA(EventShimmer, UIElement.OpacityProperty, 1, 0, 0.08, 0.82));

            // Hold, then crisp fade (1.6–2.0s)
            sb.Children.Add(MakeDA(EventOverlay, UIElement.OpacityProperty, 1, 0, 0.4, 1.6, ease));

            sb.Completed += (_, __) =>
            {
                _eventBannerShowing = false;
                ResetEventOverlay();
            };

            _eventSb = sb;
            sb.Begin();
        }

        private void ResetEventOverlay()
        {
            EventOverlay.BeginAnimation(UIElement.OpacityProperty, null);
            EventOverlay.Opacity = 0;
            EventScoreGlow.BeginAnimation(UIElement.OpacityProperty, null);
            EventScoreGlow.Opacity = 0;
            EventEdgeGlow.BeginAnimation(UIElement.OpacityProperty, null);
            EventEdgeGlow.Opacity = 0;
            EventShimmer.BeginAnimation(UIElement.OpacityProperty, null);
            EventShimmer.Opacity = 0;
            EventTextScale.ScaleX = 1;
            EventTextScale.ScaleY = 1;
            EventGradientScroll.X = -1400;
            EventShimmerScroll.X = -200;
        }

        // ---- Storyboard animation helper (matches AFL ScorebugControl pattern) ----

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

        // ---- Helpers ----

        private static void TrySetColor(SolidColorBrush brush, string hex)
        {
            try { brush.Color = (Color)ColorConverter.ConvertFromString(hex); }
            catch { }
        }

        private static Color SafeColor(string hex, string fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return (Color)ColorConverter.ConvertFromString(fallback); }
        }

        private static SolidColorBrush SafeBrush(string hex, string fallback)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)); }
        }

        /// <summary>Car-indicator-style flash: blinks gold 4 times then settles back to white.</summary>
        private static void FlashElement(TextBlock element)
        {
            var brush = new SolidColorBrush(Colors.White);
            element.Foreground = brush;

            var blink = new ColorAnimation
            {
                From = Colors.White,
                To = Colors.Gold,
                Duration = TimeSpan.FromSeconds(1.0),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(4),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            blink.Completed += (_, __) =>
            {
                brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                element.Foreground = WhiteBrush;
            };

            brush.BeginAnimation(SolidColorBrush.ColorProperty, blink);
        }

        /// <summary>Slot-machine-style rolling number animation: rapidly cycles through
        /// intermediate values with quick slide-up snaps, decelerating into the final
        /// value which lands with the full smooth scroll + fade transition.</summary>
        private void AnimateScoreRoll(TextBlock element, TranslateTransform translate,
            int fromValue, int toValue, double height = 60)
        {
            int diff = toValue - fromValue;
            if (diff == 0) return;

            // For single-digit changes, just do the normal smooth scroll
            if (Math.Abs(diff) == 1)
            {
                ScrollValue(element, translate, toValue.ToString(), height);
                return;
            }

            // Cap rolling to last 10 intermediate steps for large jumps
            int direction = diff > 0 ? 1 : -1;
            int maxSteps = 10;
            int totalSteps = Math.Min(Math.Abs(diff), maxSteps);
            int rollStart = toValue - direction * totalSteps;
            int currentValue = rollStart;
            int currentStep = 0;

            // If we skipped ahead, immediately set the starting number
            if (rollStart != fromValue)
                element.Text = rollStart.ToString();

            var timer = new DispatcherTimer();

            void Tick(object? s, EventArgs e)
            {
                currentStep++;
                currentValue += direction;

                // Eased timing: starts fast (~50ms), decelerates to ~180ms at end
                double progress = (double)currentStep / totalSteps;
                double interval = 50 + 130 * progress * progress;
                timer.Interval = TimeSpan.FromMilliseconds(interval);

                if (currentValue == toValue)
                {
                    timer.Stop();
                    timer.Tick -= Tick;
                    // Final value gets the full smooth scroll-in
                    ScrollValue(element, translate, toValue.ToString(), height);
                }
                else
                {
                    // Intermediate: quick slide-snap upward
                    translate.BeginAnimation(TranslateTransform.YProperty, null);
                    translate.Y = height * 0.25;
                    element.Text = currentValue.ToString();
                    var snapIn = new DoubleAnimation(height * 0.25, 0, TimeSpan.FromSeconds(0.06))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    translate.BeginAnimation(TranslateTransform.YProperty, snapIn);
                }
            }

            timer.Interval = TimeSpan.FromMilliseconds(50);
            timer.Tick += Tick;
            timer.Start();
        }

        /// <summary>Vertical scroll animation for a TextBlock: slides + fades old value up, then slides + fades new value in from below.
        /// The TextBlock must have a TranslateTransform as its RenderTransform.</summary>
        private static void ScrollValue(TextBlock element, TranslateTransform translate, string newValue, double height = 28)
        {
            if (element.Text == newValue) return;

            var dur = TimeSpan.FromSeconds(0.35);
            var exitEase = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            var enterEase = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            // Slide + fade old value out (accelerating away)
            var slideOut = new DoubleAnimation(0, -height, dur) { EasingFunction = exitEase };
            var fadeOut = new DoubleAnimation(1, 0, dur) { EasingFunction = exitEase };

            slideOut.Completed += (_, __) =>
            {
                // Set new value and slide + fade in from below (decelerating to rest)
                element.Text = newValue;
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.Y = height;
                element.BeginAnimation(OpacityProperty, null);
                element.Opacity = 0;

                var slideIn = new DoubleAnimation(height, 0, dur) { EasingFunction = enterEase };
                var fadeIn = new DoubleAnimation(0, 1, dur) { EasingFunction = enterEase };
                translate.BeginAnimation(TranslateTransform.YProperty, slideIn);
                element.BeginAnimation(OpacityProperty, fadeIn);
            };

            translate.BeginAnimation(TranslateTransform.YProperty, slideOut);
            element.BeginAnimation(OpacityProperty, fadeOut);
        }

        // ======== AFL-STYLE OVERLAY BAR SYSTEM ========

        private void OnOverlayBarHideTimer(object? sender, EventArgs e)
        {
            _overlayBarHideTimer?.Stop();
            if (_activeOverlayBar != null)
                HideOverlayBar(_activeOverlayBar);
        }

        private Border? GetOverlayBarBorder(string barName) => barName switch
        {
            "partnership" => PartnershipBarBorder,
            "runrate" => RunRateBarBorder,
            "target" => TargetBarBorder,
            "powerplay" => PowerplayBarBorder,
            "last5" => Last5OversBarBorder,
            "drought" => BoundaryDroughtBarBorder,
            "overs_warning" => OversWarningBarBorder,
            "milestone" => MilestoneBarBorder,
            _ => null
        };

        /// <summary>Shows a named overlay bar with Height animation (AFL-style).</summary>
        private void ShowOverlayBarInternal(string barName, double height = 50)
        {
            // Hide any currently showing bar first
            if (_activeOverlayBar != null && _activeOverlayBar != barName)
                HideOverlayBarImmediate(_activeOverlayBar);

            var border = GetOverlayBarBorder(barName);
            if (border == null) return;

            _activeOverlayBar = barName;
            _overlayBarHideTimer?.Stop();

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var heightAnim = new DoubleAnimation(0, height, TimeSpan.FromSeconds(0.35)) { EasingFunction = ease };
            border.BeginAnimation(HeightProperty, heightAnim);

            _overlayBarHideTimer!.Interval = TimeSpan.FromSeconds(OverlayBarDisplaySec);
            _overlayBarHideTimer.Start();
        }

        /// <summary>Hides a named overlay bar with Height animation.</summary>
        private void HideOverlayBar(string barName)
        {
            var border = GetOverlayBarBorder(barName);
            if (border == null) return;

            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var heightAnim = new DoubleAnimation(border.ActualHeight, 0, TimeSpan.FromSeconds(0.3)) { EasingFunction = ease };
            heightAnim.Completed += (_, __) =>
            {
                border.BeginAnimation(HeightProperty, null);
                border.Height = 0;
                if (_activeOverlayBar == barName)
                {
                    _activeOverlayBar = null;
                    // Stop stripe animation if it was the warning bar
                    if (barName == "overs_warning")
                        _oversWarningStripeTimer?.Stop();
                    OnOverlayFinished();
                }
            };
            border.BeginAnimation(HeightProperty, heightAnim);
        }

        private void HideOverlayBarImmediate(string barName)
        {
            var border = GetOverlayBarBorder(barName);
            if (border == null) return;
            border.BeginAnimation(HeightProperty, null);
            border.Height = 0;
            if (_activeOverlayBar == barName)
                _activeOverlayBar = null;
            if (barName == "overs_warning")
                _oversWarningStripeTimer?.Stop();
        }

        // ---- Partnership Bar ----

        /// <summary>Shows the partnership bar overlay.</summary>
        public void ShowPartnershipBar()
        {
            if (_lastMatch?.CurrentInnings == null) return;
            EnqueueOverlay(() => ShowPartnershipBarInternal());
        }

        private void ShowPartnershipBarInternal()
        {
            var inn = _lastMatch?.CurrentInnings;
            if (inn == null) { OnOverlayFinished(); return; }

            PartnershipBarRuns.Text = inn.PartnershipRuns.ToString();
            PartnershipBarBalls.Text = $"({inn.PartnershipBalls})";
            ShowOverlayBarInternal("partnership");
        }

        // ---- Run Rate Bar ----

        public void ShowRunRateBar()
        {
            EnqueueOverlay(() => ShowRunRateBarInternal());
        }

        private void ShowRunRateBarInternal()
        {
            if (_lastMatch == null) { OnOverlayFinished(); return; }
            var inn = _lastMatch.CurrentInnings;
            if (inn == null) { OnOverlayFinished(); return; }

            RunRateBarCRR.Text = inn.RunRate.ToString("F2");
            RunRateBarRRR.Text = _lastMatch.RequiredRunRate?.ToString("F2") ?? "\u2014";
            ShowOverlayBarInternal("runrate");
        }

        // ---- Target / Chase Bar ----

        public void ShowTargetBar()
        {
            EnqueueOverlay(() => ShowTargetBarInternal());
        }

        private void ShowTargetBarInternal()
        {
            if (_lastMatch == null) { OnOverlayFinished(); return; }

            TargetBarTarget.Text = _lastMatch.Target?.ToString() ?? "\u2014";
            TargetBarNeed.Text = _lastMatch.RunsRequired?.ToString() ?? "\u2014";
            TargetBarBalls.Text = _lastMatch.BallsRemaining?.ToString() ?? "\u2014";
            ShowOverlayBarInternal("target");
        }

        // ---- Powerplay Bar ----

        public void ShowPowerplayBar()
        {
            EnqueueOverlay(() => ShowPowerplayBarInternal());
        }

        private void ShowPowerplayBarInternal()
        {
            if (_lastMatch?.CurrentInnings == null) { OnOverlayFinished(); return; }
            var inn = _lastMatch.CurrentInnings;
            int oversUsed = Math.Min(inn.CompletedOvers, 6);
            PowerplayBarOvers.Text = $"{oversUsed}/6";
            ShowOverlayBarInternal("powerplay");
        }

        // ---- Last 5 Overs Bar ----

        public void ShowLast5OversBar()
        {
            EnqueueOverlay(() => ShowLast5OversBarInternal());
        }

        private void ShowLast5OversBarInternal()
        {
            if (_lastMatch?.CurrentInnings == null) { OnOverlayFinished(); return; }
            int runs = _lastMatch.CurrentInnings.RunsInLastOvers(5);
            Last5OversBarValue.Text = $"{runs} runs";
            ShowOverlayBarInternal("last5");
        }

        // ---- Boundary Drought Bar ----

        public void ShowBoundaryDroughtBar()
        {
            EnqueueOverlay(() => ShowBoundaryDroughtBarInternal());
        }

        private void ShowBoundaryDroughtBarInternal()
        {
            if (_lastMatch?.CurrentInnings == null) { OnOverlayFinished(); return; }
            int balls = _lastMatch.CurrentInnings.BoundaryDroughtBalls;
            BoundaryDroughtBarValue.Text = $"{balls} balls";
            ShowOverlayBarInternal("drought");
        }

        // ---- Overs Remaining Warning Bar (AFL 5-minute style) ----

        public void ShowOversRemainingWarning(int oversLeft)
        {
            EnqueueOverlay(() => ShowOversRemainingWarningInternal(oversLeft));
        }

        private void ShowOversRemainingWarningInternal(int oversLeft)
        {
            OversWarningText.Text = $"{oversLeft} OVERS REMAINING";
            _oversWarningStripeTimer?.Start();
            ShowOverlayBarInternal("overs_warning");
        }

        private void OnOversWarningStripeTick(object? sender, EventArgs e)
        {
            OversWarningStripesTranslate.X = (OversWarningStripesTranslate.X + 0.8) % 40;
        }

        // ---- Milestone Bar (50/100/150/200) ----

        public void ShowMilestoneCelebration(string playerName, int milestone)
        {
            EnqueueOverlay(() => ShowMilestoneInternal(playerName, milestone));
        }

        private void ShowMilestoneInternal(string playerName, int milestone)
        {
            string text = milestone switch
            {
                50 => "HALF CENTURY!",
                100 => "CENTURY!",
                150 => "150 UP!",
                200 => "DOUBLE CENTURY!",
                _ => $"{milestone} RUNS!"
            };

            string emoji = milestone >= 100 ? "\U0001F31F" : "\U0001F389";

            // Gold gradient for century+, standard for 50
            Color gradStart = milestone >= 100
                ? Color.FromRgb(0x3D, 0x2D, 0x00)
                : Color.FromRgb(0x1A, 0x1A, 0x00);
            Color gradMid = milestone >= 100
                ? Color.FromRgb(0x6B, 0x50, 0x00)
                : Color.FromRgb(0x3D, 0x3D, 0x00);

            MilestoneBarGradient.GradientStops[0].Color = gradStart;
            MilestoneBarGradient.GradientStops[1].Color = gradMid;
            MilestoneBarGradient.GradientStops[2].Color = gradStart;

            MilestoneBarEmoji.Text = emoji;
            MilestoneBarText.Text = text;
            MilestoneBarPlayer.Text = playerName.ToUpper();
            ShowOverlayBarInternal("milestone");
        }

        // ---- Auto-trigger checks (called from UpdateFromMatch) ----

        /// <summary>Checks for automatic overlay triggers based on match state changes.</summary>
        private void CheckAutoOverlays(CricketMatchManager match)
        {
            var inn = match.CurrentInnings;
            if (inn == null) return;

            // Auto-show powerplay bar at start and on over completion during powerplay
            if (match.IsInPowerplay)
            {
                int currentOver = inn.CompletedOvers;
                if (currentOver != _lastPowerplayOver && currentOver < 6)
                {
                    _lastPowerplayOver = currentOver;
                    if (!_powerplayBarShownThisInnings || currentOver == 0)
                    {
                        _powerplayBarShownThisInnings = true;
                        ShowPowerplayBar();
                    }
                }
            }

            // Auto-show boundary drought after 18+ balls (3 overs) without boundary
            if (inn.BoundaryDroughtBalls >= 18 && inn.BoundaryDroughtBalls % 6 == 0)
                ShowBoundaryDroughtBar();

            // Auto-show overs remaining at key points
            if (match.Format == CricketFormat.LimitedOvers && match.BallsRemaining != null)
            {
                int oversLeft = match.BallsRemaining.Value / 6;
                int ballsLeftInOver = match.BallsRemaining.Value % 6;

                // Trigger at exactly 10 and 5 overs remaining (on the ball)
                if (ballsLeftInOver == 0 && oversLeft > 0)
                {
                    if ((oversLeft == 10 || oversLeft == 5) && _lastOversWarningThreshold != oversLeft)
                    {
                        _lastOversWarningThreshold = oversLeft;
                        ShowOversRemainingWarning(oversLeft);
                    }
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;

// WinForms colour wheel
using WinForms = System.Windows.Forms;
using DrawingColor = System.Drawing.Color;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brushes = System.Windows.Media.Brushes;
using Panel = System.Windows.Controls.Panel;
using Point = System.Windows.Point;
using MessageBox = System.Windows.MessageBox;

namespace Roche_Scoreboard
{
    public partial class MainWindow : Window
    {
        private readonly MatchManager _match = new();
        private Window? _displayWindow;
        private ScorebugControl? _scorebug;
        private BreakScreenControl? _breakScreen;
        private ScorewormControl? _scoreworm;
        private StatsScreenControl? _statsScreen;
        private VideoPlayerControl? _videoPlayer;
        private Grid? _displayContainer;
        private bool _showingBreakScreen;

        // Custom video presentation path
        private string? _customVideoPath;

        // Break screen rotation
        private DispatcherTimer? _rotationTimer;
        private int _rotationIndex;

        // Periodic stats bar on live scorebug
        private DispatcherTimer? _statsBarTimer;
        private DispatcherTimer? _statsBarHideTimer;

        // 5-minute warning state
        private bool _fiveMinWarningShown;

        private string? _homeLogoPath;
        private string? _awayLogoPath;

        private double _homeLogoZoom = 1.0;
        private double _homeLogoOffsetX;
        private double _homeLogoOffsetY;
        private double _awayLogoZoom = 1.0;
        private double _awayLogoOffsetX;
        private double _awayLogoOffsetY;

        private bool _syncingLogoCropControls;
        private string _selectedPreviewKey = "presentation:scorebug";

        // Custom goal video paths
        private string? _homeGoalVideoPath;
        private string? _awayGoalVideoPath;
        private MediaElement? _goalVideoOverlay;
        private Action? _goalVideoCompletionCallback;

        private readonly List<string> _messages = new();

        // Win probability engine
        private readonly WinProbabilityEngine _winProbEngine = new();

        // Sport mode
        private SportMode _sportMode = SportMode.AFL;

        // Cricket
        private CricketMatchManager? _cricketMatch;
        private CricketScorebugControl? _cricketScorebug;
        private CricketSummaryControl? _cricketSummary;
        private Window? _cricketDisplayWindow;

        public MainWindow()
        {
            InitializeComponent();

            _match.SetTeams("Home Team", "HOM", "Away Team", "AWA");

            CompositionTarget.Rendering += OnFrameRendering;

            _match.MatchChanged += () =>
            {
                Title = $"Roche Scoreboard | Q{_match.Quarter} | {_match.HomeTotal} – {_match.AwayTotal}";
                PushAllToScoreboard();
                UpdateLiveUI();
            };

            _match.ScoreEventAdded += (ev) =>
            {
                // Insert a quarter separator if this event starts a new quarter
                var events = _match.Events;
                if (events.Count >= 2)
                {
                    var prev = events[events.Count - 2];
                    if (prev.Quarter != ev.Quarter)
                    {
                        EventList.Children.Insert(0, CreateQuarterSplitBar(ev.Quarter));
                    }
                }

                EventList.Children.Insert(0, CreateScoreLogBar(ev));
                LastEventText.Text = $"Last: {ev.Description}";

                // Reset scoreless timer on any score
                _scorebug?.ResetScorelessTimer();

                // Reset per-team drought tracker for the scoring team
                _scorebug?.ResetTeamDroughtTimer(ev.Team == TeamSide.Home);

                // Reset the idle auto-show G/B timer
                _scorebug?.ResetAutoShowGBTimer();

                // Auto-detect scoring runs and lead changes
                CheckScoringRunAfterScore(ev);
                CheckLeadChangesAfterScore();

                // Push updated data to any visible overlay or screen
                RefreshVisibleScreens();
            };

            ApplyStatusText.Text = "";

            // Wire up the embedded setup wizard (AFL)
            SetupWizardPanel.SetupCompleted += OnSetupCompleted;

            // Wire up the cricket setup wizard
            CricketSetupWizardPanel.SetupCompleted += OnCricketSetupCompleted;

            // Wire up the cricket control panel reset
            CricketControlPanelView.ResetRequested += OnCricketResetRequested;

            // Wire up sport selection
            SportSelectionPanel.SportSelected += OnSportSelected;

            // Sport selection visible by default; everything else hidden
            MainContent.Visibility = Visibility.Collapsed;
            SetupWizardPanel.Visibility = Visibility.Collapsed;
            CricketSetupWizardPanel.Visibility = Visibility.Collapsed;
            CricketControlPanelView.Visibility = Visibility.Collapsed;
            SportSelectionPanel.Visibility = Visibility.Visible;

            Closing += MainWindow_Closing;
            Loaded += MainWindow_Loaded;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            UpdateLogoCropSummaryText();
            UpdateHoverPreviewVisual(_selectedPreviewKey);
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            CompositionTarget.Rendering -= OnFrameRendering;
            _displayWindow?.Close();
            _cricketDisplayWindow?.Close();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - ActualWidth;
            Top = workArea.Bottom - ActualHeight;
        }

        private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Don't intercept when typing in a TextBox
            if (e.OriginalSource is System.Windows.Controls.TextBox) return;
            if (_sportMode != SportMode.AFL) return;

            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            bool q4Ended = _match.Quarter == 4 && _match.GetQuarterSnapshot(4) != null;

            switch (e.Key)
            {
                case Key.Space:
                    if (!q4Ended)
                    {
                        StartPause_Click(this, new RoutedEventArgs());
                        e.Handled = true;
                    }
                    break;
                case Key.G when ctrl:
                    if (!q4Ended) { HomeGoal_Click(this, new RoutedEventArgs()); e.Handled = true; }
                    break;
                case Key.B when ctrl:
                    if (!q4Ended) { HomeBehind_Click(this, new RoutedEventArgs()); e.Handled = true; }
                    break;
                case Key.G when alt:
                    if (!q4Ended) { AwayGoal_Click(this, new RoutedEventArgs()); e.Handled = true; }
                    break;
                case Key.B when alt:
                    if (!q4Ended) { AwayBehind_Click(this, new RoutedEventArgs()); e.Handled = true; }
                    break;
                case Key.Z when ctrl:
                    Undo_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.E when ctrl:
                    if (!q4Ended) { EndQuarter_Click(this, new RoutedEventArgs()); e.Handled = true; }
                    break;
            }
        }

        // ----------------------------
        // Clock — fires every frame via CompositionTarget.Rendering
        // ----------------------------
        private int _lastDisplayedSecond = -1;

        private void OnFrameRendering(object? sender, EventArgs e)
        {
            _match.Tick();   // handles countdown expiry only

            if (!_match.ClockRunning) return;

            // Only touch the UI when the displayed second actually changes
            int sec = (int)_match.DisplayClock.TotalSeconds;
            if (sec == _lastDisplayedSecond) return;
            _lastDisplayedSecond = sec;

            PushClockToScoreboard();

            // Update just the clock portion of the control panel
            var dc = _match.DisplayClock;
            HeaderQuarterClock.Text = $"Q{_match.Quarter}  {(int)dc.TotalMinutes:D2}:{dc.Seconds:D2}";
        }

        // ----------------------------
        // Live UI updates
        // ----------------------------
        private static readonly SolidColorBrush LiveGreenBrush = new(Color.FromRgb(0x22, 0xC5, 0x5E));
        private static readonly SolidColorBrush LiveGreenBgBrush = new(Color.FromRgb(0x14, 0x53, 0x2D));
        private static readonly SolidColorBrush PausedAmberBrush = new(Color.FromRgb(0xF5, 0x9E, 0x0B));
        private static readonly SolidColorBrush PausedAmberBgBrush = new(Color.FromRgb(0x42, 0x20, 0x06));
        private static readonly SolidColorBrush ClockRunningBrush = new(Color.FromRgb(0x4A, 0xDE, 0x80));
        private static readonly SolidColorBrush ClockStoppedBrush = new(Color.FromRgb(0x94, 0xA3, 0xB8));

        private static readonly SolidColorBrush FullTimeBrush = new(Color.FromRgb(0x94, 0xA3, 0xB8));
        private static readonly SolidColorBrush FullTimeBgBrush = new(Color.FromRgb(0x1E, 0x29, 0x3B));

        private void UpdateLiveUI()
        {
            bool q4Ended = _match.Quarter == 4 && _match.GetQuarterSnapshot(4) != null;

            // Status badge
            if (q4Ended)
            {
                StatusDot.Fill = FullTimeBrush;
                StatusText.Text = "FULL TIME";
                StatusText.Foreground = FullTimeBrush;
                StatusBadge.Background = FullTimeBgBrush;
            }
            else if (_match.ClockRunning)
            {
                StatusDot.Fill = LiveGreenBrush;
                StatusText.Text = "LIVE";
                StatusText.Foreground = LiveGreenBrush;
                StatusBadge.Background = LiveGreenBgBrush;
            }
            else
            {
                StatusDot.Fill = PausedAmberBrush;
                StatusText.Text = "PAUSED";
                StatusText.Foreground = PausedAmberBrush;
                StatusBadge.Background = PausedAmberBgBrush;
            }

            // Start/Pause button
            if (q4Ended)
            {
                StartPauseButton.Content = "Full Time";
            }
            else if (_match.ClockRunning)
            {
                StartPauseButton.Content = "⏸  Pause";
            }
            else
            {
                StartPauseButton.Content = "▶  Start";
            }

            // End quarter button — show current quarter and disable after Q4
            EndQtrButton.Content = q4Ended ? "FT" : $"End Q{_match.Quarter}";
            EndQtrButton.IsEnabled = !q4Ended;

            // Disable scoring and clock after full time
            StartPauseButton.IsEnabled = !q4Ended;

            // Quarter + Clock in header
            var dc = _match.DisplayClock;
            HeaderQuarterClock.Text = $"Q{_match.Quarter}  {(int)dc.TotalMinutes:D2}:{dc.Seconds:D2}";

            // Score summary in header
            HeaderHomeAbbr.Text = _match.HomeAbbr.ToUpper();
            HeaderAwayAbbr.Text = _match.AwayAbbr.ToUpper();
            HeaderScore.Text = $"{_match.HomeTotal} – {_match.AwayTotal}";

            // Margin indicator
            int margin = _match.Margin;
            if (margin > 0)
                HeaderMarginText.Text = $"{_match.HomeAbbr} +{margin}";
            else if (margin < 0)
                HeaderMarginText.Text = $"{_match.AwayAbbr} +{-margin}";
            else
                HeaderMarginText.Text = "DRAWN";

            // Centre scoring panels
            HomeScoringLabel.Text = _match.HomeName.ToUpper();
            AwayScoringLabel.Text = _match.AwayName.ToUpper();
            HomeScoreDisplay.Text = $"{_match.HomeGoals}.{_match.HomeBehinds}.{_match.HomeTotal}";
            AwayScoreDisplay.Text = $"{_match.AwayGoals}.{_match.AwayBehinds}.{_match.AwayTotal}";

            // Match result summary
            if (margin > 0)
                MatchResultText.Text = $"{_match.HomeName} leads by {margin} point{(margin == 1 ? "" : "s")}";
            else if (margin < 0)
                MatchResultText.Text = $"{_match.AwayName} leads by {-margin} point{(-margin == 1 ? "" : "s")}";
            else if (_match.HomeTotal > 0 || _match.AwayTotal > 0)
                MatchResultText.Text = "Scores are level";
            else
                MatchResultText.Text = "";

            // Start button tooltip — context-sensitive hint
            UpdateStartButtonTooltip(q4Ended);

            // Clock status text in settings
            UpdateClockStatus();
        }

        private void UpdateStartButtonTooltip(bool q4Ended)
        {
            if (q4Ended || _match.ClockRunning)
            {
                StartSwitchWarning.Visibility = Visibility.Collapsed;
                return;
            }

            // Show warning when pressing Start would auto-switch the display
            StartSwitchWarning.Visibility = (_showingBreakScreen || _isBreakRotating)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // ----------------------------
        // Setup wizard
        // ----------------------------
        private void ShowSetupWizard()
        {
            // Clear any held animation values from a previous transition so
            // local property sets take effect on the next wizard completion.
            MainContent.BeginAnimation(OpacityProperty, null);
            SetupWizardPanel.BeginAnimation(OpacityProperty, null);

            // Return to sport selection
            MainContent.Visibility = Visibility.Collapsed;
            SetupWizardPanel.Visibility = Visibility.Collapsed;
            CricketSetupWizardPanel.Visibility = Visibility.Collapsed;
            CricketControlPanelView.Visibility = Visibility.Collapsed;
            SportSelectionPanel.Opacity = 1;
            SportSelectionPanel.Visibility = Visibility.Visible;
        }

        private void OnSportSelected(SportMode mode)
        {
            _sportMode = mode;
            SportSelectionPanel.Visibility = Visibility.Collapsed;

            if (mode == SportMode.AFL)
            {
                SetupWizardPanel.Reset();
                SetupWizardPanel.Opacity = 1;
                SetupWizardPanel.Visibility = Visibility.Visible;
            }
            else
            {
                CricketSetupWizardPanel.Reset();
                CricketSetupWizardPanel.Opacity = 1;
                CricketSetupWizardPanel.Visibility = Visibility.Visible;
            }
        }

        private void OnSetupCompleted(SetupResult result)
        {
            ApplySetupResult(result);

            // Animate wizard out and main content in
            MainContent.Opacity = 0;
            MainContent.RenderTransform = new TranslateTransform(0, 20);
            MainContent.Visibility = Visibility.Visible;

            var sb = new Storyboard();

            // Wizard fades out
            var wizardFade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.25))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            Storyboard.SetTarget(wizardFade, SetupWizardPanel);
            Storyboard.SetTargetProperty(wizardFade, new PropertyPath(OpacityProperty));
            sb.Children.Add(wizardFade);

            // Main content fades in (slightly delayed)
            var contentFade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.35))
            {
                BeginTime = TimeSpan.FromSeconds(0.15),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(contentFade, MainContent);
            Storyboard.SetTargetProperty(contentFade, new PropertyPath(OpacityProperty));
            sb.Children.Add(contentFade);

            // Main content slides up
            var contentSlide = new DoubleAnimation(20, 0, TimeSpan.FromSeconds(0.35))
            {
                BeginTime = TimeSpan.FromSeconds(0.15),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(contentSlide, MainContent);
            Storyboard.SetTargetProperty(contentSlide, new PropertyPath("RenderTransform.(TranslateTransform.Y)"));
            sb.Children.Add(contentSlide);

            sb.Completed += (_, __) =>
            {
                SetupWizardPanel.Visibility = Visibility.Collapsed;
                SetupWizardPanel.Opacity = 1;
                MainContent.RenderTransform = null;
            };
            sb.Begin();

            // Show the scorebug on the scoreboard display
            EnsureScoreboardWindow();
            if (_displayWindow is { IsVisible: false }) _displayWindow.Show();
            PushAllToScoreboard();
        }

        // ----------------------------
        // Cricket setup + display
        // ----------------------------
        private void OnCricketSetupCompleted(CricketSetupResult result)
        {
            // Create and configure the cricket match manager
            _cricketMatch = new CricketMatchManager
            {
                Format = result.Format,
                TotalOvers = result.TotalOvers,
                TotalInnings = result.Format == CricketFormat.LimitedOvers ? 2 : 4,
                TeamABatsFirst = result.TeamABatsFirst,
                TeamAPrimaryColor = result.TeamAPrimaryColor,
                TeamASecondaryColor = result.TeamASecondaryColor,
                TeamBPrimaryColor = result.TeamBPrimaryColor,
                TeamBSecondaryColor = result.TeamBSecondaryColor,
                TeamALogoPath = result.TeamALogoPath,
                TeamBLogoPath = result.TeamBLogoPath,
                TeamAPlayers = result.TeamAPlayers,
                TeamBPlayers = result.TeamBPlayers,
                Messages = result.Messages
            };
            _cricketMatch.SetTeams(result.TeamAName, result.TeamAAbbr,
                                   result.TeamBName, result.TeamBAbbr);
            _cricketMatch.StartMatch();

            // Create the cricket scorebug
            _cricketScorebug = new CricketScorebugControl();
            _cricketScorebug.UpdateFromMatch(_cricketMatch);
            _cricketScorebug.SetMarqueeMessages(result.Messages);

            // Create the summary control
            _cricketSummary = new CricketSummaryControl();
            _cricketSummary.Visibility = Visibility.Collapsed;

            // Wire control panel
            CricketControlPanelView.SetMatch(_cricketMatch);
            CricketControlPanelView.SetScorebug(_cricketScorebug);
            CricketControlPanelView.SetSummaryControl(_cricketSummary);
            CricketControlPanelView.SetMessages(result.Messages);
            CricketControlPanelView.PresentationChanged += OnCricketPresentationChanged;

            // Create the cricket display window
            EnsureCricketDisplayWindow();

            // Transition: hide wizard, show cricket control panel
            CricketSetupWizardPanel.Visibility = Visibility.Collapsed;
            CricketControlPanelView.Opacity = 0;
            CricketControlPanelView.RenderTransform = new TranslateTransform(0, 20);
            CricketControlPanelView.Visibility = Visibility.Visible;

            var sb = new Storyboard();

            var fade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.35))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(fade, CricketControlPanelView);
            Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));
            sb.Children.Add(fade);

            var slide = new DoubleAnimation(20, 0, TimeSpan.FromSeconds(0.35))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(slide, CricketControlPanelView);
            Storyboard.SetTargetProperty(slide, new PropertyPath("RenderTransform.(TranslateTransform.Y)"));
            sb.Children.Add(slide);

            sb.Completed += (_, __) =>
            {
                CricketControlPanelView.RenderTransform = null;
                // Prompt for opening batters + bowler right after panel appears
                CricketControlPanelView.TriggerInitialSelection();
            };
            sb.Begin();

            Title = $"Roche Scoreboard | Cricket | {result.TeamAName} vs {result.TeamBName}";
        }

        private void EnsureCricketDisplayWindow()
        {
            if (_cricketDisplayWindow != null && _cricketDisplayWindow.IsVisible) return;
            if (_cricketScorebug == null) return;

            var container = new Grid();
            container.Children.Add(_cricketScorebug);
            if (_cricketSummary != null)
                container.Children.Add(_cricketSummary);

            _cricketDisplayWindow = new Window
            {
                Title = "Cricket Scoreboard Display",
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                Topmost = true,
                ShowInTaskbar = false,
                Background = System.Windows.Media.Brushes.Transparent,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Width = 752,
                Height = 440,
                Left = 0,
                Top = 0,
                Content = container
            };

            _cricketDisplayWindow.Closed += (_, __) =>
            {
                _cricketDisplayWindow = null;
            };

            _cricketDisplayWindow.Loaded += (_, __) =>
            {
                _cricketDisplayWindow!.Left = 0;
                _cricketDisplayWindow.Top = 0;
            };

            _cricketDisplayWindow.Show();
        }

        private void OnCricketPresentationChanged(string target)
        {
            if (target == "window")
            {
                EnsureCricketDisplayWindow();
                return;
            }

            // Ensure display window exists
            EnsureCricketDisplayWindow();
        }

        private void OnCricketResetRequested()
        {
            _cricketMatch?.ResetForNewGame();
            CricketControlPanelView.PresentationChanged -= OnCricketPresentationChanged;
            _cricketDisplayWindow?.Close();
            _cricketDisplayWindow = null;
            _cricketScorebug = null;
            _cricketSummary = null;
            _cricketMatch = null;

            CricketControlPanelView.BeginAnimation(OpacityProperty, null);
            CricketControlPanelView.Visibility = Visibility.Collapsed;
            SportSelectionPanel.Opacity = 1;
            SportSelectionPanel.Visibility = Visibility.Visible;
        }

        private void ApplySetupResult(SetupResult r)
        {
            _match.SetTeams(r.HomeName, r.HomeAbbr, r.AwayName, r.AwayAbbr);

            HomeColorBox.Text = r.HomePrimaryColor;
            HomeSecondaryColorBox.Text = r.HomeSecondaryColor;
            AwayColorBox.Text = r.AwayPrimaryColor;
            AwaySecondaryColorBox.Text = r.AwaySecondaryColor;
            TrySetPreview(HomeColorPreview, r.HomePrimaryColor);
            TrySetPreview(HomeSecondaryColorPreview, r.HomeSecondaryColor);
            TrySetPreview(AwayColorPreview, r.AwayPrimaryColor);
            TrySetPreview(AwaySecondaryColorPreview, r.AwaySecondaryColor);

            _homeLogoPath = r.HomeLogoPath;
            _awayLogoPath = r.AwayLogoPath;
            _homeGoalVideoPath = r.HomeGoalVideoPath;
            _awayGoalVideoPath = r.AwayGoalVideoPath;

            ResetLogoCropState();

            if (r.ClockMode == ClockMode.Countdown)
            {
                ClockModeCountdown.IsChecked = true;
                _match.SetClockMode(ClockMode.Countdown);
                _match.SetQuarterDuration(TimeSpan.FromMinutes(r.QuarterMinutes) + TimeSpan.FromSeconds(r.QuarterSeconds));
                QuarterMinutesBox.Text = r.QuarterMinutes.ToString();
                QuarterSecondsBox.Text = r.QuarterSeconds.ToString("D2");
            }
            else
            {
                ClockModeCountUp.IsChecked = true;
                _match.SetClockMode(ClockMode.CountUp);
            }

            _messages.Clear();
            _messages.AddRange(r.Messages);
            MessageList.Items.Clear();
            foreach (var msg in _messages)
                MessageList.Items.Add(msg);

            SyncControlPanelFields();
            ApplyTeamColorsToScoringPanels();

            if (_displayWindow != null && _displayWindow.IsVisible)
            {
                PushAllToScoreboard();
            }
        }

        private void SyncControlPanelFields()
        {
            HomeNameBox.Text = _match.HomeName;
            HomeAbbrBox.Text = _match.HomeAbbr;
            AwayNameBox.Text = _match.AwayName;
            AwayAbbrBox.Text = _match.AwayAbbr;
        }

        private static void TrySetPreview(Border preview, string hex)
        {
            try { preview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { /* ignore */ }
        }

        // ----------------------------
        // Scoreboard window management
        // ----------------------------
        private const int RESIZE_BORDER = 10;

        private void EnsureScoreboardWindow()
        {
            if (_displayWindow != null && _displayWindow.IsVisible) return;

            _scorebug = new ScorebugControl();
            _breakScreen = new BreakScreenControl { Visibility = Visibility.Collapsed };
            _scoreworm = new ScorewormControl { Visibility = Visibility.Collapsed };
            _statsScreen = new StatsScreenControl { Visibility = Visibility.Collapsed };
            _videoPlayer = new VideoPlayerControl { Visibility = Visibility.Collapsed };

            _displayContainer = new Grid { ClipToBounds = true };
            _displayContainer.Children.Add(_scorebug);
            _displayContainer.Children.Add(_breakScreen);
            _displayContainer.Children.Add(_scoreworm);
            _displayContainer.Children.Add(_statsScreen);
            _displayContainer.Children.Add(_videoPlayer);

            _goalVideoOverlay = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Stretch = System.Windows.Media.Stretch.UniformToFill,
                Visibility = Visibility.Collapsed,
                Volume = 1.0
            };
            Panel.SetZIndex(_goalVideoOverlay, 200);
            _goalVideoOverlay.MediaEnded += GoalVideo_MediaEnded;
            _goalVideoOverlay.MediaFailed += GoalVideo_MediaFailed;
            _displayContainer.Children.Add(_goalVideoOverlay);

            _showingBreakScreen = false;

            _displayWindow = new Window
            {
                Title = "Scoreboard Display",
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                Topmost = true,
                ShowInTaskbar = false,
                Background = Brushes.Transparent,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Width = 752,
                Height = 440,
                Left = 0,
                Top = 0,
                Content = _displayContainer
            };

            _displayWindow.Closed += (_, __) =>
            {
                _rotationTimer?.Stop();
                _rotationTimer = null;
                StopStatsBarTimer();
                StopGoalVideo();
                _videoPlayer?.Stop();
                _displayWindow = null;
                _scorebug = null;
                _breakScreen = null;
                _scoreworm = null;
                _statsScreen = null;
                _videoPlayer = null;
                _goalVideoOverlay = null;
                _displayContainer = null;
                _showingBreakScreen = false;
            };

            _displayWindow.Loaded += (_, __) =>
            {
                _displayWindow.Left = 0;
                _displayWindow.Top = 0;

                var source = (HwndSource)PresentationSource.FromVisual(_displayWindow)!;
                source.AddHook(DisplayWndProc);
            };

            _displayWindow.Show();
            PushAllToScoreboard();

            AnimationRadio_Changed(this, new RoutedEventArgs());
        }

        private void PushAllToScoreboard()
        {
            if (_scorebug == null || _displayWindow == null || !_displayWindow.IsVisible) return;

            _scorebug.SetTeams(_match.HomeName, _match.HomeAbbr, _match.AwayName, _match.AwayAbbr);
            _scorebug.SetQuarter(_match.Quarter);

            if (DateTime.UtcNow >= _deferScorePushUntilUtc)
            {
                _scorebug.SetScores(_match.HomeGoals, _match.HomeBehinds, _match.AwayGoals, _match.AwayBehinds);
            }

            if (_match.ClockMode == ClockMode.Countdown && _match.ClockRunning)
            {
                var remaining = _match.DisplayClock;
                if (remaining <= TimeSpan.FromMinutes(5) && remaining > TimeSpan.Zero && !_fiveMinWarningShown)
                {
                    _fiveMinWarningShown = true;
                    _scorebug.ShowFiveMinuteWarning();
                }
            }
            else if (_fiveMinWarningShown && (!_match.ClockRunning || _match.ClockMode != ClockMode.Countdown))
            {
                _fiveMinWarningShown = false;
                _scorebug.HideFiveMinuteWarning();
            }

            _scorebug.SetTeamColors(
                HomeColorBox.Text, AwayColorBox.Text,
                HomeSecondaryColorBox.Text, AwaySecondaryColorBox.Text);
            _scorebug.SetLogos(_homeLogoPath, _awayLogoPath);
            ApplyLogoCropToScorebug();
            _scorebug.SetMarqueeMessages(_messages);

            PushClockToScoreboard();
        }

        private void PushClockToScoreboard()
        {
            if (_scorebug == null || _displayWindow == null || !_displayWindow.IsVisible) return;

            var dc = _match.DisplayClock;
            _scorebug.SetClock($"{(int)dc.TotalMinutes:D2}:{dc.Seconds:D2}");

            if (_match.ClockRunning && !_showingBreakScreen)
            {
                _scorebug.CheckScorelessTimer();
                _scorebug.CheckTeamDrought(
                    _match.HomeName, _match.AwayName,
                    GetTeamColor(true), GetTeamColor(false));
                _scorebug.CheckAutoShowGBColumns();
                CheckPeriodicOverlays();
            }

            UpdateClockStatus();
        }

        // ----------------------------
        // Logo crop in advanced settings
        // ----------------------------
        private void ResetLogoCropState()
        {
            _homeLogoZoom = 1.0;
            _homeLogoOffsetX = 0;
            _homeLogoOffsetY = 0;
            _awayLogoZoom = 1.0;
            _awayLogoOffsetX = 0;
            _awayLogoOffsetY = 0;
            SyncLogoCropControlsFromState();
            UpdateLogoCropSummaryText();
        }

        private void SyncLogoCropControlsFromState()
        {
            if (HomeLogoZoomSlider == null || AwayLogoZoomSlider == null) return;

            _syncingLogoCropControls = true;
            HomeLogoZoomSlider.Value = _homeLogoZoom;
            HomeLogoXSlider.Value = _homeLogoOffsetX;
            HomeLogoYSlider.Value = _homeLogoOffsetY;
            AwayLogoZoomSlider.Value = _awayLogoZoom;
            AwayLogoXSlider.Value = _awayLogoOffsetX;
            AwayLogoYSlider.Value = _awayLogoOffsetY;
            _syncingLogoCropControls = false;

            UpdateInlineLogoCropPreviews();
        }

        private void UpdateLogoCropSummaryText()
        {
            if (LogoCropSummaryText == null) return;

            LogoCropSummaryText.Text =
                $"Home: z {_homeLogoZoom:0.00}, x {_homeLogoOffsetX:0}, y {_homeLogoOffsetY:0}    |    " +
                $"Away: z {_awayLogoZoom:0.00}, x {_awayLogoOffsetX:0}, y {_awayLogoOffsetY:0}";

            UpdateInlineLogoCropPreviews();
        }

        private void UpdateInlineLogoCropPreviews()
        {
            if (HomeLogoCropPreviewImage == null || AwayLogoCropPreviewImage == null) return;

            HomeLogoCropPreviewImage.Source = LoadLogoPreview(_homeLogoPath);
            AwayLogoCropPreviewImage.Source = LoadLogoPreview(_awayLogoPath);

            ApplyImageCropTransform(HomeLogoCropPreviewImage, _homeLogoZoom, _homeLogoOffsetX, _homeLogoOffsetY);
            ApplyImageCropTransform(AwayLogoCropPreviewImage, _awayLogoZoom, _awayLogoOffsetX, _awayLogoOffsetY);
        }

        private static BitmapImage? LoadLogoPreview(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return null;

            try
            {
                BitmapImage bitmap = new();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        private static void ApplyImageCropTransform(System.Windows.Controls.Image image, double zoom, double x, double y)
        {
            image.RenderTransformOrigin = new Point(0.5, 0.5);
            TransformGroup group = new();
            group.Children.Add(new ScaleTransform(zoom, zoom));
            group.Children.Add(new TranslateTransform(x, y));
            image.RenderTransform = group;
        }

        private void PreviewButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Presentation preview box removed.
        }

        private void PreviewButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Presentation preview box removed.
        }

        private void SetSelectedPreview(string key)
        {
            _selectedPreviewKey = key;
        }

        private void UpdateHoverPreviewVisual(string key)
        {
            // Presentation preview box removed.
        }

        private void ShowScoreboard_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedPreview("presentation:window");
            EnsureScoreboardWindow();
            _displayWindow?.Activate();
        }

        // ----------------------------
        // Screen switching buttons
        // ----------------------------
        private void SwitchToScorebug_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedPreview("presentation:scorebug");
            EnsureScoreboardWindow();
            if (_scorebug == null) return;
            SwitchToScreen(_scorebug, "Live Scorebug");
        }

        private void SwitchToBreakScreen_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedPreview("presentation:summary");
            EnsureScoreboardWindow();
            if (_breakScreen == null) return;
            ShowBreakScreen();
        }

        private void SwitchToScoreworm_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedPreview("presentation:worm");
            EnsureScoreboardWindow();
            if (_scoreworm == null) return;
            ShowScoreworm();
        }

        private void SwitchToStatsScreen_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedPreview("presentation:stats");
            EnsureScoreboardWindow();
            if (_statsScreen == null) return;
            ShowStatsScreen();
        }

        // ----------------------------
        // Video presentation
        // ----------------------------
        private void SwitchToVideo_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedPreview("presentation:video");
            try
            {
                if (!string.IsNullOrWhiteSpace(_customVideoPath) && System.IO.File.Exists(_customVideoPath))
                {
                    if (_videoPlayer != null)
                    {
                        _videoPlayer.SetVideoSource(_customVideoPath);
                        _videoPlayer.Play();
                    }

                    if (_goalVideoOverlay != null)
                    {
                        _goalVideoOverlay.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    _customVideoPath = null;
                }
            }
            catch
            {
                _customVideoPath = null;
            }

            VideoPickerPrompt.Visibility = Visibility.Visible;
        }

        // Manual overlay handlers are implemented in `MainWindow.RestoreMembers.cs`.
    }
}

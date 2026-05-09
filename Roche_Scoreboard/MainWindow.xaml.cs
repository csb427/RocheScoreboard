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
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Roche_Scoreboard.Web;

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
        private WeatherScreenControl? _weatherScreen;
        private Grid? _displayContainer;
        private bool _showingBreakScreen;

        // Custom video presentation path
        private string? _customVideoPath;

        // Clock state tracking for animations
        private bool _previousClockRunning;

        // Break screen rotation
        private DispatcherTimer? _rotationTimer;
        private int _rotationIndex;

        // Periodic stats bar on live scorebug
        private DispatcherTimer? _statsBarTimer;
        private DispatcherTimer? _statsBarHideTimer;

        // Periodic auto-save so a crash, force-kill, power loss or session-end
        // never loses more than a few seconds of clock progress. The MatchChanged
        // event only fires on discrete changes (start/pause/score/quarter end),
        // which would otherwise leave the persisted ElapsedInQuarterTicks lagging
        // far behind the live stopwatch while the clock is just running.
        private DispatcherTimer? _autoSaveTimer;
        private static readonly TimeSpan AutoSaveInterval = TimeSpan.FromSeconds(5);

        // 5-minute warning state
        private bool _fiveMinWarningShown;
        private TimeSpan _previousRemainingTime = TimeSpan.MaxValue;

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

        // Retro CRT transition overlay
        private Border? _retroCrtOverlay;

        // Calibration overlay (Ctrl+Shift+C on the display window) — helps the
        // operator manually align the output window with the mirrored capture
        // region of the outdoor electronic scoreboard.
        private CalibrationOverlay? _calibrationOverlay;

        private readonly List<string> _messages = new();
        private readonly System.Collections.ObjectModel.ObservableCollection<Roche_Scoreboard.Models.MarqueeMessage> _styledMessages = new();
        private bool _finalsMode;
        private string? _weatherLocation;
        private WeatherService? _weatherService;
        private string? _activeWeatherLocation;

        // Suppresses TeamSettings_TextChanged during bulk field sync
        private bool _suppressTeamSync;

        // Win probability engine
        private readonly WinProbabilityEngine _winProbEngine = new();

        // Structured overlay scheduler
        private readonly OverlayScheduler _overlayScheduler = new();

        // Sport mode
        private SportMode _sportMode = SportMode.AFL;

        // Cricket
        private CricketMatchManager? _cricketMatch;
        private CricketScorebugControl? _cricketScorebug;
        private CricketSummaryControl? _cricketSummary;
        private Window? _cricketDisplayWindow;

        // Training mode (intervals / stopwatches)
        private TrainingSession? _trainingSession;
        private TrainingScorebugControl? _trainingScorebug;
        private Window? _trainingDisplayWindow;
        private DispatcherTimer? _trainingTickTimer;

        // Embedded web server for remote control panel and live view
        private WebHostService? _webHost;
        private string _activeScreenKey = "scorebug";

        public MainWindow()
        {
            InitializeComponent();

            _match.SetTeams("Home Team", "HOM", "Away Team", "AWA");

            CompositionTarget.Rendering += OnFrameRendering;

            _match.MatchChanged += () =>
            {
                Title = $"Roche Scoreboard v{AutoUpdateService.CurrentVersion.ToString(3)} | Q{_match.Quarter} | {_match.HomeTotal} – {_match.AwayTotal}";
                PushAllToScoreboard();
                UpdateLiveUI();
                BroadcastWebState();
                SaveGameState();
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
                        Border splitBar = CreateQuarterSplitBar(ev.Quarter);
                        EventList.Children.Insert(0, splitBar);
                        AnimateLogEntry(splitBar);
                    }
                }

                Border logBar = CreateScoreLogBar(ev);
                EventList.Children.Insert(0, logBar);
                AnimateLogEntry(logBar);
                string teamName = ev.Team == TeamSide.Home ? _match.HomeName : _match.AwayName;
                string scoreType = ev.Type == ScoreType.Goal ? "Goal" : "Behind";
                LastEventText.Text = $"Last: {teamName} {scoreType}";

                // Hide the empty-state hint as soon as any event appears
                if (MatchLogEmptyHint is not null)
                    MatchLogEmptyHint.Visibility = Visibility.Collapsed;

                // Update the count badge
                if (MatchLogCountBadge is not null)
                    MatchLogCountBadge.Text = $"({_match.Events.Count})";

                // Pulse the control panel score display for the scoring team
                PulseElement(ev.Team == TeamSide.Home ? HomeScoreDisplay : AwayScoreDisplay, 250);
                PulseElement(HeaderScore, 200);

                // Reset scoreless timer on any score
                _scorebug?.ResetScorelessTimer();

                // Reset per-team drought tracker for the scoring team
                _scorebug?.ResetTeamDroughtTimer(ev.Team == TeamSide.Home);

                // If a scoreless/drought overlay is currently on-screen, fade
                // it out immediately — the underlying information (time since
                // last score) is stale the moment a team scores.
                _scorebug?.CancelScorelessOrDroughtOverlay();

                // Reset the idle auto-show G/B timer
                _scorebug?.ResetAutoShowGBTimer();

                // Notify the overlay scheduler that a scoring event occurred
                _overlayScheduler.NotifyScoreEvent();

                // Auto-detect scoring runs and lead changes. Scoring run is
                // event-driven and time-sensitive (the viewer wants to see it
                // close to the goal that produced it) so it's flagged priority,
                // bypassing the 90-second informational-overlay cooldown.
                var capturedEv = ev;
                ScheduleAutoOverlay(
                    () => { if (_scorebug != null) EnqueueAutoOverlay(() => CheckScoringRunAfterScore(capturedEv), isPriority: true); },
                    ScoringRunOverlayDelay,
                    () => _scoringRunDelayTimer,
                    t => _scoringRunDelayTimer = t);
                ScheduleAutoOverlay(
                    () => { if (_scorebug != null) EnqueueAutoOverlay(() => CheckLeadChangesAfterScore(), isPriority: true); });

                // Push updated data to any visible overlay or screen
                RefreshVisibleScreens();
            };

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
            InitSettingsWeatherLocation();
            RegisterScheduledOverlays();
        }

        private void MainTabControl_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (e.Source != MainTabControl)
                return;

            // Hide the match strip when the scoring tab is active (index 0)
            // because scoring already displays scores prominently
            MatchStripBorder.Visibility = MainTabControl.SelectedIndex == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Update prompt on close has been removed in favour of an in-app
            // update section in the Settings tab. Proceed straight to teardown.

            CompositionTarget.Rendering -= OnFrameRendering;
            ScoreboardHub.CommandReceived -= OnWebCommand;
            StopAutoSaveTimer();
            SaveGameState();
            SaveAllWindowLayouts();
            StopFrameCapture();
            _weatherService?.Dispose();
            _weatherService = null;
            _displayWindow?.Close();
            _cricketDisplayWindow?.Close();
            _trainingDisplayWindow?.Close();

            if (_webHost is not null)
            {
                await _webHost.DisposeAsync();
                _webHost = null;
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Restore the operator's last-saved control panel layout if any,
            // otherwise default to the bottom-right of the current work area.
            // Either way the position is clamped inside the visible work area
            // so the window can never spawn off-screen — for example when the
            // app launches on a smaller laptop than it was last sized on, or
            // on a different monitor configuration.
            var saved = WindowLayoutStorage.Get(WindowLayoutStorage.ControlPanelKey);
            if (saved is not null)
            {
                WindowLayoutStorage.Apply(this, saved, MinWidth, MinHeight);
            }
            else
            {
                var workArea = SystemParameters.WorkArea;
                const double padding = 40;

                // If the default 1550x1050 size is larger than the work area
                // (small laptop / low-res monitor), shrink to fit so the
                // taskbar and title bar stay reachable.
                double width = Math.Min(ActualWidth, workArea.Width - padding);
                double height = Math.Min(ActualHeight, workArea.Height - padding);
                if (width < MinWidth) width = MinWidth;
                if (height < MinHeight) height = MinHeight;

                Width = width;
                Height = height;
                Left = workArea.Right - width - padding;
                Top = workArea.Bottom - height - padding;

                // Final clamp in case anything pushed us off-screen.
                if (Left < workArea.Left) Left = workArea.Left;
                if (Top < workArea.Top) Top = workArea.Top;
            }

            StartWebServerAsync();

            // Defer alerts so the window is fully visible first
            await Dispatcher.InvokeAsync(ShowDeferredAlerts, System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// Captures the current Left/Top/Width/Height of the main window and
        /// every open display window so the operator's chosen layout is
        /// remembered between launches.
        /// </summary>
        private void SaveAllWindowLayouts()
        {
            try
            {
                WindowLayoutStorage.Save(WindowLayoutStorage.ControlPanelKey, this);
                if (_displayWindow is { IsVisible: true })
                    WindowLayoutStorage.Save(WindowLayoutStorage.AflDisplayKey, _displayWindow);
                if (_cricketDisplayWindow is { IsVisible: true })
                    WindowLayoutStorage.Save(WindowLayoutStorage.CricketDisplayKey, _cricketDisplayWindow);
                if (_trainingDisplayWindow is { IsVisible: true })
                    WindowLayoutStorage.Save(WindowLayoutStorage.TrainingDisplayKey, _trainingDisplayWindow);
            }
            catch
            {
                // Persisting layout is best-effort; never block shutdown.
            }
        }

        /// <summary>
        /// Mirrors a display window's live size into the WINDOW DIMENSIONS
        /// boxes on the operator console. Hooks SizeChanged so the numbers
        /// update in real time as the operator drags the window edges, and
        /// also seeds the boxes once the window is loaded so they reflect
        /// the actual launched size (which may have been restored from a
        /// saved layout rather than the default 960×540).
        /// </summary>
        private void HookDimensionMirror(Window displayWindow)
        {
            if (displayWindow is null) return;

            displayWindow.SizeChanged += (_, e) =>
            {
                if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
                UpdateDimensionBoxes(e.NewSize.Width, e.NewSize.Height);
            };

            displayWindow.Loaded += (_, __) =>
            {
                UpdateDimensionBoxes(displayWindow.ActualWidth, displayWindow.ActualHeight);
            };
        }

        /// <summary>
        /// Pushes the given live width/height into the operator-console
        /// dimension boxes. Skips when either box is currently focused so
        /// the operator's typing isn't overwritten mid-edit.
        /// </summary>
        private void UpdateDimensionBoxes(double width, double height)
        {
            if (WindowWidthBox is null || WindowHeightBox is null) return;

            int w = (int)Math.Round(width);
            int h = (int)Math.Round(height);

            if (!WindowWidthBox.IsKeyboardFocused)
            {
                string wText = w.ToString();
                if (WindowWidthBox.Text != wText) WindowWidthBox.Text = wText;
            }

            if (!WindowHeightBox.IsKeyboardFocused)
            {
                string hText = h.ToString();
                if (WindowHeightBox.Text != hText) WindowHeightBox.Text = hText;
            }
        }

        /// <summary>
        /// Shows deferred alerts after the window is visible.
        /// Update alert takes priority; if skipped, the game-restore alert follows.
        /// </summary>
        private void ShowDeferredAlerts()
        {
            // Refresh the in-app Settings update card so the user can act on
            // any pending update from there. Removed the close-time alert
            // entirely — update management lives in the Settings tab now.
            try { RefreshUpdateStatusUi(); } catch { }

            // Then check for a saved game in progress
            if (GameStateService.HasSavedState())
            {
                GameState? saved = GameStateService.Load();
                if (saved?.MatchState is not null)
                {
                    var dialog = new Views.ResumeGameDialog(saved)
                    {
                        Owner = this
                    };

                    dialog.ShowDialog();

                    if (dialog.ResumeChosen)
                    {
                        RestoreFromSavedState(saved);
                    }
                    else
                    {
                        GameStateService.Clear();
                    }
                }
            }
        }

        private async void StartWebServerAsync()
        {
            try
            {
                _webHost = new WebHostService();
                await _webHost.StartAsync();
                ScoreboardHub.CommandReceived += OnWebCommand;
                BroadcastWebState();
                StartFrameCapture();

                string localIp = GetLocalIpAddress();
                int port = _webHost.Port;
                string liveUrl = $"http://{localIp}:{port}/live.html";
                string controlUrl = $"http://{localIp}:{port}/control.html";
                WebLiveUrlBox.Text = liveUrl;
                WebControlUrlBox.Text = controlUrl;
                WebAccessStatus.Text = $"Server running on port {port}";
                WebAccessStatus.Foreground = FindResource("LiveBrush") as System.Windows.Media.SolidColorBrush;

                LiveQrImage.Source = GenerateQrCode(liveUrl);
                ControlQrImage.Source = GenerateQrCode(controlUrl);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Web server failed to start: {ex.Message}");
                WebAccessStatus.Text = "Web server failed to start.";
                WebAccessStatus.Foreground = FindResource("AwayAccentBrush") as System.Windows.Media.SolidColorBrush;
            }
        }

        private static BitmapSource GenerateQrCode(string text)
        {
            using var generator = new QRCoder.QRCodeGenerator();
            using QRCoder.QRCodeData data = generator.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.M);
            using var qr = new QRCoder.BitmapByteQRCode(data);
            byte[] png = qr.GetGraphic(6, [255, 255, 255], [13, 17, 23]);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new System.IO.MemoryStream(png);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private static string GetLocalIpAddress()
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                    continue;
                if (ni.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Loopback
                    or System.Net.NetworkInformation.NetworkInterfaceType.Tunnel)
                    continue;

                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return addr.Address.ToString();
                }
            }

            return "localhost";
        }

        private void CopyLiveUrl_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(WebLiveUrlBox.Text) && WebLiveUrlBox.Text != "—")
                TrySetClipboardWithFeedback(WebLiveUrlBox.Text, WebAccessStatus);
        }

        private void CopyControlUrl_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(WebControlUrlBox.Text) && WebControlUrlBox.Text != "—")
                TrySetClipboardWithFeedback(WebControlUrlBox.Text, WebAccessStatus);
        }

        private static void TrySetClipboard(string text)
        {
            try { System.Windows.Clipboard.SetText(text); }
            catch (System.Runtime.InteropServices.ExternalException) { /* clipboard locked by another process */ }
        }

        /// <summary>
        /// Copies text to the clipboard and shows a brief "Copied!" flash on a status TextBlock.
        /// </summary>
        private void TrySetClipboardWithFeedback(string text, System.Windows.Controls.TextBlock statusBlock)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);

                string prev = statusBlock.Text;
                System.Windows.Media.Brush prevBrush = statusBlock.Foreground;
                statusBlock.Text = "✓ Copied!";
                statusBlock.Foreground = FindResource("LiveBrush") as SolidColorBrush ?? Brushes.Green;

                var revert = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                revert.Tick += (_, _) =>
                {
                    revert.Stop();
                    statusBlock.Text = prev;
                    statusBlock.Foreground = prevBrush;
                };
                revert.Start();
            }
            catch (System.Runtime.InteropServices.ExternalException) { /* clipboard locked */ }
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

        // Toggles the calibration overlay on the display window. Bound to
        // Ctrl+Shift+C so the operator can quickly verify the output window
        // matches the mirrored capture region of the outdoor scoreboard.
        private void DisplayWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (_calibrationOverlay is null) return;

            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

            if (ctrl && shift && e.Key == Key.C)
            {
                _calibrationOverlay.Visibility = _calibrationOverlay.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                e.Handled = true;
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

            CheckPeriodicOverlays();
            BroadcastWebState();
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

        // Cached brushes for Start/Pause button states (avoid per-frame allocation)
        private static readonly SolidColorBrush StartBtnBg = new(Color.FromRgb(0x0D, 0x30, 0x18));
        private static readonly SolidColorBrush StartBtnBorder = new(Color.FromRgb(0x1A, 0x60, 0x40));
        private static readonly SolidColorBrush PauseBtnBg = new(Color.FromRgb(0x42, 0x20, 0x06));
        private static readonly SolidColorBrush PauseBtnBorder = new(Color.FromRgb(0x8B, 0x5C, 0x0F));
        private static readonly SolidColorBrush FullTimeBtnBg = new(Color.FromRgb(0x21, 0x26, 0x2D));
        private static readonly SolidColorBrush FullTimeBtnBorder = new(Color.FromRgb(0x30, 0x36, 0x3D));
        private static readonly SolidColorBrush UndoDisabledBrush = new(Color.FromRgb(0x1A, 0x10, 0x11));
        private static readonly SolidColorBrush UndoDisabledBorderBrush = new(Color.FromRgb(0x3A, 0x28, 0x2A));
        private static readonly SolidColorBrush UndoEnabledBrush = new(Color.FromRgb(0x2A, 0x14, 0x16));
        private static readonly SolidColorBrush UndoEnabledBorderBrush = new(Color.FromRgb(0x6B, 0x3A, 0x3E));

        private void UpdateLiveUI()
        {
            bool q4Ended = _match.Quarter == 4 && _match.GetQuarterSnapshot(4) != null;
            bool clockStateChanged = _match.ClockRunning != _previousClockRunning;
            _previousClockRunning = _match.ClockRunning;

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

            // Animate state change on Start/Pause button and status badge
            if (clockStateChanged && !q4Ended)
            {
                AnimateClockStateChange();
            }

            // Drive the scorebug's pause-aware match clock from the same
            // signal so scoring-run / scoreless / drought elapsed timers
            // freeze when the game clock is paused and resume when it
            // restarts. Safe to call repeatedly (Notify methods are idempotent).
            if (clockStateChanged)
            {
                if (_match.ClockRunning)
                {
                    _scorebug?.NotifyClockStarted();
                    StartAutoSaveTimer();
                }
                else
                {
                    _scorebug?.NotifyClockStopped();
                    // Persist the freshly-banked elapsed immediately so a
                    // crash between now and the next auto-save tick still
                    // captures the exact pause-time elapsed.
                    StopAutoSaveTimer();
                    SaveGameState();
                }
            }

            // Start/Pause button — uses cached brushes to avoid per-frame allocation
            if (q4Ended)
            {
                StartPauseButton.Content = "Full Time";
                StartPauseButton.Background = FullTimeBtnBg;
                StartPauseButton.BorderBrush = FullTimeBtnBorder;
            }
            else if (_match.ClockRunning)
            {
                StartPauseButton.Content = "⏸  Pause";
                StartPauseButton.Background = PauseBtnBg;
                StartPauseButton.BorderBrush = PauseBtnBorder;
            }
            else
            {
                // Between quarters (Q1 ended, Q2 not yet started, etc.) the
                // button starts the *next* quarter, so make that explicit. At
                // the very start of a fresh match (Q1, no elapsed time, no
                // events) keep the plain "Start" label.
                bool atFreshMatchStart = _match.Quarter == 1
                    && _match.ElapsedInQuarter == TimeSpan.Zero
                    && _match.Events.Count == 0;
                StartPauseButton.Content = atFreshMatchStart
                    ? "▶  Start"
                    : $"▶  Start Q{_match.Quarter}";
                StartPauseButton.Background = StartBtnBg;
                StartPauseButton.BorderBrush = StartBtnBorder;
            }

            // End quarter button — show current quarter and disable after Q4
            EndQtrButton.IsEnabled = !q4Ended;

            // Swap End Qtr / Continue Qtr button visibility
            bool canContinue = _match.CanContinueQuarter;
            EndQtrButton.Visibility = canContinue ? Visibility.Collapsed : Visibility.Visible;
            ContinueQtrButton.Visibility = canContinue ? Visibility.Visible : Visibility.Collapsed;

            if (canContinue)
            {
                // During break: show which quarter can be resumed
                int endedQ = _match.Quarter - 1;
                ContinueQtrButton.Content = $"↩ Resume Q{endedQ}";
                EndQtrButton.Content = $"End Q{endedQ}";
            }
            else
            {
                EndQtrButton.Content = q4Ended ? "FT" : $"End Q{_match.Quarter}";
            }

            // Disable scoring and clock after full time
            StartPauseButton.IsEnabled = !q4Ended;
            HomeGoalButton.IsEnabled = !q4Ended;
            HomeBehindButton.IsEnabled = !q4Ended;
            AwayGoalButton.IsEnabled = !q4Ended;
            AwayBehindButton.IsEnabled = !q4Ended;

            // Undo button — dim when no events exist, emphasize when available
            bool canUndo = _match.Events.Count > 0;
            UndoButton.IsEnabled = canUndo;
            UndoButton.Opacity = canUndo ? 1.0 : 0.4;
            UndoButton.Background = canUndo ? UndoEnabledBrush : UndoDisabledBrush;
            UndoButton.BorderBrush = canUndo ? UndoEnabledBorderBrush : UndoDisabledBorderBrush;

            // Quarter + Clock in header
            var dc = _match.DisplayClock;
            HeaderQuarterClock.Text = $"Q{_match.Quarter}  {(int)dc.TotalMinutes:D2}:{dc.Seconds:D2}";

            // Score summary in header
            HeaderHomeAbbr.Text = _match.HomeAbbr.ToUpper();
            HeaderAwayAbbr.Text = _match.AwayAbbr.ToUpper();
            HeaderScore.Text = $"{_match.HomeTotal} – {_match.AwayTotal}";

            // Margin indicator — tint with leading team colour
            int margin = _match.Margin;
            if (margin > 0)
            {
                HeaderMarginText.Text = $"{_match.HomeAbbr} +{margin}";
                HeaderMarginText.Foreground = FindResource("HomeAccentBrush") as SolidColorBrush ?? Brushes.White;
            }
            else if (margin < 0)
            {
                HeaderMarginText.Text = $"{_match.AwayAbbr} +{-margin}";
                HeaderMarginText.Foreground = FindResource("AwayAccentBrush") as SolidColorBrush ?? Brushes.White;
            }
            else
            {
                HeaderMarginText.Text = "DRAWN";
                HeaderMarginText.Foreground = FindResource("TextMidBrush") as SolidColorBrush ?? Brushes.White;
            }

            // Centre scoring panels
            HomeScoringLabel.Text = _match.HomeName.ToUpper();
            AwayScoringLabel.Text = _match.AwayName.ToUpper();
            HomeScoreDisplay.Text = $"{_match.HomeGoals}.{_match.HomeBehinds}.{_match.HomeTotal}";
            AwayScoreDisplay.Text = $"{_match.AwayGoals}.{_match.AwayBehinds}.{_match.AwayTotal}";

            // Match result summary — context-aware wording for live vs full time
            if (q4Ended)
            {
                if (margin > 0)
                    MatchResultText.Text = $"{_match.HomeName} defeated {_match.AwayName} by {margin} point{(margin == 1 ? "" : "s")}";
                else if (margin < 0)
                    MatchResultText.Text = $"{_match.AwayName} defeated {_match.HomeName} by {-margin} point{(-margin == 1 ? "" : "s")}";
                else
                    MatchResultText.Text = "Match drawn";
            }
            else if (margin > 0)
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
            GameStateService.Clear();
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
            else if (mode == SportMode.Cricket)
            {
                CricketSetupWizardPanel.Reset();
                CricketSetupWizardPanel.Opacity = 1;
                CricketSetupWizardPanel.Visibility = Visibility.Visible;
            }
            else // Training
            {
                EnterTrainingMode();
            }
        }

        // ----------------------------
        // Training mode
        // ----------------------------
        private void EnterTrainingMode()
        {
            // Tear down any prior session
            ExitTrainingModeInternal(suppressUiTransition: true);

            _trainingSession = new TrainingSession();
            _trainingScorebug = new TrainingScorebugControl();
            _trainingScorebug.Bind(_trainingSession);

            TrainingControlPanelView.Bind(_trainingSession);
            TrainingControlPanelView.ExitRequested -= OnTrainingExitRequested;
            TrainingControlPanelView.ExitRequested += OnTrainingExitRequested;

            // Drive the session's countdown from a UI tick
            _trainingTickTimer?.Stop();
            _trainingTickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _trainingTickTimer.Tick += (_, _) => _trainingSession?.Tick();
            _trainingTickTimer.Start();

            EnsureTrainingDisplayWindow();

            TrainingControlPanelView.Opacity = 0;
            TrainingControlPanelView.RenderTransform = new TranslateTransform(0, 20);
            TrainingControlPanelView.Visibility = Visibility.Visible;

            var sb = new Storyboard();
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.35))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(fade, TrainingControlPanelView);
            Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));
            sb.Children.Add(fade);
            var slide = new DoubleAnimation(20, 0, TimeSpan.FromSeconds(0.35))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(slide, TrainingControlPanelView);
            Storyboard.SetTargetProperty(slide, new PropertyPath("RenderTransform.(TranslateTransform.Y)"));
            sb.Children.Add(slide);
            sb.Completed += (_, _) => TrainingControlPanelView.RenderTransform = null;
            sb.Begin();

            Title = $"Roche Scoreboard v{AutoUpdateService.CurrentVersion.ToString(3)} | Training Mode";
        }

        private void EnsureTrainingDisplayWindow()
        {
            if (_trainingDisplayWindow != null && _trainingDisplayWindow.IsVisible) return;
            if (_trainingScorebug == null) return;

            // Wrap the scorebug in a Viewbox so the content scales as the
            // window is resized — same approach as the AFL / Cricket displays
            // so nothing clips at small sizes and everything stays legible.
            var container = new Grid { ClipToBounds = true };
            container.Children.Add(_trainingScorebug);

            _trainingDisplayWindow = new Window
            {
                Title = $"Roche Scoreboard v{AutoUpdateService.CurrentVersion.ToString(3)} | Training Display",
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                ResizeMode = ResizeMode.CanResize,
                Topmost = true,
                ShowInTaskbar = false,
                Background = System.Windows.Media.Brushes.Transparent,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Width = 386,
                Height = 193,
                Left = 0,
                Top = 0,
                Content = container
            };

            // Restore the operator's last-saved Training display window layout if any.
            var savedTraining = WindowLayoutStorage.Get(WindowLayoutStorage.TrainingDisplayKey);
            if (savedTraining is not null)
                WindowLayoutStorage.Apply(_trainingDisplayWindow, savedTraining);

            _trainingDisplayWindow.Closed += (_, _) => _trainingDisplayWindow = null;

            HookDimensionMirror(_trainingDisplayWindow);

            // Hook the borderless-resize NC hit-test so the user can grab any
            // edge or corner to resize the training display, exactly like the
            // AFL / Cricket display windows.
            _trainingDisplayWindow.SourceInitialized += (_, _) =>
            {
                if (_trainingDisplayWindow is null) return;
                var source = (HwndSource)PresentationSource.FromVisual(_trainingDisplayWindow)!;
                source.AddHook(TrainingDisplayWndProc);
            };

            _trainingDisplayWindow.Show();
        }

        private void OnTrainingExitRequested()
        {
            ExitTrainingModeInternal(suppressUiTransition: false);
        }

        private void ExitTrainingModeInternal(bool suppressUiTransition)
        {
            _trainingTickTimer?.Stop();
            _trainingTickTimer = null;

            _trainingScorebug?.ResetVisuals();
            _trainingScorebug = null;
            _trainingSession = null;

            TrainingControlPanelView.ExitRequested -= OnTrainingExitRequested;

            _trainingDisplayWindow?.Close();
            _trainingDisplayWindow = null;

            if (suppressUiTransition) return;

            TrainingControlPanelView.BeginAnimation(OpacityProperty, null);
            TrainingControlPanelView.Visibility = Visibility.Collapsed;
            SportSelectionPanel.Opacity = 1;
            SportSelectionPanel.Visibility = Visibility.Visible;
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

            Title = $"Roche Scoreboard v{AutoUpdateService.CurrentVersion.ToString(3)} | Cricket | {result.TeamAName} vs {result.TeamBName}";
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
                Title = $"Roche Scoreboard v{AutoUpdateService.CurrentVersion.ToString(3)} | Cricket Display",
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                ResizeMode = ResizeMode.CanResize,
                Topmost = true,
                ShowInTaskbar = false,
                Background = System.Windows.Media.Brushes.Transparent,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Width = 386,
                Height = 193,
                Left = 0,
                Top = 0,
                Content = container
            };

            // Restore the operator's last-saved Cricket display window layout if any.
            var savedCricket = WindowLayoutStorage.Get(WindowLayoutStorage.CricketDisplayKey);
            if (savedCricket is not null)
                WindowLayoutStorage.Apply(_cricketDisplayWindow, savedCricket);

            _cricketDisplayWindow.Closed += (_, __) =>
            {
                _cricketDisplayWindow = null;
                UpdateWindowToggleVisual();
            };

            HookDimensionMirror(_cricketDisplayWindow);

            _cricketDisplayWindow.Loaded += (_, __) =>
            {
                if (savedCricket is null)
                {
                    _cricketDisplayWindow!.Left = 0;
                    _cricketDisplayWindow.Top = 0;
                }

                var source = (HwndSource)PresentationSource.FromVisual(_cricketDisplayWindow)!;
                source.AddHook(CricketDisplayWndProc);
            };

            _cricketDisplayWindow.Show();
            UpdateWindowToggleVisual();
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
            GameStateService.Clear();
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
            _styledMessages.Clear();
            foreach (var msg in _messages)
                _styledMessages.Add(new Roche_Scoreboard.Models.MarqueeMessage(msg));
            if (MessageList.ItemsSource is null)
                MessageList.ItemsSource = _styledMessages;

            _finalsMode = r.FinalsMode;
            SettingsFinalsToggle.IsChecked = _finalsMode;
            UpdateSettingsFinalsCardVisuals();
            _weatherLocation = r.WeatherLocation;

            // Sync the settings weather ComboBox
            _suppressSettingsWeatherFilter = true;
            _settingsWeatherActivated = false;
            SettingsWeatherLocation.Text = _weatherLocation ?? "";
            _suppressSettingsWeatherFilter = false;

            SyncControlPanelFields();
            ApplyTeamColorsToScoringPanels();

            // Reset layout, animation, and presentation selections to defaults
            LayoutClassicRadio.IsChecked = true;
            UpdateLayoutCards();
            // Default to Custom Video animation only if at least one team has a video configured
            bool hasCustomVideo = !string.IsNullOrWhiteSpace(_homeGoalVideoPath) || !string.IsNullOrWhiteSpace(_awayGoalVideoPath);
            if (hasCustomVideo)
                AnimCustomVideoRadio.IsChecked = true;
            else
                AnimClassicRadio.IsChecked = true;
            UpdateAnimCards();
            UpdatePresentationCards("scorebug");
            _activeScreenKey = "scorebug";

            if (_displayWindow != null && _displayWindow.IsVisible)
            {
                PushAllToScoreboard();
            }

            _ = StartOrUpdateWeatherServiceAsync(_weatherLocation);
        }

        private void SyncControlPanelFields()
        {
            _suppressTeamSync = true;
            HomeNameBox.Text = _match.HomeName;
            HomeAbbrBox.Text = _match.HomeAbbr;
            AwayNameBox.Text = _match.AwayName;
            AwayAbbrBox.Text = _match.AwayAbbr;
            _suppressTeamSync = false;
        }

        private static void TrySetPreview(Border preview, string hex)
        {
            try { preview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { /* ignore */ }
        }

        // ----------------------------
        // Game state persistence
        // ----------------------------
        private void SaveGameState()
        {
            // Only persist AFL games that are in progress
            if (_sportMode != SportMode.AFL) return;
            if (_match.Events.Count == 0 && !_match.ClockRunning && _match.Quarter == 1) return;

            // Don't persist completed games — full time means Q4 has ended
            bool isFullTime = _match.Quarter == 4 && _match.GetQuarterSnapshot(4) != null;
            if (isFullTime)
            {
                GameStateService.Clear();
                return;
            }

            var state = new GameState
            {
                MatchState = _match.ToState(),
                HomePrimaryColor = HomeColorBox.Text,
                HomeSecondaryColor = HomeSecondaryColorBox.Text,
                AwayPrimaryColor = AwayColorBox.Text,
                AwaySecondaryColor = AwaySecondaryColorBox.Text,
                HomeLogoPath = _homeLogoPath,
                AwayLogoPath = _awayLogoPath,
                HomeGoalVideoPath = _homeGoalVideoPath,
                AwayGoalVideoPath = _awayGoalVideoPath,
                Messages = [.. _messages],
                FinalsMode = _finalsMode,
                WeatherLocation = _weatherLocation,
                HomeLogoZoom = _homeLogoZoom,
                HomeLogoOffsetX = _homeLogoOffsetX,
                HomeLogoOffsetY = _homeLogoOffsetY,
                AwayLogoZoom = _awayLogoZoom,
                AwayLogoOffsetX = _awayLogoOffsetX,
                AwayLogoOffsetY = _awayLogoOffsetY,
            };

            GameStateService.Save(state);
        }

        /// <summary>
        /// Crash-safe save wrapper invoked from the global exception handlers
        /// and Windows session-end handler in <see cref="App"/>. Swallows any
        /// exception so a save attempt during a teardown never re-throws and
        /// short-circuits the rest of the shutdown path. Always called on the
        /// UI thread.
        /// </summary>
        internal void TrySaveGameStateForShutdown()
        {
            try
            {
                SaveGameState();
            }
            catch
            {
                // Best-effort during shutdown / crash; do not re-throw.
            }
        }

        private void StartAutoSaveTimer()
        {
            if (_autoSaveTimer is not null) return;
            _autoSaveTimer = new DispatcherTimer { Interval = AutoSaveInterval };
            _autoSaveTimer.Tick += (_, _) => TrySaveGameStateForShutdown();
            _autoSaveTimer.Start();
        }

        private void StopAutoSaveTimer()
        {
            _autoSaveTimer?.Stop();
            _autoSaveTimer = null;
        }

        private void RestoreFromSavedState(GameState state)
        {
            if (state.MatchState is null) return;

            _match.LoadState(state.MatchState);

            HomeColorBox.Text = state.HomePrimaryColor;
            HomeSecondaryColorBox.Text = state.HomeSecondaryColor;
            AwayColorBox.Text = state.AwayPrimaryColor;
            AwaySecondaryColorBox.Text = state.AwaySecondaryColor;
            TrySetPreview(HomeColorPreview, state.HomePrimaryColor);
            TrySetPreview(HomeSecondaryColorPreview, state.HomeSecondaryColor);
            TrySetPreview(AwayColorPreview, state.AwayPrimaryColor);
            TrySetPreview(AwaySecondaryColorPreview, state.AwaySecondaryColor);

            _homeLogoPath = state.HomeLogoPath;
            _awayLogoPath = state.AwayLogoPath;
            _homeGoalVideoPath = state.HomeGoalVideoPath;
            _awayGoalVideoPath = state.AwayGoalVideoPath;

            _homeLogoZoom = state.HomeLogoZoom;
            _homeLogoOffsetX = state.HomeLogoOffsetX;
            _homeLogoOffsetY = state.HomeLogoOffsetY;
            _awayLogoZoom = state.AwayLogoZoom;
            _awayLogoOffsetX = state.AwayLogoOffsetX;
            _awayLogoOffsetY = state.AwayLogoOffsetY;
            SyncLogoCropControlsFromState();
            UpdateLogoCropSummaryText();

            if (state.MatchState.ClockMode == (int)ClockMode.Countdown)
            {
                ClockModeCountdown.IsChecked = true;
                var dur = _match.QuarterDuration;
                QuarterMinutesBox.Text = ((int)dur.TotalMinutes).ToString();
                QuarterSecondsBox.Text = (dur.Seconds).ToString("D2");
            }
            else
            {
                ClockModeCountUp.IsChecked = true;
            }

            _messages.Clear();
            _messages.AddRange(state.Messages);
            _styledMessages.Clear();
            foreach (string msg in _messages)
                _styledMessages.Add(new Roche_Scoreboard.Models.MarqueeMessage(msg));
            if (MessageList.ItemsSource is null)
                MessageList.ItemsSource = _styledMessages;

            _finalsMode = state.FinalsMode;
            SettingsFinalsToggle.IsChecked = _finalsMode;
            UpdateSettingsFinalsCardVisuals();
            _weatherLocation = state.WeatherLocation;
            _suppressSettingsWeatherFilter = true;
            _settingsWeatherActivated = false;
            SettingsWeatherLocation.Text = _weatherLocation ?? "";
            _suppressSettingsWeatherFilter = false;

            SyncControlPanelFields();
            ApplyTeamColorsToScoringPanels();

            // Rebuild the match log UI from restored events
            EventList.Children.Clear();
            int lastQ = 0;
            foreach (ScoreEvent ev in _match.Events)
            {
                if (lastQ != 0 && ev.Quarter != lastQ)
                {
                    Border splitBar = CreateQuarterSplitBar(ev.Quarter);
                    EventList.Children.Insert(0, splitBar);
                }
                lastQ = ev.Quarter;

                Border logBar = CreateScoreLogBar(ev);
                EventList.Children.Insert(0, logBar);
            }

            if (_match.Events.Count > 0)
            {
                if (MatchLogEmptyHint is not null)
                    MatchLogEmptyHint.Visibility = Visibility.Collapsed;
                if (MatchLogCountBadge is not null)
                    MatchLogCountBadge.Text = $"({_match.Events.Count})";

                ScoreEvent last = _match.Events[^1];
                string teamName = last.Team == TeamSide.Home ? _match.HomeName : _match.AwayName;
                string scoreType = last.Type == ScoreType.Goal ? "Goal" : "Behind";
                LastEventText.Text = $"Last: {teamName} {scoreType}";
            }

            // Reset layout/anim to defaults
            LayoutClassicRadio.IsChecked = true;
            UpdateLayoutCards();
            bool hasCustomVideo = !string.IsNullOrWhiteSpace(_homeGoalVideoPath) || !string.IsNullOrWhiteSpace(_awayGoalVideoPath);
            if (hasCustomVideo)
                AnimCustomVideoRadio.IsChecked = true;
            else
                AnimClassicRadio.IsChecked = true;
            UpdateAnimCards();
            UpdatePresentationCards("scorebug");
            _activeScreenKey = "scorebug";

            // Skip sport selection and wizard — go straight to main content
            SportSelectionPanel.Visibility = Visibility.Collapsed;
            SetupWizardPanel.Visibility = Visibility.Collapsed;
            MainContent.Opacity = 1;
            MainContent.Visibility = Visibility.Visible;

            _sportMode = SportMode.AFL;
            EnsureScoreboardWindow();
            if (_displayWindow is { IsVisible: false }) _displayWindow.Show();

            // Seed the overlay scheduler BEFORE pushing scoreboard state so the
            // first push (and any clock-edge detection it performs) cannot fire
            // an informational overlay during the resume itself.
            _overlayScheduler.NotifyGameResumed();

            // If the saved clock is at or below the 5-minute threshold, mark
            // the warning as already shown so the edge-detection in
            // PushAllToScoreboard does not fire it on the first restored tick.
            if (_match.ClockMode == ClockMode.Countdown)
            {
                _previousRemainingTime = _match.DisplayClock;
                if (_previousRemainingTime <= TimeSpan.FromMinutes(5))
                    _fiveMinWarningShown = true;
            }

            // Suppress overlays that the scoreboard control might enqueue
            // during its own startup checks (e.g. scoreless drought).
            _scorebug?.ResetAllOverlayState();

            PushAllToScoreboard();

            _ = StartOrUpdateWeatherServiceAsync(_weatherLocation);
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
            _weatherScreen = new WeatherScreenControl { Visibility = Visibility.Collapsed };

            _displayContainer = new Grid { ClipToBounds = true };
            _displayContainer.Children.Add(_scorebug);
            _displayContainer.Children.Add(_breakScreen);
            _displayContainer.Children.Add(_scoreworm);
            _displayContainer.Children.Add(_statsScreen);
            _displayContainer.Children.Add(_videoPlayer);
            _displayContainer.Children.Add(_weatherScreen);

            _goalVideoOverlay = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                // Fill: stretch the goal/celebration video to completely
                // cover the responsive output area (e.g. 386x193) so it
                // matches the scoreboard dimensions exactly.
                Stretch = System.Windows.Media.Stretch.Fill,
                Visibility = Visibility.Collapsed,
                Volume = 1.0
            };
            Panel.SetZIndex(_goalVideoOverlay, 200);
            _goalVideoOverlay.MediaEnded += GoalVideo_MediaEnded;
            _goalVideoOverlay.MediaFailed += GoalVideo_MediaFailed;
            _displayContainer.Children.Add(_goalVideoOverlay);

            // Retro CRT transition overlay — hidden until a retro transition fires
            _retroCrtOverlay = new Border
            {
                Background = Brushes.Black,
                Opacity = 0,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            Panel.SetZIndex(_retroCrtOverlay, 300);
            _displayContainer.Children.Add(_retroCrtOverlay);

            // Calibration overlay — hidden by default; toggled with Ctrl+Shift+C
            // when the display window has focus. Sits above everything so the
            // operator can see the exact bounds of the output window.
            _calibrationOverlay = new CalibrationOverlay { Visibility = Visibility.Collapsed };
            Panel.SetZIndex(_calibrationOverlay, 400);
            _displayContainer.Children.Add(_calibrationOverlay);

            _showingBreakScreen = false;

            _displayWindow = new Window
            {
                Title = $"Roche Scoreboard v{AutoUpdateService.CurrentVersion.ToString(3)} | Display",
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                ResizeMode = ResizeMode.CanResize,
                Topmost = true,
                ShowInTaskbar = false,
                Background = Brushes.Transparent,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Width = 386,
                Height = 193,
                Left = 0,
                Top = 0,
                Content = _displayContainer
            };

            // Restore the operator's last-saved AFL display window layout if any.
            // Falls back to the 386×193 capture-area default at (0,0) — top-left of the primary monitor.
            var savedAfl = WindowLayoutStorage.Get(WindowLayoutStorage.AflDisplayKey);
            if (savedAfl is not null)
                WindowLayoutStorage.Apply(_displayWindow, savedAfl);

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
                _weatherScreen = null;
                _goalVideoOverlay = null;
                _retroCrtOverlay = null;
                _calibrationOverlay = null;
                _displayContainer = null;
                _showingBreakScreen = false;
                UpdateWindowToggleVisual();
            };

            HookDimensionMirror(_displayWindow);

            // Ctrl+Shift+C toggles the calibration overlay on the display window.
            _displayWindow.PreviewKeyDown += DisplayWindow_PreviewKeyDown;

            _displayWindow.Loaded += (_, __) =>
            {
                // Don't snap back to (0,0) if we restored a saved layout above.
                if (savedAfl is null)
                {
                    _displayWindow.Left = 0;
                    _displayWindow.Top = 0;
                }

                var source = (HwndSource)PresentationSource.FromVisual(_displayWindow)!;
                source.AddHook(DisplayWndProc);
            };

            _displayWindow.Show();
            PushAllToScoreboard();
            UpdateWindowToggleVisual();

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
                var threshold = TimeSpan.FromMinutes(5);

                // Edge detection: trigger exactly when remaining crosses 5:00
                if (!_fiveMinWarningShown &&
                    _previousRemainingTime > threshold &&
                    remaining <= threshold && remaining > TimeSpan.Zero)
                {
                    _fiveMinWarningShown = true;
                    _overlayScheduler.NotifyEventDrivenOverlay();
                    _scorebug.ShowFiveMinuteWarning();
                }

                _previousRemainingTime = remaining;
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
            _scorebug.SetMarqueeMessages((System.Collections.Generic.IList<Roche_Scoreboard.Models.MarqueeMessage>)_styledMessages);
            _scorebug.SetFinalsMode(_finalsMode);
            _scorebug.SetWeatherLocation(_weatherLocation);

            PushClockToScoreboard();
        }

        private void PushClockToScoreboard()
        {
            if (_scorebug == null || _displayWindow == null || !_displayWindow.IsVisible) return;

            var dc = _match.DisplayClock;
            _scorebug.SetClock($"{(int)dc.TotalMinutes:D2}:{dc.Seconds:D2}");

            if (!_showingBreakScreen)
            {
                bool scorelessTriggered = false;
                if (_scorebug.CheckScorelessTimer())
                {
                    scorelessTriggered = true;
                    _overlayScheduler.NotifyEventDrivenOverlay();
                    // Event-driven: bypass the 90-second informational cooldown
                    // so the staleness moment lands when the viewer expects it.
                    EnqueueAutoOverlay(() => _scorebug?.ShowLastScoreTime(), isPriority: true);
                }

                // Per-team drought only fires when the game as a whole isn't
                // already flagged as scoreless — otherwise we'd show two very
                // similar overlays for the same situation.
                if (!scorelessTriggered && _scorebug.CheckAutoTeamDrought())
                {
                    _overlayScheduler.NotifyEventDrivenOverlay();
                    bool droughtIsHome = _scorebug.GetDroughtTeamIsHome();
                    EnqueueAutoOverlay(() =>
                    {
                        if (_scorebug == null) return;
                        string teamName = droughtIsHome ? _match.HomeName : _match.AwayName;
                        var color = GetTeamColor(droughtIsHome);
                        string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                        string secHex = droughtIsHome ? HomeSecondaryColorBox.Text : AwaySecondaryColorBox.Text;
                        _scorebug.ShowTeamDrought(teamName, _scorebug.GetDroughtSince(droughtIsHome), hex, secHex);
                    }, isPriority: true);
                }

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

        private static ImageSource? LoadLogoPreview(string? path)
            => Services.ImageLoadHelper.Load(path);

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
            _activeScreenKey = "scorebug";
            EnsureScoreboardWindow();
            if (_scorebug == null) return;
            SwitchToScreen(_scorebug, "Live Scorebug");
            BroadcastWebState();
        }

        private void SwitchToBreakScreen_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedPreview("presentation:summary");
            _activeScreenKey = "summary";
            EnsureScoreboardWindow();
            if (_breakScreen == null) return;
            ShowBreakScreen();
            BroadcastWebState();
        }

        private void SwitchToScoreworm_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedPreview("presentation:worm");
            _activeScreenKey = "worm";
            EnsureScoreboardWindow();
            if (_scoreworm == null) return;
            ShowScoreworm();
            BroadcastWebState();
        }

        private void SwitchToStatsScreen_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedPreview("presentation:stats");
            _activeScreenKey = "stats";
            EnsureScoreboardWindow();
            if (_statsScreen == null) return;
            ShowStatsScreen();
            BroadcastWebState();
        }

        private void SwitchToWeatherScreen_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedPreview("presentation:weather");
            _activeScreenKey = "weather";
            EnsureScoreboardWindow();
            if (_weatherScreen == null) return;
            ShowWeatherScreen();
            BroadcastWebState();
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

        /// <summary>
        /// Animates a new event log entry sliding in from the right with a fade.
        /// </summary>
        private static void AnimateLogEntry(Border entry)
        {
            entry.Opacity = 0;
            entry.RenderTransform = new TranslateTransform(24, 0);

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
            var slideIn = new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease };

            entry.BeginAnimation(OpacityProperty, fadeIn);
            ((TranslateTransform)entry.RenderTransform).BeginAnimation(TranslateTransform.XProperty, slideIn);
        }

        /// <summary>
        /// Animates the Start/Pause button and status badge when the clock state changes.
        /// Provides a scale pulse and opacity flash to confirm the state transition to the operator.
        /// </summary>
        private void AnimateClockStateChange()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var backEase = new BackEase { Amplitude = 0.2, EasingMode = EasingMode.EaseOut };

            // Status badge: quick scale pulse
            if (StatusBadge.RenderTransform is not ScaleTransform badgeSc)
            {
                StatusBadge.RenderTransformOrigin = new Point(0.5, 0.5);
                StatusBadge.RenderTransform = badgeSc = new ScaleTransform(1, 1);
            }

            badgeSc.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(1.12, 1.0, TimeSpan.FromMilliseconds(280)) { EasingFunction = backEase });
            badgeSc.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1.12, 1.0, TimeSpan.FromMilliseconds(280)) { EasingFunction = backEase });

            // Status badge: opacity flash
            StatusBadge.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0.6, 1.0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });

            // Score displays: subtle pulse to acknowledge state change is registered
            PulseElement(HomeScoreDisplay, 180);
            PulseElement(AwayScoreDisplay, 180);
        }

        /// <summary>
        /// Quick scale pulse on a text element to acknowledge a state change.
        /// </summary>
        private static void PulseElement(FrameworkElement element, int durationMs)
        {
            if (element.RenderTransform is not ScaleTransform sc)
            {
                element.RenderTransformOrigin = new Point(0.5, 0.5);
                element.RenderTransform = sc = new ScaleTransform(1, 1);
            }

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(1.04, 1.0, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = ease });
            sc.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1.04, 1.0, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = ease });
        }
    }
}

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

// WinForms colour wheel
using WinForms = System.Windows.Forms;
using DrawingColor = System.Drawing.Color;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brushes = System.Windows.Media.Brushes;
using Panel = System.Windows.Controls.Panel;
using Point = System.Windows.Point;
using MessageBox = System.Windows.MessageBox;
using AflAction = Roche_Scoreboard.Services.AflKeybindSettings.AflAction;

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

        // Custom media presentation path (video or image)
        private string? _customVideoPath;
        private bool _customMediaIsImage;
        // When true, SwitchToScreen will NOT auto-play the video — used so
        // Apply loads the video paused on its first frame for operator review.
        private bool _suppressVideoAutoPlay;

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

        // Optional control-panel-only stoppage countdown for count-up matches.
        private bool _showControlPanelCountdown;
        private bool _controlPanelCountdownRunning;
        private TimeSpan _controlPanelCountdownDuration = TimeSpan.FromMinutes(20);
        private TimeSpan _controlPanelCountdownRemaining = TimeSpan.FromMinutes(20);
        private bool _controlPanelCountdownAutoPausedClock;
        // Wall-clock gate so the countdown decrements exactly once per real second,
        // not once per rendered frame (~60×/s via CompositionTarget.Rendering).
        private DateTime _countdownNextTick = DateTime.MinValue;

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

        // Custom goal video paths. Goal-celebration MP4s now play through
        // the same LibVLCSharp _video player surface as presentation videos
        // — see PlayCustomGoalVideoIfConfigured / EnterVideoTakeover.
        private string? _homeGoalVideoPath;
        private string? _awayGoalVideoPath;
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

        private string _activeScreenKey = "scorebug";
        private Dictionary<AflAction, Key> _aflKeybinds = new();
        private AflAction? _pendingKeybindAction;
        private UxPreferencesData _uxPreferences = new();

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
            SetupWizardPanel.BackToSportSelection += ShowSetupWizard;

            // Wire up the cricket setup wizard
            CricketSetupWizardPanel.SetupCompleted += OnCricketSetupCompleted;
            CricketSetupWizardPanel.BackToSportSelection += ShowSetupWizard;

            // Populate the custom quarter / break labels in settings from
            // persisted overrides; live updates flow through QuarterLabelService.
            LoadQuarterLabelsToSettings();
            _aflKeybinds = new Dictionary<AflAction, Key>(AflKeybindSettings.LoadBindings());

            // Wire up the cricket control panel reset
            CricketControlPanelView.ResetRequested += OnCricketResetRequested;

            // Wire up sport selection
            SportSelectionPanel.SportSelected += OnSportSelected;
            GlobalSettingsPanel.CategoryRequested += OnGlobalSettingsCategoryRequested;
            GlobalSettingsPanel.BackRequested += OnGlobalSettingsBackRequested;
            GlobalSettingsPanel.StartupModeChanged += OnGlobalSettingsStartupModeChanged;

            // Sport selection visible by default; everything else hidden
            MainContent.Visibility = Visibility.Collapsed;
            SetupWizardPanel.Visibility = Visibility.Collapsed;
            CricketSetupWizardPanel.Visibility = Visibility.Collapsed;
            CricketControlPanelView.Visibility = Visibility.Collapsed;
            GlobalSettingsPanel.Visibility = Visibility.Collapsed;
            SportSelectionPanel.Visibility = Visibility.Visible;

            Closing += MainWindow_Closing;
            Loaded += MainWindow_Loaded;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            UpdateLogoCropSummaryText();
            UpdateHoverPreviewVisual(_selectedPreviewKey);
            InitSettingsWeatherLocation();
            RegisterScheduledOverlays();
            ApplyAflKeybindsToUi();
            _uxPreferences = UxPreferences.Load();
            ApplyStartupPreferenceSelectionToUi();
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

            // Only paint the VIDEO-tab live mirror while the tab is visible.
            // VisualBrush of a 1080p container is a heavy per-frame operation
            // on slower devices, so we keep it disconnected when the operator
            // is on any other tab.
            bool onVideoTab = MainTabControl?.SelectedItem is System.Windows.Controls.TabItem ti
                              && ti.Header is string h && h.Contains("VIDEO", StringComparison.OrdinalIgnoreCase);
            bool onSettingsTab = MainTabControl?.SelectedItem is System.Windows.Controls.TabItem settingsTi
                                 && settingsTi.Header is string settingsHeader && settingsHeader.Contains("SETTINGS", StringComparison.OrdinalIgnoreCase);
            if (onVideoTab)
            {
                // Make sure the scoreboard window exists so the live mirror
                // has a real visual to sample. Without this the operator
                // sees the "Open the scoreboard window" placeholder until
                // they manually trigger something on another tab.
                EnsureScoreboardWindow();
                // Finish any deferred restore load now that the operator is
                // actually on the VIDEO tab.
                HandleVideoTabActivated();
                AttachLiveMirror();
                // Hook the position event only while the operator can actually
                // see the slider/clock. A paused video still ticks the libvlc
                // event thread occasionally; gating the subscription means
                // zero UI-thread dispatches happen on any other tab.
                if (_videoPlayer is not null && _videoPlayer.HasSource)
                    EnsureVideoTabPreviewSyncTimer();
            }
            else
            {
                DetachLiveMirror();
                StopVideoTabPreviewSyncTimer();
            }

            if (onSettingsTab)
            {
                // Force true tabbed behaviour on first entry so only one
                // settings section is visible (never a continuous full page).
                ApplySettingsCategoryFilter(string.IsNullOrWhiteSpace(_uxPreferences.LastSettingsCategory)
                    ? _settingsActiveCategory
                    : _uxPreferences.LastSettingsCategory);
                UpdateSettingsControlPanelCountdownOptionVisuals();
            }

            if (MainTabControl is not null && MainTabControl.SelectedIndex >= 0)
            {
                _uxPreferences.LastMainTabIndex = MainTabControl.SelectedIndex;
                UxPreferences.Save(_uxPreferences);
            }
        }

        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Update prompt on close has been removed in favour of an in-app
            // update section in the Settings tab. Proceed straight to teardown.

            CompositionTarget.Rendering -= OnFrameRendering;
            StopAutoSaveTimer();
            SaveGameState();
            SaveAllWindowLayouts();
            _weatherService?.Dispose();
            _weatherService = null;
            _displayWindow?.Close();
            _cricketDisplayWindow?.Close();
            _trainingDisplayWindow?.Close();

            await Task.CompletedTask;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _uxPreferences = UxPreferences.Load();
            ApplyStartupPreferenceSelectionToUi();
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

            // Cloud remote session startup is wired up in MainWindow.CloudRemote.cs.
            StartCloudRemoteOnLoad();

            // Embed the AFL Scoring & Clock cloud remote launcher so the
            // operator can spin up / show the QR without flipping to
            // Settings. The Training Mode launcher is registered in
            // EnterTrainingMode() because that control is created lazily.
            RegisterCloudRemoteLauncher(ScoringTabCloudLauncher);

            // Restore last-loaded custom MP4 in the VIDEO tab (best-effort).
            TryRestoreLastVideoPath();

            // Defer alerts so the window is fully visible first
            ApplyQuickStartupNavigation();

            await Dispatcher.InvokeAsync(async () => await ShowDeferredAlerts(), System.Windows.Threading.DispatcherPriority.Background);
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
        private async Task ShowDeferredAlerts()
        {
            // Wait for the background update check kicked off in App.OnStartup
            // to finish before deciding whether to show the prompt. Without
            // this await, the check is still in flight when this method runs
            // and UpdateAvailable returns false on first launch, so the prompt
            // never appears.
            if (System.Windows.Application.Current is App startupApp)
            {
                try { await startupApp.UpdateCheckTask; } catch { }
            }

            // Refresh the in-app Settings update card so the user can act on
            // any pending update from there.
            try { RefreshUpdateStatusUi(); } catch { }

            // If a newer release is already known from the background check
            // in App.OnStartup, surface the update prompt right away so the
            // operator doesn't have to dig into Settings to find it.
            if (System.Windows.Application.Current is App app && app.UpdateService.UpdateAvailable)
            {
                if (app.PromptForUpdate())
                {
                    // Update was applied and the app is shutting down to let
                    // the external updater finish — skip the resume dialog.
                    return;
                }
            }

            bool shouldOfferResume = _uxPreferences.StartupMode == StartupExperience.ResumeIfAvailable;

            // Then check for a saved game in progress
            if (shouldOfferResume && GameStateService.HasSavedState())
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

        // ───────────────────────────────────────────────────────────────
        // QR / clipboard helpers — used by the Cloud Remote settings card
        // (see MainWindow.CloudRemote.cs). The legacy local web server,
        // live view, and SignalR command bridge have been removed.
        // ───────────────────────────────────────────────────────────────

        internal static BitmapSource GenerateQrCode(string text)
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

            if (TryHandleKeybindCapture(e.Key))
            {
                e.Handled = true;
                return;
            }

            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            bool q4Ended = _match.Quarter == 4 && _match.GetQuarterSnapshot(4) != null;

            // Number-row / NumPad shortcuts for presentation switching. Match
            // the order of the cards in the Display & Messages tab:
            //   1 Scorebug · 2 Summary · 3 Worm · 4 Stats · 5 Weather · 6 Video
            // Modifier keys are intentionally excluded so they don't collide
            // with operator shortcuts.
            if (!ctrl && !alt && !shift && TryHandlePresentationKey(e.Key))
            {
                e.Handled = true;
                return;
            }

            if (ctrl || alt || shift)
            {
                if (ctrl && e.Key == Key.Z)
                {
                    Undo_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                return;
            }

            if (MatchesKeybind(AflAction.ToggleClock, e.Key) && !q4Ended)
            {
                StartPause_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (MatchesKeybind(AflAction.HomeGoal, e.Key) && !q4Ended)
            {
                HomeGoal_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (MatchesKeybind(AflAction.HomeBehind, e.Key) && !q4Ended)
            {
                HomeBehind_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (MatchesKeybind(AflAction.AwayGoal, e.Key) && !q4Ended)
            {
                AwayGoal_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (MatchesKeybind(AflAction.AwayBehind, e.Key) && !q4Ended)
            {
                AwayBehind_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (MatchesKeybind(AflAction.EndQuarter, e.Key) && !q4Ended)
            {
                EndQuarter_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        /// <summary>
        /// Maps a top-row or NumPad digit to the matching presentation switcher.
        /// Returns <c>true</c> when the key was a known presentation digit so the
        /// caller can mark the event handled.
        /// </summary>
        private bool MatchesKeybind(AflAction action, Key key)
            => _aflKeybinds.TryGetValue(action, out Key configured) && configured == key;

        private string GetKeybindLabel(AflAction action)
            => _aflKeybinds.TryGetValue(action, out Key key)
                ? AflKeybindSettings.FormatKey(key)
                : string.Empty;

        private T? FindElement<T>(string name) where T : class
            => FindName(name) as T;

        private void ApplyAflKeybindsToUi()
        {
            var homeGoalShortcutText = FindElement<TextBlock>("HomeGoalShortcutText");
            var homeBehindShortcutText = FindElement<TextBlock>("HomeBehindShortcutText");
            var awayGoalShortcutText = FindElement<TextBlock>("AwayGoalShortcutText");
            var awayBehindShortcutText = FindElement<TextBlock>("AwayBehindShortcutText");
            var startPauseShortcutText = FindElement<TextBlock>("StartPauseShortcutText");
            var keybindToggleClockButton = FindElement<System.Windows.Controls.Button>("KeybindToggleClockButton");
            var keybindHomeGoalButton = FindElement<System.Windows.Controls.Button>("KeybindHomeGoalButton");
            var keybindHomeBehindButton = FindElement<System.Windows.Controls.Button>("KeybindHomeBehindButton");
            var keybindAwayGoalButton = FindElement<System.Windows.Controls.Button>("KeybindAwayGoalButton");
            var keybindAwayBehindButton = FindElement<System.Windows.Controls.Button>("KeybindAwayBehindButton");
            var keybindEndQuarterButton = FindElement<System.Windows.Controls.Button>("KeybindEndQuarterButton");

            string toggleClockLabel = GetKeybindLabel(AflAction.ToggleClock);
            string homeGoalLabel = GetKeybindLabel(AflAction.HomeGoal);
            string homeBehindLabel = GetKeybindLabel(AflAction.HomeBehind);
            string awayGoalLabel = GetKeybindLabel(AflAction.AwayGoal);
            string awayBehindLabel = GetKeybindLabel(AflAction.AwayBehind);
            string endQuarterLabel = GetKeybindLabel(AflAction.EndQuarter);

            if (homeGoalShortcutText is not null) homeGoalShortcutText.Text = $" {homeGoalLabel}";
            if (homeBehindShortcutText is not null) homeBehindShortcutText.Text = $" {homeBehindLabel}";
            if (awayGoalShortcutText is not null) awayGoalShortcutText.Text = $" {awayGoalLabel}";
            if (awayBehindShortcutText is not null) awayBehindShortcutText.Text = $" {awayBehindLabel}";
            if (startPauseShortcutText is not null) startPauseShortcutText.Text = toggleClockLabel;

            EndQtrButton.ToolTip = $"End the current quarter — {endQuarterLabel}";
            HomeGoalButton.ToolTip = $"Goal (+6 points) — {homeGoalLabel}";
            HomeBehindButton.ToolTip = $"Behind (+1 point) — {homeBehindLabel}";
            AwayGoalButton.ToolTip = $"Goal (+6 points) — {awayGoalLabel}";
            AwayBehindButton.ToolTip = $"Behind (+1 point) — {awayBehindLabel}";
            StartPauseButton.ToolTip = $"Start the quarter, or control stoppage timing during count-up matches — {toggleClockLabel}";

            if (keybindToggleClockButton is not null) keybindToggleClockButton.Content = toggleClockLabel;
            if (keybindHomeGoalButton is not null) keybindHomeGoalButton.Content = homeGoalLabel;
            if (keybindHomeBehindButton is not null) keybindHomeBehindButton.Content = homeBehindLabel;
            if (keybindAwayGoalButton is not null) keybindAwayGoalButton.Content = awayGoalLabel;
            if (keybindAwayBehindButton is not null) keybindAwayBehindButton.Content = awayBehindLabel;
            if (keybindEndQuarterButton is not null) keybindEndQuarterButton.Content = endQuarterLabel;
        }

        private void BeginKeybindCapture(AflAction action)
        {
            _pendingKeybindAction = action;
            var keybindHintText = FindElement<TextBlock>("KeybindHintText");
            if (keybindHintText is not null)
            {
                keybindHintText.Text = $"Press a new key for {action}. Esc cancels.";
                keybindHintText.Foreground = FindResource("AccentBrush") as SolidColorBrush ?? Brushes.White;
            }
        }

        private void FinishKeybindCapture(string message, System.Windows.Media.Brush? foreground = null)
        {
            _pendingKeybindAction = null;
            var keybindHintText = FindElement<TextBlock>("KeybindHintText");
            if (keybindHintText is not null)
            {
                keybindHintText.Text = message;
                keybindHintText.Foreground = foreground ?? (FindResource("TextDimBrush") as SolidColorBrush ?? Brushes.White);
            }
        }

        private bool TryHandleKeybindCapture(Key key)
        {
            if (_pendingKeybindAction is not AflAction action)
            {
                return false;
            }

            if (key == Key.Escape)
            {
                FinishKeybindCapture("Keybind change cancelled.");
                return true;
            }

            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            {
                return true;
            }

            foreach (var pair in _aflKeybinds)
            {
                if (pair.Key != action && pair.Value == key)
                {
                    FinishKeybindCapture($"{AflKeybindSettings.FormatKey(key)} is already used for {pair.Key}.", PausedAmberBrush);
                    return true;
                }
            }

            _aflKeybinds[action] = key;
            AflKeybindSettings.SaveBindings(_aflKeybinds);
            ApplyAflKeybindsToUi();
            UpdateLiveUI();
            FinishKeybindCapture($"{action} is now bound to {AflKeybindSettings.FormatKey(key)}.", LiveGreenBrush);
            return true;
        }

        private void KeybindToggleClockButton_Click(object sender, RoutedEventArgs e)
            => BeginKeybindCapture(AflAction.ToggleClock);

        private void KeybindHomeGoalButton_Click(object sender, RoutedEventArgs e)
            => BeginKeybindCapture(AflAction.HomeGoal);

        private void KeybindHomeBehindButton_Click(object sender, RoutedEventArgs e)
            => BeginKeybindCapture(AflAction.HomeBehind);

        private void KeybindAwayGoalButton_Click(object sender, RoutedEventArgs e)
            => BeginKeybindCapture(AflAction.AwayGoal);

        private void KeybindAwayBehindButton_Click(object sender, RoutedEventArgs e)
            => BeginKeybindCapture(AflAction.AwayBehind);

        private void KeybindEndQuarterButton_Click(object sender, RoutedEventArgs e)
            => BeginKeybindCapture(AflAction.EndQuarter);

        private bool TryHandlePresentationKey(Key key)
        {
            int slot = key switch
            {
                Key.D1 or Key.NumPad1 => 1,
                Key.D2 or Key.NumPad2 => 2,
                Key.D3 or Key.NumPad3 => 3,
                Key.D4 or Key.NumPad4 => 4,
                Key.D5 or Key.NumPad5 => 5,
                Key.D6 or Key.NumPad6 => 6,
                _ => 0,
            };

            if (slot == 0) return false;

            var args = new RoutedEventArgs();
            switch (slot)
            {
                case 1:
                    SwitchToScorebug_Click(this, args);
                    UpdatePresentationCards("scorebug");
                    break;
                case 2:
                    SwitchToBreakScreen_Click(this, args);
                    UpdatePresentationCards("summary");
                    break;
                case 3:
                    SwitchToScoreworm_Click(this, args);
                    UpdatePresentationCards("worm");
                    break;
                case 4:
                    SwitchToStatsScreen_Click(this, args);
                    UpdatePresentationCards("stats");
                    break;
                case 5:
                    SwitchToWeatherScreen_Click(this, args);
                    UpdatePresentationCards("weather");
                    break;
                case 6:
                    SwitchToVideo_Click(this, args);
                    UpdatePresentationCards("video");
                    break;
            }

            return true;
        }

        // Toggles the calibration overlay on the display window. Bound to
        // Ctrl+Shift+C so the operator can quickly verify the output window
        // matches the mirrored capture region of the outdoor scoreboard.
        private void DisplayWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

            if (ctrl && shift && e.Key == Key.C && _calibrationOverlay is not null)
            {
                _calibrationOverlay.Visibility = _calibrationOverlay.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                e.Handled = true;
                return;
            }

            // Ctrl+Shift+T toggles the persisted "opaque display windows"
            // preference. AllowsTransparency cannot be changed after a window
            // is shown, so we save the flip and prompt the operator to
            // reopen the display window so the new rendering mode takes
            // effect on the scoreboard PC. This is the runtime A/B switch
            // for diagnosing layered-transparency framerate drops.
            if (ctrl && shift && e.Key == Key.T)
            {
                bool newValue = !DisplaySettings.OpaqueDisplayWindows;
                DisplaySettings.OpaqueDisplayWindows = newValue;
                e.Handled = true;

                MessageBox.Show(
                    sender as Window ?? this,
                    newValue
                        ? "Display windows will use OPAQUE rendering (no transparency).\n\nClose and reopen the display window for this to take effect."
                        : "Display windows will use TRANSPARENT rendering.\n\nClose and reopen the display window for this to take effect.",
                    "Display rendering mode",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        // ----------------------------
        // Clock — fires every frame via CompositionTarget.Rendering
        // ----------------------------
        private int _lastDisplayedSecond = -1;

        private void OnFrameRendering(object? sender, EventArgs e)
        {
            _match.Tick();   // handles countdown expiry only

            int scorebugSecond = (int)_match.DisplayClock.TotalSeconds;
            bool scorebugSecondChanged = scorebugSecond != _lastDisplayedSecond;
            if (scorebugSecondChanged)
            {
                _lastDisplayedSecond = scorebugSecond;
            }

            bool countdownSecondChanged = false;
            if (_controlPanelCountdownRunning && _controlPanelCountdownRemaining > TimeSpan.Zero)
            {
                // Gate on real wall-clock time and consume every elapsed full second
                // to avoid drift if a frame arrives late.
                var now = DateTime.UtcNow;
                if (_countdownNextTick == DateTime.MinValue)
                    _countdownNextTick = now.AddSeconds(1);

                if (now >= _countdownNextTick)
                {
                    int elapsedSeconds = (int)Math.Floor((now - _countdownNextTick).TotalSeconds) + 1;
                    _countdownNextTick = _countdownNextTick.AddSeconds(elapsedSeconds);

                    _controlPanelCountdownRemaining -= TimeSpan.FromSeconds(elapsedSeconds);
                    if (_controlPanelCountdownRemaining < TimeSpan.Zero)
                        _controlPanelCountdownRemaining = TimeSpan.Zero;

                    countdownSecondChanged = true;

                    if (_controlPanelCountdownRemaining == TimeSpan.Zero)
                    {
                        _controlPanelCountdownRunning = false;
                        _countdownNextTick = DateTime.MinValue;
                        if (_match.ClockRunning)
                        {
                            _controlPanelCountdownAutoPausedClock = true;
                            _match.PauseClock();
                        }
                    }
                }
            }
            else if (!_controlPanelCountdownRunning)
            {
                // Reset the gate whenever the countdown is not running so it
                // starts fresh (first tick 1 s after resume) on next start.
                _countdownNextTick = DateTime.MinValue;
            }

            if (!scorebugSecondChanged && !countdownSecondChanged)
            {
                return;
            }

            if (scorebugSecondChanged)
            {
                PushClockToScoreboard();

                // Update just the clock portion of the control panel
                var dc = _match.DisplayClock;
                HeaderQuarterClock.Text = $"Q{_match.Quarter}  {(int)dc.TotalMinutes:D2}:{dc.Seconds:D2}";

                CheckPeriodicOverlays();
                BroadcastWebState();
            }

            if (countdownSecondChanged || scorebugSecondChanged)
            {
                UpdateControlPanelCountdownUi();
            }
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

            bool controlsStoppageCountdownOnly = _match.ClockMode == ClockMode.CountUp
                && _showControlPanelCountdown
                && _match.ClockRunning
                && _match.ElapsedInQuarter.TotalSeconds >= 2;

            // Start/Pause button — uses cached brushes to avoid per-frame allocation
            if (q4Ended)
            {
                StartPauseButton.Content = "Full Time";
                StartPauseButton.Background = FullTimeBtnBg;
                StartPauseButton.BorderBrush = FullTimeBtnBorder;
            }
            else if (controlsStoppageCountdownOnly)
            {
                if (_controlPanelCountdownRunning)
                {
                    StartPauseButton.Content = "⏸  Pause Stoppage";
                    StartPauseButton.Background = PauseBtnBg;
                    StartPauseButton.BorderBrush = PauseBtnBorder;
                }
                else
                {
                    StartPauseButton.Content = "▶  Start Stoppage";
                    StartPauseButton.Background = StartBtnBg;
                    StartPauseButton.BorderBrush = StartBtnBorder;
                }
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
            ApplyAflKeybindsToUi();
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
            GlobalSettingsPanel.Visibility = Visibility.Collapsed;
            SportSelectionPanel.Opacity = 1;
            SportSelectionPanel.Visibility = Visibility.Visible;
        }

        private void OnSportSelected(SportMode mode)
        {
            _sportMode = mode;
            PersistLastMode(mode);
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
            else if (mode == SportMode.Settings)
            {
                MainContent.Visibility = Visibility.Collapsed;
                SetupWizardPanel.Visibility = Visibility.Collapsed;
                CricketSetupWizardPanel.Visibility = Visibility.Collapsed;
                CricketControlPanelView.Visibility = Visibility.Collapsed;
                TrainingControlPanelView.Visibility = Visibility.Collapsed;
                GlobalSettingsPanel.Visibility = Visibility.Visible;
                Title = $"Roche Scoreboard v{AutoUpdateService.CurrentVersion.ToString(3)} | Global Settings";
            }
            else
            {
                EnterTrainingMode();
            }
        }

        private void OnGlobalSettingsCategoryRequested(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return;

            _uxPreferences.LastSettingsCategory = category;
            UxPreferences.Save(_uxPreferences);

            GlobalSettingsPanel.Visibility = Visibility.Collapsed;
            MainContent.Visibility = Visibility.Visible;
            SetupWizardPanel.Visibility = Visibility.Collapsed;
            CricketSetupWizardPanel.Visibility = Visibility.Collapsed;
            CricketControlPanelView.Visibility = Visibility.Collapsed;
            TrainingControlPanelView.Visibility = Visibility.Collapsed;

            if (MainTabControl is not null)
                MainTabControl.SelectedIndex = 3;

            ApplySettingsCategoryFilter(category);
            UpdateSettingsControlPanelCountdownOptionVisuals();
            Title = $"Roche Scoreboard v{AutoUpdateService.CurrentVersion.ToString(3)} | Settings";
        }

        private void OnGlobalSettingsBackRequested()
        {
            PersistLastMode(SportMode.Settings);
            GlobalSettingsPanel.Visibility = Visibility.Collapsed;
            MainContent.Visibility = Visibility.Collapsed;
            SetupWizardPanel.Visibility = Visibility.Collapsed;
            CricketSetupWizardPanel.Visibility = Visibility.Collapsed;
            CricketControlPanelView.Visibility = Visibility.Collapsed;
            TrainingControlPanelView.Visibility = Visibility.Collapsed;
            SportSelectionPanel.Opacity = 1;
            SportSelectionPanel.Visibility = Visibility.Visible;
            Title = $"Roche Scoreboard v{AutoUpdateService.CurrentVersion.ToString(3)}";
        }

        private void OnGlobalSettingsStartupModeChanged(string mode)
        {
            _uxPreferences.StartupMode = mode switch
            {
                "sportSelection" => StartupExperience.SportSelection,
                "lastMode" => StartupExperience.LastMode,
                _ => StartupExperience.ResumeIfAvailable,
            };
            UxPreferences.Save(_uxPreferences);
        }

        private void ApplyStartupPreferenceSelectionToUi()
        {
            string mode = _uxPreferences.StartupMode switch
            {
                StartupExperience.SportSelection => "sportSelection",
                StartupExperience.LastMode => "lastMode",
                _ => "resumeIfAvailable",
            };
            GlobalSettingsPanel.SetStartupModeSelection(mode);
        }

        private void PersistLastMode(SportMode mode)
        {
            _uxPreferences.LastMode = mode switch
            {
                SportMode.AFL => PersistedMode.Afl,
                SportMode.Cricket => PersistedMode.Cricket,
                SportMode.Training => PersistedMode.Training,
                SportMode.Settings => PersistedMode.GlobalSettings,
                _ => PersistedMode.SportSelection,
            };
            UxPreferences.Save(_uxPreferences);
        }

        private void ApplyQuickStartupNavigation()
        {
            if (_uxPreferences.StartupMode != StartupExperience.LastMode)
                return;

            switch (_uxPreferences.LastMode)
            {
                case PersistedMode.Afl:
                    OnSportSelected(SportMode.AFL);
                    break;
                case PersistedMode.Cricket:
                    OnSportSelected(SportMode.Cricket);
                    break;
                case PersistedMode.Training:
                    OnSportSelected(SportMode.Training);
                    break;
                case PersistedMode.GlobalSettings:
                    OnSportSelected(SportMode.Settings);
                    break;
                default:
                    SportSelectionPanel.Visibility = Visibility.Visible;
                    break;
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

            // Mirror the AFL Scoring & Clock launcher onto the Training
            // header so operators can start / share the cloud remote
            // without leaving training mode.
            RegisterCloudRemoteLauncher(TrainingControlPanelView.TrainingCloudLauncher);

            // Hook the cloud publisher into structural session changes so
            // the phone queue + active timer stay in lock-step with the
            // desktop. Tick-driven countdown updates ride on the normal
            // throttled snapshot publish.
            AttachTrainingCloudBridge(_trainingSession);

            // Drive the session's countdown from a UI tick
            _trainingTickTimer?.Stop();
            _trainingTickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _trainingTickTimer.Tick += (_, _) =>
            {
                _trainingSession?.Tick();
                // The publisher is internally throttled to ~10/s, so it's
                // safe to call on the 30 fps tick.
                if (_trainingSession is not null) PublishCloudSnapshot();
            };
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
                // Match the AFL display window: opaque, no layered transparency,
                // so Windows 11 doesn't draw rounded corners or a faint border
                // around the client area.
                AllowsTransparency = false,
                ResizeMode = ResizeMode.CanResize,
                Topmost = true,
                ShowInTaskbar = false,
                Background = System.Windows.Media.Brushes.Black,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Width = 386,
                Height = 193,
                Left = 0,
                Top = 0,
                Content = container
            };

            // Collapse the non-client frame entirely so the training scorebug
            // sits flush in the top-left with sharp corners and no border —
            // identical to the AFL / Cricket display window styling.
            System.Windows.Shell.WindowChrome.SetWindowChrome(_trainingDisplayWindow, new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 0,
                GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });

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

            if (_trainingSession is not null)
            {
                DetachTrainingCloudBridge(_trainingSession);
            }

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

        private void ShowMatchLoadingScreen(
            string homeName, string homeAbbr, string? homeLogo, string? homePrimaryColor,
            string awayName, string awayAbbr, string? awayLogo, string? awayPrimaryColor,
            string subtitle,
            IReadOnlyList<string> steps,
            FrameworkElement fadeOutWizard,
            Action onReady)
        {
            MatchLoadingPanel.ConfigureTeams(
                homeName, homeAbbr, homeLogo, homePrimaryColor,
                awayName, awayAbbr, awayLogo, awayPrimaryColor);
            MatchLoadingPanel.SetBrandSubtitle(subtitle);

            MatchLoadingPanel.Opacity = 0;
            MatchLoadingPanel.Visibility = Visibility.Visible;

            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            MatchLoadingPanel.BeginAnimation(OpacityProperty, fadeIn);

            // Fade the wizard panel out softly so the loader takes over the visual stage.
            if (fadeOutWizard is not null)
            {
                var wizardFade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
                wizardFade.Completed += (_, _) =>
                {
                    fadeOutWizard.Visibility = Visibility.Collapsed;
                    fadeOutWizard.Opacity = 1;
                };
                fadeOutWizard.BeginAnimation(OpacityProperty, wizardFade);
            }

            Action? finishedHandler = null;
            bool readyInvoked = false;
            finishedHandler = () =>
            {
                MatchLoadingPanel.LoadingFinished -= finishedHandler;

                // Fade the loader out first, THEN run the (potentially heavy)
                // match-startup work. Running it before the fade was making
                // the dispatcher block on the final step and the "READY"
                // beat, which looked like the loader was stuck at 100%.
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
                };
                fadeOut.Completed += (_, _) =>
                {
                    MatchLoadingPanel.Visibility = Visibility.Collapsed;
                    MatchLoadingPanel.Opacity = 1;

                    if (readyInvoked) return;
                    readyInvoked = true;

                    // Defer the heavy work one dispatcher beat so WPF can
                    // actually commit the collapsed visibility / wizard
                    // teardown before we start hammering the UI thread.
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { onReady(); }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[MatchLoading] onReady error: {ex.Message}");
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                };
                MatchLoadingPanel.BeginAnimation(OpacityProperty, fadeOut);
            };
            MatchLoadingPanel.LoadingFinished += finishedHandler;

            MatchLoadingPanel.StartLoading(steps, TimeSpan.FromMilliseconds(2600));
        }

        private void OnSetupCompleted(SetupResult result)
        {
            FinalizeAflSetup(result);
        }

        private void FinalizeAflSetup(SetupResult result)
        {
            ApplySetupResult(result);

            MainContent.BeginAnimation(OpacityProperty, null);
            SetupWizardPanel.BeginAnimation(OpacityProperty, null);

            // Animate wizard out and main content in
            MainContent.Opacity = 0;
            MainContent.RenderTransform = new TranslateTransform(0, 20);
            MainContent.Visibility = Visibility.Visible;

            var sb = new Storyboard();

            var wizardFade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.25))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            Storyboard.SetTarget(wizardFade, SetupWizardPanel);
            Storyboard.SetTargetProperty(wizardFade, new PropertyPath(OpacityProperty));
            sb.Children.Add(wizardFade);

            var contentFade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.35))
            {
                BeginTime = TimeSpan.FromSeconds(0.05),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(contentFade, MainContent);
            Storyboard.SetTargetProperty(contentFade, new PropertyPath(OpacityProperty));
            sb.Children.Add(contentFade);

            var contentSlide = new DoubleAnimation(20, 0, TimeSpan.FromSeconds(0.35))
            {
                BeginTime = TimeSpan.FromSeconds(0.05),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(contentSlide, MainContent);
            Storyboard.SetTargetProperty(contentSlide, new PropertyPath("RenderTransform.(TranslateTransform.Y)"));
            sb.Children.Add(contentSlide);

            sb.Completed += (_, __) =>
            {
                SetupWizardPanel.Visibility = Visibility.Collapsed;
                SetupWizardPanel.Opacity = 1;
                MainContent.Opacity = 1;
                MainContent.RenderTransform = null;
                Activate();
                Focus();
                Keyboard.Focus(this);
            };
            sb.Begin();

            EnsureScoreboardWindow();
            if (_displayWindow is { IsVisible: false }) _displayWindow.Show();
            PushAllToScoreboard();
        }

        // ----------------------------
        // Cricket setup + display
        // ----------------------------
        private void OnCricketSetupCompleted(CricketSetupResult result)
        {
            ShowMatchLoadingScreen(
                homeName: result.TeamAName,
                homeAbbr: result.TeamAAbbr,
                homeLogo: result.TeamALogoPath,
                homePrimaryColor: result.TeamAPrimaryColor,
                awayName: result.TeamBName,
                awayAbbr: result.TeamBAbbr,
                awayLogo: result.TeamBLogoPath,
                awayPrimaryColor: result.TeamBPrimaryColor,
                subtitle: "PREPARING CRICKET BROADCAST",
                steps: new[]
                {
                    "Calibrating match engine",
                    "Loading squads and logos",
                    "Configuring format and overs",
                    "Mounting marquee feed",
                    "Spinning up scorebug",
                    "Linking cricket display",
                    "Setting innings and toss"
                },
                fadeOutWizard: CricketSetupWizardPanel,
                onReady: () => FinalizeCricketSetup(result));
        }

        private void FinalizeCricketSetup(CricketSetupResult result)
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
                AllowsTransparency = !DisplaySettings.OpaqueDisplayWindows,
                ResizeMode = ResizeMode.CanResize,
                Topmost = true,
                ShowInTaskbar = false,
                Background = DisplaySettings.OpaqueDisplayWindows
                    ? (System.Windows.Media.Brush)System.Windows.Media.Brushes.Black
                    : System.Windows.Media.Brushes.Transparent,
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
            if (!string.IsNullOrWhiteSpace(_homeGoalVideoPath)) _ = Services.VideoOptimizer.EnsureOptimisedAsync(_homeGoalVideoPath);
            if (!string.IsNullOrWhiteSpace(_awayGoalVideoPath)) _ = Services.VideoOptimizer.EnsureOptimisedAsync(_awayGoalVideoPath);

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

            _showControlPanelCountdown = r.ClockMode == ClockMode.CountUp && r.ShowControlPanelCountdown;
            _controlPanelCountdownRunning = false;
            _controlPanelCountdownDuration = TimeSpan.FromMinutes(r.ControlPanelCountdownMinutes) + TimeSpan.FromSeconds(r.ControlPanelCountdownSeconds);
            if (_controlPanelCountdownDuration <= TimeSpan.Zero)
            {
                _controlPanelCountdownDuration = TimeSpan.FromMinutes(Math.Max(1, r.QuarterMinutes)) + TimeSpan.FromSeconds(Math.Max(0, r.QuarterSeconds));
            }
            _controlPanelCountdownRemaining = _controlPanelCountdownDuration;
            _controlPanelCountdownAutoPausedClock = false;
            UpdateClockStatus();

            _messages.Clear();
            _styledMessages.Clear();
            foreach (var msg in r.Messages)
            {
                _messages.Add(msg.Text ?? string.Empty);
                _styledMessages.Add(new Roche_Scoreboard.Models.MarqueeMessage(msg.Text ?? string.Empty, msg.TextColor, msg.HighlightColor));
            }
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
                ShowControlPanelCountdown = _showControlPanelCountdown,
                ControlPanelCountdownDurationSeconds = (int)Math.Max(0, _controlPanelCountdownDuration.TotalSeconds),
                ControlPanelCountdownRemainingSeconds = (int)Math.Max(0, _controlPanelCountdownRemaining.TotalSeconds),
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
            if (!string.IsNullOrWhiteSpace(_homeGoalVideoPath)) _ = Services.VideoOptimizer.EnsureOptimisedAsync(_homeGoalVideoPath);
            if (!string.IsNullOrWhiteSpace(_awayGoalVideoPath)) _ = Services.VideoOptimizer.EnsureOptimisedAsync(_awayGoalVideoPath);

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
            _showControlPanelCountdown = state.ShowControlPanelCountdown && _match.ClockMode == ClockMode.CountUp;
            _controlPanelCountdownRunning = false;
            _controlPanelCountdownDuration = state.ControlPanelCountdownDurationSeconds > 0
                ? TimeSpan.FromSeconds(state.ControlPanelCountdownDurationSeconds)
                : _match.QuarterDuration;
            _controlPanelCountdownRemaining = state.ControlPanelCountdownRemainingSeconds > 0
                ? TimeSpan.FromSeconds(state.ControlPanelCountdownRemainingSeconds)
                : _controlPanelCountdownDuration;
            _controlPanelCountdownAutoPausedClock = false;
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
            _breakScreen.SetFinalsMode(_finalsMode);
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

            // _videoPlayer is the sole MP4 surface: goal videos and
            // presentation videos both route through it. Push it above the
            // scorebug/other screens so toggling Visibility=Visible cleanly
            // takes over the output window.
            Panel.SetZIndex(_videoPlayer, 200);

            // The live VisualBrush mirror is attached lazily when the operator
            // opens the VIDEO tab — see MainTabControl_SelectionChanged. A
            // permanent attachment forces WPF to re-rasterise the entire
            // scoreboard for every frame regardless of whether anyone is
            // looking at the preview, which is the dominant background cost
            // on lower-spec devices.

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

            // VLC's VideoView embeds a native HWND via HwndHost. Layered
            // (AllowsTransparency=true) WPF windows cannot host child HWNDs,
            // so libvlc falls back to creating its own top-level window for
            // the video — which is the "separate VLC window" the operator
            // was seeing on Play. Forcing the AFL display window opaque
            // keeps the video surface embedded in the top-left display
            // window where it belongs, and also restores GPU-accelerated
            // composition (the existing comment block below already
            // recommends this for MP4 playback). Persist the choice so the
            // Ctrl+Shift+T toggle stays in sync.
            if (!DisplaySettings.OpaqueDisplayWindows)
            {
                DisplaySettings.OpaqueDisplayWindows = true;
            }

            _displayWindow = new Window
            {
                Title = $"Roche Scoreboard v{AutoUpdateService.CurrentVersion.ToString(3)} | Display",
                WindowStyle = WindowStyle.None,
                // AllowsTransparency must stay false here: libvlc's VideoView
                // hosts a child HWND for the video surface and child HWNDs
                // cannot render inside a layered WPF window. Leaving this
                // true caused VLC to spawn its own floating window for
                // playback instead of drawing into the scoreboard surface.
                AllowsTransparency = false,
                ResizeMode = ResizeMode.CanResize,
                Topmost = true,
                ShowInTaskbar = false,
                Background = Brushes.Black,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Width = 386,
                Height = 193,
                Left = 0,
                Top = 0,
                Content = _displayContainer
            };

            // Strip the non-client frame entirely. WindowStyle=None on a
            // non-layered window still leaves Windows 11 drawing a thin
            // border + rounded corners around the client area; applying a
            // WindowChrome with zero glass, zero caption, zero border and
            // zero corner radius collapses the non-client area to nothing
            // so the scorebug sits flush in the top-left with no bars,
            // gaps or curves. The existing WM_NCHITTEST hook continues to
            // provide edge resizing.
            System.Windows.Shell.WindowChrome.SetWindowChrome(_displayWindow, new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 0,
                GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });

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
                ExitVideoTakeover();
                _videoPlayer?.Stop();
                DetachLiveMirror();
                _displayWindow = null;
                _scorebug = null;
                _breakScreen = null;
                _scoreworm = null;
                _statsScreen = null;
                _videoPlayer = null;
                _weatherScreen = null;
                _retroCrtOverlay = null;
                _calibrationOverlay = null;
                _displayContainer = null;
                _showingBreakScreen = false;
                UpdateWindowToggleVisual();
            };

            HookDimensionMirror(_displayWindow);

            // Hard opt-out from Windows 11 DWM rounded corners + accent border.
            // Without this, WindowStyle.None windows get curved edges and a
            // 1px coloured border on some machines (notably the scoreboard PC).
            Services.WindowChromeEnforcer.Attach(_displayWindow);

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

            if (!IsScoreAnimationActive && DateTime.UtcNow >= _deferScorePushUntilUtc)
            {
                _scorebug.SetScores(_match.HomeGoals, _match.HomeBehinds, _match.AwayGoals, _match.AwayBehinds);
            }

            // 5-minute warning edge detection lives in PushClockToScoreboard
            // (which runs every second from CompositionTarget.Rendering).
            // Doing it here would only fire on MatchChanged — i.e. score
            // events — so a quiet stretch between 5:01 and the next score
            // would delay the warning until well past the 5-minute mark.

            _scorebug.SetTeamColors(
                HomeColorBox.Text, AwayColorBox.Text,
                HomeSecondaryColorBox.Text, AwaySecondaryColorBox.Text);
            _scorebug.SetLogos(_homeLogoPath, _awayLogoPath);
            ApplyLogoCropToScorebug();
            _scorebug.SetMarqueeMessages((System.Collections.Generic.IList<Roche_Scoreboard.Models.MarqueeMessage>)_styledMessages);
            _scorebug.SetFinalsMode(_finalsMode);
            _scorebug.SetHideTeamNames(_hideTeamNames);
            _scorebug.SetWeatherLocation(_weatherLocation);

            PushClockToScoreboard();
        }

        private void PushClockToScoreboard()
        {
            if (_scorebug == null || _displayWindow == null || !_displayWindow.IsVisible) return;

            var dc = _match.DisplayClock;
            _scorebug.SetClock($"{(int)dc.TotalMinutes:D2}:{dc.Seconds:D2}");

            // Fire the 5-minute warning the instant the countdown ticks through
            // 5:00. This runs every second via OnFrameRendering so a quiet game
            // (no goals/behinds) can never delay the trigger past the mark.
            CheckFiveMinuteWarning(dc);

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

        /// <summary>
        /// Fires the 5-minute warning the moment the countdown clock first
        /// reads &lt;= 5:00. The previous remaining time is tracked across
        /// ticks so we only trigger on the falling edge through the 5:00
        /// threshold — not on every subsequent tick while we're inside the
        /// window.
        /// <para>
        /// Lives on the per-frame path (called from <see cref="PushClockToScoreboard"/>)
        /// rather than the match-changed path so a quiet stretch between
        /// 5:01 and the next score event can't delay the warning past the
        /// 5-minute mark.
        /// </para>
        /// </summary>
        private void CheckFiveMinuteWarning(TimeSpan remaining)
        {
            if (_scorebug == null) return;

            if (_match.ClockMode == ClockMode.Countdown && _match.ClockRunning)
            {
                var threshold = TimeSpan.FromMinutes(5);

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
            else if (_fiveMinWarningShown)
            {
                // Clock paused or mode flipped away from countdown — reset so
                // the warning can re-arm if countdown resumes.
                _fiveMinWarningShown = false;
                _scorebug.HideFiveMinuteWarning();
            }
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
            // The dedicated VIDEO tab now owns the upload / playback workflow.
            // This handler remains so legacy entry points (web control, setup
            // wizard, sport switching) can still route a "show video" request
            // to the scoreboard window when a clip is already loaded.
            SetSelectedPreview("presentation:video");
            EnsureScoreboardWindow();
            if (_videoPlayer is null) return;
            if (!_videoPlayer.HasSource) return;
            SwitchToScreen(_videoPlayer, "Custom Video");
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

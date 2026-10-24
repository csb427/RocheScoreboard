using RocheScoreboard.Mac.Production;
// Generated from MainWindow.xaml.cs; edit translator/adapters.
// Source SHA-256: ac0ec10c4e26a27541dffa36e669ff7d83544773fc07adf38e39fa7ae4c1cf61
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
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Media.Imaging;

using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;

// WinForms colour wheel

using DrawingColor = System.Drawing.Color;
using Color = Avalonia.Media.Color;
using ColorConverter = RocheScoreboard.Mac.Native.ColorConverter;
using Brushes = Avalonia.Media.Brushes;
using Panel = Avalonia.Controls.Panel;
using Point = Avalonia.Point;
using MessageBox = RocheScoreboard.Mac.Native.NativeMessageBox;
using AflAction = Roche_Scoreboard.Services.AflKeybindSettings.AflAction;

namespace Roche_Scoreboard
{
    public partial class MainWindow : NativeWindow
    {
        private readonly MatchManager _match = new();
        private NativeWindow? _displayWindow;
        private ScorebugControl? _scorebug;
        private OutputScreensaverControl? _outputScreensaver;
        private QuarterBreakScorebugControl? _quarterBreakScorebug;
        private BreakScreenControl? _breakScreen;
        private ScorewormControl? _scoreworm;
        private StatsScreenControl? _statsScreen;
        private VideoPlayerControl? _videoPlayer;
        private WeatherScreenControl? _weatherScreen;
        private Grid? _displayContainer;
        private bool _showingBreakScreen;
        private bool _aflMatchReady;
        private bool _screensaverEnabled = true;
        private string? _screensaverMediaPath;

        // Custom media presentation path (video or image)
        private string? _customVideoPath;
        private bool _customMediaIsImage;
        // When true, SwitchToScreen will NOT auto-play the video — used so
        // Apply loads the video paused on its first frame for operator review.

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
        private DispatcherTimer? _deferredGameStateSaveTimer;
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
        // not once per rendered frame (~60×/s via NativeRendering.Rendering).
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
        private int _screenTransitionVersion;
        private Storyboard? _presentationTransitionStoryboard;

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
        private NativeWindow? _cricketDisplayWindow;

        // Training mode (intervals / stopwatches)
        private TrainingSession? _trainingSession;
        private TrainingScorebugControl? _trainingScorebug;
        private NativeWindow? _trainingDisplayWindow;
        private DispatcherTimer? _trainingTickTimer;

        private string _activeScreenKey = "scorebug";
        private Dictionary<AflAction, Key> _aflKeybinds = new();
        private AflAction? _pendingKeybindAction;
        private UxPreferencesData _uxPreferences = new();
        private bool _suppressNavigationPersistence = true;
        private bool _initializingGoalAnimation = true;
        private DateTime _navigationPointerGuardUntilUtc = DateTime.MinValue;
        private DispatcherTimer? _uxPreferencesSaveTimer;
        private DispatcherTimer? _outputDimensionsSaveTimer;
        private int _pendingOutputWidth;
        private int _pendingOutputHeight;
        private bool? _compactTabLayout;

        private void QueueUxPreferencesSave()
        {
            _uxPreferencesSaveTimer?.Stop();
            _uxPreferencesSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _uxPreferencesSaveTimer.Tick += (_, _) =>
            {
                _uxPreferencesSaveTimer?.Stop();
                _uxPreferencesSaveTimer = null;
                TrySaveUxPreferences();
            };
            _uxPreferencesSaveTimer.Start();
        }

        private void TrySaveUxPreferences()
        {
            try { UxPreferences.Save(_uxPreferences); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"UX preference save skipped: {ex.Message}"); }
        }

        private void FlushUxPreferencesSave()
        {
            bool pending = _uxPreferencesSaveTimer is not null;
            _uxPreferencesSaveTimer?.Stop();
            _uxPreferencesSaveTimer = null;
            if (pending) TrySaveUxPreferences();
        }

        private void QueueOutputDimensionsSave(int width, int height)
        {
            _pendingOutputWidth = Math.Clamp(width, 200, 7680);
            _pendingOutputHeight = Math.Clamp(height, 100, 4320);
            _outputDimensionsSaveTimer?.Stop();
            _outputDimensionsSaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _outputDimensionsSaveTimer.Tick -= OutputDimensionsSaveTimer_Tick;
            _outputDimensionsSaveTimer.Tick += OutputDimensionsSaveTimer_Tick;
            _outputDimensionsSaveTimer.Start();
        }

        private void OutputDimensionsSaveTimer_Tick(object? sender, EventArgs e)
        {
            _outputDimensionsSaveTimer?.Stop();
            OperatorWorkflowPreferences.Update(data =>
            {
                data.OutputWindowWidth = _pendingOutputWidth;
                data.OutputWindowHeight = _pendingOutputHeight;
            });
        }

        private void FlushOutputDimensionsSave()
        {
            if (_outputDimensionsSaveTimer?.IsEnabled != true) return;
            OutputDimensionsSaveTimer_Tick(this, EventArgs.Empty);
        }

        private void MainContent_PreviewMouseWheel(object? sender, PointerWheelEventArgs e)
            => NestedScrollRouting.RoutePreviewMouseWheel(MainContent, e);

        public MainWindow()
        {
            NativePointer.EnsureInitialized(); InitializeComponent(); ProductionNames.CompleteLoad(this);
            InteractionMotion.Enable(this);
            _styledMessages.CollectionChanged += (_, _) => UpdateTickerEditorState();
            EnsureTickerSlots();
            MessageList.ItemsSource = _styledMessages;
            UpdateTickerEditorState();

            MainContent.PointerWheelChanged += MainContent_PreviewMouseWheel;
            MainContent.AddHandler(Control.RequestBringIntoViewEvent, (_, e) => e.Handled = true, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            SizeChanged += (_, e) =>
            {
                UpdateControlPanelViewportConstraints(e.NewSize);
                // Protect the live match identity at narrow operator-window
                // widths. Secondary hints progressively yield before team,
                // score, quarter and clock information can be clipped.
                HeaderShortcutText.IsVisible = false;
                HeaderOutputBadge.IsVisible = e.NewSize.Width >= 780
                    ? true
                    : false;

                if (MainTabControl.Items.Count > 0)
                {
                    bool compactTabs = e.NewSize.Width < 900;
                    if (_compactTabLayout != compactTabs)
                    {
                        _compactTabLayout = compactTabs;
                        UpdateOperatorTabHeaders();
                        MatchControlTab.Tag = compactTabs ? null : (_uxPreferences.GuidedOperatorMode ? "SCORE · CLOCK · UNDO" : "SCORING & CLOCK");
                        OutputStudioTab.Tag = compactTabs ? null : "PRESENTATIONS";
                        MediaDeskTab.Tag = compactTabs ? null : "VIDEO & TICKER";
                        MatchSetupTab.Tag = compactTabs ? null : (_uxPreferences.GuidedOperatorMode ? "TEAMS · SCOREBOARD" : "TEAMS & OPTIONS");

                        foreach (var item in MainTabControl.Items.OfType<Avalonia.Controls.TabItem>())
                            item.FontSize = compactTabs ? 11 : 13;
                    }
                }
            };

            _match.SetTeams("Home Team", "HOM", "Away Team", "AWA");

            NativeRendering.Rendering += OnFrameRendering;

            _match.MatchChanged += () =>
            {
                Title = $"Roche Scoreboard v{ProductionVersion.CurrentVersion.ToString(3)} | Q{_match.Quarter} | {_match.HomeTotal} – {_match.AwayTotal}";
                PushAllToScoreboard();
                // Every presentation is a live view of the same match. Refresh
                // the visible surface for undo, corrections, quarter changes
                // and clock-state changes as well as newly-added scores.
                RefreshVisibleScreens();
                UpdateLiveUI();
                BroadcastWebState();
                QueueGameStateSave();
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
                    MatchLogEmptyHint.IsVisible = false;

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
                _scorebug?.CancelScorelessOrDroughtOverlay(ev.Team == TeamSide.Home);

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
                    t => _scoringRunDelayTimer = t,
                    allowWhenClockStopped: true);
                ScheduleAutoOverlay(
                    () => { if (_scorebug != null) EnqueueAutoOverlay(() => CheckLeadChangesAfterScore(), isPriority: true); },
                    allowWhenClockStopped: true);

                // Push updated data to any visible overlay or screen
                RefreshVisibleScreens();

                // Full-screen presentations share the same score-feedback
                // language as the live scorebug. Animate only the surface the
                // audience can currently see so hidden controls never queue a
                // stale +6/+1 wipe for later.
                if (_breakScreen?.IsVisible == true) _breakScreen.PlayScoreAnimation(ev);
                if (_scoreworm?.IsVisible == true) _scoreworm.PlayScoreAnimation(ev);
                if (_statsScreen?.IsVisible == true) _statsScreen.PlayScoreAnimation(ev);
                if (_weatherScreen?.IsVisible == true) _weatherScreen.PlayScoreAnimation(ev);
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
            GlobalSettingsPanel.BackRequested += OnGlobalSettingsBackRequested;
            GlobalSettingsPanel.StartupModeChanged += OnGlobalSettingsStartupModeChanged;
            GlobalSettingsPanel.ThemeChanged += OnGlobalSettingsThemeChanged;
            GlobalSettingsPanel.DensityChanged += OnGlobalSettingsDensityChanged;
            GlobalSettingsPanel.QuickStartupHintsChanged += OnGlobalSettingsQuickHintsChanged;
            GlobalSettingsPanel.GoalAnimationChanged += OnGlobalGoalAnimationChanged;
            GlobalSettingsPanel.OutputDimensionsChanged += OnGlobalOutputDimensionsChanged;
            GlobalSettingsPanel.BreakScreenEnabledChanged += OnGlobalBreakScreenEnabledChanged;
            GlobalSettingsPanel.AutoOverlaysEnabledChanged += OnGlobalAutoOverlaysEnabledChanged;
            GlobalSettingsPanel.ScreensaverEnabledChanged += OnGlobalScreensaverEnabledChanged;

            // Sport selection visible by default; everything else hidden
            MainContent.IsVisible = false;
            SetupWizardPanel.IsVisible = false;
            CricketSetupWizardPanel.IsVisible = false;
            CricketControlPanelView.IsVisible = false;
            GlobalSettingsPanel.IsVisible = false;
            SportSelectionPanel.IsVisible = true;

            Closing += MainWindow_Closing;
            Loaded += MainWindow_Loaded;
            KeyDown += MainWindow_PreviewKeyDown;
            PointerPressed += MainWindow_PreviewMouseLeftButtonDown;
            PointerReleased += MainWindow_PreviewMouseLeftButtonUp;
            PointerWheelChanged += MainWindow_PreviewMouseWheelGuard;
            UpdateLogoCropSummaryText();
            UpdateHoverPreviewVisual(_selectedPreviewKey);
            InitSettingsWeatherLocation();
            RegisterScheduledOverlays();
            ApplyAflKeybindsToUi();
            _uxPreferences = UxPreferences.Load();
            ApplyRememberedScorebugLayout();
            ApplyGuidedOperatorMode();
            GlobalSettingsPanel.SetApplicationPreferences(
                _uxPreferences.Theme, _uxPreferences.Density, _uxPreferences.EnableQuickStartupHints);
            OperatorWorkflowPreferencesData workflowPreferences = OperatorWorkflowPreferences.Load();
            _screensaverEnabled = workflowPreferences.OutputScreensaverEnabled;
            _screensaverMediaPath = workflowPreferences.OutputScreensaverMediaPath;
            _breakScreenEnabled = workflowPreferences.AflBreakScreenEnabled;
            _autoOverlaysEnabled = workflowPreferences.AflAutoOverlaysEnabled;
            SettingsBreakScreenToggle.IsChecked = _breakScreenEnabled;
            AutoOverlaysToggle.IsChecked = _autoOverlaysEnabled;
            UpdateScreensaverSettingsStatus();
            ApplyRememberedGoalAnimation(workflowPreferences.AflGoalAnimation);
            RefreshGlobalSettingsOutputPreferences(workflowPreferences);
            SettingsShowControlPanelCountdown.IsChecked = workflowPreferences.AflControlPanelCountdown;
            _initializingGoalAnimation = false;
            UpdateAnimCards();
            _hideTeamNames = _uxPreferences.HideTeamNames;
            SettingsHideTeamNamesToggle.IsChecked = _hideTeamNames;
            UpdateSettingsHideTeamNamesCardVisuals();
            ApplyStartupPreferenceSelectionToUi();
            _suppressNavigationPersistence = false;
        }

        private void MainTabControl_SelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
        {
            if (e.Source != MainTabControl)
                return;

            ArmNavigationPointerGuard();
            // Reset the outer viewport synchronously while the tab selection
            // event is still handling the original press. A deferred reset
            // could arrive after release and visibly move a different command
            // under the operator's next click.
            MainContent.ScrollToHome();
            MainContent.ScrollToLeftEnd();

            // Keep match identity, clock and output state visible everywhere.
            // Operators should never have to leave Media, Display or Settings
            // merely to confirm the live score or whether the clock is running.
            MatchStripBorder.IsVisible = true;

            // Only paint the VIDEO-tab live mirror while the tab is visible.
            // VisualBrush of a 1080p container is a heavy per-frame operation
            // on slower devices, so we keep it disconnected when the operator
            // is on any other tab.
            bool onVideoTab = ReferenceEquals(MainTabControl?.SelectedItem, MediaDeskTab);
            bool onSettingsTab = ReferenceEquals(MainTabControl?.SelectedItem, MatchSetupTab);
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

            if (!_suppressNavigationPersistence && MainTabControl is not null && MainTabControl.SelectedIndex >= 0)
            {
                _uxPreferences.LastMainTabIndex = MainTabControl.SelectedIndex;
                QueueUxPreferencesSave();
            }
        }

        private void ArmNavigationPointerGuard()
        {
            // A tab/category swap can move a live control underneath the
            // pointer before the user's release or touchpad inertia finishes.
            // Briefly absorb that trailing input so navigation can never score,
            // start a clock or trigger an output action accidentally.
            _navigationPointerGuardUntilUtc = DateTime.UtcNow.AddMilliseconds(450);
            NativeKeyboard.ClearFocus();
        }

        private void MainWindow_PreviewMouseLeftButtonUp(object? sender, PointerEventArgs e)
        {
            if (DateTime.UtcNow < _navigationPointerGuardUntilUtc)
                e.Handled = true;
        }

        private void MainWindow_PreviewMouseLeftButtonDown(object? sender, PointerEventArgs e)
        {
            if (DateTime.UtcNow < _navigationPointerGuardUntilUtc)
                e.Handled = true;
        }

        private void MainWindow_PreviewMouseWheelGuard(object? sender, PointerWheelEventArgs e)
        {
            if (DateTime.UtcNow < _navigationPointerGuardUntilUtc)
                e.Handled = true;
        }

        private void RestoreLastMainTabSelection()
        {
            if (MainTabControl is null || MainTabControl.Items.Count == 0)
                return;

            int index = Math.Clamp(_uxPreferences.LastMainTabIndex, 0, MainTabControl.Items.Count - 1);
            if (_uxPreferences.GuidedOperatorMode && index is 1 or 2)
                index = 0;
            bool wasSuppressed = _suppressNavigationPersistence;
            _suppressNavigationPersistence = true;
            try
            {
                MainTabControl.SelectedIndex = index;
            }
            finally
            {
                _suppressNavigationPersistence = wasSuppressed;
            }
        }

        private void GuidedModeButton_Click(object? sender, RoutedEventArgs e)
        {
            ArmNavigationPointerGuard();
            _uxPreferences.GuidedOperatorMode = !_uxPreferences.GuidedOperatorMode;
            ApplyGuidedOperatorMode();
            QueueUxPreferencesSave();
        }

        private void ApplyGuidedOperatorMode()
        {
            bool essentials = _uxPreferences.GuidedOperatorMode;
            if (essentials && (ReferenceEquals(MainTabControl?.SelectedItem, OutputStudioTab) ||
                               ReferenceEquals(MainTabControl?.SelectedItem, MediaDeskTab)))
                MatchControlTab.IsSelected = true;

            OutputStudioTab.IsVisible = essentials ? false : true;
            MediaDeskTab.IsVisible = essentials ? false : true;
            EssentialsModeBadge.IsVisible = true;
            EssentialsModeBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                essentials ? "#102A19" : "#241A08"));
            EssentialsModeBadge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                essentials ? "#3FB950" : "#D9A441"));
            OperatorModeBadgeText.Text = essentials ? "ESSENTIALS" : "ADVANCED";
            OperatorModeBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                essentials ? "#86EFAC" : "#FFD77A"));
            GuidedModeButton.Content = essentials ? "ADVANCED TOOLS" : "RETURN TO ESSENTIALS";
            Avalonia.Controls.ToolTip.SetTip(GuidedModeButton, essentials
                ? "Show presentation, overlay, video and ticker tools · Ctrl+Shift+A"
                : "Hide specialist tools and keep only Match and Setup · Ctrl+Shift+A");

            ScoringSectionTitle.Text = essentials ? "ESSENTIAL SCORING" : "SCORING";
            MatchControlTab.Tag = _compactTabLayout == true ? null : essentials ? "SCORE · CLOCK · UNDO" : "SCORING & CLOCK";
            MatchSetupTab.Tag = _compactTabLayout == true ? null : essentials ? "TEAMS · SCOREBOARD" : "TEAMS & OPTIONS";
            UpdateOperatorTabHeaders();
            Avalonia.Controls.ToolTip.SetTip(MatchControlTab, "Scoring, clock and match log · Ctrl+1");
            Avalonia.Controls.ToolTip.SetTip(MatchSetupTab, essentials
                ? "Teams and essential scoreboard options · Ctrl+2"
                : "Teams, match options and app settings · Ctrl+4");

            bool specialistVisibility = essentials ? false : true;
            SettingsNavBtn_Keybinds.IsVisible = specialistVisibility;
            SettingsNavBtn_Overlays.IsVisible = specialistVisibility;
            SettingsNavBtn_Media.IsVisible = specialistVisibility;
            SettingsNavBtn_Advanced.IsVisible = specialistVisibility;
            SettingsSearchHost.IsVisible = specialistVisibility;
            SettingsEssentialsHint.IsVisible = essentials ? true : false;
            SettingsHeaderTitle.Text = essentials ? "ESSENTIAL MATCH SETUP" : "LIVE MATCH SETTINGS";
            SettingsHeaderSubtitle.Text = essentials
                ? "The everyday volunteer view · teams, timing and scoreboard readability"
                : "AFL match, controls and output";

            if (essentials && _settingsActiveCategory is not ("match" or "scoreboard"))
                ApplySettingsCategoryFilter("match");
        }

        private void UpdateOperatorTabHeaders()
        {
            bool compact = _compactTabLayout == true;
            bool essentials = _uxPreferences.GuidedOperatorMode;
            MatchControlTab.Header = essentials || compact ? "MATCH" : "1  MATCH";
            OutputStudioTab.Header = compact ? "OUTPUT" : "2  OUTPUT";
            MediaDeskTab.Header = compact ? "MEDIA" : "3  MEDIA";
            MatchSetupTab.Header = essentials || compact ? "SETUP" : "4  SETUP";
        }

        internal IReadOnlyDictionary<string, bool> RunOperatorModeSelfCheckForQa()
        {
            bool originalMode = _uxPreferences.GuidedOperatorMode;
            object? originalSelection = MainTabControl.SelectedItem;
            var checks = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            try
            {
                _uxPreferences.GuidedOperatorMode = true;
                ApplyGuidedOperatorMode();
                checks["essentials-two-visible-tabs"] =
                    MatchControlTab.IsVisible == true &&
                    MatchSetupTab.IsVisible == true &&
                    OutputStudioTab.IsVisible == false &&
                    MediaDeskTab.IsVisible == false;
                checks["essentials-clear-tab-labels"] =
                    Equals(MatchControlTab.Header, "MATCH") && Equals(MatchSetupTab.Header, "SETUP");
                checks["essentials-setup-shortcut"] =
                    Avalonia.Controls.ToolTip.GetTip(MatchSetupTab)?.ToString()?.Contains("Ctrl+2", StringComparison.Ordinal) == true;

                _uxPreferences.GuidedOperatorMode = false;
                ApplyGuidedOperatorMode();
                checks["advanced-four-visible-tabs"] =
                    OutputStudioTab.IsVisible == true && MediaDeskTab.IsVisible == true;
                checks["advanced-numbered-tabs"] = _compactTabLayout == true ||
                    (Equals(MatchControlTab.Header, "1  MATCH") && Equals(MatchSetupTab.Header, "4  SETUP"));
            }
            finally
            {
                _uxPreferences.GuidedOperatorMode = originalMode;
                ApplyGuidedOperatorMode();
                if (originalSelection is not null)
                    MainTabControl.SelectedItem = originalSelection;
            }
            return checks;
        }

        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Update prompt on close has been removed in favour of an in-app
            // update section in the Settings tab. Proceed straight to teardown.

            NativeRendering.Rendering -= OnFrameRendering;
            FlushUxPreferencesSave();
            FlushOutputDimensionsSave();
            _deferredGameStateSaveTimer?.Stop();
            _deferredGameStateSaveTimer = null;
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

        private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            UpdateControlPanelViewportConstraints(new Avalonia.Size(Bounds.Width, Bounds.Height));
            _uxPreferences = UxPreferences.Load();
            ApplyRememberedScorebugLayout();
            ApplyGuidedOperatorMode();
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
                var workArea = NativeDesktop.WorkArea;
                const double padding = 40;

                // If the default 1550x1050 size is larger than the work area
                // (small laptop / low-res monitor), shrink to fit so the
                // taskbar and title bar stay reachable.
                double width = Math.Min(Bounds.Width, workArea.Width - padding);
                double height = Math.Min(Bounds.Height, workArea.Height - padding);
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

            bool selfTest = Avalonia.Application.Current is RocheScoreboard.Mac.App { IsSelfTestMode: true };
            if (!selfTest)
            {
                // Keep the public display useful before setup: the neutral,
                // low-cost sports scene is replaced atomically at match ready.
                EnsureScoreboardWindow();
                ShowOutputScreensaver(manualSelection: false);

                // Cloud remote session startup is wired up in MainWindow.CloudRemote.cs.
                StartCloudRemoteOnLoad();

                // Embed the AFL Scoring & Clock cloud remote launcher so the
                // operator can spin up / show the QR without flipping to
                // Settings. The Training Mode launcher is registered in
                // EnterTrainingMode() because that control is created lazily.

                // Restore last-loaded custom MP4 in the VIDEO tab (best-effort).
                TryRestoreLastVideoPath();

                // Defer alerts so the window is fully visible first.
                ApplyQuickStartupNavigation();
                await Dispatcher.UIThread.InvokeAsync(async () => await ShowDeferredAlerts(), Avalonia.Threading.DispatcherPriority.Background);
            }
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
        private void HookDimensionMirror(NativeWindow displayWindow)
        {
            if (displayWindow is null) return;

            displayWindow.SizeChanged += (_, e) =>
            {
                if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
                var pixels = PixelSizeHelper.GetWindowPixels(displayWindow, e.NewSize.Width, e.NewSize.Height);
                UpdateDimensionBoxes(pixels.Width, pixels.Height);
                if (ReferenceEquals(displayWindow, _displayWindow))
                {
                    int width = Convert.ToInt32(pixels.Width);
                    int height = Convert.ToInt32(pixels.Height);
                    GlobalSettingsPanel.SetOutputDimensions(width, height);
                    QueueOutputDimensionsSave(width, height);
                }
            };

            displayWindow.Loaded += (_, __) =>
            {
                var pixels = PixelSizeHelper.GetWindowPixels(displayWindow, displayWindow.NativeWidth(), displayWindow.NativeHeight());
                UpdateDimensionBoxes(pixels.Width, pixels.Height);
                if (ReferenceEquals(displayWindow, _displayWindow))
                    GlobalSettingsPanel.SetOutputDimensions(Convert.ToInt32(pixels.Width), Convert.ToInt32(pixels.Height));
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

            if (!WindowWidthBox.IsFocused)
            {
                string wText = w.ToString();
                if ((WindowWidthBox.Text ?? string.Empty) != wText) WindowWidthBox.Text = wText;
            }

            if (!WindowHeightBox.IsFocused)
            {
                string hText = h.ToString();
                if ((WindowHeightBox.Text ?? string.Empty) != hText) WindowHeightBox.Text = hText;
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
            if (Avalonia.Application.Current is RocheScoreboard.Mac.App startupApp)
            {
                try { await startupApp.UpdateCheckTask; } catch { }
            }

            // Refresh the in-app Settings update card so the user can act on
            // any pending update from there.
            try { RefreshUpdateStatusUi(); } catch { }

            // If a newer release is already known from the background check
            // in App.OnStartup, surface the update prompt right away so the
            // operator doesn't have to dig into Settings to find it.
            if (Avalonia.Application.Current is RocheScoreboard.Mac.App app && app.UpdateService.UpdateAvailable)
            {
                if (app.PromptForUpdate())
                {
                    // Update was applied and the app is shutting down to let
                    // the external updater finish — skip the resume dialog.
                    return;
                }
            }

            // A valid live recovery takes precedence over a navigation
            // preference. StartupMode only decides where a fresh session opens.
            if (GameStateService.HasSavedState())
            {
                GameState? saved = GameStateService.Load();
                if (saved?.MatchState is not null || saved?.CricketMatchState is not null)
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
                    else if (dialog.DiscardChosen)
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

        internal static NativeBitmap GenerateQrCode(string text)
        {
            using var generator = new QRCoder.QRCodeGenerator();
            using QRCoder.QRCodeData data = generator.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.M);
            using var qr = new QRCoder.BitmapByteQRCode(data);
            byte[] png = qr.GetGraphic(6, [255, 255, 255], [13, 17, 23]);

            var bitmap = new NativeBitmap();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new System.IO.MemoryStream(png);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private static void TrySetClipboard(string text)
        {
            try { NativeClipboard.SetText(text); }
            catch (System.Runtime.InteropServices.ExternalException) { /* clipboard locked by another process */ }
        }

        /// <summary>
        /// Copies text to the clipboard and shows a brief "Copied!" flash on a status TextBlock.
        /// </summary>
        private void TrySetClipboardWithFeedback(string text, Avalonia.Controls.TextBlock statusBlock)
        {
            try
            {
                NativeClipboard.SetText(text);

                string prev = (statusBlock.Text ?? string.Empty);
                Avalonia.Media.IBrush? prevBrush = statusBlock.Foreground;
                statusBlock.Text = "✓ Copied!";
                statusBlock.Foreground = this.FindResource("LiveBrush") as SolidColorBrush ?? new SolidColorBrush(Brushes.Green.Color);

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

        private void MainWindow_PreviewKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            bool ctrlPressed = (NativeKeyboard.Modifiers & KeyModifiers.Control) != 0;
            bool settingsVisible = ReferenceEquals(MainTabControl?.SelectedItem, MatchSetupTab);

            if (ctrlPressed && (NativeKeyboard.Modifiers & KeyModifiers.Shift) != 0 && e.Key == Key.A)
            {
                GuidedModeButton_Click(GuidedModeButton, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (settingsVisible && ctrlPressed && !_uxPreferences.GuidedOperatorMode && e.Key == Key.F)
            {
                SettingsSearchBox.Focus();
                SettingsSearchBox.SelectAll();
                e.Handled = true;
                return;
            }

            if (settingsVisible && e.Key == Key.Escape && SettingsSearchBox.IsKeyboardFocusWithin)
            {
                SettingsSearchBox.Clear();
                NativeKeyboard.ClearFocus();
                e.Handled = true;
                return;
            }

            if (ctrlPressed && MainTabControl is not null)
            {
                if (_uxPreferences.GuidedOperatorMode && e.Key is Key.D2 or Key.NumPad2)
                {
                    MainTabControl.SelectedItem = MatchSetupTab;
                    e.Handled = true;
                    return;
                }
                int tabIndex = e.Key switch
                {
                    Key.D1 or Key.NumPad1 => 0,
                    Key.D2 or Key.NumPad2 => 1,
                    Key.D3 or Key.NumPad3 => 2,
                    Key.D4 or Key.NumPad4 => 3,
                    _ => -1
                };
                if (tabIndex >= 0)
                {
                    if (_uxPreferences.GuidedOperatorMode && tabIndex is 1 or 2 or 3)
                    {
                        e.Handled = true;
                        return;
                    }
                    MainTabControl.SelectedIndex = tabIndex;
                    e.Handled = true;
                    return;
                }
            }

            // Don't intercept when typing in a TextBox
            if (e.Source is Avalonia.Controls.TextBox) return;
            if (_sportMode != SportMode.AFL) return;

            if (TryHandleKeybindCapture(e.Key))
            {
                e.Handled = true;
                return;
            }

            bool ctrl = ctrlPressed;
            bool alt = (NativeKeyboard.Modifiers & KeyModifiers.Alt) != 0;
            bool shift = (NativeKeyboard.Modifiers & KeyModifiers.Shift) != 0;
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
            => NativeLookup.Find(this, name) as T;

        private void ApplyAflKeybindsToUi()
        {
            var homeGoalShortcutText = FindElement<TextBlock>("HomeGoalShortcutText");
            var homeBehindShortcutText = FindElement<TextBlock>("HomeBehindShortcutText");
            var awayGoalShortcutText = FindElement<TextBlock>("AwayGoalShortcutText");
            var awayBehindShortcutText = FindElement<TextBlock>("AwayBehindShortcutText");
            var startPauseShortcutText = FindElement<TextBlock>("StartPauseShortcutText");
            var keybindToggleClockButton = FindElement<Avalonia.Controls.Button>("KeybindToggleClockButton");
            var keybindHomeGoalButton = FindElement<Avalonia.Controls.Button>("KeybindHomeGoalButton");
            var keybindHomeBehindButton = FindElement<Avalonia.Controls.Button>("KeybindHomeBehindButton");
            var keybindAwayGoalButton = FindElement<Avalonia.Controls.Button>("KeybindAwayGoalButton");
            var keybindAwayBehindButton = FindElement<Avalonia.Controls.Button>("KeybindAwayBehindButton");
            var keybindEndQuarterButton = FindElement<Avalonia.Controls.Button>("KeybindEndQuarterButton");

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

            Avalonia.Controls.ToolTip.SetTip(EndQtrButton, $"End the current quarter — {endQuarterLabel}");
            Avalonia.Controls.ToolTip.SetTip(HomeGoalButton, $"Goal (+6 points) — {homeGoalLabel}");
            Avalonia.Controls.ToolTip.SetTip(HomeBehindButton, $"Behind (+1 point) — {homeBehindLabel}");
            Avalonia.Controls.ToolTip.SetTip(AwayGoalButton, $"Goal (+6 points) — {awayGoalLabel}");
            Avalonia.Controls.ToolTip.SetTip(AwayBehindButton, $"Behind (+1 point) — {awayBehindLabel}");
            Avalonia.Controls.ToolTip.SetTip(StartPauseButton, $"Start the quarter, or control stoppage timing during count-up matches — {toggleClockLabel}");

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
                keybindHintText.Foreground = this.FindResource("AccentBrush") as SolidColorBrush ?? new SolidColorBrush(Brushes.White.Color);
            }
        }

        private void FinishKeybindCapture(string message, Avalonia.Media.IBrush? foreground = null)
        {
            _pendingKeybindAction = null;
            var keybindHintText = FindElement<TextBlock>("KeybindHintText");
            if (keybindHintText is not null)
            {
                keybindHintText.Text = message;
                keybindHintText.Foreground = foreground ?? (this.FindResource("TextDimBrush") as SolidColorBrush ?? new SolidColorBrush(Brushes.White.Color));
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

        private void KeybindToggleClockButton_Click(object? sender, RoutedEventArgs e)
            => BeginKeybindCapture(AflAction.ToggleClock);

        private void KeybindHomeGoalButton_Click(object? sender, RoutedEventArgs e)
            => BeginKeybindCapture(AflAction.HomeGoal);

        private void KeybindHomeBehindButton_Click(object? sender, RoutedEventArgs e)
            => BeginKeybindCapture(AflAction.HomeBehind);

        private void KeybindAwayGoalButton_Click(object? sender, RoutedEventArgs e)
            => BeginKeybindCapture(AflAction.AwayGoal);

        private void KeybindAwayBehindButton_Click(object? sender, RoutedEventArgs e)
            => BeginKeybindCapture(AflAction.AwayBehind);

        private void KeybindEndQuarterButton_Click(object? sender, RoutedEventArgs e)
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
        private void DisplayWindow_PreviewKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            bool ctrl = (NativeKeyboard.Modifiers & KeyModifiers.Control) != 0;
            bool shift = (NativeKeyboard.Modifiers & KeyModifiers.Shift) != 0;

            if (ctrl && shift && e.Key == Key.C && _calibrationOverlay is not null)
            {
                _calibrationOverlay.IsVisible = _calibrationOverlay.IsVisible == true
                    ? false
                    : true;
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
                    sender as NativeWindow ?? this,
                    newValue
                        ? "Display windows will use OPAQUE rendering (no transparency).\n\nClose and reopen the display window for this to take effect."
                        : "Display windows will use TRANSPARENT rendering.\n\nClose and reopen the display window for this to take effect.",
                    "Display rendering mode",
                    NativeMessageBoxButton.OK,
                    NativeMessageBoxImage.Information);
            }
        }

        // ----------------------------
        // Clock — fires every frame via NativeRendering.Rendering
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

                // Time-in-front and other clock-derived values remain live
                // while the stats presentation is on air, even in a quiet
                // passage with no scoring events.
                if (_statsScreen?.IsVisible == true)
                    PopulateStatsScreen();
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
            bool canContinue = _match.CanContinueQuarter;
            bool clockStateChanged = _match.ClockRunning != _previousClockRunning;
            _previousClockRunning = _match.ClockRunning;

            // Status badge
            if (q4Ended)
            {
                StatusDot.Fill = FullTimeBrush;
                StatusText.Text = "FULL TIME";
                StatusText.Foreground = FullTimeBrush;
                StatusBadge.Background = FullTimeBgBrush;
                Avalonia.Controls.ToolTip.SetTip(StatusBadge, "Confirm the final score with both goal umpires before closing the match.");
            }
            else if (_match.ClockRunning)
            {
                StatusDot.Fill = LiveGreenBrush;
                StatusText.Text = "LIVE";
                StatusText.Foreground = LiveGreenBrush;
                StatusBadge.Background = LiveGreenBgBrush;
                Avalonia.Controls.ToolTip.SetTip(StatusBadge, "The match clock is running.");
            }
            else if (canContinue)
            {
                StatusDot.Fill = PausedAmberBrush;
                StatusText.Text = "BREAK · CHECK SCORE";
                StatusText.Foreground = PausedAmberBrush;
                StatusBadge.Background = PausedAmberBgBrush;
                Avalonia.Controls.ToolTip.SetTip(StatusBadge, "Confirm both scores with the goal umpires before starting the next quarter.");
            }
            else
            {
                StatusDot.Fill = PausedAmberBrush;
                StatusText.Text = "PAUSED";
                StatusText.Foreground = PausedAmberBrush;
                StatusBadge.Background = PausedAmberBgBrush;
                Avalonia.Controls.ToolTip.SetTip(StatusBadge, "The match clock is stopped.");
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
                    _deferredGameStateSaveTimer?.Stop();
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
                Avalonia.Controls.ToolTip.SetTip(StartPauseButton, "Match complete.");
            }
            else if (controlsStoppageCountdownOnly)
            {
                if (_controlPanelCountdownRunning)
                {
                    StartPauseButton.Content = "⏸  Pause Stoppage";
                    StartPauseButton.Background = PauseBtnBg;
                    StartPauseButton.BorderBrush = PauseBtnBorder;
                    Avalonia.Controls.ToolTip.SetTip(StartPauseButton, "Pause the separate stoppage countdown.");
                }
                else
                {
                    StartPauseButton.Content = "▶  Start Stoppage";
                    StartPauseButton.Background = StartBtnBg;
                    StartPauseButton.BorderBrush = StartBtnBorder;
                    Avalonia.Controls.ToolTip.SetTip(StartPauseButton, "Start the separate stoppage countdown.");
                }
            }
            else if (_match.ClockRunning)
            {
                StartPauseButton.Content = "⏸  Pause";
                StartPauseButton.Background = PauseBtnBg;
                StartPauseButton.BorderBrush = PauseBtnBorder;
                Avalonia.Controls.ToolTip.SetTip(StartPauseButton, "Pause the match clock for time-on.");
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
                Avalonia.Controls.ToolTip.SetTip(StartPauseButton, atFreshMatchStart
                    ? "Start Quarter 1."
                    : $"Confirm the previous score, then start Quarter {_match.Quarter}.");
            }

            // End quarter button — show current quarter and disable after Q4
            EndQtrButton.IsEnabled = !q4Ended;

            // Swap End Qtr / Continue Qtr button visibility
            EndQtrButton.IsVisible = canContinue ? false : true;
            ContinueQtrButton.IsVisible = canContinue ? true : false;

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
                HeaderMarginText.Foreground = this.FindResource("HomeAccentBrush") as SolidColorBrush ?? new SolidColorBrush(Brushes.White.Color);
            }
            else if (margin < 0)
            {
                HeaderMarginText.Text = $"{_match.AwayAbbr} +{-margin}";
                HeaderMarginText.Foreground = this.FindResource("AwayAccentBrush") as SolidColorBrush ?? new SolidColorBrush(Brushes.White.Color);
            }
            else
            {
                HeaderMarginText.Text = "DRAWN";
                HeaderMarginText.Foreground = this.FindResource("TextMidBrush") as SolidColorBrush ?? new SolidColorBrush(Brushes.White.Color);
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
                MatchResultText.Text = "Scores level";

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
                StartSwitchWarning.IsVisible = false;
                return;
            }

            // Show warning when pressing Start would auto-switch the display
            StartSwitchWarning.IsVisible = (_showingBreakScreen || _isBreakRotating)
                ? true
                : false;
        }

        // ----------------------------
        // Setup wizard
        // ----------------------------
        private void ShowSetupWizard()
        {
            GameStateService.Clear();
            _aflMatchReady = false;
            ShowOutputScreensaver(manualSelection: false);
            // Clear any held animation values from a previous transition so
            // local property sets take effect on the next wizard completion.
            MainContent.BeginAnimation(OpacityProperty, null);
            SetupWizardPanel.BeginAnimation(OpacityProperty, null);

            // Return to sport selection
            MainContent.IsVisible = false;
            SetupWizardPanel.IsVisible = false;
            CricketSetupWizardPanel.IsVisible = false;
            CricketControlPanelView.IsVisible = false;
            GlobalSettingsPanel.IsVisible = false;
            SportSelectionPanel.PrepareForDisplay();
            SportSelectionPanel.Opacity = 1;
            SportSelectionPanel.IsVisible = true;
        }

        private void OnSportSelected(SportMode mode)
        {
            _sportMode = mode;
            PersistLastMode(mode);
            SportSelectionPanel.IsVisible = false;
            if (!_aflMatchReady)
                ShowOutputScreensaver(manualSelection: false);

            if (mode == SportMode.AFL)
            {
                SetupWizardPanel.Reset();
                SetupWizardPanel.Opacity = 1;
                SetupWizardPanel.IsVisible = true;
            }
            else if (mode == SportMode.Cricket)
            {
                CricketSetupWizardPanel.Reset();
                CricketSetupWizardPanel.Opacity = 1;
                CricketSetupWizardPanel.IsVisible = true;
            }
            else if (mode == SportMode.Settings)
            {
                RefreshGlobalSettingsOutputPreferences();
                MainContent.IsVisible = false;
                SetupWizardPanel.IsVisible = false;
                CricketSetupWizardPanel.IsVisible = false;
                CricketControlPanelView.IsVisible = false;
                TrainingControlPanelView.IsVisible = false;
                GlobalSettingsPanel.IsVisible = true;
                Title = $"Roche Scoreboard v{ProductionVersion.CurrentVersion.ToString(3)} | Global Settings";
            }
            else
            {
                EnterTrainingMode();
            }
        }

        private void OnGlobalSettingsBackRequested()
        {
            PersistLastMode(SportMode.Settings);
            GlobalSettingsPanel.IsVisible = false;
            MainContent.IsVisible = false;
            SetupWizardPanel.IsVisible = false;
            CricketSetupWizardPanel.IsVisible = false;
            CricketControlPanelView.IsVisible = false;
            TrainingControlPanelView.IsVisible = false;
            SportSelectionPanel.PrepareForDisplay();
            SportSelectionPanel.Opacity = 1;
            SportSelectionPanel.IsVisible = true;
            Title = $"Roche Scoreboard v{ProductionVersion.CurrentVersion.ToString(3)}";
        }

        private void OnGlobalSettingsStartupModeChanged(string mode)
        {
            _uxPreferences.StartupMode = mode switch
            {
                "sportSelection" => StartupExperience.SportSelection,
                "lastMode" => StartupExperience.LastMode,
                _ => StartupExperience.ResumeIfAvailable,
            };
            QueueUxPreferencesSave();
        }

        private void OnGlobalSettingsThemeChanged(AppTheme theme)
        {
            _uxPreferences.Theme = theme;
            if (Avalonia.Application.Current is RocheScoreboard.Mac.App app)
            {
                app.UxPreferences.Theme = theme;
                app.ThemeService.SetTheme(theme);
            }
            QueueUxPreferencesSave();
        }

        private void OnGlobalSettingsDensityChanged(UiDensity density)
        {
            _uxPreferences.Density = density;
            if (Avalonia.Application.Current is RocheScoreboard.Mac.App app)
            {
                app.UxPreferences.Density = density;
                app.ThemeService.SetDensity(density);
            }
            QueueUxPreferencesSave();
        }

        private void OnGlobalSettingsQuickHintsChanged(bool enabled)
        {
            _uxPreferences.EnableQuickStartupHints = enabled;
            if (Avalonia.Application.Current is RocheScoreboard.Mac.App app)
                app.UxPreferences.EnableQuickStartupHints = enabled;
            QueueUxPreferencesSave();
        }

        private void OnGlobalGoalAnimationChanged(string style)
        {
            ApplyRememberedGoalAnimation(style);
        }

        private void OnGlobalOutputDimensionsChanged(int width, int height)
        {
            OperatorWorkflowPreferences.Update(data =>
            {
                data.OutputWindowWidth = width;
                data.OutputWindowHeight = height;
            });

            if (_displayWindow is not null)
            {
                PixelSizeHelper.SetWindowPixels(_displayWindow, width, height);
                WindowLayoutStorage.Save(WindowLayoutStorage.AflDisplayKey, _displayWindow);
            }
            UpdateDimensionBoxes(width, height);
        }

        private void OnGlobalBreakScreenEnabledChanged(bool enabled)
        {
            SettingsBreakScreenToggle.IsChecked = enabled;
        }

        private void OnGlobalAutoOverlaysEnabledChanged(bool enabled)
        {
            AutoOverlaysToggle.IsChecked = enabled;
        }

        private void OnGlobalScreensaverEnabledChanged(bool enabled)
        {
            _screensaverEnabled = enabled;
            OperatorWorkflowPreferences.Update(data => data.OutputScreensaverEnabled = enabled);
            if (!enabled && _outputScreensaver?.IsVisible == true)
                ShowScorebug();
            else if (enabled && !_aflMatchReady)
                ShowOutputScreensaver(manualSelection: false);
        }

        private void RefreshGlobalSettingsOutputPreferences(OperatorWorkflowPreferencesData? preferences = null)
        {
            preferences ??= OperatorWorkflowPreferences.Load();
            int width = preferences.OutputWindowWidth;
            int height = preferences.OutputWindowHeight;
            if (_displayWindow is { IsLoaded: true })
            {
                var pixels = PixelSizeHelper.GetWindowPixels(_displayWindow, _displayWindow.NativeWidth(), _displayWindow.NativeHeight());
                width = Convert.ToInt32(pixels.Width);
                height = Convert.ToInt32(pixels.Height);
            }
            GlobalSettingsPanel.SetOutputPreferences(
                preferences.AflGoalAnimation,
                width,
                height,
                _breakScreenEnabled,
                _autoOverlaysEnabled,
                _screensaverEnabled);
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
            QueueUxPreferencesSave();
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
                    SportSelectionPanel.IsVisible = true;
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
            _displayWindow?.Hide();

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
            TrainingControlPanelView.IsVisible = true;

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

            Title = $"Roche Scoreboard v{ProductionVersion.CurrentVersion.ToString(3)} | Training Mode";
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

            _trainingDisplayWindow = new NativeWindow
            {
                Title = $"Roche Scoreboard v{ProductionVersion.CurrentVersion.ToString(3)} | Training Display",
                SystemDecorations = Avalonia.Controls.SystemDecorations.Full,
                // Match the AFL display window: opaque, no layered transparency,
                // so Windows 11 doesn't draw rounded corners or a faint border
                // around the client area.
                
                CanResize = true,
                Topmost = true,
                ShowInTaskbar = false,
                Background = Avalonia.Media.Brushes.Black,
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
            

            // Restore the operator's last-saved Training display window layout if any.
            var savedTraining = WindowLayoutStorage.Get(WindowLayoutStorage.TrainingDisplayKey);
            if (savedTraining is not null)
                WindowLayoutStorage.Apply(_trainingDisplayWindow, savedTraining);

            _trainingDisplayWindow.Closed += (_, _) => _trainingDisplayWindow = null;

            HookDimensionMirror(_trainingDisplayWindow);

            // Hook the borderless-resize NC hit-test so the user can grab any
            // edge or corner to resize the training display, exactly like the
            // AFL / Cricket display windows.
            _trainingDisplayWindow.Opened += (_, _) =>
            {
                if (_trainingDisplayWindow is null) return;
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
            TrainingControlPanelView.IsVisible = false;
            SportSelectionPanel.Opacity = 1;
            SportSelectionPanel.IsVisible = true;
            ShowOutputScreensaver(manualSelection: false);
        }

        private void ShowMatchLoadingScreen(
            string homeName, string homeAbbr, string? homeLogo, string? homePrimaryColor,
            string awayName, string awayAbbr, string? awayLogo, string? awayPrimaryColor,
            string subtitle,
            IReadOnlyList<string> steps,
            Control fadeOutWizard,
            Action onReady)
        {
            MatchLoadingPanel.ConfigureTeams(
                homeName, homeAbbr, homeLogo, homePrimaryColor,
                awayName, awayAbbr, awayLogo, awayPrimaryColor);
            MatchLoadingPanel.SetBrandSubtitle(subtitle);

            MatchLoadingPanel.Opacity = 0;
            MatchLoadingPanel.IsVisible = true;

            var fadeIn = new RocheScoreboard.Mac.Native.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new RocheScoreboard.Mac.Native.CubicEase { EasingMode = RocheScoreboard.Mac.Native.EasingMode.EaseOut }
            };
            MatchLoadingPanel.BeginAnimation(OpacityProperty, fadeIn);

            // Fade the wizard panel out softly so the loader takes over the visual stage.
            if (fadeOutWizard is not null)
            {
                var wizardFade = new RocheScoreboard.Mac.Native.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
                wizardFade.Completed += (_, _) =>
                {
                    fadeOutWizard.IsVisible = false;
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
                var fadeOut = new RocheScoreboard.Mac.Native.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new RocheScoreboard.Mac.Native.CubicEase { EasingMode = RocheScoreboard.Mac.Native.EasingMode.EaseIn }
                };
                fadeOut.Completed += (_, _) =>
                {
                    MatchLoadingPanel.IsVisible = false;
                    MatchLoadingPanel.Opacity = 1;

                    if (readyInvoked) return;
                    readyInvoked = true;

                    // Defer the heavy work one dispatcher beat so WPF can
                    // actually commit the collapsed visibility / wizard
                    // teardown before we start hammering the UI thread.
                    Dispatcher.UIThread.Post(new Action(() =>
                    {
                        try { onReady(); }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[MatchLoading] onReady error: {ex.Message}");
                        }
                    }), Avalonia.Threading.DispatcherPriority.Background);
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
            _aflMatchReady = true;
            ApplySetupResult(result);

            MainContent.BeginAnimation(OpacityProperty, null);
            SetupWizardPanel.BeginAnimation(OpacityProperty, null);

            // Animate wizard out and main content in
            MainContent.Opacity = 0;
            MainContent.RenderTransform = new TranslateTransform(0, 20);
            MainContent.IsVisible = true;

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
                SetupWizardPanel.IsVisible = false;
                SetupWizardPanel.Opacity = 1;
                MainContent.Opacity = 1;
                MainContent.RenderTransform = null;
                Activate();
                Focus();
                NativeKeyboard.Focus(this);
            };
            sb.Begin();

            EnsureScoreboardWindow();
            if (_displayWindow is { IsVisible: false }) _displayWindow.Show();
            PushAllToScoreboard();
            if (_scorebug is not null)
            {
                _activeScreenKey = "scorebug";
                SwitchToScreen(_scorebug, "Live Scorebug");
                UpdatePresentationCards("scorebug");
            }
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

        private void FinalizeCricketSetup(CricketSetupResult result, bool promptForInitialSelection = true)
        {
            // The shared idle output hands off to cricket's dedicated live
            // display rather than leaving two topmost windows stacked.
            _displayWindow?.Hide();
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
            _cricketMatch.MatchChanged += OnCricketMatchChanged;

            // Create the cricket scorebug
            _cricketScorebug = new CricketScorebugControl();
            _cricketScorebug.UpdateFromMatch(_cricketMatch);
            _cricketScorebug.SetMarqueeMessages(result.Messages);

            // Create the summary control
            _cricketSummary = new CricketSummaryControl();
            _cricketSummary.IsVisible = false;

            // Wire control panel
            CricketControlPanelView.SetMatch(_cricketMatch);
            CricketControlPanelView.SetScorebug(_cricketScorebug);
            CricketControlPanelView.SetSummaryControl(_cricketSummary);
            CricketControlPanelView.SetMessages(result.Messages);
            CricketControlPanelView.PresentationChanged += OnCricketPresentationChanged;

            // Create the cricket display window
            EnsureCricketDisplayWindow();

            // Transition: hide wizard, show cricket control panel
            CricketSetupWizardPanel.IsVisible = false;
            CricketControlPanelView.Opacity = 0;
            CricketControlPanelView.RenderTransform = new TranslateTransform(0, 20);
            CricketControlPanelView.IsVisible = true;

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
                if (promptForInitialSelection)
                    CricketControlPanelView.TriggerInitialSelection();
            };
            sb.Begin();

            Title = $"Roche Scoreboard v{ProductionVersion.CurrentVersion.ToString(3)} | Cricket | {result.TeamAName} vs {result.TeamBName}";
        }

        private void EnsureCricketDisplayWindow()
        {
            if (_cricketDisplayWindow != null && _cricketDisplayWindow.IsVisible) return;
            if (_cricketScorebug == null) return;

            var container = new Grid();
            container.Children.Add(_cricketScorebug);
            if (_cricketSummary != null)
                container.Children.Add(_cricketSummary);

            _cricketDisplayWindow = new NativeWindow
            {
                Title = $"Roche Scoreboard v{ProductionVersion.CurrentVersion.ToString(3)} | Cricket Display",
                SystemDecorations = Avalonia.Controls.SystemDecorations.Full,
                
                CanResize = true,
                Topmost = true,
                ShowInTaskbar = false,
                Background = DisplaySettings.OpaqueDisplayWindows
                    ? (Avalonia.Media.IBrush)Avalonia.Media.Brushes.Black
                    : Avalonia.Media.Brushes.Transparent,
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

        private void OnCricketMatchChanged()
        {
            SaveGameState();
            PublishCloudSnapshot();
        }

        private void OnCricketResetRequested()
        {
            GameStateService.Clear();
            if (_cricketMatch != null)
            {
                _cricketMatch.MatchChanged -= OnCricketMatchChanged;
                _cricketMatch.ResetForNewGame();
            }
            CricketControlPanelView.PresentationChanged -= OnCricketPresentationChanged;
            _cricketDisplayWindow?.Close();
            _cricketDisplayWindow = null;
            _cricketScorebug = null;
            _cricketSummary = null;
            _cricketMatch = null;

            CricketControlPanelView.BeginAnimation(OpacityProperty, null);
            CricketControlPanelView.IsVisible = false;
            SportSelectionPanel.Opacity = 1;
            SportSelectionPanel.IsVisible = true;
            ShowOutputScreensaver(manualSelection: false);
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
            SettingsShowControlPanelCountdown.IsChecked = OperatorWorkflowPreferences.Load().AflControlPanelCountdown;
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
            ClearTickerUndoState();
            _suppressTickerSync = true;
            try
            {
                _styledMessages.Clear();
                foreach (var msg in r.Messages)
                {
                    if (_styledMessages.Count >= TickerSlotCount) break;
                    _styledMessages.Add(new Roche_Scoreboard.Models.MarqueeMessage(
                        (msg.Text ?? string.Empty) ?? string.Empty, msg.TextColor, msg.HighlightColor, isEnabled: true));
                }
                EnsureTickerSlots();
            }
            finally { _suppressTickerSync = false; }
            SyncTickerMessages();

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
            ApplyRememberedScorebugLayout();
            // Preserve the operator's remembered animation. Custom Video only
            // falls back when neither team actually has a clip configured.
            bool hasCustomVideo = !string.IsNullOrWhiteSpace(_homeGoalVideoPath) || !string.IsNullOrWhiteSpace(_awayGoalVideoPath);
            string rememberedAnimation = OperatorWorkflowPreferences.Load().AflGoalAnimation;
            ApplyRememberedGoalAnimation(!hasCustomVideo &&
                string.Equals(rememberedAnimation, "customVideo", StringComparison.OrdinalIgnoreCase)
                    ? "broadcast"
                    : rememberedAnimation);
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
            if (_sportMode == SportMode.Cricket)
            {
                if (_cricketMatch?.CurrentInnings is null) return;
                bool hasStarted = _cricketMatch.AllInnings.Any(i => i.Deliveries.Count > 0)
                    || _cricketMatch.CurrentInnings.StrikerIndex >= 0
                    || _cricketMatch.CurrentInnings.CurrentBowlerIndex >= 0;
                if (!hasStarted) return;

                if (_cricketMatch.MatchResult is not null)
                {
                    GameStateService.Clear();
                    return;
                }

                GameStateService.Save(new GameState
                {
                    Sport = "cricket",
                    CricketMatchState = _cricketMatch.ToState(),
                    HomePrimaryColor = _cricketMatch.TeamAPrimaryColor,
                    HomeSecondaryColor = _cricketMatch.TeamASecondaryColor,
                    AwayPrimaryColor = _cricketMatch.TeamBPrimaryColor,
                    AwaySecondaryColor = _cricketMatch.TeamBSecondaryColor,
                    HomeLogoPath = _cricketMatch.TeamALogoPath,
                    AwayLogoPath = _cricketMatch.TeamBLogoPath,
                    Messages = [.. _cricketMatch.Messages]
                });
                return;
            }

            // Persist AFL games that are in progress.
            if (_sportMode != SportMode.AFL) return;
            // A completed setup is a live match even before the first score or
            // clock start. Preserve it so closing immediately after setup still
            // offers recovery on the next launch.
            if (!_aflMatchReady && _match.Events.Count == 0 && !_match.ClockRunning && _match.Quarter == 1) return;

            // Don't persist completed games — full time means Q4 has ended
            bool isFullTime = _match.Quarter == 4 && _match.GetQuarterSnapshot(4) != null;
            if (isFullTime)
            {
                GameStateService.Clear();
                return;
            }

            var state = new GameState
            {
                Sport = "afl",
                MatchState = _match.ToState(),
                HomePrimaryColor = (HomeColorBox.Text ?? string.Empty),
                HomeSecondaryColor = (HomeSecondaryColorBox.Text ?? string.Empty),
                AwayPrimaryColor = (AwayColorBox.Text ?? string.Empty),
                AwaySecondaryColor = (AwaySecondaryColorBox.Text ?? string.Empty),
                HomeLogoPath = _homeLogoPath,
                AwayLogoPath = _awayLogoPath,
                HomeGoalVideoPath = _homeGoalVideoPath,
                AwayGoalVideoPath = _awayGoalVideoPath,
                Messages = [.. _messages],
                TickerSlots = _styledMessages.Select(message => new Roche_Scoreboard.Models.MarqueeMessage(
                    (message.Text ?? string.Empty), message.TextColor, message.HighlightColor, message.IsEnabled)).ToList(),
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

        private void QueueGameStateSave()
        {
            // The five-second running-clock autosave already owns persistence
            // while play is live. Avoid synchronous disk I/O in the score-click
            // path; stopped-clock edits are coalesced into one near-immediate
            // atomic save instead.
            if (_match.ClockRunning) return;
            _deferredGameStateSaveTimer?.Stop();
            _deferredGameStateSaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _deferredGameStateSaveTimer.Tick -= DeferredGameStateSaveTimer_Tick;
            _deferredGameStateSaveTimer.Tick += DeferredGameStateSaveTimer_Tick;
            _deferredGameStateSaveTimer.Start();
        }

        private void DeferredGameStateSaveTimer_Tick(object? sender, EventArgs e)
        {
            _deferredGameStateSaveTimer?.Stop();
            SaveGameState();
        }

        private void RestoreFromSavedState(GameState state)
        {
            if (state.CricketMatchState is not null || string.Equals(state.Sport, "cricket", StringComparison.OrdinalIgnoreCase))
            {
                RestoreCricketFromSavedState(state);
                return;
            }

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
            ClearTickerUndoState();
            _suppressTickerSync = true;
            try
            {
                _styledMessages.Clear();
                IEnumerable<Roche_Scoreboard.Models.MarqueeMessage> restoredSlots = state.TickerSlots.Count > 0
                    ? state.TickerSlots
                    : state.Messages.Select(message => new Roche_Scoreboard.Models.MarqueeMessage(message));
                foreach (Roche_Scoreboard.Models.MarqueeMessage slot in restoredSlots.Take(TickerSlotCount))
                    _styledMessages.Add(new Roche_Scoreboard.Models.MarqueeMessage(
                        (slot.Text ?? string.Empty), slot.TextColor, slot.HighlightColor, slot.IsEnabled));
                EnsureTickerSlots();
            }
            finally { _suppressTickerSync = false; }
            SyncTickerMessages();

            _finalsMode = state.FinalsMode;
            SettingsFinalsToggle.IsChecked = _finalsMode;
            UpdateSettingsFinalsCardVisuals();
            _weatherLocation = state.WeatherLocation;
            _showControlPanelCountdown = state.ShowControlPanelCountdown && _match.ClockMode == ClockMode.CountUp;
            SettingsShowControlPanelCountdown.IsChecked = state.ShowControlPanelCountdown;
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
                    MatchLogEmptyHint.IsVisible = false;
                if (MatchLogCountBadge is not null)
                    MatchLogCountBadge.Text = $"({_match.Events.Count})";

                ScoreEvent last = _match.Events[^1];
                string teamName = last.Team == TeamSide.Home ? _match.HomeName : _match.AwayName;
                string scoreType = last.Type == ScoreType.Goal ? "Goal" : "Behind";
                LastEventText.Text = $"Last: {teamName} {scoreType}";
            }

            // Reset layout/anim to defaults
            ApplyRememberedScorebugLayout();
            bool hasCustomVideo = !string.IsNullOrWhiteSpace(_homeGoalVideoPath) || !string.IsNullOrWhiteSpace(_awayGoalVideoPath);
            string rememberedAnimation = OperatorWorkflowPreferences.Load().AflGoalAnimation;
            ApplyRememberedGoalAnimation(!hasCustomVideo &&
                string.Equals(rememberedAnimation, "customVideo", StringComparison.OrdinalIgnoreCase)
                    ? "broadcast"
                    : rememberedAnimation);
            UpdateAnimCards();
            UpdatePresentationCards("scorebug");
            _activeScreenKey = "scorebug";

            // Skip sport selection and wizard — go straight to main content
            SportSelectionPanel.IsVisible = false;
            SetupWizardPanel.IsVisible = false;
            MainContent.Opacity = 1;
            MainContent.IsVisible = true;
            RestoreLastMainTabSelection();

            _sportMode = SportMode.AFL;
            _aflMatchReady = true;
            EnsureScoreboardWindow();
            if (_displayWindow is { IsVisible: false }) _displayWindow.Show();
            if (_scorebug is not null)
                SwitchToScreen(_scorebug, "Live Scorebug");

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
            if (_displayWindow != null)
            {
                if (!_displayWindow.IsVisible) _displayWindow.Show();
                return;
            }

            _scorebug = new ScorebugControl { IsVisible = _aflMatchReady ? true : false };
            _scorebug.SetGoalAnimationStyle(GetSelectedGoalAnimationStyle());
            _outputScreensaver = new OutputScreensaverControl
            {
                IsVisible = _aflMatchReady || !_screensaverEnabled ? false : true
            };
            _outputScreensaver.Configure(_screensaverMediaPath);
            _quarterBreakScorebug = new QuarterBreakScorebugControl { IsVisible = false };
            _breakScreen = new BreakScreenControl { IsVisible = false };
            _breakScreen.SetFinalsMode(_finalsMode);
            _scoreworm = new ScorewormControl { IsVisible = false };
            _statsScreen = new StatsScreenControl { IsVisible = false };
            _videoPlayer = new VideoPlayerControl { IsVisible = false };
            _weatherScreen = new WeatherScreenControl { IsVisible = false };

            _displayContainer = new Grid { ClipToBounds = true };
            _displayContainer.Children.Add(_outputScreensaver);
            _displayContainer.Children.Add(_scorebug);
            _displayContainer.Children.Add(_quarterBreakScorebug);
            _displayContainer.Children.Add(_breakScreen);
            _displayContainer.Children.Add(_scoreworm);
            _displayContainer.Children.Add(_statsScreen);
            _displayContainer.Children.Add(_videoPlayer);
            _displayContainer.Children.Add(_weatherScreen);

            // _videoPlayer is the sole MP4 surface: goal videos and
            // presentation videos both route through it. Push it above the
            // scorebug/other screens so toggling IsVisible=Visible cleanly
            // takes over the output window.
            _videoPlayer.ZIndex = 200;

            ApplyFinalsModeToOutputs();

            // The live VisualBrush mirror is attached lazily when the operator
            // opens the VIDEO tab — see MainTabControl_SelectionChanged. A
            // permanent attachment forces WPF to re-rasterise the entire
            // scoreboard for every frame regardless of whether anyone is
            // looking at the preview, which is the dominant background cost
            // on lower-spec devices.

            // Retro CRT transition overlay — hidden until a retro transition fires
            _retroCrtOverlay = new NativeBorder
            {
                Background = Brushes.Black,
                Opacity = 0,
                IsVisible = false,
                IsHitTestVisible = false
            };
            _retroCrtOverlay.ZIndex = 300;
            _displayContainer.Children.Add(_retroCrtOverlay);

            // Calibration overlay — hidden by default; toggled with Ctrl+Shift+C
            // when the display window has focus. Sits above everything so the
            // operator can see the exact bounds of the output window.
            _calibrationOverlay = new CalibrationOverlay { IsVisible = false };
            _calibrationOverlay.ZIndex = 400;
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

            _displayWindow = new NativeWindow
            {
                Title = $"Roche Scoreboard v{ProductionVersion.CurrentVersion.ToString(3)} | Display",
                SystemDecorations = Avalonia.Controls.SystemDecorations.Full,
                // AllowsTransparency must stay false here: libvlc's VideoView
                // hosts a child HWND for the video surface and child HWNDs
                // cannot render inside a layered WPF window. Leaving this
                // true caused VLC to spawn its own floating window for
                // playback instead of drawing into the scoreboard surface.
                
                CanResize = true,
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
            

            // Restore the operator's last-saved AFL display window layout if any.
            // Falls back to the 386×193 capture-area default at (0,0) — top-left of the primary monitor.
            var savedAfl = WindowLayoutStorage.Get(WindowLayoutStorage.AflDisplayKey);
            if (savedAfl is not null)
                WindowLayoutStorage.Apply(_displayWindow, savedAfl);
            else
            {
                OperatorWorkflowPreferencesData outputDefaults = OperatorWorkflowPreferences.Load();
                PixelSizeHelper.SetWindowPixels(
                    _displayWindow,
                    outputDefaults.OutputWindowWidth,
                    outputDefaults.OutputWindowHeight);
            }

            _displayWindow.Closed += (_, __) =>
            {
                // Invalidate callbacks tied to the old display controls before
                // their references are cleared. A late storyboard or VLC event
                // must not complete an animation on a newly opened output.
                ResetScoreAnimationQueue();
                _rotationTimer?.Stop();
                _rotationTimer = null;
                StopStatsBarTimer();
                ExitVideoTakeover();
                _videoPlayer?.Stop();
                DetachLiveMirror();
                _displayWindow = null;
                _scorebug = null;
                _outputScreensaver = null;
                _quarterBreakScorebug = null;
                _breakScreen = null;
                _scoreworm = null;
                _statsScreen = null;
                _videoPlayer = null;
                _weatherScreen = null;
                _retroCrtOverlay = null;
                _calibrationOverlay = null;
                _displayContainer = null;
                _showingBreakScreen = false;
                _overlayQueueWired = false;
                CancelQueuedOverlays();
                _periodicOverlayReserved = false;
                _autoOverlayDelayTimer?.Stop();
                _autoOverlayDelayTimer = null;
                _autoOverlayDelayCancellation?.Invoke();
                _autoOverlayDelayCancellation = null;
                _scoringRunDelayTimer?.Stop();
                _scoringRunDelayTimer = null;
                UpdateWindowToggleVisual();
            };

            HookDimensionMirror(_displayWindow);

            // Hard opt-out from Windows 11 DWM rounded corners + accent border.
            // Without this, WindowStyle.None windows get curved edges and a
            // 1px coloured border on some machines (notably the scoreboard PC).

            // Ctrl+Shift+C toggles the calibration overlay on the display window.
            _displayWindow.KeyDown += DisplayWindow_PreviewKeyDown;

            _displayWindow.Loaded += (_, __) =>
            {
                // Don't snap back to (0,0) if we restored a saved layout above.
                if (savedAfl is null)
                {
                    _displayWindow.Left = 0;
                    _displayWindow.Top = 0;
                }
            };

            _displayWindow.Show();
            PushAllToScoreboard();
            UpdateWindowToggleVisual();

            AnimationRadio_Changed(this, new RoutedEventArgs());
        }

        private void RestoreCricketFromSavedState(GameState state)
        {
            var saved = state.CricketMatchState;
            if (saved is null || saved.AllInnings.Count == 0) return;

            _sportMode = SportMode.Cricket;
            PersistLastMode(SportMode.Cricket);
            SportSelectionPanel.IsVisible = false;
            SetupWizardPanel.IsVisible = false;
            CricketSetupWizardPanel.IsVisible = false;

            var setup = new CricketSetupResult
            {
                TeamAName = saved.TeamAName,
                TeamAAbbr = saved.TeamAAbbr,
                TeamAPrimaryColor = saved.TeamAPrimaryColor,
                TeamASecondaryColor = saved.TeamASecondaryColor,
                TeamALogoPath = saved.TeamALogoPath,
                TeamAPlayers = saved.TeamAPlayers.Select(p => p.Clone()).ToList(),
                TeamBName = saved.TeamBName,
                TeamBAbbr = saved.TeamBAbbr,
                TeamBPrimaryColor = saved.TeamBPrimaryColor,
                TeamBSecondaryColor = saved.TeamBSecondaryColor,
                TeamBLogoPath = saved.TeamBLogoPath,
                TeamBPlayers = saved.TeamBPlayers.Select(p => p.Clone()).ToList(),
                Format = saved.Format,
                TotalOvers = saved.TotalOvers,
                TeamABatsFirst = saved.TeamABatsFirst,
                Messages = [.. saved.Messages]
            };

            FinalizeCricketSetup(setup, promptForInitialSelection: false);
            _cricketMatch?.LoadState(saved);
            if (_cricketMatch != null)
            {
                _cricketScorebug?.UpdateFromMatch(_cricketMatch);
                CricketControlPanelView.ResumePendingSelection();
            }
        }

        private void ApplyFinalsModeToOutputs()
        {
            _scorebug?.SetFinalsMode(_finalsMode);
            _quarterBreakScorebug?.SetFinalsMode(_finalsMode);
            _breakScreen?.SetFinalsMode(_finalsMode);
            _scoreworm?.SetFinalsMode(_finalsMode);
            _statsScreen?.SetFinalsMode(_finalsMode);
            _weatherScreen?.SetFinalsMode(_finalsMode);

            // LibVLC renders through a native child HWND, so WPF overlays
            // cannot paint on top of the video surface. A gold container and
            // inset frame keep finals identity visible in video mode without
            // touching or recolouring the footage itself.
            if (_displayContainer is not null)
            {
                _displayContainer.Background = _finalsMode ? Services.FinalsPalette.FrameBrush() : Brushes.Black;
            }
            if (_videoPlayer is not null)
                _videoPlayer.Margin = _finalsMode ? new Thickness(3) : new Thickness(0);

        }

        private void PushAllToScoreboard()
        {
            if (_scorebug == null || _displayWindow == null || !_displayWindow.IsVisible) return;

            _scorebug.SetTeams(_match.HomeName, _match.HomeAbbr, _match.AwayName, _match.AwayAbbr);
            _scorebug.SetLayout(LayoutExpandedRadio?.IsChecked == true
                ? ScorebugLayout.Distance
                : ScorebugLayout.Expanded);
            _scorebug.SetQuarter(_match.Quarter);

            if (!IsScoreAnimationActive && DateTime.UtcNow >= _deferScorePushUntilUtc)
            {
                _scorebug.SetScores(_match.HomeGoals, _match.HomeBehinds, _match.AwayGoals, _match.AwayBehinds);
            }

            // 5-minute warning edge detection lives in PushClockToScoreboard
            // (which runs every second from NativeRendering.Rendering).
            // Doing it here would only fire on MatchChanged — i.e. score
            // events — so a quiet stretch between 5:01 and the next score
            // would delay the warning until well past the 5-minute mark.

            _scorebug.SetTeamColors(
                (HomeColorBox.Text ?? string.Empty), (AwayColorBox.Text ?? string.Empty),
                (HomeSecondaryColorBox.Text ?? string.Empty), (AwaySecondaryColorBox.Text ?? string.Empty));
            _scorebug.SetLogos(_homeLogoPath, _awayLogoPath);
            ApplyLogoCropToScorebug();
            _scorebug.SetMarqueeMessages(GetActiveTickerMessages());
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
            // Keep pause-aware overlay clocks synchronized even when the
            // scorebug control is recreated while a match is already running.
            if (_match.ClockRunning) _scorebug.NotifyClockStarted();
            else _scorebug.NotifyClockStopped();

            // Fire the 5-minute warning the instant the countdown ticks through
            // 5:00. This runs every second via OnFrameRendering so a quiet game
            // (no goals/behinds) can never delay the trigger past the mark.
            CheckFiveMinuteWarning();

            // Automatic match-time overlays must not age or appear while the
            // operator has paused the clock, or while a quarter-break screen is
            // active. Manual overlay buttons remain available in either state.
            if (!_showingBreakScreen && _match.ClockRunning)
            {
                bool droughtOverlayAvailable = DateTime.UtcNow >= _nextDroughtOverlayAllowedAtUtc;
                if (droughtOverlayAvailable)
                {
                    AflDroughtSnapshot droughts = CalculateDroughts();
                    AflDroughtCandidate? trigger = null;

                    // A match-wide drought always owns the story. Individual
                    // team droughts are considered only while the match has had
                    // a score/goal inside the last ten minutes, preventing three
                    // near-identical alerts from competing.
                    if (droughts.Match.TenMinuteMilestone >= 10)
                    {
                        if (TryConsumeDroughtMilestone(droughts.Match)) trigger = droughts.Match;
                    }
                    else
                    {
                        foreach (AflDroughtCandidate candidate in new[] { droughts.Home, droughts.Away }
                                     .OrderByDescending(candidate => candidate.Elapsed))
                        {
                            if (!TryConsumeDroughtMilestone(candidate)) continue;
                            trigger = candidate;
                            break;
                        }
                    }

                    if (trigger is not null)
                    {
                        _nextDroughtOverlayAllowedAtUtc = DateTime.UtcNow + DroughtOverlayCooldown;
                        AflDroughtCandidate captured = trigger;
                        EnqueueAutoOverlay(() =>
                        {
                            if (_scorebug == null) return;
                            _overlayScheduler.NotifyEventDrivenOverlay();
                            if (captured.Scope == AflDroughtScope.Match)
                            {
                                _scorebug.ShowLastEventTime(captured.Kind.ToString(), captured.Elapsed);
                                return;
                            }

                            bool isHome = captured.Scope == AflDroughtScope.Home;
                            string teamName = isHome ? _match.HomeName : _match.AwayName;
                            string teamAbbr = isHome ? _match.HomeAbbr : _match.AwayAbbr;
                            var color = GetTeamColor(isHome);
                            string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                            _scorebug.ShowTeamDrought(teamName, teamAbbr,
                                captured.Kind.ToString(), captured.Elapsed, hex);
                        }, isPriority: true, onCancelled: () =>
                        {
                            // A ticker, reset, or quarter transition may cancel
                            // the pending story. Let the same milestone retry;
                            // it was never actually visible to the audience.
                            if (_shownDroughtMilestones.TryGetValue(captured.Key, out int shown)
                                && shown == captured.TenMinuteMilestone)
                                _shownDroughtMilestones.Remove(captured.Key);
                            _nextDroughtOverlayAllowedAtUtc = DateTime.MinValue;
                        });
                    }
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
        private void CheckFiveMinuteWarning()
        {
            if (_scorebug == null) return;

            bool matchCountdownActive = _match.ClockMode == ClockMode.Countdown && _match.ClockRunning;
            bool stoppageCountdownActive = _match.ClockMode == ClockMode.CountUp
                                           && _showControlPanelCountdown
                                           && _controlPanelCountdownRunning;
            if (matchCountdownActive || stoppageCountdownActive)
            {
                var threshold = TimeSpan.FromMinutes(5);
                TimeSpan remaining = matchCountdownActive
                    ? _match.DisplayClock
                    : _controlPanelCountdownRemaining;

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
            else if (_match.ClockMode == ClockMode.CountUp && !_showControlPanelCountdown)
            {
                // No countdown source exists. Reset for the next configured
                // countdown, but never abruptly pull an alert merely because
                // the operator paused a live clock.
                _fiveMinWarningShown = false;
                _previousRemainingTime = TimeSpan.MaxValue;
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

        private static IImage? LoadLogoPreview(string? path)
            => Services.ImageLoadHelper.Load(path);

        private static void ApplyImageCropTransform(Avalonia.Controls.Image image, double zoom, double x, double y)
        {
            image.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            TransformGroup group = new();
            group.Children.Add(new ScaleTransform(zoom, zoom));
            group.Children.Add(new TranslateTransform(x, y));
            image.RenderTransform = group;
        }

        private void PreviewButton_MouseEnter(object? sender, Avalonia.Input.PointerEventArgs e)
        {
            // Presentation preview box removed.
        }

        private void PreviewButton_MouseLeave(object? sender, Avalonia.Input.PointerEventArgs e)
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

        private void ShowScoreboard_Click(object? sender, RoutedEventArgs e)
        {
            SetSelectedPreview("presentation:window");
            EnsureScoreboardWindow();
            _displayWindow?.Activate();
        }

        // ----------------------------
        // Screen switching buttons
        // ----------------------------
        private void SwitchToScorebug_Click(object? sender, RoutedEventArgs e)
        {
            SetSelectedPreview("presentation:scorebug");
            _activeScreenKey = "scorebug";
            EnsureScoreboardWindow();
            if (_scorebug == null) return;
            SwitchToScreen(_scorebug, "Live Scorebug");
            BroadcastWebState();
        }

        private void SwitchToBreakScreen_Click(object? sender, RoutedEventArgs e)
        {
            SetSelectedPreview("presentation:summary");
            _activeScreenKey = "summary";
            EnsureScoreboardWindow();
            if (_breakScreen == null) return;
            ShowBreakScreen();
            BroadcastWebState();
        }

        private void SwitchToScoreworm_Click(object? sender, RoutedEventArgs e)
        {
            SetSelectedPreview("presentation:worm");
            _activeScreenKey = "worm";
            EnsureScoreboardWindow();
            if (_scoreworm == null) return;
            ShowScoreworm();
            BroadcastWebState();
        }

        private void SwitchToStatsScreen_Click(object? sender, RoutedEventArgs e)
        {
            SetSelectedPreview("presentation:stats");
            _activeScreenKey = "stats";
            EnsureScoreboardWindow();
            if (_statsScreen == null) return;
            ShowStatsScreen();
            BroadcastWebState();
        }

        private void SwitchToWeatherScreen_Click(object? sender, RoutedEventArgs e)
        {
            SetSelectedPreview("presentation:weather");
            _activeScreenKey = "weather";
            EnsureScoreboardWindow();
            if (_weatherScreen == null) return;
            ShowWeatherScreen();
            BroadcastWebState();
        }

        private void SwitchToScreensaver_Click(object? sender, RoutedEventArgs e)
            => ShowOutputScreensaver(manualSelection: true);

        private void ShowOutputScreensaver(bool manualSelection)
        {
            if (!manualSelection && !_screensaverEnabled)
                return;

            EnsureScoreboardWindow();
            if (_outputScreensaver is null) return;
            _outputScreensaver.Configure(_screensaverMediaPath);
            if (manualSelection)
            {
                SetSelectedPreview("presentation:screensaver");
                _activeScreenKey = "screensaver";
            }
            SwitchToScreen(_outputScreensaver, "Screensaver");
            UpdatePresentationCards("screensaver");
            BroadcastWebState();
        }

        private void ChooseScreensaverMedia_Click(object? sender, RoutedEventArgs e)
        {
            var dialog = new NativeOpenFileDialog
            {
                Title = "Choose output screensaver image or video",
                Filter = "Images and videos|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.mp4;*.mov;*.mkv;*.avi;*.webm|Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp|Videos|*.mp4;*.mov;*.mkv;*.avi;*.webm",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return;

            _screensaverEnabled = true;
            _screensaverMediaPath = dialog.FileName;
            OperatorWorkflowPreferences.Update(data =>
            {
                data.OutputScreensaverEnabled = true;
                data.OutputScreensaverMediaPath = _screensaverMediaPath;
            });
            _outputScreensaver?.Configure(_screensaverMediaPath);
            UpdateScreensaverSettingsStatus();
        }

        private void ResetScreensaverMedia_Click(object? sender, RoutedEventArgs e)
        {
            _screensaverEnabled = true;
            _screensaverMediaPath = null;
            OperatorWorkflowPreferences.Update(data =>
            {
                data.OutputScreensaverEnabled = true;
                data.OutputScreensaverMediaPath = null;
            });
            _outputScreensaver?.ShowDefault();
            UpdateScreensaverSettingsStatus();
        }

        private void UpdateScreensaverSettingsStatus()
        {
            if (ScreensaverMediaStatus is null) return;
            if (string.IsNullOrWhiteSpace(_screensaverMediaPath))
            {
                ScreensaverMediaStatus.Text = "Default animated sports scene";
                return;
            }
            if (!System.IO.File.Exists(_screensaverMediaPath))
            {
                ScreensaverMediaStatus.Text = "Saved media is unavailable · using the default sports scene";
                return;
            }

            string kind = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp" }
                .Contains(System.IO.Path.GetExtension(_screensaverMediaPath), StringComparer.OrdinalIgnoreCase)
                ? "IMAGE"
                : "VIDEO";
            ScreensaverMediaStatus.Text = $"{kind} · {System.IO.Path.GetFileName(_screensaverMediaPath)}";
        }

        // ----------------------------
        // Video presentation
        // ----------------------------
        private void SwitchToVideo_Click(object? sender, RoutedEventArgs e)
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
                StatusBadge.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
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
        private static void PulseElement(Control element, int durationMs)
        {
            if (element.RenderTransform is not ScaleTransform sc)
            {
                element.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
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

namespace Roche_Scoreboard { public partial class MainWindow {
    private ScaleTransform Sc => (ScaleTransform)ProductionNames.RegistryFor(this)["Sc"];
    private TranslateTransform Lift => (TranslateTransform)ProductionNames.RegistryFor(this)["Lift"];
    private ScaleTransform TbSc => (ScaleTransform)ProductionNames.RegistryFor(this)["TbSc"];
    private TranslateTransform TbLift => (TranslateTransform)ProductionNames.RegistryFor(this)["TbLift"];
    private ScaleTransform RbSc => (ScaleTransform)ProductionNames.RegistryFor(this)["RbSc"];
    private ScaleTransform ControlPanelCountdownEditPopupScale => (ScaleTransform)ProductionNames.RegistryFor(this)["ControlPanelCountdownEditPopupScale"];
    private TranslateTransform WindowToggleThumbTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["WindowToggleThumbTranslate"];
    private TranslateTransform SettingsControlPanelCountdownCardTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["SettingsControlPanelCountdownCardTranslate"];
    private ScaleTransform SettingsControlPanelCountdownCardScale => (ScaleTransform)ProductionNames.RegistryFor(this)["SettingsControlPanelCountdownCardScale"];
    private TranslateTransform SettingsControlPanelCountdownThumbTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["SettingsControlPanelCountdownThumbTranslate"];
    private void Native_AnimBroadcastCard_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; AnimBroadcastCard_Click(sender!, e); }
    private void Native_AnimCinematicCard_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; AnimCinematicCard_Click(sender!, e); }
    private void Native_AnimClassicCard_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; AnimClassicCard_Click(sender!, e); }
    private void Native_AnimCleanCard_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; AnimCleanCard_Click(sender!, e); }
    private void Native_AnimCustomVideoCard_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; AnimCustomVideoCard_Click(sender!, e); }
    private void Native_AnimElectricCard_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; AnimElectricCard_Click(sender!, e); }
    private void Native_AnimationRadio_Changed(object? sender, RoutedEventArgs e) { AnimationRadio_Changed(sender!, e); }
    private void Native_AutoOverlaysCard_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; AutoOverlaysCard_Click(sender!, e); }
    private void Native_AutoOverlaysToggle_Changed(object? sender, RoutedEventArgs e) { AutoOverlaysToggle_Changed(sender!, e); }
    private void Native_ClassicLayoutCard_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; ClassicLayoutCard_Click(sender!, e); }
    private void Native_ClearItemHighlightColor_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Right) return; ClearItemHighlightColor_Click(sender!, e); }
    private void Native_ClearNewHighlightColor_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Right) return; ClearNewHighlightColor_Click(sender!, e); }
    private void Native_ClockMode_Changed(object? sender, RoutedEventArgs e) { ClockMode_Changed(sender!, e); }
    private void Native_CopyCloudJoinCode_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; CopyCloudJoinCode_Click(sender!, e); }
    private void Native_DropperAwayColor_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; DropperAwayColor_Click(sender!, e); }
    private void Native_DropperAwaySecondaryColor_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; DropperAwaySecondaryColor_Click(sender!, e); }
    private void Native_DropperHomeColor_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; DropperHomeColor_Click(sender!, e); }
    private void Native_DropperHomeSecondaryColor_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; DropperHomeSecondaryColor_Click(sender!, e); }
    private void Native_ExpandedLayoutCard_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; ExpandedLayoutCard_Click(sender!, e); }
    private void Native_HeightMinus_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; HeightMinus_Click(sender!, e); }
    private void Native_HeightPlus_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; HeightPlus_Click(sender!, e); }
    private void Native_LayoutRadio_Changed(object? sender, RoutedEventArgs e) { LayoutRadio_Changed(sender!, e); }
    private void Native_MainTabControl_SelectionChanged(object? sender, SelectionChangedEventArgs e) { MainTabControl_SelectionChanged(sender!, e); }
    private void Native_MessageItem_TextChanged(object? sender, TextChangedEventArgs e) { MessageItem_TextChanged(sender!, e); }
    private void Native_NewMessageBox_TextChanged(object? sender, TextChangedEventArgs e) { NewMessageBox_TextChanged(sender!, e); }
    private void Native_OverlayAwayDrought_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; OverlayAwayDrought_Click(sender!, e); }
    private void Native_OverlayHomeDrought_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; OverlayHomeDrought_Click(sender!, e); }
    private void Native_OverlayLeadChanges_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; OverlayLeadChanges_Click(sender!, e); }
    private void Native_OverlayQtrScores_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; OverlayQtrScores_Click(sender!, e); }
    private void Native_OverlayRainForecast_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; OverlayRainForecast_Click(sender!, e); }
    private void Native_OverlayRecentScores_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; OverlayRecentScores_Click(sender!, e); }
    private void Native_OverlayScoreless_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; OverlayScoreless_Click(sender!, e); }
    private void Native_OverlayScoringRun_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; OverlayScoringRun_Click(sender!, e); }
    private void Native_OverlayStatsBar_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; OverlayStatsBar_Click(sender!, e); }
    private void Native_OverlayWarning_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; OverlayWarning_Click(sender!, e); }
    private void Native_OverlayWeatherForecast_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; OverlayWeatherForecast_Click(sender!, e); }
    private void Native_OverlayWeatherStats_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; OverlayWeatherStats_Click(sender!, e); }
    private void Native_OverlayWinProb_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; OverlayWinProb_Click(sender!, e); }
    private void Native_PickAwayColor_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; PickAwayColor_Click(sender!, e); }
    private void Native_PickAwaySecondaryColor_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; PickAwaySecondaryColor_Click(sender!, e); }
    private void Native_PickHomeColor_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; PickHomeColor_Click(sender!, e); }
    private void Native_PickHomeSecondaryColor_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; PickHomeSecondaryColor_Click(sender!, e); }
    private void Native_PresCardQuarterBreak_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; PresCardQuarterBreak_Click(sender!, e); }
    private void Native_PresCardScorebug_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; PresCardScorebug_Click(sender!, e); }
    private void Native_PresCardScreensaver_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; PresCardScreensaver_Click(sender!, e); }
    private void Native_PresCardStats_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; PresCardStats_Click(sender!, e); }
    private void Native_PresCardSummary_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; PresCardSummary_Click(sender!, e); }
    private void Native_PresCardWeather_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; PresCardWeather_Click(sender!, e); }
    private void Native_PresCardWindow_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; PresCardWindow_Click(sender!, e); }
    private void Native_PresCardWorm_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; PresCardWorm_Click(sender!, e); }
    private void Native_QuarterLabelCurrentBox_TextChanged(object? sender, TextChangedEventArgs e) { QuarterLabelCurrentBox_TextChanged(sender!, e); }
    private void Native_ResetCardSettings_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; ResetCardSettings_Click(sender!, e); }
    private void Native_ResizeGrip_MouseDown(object? sender, PointerPressedEventArgs e) { ResizeGrip_MouseDown(sender!, e); }
    private void Native_SettingsBreakScreenCard_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; SettingsBreakScreenCard_Click(sender!, e); }
    private void Native_SettingsBreakScreenToggle_Changed(object? sender, RoutedEventArgs e) { SettingsBreakScreenToggle_Changed(sender!, e); }
    private void Native_SettingsControlPanelCountdownOptionCard_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; SettingsControlPanelCountdownOptionCard_Click(sender!, e); }
    private void Native_SettingsCountUpCard_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; SettingsCountUpCard_Click(sender!, e); }
    private void Native_SettingsCountdownCard_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; SettingsCountdownCard_Click(sender!, e); }
    private void Native_SettingsFinalsCard_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; SettingsFinalsCard_Click(sender!, e); }
    private void Native_SettingsFinalsToggle_Changed(object? sender, RoutedEventArgs e) { SettingsFinalsToggle_Changed(sender!, e); }
    private void Native_SettingsHideTeamNamesCard_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; SettingsHideTeamNamesCard_Click(sender!, e); }
    private void Native_SettingsHideTeamNamesToggle_Changed(object? sender, RoutedEventArgs e) { SettingsHideTeamNamesToggle_Changed(sender!, e); }
    private void Native_SettingsMinutesMinus_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; SettingsMinutesMinus_Click(sender!, e); }
    private void Native_SettingsMinutesPlus_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; SettingsMinutesPlus_Click(sender!, e); }
    private void Native_SettingsNav_Advanced_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; SettingsNav_Advanced_Click(sender!, e); }
    private void Native_SettingsNav_Display_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; SettingsNav_Display_Click(sender!, e); }
    private void Native_SettingsNav_Keybinds_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; SettingsNav_Keybinds_Click(sender!, e); }
    private void Native_SettingsNav_Media_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; SettingsNav_Media_Click(sender!, e); }
    private void Native_SettingsNav_Overlays_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; SettingsNav_Overlays_Click(sender!, e); }
    private void Native_SettingsNav_Teams_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; SettingsNav_Teams_Click(sender!, e); }
    private void Native_SettingsSearchClear_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; SettingsSearchClear_Click(sender!, e); }
    private void Native_SettingsSearch_TextChanged(object? sender, TextChangedEventArgs e) { SettingsSearch_TextChanged(sender!, e); }
    private void Native_SettingsSecondsMinus_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; SettingsSecondsMinus_Click(sender!, e); }
    private void Native_SettingsSecondsPlus_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; SettingsSecondsPlus_Click(sender!, e); }
    private void Native_TeamSettings_TextChanged(object? sender, TextChangedEventArgs e) { TeamSettings_TextChanged(sender!, e); }
    private void Native_TickerSlotEnabled_Changed(object? sender, RoutedEventArgs e) { TickerSlotEnabled_Changed(sender!, e); }
    private void Native_VideoTabSeek_PreviewMouseLeftButtonUp(object? sender, PointerReleasedEventArgs e) { VideoTabSeek_PreviewMouseLeftButtonUp(sender!, e); }
    private void Native_VideoTabSpeed_Changed(object? sender, SelectionChangedEventArgs e) { VideoTabSpeed_Changed(sender!, e); }
    private void Native_WidthMinus_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; WidthMinus_Click(sender!, e); }
    private void Native_WidthPlus_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; WidthPlus_Click(sender!, e); }
} }

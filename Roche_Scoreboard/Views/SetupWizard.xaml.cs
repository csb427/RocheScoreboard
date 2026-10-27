using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using WinForms = System.Windows.Forms;
using DrawingColor = System.Drawing.Color;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using TextBox = System.Windows.Controls.TextBox;
using Border = System.Windows.Controls.Border;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Roche_Scoreboard.Views
{
    public partial class SetupWizard : System.Windows.Controls.UserControl
    {
        private void ResponsiveScrollHost_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
            => NestedScrollRouting.RoutePreviewMouseWheel(ResponsiveScrollHost, e);

        private List<TeamPreset> _presets;
        private string? _homeLogoPath;
        private string? _awayLogoPath;
        private string? _homeVideoPath;
        private string? _awayVideoPath;
        private int _currentStep = 1;
        private int _stepTransitionVersion;
        private Storyboard? _stepTransitionStoryboard;
        private DispatcherTimer? _ambientRestartTimer;

        private TeamPreset? _selectedHomePreset;
        private TeamPreset? _selectedAwayPreset;

        private Border? _dragTile;
        private System.Windows.Point _dragStartPos;
        private int _dragOrigIndex;
        private int _dragCurrentIndex;
        private bool _isDragging;
        private bool _dragIsHome;
        private double[] _slotCentersX = [];
        private double[] _slotCentersY = [];
        private const double DragThreshold = 6;
        private bool _suppressWeatherFilter;
        private bool _weatherBoxActivated;

        // ── Easing curves (matching sport selection screen) ──
        private static readonly CubicEase Smooth = new() { EasingMode = EasingMode.EaseOut };
        private static readonly SineEase Sine = new() { EasingMode = EasingMode.EaseInOut };


        internal static readonly string[] AustralianCities =
        [
            "Adelaide, Australia",
            "Albany, Australia",
            "Albury, Australia",
            "Alice Springs, Australia",
            "Armidale, Australia",
            "Ballarat, Australia",
            "Bathurst, Australia",
            "Bendigo, Australia",
            "Brisbane, Australia",
            "Broome, Australia",
            "Bundaberg, Australia",
            "Bunbury, Australia",
            "Burnie, Australia",
            "Cairns, Australia",
            "Caloundra, Australia",
            "Canberra, Australia",
            "Cessnock, Australia",
            "Coffs Harbour, Australia",
            "Darwin, Australia",
            "Devonport, Australia",
            "Dubbo, Australia",
            "Echuca, Australia",
            "Esperance, Australia",
            "Frankston, Australia",
            "Geelong, Australia",
            "Geraldton, Australia",
            "Gladstone, Australia",
            "Gold Coast, Australia",
            "Gosford, Australia",
            "Goulburn, Australia",
            "Grafton, Australia",
            "Gympie, Australia",
            "Hervey Bay, Australia",
            "Hobart, Australia",
            "Ipswich, Australia",
            "Kalgoorlie, Australia",
            "Karratha, Australia",
            "Katherine, Australia",
            "Launceston, Australia",
            "Lismore, Australia",
            "Lithgow, Australia",
            "Mackay, Australia",
            "Maitland, Australia",
            "Mandurah, Australia",
            "Maryborough, Australia",
            "Melbourne, Australia",
            "Mildura, Australia",
            "Mount Gambier, Australia",
            "Mount Isa, Australia",
            "Mudgee, Australia",
            "Murray Bridge, Australia",
            "Newcastle, Australia",
            "Noosa, Australia",
            "Nowra, Australia",
            "Orange, Australia",
            "Palmerston, Australia",
            "Parkes, Australia",
            "Perth, Australia",
            "Port Augusta, Australia",
            "Port Hedland, Australia",
            "Port Lincoln, Australia",
            "Port Macquarie, Australia",
            "Queanbeyan, Australia",
            "Rockhampton, Australia",
            "Rockingham, Australia",
            "Sale, Australia",
            "Shepparton, Australia",
            "Singleton, Australia",
            "Sunbury, Australia",
            "Sunshine Coast, Australia",
            "Sydney, Australia",
            "Tamworth, Australia",
            "Taree, Australia",
            "Toowoomba, Australia",
            "Townsville, Australia",
            "Traralgon, Australia",
            "Tweed Heads, Australia",
            "Victor Harbor, Australia",
            "Wagga Wagga, Australia",
            "Wangaratta, Australia",
            "Warragul, Australia",
            "Warrnambool, Australia",
            "Whyalla, Australia",
            "Wodonga, Australia",
            "Wollongong, Australia",
            "Yeppoon, Australia"
        ];

        public SetupResult? Result { get; private set; }
        public event Action<SetupResult>? SetupCompleted;
        public event Action? BackToSportSelection;

        private const int TotalSteps = 3;

        // ── Styled marquee messages (parity with main message editor) ──
        private readonly System.Collections.ObjectModel.ObservableCollection<MarqueeMessage> _setupMessages = [];
        private string? _newMessageTextColor;       // null = default (white)
        private string? _newMessageHighlightColor;  // null = no highlight

        public SetupWizard()
        {
            InitializeComponent();
            WizardFlowDesign.Enable(this);
            SetupMessageList.ItemsSource = _setupMessages;
            _presets = PresetStorage.LoadAll();
            RefreshPresetPanels();

            SetupWeatherLocation.AddHandler(
                System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler(SetupWeatherLocation_TextChanged));
            SetupWeatherLocation.GotFocus += (_, _) => _weatherBoxActivated = true;
            SetupWeatherLocation.DropDownOpened += (_, _) => _weatherBoxActivated = true;

            Loaded += OnLoaded;
            Unloaded += OnWizardUnloaded;
            IsVisibleChanged += (_, args) => { if (args.NewValue is false) OnWizardUnloaded(this, new RoutedEventArgs()); };
        }

        // ═══════════════════════════════════════════════════════
        //  ENTRY ANIMATION — staggered cinematic reveal
        // ═══════════════════════════════════════════════════════

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateClockModeVisuals();
            UpdateControlPanelCountdownOptionVisuals();
            ShowStep(1, animate: false);
            PlayEntryAnimation();
        }

        private void SettleWizardMotion()
        {
            foreach (var element in new UIElement[] { HeaderPanel, StepIndicator, HomePresetCard, AwayPresetCard, HomeTeamCard, AwayTeamCard,
                HomeHeaderGroup, AwayHeaderGroup, ClockHeaderGroup, MsgHeaderGroup, NavBar, BottomAccent })
            { element.BeginAnimation(OpacityProperty, null); element.Opacity = 1; }
            foreach (var shift in new[] { HeaderTranslate, StepIndicatorTranslate, HomePresetTranslate, AwayPresetTranslate,
                HomeCardTranslate, AwayCardTranslate, HomeHeaderTranslate, AwayHeaderTranslate, ClockHeaderTranslate, MsgHeaderTranslate, NavBarTranslate, BeamTranslate })
            { shift.BeginAnimation(TranslateTransform.XProperty, null); shift.BeginAnimation(TranslateTransform.YProperty, null); shift.X = shift.Y = 0; }
            DividerScale.BeginAnimation(ScaleTransform.ScaleYProperty, null); DividerScale.ScaleY = 1;
            foreach (var glow in new UIElement[] { HomeGlow, AwayGlow, BgGlow, ClockGlow, MsgGlow, DividerGlowLine })
            { glow.BeginAnimation(OpacityProperty, null); glow.Opacity = .22; }
            _countdownOptionHideTimer?.Stop(); _countdownOptionHideTimer = null;
            ControlPanelCountdownOptionCard.BeginAnimation(OpacityProperty, null); ControlPanelCountdownOptionCard.Opacity = 1;
            ControlPanelCountdownCardTranslate.BeginAnimation(TranslateTransform.YProperty, null); ControlPanelCountdownCardTranslate.Y = 0;
            ControlPanelCountdownCardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null); ControlPanelCountdownCardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            ControlPanelCountdownCardScale.ScaleX = ControlPanelCountdownCardScale.ScaleY = 1;
            bool showCountdownOption = SetupCountUp.IsChecked == true;
            ControlPanelCountdownOptionCard.Visibility = showCountdownOption ? Visibility.Visible : Visibility.Collapsed;
            ControlPanelCountdownOptionCard.IsHitTestVisible = showCountdownOption;
            _countdownOptionCardWasCollapsed = !showCountdownOption;
            ControlPanelCountdownThumbTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            ControlPanelCountdownThumbTranslate.X = SetupControlPanelCountdown.IsChecked == true ? 26 : 0;
            ControlPanelCountdownToggleLabel.BeginAnimation(OpacityProperty, null); ControlPanelCountdownToggleLabel.Opacity = 1;
            if (ControlPanelCountdownToggleTrack.Background is SolidColorBrush track)
            {
                if (track.IsFrozen) { track = track.Clone(); ControlPanelCountdownToggleTrack.Background = track; }
                track.BeginAnimation(SolidColorBrush.ColorProperty, null); track.Color = (Color)ColorConverter.ConvertFromString(SetupControlPanelCountdown.IsChecked == true ? "#1F6FEB" : "#4B5563");
            }
        }
        private void OnWizardUnloaded(object sender, RoutedEventArgs args)
        {
            _stepTransitionVersion++;
            _ambientRestartTimer?.Stop(); _ambientRestartTimer = null;
            _countdownOptionHideTimer?.Stop(); _countdownOptionHideTimer = null;
            if (_stepTransitionStoryboard is not null) { _stepTransitionStoryboard.Remove(this); _stepTransitionStoryboard = null; }
            SettleWizardMotion();
            ShowStep(_currentStep, false);
        }
        private void PlayEntryAnimation()
        {
            SettleWizardMotion();
            if (!OperatorMotion.Enabled || !IsLoaded) return;
            int index = 0;
            foreach (var element in new UIElement[] { HeaderPanel, StepIndicator, HomePresetCard, AwayPresetCard, HomeTeamCard, AwayTeamCard, NavBar })
            {
                element.BeginAnimation(OpacityProperty, new DoubleAnimation(.82, 1, OperatorMotion.Page)
                { BeginTime = TimeSpan.FromMilliseconds(Math.Min(index++, 3) * OperatorMotion.Stagger.TotalMilliseconds), FillBehavior = FillBehavior.Stop, EasingFunction = OperatorMotion.SmoothOut });
            }
            StartAmbientAnimations();
        }
        // ═══════════════════════════════════════════════════════
        //  AMBIENT ANIMATIONS — continuous atmosphere
        // ═══════════════════════════════════════════════════════

        private void StartAmbientAnimations()
        {
            HomeGlow.BeginAnimation(OpacityProperty, null);
            AwayGlow.BeginAnimation(OpacityProperty, null);
            BgGlow.BeginAnimation(OpacityProperty, null);
            BeamTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            HomeGlow.Opacity = AwayGlow.Opacity = 0;
            WizardFlowDesign.StopDecoration(this);
            BgGlow.Opacity = 0;
            BeamTranslate.X = -300;
        }

        // ═══════════════════════════════════════════════════════
        //  HOVER — nav button scale effects
        // ═══════════════════════════════════════════════════════



        // ═══════════════════════════════════════════════════════
        //  STEP NAVIGATION — cinematic transitions
        // ═══════════════════════════════════════════════════════

        private static readonly SolidColorBrush s_accentBrush = new((Color)ColorConverter.ConvertFromString("#58A6FF"));
        private static readonly SolidColorBrush s_surfaceBrush = new((Color)ColorConverter.ConvertFromString("#242427"));
        private static readonly SolidColorBrush s_textMutedBrush = new((Color)ColorConverter.ConvertFromString("#8E8E98"));
        private static readonly SolidColorBrush s_whiteBrush = new(Colors.White);
        private static readonly SolidColorBrush s_edgeBrush = new((Color)ColorConverter.ConvertFromString("#3A3A40"));

        private void ShowStep(int step, bool animate = true)
        {
            int previous = _currentStep;
            _currentStep = Math.Clamp(step, 1, TotalSteps);
            int version = ++_stepTransitionVersion;
            _ambientRestartTimer?.Stop(); _ambientRestartTimer = null;
            if (_stepTransitionStoryboard is not null) { _stepTransitionStoryboard.Remove(this); _stepTransitionStoryboard = null; }
            FlashOverlay.BeginAnimation(OpacityProperty, null); FlashOverlay.Opacity = 0;
            FrameworkElement[] panels = [TeamsStepContainer, Step3Panel, Step4Panel];
            var active = panels[_currentStep - 1];
            foreach (var panel in panels)
            {
                panel.BeginAnimation(OpacityProperty, null); panel.Opacity = 1;
                if (panel.RenderTransform is TranslateTransform old) old.BeginAnimation(TranslateTransform.XProperty, null);
                panel.RenderTransform = Transform.Identity;
                panel.Visibility = ReferenceEquals(panel, active) ? Visibility.Visible : Visibility.Collapsed;
            }
            SettleWizardMotion();
            if (animate && previous != _currentStep && OperatorMotion.Enabled && IsLoaded)
            {
                var shift = new TranslateTransform(); active.RenderTransform = shift;
                var storyboard = new Storyboard();
                var fade = new DoubleAnimation(.82, 1, OperatorMotion.Page) { FillBehavior = FillBehavior.Stop, EasingFunction = OperatorMotion.SmoothOut };
                Storyboard.SetTarget(fade, active); Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty)); storyboard.Children.Add(fade);
                var slide = new DoubleAnimation(_currentStep > previous ? 8 : -8, 0, OperatorMotion.Page) { FillBehavior = FillBehavior.Stop, EasingFunction = OperatorMotion.SmoothOut };
                Storyboard.SetTarget(slide, shift); Storyboard.SetTargetProperty(slide, new PropertyPath(TranslateTransform.XProperty)); storyboard.Children.Add(slide);
                storyboard.Completed += (_, _) =>
                {
                    if (version != _stepTransitionVersion) return;
                    storyboard.Remove(this); active.Opacity = 1; active.RenderTransform = Transform.Identity; _stepTransitionStoryboard = null;
                };
                _stepTransitionStoryboard = storyboard; storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
            }
            BackButton.Visibility = Visibility.Visible;
            NextButtonText.Text = _currentStep == TotalSteps ? "Start match" : "Continue";
            WizardFlowDesign.SelectStep(this, _currentStep, TotalSteps);
            UpdateStepDot(Step1Dot, Step1Label, true, _currentStep == 1);
            UpdateStepDot(Step2Dot, Step2Label, _currentStep >= 2, _currentStep == 2);
            UpdateStepDot(Step3Dot, Step3Label, _currentStep >= 3, _currentStep == 3);
            UpdateSetupClockSummary();
        }
        private void UpdateStepDot(Border dot, TextBlock label, bool reached, bool active)
        {
            dot.Background = reached ? s_accentBrush : s_surfaceBrush;
            dot.BorderBrush = reached ? s_accentBrush : s_edgeBrush;
            var dotText = (TextBlock)dot.Child;
            dotText.Foreground = reached ? s_whiteBrush : s_textMutedBrush;
            // The shared segmented lens owns label contrast and selection ink.

            // Glow effect on active dot
            if (dot.Effect is DropShadowEffect glow)
                glow.Opacity = 0;
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep < TotalSteps)
                ShowStep(_currentStep + 1);
            else
                FinishWizard();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 1)
                ShowStep(_currentStep - 1);
            else
                BackToSportSelection?.Invoke();
        }

        private void GoToStep1_Click(object sender, RoutedEventArgs e) => ShowStep(1);
        private void GoToStep2_Click(object sender, RoutedEventArgs e) => ShowStep(2);
        private void GoToStep3_Click(object sender, RoutedEventArgs e) => ShowStep(3);

        // ═══════════════════════════════════════════════════════
        //  RESET
        // ═══════════════════════════════════════════════════════

        public void Reset()
        {
            OperatorWorkflowPreferencesData remembered = OperatorWorkflowPreferences.Load();
            Result = null;
            _homeLogoPath = null;
            _awayLogoPath = null;
            _homeVideoPath = null;
            _awayVideoPath = null;
            _selectedHomePreset = null;
            _selectedAwayPreset = null;
            _currentStep = 1;

            SetupHomeNameBox.Text = "";
            SetupHomeAbbrBox.Text = "";
            SetupHomeColorBox.Text = "#0A2A6A";
            SetupHomeSecondaryBox.Text = "#FFFFFF";
            TrySetPreview(SetupHomeColorPreview, "#0A2A6A");
            TrySetPreview(SetupHomeSecondaryPreview, "#FFFFFF");
            SetupHomeLogoText.Text = "(none)";
            SetupHomeLogoImage.Source = null;
            SetupHomeLogoPreview.Visibility = Visibility.Collapsed;
            SetupHomeVideoText.Text = "(none)";

            SetupAwayNameBox.Text = "";
            SetupAwayAbbrBox.Text = "";
            SetupAwayColorBox.Text = "#7A1A1A";
            SetupAwaySecondaryBox.Text = "#FFFFFF";
            TrySetPreview(SetupAwayColorPreview, "#7A1A1A");
            TrySetPreview(SetupAwaySecondaryPreview, "#FFFFFF");
            SetupAwayLogoText.Text = "(none)";
            SetupAwayLogoImage.Source = null;
            SetupAwayLogoPreview.Visibility = Visibility.Collapsed;
            SetupAwayVideoText.Text = "(none)";

            SetupCountdown.IsChecked = remembered.AflCountdown;
            SetupCountUp.IsChecked = !remembered.AflCountdown;
            SetupControlPanelCountdown.IsChecked = remembered.AflControlPanelCountdown;
            SetupQuarterMinutes.Text = remembered.AflQuarterMinutes.ToString();
            SetupQuarterSeconds.Text = remembered.AflQuarterSeconds.ToString("00");
            SetupFinalsMode.IsChecked = false;
            DurationMinutesDisplay.Text = remembered.AflQuarterMinutes.ToString();
            UpdateClockModeVisuals();
            _countdownOptionCardWasCollapsed = true; // force entrance anim on next reveal
            UpdateControlPanelCountdownOptionVisuals();
            HighlightActivePresetTile(remembered.AflQuarterMinutes);
            UpdateFinalsToggleVisuals();
            UpdateSetupClockSummary();

            _setupMessages.Clear();

            _suppressWeatherFilter = true;
            _weatherBoxActivated = false;
            SetupWeatherLocation.Text = remembered.AflWeatherLocation;
            _suppressWeatherFilter = false;

            _presets = PresetStorage.LoadAll();
            RefreshPresetPanels();
            // The snapshot reflects what was actually used, including any
            // colour/name adjustments made after selecting a preset.
            TeamPreset? rememberedHome = remembered.LastHomeTeam ?? FindRememberedPreset(remembered.LastHomePresetKey);
            TeamPreset? rememberedAway = remembered.LastAwayTeam ?? FindRememberedPreset(remembered.LastAwayPresetKey);
            if (rememberedHome is not null) ApplyPresetToHome(rememberedHome);
            if (rememberedAway is not null) ApplyPresetToAway(rememberedAway);
            ShowStep(1, animate: false);

            // Replay the cinematic entry
            PlayEntryAnimation();
        }

        // ═══════════════════════════════════════════════════════
        //  FINISH WIZARD
        // ═══════════════════════════════════════════════════════

        private void FinishWizard()
        {
            int.TryParse(SetupQuarterMinutes.Text, out int mins);
            int.TryParse(SetupQuarterSeconds.Text, out int secs);

            var messages = new List<MarqueeMessage>();
            foreach (var item in _setupMessages)
            {
                if (!string.IsNullOrWhiteSpace(item.Text))
                    messages.Add(new MarqueeMessage(item.Text, item.TextColor, item.HighlightColor));
            }

            string homeName = SetupHomeNameBox.Text.Trim();
            string homeAbbr = SetupHomeAbbrBox.Text.Trim();
            string awayName = SetupAwayNameBox.Text.Trim();
            string awayAbbr = SetupAwayAbbrBox.Text.Trim();

            Result = new SetupResult
            {
                HomeName = string.IsNullOrWhiteSpace(homeName) ? "Home Team" : homeName,
                HomeAbbr = string.IsNullOrWhiteSpace(homeAbbr) ? "HOM" : homeAbbr,
                HomePrimaryColor = SetupHomeColorBox.Text.Trim(),
                HomeSecondaryColor = SetupHomeSecondaryBox.Text.Trim(),
                HomeLogoPath = _homeLogoPath,
                HomeGoalVideoPath = _homeVideoPath,

                AwayName = string.IsNullOrWhiteSpace(awayName) ? "Away Team" : awayName,
                AwayAbbr = string.IsNullOrWhiteSpace(awayAbbr) ? "AWA" : awayAbbr,
                AwayPrimaryColor = SetupAwayColorBox.Text.Trim(),
                AwaySecondaryColor = SetupAwaySecondaryBox.Text.Trim(),
                AwayLogoPath = _awayLogoPath,
                AwayGoalVideoPath = _awayVideoPath,

                ClockMode = SetupCountdown.IsChecked == true ? ClockMode.Countdown : ClockMode.CountUp,
                QuarterMinutes = mins > 0 ? mins : 20,
                QuarterSeconds = secs >= 0 ? secs : 0,
                ShowControlPanelCountdown = SetupCountUp.IsChecked == true && SetupControlPanelCountdown.IsChecked == true,
                ControlPanelCountdownMinutes = mins > 0 ? mins : 20,
                ControlPanelCountdownSeconds = secs >= 0 ? secs : 0,
                FinalsMode = SetupFinalsMode.IsChecked == true,

                WeatherLocation = string.IsNullOrWhiteSpace(SetupWeatherLocation.Text) ? null : SetupWeatherLocation.Text.Trim(),

                Messages = messages
            };

            // Remember venue/operator defaults only after a valid setup is
            // completed. Finals mode and messages remain deliberately per-match.
            OperatorWorkflowPreferences.Update(remembered =>
            {
                remembered.AflCountdown = SetupCountdown.IsChecked == true;
                remembered.AflQuarterMinutes = Result.QuarterMinutes;
                remembered.AflQuarterSeconds = Result.QuarterSeconds;
                remembered.AflControlPanelCountdown = SetupControlPanelCountdown.IsChecked == true;
                remembered.AflWeatherLocation = Result.WeatherLocation ?? "Melbourne, Australia";
                remembered.LastHomePresetKey = GetPresetKey(_selectedHomePreset);
                remembered.LastAwayPresetKey = GetPresetKey(_selectedAwayPreset);
                remembered.LastHomeTeam = CreateRememberedTeam(
                    Result.HomeName, Result.HomeAbbr, Result.HomePrimaryColor, Result.HomeSecondaryColor,
                    Result.HomeLogoPath, Result.HomeGoalVideoPath, _selectedHomePreset?.LibraryTeamId);
                remembered.LastAwayTeam = CreateRememberedTeam(
                    Result.AwayName, Result.AwayAbbr, Result.AwayPrimaryColor, Result.AwaySecondaryColor,
                    Result.AwayLogoPath, Result.AwayGoalVideoPath, _selectedAwayPreset?.LibraryTeamId);
            });

            SetupCompleted?.Invoke(Result);
        }

        private TeamPreset? FindRememberedPreset(string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            return _presets.FirstOrDefault(p => string.Equals(GetPresetKey(p), key, StringComparison.OrdinalIgnoreCase));
        }

        private static string? GetPresetKey(TeamPreset? preset)
        {
            if (preset is null) return null;
            return !string.IsNullOrWhiteSpace(preset.LibraryTeamId)
                ? "library:" + preset.LibraryTeamId
                : "preset:" + preset.PresetName.Trim();
        }

        private static TeamPreset CreateRememberedTeam(
            string name, string abbreviation, string primary, string secondary,
            string? logoPath, string? goalVideoPath, string? libraryTeamId) => new()
        {
            PresetName = name,
            TeamName = name,
            Abbreviation = abbreviation,
            PrimaryColor = primary,
            SecondaryColor = secondary,
            LogoPath = logoPath,
            GoalVideoPath = goalVideoPath,
            LibraryTeamId = libraryTeamId
        };

        // ═══════════════════════════════════════════════════════
        //  CLOCK MODE
        // ═══════════════════════════════════════════════════════

        private void CountUpCard_Click(object sender, MouseButtonEventArgs e)
        {
            SetupCountUp.IsChecked = true;
        }

        private void CountdownCard_Click(object sender, MouseButtonEventArgs e)
        {
            SetupCountdown.IsChecked = true;
        }

        private void ControlPanelCountdownOptionCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (SetupCountUp.IsChecked != true)
                return;

            SetupControlPanelCountdown.IsChecked = SetupControlPanelCountdown.IsChecked != true;
            UpdateClockModeVisuals();
            UpdateControlPanelCountdownOptionVisuals();
        }

        private void SetupClockMode_Changed(object sender, RoutedEventArgs e)
        {
            if(!IsInitialized)return;
            UpdateClockModeVisuals();
            UpdateControlPanelCountdownOptionVisuals();
            UpdateSetupClockSummary();
        }

        private void UpdateClockModeVisuals()
        {
            if(!IsInitialized || CountUpCard is null || CountdownCard is null || QuarterLengthControls is null)return;
            bool isCountdown=SetupCountdown.IsChecked==true;
            WizardFlowDesign.StyleChoice(CountUpCard,!isCountdown);WizardFlowDesign.StyleChoice(CountdownCard,isCountdown);
            CountUpSelected.Visibility=isCountdown?Visibility.Collapsed:Visibility.Visible;
            CountdownSelected.Visibility=isCountdown?Visibility.Visible:Visibility.Collapsed;
            foreach(var mark in new[]{CountUpSelected,CountdownSelected})
            {mark.Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString("#368EEE"));if(mark.Child is TextBlock text)text.Foreground=System.Windows.Media.Brushes.White;}
            bool editable=isCountdown || SetupControlPanelCountdown.IsChecked==true;
            QuarterLengthControls.IsEnabled=editable;QuarterLengthControls.Opacity=editable?1:.4;
            DurationDisabledOverlay.Visibility=editable?Visibility.Collapsed:Visibility.Visible;
            SetupDurationPanel.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SurfaceBrush");
            SetupDurationPanel.SetResourceReference(Border.BorderBrushProperty,"ControlCentre.BorderBrush");
            DurationGlowBg.Opacity=0;CountUpIcon.Effect=CountdownIcon.Effect=null;
            HighlightActivePresetTile(int.TryParse(SetupQuarterMinutes.Text,out int minutes)?minutes:20);
        }

        // Tracks previous visibility state so we only play the entrance animation once per appearance.
        private bool _countdownOptionCardWasCollapsed = true;
        private DispatcherTimer? _countdownOptionHideTimer;

        private void UpdateControlPanelCountdownOptionVisuals()
        {
            if (ControlPanelCountdownOptionCard is null)
                return;

            var controlPanelCountdownHintText = FindName("ControlPanelCountdownHintText") as TextBlock;

            bool isVisible = SetupCountUp.IsChecked == true;
            bool isEnabled = SetupControlPanelCountdown.IsChecked == true;
            bool wasCollapsed = _countdownOptionCardWasCollapsed;
            _countdownOptionCardWasCollapsed = !isVisible;

            _countdownOptionHideTimer?.Stop();
            _countdownOptionHideTimer = null;

            if (isVisible)
            {
                ControlPanelCountdownOptionCard.BeginAnimation(OpacityProperty, null);
                ControlPanelCountdownCardTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                ControlPanelCountdownCardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                ControlPanelCountdownCardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

                ControlPanelCountdownOptionCard.Visibility = Visibility.Visible;
                ControlPanelCountdownOptionCard.IsHitTestVisible = true;

                if (wasCollapsed)
                {
                    ControlPanelCountdownOptionCard.Opacity = 0;
                    ControlPanelCountdownCardTranslate.Y = 12;
                    ControlPanelCountdownCardScale.ScaleX = 0.98;
                    ControlPanelCountdownCardScale.ScaleY = 0.98;

                    Animate(ControlPanelCountdownOptionCard, OpacityProperty, 1, 220, easing: Smooth);
                    Animate(ControlPanelCountdownCardTranslate, TranslateTransform.YProperty, 0, 220, easing: Smooth);
                    Animate(ControlPanelCountdownCardScale, ScaleTransform.ScaleXProperty, 1, 220, easing: Smooth);
                    Animate(ControlPanelCountdownCardScale, ScaleTransform.ScaleYProperty, 1, 220, easing: Smooth);
                }
                else
                {
                    ControlPanelCountdownOptionCard.Opacity = 1;
                    ControlPanelCountdownCardTranslate.Y = 0;
                    ControlPanelCountdownCardScale.ScaleX = 1;
                    ControlPanelCountdownCardScale.ScaleY = 1;
                }
            }
            else
            {
                ControlPanelCountdownOptionCard.BeginAnimation(OpacityProperty, null);
                ControlPanelCountdownCardTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                ControlPanelCountdownCardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                ControlPanelCountdownCardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

                ControlPanelCountdownOptionCard.IsHitTestVisible = false;
                Animate(ControlPanelCountdownOptionCard, OpacityProperty, 0, 220, easing: Smooth);
                Animate(ControlPanelCountdownCardTranslate, TranslateTransform.YProperty, 14, 220, easing: Smooth);
                Animate(ControlPanelCountdownCardScale, ScaleTransform.ScaleXProperty, 0.98, 220, easing: Smooth);
                Animate(ControlPanelCountdownCardScale, ScaleTransform.ScaleYProperty, 0.98, 220, easing: Smooth);

                _countdownOptionHideTimer = new DispatcherTimer { Interval = OperatorMotion.SurfaceClose };
                _countdownOptionHideTimer.Tick += (_, _) =>
                {
                    _countdownOptionHideTimer?.Stop();
                    _countdownOptionHideTimer = null;
                    if (SetupCountUp.IsChecked != true)
                    {
                        ControlPanelCountdownOptionCard.Visibility = Visibility.Collapsed;
                    }
                };
                if (OperatorMotion.Enabled) _countdownOptionHideTimer.Start();
                else { _countdownOptionHideTimer = null; ControlPanelCountdownOptionCard.Visibility = Visibility.Collapsed; }
            }

            ControlPanelCountdownOptionCard.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isEnabled ? "#12253A" : "#151E29"));
            ControlPanelCountdownOptionCard.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isEnabled ? "#6CB6FF" : "#4D637C"));
            ControlPanelCountdownOptionCard.SetResourceReference(Border.BackgroundProperty,"ControlCentre.GlassBrush");
            ControlPanelCountdownOptionCard.SetResourceReference(Border.BorderBrushProperty,isEnabled?"ControlCentre.AccentBrush":"ControlCentre.BorderBrush");
            ControlPanelCountdownOptionCard.BorderThickness = new Thickness(1);

            if (ControlPanelCountdownToggleTrack?.Background is not SolidColorBrush trackBrush)
            {
                trackBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isEnabled ? "#1F6FEB" : "#4B5563"));
                if (ControlPanelCountdownToggleTrack is not null)
                {
                    ControlPanelCountdownToggleTrack.Background = trackBrush;
                }
            }

            Color targetTrackColor = (Color)ColorConverter.ConvertFromString(isEnabled ? "#1F6FEB" : "#4B5563");
            trackBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            if (!OperatorMotion.Enabled) trackBrush.Color = targetTrackColor;
            else trackBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
            {
                To = targetTrackColor,
                Duration = OperatorMotion.Toggle,
                EasingFunction = Sine
            });

            if (ControlPanelCountdownThumbTranslate is not null)
            {
                ControlPanelCountdownThumbTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                Animate(ControlPanelCountdownThumbTranslate, TranslateTransform.XProperty, isEnabled ? 26 : 0, 220, easing: Smooth);
            }

            if (ControlPanelCountdownToggleLabel is not null)
            {
                ControlPanelCountdownToggleLabel.BeginAnimation(OpacityProperty, null);
                ControlPanelCountdownToggleLabel.Opacity = 0.65;
                ControlPanelCountdownToggleLabel.Text = isEnabled ? "ON" : "OFF";
                ControlPanelCountdownToggleLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isEnabled ? "#58A6FF" : "#A2ACB8"));
                Animate(ControlPanelCountdownToggleLabel, OpacityProperty, 1, 180, easing: Smooth);
            }

            if (controlPanelCountdownHintText is not null)
            {
                controlPanelCountdownHintText.Text = isEnabled ? "Quarter length editable" : "Quarter length locked";
                controlPanelCountdownHintText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isEnabled ? "#9CC9FF" : "#7D8792"));
            }
        }

        private void MinutesMinus_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(SetupQuarterMinutes.Text, out int mins) && mins > 1)
                SetQuickDuration(mins - 1, 0);
        }

        private void MinutesPlus_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(SetupQuarterMinutes.Text, out int mins) && mins < 60)
                SetQuickDuration(mins + 1, 0);
        }

        private void SetupQuarterDuration_TextChanged(object sender, TextChangedEventArgs e)
            => UpdateSetupClockSummary();

        private void SetupQuickDuration10_Click(object sender, RoutedEventArgs e) => SetQuickDuration(10, 0);
        private void SetupQuickDuration15_Click(object sender, RoutedEventArgs e) => SetQuickDuration(15, 0);
        private void SetupQuickDuration20_Click(object sender, RoutedEventArgs e) => SetQuickDuration(20, 0);
        private void SetupQuickDuration25_Click(object sender, RoutedEventArgs e) => SetQuickDuration(25, 0);

        private void SetQuickDuration(int minutes, int seconds)
        {
            SetupQuarterMinutes.Text = minutes.ToString();
            SetupQuarterSeconds.Text = seconds.ToString("00");
            DurationMinutesDisplay.Text = minutes.ToString();
            HighlightActivePresetTile(minutes);
            UpdateSetupClockSummary();
        }

        private void HighlightActivePresetTile(int minutes)
        {
            foreach(var (tile,value) in new[]{(Preset10Tile,10),(Preset15Tile,15),(Preset20Tile,20),(Preset25Tile,25)})
            {
                if(tile?.Child is not System.Windows.Controls.Button button)continue;
                bool selected=value==minutes;
                button.SetResourceReference(StyleProperty,selected?"ControlCentre.PrimaryButton":"ControlCentre.Button");
                System.Windows.Automation.AutomationProperties.SetItemStatus(button,selected?"Selected quarter length":"Not selected");
            }
        }

        private void FinalsCard_Click(object sender, MouseButtonEventArgs e)
        {
            SetupFinalsMode.IsChecked = SetupFinalsMode.IsChecked != true;
            UpdateFinalsToggleVisuals();
        }

        private void UpdateFinalsToggleVisuals()
        {
            if (FinalsCard is null) return;
            bool on = SetupFinalsMode.IsChecked == true;
            WizardFlowDesign.StyleChoice(FinalsCard, on);
            if(on)FinalsCard.Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString("#76551C"));
            FinalsGlowBg.Visibility = Visibility.Collapsed;
            FinalsSelectedBadge.Visibility = Visibility.Collapsed;
            if(FinalsCard.Child is Grid content)foreach(var symbol in content.Children.OfType<OperatorSymbol>())symbol.IsActive=on;
            FinalsIcon.Effect = null;
        }

        private void UpdateSetupClockSummary()
        {
            // No-op: summary panel removed for simplicity
        }

        // ═══════════════════════════════════════════════════════
        //  WEATHER LOCATION AUTOCOMPLETE
        // ═══════════════════════════════════════════════════════

        private void SetupWeatherLocation_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressWeatherFilter || !_weatherBoxActivated) return;

            var combo = SetupWeatherLocation;
            string input = combo.Text ?? "";

            if (string.IsNullOrWhiteSpace(input))
            {
                combo.IsDropDownOpen = false;
                return;
            }

            _suppressWeatherFilter = true;

            var matches = AustralianCities
                .Where(c => c.Contains(input, StringComparison.OrdinalIgnoreCase))
                .ToList();

            combo.Items.Clear();
            foreach (string city in matches)
                combo.Items.Add(city);

            var editBox = combo.Template.FindName("PART_EditableTextBox", combo) as System.Windows.Controls.TextBox;
            int caretPos = editBox?.CaretIndex ?? input.Length;

            combo.IsDropDownOpen = matches.Count > 0;

            if (editBox is not null)
            {
                editBox.CaretIndex = caretPos;
            }

            _suppressWeatherFilter = false;
        }

        // ═══════════════════════════════════════════════════════
        //  COLOUR PICKERS
        // ═══════════════════════════════════════════════════════

        private void PickSetupHomeColor_Click(object sender, MouseButtonEventArgs e) => PickColor(SetupHomeColorBox, SetupHomeColorPreview);
        private void PickSetupHomeSecondary_Click(object sender, MouseButtonEventArgs e) => PickColor(SetupHomeSecondaryBox, SetupHomeSecondaryPreview);
        private void PickSetupAwayColor_Click(object sender, MouseButtonEventArgs e) => PickColor(SetupAwayColorBox, SetupAwayColorPreview);
        private void PickSetupAwaySecondary_Click(object sender, MouseButtonEventArgs e) => PickColor(SetupAwaySecondaryBox, SetupAwaySecondaryPreview);

        private void DropperSetupHomeColor_Click(object sender, MouseButtonEventArgs e) => DropColor(SetupHomeColorBox, SetupHomeColorPreview);
        private void DropperSetupHomeSecondary_Click(object sender, MouseButtonEventArgs e) => DropColor(SetupHomeSecondaryBox, SetupHomeSecondaryPreview);
        private void DropperSetupAwayColor_Click(object sender, MouseButtonEventArgs e) => DropColor(SetupAwayColorBox, SetupAwayColorPreview);
        private void DropperSetupAwaySecondary_Click(object sender, MouseButtonEventArgs e) => DropColor(SetupAwaySecondaryBox, SetupAwaySecondaryPreview);

        private static void PickColor(TextBox box, Border preview)
        {
            Color initial = Colors.White;
            try
            {
                initial = (Color)ColorConverter.ConvertFromString(box.Text);
            }
            catch { /* ignore */ }

            if (!ColorPickerWindow.TryPick(Window.GetWindow(box), initial, out Color c)) return;
            string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            box.Text = hex;
            TrySetPreview(preview, hex);
        }

        private static void DropColor(TextBox box, Border preview)
        {
            var picked = EyeDropper.Pick();
            if (picked == null) return;
            var c = picked.Value;
            RecentColorStorage.Record(c);
            string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            box.Text = hex;
            TrySetPreview(preview, hex);
        }

        // ═══════════════════════════════════════════════════════
        //  LOGO PICKERS
        // ═══════════════════════════════════════════════════════

        private void ChooseSetupHomeLogo_Click(object sender, RoutedEventArgs e)
        {
            var path = PickLogoFile("Home");
            if (path == null) return;
            _homeLogoPath = path;
            SetupHomeLogoText.Text = System.IO.Path.GetFileName(path);
            ShowLogoPreview(SetupHomeLogoImage, SetupHomeLogoPreview, path);
        }

        private void ChooseSetupAwayLogo_Click(object sender, RoutedEventArgs e)
        {
            var path = PickLogoFile("Away");
            if (path == null) return;
            _awayLogoPath = path;
            SetupAwayLogoText.Text = System.IO.Path.GetFileName(path);
            ShowLogoPreview(SetupAwayLogoImage, SetupAwayLogoPreview, path);
        }

        private static string? PickLogoFile(string teamLabel)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"Select {teamLabel} Team Logo",
                Filter = Services.ImageLoadHelper.LogoFilter
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        private static void ShowLogoPreview(System.Windows.Controls.Image image, Border container, string? path)
        {
            var source = Services.ImageLoadHelper.Load(path, decodePixelHeight: 160);
            image.Source = source;
            container.Visibility = source is not null ? Visibility.Visible : Visibility.Collapsed;
        }

        // ═══════════════════════════════════════════════════════
        //  GOAL VIDEO PICKERS
        // ═══════════════════════════════════════════════════════

        private void ChooseSetupHomeVideo_Click(object sender, RoutedEventArgs e)
        {
            var path = PickVideoFile("Home");
            if (path == null) return;
            _homeVideoPath = path;
            SetupHomeVideoText.Text = System.IO.Path.GetFileName(path);
            _ = Roche_Scoreboard.Services.VideoOptimizer.EnsureOptimisedAsync(path);
        }

        private void ChooseSetupAwayVideo_Click(object sender, RoutedEventArgs e)
        {
            var path = PickVideoFile("Away");
            if (path == null) return;
            _awayVideoPath = path;
            SetupAwayVideoText.Text = System.IO.Path.GetFileName(path);
            _ = Roche_Scoreboard.Services.VideoOptimizer.EnsureOptimisedAsync(path);
        }

        private static string? PickVideoFile(string teamLabel)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"Select {teamLabel} Goal Animation Video",
                Filter = "Video Files|*.mp4;*.wmv;*.avi;*.mov|All Files|*.*"
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        // ═══════════════════════════════════════════════════════
        //  MESSAGES
        // ═══════════════════════════════════════════════════════

        private void SetupAddMessage_Click(object sender, RoutedEventArgs e)
        {
            string? text = SetupNewMessageBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;
            _setupMessages.Add(new MarqueeMessage(text, _newMessageTextColor, _newMessageHighlightColor));
            SetupNewMessageBox.Text = "";
        }

        private void SetupNewMessageBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            SetupAddMessage_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }

        private void SetupRemoveMessageItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            if (btn.Tag is not MarqueeMessage msg) return;
            _setupMessages.Remove(msg);
        }

        private void SetupPickItemTextColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            if (btn.Tag is not MarqueeMessage msg) return;
            var initial = ParseDrawingColor(msg.TextColor, DrawingColor.White);
            if (!TryPickColor(initial, out var picked)) return;
            msg.TextColor = ToHex(picked);
        }

        private void SetupPickItemHighlightColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            if (btn.Tag is not MarqueeMessage msg) return;
            var initial = ParseDrawingColor(msg.HighlightColor, DrawingColor.FromArgb(255, 30, 64, 175));
            if (!TryPickColor(initial, out var picked)) return;
            msg.HighlightColor = ToHex(picked);
        }

        private void SetupClearItemHighlightColor_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            if (btn.Tag is not MarqueeMessage msg) return;
            msg.HighlightColor = null;
        }

        private void SetupPickNewTextColor_Click(object sender, RoutedEventArgs e)
        {
            var initial = ParseDrawingColor(_newMessageTextColor, DrawingColor.White);
            if (!TryPickColor(initial, out var picked)) return;
            _newMessageTextColor = ToHex(picked);
            if (SetupNewMessageTextColorBtn != null)
                SetupNewMessageTextColorBtn.Background = new SolidColorBrush(Color.FromRgb(picked.R, picked.G, picked.B));
        }

        private void SetupPickNewHighlightColor_Click(object sender, RoutedEventArgs e)
        {
            var initial = ParseDrawingColor(_newMessageHighlightColor, DrawingColor.FromArgb(255, 30, 64, 175));
            if (!TryPickColor(initial, out var picked)) return;
            _newMessageHighlightColor = ToHex(picked);
            if (SetupNewMessageHighlightColorBtn != null)
                SetupNewMessageHighlightColorBtn.Background = new SolidColorBrush(Color.FromRgb(picked.R, picked.G, picked.B));
        }

        private void SetupClearNewHighlightColor_Click(object sender, MouseButtonEventArgs e)
        {
            _newMessageHighlightColor = null;
            if (SetupNewMessageHighlightColorBtn != null)
                SetupNewMessageHighlightColorBtn.Background = System.Windows.Media.Brushes.Transparent;
        }

        private static string ToHex(DrawingColor c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        private static DrawingColor ParseDrawingColor(string? hex, DrawingColor fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            try
            {
                var media = (Color)ColorConverter.ConvertFromString(hex);
                return DrawingColor.FromArgb(media.R, media.G, media.B);
            }
            catch { return fallback; }
        }

        private static bool TryPickColor(DrawingColor initial, out DrawingColor picked)
        {
            Color mediaInitial = Color.FromRgb(initial.R, initial.G, initial.B);
            bool accepted = ColorPickerWindow.TryPick(System.Windows.Application.Current?.MainWindow,
                mediaInitial, out Color mediaPicked);
            picked = accepted ? DrawingColor.FromArgb(mediaPicked.R, mediaPicked.G, mediaPicked.B) : initial;
            return accepted;
        }

        // ═══════════════════════════════════════════════════════
        //  PRESETS
        // ═══════════════════════════════════════════════════════

        private void BrowseHomeTeam_Click(object sender, RoutedEventArgs e)
            => BrowseTeamLibrary(isHome: true);

        private void BrowseAwayTeam_Click(object sender, RoutedEventArgs e)
            => BrowseTeamLibrary(isHome: false);

        private void BrowseTeamLibrary(bool isHome)
        {
            TeamLibraryWindow browser = new(isHome ? "Home" : "Away")
            {
                Owner = Window.GetWindow(this)
            };

            bool? accepted = browser.ShowDialog();
            if (browser.PresetsChanged)
            {
                _presets = PresetStorage.LoadAll();
                RefreshPresetPanels();
            }

            if (accepted != true || browser.Selection is null) return;
            TeamLibrarySelection selection = browser.Selection;
            TeamPreset preset = new()
            {
                PresetName = selection.Team.TeamName,
                TeamName = selection.Team.TeamName,
                Abbreviation = selection.Team.Abbreviation,
                PrimaryColor = selection.Team.PrimaryColor,
                SecondaryColor = selection.Team.SecondaryColor,
                LogoPath = selection.TransparentLogoPath,
                LibraryTeamId = selection.Team.Id
            };

            if (isHome)
                ApplyPresetToHome(preset);
            else
                ApplyPresetToAway(preset);
        }

        private void RefreshPresetPanels()
        {
            BuildPresetTiles(HomePresetPanel, HomePresetEmpty, true);
            BuildPresetTiles(AwayPresetPanel, AwayPresetEmpty, false);
        }

        private void BuildPresetTiles(System.Windows.Controls.WrapPanel panel, TextBlock emptyLabel, bool isHome)
        {
            if(panel.Children.Count==_presets.Count && panel.Children.OfType<Border>().Select(tile=>tile.Tag).SequenceEqual(_presets.Cast<object>()))
            {
                foreach(var existing in panel.Children.OfType<Border>())if(existing.Tag is TeamPreset preset)
                    OperatorClubPresetDesign.Update(existing,preset,string.Equals((isHome?_selectedHomePreset:_selectedAwayPreset)?.PresetName,preset.PresetName,StringComparison.OrdinalIgnoreCase));
                emptyLabel.Visibility=_presets.Count==0?Visibility.Visible:Visibility.Collapsed;return;
            }
            panel.Children.Clear();

            if (_presets.Count == 0)
            {
                emptyLabel.Visibility = Visibility.Visible;
                return;
            }

            emptyLabel.Visibility = Visibility.Collapsed;

            for (int idx = 0; idx < _presets.Count; idx++)
            {
                TeamPreset preset = _presets[idx];
                Color bgColor = SafeParseColor(preset.PrimaryColor, "#333");
                Color fgColor = SafeParseColor(preset.SecondaryColor, "#FFF");

                bool isSelected = isHome
                    ? string.Equals(_selectedHomePreset?.PresetName, preset.PresetName, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(_selectedAwayPreset?.PresetName, preset.PresetName, StringComparison.OrdinalIgnoreCase);

                string abbr = NormalizeAbbreviation(preset.Abbreviation);

                TextBlock abbrLabel = new()
                {
                    Text = abbr,
                    Foreground = new SolidColorBrush(fgColor),
                    FontSize = 16,
                    FontWeight = FontWeights.Black,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    IsHitTestVisible = false
                };

                TextBlock nameLabel = new()
                {
                    Text = string.IsNullOrWhiteSpace(preset.TeamName) ? "Unnamed team" : preset.TeamName,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xCC, fgColor.R, fgColor.G, fgColor.B)),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 94,
                    IsHitTestVisible = false
                };

                StackPanel stack = new()
                {
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    IsHitTestVisible = false
                };
                stack.Children.Add(abbrLabel);
                stack.Children.Add(nameLabel);

                Grid tileContent = new() { IsHitTestVisible = false };
                tileContent.Children.Add(stack);

                if (isSelected)
                {
                    Border overlay = new()
                    {
                        Background = new SolidColorBrush(Color.FromArgb(0x8C, 0x00, 0x00, 0x00)),
                        IsHitTestVisible = false
                    };
                    tileContent.Children.Add(overlay);

                    TextBlock tickText = new()
                    {
                        Text = "✓",
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 18,
                        FontWeight = FontWeights.Black,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center,
                        IsHitTestVisible = false,
                        RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                        RenderTransform = new ScaleTransform(0, 0)
                    };
                    tileContent.Children.Add(tickText);

                    ((ScaleTransform)tickText.RenderTransform).ScaleX = ((ScaleTransform)tickText.RenderTransform).ScaleY = 1;
                    overlay.Opacity = 1;
                    if (OperatorMotion.Enabled)
                    {
                        var scaleIn = new DoubleAnimation(.75, 1, OperatorMotion.CheckDraw) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop };
                        ((ScaleTransform)tickText.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
                        ((ScaleTransform)tickText.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn.Clone());
                        overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, OperatorMotion.SurfaceOpen) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
                    }
                }

                var tileScaleTransform = new ScaleTransform(1, 1);
                var tileTranslateTransform = new TranslateTransform(0, 0);
                var tileTransformGroup = new TransformGroup();
                tileTransformGroup.Children.Add(tileScaleTransform);
                tileTransformGroup.Children.Add(tileTranslateTransform);

                Border tile = new()
                {
                    Width = 110,
                    Height = 70,
                    Padding = new Thickness(4),
                    Margin = new Thickness(0, 0, 8, 8),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Focusable = true,
                    ToolTip = $"{preset.TeamName}\n{abbr}\n{(HasValidFile(preset.LogoPath) ? "Logo set" : "No logo")} · {(HasValidFile(preset.GoalVideoPath) ? "Video set" : "No video")}",
                    Child = tileContent,
                    Tag = preset,
                    BorderBrush = isSelected
                        ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E))
                        : new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(2),
                    RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                    RenderTransform = tileTransformGroup,
                    Background = new LinearGradientBrush(
                        BlendColor(bgColor, Colors.Black, 0.28),
                        BlendColor(bgColor, Colors.White, 0.08),
                        new System.Windows.Point(0, 0),
                        new System.Windows.Point(1, 1))
                };

                OperatorClubPresetDesign.Update(tile,preset,isSelected);
                tile.MouseLeftButtonDown += (_, me) =>
                {
                    if (_isDragging) return;

                    _dragStartPos = me.GetPosition(panel);
                    _dragTile = tile;
                    _dragOrigIndex = panel.Children.IndexOf(tile);
                    _dragCurrentIndex = _dragOrigIndex;
                    _isDragging = false;
                    _dragIsHome = isHome;

                    int count = panel.Children.Count;
                    _slotCentersX = new double[count];
                    _slotCentersY = new double[count];
                    for (int i = 0; i < count; i++)
                    {
                        if (panel.Children[i] is FrameworkElement fe)
                        {
                            Vector offset = VisualTreeHelper.GetOffset(fe);
                            _slotCentersX[i] = offset.X + fe.ActualWidth / 2;
                            _slotCentersY[i] = offset.Y + fe.ActualHeight / 2;
                        }
                    }

                    tile.CaptureMouse();
                    me.Handled = true;
                };

                tile.MouseMove += (_, me) =>
                {
                    if (_dragTile != tile || !tile.IsMouseCaptured) return;

                    System.Windows.Point pos = me.GetPosition(panel);
                    double dx = pos.X - _dragStartPos.X;
                    double dy = pos.Y - _dragStartPos.Y;

                    if (!_isDragging)
                    {
                        if (Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold) return;
                        _isDragging = true;
                        System.Windows.Controls.Panel.SetZIndex(tile, 100);
                        tile.Opacity = 0.85;
                    }

                    {
                        tileTranslateTransform.BeginAnimation(TranslateTransform.XProperty, null);
                        tileTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
                        tileTranslateTransform.X = dx;
                        tileTranslateTransform.Y = dy;
                    }

                    double dragCenterX = _slotCentersX[_dragOrigIndex] + dx;
                    double dragCenterY = _slotCentersY[_dragOrigIndex] + dy;
                    int newIndex = _dragOrigIndex;
                    double bestDist = double.MaxValue;
                    for (int i = 0; i < _slotCentersX.Length; i++)
                    {
                        double ddx = dragCenterX - _slotCentersX[i];
                        double ddy = dragCenterY - _slotCentersY[i];
                        double d = ddx * ddx + ddy * ddy;
                        if (d < bestDist) { bestDist = d; newIndex = i; }
                    }

                    if (newIndex != _dragCurrentIndex)
                    {
                        _dragCurrentIndex = newIndex;
                        ShiftTilesForDrag(panel, _dragOrigIndex, _dragCurrentIndex, tile);
                    }
                };

                tile.MouseLeftButtonUp += (_, me) =>
                {
                    if (_dragTile != tile) return;
                    CompleteDrag(panel, isHome, preset);
                    me.Handled = true;
                };

                tile.LostMouseCapture += (_, _) =>
                {
                    if (_dragTile != tile) return;
                    CompleteDrag(panel, isHome, preset);
                };

                var restEdge = tile.BorderBrush;
                void PaintInteraction()=>OperatorClubPresetDesign.Hover(tile,tile.IsMouseOver||tile.IsKeyboardFocusWithin);
                tile.MouseEnter+=(_,_)=>PaintInteraction();tile.MouseLeave+=(_,_)=>PaintInteraction();tile.GotKeyboardFocus+=(_,_)=>PaintInteraction();tile.LostKeyboardFocus+=(_,_)=>PaintInteraction();
                panel.Children.Add(tile);
            }
        }

        private void CompleteDrag(System.Windows.Controls.WrapPanel panel, bool isHome, TeamPreset clickedPreset)
        {
            Border? tile = _dragTile;
            bool wasDragging = _isDragging;
            int origIndex = _dragOrigIndex;
            int currentIndex = _dragCurrentIndex;

            _dragTile = null;
            _isDragging = false;

            if (tile?.IsMouseCaptured == true)
                tile.ReleaseMouseCapture();

            if (wasDragging)
            {
                if (origIndex != currentIndex)
                {
                    TeamPreset moved = _presets[origIndex];
                    _presets.RemoveAt(origIndex);
                    _presets.Insert(currentIndex, moved);
                    PresetStorage.SaveAll(_presets);
                }

                ResetAllTileTransforms(panel);
                RefreshPresetPanels();
            }
            else
            {
                if (isHome)
                    ApplyPresetToHome(clickedPreset);
                else
                    ApplyPresetToAway(clickedPreset);
            }
        }

        private static void ResetAllTileTransforms(System.Windows.Controls.WrapPanel panel)
        {
            foreach (UIElement child in panel.Children)
            {
                TranslateTransform? t = GetTranslate(child);
                if (t != null)
                {
                    t.BeginAnimation(TranslateTransform.XProperty, null);
                    t.BeginAnimation(TranslateTransform.YProperty, null);
                    t.X = 0;
                    t.Y = 0;
                }
                System.Windows.Controls.Panel.SetZIndex(child, 0);
                child.Opacity = 1;
            }
        }

        private static TranslateTransform? GetTranslate(UIElement element)
        {
            if (element is not FrameworkElement fe) return null;
            if (fe.RenderTransform is TranslateTransform tt) return tt;
            if (fe.RenderTransform is TransformGroup tg)
            {
                foreach (Transform t in tg.Children)
                    if (t is TranslateTransform translate) return translate;
            }
            return null;
        }

        private void ShiftTilesForDrag(System.Windows.Controls.WrapPanel panel, int origIndex, int hoverIndex, Border draggedTile)
        {
            TimeSpan duration = TimeSpan.FromMilliseconds(160);
            CubicEase ease = new() { EasingMode = EasingMode.EaseOut };

            for (int i = 0; i < panel.Children.Count; i++)
            {
                if (panel.Children[i] is not Border child || child == draggedTile) continue;
                TranslateTransform? tt = GetTranslate(child);
                if (tt == null) continue;

                int visualIndex = i;
                if (origIndex < hoverIndex)
                {
                    if (i > origIndex && i <= hoverIndex) visualIndex = i - 1;
                }
                else if (origIndex > hoverIndex)
                {
                    if (i >= hoverIndex && i < origIndex) visualIndex = i + 1;
                }

                double targetX = _slotCentersX[visualIndex] - _slotCentersX[i];
                double targetY = _slotCentersY[visualIndex] - _slotCentersY[i];

                DoubleAnimation animX = new(targetX, duration) { EasingFunction = ease };
                DoubleAnimation animY = new(targetY, duration) { EasingFunction = ease };
                tt.BeginAnimation(TranslateTransform.XProperty, animX);
                tt.BeginAnimation(TranslateTransform.YProperty, animY);
            }
        }

        private void ApplyPresetToHome(TeamPreset p)
        {
            _selectedHomePreset = p;
            SetupHomeNameBox.Text = p.TeamName;
            SetupHomeAbbrBox.Text = NormalizeAbbreviation(p.Abbreviation);
            SetupHomeColorBox.Text = p.PrimaryColor;
            SetupHomeSecondaryBox.Text = p.SecondaryColor;
            TrySetPreview(SetupHomeColorPreview, p.PrimaryColor);
            TrySetPreview(SetupHomeSecondaryPreview, p.SecondaryColor);

            if (HasValidFile(p.LogoPath))
            {
                _homeLogoPath = p.LogoPath;
                SetupHomeLogoText.Text = System.IO.Path.GetFileName(p.LogoPath);
                ShowLogoPreview(SetupHomeLogoImage, SetupHomeLogoPreview, p.LogoPath);
            }
            else
            {
                _homeLogoPath = null;
                SetupHomeLogoText.Text = "(none)";
                ShowLogoPreview(SetupHomeLogoImage, SetupHomeLogoPreview, null);
            }

            if (HasValidFile(p.GoalVideoPath))
            {
                _homeVideoPath = p.GoalVideoPath;
                SetupHomeVideoText.Text = System.IO.Path.GetFileName(p.GoalVideoPath);
            }
            else
            {
                _homeVideoPath = null;
                SetupHomeVideoText.Text = "(none)";
            }

            BuildPresetTiles(HomePresetPanel, HomePresetEmpty, true);
            BuildPresetTiles(AwayPresetPanel, AwayPresetEmpty, false);
        }

        private void ApplyPresetToAway(TeamPreset p)
        {
            _selectedAwayPreset = p;
            SetupAwayNameBox.Text = p.TeamName;
            SetupAwayAbbrBox.Text = NormalizeAbbreviation(p.Abbreviation);
            SetupAwayColorBox.Text = p.PrimaryColor;
            SetupAwaySecondaryBox.Text = p.SecondaryColor;
            TrySetPreview(SetupAwayColorPreview, p.PrimaryColor);
            TrySetPreview(SetupAwaySecondaryPreview, p.SecondaryColor);

            if (HasValidFile(p.LogoPath))
            {
                _awayLogoPath = p.LogoPath;
                SetupAwayLogoText.Text = System.IO.Path.GetFileName(p.LogoPath);
                ShowLogoPreview(SetupAwayLogoImage, SetupAwayLogoPreview, p.LogoPath);
            }
            else
            {
                _awayLogoPath = null;
                SetupAwayLogoText.Text = "(none)";
                ShowLogoPreview(SetupAwayLogoImage, SetupAwayLogoPreview, null);
            }

            if (HasValidFile(p.GoalVideoPath))
            {
                _awayVideoPath = p.GoalVideoPath;
                SetupAwayVideoText.Text = System.IO.Path.GetFileName(p.GoalVideoPath);
            }
            else
            {
                _awayVideoPath = null;
                SetupAwayVideoText.Text = "(none)";
            }

            BuildPresetTiles(HomePresetPanel, HomePresetEmpty, true);
            BuildPresetTiles(AwayPresetPanel, AwayPresetEmpty, false);
        }

        private void DeletePreset(TeamPreset? preset)
        {
            if (preset == null) return;

            _presets.Remove(preset);

            if (string.Equals(_selectedHomePreset?.PresetName, preset.PresetName, StringComparison.OrdinalIgnoreCase))
                _selectedHomePreset = null;

            if (string.Equals(_selectedAwayPreset?.PresetName, preset.PresetName, StringComparison.OrdinalIgnoreCase))
                _selectedAwayPreset = null;

            PresetStorage.SaveAll(_presets);
            RefreshPresetPanels();
        }

        private static Color SafeParseColor(string hex, string fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return (Color)ColorConverter.ConvertFromString(fallback); }
        }

        private static void TrySetPreview(Border preview, string hex)
        {
            try { preview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { /* ignore */ }
        }

        private void SetupHomeColorBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TrySetPreview(SetupHomeColorPreview, SetupHomeColorBox.Text.Trim());
        }

        private void SetupHomeSecondaryBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TrySetPreview(SetupHomeSecondaryPreview, SetupHomeSecondaryBox.Text.Trim());
        }

        private void SetupAwayColorBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TrySetPreview(SetupAwayColorPreview, SetupAwayColorBox.Text.Trim());
        }

        private void SetupAwaySecondaryBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TrySetPreview(SetupAwaySecondaryPreview, SetupAwaySecondaryBox.Text.Trim());
        }

        private static string NormalizeAbbreviation(string? value)
        {
            string cleaned = (value ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(cleaned)) return "TEAM";
            return cleaned.Length > 4 ? cleaned[..4] : cleaned;
        }

        private static bool HasValidFile(string? path)
            => !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path);

        private void SaveHomePreset_Click(object sender, RoutedEventArgs e)
            => SavePreset(SetupHomeNameBox, SetupHomeAbbrBox, SetupHomeColorBox, SetupHomeSecondaryBox, _homeLogoPath, _homeVideoPath, true);

        private void DeleteHomePreset_Click(object sender, RoutedEventArgs e)
            => DeletePreset(_selectedHomePreset);

        private void SaveAwayPreset_Click(object sender, RoutedEventArgs e)
            => SavePreset(SetupAwayNameBox, SetupAwayAbbrBox, SetupAwayColorBox, SetupAwaySecondaryBox, _awayLogoPath, _awayVideoPath, false);

        private void DeleteAwayPreset_Click(object sender, RoutedEventArgs e)
            => DeletePreset(_selectedAwayPreset);

        private void SavePreset(TextBox nameBox, TextBox abbrBox, TextBox colorBox, TextBox secBox, string? logoPath, string? videoPath, bool isHome)
        {
            string name = nameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            string abbreviation = NormalizeAbbreviation(abbrBox.Text);

            TeamPreset? existing = _presets.FirstOrDefault(p => string.Equals(p.PresetName, name, StringComparison.OrdinalIgnoreCase));
            TeamPreset target;

            if (existing != null)
            {
                target = existing;
                target.PresetName = name;
            }
            else
            {
                target = new TeamPreset { PresetName = name };
                _presets.Add(target);
            }

            target.TeamName = name;
            target.Abbreviation = abbreviation;
            target.PrimaryColor = colorBox.Text.Trim();
            target.SecondaryColor = secBox.Text.Trim();
            target.LogoPath = logoPath;
            target.GoalVideoPath = videoPath;

            if (isHome)
                _selectedHomePreset = target;
            else
                _selectedAwayPreset = target;

            PresetStorage.SaveAll(_presets);
            RefreshPresetPanels();
        }

        private static Color BlendColor(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromRgb(
                (byte)(a.R + ((b.R - a.R) * t)),
                (byte)(a.G + ((b.G - a.G) * t)),
                (byte)(a.B + ((b.B - a.B) * t)));
        }

        // ═══════════════════════════════════════════════════════
        //  ANIMATION HELPERS (matching sport selection screen)
        // ═══════════════════════════════════════════════════════

        private static void Animate(IAnimatable target, DependencyProperty prop,
            double to, int durationMs, int delay = 0, IEasingFunction? easing = null)
        {
            if (!OperatorMotion.Enabled)
            {
                target.BeginAnimation(prop, null);
                if (target is DependencyObject value) value.SetValue(prop, to);
                return;
            }
            var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(durationMs))
            {
                BeginTime = delay > 0 ? TimeSpan.FromMilliseconds(delay) : TimeSpan.Zero,
                EasingFunction = easing
            };
            target.BeginAnimation(prop, anim);
        }

        private static void PulseForever(IAnimatable target, DependencyProperty prop,
            double from, double to, int periodMs)
        {
            if (!OperatorMotion.Enabled) { target.BeginAnimation(prop, null); return; }
            var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(periodMs))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = Sine
            };
            target.BeginAnimation(prop, anim);
        }
    }
}

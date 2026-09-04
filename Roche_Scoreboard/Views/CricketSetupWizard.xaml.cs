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
    public partial class CricketSetupWizard : System.Windows.Controls.UserControl
    {
        private List<TeamPreset> _presets;
        private string? _homeLogoPath;
        private string? _awayLogoPath;
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
        private static readonly BackEase Overshoot = new() { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
        private static readonly SineEase Sine = new() { EasingMode = EasingMode.EaseInOut };

        private static readonly TimeSpan HoverIn = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan HoverOut = TimeSpan.FromMilliseconds(350);

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

        public CricketSetupResult? Result { get; private set; }
        public event Action<CricketSetupResult>? SetupCompleted;
        public event Action? BackToSportSelection;

        private const int TotalSteps = 4;

        // ── Player lists ──
        private readonly System.Collections.ObjectModel.ObservableCollection<CricketPlayer> _homePlayers = [];
        private readonly System.Collections.ObjectModel.ObservableCollection<CricketPlayer> _awayPlayers = [];

        // ── Match format ──
        private CricketFormat _selectedFormat = CricketFormat.LimitedOvers;
        private int _selectedOvers = 20;

        // ── Styled marquee messages (parity with main message editor) ──
        private readonly System.Collections.ObjectModel.ObservableCollection<MarqueeMessage> _setupMessages = [];
        private string? _newMessageTextColor;       // null = default (white)
        private string? _newMessageHighlightColor;  // null = no highlight

        public CricketSetupWizard()
        {
            InitializeComponent();
            SetupMessageList.ItemsSource = _setupMessages;
            HomePlayerList.ItemsSource = _homePlayers;
            AwayPlayerList.ItemsSource = _awayPlayers;
            _presets = PresetStorage.LoadAll();
            RefreshPresetPanels();

            SetupWeatherLocation.AddHandler(
                System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler(SetupWeatherLocation_TextChanged));
            SetupWeatherLocation.GotFocus += (_, _) => _weatherBoxActivated = true;
            SetupWeatherLocation.DropDownOpened += (_, _) => _weatherBoxActivated = true;

            Loaded += OnLoaded;
        }

        // ═══════════════════════════════════════════════════════
        //  ENTRY ANIMATION — staggered cinematic reveal
        // ═══════════════════════════════════════════════════════

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ShowStep(1, animate: false);
            PlayEntryAnimation();
        }

        private void PlayEntryAnimation()
        {
            // Reset all animated elements to initial states so the staggered
            // reveal replays cleanly (clears any held animation values).
            HeaderPanel.BeginAnimation(OpacityProperty, null);
            HeaderPanel.Opacity = 0;
            HeaderTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            HeaderTranslate.Y = -30;

            StepIndicator.BeginAnimation(OpacityProperty, null);
            StepIndicator.Opacity = 0;
            StepIndicatorTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            StepIndicatorTranslate.Y = -20;

            BgGlow.BeginAnimation(OpacityProperty, null);
            BgGlow.Opacity = 0;

            HomePresetCard.BeginAnimation(OpacityProperty, null);
            HomePresetCard.Opacity = 0;
            HomePresetTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            HomePresetTranslate.X = -60;

            AwayPresetCard.BeginAnimation(OpacityProperty, null);
            AwayPresetCard.Opacity = 0;
            AwayPresetTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            AwayPresetTranslate.X = 60;

            HomeTeamCard.BeginAnimation(OpacityProperty, null);
            HomeTeamCard.Opacity = 0;
            HomeCardTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            HomeCardTranslate.X = -80;

            AwayTeamCard.BeginAnimation(OpacityProperty, null);
            AwayTeamCard.Opacity = 0;
            AwayCardTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            AwayCardTranslate.X = 80;

            DividerScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            DividerScale.ScaleY = 0;
            DividerGlowLine.BeginAnimation(OpacityProperty, null);
            DividerGlowLine.Opacity = 0;

            HomeHeaderGroup.BeginAnimation(OpacityProperty, null);
            HomeHeaderGroup.Opacity = 0;
            HomeHeaderTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            HomeHeaderTranslate.Y = 15;

            AwayHeaderGroup.BeginAnimation(OpacityProperty, null);
            AwayHeaderGroup.Opacity = 0;
            AwayHeaderTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            AwayHeaderTranslate.Y = 15;

            HomeGlow.BeginAnimation(OpacityProperty, null);
            HomeGlow.Opacity = 0;
            AwayGlow.BeginAnimation(OpacityProperty, null);
            AwayGlow.Opacity = 0;

            NavBar.BeginAnimation(OpacityProperty, null);
            NavBar.Opacity = 0;
            NavBarTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            NavBarTranslate.Y = 30;

            BottomAccent.BeginAnimation(OpacityProperty, null);
            BottomAccent.Opacity = 0;

            // Header: fade + slide down
            Animate(HeaderPanel, OpacityProperty, 1, 500, delay: 100, easing: Smooth);
            Animate(HeaderTranslate, TranslateTransform.YProperty, 0, 500, delay: 100, easing: Smooth);

            // Step indicator: fade + slide down
            Animate(StepIndicator, OpacityProperty, 1, 500, delay: 200, easing: Smooth);
            Animate(StepIndicatorTranslate, TranslateTransform.YProperty, 0, 500, delay: 200, easing: Smooth);

            // Background glow
            Animate(BgGlow, OpacityProperty, 0.25, 800, delay: 200);

            // Home preset card: fade + slide from left
            Animate(HomePresetCard, OpacityProperty, 1, 500, delay: 300, easing: Smooth);
            Animate(HomePresetTranslate, TranslateTransform.XProperty, 0, 600, delay: 300, easing: Smooth);

            // Away preset card: fade + slide from right
            Animate(AwayPresetCard, OpacityProperty, 1, 500, delay: 400, easing: Smooth);
            Animate(AwayPresetTranslate, TranslateTransform.XProperty, 0, 600, delay: 400, easing: Smooth);

            // Home team card: fade + slide from left
            Animate(HomeTeamCard, OpacityProperty, 1, 600, delay: 400);
            Animate(HomeCardTranslate, TranslateTransform.XProperty, 0, 700, delay: 400, easing: Smooth);

            // Away team card: fade + slide from right
            Animate(AwayTeamCard, OpacityProperty, 1, 600, delay: 500);
            Animate(AwayCardTranslate, TranslateTransform.XProperty, 0, 700, delay: 500, easing: Smooth);

            // Divider: grow from centre
            Animate(DividerScale, ScaleTransform.ScaleYProperty, 1, 800, delay: 500, easing: Smooth);
            Animate(DividerGlowLine, OpacityProperty, 0.6, 800, delay: 500);

            // Home header: fade + slide up with overshoot
            Animate(HomeHeaderGroup, OpacityProperty, 1, 500, delay: 650);
            Animate(HomeHeaderTranslate, TranslateTransform.YProperty, 0, 600, delay: 650, easing: Overshoot);

            // Away header: fade + slide up with overshoot
            Animate(AwayHeaderGroup, OpacityProperty, 1, 500, delay: 750);
            Animate(AwayHeaderTranslate, TranslateTransform.YProperty, 0, 600, delay: 750, easing: Overshoot);

            // Hero glows
            Animate(HomeGlow, OpacityProperty, 0.30, 600, delay: 700);
            Animate(AwayGlow, OpacityProperty, 0.30, 600, delay: 800);

            // Nav bar: fade + slide up
            Animate(NavBar, OpacityProperty, 1, 500, delay: 800, easing: Smooth);
            Animate(NavBarTranslate, TranslateTransform.YProperty, 0, 500, delay: 800, easing: Smooth);

            // Bottom accent: last to appear, triggers ambient loop
            var lastAnim = new DoubleAnimation(1, TimeSpan.FromMilliseconds(500))
            {
                BeginTime = TimeSpan.FromMilliseconds(900)
            };
            lastAnim.Completed += (_, _) => StartAmbientAnimations();
            BottomAccent.BeginAnimation(OpacityProperty, lastAnim);
        }

        // ═══════════════════════════════════════════════════════
        //  AMBIENT ANIMATIONS — continuous atmosphere
        // ═══════════════════════════════════════════════════════

        private void StartAmbientAnimations()
        {
            // Glow pulse
            PulseForever(HomeGlow, OpacityProperty, 0.18, 0.40, 2800);
            PulseForever(AwayGlow, OpacityProperty, 0.18, 0.40, 3100);
            PulseForever(BgGlow, OpacityProperty, 0.15, 0.30, 4000);

            // Light beam sweep
            PulseForever(BeamTranslate, TranslateTransform.XProperty, -300, 1200, 6000);
        }

        // ═══════════════════════════════════════════════════════
        //  HOVER — nav button scale effects
        // ═══════════════════════════════════════════════════════

        private void NavBtn_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not Border btn) return;
            ScaleTransform sc = btn == BackButton ? BackBtnScale : NextBtnScale;
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1.05, HoverIn, Smooth));
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1.05, HoverIn, Smooth));
        }

        private void NavBtn_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is not Border btn) return;
            ScaleTransform sc = btn == BackButton ? BackBtnScale : NextBtnScale;
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1.0, HoverOut, Smooth));
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1.0, HoverOut, Smooth));
        }

        // ═══════════════════════════════════════════════════════
        //  STEP NAVIGATION — cinematic transitions
        // ═══════════════════════════════════════════════════════

        private static readonly SolidColorBrush s_accentBrush = new((Color)ColorConverter.ConvertFromString("#58A6FF"));
        private static readonly SolidColorBrush s_surfaceBrush = new((Color)ColorConverter.ConvertFromString("#1A2436"));
        private static readonly SolidColorBrush s_textMutedBrush = new((Color)ColorConverter.ConvertFromString("#484F58"));
        private static readonly SolidColorBrush s_whiteBrush = new(Colors.White);
        private static readonly SolidColorBrush s_edgeBrush = new((Color)ColorConverter.ConvertFromString("#2A3A52"));

        private void ShowStep(int step, bool animate = true)
        {
            int previousStep = _currentStep;
            _currentStep = step;

            FrameworkElement[] panels = [TeamsStepContainer, Step3Panel, Step3bPanel, Step4Panel];
            FrameworkElement activePanel = step switch
            {
                1 => TeamsStepContainer,
                2 => Step3Panel,
                3 => Step3bPanel,
                _ => Step4Panel
            };
            int transitionVersion = ++_stepTransitionVersion;
            if (_stepTransitionStoryboard is not null)
            {
                try { _stepTransitionStoryboard.Remove(this); } catch { }
                _stepTransitionStoryboard = null;
            }
            FlashOverlay.BeginAnimation(OpacityProperty, null);
            FlashOverlay.Opacity = 0;
            foreach (FrameworkElement panel in panels)
            {
                panel.BeginAnimation(OpacityProperty, null);
                panel.Opacity = 1;
                if (panel.RenderTransform is TranslateTransform existingTranslate)
                    existingTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                panel.RenderTransform = null;
            }

            bool forward = step >= previousStep;
            double slideOutX = forward ? -60 : 60;
            double slideInX = forward ? 60 : -60;

            if (animate && step != previousStep)
            {
                // Animate outgoing panel exit
                FrameworkElement? outgoing = null;
                foreach (FrameworkElement p in panels)
                {
                    if (p.Visibility == Visibility.Visible)
                    {
                        outgoing = p;
                        break;
                    }
                }

                if (outgoing is not null)
                {
                    FrameworkElement fading = outgoing;
                    fading.RenderTransform = new TranslateTransform(0, 0);

                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                    var slideOut = new DoubleAnimation(0, slideOutX, TimeSpan.FromMilliseconds(250))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };

                    fadeOut.Completed += (_, _) =>
                    {
                        if (transitionVersion != _stepTransitionVersion || ReferenceEquals(fading, activePanel)) return;
                        fading.BeginAnimation(OpacityProperty, null);
                        fading.Visibility = Visibility.Collapsed;
                        fading.Opacity = 1;
                        fading.RenderTransform = null;
                    };

                    fading.BeginAnimation(OpacityProperty, fadeOut);
                    ((TranslateTransform)fading.RenderTransform).BeginAnimation(TranslateTransform.XProperty, slideOut);
                }
            }
            else
            {
                foreach (FrameworkElement p in panels)
                {
                    p.BeginAnimation(OpacityProperty, null);
                    p.Visibility = Visibility.Collapsed;
                    p.Opacity = 1;
                    p.RenderTransform = null;
                }
            }

            if (animate && step != previousStep)
            {
                activePanel.Visibility = Visibility.Visible;
                activePanel.Opacity = 0;
                activePanel.RenderTransform = new TranslateTransform(slideInX, 0);

                Storyboard sb = new();
                DoubleAnimation fadeIn = new(0, 1, TimeSpan.FromMilliseconds(300))
                {
                    BeginTime = TimeSpan.FromMilliseconds(80),
                    EasingFunction = Smooth
                };
                Storyboard.SetTarget(fadeIn, activePanel);
                Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
                sb.Children.Add(fadeIn);

                DoubleAnimation slideIn = new(slideInX, 0, TimeSpan.FromMilliseconds(400))
                {
                    BeginTime = TimeSpan.FromMilliseconds(60),
                    EasingFunction = Smooth
                };
                Storyboard.SetTarget(slideIn, activePanel);
                Storyboard.SetTargetProperty(slideIn, new PropertyPath("RenderTransform.(TranslateTransform.X)"));
                sb.Children.Add(slideIn);
                sb.Completed += (_, _) =>
                {
                    if (transitionVersion != _stepTransitionVersion) return;
                    activePanel.BeginAnimation(OpacityProperty, null);
                    activePanel.Opacity = 1;
                    activePanel.RenderTransform = null;
                    if (ReferenceEquals(_stepTransitionStoryboard, sb))
                        _stepTransitionStoryboard = null;
                };
                _stepTransitionStoryboard = sb;
                sb.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);

                // Animate step-specific hero content
                PlayStepEntryAnimation(step, transitionVersion);

                // Flash overlay for dramatic transition
                var flashIn = new DoubleAnimation(0.08, TimeSpan.FromMilliseconds(80));
                flashIn.Completed += (_, _) =>
                {
                    if (transitionVersion == _stepTransitionVersion)
                        FlashOverlay.BeginAnimation(OpacityProperty,
                            new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)));
                };
                FlashOverlay.BeginAnimation(OpacityProperty, flashIn);
            }
            else
            {
                activePanel.Visibility = Visibility.Visible;
                activePanel.Opacity = 1;
                activePanel.RenderTransform = null;
            }

            BackButton.Visibility = Visibility.Visible;
            NextButtonText.Text = step == TotalSteps ? "START MATCH →" : "NEXT →";

            UpdateStepDot(Step1Dot, Step1Label, true, step == 1);
            UpdateStepDot(Step2Dot, Step2Label, step >= 2, step == 2);
            UpdateStepDot(Step3Dot, Step3Label, step >= 3, step == 3);
            UpdateStepDot(Step4Dot, Step4Label, step >= 4, step == 4);
        }

        private void PlayStepEntryAnimation(int step, int transitionVersion)
        {
            switch (step)
            {
                case 1:
                    // Reset and replay team card animations
                    HomeHeaderGroup.Opacity = 0;
                    HomeHeaderTranslate.Y = 15;
                    AwayHeaderGroup.Opacity = 0;
                    AwayHeaderTranslate.Y = 15;

                    Animate(HomeHeaderGroup, OpacityProperty, 1, 500, delay: 200);
                    Animate(HomeHeaderTranslate, TranslateTransform.YProperty, 0, 600, delay: 200, easing: Overshoot);
                    Animate(AwayHeaderGroup, OpacityProperty, 1, 500, delay: 300);
                    Animate(AwayHeaderTranslate, TranslateTransform.YProperty, 0, 600, delay: 300, easing: Overshoot);

                    // Reset and animate glows
                    HomeGlow.Opacity = 0;
                    AwayGlow.Opacity = 0;
                    Animate(HomeGlow, OpacityProperty, 0.30, 600, delay: 300);
                    Animate(AwayGlow, OpacityProperty, 0.30, 600, delay: 400);

                    // Restart ambient glow pulse after entry
                    _ambientRestartTimer?.Stop();
                    _ambientRestartTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(800)
                    };
                    _ambientRestartTimer.Tick += (_, _) =>
                    {
                        _ambientRestartTimer?.Stop();
                        _ambientRestartTimer = null;
                        if (transitionVersion != _stepTransitionVersion || _currentStep != 1) return;
                        PulseForever(HomeGlow, OpacityProperty, 0.18, 0.40, 2800);
                        PulseForever(AwayGlow, OpacityProperty, 0.18, 0.40, 3100);
                    };
                    _ambientRestartTimer.Start();
                    break;

                case 2:
                    PlayersHeaderGroup.Opacity = 0;
                    PlayersHeaderTranslate.Y = 15;
                    PlayersGlow.Opacity = 0;

                    Animate(PlayersHeaderGroup, OpacityProperty, 1, 500, delay: 200);
                    Animate(PlayersHeaderTranslate, TranslateTransform.YProperty, 0, 600, delay: 200, easing: Overshoot);
                    Animate(PlayersGlow, OpacityProperty, 0.30, 600, delay: 300);

                    // Update player panel team name labels and side accent colours
                    UpdatePlayerPanelTeamIdentity();
                    break;

                case 3:
                    FormatHeaderGroup.Opacity = 0;
                    FormatHeaderTranslate.Y = 15;
                    FormatGlow.Opacity = 0;

                    Animate(FormatHeaderGroup, OpacityProperty, 1, 500, delay: 200);
                    Animate(FormatHeaderTranslate, TranslateTransform.YProperty, 0, 600, delay: 200, easing: Overshoot);
                    Animate(FormatGlow, OpacityProperty, 0.30, 600, delay: 300);
                    UpdateFormatCardVisuals();
                    break;

                case 4:
                    MsgHeaderGroup.Opacity = 0;
                    MsgHeaderTranslate.Y = 15;
                    MsgGlow.Opacity = 0;

                    Animate(MsgHeaderGroup, OpacityProperty, 1, 500, delay: 200);
                    Animate(MsgHeaderTranslate, TranslateTransform.YProperty, 0, 600, delay: 200, easing: Overshoot);
                    Animate(MsgGlow, OpacityProperty, 0.30, 600, delay: 300);
                    Animate(WeatherGlowBg, OpacityProperty, 0.08, 600, delay: 500);
                    Animate(MsgSectionGlow, OpacityProperty, 0.06, 600, delay: 500);
                    break;
            }
        }

        private void UpdateStepDot(Border dot, TextBlock label, bool reached, bool active)
        {
            dot.Background = reached ? s_accentBrush : s_surfaceBrush;
            dot.BorderBrush = reached ? s_accentBrush : s_edgeBrush;
            var dotText = (TextBlock)dot.Child;
            dotText.Foreground = reached ? s_whiteBrush : s_textMutedBrush;
            label.Foreground = active ? s_whiteBrush : (reached ? s_accentBrush : s_textMutedBrush);

            // Glow effect on active dot
            if (dot.Effect is DropShadowEffect glow)
                glow.Opacity = active ? 0.6 : 0;
        }

        private void Next_Click(object sender, MouseButtonEventArgs e)
        {
            // Click punch animation
            NextBtnScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.93, 1.0, TimeSpan.FromMilliseconds(200)) { EasingFunction = Smooth });
            NextBtnScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.93, 1.0, TimeSpan.FromMilliseconds(200)) { EasingFunction = Smooth });

            if (_currentStep < TotalSteps)
                ShowStep(_currentStep + 1);
            else
                FinishWizard();
        }

        private void Back_Click(object sender, MouseButtonEventArgs e)
        {
            BackBtnScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.93, 1.0, TimeSpan.FromMilliseconds(200)) { EasingFunction = Smooth });
            BackBtnScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.93, 1.0, TimeSpan.FromMilliseconds(200)) { EasingFunction = Smooth });

            if (_currentStep > 1)
                ShowStep(_currentStep - 1);
            else
                BackToSportSelection?.Invoke();
        }

        private void GoToStep1_Click(object sender, MouseButtonEventArgs e) => ShowStep(1);
        private void GoToStep2_Click(object sender, MouseButtonEventArgs e) => ShowStep(2);
        private void GoToStep3_Click(object sender, MouseButtonEventArgs e) => ShowStep(3);
        private void GoToStep4_Click(object sender, MouseButtonEventArgs e) => ShowStep(4);

        // ═══════════════════════════════════════════════════════
        //  RESET
        // ═══════════════════════════════════════════════════════

        public void Reset()
        {
            Result = null;
            _homeLogoPath = null;
            _awayLogoPath = null;
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

            SetupAwayNameBox.Text = "";
            SetupAwayAbbrBox.Text = "";
            SetupAwayColorBox.Text = "#7A1A1A";
            SetupAwaySecondaryBox.Text = "#FFFFFF";
            TrySetPreview(SetupAwayColorPreview, "#7A1A1A");
            TrySetPreview(SetupAwaySecondaryPreview, "#FFFFFF");
            SetupAwayLogoText.Text = "(none)";
            SetupAwayLogoImage.Source = null;
            SetupAwayLogoPreview.Visibility = Visibility.Collapsed;

            // Players
            _homePlayers.Clear();
            _awayPlayers.Clear();

            // Format
            _selectedFormat = CricketFormat.LimitedOvers;
            _selectedOvers = 20;
            UpdateFormatCardVisuals();
            UpdateOversDisplay();

            _setupMessages.Clear();

            _suppressWeatherFilter = true;
            _weatherBoxActivated = false;
            SetupWeatherLocation.Text = "Melbourne, Australia";
            _suppressWeatherFilter = false;

            _presets = PresetStorage.LoadAll();
            RefreshPresetPanels();
            ShowStep(1, animate: false);

            // Replay the cinematic entry
            PlayEntryAnimation();
        }

        // ═══════════════════════════════════════════════════════
        //  FINISH WIZARD
        // ═══════════════════════════════════════════════════════

        private void FinishWizard()
        {
            if (_homePlayers.Count < 2 || _awayPlayers.Count < 2)
            {
                System.Windows.MessageBox.Show(
                    "Each cricket team needs at least two registered players so the innings can have a striker and non-striker. Add the available squad now; you can use a short community squad.",
                    "Complete Both Squads",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                ShowStep(2);
                return;
            }

            var messages = new List<string>();
            foreach (var item in _setupMessages)
            {
                if (!string.IsNullOrWhiteSpace(item.Text))
                    messages.Add(item.Text);
            }

            string homeName = SetupHomeNameBox.Text.Trim();
            string homeAbbr = SetupHomeAbbrBox.Text.Trim();
            string awayName = SetupAwayNameBox.Text.Trim();
            string awayAbbr = SetupAwayAbbrBox.Text.Trim();

            Result = new CricketSetupResult
            {
                TeamAName = string.IsNullOrWhiteSpace(homeName) ? "Team A" : homeName,
                TeamAAbbr = string.IsNullOrWhiteSpace(homeAbbr) ? "TMA" : homeAbbr,
                TeamAPrimaryColor = SetupHomeColorBox.Text.Trim(),
                TeamASecondaryColor = SetupHomeSecondaryBox.Text.Trim(),
                TeamALogoPath = _homeLogoPath,
                TeamAPlayers = [.. _homePlayers],

                TeamBName = string.IsNullOrWhiteSpace(awayName) ? "Team B" : awayName,
                TeamBAbbr = string.IsNullOrWhiteSpace(awayAbbr) ? "TMB" : awayAbbr,
                TeamBPrimaryColor = SetupAwayColorBox.Text.Trim(),
                TeamBSecondaryColor = SetupAwaySecondaryBox.Text.Trim(),
                TeamBLogoPath = _awayLogoPath,
                TeamBPlayers = [.. _awayPlayers],

                Format = _selectedFormat,
                TotalOvers = _selectedOvers,

                TeamABatsFirst = true,

                Messages = messages
            };

            SetupCompleted?.Invoke(Result);
        }  

        // ═══════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════
        //  PLAYER SELECTION — STEP 2
        // ═══════════════════════════════════════════════════════

        private void UpdatePlayerPanelTeamIdentity()
        {
            if (HomePlyTeamName is null) return;
            string homeName = SetupHomeNameBox.Text.Trim();
            string awayName = SetupAwayNameBox.Text.Trim();
            HomePlyTeamName.Text = string.IsNullOrWhiteSpace(homeName) ? "HOME" : homeName.ToUpperInvariant();
            AwayPlyTeamName.Text = string.IsNullOrWhiteSpace(awayName) ? "AWAY" : awayName.ToUpperInvariant();
            try { var c = (Color)ColorConverter.ConvertFromString(SetupHomeColorBox.Text.Trim()); HomePlayerSideBrush.Color = c; HomePlyGlow.Color = c; } catch { }
            try { AwayPlayerSideBrush.Color = (Color)ColorConverter.ConvertFromString(SetupAwayColorBox.Text.Trim()); } catch { }
        }

        private void AddHomePlayer_Click(object sender, MouseButtonEventArgs e)
        {
            string first = HomePlayerFirstBox.Text.Trim();
            string last = HomePlayerLastBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(last)) return;
            _homePlayers.Add(new CricketPlayer { FirstName = first, LastName = last });
            HomePlayerFirstBox.Text = "";
            HomePlayerLastBox.Text = "";
            HomePlayerFirstBox.Focus();
        }

        private void AddAwayPlayer_Click(object sender, MouseButtonEventArgs e)
        {
            string first = AwayPlayerFirstBox.Text.Trim();
            string last = AwayPlayerLastBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(last)) return;
            _awayPlayers.Add(new CricketPlayer { FirstName = first, LastName = last });
            AwayPlayerFirstBox.Text = "";
            AwayPlayerLastBox.Text = "";
            AwayPlayerFirstBox.Focus();
        }

        private void HomePlayerLastBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            AddHomePlayer_Click(sender, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left));
            e.Handled = true;
        }

        private void AwayPlayerLastBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            AddAwayPlayer_Click(sender, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left));
            e.Handled = true;
        }

        private void RemoveHomePlayer_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border { Tag: CricketPlayer player }) _homePlayers.Remove(player);
        }

        private void RemoveAwayPlayer_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border { Tag: CricketPlayer player }) _awayPlayers.Remove(player);
        }

        private void ToggleHomeWicketkeeper_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border { Tag: CricketPlayer tapped }) return;
            bool wasWK = tapped.IsWicketkeeper;
            foreach (var p in _homePlayers) p.IsWicketkeeper = false;
            tapped.IsWicketkeeper = !wasWK;
            var src = HomePlayerList.ItemsSource;
            HomePlayerList.ItemsSource = null;
            HomePlayerList.ItemsSource = src;
        }

        private void ToggleAwayWicketkeeper_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border { Tag: CricketPlayer tapped }) return;
            bool wasWK = tapped.IsWicketkeeper;
            foreach (var p in _awayPlayers) p.IsWicketkeeper = false;
            tapped.IsWicketkeeper = !wasWK;
            var src = AwayPlayerList.ItemsSource;
            AwayPlayerList.ItemsSource = null;
            AwayPlayerList.ItemsSource = src;
        }

        // ═══════════════════════════════════════════════════════
        //  MATCH FORMAT — STEP 3
        // ═══════════════════════════════════════════════════════

        private void LimitedOversCard_Click(object sender, MouseButtonEventArgs e)
        {
            _selectedFormat = CricketFormat.LimitedOvers;
            UpdateFormatCardVisuals();
        }

        private void MultiInningsCard_Click(object sender, MouseButtonEventArgs e)
        {
            _selectedFormat = CricketFormat.MultiInnings;
            UpdateFormatCardVisuals();
        }

        private void UpdateFormatCardVisuals()
        {
            if (LimitedOversCard is null) return;
            bool isLimited = _selectedFormat == CricketFormat.LimitedOvers;
            LimitedOversCard.BorderBrush = new SolidColorBrush(isLimited
                ? (Color)ColorConverter.ConvertFromString("#58A6FF")
                : (Color)ColorConverter.ConvertFromString("#2A3A52"));
            LimitedOversCard.BorderThickness = new Thickness(2);
            LimitedOversCard.Background = new SolidColorBrush(isLimited
                ? (Color)ColorConverter.ConvertFromString("#0A1628")
                : (Color)ColorConverter.ConvertFromString("#0A0E14"));
            LimitedOversGlowBg.Opacity = isLimited ? 0.15 : 0;
            LimitedOversSelected.Visibility = isLimited ? Visibility.Visible : Visibility.Collapsed;
            MultiInningsCard.BorderBrush = new SolidColorBrush(!isLimited
                ? (Color)ColorConverter.ConvertFromString("#3FB950")
                : (Color)ColorConverter.ConvertFromString("#2A3A52"));
            MultiInningsCard.BorderThickness = new Thickness(2);
            MultiInningsCard.Background = new SolidColorBrush(!isLimited
                ? (Color)ColorConverter.ConvertFromString("#0A1E0A")
                : (Color)ColorConverter.ConvertFromString("#0A0E14"));
            MultiInningsGlowBg.Opacity = !isLimited ? 0.15 : 0;
            MultiInningsSelected.Visibility = !isLimited ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateOversDisplay()
        {
            if (OversDisplay is not null) OversDisplay.Text = _selectedOvers.ToString();
        }

        private void OversMinus_Click(object sender, MouseButtonEventArgs e) { if (_selectedOvers > 1) _selectedOvers--; UpdateOversDisplay(); }
        private void OversPlus_Click(object sender, MouseButtonEventArgs e) { if (_selectedOvers < 100) _selectedOvers++; UpdateOversDisplay(); }
        private void OversPreset20_Click(object sender, MouseButtonEventArgs e) { _selectedOvers = 20; UpdateOversDisplay(); }
        private void OversPreset40_Click(object sender, MouseButtonEventArgs e) { _selectedOvers = 40; UpdateOversDisplay(); }
        private void OversPreset50_Click(object sender, MouseButtonEventArgs e) { _selectedOvers = 50; UpdateOversDisplay(); }
        private void OversPreset100_Click(object sender, MouseButtonEventArgs e) { _selectedOvers = 100; UpdateOversDisplay(); }

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
                editBox.CaretIndex = caretPos;

            _suppressWeatherFilter = false;
        }

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

        private void RefreshPresetPanels()
        {
            BuildPresetTiles(HomePresetPanel, HomePresetEmpty, true);
            BuildPresetTiles(AwayPresetPanel, AwayPresetEmpty, false);
        }

        private void BuildPresetTiles(System.Windows.Controls.WrapPanel panel, TextBlock emptyLabel, bool isHome)
        {
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

                    var scaleEase = new ElasticEase { Oscillations = 1, Springiness = 5, EasingMode = EasingMode.EaseOut };
                    var scaleIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)) { EasingFunction = scaleEase };
                    ((ScaleTransform)tickText.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
                    ((ScaleTransform)tickText.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn.Clone());

                    overlay.Opacity = 0;
                    overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
                    {
                        EasingFunction = Smooth
                    });
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
                    ToolTip = $"{preset.TeamName}\n{abbr}\n{(HasValidFile(preset.LogoPath) ? "Logo set" : "No logo")}",
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

                tile.MouseEnter += (_, _) =>
                {
                    if (_isDragging) return;
                    tileScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty,
                        new DoubleAnimation(1.06, TimeSpan.FromMilliseconds(140)) { EasingFunction = Smooth });
                    tileScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty,
                        new DoubleAnimation(1.06, TimeSpan.FromMilliseconds(140)) { EasingFunction = Smooth });
                    tile.BeginAnimation(OpacityProperty,
                        new DoubleAnimation(0.92, TimeSpan.FromMilliseconds(120)) { EasingFunction = Smooth });
                };
                tile.MouseLeave += (_, _) =>
                {
                    if (_isDragging) return;
                    tileScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty,
                        new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200)) { EasingFunction = Smooth });
                    tileScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty,
                        new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200)) { EasingFunction = Smooth });
                    tile.BeginAnimation(OpacityProperty,
                        new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180)));
                };

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
            => SavePreset(SetupHomeNameBox, SetupHomeAbbrBox, SetupHomeColorBox, SetupHomeSecondaryBox, _homeLogoPath, true);

        private void DeleteHomePreset_Click(object sender, RoutedEventArgs e)
            => DeletePreset(_selectedHomePreset);

        private void SaveAwayPreset_Click(object sender, RoutedEventArgs e)
            => SavePreset(SetupAwayNameBox, SetupAwayAbbrBox, SetupAwayColorBox, SetupAwaySecondaryBox, _awayLogoPath, false);

        private void DeleteAwayPreset_Click(object sender, RoutedEventArgs e)
            => DeletePreset(_selectedAwayPreset);

        private void SavePreset(TextBox nameBox, TextBox abbrBox, TextBox colorBox, TextBox secBox, string? logoPath, bool isHome)
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

        private static DoubleAnimation Anim(double to, TimeSpan duration, IEasingFunction? easing = null)
        {
            return new DoubleAnimation(to, duration) { EasingFunction = easing };
        }

        private static void Animate(IAnimatable target, DependencyProperty prop,
            double to, int durationMs, int delay = 0, IEasingFunction? easing = null)
        {
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



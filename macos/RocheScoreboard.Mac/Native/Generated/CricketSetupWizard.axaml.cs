using RocheScoreboard.Mac.Production;
// Generated from Views/CricketSetupWizard.xaml.cs; edit translator/adapters.
// Source SHA-256: bed4dddd52307677cc7ee239609ff1cc044b72d731dfed3676a5bd814800397b
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
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;

using DrawingColor = System.Drawing.Color;
using Color = Avalonia.Media.Color;
using ColorConverter = RocheScoreboard.Mac.Native.ColorConverter;
using TextBox = Avalonia.Controls.TextBox;
using Border = Avalonia.Controls.Border;
using MouseEventArgs = Avalonia.Input.PointerEventArgs;

namespace Roche_Scoreboard.Views
{
    public partial class CricketSetupWizard : Avalonia.Controls.UserControl
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
        private Avalonia.Point _dragStartPos;
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
            NativePointer.EnsureInitialized(); InitializeComponent(); ProductionNames.CompleteLoad(this);
            SetupMessageList.ItemsSource = _setupMessages;
            HomePlayerList.ItemsSource = _homePlayers;
            AwayPlayerList.ItemsSource = _awayPlayers;
            _presets = PresetStorage.LoadAll();
            RefreshPresetPanels();

            SetupWeatherLocation.AddHandler(
                TextBox.TextChangedEvent,
                new EventHandler<TextChangedEventArgs>(SetupWeatherLocation_TextChanged));
            SetupWeatherLocation.GotFocus += (_, _) => _weatherBoxActivated = true;
            SetupWeatherLocation.DropDownOpened += (_, _) => _weatherBoxActivated = true;

            Loaded += OnLoaded;
        }

        // ═══════════════════════════════════════════════════════
        //  ENTRY ANIMATION — staggered cinematic reveal
        // ═══════════════════════════════════════════════════════

        private void OnLoaded(object? sender, RoutedEventArgs e)
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

        private void NavBtn_MouseEnter(object? sender, MouseEventArgs e)
        {
            if (sender is not Border btn) return;
            ScaleTransform sc = btn == BackButton ? BackBtnScale : NextBtnScale;
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1.05, HoverIn, Smooth));
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1.05, HoverIn, Smooth));
        }

        private void NavBtn_MouseLeave(object? sender, MouseEventArgs e)
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

            Control[] panels = [TeamsStepContainer, Step3Panel, Step3bPanel, Step4Panel];
            Control activePanel = step switch
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
            foreach (Control panel in panels)
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
                Control? outgoing = null;
                foreach (Control p in panels)
                {
                    if (p.IsVisible == true)
                    {
                        outgoing = p;
                        break;
                    }
                }

                if (outgoing is not null)
                {
                    Control fading = outgoing;
                    fading.RenderTransform = new TranslateTransform(0, 0);

                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                    var slideOut = new DoubleAnimation(0, slideOutX, TimeSpan.FromMilliseconds(250))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };

                    fadeOut.Completed += (_, _) =>
                    {
                        if (transitionVersion != _stepTransitionVersion || ReferenceEquals(fading, activePanel)) return;
                        fading.BeginAnimation(OpacityProperty, null);
                        fading.IsVisible = false;
                        fading.Opacity = 1;
                        fading.RenderTransform = null;
                    };

                    fading.BeginAnimation(OpacityProperty, fadeOut);
                    ((TranslateTransform)fading.RenderTransform).BeginAnimation(TranslateTransform.XProperty, slideOut);
                }
            }
            else
            {
                foreach (Control p in panels)
                {
                    p.BeginAnimation(OpacityProperty, null);
                    p.IsVisible = false;
                    p.Opacity = 1;
                    p.RenderTransform = null;
                }
            }

            if (animate && step != previousStep)
            {
                activePanel.IsVisible = true;
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
                activePanel.IsVisible = true;
                activePanel.Opacity = 1;
                activePanel.RenderTransform = null;
            }

            BackButton.IsVisible = true;
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
                    _ambientRestartTimer = new Avalonia.Threading.DispatcherTimer
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
            var dotText = NativeObjects.Require<TextBlock>(dot.Child);
            dotText.Foreground = reached ? s_whiteBrush : s_textMutedBrush;
            label.Foreground = active ? s_whiteBrush : (reached ? s_accentBrush : s_textMutedBrush);

            // Glow effect on active dot
            if (dot.Effect is DropShadowEffect glow)
                glow.Opacity = active ? 0.6 : 0;
        }

        private void Next_Click(object? sender, PointerEventArgs e)
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

        private void Back_Click(object? sender, PointerEventArgs e)
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

        private void GoToStep1_Click(object? sender, PointerEventArgs e) => ShowStep(1);
        private void GoToStep2_Click(object? sender, PointerEventArgs e) => ShowStep(2);
        private void GoToStep3_Click(object? sender, PointerEventArgs e) => ShowStep(3);
        private void GoToStep4_Click(object? sender, PointerEventArgs e) => ShowStep(4);

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
            SetupHomeLogoPreview.IsVisible = false;

            SetupAwayNameBox.Text = "";
            SetupAwayAbbrBox.Text = "";
            SetupAwayColorBox.Text = "#7A1A1A";
            SetupAwaySecondaryBox.Text = "#FFFFFF";
            TrySetPreview(SetupAwayColorPreview, "#7A1A1A");
            TrySetPreview(SetupAwaySecondaryPreview, "#FFFFFF");
            SetupAwayLogoText.Text = "(none)";
            SetupAwayLogoImage.Source = null;
            SetupAwayLogoPreview.IsVisible = false;

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
                RocheScoreboard.Mac.Native.NativeMessageBox.Show(
                    "Each cricket team needs at least two registered players so the innings can have a striker and non-striker. Add the available squad now; you can use a short community squad.",
                    "Complete Both Squads",
                    RocheScoreboard.Mac.Native.NativeMessageBoxButton.OK,
                    RocheScoreboard.Mac.Native.NativeMessageBoxImage.Information);
                ShowStep(2);
                return;
            }

            var messages = new List<string>();
            foreach (var item in _setupMessages)
            {
                if (!string.IsNullOrWhiteSpace((item.Text ?? string.Empty)))
                    messages.Add((item.Text ?? string.Empty));
            }

            string homeName = (SetupHomeNameBox.Text ?? string.Empty).Trim();
            string homeAbbr = (SetupHomeAbbrBox.Text ?? string.Empty).Trim();
            string awayName = (SetupAwayNameBox.Text ?? string.Empty).Trim();
            string awayAbbr = (SetupAwayAbbrBox.Text ?? string.Empty).Trim();

            Result = new CricketSetupResult
            {
                TeamAName = string.IsNullOrWhiteSpace(homeName) ? "Team A" : homeName,
                TeamAAbbr = string.IsNullOrWhiteSpace(homeAbbr) ? "TMA" : homeAbbr,
                TeamAPrimaryColor = (SetupHomeColorBox.Text ?? string.Empty).Trim(),
                TeamASecondaryColor = (SetupHomeSecondaryBox.Text ?? string.Empty).Trim(),
                TeamALogoPath = _homeLogoPath,
                TeamAPlayers = [.. _homePlayers],

                TeamBName = string.IsNullOrWhiteSpace(awayName) ? "Team B" : awayName,
                TeamBAbbr = string.IsNullOrWhiteSpace(awayAbbr) ? "TMB" : awayAbbr,
                TeamBPrimaryColor = (SetupAwayColorBox.Text ?? string.Empty).Trim(),
                TeamBSecondaryColor = (SetupAwaySecondaryBox.Text ?? string.Empty).Trim(),
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
            string homeName = (SetupHomeNameBox.Text ?? string.Empty).Trim();
            string awayName = (SetupAwayNameBox.Text ?? string.Empty).Trim();
            HomePlyTeamName.Text = string.IsNullOrWhiteSpace(homeName) ? "HOME" : homeName.ToUpperInvariant();
            AwayPlyTeamName.Text = string.IsNullOrWhiteSpace(awayName) ? "AWAY" : awayName.ToUpperInvariant();
            try { var c = (Color)ColorConverter.ConvertFromString((SetupHomeColorBox.Text ?? string.Empty).Trim()); HomePlayerSideBrush.Color = c; HomePlyGlow.Color = c; } catch { }
            try { AwayPlayerSideBrush.Color = (Color)ColorConverter.ConvertFromString((SetupAwayColorBox.Text ?? string.Empty).Trim()); } catch { }
        }

        private void AddHomePlayer_Click(object? sender, RoutedEventArgs e)
        {
            string first = (HomePlayerFirstBox.Text ?? string.Empty).Trim();
            string last = (HomePlayerLastBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(last)) return;
            _homePlayers.Add(new CricketPlayer { FirstName = first, LastName = last });
            HomePlayerFirstBox.Text = "";
            HomePlayerLastBox.Text = "";
            HomePlayerFirstBox.Focus();
        }

        private void AddAwayPlayer_Click(object? sender, RoutedEventArgs e)
        {
            string first = (AwayPlayerFirstBox.Text ?? string.Empty).Trim();
            string last = (AwayPlayerLastBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(last)) return;
            _awayPlayers.Add(new CricketPlayer { FirstName = first, LastName = last });
            AwayPlayerFirstBox.Text = "";
            AwayPlayerLastBox.Text = "";
            AwayPlayerFirstBox.Focus();
        }

        private void HomePlayerLastBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            AddHomePlayer_Click(sender, e);
            e.Handled = true;
        }

        private void AwayPlayerLastBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            AddAwayPlayer_Click(sender, e);
            e.Handled = true;
        }

        private void RemoveHomePlayer_Click(object? sender, PointerEventArgs e)
        {
            if (sender is Border { Tag: CricketPlayer player }) _homePlayers.Remove(player);
        }

        private void RemoveAwayPlayer_Click(object? sender, PointerEventArgs e)
        {
            if (sender is Border { Tag: CricketPlayer player }) _awayPlayers.Remove(player);
        }

        private void ToggleHomeWicketkeeper_Click(object? sender, PointerEventArgs e)
        {
            if (sender is not Border { Tag: CricketPlayer tapped }) return;
            bool wasWK = tapped.IsWicketkeeper;
            foreach (var p in _homePlayers) p.IsWicketkeeper = false;
            tapped.IsWicketkeeper = !wasWK;
            var src = HomePlayerList.ItemsSource;
            HomePlayerList.ItemsSource = null;
            HomePlayerList.ItemsSource = src;
        }

        private void ToggleAwayWicketkeeper_Click(object? sender, PointerEventArgs e)
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

        private void LimitedOversCard_Click(object? sender, PointerEventArgs e)
        {
            _selectedFormat = CricketFormat.LimitedOvers;
            UpdateFormatCardVisuals();
        }

        private void MultiInningsCard_Click(object? sender, PointerEventArgs e)
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
            LimitedOversSelected.IsVisible = isLimited ? true : false;
            MultiInningsCard.BorderBrush = new SolidColorBrush(!isLimited
                ? (Color)ColorConverter.ConvertFromString("#3FB950")
                : (Color)ColorConverter.ConvertFromString("#2A3A52"));
            MultiInningsCard.BorderThickness = new Thickness(2);
            MultiInningsCard.Background = new SolidColorBrush(!isLimited
                ? (Color)ColorConverter.ConvertFromString("#0A1E0A")
                : (Color)ColorConverter.ConvertFromString("#0A0E14"));
            MultiInningsGlowBg.Opacity = !isLimited ? 0.15 : 0;
            MultiInningsSelected.IsVisible = !isLimited ? true : false;
        }

        private void UpdateOversDisplay()
        {
            if (OversDisplay is not null) OversDisplay.Text = _selectedOvers.ToString();
        }

        private void OversMinus_Click(object? sender, PointerEventArgs e) { if (_selectedOvers > 1) _selectedOvers--; UpdateOversDisplay(); }
        private void OversPlus_Click(object? sender, PointerEventArgs e) { if (_selectedOvers < 100) _selectedOvers++; UpdateOversDisplay(); }
        private void OversPreset20_Click(object? sender, PointerEventArgs e) { _selectedOvers = 20; UpdateOversDisplay(); }
        private void OversPreset40_Click(object? sender, PointerEventArgs e) { _selectedOvers = 40; UpdateOversDisplay(); }
        private void OversPreset50_Click(object? sender, PointerEventArgs e) { _selectedOvers = 50; UpdateOversDisplay(); }
        private void OversPreset100_Click(object? sender, PointerEventArgs e) { _selectedOvers = 100; UpdateOversDisplay(); }

        // ═══════════════════════════════════════════════════════
        //  WEATHER LOCATION AUTOCOMPLETE
        // ═══════════════════════════════════════════════════════

        private void SetupWeatherLocation_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_suppressWeatherFilter || !_weatherBoxActivated) return;

            var combo = SetupWeatherLocation;
            string input = (combo.Text ?? string.Empty) ?? "";

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

            var editBox = NativeTemplates.Find(combo, "PART_EditableTextBox") as Avalonia.Controls.TextBox;
            int caretPos = editBox?.CaretIndex ?? input.Length;
            combo.IsDropDownOpen = matches.Count > 0;
            if (editBox is not null)
                editBox.CaretIndex = caretPos;

            _suppressWeatherFilter = false;
        }

        //  COLOUR PICKERS
        // ═══════════════════════════════════════════════════════

        private void PickSetupHomeColor_Click(object? sender, PointerEventArgs e) => PickColor(SetupHomeColorBox, SetupHomeColorPreview);
        private void PickSetupHomeSecondary_Click(object? sender, PointerEventArgs e) => PickColor(SetupHomeSecondaryBox, SetupHomeSecondaryPreview);
        private void PickSetupAwayColor_Click(object? sender, PointerEventArgs e) => PickColor(SetupAwayColorBox, SetupAwayColorPreview);
        private void PickSetupAwaySecondary_Click(object? sender, PointerEventArgs e) => PickColor(SetupAwaySecondaryBox, SetupAwaySecondaryPreview);

        private void DropperSetupHomeColor_Click(object? sender, PointerEventArgs e) => DropColor(SetupHomeColorBox, SetupHomeColorPreview);
        private void DropperSetupHomeSecondary_Click(object? sender, PointerEventArgs e) => DropColor(SetupHomeSecondaryBox, SetupHomeSecondaryPreview);
        private void DropperSetupAwayColor_Click(object? sender, PointerEventArgs e) => DropColor(SetupAwayColorBox, SetupAwayColorPreview);
        private void DropperSetupAwaySecondary_Click(object? sender, PointerEventArgs e) => DropColor(SetupAwaySecondaryBox, SetupAwaySecondaryPreview);

        private static void PickColor(TextBox box, Border preview)
        {
            Color initial = Colors.White;
            try
            {
                initial = (Color)ColorConverter.ConvertFromString((box.Text ?? string.Empty));
            }
            catch { /* ignore */ }

            if (!ColorPickerWindow.TryPick(NativeWindow.GetWindow(box), initial, out Color c)) return;
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

        private void ChooseSetupHomeLogo_Click(object? sender, RoutedEventArgs e)
        {
            var path = PickLogoFile("Home");
            if (path == null) return;
            _homeLogoPath = path;
            SetupHomeLogoText.Text = System.IO.Path.GetFileName(path);
            ShowLogoPreview(SetupHomeLogoImage, SetupHomeLogoPreview, path);
        }

        private void ChooseSetupAwayLogo_Click(object? sender, RoutedEventArgs e)
        {
            var path = PickLogoFile("Away");
            if (path == null) return;
            _awayLogoPath = path;
            SetupAwayLogoText.Text = System.IO.Path.GetFileName(path);
            ShowLogoPreview(SetupAwayLogoImage, SetupAwayLogoPreview, path);
        }

        private static string? PickLogoFile(string teamLabel)
        {
            var dlg = new NativeOpenFileDialog
            {
                Title = $"Select {teamLabel} Team Logo",
                Filter = Services.ImageLoadHelper.LogoFilter
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        private static void ShowLogoPreview(Avalonia.Controls.Image image, Border container, string? path)
        {
            var source = Services.ImageLoadHelper.Load(path, decodePixelHeight: 160);
            image.Source = source;
            container.IsVisible = source is not null ? true : false;
        }

        // ═══════════════════════════════════════════════════════
        //  MESSAGES
        // ═══════════════════════════════════════════════════════

        private void SetupAddMessage_Click(object? sender, RoutedEventArgs e)
        {
            string? text = (SetupNewMessageBox.Text ?? string.Empty)?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;
            _setupMessages.Add(new MarqueeMessage(text, _newMessageTextColor, _newMessageHighlightColor));
            SetupNewMessageBox.Text = "";
        }

        private void SetupNewMessageBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            SetupAddMessage_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }

        private void SetupRemoveMessageItem_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Avalonia.Controls.Button btn) return;
            if (btn.Tag is not MarqueeMessage msg) return;
            _setupMessages.Remove(msg);
        }

        private void SetupPickItemTextColor_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Avalonia.Controls.Button btn) return;
            if (btn.Tag is not MarqueeMessage msg) return;
            var initial = ParseDrawingColor(msg.TextColor, DrawingColor.White);
            if (!TryPickColor(initial, out var picked)) return;
            msg.TextColor = ToHex(picked);
        }

        private void SetupPickItemHighlightColor_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Avalonia.Controls.Button btn) return;
            if (btn.Tag is not MarqueeMessage msg) return;
            var initial = ParseDrawingColor(msg.HighlightColor, DrawingColor.FromArgb(255, 30, 64, 175));
            if (!TryPickColor(initial, out var picked)) return;
            msg.HighlightColor = ToHex(picked);
        }

        private void SetupClearItemHighlightColor_Click(object? sender, PointerEventArgs e)
        {
            if (sender is not Avalonia.Controls.Button btn) return;
            if (btn.Tag is not MarqueeMessage msg) return;
            msg.HighlightColor = null;
        }

        private void SetupPickNewTextColor_Click(object? sender, RoutedEventArgs e)
        {
            var initial = ParseDrawingColor(_newMessageTextColor, DrawingColor.White);
            if (!TryPickColor(initial, out var picked)) return;
            _newMessageTextColor = ToHex(picked);
            if (SetupNewMessageTextColorBtn != null)
                SetupNewMessageTextColorBtn.Background = new SolidColorBrush(Color.FromRgb(picked.R, picked.G, picked.B));
        }

        private void SetupPickNewHighlightColor_Click(object? sender, RoutedEventArgs e)
        {
            var initial = ParseDrawingColor(_newMessageHighlightColor, DrawingColor.FromArgb(255, 30, 64, 175));
            if (!TryPickColor(initial, out var picked)) return;
            _newMessageHighlightColor = ToHex(picked);
            if (SetupNewMessageHighlightColorBtn != null)
                SetupNewMessageHighlightColorBtn.Background = new SolidColorBrush(Color.FromRgb(picked.R, picked.G, picked.B));
        }

        private void SetupClearNewHighlightColor_Click(object? sender, PointerEventArgs e)
        {
            _newMessageHighlightColor = null;
            if (SetupNewMessageHighlightColorBtn != null)
                SetupNewMessageHighlightColorBtn.Background = Avalonia.Media.Brushes.Transparent;
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
            bool accepted = ColorPickerWindow.TryPick(NativeDesktop.MainWindow,
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

        private void BuildPresetTiles(Avalonia.Controls.WrapPanel panel, TextBlock emptyLabel, bool isHome)
        {
            panel.Children.Clear();

            if (_presets.Count == 0)
            {
                emptyLabel.IsVisible = true;
                return;
            }

            emptyLabel.IsVisible = false;

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
                    FontWeight = FontWeight.Black,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    IsHitTestVisible = false
                };

                TextBlock nameLabel = new()
                {
                    Text = string.IsNullOrWhiteSpace(preset.TeamName) ? "Unnamed team" : preset.TeamName,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xCC, fgColor.R, fgColor.G, fgColor.B)),
                    FontSize = 9,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 94,
                    IsHitTestVisible = false
                };

                StackPanel stack = new()
                {
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    IsHitTestVisible = false
                };
                stack.Children.Add(abbrLabel);
                stack.Children.Add(nameLabel);

                Grid tileContent = new() { IsHitTestVisible = false };
                tileContent.Children.Add(stack);

                if (isSelected)
                {
                    Border overlay = new NativeBorder()
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
                        FontWeight = FontWeight.Black,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        IsHitTestVisible = false,
                        RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
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

                Border tile = new NativeBorder()
                {
                    Width = 110,
                    Height = 70,
                    Padding = new Thickness(4),
                    Margin = new Thickness(0, 0, 8, 8),
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                    ToolTip = $"{preset.TeamName}\n{abbr}\n{(HasValidFile(preset.LogoPath) ? "Logo set" : "No logo")}",
                    Child = tileContent,
                    Tag = preset,
                    BorderBrush = isSelected
                        ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E))
                        : new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(2),
                    RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                    RenderTransform = tileTransformGroup,
                    Background = NativeBrush.Linear(
                        BlendColor(bgColor, Colors.Black, 0.28),
                        BlendColor(bgColor, Colors.White, 0.08),
                        new Avalonia.Point(0, 0),
                        new Avalonia.Point(1, 1))
                };

                tile.PointerPressed += (_, me) =>
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
                        if (panel.Children[i] is Control fe)
                        {
                            Vector offset = VisualTreeHelper.GetOffset(fe);
                            _slotCentersX[i] = offset.X + fe.NativeWidth() / 2;
                            _slotCentersY[i] = offset.Y + fe.NativeHeight() / 2;
                        }
                    }

                    NativePointer.Capture(tile);
                    me.Handled = true;
                };

                tile.PointerMoved += (_, me) =>
                {
                    if (_dragTile != tile || !NativePointer.IsCaptured(tile)) return;

                    Avalonia.Point pos = me.GetPosition(panel);
                    double dx = pos.X - _dragStartPos.X;
                    double dy = pos.Y - _dragStartPos.Y;

                    if (!_isDragging)
                    {
                        if (Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold) return;
                        _isDragging = true;
                        tile.ZIndex = 100;
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

                tile.PointerReleased += (_, me) =>
                {
                    if (_dragTile != tile) return;
                    CompleteDrag(panel, isHome, preset);
                    me.Handled = true;
                };

                tile.PointerCaptureLost += (_, _) =>
                {
                    if (_dragTile != tile) return;
                    CompleteDrag(panel, isHome, preset);
                };

                tile.PointerEntered += (_, _) =>
                {
                    if (_isDragging) return;
                    tileScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty,
                        new DoubleAnimation(1.06, TimeSpan.FromMilliseconds(140)) { EasingFunction = Smooth });
                    tileScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty,
                        new DoubleAnimation(1.06, TimeSpan.FromMilliseconds(140)) { EasingFunction = Smooth });
                    tile.BeginAnimation(OpacityProperty,
                        new DoubleAnimation(0.92, TimeSpan.FromMilliseconds(120)) { EasingFunction = Smooth });
                };
                tile.PointerExited += (_, _) =>
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

        private void CompleteDrag(Avalonia.Controls.WrapPanel panel, bool isHome, TeamPreset clickedPreset)
        {
            Border? tile = _dragTile;
            bool wasDragging = _isDragging;
            int origIndex = _dragOrigIndex;
            int currentIndex = _dragCurrentIndex;

            _dragTile = null;
            _isDragging = false;

            if (NativePointer.IsCaptured(tile) == true)
                NativePointer.Release(tile);

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

        private static void ResetAllTileTransforms(Avalonia.Controls.WrapPanel panel)
        {
            foreach (Control child in panel.Children)
            {
                TranslateTransform? t = GetTranslate(child);
                if (t != null)
                {
                    t.BeginAnimation(TranslateTransform.XProperty, null);
                    t.BeginAnimation(TranslateTransform.YProperty, null);
                    t.X = 0;
                    t.Y = 0;
                }
                child.ZIndex = 0;
                child.Opacity = 1;
            }
        }

        private static TranslateTransform? GetTranslate(Control element)
        {
            if (element is not Control fe) return null;
            if (fe.RenderTransform is TranslateTransform tt) return tt;
            if (fe.RenderTransform is TransformGroup tg)
            {
                foreach (Transform t in tg.Children)
                    if (t is TranslateTransform translate) return translate;
            }
            return null;
        }

        private void ShiftTilesForDrag(Avalonia.Controls.WrapPanel panel, int origIndex, int hoverIndex, Border draggedTile)
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

        private void SetupHomeColorBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            TrySetPreview(SetupHomeColorPreview, (SetupHomeColorBox.Text ?? string.Empty).Trim());
        }

        private void SetupHomeSecondaryBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            TrySetPreview(SetupHomeSecondaryPreview, (SetupHomeSecondaryBox.Text ?? string.Empty).Trim());
        }

        private void SetupAwayColorBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            TrySetPreview(SetupAwayColorPreview, (SetupAwayColorBox.Text ?? string.Empty).Trim());
        }

        private void SetupAwaySecondaryBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            TrySetPreview(SetupAwaySecondaryPreview, (SetupAwaySecondaryBox.Text ?? string.Empty).Trim());
        }

        private static string NormalizeAbbreviation(string? value)
        {
            string cleaned = (value ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(cleaned)) return "TEAM";
            return cleaned.Length > 4 ? cleaned[..4] : cleaned;
        }

        private static bool HasValidFile(string? path)
            => !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path);

        private void SaveHomePreset_Click(object? sender, RoutedEventArgs e)
            => SavePreset(SetupHomeNameBox, SetupHomeAbbrBox, SetupHomeColorBox, SetupHomeSecondaryBox, _homeLogoPath, true);

        private void DeleteHomePreset_Click(object? sender, RoutedEventArgs e)
            => DeletePreset(_selectedHomePreset);

        private void SaveAwayPreset_Click(object? sender, RoutedEventArgs e)
            => SavePreset(SetupAwayNameBox, SetupAwayAbbrBox, SetupAwayColorBox, SetupAwaySecondaryBox, _awayLogoPath, false);

        private void DeleteAwayPreset_Click(object? sender, RoutedEventArgs e)
            => DeletePreset(_selectedAwayPreset);

        private void SavePreset(TextBox nameBox, TextBox abbrBox, TextBox colorBox, TextBox secBox, string? logoPath, bool isHome)
        {
            string name = (nameBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            string abbreviation = NormalizeAbbreviation((abbrBox.Text ?? string.Empty));

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
            target.PrimaryColor = (colorBox.Text ?? string.Empty).Trim();
            target.SecondaryColor = (secBox.Text ?? string.Empty).Trim();
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

        private static void Animate(AvaloniaObject target, AvaloniaProperty prop,
            double to, int durationMs, int delay = 0, IEasingFunction? easing = null)
        {
            var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(durationMs))
            {
                BeginTime = delay > 0 ? TimeSpan.FromMilliseconds(delay) : TimeSpan.Zero,
                EasingFunction = easing
            };
            target.BeginAnimation(prop, anim);
        }

        private static void PulseForever(AvaloniaObject target, AvaloniaProperty prop,
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



namespace Roche_Scoreboard.Views
{
public partial class CricketSetupWizard
{
    private ScaleTransform BdSc => (ScaleTransform)ProductionNames.RegistryFor(this)["BdSc"];
    private TranslateTransform BdLift => (TranslateTransform)ProductionNames.RegistryFor(this)["BdLift"];
    private TranslateTransform BeamTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["BeamTranslate"];
    private TranslateTransform HeaderTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["HeaderTranslate"];
    private TranslateTransform StepIndicatorTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["StepIndicatorTranslate"];
    private DropShadowEffect Step1Glow => (DropShadowEffect)ProductionNames.RegistryFor(this)["Step1Glow"];
    private TranslateTransform HomePresetTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["HomePresetTranslate"];
    private TranslateTransform AwayPresetTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["AwayPresetTranslate"];
    private ScaleTransform HomeCardScale => (ScaleTransform)ProductionNames.RegistryFor(this)["HomeCardScale"];
    private TranslateTransform HomeCardTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["HomeCardTranslate"];
    private TranslateTransform HomeHeaderTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["HomeHeaderTranslate"];
    private ScaleTransform DividerScale => (ScaleTransform)ProductionNames.RegistryFor(this)["DividerScale"];
    private ScaleTransform AwayCardScale => (ScaleTransform)ProductionNames.RegistryFor(this)["AwayCardScale"];
    private TranslateTransform AwayCardTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["AwayCardTranslate"];
    private TranslateTransform AwayHeaderTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["AwayHeaderTranslate"];
    private TranslateTransform Step3Translate => (TranslateTransform)ProductionNames.RegistryFor(this)["Step3Translate"];
    private TranslateTransform PlayersHeaderTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["PlayersHeaderTranslate"];
    private SolidColorBrush HomePlayerSideBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["HomePlayerSideBrush"];
    private DropShadowEffect HomePlyGlow => (DropShadowEffect)ProductionNames.RegistryFor(this)["HomePlyGlow"];
    private SolidColorBrush AwayPlayerSideBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["AwayPlayerSideBrush"];
    private DropShadowEffect AwayPlyGlow => (DropShadowEffect)ProductionNames.RegistryFor(this)["AwayPlyGlow"];
    private TranslateTransform Step3bTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["Step3bTranslate"];
    private TranslateTransform FormatHeaderTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["FormatHeaderTranslate"];
    private TranslateTransform Step4Translate => (TranslateTransform)ProductionNames.RegistryFor(this)["Step4Translate"];
    private TranslateTransform MsgHeaderTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["MsgHeaderTranslate"];
    private TranslateTransform NavBarTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["NavBarTranslate"];
    private ScaleTransform BackBtnScale => (ScaleTransform)ProductionNames.RegistryFor(this)["BackBtnScale"];
    private ScaleTransform NextBtnScale => (ScaleTransform)ProductionNames.RegistryFor(this)["NextBtnScale"];
    private void Native_AddAwayPlayer_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; AddAwayPlayer_Click(sender!, e); }
    private void Native_AddHomePlayer_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; AddHomePlayer_Click(sender!, e); }
    private void Native_Back_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; Back_Click(sender!, e); }
    private void Native_DropperSetupAwayColor_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; DropperSetupAwayColor_Click(sender!, e); }
    private void Native_DropperSetupAwaySecondary_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; DropperSetupAwaySecondary_Click(sender!, e); }
    private void Native_DropperSetupHomeColor_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; DropperSetupHomeColor_Click(sender!, e); }
    private void Native_DropperSetupHomeSecondary_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; DropperSetupHomeSecondary_Click(sender!, e); }
    private void Native_GoToStep1_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; GoToStep1_Click(sender!, e); }
    private void Native_GoToStep2_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; GoToStep2_Click(sender!, e); }
    private void Native_GoToStep3_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; GoToStep3_Click(sender!, e); }
    private void Native_GoToStep4_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; GoToStep4_Click(sender!, e); }
    private void Native_LimitedOversCard_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; LimitedOversCard_Click(sender!, e); }
    private void Native_MultiInningsCard_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; MultiInningsCard_Click(sender!, e); }
    private void Native_NavBtn_MouseEnter(object? sender, PointerEventArgs e) { NavBtn_MouseEnter(sender!, e); }
    private void Native_NavBtn_MouseLeave(object? sender, PointerEventArgs e) { NavBtn_MouseLeave(sender!, e); }
    private void Native_Next_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; Next_Click(sender!, e); }
    private void Native_OversMinus_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; OversMinus_Click(sender!, e); }
    private void Native_OversPlus_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; OversPlus_Click(sender!, e); }
    private void Native_OversPreset100_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; OversPreset100_Click(sender!, e); }
    private void Native_OversPreset20_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; OversPreset20_Click(sender!, e); }
    private void Native_OversPreset40_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; OversPreset40_Click(sender!, e); }
    private void Native_OversPreset50_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; OversPreset50_Click(sender!, e); }
    private void Native_PickSetupAwayColor_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; PickSetupAwayColor_Click(sender!, e); }
    private void Native_PickSetupAwaySecondary_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; PickSetupAwaySecondary_Click(sender!, e); }
    private void Native_PickSetupHomeColor_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; PickSetupHomeColor_Click(sender!, e); }
    private void Native_PickSetupHomeSecondary_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; PickSetupHomeSecondary_Click(sender!, e); }
    private void Native_RemoveAwayPlayer_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; RemoveAwayPlayer_Click(sender!, e); }
    private void Native_RemoveHomePlayer_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; RemoveHomePlayer_Click(sender!, e); }
    private void Native_SetupAwayColorBox_TextChanged(object? sender, TextChangedEventArgs e) { SetupAwayColorBox_TextChanged(sender!, e); }
    private void Native_SetupAwaySecondaryBox_TextChanged(object? sender, TextChangedEventArgs e) { SetupAwaySecondaryBox_TextChanged(sender!, e); }
    private void Native_SetupClearItemHighlightColor_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Right) return; SetupClearItemHighlightColor_Click(sender!, e); }
    private void Native_SetupClearNewHighlightColor_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Right) return; SetupClearNewHighlightColor_Click(sender!, e); }
    private void Native_SetupHomeColorBox_TextChanged(object? sender, TextChangedEventArgs e) { SetupHomeColorBox_TextChanged(sender!, e); }
    private void Native_SetupHomeSecondaryBox_TextChanged(object? sender, TextChangedEventArgs e) { SetupHomeSecondaryBox_TextChanged(sender!, e); }
    private void Native_ToggleAwayWicketkeeper_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; ToggleAwayWicketkeeper_Click(sender!, e); }
    private void Native_ToggleHomeWicketkeeper_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; ToggleHomeWicketkeeper_Click(sender!, e); }
}
}

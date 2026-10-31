using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        private int _presetOwnerGeneration, _presetReadVersion, _librarySessionVersion;
        private long _presetRevision;
        private bool _presetOperationPending, _presetLoading, _presetLoadingMessage, _libraryOpen, _presetRefreshAfterDrag;
        private TeamLibrarySport PresetSport => TeamLibrarySport.Cricket;
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
            WizardFlowDesign.Enable(this, () => ShowStep(_currentStep, false));
            InitializePlayerEntry();
            UpdateFormatCardVisuals();
            UpdateOversDisplay();
            SetupMessageList.ItemsSource = _setupMessages;
            HomePlayerList.ItemsSource = _homePlayers;
            AwayPlayerList.ItemsSource = _awayPlayers;
            PresetReadResult initialPresets = PresetStorage.ReadSnapshot(PresetSport);
            _presets = initialPresets.Presets.Select(PresetStorage.Clone).ToList();
            _presetRevision = initialPresets.Revision;
            _presetLoading = _presetLoadingMessage = initialPresets.Pending;
            if (initialPresets.Pending) WizardTeamEditor.SetPresetStatus(this, "Home", "Loading saved clubs…");
            if (!initialPresets.Succeeded) WizardTeamEditor.SetPresetStatus(this, "Home", initialPresets.Error);
            WizardTeamEditor.SetPresetActionsPending(this, _presetLoading);
            RefreshPresetPanels();

            SetupWeatherLocation.AddHandler(
                System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler(SetupWeatherLocation_TextChanged));
            SetupWeatherLocation.GotFocus += (_, _) => _weatherBoxActivated = true;
            SetupWeatherLocation.DropDownOpened += (_, _) => _weatherBoxActivated = true;

            Loaded += OnLoaded;
            Unloaded += (_, _) =>
            {
                InvalidatePresetOwner();
                StopAmbientAnimations(); ++_stepTransitionVersion;
                if (_stepTransitionStoryboard is not null) { _stepTransitionStoryboard.Remove(this); _stepTransitionStoryboard = null; }
            };
            IsVisibleChanged += (_, args) =>
            {
                if (args.NewValue is false) { InvalidatePresetOwner(); StopAmbientAnimations(); ShowStep(_currentStep, false); }
                else if (IsLoaded) { StartAmbientAnimations(); RefreshSavedPresets(); }
            };
        }

        // ═══════════════════════════════════════════════════════
        //  ENTRY ANIMATION — staggered cinematic reveal
        // ═══════════════════════════════════════════════════════

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RefreshSavedPresets();
            ShowStep(1, animate: false);
            PlayEntryAnimation();
        }

        private void PlayEntryAnimation()
        {
            SettleFlowSurfaces();
            if (OperatorMotion.Enabled && IsLoaded) SharedSurfaceMotion.Reveal(TeamsStepContainer);
        }

        private void SettleFlowSurfaces()
        {
            foreach (var element in new UIElement[] { HeaderPanel, StepIndicator, HomePresetCard, AwayPresetCard, HomeTeamCard, AwayTeamCard, HomeHeaderGroup, AwayHeaderGroup, PlayersHeaderGroup, FormatHeaderGroup, MsgHeaderGroup, NavBar })
            { element.BeginAnimation(OpacityProperty, null); element.Opacity = 1; }
            foreach (var shift in new[] { HeaderTranslate, StepIndicatorTranslate, HomePresetTranslate, AwayPresetTranslate, HomeCardTranslate, AwayCardTranslate, HomeHeaderTranslate, AwayHeaderTranslate, PlayersHeaderTranslate, FormatHeaderTranslate, MsgHeaderTranslate, NavBarTranslate })
            { shift.BeginAnimation(TranslateTransform.XProperty, null); shift.BeginAnimation(TranslateTransform.YProperty, null); shift.X = shift.Y = 0; }
            WizardFlowDesign.StopDecoration(this);
        }

        private void StopAmbientAnimations()
        {
            _ambientRestartTimer?.Stop();
            HomeGlow.BeginAnimation(OpacityProperty, null); AwayGlow.BeginAnimation(OpacityProperty, null);
            BgGlow.BeginAnimation(OpacityProperty, null); BeamTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        }
        private void StartAmbientAnimations()
        {
            StopAmbientAnimations();
            HomeGlow.Opacity = AwayGlow.Opacity = 0;
            WizardFlowDesign.StopDecoration(this);
            BgGlow.Opacity = 0;
            BeamTranslate.X = -300;
        }

        // ═══════════════════════════════════════════════════════
        //  HOVER — nav button scale effects
        // ═══════════════════════════════════════════════════════

        private void NavBtn_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement btn || !OperatorMotion.Enabled) return;
            ScaleTransform sc = btn == BackButton ? BackBtnScale : NextBtnScale;
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(.99, OperatorMotion.HoverIn, Smooth));
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(.99, OperatorMotion.HoverIn, Smooth));
        }

        private void NavBtn_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement btn || !OperatorMotion.Enabled) return;
            ScaleTransform sc = btn == BackButton ? BackBtnScale : NextBtnScale;
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1.0, HoverOut, Smooth));
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1.0, HoverOut, Smooth));
        }

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
            FrameworkElement[] panels = [TeamsStepContainer, Step3Panel, Step3bPanel, Step4Panel];
            FrameworkElement active = panels[_currentStep - 1];
            foreach (var panel in panels)
            {
                panel.BeginAnimation(OpacityProperty, null); panel.Opacity = 1;
                if (panel.RenderTransform is TranslateTransform old) old.BeginAnimation(TranslateTransform.XProperty, null);
                panel.RenderTransform = Transform.Identity;
                panel.Visibility = ReferenceEquals(panel, active) ? Visibility.Visible : Visibility.Collapsed;
            }
            SettleFlowSurfaces();
            if (_currentStep == 2) UpdatePlayerPanelTeamIdentity();
            if (_currentStep == 3) UpdateFormatCardVisuals();
            if (animate && previous != _currentStep && OperatorMotion.Enabled && IsLoaded)
            {
                var shift = new TranslateTransform(); active.RenderTransform = shift;
                var storyboard = new Storyboard();
                var fade = new DoubleAnimation(.86, 1, OperatorMotion.Page) { FillBehavior = FillBehavior.Stop, EasingFunction = OperatorMotion.SmoothOut };
                Storyboard.SetTarget(fade, active); Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty)); storyboard.Children.Add(fade);
                var slide = new DoubleAnimation((_currentStep > previous ? 8 : -8) * OperatorMotion.TravelScale, 0, OperatorMotion.Page) { FillBehavior = FillBehavior.Stop, EasingFunction = OperatorMotion.SmoothOut };
                Storyboard.SetTarget(slide, shift); Storyboard.SetTargetProperty(slide, new PropertyPath(TranslateTransform.XProperty)); storyboard.Children.Add(slide);
                storyboard.Completed += (_, _) => { if (version != _stepTransitionVersion) return; storyboard.Remove(this); active.Opacity = 1; active.RenderTransform = Transform.Identity; _stepTransitionStoryboard = null; };
                _stepTransitionStoryboard = storyboard; storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
            }
            BackButton.Visibility = Visibility.Visible;
            NextButtonText.Text = _currentStep == TotalSteps ? "Start match" : "Continue";
            UpdateStepDot(Step1Dot, Step1Label, true, _currentStep == 1);
            UpdateStepDot(Step2Dot, Step2Label, _currentStep >= 2, _currentStep == 2);
            UpdateStepDot(Step3Dot, Step3Label, _currentStep >= 3, _currentStep == 3);
            UpdateStepDot(Step4Dot, Step4Label, _currentStep >= 4, _currentStep == 4);
            WizardFlowDesign.SelectStep(this, _currentStep, TotalSteps);
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
            if (_completionPending || Result is not null) return;
            if (_currentStep == 3 && !ValidateOversEntry()) return;
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
        private void GoToStep4_Click(object sender, RoutedEventArgs e) => ShowStep(4);

        // ═══════════════════════════════════════════════════════
        //  RESET
        // ═══════════════════════════════════════════════════════

        public void Reset()
        {
            InvalidatePresetOwner();
            // A visible reset is a new draft too. Invalidate the editor before
            // cancelling its sheet: an accepted result may already be queued.
            ++_playerEntryGeneration;
            if (Content is Grid host) CricketPrompt.CancelAll(host);
            Result = null;
            _completionPending = false;
            TossWinnerChoice.SelectedIndex = 0;
            TossDecisionChoice.SelectedIndex = 0;
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
            HomePlayerFirstBox.Clear();
            HomePlayerLastBox.Clear();
            AwayPlayerFirstBox.Clear();
            AwayPlayerLastBox.Clear();

            // Format
            _selectedFormat = CricketFormat.LimitedOvers;
            _selectedOvers = 20;
            ReplaceWideBallsToggle.IsChecked=ReplaceNoBallsToggle.IsChecked=true;FreeHitsToggle.IsChecked=false;
            UpdateFormatCardVisuals();
            UpdateOversDisplay();

            _setupMessages.Clear();

            _suppressWeatherFilter = true;
            _weatherBoxActivated = false;
            SetupWeatherLocation.Text = "Melbourne, Australia";
            _suppressWeatherFilter = false;

            PresetReadResult initialPresets = PresetStorage.ReadSnapshot(PresetSport);
            _presets = initialPresets.Presets.Select(PresetStorage.Clone).ToList();
            _presetRevision = initialPresets.Revision;
            _presetLoading = _presetLoadingMessage = initialPresets.Pending;
            if (initialPresets.Pending) WizardTeamEditor.SetPresetStatus(this, "Home", "Loading saved clubs…");
            if (!initialPresets.Succeeded) WizardTeamEditor.SetPresetStatus(this, "Home", initialPresets.Error);
            WizardTeamEditor.SetPresetActionsPending(this, _presetLoading);
            RefreshPresetPanels();
            ShowStep(1, animate: false);

            // Replay the cinematic entry
            PlayEntryAnimation();
            RefreshSavedPresets();
        }

        // ═══════════════════════════════════════════════════════
        //  FINISH WIZARD
        // ═══════════════════════════════════════════════════════

        private void FinishWizardCore()
        {
            if (!ValidateOversEntry()) return;
            if (_homePlayers.Count == 1 || _awayPlayers.Count == 1)
            {
                System.Windows.MessageBox.Show(
                    "A named squad needs at least two players. Add another player, or leave that squad empty to use 11 numbered players.",
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

            if (ConfirmCompletion?.Invoke() == false) return;
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
                ReplaceWideBalls=ReplaceWideBallsToggle.IsChecked==true,
                ReplaceNoBalls=ReplaceNoBallsToggle.IsChecked==true,
                FreeHitsEnabled=FreeHitsToggle.IsChecked==true,
                WeatherLocation = SetupWeatherLocation.Text.Trim(),

                TossWinner = TossWinnerChoice.SelectedIndex == 1 ? homeName : TossWinnerChoice.SelectedIndex == 2 ? awayName : "",
                TossWinnerElectedToBat = TossDecisionChoice.SelectedIndex == 0,
                TeamABatsFirst = TossWinnerChoice.SelectedIndex == 0 || (TossWinnerChoice.SelectedIndex == 1) == (TossDecisionChoice.SelectedIndex == 0),

                Messages = messages
            };

            Result.FillEmptySquads();
            SetupCompleted?.Invoke(Result);
        }  

        // ═══════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════
        //  PLAYER SELECTION — STEP 2
        // ═══════════════════════════════════════════════════════

        private void UpdatePlayerPanelTeamIdentity()
        {
            RefreshRosterHeaders();
            if (HomePlyTeamName is null) return;
            string homeName = SetupHomeNameBox.Text.Trim();
            string awayName = SetupAwayNameBox.Text.Trim();
            HomePlyTeamName.Text = string.IsNullOrWhiteSpace(homeName) ? "HOME" : homeName.ToUpperInvariant();
            AwayPlyTeamName.Text = string.IsNullOrWhiteSpace(awayName) ? "AWAY" : awayName.ToUpperInvariant();
            try { var c = (Color)ColorConverter.ConvertFromString(SetupHomeColorBox.Text.Trim()); HomePlayerSideBrush.Color = c; HomePlyGlow.Color = c; } catch { }
            try { AwayPlayerSideBrush.Color = (Color)ColorConverter.ConvertFromString(SetupAwayColorBox.Text.Trim()); } catch { }
        }

        private void AddHomePlayer_Click(object sender, RoutedEventArgs e)
        {
            string first = HomePlayerFirstBox.Text.Trim();
            string last = HomePlayerLastBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(last)) return;
            _homePlayers.Add(new CricketPlayer { FirstName = first, LastName = last });
            HomePlayerFirstBox.Text = "";
            HomePlayerLastBox.Text = "";
            HomePlayerFirstBox.Focus();
        }

        private void AddAwayPlayer_Click(object sender, RoutedEventArgs e)
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

        private void RemoveHomePlayer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: CricketPlayer player }) _homePlayers.Remove(player);
        }

        private void RemoveAwayPlayer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: CricketPlayer player }) _awayPlayers.Remove(player);
        }

        private void ToggleHomeWicketkeeper_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: CricketPlayer tapped }) return;
            tapped.IsWicketkeeper = !tapped.IsWicketkeeper;

        }

        private void ToggleAwayWicketkeeper_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: CricketPlayer tapped }) return;
            tapped.IsWicketkeeper = !tapped.IsWicketkeeper;

        }

        // ═══════════════════════════════════════════════════════
        //  MATCH FORMAT — STEP 3
        // ═══════════════════════════════════════════════════════

        private void FormatChoice_Click(object sender, RoutedEventArgs args)
        {
            if (LimitedFormatChoice is null || MultiFormatChoice is null) return;
            _selectedFormat = ReferenceEquals(sender, MultiFormatChoice) ? CricketFormat.MultiInnings : CricketFormat.LimitedOvers;
            UpdateFormatCardVisuals();
        }

        private void UpdateFormatCardVisuals()
        {
            if (LimitedOversCard is null || LimitedFormatChoice is null || MultiFormatChoice is null) return;
            bool isLimited = _selectedFormat == CricketFormat.LimitedOvers;
            LimitedFormatChoice.IsChecked = isLimited;
            MultiFormatChoice.IsChecked = !isLimited;
            if (OversInputPanel is not null) OversInputPanel.Visibility = isLimited ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateOversDisplay()
        {
            if (OversDisplay is not null) OversDisplay.Text = _selectedOvers.ToString();
            foreach(var (button,value) in new[]{(OversPreset20Button,20),(OversPreset40Button,40),(OversPreset45Button,45),(OversPreset50Button,50),(OversPreset100Button,100)})
            {
                if(button is null)continue;
                button.SetResourceReference(StyleProperty,value==_selectedOvers?"ControlCentre.PrimaryButton":"ControlCentre.Button");
                System.Windows.Automation.AutomationProperties.SetItemStatus(button,value==_selectedOvers?"Selected over limit":"Not selected");
            }
        }

        internal static bool TryParseOvers(string text, out int overs) =>
            int.TryParse(text, out overs) && overs is >= 1 and <= 100;

        private bool ValidateOversEntry()
        {
            if (_selectedFormat != CricketFormat.LimitedOvers) return true;
            if (TryParseOvers(OversDisplay.Text, out int overs))
            {
                _selectedOvers = overs;
                OversValidationMessage.Visibility = Visibility.Collapsed;
                return true;
            }
            OversDisplay.ToolTip = "Enter a whole number from 1 to 100.";
            OversValidationMessage.Visibility = Visibility.Visible;
            // Direct step navigation may leave this field in a collapsed page.
            // Make the correction visible before focus, then reveal the field
            // and its explanation together through the existing viewport path.
            if (_currentStep != 3) ShowStep(3, animate: false);
            UpdateLayout();
            OversDisplay.Focus();
            OperatorViewport.RevealValidationError(OversValidationTarget);
            return false;
        }

        private void OversDisplay_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox field) return;
            bool valid = TryParseOvers(field.Text, out int overs);
            field.BorderThickness = new Thickness(1);
            field.BorderBrush = valid ? System.Windows.Media.Brushes.Transparent : System.Windows.Media.Brushes.OrangeRed;
            if (valid)
            {
                _selectedOvers = overs;
                if (OversValidationMessage is not null) OversValidationMessage.Visibility = Visibility.Collapsed;
            }
        }

        private void OversDisplay_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (TryParseOvers(OversDisplay.Text, out int overs))
            {
                _selectedOvers = overs;
                UpdateOversDisplay();
            }
        }

        private void OversDisplay_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { UpdateOversDisplay(); e.Handled = true; }
            else if (e.Key == Key.Enter)
            {
                if (ValidateOversEntry()) Keyboard.ClearFocus();
                e.Handled = true;
            }
        }

        private void SelectOvers(int overs)
        {
            _selectedOvers = Math.Clamp(overs, 1, 100);
            _selectedFormat = CricketFormat.LimitedOvers;
            UpdateFormatCardVisuals();
            UpdateOversDisplay();
        }
        private void OversMinus_Click(object sender, RoutedEventArgs e) => SelectOvers(_selectedOvers - 1);
        private void OversPlus_Click(object sender, RoutedEventArgs e) => SelectOvers(_selectedOvers + 1);
        private void OversPreset20_Click(object sender, RoutedEventArgs e) => SelectOvers(20);
        private void OversPreset40_Click(object sender, RoutedEventArgs e) => SelectOvers(40);
        private void OversPreset45_Click(object sender, RoutedEventArgs e) => SelectOvers(45);
        private void OversPreset50_Click(object sender, RoutedEventArgs e) => SelectOvers(50);
        private void OversPreset100_Click(object sender, RoutedEventArgs e) => SelectOvers(100);

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

        private void BrowseHomeTeam_Click(object sender, RoutedEventArgs e) => BrowseTeamLibrary(true);
        private void BrowseAwayTeam_Click(object sender, RoutedEventArgs e) => BrowseTeamLibrary(false);
        private async void BrowseTeamLibrary(bool isHome)
        {
            if (_libraryOpen || !IsLoaded || !IsVisible) return;
            int generation = _presetOwnerGeneration, session = ++_librarySessionVersion;
            _libraryOpen = true;
            TeamLibraryWindow browser = new(isHome ? "Home" : "Away", TeamLibrarySport.Cricket) { Owner = Window.GetWindow(this) };
            bool? accepted;
            try { accepted = browser.ShowDialog(); }
            finally { _libraryOpen = false; }
            Task<PresetCommitResult[]> changes = browser.AcceptedPresetChanges;
            // Use applies its frozen choice once, before waiting for unrelated
            // favourite persistence. Close/Escape apply no team.
            if (generation == _presetOwnerGeneration && session == _librarySessionVersion && IsLoaded && IsVisible
                && accepted == true && browser.Selection is { } selection)
            {
                TeamPreset preset = new()
                {
                    PresetName = selection.Team.TeamName, TeamName = selection.Team.TeamName,
                    Abbreviation = selection.Team.Abbreviation, PrimaryColor = selection.Team.PrimaryColor,
                    SecondaryColor = selection.Team.SecondaryColor, LogoPath = selection.TransparentLogoPath,
                    LibraryTeamId = selection.Team.Id
                };
                if (isHome) ApplyPresetToHome(preset); else ApplyPresetToAway(preset);
            }
            PresetCommitResult[] completed = await changes;
            if (generation != _presetOwnerGeneration || session != _librarySessionVersion || !IsLoaded || !IsVisible) return;
            int read = ++_presetReadVersion;
            // Every modal return reads through accepted transactions, including
            // an earlier closed session superseded by this newer session.
            PresetReadResult latest = await PresetStorage.ReadAsync(PresetSport);
            if (generation != _presetOwnerGeneration || session != _librarySessionVersion
                || read != _presetReadVersion || !IsLoaded || !IsVisible) return;
            _presetLoading = false;
            if (latest.Succeeded)
            {
                PublishPresetSnapshot(latest);
                if (_presetLoadingMessage) WizardTeamEditor.SetPresetStatus(this, "Home", "");
            }
            _presetLoadingMessage = false;
            WizardTeamEditor.SetPresetActionsPending(this, _presetOperationPending);
            string? error = completed.LastOrDefault(result => !result.Succeeded)?.Error;
            if (!latest.Succeeded) error = latest.Error;
            if (!string.IsNullOrWhiteSpace(error)) WizardTeamEditor.SetPresetStatus(this, isHome ? "Home" : "Away", error);
            WizardTeamEditor.SetPresetActionsPending(this, _presetOperationPending);
        }

        private void InvalidatePresetOwner()
        {
            ++_presetOwnerGeneration; ++_presetReadVersion; ++_librarySessionVersion;
            _presetOperationPending = _presetLoading = _presetLoadingMessage = _presetRefreshAfterDrag = false;
            WizardTeamEditor.SetPresetStatus(this, "Home", "");
            WizardTeamEditor.SetPresetStatus(this, "Away", "");
            WizardTeamEditor.SetPresetActionsPending(this, false);
        }

        private void PublishPresetSnapshot(PresetReadResult result)
        {
            if (!result.Succeeded || result.Revision < _presetRevision) return;
            if (_dragTile is not null) { _presetRefreshAfterDrag = true; return; }
            Border? focused = HomePresetPanel.Children.OfType<Border>().Concat(AwayPresetPanel.Children.OfType<Border>())
                .FirstOrDefault(tile => tile.IsKeyboardFocusWithin);
            bool focusedHome = focused is not null && HomePresetPanel.Children.Contains(focused);
            TeamPreset? focusedPreset = focused?.Tag as TeamPreset;
            _presetRevision = result.Revision;
            bool homeWasSaved = _selectedHomePreset is not null && _presets.Contains(_selectedHomePreset);
            bool awayWasSaved = _selectedAwayPreset is not null && _presets.Contains(_selectedAwayPreset);
            string? homeKey = _selectedHomePreset?.PresetName, awayKey = _selectedAwayPreset?.PresetName;
            // Keep unchanged tile instances and their focus/motion state. Changed
            // records are detached copies; no committed object is edited in place.
            List<TeamPreset> remaining = _presets.ToList();
            _presets = result.Presets.Select(preset =>
            {
                TeamPreset? retained = remaining.FirstOrDefault(old =>
                    string.Equals(old.PresetName, preset.PresetName, StringComparison.OrdinalIgnoreCase)
                    && PresetStorage.SameContents(new[] { old }, new[] { preset }));
                if (retained is not null) remaining.Remove(retained);
                return retained ?? PresetStorage.Clone(preset);
            }).ToList();
            if (homeWasSaved && !_presets.Contains(_selectedHomePreset!))
                _selectedHomePreset = _presets.Count(p => string.Equals(p.PresetName, homeKey, StringComparison.OrdinalIgnoreCase)) == 1
                    ? _presets.First(p => string.Equals(p.PresetName, homeKey, StringComparison.OrdinalIgnoreCase)) : null;
            if (awayWasSaved && !_presets.Contains(_selectedAwayPreset!))
                _selectedAwayPreset = _presets.Count(p => string.Equals(p.PresetName, awayKey, StringComparison.OrdinalIgnoreCase)) == 1
                    ? _presets.First(p => string.Equals(p.PresetName, awayKey, StringComparison.OrdinalIgnoreCase)) : null;
            RefreshPresetPanels();
            if (focusedPreset is not null && _presets.Contains(focusedPreset))
                (focusedHome ? HomePresetPanel : AwayPresetPanel).Children.OfType<Border>()
                    .FirstOrDefault(tile => ReferenceEquals(tile.Tag, focusedPreset))?.Focus();
        }

        private async void RefreshSavedPresets()
        {
            if (!IsLoaded || !IsVisible) return;
            int generation = _presetOwnerGeneration, read = ++_presetReadVersion;
            _presetLoading = true;
            WizardTeamEditor.SetPresetActionsPending(this, true);
            PresetReadResult result = await PresetStorage.ReadAsync(PresetSport);
            if (generation != _presetOwnerGeneration || read != _presetReadVersion || !IsLoaded || !IsVisible) return;
            _presetLoading = false;
            if (result.Succeeded)
            {
                PublishPresetSnapshot(result);
                if (_presetLoadingMessage) WizardTeamEditor.SetPresetStatus(this, "Home", "");
            }
            else WizardTeamEditor.SetPresetStatus(this, "Home", result.Error);
            _presetLoadingMessage = false;
            WizardTeamEditor.SetPresetActionsPending(this, _presetOperationPending);
        }

        private async Task PersistPresetChange(Task<PresetCommitResult> accepted, bool isHome, string pending,
            string success, string? selectName = null, TeamPreset? selectedAtStart = null, TeamPreset? deletedPreset = null)
        {
            int generation = _presetOwnerGeneration;
            string side = isHome ? "Home" : "Away";
            _presetOperationPending = true;
            WizardTeamEditor.SetPresetStatus(this, side, pending, true);
            WizardTeamEditor.SetPresetActionsPending(this, true);
            PresetCommitResult result = await accepted;
            if (generation != _presetOwnerGeneration || !IsLoaded || !IsVisible) return;
            _presetOperationPending = false;
            WizardTeamEditor.SetPresetActionsPending(this, _presetLoading);
            if (!result.Succeeded)
            {
                // A conflict contains a fresh confirmed basis. Reconcile only
                // saved tiles so the retry targets current records/order;
                // editable team fields and the failure explanation stay intact.
                if (result.Status == PresetCommitStatus.Conflict) PublishPresetSnapshot(result.Snapshot);
                WizardTeamEditor.SetPresetStatus(this, side, result.Error);
                WizardTeamEditor.SetPresetActionsPending(this, _presetLoading);
                return;
            }
            bool selectSubmitted = _dragTile is null && selectName is not null
                && ReferenceEquals(isHome ? _selectedHomePreset : _selectedAwayPreset, selectedAtStart)
                && string.Equals(((isHome ? SetupHomeNameBox : SetupAwayNameBox).Text ?? string.Empty).Trim(), selectName, StringComparison.OrdinalIgnoreCase);
            if (deletedPreset is not null && result.Snapshot.Revision >= _presetRevision)
            {
                if (ReferenceEquals(_selectedHomePreset, deletedPreset)) _selectedHomePreset = null;
                if (ReferenceEquals(_selectedAwayPreset, deletedPreset)) _selectedAwayPreset = null;
                // Remove the confirmed deleted identity from the reuse pool so
                // an identical legacy duplicate retains its own tile identity.
                _presets.Remove(deletedPreset);
            }
            PublishPresetSnapshot(result.Snapshot);
            if (selectSubmitted)
            {
                TeamPreset? selected = _presets.FirstOrDefault(p => string.Equals(p.PresetName, selectName, StringComparison.OrdinalIgnoreCase));
                if (isHome) _selectedHomePreset = selected; else _selectedAwayPreset = selected;
                RefreshPresetPanels();
            }
            WizardTeamEditor.SetPresetStatus(this, side, success);
            WizardTeamEditor.SetPresetActionsPending(this, _presetLoading);
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
                Color fgColor = ContrastHelper.GetContrastForeground(bgColor);

                bool isSelected = isHome
                    ? string.Equals(_selectedHomePreset?.PresetName, preset.PresetName, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(_selectedAwayPreset?.PresetName, preset.PresetName, StringComparison.OrdinalIgnoreCase);

                string abbr = NormalizeAbbreviation(preset.Abbreviation);

                TextBlock abbrLabel = new()
                {
                    Text = abbr,
                    Foreground = new SolidColorBrush(fgColor),
                    FontSize = 20,
                    FontWeight = FontWeights.Black,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    IsHitTestVisible = false
                };

                TextBlock nameLabel = new()
                {
                    Text = string.IsNullOrWhiteSpace(preset.TeamName) ? "Unnamed team" : preset.TeamName,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xCC, fgColor.R, fgColor.G, fgColor.B)),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 110,
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
                    var check = new System.Windows.Shapes.Path { Tag = "preset-selection-marker", Data = Geometry.Parse("M 2,7 L 6,11 L 13,3"), Stroke = System.Windows.Media.Brushes.White,
                        StrokeThickness = 2.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                        Width = 15, Height = 14, Stretch = Stretch.Uniform, StrokeDashArray = new DoubleCollection { 7, 7 } };
                    var confirmation = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromRgb(29,123,84)),
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Child = check, IsHitTestVisible = false,
                        RenderTransformOrigin = new System.Windows.Point(.5,.5), RenderTransform = new ScaleTransform(1,1) };
                    tileContent.Children.Add(confirmation);
                    if (OperatorMotion.Enabled)
                    {
                        check.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, new DoubleAnimation(7,0,OperatorMotion.CheckDraw) {FillBehavior=FillBehavior.Stop,EasingFunction=OperatorMotion.SmoothOut});
                        var scale = (ScaleTransform)confirmation.RenderTransform;
                        scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(.7,1,OperatorMotion.SurfaceOpen) {FillBehavior=FillBehavior.Stop,EasingFunction=Overshoot});
                        scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(.7,1,OperatorMotion.SurfaceOpen) {FillBehavior=FillBehavior.Stop,EasingFunction=Overshoot});
                    }
                }

                var tileScaleTransform = new ScaleTransform(1, 1);
                var tileTranslateTransform = new TranslateTransform(0, 0);
                var tileTransformGroup = new TransformGroup();
                tileTransformGroup.Children.Add(tileScaleTransform);
                tileTransformGroup.Children.Add(tileTranslateTransform);

                Border tile = new()
                {
                    Width = 124,
                    Height = 80,
                    Padding = new Thickness(4),
                    Margin = new Thickness(0, 0, 8, 8),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Focusable = true,
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
                System.Windows.Automation.AutomationProperties.SetName(tile,preset.TeamName+" · "+(isHome?"Home":"Away")+" preset");
                System.Windows.Automation.AutomationProperties.SetItemStatus(tile,isSelected?"Selected":"Not selected");
                tile.KeyDown += (_, key) => { if(key.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space) {key.Handled=true;if(isHome)ApplyPresetToHome(preset);else ApplyPresetToAway(preset);panel.Children.OfType<Border>().FirstOrDefault(item=>ReferenceEquals(item.Tag,preset))?.Focus();} };

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

                tile.MouseEnter+=(_,_)=>OperatorClubPresetDesign.Hover(tile,true);tile.MouseLeave+=(_,_)=>OperatorClubPresetDesign.Hover(tile,false);
                panel.Children.Add(tile);
            }
        }

        private async void CompleteDrag(System.Windows.Controls.WrapPanel panel, bool isHome, TeamPreset clickedPreset)
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
                string[] order = _presets.Select(p => p.PresetName).ToArray();
                ResetAllTileTransforms(panel);
                RefreshPresetPanels();
                if (origIndex != currentIndex && !_presetOperationPending && !_presetLoading)
                    await PersistPresetChange(PresetStorage.ReorderAsync(PresetSport, order, origIndex, currentIndex), isHome,
                        "Saving club order…", "Saved club order updated.");
            }
            else
            {
                if (isHome)
                    ApplyPresetToHome(clickedPreset);
                else
                    ApplyPresetToAway(clickedPreset);
            }
            if (_presetRefreshAfterDrag)
            { _presetRefreshAfterDrag = false; RefreshSavedPresets(); }

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

        private async void DeletePreset(TeamPreset? preset, bool isHome)
        {
            if (preset is null || _presetOperationPending || _presetLoading) return;
            int selectedIndex = _presets.IndexOf(preset);
            if (selectedIndex < 0)
            { WizardTeamEditor.SetPresetStatus(this, isHome ? "Home" : "Away", "Select a saved club before deleting it."); return; }
            await PersistPresetChange(PresetStorage.DeletePresetAsync(PresetSport, _presets, selectedIndex, _presetRevision), isHome,
                $"Removing {preset.PresetName}…", $"{preset.PresetName} removed from saved clubs.", deletedPreset: preset);
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
            => DeletePreset(_selectedHomePreset, true);

        private void SaveAwayPreset_Click(object sender, RoutedEventArgs e)
            => SavePreset(SetupAwayNameBox, SetupAwayAbbrBox, SetupAwayColorBox, SetupAwaySecondaryBox, _awayLogoPath, false);

        private void DeleteAwayPreset_Click(object sender, RoutedEventArgs e)
            => DeletePreset(_selectedAwayPreset, false);

        private async void SavePreset(TextBox nameBox, TextBox abbrBox, TextBox colorBox, TextBox secBox, string? logoPath, bool isHome)
        {
            if (_presetOperationPending || _presetLoading) return;
            string name = nameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            { WizardTeamEditor.SetPresetStatus(this, isHome ? "Home" : "Away", "Enter a team name before saving this club."); return; }
            TeamPreset? selectedAtStart = isHome ? _selectedHomePreset : _selectedAwayPreset;
            var draft = new TeamPreset
            {
                PresetName = name, TeamName = name, Abbreviation = NormalizeAbbreviation(abbrBox.Text),
                PrimaryColor = colorBox.Text.Trim(), SecondaryColor = secBox.Text.Trim(), LogoPath = logoPath,
            };
            await PersistPresetChange(PresetStorage.SavePresetAsync(PresetSport, draft, includeGoalVideo: false), isHome,
                $"Saving {name}…", $"{name} saved as a preset.", name, selectedAtStart);
        }

        private static Color BlendColor(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromRgb(
                (byte)(a.R + ((b.R - a.R) * t)),
                (byte)(a.G + ((b.G - a.G) * t)),
                (byte)(a.B + ((b.B - a.B) * t)));
        }

        private static DoubleAnimation Anim(double to, TimeSpan duration, IEasingFunction? easing = null)
        {
            return new DoubleAnimation(to, duration) { EasingFunction = easing };
        }

        private static void Animate(IAnimatable target, DependencyProperty prop,
            double to, int durationMs, int delay = 0, IEasingFunction? easing = null)
        {
            if (!OperatorMotion.Enabled) { target.BeginAnimation(prop,null); if(target is DependencyObject element) element.SetValue(prop,to); return; }
            var anim = new DoubleAnimation(to, OperatorMotion.Page)
            {
                BeginTime = delay > 0 ? TimeSpan.FromMilliseconds(Math.Min(200, delay / 4)) : TimeSpan.Zero,
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



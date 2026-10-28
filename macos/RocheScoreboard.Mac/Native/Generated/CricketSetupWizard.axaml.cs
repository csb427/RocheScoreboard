using RocheScoreboard.Mac.Production;
// Generated from Views/CricketSetupWizard.xaml.cs; edit translator/adapters.
// Source SHA-256: df0d4709aadd4cf597b8bdf30bf352f073be6c2eff4fae0618a6f07b9e92350c
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
            NativePointer.EnsureInitialized(); ProductionNames.BeginLoad(this); try { InitializeComponent(); } finally { ProductionNames.CompleteLoad(this); }
            WizardFlowDesign.Enable(this);
            InitializePlayerEntry();
            UpdateFormatCardVisuals();
            UpdateOversDisplay();
            SetupMessageList.ItemsSource = _setupMessages;
            HomePlayerList.ItemsSource = _homePlayers;
            AwayPlayerList.ItemsSource = _awayPlayers;
            _presets = PresetStorage.LoadAllCricket();
            RefreshPresetPanels();

            SetupWeatherLocation.AddHandler(
                TextBox.TextChangedEvent,
                new EventHandler<TextChangedEventArgs>(SetupWeatherLocation_TextChanged));
            SetupWeatherLocation.GotFocus += (_, _) => _weatherBoxActivated = true;
            SetupWeatherLocation.DropDownOpened += (_, _) => _weatherBoxActivated = true;

            Loaded += OnLoaded;
            Unloaded += (_, _) =>
            {
                StopAmbientAnimations(); ++_stepTransitionVersion;
                if (_stepTransitionStoryboard is not null) { _stepTransitionStoryboard.Remove(this); _stepTransitionStoryboard = null; }
            };
            NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsVisibleProperty,true,(_, args) => { if (args.NewValue is false) StopAmbientAnimations(); else if (IsLoaded) StartAmbientAnimations(); });
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
            SettleFlowSurfaces();
            if (OperatorMotion.Enabled && IsLoaded) SharedSurfaceMotion.Reveal(TeamsStepContainer);
        }

        private void SettleFlowSurfaces()
        {
            foreach (var element in new Control[] { HeaderPanel, StepIndicator, HomePresetCard, AwayPresetCard, HomeTeamCard, AwayTeamCard, HomeHeaderGroup, AwayHeaderGroup, PlayersHeaderGroup, FormatHeaderGroup, MsgHeaderGroup, NavBar })
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

        private void NavBtn_MouseEnter(object? sender, MouseEventArgs e)
        {
            if (sender is not Control btn || !OperatorMotion.Enabled) return;
            ScaleTransform sc = btn == BackButton ? BackBtnScale : NextBtnScale;
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(.99, OperatorMotion.HoverIn, Smooth));
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(.99, OperatorMotion.HoverIn, Smooth));
        }

        private void NavBtn_MouseLeave(object? sender, MouseEventArgs e)
        {
            if (sender is not Control btn || !OperatorMotion.Enabled) return;
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
            Control[] panels = [TeamsStepContainer, Step3Panel, Step3bPanel, Step4Panel];
            Control active = panels[_currentStep - 1];
            foreach (var panel in panels)
            {
                panel.BeginAnimation(OpacityProperty, null); panel.Opacity = 1;
                if (panel.RenderTransform is TranslateTransform old) old.BeginAnimation(TranslateTransform.XProperty, null);
                panel.RenderTransform = new MatrixTransform(Matrix.Identity);
                panel.IsVisible = ReferenceEquals(panel, active) ? true : false;
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
                var slide = new DoubleAnimation(_currentStep > previous ? 8 : -8, 0, OperatorMotion.Page) { FillBehavior = FillBehavior.Stop, EasingFunction = OperatorMotion.SmoothOut };
                Storyboard.SetTarget(slide, shift); Storyboard.SetTargetProperty(slide, new PropertyPath(TranslateTransform.XProperty)); storyboard.Children.Add(slide);
                storyboard.Completed += (_, _) => { if (version != _stepTransitionVersion) return; storyboard.Remove(this); active.Opacity = 1; active.RenderTransform = new MatrixTransform(Matrix.Identity); _stepTransitionStoryboard = null; };
                _stepTransitionStoryboard = storyboard; storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
            }
            BackButton.IsVisible = true;
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
            var dotText = NativeObjects.Require<TextBlock>(dot.Child);
            dotText.Foreground = reached ? s_whiteBrush : s_textMutedBrush;
            // The shared segmented lens owns label contrast and selection ink.

            // Glow effect on active dot
            if (dot.Effect is DropShadowEffect glow)
                glow.Opacity = 0;
        }

        private void Next_Click(object? sender, RoutedEventArgs e)
        {
            if (_currentStep == 3 && !ValidateOversEntry()) return;
            if (_currentStep < TotalSteps)
                ShowStep(_currentStep + 1);
            else
                FinishWizard();
        }

        private void Back_Click(object? sender, RoutedEventArgs e)
        {
            if (_currentStep > 1)
                ShowStep(_currentStep - 1);
            else
                BackToSportSelection?.Invoke();
        }

        private void GoToStep1_Click(object? sender, RoutedEventArgs e) => ShowStep(1);
        private void GoToStep2_Click(object? sender, RoutedEventArgs e) => ShowStep(2);
        private void GoToStep3_Click(object? sender, RoutedEventArgs e) => ShowStep(3);
        private void GoToStep4_Click(object? sender, RoutedEventArgs e) => ShowStep(4);

        // ═══════════════════════════════════════════════════════
        //  RESET
        // ═══════════════════════════════════════════════════════

        public void Reset()
        {
            Result = null;
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

            _presets = PresetStorage.LoadAllCricket();
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
            if (!ValidateOversEntry()) { ShowStep(3); return; }
            if (_homePlayers.Count == 1 || _awayPlayers.Count == 1)
            {
                RocheScoreboard.Mac.Native.NativeMessageBox.Show(
                    "A named squad needs at least two players. Add another player, or leave that squad empty to use 11 numbered players.",
                    "Complete Both Squads",
                    RocheScoreboard.Mac.Native.NativeMessageBoxButton.OK,
                    RocheScoreboard.Mac.Native.NativeMessageBoxImage.Information);
                ShowStep(2);
                return;
            }

            var messages = new List<string>();
            foreach (var item in _setupMessages)
            {
                if (!string.IsNullOrWhiteSpace(item.Text))
                    messages.Add(item.Text);
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
                ReplaceWideBalls=ReplaceWideBallsToggle.IsChecked==true,
                ReplaceNoBalls=ReplaceNoBallsToggle.IsChecked==true,
                FreeHitsEnabled=FreeHitsToggle.IsChecked==true,
                WeatherLocation = (SetupWeatherLocation.Text ?? string.Empty).Trim(),

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

        private void RemoveHomePlayer_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control { DataContext: CricketPlayer player }) _homePlayers.Remove(player);
        }

        private void RemoveAwayPlayer_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control { DataContext: CricketPlayer player }) _awayPlayers.Remove(player);
        }

        private void ToggleHomeWicketkeeper_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Control { Tag: CricketPlayer tapped }) return;
            tapped.IsWicketkeeper = !tapped.IsWicketkeeper;

        }

        private void ToggleAwayWicketkeeper_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Control { Tag: CricketPlayer tapped }) return;
            tapped.IsWicketkeeper = !tapped.IsWicketkeeper;

        }

        // ═══════════════════════════════════════════════════════
        //  MATCH FORMAT — STEP 3
        // ═══════════════════════════════════════════════════════

        private void FormatChoice_Click(object? sender, RoutedEventArgs args)
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
            if (OversInputPanel is not null) OversInputPanel.IsVisible = isLimited ? true : false;
        }

        private void UpdateOversDisplay()
        {
            if (OversDisplay is not null) OversDisplay.Text = _selectedOvers.ToString();
            foreach(var (button,value) in new[]{(OversPreset20Button,20),(OversPreset40Button,40),(OversPreset45Button,45),(OversPreset50Button,50),(OversPreset100Button,100)})
            {
                if(button is null)continue;
                button.SetResourceReference(ThemeProperty,value==_selectedOvers?"ControlCentre.PrimaryButton":"ControlCentre.Button");
                Avalonia.Automation.AutomationProperties.SetItemStatus(button,value==_selectedOvers?"Selected over limit":"Not selected");
            }
        }

        internal static bool TryParseOvers(string text, out int overs) =>
            int.TryParse(text, out overs) && overs is >= 1 and <= 100;

        private bool ValidateOversEntry()
        {
            if (_selectedFormat != CricketFormat.LimitedOvers) return true;
            if (TryParseOvers((OversDisplay.Text ?? string.Empty), out int overs))
            {
                _selectedOvers = overs;
                return true;
            }
            Avalonia.Controls.ToolTip.SetTip(OversDisplay, "Enter a whole number from 1 to 100.");
            OversDisplay.Focus();
            return false;
        }

        private void OversDisplay_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox field) return;
            bool valid = TryParseOvers((field.Text ?? string.Empty), out int overs);
            field.BorderThickness = new Thickness(1);
            field.BorderBrush = valid ? Avalonia.Media.Brushes.Transparent : Avalonia.Media.Brushes.OrangeRed;
            if (valid) _selectedOvers = overs;
        }

        private void OversDisplay_LostFocus(object? sender, RoutedEventArgs e)
        {
            if (TryParseOvers((OversDisplay.Text ?? string.Empty), out int overs))
            {
                _selectedOvers = overs;
                UpdateOversDisplay();
            }
        }

        private void OversDisplay_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { UpdateOversDisplay(); e.Handled = true; }
            else if (e.Key == Key.Enter)
            {
                if (ValidateOversEntry()) NativeKeyboard.ClearFocus();
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
        private void OversMinus_Click(object? sender, RoutedEventArgs e) => SelectOvers(_selectedOvers - 1);
        private void OversPlus_Click(object? sender, RoutedEventArgs e) => SelectOvers(_selectedOvers + 1);
        private void OversPreset20_Click(object? sender, RoutedEventArgs e) => SelectOvers(20);
        private void OversPreset40_Click(object? sender, RoutedEventArgs e) => SelectOvers(40);
        private void OversPreset45_Click(object? sender, RoutedEventArgs e) => SelectOvers(45);
        private void OversPreset50_Click(object? sender, RoutedEventArgs e) => SelectOvers(50);
        private void OversPreset100_Click(object? sender, RoutedEventArgs e) => SelectOvers(100);

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

            var editBox = NativeTemplates.FindObject(combo,"PART_EditableTextBox") as Avalonia.Controls.TextBox;
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

        private void BrowseHomeTeam_Click(object? sender, RoutedEventArgs e) => BrowseTeamLibrary(true);
        private void BrowseAwayTeam_Click(object? sender, RoutedEventArgs e) => BrowseTeamLibrary(false);
        private void BrowseTeamLibrary(bool isHome)
        {
            TeamLibraryWindow browser = new(isHome ? "Home" : "Away", TeamLibrarySport.Cricket) { Owner = NativeWindow.GetWindow(this) };
            bool? accepted = browser.ShowDialog();
            if (browser.PresetsChanged) { _presets = PresetStorage.LoadAllCricket(); RefreshPresetPanels(); }
            if (accepted != true || browser.Selection is not { } selection) return;
            TeamPreset preset = new() { PresetName=selection.Team.TeamName, TeamName=selection.Team.TeamName,
                Abbreviation=selection.Team.Abbreviation, PrimaryColor=selection.Team.PrimaryColor,
                SecondaryColor=selection.Team.SecondaryColor, LogoPath=selection.TransparentLogoPath, LibraryTeamId=selection.Team.Id };
            if (isHome) ApplyPresetToHome(preset); else ApplyPresetToAway(preset);
        }

        private void RefreshPresetPanels()
        {
            BuildPresetTiles(HomePresetPanel, HomePresetEmpty, true);
            BuildPresetTiles(AwayPresetPanel, AwayPresetEmpty, false);
        }

        private void BuildPresetTiles(Avalonia.Controls.WrapPanel panel, TextBlock emptyLabel, bool isHome)
        {
            if(panel.Children.Count==_presets.Count && panel.Children.OfType<Border>().Select(tile=>tile.Tag).SequenceEqual(_presets.Cast<object>()))
            {
                foreach(var existing in panel.Children.OfType<Border>())if(existing.Tag is TeamPreset preset)
                    OperatorClubPresetDesign.Update(existing,preset,string.Equals((isHome?_selectedHomePreset:_selectedAwayPreset)?.PresetName,preset.PresetName,StringComparison.OrdinalIgnoreCase));
                emptyLabel.IsVisible=_presets.Count==0?true:false;return;
            }
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
                    FontWeight = FontWeight.Black,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    IsHitTestVisible = false
                };

                TextBlock nameLabel = new()
                {
                    Text = string.IsNullOrWhiteSpace(preset.TeamName) ? "Unnamed team" : preset.TeamName,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xCC, fgColor.R, fgColor.G, fgColor.B)),
                    FontSize = 11,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 110,
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
                    var check = new Avalonia.Controls.Shapes.Path { Tag = "preset-selection-marker", Data = Geometry.Parse("M 2,7 L 6,11 L 13,3"), Stroke = Avalonia.Media.Brushes.White,
                        StrokeThickness = 2.5, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
                        Width = 15, Height = 14, Stretch = Stretch.Uniform, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 7, 7 } };
                    var confirmation = new NativeBorder { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromRgb(29,123,84)),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Child = check, IsHitTestVisible = false,
                        RenderTransformOrigin = new RelativePoint(.5,.5,RelativeUnit.Relative), RenderTransform = new ScaleTransform(1,1) };
                    tileContent.Children.Add(confirmation);
                    if (OperatorMotion.Enabled)
                    {
                        check.BeginAnimation(Avalonia.Controls.Shapes.Shape.StrokeDashOffsetProperty, new DoubleAnimation(7,0,OperatorMotion.CheckDraw) {FillBehavior=FillBehavior.Stop,EasingFunction=OperatorMotion.SmoothOut});
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

                Border tile = new NativeBorder()
                {
                    Width = 124,
                    Height = 80,
                    Padding = new Thickness(4),
                    Margin = new Thickness(0, 0, 8, 8),
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                    Focusable = true,
                    ToolTip = $"{preset.TeamName}\n{abbr}\n{(HasValidFile(preset.LogoPath) ? "Logo set" : "No logo")}",
                    Child = tileContent,
                    Tag = preset,
                    BorderBrush = isSelected
                        ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E))
                        : new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(2),
                    RenderTransformOrigin = new RelativePoint(0.5, 0.5,RelativeUnit.Relative),
                    RenderTransform = tileTransformGroup,
                    Background = NativeBrush.Linear(
                        BlendColor(bgColor, Colors.Black, 0.28),
                        BlendColor(bgColor, Colors.White, 0.08),
                        new Avalonia.Point(0, 0),
                        new Avalonia.Point(1, 1))
                };
                Avalonia.Automation.AutomationProperties.SetName(tile,preset.TeamName+" · "+(isHome?"Home":"Away")+" preset");
                Avalonia.Automation.AutomationProperties.SetItemStatus(tile,isSelected?"Selected":"Not selected");
                tile.KeyDown += (_, key) => { if(key.Key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Space) {key.Handled=true;if(isHome)ApplyPresetToHome(preset);else ApplyPresetToAway(preset);panel.Children.OfType<Border>().FirstOrDefault(item=>ReferenceEquals(item.Tag,preset))?.Focus();} };

                OperatorClubPresetDesign.Update(tile,preset,isSelected);
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

                tile.PointerEntered+=(_,_)=>OperatorClubPresetDesign.Hover(tile,true);tile.PointerExited+=(_,_)=>OperatorClubPresetDesign.Hover(tile,false);
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
                    PresetStorage.SaveAllCricket(_presets);
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

            PresetStorage.SaveAllCricket(_presets);
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

            PresetStorage.SaveAllCricket(_presets);
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
            if (!OperatorMotion.Enabled) { target.BeginAnimation(prop,null); if(target is AvaloniaObject element) element.SetValue(prop,to); return; }
            var anim = new DoubleAnimation(to, OperatorMotion.Page)
            {
                BeginTime = delay > 0 ? TimeSpan.FromMilliseconds(Math.Min(200, delay / 4)) : TimeSpan.Zero,
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
    private TranslateTransform HomePresetTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["HomePresetTranslate"];
    private ScaleTransform HomeCardScale => (ScaleTransform)ProductionNames.RegistryFor(this)["HomeCardScale"];
    private TranslateTransform HomeCardTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["HomeCardTranslate"];
    private TranslateTransform HomeHeaderTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["HomeHeaderTranslate"];
    private TranslateTransform AwayPresetTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["AwayPresetTranslate"];
    private ScaleTransform AwayCardScale => (ScaleTransform)ProductionNames.RegistryFor(this)["AwayCardScale"];
    private TranslateTransform AwayCardTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["AwayCardTranslate"];
    private TranslateTransform AwayHeaderTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["AwayHeaderTranslate"];
    private ScaleTransform DividerScale => (ScaleTransform)ProductionNames.RegistryFor(this)["DividerScale"];
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
    private TranslateTransform BeamTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["BeamTranslate"];
    private TranslateTransform HeaderTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["HeaderTranslate"];
    private TranslateTransform StepIndicatorTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["StepIndicatorTranslate"];
    private DropShadowEffect Step1Glow => (DropShadowEffect)ProductionNames.RegistryFor(this)["Step1Glow"];
    private TranslateTransform NavBarTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["NavBarTranslate"];
    private ScaleTransform BackBtnScale => (ScaleTransform)ProductionNames.RegistryFor(this)["BackBtnScale"];
    private ScaleTransform NextBtnScale => (ScaleTransform)ProductionNames.RegistryFor(this)["NextBtnScale"];
    private void Native_DropperSetupAwayColor_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; DropperSetupAwayColor_Click(sender!, e); }
    private void Native_DropperSetupAwaySecondary_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; DropperSetupAwaySecondary_Click(sender!, e); }
    private void Native_DropperSetupHomeColor_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; DropperSetupHomeColor_Click(sender!, e); }
    private void Native_DropperSetupHomeSecondary_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; DropperSetupHomeSecondary_Click(sender!, e); }
    private void Native_FormatChoice_Click_Checked(object? sender, RoutedEventArgs e) { if (sender is Avalonia.Controls.Primitives.ToggleButton toggle && toggle.IsChecked != true) return; FormatChoice_Click(sender!, e); }
    private void Native_OversDisplay_LostKeyboardFocus(object? sender, RoutedEventArgs e) { OversDisplay_LostFocus(sender!, e); }
    private void Native_OversDisplay_TextChanged(object? sender, TextChangedEventArgs e) { OversDisplay_TextChanged(sender!, e); }
    private void Native_PickSetupAwayColor_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; PickSetupAwayColor_Click(sender!, e); }
    private void Native_PickSetupAwaySecondary_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; PickSetupAwaySecondary_Click(sender!, e); }
    private void Native_PickSetupHomeColor_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; PickSetupHomeColor_Click(sender!, e); }
    private void Native_PickSetupHomeSecondary_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; PickSetupHomeSecondary_Click(sender!, e); }
    private void Native_SetupAwayColorBox_TextChanged(object? sender, TextChangedEventArgs e) { SetupAwayColorBox_TextChanged(sender!, e); }
    private void Native_SetupAwaySecondaryBox_TextChanged(object? sender, TextChangedEventArgs e) { SetupAwaySecondaryBox_TextChanged(sender!, e); }
    private void Native_SetupClearItemHighlightColor_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Right) return; SetupClearItemHighlightColor_Click(sender!, e); }
    private void Native_SetupClearNewHighlightColor_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Right) return; SetupClearNewHighlightColor_Click(sender!, e); }
    private void Native_SetupHomeColorBox_TextChanged(object? sender, TextChangedEventArgs e) { SetupHomeColorBox_TextChanged(sender!, e); }
    private void Native_SetupHomeSecondaryBox_TextChanged(object? sender, TextChangedEventArgs e) { SetupHomeSecondaryBox_TextChanged(sender!, e); }
}
}

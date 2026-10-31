using RocheScoreboard.Mac.Production;
// Generated from Views/CricketControlPanel.xaml.cs; edit translator/adapters.
// Source SHA-256: 883eb038022c8086156d7734cc81266627bfe2de180de08f628cf68ef3b0c881
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
using Brush = Avalonia.Media.IBrush;
using Brushes = Avalonia.Media.Brushes;
using Button = Avalonia.Controls.Button;
using Color = Avalonia.Media.Color;
using ColorConverter = RocheScoreboard.Mac.Native.ColorConverter;
using FontFamily = Avalonia.Media.FontFamily;
using HorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using MessageBox = RocheScoreboard.Mac.Native.NativeMessageBox;
using UserControl = Avalonia.Controls.UserControl;

namespace Roche_Scoreboard.Views
{
    public partial class CricketControlPanel : UserControl
    {
        private sealed record PlayerChoice(string Name, string Figures, string Status);
        public event Action? ApplicationPreferencesRequested;
        private void OpenApplicationPreferences_Click(object? sender, RoutedEventArgs e)
            => ApplicationPreferencesRequested?.Invoke();
        private CricketOverlayHost? _overlayHost;
        private readonly Dictionary<CricketOverlayKind, Button> _overlayButtons = new();
        public void SetOverlayHost(CricketOverlayHost host)
        {
            if (_overlayHost != null) _overlayHost.StateChanged -= RefreshOverlayControls;
            _overlayHost = host; host.StateChanged += RefreshOverlayControls;
            host.SetPresentation(_currentPresentation);
            host.AutomaticEnabled = CricketAutomaticOverlays.IsChecked == true;
            CricketOverlayButtons.Children.Clear(); _overlayButtons.Clear();
            RegisterOverlayButton(CricketOverlayKind.Extras, CricketExtrasOverlayButton);
            RegisterOverlayButton(CricketOverlayKind.Boundaries, CricketBoundariesOverlayButton);
            foreach (CricketOverlayGroup group in Enum.GetValues<CricketOverlayGroup>())
            {
                var colour = group switch { CricketOverlayGroup.Review => "#75D286", CricketOverlayGroup.Pressure => "#E7B94F", CricketOverlayGroup.Context => "#8FC5FF", _ => "#B9A1FA" };
                var background = group switch { CricketOverlayGroup.Review => "#081A0E", CricketOverlayGroup.Pressure => "#14100A", CricketOverlayGroup.Context => "#06101C", _ => "#151025" };
                CricketOverlayButtons.Children.Add(new NativeTextBlock
                {
                    Text = group switch { CricketOverlayGroup.Review => "RECENT PLAY", CricketOverlayGroup.Pressure => "PRESSURE", CricketOverlayGroup.Context => "MATCH CONTEXT", _ => "SCORING OUTLOOK" },
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colour)),
                    FontSize = 11,
                    FontWeight = FontWeight.Black,
                    Margin = new Thickness(3, 8, 0, 6)
                });
                var cards = new Avalonia.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 10) };
                CricketOverlayButtons.Children.Add(cards);
                foreach (var fact in host.Facts.Where(f => f.Group == group && f.Kind is not (CricketOverlayKind.Extras or CricketOverlayKind.Boundaries)))
                {
                    CricketOverlayKind kind = fact.Kind;
                    var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(4, 10, 4, 10) };
                    bool situation=kind is CricketOverlayKind.Partnership or CricketOverlayKind.MatchState or CricketOverlayKind.InningsRecap;
                    if(situation)content.Children.Add(new OperatorSymbol
                    {
                        Kind=kind switch {CricketOverlayKind.Partnership=>"partnership",CricketOverlayKind.MatchState=>"equation",_=>"innings-recap"},
                        Width=24,Height=24,Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(colour)),Margin=new Thickness(0,0,0,3)
                    });
                    else content.Children.Add(new NativeTextBlock
                    {
                        Text = group switch { CricketOverlayGroup.Review => "↶", CricketOverlayGroup.Pressure => "◷", CricketOverlayGroup.Context => "≡", _ => "↗" },
                        FontSize = 18,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colour)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 3)
                    });
                    content.Children.Add(new NativeTextBlock { Text = OverlayCaption(kind), FontSize = 11, FontWeight = FontWeight.Black, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colour)), HorizontalAlignment = HorizontalAlignment.Center,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center });
                    var button = new NativeButton
                    {
                        Tag = kind,
                        Content = content,
                        Theme = NativeResources.Require<Avalonia.Styling.ControlTheme>(this, "OperatorPresentationButton"),
                        MinHeight = 72,
                        Margin = new Thickness(4),
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(background))
                    };
                    button.Click += (_, _) => _overlayHost?.Request(kind);
                    if(situation){OperatorFeedback.SetManaged(button,true);CricketActionMotion.SetEnabled(button,false);}
                    RegisterOverlayButton(kind,button);cards.Children.Add(button);
                }
            }
            RefreshOverlayControls();
        }
        private void RegisterOverlayButton(CricketOverlayKind kind, Button button)
        {
            button.Tag = kind; _overlayButtons[kind] = button;
            Avalonia.Automation.AutomationProperties.SetName(button, OverlayCaption(kind));
            Avalonia.Controls.ToolTip.SetShowOnDisabled(button,true);
        }
        private void ExtrasOverlay_Click(object? sender, RoutedEventArgs args) => _overlayHost?.Request(CricketOverlayKind.Extras);
        private void BoundariesOverlay_Click(object? sender, RoutedEventArgs args) => _overlayHost?.Request(CricketOverlayKind.Boundaries);
        private static string OverlayCaption(CricketOverlayKind kind) => kind switch
        {
            CricketOverlayKind.LastOver => "LAST OVER",
            CricketOverlayKind.SinceScore => "SINCE SCORE",
            CricketOverlayKind.SinceWicket => "SINCE WICKET",
            CricketOverlayKind.SinceBoundary => "SINCE BOUNDARY",
            CricketOverlayKind.LastWicket => "LAST WICKET",
            CricketOverlayKind.ScoringWindow => "SCORING WINDOW",
            CricketOverlayKind.Comparison => "CHASE COMPARISON",
            CricketOverlayKind.Projection => "PROJECTED SCORE",
            CricketOverlayKind.Toss => "TOSS RESULT",
            CricketOverlayKind.Boundaries => "INNINGS BOUNDARIES",
            CricketOverlayKind.Extras => "EXTRAS",
            CricketOverlayKind.Partnership => "CURRENT PARTNERSHIP",
            CricketOverlayKind.MatchState => "MATCH STATE",
            CricketOverlayKind.InningsRecap => "INNINGS RECAP",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown cricket overlay")
        };
        internal void ClearOverlayHost(CricketOverlayHost host)
        {
            if (!ReferenceEquals(_overlayHost, host)) return;
            host.SetOutputActive(false);
            host.StateChanged -= RefreshOverlayControls;
            _overlayHost = null;
            OverlayStatusText.Text = "OUTPUT OFF · OVERLAYS PAUSED";
            foreach (var button in _overlayButtons.Values) { button.IsEnabled = false; button.Opacity = .45; }
        }

        private void RefreshOverlayControls()
        {
            if (_overlayHost is null) return;
            OverlayStatusText.Text = _overlayHost.Status;
            foreach (var button in _overlayButtons.Values)
            {
                var fact = _overlayHost.Facts.FirstOrDefault(f => f.Kind == (CricketOverlayKind)button.Tag);
                button.IsEnabled = _overlayHost.OutputActive && _overlayHost.AllowsOverlays && fact?.Available == true;
                button.Opacity = button.IsEnabled ? 1 : .45;
                Avalonia.Controls.ToolTip.SetTip(button, !_overlayHost.OutputActive ? "Show the output window before showing an overlay" : !_overlayHost.AllowsOverlays ? "Return to the live scorebug to show overlays" : fact?.Available == true ? fact.Title : fact?.UnavailableReason);
                if(_overlayHost.OutputActive && _overlayHost.AllowsOverlays && fact?.Available==true)
                {
                    if(fact.Kind==CricketOverlayKind.Partnership)Avalonia.Controls.ToolTip.SetTip(button,fact.Title+"\n"+CricketPartnershipAnalysis.DeliveryConvention);
                    else if(!string.IsNullOrWhiteSpace(fact.UnavailableReason))Avalonia.Controls.ToolTip.SetTip(button,fact.Title+"\n"+fact.UnavailableReason);
                }
            }
        }
        private void AutoOverlays_Changed(object? sender, RoutedEventArgs args)
        {
            if (_overlayHost != null) _overlayHost.AutomaticEnabled = CricketAutomaticOverlays.IsChecked == true;
            if (!_loadingPresentationPreferences && Avalonia.Application.Current is not RocheScoreboard.Mac.App { IsSelfTestMode: true })
                OperatorWorkflowPreferences.Update(p => p.CricketAutomaticOverlays = CricketAutomaticOverlays.IsChecked == true);
        }
        private void CancelCricketOverlay_Click(object? sender, RoutedEventArgs args) => _overlayHost?.Cancel();

        private CricketMatchManager? _match;
        private CricketScorebugControl? _scorebug;
        private Window? _displayWindow;

        private CricketDeliveryDialog? _deliveryEntry;
        private DateTime _deliveryPointerGuardUntil;
        private bool _loadingPresentationPreferences = true;
        private void CricketPresentationPreference_Changed(object? sender, RoutedEventArgs args)
        {
            if (_loadingPresentationPreferences) return;
            if (_scorebug != null)
            {
                _scorebug.EventAnimationsEnabled = CricketEventAnimations.IsChecked == true; _scorebug.RollingFiguresEnabled = CricketRollingFigures.IsChecked == true;
                _scorebug.BowlerDetailsEnabled = CricketBowlerDetails.IsChecked == true;
            }
            if (Avalonia.Application.Current is not RocheScoreboard.Mac.App { IsSelfTestMode: true }) OperatorWorkflowPreferences.Update(p => { p.CricketEventAnimations = CricketEventAnimations.IsChecked == true; p.CricketRollingFigures = CricketRollingFigures.IsChecked == true; p.CricketBowlerDetails = CricketBowlerDetails.IsChecked == true; });
        }

        // Extras popup state
        // Batter selection mode: "wicket", "striker", "nonstriker"
        private string _batterSelectionMode = "wicket";

        // Batter display rows: track which player index occupies top vs bottom
        private int _cpTopBatterIndex = -1;
        private int _cpBottomBatterIndex = -1;

        // Delivery log: current over group tracking
        private int _logCurrentOver = -1;
        private StackPanel? _logCurrentOverPanel;
        private CricketInnings? _loggedInnings;
        private int _loggedDeliveryCount;

        public event Action? ResetRequested;

        public CricketControlPanel()
        {
            NativePointer.EnsureInitialized(); ProductionNames.BeginLoad(this); try { InitializeComponent(); } finally { ProductionNames.CompleteLoad(this); }
            PointerPressed += (_, args) => { if (DateTime.UtcNow < _deliveryPointerGuardUntil) args.Handled = true; };
            KeyDown += (_, args) =>
            {
                if (args.Key == Key.Escape && _deliveryEntry is null
                    && (BatterSelectionOverlay.IsVisible == true || BowlerSelectionOverlay.IsVisible == true))
                {
                    args.Handled = true;
                    CancelPlayerSelection();
                    return;
                }
                if (_deliveryEntry is null || args.Source is not Control source || !ConsoleViewport.IsAncestorOf(source)) return;
                args.Handled = true; _deliveryEntry.ApplyButton.Focus();
            };
            GotFocus += (_, args) =>
            {
                if (_deliveryEntry is null || args.Source is not Control destination || !ConsoleViewport.IsAncestorOf(destination)) return;
                args.Handled = true; _deliveryEntry.ApplyButton.Focus();
            };
            var preferences = OperatorWorkflowPreferences.Load();
            CricketAutomaticOverlays.IsChecked = preferences.CricketAutomaticOverlays; CricketEventAnimations.IsChecked = preferences.CricketEventAnimations; CricketRollingFigures.IsChecked = preferences.CricketRollingFigures; CricketBowlerDetails.IsChecked = preferences.CricketBowlerDetails;
            _loadingPresentationPreferences = false;
            CricketSettingsColumns.SizeChanged += (_, _) => ReflowSettingsColumns();
            InitializeWorkflowControls();
            InitializeBreakPresentations();
        }

        private bool? _narrowConsole;
        private readonly Dictionary<Grid, (GridLength[] Columns, (Control Element, int Column)[] Children)> _consoleLayouts = new();
        private void Console_SizeChanged(object? sender, SizeChangedEventArgs e) => AdaptConsole();

        private void AdaptConsole()
        {
            if (ConsoleViewport is null || ScoringColumns is null) return;
            bool narrow = Bounds.Width < 900;
            ConsoleViewport.MinimumCanvasWidth = 320;
            ConsoleViewport.NaturalHeight = false;
            ConsoleViewport.MinimumCanvasHeight = 0;
            if (_narrowConsole != narrow)
            {
                foreach (Grid grid in new[] { ScoringColumns, DisplayColumns })
                {
                    if (!_consoleLayouts.ContainsKey(grid))
                        _consoleLayouts[grid] = (grid.ColumnDefinitions.Select(c => c.Width).ToArray(),
                            grid.Children.OfType<Control>().Select(child => (child, Grid.GetColumn(child))).ToArray());
                    var saved = _consoleLayouts[grid];
                    grid.RowDefinitions.Clear();
                    if (narrow)
                    {
                        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        grid.RowDefinitions.Add(new RowDefinition { Height = grid == ScoringColumns ? new GridLength(320) : GridLength.Auto });
                    }
                    for (int i = 0; i < 3; i++) grid.ColumnDefinitions[i].Width = narrow
                        ? new GridLength(i == 0 ? 1 : 0, GridUnitType.Star) : saved.Columns[i];
                    // The original log column has a minimum width which must yield in stacked mode.
                    grid.ColumnDefinitions[2].MinWidth = !narrow && grid == ScoringColumns ? 260 : 0;
                    foreach (var child in saved.Children)
                    {
                        Grid.SetColumn(child.Element, narrow ? 0 : child.Column);
                        Grid.SetRow(child.Element, narrow && child.Column == 2 ? 1 : 0);
                        if(grid==ScoringColumns && child.Column==0 && child.Element is ScrollViewer scorer) scorer.VerticalScrollBarVisibility=narrow ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
                    }
                }
                _narrowConsole = narrow;
            }
            // Pages scroll independently while the navigation dock remains anchored.
            double scoringHeight = narrow ? double.NaN : Math.Max(260, CricketMatchPageViewport.NativeHeight() - 24);
            double outputHeight = narrow ? double.NaN : Math.Max(260, CricketOutputPageViewport.NativeHeight() - 32);
            if (narrow) { ScoringColumns.Height = double.NaN; DisplayColumns.Height = double.NaN; }
            else
            {
                if (!double.IsFinite(ScoringColumns.Height) || Math.Abs(ScoringColumns.Height - scoringHeight) > .5) ScoringColumns.Height = scoringHeight;
                if (!double.IsFinite(DisplayColumns.Height) || Math.Abs(DisplayColumns.Height - outputHeight) > .5) DisplayColumns.Height = outputHeight;
            }
            CricketPresentationGrid.Columns = Bounds.Width < 650 ? 2 : 3;
            HeaderOversBadge.IsVisible = narrow ? false : true;
            bool tiny = Bounds.Width < 560;
            Grid.SetColumnSpan(InningsLabel, tiny ? 3 : 1);
            WorkflowBadge.IsVisible = Bounds.Width < 430 ? false : true;
            PersistentHeader.ColumnDefinitions[1].Width = new GridLength(Bounds.Width < 430 ? 0 : 18);
            InningsLabel.FontSize = narrow ? 20 : 24;
            bool shortWindow = Bounds.Height < 500;
            foreach (Border card in new[] { BatterSelectionCard, BowlerSelectionCard })
                card.Padding = new Thickness(shortWindow ? 14 : 24);
            BatterSelectionTitle.FontSize = shortWindow ? 20 : 25;
            BatterSelectionOverlay.Padding = BowlerSelectionOverlay.Padding = new Thickness(shortWindow ? 12 : 24);
            ConfirmBatterButton.Height = ConfirmBowlerButton.Height = shortWindow ? 42 : 48;
            ConsoleViewport.RefreshViewport();
        }

        private bool? _stackedSettings;
        private void ReflowSettingsColumns()
        {
            var grid = CricketSettingsColumns;
            if (grid.NativeWidth() <= 0) return;
            bool stack = grid.NativeWidth() < 720;
            if (_stackedSettings == stack) return;
            if (!_consoleLayouts.ContainsKey(grid))
                _consoleLayouts[grid] = (grid.ColumnDefinitions.Select(c => c.Width).ToArray(),
                    grid.Children.OfType<Control>().Select(child => (child, Grid.GetColumn(child))).ToArray());
            var saved = _consoleLayouts[grid]; grid.RowDefinitions.Clear();
            if (stack) { grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); }
            for (int i = 0; i < 3; i++) grid.ColumnDefinitions[i].Width = stack ? new GridLength(i == 0 ? 1 : 0, GridUnitType.Star) : saved.Columns[i];
            foreach (var child in saved.Children)
            {
                Grid.SetColumn(child.Element, stack ? 0 : child.Column); Grid.SetRow(child.Element, stack && child.Column == 2 ? 1 : 0);
                child.Element.Margin = new Thickness(0, stack && child.Column == 2 ? 16 : 0, 0, 0);
            }
            _stackedSettings = stack;
        }

        private void MainTabControl_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.Source != MainTabControl)
                return;

            AdaptConsole();
            if(MainTabControl.SelectedItem==ScorecardTab) RefreshScorecard();
            AnimateSelectedTabContent();
        }

        private void AnimateSelectedTabContent()
        {
            if(MainTabControl?.SelectedContent is Control content) SharedSurfaceMotion.Reveal(content);
        }

        public void SetMatch(CricketMatchManager match)
        {
            int clearedGeneration = unchecked(_selectorGeneration + 1);
            ClearMatch();
            // Naming the cleared sources can synchronously bind another game.
            // Do not let this older clear-and-bind continuation replace it.
            if (_selectorGeneration != clearedGeneration) return;
            _match = match;
            _match.MatchChanged += UpdateUI;
            _match.DeliveryAdded += OnDeliveryAdded;
            _match.BatterSelectionNeeded += OnBatterSelectionNeeded;
            _match.BowlerSelectionNeeded += OnBowlerSelectionNeeded;
            CricketWeatherLocation.Text = match.WeatherLocation;
            CricketWeatherTemperature.Text = "—";
            CricketWeatherDescription.Text = "Weather will update for your selected venue.";
            UpdateUI();

            RebuildDeliveryLog();
        }

        /// <summary>Detach the previous game before resetting or binding another.</summary>
        public void ClearMatch()
        {
            ClosePlayerMenu();
            ResetBreakPresentations();
            if (_match != null)
            {
                _match.MatchChanged -= UpdateUI;
                _match.DeliveryAdded -= OnDeliveryAdded;
                _match.BatterSelectionNeeded -= OnBatterSelectionNeeded;
                _match.BowlerSelectionNeeded -= OnBowlerSelectionNeeded;
            }
            if (_overlayHost != null) { _overlayHost.StateChanged -= RefreshOverlayControls; _overlayHost.Dispose(); _overlayHost = null; }
            TossOverlay.IsVisible = false;
            TossHomeChoice.IsChecked = TossAwayChoice.IsChecked = TossBatChoice.IsChecked = TossBowlChoice.IsChecked = false;
            _selectorGeneration++;
            ClearSelectorReturnContext();
            _cpBatLogo = _cpBowlLogo = _cpThemeSignature = null;
            _match = null;
            ResetWorkflowControls();
            _deliveryEntry?.Cancel();
            _deliveryEntry = null; DeliveryEntryContent.Content = null; DeliveryEntryOverlay.IsVisible = false; ConsoleViewport.IsEnabled = true; ConsoleViewport.IsHitTestVisible = true;
            _scorebug = null;
            _summaryControl = null;
            _displayWindow = null;
            _cpTopBatterIndex = _cpBottomBatterIndex = -1;
            if (!UpdatePlayerMenuNames()) return;
            _currentPresentation = "scorebug";
            _batterSelectionMode = "striker";
            BatterSelectionOverlay.IsVisible = BowlerSelectionOverlay.IsVisible =
                false;
            BatterSelectionList.Items.Clear();
            BowlerSelectionList.Items.Clear();
            _rebuildingLog = false; _overChoices = [];
            _refreshingOverChoices = true; DeliveryOverChoice.Items.Clear(); _refreshingOverChoices = false;
            EventLog.Children.Clear();
            _logCurrentOver = -1;
            _logCurrentOverPanel = null;
            _loggedInnings = null;
            _loggedDeliveryCount = 0;
            MainTabControl.SelectedIndex = 0;
            UpdatePresentationSelection();
        }

        public void SetScorebug(CricketScorebugControl scorebug)
        {
            _scorebug = scorebug;
            scorebug.EventAnimationsEnabled = CricketEventAnimations.IsChecked == true;
            scorebug.RollingFiguresEnabled = CricketRollingFigures.IsChecked == true;
            scorebug.BowlerDetailsEnabled = CricketBowlerDetails.IsChecked == true;
        }

        public void SetDisplayWindow(Window window)
        {
            _displayWindow = window;
        }

        public void SetMessages(List<string> messages)
        {
            // Preserve saved data without exposing a ticker that is no longer on air.
            if (_match is not null) _match.Messages = new List<string>(messages);
        }

        /// <summary>Trigger the initial batter + bowler selection at match start.</summary>
        public void TriggerInitialSelection()
        {
            _cpTopBatterIndex = -1;
            _cpBottomBatterIndex = -1;
            if (_match?.TossPending == true || _match?.IsMatchFinalized == true) return;
            _batterSelectionMode = "striker";
            ShowBatterSelection("SELECT OPENING STRIKER");
        }

        public void ResumePendingSelection() => ResumePendingSelection(continueSelectionFlow: false);

        private void ResumePendingSelection(bool continueSelectionFlow)
        {
            var innings = _match?.CurrentInnings;
            if (innings == null || innings.IsComplete || _match?.IsMatchFinalized == true || _match?.TossPending == true || _deliveryEntry != null) return;
            if (innings.NeedsBatterSelection)
            {
                _batterSelectionMode = innings.Striker is null ? "striker"
                    : innings.NonStriker is null ? "nonstriker" : "wicket";
                ShowBatterSelection(_batterSelectionMode == "striker" ? "SELECT OPENING STRIKER"
                    : _batterSelectionMode == "nonstriker" ? "SELECT OPENING NON-STRIKER" : "SELECT NEW BATTER",
                    continueSelectionFlow: continueSelectionFlow);
            }
            else if (innings.NeedsBowlerSelection)
            {
                ShowBowlerSelection(innings.LegalBallsBowled > 0, continueSelectionFlow: continueSelectionFlow);
            }
            else ClearSelectorReturnContext();
        }

        private void PresentationsbattingGuard(bool waiting)
        { Presentationbatting.IsEnabled = Presentationbowling.IsEnabled = Presentationsummary.IsEnabled = !waiting; }

        private void OpenToss_Click(object? sender, RoutedEventArgs args)
        {
            if (_match?.TossPending != true) return;
            TossHomeChoice.Content = TossTeamCard(_match.TeamAName, _match.TeamAAbbr, _match.TeamALogoPath);
            TossAwayChoice.Content = TossTeamCard(_match.TeamBName, _match.TeamBAbbr, _match.TeamBLogoPath);
            TossHomeChoice.Background = TossTeamTint(_match.TeamAPrimaryColor);
            TossAwayChoice.Background = TossTeamTint(_match.TeamBPrimaryColor);
            TossError.Text = ""; TossChoice_Checked(this, new RoutedEventArgs()); TossOverlay.IsVisible = true; SharedSurfaceMotion.Reveal(TossSheet); TossHomeChoice.Focus();
        }
        private static Brush TossTeamTint(string value)
        {
            try { var colour=(Color)ColorConverter.ConvertFromString(value);return new SolidColorBrush(Color.FromArgb(45,colour.R,colour.G,colour.B)); }
            catch(Exception error) when(error is FormatException or NotSupportedException or ArgumentException) { return Brushes.Transparent; }
        }
        private static Grid TossTeamCard(string name, string abbreviation, string? logoPath)
        {
            var logo = TeamVisuals.LoadLogo(logoPath);
            var card = new Grid { MinHeight = 60 };
            var content = new Grid(); content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) }); content.ColumnDefinitions.Add(new ColumnDefinition());
            if (logo is not null) content.Children.Add(new Avalonia.Controls.Image { Source = logo, Width = 46, Height = 52, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center });
            else
            {
                var monogram=new NativeTextBlock {Text=abbreviation,FontSize=11,FontWeight=FontWeight.Bold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
                monogram.SetBinding(TextBlock.ForegroundProperty,new Avalonia.Data.Binding("Foreground"){RelativeSource=new Avalonia.Data.RelativeSource(Avalonia.Data.RelativeSourceMode.FindAncestor){AncestorType=typeof(Avalonia.Controls.RadioButton),AncestorLevel=1}});
                content.Children.Add(new NativeBorder {Width=36,Height=36,CornerRadius=new CornerRadius(10),Background=new SolidColorBrush(Color.FromArgb(30,128,150,175)),Child=monogram});
            }
            var title = new NativeTextBlock { Text = name, TextTrimming=TextTrimming.CharacterEllipsis, ToolTip=name, FontSize = 17, FontWeight = FontWeight.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            title.SetBinding(TextBlock.ForegroundProperty,new Avalonia.Data.Binding("Foreground"){RelativeSource=new Avalonia.Data.RelativeSource(Avalonia.Data.RelativeSourceMode.FindAncestor){AncestorType=typeof(Avalonia.Controls.RadioButton),AncestorLevel=1}});
            Grid.SetColumn(title, 1); content.Children.Add(title); card.Children.Add(content); return card;
        }
        private void TossChoice_Checked(object? sender, RoutedEventArgs args)
        {
            if (TossError is null || ConfirmTossButton is null) return;
            TossError.Text = "";
            ConfirmTossButton.IsEnabled = (TossHomeChoice.IsChecked == true || TossAwayChoice.IsChecked == true)
                && (TossBatChoice.IsChecked == true || TossBowlChoice.IsChecked == true);
            if (TossSelectionSummary is not null && _match is not null)
                TossSelectionSummary.Text = ConfirmTossButton.IsEnabled ? $"{(TossHomeChoice.IsChecked == true ? _match.TeamAName : _match.TeamBName)} won the toss and will {(TossBatChoice.IsChecked == true ? "bat" : "bowl")} first." : "Choose the toss winner and their decision.";
        }
        private void Toss_PreviewKeyDown(object? sender, Avalonia.Input.KeyEventArgs args)
        {
            if (args.Key != Avalonia.Input.Key.Escape) return;
            args.Handled = true; CloseToss_Click(this, new RoutedEventArgs());
        }
        private void CloseToss_Click(object? sender, RoutedEventArgs args)
        { TossOverlay.IsVisible = false; StartTossButton.Focus(); }
        private void ConfirmToss_Click(object? sender, RoutedEventArgs args)
        {
            if (_match is null) return;
            if (TossHomeChoice.IsChecked != true && TossAwayChoice.IsChecked != true || TossBatChoice.IsChecked != true && TossBowlChoice.IsChecked != true)
            { TossError.Text = "Choose the toss winner and whether they will bat or bowl."; return; }
            if (!_match.CompleteToss(TossHomeChoice.IsChecked == true, TossBatChoice.IsChecked == true))
            { TossError.Text = "The match state changed. Close this panel and check the match."; return; }
            _cpTopBatterIndex = _cpBottomBatterIndex = -1;
            TossOverlay.IsVisible = false; UpdateUI(); TriggerInitialSelection();
        }
        private void SkipPlayerSelection_Click(object? sender, RoutedEventArgs args) => CancelPlayerSelection();

        private Control? _selectorReturnSource;
        private CricketMatchManager? _selectorReturnMatch;
        private CricketInnings? _selectorReturnInnings;
        private Window? _selectorReturnOwner;

        private bool IsSelectorChild(Control element) => ReferenceEquals(element, BatterSelectionOverlay)
            || ReferenceEquals(element, BowlerSelectionOverlay) || BatterSelectionOverlay.IsAncestorOf(element)
            || BowlerSelectionOverlay.IsAncestorOf(element);

        private static bool IsVisibleEnabledToOwner(Control source, Window owner)
        {
            // WPF inherits IsEnabled; the translated control's local value is
            // not sufficient when its scoring group has just become disabled.
            for (AvaloniaObject? current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is Control element && (!element.IsVisible || !element.IsEnabled)) return false;
                if (ReferenceEquals(current, owner)) return true;
            }
            return false;
        }

        private bool IsLiveSelectorSource(Control? source, Avalonia.Controls.Window? owner) => owner is not null
            && source is { IsLoaded: true, Focusable: true } && IsVisibleEnabledToOwner(source, owner)
            && this.IsAncestorOf(source) && !IsSelectorChild(source) && ReferenceEquals(NativeWindow.GetWindow(source), owner);

        private void ClearSelectorReturnContext()
        {
            _selectorReturnSource = null; _selectorReturnMatch = null;
            _selectorReturnInnings = null; _selectorReturnOwner = null;
        }

        private void CaptureSelectorReturnContext(Control? source, bool continueSelectionFlow)
        {
            var owner = NativeWindow.GetWindow(this);
            if (continueSelectionFlow && ReferenceEquals(_selectorReturnMatch, _match)
                && ReferenceEquals(_selectorReturnInnings, _match?.CurrentInnings)
                && ReferenceEquals(_selectorReturnOwner, owner)) return;
            _selectorReturnMatch = _match; _selectorReturnInnings = _match?.CurrentInnings;
            _selectorReturnOwner = owner;
            _selectorReturnSource = IsLiveSelectorSource(source, owner) ? source
                : NativeKeyboard.FocusedElement is Control focused && IsLiveSelectorSource(focused, owner) ? focused : null;
        }

        private void CancelPlayerSelection()
        {
            if (BatterSelectionOverlay.IsVisible != true && BowlerSelectionOverlay.IsVisible != true) return;
            bool bowling = BowlerSelectionOverlay.IsVisible == true;
            var closingOverlay = bowling ? BowlerSelectionOverlay : BatterSelectionOverlay;
            var otherOverlay = bowling ? BatterSelectionOverlay : BowlerSelectionOverlay;
            bool closeOther = otherOverlay.IsVisible == true;
            var match = _match; var innings = match?.CurrentInnings; var owner = NativeWindow.GetWindow(this);
            var source = ReferenceEquals(_selectorReturnMatch, match) && ReferenceEquals(_selectorReturnInnings, innings)
                && ReferenceEquals(_selectorReturnOwner, owner) ? _selectorReturnSource : null;
            int generation = ++_selectorGeneration;
            ClearSelectorReturnContext(); // No queued focus authority survives this Cancel.
            bool StillCurrent() => generation == _selectorGeneration && ReferenceEquals(_match, match)
                && ReferenceEquals(_match?.CurrentInnings, innings) && ReferenceEquals(NativeWindow.GetWindow(this), owner);
            // Each visibility write can synchronously open a replacement flow.
            // Retire only this entry's overlays, never one opened by its observer.
            closingOverlay.IsVisible = false;
            if (!StillCurrent()) return;
            if (closeOther)
            {
                otherOverlay.IsVisible = false;
                if (!StillCurrent()) return;
            }
            if (!IsLoaded || !IsVisible || owner is not { IsActive: true }) return;
            if (!IsLiveSelectorSource(source, owner)) source = IsLiveSelectorSource(ScoringLockBanner, owner) ? ScoringLockBanner
                : bowling ? SelectOpeningBowler : SelectOpeningBatters;
            if (source is null || !IsLiveSelectorSource(source, owner)) return;
            // Collapsing a picker can synchronously notify other UI owners. A
            // live focus claim outside it belongs to that owner, not this exit.
            if (NativeKeyboard.FocusedElement is Control focused && !ReferenceEquals(focused, source)
                && focused is { IsLoaded: true, Focusable: true } && IsVisibleEnabledToOwner(focused, owner)
                && !IsSelectorChild(focused) && ReferenceEquals(NativeWindow.GetWindow(focused), owner)) return;
            NativeKeyboard.Focus(source);
            OperatorViewport.RevealOperatorTarget(source, () => StillCurrent()
                && ReferenceEquals(NativeKeyboard.FocusedElement, source) && IsLiveSelectorSource(source, owner)
                && IsLoaded && IsVisible && owner.IsActive);
        }
        private void SelectOpeningBatters_Click(object? sender, RoutedEventArgs args)
        {
            if (_match?.TossPending != false) return;
            _batterSelectionMode = _match.CurrentInnings?.Striker is null ? "striker" : _match.CurrentInnings?.NonStriker is null ? "nonstriker" : "wicket";
            ShowBatterSelection(_batterSelectionMode == "striker" ? "SELECT OPENING STRIKER" : _batterSelectionMode == "nonstriker" ? "SELECT NON-STRIKER" : "SELECT NEW BATTER", sender as Control);
        }
        internal bool HasPendingOperatorEdits => _scorecardDirty || _scorecardFowEditing;
        internal void OpenCommandBowler() => SelectOpeningBowler_Click(this, new RoutedEventArgs());

        private void SelectOpeningBowler_Click(object? sender, RoutedEventArgs args)
        { if (_match?.TossPending == false) ShowBowlerSelection(false, sender as Control); }

        // ---- Scoring ----

        private void RecordQuickScore(int runs, CricketDeliveryType type)
        { if (_deliveryEntry is null) _match?.RecordDelivery(runs, type); }
        private void Dot_Click(object? sender, RoutedEventArgs e) =>
            RecordQuickScore(0, CricketDeliveryType.Dot);
        private void Run1_Click(object? sender, RoutedEventArgs e) =>
            RecordQuickScore(1, CricketDeliveryType.Runs);
        private void Run2_Click(object? sender, RoutedEventArgs e) =>
            RecordQuickScore(2, CricketDeliveryType.Runs);
        private void Run3_Click(object? sender, RoutedEventArgs e) =>
            RecordQuickScore(3, CricketDeliveryType.Runs);
        private void Four_Click(object? sender, RoutedEventArgs e) =>
            RecordQuickScore(4, CricketDeliveryType.Four);
        private void Run5_Click(object? sender, RoutedEventArgs e) =>
            RecordQuickScore(5, CricketDeliveryType.Runs);
        private void Six_Click(object? sender, RoutedEventArgs e) =>
            RecordQuickScore(6, CricketDeliveryType.Six);
        private void Wicket_Click(object? sender, RoutedEventArgs e) => OpenDeliveryDialog(CricketDeliveryType.Wicket);
        private void Wide_Click(object? sender, RoutedEventArgs e) => OpenDeliveryDialog(CricketDeliveryType.Wide);
        private void NoBall_Click(object? sender, RoutedEventArgs e) => OpenDeliveryDialog(CricketDeliveryType.NoBall);
        private void Bye_Click(object? sender, RoutedEventArgs e) => OpenDeliveryDialog(CricketDeliveryType.Bye);
        private void LegBye_Click(object? sender, RoutedEventArgs e) => OpenDeliveryDialog(CricketDeliveryType.LegBye);

        private void OpenDeliveryDialog(CricketDeliveryType type, CricketDelivery? delivery = null)
        {
            if (_match?.CurrentInnings is not CricketInnings innings) return;
            if (delivery is null && (innings.IsComplete || innings.NeedsBatterSelection || innings.NeedsBowlerSelection)) return;
            if (_deliveryEntry != null) return;
            var entry = new CricketDeliveryDialog(_match, delivery, type); _deliveryEntry = entry;
            DeliveryEntryContent.Content = entry.Embed(applied =>
            {
                if (!ReferenceEquals(_deliveryEntry, entry)) return;
                _deliveryPointerGuardUntil = DateTime.UtcNow.AddMilliseconds(250);
                _deliveryEntry = null; DeliveryEntryOverlay.IsVisible = false; DeliveryEntryContent.Content = null; ConsoleViewport.IsEnabled = true; ConsoleViewport.IsHitTestVisible = true;
                RefreshPlayerCardOpacity();
                if (applied) ResumePendingSelection();
                else UndoDeliveryButton.Focus();
                UpdateWorkflowState();
            });
            // Keep read-only match cards visually live while the modal owns
            // input. Disabling the entire parent previously left their cached
            // opacity at .45 when an extra refreshed the match before closing.
            ConsoleViewport.IsEnabled = true; ConsoleViewport.IsHitTestVisible = false; DeliveryEntryOverlay.IsVisible = true;
            KeyboardNavigation.SetTabNavigation(DeliveryEntryOverlay, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetTabNavigation(DeliveryEntryOverlay, KeyboardNavigationMode.Cycle);
            entry.ApplyButton.Focus();
            Dispatcher.UIThread.InvokeAsync(new Action(() => { if (ReferenceEquals(_deliveryEntry, entry)) entry.ApplyButton.Focus(); }));
        }

        // ---- Batter/bowler bars ----

        private void StrikerMenu_Click(object? sender, RoutedEventArgs e)=>ShowPlayerMenu(sender as Control, false, _cpTopBatterIndex);

        private void NonStrikerMenu_Click(object? sender, RoutedEventArgs e)=>ShowPlayerMenu(sender as Control, false, _cpBottomBatterIndex);

        private void BowlerMenu_Click(object? sender, RoutedEventArgs e)
            => ShowPlayerMenu(sender as Control, true, _match?.CurrentInnings?.CurrentBowlerIndex ?? -1);

        private void ChangeStriker_Click(object? sender, RoutedEventArgs e)
        {
            _batterSelectionMode = "striker";
            ShowBatterSelection("SELECT STRIKER", sender as Control);
        }

        private void ChangeNonStriker_Click(object? sender, RoutedEventArgs e)
        {
            _batterSelectionMode = "nonstriker";
            ShowBatterSelection("SELECT NON-STRIKER", sender as Control);
        }

        private void ChangeBowlerMenu_Click(object? sender, RoutedEventArgs e)
        {
            ShowBowlerSelection(false, sender as Control);
        }

        private void SwapStrike_Click(object? sender, RoutedEventArgs e)
        {
            if (_deliveryEntry is not null) return;
            _match?.RotateStrike();
        }

        private void ApplySelectionTheme(bool batting)
        {
            if (_match is null) return;
            string primary = batting ? _match.BattingTeamPrimaryColor : _match.BowlingTeamPrimaryColor;
            string secondary = batting ? _match.BattingTeamSecondaryColor : _match.BowlingTeamSecondaryColor;
            bool teamA = (batting ? _match.BattingTeamName : _match.BowlingTeamName) == _match.TeamAName;
            var card = batting ? BatterSelectionCard : BowlerSelectionCard;
            var logo = batting ? BatterSelectionLogo : BowlerSelectionLogo;
            var list = batting ? BatterSelectionList : BowlerSelectionList;
            var accent = TeamVisuals.Accent(primary);
            card.Background = TeamVisuals.Panel(primary, primary);
            card.BorderBrush = accent;
            var confirm = batting ? ConfirmBatterButton : ConfirmBowlerButton;
            confirm.Background = TeamVisuals.Panel(primary, primary, .4);
            confirm.BorderBrush = accent;
            logo.Source = TeamVisuals.LoadLogo(teamA ? _match.TeamALogoPath : _match.TeamBLogoPath);
            list.Resources["PlayerSelectionSurface"] = TeamVisuals.Panel(primary, primary, .4);
            list.Resources["PlayerSelectionHover"] = TeamVisuals.Panel(primary, primary, .3);
            list.Resources["PlayerSelectionAccent"] = accent;
            if (batting) BatterSelectionTeamText.Text = _match.BattingTeamName.ToUpperInvariant();
            else BowlerSelectionTeamText.Text = _match.BowlingTeamName.ToUpperInvariant();
        }

        // ---- Batter selection ----

        private void OnBatterSelectionNeeded()
        {
            _batterSelectionMode = "wicket";
            ShowBatterSelection("SELECT NEW BATTER");
        }

        private void ShowBatterSelection(string title, Control? source = null, bool continueSelectionFlow = false)
        {
            _batterWasRequired=_match?.CurrentInnings?.NeedsBatterSelection==true;
            if (_match?.CurrentInnings == null || _match.TossPending || _deliveryEntry != null) return;
            var inn = _match.CurrentInnings;
            CaptureSelectorReturnContext(source, continueSelectionFlow);

            ApplySelectionTheme(batting: true);
            BatterSelectionTitle.Text = title;
            BatterSelectionList.Items.Clear();
            BatterSelectionList.Tag = new List<int>();

            var indexList = (List<int>)BatterSelectionList.Tag;
            foreach (int i in Enumerable.Range(0,inn.BattingOrder.Count).OrderBy(i=>inn.BattingOrder[i].BattingPosition))
            {
                var p = inn.BattingOrder[i];
                if (p.IsOut) continue;

                // Exclude the OTHER active batter to prevent selecting same player for both slots
                if (_batterSelectionMode == "striker" && i == inn.NonStrikerIndex) continue;
                if (_batterSelectionMode == "nonstriker" && i == inn.StrikerIndex) continue;
                // For wicket mode, exclude both current batsmen at crease
                if (_batterSelectionMode == "wicket" &&
                    (i == inn.StrikerIndex || i == inn.NonStrikerIndex)) continue;

                string suffix = (i == inn.StrikerIndex) ? "  (striker)"
                              : (i == inn.NonStrikerIndex) ? "  (non-striker)"
                              : "";
                indexList.Add(i);
                BatterSelectionList.Items.Add(new PlayerChoice(p.FullName, p.BallsFaced > 0 || p.Runs > 0 || p.IsOut ? $"{p.Runs} ({p.BallsFaced})" : "",
                    string.IsNullOrEmpty(suffix) ? "Available to bat" : suffix.Trim()));
            }

            if (BatterSelectionList.Items.Count > 0)
                BatterSelectionList.SelectedIndex = 0;

            BatterSelectionOverlay.IsVisible = true;
            AnimateSelector(BatterSelectionCard); BatterSelectionList.Focus();
            ResetPlayerSelectorScroll(BatterSelectionList);

        }

        private bool IsCurrentSelectorContext(CricketMatchManager match, CricketInnings innings, Avalonia.Controls.Window? owner,
            object? batterChoices, object? bowlerChoices) => ReferenceEquals(_match, match)
            && ReferenceEquals(match.CurrentInnings, innings) && ReferenceEquals(NativeWindow.GetWindow(this), owner)
            && ReferenceEquals(_selectorReturnMatch, match) && ReferenceEquals(_selectorReturnInnings, innings)
            && ReferenceEquals(_selectorReturnOwner, owner) && ReferenceEquals(BatterSelectionList.Tag, batterChoices)
            && ReferenceEquals(BowlerSelectionList.Tag, bowlerChoices);

        private void ConfirmBatterSelection_Click(object? sender, RoutedEventArgs e)
        {
            if (BatterSelectionList.SelectedIndex < 0 || _match is not CricketMatchManager match
                || match.CurrentInnings is not CricketInnings innings) return;
            var indexList = BatterSelectionList.Tag as List<int>;
            if (indexList == null || BatterSelectionList.SelectedIndex >= indexList.Count) return;

            int playerIndex = indexList[BatterSelectionList.SelectedIndex];
            string mode = _batterSelectionMode;
            int generation = _selectorGeneration;
            var owner = NativeWindow.GetWindow(this);
            var bowlerChoices = BowlerSelectionList.Tag;
            BatterSelectionOverlay.IsVisible = false;
            // A visibility observer may replace the game or open a new chooser.
            // Never apply an old list's player index or selection mode to it.
            if (generation != _selectorGeneration || !IsCurrentSelectorContext(match, innings, owner, indexList, bowlerChoices)) return;

            switch (mode)
            {
                case "wicket":
                    match.SelectNewBatter(playerIndex);
                    break;
                case "striker":
                    match.SetStriker(playerIndex);
                    break;
                case "nonstriker":
                    match.SetNonStriker(playerIndex);
                    break;
            }
            // A normal required-selection resolution may advance the animation
            // generation. New chooser list identities or retired ownership still
            // invalidate this continuation after synchronous model callbacks.
            if (!IsCurrentSelectorContext(match, innings, owner, indexList, bowlerChoices)) return;
            ResumePendingSelection(continueSelectionFlow: true);
        }

        // ---- Bowler selection ----

        private void OnBowlerSelectionNeeded()
        {
            if (_deliveryEntry == null) ShowBowlerSelection(true);
        }

        private void ShowBowlerSelection(bool isOverEnd, Control? source = null, bool continueSelectionFlow = false)
        {
            _bowlerWasRequired=_match?.CurrentInnings?.NeedsBowlerSelection==true;
            if (_match?.CurrentInnings == null) return;
            var inn = _match.CurrentInnings;
            CaptureSelectorReturnContext(source, continueSelectionFlow);

            ApplySelectionTheme(batting: false);
            isOverEnd |= inn.NeedsBowlerSelection && inn.LegalBallsBowled > 0;
            BowlerSelectionHint.Text = isOverEnd
                ? "End of over — choose the next bowler"
                : "Select a bowler";

            BowlerSelectionList.Items.Clear();
            BowlerSelectionList.Tag = new List<int>();
            var indexList = (List<int>)BowlerSelectionList.Tag;
            int currentCompletedOvers = inn.CompletedOvers;
            int suggestedIndex = _match.SuggestedNextBowler();

            foreach (int i in Enumerable.Range(0, inn.BowlingAttack.Count).Where(_match.CanSelectBowler)
                .OrderByDescending(index => index == suggestedIndex).ThenByDescending(index => inn.BowlingAttack[index].LastOverBowledAt))
            {
                var p = inn.BowlingAttack[i];

                // Don't allow the bowler who just finished to bowl back-to-back
                if (!_match.CanSelectBowler(i)) continue;

                string hint;
                if (p.LastOverBowledAt >= 0)
                {
                    int oversAgo = currentCompletedOvers - p.LastOverBowledAt;
                    hint = oversAgo == 1 ? "  bowled 1 over ago" : $"  bowled {oversAgo} overs ago";
                }
                else
                    hint = "  not yet bowled";

                indexList.Add(i);
                BowlerSelectionList.Items.Add(new PlayerChoice(p.FullName, p.BallsBowled > 0 || p.RunsConceded > 0 || p.Wickets > 0 ? $"{p.BowlingFigures} ({p.OversDisplay})" : "", (i == suggestedIndex && p.LastOverBowledAt >= 0 ? "RECOMMENDED · " : "") + hint.Trim().ToUpperInvariant()));
            }

            // Pre-select the suggested bowler if available in filtered list
            int sugIdx = indexList.IndexOf(suggestedIndex);
            if (sugIdx >= 0)
                BowlerSelectionList.SelectedIndex = sugIdx;
            else if (BowlerSelectionList.Items.Count > 0)
                BowlerSelectionList.SelectedIndex = 0;

            BowlerSelectionOverlay.IsVisible = true;
            AnimateSelector(BowlerSelectionCard); BowlerSelectionList.Focus();
            ResetPlayerSelectorScroll(BowlerSelectionList);
        }

        private static void ResetPlayerSelectorScroll(Avalonia.Controls.ListBox list)
        {
            list.UpdateLayout();
            var scroll=FindSelectorScroll(list);
            scroll?.ScrollToHome();
            if(list.SelectedItem is object selected)list.ScrollIntoView(selected);
            Dispatcher.UIThread.InvokeAsync(Avalonia.Threading.DispatcherPriority.Loaded,new Action(()=>
            {
                if(!list.IsVisible)return;
                FindSelectorScroll(list)?.ScrollToHome();
                if(list.SelectedItem is object current)list.ScrollIntoView(current);
            }));
        }
        private static Avalonia.Controls.ScrollViewer? FindSelectorScroll(AvaloniaObject root)
        {
            if(root is Avalonia.Controls.ScrollViewer scroll)return scroll;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
                if(FindSelectorScroll(VisualTreeHelper.GetChild(root,i)) is {} child)return child;
            return null;
        }

        private void ConfirmBowlerSelection_Click(object? sender, RoutedEventArgs e)
        {
            if (BowlerSelectionList.SelectedIndex < 0 || _match is not CricketMatchManager match
                || match.CurrentInnings is not CricketInnings innings) return;
            var indexList = BowlerSelectionList.Tag as List<int>;
            if (indexList == null || BowlerSelectionList.SelectedIndex >= indexList.Count) return;

            int bowlerIndex = indexList[BowlerSelectionList.SelectedIndex];
            var owner = NativeWindow.GetWindow(this);
            var batterChoices = BatterSelectionList.Tag;
            if (!IsCurrentSelectorContext(match, innings, owner, batterChoices, indexList)) return;
            match.ConfirmBowler(bowlerIndex);
            if (!IsCurrentSelectorContext(match, innings, owner, batterChoices, indexList) || innings.NeedsBowlerSelection) return;
            CloseSelector(BowlerSelectionOverlay, BowlerSelectionCard, () =>
            {
                if (!IsCurrentSelectorContext(match, innings, owner, batterChoices, indexList)) return;
                if (innings.NeedsBatterSelection) ResumePendingSelection(continueSelectionFlow: true);
                else ClearSelectorReturnContext();
            });

        }

        private void AnimateSelector(Border card)
        {
            _selectorGeneration++;
            if (ReferenceEquals(card, BatterSelectionCard)) BowlerSelectionOverlay.IsVisible = false;
            else if (ReferenceEquals(card, BowlerSelectionCard)) BatterSelectionOverlay.IsVisible = false;
            card.IsHitTestVisible = true;
            card.RenderTransformOrigin = new RelativePoint(.5, .5,RelativeUnit.Relative);
            if (card.RenderTransform is not TranslateTransform) card.RenderTransform = new TranslateTransform();
            var translate = (TranslateTransform)card.RenderTransform;
            translate.BeginAnimation(TranslateTransform.YProperty, null); translate.Y = 0;
            card.BeginAnimation(OpacityProperty, null); card.Opacity = 1;
            if (!OperatorMotion.Enabled) return;
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, OperatorMotion.SurfaceOpen) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(.3, 1, OperatorMotion.SurfaceOpen) { FillBehavior = FillBehavior.Stop });
        }
        private int _selectorGeneration;
        private void CloseSelector(Border overlay, Border card, Action? next = null)
        {
            int generation = ++_selectorGeneration;
            card.IsHitTestVisible = false;
            void Complete()
            {
                if (generation != _selectorGeneration) return;
                overlay.IsVisible = false;
                if (generation != _selectorGeneration) return;
                card.BeginAnimation(OpacityProperty, null);
                if (generation != _selectorGeneration) return;
                card.Opacity = 1;
                if (generation != _selectorGeneration) return;
                card.IsHitTestVisible = true;
                if (generation != _selectorGeneration) return;
                next?.Invoke();
            }
            if (!OperatorMotion.Enabled || !overlay.IsLoaded) { Complete(); return; }
            var fade = new DoubleAnimation(0, OperatorMotion.SurfaceClose) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.HoldEnd };
            fade.Completed += (_, _) => Complete();
            card.BeginAnimation(OpacityProperty, fade);
        }
        private string? _cpBatLogo, _cpBowlLogo, _cpThemeSignature;
        private void UpdateTeamSurfaces()
        {
            if (_match is null) return;
            string signature = string.Join("|", _match.TossPending, _match.BattingTeamName, _match.BowlingTeamName, _match.TeamAPrimaryColor, _match.TeamASecondaryColor, _match.TeamBPrimaryColor, _match.TeamBSecondaryColor, _match.TeamALogoPath, _match.TeamBLogoPath);
            if (signature == _cpThemeSignature) return; _cpThemeSignature = signature;
            BattingZone.Background = TeamVisuals.Panel(_match.BattingTeamPrimaryColor, _match.BattingTeamPrimaryColor, .28);
            BowlingZone.Background = TeamVisuals.Panel(_match.BowlingTeamPrimaryColor, _match.BowlingTeamPrimaryColor, .28);
            BattingZone.BorderBrush = TeamVisuals.Accent(_match.BattingTeamPrimaryColor);
            BowlingZone.BorderBrush = TeamVisuals.Accent(_match.BowlingTeamPrimaryColor);
            BattingTeamHeading.Text = _match.BattingTeamName.ToUpperInvariant() + " · BATTING";
            BowlingTeamHeading.Text = _match.BowlingTeamName.ToUpperInvariant() + " · BOWLING";
            BattingTeamHeading.Foreground = BowlingTeamHeading.Foreground = Brushes.White;
            bool teamA = _match.BattingTeamName == _match.TeamAName;
            string? batting = teamA ? _match.TeamALogoPath : _match.TeamBLogoPath;
            string? bowling = teamA ? _match.TeamBLogoPath : _match.TeamALogoPath;
            _cpBatLogo = batting; _cpBowlLogo = bowling;
            BattingZoneLogo.Source = BowlingZoneLogo.Source = null;
            BattingZoneLogo.IsVisible = BowlingZoneLogo.IsVisible = false;
            if (BattingZone.Child is Grid battingGrid) battingGrid.Background = null;
            if (BowlingZone.Child is Grid bowlingGrid) bowlingGrid.Background = null;
        }

        // ---- Actions ----

        private void Undo_Click(object? sender, RoutedEventArgs e)
        {
            if (_deliveryEntry is not null) return;
            if (_match is { } match && match.TryUndoLastScoringAction(match.WorkflowToken, out _))
            {
                BatterSelectionOverlay.IsVisible = BowlerSelectionOverlay.IsVisible = false;
                ResumePendingSelection();
            }
            else if(_match?.LastOperationError is {Length:>0} error) _=ShowErrorAsync("Unable to undo",error);
        }

        private async void EndInnings_Click(object? sender, RoutedEventArgs e)=>await OpenInningsDecisionAsync(false);

        private CricketSummaryControl? _summaryControl;
        private string _currentPresentation = "scorebug";
        public event Action<string>? PresentationChanged;

        public void SetSummaryControl(CricketSummaryControl summary)
        {
            _summaryControl = summary;
        }

        private void ShowBattingSummary_Click(object? sender, RoutedEventArgs e)
        {
            if (_match == null) return;
            SwitchPresentation("batting");
        }

        private void ShowBattingSummary_Click(object? sender, PointerEventArgs e)
        {
            if (_match == null) return;
            SwitchPresentation("batting");
        }

        private void ShowBowlingSummary_Click(object? sender, RoutedEventArgs e)
        {
            if (_match == null) return;
            SwitchPresentation("bowling");
        }

        private void ShowBowlingSummary_Click(object? sender, PointerEventArgs e)
        {
            if (_match == null) return;
            SwitchPresentation("bowling");
        }

        // ---- Bowler settings list ----

        // ---- Display ----

        private void ShowScorebug_Click(object? sender, RoutedEventArgs e)
        {
            SwitchPresentation("scorebug");
        }

        private void ShowScorebug_Click(object? sender, PointerEventArgs e)
        {
            SwitchPresentation("scorebug");
        }

        private void ShowWindow_Click(object? sender, RoutedEventArgs e)
        {
            PresentationChanged?.Invoke("window");
        }

        private void ShowWindow_Click(object? sender, PointerEventArgs e)
        {
            PresentationChanged?.Invoke("window");
        }

        /// <summary>Switches the display window between scorebug, batting card, and bowling card permanently.</summary>
        private void SwitchPresentation(string target)
        {
            if (_match == null) return;
            if (target is not ("scorebug" or "batting" or "bowling" or "summary" or "worm" or "manhattan")) return;
            if (target == _currentPresentation) { PresentationChanged?.Invoke(target); return; }

            _overlayHost?.SetPresentation(target);

            if (target == "scorebug")
            {
                _summaryControl?.HideSummary();
                _currentPresentation = "scorebug";
            }
            else if (target == "batting")
            {
                _summaryControl?.ShowBattingSummary(_match);
                _currentPresentation = "batting";
            }
            else if (target == "bowling")
            {
                _summaryControl?.ShowBowlingSummary(_match);
                _currentPresentation = "bowling";
            }
            else if (target == "summary")
            {
                _summaryControl?.ShowMatchSummary(_match);
                _currentPresentation = "summary";
            }
            else if(target is "worm" or "manhattan")
            {
                _summaryControl?.ShowChart(_match,target);
                _currentPresentation=target;
            }
            else return;


            UpdatePresentationSelection();
            PresentationChanged?.Invoke(target);
        }

        private void PresentationButton_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string target }) return;
            if (target == "window") PresentationChanged?.Invoke("window");
            else SwitchPresentation(target);
        }

        private bool _operatorOutputVisible = true;
        internal void SetOperatorOutputVisible(bool visible)
        {
            bool changed = _operatorOutputVisible != visible;
            _operatorOutputVisible = visible;
            UpdatePresentationSelection();
            if (changed && _breakPresentationInitialized) UpdateBreakPresentations();
        }

        private void UpdatePresentationSelection()
        {
            CricketOnAirText.Text = (_operatorOutputVisible ? "ON AIR · " : "OUTPUT OFF · PREVIEW · ") + (_currentPresentation == "scorebug" ? "LIVE SCOREBUG" : _currentPresentation == "summary" ? "MATCH SUMMARY" : _currentPresentation=="worm"?"SCOREWORM":_currentPresentation=="manhattan"?"MANHATTAN":_currentPresentation.ToUpperInvariant() + " SCORECARD");
            foreach (var button in new[] { Presentationscorebug, Presentationbatting, Presentationbowling, Presentationsummary,Presentationworm,Presentationmanhattan })
            {
                if (button is null) continue;
                bool selected = (string)button.Tag == _currentPresentation;
                button.BorderBrush = new SolidColorBrush(selected ? Color.FromRgb(0x58, 0xA6, 0xFF) : Color.FromRgb(0x7A, 0x86, 0x93));
                button.Background = new SolidColorBrush(selected ? Color.FromRgb(0x16, 0x2B, 0x47) : Color.FromRgb(0x0A, 0x16, 0x28));
                Avalonia.Automation.AutomationProperties.SetItemStatus(button, selected ? "Selected" : "");
            }
        }

        /// <summary>Refreshes the current summary card if one is showing (called after score updates).</summary>
        public void RefreshSummaryIfShowing()
        {
            if (_match == null || _summaryControl == null) return;
            if (_currentPresentation == "batting")
                _summaryControl.RefreshBattingSummary(_match);
            else if (_currentPresentation == "bowling")
                _summaryControl.RefreshBowlingSummary(_match);
            else if (_currentPresentation == "summary")
                _summaryControl.RefreshMatchSummary(_match);
            else if(_currentPresentation is "worm" or "manhattan")_summaryControl.RefreshChart(_match,_currentPresentation);

        }

        // ---- Shared venue weather ----
        public event Action<string>? WeatherLocationChanged;

        private void UpdateCricketWeather_Click(object? sender, RoutedEventArgs e)
        {
            string location = (CricketWeatherLocation.Text ?? string.Empty).Trim();
            if (_match is null || string.IsNullOrWhiteSpace(location)) return;
            _match.WeatherLocation = location;
            _match.RaiseMatchChanged();
            WeatherLocationChanged?.Invoke(location);
        }

        public void UpdateWeatherSnapshot(Roche_Scoreboard.Services.WeatherSnapshot snapshot)
        {
            CricketWeatherTemperature.Text = $"{snapshot.Icon} {Math.Round(snapshot.CurrentTemp)}°";
            CricketWeatherDescription.Text = $"{snapshot.Description}\nFeels like {Math.Round(snapshot.FeelsLike)}° · {Math.Round(snapshot.DayMin)}°–{Math.Round(snapshot.DayMax)}°";
        }

        // ---- Reset ----

        private async void ResetMatch_Click(object? sender, RoutedEventArgs e)
        { if(await ChooseAsync("Reset this match?","This clears the current match and returns to sports selection. Save any scorecard changes first.",new("cancel","Keep this match",true),new("reset","Reset and set up a new match",false,true))=="reset") ResetRequested?.Invoke(); }

        // ---- Delivery log grouped by over ----

        private void OnDeliveryAdded(CricketDelivery delivery) => AppendDelivery(delivery, triggerPresentation: true);

        private void AppendDelivery(CricketDelivery delivery, bool triggerPresentation)
        {
            _loggedDeliveryCount++;
            // Start a new over group if the over number changed
            if (delivery.OverNumber != _logCurrentOver)
            {
                _logCurrentOver = delivery.OverNumber;
                _logCurrentOverPanel = new StackPanel();

                // Over header
                var header = new NativeBorder
                {
                    CornerRadius = new CornerRadius(6, 6, 0, 0),
                    Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 6, 0, 0)
                };
                var headerGrid = new Grid();
                headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var overLabel = new NativeTextBlock
                {
                    Text = $"OVER {delivery.OverNumber + 1}",
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeight.Black,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(overLabel, 0);
                headerGrid.Children.Add(overLabel);

                var bowlerLabel = new NativeTextBlock
                {
                    Tag = "overDetail",
                    Text = delivery.BowlerName ?? "",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                    FontSize = 11,
                    FontWeight = FontWeight.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 3, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetColumn(bowlerLabel, 0); Grid.SetColumnSpan(bowlerLabel, 3); Grid.SetRow(bowlerLabel, 1);
                headerGrid.Children.Add(bowlerLabel);

                var scoreLabel = new NativeTextBlock
                {
                    Tag = "overScore",
                    Text = $"{delivery.TotalAfter}/{delivery.WicketsAfter}",
                    Foreground = Brushes.White,
                    FontSize = 15,
                    FontWeight = FontWeight.Bold,
                    FontFamily = NativeFonts.Family("Bahnschrift"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(scoreLabel, 2);
                headerGrid.Children.Add(scoreLabel);

                header.Child = headerGrid;

                var overGroup = new StackPanel { Tag = delivery.OverNumber };
                overGroup.Children.Add(header);
                overGroup.Children.Add(_logCurrentOverPanel);

                EventLog.Children.Insert(0, overGroup);
            }

            // Update the over score in the header to latest
            if (_logCurrentOverPanel?.Parent is StackPanel group
                && group.Children[0] is Border hdr
                && hdr.Child is Grid hdrGrid)
            {
                foreach (Control child in hdrGrid.Children)
                {
                    if (child is TextBlock tb && tb.Tag is string tag && tag == "overScore")
                    {
                        tb.Text = $"{delivery.TotalAfter}/{delivery.WicketsAfter}";
                    }
                    if (child is TextBlock detail && Equals(detail.Tag, "overDetail"))
                    {
                        var overBalls = _match?.CurrentInnings?.Deliveries.Where(ball => ball.OverNumber == delivery.OverNumber).ToArray() ?? [];
                        int runs = overBalls.Sum(ball => ball.Runs), wickets = overBalls.Count(ball => ball.Type == CricketDeliveryType.Wicket);
                        detail.Text = $"{delivery.BowlerName} · {runs} {(runs == 1 ? "run" : "runs")} · {wickets} {(wickets == 1 ? "wicket" : "wickets")}";
                    }
                }
            }

            // Add delivery bar into the current over group
            var bar = CreateDeliveryBar(delivery);
            _logCurrentOverPanel?.Children.Insert(0,bar);
            if (triggerPresentation && NativeSystemPreferences.ClientAreaAnimation)
            {
                var slide = new TranslateTransform(); bar.RenderTransform = slide;
                slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
                bar.BeginAnimation(OpacityProperty, new DoubleAnimation(.15, 1, TimeSpan.FromMilliseconds(260)) { FillBehavior = FillBehavior.Stop });
            }
            if (!_rebuildingLog) RefreshOverChoices();
        }

        private Border CreateDeliveryBar(CricketDelivery delivery)
        {
            Color badgeColour = delivery.Type switch
            {
                CricketDeliveryType.Wicket => Color.FromRgb(185, 28, 28),
                CricketDeliveryType.Four => Color.FromRgb(21, 128, 61),
                CricketDeliveryType.Six => Color.FromRgb(86, 51, 170),
                CricketDeliveryType.Wide or CricketDeliveryType.NoBall => Color.FromRgb(217, 119, 6),
                _ => Color.FromRgb(51, 65, 85)
            };
            string symbol = delivery.Type switch
            {
                CricketDeliveryType.Wicket => "W",
                CricketDeliveryType.Four => "4",
                CricketDeliveryType.Six => "6",
                CricketDeliveryType.Wide => "Wd",
                CricketDeliveryType.NoBall => "Nb",
                CricketDeliveryType.Bye => "B",
                CricketDeliveryType.LegBye => "Lb",
                CricketDeliveryType.Dot => "•",
                _ => delivery.Runs.ToString()
            };
            var border = new NativeBorder
            {
                Tag = delivery,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 0, 3),
                Padding = new Thickness(7, 4, 7, 4),
                BorderThickness = new Thickness(1),
                BorderBrush = TeamVisuals.Accent(_match?.BattingTeamPrimaryColor),
                Background = TeamVisuals.Panel(_match?.BattingTeamPrimaryColor, _match?.BattingTeamPrimaryColor, .2),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Focusable = true,
                ToolTip = $"Edit ball · {delivery.BatsmanName} facing {delivery.BowlerName}"
            };
            var body = new StackPanel();
            var header = new Grid(); header.ColumnDefinitions.Add(new() { Width = new GridLength(34) }); header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var code = new NativeBorder
            {
                Width = 28,
                Height = 24,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(badgeColour),
                Child = new NativeTextBlock { Text = symbol, FontSize = 12, FontWeight = FontWeight.Bold, Foreground = ContrastHelper.GetContrastBrush(badgeColour), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            header.Children.Add(code);
            var description = new NativeTextBlock { Text = delivery.Description, FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 8, 0) };
            Grid.SetColumn(description, 1); header.Children.Add(description);
            bool legal = CricketOverlayAnalysis.IsLegal(delivery);
            int ballNumber = legal && delivery.BallInOver > 0 ? delivery.BallInOver : (delivery.StateBefore?.LegalBallsBowled ?? delivery.OverNumber * 6) % 6 + 1;
            var total = new NativeTextBlock
            {
                Tag = "ball-number",
                Text = $"{delivery.OverNumber}.{ballNumber}",
                FontSize = 15,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = legal ? "Over.ball" : "Extra delivery — the same ball number is used again"
            };
            Grid.SetColumn(total, 2); header.Children.Add(total); body.Children.Add(header);
            var actionContent = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
            actionContent.Children.Add(new NativeTextBlock { Text = "\uE70F", FontFamily = NativeFonts.Family("Segoe MDL2 Assets"), FontSize = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            actionContent.Children.Add(new NativeTextBlock { Text = "Edit ball", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var editAction = new NativeButton
            {
                Tag = "edit-ball",
                Content = actionContent,
                Theme = NativeResources.Require<Avalonia.Styling.ControlTheme>(this, "ToolBtn"),
                Height = 24,
                FontSize = 12,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            Avalonia.Automation.AutomationProperties.SetName(editAction, "Edit ball");
            editAction.Click += (_, args) => { args.Handled = true; OpenDeliveryDialog(delivery.Type, delivery); };
            var reveal = new NativeBorder { Height = 0, ClipToBounds = true, Child = editAction }; body.Children.Add(reveal); border.Child = body;
            void Reveal(bool show)
            {
                double from = reveal.NativeHeight(); reveal.BeginAnimation(HeightProperty, null); reveal.Height = show ? 28 : 0;
                if (NativeSystemPreferences.ClientAreaAnimation) reveal.BeginAnimation(HeightProperty, new DoubleAnimation(from, reveal.Height, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                editAction.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 0 : 1, show ? 1 : 0, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.HoldEnd });
            }
            var rest = border.BorderBrush;
            border.PointerEntered += (_, _) => { Reveal(true); border.BorderBrush = new SolidColorBrush(Color.FromRgb(121, 185, 255)); };
            border.PointerExited += (_, _) => { if (!border.IsKeyboardFocusWithin) Reveal(false); border.BorderBrush = rest; };
            border.GotFocus += (_, _) => Reveal(true); border.LostFocus += (_, _) => { if (!border.IsKeyboardFocusWithin && !border.IsPointerOver) Reveal(false); };
            border.KeyDown += (_, args) => { if (args.Source is not Button && args.Key is Key.Enter or Key.Space) { args.Handled = true; OpenDeliveryDialog(delivery.Type, delivery); } };
            border.PointerReleased += (_, args) => { args.Handled = true; OpenDeliveryDialog(delivery.Type, delivery); };
            return border;
        }

        private int _loggedRevision = -1;
        private bool _rebuildingLog, _refreshingOverChoices;
        private int[] _overChoices = [];
        private sealed record OverChoice(int Over)
        {
            public override string ToString() => Over < 0 ? "All overs · latest first" : $"Over {Over + 1}";
        }
        private void RefreshOverChoices()
        {
            int selected = (DeliveryOverChoice.SelectedItem as OverChoice)?.Over ?? -1;
            int[] overs = _match?.CurrentInnings?.Deliveries.Select(delivery => delivery.OverNumber).Distinct().OrderDescending().ToArray() ?? [];
            if (!overs.SequenceEqual(_overChoices) || DeliveryOverChoice.Items.Count == 0)
            {
                _overChoices = overs; _refreshingOverChoices = true;
                DeliveryOverChoice.Items.Clear(); DeliveryOverChoice.Items.Add(new OverChoice(-1));
                foreach (int over in overs) DeliveryOverChoice.Items.Add(new OverChoice(over));
                DeliveryOverChoice.SelectedIndex = Math.Max(0, Array.IndexOf(overs, selected) + 1);
                _refreshingOverChoices = false;
            }
            FilterDeliveryOvers();
        }
        private void FilterDeliveryOvers()
        {
            int over = (DeliveryOverChoice.SelectedItem as OverChoice)?.Over ?? -1;
            foreach (StackPanel group in EventLog.Children.OfType<StackPanel>())
                group.IsVisible = group.Tag is not int groupOver || over < 0 || groupOver == over ? true : false;
        }
        private void DeliveryOverChoice_Changed(object? sender, SelectionChangedEventArgs args)
        {
            if (_refreshingOverChoices || EventLog is null) return;
            FilterDeliveryOvers(); DeliveryLogScroller.ScrollToHome();
        }
        private void RebuildDeliveryLog()
        {
            _rebuildingLog = true;
            EventLog.Children.Clear();
            _logCurrentOver = -1;
            _logCurrentOverPanel = null;
            _loggedDeliveryCount = 0;
            _loggedInnings = _match?.CurrentInnings;
            _loggedRevision = _match?.DeliveryRevision ?? -1;
            var deliveries = _loggedInnings?.Deliveries;
            if (deliveries != null)
                foreach (CricketDelivery delivery in deliveries) AppendDelivery(delivery, triggerPresentation: false);
            RenderNonDeliveryHistory(_loggedInnings);
            _rebuildingLog = false; RefreshOverChoices();
        }

        // ---- UI updates ----

        private void RefreshPlayerCardOpacity()
        {
            StrikerBar.Opacity = StrikerBar.IsEnabled ? 1 : .45;
            NonStrikerBar.Opacity = NonStrikerBar.IsEnabled ? 1 : .45;
            BowlerBar.Opacity = BowlerBar.IsEnabled ? 1 : .45;
        }

        private void UpdateUI()
        {
            ClosePlayerMenu();
            if (_match == null) return;
            var match = _match;
            var inn = match.CurrentInnings;
            if (inn == null) return;

            if (!ReferenceEquals(inn, _loggedInnings) || _loggedDeliveryCount != inn.Deliveries.Count || _loggedRevision != _match.DeliveryRevision)
                RebuildDeliveryLog();

            bool waiting = _match.TossPending;
            StartTossButton.IsVisible = waiting ? true : false;
            ScoringCommands.IsVisible = ExtraCommands.IsVisible = waiting ? false : true;
            bool battingReady = inn.Striker is { IsOut: false } && inn.NonStriker is { IsOut: false } && inn.StrikerIndex != inn.NonStrikerIndex;
            bool bowlingReady = inn.CurrentBowler is not null;
            bool battingNeeded = (inn.NeedsBatterSelection || !battingReady) && !inn.IsComplete;
            bool bowlingNeeded = (inn.NeedsBowlerSelection || !bowlingReady) && !inn.IsComplete;
            if (!CloseResolvedSelections(battingNeeded, bowlingNeeded, match, inn)) return;
            BattingStartHint.IsVisible = waiting || battingNeeded ? true : false;
            BowlingStartHint.IsVisible = waiting || bowlingNeeded ? true : false;
            BattingStartHint.Text = waiting ? "Record the toss before selecting batters." : "Select the batters when you are ready.";
            BowlingStartHint.Text = waiting ? "Record the toss before selecting a bowler." : "Select the bowler when you are ready.";
            SelectOpeningBatters.IsVisible = battingNeeded && !waiting ? true : false;
            SelectOpeningBowler.IsVisible = bowlingNeeded && !waiting ? true : false;
            StrikerBar.IsEnabled = !waiting && inn.Striker is not null;
            NonStrikerBar.IsEnabled = !waiting && inn.NonStriker is not null;
            BowlerBar.IsEnabled = !waiting && inn.CurrentBowler is not null;
            RefreshPlayerCardOpacity();
            PresentationsbattingGuard(waiting);
            UpdateTeamSurfaces();
            OperatorFreeHitText.IsVisible=_match.FreeHitPending && !waiting ? true : false;
            EndInningsButton.IsEnabled = !waiting;
            ScoringCommands.IsEnabled = ExtraCommands.IsEnabled = !waiting && battingReady && bowlingReady && !inn.NeedsBatterSelection && !inn.NeedsBowlerSelection && !inn.IsComplete;
            ScoringCommands.Opacity = ExtraCommands.Opacity = ScoringCommands.IsEnabled ? 1 : .38;
            Avalonia.Controls.ToolTip.SetTip(ScoringCommands, ScoringCommands.IsEnabled ? null : "Select both batters and a bowler before scoring.");
            ScoringLockBanner.IsVisible = !waiting && !inn.IsComplete && !ScoringCommands.IsEnabled ? true : false;
            ScoringReadyHint.Text = battingNeeded && bowlingNeeded ? "Select batters and bowler →"
                : battingNeeded ? "Select next batter →" : bowlingNeeded ? "Select next bowler →" : "";
            if (waiting) { BattingTeamHeading.Text = _match.TeamAName + " · TEAM A"; BowlingTeamHeading.Text = _match.TeamBName + " · TEAM B"; }
            UndoDeliveryButton.IsEnabled = _match.CanUndoLastScoringAction;
            Avalonia.Controls.ToolTip.SetTip(UndoDeliveryButton, _match.CanUndoNonDeliveryRunOut ? "Undo the run-out before delivery" : UndoDeliveryButton.IsEnabled ? "Undo the last delivery exactly" : _match.IsMatchFinalized ? "Reopen the match or use scorecard corrections" : inn.ScorecardAnchor >= 0 && inn.Deliveries.Count <= inn.ScorecardAnchor ? "Revert the latest scorecard adjustment before undoing earlier balls" : inn.Deliveries.Count == 0 ? "No scoring action to undo" : "This imported ball has no recovery snapshot. Use the delivery editor instead.");
            // Header
            InningsLabel.Text = waiting ? "Toss pending" : _match.CurrentInningsNumber switch
            {
                1 => "1st Innings",
                2 => "2nd Innings",
                3 => "3rd Innings",
                _ => $"{_match.CurrentInningsNumber}th Innings"
            };
            HeaderScore.Text = waiting ? "—" : $"{inn.TotalWickets}/{inn.TotalRuns}";
            HeaderOvers.Text = waiting ? "NOT STARTED" : $"Over {inn.OversDisplay}";
            SettingsMatchText.Text = $"{_match.Format.ToString().ToUpperInvariant()}  •  {(_match.Format == CricketFormat.LimitedOvers ? $"{_match.CurrentOverLimit} OVERS" : "MULTI INNINGS")}";
            SettingsStateText.Text = $"INNINGS {_match.CurrentInningsNumber}  •  {inn.TotalWickets}/{inn.TotalRuns} ({inn.OversDisplay})";
            SettingsSelectionText.Text = _match.IsMatchFinalized ? (_match.MatchResult ?? "Match ended") + (string.IsNullOrWhiteSpace(_match.ResultNote) ? "" : " · " + _match.ResultNote) : inn.IsComplete
                ? (_match.MatchResult ?? "Innings complete — advance when ready.")
                : inn.NeedsBatterSelection
                    ? "Scoring is safely paused until the next batter is selected."
                    : inn.NeedsBowlerSelection
                        ? "Scoring is safely paused until the next bowler is selected."
                        : "Scoring is live. Undo restores the exact state before the last delivery.";

            // Batter display — keep rows stable, only move the indicator
            int si = inn.StrikerIndex;
            int nsi = inn.NonStrikerIndex;

            if (_cpTopBatterIndex < 0 || _cpBottomBatterIndex < 0)
            {
                _cpTopBatterIndex = si;
                _cpBottomBatterIndex = nsi;
            }
            else
            {
                bool topStillActive = _cpTopBatterIndex == si || _cpTopBatterIndex == nsi;
                bool bottomStillActive = _cpBottomBatterIndex == si || _cpBottomBatterIndex == nsi;

                if (!topStillActive && bottomStillActive)
                    _cpTopBatterIndex = (_cpBottomBatterIndex == si) ? nsi : si;
                else if (topStillActive && !bottomStillActive)
                    _cpBottomBatterIndex = (_cpTopBatterIndex == si) ? nsi : si;
                else if (!topStillActive && !bottomStillActive)
                {
                    _cpTopBatterIndex = si;
                    _cpBottomBatterIndex = nsi;
                }
            }

            var topPlayer = (_cpTopBatterIndex >= 0 && _cpTopBatterIndex < inn.BattingOrder.Count)
                ? inn.BattingOrder[_cpTopBatterIndex] : null;
            var bottomPlayer = (_cpBottomBatterIndex >= 0 && _cpBottomBatterIndex < inn.BattingOrder.Count)
                ? inn.BattingOrder[_cpBottomBatterIndex] : null;

            bool topIsStriker = _cpTopBatterIndex == si;
            bool topGreyOut = topPlayer?.IsOut == true;
            bool bottomGreyOut = bottomPlayer?.IsOut == true;

            // Top batter bar (uses Striker* controls)
            if (topPlayer != null)
            {
                StrikerName.Text = topPlayer.DisplayName;
                StrikerName.Foreground = topGreyOut ? Brushes.Gray : Brushes.White;
                StrikerStats.Text = $"SR {topPlayer.StrikeRate:F1}";
                StrikerBallsFaced.Text = $"({topPlayer.BallsFaced})";
                StrikerRuns.Text = topPlayer.Runs.ToString();
                StrikerRuns.Foreground = StrikerBallsFaced.Foreground = StrikerName.Foreground;
                TopBatterIndicator.Text = topGreyOut ? "" : topIsStriker ? "▶" : "  ";
            }
            else
            {
                StrikerName.Text = "—";
                StrikerStats.Text = ""; StrikerBallsFaced.Text = "";
                StrikerRuns.Text = "0";
                TopBatterIndicator.Text = "  ";
            }

            // Bottom batter bar (uses NonStriker* controls)
            if (bottomPlayer != null)
            {
                NonStrikerName.Text = bottomPlayer.DisplayName;
                NonStrikerName.Foreground = bottomGreyOut ? Brushes.Gray : Brushes.White;
                NonStrikerStats.Text = $"SR {bottomPlayer.StrikeRate:F1}";
                NonStrikerBallsFaced.Text = $"({bottomPlayer.BallsFaced})";
                NonStrikerRunsText.Text = bottomPlayer.Runs.ToString();
                NonStrikerRunsText.Foreground = NonStrikerBallsFaced.Foreground = NonStrikerName.Foreground;
                BottomBatterIndicator.Text = bottomGreyOut ? "" : !topIsStriker ? "▶" : "  ";
            }
            else
            {
                NonStrikerName.Text = "—";
                NonStrikerStats.Text = ""; NonStrikerBallsFaced.Text = "";
                NonStrikerRunsText.Text = "0";
                BottomBatterIndicator.Text = "  ";
            }

            // Bowler bar
            var bowler = inn.CurrentBowler;
            if (bowler != null)
            {
                BowlerName.Text = bowler.DisplayName;
                BowlerStats.Text = $"Economy {bowler.Economy:F2}";
                BowlerOverFigures.Text = $"({bowler.OversDisplay})";
                BowlerFigures.Text = bowler.BowlingFigures;
            }
            else
            {
                BowlerName.Text = "—";
                BowlerStats.Text = ""; BowlerOverFigures.Text = "";
                BowlerFigures.Text = "0-0";
            }

            if (!UpdatePlayerMenuNames()) return;

            OverDisplay.Text = $"This over: {string.Join("  ", inn.CurrentOverBalls)}";

            _scorebug?.UpdateFromMatch(_match);
            RefreshSummaryIfShowing();
            UpdateWorkflowState();
            UpdateBreakPresentations();
        }
    }
}

namespace Roche_Scoreboard.Views
{
public partial class CricketControlPanel
{
    private void Native_AutoOverlays_Changed(object? sender, RoutedEventArgs e) { AutoOverlays_Changed(sender!, e); }
    private void Native_CricketPresentationPreference_Changed(object? sender, RoutedEventArgs e) { CricketPresentationPreference_Changed(sender!, e); }
    private void Native_DeliveryOverChoice_Changed(object? sender, SelectionChangedEventArgs e) { DeliveryOverChoice_Changed(sender!, e); }
    private void Native_MainTabControl_SelectionChanged(object? sender, SelectionChangedEventArgs e) { MainTabControl_SelectionChanged(sender!, e); }
    private void Native_TossChoice_Checked_Checked(object? sender, RoutedEventArgs e) { if (sender is Avalonia.Controls.Primitives.ToggleButton toggle && toggle.IsChecked != true) return; TossChoice_Checked(sender!, e); }
    private void Native_Toss_PreviewKeyDown(object? sender, KeyEventArgs e) { Toss_PreviewKeyDown(sender!, e); }
}
}

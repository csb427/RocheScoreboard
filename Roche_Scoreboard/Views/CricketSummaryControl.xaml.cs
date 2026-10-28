using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using UserControl = System.Windows.Controls.UserControl;

namespace Roche_Scoreboard.Views
{
    public partial class CricketSummaryControl : UserControl
    {
        private int _transitionGeneration;
        private System.Windows.Controls.Image? _presentationGhost;
        private ImageSource? _previousPresentation;
        private void PreparePresentationChange()
        {
            _previousPresentation = null;
            if (!IsVisible || !OperatorMotion.Enabled || !OverlayMotionEnabled || RootGrid.ActualWidth < 1 || RootGrid.ActualHeight < 1) return;
            // Capture the current composition, including an interrupted outgoing
            // frame, before replacing any rows. The incoming screen stays opaque.
            var frame = new RenderTargetBitmap((int)Math.Ceiling(RootGrid.ActualWidth), (int)Math.Ceiling(RootGrid.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            frame.Render(RootGrid); frame.Freeze(); _previousPresentation = frame;
        }
        private void ClearPresentationGhost()
        {
            if (_presentationGhost is null) return;
            _presentationGhost.BeginAnimation(OpacityProperty, null);
            RootGrid.Children.Remove(_presentationGhost); _presentationGhost = null;
        }
        private bool _overlayCompact;
        internal bool OverlayMotionEnabled {get;set;}=true;
        public void SetOverlayCompact(bool compact)
        {
            if(_overlayCompact==compact) return;
            _overlayCompact=compact;
            double old=RootGrid.RowDefinitions[0].Height.Value;
            double target=compact ? RootGrid.Height*CricketBroadcastStyle.CompactTopFraction : 72;
            RootGrid.RowDefinitions[0].Height=new GridLength(target);
            SummaryHeaderPanel.Opacity=compact ? 0 : 1;
            foreach(var panel in new FrameworkElement[] { SummaryColumnPanel,RowsPanel })
            {
                var translate=new TranslateTransform(); panel.RenderTransform=translate;
                if(OverlayMotionEnabled && SystemParameters.ClientAreaAnimation)
                    translate.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(old-target,0,CricketBroadcastStyle.OverlayMotionDuration) { FillBehavior=FillBehavior.Stop,EasingFunction=CricketBroadcastStyle.OverlayMotionEase() });
            }
            _pageIndex=0;
            if(_pagedMatch?.CurrentInnings is CricketInnings inn)
            { if(_pagedMode=="batting") PopulateBatting(_pagedMatch,inn); else if(_pagedMode=="bowling") PopulateBowling(_pagedMatch,inn); else if(_pagedMode=="summary") PopulateMatchSummary(_pagedMatch,inn); }
        }
        private (int Start,int Count) ActivePageRange(int count)
        {
            if(!_overlayCompact) return PageRange(count,_pageIndex);
            int start=(_pageIndex%Math.Max(1,(count+2)/3))*3;
            return (start,Math.Min(3,count-start));
        }
        private CricketMatchManager? _pagedMatch;
        private string _pagedMode = "";
        private int _pageIndex;
        private int _pageSeconds;
        private int _pagedInningsNumber;
        private readonly System.Windows.Threading.DispatcherTimer _pageTimer = new() { Interval = TimeSpan.FromSeconds(1) };

        private void SetPagedMode(CricketMatchManager match, string mode)
        {
            if (!ReferenceEquals(_pagedMatch, match) || _pagedMode != mode || _pagedInningsNumber != match.CurrentInningsNumber)
            {
                _pageIndex = 0;
                _pageSeconds = 0;
            }
            _pagedMatch = match;
            _pagedMode = mode;
            _pagedInningsNumber = match.CurrentInningsNumber;
        }

        private void TickSummary()
        {
            SummaryClock.Text = BroadcastClock.WallTime();
            if (Visibility != Visibility.Visible || _pagedMatch?.CurrentInnings is not CricketInnings inn) return;
            if (_pagedMode == "summary") return;
            if (++_pageSeconds < 18) return;
            _pageSeconds = 0;
            if(_pagedMode is "worm" or "manhattan") { _pageIndex++;PopulateChart(_pagedMatch,_pagedMode);return; }
            int count = _pagedMode == "batting" ? inn.BattingOrder.Count : BowledPlayers(inn).Count;
            int pageSize=_overlayCompact ? 3 : 6;
            if (count <= pageSize) return;
            _pageIndex = (_pageIndex + 1) % Math.Max(1, (count + pageSize-1) / pageSize);
            if (_pagedMode == "batting") PopulateBatting(_pagedMatch, inn);
            else if (_pagedMode == "bowling") PopulateBowling(_pagedMatch, inn);
        }

        public CricketSummaryControl()
        {
            InitializeComponent();
            _pageTimer.Tick += (_, __) => TickSummary();
            Loaded += (_, __) => { if (IsVisible) _pageTimer.Start(); };
            IsVisibleChanged += (_, __) => { if (IsVisible) _pageTimer.Start(); else _pageTimer.Stop(); };
            Unloaded += (_, __) => { _pageTimer.Stop(); ++_transitionGeneration; ClearPresentationGhost(); _previousPresentation = null; };
            SizeChanged += (_, e) => { BroadcastLayout.FitCanvas(RootGrid,e.NewSize.Width,e.NewSize.Height); if(_overlayCompact) RootGrid.RowDefinitions[0].Height=new GridLength(RootGrid.Height*.27); };
        }

        public void ShowBattingSummary(CricketMatchManager match)
        {
            var inn = match.CurrentInnings;
            if (inn == null) return;
            PreparePresentationChange();
            SetPagedMode(match, "batting");
            PopulateBatting(match, inn);
            ShowSummary();
        }

        public void ShowBowlingSummary(CricketMatchManager match)
        {
            var inn = match.CurrentInnings;
            if (inn == null) return;
            PreparePresentationChange();
            SetPagedMode(match, "bowling");
            PopulateBowling(match, inn);
            ShowSummary();
        }

        /// <summary>Refresh batting data without transition animation.</summary>
        public void RefreshBattingSummary(CricketMatchManager match)
        {
            var inn = match.CurrentInnings;
            if (inn == null || Visibility != Visibility.Visible) return;
            SetPagedMode(match, "batting");
            PopulateBatting(match, inn);
        }

        /// <summary>Refresh bowling data without transition animation.</summary>
        public void RefreshBowlingSummary(CricketMatchManager match)
        {
            var inn = match.CurrentInnings;
            if (inn == null || Visibility != Visibility.Visible) return;
            SetPagedMode(match, "bowling");
            PopulateBowling(match, inn);
        }

        internal static (int Start, int Count) PageRange(int count, int page)
        {
            if (count <= 6) return (0, count);
            // Eleven-player squads follow the requested five + six split.
            if (count <= 11) return page % 2 == 0 ? (0, 5) : (5, count - 5);
            int start = (page % ((count + 5) / 6)) * 6;
            return (start, Math.Min(6, count - start));
        }

        private void SummaryHeader(CricketMatchManager match, string mode, string name, string colour)
        {
            SummaryColumnPanel.Visibility=Visibility.Visible;
            RootGrid.Background=new SolidColorBrush(Color.FromRgb(0x0B,0x11,0x20)); RowsPanel.Background=null;
            RootGrid.RowDefinitions[1].Height = new GridLength(44);
            RowsPanel.Tag = null;
            TrySetColor(HeaderBg, colour);
            string secondary = mode == "BOWLING" ? match.BowlingTeamSecondaryColor : match.BattingTeamSecondaryColor;
            SummaryHeaderPanel.Background = SummaryColumnPanel.Background = CricketBroadcastStyle.Gradient(colour, secondary);
            SetSummaryLogo(match, mode == "BOWLING");
            if(SummaryHeaderLogo.Parent is Grid headerSurface) headerSurface.Background=CricketBroadcastStyle.CenterLogo(SummaryHeaderLogo.Source,.18);
            TrySetColor(SeparatorBg, colour);
            var foreground = System.Windows.Media.Brushes.White;
            SummaryHeaderLogo.Opacity = .18;
            HeaderSheen.Opacity = 0;
            HeaderShimmer.Opacity = 0;
            HeaderText.Foreground = SummaryClock.Foreground = foreground;
            string ordinal = match.CurrentInningsNumber switch { 1 => "1ST", 2 => "2ND", 3 => "3RD", _ => "4TH" };
            HeaderText.Text = $"{name.ToUpper()} {mode} | {ordinal} INNINGS";
            SummaryClock.Text = BroadcastClock.WallTime();
            SummaryColumns.Children.Clear();
            SummaryColumns.ColumnDefinitions.Clear();
            SummaryColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            SummaryColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bool bowling = mode == "BOWLING";
            if (!bowling) SummaryColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            string[] headings = bowling ? ["O", "M", "R", "W", "ECON"] : ["RUNS", "BALLS"];
            for (int i = 0; i < headings.Length; i++)
            {
                SummaryColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(bowling ? 100 : 125) });
                var label = new TextBlock { Text = headings[i], FontFamily = CricketBroadcastStyle.Typeface, FontSize = 32, FontWeight = FontWeights.Bold, Foreground = foreground, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(label, (bowling ? 2 : 3) + i);
                SummaryColumns.Children.Add(label);
            }
        }

        private void PopulateBatting(CricketMatchManager match, CricketInnings inn)
        {
            SummaryHeader(match, "BATTING", match.BattingTeamName, match.BattingTeamPrimaryColor);
            RowsPanel.Children.Clear();
            var ordered=CricketPresentationOrder.Batters(inn);
            var range = ActivePageRange(ordered.Count);
            RowsPanel.Rows = Math.Max(1, range.Count);
            for (int i = range.Start; i < range.Start + range.Count; i++)
            {
                CricketPlayer p = ordered[i].Player;
                bool active = ordered[i].Index == inn.StrikerIndex || ordered[i].Index == inn.NonStrikerIndex;
                RowsPanel.Children.Add(CreateBroadcastPlayerRow(i + 1, p, false, active, match.BattingTeamPrimaryColor, match.BattingTeamSecondaryColor));
            }
            PopulateFooter(match, inn);
        }

        private void PopulateBowling(CricketMatchManager match, CricketInnings inn)
        {
            SummaryHeader(match, "BOWLING", match.BowlingTeamName, match.BowlingTeamPrimaryColor);
            RowsPanel.Children.Clear();
            var players = BowledPlayers(inn);
            var range = ActivePageRange(players.Count);
            RowsPanel.Rows = players.Count == 0 ? 1 : Math.Max(_overlayCompact ? 3 : 5, range.Count);
            for (int i = range.Start; i < range.Start + range.Count; i++)
                RowsPanel.Children.Add(CreateBroadcastPlayerRow(i + 1, players[i].Player, true,
                    players[i].Index == inn.CurrentBowlerIndex, match.BowlingTeamPrimaryColor, match.BowlingTeamSecondaryColor));
            if (players.Count == 0) RowsPanel.Children.Add(new TextBlock { Text = "NO DELIVERIES BOWLED", FontSize = 46, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            PopulateFooter(match, inn);
        }

        private static Border CreateBroadcastPlayerRow(int order, CricketPlayer player, bool bowling, bool active, string colour, string secondary)
        {
            Color team = ParseColor(colour, Colors.Navy);
            bool filled = bowling ? active : !player.IsOut && (active || player.BallsFaced > 0 || player.Runs > 0);
            System.Windows.Media.Brush foreground = System.Windows.Media.Brushes.White;
            var grid = new Grid { Margin = new Thickness(12, 0, 12, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (!bowling) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            string[] values = bowling
                ? [player.OversDisplay, player.Maidens.ToString(), player.RunsConceded.ToString(), player.Wickets.ToString(), player.Economy.ToString("0.0")]
                : [player.Runs.ToString(), player.BallsFaced.ToString()];
            foreach (string _ in values) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(bowling ? 100 : 125) });
            void Cell(string text, int col, double size, HorizontalAlignment align = HorizontalAlignment.Center)
            {
                var cell = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.Bold,
                    FontFamily = CricketBroadcastStyle.Typeface, Foreground = foreground,
                    VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = align, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(cell, col); grid.Children.Add(cell);
            }
            Cell($"{order}.", 0, 38);
            Cell(player.DisplayName.ToUpper(), 1, bowling ? 38 : 44, HorizontalAlignment.Stretch);
            if (!bowling)
            {
                string dismissal = player.IsOut ? player.DismissalSummary.Replace(" b:", "\nb:") : active ? "NOT OUT" : player.BallsFaced > 0 ? "NOT OUT" : "YET TO BAT";
                Cell(dismissal, 2, 25, HorizontalAlignment.Stretch);
            }
            for (int i = 0; i < values.Length; i++)
            {
                var stack = new Grid();
                var number = new TextBlock { Text = values[i], FontSize = bowling ? 52 : i == 0 ? 50 : 38,
                    Foreground = foreground, FontFamily = CricketBroadcastStyle.Typeface, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(new Viewbox { Child=number,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,Margin=new Thickness(2,0,2,0) });
                Grid.SetColumn(stack, (bowling ? 2 : 3) + i); grid.Children.Add(stack);
            }
            return new Border { Child = grid, Background = filled ? CricketBroadcastStyle.Gradient(colour, secondary) : TeamVisuals.Panel(colour, colour, .18),
                BorderBrush = new SolidColorBrush(team), BorderThickness = new Thickness(2), Margin = new Thickness(0, 4, 0, 4) };
        }

        internal static List<(int Index, CricketPlayer Player)> BowledPlayers(CricketInnings innings) =>CricketPresentationOrder.Bowlers(innings);

        private string? _summaryLogoPath;
        private void SetSummaryLogo(CricketMatchManager match, bool bowling)
        {
            bool teamA = (bowling ? match.BowlingTeamName : match.BattingTeamName) == match.TeamAName;
            string? path = teamA ? match.TeamALogoPath : match.TeamBLogoPath;
            if (_summaryLogoPath == path) return;
            _summaryLogoPath = path;
            SummaryHeaderLogo.Source = TeamVisuals.LoadLogo(path);
            SummaryHeaderLogo.Visibility=Visibility.Collapsed;
            if(SummaryHeaderLogo.Parent is Grid surface) surface.Background=CricketBroadcastStyle.CenterLogo(SummaryHeaderLogo.Source,.18);
        }

        private void PopulateFooter(CricketMatchManager match, CricketInnings inn)
        {
            FooterTeamName.Text = match.BattingTeamName.ToUpper();
            FooterTeamScore.Text = $"{inn.TotalWickets}/{inn.TotalRuns}";
            FooterOvers.Text = inn.OversDisplay;

            if (match.Target != null)
            {
                FooterStatLabel.Text = "TARGET";
                FooterStatValue.Text = match.Target.Value.ToString();
            }
            else
            {
                FooterStatLabel.Text = "RUN RATE";
                FooterStatValue.Text = inn.RunRate.ToString("F2");
            }

            if (match.RequiredRunRate != null)
            {
                FooterStat2Label.Text = "REQ. RATE";
                FooterStat2Value.Text = match.RequiredRunRate.Value.ToString("F2");
            }
            else
            {
                FooterStat2Label.Text = "P'SHIP";
                FooterStat2Value.Text = $"{inn.PartnershipRuns}({inn.PartnershipBalls})";
            }
        }




        private void ShowSummary()
        {
            int generation = ++_transitionGeneration;
            bool replacing = IsVisible;
            ClearPresentationGhost();
            BeginAnimation(OpacityProperty, null);
            RootTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            RootTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            RootTranslate.X = RootTranslate.Y = 0;
            Visibility = Visibility.Visible;
            Opacity = 1;
            if (_previousPresentation is not null && OperatorMotion.Enabled && OverlayMotionEnabled)
            {
                var ghost = new System.Windows.Controls.Image { Source = _previousPresentation, Stretch = Stretch.Fill, IsHitTestVisible = false, Tag = "presentation-outgoing-frame" };
                _previousPresentation = null; _presentationGhost = ghost;
                System.Windows.Controls.Grid.SetRowSpan(ghost, 4); RootGrid.Children.Add(ghost);
                var dissolve = new DoubleAnimation(1, 0, BroadcastMotion.PresentationDuration) { FillBehavior = FillBehavior.Stop, EasingFunction = BroadcastMotion.CreateOverlayEase() };
                dissolve.Completed += (_, _) => { if (generation == _transitionGeneration) ClearPresentationGhost(); };
                ghost.BeginAnimation(OpacityProperty, dissolve);
                return;
            }
            _previousPresentation = null;
            if (replacing || !OperatorMotion.Enabled || !OverlayMotionEnabled) return;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, BroadcastMotion.PresentationDuration)
            { FillBehavior = FillBehavior.Stop, EasingFunction = BroadcastMotion.CreateOverlayEase() });
            RootTranslate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(14, 0, BroadcastMotion.PresentationDuration)
                { FillBehavior = FillBehavior.Stop, EasingFunction = BroadcastMotion.CreateOverlayEase() });
        }

        public void HideSummary()
        {
            int generation = ++_transitionGeneration;
            double opacity = Opacity;
            BeginAnimation(OpacityProperty, null);
            RootTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            RootTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            RootTranslate.X = RootTranslate.Y = 0;
            Opacity = 0;
            _previousPresentation = null;
            if (!OperatorMotion.Enabled || !OverlayMotionEnabled)
            { ClearPresentationGhost(); Visibility = Visibility.Collapsed; Opacity = 1; return; }
            var fadeOut = new DoubleAnimation(opacity, 0, BroadcastMotion.PresentationDuration)
            { FillBehavior = FillBehavior.Stop, EasingFunction = BroadcastMotion.CreateOverlayEase() };
            fadeOut.Completed += (_, __) =>
            {
                if (generation != _transitionGeneration) return;
                ClearPresentationGhost();
                Visibility = Visibility.Collapsed;
            };
            BeginAnimation(OpacityProperty, fadeOut);
            RootTranslate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, -14, BroadcastMotion.PresentationDuration)
                { FillBehavior = FillBehavior.Stop, EasingFunction = BroadcastMotion.CreateOverlayEase() });
        }

        private static void TrySetColor(SolidColorBrush brush, string hex)
        {
            try { brush.Color = (Color)ColorConverter.ConvertFromString(hex); }
            catch { }
        }

        private static Color ParseColor(string hex, Color fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return fallback; }
        }
    }
}

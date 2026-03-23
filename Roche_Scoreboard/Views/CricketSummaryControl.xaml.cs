using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Roche_Scoreboard.Models;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using UserControl = System.Windows.Controls.UserControl;

namespace Roche_Scoreboard.Views
{
    public partial class CricketSummaryControl : UserControl
    {
        private string _currentMode = ""; // "batting" or "bowling"

        public CricketSummaryControl()
        {
            InitializeComponent();
        }

        public void ShowBattingSummary(CricketMatchManager match)
        {
            var inn = match.CurrentInnings;
            if (inn == null) return;

            bool wasVisible = Visibility == Visibility.Visible;
            Action populate = () => PopulateBatting(match, inn);

            if (wasVisible && _currentMode == "bowling")
                SlideTransition(populate, slideLeft: true);
            else
            {
                populate();
                ShowSummary();
            }
            _currentMode = "batting";
        }

        public void ShowBowlingSummary(CricketMatchManager match)
        {
            var inn = match.CurrentInnings;
            if (inn == null) return;

            bool wasVisible = Visibility == Visibility.Visible;
            Action populate = () => PopulateBowling(match, inn);

            if (wasVisible && _currentMode == "batting")
                SlideTransition(populate, slideLeft: false);
            else
            {
                populate();
                ShowSummary();
            }
            _currentMode = "bowling";
        }

        /// <summary>Refresh batting data without transition animation.</summary>
        public void RefreshBattingSummary(CricketMatchManager match)
        {
            var inn = match.CurrentInnings;
            if (inn == null || Visibility != Visibility.Visible) return;
            PopulateBatting(match, inn);
        }

        /// <summary>Refresh bowling data without transition animation.</summary>
        public void RefreshBowlingSummary(CricketMatchManager match)
        {
            var inn = match.CurrentInnings;
            if (inn == null || Visibility != Visibility.Visible) return;
            PopulateBowling(match, inn);
        }

        private void PopulateBatting(CricketMatchManager match, CricketInnings inn)
        {
            TrySetColor(HeaderBg, match.BattingTeamPrimaryColor);
            HeaderText.Text = $"{match.BattingTeamName.ToUpper()} BATTING";
            RowsPanel.Children.Clear();

            for (int i = 0; i < inn.BattingOrder.Count; i++)
            {
                var p = inn.BattingOrder[i];
                bool hasBatted = p.BallsFaced > 0 || p.IsOut;
                if (!hasBatted && i != inn.StrikerIndex && i != inn.NonStrikerIndex) continue;

                bool isStriker = i == inn.StrikerIndex;
                bool isNonStriker = i == inn.NonStrikerIndex;
                bool isActive = (isStriker || isNonStriker) && !p.IsOut;

                RowsPanel.Children.Add(CreateBatterRow(i + 1, p, isActive, isStriker, match.BattingTeamPrimaryColor));
            }

            RowsPanel.Children.Add(CreateLabelValueRow("EXTRAS", inn.TotalExtras.ToString(),
                $"(wd {inn.Wides}, nb {inn.NoBalls}, b {inn.Byes}, lb {inn.LegByes})"));

            PopulateFooter(match, inn);
        }

        private void PopulateBowling(CricketMatchManager match, CricketInnings inn)
        {
            TrySetColor(HeaderBg, match.BowlingTeamPrimaryColor);
            HeaderText.Text = $"{match.BowlingTeamName.ToUpper()} BOWLING";
            RowsPanel.Children.Clear();
            RowsPanel.Children.Add(CreateBowlingHeaderRow());

            for (int i = 0; i < inn.BowlingAttack.Count; i++)
            {
                var p = inn.BowlingAttack[i];
                if (p.BallsBowled == 0 && i != inn.CurrentBowlerIndex) continue;
                RowsPanel.Children.Add(CreateBowlerRow(p, i == inn.CurrentBowlerIndex, match.BowlingTeamPrimaryColor));
            }

            PopulateFooter(match, inn);
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

        private void SlideTransition(Action populateNew, bool slideLeft)
        {
            double dir = slideLeft ? -1 : 1;
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var dur = TimeSpan.FromSeconds(0.35);

            // Slide out
            var slideOut = new DoubleAnimation(0, dir * 960, dur) { EasingFunction = ease };
            slideOut.Completed += (_, __) =>
            {
                populateNew();
                RootTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                RootTranslate.X = -dir * 960;
                var slideIn = new DoubleAnimation(-dir * 960, 0, dur) { EasingFunction = ease };
                RootTranslate.BeginAnimation(TranslateTransform.XProperty, slideIn);
            };
            RootTranslate.BeginAnimation(TranslateTransform.XProperty, slideOut);
        }

        private Border CreateBatterRow(int order, CricketPlayer p, bool isActive, bool isStriker, string teamColor)
        {
            var borderColor = isActive ? ParseColor(teamColor, Color.FromRgb(0xCC, 0, 0)) : Color.FromRgb(0x1E, 0x29, 0x3B);

            var border = new Border
            {
                BorderBrush = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(0, 0, 0, 2),
                Background = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
                Padding = new Thickness(16, 10, 16, 10),
                Margin = new Thickness(0, 0, 0, 0)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });    // order #
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // name + dismissal
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });       // runs
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });    // balls

            // Order number
            var orderText = new TextBlock
            {
                Text = $"{order}.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                FontSize = 16,
                FontWeight = FontWeights.Black,
                FontFamily = new FontFamily("Bahnschrift"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(orderText, 0);
            grid.Children.Add(orderText);

            // Name + dismissal
            var namePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var nameText = new TextBlock
            {
                Text = p.DisplayName.ToUpper(),
                Foreground = new SolidColorBrush(p.IsOut ? Color.FromRgb(0x94, 0xA3, 0xB8) : Colors.White),
                FontSize = 18,
                FontWeight = FontWeights.Black,
                FontFamily = new FontFamily("Bahnschrift"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            namePanel.Children.Add(nameText);

            var dismissalLine = new TextBlock
            {
                Text = p.DismissalSummary,
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                FontSize = 12,
                FontWeight = FontWeights.Normal,
                FontStyle = p.IsOut ? FontStyles.Normal : FontStyles.Italic,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            namePanel.Children.Add(dismissalLine);
            Grid.SetColumn(namePanel, 1);
            grid.Children.Add(namePanel);

            // Runs (large)
            var runsText = new TextBlock
            {
                Text = p.Runs.ToString(),
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 28,
                FontWeight = FontWeights.Black,
                FontFamily = new FontFamily("Bahnschrift"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 6, 0)
            };
            Grid.SetColumn(runsText, 2);
            grid.Children.Add(runsText);

            // Balls (smaller, muted)
            var ballsText = new TextBlock
            {
                Text = p.BallsFaced.ToString(),
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Bahnschrift"),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 4)
            };
            Grid.SetColumn(ballsText, 3);
            grid.Children.Add(ballsText);

            border.Child = grid;
            return border;
        }

        private static Border CreateBowlingHeaderRow()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x48)),
                Padding = new Thickness(16, 6, 16, 6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // name
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });  // O
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });  // M
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });  // R
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });  // W
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });  // ECON

            AddHeaderCell(grid, "BOWLER", 0, HorizontalAlignment.Left);
            AddHeaderCell(grid, "O", 1, HorizontalAlignment.Center);
            AddHeaderCell(grid, "M", 2, HorizontalAlignment.Center);
            AddHeaderCell(grid, "R", 3, HorizontalAlignment.Center);
            AddHeaderCell(grid, "W", 4, HorizontalAlignment.Center);
            AddHeaderCell(grid, "ECON", 5, HorizontalAlignment.Center);

            border.Child = grid;
            return border;
        }

        private static void AddHeaderCell(Grid grid, string text, int col, HorizontalAlignment align)
        {
            var tb = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                FontSize = 11,
                FontWeight = FontWeights.Black,
                HorizontalAlignment = align,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(tb, col);
            grid.Children.Add(tb);
        }

        private Border CreateBowlerRow(CricketPlayer p, bool isCurrent, string teamColor)
        {
            var borderColor = isCurrent ? ParseColor(teamColor, Color.FromRgb(0, 0, 0x80)) : Color.FromRgb(0x1E, 0x29, 0x3B);

            var border = new Border
            {
                BorderBrush = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(0, 0, 0, 2),
                Background = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
                Padding = new Thickness(16, 10, 16, 10)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // name
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });  // O
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });  // M
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });  // R
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });  // W
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });  // ECON

            var nameText = new TextBlock
            {
                Text = p.DisplayName.ToUpper(),
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 16,
                FontWeight = FontWeights.Black,
                FontFamily = new FontFamily("Bahnschrift"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(nameText, 0);
            grid.Children.Add(nameText);

            AddStatCell(grid, p.OversDisplay, 1);
            AddStatCell(grid, p.Maidens.ToString(), 2);
            AddStatCell(grid, p.RunsConceded.ToString(), 3);
            AddStatCell(grid, p.Wickets.ToString(), 4, p.Wickets > 0 ? Colors.White : Color.FromRgb(0x94, 0xA3, 0xB8));
            AddStatCell(grid, p.Economy.ToString("F1"), 5);

            border.Child = grid;
            return border;
        }

        private static void AddStatCell(Grid grid, string text, int col, Color? color = null)
        {
            var tb = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(color ?? Color.FromRgb(0x94, 0xA3, 0xB8)),
                FontSize = 18,
                FontWeight = FontWeights.Black,
                FontFamily = new FontFamily("Bahnschrift"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(tb, col);
            grid.Children.Add(tb);
        }

        private static Border CreateLabelValueRow(string label, string value, string? detail = null)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x48)),
                Padding = new Thickness(16, 8, 16, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var labelText = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                FontSize = 14,
                FontWeight = FontWeights.Black,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(labelText, 0);
            grid.Children.Add(labelText);

            if (detail != null)
            {
                var detailText = new TextBlock
                {
                    Text = detail,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0)
                };
                Grid.SetColumn(detailText, 1);
                grid.Children.Add(detailText);
            }

            var valueText = new TextBlock
            {
                Text = value,
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 20,
                FontWeight = FontWeights.Black,
                FontFamily = new FontFamily("Bahnschrift"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(valueText, 2);
            grid.Children.Add(valueText);

            border.Child = grid;
            return border;
        }

        private void ShowSummary()
        {
            Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, fadeIn);
        }

        public void HideSummary()
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.35))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (_, __) =>
            {
                _currentMode = "";
                Visibility = Visibility.Collapsed;
                BeginAnimation(OpacityProperty, null);
                Opacity = 1;
            };
            BeginAnimation(OpacityProperty, fadeOut);
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

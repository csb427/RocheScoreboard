using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Color = System.Windows.Media.Color;

namespace Roche_Scoreboard.Views
{
    public partial class StatsScreenControl : System.Windows.Controls.UserControl
    {
        private readonly DispatcherTimer _wallClockTimer;

        public StatsScreenControl()
        {
            InitializeComponent();

            StatsTimeText.Text = DateTime.Now.ToString("h:mm tt");
            _wallClockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _wallClockTimer.Tick += (_, __) => StatsTimeText.Text = DateTime.Now.ToString("h:mm tt");
            _wallClockTimer.Start();
        }

        public void Populate(MatchManager match, MatchStats stats, int endedQuarter,
            string homeColorHex, string awayColorHex,
            string homeSecondaryHex, string awaySecondaryHex,
            string? barTitleOverride = null)
        {
            var homeColor = SafeColor(homeColorHex, "#4488FF");
            var awayColor = SafeColor(awayColorHex, "#FF6644");

            // Background gradient with subtle team tints
            StatsBgGradient.GradientStops.Clear();
            StatsBgGradient.GradientStops.Add(new GradientStop(Darken(homeColor, 0.85), 0.0));
            StatsBgGradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x0F, 0x16, 0x28), 0.5));
            StatsBgGradient.GradientStops.Add(new GradientStop(Darken(awayColor, 0.85), 1.0));

            // Legends
            StatsHomeLegendRect.Fill = new SolidColorBrush(homeColor);
            StatsAwayLegendRect.Fill = new SolidColorBrush(awayColor);
            StatsHomeLegendText.Text = match.HomeAbbr.ToUpper();
            StatsAwayLegendText.Text = match.AwayAbbr.ToUpper();

            // Bottom bar
            StatBarHomeAbbr.Text = match.HomeAbbr.ToUpper();
            StatBarAwayAbbr.Text = match.AwayAbbr.ToUpper();
            StatBarHomeAbbrBrush.Color = Lighten(homeColor, 0.3);
            StatBarAwayAbbrBrush.Color = Lighten(awayColor, 0.3);
            StatBarHomeScore.Text = $"{match.HomeGoals}.{match.HomeBehinds}.{match.HomeTotal}";
            StatBarAwayScore.Text = $"{match.AwayGoals}.{match.AwayBehinds}.{match.AwayTotal}";
            if (!string.IsNullOrEmpty(barTitleOverride))
            {
                StatBarTitle.Text = barTitleOverride;
            }
            else
            {
                StatBarTitle.Text = endedQuarter switch
                {
                    1 => "QT",
                    2 => "HT",
                    3 => "3QT",
                    _ => "FT"
                };
            }

            // --- Populate stat rows ---

            // Scoring Shots
            StatsScrHomeShots.Text = stats.HomeScoringShots.ToString();
            StatsScrAwayShots.Text = stats.AwayScoringShots.ToString();
            SetClashBar(ShotsHomeCol, ShotsAwayCol, ShotHomeBrush, ShotAwayBrush,
                stats.HomeScoringShots, stats.AwayScoringShots, homeColor, awayColor);

            // Accuracy
            StatsScrHomeAcc.Text = $"{stats.HomeAccuracy:0}%";
            StatsScrAwayAcc.Text = $"{stats.AwayAccuracy:0}%";
            SetClashBar(AccHomeCol, AccAwayCol, AccHomeBrush, AccAwayBrush,
                stats.HomeAccuracy, stats.AwayAccuracy, homeColor, awayColor);

            // Time in Front
            StatsScrHomeTime.Text = $"{stats.HomeTimePctInFront:0}%";
            StatsScrAwayTime.Text = $"{stats.AwayTimePctInFront:0}%";
            SetClashBar(TimeHomeCol, TimeAwayCol, TimeHomeBrush, TimeAwayBrush,
                stats.HomeTimePctInFront, stats.AwayTimePctInFront, homeColor, awayColor);

            // Largest Lead
            StatsScrHomeLead.Text = stats.HomeLargestLead > 0 ? stats.HomeLargestLead.ToString() : "–";
            StatsScrAwayLead.Text = stats.AwayLargestLead > 0 ? stats.AwayLargestLead.ToString() : "–";
            SetClashBar(LeadHomeCol, LeadAwayCol, LeadHomeBrush, LeadAwayBrush,
                stats.HomeLargestLead, stats.AwayLargestLead, homeColor, awayColor);
        }

        private static void SetClashBar(ColumnDefinition homeCol, ColumnDefinition awayCol,
            SolidColorBrush homeBrush, SolidColorBrush awayBrush,
            double homeVal, double awayVal, Color homeColor, Color awayColor)
        {
            homeBrush.Color = homeColor;
            awayBrush.Color = awayColor;

            double total = homeVal + awayVal;
            double homePct = total > 0 ? homeVal / total : 0.5;
            homePct = Math.Clamp(homePct, 0.05, 0.95);

            homeCol.Width = new GridLength(homePct, GridUnitType.Star);
            awayCol.Width = new GridLength(1.0 - homePct, GridUnitType.Star);
        }

        private static Color SafeColor(string? hex, string fallbackHex)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) hex = fallbackHex;
                if (!hex.StartsWith("#")) hex = "#" + hex.Trim();
                return (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return (Color)System.Windows.Media.ColorConverter.ConvertFromString(fallbackHex);
            }
        }

        private static Color Lighten(Color c, double amount)
        {
            amount = Math.Clamp(amount, 0, 1);
            return Color.FromRgb(
                (byte)Math.Min(255, c.R + (255 - c.R) * amount),
                (byte)Math.Min(255, c.G + (255 - c.G) * amount),
                (byte)Math.Min(255, c.B + (255 - c.B) * amount));
        }

        private static Color Darken(Color c, double amount)
        {
            amount = Math.Clamp(amount, 0, 1);
            return Color.FromRgb(
                (byte)(c.R * (1 - amount)),
                (byte)(c.G * (1 - amount)),
                (byte)(c.B * (1 - amount)));
        }
    }
}

using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;

namespace Roche_Scoreboard.Views
{
    public partial class BreakScreenControl : System.Windows.Controls.UserControl
    {
        private readonly DispatcherTimer _wallClockTimer;

        public BreakScreenControl()
        {
            InitializeComponent();

            BreakTimeText.Text = DateTime.Now.ToString("h:mm tt");
            _wallClockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _wallClockTimer.Tick += (_, __) => BreakTimeText.Text = DateTime.Now.ToString("h:mm tt");
            _wallClockTimer.Start();
        }

        /// <summary>
        /// Populate the entire break screen from match state.
        /// endedQuarter is the quarter that just finished (1-4).
        /// </summary>
        public void Populate(MatchManager match, int endedQuarter,
            string homeColorHex, string awayColorHex,
            string homeSecondaryHex, string awaySecondaryHex,
            string? homeLogoPath, string? awayLogoPath,
            string? barTitleOverride = null)
        {
            var homeColor = SafeColor(homeColorHex, "#C04020");
            var awayColor = SafeColor(awayColorHex, "#207A20");
            var homeSecondary = SafeColor(homeSecondaryHex, "#FFFFFF");
            var awaySecondary = SafeColor(awaySecondaryHex, "#FFFFFF");

            // Background gradient: home colour left → dark middle → away colour right
            BgGradient.GradientStops.Clear();
            BgGradient.GradientStops.Add(new GradientStop(Lighten(homeColor, 0.15), 0.0));
            BgGradient.GradientStops.Add(new GradientStop(Darken(homeColor, 0.3), 0.35));
            BgGradient.GradientStops.Add(new GradientStop(Darken(awayColor, 0.3), 0.65));
            BgGradient.GradientStops.Add(new GradientStop(Lighten(awayColor, 0.15), 1.0));

            // Team names — use abbreviation if the full name is too long to display cleanly
            const int MaxNameLength = 12;
            BreakHomeName.Text = match.HomeName.Length > MaxNameLength
                ? match.HomeAbbr.ToUpper()
                : match.HomeName.ToUpper();
            BreakAwayName.Text = match.AwayName.Length > MaxNameLength
                ? match.AwayAbbr.ToUpper()
                : match.AwayName.ToUpper();

            // Contrast-aware text for team names against their background
            BreakHomeName.Foreground = ContrastHelper.GetContrastBrush(homeColor);
            BreakAwayName.Foreground = ContrastHelper.GetContrastBrush(awayColor);

            // Bottom bar
            BarHomeAbbr.Text = match.HomeAbbr.ToUpper();
            BarAwayAbbr.Text = match.AwayAbbr.ToUpper();
            BarHomeAbbrBrush.Color = Lighten(homeColor, 0.3);
            BarAwayAbbrBrush.Color = Lighten(awayColor, 0.3);

            BarHomeScore.Text = $"{match.HomeGoals}.{match.HomeBehinds}.{match.HomeTotal}";
            BarAwayScore.Text = $"{match.AwayGoals}.{match.AwayBehinds}.{match.AwayTotal}";

            if (!string.IsNullOrEmpty(barTitleOverride))
            {
                BarTitle.Text = barTitleOverride;
            }
            else
            {
                BarTitle.Text = endedQuarter switch
                {
                    1 => "QT",
                    2 => "HT",
                    3 => "3QT",
                    _ => "FT"
                };
            }

            // Logos
            ApplyLogo(BreakHomeLogoImage, BreakHomeLogoCircle, homeLogoPath);
            ApplyLogo(BreakAwayLogoImage, BreakAwayLogoCircle, awayLogoPath);

            // Quarter rows — use contrast-aware text for cell backgrounds
            var homeCellBgColor = Lighten(homeColor, 0.25);
            var awayCellBgColor = Lighten(awayColor, 0.25);
            var homeCellBg = new SolidColorBrush(homeCellBgColor);
            var awayCellBg = new SolidColorBrush(awayCellBgColor);
            var homeCellFg = ContrastHelper.GetContrastBrush(homeCellBgColor);
            var awayCellFg = ContrastHelper.GetContrastBrush(awayCellBgColor);

            // Cell borders: H1G, H1B, H1T, A1G, A1B, A1T ... H4G, H4B, H4T, A4G, A4B, A4T
            Border[,] homeCells = {
                { H1G, H1B, H1T },
                { H2G, H2B, H2T },
                { H3G, H3B, H3T },
                { H4G, H4B, H4T }
            };
            Border[,] awayCells = {
                { A1G, A1B, A1T },
                { A2G, A2B, A2T },
                { A3G, A3B, A3T },
                { A4G, A4B, A4T }
            };

            for (int q = 0; q < 4; q++)
            {
                var snap = match.GetQuarterSnapshot(q + 1);
                bool played = snap != null;

                // If no snapshot but this is the current in-progress quarter, show live scores
                // Only if the clock has actually run in this quarter (elapsed > 0)
                if (!played && q + 1 == match.Quarter &&
                    match.ElapsedInQuarter > TimeSpan.Zero &&
                    (match.HomeGoals + match.HomeBehinds + match.AwayGoals + match.AwayBehinds) > 0)
                {
                    played = true;
                    snap = new QuarterSnapshot
                    {
                        HomeGoals = match.HomeGoals,
                        HomeBehinds = match.HomeBehinds,
                        AwayGoals = match.AwayGoals,
                        AwayBehinds = match.AwayBehinds
                    };
                }

                string hg, hb, ht, ag, ab, at;
                if (played && snap != null)
                {
                    hg = snap.HomeGoals.ToString();
                    hb = snap.HomeBehinds.ToString();
                    ht = snap.HomeTotal.ToString();
                    ag = snap.AwayGoals.ToString();
                    ab = snap.AwayBehinds.ToString();
                    at = snap.AwayTotal.ToString();
                }
                else
                {
                    hg = hb = ht = ag = ab = at = "–";
                }

                StyleCell(homeCells[q, 0], hg, homeCellBg, homeCellFg, played);
                StyleCell(homeCells[q, 1], hb, homeCellBg, homeCellFg, played);
                StyleCell(homeCells[q, 2], ht, homeCellBg, homeCellFg, played);
                StyleCell(awayCells[q, 0], ag, awayCellBg, awayCellFg, played);
                StyleCell(awayCells[q, 1], ab, awayCellBg, awayCellFg, played);
                StyleCell(awayCells[q, 2], at, awayCellBg, awayCellFg, played);
            }
        }

        private static void StyleCell(Border border, string text, Brush bg, Brush fg, bool played)
        {
            border.CornerRadius = new CornerRadius(12);
            border.Background = bg;
            border.Margin = new Thickness(3, 3, 3, 3);
            border.Padding = new Thickness(0);

            if (!played)
            {
                border.Opacity = 0.4;
            }
            else
            {
                border.Opacity = 1.0;
            }

            var tb = new TextBlock
            {
                Text = text,
                Foreground = fg,
                FontSize = 32,
                FontWeight = FontWeights.Black,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            var vb = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                Child = tb,
                Margin = new Thickness(4, 2, 4, 2)
            };

            border.Child = vb;
        }

        private static void ApplyLogo(Image image, Ellipse fallback, string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                image.Source = null;
                image.Visibility = Visibility.Collapsed;
                fallback.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();

                image.Source = bmp;
                image.Visibility = Visibility.Visible;
                fallback.Visibility = Visibility.Collapsed;
            }
            catch
            {
                image.Source = null;
                image.Visibility = Visibility.Collapsed;
                fallback.Visibility = Visibility.Visible;
            }
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

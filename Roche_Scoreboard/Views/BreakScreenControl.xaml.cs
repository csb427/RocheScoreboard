using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using UserControl = System.Windows.Controls.UserControl;
using Image = System.Windows.Controls.Image;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace Roche_Scoreboard.Views
{
    public partial class BreakScreenControl : UserControl
    {
        private readonly DispatcherTimer _wallClockTimer;
        private bool _isDisposed;

        public BreakScreenControl()
        {
            InitializeComponent();

            BreakTimeText.Text = DateTime.Now.ToString("h:mm tt");
            _wallClockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _wallClockTimer.Tick += OnWallClockTick;
            _wallClockTimer.Start();

            SizeChanged += (_, _) => ApplyResponsiveScale();

            Unloaded += (_, _) => Cleanup();
        }

        private double ScaleUnit => ScoreboardScaleHelper.GetScale(ActualWidth, ActualHeight);

        private double ScalePx(double designValue)
            => ScoreboardScaleHelper.Scale(designValue, ScaleUnit);

        /// <summary>
        /// Scales fixed-pixel chrome rows (title bar, bottom score bar) so
        /// that at small host sizes they shrink to keep the same proportion
        /// of the screen they have at design size, instead of dominating.
        /// </summary>
        private void ApplyResponsiveScale()
        {
        }

        private void OnWallClockTick(object? sender, EventArgs e)
        {
            BreakTimeText.Text = DateTime.Now.ToString("h:mm tt");
        }

        private void Cleanup()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _wallClockTimer.Stop();
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
            ArgumentNullException.ThrowIfNull(match);

            var homeColor = SafeColor(homeColorHex, "#C04020");
            var awayColor = SafeColor(awayColorHex, "#207A20");
            var homeSecondary = SafeColor(homeSecondaryHex, "#FFFFFF");
            var awaySecondary = SafeColor(awaySecondaryHex, "#FFFFFF");

            // Background gradient: home colour left → black middle → away colour right
            BgGradient.GradientStops.Clear();
            BgGradient.GradientStops.Add(new GradientStop(homeColor, 0.0));
            BgGradient.GradientStops.Add(new GradientStop(Colors.Black, 0.45));
            BgGradient.GradientStops.Add(new GradientStop(awayColor, 1.0));

            // Title accent line: home colour left → transparent centre → away colour right
            TitleAccentGradient.GradientStops.Clear();
            TitleAccentGradient.GradientStops.Add(new GradientStop(homeColor, 0.0));
            TitleAccentGradient.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, homeColor.R, homeColor.G, homeColor.B), 0.35));
            TitleAccentGradient.GradientStops.Add(new GradientStop(Colors.Transparent, 0.5));
            TitleAccentGradient.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, awayColor.R, awayColor.G, awayColor.B), 0.65));
            TitleAccentGradient.GradientStops.Add(new GradientStop(awayColor, 1.0));

            // Team headers: subtle dark bg, bold team-colour border, secondary colour text.
            // If the team primary is too dark to register against the panel's
            // dark background, fall back to the team's secondary so the border
            // actually reads as a line.
            HomeHeaderBg.Color = Darken(homeColor, 0.7);
            AwayHeaderBg.Color = Darken(awayColor, 0.7);
            var homeHeaderBg = HomeHeaderBg.Color;
            var awayHeaderBg = AwayHeaderBg.Color;
            HomeTeamHeader.BorderBrush = Services.ContrastHelper.GetVisibleBorderBrush(homeHeaderBg, homeColor, homeSecondary);
            AwayTeamHeader.BorderBrush = Services.ContrastHelper.GetVisibleBorderBrush(awayHeaderBg, awayColor, awaySecondary);
            HomeNameFg.Color = Lighten(homeColor, 0.4);
            AwayNameFg.Color = Lighten(awayColor, 0.4);

            // Column headers: lightened team colours so they read clearly
            var homeHdrBrush = new SolidColorBrush(Lighten(homeColor, 0.4));
            var awayHdrBrush = new SolidColorBrush(Lighten(awayColor, 0.4));
            HomeHdrG.Foreground = homeHdrBrush;
            HomeHdrB.Foreground = homeHdrBrush;
            HomeHdrT.Foreground = homeHdrBrush;
            AwayHdrG.Foreground = awayHdrBrush;
            AwayHdrB.Foreground = awayHdrBrush;
            AwayHdrT.Foreground = awayHdrBrush;

            // Team names — always use the full name; the team-name Viewbox
            // (Stretch=Uniform StretchDirection=DownOnly MaxHeight=26)
            // auto-shrinks long names so they still fit the header strip.
            BreakHomeName.Text = match.HomeName.ToUpper();
            BreakAwayName.Text = match.AwayName.ToUpper();

            // Bottom score bar
            ScoreBar.Populate(match, endedQuarter, homeColorHex, awayColorHex,
                homeSecondaryHex, awaySecondaryHex, homeLogoPath, awayLogoPath,
                barTitleOverride);

            // Logos — hide all placeholders if at least one team has no logo
            bool hideLogos = Services.ImageLoadHelper.Load(homeLogoPath) is null
                          || Services.ImageLoadHelper.Load(awayLogoPath) is null;
            ApplyLogo(BreakHomeLogoImage, BreakHomeLogoCircle, homeLogoPath, hideLogos);
            ApplyLogo(BreakAwayLogoImage, BreakAwayLogoCircle, awayLogoPath, hideLogos);

            // Quarter rows — bold team-coloured borders, subtle dark backgrounds
            var homeCellBg = new SolidColorBrush(Darken(homeColor, 0.80));
            var awayCellBg = new SolidColorBrush(Darken(awayColor, 0.80));
            var homeCellFg = new SolidColorBrush(Lighten(homeColor, 0.5));
            var awayCellFg = new SolidColorBrush(Lighten(awayColor, 0.5));
            // Total columns: bright team colour for max emphasis
            var homeTotalFg = new SolidColorBrush(Lighten(homeColor, 0.3));
            var awayTotalFg = new SolidColorBrush(Lighten(awayColor, 0.3));
            // Cell borders: full opacity team primary — bold and vibrant.
            // If the team primary is too dark against the cell's dark background
            // the border vanishes; fall through to secondary in that case so the
            // grid stays visually structured.
            var homeCellBgColor = Darken(homeColor, 0.80);
            var awayCellBgColor = Darken(awayColor, 0.80);
            var homeBorderBrush = Services.ContrastHelper.GetVisibleBorderBrush(homeCellBgColor, homeColor, homeSecondary);
            var awayBorderBrush = Services.ContrastHelper.GetVisibleBorderBrush(awayCellBgColor, awayColor, awaySecondary);

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

                StyleCell(homeCells[q, 0], hg, homeCellBg, homeCellFg, homeBorderBrush, played, ScaleUnit);
                StyleCell(homeCells[q, 1], hb, homeCellBg, homeCellFg, homeBorderBrush, played, ScaleUnit);
                StyleCell(homeCells[q, 2], ht, homeCellBg, homeTotalFg, homeBorderBrush, played, ScaleUnit, isTotal: true);
                StyleCell(awayCells[q, 0], ag, awayCellBg, awayCellFg, awayBorderBrush, played, ScaleUnit);
                StyleCell(awayCells[q, 1], ab, awayCellBg, awayCellFg, awayBorderBrush, played, ScaleUnit);
                StyleCell(awayCells[q, 2], at, awayCellBg, awayTotalFg, awayBorderBrush, played, ScaleUnit, isTotal: true);
            }
        }

        private static void StyleCell(Border border, string text, Brush bg, Brush fg, Brush borderBrush, bool played, double scale, bool isTotal = false)
        {
            border.CornerRadius = new CornerRadius(0);
            border.Background = bg;
            border.BorderBrush = borderBrush;
            border.BorderThickness = new Thickness(1);
            border.Margin = new Thickness(2, 0, 2, 0);
            border.Padding = new Thickness(0);
            border.Opacity = played ? 1.0 : 0.25;

            border.Child = new TextBlock
            {
                Text = text,
                Foreground = fg,
                FontSize = isTotal ? 13 : 12,
                FontWeight = isTotal ? FontWeights.Black : FontWeights.Bold,
                FontFamily = new System.Windows.Media.FontFamily("Bahnschrift"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static void ApplyLogo(Image image, Ellipse fallback, string? path, bool hideLogos = false)
        {
            if (hideLogos)
            {
                image.Source = null;
                image.Visibility = Visibility.Collapsed;
                fallback.Visibility = Visibility.Collapsed;
                return;
            }

            ImageSource? source = ImageLoadHelper.LoadTrimmed(path);
            if (source is null)
            {
                image.Source = null;
                image.Visibility = Visibility.Collapsed;
                fallback.Visibility = Visibility.Visible;
                return;
            }

            image.Source = source;
            image.Visibility = Visibility.Visible;
            fallback.Visibility = Visibility.Collapsed;
        }

        private static Color SafeColor(string? hex, string fallbackHex)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex))
                    hex = fallbackHex;
                if (!hex.StartsWith("#"))
                    hex = "#" + hex;
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return (Color)ColorConverter.ConvertFromString(fallbackHex);
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

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using UserControl = System.Windows.Controls.UserControl;
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

            // Title accent line
            TitleAccentGradient.GradientStops.Clear();
            TitleAccentGradient.GradientStops.Add(new GradientStop(homeColor, 0.0));
            TitleAccentGradient.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, homeColor.R, homeColor.G, homeColor.B), 0.35));
            TitleAccentGradient.GradientStops.Add(new GradientStop(Colors.Transparent, 0.5));
            TitleAccentGradient.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, awayColor.R, awayColor.G, awayColor.B), 0.65));
            TitleAccentGradient.GradientStops.Add(new GradientStop(awayColor, 1.0));

            // Team panel background and border washes, scorebug-style
            HomeHeaderBorder.Color = homeColor;
            AwayHeaderBorder.Color = awayColor;
            HomeHeaderBg.Color = Darken(homeColor, 0.55);
            AwayHeaderBg.Color = Darken(awayColor, 0.55);
            HomeNameFg.Color = Lighten(homeColor, 0.55);
            AwayNameFg.Color = Lighten(awayColor, 0.55);

            // Smart contrast: if the lightened tint fails WCAG against its panel background,
            // fall back to high-contrast white/black so abbreviations remain readable.
            if (Services.SmartContrast.IsLowContrast(HomeNameFg.Color, HomeHeaderBg.Color))
                HomeNameFg.Color = Services.SmartContrast.PickForeground(HomeHeaderBg.Color);
            if (Services.SmartContrast.IsLowContrast(AwayNameFg.Color, AwayHeaderBg.Color))
                AwayNameFg.Color = Services.SmartContrast.PickForeground(AwayHeaderBg.Color);

            // Team abbreviations (large, readable label)
            BreakHomeAbbr.Text = SafeAbbr(match.HomeAbbr, match.HomeName);
            BreakAwayAbbr.Text = SafeAbbr(match.AwayAbbr, match.AwayName);

            // Bottom score bar - the break screen IS the quarter break, so the
            // bar only shows the quarter label (QT/HT/3QT/FT). Never show the
            // running game clock here regardless of any caller-supplied title.
            ScoreBar.Populate(match, endedQuarter, homeColorHex, awayColorHex,
                homeSecondaryHex, awaySecondaryHex, homeLogoPath, awayLogoPath,
                barTitleOverride, showTimer: false);

            // Zoomed logo backgrounds (faint art behind the abbreviation)
            ApplyLogoBg(BreakHomeLogoBg, homeLogoPath);
            ApplyLogoBg(BreakAwayLogoBg, awayLogoPath);

            // Per-quarter scores (per-quarter only - overall totals already shown by ScoreBar)
            var homeCellFg = new SolidColorBrush(Lighten(homeColor, 0.5));
            var awayCellFg = new SolidColorBrush(Lighten(awayColor, 0.5));

            Border[] homeQuarterCells = { H1T, H2T, H3T, H4T };
            Border[] awayQuarterCells = { A1T, A2T, A3T, A4T };

            for (int q = 0; q < 4; q++)
            {
                var snap = match.GetQuarterSnapshot(q + 1);
                bool played = snap != null;

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

                string homeText, awayText;
                if (played && snap != null)
                {
                    homeText = snap.HomeTotal.ToString();
                    awayText = snap.AwayTotal.ToString();
                }
                else
                {
                    homeText = "–";
                    awayText = "–";
                }

                StyleQuarterCell(homeQuarterCells[q], homeText, homeCellFg, played);
                StyleQuarterCell(awayQuarterCells[q], awayText, awayCellFg, played);
            }
        }

        private static void StyleQuarterCell(Border border, string text, Brush fg, bool played)
        {
            border.Background = System.Windows.Media.Brushes.Transparent;
            border.BorderThickness = new Thickness(0);
            border.Margin = new Thickness(0);
            border.Padding = new Thickness(0);
            border.Opacity = played ? 1.0 : 0.35;

            var textBlock = new TextBlock
            {
                Text = text,
                Foreground = fg,
                FontSize = 48,
                FontWeight = FontWeights.Black,
                FontFamily = new System.Windows.Media.FontFamily("Bahnschrift"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            border.Child = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Margin = new Thickness(1, 2, 1, 2),
                Child = textBlock
            };
        }

        private static void ApplyLogoBg(System.Windows.Controls.Image image, string? path)
        {
            ImageSource? source = ImageLoadHelper.LoadTrimmed(path);
            image.Source = source;
            image.Visibility = source is not null ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string SafeAbbr(string? abbr, string? fallbackName)
        {
            if (!string.IsNullOrWhiteSpace(abbr))
                return abbr.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(fallbackName))
                return "---";
            string clean = fallbackName.Trim();
            return (clean.Length <= 4 ? clean : clean[..3]).ToUpperInvariant();
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

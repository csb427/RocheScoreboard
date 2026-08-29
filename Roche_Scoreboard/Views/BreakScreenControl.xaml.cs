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
        private bool _responsiveLayoutQueued;

        public BreakScreenControl()
        {
            InitializeComponent();

            BreakTimeText.Text = DateTime.Now.ToString("h:mm tt");
            _wallClockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _wallClockTimer.Tick += OnWallClockTick;
            _wallClockTimer.Start();

            SizeChanged += (_, _) => QueueResponsiveLayout();
            IsVisibleChanged += (_, e) =>
            {
                if (e.NewValue is true) QueueResponsiveLayout();
            };
            Loaded += (_, _) => QueueResponsiveLayout();

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
            if (!IsVisible || ActualWidth <= 0 || ActualHeight <= 0)
                return;

            ResponsivePresentationTypography.Apply(this, ScaleUnit);

            bool compact = ActualWidth < 320 || ActualHeight < 160;
            BreakTitleText.Text = compact ? "QTR BREAKDOWN" : "QUARTER BY QUARTER";
            BreakTimeText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;

            double teamColumnMax = Math.Max(58, ScalePx(92));
            BreakTeamColumn.Width = new GridLength(Math.Clamp(ActualWidth * 0.24, 58, teamColumnMax));
            double quarterHeaderMax = Math.Max(17, ActualHeight * 0.16);
            BreakQuarterHeaderRow.Height = new GridLength(Math.Clamp(ScalePx(24), 17, quarterHeaderMax));

            TextBlock[] details = [H1Detail, H2Detail, H3Detail, H4Detail,
                                   A1Detail, A2Detail, A3Detail, A4Detail];
            foreach (TextBlock detail in details)
            {
                detail.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
                if (detail.Parent is Grid cellGrid && cellGrid.RowDefinitions.Count == 2)
                {
                    cellGrid.RowDefinitions[0].Height = new GridLength(compact ? 1 : 3, GridUnitType.Star);
                    cellGrid.RowDefinitions[1].Height = compact
                        ? new GridLength(0)
                        : new GridLength(2, GridUnitType.Star);
                }
            }
        }

        private void QueueResponsiveLayout()
        {
            if (_responsiveLayoutQueued || _isDisposed)
                return;

            _responsiveLayoutQueued = true;
            Dispatcher.BeginInvoke(() =>
            {
                _responsiveLayoutQueued = false;
                ApplyResponsiveScale();
            }, DispatcherPriority.Render);
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
            HomeHeaderBg.Color = Darken(homeColor, 0.55);
            AwayHeaderBg.Color = Darken(awayColor, 0.55);
            HomeHeaderBorder.Color = ContrastHelper.GetVisibleBorderColor(HomeHeaderBg.Color, homeColor, homeSecondary);
            AwayHeaderBorder.Color = ContrastHelper.GetVisibleBorderColor(AwayHeaderBg.Color, awayColor, awaySecondary);
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

            // This screen can be selected during live play as well as at a
            // break. The shared bar decides whether the timer is live from the
            // actual match state instead of assuming this is always a break.
            ScoreBar.Populate(match, endedQuarter, homeColorHex, awayColorHex,
                homeSecondaryHex, awaySecondaryHex, homeLogoPath, awayLogoPath,
                barTitleOverride, showTimer: true);

            // Zoomed logo backgrounds (faint art behind the abbreviation)
            ApplyLogoBg(BreakHomeLogoBg, homeLogoPath);
            ApplyLogoBg(BreakAwayLogoBg, awayLogoPath);

            Border[] homeQuarterCells = { H1T, H2T, H3T, H4T };
            Border[] awayQuarterCells = { A1T, A2T, A3T, A4T };
            TextBlock[] homeQuarterTotals = { H1Total, H2Total, H3Total, H4Total };
            TextBlock[] awayQuarterTotals = { A1Total, A2Total, A3Total, A4Total };
            TextBlock[] homeQuarterDetails = { H1Detail, H2Detail, H3Detail, H4Detail };
            TextBlock[] awayQuarterDetails = { A1Detail, A2Detail, A3Detail, A4Detail };

            bool atQuarterBreak = !match.ClockRunning && match.ElapsedInQuarter <= TimeSpan.Zero
                                  && (match.Quarter > 1 || match.GetQuarterSnapshot(4) is not null);

            for (int q = 0; q < 4; q++)
            {
                var snap = match.GetQuarterSnapshot(q + 1);
                bool played = snap != null;
                bool current = q + 1 == match.Quarter && !atQuarterBreak && snap is null;

                if (current)
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

                string homeBreakdown, awayBreakdown, homeTotal, awayTotal;
                if (played && snap != null)
                {
                    var previous = q > 0 ? match.GetQuarterSnapshot(q) : null;
                    int homeGoals = Math.Max(0, snap.HomeGoals - (previous?.HomeGoals ?? 0));
                    int homeBehinds = Math.Max(0, snap.HomeBehinds - (previous?.HomeBehinds ?? 0));
                    int awayGoals = Math.Max(0, snap.AwayGoals - (previous?.AwayGoals ?? 0));
                    int awayBehinds = Math.Max(0, snap.AwayBehinds - (previous?.AwayBehinds ?? 0));
                    homeBreakdown = $"{homeGoals}.{homeBehinds}";
                    awayBreakdown = $"{awayGoals}.{awayBehinds}";
                    homeTotal = (homeGoals * 6 + homeBehinds).ToString();
                    awayTotal = (awayGoals * 6 + awayBehinds).ToString();
                }
                else
                {
                    homeBreakdown = awayBreakdown = "—";
                    homeTotal = awayTotal = "—";
                }

                StyleQuarterCell(homeQuarterCells[q], homeQuarterTotals[q], homeQuarterDetails[q],
                    homeBreakdown, homeTotal, homeColor, played, current);
                StyleQuarterCell(awayQuarterCells[q], awayQuarterTotals[q], awayQuarterDetails[q],
                    awayBreakdown, awayTotal, awayColor, played, current);
            }

            // Populate may run while the presentation is collapsed. Re-apply
            // responsive sizing once WPF has made the surface visible and its
            // real dimensions are available.
            QueueResponsiveLayout();
        }

        private static void StyleQuarterCell(Border border, TextBlock totalText, TextBlock detailText,
            string breakdown, string total, Color teamColor, bool played, bool current)
        {
            Color panelColor = Darken(teamColor, played ? 0.63 : 0.82);
            border.Background = new SolidColorBrush(panelColor);
            border.BorderBrush = new SolidColorBrush(Color.FromArgb(current ? (byte)0xFF : played ? (byte)0xC8 : (byte)0x58, teamColor.R, teamColor.G, teamColor.B));
            border.BorderThickness = new Thickness(current ? 2 : 1);
            border.CornerRadius = new CornerRadius(5);
            border.Margin = new Thickness(1);
            border.Padding = new Thickness(2, 0, 2, 0);
            border.Opacity = played ? 1.0 : 0.62;

            Color totalColor = Lighten(teamColor, 0.62);
            if (SmartContrast.IsLowContrast(totalColor, panelColor))
                totalColor = SmartContrast.PickForeground(panelColor);
            totalText.Text = total;
            Color emptyTotal = SmartContrast.PickReadableForeground(Color.FromRgb(0x7F, 0x8C, 0x9A), panelColor);
            totalText.Foreground = new SolidColorBrush(played ? totalColor : emptyTotal);
            detailText.Text = breakdown;
            Color emptyDetail = SmartContrast.PickReadableForeground(Color.FromRgb(0x70, 0x7D, 0x8A), panelColor);
            detailText.Foreground = new SolidColorBrush(played
                ? SmartContrast.PickForeground(panelColor)
                : emptyDetail);
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

        /// <summary>
        /// Applies the shared finals palette to meaningful components only.
        /// </summary>
        public void SetFinalsMode(bool enabled)
        {
            BreakTitlePanel.Background = enabled ? Services.FinalsPalette.PanelBrush() : new SolidColorBrush(Color.FromRgb(0x0D, 0x11, 0x17));
            BreakTitleText.Foreground = enabled ? new SolidColorBrush(Services.FinalsPalette.Ink) : new SolidColorBrush(Color.FromRgb(0xF0, 0xC0, 0x40));
            BreakTimeText.Foreground = enabled ? new SolidColorBrush(Services.FinalsPalette.Ink) : new SolidColorBrush(Color.FromRgb(0x58, 0xA6, 0xFF));
            ScoreBar?.SetFinalsMode(enabled);
        }

        public void PlayScoreAnimation(ScoreEvent scoreEvent)
            => ScoreBar?.PlayScoreAnimation(scoreEvent);
    }
}

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using GradientStop = System.Windows.Media.GradientStop;
using UserControl = System.Windows.Controls.UserControl;
using Roche_Scoreboard.Services;

namespace Roche_Scoreboard.Views
{
    public partial class StatsScreenControl : UserControl
    {
        private readonly DispatcherTimer _wallClockTimer;
        private bool _isDisposed;
        private bool _responsiveLayoutQueued;

        public void SetFinalsMode(bool enabled)
        {
            StatsTitlePanel.Background = enabled ? FinalsPalette.PanelBrush() : new SolidColorBrush(Color.FromRgb(0x0D, 0x11, 0x17));
            StatsTitleText.Foreground = StatsTimeText.Foreground = enabled
                ? new SolidColorBrush(FinalsPalette.Ink)
                : System.Windows.Media.Brushes.White;
            ScoreBar?.SetFinalsMode(enabled);
        }

        public void PlayScoreAnimation(ScoreEvent scoreEvent)
            => ScoreBar?.PlayScoreAnimation(scoreEvent);

        public StatsScreenControl()
        {
            InitializeComponent();

            StatsTimeText.Text = DateTime.Now.ToString("h:mm tt");
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
        /// Chrome rows are star-proportional in XAML; this method is
        /// kept for compatibility with size-changed wiring but no longer
        /// overwrites row heights.
        /// </summary>
        private void ApplyResponsiveScale()
        {
            if (!IsVisible || ActualWidth <= 0 || ActualHeight <= 0)
                return;

            ResponsivePresentationTypography.Apply(this, ScaleUnit);

            bool compact = ActualWidth < 320 || ActualHeight < 160;
            StatsTimeText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;

            double logoSize = Math.Clamp(ScalePx(24), 18, Math.Max(18, ActualHeight * 0.16));
            StatsHomeLogoImage.Width = StatsHomeLogoImage.Height = logoSize;
            StatsAwayLogoImage.Width = StatsAwayLogoImage.Height = logoSize;
            StatsHomeLogoFallback.Width = StatsHomeLogoFallback.Height = logoSize;
            StatsAwayLogoFallback.Width = StatsAwayLogoFallback.Height = logoSize;

            // Size-dependent chrome belongs here, not in Populate. Populate can
            // run while the screen is still collapsed and ActualWidth is zero;
            // calculating borders there made the second exposure thicker than
            // the first at large output sizes.
            double teamBorder = Math.Clamp(ScalePx(1.5), 1, 3);
            StatsHomeTeamHeader.BorderThickness = new Thickness(teamBorder);
            StatsAwayTeamHeader.BorderThickness = new Thickness(teamBorder);

            // Numeric columns track the available width instead of remaining
            // fixed at their 386px design-canvas size. This lets the values use
            // the same strong proportion on narrow and high-resolution outputs.
            double valueWidth = Math.Clamp(ActualWidth * 0.24, 52, ActualWidth * 0.29);
            TextBlock[] homeValues =
            [
                StatsScrHomeShots, StatsScrHomeAcc,
                StatsScrHomeTime, StatsScrHomeLead
            ];
            foreach (TextBlock value in homeValues)
            {
                if (value.Parent is Viewbox valueBox &&
                    valueBox.Parent is Grid row &&
                    row.ColumnDefinitions.Count >= 3)
                {
                    row.ColumnDefinitions[0].Width = new GridLength(valueWidth);
                    row.ColumnDefinitions[2].Width = new GridLength(valueWidth);
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
            StatsTimeText.Text = DateTime.Now.ToString("h:mm tt");
        }

        private void Cleanup()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _wallClockTimer.Stop();
        }

        public void Populate(MatchManager match, MatchStats stats, int endedQuarter,
            string homeColorHex, string awayColorHex,
            string homeSecondaryHex, string awaySecondaryHex,
            string? homeLogoPath, string? awayLogoPath,
            string? barTitleOverride = null)
        {
            ArgumentNullException.ThrowIfNull(match);
            ArgumentNullException.ThrowIfNull(stats);

            var homeColor = SafeColor(homeColorHex, "#4488FF");
            var awayColor = SafeColor(awayColorHex, "#FF6644");
            var homeSecondary = SafeColor(homeSecondaryHex, "#FFFFFF");
            var awaySecondary = SafeColor(awaySecondaryHex, "#FFFFFF");

            // Background gradient with team colour tints at edges
            StatsBgGradient.GradientStops.Clear();
            StatsBgGradient.GradientStops.Add(new GradientStop(Darken(homeColor, 0.85), 0.0));
            StatsBgGradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x0D, 0x11, 0x17), 0.5));
            StatsBgGradient.GradientStops.Add(new GradientStop(Darken(awayColor, 0.85), 1.0));

            // Title accent: team colours split left/right
            StatsTitleAccent.GradientStops.Clear();
            StatsTitleAccent.GradientStops.Add(new GradientStop(homeColor, 0.0));
            StatsTitleAccent.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, homeColor.R, homeColor.G, homeColor.B), 0.35));
            StatsTitleAccent.GradientStops.Add(new GradientStop(System.Windows.Media.Colors.Transparent, 0.5));
            StatsTitleAccent.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, awayColor.R, awayColor.G, awayColor.B), 0.65));
            StatsTitleAccent.GradientStops.Add(new GradientStop(awayColor, 1.0));

            // Team headers: subtle dark bg, bold team-colour border with glow.
            // Falls back to secondary if primary is too dark to register against
            // the dark header background.
            var statsHomeHeaderBg = Darken(homeColor, 0.7);
            var statsAwayHeaderBg = Darken(awayColor, 0.7);
            StatsHomeTeamHeader.Background = new SolidColorBrush(statsHomeHeaderBg);
            StatsAwayTeamHeader.Background = new SolidColorBrush(statsAwayHeaderBg);
            StatsHomeTeamHeader.BorderBrush = Services.ContrastHelper.GetVisibleBorderBrush(statsHomeHeaderBg, homeColor, homeSecondary);
            StatsAwayTeamHeader.BorderBrush = Services.ContrastHelper.GetVisibleBorderBrush(statsAwayHeaderBg, awayColor, awaySecondary);
            StatsHomeGlow.Color = System.Windows.Media.Color.FromArgb(0xFF, homeColor.R, homeColor.G, homeColor.B);
            StatsAwayGlow.Color = System.Windows.Media.Color.FromArgb(0xFF, awayColor.R, awayColor.G, awayColor.B);
            // The header is intentionally compact. Use the configured scoreboard
            // abbreviations so both teams remain identifiable at oval distance;
            // full names are already represented by the logos and match state.
            StatsHomeTeamText.Text = match.HomeAbbr.ToUpperInvariant();
            StatsAwayTeamText.Text = match.AwayAbbr.ToUpperInvariant();
            StatsHomeTeamText.Foreground = new SolidColorBrush(
                SmartContrast.PickReadableForeground(Lighten(homeColor, 0.4), statsHomeHeaderBg));
            StatsAwayTeamText.Foreground = new SolidColorBrush(
                SmartContrast.PickReadableForeground(Lighten(awayColor, 0.4), statsAwayHeaderBg));

            // Value text: team primary colours with glow
            Color homeEdgeBackground = Darken(homeColor, 0.85);
            Color awayEdgeBackground = Darken(awayColor, 0.85);
            var homeLightened = SmartContrast.PickReadableForeground(
                Lighten(homeColor, 0.3), homeEdgeBackground, Color.FromRgb(0x0D, 0x11, 0x17));
            var awayLightened = SmartContrast.PickReadableForeground(
                Lighten(awayColor, 0.3), awayEdgeBackground, Color.FromRgb(0x0D, 0x11, 0x17));
            HomeValueBrush1.Color = homeLightened;
            HomeValueBrush2.Color = homeLightened;
            HomeValueBrush3.Color = homeLightened;
            HomeValueBrush4.Color = homeLightened;
            AwayValueBrush1.Color = awayLightened;
            AwayValueBrush2.Color = awayLightened;
            AwayValueBrush3.Color = awayLightened;
            AwayValueBrush4.Color = awayLightened;

            // Value glow effects: team primary colours
            SetGlowColor(StatsScrHomeShots, homeColor);
            SetGlowColor(StatsScrHomeAcc, homeColor);
            SetGlowColor(StatsScrHomeTime, homeColor);
            SetGlowColor(StatsScrHomeLead, homeColor);
            SetGlowColor(StatsScrAwayShots, awayColor);
            SetGlowColor(StatsScrAwayAcc, awayColor);
            SetGlowColor(StatsScrAwayTime, awayColor);
            SetGlowColor(StatsScrAwayLead, awayColor);

            // Stat bar borders: team-coloured blend
            var blendedBorder = Color.FromRgb(
                (byte)((homeColor.R + awayColor.R) / 2),
                (byte)((homeColor.G + awayColor.G) / 2),
                (byte)((homeColor.B + awayColor.B) / 2));
            var barBorderColor = blendedBorder;
            ShotsBarBorder.Color = barBorderColor;
            AccBarBorder.Color = barBorderColor;
            TimeBarBorder.Color = barBorderColor;
            LeadBarBorder.Color = barBorderColor;

            // Resolve each logo independently. One missing club asset must not
            // make the other team's valid logo disappear.
            ApplyLogo(StatsHomeLogoImage, StatsHomeLogoFallback, homeLogoPath);
            ApplyLogo(StatsAwayLogoImage, StatsAwayLogoFallback, awayLogoPath);

            // Bottom score bar
            ScoreBar.Populate(match, endedQuarter, homeColorHex, awayColorHex,
                homeSecondaryHex, awaySecondaryHex, homeLogoPath, awayLogoPath,
                barTitleOverride);

            // Populate stat rows
            StatsScrHomeShots.Text = stats.HomeScoringShots.ToString();
            StatsScrAwayShots.Text = stats.AwayScoringShots.ToString();
            SetClashBar(ShotsHomeCol, ShotsAwayCol, ShotHomeBrush, ShotAwayBrush,
                stats.HomeScoringShots, stats.AwayScoringShots, homeColor, awayColor);

            StatsScrHomeAcc.Text = $"{stats.HomeAccuracy:0}%";
            StatsScrAwayAcc.Text = $"{stats.AwayAccuracy:0}%";
            SetClashBar(AccHomeCol, AccAwayCol, AccHomeBrush, AccAwayBrush,
                stats.HomeAccuracy, stats.AwayAccuracy, homeColor, awayColor);

            StatsScrHomeTime.Text = $"{stats.HomeTimePctInFront:0}%";
            StatsScrAwayTime.Text = $"{stats.AwayTimePctInFront:0}%";
            SetTimeInFrontBar(TimeHomeCol, TimeTiedCol, TimeAwayCol,
                TimeHomeBrush, TimeTiedBrush, TimeAwayBrush,
                stats.HomeTimePctInFront, stats.DrawPct, stats.AwayTimePctInFront,
                homeColor, awayColor);

            StatsScrHomeLead.Text = stats.HomeLargestLead > 0 ? stats.HomeLargestLead.ToString() : "–";
            StatsScrAwayLead.Text = stats.AwayLargestLead > 0 ? stats.AwayLargestLead.ToString() : "–";
            SetClashBar(LeadHomeCol, LeadAwayCol, LeadHomeBrush, LeadAwayBrush,
                stats.HomeLargestLead, stats.AwayLargestLead, homeColor, awayColor);

            QueueResponsiveLayout();
        }

        private static void SetGlowColor(System.Windows.Controls.TextBlock tb, Color color)
        {
            if (tb.Effect is System.Windows.Media.Effects.DropShadowEffect glow)
                glow.Color = System.Windows.Media.Color.FromArgb(0xFF, color.R, color.G, color.B);
        }

        private static void SetClashBar(ColumnDefinition homeCol, ColumnDefinition awayCol,
            SolidColorBrush homeBrush, SolidColorBrush awayBrush,
            double homeVal, double awayVal, Color homeColor, Color awayColor)
        {
            homeBrush.Color = homeColor;
            awayBrush.Color = awayColor;

            if (homeVal + awayVal == 0)
            {
                homeCol.Width = new GridLength(1, GridUnitType.Star);
                awayCol.Width = new GridLength(1, GridUnitType.Star);
            }
            else
            {
                homeCol.Width = new GridLength(homeVal, GridUnitType.Star);
                awayCol.Width = new GridLength(awayVal, GridUnitType.Star);
            }
        }

        private static void SetTimeInFrontBar(
            ColumnDefinition homeCol, ColumnDefinition tiedCol, ColumnDefinition awayCol,
            SolidColorBrush homeBrush, SolidColorBrush tiedBrush, SolidColorBrush awayBrush,
            double homePct, double drawPct, double awayPct,
            Color homeColor, Color awayColor)
        {
            homeBrush.Color = homeColor;
            tiedBrush.Color = Color.FromRgb(0x66, 0x66, 0x66);
            awayBrush.Color = awayColor;

            double total = homePct + drawPct + awayPct;
            if (total == 0)
            {
                homeCol.Width = new GridLength(1, GridUnitType.Star);
                tiedCol.Width = new GridLength(0, GridUnitType.Star);
                awayCol.Width = new GridLength(1, GridUnitType.Star);
            }
            else
            {
                homeCol.Width = new GridLength(Math.Max(0.01, homePct), GridUnitType.Star);
                tiedCol.Width = new GridLength(Math.Max(0, drawPct), GridUnitType.Star);
                awayCol.Width = new GridLength(Math.Max(0.01, awayPct), GridUnitType.Star);
            }
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

        private static void ApplyLogo(System.Windows.Controls.Image image, Ellipse fallback, string? path, bool hideLogos = false)
        {
            if (hideLogos)
            {
                image.Source = null;
                image.Visibility = Visibility.Collapsed;
                fallback.Visibility = Visibility.Collapsed;
                return;
            }

            var source = Services.ImageLoadHelper.LoadTrimmed(path);
            image.Source = source;
            image.Visibility = source is not null ? Visibility.Visible : Visibility.Collapsed;
            fallback.Visibility = source is not null ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}

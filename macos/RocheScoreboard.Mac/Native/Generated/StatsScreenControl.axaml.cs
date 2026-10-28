using RocheScoreboard.Mac.Production;
// Generated from Views/StatsScreenControl.xaml.cs; edit translator/adapters.
// Source SHA-256: b1c54daadf6032cf2909de389a50dcd8e833b86463fdb48fb947685e09984234
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Input;
using RocheScoreboard.Mac.Native;
using System;
using Roche_Scoreboard.Models;
using Avalonia.Media.Imaging;
using Avalonia.Controls.Shapes;
using Color = Avalonia.Media.Color;
using ColorConverter = RocheScoreboard.Mac.Native.ColorConverter;
using SolidColorBrush = Avalonia.Media.SolidColorBrush;
using GradientStop = Avalonia.Media.GradientStop;
using UserControl = Avalonia.Controls.UserControl;
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
                : Avalonia.Media.Brushes.White;
            ScoreBar?.SetFinalsMode(enabled);
        }

        public void PlayScoreAnimation(ScoreEvent scoreEvent)
            => ScoreBar?.PlayScoreAnimation(scoreEvent);

        public StatsScreenControl()
        {
            NativePointer.EnsureInitialized(); ProductionNames.BeginLoad(this); try { InitializeComponent(); } finally { ProductionNames.CompleteLoad(this); }

            StatsTimeText.Text = DateTime.Now.ToString("h:mm tt");
            _wallClockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _wallClockTimer.Tick += OnWallClockTick;
            _wallClockTimer.Start();

            SizeChanged += (_, _) => QueueResponsiveLayout();
            NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsVisibleProperty,true,(_, e) =>
            {
                if (e.NewValue is true) QueueResponsiveLayout();
            });
            Loaded += (_, _) =>
            {
                _isDisposed = false;
                _wallClockTimer.Start();
                QueueResponsiveLayout();
            };

            Unloaded += (_, _) => Cleanup();
        }

        private double ScaleUnit => ScoreboardScaleHelper.GetScale(Bounds.Width, Bounds.Height);

        private double ScalePx(double designValue)
            => ScoreboardScaleHelper.Scale(designValue, ScaleUnit);

        /// <summary>
        /// Chrome rows are star-proportional in XAML; this method is
        /// kept for compatibility with size-changed wiring but no longer
        /// overwrites row heights.
        /// </summary>
        private void ApplyResponsiveScale()
        {
            if (!IsVisible || Bounds.Width <= 0 || Bounds.Height <= 0)
                return;

            ResponsivePresentationTypography.Apply(this, ScaleUnit);

            bool compact = Bounds.Width < 320 || Bounds.Height < 160;
            StatsTimeText.IsVisible = compact ? false : true;

            // Size-dependent chrome belongs here, not in Populate. Populate can
            // run while the screen is still collapsed and Bounds.Width is zero;
            // calculating borders there made the second exposure thicker than
            // the first at large output sizes.
            double teamBorder = Math.Clamp(ScalePx(1.5), 1, 3);
            StatsHomeTeamHeader.BorderThickness = new Thickness(teamBorder);
            StatsAwayTeamHeader.BorderThickness = new Thickness(teamBorder);

            // Numeric columns track the available width instead of remaining
            // fixed at their 386px design-canvas size. This lets the values use
            // the same strong proportion on narrow and high-resolution outputs.
            // During a rapid screen handoff WPF can briefly arrange this
            // control below 200 px wide. Never pass an inverted min/max pair
            // to Math.Clamp in that transient state.
            double valueWidth = CalculateValueColumnWidth(Bounds.Width);
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

        internal static double CalculateValueColumnWidth(double actualWidth)
        {
            double safeWidth = Math.Max(0, actualWidth);
            double maximumValueWidth = Math.Max(1, safeWidth * 0.23);
            double minimumValueWidth = Math.Min(46, maximumValueWidth);
            return Math.Clamp(safeWidth * 0.20, minimumValueWidth, maximumValueWidth);
        }

        private void QueueResponsiveLayout()
        {
            if (_responsiveLayoutQueued || _isDisposed)
                return;

            _responsiveLayoutQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _responsiveLayoutQueued = false;
                ApplyResponsiveScale();
            }, DispatcherPriority.Background);
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

            StatsTitleRow.Height = new GridLength(0);
            StatsTeamsRow.Height = new GridLength(26, GridUnitType.Star);
            StatsBodyRow.Height = new GridLength(123, GridUnitType.Star);
            StatsTitlePanel.IsVisible = false;

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
            StatsTitleAccent.GradientStops.Add(new GradientStop(Avalonia.Media.Colors.Transparent, 0.5));
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
            StatsHomeGlow.Color = Avalonia.Media.Color.FromArgb(0xFF, homeColor.R, homeColor.G, homeColor.B);
            StatsAwayGlow.Color = Avalonia.Media.Color.FromArgb(0xFF, awayColor.R, awayColor.G, awayColor.B);
            // The header is intentionally compact. Use the configured scoreboard
            // abbreviations so both teams remain identifiable at oval distance;
            // full names are already represented by the logos and match state.
            StatsHomeTeamText.Text = CompactHeaderName(match.HomeName, match.HomeAbbr);
            StatsAwayTeamText.Text = CompactHeaderName(match.AwayName, match.AwayAbbr);
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

            Color splitLabelInk = SmartContrast.PickSplitBarForeground(
                homeColor, awayColor, Color.FromRgb(0x48, 0x4F, 0x58));
            foreach (TextBlock label in new[] { ShotsBarLabel, AccuracyBarLabel, TimeBarLabel, LeadBarLabel })
            {
                label.Foreground = new SolidColorBrush(splitLabelInk);
                if (label.Effect is Avalonia.Media.DropShadowEffect shadow)
                {
                    shadow.Color = splitLabelInk == Avalonia.Media.Colors.Black
                        ? Avalonia.Media.Colors.White
                        : Avalonia.Media.Colors.Black;
                    shadow.Opacity = 0.92;
                }
            }

            QueueResponsiveLayout();
        }

        private static void SetGlowColor(Avalonia.Controls.TextBlock tb, Color color)
        {
            if (tb.Effect is Avalonia.Media.DropShadowEffect glow)
                glow.Color = Avalonia.Media.Color.FromArgb(0xFF, color.R, color.G, color.B);
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

        private static string CompactHeaderName(string? name, string? abbreviation)
        {
            string value = string.IsNullOrWhiteSpace(name) ? "TEAM" : name.Trim().ToUpperInvariant();
            foreach (string suffix in new[] { " FOOTBALL CLUB", " AUSTRALIAN FOOTBALL CLUB" })
            {
                if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    value = value[..^suffix.Length].TrimEnd();
                    break;
                }
            }

            return value.Length <= 15 || string.IsNullOrWhiteSpace(abbreviation)
                ? value
                : abbreviation.Trim().ToUpperInvariant();
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

        private static void ApplyLogo(Avalonia.Controls.Image image, Ellipse fallback, string? path, bool hideLogos = false)
        {
            if (hideLogos)
            {
                image.Source = null;
                image.IsVisible = false;
                fallback.IsVisible = false;
                return;
            }

            var source = Services.ImageLoadHelper.LoadTrimmed(path);
            image.Source = source;
            image.IsVisible = source is not null ? true : false;
            fallback.IsVisible = source is not null ? false : true;
        }
    }
}

namespace Roche_Scoreboard.Views
{
public partial class StatsScreenControl
{
    private LinearGradientBrush StatsBgGradient => (LinearGradientBrush)ProductionNames.RegistryFor(this)["StatsBgGradient"];
    private RowDefinition StatsTitleRow => (RowDefinition)ProductionNames.RegistryFor(this)["StatsTitleRow"];
    private RowDefinition StatsTeamsRow => (RowDefinition)ProductionNames.RegistryFor(this)["StatsTeamsRow"];
    private RowDefinition StatsBodyRow => (RowDefinition)ProductionNames.RegistryFor(this)["StatsBodyRow"];
    private LinearGradientBrush StatsTitleAccent => (LinearGradientBrush)ProductionNames.RegistryFor(this)["StatsTitleAccent"];
    private DropShadowEffect StatsHomeGlow => (DropShadowEffect)ProductionNames.RegistryFor(this)["StatsHomeGlow"];
    private DropShadowEffect StatsAwayGlow => (DropShadowEffect)ProductionNames.RegistryFor(this)["StatsAwayGlow"];
    private SolidColorBrush HomeValueBrush1 => (SolidColorBrush)ProductionNames.RegistryFor(this)["HomeValueBrush1"];
    private SolidColorBrush ShotsBarBorder => (SolidColorBrush)ProductionNames.RegistryFor(this)["ShotsBarBorder"];
    private ColumnDefinition ShotsHomeCol => (ColumnDefinition)ProductionNames.RegistryFor(this)["ShotsHomeCol"];
    private ColumnDefinition ShotsAwayCol => (ColumnDefinition)ProductionNames.RegistryFor(this)["ShotsAwayCol"];
    private SolidColorBrush ShotHomeBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["ShotHomeBrush"];
    private SolidColorBrush ShotAwayBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["ShotAwayBrush"];
    private SolidColorBrush AwayValueBrush1 => (SolidColorBrush)ProductionNames.RegistryFor(this)["AwayValueBrush1"];
    private SolidColorBrush HomeValueBrush2 => (SolidColorBrush)ProductionNames.RegistryFor(this)["HomeValueBrush2"];
    private SolidColorBrush AccBarBorder => (SolidColorBrush)ProductionNames.RegistryFor(this)["AccBarBorder"];
    private ColumnDefinition AccHomeCol => (ColumnDefinition)ProductionNames.RegistryFor(this)["AccHomeCol"];
    private ColumnDefinition AccAwayCol => (ColumnDefinition)ProductionNames.RegistryFor(this)["AccAwayCol"];
    private SolidColorBrush AccHomeBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["AccHomeBrush"];
    private SolidColorBrush AccAwayBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["AccAwayBrush"];
    private SolidColorBrush AwayValueBrush2 => (SolidColorBrush)ProductionNames.RegistryFor(this)["AwayValueBrush2"];
    private SolidColorBrush HomeValueBrush3 => (SolidColorBrush)ProductionNames.RegistryFor(this)["HomeValueBrush3"];
    private SolidColorBrush TimeBarBorder => (SolidColorBrush)ProductionNames.RegistryFor(this)["TimeBarBorder"];
    private ColumnDefinition TimeHomeCol => (ColumnDefinition)ProductionNames.RegistryFor(this)["TimeHomeCol"];
    private ColumnDefinition TimeTiedCol => (ColumnDefinition)ProductionNames.RegistryFor(this)["TimeTiedCol"];
    private ColumnDefinition TimeAwayCol => (ColumnDefinition)ProductionNames.RegistryFor(this)["TimeAwayCol"];
    private SolidColorBrush TimeHomeBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["TimeHomeBrush"];
    private SolidColorBrush TimeTiedBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["TimeTiedBrush"];
    private SolidColorBrush TimeAwayBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["TimeAwayBrush"];
    private SolidColorBrush AwayValueBrush3 => (SolidColorBrush)ProductionNames.RegistryFor(this)["AwayValueBrush3"];
    private SolidColorBrush HomeValueBrush4 => (SolidColorBrush)ProductionNames.RegistryFor(this)["HomeValueBrush4"];
    private SolidColorBrush LeadBarBorder => (SolidColorBrush)ProductionNames.RegistryFor(this)["LeadBarBorder"];
    private ColumnDefinition LeadHomeCol => (ColumnDefinition)ProductionNames.RegistryFor(this)["LeadHomeCol"];
    private ColumnDefinition LeadAwayCol => (ColumnDefinition)ProductionNames.RegistryFor(this)["LeadAwayCol"];
    private SolidColorBrush LeadHomeBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["LeadHomeBrush"];
    private SolidColorBrush LeadAwayBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["LeadAwayBrush"];
    private SolidColorBrush AwayValueBrush4 => (SolidColorBrush)ProductionNames.RegistryFor(this)["AwayValueBrush4"];
}
}

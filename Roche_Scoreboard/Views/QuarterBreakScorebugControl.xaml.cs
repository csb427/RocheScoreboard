using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Color = System.Windows.Media.Color;
using UserControl = System.Windows.Controls.UserControl;

namespace Roche_Scoreboard.Views;

public partial class QuarterBreakScorebugControl : UserControl
{
    private readonly DispatcherTimer _clockTimer;
    private bool _showTopBar = true;
    private double? _matchedTopBarHeight;

    public QuarterBreakScorebugControl()
    {
        InitializeComponent();
        UpdateWallClock();
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateWallClock();
        _clockTimer.Start();
        SizeChanged += (_, _) => ApplyTopBarLayout();
        Loaded += (_, _) =>
        {
            UpdateWallClock();
            _clockTimer.Start();
        };
        Unloaded += (_, _) => _clockTimer.Stop();
    }

    public IReadOnlyList<FrameworkElement> GetFoldPanels()
        // Content panels are children of the colour wings. Returning both made
        // each side receive two transforms during the same transition, which
        // caused the uneven, jerky double-fold seen on repeat switches.
        => [HomeColourWing, BreakCentrePanel, AwayColourWing];

    public double GetTransitionTopBarHeight()
        => Math.Max(0, TopInfoRow.ActualHeight);

    public void SetTopBarVisible(bool visible, double? matchedHeight = null)
    {
        _showTopBar = visible;
        _matchedTopBarHeight = visible && matchedHeight is > 0 ? matchedHeight : null;
        ApplyTopBarLayout();
    }

    private void ApplyTopBarLayout()
    {
        TopInfoBorder.Visibility = _showTopBar ? Visibility.Visible : Visibility.Collapsed;
        TopInfoRow.Height = _showTopBar
            ? new GridLength(_matchedTopBarHeight ?? Math.Max(
                Math.Clamp(ActualHeight * 0.145, 16, 32),
                ActualHeight * (58.0 / 540.0)))
            : new GridLength(0);
    }

    public void SetFinalsMode(bool enabled)
    {
        BreakLabelText.Foreground = new SolidColorBrush(enabled ? FinalsPalette.GoldLight : Colors.White);
    }

    public void Populate(MatchManager match, int endedQuarter,
        string homeColorHex, string awayColorHex,
        string homeSecondaryHex, string awaySecondaryHex,
        string? homeLogoPath, string? awayLogoPath,
        WeatherSnapshot? weather)
    {
        Color home = SafeColor(homeColorHex, Colors.Firebrick);
        Color away = SafeColor(awayColorHex, Colors.RoyalBlue);
        Color homeSecondary = SafeColor(homeSecondaryHex, Colors.White);
        Color awaySecondary = SafeColor(awaySecondaryHex, Colors.White);

        HomeGradientLight.Color = Lighten(home, 0.20);
        HomeGradientDark.Color = Darken(home, 0.58);
        AwayGradientLight.Color = Lighten(away, 0.20);
        AwayGradientDark.Color = Darken(away, 0.58);

        HomeScoreText.Text = match.HomeTotal.ToString();
        AwayScoreText.Text = match.AwayTotal.ToString();
        HomeScoreDetailText.Text = $"{match.HomeGoals}.{match.HomeBehinds}";
        AwayScoreDetailText.Text = $"{match.AwayGoals}.{match.AwayBehinds}";
        HomeScoreText.Foreground = new SolidColorBrush(
            SmartContrast.PickReadableForeground(homeSecondary, Darken(home, 0.36)));
        AwayScoreText.Foreground = new SolidColorBrush(
            SmartContrast.PickReadableForeground(awaySecondary, Darken(away, 0.36)));
        HomeScoreDetailText.Foreground = HomeScoreText.Foreground;
        AwayScoreDetailText.Foreground = AwayScoreText.Foreground;

        HomeWatermark.Source = ImageLoadHelper.LoadTrimmed(homeLogoPath);
        AwayWatermark.Source = ImageLoadHelper.LoadTrimmed(awayLogoPath);
        HomeWatermark.Visibility = HomeWatermark.Source is null ? Visibility.Collapsed : Visibility.Visible;
        AwayWatermark.Visibility = AwayWatermark.Source is null ? Visibility.Collapsed : Visibility.Visible;

        bool liveQuarter = match.ClockRunning || match.ElapsedInQuarter > TimeSpan.Zero;
        BreakLabelText.Text = liveQuarter
            ? QuarterLabelService.GetQuarterLabel(Math.Clamp(match.Quarter, 1, 4))
            : QuarterLabelService.GetBreakLabel(Math.Clamp(endedQuarter, 1, 4));
        TopWeatherText.Text = weather is null
            ? "--°"
            : $"{(string.IsNullOrWhiteSpace(weather.Icon) ? string.Empty : weather.Icon + " ")}{weather.CurrentTemp:0}°";
    }

    private void UpdateWallClock()
    {
        DateTime now = DateTime.Now;
        TopTimeText.Text = now.ToString("h:mm tt");
        TopDateText.Text = now.ToString("ddd d MMM");
    }

    private static Color SafeColor(string? value, Color fallback)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            return (Color)System.Windows.Media.ColorConverter.ConvertFromString(value.StartsWith('#') ? value : "#" + value);
        }
        catch { return fallback; }
    }

    private static Color Lighten(Color color, double amount)
        => Color.FromRgb(
            (byte)(color.R + ((255 - color.R) * amount)),
            (byte)(color.G + ((255 - color.G) * amount)),
            (byte)(color.B + ((255 - color.B) * amount)));

    private static Color Darken(Color color, double amount)
        => Color.FromRgb(
            (byte)(color.R * (1 - amount)),
            (byte)(color.G * (1 - amount)),
            (byte)(color.B * (1 - amount)));
}

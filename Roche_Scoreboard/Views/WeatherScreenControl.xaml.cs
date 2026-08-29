using System;
using System.Windows.Controls;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using UserControl = System.Windows.Controls.UserControl;

namespace Roche_Scoreboard.Views;

public partial class WeatherScreenControl : UserControl
{
    private readonly DispatcherTimer _wallClockTimer;
    private bool _isDisposed;

    public void SetFinalsMode(bool enabled)
    {
        WeatherTitlePanel.Background = enabled ? FinalsPalette.PanelBrush() : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0D, 0x11, 0x17));
        WeatherTitleText.Foreground = enabled
            ? new System.Windows.Media.SolidColorBrush(FinalsPalette.Ink)
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF0, 0xC0, 0x40));
        LocationText.Foreground = enabled
            ? new System.Windows.Media.SolidColorBrush(FinalsPalette.Ink)
            : System.Windows.Media.Brushes.White;
        ScoreBar?.SetFinalsMode(enabled);
    }

    public void PlayScoreAnimation(ScoreEvent scoreEvent)
        => ScoreBar?.PlayScoreAnimation(scoreEvent);

    public WeatherScreenControl()
    {
        InitializeComponent();

        WeatherTimeText.Text = DateTime.Now.ToString("h:mm tt");
        _wallClockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _wallClockTimer.Tick += OnWallClockTick;
        _wallClockTimer.Start();

        SizeChanged += (_, _) => ApplyResponsiveLayout();

        Unloaded += (_, _) => Cleanup();
    }

    private void ApplyResponsiveLayout()
    {
        ResponsivePresentationTypography.Apply(
            this, ScoreboardScaleHelper.GetScale(ActualWidth, ActualHeight));

        bool compact = ActualWidth < 320 || ActualHeight < 160;
        WeatherTimeText.Visibility = compact
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;
        WeatherStatsPanel.Visibility = compact
            ? System.Windows.Visibility.Collapsed
            : WeatherDataPanel.Visibility;
        WeatherHeroRow.Height = new System.Windows.GridLength(
            compact ? 1 : 87, System.Windows.GridUnitType.Star);
        WeatherStatsRow.Height = compact
            ? new System.Windows.GridLength(0)
            : new System.Windows.GridLength(42, System.Windows.GridUnitType.Star);
    }

    private void OnWallClockTick(object? sender, EventArgs e)
    {
        WeatherTimeText.Text = DateTime.Now.ToString("h:mm tt");
    }

    private void Cleanup()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _wallClockTimer.Stop();
    }

    /// <summary>
    /// Populate the simplified weather screen (single hero panel) from snapshot data and current match state.
    /// </summary>
    public void Populate(WeatherSnapshot? snapshot, string? locationName,
        MatchManager match, int endedQuarter,
        string homeColorHex, string awayColorHex,
        string homeSecondaryHex, string awaySecondaryHex,
        string? homeLogoPath, string? awayLogoPath,
        string? barTitleOverride = null)
    {
        ArgumentNullException.ThrowIfNull(match);

        LocationText.Text = string.IsNullOrWhiteSpace(locationName)
            ? "WEATHER"
            : locationName.ToUpperInvariant();

        bool hasWeather = snapshot is not null;
        WeatherDataPanel.Visibility = hasWeather
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        WeatherStatsPanel.Visibility = hasWeather
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        WeatherUnavailablePanel.Visibility = hasWeather
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

        if (snapshot is not null)
        {
            // Strip the U+FE0F emoji-presentation variation selector so WPF uses
            // the monochrome glyph from Segoe UI Symbol and honours the bright
            // Foreground brush we set in XAML. Otherwise the colour emoji
            // bitmap renders very dark on the #0D1117 background.
            CurrentIcon.Text = snapshot.Icon?.Replace("\uFE0F", string.Empty);
            CurrentTemp.Text = $"{snapshot.CurrentTemp:0}°";
            CurrentDescription.Text = snapshot.Description;
            FeelsLikeTemp.Text = $"{snapshot.FeelsLike:0}\u00B0";
            DayMinTemp.Text = $"{snapshot.DayMin:0}\u00B0";
            DayMaxTemp.Text = $"{snapshot.DayMax:0}\u00B0";
        }

        ScoreBar.Populate(match, endedQuarter, homeColorHex, awayColorHex,
            homeSecondaryHex, awaySecondaryHex, homeLogoPath, awayLogoPath,
            barTitleOverride);
    }
}

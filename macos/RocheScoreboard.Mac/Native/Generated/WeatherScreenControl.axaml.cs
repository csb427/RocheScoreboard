using RocheScoreboard.Mac.Production;
// Generated from Views/WeatherScreenControl.xaml.cs; edit translator/adapters.
// Source SHA-256: da5a0364d9f03a55c6a2c40485be0d9ce0597474f5b5818afe97412494c06efb
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
using Roche_Scoreboard.Services;
using UserControl = Avalonia.Controls.UserControl;

namespace Roche_Scoreboard.Views;

public partial class WeatherScreenControl : UserControl
{
    private readonly DispatcherTimer _wallClockTimer;
    private bool _isDisposed;

    public void SetFinalsMode(bool enabled)
    {
        WeatherTitlePanel.Background = enabled ? FinalsPalette.PanelBrush() : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x0D, 0x11, 0x17));
        WeatherTitleText.Foreground = enabled
            ? new Avalonia.Media.SolidColorBrush(FinalsPalette.Ink)
            : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0xF0, 0xC0, 0x40));
        LocationText.Foreground = enabled
            ? new Avalonia.Media.SolidColorBrush(FinalsPalette.Ink)
            : Avalonia.Media.Brushes.White;
        ScoreBar?.SetFinalsMode(enabled);
    }

    public void PlayScoreAnimation(ScoreEvent scoreEvent)
        => ScoreBar?.PlayScoreAnimation(scoreEvent);

    public WeatherScreenControl()
    {
        NativePointer.EnsureInitialized(); ProductionNames.BeginLoad(this); try { InitializeComponent(); } finally { ProductionNames.CompleteLoad(this); }

        WeatherTimeText.Text = DateTime.Now.ToString("h:mm tt");
        _wallClockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _wallClockTimer.Tick += OnWallClockTick;
        _wallClockTimer.Start();

        SizeChanged += (_, _) => ApplyResponsiveLayout();
        Loaded += (_, _) =>
        {
            _isDisposed = false;
            _wallClockTimer.Start();
            ApplyResponsiveLayout();
        };

        Unloaded += (_, _) => Cleanup();
    }

    private void ApplyResponsiveLayout()
    {
        ResponsivePresentationTypography.Apply(
            this, ScoreboardScaleHelper.GetScale(Bounds.Width, Bounds.Height));

        bool compact = Bounds.Width < 320 || Bounds.Height < 160;
        WeatherTimeText.IsVisible = compact
            ? false
            : true;
        WeatherStatsPanel.IsVisible = WeatherDataPanel.IsVisible;
        WeatherHeroRow.Height = new Avalonia.Controls.GridLength(78, Avalonia.Controls.GridUnitType.Star);
        WeatherStatsRow.Height = new Avalonia.Controls.GridLength(51, Avalonia.Controls.GridUnitType.Star);
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
        WeatherTitleRow.Height = new Avalonia.Controls.GridLength(0);
        WeatherBodyRow.Height = new Avalonia.Controls.GridLength(149, Avalonia.Controls.GridUnitType.Star);
        WeatherTitlePanel.IsVisible = false;
        ArgumentNullException.ThrowIfNull(match);

        LocationText.Text = string.IsNullOrWhiteSpace(locationName)
            ? "WEATHER"
            : locationName.ToUpperInvariant();

        bool hasWeather = snapshot is not null;
        WeatherDataPanel.IsVisible = hasWeather
            ? true
            : false;
        WeatherStatsPanel.IsVisible = hasWeather
            ? true
            : false;
        WeatherUnavailablePanel.IsVisible = hasWeather
            ? false
            : true;

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

public partial class WeatherScreenControl
{
    private RowDefinition WeatherTitleRow => (RowDefinition)ProductionNames.RegistryFor(this)["WeatherTitleRow"];
    private RowDefinition WeatherBodyRow => (RowDefinition)ProductionNames.RegistryFor(this)["WeatherBodyRow"];
    private RowDefinition WeatherHeroRow => (RowDefinition)ProductionNames.RegistryFor(this)["WeatherHeroRow"];
    private RowDefinition WeatherStatsRow => (RowDefinition)ProductionNames.RegistryFor(this)["WeatherStatsRow"];
}

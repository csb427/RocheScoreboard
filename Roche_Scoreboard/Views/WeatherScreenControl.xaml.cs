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

    public WeatherScreenControl()
    {
        InitializeComponent();

        WeatherTimeText.Text = DateTime.Now.ToString("h:mm tt");
        _wallClockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _wallClockTimer.Tick += OnWallClockTick;
        _wallClockTimer.Start();

        Unloaded += (_, _) => Cleanup();
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
    public void Populate(WeatherSnapshot snapshot, string? locationName,
        MatchManager match, int endedQuarter,
        string homeColorHex, string awayColorHex,
        string homeSecondaryHex, string awaySecondaryHex,
        string? homeLogoPath, string? awayLogoPath,
        string? barTitleOverride = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(match);

        LocationText.Text = string.IsNullOrWhiteSpace(locationName)
            ? "WEATHER"
            : locationName.ToUpperInvariant();

        CurrentIcon.Text = snapshot.Icon;
        CurrentTemp.Text = $"{snapshot.CurrentTemp:0}°";
        CurrentDescription.Text = snapshot.Description;
        FeelsLikeTemp.Text = $"{snapshot.FeelsLike:0}\u00B0";
        DayMinTemp.Text = $"{snapshot.DayMin:0}\u00B0";
        DayMaxTemp.Text = $"{snapshot.DayMax:0}\u00B0";

        ScoreBar.Populate(match, endedQuarter, homeColorHex, awayColorHex,
            homeSecondaryHex, awaySecondaryHex, homeLogoPath, awayLogoPath,
            barTitleOverride);
    }
}

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
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

        SizeChanged += (_, _) => ApplyResponsiveScale();

        Unloaded += (_, _) => Cleanup();
    }

    private double ScaleUnit => ScoreboardScaleHelper.GetScale(ActualWidth, ActualHeight);

    private double ScalePx(double designValue)
        => ScoreboardScaleHelper.Scale(designValue, ScaleUnit);

    private Thickness ScaleThickness(Thickness designThickness)
        => ScoreboardScaleHelper.Scale(designThickness, ScaleUnit);

    /// <summary>
    /// Chrome rows are star-proportional in XAML; this method is
    /// kept for compatibility with size-changed wiring.
    /// </summary>
    private void ApplyResponsiveScale()
    {
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
    /// Populate the weather screen from snapshot data and current match state.
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

        // Location label
        LocationText.Text = string.IsNullOrWhiteSpace(locationName)
            ? "WEATHER"
            : locationName.ToUpperInvariant();

        // Current conditions
        CurrentIcon.Text = snapshot.Icon;
        CurrentTemp.Text = $"{snapshot.CurrentTemp:0}°";
        CurrentDescription.Text = snapshot.Description;
        FeelsLikeTemp.Text = $"{snapshot.FeelsLike:0}°";
        DayMinTemp.Text = $"{snapshot.DayMin:0}°";
        DayMaxTemp.Text = $"{snapshot.DayMax:0}°";

        // Build hourly forecast columns
        BuildHourlyForecast(snapshot);

        // Bottom score bar
        ScoreBar.Populate(match, endedQuarter, homeColorHex, awayColorHex,
            homeSecondaryHex, awaySecondaryHex, homeLogoPath, awayLogoPath,
            barTitleOverride);
    }

    private void BuildHourlyForecast(WeatherSnapshot snapshot)
    {
        HourlyGrid.Children.Clear();
        RainGrid.Children.Clear();

        double iconBoxSize = 16;
        double iconFontSize = 12;
        double timeFontSize = 8;
        double tempFontSize = 11;
        double descFontSize = 7;
        double descMaxWidth = 36;
        double rainMmFontSize = 7;
        double rainPctFontSize = 8;
        double rainLabelBlur = 2;

        foreach (HourlyForecast hour in snapshot.HourlyForecast)
        {
            // ── Hourly column: time, icon, temp ──
            var column = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Time label
            column.Children.Add(new TextBlock
            {
                Text = hour.Time.ToString("h tt"),
                Foreground = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)),
                FontSize = timeFontSize,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = ScaleThickness(new Thickness(0, 0, 0, 4))
            });

            // Weather icon
            var iconVb = new Viewbox
            {
                Height = iconBoxSize,
                Width = iconBoxSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = ScaleThickness(new Thickness(0, 0, 0, 2))
            };
            iconVb.Child = new TextBlock
            {
                Text = hour.Icon,
                FontSize = iconFontSize,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            column.Children.Add(iconVb);

            // Temperature
            column.Children.Add(new TextBlock
            {
                Text = $"{hour.Temperature:0}°",
                Foreground = Brushes.White,
                FontSize = tempFontSize,
                FontWeight = FontWeights.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = ScaleThickness(new Thickness(0, 2, 0, 2))
            });

            // Short description
            column.Children.Add(new TextBlock
            {
                Text = hour.Description,
                Foreground = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                FontSize = descFontSize,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = descMaxWidth
            });

            // Rain amount if > 0
            if (hour.PrecipitationMm > 0)
            {
                column.Children.Add(new TextBlock
                {
                    Text = $"{hour.PrecipitationMm:0.#}mm",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x99, 0xDD)),
                    FontSize = rainMmFontSize,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = ScaleThickness(new Thickness(0, 1, 0, 0))
                });
            }

            HourlyGrid.Children.Add(column);

            // ── Rain probability bar ──
            var rainCell = new Grid { Margin = ScaleThickness(new Thickness(3, 0, 3, 0)) };

            // Background track
            rainCell.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(ScalePx(3)),
                Background = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Stretch
            });

            // Filled portion
            double pct = Math.Clamp(hour.PrecipitationProbability / 100.0, 0, 1);
            var fillColor = GetRainBarColor(hour.PrecipitationProbability);
            var fill = new Border
            {
                CornerRadius = new CornerRadius(ScalePx(3)),
                Background = new SolidColorBrush(fillColor),
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 0
            };

            // Use a grid row trick for the fill height
            var innerGrid = new Grid();
            innerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1 - pct, GridUnitType.Star) });
            innerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(pct, 0.01), GridUnitType.Star) });

            var emptyTop = new Border();
            Grid.SetRow(emptyTop, 0);
            innerGrid.Children.Add(emptyTop);

            var fillBar = new Border
            {
                CornerRadius = new CornerRadius(ScalePx(3)),
                Background = new SolidColorBrush(fillColor)
            };
            Grid.SetRow(fillBar, 1);
            innerGrid.Children.Add(fillBar);

            rainCell.Children.Add(innerGrid);

            // Percentage label
            rainCell.Children.Add(new TextBlock
            {
                Text = $"{hour.PrecipitationProbability}%",
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                FontSize = rainPctFontSize,
                FontWeight = FontWeights.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = ScaleThickness(new Thickness(0, 0, 0, 2)),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = rainLabelBlur,
                    ShadowDepth = 0,
                    Color = Colors.Black,
                    Opacity = 0.8
                }
            });

            RainGrid.Children.Add(rainCell);
        }
    }

    private static Color GetRainBarColor(int probability) => probability switch
    {
        < 20 => Color.FromArgb(0x60, 0x44, 0x88, 0xAA),
        < 40 => Color.FromArgb(0x80, 0x44, 0x99, 0xCC),
        < 60 => Color.FromArgb(0xA0, 0x44, 0x88, 0xDD),
        < 80 => Color.FromArgb(0xC0, 0x33, 0x77, 0xEE),
        _ => Color.FromArgb(0xDD, 0x22, 0x66, 0xFF)
    };
}

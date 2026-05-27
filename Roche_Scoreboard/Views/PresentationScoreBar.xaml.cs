using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Image = System.Windows.Controls.Image;
using UserControl = System.Windows.Controls.UserControl;

namespace Roche_Scoreboard.Views;

/// <summary>
/// Broadcast-style score bar mirroring the main scorebug layout:
/// team-colour gradient panels, dark centre quarter/clock panel,
/// scorebug-style separator splits, and Stolzl typography.
/// </summary>
public partial class PresentationScoreBar : UserControl
{
    private MatchManager? _match;
    private bool _showTimer = true;
    private readonly DispatcherTimer _clockTimer;

    public PresentationScoreBar()
    {
        InitializeComponent();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _clockTimer.Tick += OnClockTick;

        Loaded += (_, _) => _clockTimer.Start();
        Unloaded += (_, _) => _clockTimer.Stop();
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        if (_match is null || !_showTimer) return;
        TimeSpan dc = _match.DisplayClock;
        TimerText.Text = $"{(int)dc.TotalMinutes:D2}:{dc.Seconds:D2}";
    }

    /// <summary>
    /// Populate the score bar with match state, team colours, and logos.
    /// When <paramref name="showTimer"/> is false (quarter breaks), only the
    /// quarter label is displayed in the centre panel.
    /// </summary>
    public void Populate(MatchManager match, int endedQuarter,
        string homeColorHex, string awayColorHex,
        string homeSecondaryHex, string awaySecondaryHex,
        string? homeLogoPath, string? awayLogoPath,
        string? barTitleOverride = null,
        bool showTimer = true)
    {
        ArgumentNullException.ThrowIfNull(match);
        _match = match;

        Color homeColor = SafeColor(homeColorHex, "#C9483B");
        Color awayColor = SafeColor(awayColorHex, "#B68F12");
        _ = homeSecondaryHex;
        _ = awaySecondaryHex;

        // Panel gradients — same dark->bright sweep as the main scorebug.
        Color homeDark = Darken(homeColor, 0.55);
        Color awayDark = Darken(awayColor, 0.55);

        HomePanelGradStart.Color = homeDark;
        HomePanelGradEnd.Color   = homeColor;
        AwayPanelGradStart.Color = awayColor;
        AwayPanelGradEnd.Color   = awayDark;

        // Logo cell gradients match the adjacent panel edge so the seam
        // reads as a single coloured strip across home / logo and logo / away.
        HomeLogoCellGradStart.Color = homeDark;
        HomeLogoCellGradEnd.Color   = homeColor;
        AwayLogoCellGradStart.Color = awayColor;
        AwayLogoCellGradEnd.Color   = awayDark;

        // Score / total values
        HomeGoalsText.Text   = match.HomeGoals.ToString();
        HomeBehindsText.Text = match.HomeBehinds.ToString();
        HomeTotalText.Text   = match.HomeTotal.ToString();
        AwayGoalsText.Text   = match.AwayGoals.ToString();
        AwayBehindsText.Text = match.AwayBehinds.ToString();
        AwayTotalText.Text   = match.AwayTotal.ToString();

        // Smart contrast: pick readable text against each panel gradient.
        var homeFg = new SolidColorBrush(Services.SmartContrast.PickForeground(
            Services.SmartContrast.ContrastRatio(homeDark, Colors.White) < Services.SmartContrast.ContrastRatio(homeColor, Colors.White) ? homeDark : homeColor));
        var awayFg = new SolidColorBrush(Services.SmartContrast.PickForeground(
            Services.SmartContrast.ContrastRatio(awayDark, Colors.White) < Services.SmartContrast.ContrastRatio(awayColor, Colors.White) ? awayDark : awayColor));
        HomeGoalsText.Foreground = homeFg;
        HomeBehindsText.Foreground = homeFg;
        HomeTotalText.Foreground = homeFg;
        AwayGoalsText.Foreground = awayFg;
        AwayBehindsText.Foreground = awayFg;
        AwayTotalText.Foreground = awayFg;
        HomeLogoFallback.Foreground = homeFg;
        AwayLogoFallback.Foreground = awayFg;

        // Logo fallback letters
        HomeLogoFallback.Text = match.HomeAbbr.ToUpperInvariant();
        AwayLogoFallback.Text = match.AwayAbbr.ToUpperInvariant();

        // Centre quarter label
        QuarterText.Text = !string.IsNullOrEmpty(barTitleOverride)
            ? barTitleOverride
            : Services.QuarterLabelService.GetBreakLabel(endedQuarter);

        // Timer behaviour - hidden at quarter breaks. The bar additionally
        // auto-hides the timer whenever the match is sitting on a break
        // (clock stopped and no elapsed time in the current quarter) so the
        // scoreworm / weather / stats screens don't display a stale "00:00"
        // during the same window the break screen suppresses it.
        bool atBreak = !match.ClockRunning && match.ElapsedInQuarter <= TimeSpan.Zero;
        _showTimer = showTimer && !atBreak;
        if (_showTimer)
        {
            TimeSpan dc = match.DisplayClock;
            TimerText.Text = $"{(int)dc.TotalMinutes:D2}:{dc.Seconds:D2}";
            TimerText.Visibility = Visibility.Visible;
        }
        else
        {
            TimerText.Visibility = Visibility.Collapsed;
        }

        // Logos - hide both placeholders if either logo failed to load.
        bool hideLogos = Services.ImageLoadHelper.Load(homeLogoPath) is null
                      || Services.ImageLoadHelper.Load(awayLogoPath) is null;
        ApplyLogo(HomeLogoImage, HomeLogoFallback, homeLogoPath, hideLogos);
        ApplyLogo(AwayLogoImage, AwayLogoFallback, awayLogoPath, hideLogos);
    }

    private static void ApplyLogo(Image image, TextBlock fallback, string? path, bool hideLogos)
    {
        if (hideLogos)
        {
            image.Source = null;
            image.Visibility = Visibility.Collapsed;
            fallback.Visibility = Visibility.Collapsed;
            return;
        }

        ImageSource? source = Services.ImageLoadHelper.LoadTrimmed(path);
        image.Source = source;
        image.Visibility    = source is not null ? Visibility.Visible : Visibility.Collapsed;
        fallback.Visibility = source is not null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static Color SafeColor(string? hex, string fallbackHex)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex))
                hex = fallbackHex;
            if (!hex.StartsWith('#'))
                hex = "#" + hex;
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return (Color)ColorConverter.ConvertFromString(fallbackHex);
        }
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

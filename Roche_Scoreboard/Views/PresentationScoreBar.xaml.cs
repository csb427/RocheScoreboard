using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Image = System.Windows.Controls.Image;
using UserControl = System.Windows.Controls.UserControl;

namespace Roche_Scoreboard.Views;

/// <summary>
/// Broadcast-style score bar — dark background with team-colour accent stripes,
/// labelled goals/behinds, and team-tinted total badges.
/// </summary>
public partial class PresentationScoreBar : UserControl
{
    private MatchManager? _match;
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
        if (_match == null) return;
        TimeSpan dc = _match.DisplayClock;
        TimerText.Text = $"{(int)dc.TotalMinutes:D2}:{dc.Seconds:D2}";
    }

    /// <summary>
    /// Populate the score bar with match state, team colours, and logos.
    /// </summary>
    public void Populate(MatchManager match, int endedQuarter,
        string homeColorHex, string awayColorHex,
        string homeSecondaryHex, string awaySecondaryHex,
        string? homeLogoPath, string? awayLogoPath,
        string? barTitleOverride = null)
    {
        ArgumentNullException.ThrowIfNull(match);
        _match = match;

        Color homeColor = SafeColor(homeColorHex, "#4488FF");
        Color awayColor = SafeColor(awayColorHex, "#FF6644");
        Color homeSecondary = SafeColor(homeSecondaryHex, "#FFFFFF");
        Color awaySecondary = SafeColor(awaySecondaryHex, "#FFFFFF");
        Color homeLift = LiftForDarkBg(homeColor);
        Color awayLift = LiftForDarkBg(awayColor);

        // Top accent gradient: home → centre → away
        BarTopAccent.GradientStops[0].Color = homeColor;
        BarTopAccent.GradientStops[2].Color = awayColor;

        // Section borders — bold team colour
        HomeBgBrush.Color = homeColor;
        AwayBgBrush.Color = awayColor;

        // Subtle team-tinted section backgrounds
        HomeSectionBg.Color = Color.FromArgb(0x18, homeColor.R, homeColor.G, homeColor.B);
        AwaySectionBg.Color = Color.FromArgb(0x18, awayColor.R, awayColor.G, awayColor.B);

        // Abbreviation text — team coloured with glow
        HomeAbbrBrush.Color = homeLift;
        AwayAbbrBrush.Color = awayLift;
        HomeAbbrGlow.Color = homeColor;
        AwayAbbrGlow.Color = awayColor;

        // Total — white text, team-coloured border and glow. Border falls
        // back to secondary if primary is too dark to register against the
        // bar's dark background.
        HomeTotalFg.Color = Colors.White;
        AwayTotalFg.Color = Colors.White;
        Color barBg = Color.FromRgb(0x0D, 0x11, 0x17);
        HomeTotalBorderBrush.Color = Services.ContrastHelper.GetVisibleBorderColor(barBg, homeColor, homeSecondary);
        AwayTotalBorderBrush.Color = Services.ContrastHelper.GetVisibleBorderColor(barBg, awayColor, awaySecondary);
        HomeTotalGlow.Color = homeColor;
        AwayTotalGlow.Color = awayColor;

        // Goals/behinds chips — team-coloured borders + glow on the goals chip
        // for a punchier broadcast look. Behinds chip uses the same border
        // colour but a more muted background so goals read as the headline.
        HomeGoalsChipBorder.Color = homeColor;
        AwayGoalsChipBorder.Color = awayColor;
        HomeGoalsChipGlow.Color = homeColor;
        AwayGoalsChipGlow.Color = awayColor;
        HomeBehindsChipBorder.Color = Color.FromArgb(0xB0, homeColor.R, homeColor.G, homeColor.B);
        AwayBehindsChipBorder.Color = Color.FromArgb(0xB0, awayColor.R, awayColor.G, awayColor.B);
        HomeGoalsChipBg.Color = Color.FromArgb(0x30, homeColor.R, homeColor.G, homeColor.B);
        AwayGoalsChipBg.Color = Color.FromArgb(0x30, awayColor.R, awayColor.G, awayColor.B);
        HomeBehindsChipBg.Color = Color.FromArgb(0x14, homeColor.R, homeColor.G, homeColor.B);
        AwayBehindsChipBg.Color = Color.FromArgb(0x14, awayColor.R, awayColor.G, awayColor.B);

        // Team abbreviations — the abbreviation slot is short and the bar
        // appears on every presentation screen, so abbreviations read more
        // cleanly than full names regardless of window width.
        HomeAbbrText.Text = match.HomeAbbr.ToUpperInvariant();
        AwayAbbrText.Text = match.AwayAbbr.ToUpperInvariant();

        // Scores
        HomeGoalsText.Text = match.HomeGoals.ToString();
        HomeBehindsText.Text = match.HomeBehinds.ToString();
        HomeTotalText.Text = match.HomeTotal.ToString();
        AwayGoalsText.Text = match.AwayGoals.ToString();
        AwayBehindsText.Text = match.AwayBehinds.ToString();
        AwayTotalText.Text = match.AwayTotal.ToString();

        // Quarter label
        QuarterText.Text = !string.IsNullOrEmpty(barTitleOverride) ? barTitleOverride : endedQuarter switch
        {
            1 => "QT",
            2 => "HT",
            3 => "3QT",
            _ => "FT"
        };

        // Match timer
        TimeSpan dc = match.DisplayClock;
        TimerText.Text = $"{(int)dc.TotalMinutes:D2}:{dc.Seconds:D2}";

        // Logos — hide all placeholders if at least one team has no logo
        bool hideLogos = Services.ImageLoadHelper.Load(homeLogoPath) is null
                      || Services.ImageLoadHelper.Load(awayLogoPath) is null;
        ApplyLogo(HomeLogoImage, HomeLogoFallback, homeLogoPath, hideLogos);
        ApplyLogo(AwayLogoImage, AwayLogoFallback, awayLogoPath, hideLogos);
    }

    private static void ApplyLogo(Image image, Ellipse fallback, string? path, bool hideLogos = false)
    {
        if (hideLogos)
        {
            image.Visibility = Visibility.Collapsed;
            fallback.Visibility = Visibility.Collapsed;
            return;
        }

        var source = Services.ImageLoadHelper.LoadTrimmed(path);
        image.Source = source;
        image.Visibility = source is not null ? Visibility.Visible : Visibility.Collapsed;
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

    /// <summary>
    /// Brightens a colour that would be invisible against the dark bar background.
    /// Blends toward white just enough to keep text readable.
    /// </summary>
    private static Color LiftForDarkBg(Color c)
    {
        int sum = c.R + c.G + c.B;
        if (sum >= 200) return c;

        double t = Math.Max(0.0, (200 - sum) / 400.0);
        return Color.FromRgb(
            (byte)(c.R + (255 - c.R) * t),
            (byte)(c.G + (255 - c.G) * t),
            (byte)(c.B + (255 - c.B) * t));
    }
}

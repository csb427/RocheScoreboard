using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Globalization;
using Roche_Scoreboard.Models;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Image = System.Windows.Controls.Image;
using UserControl = System.Windows.Controls.UserControl;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Roche_Scoreboard.Views;

/// <summary>
/// Distance-first persistent score bar with team-colour panels, protected
/// logos, a live quarter/clock cell and fixed-capacity total typography.
/// </summary>
public partial class PresentationScoreBar : UserControl
{
    private MatchManager? _match;
    private bool _timerAllowed = true;
    private readonly DispatcherTimer _clockTimer;
    private DispatcherTimer? _homeDeltaTimer;
    private DispatcherTimer? _awayDeltaTimer;
    private Color _homeSecondary = Colors.White;
    private Color _awaySecondary = Colors.White;

    public PresentationScoreBar()
    {
        InitializeComponent();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _clockTimer.Tick += OnClockTick;

        Loaded += (_, _) =>
        {
            ApplyLiveMatchState();
            Dispatcher.BeginInvoke(UpdateResponsiveTypography, DispatcherPriority.Loaded);
            _clockTimer.Start();
        };
        Unloaded += (_, _) => _clockTimer.Stop();
        SizeChanged += (_, _) => Dispatcher.BeginInvoke(UpdateResponsiveTypography, DispatcherPriority.Loaded);
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
                Dispatcher.BeginInvoke(UpdateResponsiveTypography, DispatcherPriority.Loaded);
        };
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        ApplyLiveMatchState();
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
        _homeSecondary = SafeColor(homeSecondaryHex, "#FFFFFF");
        _awaySecondary = SafeColor(awaySecondaryHex, "#FFFFFF");

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

        // Smart contrast: pick readable text against each panel gradient.
        var homeFg = new SolidColorBrush(Services.SmartContrast.PickForeground(homeDark, homeColor));
        var awayFg = new SolidColorBrush(Services.SmartContrast.PickForeground(awayDark, awayColor));
        HomeTotalText.Foreground = homeFg;
        AwayTotalText.Foreground = awayFg;
        HomeLogoFallback.Foreground = homeFg;
        AwayLogoFallback.Foreground = awayFg;

        HomeDeltaOverlay.Background = new SolidColorBrush(_homeSecondary);
        AwayDeltaOverlay.Background = new SolidColorBrush(_awaySecondary);
        HomeDeltaText.Foreground = new SolidColorBrush(Services.SmartContrast.PickForeground(_homeSecondary));
        AwayDeltaText.Foreground = new SolidColorBrush(Services.SmartContrast.PickForeground(_awaySecondary));

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
        _timerAllowed = showTimer;
        ApplyLiveMatchState();

        // Each side falls back independently to its abbreviation. One missing
        // logo must never erase both team identities from the persistent bar.
        ApplyLogo(HomeLogoImage, HomeLogoFallback, homeLogoPath);
        ApplyLogo(AwayLogoImage, AwayLogoFallback, awayLogoPath);
        Dispatcher.BeginInvoke(UpdateResponsiveTypography, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Mirrors scoring feedback on the persistent bar used by every full-screen
    /// AFL presentation. Goals get a two-beat light hit before the +6 wipe;
    /// behinds use a shorter +1 wipe so the two events remain distinguishable.
    /// </summary>
    public void PlayScoreAnimation(ScoreEvent scoreEvent)
    {
        bool home = scoreEvent.Team == TeamSide.Home;
        bool goal = scoreEvent.Type == ScoreType.Goal;
        Border overlay = home ? HomeDeltaOverlay : AwayDeltaOverlay;
        TextBlock deltaText = home ? HomeDeltaText : AwayDeltaText;
        TranslateTransform translate = home ? HomeDeltaTranslate : AwayDeltaTranslate;
        Rectangle flash = home ? HomeGoalFlash : AwayGoalFlash;
        TextBlock total = home ? HomeTotalText : AwayTotalText;

        DispatcherTimer? previous = home ? _homeDeltaTimer : _awayDeltaTimer;
        previous?.Stop();
        overlay.BeginAnimation(OpacityProperty, null);
        translate.BeginAnimation(TranslateTransform.YProperty, null);
        flash.BeginAnimation(OpacityProperty, null);
        total.BeginAnimation(OpacityProperty, null);

        deltaText.Text = goal ? "+6" : "+1";
        double height = Math.Max(ActualHeight, 32);
        deltaText.FontSize = Math.Max(22, height * 0.72);
        translate.Y = height;
        overlay.Opacity = 1;

        var enter = new DoubleAnimation(height, 0, TimeSpan.FromMilliseconds(goal ? 260 : 190))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        translate.BeginAnimation(TranslateTransform.YProperty, enter);

        if (goal)
        {
            var beats = new DoubleAnimationUsingKeyFrames();
            beats.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            beats.KeyFrames.Add(new EasingDoubleKeyFrame(0.78, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))));
            beats.KeyFrames.Add(new EasingDoubleKeyFrame(0.12, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220))));
            beats.KeyFrames.Add(new EasingDoubleKeyFrame(0.60, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330))));
            beats.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(560))));
            flash.BeginAnimation(OpacityProperty, beats);
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(goal ? 720 : 480) };
        if (home) _homeDeltaTimer = timer; else _awayDeltaTimer = timer;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var exitEase = new CubicEase { EasingMode = EasingMode.EaseIn };
            var exit = new DoubleAnimation(0, -height, TimeSpan.FromMilliseconds(240)) { EasingFunction = exitEase };
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = exitEase };
            exit.Completed += (_, _) =>
            {
                overlay.Opacity = 0;
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.Y = height;
            };
            translate.BeginAnimation(TranslateTransform.YProperty, exit);
            overlay.BeginAnimation(OpacityProperty, fade);
        };
        timer.Start();
    }

    private void ApplyLiveMatchState()
    {
        if (_match is null) return;

        HomeTotalText.Text = _match.HomeTotal.ToString();
        AwayTotalText.Text = _match.AwayTotal.ToString();

        bool fullTime = _match.Quarter == 4 && _match.GetQuarterSnapshot(4) is not null;
        bool atBreak = !_match.ClockRunning && _match.ElapsedInQuarter <= TimeSpan.Zero
                       && (_match.Quarter > 1 || fullTime);
        QuarterText.Text = atBreak
            ? Services.QuarterLabelService.GetBreakLabel(fullTime ? 4 : _match.Quarter - 1)
            : Services.QuarterLabelService.GetQuarterLabel(_match.Quarter);

        TimeSpan dc = _match.DisplayClock;
        TimerText.Text = $"{(int)dc.TotalMinutes:D2}:{dc.Seconds:D2}";
        bool showTimer = _timerAllowed && !atBreak;
        TimerText.Visibility = showTimer ? Visibility.Visible : Visibility.Collapsed;
        CenterQuarterRow.Height = new GridLength(showTimer ? 0.72 : 1, GridUnitType.Star);
        CenterTimerRow.Height = showTimer ? new GridLength(1.28, GridUnitType.Star) : new GridLength(0);
    }

    private static void ApplyLogo(Image image, TextBlock fallback, string? path)
    {

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

    /// <summary>Applies the shared finals palette to the clock component only.</summary>
    public void SetFinalsMode(bool enabled)
    {
        if (enabled)
        {
            CenterPanel.Background = Services.FinalsPalette.PanelBrush();
        }
        else
        {
            var normal = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(0, 1)
            };
            normal.GradientStops.Add(new GradientStop(Color.FromRgb(0x1A, 0x1F, 0x2E), 0));
            normal.GradientStops.Add(new GradientStop(Color.FromRgb(0x0A, 0x0D, 0x14), 0.5));
            normal.GradientStops.Add(new GradientStop(Colors.Black, 1));
            CenterPanel.Background = normal;
        }
        QuarterText.Foreground = TimerText.Foreground = enabled
            ? new SolidColorBrush(Services.FinalsPalette.Ink)
            : System.Windows.Media.Brushes.White;
    }

    private void UpdateResponsiveTypography()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        // Keep the identity/clock cells proportional to bar height, but cap
        // them so extremely narrow aspect ratios still leave score space.
        double identityWidth = Math.Min(ActualHeight * 0.95, ActualWidth * 0.115);
        double centerWidth = Math.Min(ActualHeight * 2.35, ActualWidth * 0.27);
        HomeLogoColumn.Width = new GridLength(identityWidth);
        AwayLogoColumn.Width = new GridLength(identityWidth);
        CenterColumn.Width = new GridLength(centerWidth);

        // Derive the score width from the same column calculation instead of
        // reading ActualWidth before WPF has completed the new layout pass.
        // This removes the one-frame compact-layout mismatch seen when a
        // presentation is selected for the first time.
        double scorePanelWidth = Math.Max(1, (ActualWidth - centerWidth - identityWidth * 2) / 2);
        double totalCellWidth = Math.Max(1, scorePanelWidth - 8);
        double availableHeight = Math.Max(1, ActualHeight - 4);

        // Fit the three-digit worst case, never the current score. A one-point
        // match and a 188-point match therefore use exactly the same font size.
        double totalSize = FitFont("888", totalCellWidth, availableHeight, FontWeights.Black);
        HomeTotalText.FontSize = AwayTotalText.FontSize = totalSize;
        HomeTotalText.MaxWidth = AwayTotalText.MaxWidth = totalCellWidth;
        HomeTotalText.MaxHeight = AwayTotalText.MaxHeight = availableHeight;
        HomeLogoImage.MaxWidth = AwayLogoImage.MaxWidth = Math.Max(1, identityWidth - 6);
        HomeLogoImage.MaxHeight = AwayLogoImage.MaxHeight = Math.Max(1, ActualHeight - 6);
        HomeLogoFallback.FontSize = AwayLogoFallback.FontSize = Math.Max(4, ActualHeight * 0.28);
        QuarterText.FontSize = Math.Max(18, ActualHeight * 0.72);
        TimerText.FontSize = Math.Max(6, ActualHeight * 0.43);
    }

    private double FitFont(string sample, double width, double height, FontWeight weight)
    {
        const double probeSize = 100;
        var probe = new FormattedText(sample, CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface(new System.Windows.Media.FontFamily("Arial"), FontStyles.Normal, weight, FontStretches.Normal),
            probeSize, System.Windows.Media.Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double byWidth = width / Math.Max(1, probe.WidthIncludingTrailingWhitespace) * probeSize;
        double byHeight = height / Math.Max(1, probe.Height) * probeSize;
        return Math.Max(3.5, Math.Min(byWidth, byHeight));
    }
}

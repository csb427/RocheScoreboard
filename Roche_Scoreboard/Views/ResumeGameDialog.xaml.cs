using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace Roche_Scoreboard.Views;

/// <summary>
/// Recovery decision surface for an interrupted AFL or cricket match. Closing
/// it is always non-destructive; deleting the recovery copy requires a second
/// deliberate click.
/// </summary>
public partial class ResumeGameDialog : Window
{
    private readonly DispatcherTimer _discardConfirmationTimer;
    private bool _discardArmed;

    public bool ResumeChosen { get; private set; }
    public bool DiscardChosen { get; private set; }

    internal ResumeGameDialog(GameState saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        InitializeComponent();
        InteractionMotion.Enable(this);

        _discardConfirmationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _discardConfirmationTimer.Tick += (_, _) => ResetDiscardConfirmation();
        Closed += (_, _) => _discardConfirmationTimer.Stop();
        Loaded += (_, _) => AnimateEntrance();

        SportText.Text = string.Equals(saved.Sport, "cricket", StringComparison.OrdinalIgnoreCase)
            ? "CRICKET"
            : "AFL";
        SavedTimeText.Text = FormatSavedTime(saved.SavedAtUtc);
        TrySetLogo(HomeLogoImage, saved.HomeLogoPath);
        TrySetLogo(AwayLogoImage, saved.AwayLogoPath);

        if (saved.CricketMatchState is { } cricket && cricket.AllInnings.Count > 0)
            PopulateCricket(cricket, saved);
        else
            PopulateAfl(saved.MatchState ?? new SerializableState(), saved);
    }

    private void PopulateAfl(SerializableState match, GameState saved)
    {
        string homeName = string.IsNullOrWhiteSpace(match.HomeName) ? "HOME" : match.HomeName!;
        string awayName = string.IsNullOrWhiteSpace(match.AwayName) ? "AWAY" : match.AwayName!;
        int homeTotal = match.HomeGoals * 6 + match.HomeBehinds;
        int awayTotal = match.AwayGoals * 6 + match.AwayBehinds;

        HomeNameText.Text = homeName.ToUpperInvariant();
        AwayNameText.Text = awayName.ToUpperInvariant();
        HomeTotalText.Text = homeTotal.ToString(CultureInfo.InvariantCulture);
        AwayTotalText.Text = awayTotal.ToString(CultureInfo.InvariantCulture);
        HomeGoalsBehindsText.Text = $"{match.HomeGoals}.{match.HomeBehinds}";
        AwayGoalsBehindsText.Text = $"{match.AwayGoals}.{match.AwayBehinds}";
        QuarterText.Text = match.Quarter is >= 1 and <= 4 ? $"Q{match.Quarter}" : $"Q{Math.Max(1, match.Quarter)}";

        TimeSpan elapsed = TimeSpan.FromTicks(Math.Max(0, match.ElapsedInQuarterTicks));
        ClockText.Text = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";

        int margin = homeTotal - awayTotal;
        if (margin == 0)
        {
            MarginText.Text = "LEVEL";
            MarginText.Foreground = new SolidColorBrush(Color.FromRgb(0xF7, 0xFA, 0xFC));
        }
        else
        {
            MarginText.Text = $"{Math.Abs(margin)} PTS";
            MarginText.Foreground = new SolidColorBrush(margin > 0
                ? SafeColor(saved.HomePrimaryColor, Color.FromRgb(0x69, 0xB7, 0xFF))
                : SafeColor(saved.AwayPrimaryColor, Color.FromRgb(0xFF, 0x8F, 0x75)));
        }

        ApplyTeamColors(saved);
    }

    private void PopulateCricket(CricketSerializableState cricket, GameState saved)
    {
        CricketInnings innings = cricket.AllInnings[
            Math.Clamp(cricket.CurrentInningsNumber - 1, 0, cricket.AllInnings.Count - 1)];
        bool teamABatting = string.Equals(innings.BattingTeamName, cricket.TeamAName, StringComparison.Ordinal);

        HomeNameText.Text = cricket.TeamAName.ToUpperInvariant();
        AwayNameText.Text = cricket.TeamBName.ToUpperInvariant();
        HomeTotalText.Text = teamABatting ? innings.TotalRuns.ToString(CultureInfo.InvariantCulture) : "—";
        AwayTotalText.Text = teamABatting ? "—" : innings.TotalRuns.ToString(CultureInfo.InvariantCulture);
        HomeGoalsBehindsText.Text = teamABatting ? $"{innings.TotalRuns}/{innings.TotalWickets}" : "BOWLING";
        AwayGoalsBehindsText.Text = teamABatting ? "BOWLING" : $"{innings.TotalRuns}/{innings.TotalWickets}";
        QuarterText.Text = $"INN {Math.Max(1, cricket.CurrentInningsNumber)}";
        ClockText.Text = $"{innings.OversDisplay} OV";
        MarginText.Text = cricket.Format == CricketFormat.LimitedOvers && cricket.CurrentInningsNumber > 1
            ? $"TARGET {cricket.AllInnings[0].TotalRuns + 1}"
            : $"RR {innings.RunRate:F2}";
        MarginLabelText.Text = cricket.CurrentInningsNumber > 1 ? "CHASE" : "MATCH RATE";
        RecoveryContextText.Text = "The innings, players and delivery history are ready to restore.";

        ApplyTeamColors(saved);
        MarginText.Foreground = new SolidColorBrush(teamABatting
            ? SafeColor(saved.HomePrimaryColor, Color.FromRgb(0x69, 0xB7, 0xFF))
            : SafeColor(saved.AwayPrimaryColor, Color.FromRgb(0xFF, 0x8F, 0x75)));
    }

    private void ApplyTeamColors(GameState saved)
    {
        HomeBorderBrush.Color = SafeColor(saved.HomePrimaryColor, Color.FromRgb(0x44, 0x88, 0xFF));
        AwayBorderBrush.Color = SafeColor(saved.AwayPrimaryColor, Color.FromRgb(0xFF, 0x66, 0x44));
    }

    private static Color SafeColor(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return fallback; }
    }

    private static string FormatSavedTime(DateTimeOffset? savedAtUtc)
    {
        if (savedAtUtc is null) return "Saved in an earlier version";
        TimeSpan age = DateTimeOffset.UtcNow - savedAtUtc.Value;
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        if (age < TimeSpan.FromMinutes(1)) return "Saved just now";
        if (age < TimeSpan.FromHours(1)) return $"Saved {(int)age.TotalMinutes} min ago";
        if (age < TimeSpan.FromDays(1)) return $"Saved {(int)age.TotalHours} hr ago";
        return $"Saved {savedAtUtc.Value.ToLocalTime():d MMM, h:mm tt}";
    }

    private static void TrySetLogo(System.Windows.Controls.Image target, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            target.Source = image;
            target.Visibility = Visibility.Visible;
        }
        catch
        {
            target.Source = null;
            target.Visibility = Visibility.Collapsed;
        }
    }

    private void AnimateEntrance()
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        DialogShell.Opacity = 0;
        var translate = new System.Windows.Media.TranslateTransform(0, 12);
        DialogShell.RenderTransform = translate;
        DialogShell.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        translate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(220))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        ResumeChosen = true;
        DialogResult = true;
    }

    private void KeepSaved_Click(object sender, RoutedEventArgs e)
    {
        ResetDiscardConfirmation();
        Close();
    }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        if (!_discardArmed)
        {
            _discardArmed = true;
            DiscardButton.Content = "Click again to discard";
            DiscardButton.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xF7, 0xF7));
            DiscardButton.Background = new SolidColorBrush(Color.FromRgb(0x8E, 0x2F, 0x38));
            _discardConfirmationTimer.Stop();
            _discardConfirmationTimer.Start();
            return;
        }

        DiscardChosen = true;
        DialogResult = false;
    }

    private void ResetDiscardConfirmation()
    {
        _discardConfirmationTimer.Stop();
        _discardArmed = false;
        if (DiscardButton is null) return;
        DiscardButton.Content = "Discard save";
        DiscardButton.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
        DiscardButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        ResetDiscardConfirmation();
        Close();
    }
}

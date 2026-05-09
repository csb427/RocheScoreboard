using System;
using System.Globalization;
using System.Windows;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace Roche_Scoreboard.Views;

/// <summary>
/// Custom styled dialog that asks whether to resume a saved game in progress.
/// Shows a full team-dedicated readout (names, score, quarter, clock, margin)
/// with each team's primary colour driving the accent stripes and glows.
/// </summary>
public partial class ResumeGameDialog : Window
{
    /// <summary>Gets whether the user chose to resume the game.</summary>
    public bool ResumeChosen { get; private set; }

    /// <summary>
    /// Build the dialog from a previously serialised game state. All match
    /// details (team names, score, quarter, elapsed clock) and team colours
    /// come from the saved snapshot so the user sees exactly what they're
    /// about to resume.
    /// </summary>
    internal ResumeGameDialog(GameState saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        InitializeComponent();

        SerializableState m = saved.MatchState ?? new SerializableState();

        string homeName = string.IsNullOrWhiteSpace(m.HomeName) ? "HOME" : m.HomeName!;
        string awayName = string.IsNullOrWhiteSpace(m.AwayName) ? "AWAY" : m.AwayName!;

        HomeNameText.Text = homeName.ToUpperInvariant();
        AwayNameText.Text = awayName.ToUpperInvariant();

        int homeTotal = m.HomeGoals * 6 + m.HomeBehinds;
        int awayTotal = m.AwayGoals * 6 + m.AwayBehinds;
        HomeTotalText.Text = homeTotal.ToString(CultureInfo.InvariantCulture);
        AwayTotalText.Text = awayTotal.ToString(CultureInfo.InvariantCulture);
        HomeGoalsBehindsText.Text = $"{m.HomeGoals}.{m.HomeBehinds}";
        AwayGoalsBehindsText.Text = $"{m.AwayGoals}.{m.AwayBehinds}";

        QuarterText.Text = m.Quarter switch
        {
            1 => "Q1",
            2 => "Q2",
            3 => "Q3",
            4 => "Q4",
            _ => $"Q{m.Quarter}"
        };

        // Reconstruct the elapsed clock display. Since the saved state is
        // mid-quarter, show mm:ss elapsed in the current quarter.
        TimeSpan elapsed = TimeSpan.FromTicks(m.ElapsedInQuarterTicks);
        ClockText.Text = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";

        // Margin readout — shows the leading team's name and the margin, or
        // LEVEL if scores are tied. More useful at a glance than just digits.
        int margin = homeTotal - awayTotal;
        if (margin == 0)
        {
            MarginText.Text = "LEVEL";
            MarginText.Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xED, 0xF3));
        }
        else if (margin > 0)
        {
            MarginText.Text = $"+{margin}";
            MarginText.Foreground = new SolidColorBrush(SafeColor(saved.HomePrimaryColor, Color.FromRgb(0x44, 0x88, 0xFF)));
        }
        else
        {
            MarginText.Text = $"+{-margin}";
            MarginText.Foreground = new SolidColorBrush(SafeColor(saved.AwayPrimaryColor, Color.FromRgb(0xFF, 0x66, 0x44)));
        }

        // Apply team colours to the borders, glows and the top split stripe
        // so the dialog feels genuinely team-dedicated.
        Color homeColor = SafeColor(saved.HomePrimaryColor, Color.FromRgb(0x44, 0x88, 0xFF));
        Color awayColor = SafeColor(saved.AwayPrimaryColor, Color.FromRgb(0xFF, 0x66, 0x44));

        HomeBorderBrush.Color = homeColor;
        AwayBorderBrush.Color = awayColor;
        HomeGlow.Color = homeColor;
        AwayGlow.Color = awayColor;
        HomeNameGlow.Color = homeColor;
        AwayNameGlow.Color = awayColor;
        HomeTotalGlow.Color = homeColor;
        AwayTotalGlow.Color = awayColor;
        TopStripeGradient.GradientStops[0].Color = homeColor;
        TopStripeGradient.GradientStops[1].Color = homeColor;
        TopStripeGradient.GradientStops[3].Color = awayColor;
        TopStripeGradient.GradientStops[4].Color = awayColor;
    }

    private static Color SafeColor(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return fallback;
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return fallback;
        }
    }

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        ResumeChosen = true;
        DialogResult = true;
        Close();
    }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        ResumeChosen = false;
        DialogResult = false;
        Close();
    }
}

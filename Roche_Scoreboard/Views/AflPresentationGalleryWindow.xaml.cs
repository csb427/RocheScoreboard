using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;

namespace Roche_Scoreboard.Views;

/// <summary>
/// Developer-only, in-memory visual regression gallery for AFL output surfaces.
/// Launch with --afl-presentation-gallery. It never reads or writes match state.
/// </summary>
public partial class AflPresentationGalleryWindow : Window
{
    private const string HomePrimary = "#FF3338";
    private const string AwayPrimary = "#FACC0D";
    private const string HomeSecondary = "#FFFFFF";
    private const string AwaySecondary = "#015290";

    public AflPresentationGalleryWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(BuildGallerySafely);
    }

    private void BuildGallerySafely()
    {
        try
        {
            BuildGallery();
        }
        catch (Exception ex)
        {
            GalleryErrorText.Text = $"Gallery population failed: {ex.GetType().Name}: {ex.Message}";
            GalleryErrorText.Visibility = Visibility.Visible;
        }
    }

    private void BuildGallery()
    {
        MatchManager match = BuildRepresentativeMatch();
        AddPreview(ScorebugHost, "DESIGN 707 × 420", CreateScorebug(), 707, 420);
        AddPreview(ScorebugHost, "COMPACT 386 × 193", CreateScorebug(), 386, 193);

        AddPresentationPair(BreakHost, () =>
        {
            var view = new BreakScreenControl();
            view.Populate(match, 1, HomePrimary, AwayPrimary, HomeSecondary, AwaySecondary, null, null, "Q2");
            return view;
        });
        AddPresentationPair(WormHost, () =>
        {
            var view = new ScorewormControl();
            view.Populate(match, 1, HomePrimary, AwayPrimary, HomeSecondary, AwaySecondary, null, null, "Q2");
            return view;
        });
        AddPresentationPair(StatsHost, () =>
        {
            var view = new StatsScreenControl();
            view.Populate(match, MatchStats.Calculate(match, match.Quarter, match.ElapsedInQuarter), 1,
                HomePrimary, AwayPrimary, HomeSecondary, AwaySecondary, null, null, "Q2");
            return view;
        });
        AddPresentationPair(WeatherHost, () =>
        {
            var view = new WeatherScreenControl();
            var weather = new WeatherSnapshot(22, 20, 14, 27, 0, "☀", "Clear sky", Array.Empty<HourlyForecast>());
            view.Populate(weather, "Melbourne", match, 1,
                HomePrimary, AwayPrimary, HomeSecondary, AwaySecondary, null, null, "Q2");
            return view;
        });
    }

    private static void AddPresentationPair(System.Windows.Controls.Panel host, Func<System.Windows.Controls.UserControl> factory)
    {
        AddPreview(host, "LARGE 772 × 386", factory(), 772, 386);
        AddPreview(host, "COMPACT 300 × 150", factory(), 300, 150);
    }

    private static ScorebugControl CreateScorebug()
    {
        var view = new ScorebugControl();
        view.SetTeams("Xavier", "XC", "Carey", "CY");
        view.SetTeamColors(HomePrimary, AwayPrimary, HomeSecondary, AwaySecondary);
        view.SetScores(18, 12, 17, 11);
        view.SetQuarter(4);
        view.SetClock("04:59");
        view.SetLogos(null, null);
        return view;
    }

    private static void AddPreview(System.Windows.Controls.Panel host, string label, FrameworkElement view, double width, double height)
    {
        view.Width = width;
        view.Height = height;

        var frame = new Border
        {
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x4D, 0x65)),
            BorderThickness = new Thickness(1),
            Background = System.Windows.Media.Brushes.Black,
            Child = view
        };

        var card = new StackPanel { Margin = new Thickness(0, 0, 18, 18) };
        card.Children.Add(new TextBlock
        {
            Text = label,
            FontFamily = new System.Windows.Media.FontFamily("Bahnschrift"),
            FontWeight = FontWeights.Black,
            FontSize = 14,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC7, 0xD6, 0xE5)),
            Margin = new Thickness(0, 0, 0, 7)
        });
        card.Children.Add(frame);
        host.Children.Add(card);
    }

    private static MatchManager BuildRepresentativeMatch()
    {
        var match = new MatchManager();
        match.SetTeams("Xavier", "XC", "Carey", "CY");
        match.SetClockMode(ClockMode.Countdown);
        match.SetQuarterDuration(TimeSpan.FromMinutes(20));

        ScoreAt(match, TimeSpan.FromMinutes(2), TeamSide.Home, goal: true);
        ScoreAt(match, TimeSpan.FromMinutes(5), TeamSide.Away, goal: false);
        ScoreAt(match, TimeSpan.FromMinutes(8), TeamSide.Home, goal: true);
        ScoreAt(match, TimeSpan.FromMinutes(12), TeamSide.Away, goal: true);
        ScoreAt(match, TimeSpan.FromMinutes(17), TeamSide.Home, goal: false);
        ScoreAt(match, TimeSpan.FromMinutes(19), TeamSide.Away, goal: false);
        ScoreAt(match, TimeSpan.FromMinutes(19.5), TeamSide.Away, goal: false);
        match.EndQuarter();

        ScoreAt(match, TimeSpan.FromMinutes(1), TeamSide.Away, goal: true);
        ScoreAt(match, TimeSpan.FromMinutes(2.5), TeamSide.Home, goal: false);
        ScoreAt(match, TimeSpan.FromMinutes(3.2), TeamSide.Home, goal: true);
        ScoreAt(match, TimeSpan.FromMinutes(3.7), TeamSide.Away, goal: true);
        return match;
    }

    private static void ScoreAt(MatchManager match, TimeSpan elapsed, TeamSide team, bool goal)
    {
        match.SetElapsed(elapsed);
        if (goal) match.AddGoal(team);
        else match.AddBehind(team);
    }
}

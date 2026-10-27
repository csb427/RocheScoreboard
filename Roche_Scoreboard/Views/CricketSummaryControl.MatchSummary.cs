using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Grid = System.Windows.Controls.Grid;
using TextBlock = System.Windows.Controls.TextBlock;
using Brushes = System.Windows.Media.Brushes;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Color = System.Windows.Media.Color;

namespace Roche_Scoreboard.Views;

public partial class CricketSummaryControl
{
    public void ShowMatchSummary(CricketMatchManager match)
    {
        if (match.CurrentInnings is not CricketInnings innings) return;
        SetPagedMode(match, "summary");
        PopulateMatchSummary(match, innings);
        ShowSummary();
    }

    public void RefreshMatchSummary(CricketMatchManager match)
    {
        if (match.CurrentInnings is not CricketInnings innings || Visibility != Visibility.Visible) return;
        SetPagedMode(match, "summary");
        PopulateMatchSummary(match, innings);
    }

    private void PopulateMatchSummary(CricketMatchManager match, CricketInnings innings)
    {
        SummaryHeaderPanel.Background = CricketBroadcastStyle.Gradient(match.BattingTeamPrimaryColor, match.BattingTeamSecondaryColor);
        HeaderSheen.Opacity = HeaderShimmer.Opacity = 0;
        SetSummaryLogo(match, false);
        if(SummaryHeaderLogo.Parent is Grid headerSurface) headerSurface.Background=null;
        RootGrid.Background=CricketBroadcastStyle.Gradient(match.BattingTeamPrimaryColor,match.BattingTeamSecondaryColor);
        RowsPanel.Background=CricketBroadcastStyle.CenterLogo(SummaryHeaderLogo.Source,.14);
        HeaderText.Foreground = SummaryClock.Foreground = Brushes.White;
        string title = match.BreakKind switch
        {
            CricketBreakKind.None => "MATCH SUMMARY",
            CricketBreakKind.Rain => "RAIN DELAY · SUMMARY",
            CricketBreakKind.Other => "MATCH PAUSED · SUMMARY",
            _ => match.BreakKind.ToString().ToUpperInvariant() + " · SUMMARY"
        };
        HeaderText.Text = title;
        SummaryClock.Text = BroadcastClock.WallTime();
        SummaryColumns.Children.Clear();
        RootGrid.RowDefinitions[1].Height = new GridLength(0);
        RowsPanel.Tag = "match-summary";
        RowsPanel.Children.Clear(); RowsPanel.Rows = 3;

        Grid Surface(string colour, string secondary)
        {
            var content = new Grid { Margin = new Thickness(16, 4, 16, 4) };
            var wash = new Grid { ClipToBounds = true };
            wash.Children.Add(content);
            RowsPanel.Children.Add(new Border { Margin = new Thickness(0, 4, 0, 4), Background = new SolidColorBrush(Color.FromArgb(105,0,0,0)),
                BorderBrush=CricketBroadcastStyle.Gradient(colour,secondary),BorderThickness=new Thickness(6,0,0,0),Child = wash });
            return content;
        }
        TextBlock Text(string text, double size, string? tag = null) => new() { Text = text, Tag = tag, FontFamily = CricketBroadcastStyle.Typeface, FontWeight = FontWeights.Bold,
            FontSize = size, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var score = Surface(match.BattingTeamPrimaryColor, match.BattingTeamSecondaryColor);
        score.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        score.ColumnDefinitions.Add(new() { Width = new GridLength(310) });
        score.RowDefinitions.Add(new()); score.RowDefinitions.Add(new());
        score.Children.Add(Text(match.BattingTeamName.ToUpperInvariant(), 48));
        var overs = Text(innings.OversDisplay + " OVERS", 42); Grid.SetRow(overs, 1); score.Children.Add(overs);
        var total = Text($"{innings.TotalWickets}/{innings.TotalRuns}", 108, "match-summary-score");
        total.TextAlignment = TextAlignment.Center;
        var totalFit = new Viewbox { Child = total, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
        Grid.SetColumn(totalFit, 1); Grid.SetRowSpan(totalFit, 2); score.Children.Add(totalFit);

        var batters = innings.BattingOrder.Where(player => player.Runs > 0 || player.BallsFaced > 0 || player.IsOut)
            .OrderByDescending(player => player.Runs).ThenBy(player => player.BallsFaced).Take(2).ToArray();
        var bowlers = BowledPlayers(innings).Select(item => item.Player).OrderByDescending(player => player.Wickets)
            .ThenBy(player => player.Economy).ThenBy(player => player.RunsConceded).Take(2).ToArray();
        void Performers(string title, CricketPlayer[] players, bool bowling)
        {
            var section = Surface(bowling ? match.BowlingTeamPrimaryColor : match.BattingTeamPrimaryColor,
                bowling ? match.BowlingTeamSecondaryColor : match.BattingTeamSecondaryColor);
            section.ColumnDefinitions.Add(new() { Width = new GridLength(170) });
            section.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var heading = Text(title, 32); heading.HorizontalAlignment = HorizontalAlignment.Center; section.Children.Add(heading);
            var rows = new Grid { Margin = new Thickness(10, 0, 0, 0) }; Grid.SetColumn(rows, 1); section.Children.Add(rows);
            rows.RowDefinitions.Add(new()); rows.RowDefinitions.Add(new());
            if (players.Length == 0)
            {
                var empty = Text("NO DELIVERIES", 42); Grid.SetRowSpan(empty, 2); rows.Children.Add(empty); return;
            }
            for (int index = 0; index < players.Length; index++)
            {
                var player = players[index];
                var line = new Grid(); Grid.SetRow(line, index); rows.Children.Add(line);
                line.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new() { Width = new GridLength(265) });
                line.Children.Add(Text(player.DisplayName.ToUpperInvariant(), 40, bowling ? "match-summary-bowler" : "match-summary-batter"));
                var figures = new Grid(); Grid.SetColumn(figures, 1); line.Children.Add(figures);
                figures.ColumnDefinitions.Add(new()); figures.ColumnDefinitions.Add(new());
                var main = Text(bowling ? $"{player.Wickets}/{player.RunsConceded}" : player.Runs.ToString(), 62);
                var secondary = Text(bowling ? $"({player.OversDisplay})" : $"({player.BallsFaced})", 42);
                var mainFit = new Viewbox { Child = main, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(3, 0, 4, 0) };
                var secondFit = new Viewbox { Child = secondary, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(4, 0, 0, 0) };
                Grid.SetColumn(secondFit, 1); figures.Children.Add(mainFit); figures.Children.Add(secondFit);
            }
        }
        Performers("BATTERS", batters, false);
        Performers("BOWLERS", bowlers, true);
        PopulateFooter(match, innings);
    }
}

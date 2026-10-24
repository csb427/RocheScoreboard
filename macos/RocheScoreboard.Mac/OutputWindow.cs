using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Roche_Scoreboard.Models;

namespace RocheScoreboard.Mac;

public sealed class OutputWindow : Window
{
    private readonly DemoSession _session;
    private readonly TextBlock _clock = Ui.Text("", 32);
    private readonly TextBlock _ticker = Ui.Text("", 22);
    private readonly TextBlock _story = Ui.Text("", 22, Ui.Secondary);
    private readonly TextBlock _announcement = Ui.Text("", 36);
    private readonly Border _announcementCard;
    private readonly ContentControl _body = new();
    private readonly Dictionary<string, Bitmap> _logos = new();
    private readonly DateTime _openedAt = DateTime.UtcNow;
    private TextBlock? _trainingClock;
    private ProgressBar? _trainingProgress;
    private TextBlock? _aflStats;
    private Scoreworm? _worm;

    public OutputWindow(DemoSession session)
    {
        _session = session;
        Title = "Roche Scoreboard · Demo Output";
        Width = 1100; Height = 680; MinWidth = 480; MinHeight = 300;
        Background = Ui.Background;
        var frame = new Grid { Width = 1280, Height = 720, RowDefinitions = new RowDefinitions("84,*,60,56"), Margin = new Thickness(0) };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(36, 20) };
        heading.Children.Add(Ui.Text("ROCHE SCOREBOARD", 20, Ui.Secondary));
        _clock.Name = "OutputClock"; Grid.SetColumn(_clock, 1); heading.Children.Add(_clock);
        frame.Children.Add(heading);
        _body.Margin = new Thickness(36, 8); Grid.SetRow(_body, 1); frame.Children.Add(_body);
        var story = new Border { Child = _story, Padding = new Thickness(36, 12) };
        Grid.SetRow(story, 2); frame.Children.Add(story);
        var ticker = new Border { Child = _ticker, Background = Ui.Surface, Padding = new Thickness(36, 12) };
        Grid.SetRow(ticker, 3); frame.Children.Add(ticker);
        _announcementCard = new Border
        {
            Background = Ui.Surface, BorderBrush = Ui.Accent, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(36), Margin = new Thickness(80), HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center, Child = _announcement, IsHitTestVisible = false, Opacity = 0
        };
        _announcement.TextAlignment = TextAlignment.Center;
        Grid.SetRowSpan(_announcementCard, 4); frame.Children.Add(_announcementCard);
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = frame };
        session.Changed += RefreshPresentation;
        Closed += (_, _) =>
        {
            session.Changed -= RefreshPresentation;
            foreach (var bitmap in _logos.Values) bitmap.Dispose(); _logos.Clear();
        };
        RefreshPresentation();
    }

    public void RefreshPresentation()
    {
        _trainingClock = null; _trainingProgress = null; _aflStats = null; _worm = null;
        _body.Content = _session.Screen == Presentation.Intro ? Intro() : _session.Sport switch
        {
            DemoSport.Afl => _session.Screen switch
            {
                Presentation.Summary => AflSummary(), Presentation.Statistics => AflStatistics(),
                Presentation.Scoreworm => AflScoreworm(), _ => AflScoreboard()
            },
            DemoSport.Cricket => _session.Screen switch
            { Presentation.Summary => CricketSummary(), Presentation.Statistics => CricketStatistics(), _ => CricketScoreboard() },
            _ => TrainingScoreboard(_session.Screen == Presentation.Summary)
        };
        _announcementCard.Transitions = _session.ReducedMotion ? null : new Transitions
        { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) } };
        RefreshClock();
    }

    public void RefreshClock()
    {
        _clock.Text = _session.Sport switch
        {
            DemoSport.Afl => $"{_session.QuarterLabel}   {DemoSession.Clock(_session.Afl.DisplayClock)}",
            DemoSport.Cricket => $"INNINGS {_session.Cricket.CurrentInningsNumber}   {_session.Cricket.OversDisplay} OV",
            _ => _session.Training.IsRunning ? "TRAINING · RUNNING" : "TRAINING · PAUSED"
        };
        _story.Text = _session.Sport switch
        {
            DemoSport.Afl => _session.AflStory(),
            DemoSport.Cricket => _session.Cricket.MatchResult ?? CricketAnalyticsEngine.Create(_session.Cricket).ChaseEquation,
            _ => _session.Training.UpNext is { } next ? $"UP NEXT · {next.Title}  {DemoSession.Clock(next.Duration)}" : "ROCHE SCOREBOARD · TRAINING"
        };
        if (_trainingClock is not null) _trainingClock.Text = DemoSession.Clock(_session.Training.ActiveRemaining);
        if (_trainingProgress is not null)
        {
            var timer = _session.Training.Active;
            _trainingProgress.Value = timer is { Duration.TotalSeconds: > 0 }
                ? 100 * (1 - _session.Training.ActiveRemaining.TotalSeconds / timer.Duration.TotalSeconds) : 0;
        }
        if (_aflStats is not null) _aflStats.Text = AflStatisticsText();
        _worm?.InvalidateVisual();
        _announcement.Text = string.IsNullOrWhiteSpace(_session.Announcement) ? _session.Celebration : _session.Announcement;
        _announcementCard.Opacity = string.IsNullOrEmpty(_announcement.Text) ? 0 : 1;
        int index = (int)((DateTime.UtcNow - _openedAt).TotalSeconds / 5);
        _ticker.Text = _session.Messages.Count == 0 ? "ROCHE SCOREBOARD · LOCAL DEMO" : _session.Messages[index % _session.Messages.Count];
    }

    private Control AflScoreboard()
    {
        var match = _session.Afl;
        var grid = _session.Stacked
            ? new Grid { RowDefinitions = new RowDefinitions("*,16,*") }
            : new Grid { ColumnDefinitions = new ColumnDefinitions("*,24,*") };
        grid.Children.Add(TeamCard(match.HomeName, $"{match.HomeTotal}", $"{match.HomeGoals} GOALS   {match.HomeBehinds} BEHINDS", true));
        var away = TeamCard(match.AwayName, $"{match.AwayTotal}", $"{match.AwayGoals} GOALS   {match.AwayBehinds} BEHINDS", false);
        if (_session.Stacked) Grid.SetRow(away, 2); else Grid.SetColumn(away, 2);
        grid.Children.Add(away);
        return grid;
    }

    private Control TeamCard(string name, string total, string detail, bool home)
    {
        var color = Ui.SafeBrush(home ? _session.HomeColor : _session.AwayColor);
        var textColor = Ui.SafeBrush(home ? _session.HomeTextColor : _session.AwayTextColor, Brushes.White);
        bool stacked = _session.Stacked;
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(24) };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        var logo = Logo(home ? _session.HomeLogo : _session.AwayLogo, stacked ? 44 : 72);
        if (logo is not null) nameRow.Children.Add(logo);
        var nameLabel = Ui.Text(name, stacked ? 24 : 30, textColor); nameLabel.MaxWidth = stacked ? 850 : 410;
        nameRow.Children.Add(nameLabel); content.Children.Add(nameRow);
        var score = Ui.Text(total, stacked ? 60 : 132, textColor);
        score.Name = home ? "OutputHomeScore" : "OutputAwayScore";
        score.FontWeight = FontWeight.Bold; Grid.SetRow(score, 1); content.Children.Add(score);
        var sub = Ui.Text(detail, stacked ? 18 : 22, textColor); Grid.SetRow(sub, 2); content.Children.Add(sub);
        return new Border { Background = color, CornerRadius = new CornerRadius(12), Child = content };
    }

    private Control? Logo(string path, int size)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!_logos.TryGetValue(path, out var bitmap))
        {
            try { bitmap = new Bitmap(path); _logos.Add(path, bitmap); }
            catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException) { return null; }
        }
        return new Image { Source = bitmap, Width = size, Height = size, Stretch = Stretch.Uniform };
    }

    private Control Intro()
    {
        if (_session.Sport == DemoSport.Training)
            return Ui.Card(Ui.Column(Ui.Heading("TRAINING SESSION"), Ui.Text($"{_session.Training.Timers.Count} intervals", 60),
                Ui.Text($"{DemoSession.Clock(_session.Training.TotalDuration)} total", 36)));
        bool cricket = _session.Sport == DemoSport.Cricket;
        return Ui.Card(Ui.Column(Ui.Heading(cricket ? "CRICKET" : "AUSTRALIAN FOOTBALL"),
            Ui.Text(cricket ? _session.Cricket.TeamAName : _session.Afl.HomeName, 56), Ui.Text("versus", 24, Ui.Secondary),
            Ui.Text(cricket ? _session.Cricket.TeamBName : _session.Afl.AwayName, 56)));
    }

    private Control AflSummary()
    {
        var match = _session.Afl;
        var rows = new List<string[]> { new[] { "TEAM", "Q1", "Q2", "Q3", "Q4", "TOTAL" } };
        foreach (var team in new[] { TeamSide.Home, TeamSide.Away })
        {
            var row = new List<string> { team == TeamSide.Home ? match.HomeName : match.AwayName };
            int previous = 0;
            for (int q = 1; q <= 4; q++)
            {
                var snapshot = match.GetQuarterSnapshot(q);
                int current = snapshot is null ? match.Events.Where(e => e.Quarter <= q).Sum(e => e.Team == team ? e.Type == ScoreType.Goal ? 6 : 1 : 0)
                    : team == TeamSide.Home ? snapshot.HomeTotal : snapshot.AwayTotal;
                row.Add(q <= match.Quarter ? (current - previous).ToString() : "—"); previous = current;
            }
            row.Add((team == TeamSide.Home ? match.HomeTotal : match.AwayTotal).ToString()); rows.Add(row.ToArray());
        }
        return Ui.Card(Ui.Column(Ui.Heading($"MATCH SUMMARY · {_session.QuarterLabel}"), Table(rows, "3*,*,*,*,*,*", 26),
            Ui.Text($"{match.HomeName}  {match.HomeGoals}.{match.HomeBehinds} ({match.HomeTotal})     —     {match.AwayName}  {match.AwayGoals}.{match.AwayBehinds} ({match.AwayTotal})", 28)));
    }

    private Control AflStatistics()
    {
        _aflStats = Ui.Text(AflStatisticsText(), 26);
        return Ui.Card(Ui.Column(Ui.Heading("MATCH STATISTICS"), _aflStats));
    }

    private Control AflScoreworm()
    {
        _worm = new Scoreworm(_session);
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        grid.Children.Add(Ui.Text($"{_session.Afl.HomeName} ABOVE · {_session.Afl.AwayName} BELOW · MARGIN IN POINTS", 22, Ui.Secondary));
        Grid.SetRow(_worm, 1); grid.Children.Add(_worm);
        var labels = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
        for (int i = 0; i < 4; i++)
        {
            var label = Ui.Text($"Q{i + 1}", 18, Ui.Secondary); Grid.SetColumn(label, i); labels.Children.Add(label);
        }
        Grid.SetRow(labels, 2); grid.Children.Add(labels);
        return grid;
    }

    private string AflStatisticsText()
    {
        var m = _session.Afl;
        var stats = MatchStats.Calculate(m, m.Quarter, m.ElapsedInQuarter);
        return $"{m.HomeName}     /     {m.AwayName}\n\nSCORING SHOTS    {stats.HomeScoringShots}  /  {stats.AwayScoringShots}\n\nACCURACY    {stats.HomeAccuracy:0.0}%  /  {stats.AwayAccuracy:0.0}%\n\nLARGEST LEAD    {stats.HomeLargestLead}  /  {stats.AwayLargestLead}\n\nTIME IN FRONT    {stats.HomeTimePctInFront:0.0}%  /  {stats.AwayTimePctInFront:0.0}%\n\nLEAD CHANGES    {stats.LeadChanges}";
    }

    private Control CricketScoreboard()
    {
        var m = _session.Cricket; var inn = m.CurrentInnings;
        var score = Ui.Text(m.ScoreDisplay, 100); score.Name = "OutputCricketScore";
        return Ui.Card(Ui.Column(Ui.Heading(m.BattingTeamName), score,
            Ui.Text($"{m.OversDisplay} OVERS     RUN RATE {m.RunRate:0.00}{(m.Target is { } target ? $"     TARGET {target}" : "")}", 26),
            Ui.Text($"{inn?.Striker?.FullName ?? "Select striker"} *    {inn?.Striker?.Runs ?? 0} ({inn?.Striker?.BallsFaced ?? 0})\n{inn?.NonStriker?.FullName ?? "Select non-striker"}    {inn?.NonStriker?.Runs ?? 0} ({inn?.NonStriker?.BallsFaced ?? 0})", 26),
            Ui.Text($"BOWLING   {inn?.CurrentBowler?.FullName ?? "Select bowler"}   {inn?.CurrentBowler?.BowlingFigures}   ({inn?.CurrentBowler?.OversDisplay})", 24),
            Ui.Text($"THIS OVER   {string.Join("   ", inn?.CurrentOverBalls ?? [])}", 24, Ui.Secondary)));
    }

    private Control CricketSummary()
    {
        var m = _session.Cricket; var inn = m.CurrentInnings;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,20,*") };
        var batting = new List<string[]> { new[] { "BATTER", "R", "B", "4", "6" } };
        if (inn is not null)
            batting.AddRange(inn.BattingOrder.Select(p => new[] { $"{p.DisplayName}{(p.IsOut ? " †" : "")}", p.Runs.ToString(), p.BallsFaced.ToString(), p.Fours.ToString(), p.Sixes.ToString() }));
        grid.Children.Add(Ui.Card(Ui.Column(Ui.Heading($"{m.BattingTeamName} · {m.ScoreDisplay}"), Table(batting, "4*,*,*,*,*", 18))));
        var bowling = new List<string[]> { new[] { "BOWLER", "O", "M", "R", "W" } };
        if (inn is not null)
            bowling.AddRange(inn.BowlingAttack.Where(p => p.BallsBowled > 0 || p.RunsConceded > 0).Select(p => new[]
            { p.DisplayName, p.OversDisplay, p.Maidens.ToString(), p.RunsConceded.ToString(), p.Wickets.ToString() }));
        var other = Ui.Card(Ui.Column(Ui.Heading("BOWLING"), Table(bowling, "4*,*,*,*,*", 18),
            Ui.Text(string.Join('\n', m.AllInnings.Select(i => $"INN {i.InningsNumber} · {i.BattingTeamName}  {i.TotalRuns}/{i.TotalWickets} ({i.OversDisplay})")), 18)));
        Grid.SetColumn(other, 2); grid.Children.Add(other); return grid;
    }

    private Control CricketStatistics()
    {
        var m = _session.Cricket; var inn = m.CurrentInnings; var stats = CricketAnalyticsEngine.Create(m);
        return Ui.Card(Ui.Column(Ui.Heading(stats.Phase), Ui.Text(stats.MomentumHeadline, 34), Ui.Text(stats.MomentumDetail, 22),
            Ui.Text($"PARTNERSHIP   {inn?.PartnershipRuns ?? 0} ({inn?.PartnershipBalls ?? 0})\n\nEXTRAS   {inn?.TotalExtras ?? 0}   W {inn?.Wides ?? 0}   NB {inn?.NoBalls ?? 0}   B {inn?.Byes ?? 0}   LB {inn?.LegByes ?? 0}\n\nDOT BALLS   {stats.DotBallPercentage:0.0}%     BOUNDARY DROUGHT   {stats.BoundaryDroughtBalls} balls\n\nPROJECTION   {stats.ProjectedScore}     RECENT RATE   {stats.RecentRunRate:0.00}", 26)));
    }

    private Control TrainingScoreboard(bool summary)
    {
        var session = _session.Training;
        if (summary)
        {
            var rows = new List<string[]> { new[] { "INTERVAL", "DURATION", "STATUS" } };
            rows.AddRange(session.Timers.Skip(Math.Max(0, session.ActiveIndex - 2)).Take(10).Select((t) => new[]
                { t.Title, DemoSession.Clock(t.Duration), t.HasFinished ? "Complete" : ReferenceEquals(t, session.Active) ? "Active" : "Queued" }));
            return Ui.Card(Ui.Column(Ui.Heading("TRAINING SESSION"), Table(rows, "3*,*,*", 23)));
        }
        _trainingClock = Ui.Text(DemoSession.Clock(session.ActiveRemaining), 130);
        _trainingClock.Name = "OutputTrainingClock";
        _trainingProgress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 12, Foreground = Ui.Success };
        return Ui.Card(Ui.Column(Ui.Heading(session.Active?.Title ?? (session.IsExhausted ? "SESSION COMPLETE" : "ADD A TRAINING TIMER")),
            _trainingClock, _trainingProgress, Ui.Text($"INTERVAL {Math.Min(session.ActiveIndex + 1, session.Timers.Count)} OF {session.Timers.Count}", 24, Ui.Secondary)));
    }

    private static Grid Table(IEnumerable<string[]> rows, string columns, double fontSize)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), RowSpacing = 14, ColumnSpacing = 12 };
        int rowIndex = 0;
        foreach (var cells in rows)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int column = 0; column < cells.Length; column++)
            {
                var text = Ui.Text(cells[column], fontSize, rowIndex == 0 ? Ui.Secondary : Ui.TextColor);
                Grid.SetRow(text, rowIndex); Grid.SetColumn(text, column); grid.Children.Add(text);
            }
            rowIndex++;
        }
        return grid;
    }
}

internal sealed class Scoreworm(DemoSession session) : Control
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double width = Bounds.Width, height = Bounds.Height;
        if (width < 2 || height < 2) return;
        var match = session.Afl;
        double maxMargin = Math.Max(12, match.Events.Select(e => Math.Abs(e.Margin)).DefaultIfEmpty().Max() + 6);
        double total = Math.Max(match.QuarterDuration.TotalSeconds * 4,
            match.PlayingTimeAt(match.Quarter, match.ElapsedInQuarter).TotalSeconds);
        double middle = height / 2;
        context.DrawLine(new Pen(Ui.Secondary, 1), new Point(0, middle), new Point(width, middle));
        for (int q = 1; q <= 3; q++)
            context.DrawLine(new Pen(Ui.Border, 1), new Point(width * q / 4, 0), new Point(width * q / 4, height));
        Point previous = new(0, middle);
        foreach (var score in match.Events)
        {
            double seconds = match.PlayingTimeAt(score.Quarter, score.GameTime).TotalSeconds;
            Point point = new(width * Math.Clamp(seconds / total, 0, 1), middle - score.Margin / maxMargin * (middle - 24));
            context.DrawLine(new Pen(Ui.SafeBrush(score.Margin >= 0 ? session.HomeColor : session.AwayColor), 4), previous, point);
            previous = point;
        }
        double currentX = width * Math.Clamp(match.PlayingTimeAt(match.Quarter, match.ElapsedInQuarter).TotalSeconds / total, 0, 1);
        context.DrawLine(new Pen(Ui.SafeBrush(match.Margin >= 0 ? session.HomeColor : session.AwayColor), 4), previous, new Point(Math.Max(currentX, previous.X), previous.Y));
    }
}

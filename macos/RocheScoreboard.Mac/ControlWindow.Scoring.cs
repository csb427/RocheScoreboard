using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Roche_Scoreboard.Models;

namespace RocheScoreboard.Mac;

public sealed partial class ControlWindow
{
    private Control BuildAfl()
    {
        var match = _session.Afl;
        var home = Ui.Text("", 38);
        var away = Ui.Text("", 38);
        var clock = Ui.Text("", 32);
        var start = Ui.Button("Start clock", StartAflClock, "StartClock", true);
        var pause = Ui.Button("Pause", match.PauseClock, "PauseClock");
        var continueQuarter = Ui.Button("Continue ended quarter", () =>
        { _session.LabelOverride = ""; if (match.ContinueQuarter()) { _session.Screen = Presentation.Scoreboard; _session.Notify(); } }, "ContinueQuarter");
        var end = Ui.Button("End quarter", () =>
        {
            _session.LabelOverride = "";
            if (match.EndQuarter()) { _session.Screen = Presentation.Summary; _session.Notify(); }
        }, "EndQuarter");
        var cards = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") };
        cards.Children.Add(Ui.Card(Ui.Column(Ui.Heading("HOME"), home, Ui.Row(
            Ui.Button("+6 Goal   [A]", () => match.AddGoal(TeamSide.Home), "HomeGoal", true),
            Ui.Button("+1 Behind   [S]", () => match.AddBehind(TeamSide.Home), "HomeBehind")))));
        var awayCard = Ui.Card(Ui.Column(Ui.Heading("AWAY"), away, Ui.Row(
            Ui.Button("+6 Goal   [K]", () => match.AddGoal(TeamSide.Away), "AwayGoal", true),
            Ui.Button("+1 Behind   [L]", () => match.AddBehind(TeamSide.Away), "AwayBehind"))));
        Grid.SetColumn(awayCard, 2); cards.Children.Add(awayCard);
        var mode = new ComboBox { ItemsSource = new[] { "Count up", "Countdown" }, SelectedIndex = (int)match.ClockMode, Width = 160 };
        mode.SelectionChanged += (_, _) => { if (mode.SelectedIndex >= 0) match.SetClockMode((ClockMode)mode.SelectedIndex); };
        var duration = Ui.Number((decimal)(int)match.QuarterDuration.TotalMinutes, 0, 1439);
        var durationSeconds = Ui.Number(match.QuarterDuration.Seconds, 0, 59);
        var minutes = Ui.Number(0, 0, 1439); var seconds = Ui.Number(0, 0, 59);
        var undo = Ui.Button("Undo last score   [⌘Z / Ctrl+Z]", Undo, "UndoScore");
        _refreshSport = () =>
        {
            home.Text = $"{match.HomeGoals}.{match.HomeBehinds}   ({match.HomeTotal})";
            away.Text = $"{match.AwayGoals}.{match.AwayBehinds}   ({match.AwayTotal})";
            clock.Text = $"{_session.QuarterLabel}     {DemoSession.Clock(match.DisplayClock)}     {(match.ClockRunning ? "RUNNING" : "PAUSED")}";
            start.IsEnabled = !match.ClockRunning && !match.IsFullTime;
            pause.IsEnabled = match.ClockRunning;
            continueQuarter.IsEnabled = match.CanContinueQuarter;
            end.IsEnabled = !match.IsFullTime && (!match.CanContinueQuarter || match.ElapsedInQuarter > TimeSpan.Zero);
            undo.IsEnabled = match.Events.Count > 0;
        };
        return Ui.Column(cards, Ui.Card(Ui.Column(clock, Ui.Row(start, pause, undo), Ui.Row(end, continueQuarter),
                Ui.Text("Space starts or pauses the clock. Scoring after a quarter ends is recorded in the ended quarter.", 12, Ui.Secondary))),
            Ui.Card(Ui.Column(Ui.Heading("Clock settings"), Ui.Row(Ui.Field("Mode", mode), Ui.Field("Quarter minutes", duration), Ui.Field("Seconds", durationSeconds),
                Ui.Button("Apply duration", () =>
                {
                    var value = TimeSpan.FromSeconds((double)((duration.Value ?? 20) * 60 + (durationSeconds.Value ?? 0)));
                    if (value <= TimeSpan.Zero) { SetStatus("Quarter duration must be at least one second."); return; }
                    match.SetQuarterDuration(value);
                })),
                Ui.Row(Ui.Field("Elapsed minutes", minutes), Ui.Field("Seconds", seconds),
                    Ui.Button("Set elapsed", () => match.SetElapsed(TimeSpan.FromSeconds((double)((minutes.Value ?? 0) * 60 + (seconds.Value ?? 0))))),
                    Ui.Button("−30 seconds", () => match.AdjustElapsed(TimeSpan.FromSeconds(-30))),
                    Ui.Button("+30 seconds", () => match.AdjustElapsed(TimeSpan.FromSeconds(30)))))),
            Ui.Button("Reset AFL match…", async () =>
            {
                if (!await ConfirmAsync("Reset AFL match?", "Scores, quarters and event history will be cleared.", "Reset match")) return;
                _session.LabelOverride = ""; _session.Screen = Presentation.Scoreboard; match.ResetForNewGame(); SaveLocal();
            }));
    }

    private sealed record PlayerChoice(int Index, string Label) { public override string ToString() => Label; }

    private Control BuildCricket()
    {
        var match = _session.Cricket;
        var status = Ui.Text("", 18);
        var striker = new ComboBox { Width = 210, Name = "StrikerPicker" };
        var nonstriker = new ComboBox { Width = 210, Name = "NonStrikerPicker" };
        var bowler = new ComboBox { Width = 210, Name = "BowlerPicker" };
        var players = Ui.Column(Ui.Heading("Players"), Ui.Row(Ui.Field("Striker / incoming batter", striker), Ui.Field("Non-striker", nonstriker),
            Ui.Button("Confirm batters", () =>
            {
                if (striker.SelectedItem is not PlayerChoice s || nonstriker.SelectedItem is not PlayerChoice n || s.Index == n.Index)
                { SetStatus("Choose two different batters who are not out."); return; }
                var inn = match.CurrentInnings;
                if (inn is null) return;
                if (s.Index == inn.NonStrikerIndex && n.Index == inn.StrikerIndex)
                { match.RotateStrike(); return; }
                if (n.Index == inn.StrikerIndex || s.Index == inn.NonStrikerIndex)
                { SetStatus("Use Swap strike to exchange the current batters, then choose the incoming batter."); return; }
                match.SetNonStriker(n.Index); match.SelectNewBatter(s.Index);
            }, "ConfirmBatters")),
            Ui.Row(Ui.Field("Bowler", bowler), Ui.Button("Confirm bowler", () =>
            { if (bowler.SelectedItem is PlayerChoice b) match.ConfirmBowler(b.Index); }, "ConfirmBowler"),
                Ui.Button("Swap strike", match.RotateStrike)));
        var scoring = Ui.Row();
        foreach (int run in new[] { 0, 1, 2, 3, 4, 5, 6 })
        {
            int runs = run;
            scoring.Children.Add(Ui.Button(run == 0 ? "Dot" : $"+{run}", () => match.RecordDelivery(runs, runs switch
            { 0 => CricketDeliveryType.Dot, 4 => CricketDeliveryType.Four, 6 => CricketDeliveryType.Six, _ => CricketDeliveryType.Runs }), $"CricketRuns{run}", run is 4 or 6));
        }
        var extraRuns = Ui.Number(1, 1, 7, 90);
        var extras = Ui.Row(Ui.Field("Total runs (including penalty)", extraRuns));
        foreach (var (label, kind) in new[] { ("Wide", CricketDeliveryType.Wide), ("No ball", CricketDeliveryType.NoBall), ("Bye", CricketDeliveryType.Bye), ("Leg bye", CricketDeliveryType.LegBye) })
            extras.Children.Add(Ui.Button(label, () => match.RecordDelivery((int)(extraRuns.Value ?? 1), kind)));
        var dismissal = new ComboBox { Width = 170, ItemsSource = new[] { "Bowled", "Caught", "LBW", "Run out", "Stumped", "Hit wicket" }, SelectedIndex = 0 };
        var fielder = Ui.Input("", "Fielder (optional)", 160);
        var wicket = Ui.Row(dismissal, fielder, Ui.Button("Wicket", () =>
        {
            var inn = match.CurrentInnings; var batter = inn?.Striker;
            if (inn is null || batter is null) return;
            string text = dismissal.SelectedItem?.ToString() ?? "Bowled";
            var types = new[] { DismissalType.Bowled, DismissalType.Caught, DismissalType.LBW, DismissalType.RunOut, DismissalType.Stumped, DismissalType.HitWicket };
            int before = match.TotalWickets;
            match.RecordDelivery(0, CricketDeliveryType.Wicket, text.ToLowerInvariant());
            if (match.TotalWickets == before) return;
            batter.HowOut = types[dismissal.SelectedIndex];
            batter.DismissalBowler = inn.CurrentBowler?.DisplayName;
            batter.DismissalFielder = fielder.Text;
            match.RaiseMatchChanged();
        }, "CricketWicket"));
        var deliveryControls = Ui.Column(scoring, extras, wicket);
        string signature = "";
        _refreshSport = () =>
        {
            var inn = match.CurrentInnings;
            status.Text = match.MatchResult ?? (inn?.IsComplete == true ? "INNINGS COMPLETE · start the next innings below" :
                inn?.NeedsBatterSelection == true ? "Choose the opening pair or incoming batter" :
                inn?.NeedsBowlerSelection == true ? "Choose a bowler for this over" : $"Ready · {match.RunRate:0.00} runs per over");
            deliveryControls.IsEnabled = inn is { IsComplete: false, NeedsBatterSelection: false, NeedsBowlerSelection: false };
            string next = $"{match.CurrentInningsNumber}:{inn?.StrikerIndex}:{inn?.NonStrikerIndex}:{inn?.CurrentBowlerIndex}:{inn?.TotalWickets}:{inn?.NeedsBowlerSelection}:{inn?.NeedsBatterSelection}";
            if (signature == next || inn is null) return;
            signature = next;
            var batters = inn.BattingOrder.Select((p, i) => new PlayerChoice(i, $"{p.FullName}{(p.IsOut ? " (out)" : "")}"))
                .Where(p => !inn.BattingOrder[p.Index].IsOut).ToArray();
            striker.ItemsSource = batters; nonstriker.ItemsSource = batters;
            striker.SelectedItem = batters.FirstOrDefault(p => p.Index == inn.StrikerIndex)
                ?? batters.FirstOrDefault(p => p.Index != inn.NonStrikerIndex);
            nonstriker.SelectedItem = batters.FirstOrDefault(p => p.Index == inn.NonStrikerIndex)
                ?? batters.FirstOrDefault(p => p.Index != (striker.SelectedItem as PlayerChoice)?.Index);
            var bowlers = inn.BowlingAttack.Select((p, i) => new PlayerChoice(i, p.FullName)).ToArray();
            bowler.ItemsSource = bowlers;
            bowler.SelectedItem = bowlers.FirstOrDefault(p => p.Index == (inn.NeedsBowlerSelection ? match.SuggestedNextBowler() : inn.CurrentBowlerIndex));
        };
        var rosterA = Ui.Input(string.Join('\n', match.TeamAPlayers.Select(p => p.FullName)), width: 280);
        var rosterB = Ui.Input(string.Join('\n', match.TeamBPlayers.Select(p => p.FullName)), width: 280);
        rosterA.AcceptsReturn = rosterB.AcceptsReturn = true; rosterA.Height = rosterB.Height = 180;
        var overs = Ui.Number(match.TotalOvers, 1, 500);
        var format = new ComboBox { ItemsSource = new[] { "Limited overs", "Multi-day", "Multi-innings" }, SelectedIndex = (int)match.Format, Width = 180 };
        var batFirst = new ComboBox { ItemsSource = new[] { "Home bats first", "Away bats first" }, SelectedIndex = match.TeamABatsFirst ? 0 : 1, Width = 180 };
        return Ui.Column(Ui.Card(Ui.Column(status, deliveryControls, Ui.Row(Ui.Button("Undo last delivery", Undo, "UndoDelivery"),
                Ui.Button("End innings…", async () =>
                {
                    if (await ConfirmAsync("End innings?", "The current innings will be marked complete.", "End innings")) match.EndCurrentInnings();
                }), Ui.Button("Next innings", () =>
                {
                    if (match.CanStartNextInnings()) match.StartNextInnings(); else SetStatus("End the current innings first, or the match has already finished.");
                })))), Ui.Card(players),
            Ui.Card(Ui.Column(Ui.Heading("New cricket match"), Ui.Text("One player per line; 2–11 players per team. Applying this setup clears the current cricket match.", 12, Ui.Secondary),
                Ui.Row(Ui.Field("Home roster", rosterA), Ui.Field("Away roster", rosterB)),
                Ui.Row(Ui.Field("Format", format), Ui.Field("Overs per innings", overs), Ui.Field("Batting order", batFirst)),
                Ui.Button("Apply setup & reset cricket…", async () =>
                {
                    List<CricketPlayer> ParseRoster(string? text) => (text ?? "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                        .Select(name => new CricketPlayer { LastName = name }).ToList();
                    var a = ParseRoster(rosterA.Text); var b = ParseRoster(rosterB.Text);
                    if (a.Count is < 2 or > 11 || b.Count is < 2 or > 11) { SetStatus("Each roster must contain 2–11 players."); return; }
                    if (!await ConfirmAsync("Reset cricket match?", "All deliveries and innings will be cleared.", "Reset cricket")) return;
                    match.TeamAPlayers = a; match.TeamBPlayers = b;
                    match.TotalOvers = (int)(overs.Value ?? 20); match.Format = (CricketFormat)format.SelectedIndex;
                    match.TotalInnings = match.Format == CricketFormat.LimitedOvers ? 2 : 4;
                    match.TeamABatsFirst = batFirst.SelectedIndex == 0;
                    match.StartMatch(); SaveLocal();
                }))));
    }

    private Control BuildTraining()
    {
        var session = _session.Training;
        var clock = Ui.Text("", 32);
        var queue = new ListBox { Name = "TrainingQueue", MinHeight = 120, MaxHeight = 280 };
        var title = Ui.Input("Work", "Interval name");
        var minutes = Ui.Number(1, 0, 1439); var seconds = Ui.Number(0, 0, 59);
        var auto = new CheckBox { Content = "Start next interval automatically", IsChecked = session.AutoAdvance };
        auto.IsCheckedChanged += (_, _) => session.AutoAdvance = auto.IsChecked == true;
        TimeSpan Duration() => TimeSpan.FromSeconds((double)((minutes.Value ?? 0) * 60 + (seconds.Value ?? 0)));
        int queueVersion = -1;
        string queueState = "";
        _refreshSport = () =>
        {
            clock.Text = $"{DemoSession.Clock(session.ActiveRemaining)}   {(session.IsRunning ? "RUNNING" : session.IsExhausted ? "COMPLETE" : "PAUSED")}   ·   Session {DemoSession.Clock(session.TotalRemaining)}";
            string next = string.Join('|', session.Timers.Select(t => $"{t.Id}:{t.Title}:{t.Duration}:{t.HasFinished}"));
            if (queueVersion == session.ActiveIndex && queueState == next) return;
            queueVersion = session.ActiveIndex; queueState = next;
            int selected = queue.SelectedIndex;
            queue.ItemsSource = session.Timers.Select((t, i) => $"{(i == session.ActiveIndex ? "▶ " : t.HasFinished ? "✓ " : "   ")}{t.Title}   {DemoSession.Clock(t.Duration)}").ToArray();
            queue.SelectedIndex = selected >= 0 && selected < session.Timers.Count ? selected : session.ActiveIndex;
        };
        queue.SelectionChanged += (_, _) =>
        {
            if (queue.SelectedIndex < 0 || queue.SelectedIndex >= session.Timers.Count) return;
            var t = session.Timers[queue.SelectedIndex]; title.Text = t.Title;
            minutes.Value = (decimal)(int)t.Duration.TotalMinutes; seconds.Value = t.Duration.Seconds;
        };
        void WithSelected(Action<TrainingTimer> action)
        { if (queue.SelectedIndex >= 0 && queue.SelectedIndex < session.Timers.Count) action(session.Timers[queue.SelectedIndex]); }
        return Ui.Column(Ui.Card(Ui.Column(clock, Ui.Row(Ui.Button("Start / pause   [Space]", ToggleClock, "TrainingToggle", true),
                Ui.Button("Reset active", () => { _session.ClearCelebration(); session.ResetActive(); }),
                Ui.Button("Skip interval", () => { if (session.Active is not null) { _session.ClearCelebration(); session.Skip(); } }),
                Ui.Button("+30 seconds", () => session.AddTimeToActive(TimeSpan.FromSeconds(30)))), auto)),
            Ui.Card(Ui.Column(Ui.Heading("Timer queue"), queue,
                Ui.Row(Ui.Field("Title", title), Ui.Field("Minutes", minutes), Ui.Field("Seconds", seconds)),
                Ui.Row(Ui.Button("Add timer", () =>
                {
                    if (Duration() <= TimeSpan.Zero) { SetStatus("Choose a duration of at least one second."); return; }
                    session.AddTimer(new TrainingTimer(title.Text ?? "Timer", Duration()));
                }, "AddTimer", true), Ui.Button("Update selected", () => WithSelected(t =>
                {
                    if (Duration() <= TimeSpan.Zero) { SetStatus("Choose a duration of at least one second."); return; }
                    session.UpdateTimer(t, title.Text, Duration());
                })), Ui.Button("Remove selected", () => WithSelected(session.RemoveTimer)),
                    Ui.Button("Move up", () => WithSelected(t => session.MoveTimer(t, queue.SelectedIndex - 1))),
                    Ui.Button("Move down", () => WithSelected(t => session.MoveTimer(t, queue.SelectedIndex + 1)))))),
            Ui.Card(Ui.Column(Ui.Heading("Interval builder"), BuildIntervals())),
            Ui.Button("Clear all timers…", async () =>
            {
                if (!await ConfirmAsync("Clear all timers?", "The timer queue and session progress will be cleared.", "Clear timers")) return;
                _session.ClearCelebration(); session.Clear(); SaveLocal();
            }));
    }

    private Control BuildIntervals()
    {
        var repeats = Ui.Number(5, 1, 100); var work = Ui.Number(60, 1, 86400); var rest = Ui.Number(30, 1, 86400);
        return Ui.Row(Ui.Field("Rounds", repeats), Ui.Field("Work seconds", work), Ui.Field("Rest seconds", rest),
            Ui.Button("Append intervals", () =>
            {
                for (int i = 0; i < (int)(repeats.Value ?? 5); i++)
                {
                    _session.Training.AddTimer(new TrainingTimer($"Work {i + 1}", TimeSpan.FromSeconds((double)(work.Value ?? 60))));
                    _session.Training.AddTimer(new TrainingTimer($"Rest {i + 1}", TimeSpan.FromSeconds((double)(rest.Value ?? 30))));
                }
            }));
    }
}

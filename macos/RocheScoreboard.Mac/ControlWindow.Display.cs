using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Roche_Scoreboard.Models;

namespace RocheScoreboard.Mac;

public sealed partial class ControlWindow
{
    private Action? _refreshHistory;

    private Control BuildDisplay()
    {
        bool cricket = _session.Sport == DemoSport.Cricket;
        var home = Ui.Input(cricket ? _session.Cricket.TeamAName : _session.Afl.HomeName);
        var away = Ui.Input(cricket ? _session.Cricket.TeamBName : _session.Afl.AwayName);
        var homeAbbr = Ui.Input(cricket ? _session.Cricket.TeamAAbbr : _session.Afl.HomeAbbr, width: 100);
        var awayAbbr = Ui.Input(cricket ? _session.Cricket.TeamBAbbr : _session.Afl.AwayAbbr, width: 100);
        var homeColor = Ui.Input(_session.HomeColor, width: 120); var awayColor = Ui.Input(_session.AwayColor, width: 120);
        var homeText = Ui.Input(_session.HomeTextColor, width: 120); var awayText = Ui.Input(_session.AwayTextColor, width: 120);
        var homeLogo = Ui.Input(_session.HomeLogo, "PNG or JPEG path", 300);
        var awayLogo = Ui.Input(_session.AwayLogo, "PNG or JPEG path", 300);
        async Task PickLogo(TextBox field)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            { Title = "Choose team logo", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Team logo") { Patterns = ["*.png", "*.jpg", "*.jpeg"] }] });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) field.Text = path;
        }
        var teams = Ui.Card(Ui.Column(Ui.Heading("Teams & colours"),
            Ui.Row(Ui.Field("Home name", home), Ui.Field("Abbreviation", homeAbbr), Ui.Field("Primary #RRGGBB", homeColor), Ui.Field("Text #RRGGBB", homeText)),
            Ui.Row(Ui.Field("Away name", away), Ui.Field("Abbreviation", awayAbbr), Ui.Field("Primary #RRGGBB", awayColor), Ui.Field("Text #RRGGBB", awayText)),
            Ui.Row(Ui.Field("Home logo (optional)", homeLogo), Ui.Button("Browse…", () => _ = PickLogo(homeLogo))),
            Ui.Row(Ui.Field("Away logo (optional)", awayLogo), Ui.Button("Browse…", () => _ = PickLogo(awayLogo))),
            Ui.Button("Apply teams & colours", () =>
            {
                if (string.IsNullOrWhiteSpace(home.Text) || string.IsNullOrWhiteSpace(away.Text))
                { SetStatus("Enter names for both teams."); return; }
                if (!new[] { homeColor.Text, awayColor.Text, homeText.Text, awayText.Text }.All(Ui.ValidColor))
                { SetStatus("Colours must use #RRGGBB, for example #2F63D1."); return; }
                if (new[] { homeLogo.Text, awayLogo.Text }.Any(p => !string.IsNullOrWhiteSpace(p) && !File.Exists(p)))
                { SetStatus("A selected logo file does not exist. Choose a local file or clear its path."); return; }
                if (cricket)
                {
                    if (home.Text.Trim() == away.Text.Trim()) { SetStatus("Cricket team names must be different."); return; }
                    string oldA = _session.Cricket.TeamAName, oldB = _session.Cricket.TeamBName;
                    foreach (var inn in _session.Cricket.AllInnings)
                    {
                        inn.BattingTeamName = inn.BattingTeamName == oldA ? home.Text.Trim() : away.Text.Trim();
                        inn.BowlingTeamName = inn.BowlingTeamName == oldB ? away.Text.Trim() : home.Text.Trim();
                    }
                    _session.Cricket.SetTeams(home.Text, homeAbbr.Text ?? "HOM", away.Text, awayAbbr.Text ?? "AWY");
                    _session.Cricket.TeamAPrimaryColor = homeColor.Text!; _session.Cricket.TeamBPrimaryColor = awayColor.Text!;
                    _session.Cricket.TeamASecondaryColor = homeText.Text!; _session.Cricket.TeamBSecondaryColor = awayText.Text!;
                }
                else _session.Afl.SetTeams(home.Text, homeAbbr.Text ?? "HOM", away.Text, awayAbbr.Text ?? "AWY");
                _session.HomeColor = homeColor.Text!; _session.AwayColor = awayColor.Text!;
                _session.HomeTextColor = homeText.Text!; _session.AwayTextColor = awayText.Text!;
                _session.HomeLogo = homeLogo.Text ?? ""; _session.AwayLogo = awayLogo.Text ?? "";
                _session.Notify(); SaveLocal(); SetStatus("Team settings applied to the demo output.");
            }, "ApplyTeams", true)));
        var screens = _session.Sport switch
        {
            DemoSport.Afl => Enum.GetValues<Presentation>(),
            DemoSport.Cricket => [Presentation.Scoreboard, Presentation.Summary, Presentation.Statistics, Presentation.Intro],
            _ => new[] { Presentation.Scoreboard, Presentation.Summary, Presentation.Intro }
        };
        var screen = new ComboBox { ItemsSource = screens, SelectedItem = _session.Screen, Width = 200 };
        _presentationPicker = screen;
        screen.SelectionChanged += (_, _) => { if (screen.SelectedItem is Presentation p) { _session.Screen = p; _session.Notify(); } };
        var stacked = new CheckBox { Content = "Stack teams vertically", IsChecked = _session.Stacked };
        stacked.IsCheckedChanged += (_, _) => { _session.Stacked = stacked.IsChecked == true; _session.Notify(); };
        var reduced = new CheckBox { Content = "Reduce motion", IsChecked = _session.ReducedMotion };
        reduced.IsCheckedChanged += (_, _) => { _session.ReducedMotion = reduced.IsChecked == true; _session.Notify(); };
        var label = Ui.Input(_session.LabelOverride, "Automatic quarter label", 200); label.MaxLength = 12;
        return Ui.Column(teams, Ui.Card(Ui.Column(Ui.Heading("Output window"),
            Ui.Row(Ui.Field("Presentation", screen), Ui.Button("Open / bring forward", ShowOutput)), stacked, reduced,
            Ui.Row(Ui.Field("AFL centre label (12 characters)", label), Ui.Button("Apply label", () =>
            { _session.LabelOverride = label.Text?.Trim() ?? ""; _session.Notify(); }), Ui.Button("Automatic label", () =>
            { label.Text = ""; _session.LabelOverride = ""; _session.Notify(); })),
            Ui.Text("Resize or move the output window freely. Double-click its title bar to maximise. Team logos remain local files; copy them with an exported demo when sharing.", 12, Ui.Secondary))));
    }

    private Control BuildMessages()
    {
        var list = new ListBox { MinHeight = 120, MaxHeight = 240, ItemsSource = _session.Messages.ToArray() };
        var message = Ui.Input("", "Ticker message", 430); message.MaxLength = 500;
        var announcement = Ui.Input("", "Message to show on output for 4.2 seconds", 430); announcement.MaxLength = 500;
        void RefreshMessages() { list.ItemsSource = _session.Messages.ToArray(); _session.Notify(); }
        return Ui.Column(Ui.Card(Ui.Column(Ui.Heading("Rotating ticker"),
                Ui.Text("Messages rotate every five seconds along the bottom of the output.", 12, Ui.Secondary), list,
                Ui.Row(message, Ui.Button("Add message", () =>
                {
                    if (string.IsNullOrWhiteSpace(message.Text)) return;
                    _session.Messages.Add(message.Text.Trim()); message.Text = ""; RefreshMessages();
                }, "AddMessage", true)),
                Ui.Row(Ui.Button("Remove selected", () =>
                {
                    int index = list.SelectedIndex;
                    if (index >= 0 && index < _session.Messages.Count) { _session.Messages.RemoveAt(index); RefreshMessages(); }
                }), Ui.Button("Clear ticker…", async () =>
                {
                    if (!await ConfirmAsync("Clear ticker?", "All rotating ticker messages will be removed.", "Clear ticker")) return;
                    _session.Messages.Clear(); RefreshMessages();
                })))),
            Ui.Card(Ui.Column(Ui.Heading("Announcement"), Ui.Row(announcement, Ui.Button("Show announcement", () =>
            { _session.Announce(announcement.Text ?? ""); announcement.Text = ""; }, "ShowAnnouncement", true)))));
    }

    private sealed record HistoryItem(int Index, string Label) { public override string ToString() => Label; }

    private Control BuildHistory()
    {
        var list = new ListBox { Name = "MatchHistory", MinHeight = 150, MaxHeight = 300 };
        var validation = Ui.Text("", 12, Ui.Secondary);
        var side = new ComboBox { ItemsSource = new[] { "Home", "Away" }, SelectedIndex = 0, Width = 120 };
        var type = new ComboBox { ItemsSource = new[] { "Goal", "Behind" }, SelectedIndex = 0, Width = 120 };
        var quarter = Ui.Number(_session.Afl.Quarter, 1, 4, 80); var minutes = Ui.Number(0, 0, 1439); var seconds = Ui.Number(0, 0, 59);
        list.SelectionChanged += (_, _) =>
        {
            if (_session.Sport != DemoSport.Afl || list.SelectedItem is not HistoryItem item) return;
            if (item.Index >= _session.Afl.Events.Count) return;
            var score = _session.Afl.Events[item.Index];
            side.SelectedIndex = (int)score.Team; type.SelectedIndex = (int)score.Type;
            quarter.Value = score.Quarter; minutes.Value = (decimal)(int)score.GameTime.TotalMinutes; seconds.Value = score.GameTime.Seconds;
        };
        _refreshHistory = () =>
        {
            int? index = (list.SelectedItem as HistoryItem)?.Index;
            IEnumerable<HistoryItem> items = _session.Sport switch
            {
                DemoSport.Afl => _session.Afl.Events.Select((s, i) => new HistoryItem(i, s.FormatLog(_session.Afl.HomeName, _session.Afl.AwayName))).Reverse(),
                DemoSport.Cricket => _session.Cricket.AllInnings.SelectMany(inn => inn.Deliveries).Select((d, i) =>
                    new HistoryItem(i, $"Innings {d.Innings} · {d.OverNumber}.{d.BallInOver} · {d.Description} · {d.TotalAfter}/{d.WicketsAfter}")).Reverse(),
                _ => _session.Training.Timers.Select((t, i) => new HistoryItem(i, $"{t.Title} · {DemoSession.Clock(t.Duration)} · {(t.HasFinished ? "Complete" : "Queued / active")}"))
            };
            var rows = items.ToArray();
            list.ItemsSource = rows;
            list.SelectedItem = rows.FirstOrDefault(r => r.Index == index);
            validation.Text = _session.Sport == DemoSport.Afl
                ? _session.Afl.ValidateScoreSequence() is { Count: > 0 } warnings ? string.Join('\n', warnings.Select(w => w.Message)) : "Score history is consistent. Corrections rebuild scores and quarter snapshots."
                : $"{rows.Length} records. Cricket undo restores the full state before the last delivery.";
        };
        TimeSpan EventTime() => TimeSpan.FromSeconds((double)((minutes.Value ?? 0) * 60 + (seconds.Value ?? 0)));
        var editor = Ui.Card(Ui.Column(Ui.Heading("AFL event corrections"),
            Ui.Row(Ui.Field("Team", side), Ui.Field("Type", type), Ui.Field("Quarter", quarter), Ui.Field("Minutes", minutes), Ui.Field("Seconds", seconds)),
            Ui.Row(Ui.Button("Update selected event", () =>
            {
                if (list.SelectedItem is HistoryItem item)
                    _session.Afl.ModifyEvent(item.Index, (TeamSide)side.SelectedIndex, (ScoreType)type.SelectedIndex, EventTime(), (int)(quarter.Value ?? 1));
            }), Ui.Button("Add recorded score", () => _session.Afl.AddRecordedScore((TeamSide)side.SelectedIndex,
                (ScoreType)type.SelectedIndex, (int)(quarter.Value ?? 1), EventTime())),
                Ui.Button("Remove selected event…", async () =>
                {
                    if (list.SelectedItem is not HistoryItem item) return;
                    if (await ConfirmAsync("Remove score event?", item.Label, "Remove event")) _session.Afl.RemoveEvent(item.Index);
                }))));
        editor.IsVisible = _session.Sport == DemoSport.Afl;
        RefreshHistory();
        return Ui.Column(Ui.Heading("Match history"), list, validation, Ui.Row(Ui.Button("Undo latest score / delivery", Undo)), editor);
    }

    private void RefreshHistory() => _refreshHistory?.Invoke();
}

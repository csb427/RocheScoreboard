using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;

namespace RocheScoreboard.Mac;

public sealed partial class ControlWindow : Window
{
    private readonly DemoSession _session;
    private readonly SessionStore _store;
    private readonly DispatcherTimer _timer;
    private readonly TextBlock _header = Ui.Text("", 20);
    private readonly TextBlock _status = Ui.Text("Local demo · no connection to a live scoreboard", 12, Ui.Secondary);
    private readonly TabControl _tabs = new();
    private readonly ComboBox _sport;
    private OutputWindow? _output;
    private bool _shuttingDown;
    private DateTime _lastSave = DateTime.UtcNow;
    private Action? _refreshSport;
    private ComboBox? _presentationPicker;

    public ControlWindow(DemoSession session, SessionStore? store = null)
    {
        _session = session;
        _store = store ?? new SessionStore();
        Title = "Roche Scoreboard · Mac Demo";
        Width = 1160; Height = 820; MinWidth = 760; MinHeight = 560;
        Background = Ui.Background;
        _sport = new ComboBox { Name = "SportPicker", ItemsSource = new[] { "AFL", "Cricket", "Training" }, SelectedIndex = (int)session.Sport, Width = 140 };
        _sport.SelectionChanged += (_, _) =>
        {
            if (_sport.SelectedIndex >= 0) _session.SwitchSport((DemoSport)_sport.SelectedIndex);
        };
        var title = Ui.Row(Ui.Heading("Roche Scoreboard"), _sport,
            Ui.Button("Open output window", ShowOutput, "OpenOutput", true),
            Ui.Button("Save demo…", () => _ = ExportAsync()), Ui.Button("Load demo…", () => _ = ImportAsync()));
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        root.Children.Add(new Border { Padding = new Thickness(20, 12), Child = title });
        var scoreboard = Ui.Card(_header); scoreboard.Margin = new Thickness(20, 0, 20, 12);
        Grid.SetRow(scoreboard, 1); root.Children.Add(scoreboard);
        Grid.SetRow(_tabs, 2); root.Children.Add(_tabs);
        var footer = new Border { Padding = new Thickness(20, 8), Child = _status };
        Grid.SetRow(footer, 3); root.Children.Add(footer);
        Content = root;
        BuildTabs();
        session.Changed += OnSessionChanged;
        session.SportChanged += OnSportChanged;
        KeyDown += HandleKeys;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) =>
        {
            _session.Tick();
            Refresh();
            if ((DateTime.UtcNow - _lastSave).TotalSeconds >= 5) SaveLocal();
        };
        _timer.Start();
        Closing += (_, _) =>
        {
            _shuttingDown = true;
            _timer.Stop();
            _session.Afl.PauseClock();
            _session.Training.Pause();
            SaveLocal();
            _output?.Close();
            session.Changed -= OnSessionChanged;
            session.SportChanged -= OnSportChanged;
        };
        Refresh();
    }

    public void SetStatus(string status) => _status.Text = status;

    private void BuildTabs()
    {
        _refreshSport = null;
        var items = new List<TabItem>();
        items.Add(new TabItem
        {
            Header = _session.Sport switch { DemoSport.Afl => "Score & clock", DemoSport.Cricket => "Cricket scoring", _ => "Training timers" },
            Content = Ui.Scroll(_session.Sport switch
            { DemoSport.Afl => BuildAfl(), DemoSport.Cricket => BuildCricket(), _ => BuildTraining() })
        });
        items.Add(new TabItem { Header = "Teams & display", Content = Ui.Scroll(BuildDisplay()) });
        items.Add(new TabItem { Header = "Messages", Content = Ui.Scroll(BuildMessages()) });
        items.Add(new TabItem { Header = "Match history", Content = Ui.Scroll(BuildHistory()) });
        _tabs.ItemsSource = items;
        _tabs.SelectedIndex = 0;
    }

    private void OnSportChanged()
    {
        _sport.SelectedIndex = (int)_session.Sport;
        BuildTabs();
    }

    private void OnSessionChanged()
    {
        if (_shuttingDown) return;
        Refresh();
        RefreshHistory();
    }

    private void Refresh()
    {
        _header.Text = _session.Header;
        if (_presentationPicker is not null && !Equals(_presentationPicker.SelectedItem, _session.Screen))
            _presentationPicker.SelectedItem = _session.Screen;
        _refreshSport?.Invoke();
        _output?.RefreshClock();
    }

    public void ShowOutput()
    {
        if (_output is not null) { _output.Activate(); return; }
        _output = new OutputWindow(_session);
        _output.Closed += (_, _) => _output = null;
        // An independent top-level window can be dragged onto another Mac display.
        _output.Show();
    }

    private void SaveLocal()
    {
        _lastSave = DateTime.UtcNow;
        try { _store.Save(_session); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { SetStatus($"Autosave failed: {ex.Message}"); }
    }

    private async Task ExportAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        { Title = "Save demo session", SuggestedFileName = "roche-demo.json", DefaultExtension = "json", FileTypeChoices = [JsonFile] });
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            using var writer = new StreamWriter(stream);
            await writer.WriteAsync(SessionStore.Serialize(_session));
            SetStatus("Demo saved. The saved session will open with clocks paused.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus($"Could not save: {ex.Message}"); }
    }

    private async Task ImportAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        { Title = "Load demo session", AllowMultiple = false, FileTypeFilter = [JsonFile] });
        if (files.Count == 0) return;
        if (!await ConfirmAsync("Load demo?", "This replaces the current local demo in all sports.", "Load demo")) return;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            SessionStore.Restore(_session, await reader.ReadToEndAsync());
            BuildTabs();
            _session.Notify();
            SaveLocal();
            SetStatus("Demo loaded. Clocks are paused.");
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NullReferenceException)
        { SetStatus($"Could not load this demo: {ex.Message}"); }
    }

    private static readonly FilePickerFileType JsonFile = new("Roche demo") { Patterns = ["*.json"] };

    private async Task<bool> ConfirmAsync(string title, string detail, string confirmText)
    {
        var dialog = new Window { Title = title, Width = 430, Height = 220, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var cancel = Ui.Button("Cancel", () => dialog.Close(false));
        dialog.Content = new Border { Padding = new Thickness(24), Child = Ui.Column(Ui.Heading(title), Ui.Text(detail),
            Ui.Row(cancel, Ui.Button(confirmText, () => dialog.Close(true)))) };
        dialog.Opened += (_, _) => cancel.Focus();
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; dialog.Close(false); } };
        return await dialog.ShowDialog<bool>(this);
    }

    private void ToggleClock()
    {
        if (_session.Sport == DemoSport.Afl)
        {
            if (_session.Afl.ClockRunning) _session.Afl.PauseClock();
            else StartAflClock();
        }
        else if (_session.Sport == DemoSport.Training)
        {
            if (_session.Training.IsRunning) _session.Training.Pause(); else _session.Training.Start();
        }
        Refresh();
    }

    private void StartAflClock()
    {
        if (_session.Afl.IsFullTime) return;
        if (_session.Afl.CanContinueQuarter) _session.LabelOverride = "";
        _session.Screen = Presentation.Scoreboard;
        _session.Afl.StartClock();
    }

    private void Undo()
    {
        _session.ClearCelebration();
        if (_session.Sport == DemoSport.Afl) _session.Afl.UndoLastScore();
        else if (_session.Sport == DemoSport.Cricket) _session.Cricket.UndoLastDelivery();
    }

    private void HandleKeys(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        // Routed key events can originate from the window or a templated child.
        // Consult actual keyboard focus, including ancestors of composite inputs.
        var focused = FocusManager?.GetFocusedElement() as Visual;
        if (e.Source is TextBox or NumericUpDown or ComboBox
            || focused is TextBox or NumericUpDown or ComboBox
            || focused?.GetVisualAncestors().Any(v => v is TextBox or NumericUpDown or ComboBox) == true) return;
        if ((e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) != 0)
        {
            if (e.Key == Key.Z) { Undo(); e.Handled = true; }
            return;
        }
        if (e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key == Key.Space) { ToggleClock(); e.Handled = true; return; }
        if (_session.Sport != DemoSport.Afl) return;
        switch (e.Key)
        {
            case Key.A: _session.Afl.AddGoal(TeamSide.Home); break;
            case Key.S: _session.Afl.AddBehind(TeamSide.Home); break;
            case Key.K: _session.Afl.AddGoal(TeamSide.Away); break;
            case Key.L: _session.Afl.AddBehind(TeamSide.Away); break;
            default: return;
        }
        e.Handled = true;
    }
}

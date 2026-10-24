using RocheScoreboard.Mac.Production;
// Generated from Views/ScoreEventEditorWindow.xaml.cs; edit translator/adapters.
// Source SHA-256: 905009d604b53b3eb04cccaec52917d1f753c3bbbb70ef6c47cb5c3c1deb18f3
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Input;
using RocheScoreboard.Mac.Native;
using Roche_Scoreboard.Models;
using MediaColor = Avalonia.Media.Color;

namespace Roche_Scoreboard.Views;

/// <summary>
/// Modal dialog that lets the operator edit or delete a score event.
/// </summary>
public partial class ScoreEventEditorWindow : NativeWindow
{
    /// <summary>The action the operator chose: Save, Delete, or Cancel (window closed).</summary>
    public enum EditorResult { Cancelled, Saved, Deleted }

    public EditorResult Result { get; private set; } = EditorResult.Cancelled;
    public TeamSide SelectedTeam { get; private set; }
    public ScoreType SelectedType { get; private set; }
    public TimeSpan SelectedGameTime { get; private set; }
    public int SelectedQuarter { get; private set; } = 1;

    private static readonly SolidColorBrush s_selectedBg = new(MediaColor.FromRgb(0x1C, 0x24, 0x33));
    private static readonly SolidColorBrush s_unselectedBg = new(MediaColor.FromRgb(0x0D, 0x11, 0x17));
    private static readonly SolidColorBrush s_dimBorder = new(MediaColor.FromRgb(0x30, 0x36, 0x3D));
    private SolidColorBrush _homeBorder = new(MediaColor.FromRgb(0x58, 0xA6, 0xFF));
    private SolidColorBrush _awayBorder = new(MediaColor.FromRgb(0xFF, 0x77, 0x55));
    private SolidColorBrush _homeSelectedBg = s_selectedBg;
    private SolidColorBrush _awaySelectedBg = s_selectedBg;
    private static readonly SolidColorBrush s_goalBorder = new(MediaColor.FromRgb(0x3F, 0xB9, 0x50));
    private static readonly SolidColorBrush s_behindBorder = new(MediaColor.FromRgb(0xD2, 0x99, 0x22));

    private readonly bool _isAddMode;
    private readonly int _currentQuarter;
    private readonly TimeSpan _currentGameTime;

    public ScoreEventEditorWindow(ScoreEvent ev, string homeName, string awayName,
        string homeColor = "#58A6FF", string awayColor = "#FF7755")
    {
        NativePointer.EnsureInitialized(); InitializeComponent(); ProductionNames.CompleteLoad(this);

        _currentQuarter = ev.Quarter;
        _currentGameTime = ev.GameTime;
        ApplyTeamPalette(homeColor, awayColor);

        string clock = $"Q{ev.Quarter}  {(int)ev.GameTime.TotalMinutes:D2}:{ev.GameTime.Seconds:D2}";
        EventInfoText.Text = clock;
        EventScoreText.Text = $"After: {ev.HomeGoals}.{ev.HomeBehinds}.{ev.HomeTotal} – {ev.AwayGoals}.{ev.AwayBehinds}.{ev.AwayTotal}";

        HomeTeamLabel.Text = homeName;
        AwayTeamLabel.Text = awayName;

        SelectedTeam = ev.Team;
        SelectedType = ev.Type;
        SelectedQuarter = ev.Quarter;
        SelectedGameTime = ev.GameTime;

        QuarterBox.SelectedIndex = Math.Clamp(ev.Quarter - 1, 0, 3);
        UseCurrentQuarterBox.IsVisible = false;
        UseCurrentTimeBox.IsVisible = false;
        QuarterBox.IsEnabled = true;
        MinutesBox.Text = ((int)ev.GameTime.TotalMinutes).ToString("D2");
        SecondsBox.Text = ev.GameTime.Seconds.ToString("D2");

        UpdateTeamVisuals();
        UpdateTypeVisuals();
    }

    public ScoreEventEditorWindow(string homeName, string awayName, int currentQuarter, TimeSpan currentGameTime,
        string homeColor = "#58A6FF", string awayColor = "#FF7755")
    {
        NativePointer.EnsureInitialized(); InitializeComponent(); ProductionNames.CompleteLoad(this);
        _isAddMode = true;
        _currentQuarter = Math.Clamp(currentQuarter, 1, 4);
        _currentGameTime = currentGameTime < TimeSpan.Zero ? TimeSpan.Zero : currentGameTime;
        ApplyTeamPalette(homeColor, awayColor);

        Title = "Add Score Event";
        DialogHeading.Text = "ADD SCORE TO MATCH LOG";
        EventInfoText.Text = "TEAM AND SCORE TYPE ARE REQUIRED";
        EventScoreText.Text = "Quarter and time default to the live match unless you override them.";
        HomeTeamLabel.Text = homeName;
        AwayTeamLabel.Text = awayName;
        SelectedTeam = TeamSide.Home;
        SelectedType = ScoreType.Goal;
        SelectedQuarter = _currentQuarter;
        SelectedGameTime = _currentGameTime;
        QuarterBox.SelectedIndex = _currentQuarter - 1;
        MinutesBox.Text = ((int)_currentGameTime.TotalMinutes).ToString("D2");
        SecondsBox.Text = _currentGameTime.Seconds.ToString("D2");
        DeleteButton.IsVisible = false;
        SaveButton.Content = "ADD SCORE";
        OptionalTiming_Changed(this, new RoutedEventArgs());
        UpdateTeamVisuals();
        UpdateTypeVisuals();
    }

    private void ApplyTeamPalette(string homeHex, string awayHex)
    {
        MediaColor home = ParseColor(homeHex, MediaColor.FromRgb(0x58, 0xA6, 0xFF));
        MediaColor away = ParseColor(awayHex, MediaColor.FromRgb(0xFF, 0x77, 0x55));
        _homeBorder = new SolidColorBrush(home);
        _awayBorder = new SolidColorBrush(away);
        _homeSelectedBg = new SolidColorBrush(MediaColor.FromArgb(0x72, home.R, home.G, home.B));
        _awaySelectedBg = new SolidColorBrush(MediaColor.FromArgb(0x72, away.R, away.G, away.B));
        HomeTeamLabel.Foreground = new SolidColorBrush(Roche_Scoreboard.Services.SmartContrast.PickForeground(home));
        AwayTeamLabel.Foreground = new SolidColorBrush(Roche_Scoreboard.Services.SmartContrast.PickForeground(away));
    }

    private static MediaColor ParseColor(string value, MediaColor fallback)
    {
        try { return (MediaColor)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString(value); }
        catch { return fallback; }
    }

    private void OptionalTiming_Changed(object? sender, RoutedEventArgs e)
    {
        if (QuarterBox is null || MinutesBox is null || SecondsBox is null) return;
        QuarterBox.IsEnabled = UseCurrentQuarterBox?.IsChecked != true;
        bool customTime = UseCurrentTimeBox?.IsChecked != true;
        MinutesBox.IsEnabled = customTime;
        SecondsBox.IsEnabled = customTime;
    }

    private void HomeTeam_Click(object? sender, PointerEventArgs e)
    {
        SelectedTeam = TeamSide.Home;
        UpdateTeamVisuals();
    }

    private void AwayTeam_Click(object? sender, PointerEventArgs e)
    {
        SelectedTeam = TeamSide.Away;
        UpdateTeamVisuals();
    }

    private void Goal_Click(object? sender, PointerEventArgs e)
    {
        SelectedType = ScoreType.Goal;
        UpdateTypeVisuals();
    }

    private void Behind_Click(object? sender, PointerEventArgs e)
    {
        SelectedType = ScoreType.Behind;
        UpdateTypeVisuals();
    }

    private void UpdateTeamVisuals()
    {
        bool home = SelectedTeam == TeamSide.Home;

        HomeTeamBtn.Background = home ? _homeSelectedBg : s_unselectedBg;
        HomeTeamBtn.BorderBrush = home ? _homeBorder : s_dimBorder;
        HomeTeamBtn.Opacity = home ? 1.0 : 0.5;

        AwayTeamBtn.Background = !home ? _awaySelectedBg : s_unselectedBg;
        AwayTeamBtn.BorderBrush = !home ? _awayBorder : s_dimBorder;
        AwayTeamBtn.Opacity = !home ? 1.0 : 0.5;
    }

    private void UpdateTypeVisuals()
    {
        bool goal = SelectedType == ScoreType.Goal;

        GoalBtn.Background = goal ? s_selectedBg : s_unselectedBg;
        GoalBtn.BorderBrush = goal ? s_goalBorder : s_dimBorder;
        GoalBtn.Opacity = goal ? 1.0 : 0.5;

        BehindBtn.Background = !goal ? s_selectedBg : s_unselectedBg;
        BehindBtn.BorderBrush = !goal ? s_behindBorder : s_dimBorder;
        BehindBtn.Opacity = !goal ? 1.0 : 0.5;
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        SelectedQuarter = _isAddMode && UseCurrentQuarterBox.IsChecked == true
            ? _currentQuarter
            : Math.Clamp(QuarterBox.SelectedIndex + 1, 1, 4);

        if (_isAddMode && UseCurrentTimeBox.IsChecked == true)
        {
            SelectedGameTime = _currentGameTime;
            Result = EditorResult.Saved;
            Close();
            return;
        }

        if (!int.TryParse((MinutesBox.Text ?? string.Empty), out int minutes) || minutes < 0)
        {
            RocheScoreboard.Mac.Native.NativeMessageBox.Show("Please enter a valid number of minutes.", "Invalid Time", NativeMessageBoxButton.OK, NativeMessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse((SecondsBox.Text ?? string.Empty), out int seconds) || seconds < 0 || seconds > 59)
        {
            RocheScoreboard.Mac.Native.NativeMessageBox.Show("Seconds must be between 0 and 59.", "Invalid Time", NativeMessageBoxButton.OK, NativeMessageBoxImage.Warning);
            return;
        }

        SelectedGameTime = new TimeSpan(0, minutes, seconds);
        Result = EditorResult.Saved;
        Close();
    }

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {
        NativeMessageBoxResult confirm = RocheScoreboard.Mac.Native.NativeMessageBox.Show(
            "Are you sure you want to delete this score event?\n\nAll scores will be recalculated.",
            "Delete Score Event",
            NativeMessageBoxButton.YesNo,
            NativeMessageBoxImage.Warning);

        if (confirm != NativeMessageBoxResult.Yes) return;

        Result = EditorResult.Deleted;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Result = EditorResult.Cancelled;
        Close();
    }
}

public partial class ScoreEventEditorWindow
{
    private void Native_AwayTeam_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; AwayTeam_Click(sender!, e); }
    private void Native_Behind_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; Behind_Click(sender!, e); }
    private void Native_Goal_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; Goal_Click(sender!, e); }
    private void Native_HomeTeam_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; HomeTeam_Click(sender!, e); }
    private void Native_OptionalTiming_Changed(object? sender, RoutedEventArgs e) { OptionalTiming_Changed(sender!, e); }
}

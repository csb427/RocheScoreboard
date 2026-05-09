using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using MediaColor = System.Windows.Media.Color;

namespace Roche_Scoreboard.Views;

/// <summary>
/// Modal dialog that lets the operator edit or delete a score event.
/// </summary>
public partial class ScoreEventEditorWindow : Window
{
    /// <summary>The action the operator chose: Save, Delete, or Cancel (window closed).</summary>
    public enum EditorResult { Cancelled, Saved, Deleted }

    public EditorResult Result { get; private set; } = EditorResult.Cancelled;
    public TeamSide SelectedTeam { get; private set; }
    public ScoreType SelectedType { get; private set; }
    public TimeSpan SelectedGameTime { get; private set; }

    private static readonly SolidColorBrush s_selectedBg = new(MediaColor.FromRgb(0x1C, 0x24, 0x33));
    private static readonly SolidColorBrush s_unselectedBg = new(MediaColor.FromRgb(0x0D, 0x11, 0x17));
    private static readonly SolidColorBrush s_dimBorder = new(MediaColor.FromRgb(0x30, 0x36, 0x3D));
    private static readonly SolidColorBrush s_homeBorder = new(MediaColor.FromRgb(0x58, 0xA6, 0xFF));
    private static readonly SolidColorBrush s_awayBorder = new(MediaColor.FromRgb(0xFF, 0x77, 0x55));
    private static readonly SolidColorBrush s_goalBorder = new(MediaColor.FromRgb(0x3F, 0xB9, 0x50));
    private static readonly SolidColorBrush s_behindBorder = new(MediaColor.FromRgb(0xD2, 0x99, 0x22));

    public ScoreEventEditorWindow(ScoreEvent ev, string homeName, string awayName)
    {
        InitializeComponent();

        string clock = $"Q{ev.Quarter}  {(int)ev.GameTime.TotalMinutes:D2}:{ev.GameTime.Seconds:D2}";
        EventInfoText.Text = clock;
        EventScoreText.Text = $"After: {ev.HomeGoals}.{ev.HomeBehinds}.{ev.HomeTotal} – {ev.AwayGoals}.{ev.AwayBehinds}.{ev.AwayTotal}";

        HomeTeamLabel.Text = homeName;
        AwayTeamLabel.Text = awayName;

        SelectedTeam = ev.Team;
        SelectedType = ev.Type;
        SelectedGameTime = ev.GameTime;

        MinutesBox.Text = ((int)ev.GameTime.TotalMinutes).ToString("D2");
        SecondsBox.Text = ev.GameTime.Seconds.ToString("D2");

        UpdateTeamVisuals();
        UpdateTypeVisuals();
    }

    private void HomeTeam_Click(object sender, MouseButtonEventArgs e)
    {
        SelectedTeam = TeamSide.Home;
        UpdateTeamVisuals();
    }

    private void AwayTeam_Click(object sender, MouseButtonEventArgs e)
    {
        SelectedTeam = TeamSide.Away;
        UpdateTeamVisuals();
    }

    private void Goal_Click(object sender, MouseButtonEventArgs e)
    {
        SelectedType = ScoreType.Goal;
        UpdateTypeVisuals();
    }

    private void Behind_Click(object sender, MouseButtonEventArgs e)
    {
        SelectedType = ScoreType.Behind;
        UpdateTypeVisuals();
    }

    private void UpdateTeamVisuals()
    {
        bool home = SelectedTeam == TeamSide.Home;

        HomeTeamBtn.Background = home ? s_selectedBg : s_unselectedBg;
        HomeTeamBtn.BorderBrush = home ? s_homeBorder : s_dimBorder;
        HomeTeamBtn.Opacity = home ? 1.0 : 0.5;

        AwayTeamBtn.Background = !home ? s_selectedBg : s_unselectedBg;
        AwayTeamBtn.BorderBrush = !home ? s_awayBorder : s_dimBorder;
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
        if (!int.TryParse(MinutesBox.Text, out int minutes) || minutes < 0)
        {
            System.Windows.MessageBox.Show("Please enter a valid number of minutes.", "Invalid Time", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(SecondsBox.Text, out int seconds) || seconds < 0 || seconds > 59)
        {
            System.Windows.MessageBox.Show("Seconds must be between 0 and 59.", "Invalid Time", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedGameTime = new TimeSpan(0, minutes, seconds);
        Result = EditorResult.Saved;
        Close();
    }

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {
        MessageBoxResult confirm = System.Windows.MessageBox.Show(
            "Are you sure you want to delete this score event?\n\nAll scores will be recalculated.",
            "Delete Score Event",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        Result = EditorResult.Deleted;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Result = EditorResult.Cancelled;
        Close();
    }
}

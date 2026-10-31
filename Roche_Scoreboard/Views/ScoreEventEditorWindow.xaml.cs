using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using Roche_Scoreboard.Services;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
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
    public int SelectedQuarter { get; private set; } = 1;

    private readonly FrameworkElement? _opener = Keyboard.FocusedElement as FrameworkElement;
    private bool _completed;
    private bool _confirmingDelete;

    private readonly bool _isAddMode;
    private readonly int _currentQuarter;
    private readonly TimeSpan _currentGameTime;

    public ScoreEventEditorWindow(ScoreEvent ev, string homeName, string awayName,
        string homeColor = "#58A6FF", string awayColor = "#FF7755")
    {
        InitializeComponent();
        InstallCloseHandling();

        _currentQuarter = ev.Quarter;
        _currentGameTime = ev.GameTime;
        ApplyTeamPalette(homeColor, awayColor);

        string clock = $"Q{ev.Quarter}  {(int)ev.GameTime.TotalMinutes:D2}:{ev.GameTime.Seconds:D2}";
        EventInfoText.Text = clock;
        EventScoreText.Text = $"After: {ev.HomeGoals}.{ev.HomeBehinds}.{ev.HomeTotal} – {ev.AwayGoals}.{ev.AwayBehinds}.{ev.AwayTotal}";

        HomeTeamLabel.Text = homeName;
        AwayTeamLabel.Text = awayName;
        HomeTeamBtn.ToolTip = homeName;
        AwayTeamBtn.ToolTip = awayName;
        System.Windows.Automation.AutomationProperties.SetName(HomeTeamBtn, $"Home team, {homeName}");
        System.Windows.Automation.AutomationProperties.SetName(AwayTeamBtn, $"Away team, {awayName}");

        SelectedTeam = ev.Team;
        SelectedType = ev.Type;
        SelectedQuarter = ev.Quarter;
        SelectedGameTime = ev.GameTime;

        QuarterBox.SelectedIndex = Math.Clamp(ev.Quarter - 1, 0, 3);
        UseCurrentQuarterBox.Visibility = Visibility.Collapsed;
        UseCurrentTimeBox.Visibility = Visibility.Collapsed;
        OptionalTiming_Changed(this, new RoutedEventArgs());
        MinutesBox.Text = ((int)ev.GameTime.TotalMinutes).ToString("D2");
        SecondsBox.Text = ev.GameTime.Seconds.ToString("D2");

        UpdateTeamVisuals();
        UpdateTypeVisuals();
    }

    public ScoreEventEditorWindow(string homeName, string awayName, int currentQuarter, TimeSpan currentGameTime,
        string homeColor = "#58A6FF", string awayColor = "#FF7755")
    {
        InitializeComponent();
        InstallCloseHandling();
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
        HomeTeamBtn.ToolTip = homeName;
        AwayTeamBtn.ToolTip = awayName;
        System.Windows.Automation.AutomationProperties.SetName(HomeTeamBtn, $"Home team, {homeName}");
        System.Windows.Automation.AutomationProperties.SetName(AwayTeamBtn, $"Away team, {awayName}");
        SelectedTeam = TeamSide.Home;
        SelectedType = ScoreType.Goal;
        SelectedQuarter = _currentQuarter;
        SelectedGameTime = _currentGameTime;
        QuarterBox.SelectedIndex = _currentQuarter - 1;
        MinutesBox.Text = ((int)_currentGameTime.TotalMinutes).ToString("D2");
        SecondsBox.Text = _currentGameTime.Seconds.ToString("D2");
        DeleteButton.Visibility = Visibility.Collapsed;
        SaveButton.Content = "ADD SCORE";
        OptionalTiming_Changed(this, new RoutedEventArgs());
        UpdateTeamVisuals();
        UpdateTypeVisuals();
    }

    private void ApplyTeamPalette(string homeHex, string awayHex)
    {
        MediaColor home = ParseColor(homeHex, MediaColor.FromRgb(0x58, 0xA6, 0xFF));
        MediaColor away = ParseColor(awayHex, MediaColor.FromRgb(0xFF, 0x77, 0x55));
        // Team ink belongs to the crest accent, never to text on an unrelated
        // translucent surface. Selected checks and names remain theme-aware.
        HomeTeamSymbol.Foreground = new SolidColorBrush(home);
        AwayTeamSymbol.Foreground = new SolidColorBrush(away);
    }

    private static MediaColor ParseColor(string value, MediaColor fallback)
    {
        try { return (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(value); }
        catch { return fallback; }
    }

    private void OptionalTiming_Changed(object sender, RoutedEventArgs e)
    {
        if (QuarterBox is null || MinutesBox is null || SecondsBox is null) return;
        QuarterBox.IsEnabled = !_isAddMode || UseCurrentQuarterBox?.IsChecked != true;
        bool customTime = !_isAddMode || UseCurrentTimeBox?.IsChecked != true;
        MinutesBox.IsEnabled = customTime;
        SecondsBox.IsEnabled = customTime;
    }

    private void HomeTeam_Click(object sender, RoutedEventArgs e)
    {
        if (HomeTeamBtn.IsChecked != true) return;
        SelectedTeam = TeamSide.Home;
        UpdateTeamVisuals();
    }

    private void AwayTeam_Click(object sender, RoutedEventArgs e)
    {
        if (AwayTeamBtn.IsChecked != true) return;
        SelectedTeam = TeamSide.Away;
        UpdateTeamVisuals();
    }

    private void Goal_Click(object sender, RoutedEventArgs e)
    {
        if (GoalBtn.IsChecked != true) return;
        SelectedType = ScoreType.Goal;
        UpdateTypeVisuals();
    }

    private void Behind_Click(object sender, RoutedEventArgs e)
    {
        if (BehindBtn.IsChecked != true) return;
        SelectedType = ScoreType.Behind;
        UpdateTypeVisuals();
    }

    private void UpdateTeamVisuals()
    {
        bool home = SelectedTeam == TeamSide.Home;

        HomeTeamBtn.IsChecked = home;
        AwayTeamBtn.IsChecked = !home;
    }

    private void UpdateTypeVisuals()
    {
        bool goal = SelectedType == ScoreType.Goal;

        GoalBtn.IsChecked = goal;
        BehindBtn.IsChecked = !goal;
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_completed || _confirmingDelete) return;
        int quarter = _isAddMode && UseCurrentQuarterBox.IsChecked == true
            ? _currentQuarter
            : Math.Clamp(QuarterBox.SelectedIndex + 1, 1, 4);
        TimeSpan time = _currentGameTime;
        if (!_isAddMode || UseCurrentTimeBox.IsChecked != true)
        {
            if (!int.TryParse(MinutesBox.Text, out int minutes) || minutes < 0)
            {
                ShowValidation(MinutesBox, "Enter minutes as a whole number of zero or more.");
                return;
            }
            if (!int.TryParse(SecondsBox.Text, out int seconds) || seconds < 0 || seconds > 59)
            {
                ShowValidation(SecondsBox, "Enter seconds from 0 to 59.");
                return;
            }
            time = new TimeSpan(0, minutes, seconds);
        }

        // Result fields are committed together only after the whole draft is
        // valid. The owner retains identity checks and applies the correction.
        ValidationText.Visibility = Visibility.Collapsed;
        SelectedQuarter = quarter;
        SelectedGameTime = time;
        Complete(EditorResult.Saved);
    }

    private void ShowValidation(TextBox field, string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
        System.Windows.Automation.AutomationProperties.SetHelpText(field, message);
        field.Focus();
        field.SelectAll();
        field.BringIntoView();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_completed && IsVisible) ValidationText.BringIntoView();
        }));
    }

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (_completed || _confirmingDelete || _isAddMode) return;
        _confirmingDelete = true;
        try
        {
            // The modal belongs to this editor and has one safe default. Its
            // result cannot apply the event; the existing caller does that.
            var confirmation = new System.Windows.Window
            {
                Owner = this,
                Title = "Delete score event",
                Width = 400,
                SizeToContent = SizeToContent.Height,
                MaxHeight = SystemParameters.WorkArea.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false
            };
            OperatorDesign.SetIsEnabled(confirmation, true);
            confirmation.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "ControlCentre.CanvasBrush");
            var body = new StackPanel { Margin = new Thickness(20) };
            var heading = new TextBlock { Text = "Delete this score event?", FontSize = 21, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            heading.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.TextBrush");
            body.Children.Add(heading);
            var detail = new TextBlock
            {
                Text = "All scores will be recalculated. Cancel keeps this event and returns to your changes.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 20)
            };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.SecondaryTextBrush");
            body.Children.Add(detail);
            var cancel = new Button { Content = "Cancel", IsDefault = true, IsCancel = true, MinHeight = 44, Margin = new Thickness(0, 0, 0, 8) };
            cancel.SetResourceReference(FrameworkElement.StyleProperty, "ControlCentre.Button");
            var delete = new Button
            {
                Content = "Delete score event",
                MinHeight = 44,
                Background = DeleteButton.Background,
                Foreground = DeleteButton.Foreground,
                BorderBrush = DeleteButton.BorderBrush
            };
            delete.SetResourceReference(FrameworkElement.StyleProperty, "ControlCentre.Button");
            System.Windows.Automation.AutomationProperties.SetName(cancel, "Cancel deletion");
            System.Windows.Automation.AutomationProperties.SetName(delete, "Delete score event");
            bool finished = false;
            void Finish(bool accepted)
            {
                if (finished) return;
                finished = true;
                confirmation.DialogResult = accepted;
            }
            cancel.Click += (_, _) => Finish(false);
            delete.Click += (_, _) => Finish(true);
            body.Children.Add(cancel);
            body.Children.Add(delete);
            var surface = new OperatorGlassSurface
            {
                Material = GlassMaterial.Regular,
                GlassSource = Content as FrameworkElement,
                CornerRadius = new CornerRadius(24),
                Child = body
            };
            surface.SetResourceReference(Border.BackgroundProperty, "ControlCentre.GlassBrush");
            confirmation.Content = new ScrollViewer
            {
                Content = surface,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            confirmation.Loaded += (_, _) => { cancel.Focus(); cancel.BringIntoView(); };
            if (confirmation.ShowDialog() == true && !_completed) Complete(EditorResult.Deleted);
        }
        finally
        {
            _confirmingDelete = false;
            if (!_completed && IsVisible) { DeleteButton.Focus(); DeleteButton.BringIntoView(); }
        }
    }

    private void Complete(EditorResult result)
    {
        if (_completed) return;
        _completed = true;
        Result = result;
        Close();
    }

    private void InstallCloseHandling()
    {
        // This template owns its lens, steady check and focus ring. Suppress
        // the fallback adorner so no second whole-card feedback is layered on.
        foreach (var choice in new[] { HomeTeamBtn, AwayTeamBtn, GoalBtn, BehindBtn })
            OperatorFeedback.SetManaged(choice, true);
        Closed += (_, _) =>
        {
            _completed = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Owner is not { IsVisible: true, IsEnabled: true, IsActive: true } owner
                    || _opener is not { IsLoaded: true, IsVisible: true, IsEnabled: true, Focusable: true } target
                    || Window.GetWindow(target) != owner) return;
                target.Focus();
            }), System.Windows.Threading.DispatcherPriority.Input);
        };
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Complete(EditorResult.Cancelled);
}

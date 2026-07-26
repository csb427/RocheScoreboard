using System;

namespace Roche_Scoreboard.Views;

public partial class GlobalSettingsScreen : global::System.Windows.Controls.UserControl
{
    public event Action<string>? CategoryRequested;
    public event Action? BackRequested;
    public event Action<string>? StartupModeChanged;

    public GlobalSettingsScreen()
    {
        InitializeComponent();
        StartupModeCombo.Items.Add("Resume in-progress session first");
        StartupModeCombo.Items.Add("Open last used mode");
        StartupModeCombo.Items.Add("Always open sport selection");
        StartupModeCombo.SelectedIndex = 0;
    }

    private void CategoryButton_Click(object sender, global::System.Windows.RoutedEventArgs e)
    {
        if (sender is global::System.Windows.Controls.Button { Tag: string category } && !string.IsNullOrWhiteSpace(category))
            CategoryRequested?.Invoke(category);
    }

    private void StartupModeCombo_SelectionChanged(object sender, global::System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (StartupModeCombo.SelectedIndex < 0)
            return;

        string mode = StartupModeCombo.SelectedIndex switch
        {
            0 => "resumeIfAvailable",
            1 => "lastMode",
            _ => "sportSelection"
        };

        StartupModeChanged?.Invoke(mode);
    }

    public void SetStartupModeSelection(string mode)
    {
        StartupModeCombo.SelectedIndex = mode switch
        {
            "sportSelection" => 2,
            "lastMode" => 1,
            _ => 0,
        };
    }

    private void BackButton_Click(object sender, global::System.Windows.RoutedEventArgs e)
    {
        BackRequested?.Invoke();
    }
}

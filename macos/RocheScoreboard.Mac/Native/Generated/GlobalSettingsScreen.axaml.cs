using RocheScoreboard.Mac.Production;
// Generated from Views/GlobalSettingsScreen.xaml.cs; edit translator/adapters.
// Source SHA-256: ddb8e650e9dd478b7a6f0fb6aa7cae7b7975d4b115a319ec27866c8b47243c77
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Input;
using RocheScoreboard.Mac.Native;
using System;
using Roche_Scoreboard.Services;

namespace Roche_Scoreboard.Views;

public partial class GlobalSettingsScreen : global::Avalonia.Controls.UserControl
{
    public event Action? BackRequested;
    public event Action<string>? StartupModeChanged;
    public event Action<AppTheme>? ThemeChanged;
    public event Action<UiDensity>? DensityChanged;
    public event Action<bool>? QuickStartupHintsChanged;
    public event Action<string>? GoalAnimationChanged;
    public event Action<int, int>? OutputDimensionsChanged;
    public event Action<bool>? BreakScreenEnabledChanged;
    public event Action<bool>? AutoOverlaysEnabledChanged;
    public event Action<bool>? ScreensaverEnabledChanged;
    private bool _initializing = true;

    public GlobalSettingsScreen()
    {
        NativePointer.EnsureInitialized(); InitializeComponent(); ProductionNames.CompleteLoad(this);
        StartupModeCombo.Items.Add("Resume in-progress session first");
        StartupModeCombo.Items.Add("Open last used mode");
        StartupModeCombo.Items.Add("Always open sport selection");
        StartupModeCombo.SelectedIndex = 0;
        ThemeCombo.Items.Add("Light controls");
        ThemeCombo.Items.Add("Dark controls");
        DensityCombo.Items.Add("Comfortable spacing");
        DensityCombo.Items.Add("Compact spacing");
        GoalAnimationCombo.Items.Add("TV Signal");
        GoalAnimationCombo.Items.Add("Voltage");
        GoalAnimationCombo.Items.Add("Club Cinema");
        GoalAnimationCombo.Items.Add("Clean Impact");
        GoalAnimationCombo.Items.Add("Flipboard");
        GoalAnimationCombo.Items.Add("Custom team video");
        ThemeCombo.SelectedIndex = 0;
        DensityCombo.SelectedIndex = 0;
        QuickHintsToggle.IsChecked = true;
        GoalAnimationCombo.SelectedIndex = 0;
        BreakScreenToggle.IsChecked = true;
        AutoOverlaysToggle.IsChecked = true;
        ScreensaverToggle.IsChecked = true;
        _initializing = false;
    }

    private void StartupModeCombo_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (_initializing || StartupModeCombo.SelectedIndex < 0)
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

    public void SetApplicationPreferences(AppTheme theme, UiDensity density, bool quickHints)
    {
        _initializing = true;
        ThemeCombo.SelectedIndex = theme == AppTheme.Dark ? 1 : 0;
        DensityCombo.SelectedIndex = density == UiDensity.Compact ? 1 : 0;
        QuickHintsToggle.IsChecked = quickHints;
        _initializing = false;
    }

    public void SetOutputPreferences(string goalAnimation, int width, int height,
        bool breakScreenEnabled, bool autoOverlaysEnabled, bool screensaverEnabled)
    {
        _initializing = true;
        GoalAnimationCombo.SelectedIndex = goalAnimation.Trim().ToLowerInvariant() switch
        {
            "electric" => 1,
            "cinematic" => 2,
            "clean" => 3,
            "classic" => 4,
            "customvideo" => 5,
            _ => 0
        };
        OutputWidthBox.Text = Math.Clamp(width, 200, 7680).ToString();
        OutputHeightBox.Text = Math.Clamp(height, 100, 4320).ToString();
        BreakScreenToggle.IsChecked = breakScreenEnabled;
        AutoOverlaysToggle.IsChecked = autoOverlaysEnabled;
        ScreensaverToggle.IsChecked = screensaverEnabled;
        _initializing = false;
    }

    public void SetOutputDimensions(int width, int height)
    {
        if (!OutputWidthBox.IsFocused)
            OutputWidthBox.Text = Math.Clamp(width, 200, 7680).ToString();
        if (!OutputHeightBox.IsFocused)
            OutputHeightBox.Text = Math.Clamp(height, 100, 4320).ToString();
    }

    private void ThemeCombo_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (_initializing || ThemeCombo.SelectedIndex < 0) return;
        ThemeChanged?.Invoke(ThemeCombo.SelectedIndex == 1 ? AppTheme.Dark : AppTheme.Light);
    }

    private void DensityCombo_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (_initializing || DensityCombo.SelectedIndex < 0) return;
        DensityChanged?.Invoke(DensityCombo.SelectedIndex == 1 ? UiDensity.Compact : UiDensity.Comfortable);
    }

    private void QuickHintsToggle_Changed(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_initializing) return;
        QuickStartupHintsChanged?.Invoke(QuickHintsToggle.IsChecked == true);
    }

    private void GoalAnimationCombo_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (_initializing || GoalAnimationCombo.SelectedIndex < 0) return;
        string style = GoalAnimationCombo.SelectedIndex switch
        {
            1 => "electric",
            2 => "cinematic",
            3 => "clean",
            4 => "classic",
            5 => "customVideo",
            _ => "broadcast"
        };
        GoalAnimationChanged?.Invoke(style);
    }

    private void ApplyOutputDimensions_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!int.TryParse((OutputWidthBox.Text ?? string.Empty), out int width)
            || !int.TryParse((OutputHeightBox.Text ?? string.Empty), out int height)) return;
        width = Math.Clamp(width, 200, 7680);
        height = Math.Clamp(height, 100, 4320);
        OutputWidthBox.Text = width.ToString();
        OutputHeightBox.Text = height.ToString();
        OutputDimensionsChanged?.Invoke(width, height);
    }

    private void ResetOutputDimensions_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        OutputWidthBox.Text = "386";
        OutputHeightBox.Text = "193";
        OutputDimensionsChanged?.Invoke(386, 193);
    }

    private void BreakScreenToggle_Changed(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_initializing) BreakScreenEnabledChanged?.Invoke(BreakScreenToggle.IsChecked == true);
    }

    private void AutoOverlaysToggle_Changed(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_initializing) AutoOverlaysEnabledChanged?.Invoke(AutoOverlaysToggle.IsChecked == true);
    }

    private void ScreensaverToggle_Changed(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_initializing) ScreensaverEnabledChanged?.Invoke(ScreensaverToggle.IsChecked == true);
    }

    private void BackButton_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        BackRequested?.Invoke();
    }
}

public partial class GlobalSettingsScreen
{
    private void Native_AutoOverlaysToggle_Changed(object? sender, RoutedEventArgs e) { AutoOverlaysToggle_Changed(sender!, e); }
    private void Native_BreakScreenToggle_Changed(object? sender, RoutedEventArgs e) { BreakScreenToggle_Changed(sender!, e); }
    private void Native_DensityCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e) { DensityCombo_SelectionChanged(sender!, e); }
    private void Native_GoalAnimationCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e) { GoalAnimationCombo_SelectionChanged(sender!, e); }
    private void Native_QuickHintsToggle_Changed(object? sender, RoutedEventArgs e) { QuickHintsToggle_Changed(sender!, e); }
    private void Native_ScreensaverToggle_Changed(object? sender, RoutedEventArgs e) { ScreensaverToggle_Changed(sender!, e); }
    private void Native_StartupModeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e) { StartupModeCombo_SelectionChanged(sender!, e); }
    private void Native_ThemeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e) { ThemeCombo_SelectionChanged(sender!, e); }
}

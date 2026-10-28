using RocheScoreboard.Mac.Production;
// Generated from Views/GlobalSettingsScreen.xaml.cs; edit translator/adapters.
// Source SHA-256: 9faa915195ced9f0a0eedec6bac8859cdeb7b6212f11bf9872f71069b2108327
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
    public event Action<bool>? ScreensaverClockChanged;
    public event Action? ScreensaverMediaRequested;
    public event Action? BuiltInScreensaverRequested;
    public void SetScreensaverPreferences(string? path,bool showClock,bool? enabled=null)
    {
        bool old=_initializing; _initializing=true;
        GlobalScreensaverStatus.Text=string.IsNullOrWhiteSpace(path) ? "Built-in animated sports scene" : System.IO.File.Exists(path) ? System.IO.Path.GetFileName(path) : "Custom media unavailable · built-in scene will be used";
        UpdateScreensaverSourceSelection(!string.IsNullOrWhiteSpace(path),System.IO.File.Exists(path));
        ScreensaverClockToggle.IsChecked=showClock; if(enabled is bool value) ScreensaverToggle.IsChecked=value; _initializing=old;
    }
    private void ScreensaverSource_Click(object? sender,global::Avalonia.Interactivity.RoutedEventArgs args)
    { if(sender is global::Avalonia.Controls.Button {Tag:"builtin"}) {UpdateScreensaverSourceSelection(false); BuiltInScreensaverRequested?.Invoke();} else ScreensaverMediaRequested?.Invoke(); }
    private void UpdateScreensaverSourceSelection(bool custom,bool available=true)
    {
        foreach(var (button,selected) in new[]{(BuiltinScreensaverButton,!custom),(CustomScreensaverButton,custom)})
        {
            button.SetResourceReference(global::Avalonia.Controls.Primitives.TemplatedControl.BorderBrushProperty, selected ? "ControlCentre.AccentBrush" : "ControlCentre.BorderBrush");
            button.SetResourceReference(global::Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, selected ? "ControlCentre.SelectionBrush" : "ControlCentre.SatinBrush");
            button.BorderThickness=new global::Avalonia.Thickness(1);
            global::Avalonia.Automation.AutomationProperties.SetItemStatus(button,selected?"Selected":"Not selected");
        }
        BuiltinScreensaverSelected.Text=custom?(available?"USE THIS SCENE →":"FALLBACK SCENE"):"✓ ACTIVE";
        CustomScreensaverSelected.Text=custom?(available?"✓ ACTIVE · CHANGE FILE":"FILE MISSING · REPLACE"):"SELECT FILE →";
    }
    private void ScreensaverClock_Changed(object? sender,global::Avalonia.Interactivity.RoutedEventArgs args)
    { if(!_initializing) ScreensaverClockChanged?.Invoke(ScreensaverClockToggle.IsChecked==true); }
    private bool _initializing = true, _syncingTiles;
    private void PreferenceTile_Checked(object? sender,global::Avalonia.Interactivity.RoutedEventArgs args)
    {
        if(_initializing || _syncingTiles || sender is not global::Avalonia.Controls.RadioButton { Tag: string tag }) return;
        var parts=tag.Split(':'); if(parts.Length!=2 || !int.TryParse(parts[1],out int index)) return;
        var choice=parts[0] switch { "theme"=>ThemeCombo, "density"=>DensityCombo, _=>StartupModeCombo };
        choice.SelectedIndex=index;
    }
    private void SyncPreferenceTiles()
    {
        if(LightThemeTile is null || SelectionStartupTile is null) return;
        _syncingTiles=true;
        try {
            LightThemeTile.IsChecked=ThemeCombo.SelectedIndex==0; DarkThemeTile.IsChecked=ThemeCombo.SelectedIndex==1;
            ComfortableTile.IsChecked=DensityCombo.SelectedIndex==0; CompactTile.IsChecked=DensityCombo.SelectedIndex==1;
            ResumeStartupTile.IsChecked=StartupModeCombo.SelectedIndex==0; LastModeStartupTile.IsChecked=StartupModeCombo.SelectedIndex==1;
            SelectionStartupTile.IsChecked=StartupModeCombo.SelectedIndex==2;
        } finally { _syncingTiles=false; }
    }
    public void SetBackCaption(string caption) => BackButton.Content = caption;

    private void Settings_SizeChanged(object? sender, global::Avalonia.Controls.SizeChangedEventArgs e)
    {
        double contentWidth = SettingsWorkspace?.Scroller.Viewport.Width ?? 0;
        bool narrow = (contentWidth > 0 ? contentWidth : e.NewSize.Width) < 660;
        ReflowFields(AppearanceFieldsGrid, ThemeChoicePanel, narrow);
        ReflowFields(DensityFieldPanel, DensityChoicePanel, narrow);
        ReflowFields(OutputFieldsGrid, OutputDimensionsFieldPanel, narrow);
        ReflowFields(GoalAnimationRow, GoalAnimationCombo, narrow);
    }

    private static void ReflowFields(global::Avalonia.Controls.Grid grid, global::Avalonia.Controls.Control choice, bool narrow)
    {
        if (grid is null || choice is null) return;
        grid.ColumnDefinitions[1].Width = narrow ? new global::Avalonia.Controls.GridLength(0) : global::Avalonia.Controls.GridLength.Auto;
        global::Avalonia.Controls.Grid.SetColumn(choice, narrow ? 0 : 1);
        global::Avalonia.Controls.Grid.SetRow(choice, narrow ? 1 : 0);
        choice.Width = narrow ? double.NaN : 270;
        choice.Margin = narrow ? new global::Avalonia.Thickness(0,12,0,0) : new global::Avalonia.Thickness(16,0,0,0);
    }

    public GlobalSettingsScreen()
    {
        NativePointer.EnsureInitialized(); ProductionNames.BeginLoad(this); try { InitializeComponent(); } finally { ProductionNames.CompleteLoad(this); }
        SettingsWorkspace.Scroller.SizeChanged += Settings_SizeChanged;
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
        QuickHintsToggle.IsChecked = false;
        GoalAnimationCombo.SelectedIndex = 0;
        BreakScreenToggle.IsChecked = true;
        AutoOverlaysToggle.IsChecked = true;
        ScreensaverToggle.IsChecked = true;
        _initializing = false;
        SyncPreferenceTiles();
        UpdateScreensaverSourceSelection(false);
        SyncInteractionPreferences();
        Loaded += (_,_) => { OperatorAppearance.Changed-=SyncInteractionPreferences;OperatorAppearance.Changed+=SyncInteractionPreferences;SyncInteractionPreferences(); };
        Unloaded += (_,_) => OperatorAppearance.Changed-=SyncInteractionPreferences;
    }
    private void SyncInteractionPreferences()
    {
        bool previous=_initializing;_initializing=true;
        GlassMaterialToggle.IsChecked=OperatorAppearance.GlassEnabled;ReducedMotionToggle.IsChecked=OperatorAppearance.ReduceMotion;
        _initializing=previous;
    }
    private void InteractionPreference_Changed(object? sender,global::Avalonia.Interactivity.RoutedEventArgs args)
    {
        if(!_initializing)OperatorAppearance.Configure(GlassMaterialToggle.IsChecked==true,ReducedMotionToggle.IsChecked==true);
    }

    private void StartupModeCombo_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        SyncPreferenceTiles();
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
        SyncPreferenceTiles();
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
        OutputWidthBox.Text = Math.Clamp(width, 50, 7680).ToString();
        OutputHeightBox.Text = Math.Clamp(height, 50, 4320).ToString();
        BreakScreenToggle.IsChecked = breakScreenEnabled;
        AutoOverlaysToggle.IsChecked = autoOverlaysEnabled;
        ScreensaverToggle.IsChecked = screensaverEnabled;
        _initializing = false;
        SyncPreferenceTiles();
    }

    public void SetOutputDimensions(int width, int height)
    {
        if (!OutputWidthBox.IsFocused)
            OutputWidthBox.Text = Math.Clamp(width, 50, 7680).ToString();
        if (!OutputHeightBox.IsFocused)
            OutputHeightBox.Text = Math.Clamp(height, 50, 4320).ToString();
    }

    private void ThemeCombo_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        SyncPreferenceTiles();
        if (_initializing || ThemeCombo.SelectedIndex < 0) return;
        ThemeChanged?.Invoke(ThemeCombo.SelectedIndex == 1 ? AppTheme.Dark : AppTheme.Light);
    }

    private void DensityCombo_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        SyncPreferenceTiles();
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
        width = Math.Clamp(width, 50, 7680);
        height = Math.Clamp(height, 50, 4320);
        OutputWidthBox.Text = width.ToString();
        OutputHeightBox.Text = height.ToString();
        OutputDimensionsChanged?.Invoke(width, height);
    }

    private void DoubleOutputDimensions_Click(object? sender,global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        OutputWidthBox.Text="772"; OutputHeightBox.Text="386";
        OutputDimensionsChanged?.Invoke(772,386);
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
    private void Native_InteractionPreference_Changed(object? sender, RoutedEventArgs e) { InteractionPreference_Changed(sender!, e); }
    private void Native_PreferenceTile_Checked_Checked(object? sender, RoutedEventArgs e) { if (sender is Avalonia.Controls.Primitives.ToggleButton toggle && toggle.IsChecked != true) return; PreferenceTile_Checked(sender!, e); }
    private void Native_QuickHintsToggle_Changed(object? sender, RoutedEventArgs e) { QuickHintsToggle_Changed(sender!, e); }
    private void Native_ScreensaverClock_Changed(object? sender, RoutedEventArgs e) { ScreensaverClock_Changed(sender!, e); }
    private void Native_ScreensaverToggle_Changed(object? sender, RoutedEventArgs e) { ScreensaverToggle_Changed(sender!, e); }
    private void Native_StartupModeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e) { StartupModeCombo_SelectionChanged(sender!, e); }
    private void Native_ThemeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e) { ThemeCombo_SelectionChanged(sender!, e); }
}

using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Roche_Scoreboard.Services;
using WpfApplication = System.Windows.Application;
using WpfButton = System.Windows.Controls.Button;
using WpfControl = System.Windows.Controls.Control;
using WpfToolTip = System.Windows.Controls.ToolTip;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Roche_Scoreboard.Views;

/// <summary>
/// Design System Gallery window for Score Grid component demonstration.
/// Accessible via --design-gallery command-line argument only.
/// </summary>
public partial class DesignSystemGalleryWindow : Window
{
    internal sealed record GallerySelfTestResult(
        string Theme,
        string Density,
        string FocusBrush,
        string ControlBorderBrush,
        string WindowBackground,
        string TextPrimary,
        double ButtonHeight,
        double TextBoxHeight,
        double ComboBoxHeight,
        string ControlPadding,
        string ThemeAndVisuals,
        string DensityAndLayout,
        string Keyboard,
        string Tooltip,
        string DisabledStates,
        string CloseBehavior,
        string Notes);

    public DesignSystemGalleryWindow()
    {
        InitializeComponent();
        Loaded += DesignSystemGalleryWindow_Loaded;
    }

    private void DesignSystemGalleryWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshDiagnostics();
    }

    private void BtnToggleTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (WpfApplication.Current is not App app)
            return;

        AppTheme newTheme = app.ThemeService.CurrentTheme == AppTheme.Light
            ? AppTheme.Dark
            : AppTheme.Light;

        app.ThemeService.SetTheme(newTheme);

        // Persist the change
        app.UxPreferences.Theme = newTheme;
        UxPreferences.Save(app.UxPreferences);
        RefreshDiagnostics();
    }

    private void BtnToggleDensity_Click(object? sender, RoutedEventArgs e)
    {
        if (WpfApplication.Current is not App app)
            return;

        UiDensity newDensity = app.ThemeService.CurrentDensity == UiDensity.Comfortable
            ? UiDensity.Compact
            : UiDensity.Comfortable;

        app.ThemeService.SetDensity(newDensity);

        // Persist the change
        app.UxPreferences.Density = newDensity;
        UxPreferences.Save(app.UxPreferences);
        RefreshDiagnostics();
    }

    internal GallerySelfTestResult RunSelfTest()
    {
        UpdateLayout();

        StringBuilder notes = new();
        List<WpfControl> tabControls =
        [
            BtnToggleTheme,
            BtnToggleDensity,
            PrimaryButtonSample,
            SecondaryButtonSample,
            DangerButtonSample,
            TextBoxSample,
            CheckBoxSample,
            RadioOptionA,
            RadioOptionB,
            RadioOptionC,
            ComboBoxSample,
            TooltipSampleButton,
        ];

        string initialTheme = ThemeStateText.Text;
        string initialDensity = DensityStateText.Text;
        string focusBrush = GetBrushHex("ScoreGrid.Brush.Focus");
        string controlBorder = GetBrushHex("ScoreGrid.Brush.ControlBorder");
        string windowBackground = GetBrushHex("ScoreGrid.Brush.Background");
        string textPrimary = GetBrushHex("ScoreGrid.Brush.TextPrimary");
        string initialPadding = FindResource("ScoreGrid.Padding.Control")?.ToString() ?? "unavailable";

        double comfortableButtonHeight = PrimaryButtonSample.ActualHeight;
        double comfortableTextBoxHeight = TextBoxSample.ActualHeight;
        double comfortableComboBoxHeight = ComboBoxSample.ActualHeight;
        double comfortableMargin = GetNamedFrameworkElement("GalleryContentPanel")?.Margin.Left ?? 0;

        bool themeRoundTrip = TryToggleTheme() && TryToggleTheme();
        string finalTheme = ThemeStateText.Text;

        bool densityToCompact = TryToggleDensity();
        string compactDensity = DensityStateText.Text;
        string compactPadding = FindResource("ScoreGrid.Padding.Control")?.ToString() ?? "unavailable";
        double compactButtonHeight = PrimaryButtonSample.ActualHeight;
        double compactTextBoxHeight = TextBoxSample.ActualHeight;
        double compactComboBoxHeight = ComboBoxSample.ActualHeight;
        double compactMargin = GetNamedFrameworkElement("GalleryContentPanel")?.Margin.Left ?? 0;
        bool densityBackToComfortable = TryToggleDensity();
        string densityAfterRoundTrip = DensityStateText.Text;
        string finalPadding = FindResource("ScoreGrid.Padding.Control")?.ToString() ?? "unavailable";
        double finalButtonHeight = PrimaryButtonSample.ActualHeight;
        double finalTextBoxHeight = TextBoxSample.ActualHeight;
        double finalComboBoxHeight = ComboBoxSample.ActualHeight;
        double finalMargin = GetNamedFrameworkElement("GalleryContentPanel")?.Margin.Left ?? 0;

        bool tabIndicesOrdered = ObserveTabIndexOrder(tabControls, notes);
        bool syntheticRadioSelected = ObserveSyntheticRadioSelection(notes);
        bool programmaticComboSelected = ObserveProgrammaticComboSelection(notes);
        bool tooltipOpened = VerifyTooltip(notes);
        bool disabledStatesOk = VerifyDisabledStates(notes);

        const string closeBehavior = "unverified";

        string themeAndVisuals = themeRoundTrip && finalTheme == initialTheme
            ? "pass"
            : "fail";

        bool densityChanged = comfortableButtonHeight != compactButtonHeight
            || comfortableTextBoxHeight != compactTextBoxHeight
            || comfortableComboBoxHeight != compactComboBoxHeight
            || initialPadding != compactPadding
            || comfortableMargin != compactMargin;
        bool densityRoundTrip = densityToCompact && densityBackToComfortable;
        string densityAndLayout = densityRoundTrip && densityAfterRoundTrip == initialDensity && densityChanged
            ? "pass"
            : "fail";

        notes.Append($"Compact metrics: density={compactDensity}, button={compactButtonHeight:N0}, textbox={compactTextBoxHeight:N0}, combo={compactComboBoxHeight:N0}, padding={compactPadding}, margin={compactMargin:N0}. ");

        const string keyboard = "unverified";
        notes.Append($"Keyboard observations only: sampled TabIndex values ordered={tabIndicesOrdered}, synthetic Right event left RadioOptionB selected={syntheticRadioSelected}, programmatic ComboBox open/select/close={programmaticComboSelected}. ");
        notes.Append("Keyboard unverified: no actual Tab/Shift+Tab traversal, focus trace, rendered focus cue or ComboBox key input was measured. ");
        notes.Append("Close behavior unverified: this report does not attempt Close or observe Closing/Closed; caller cleanup happens after reporting. ");
        notes.Append("Theme/visual checks cover theme/resource/text roundtrip, not rendered contrast. Density/layout checks cover sampled metrics/token changes, not supported work-area layouts. Tooltip checks programmatic IsOpen only; disabled-state checks sample three IsEnabled flags, not activation rejection. ");

        string tooltip = tooltipOpened
            ? "pass"
            : "fail";

        string disabledStates = disabledStatesOk
            ? "pass"
            : "fail";

        if (GetNamedTextBlock("SelfTestStatusText") is { } selfTestStatusText)
            selfTestStatusText.Text = $"Self-test: theme={themeAndVisuals}, density={densityAndLayout}, keyboard={keyboard}, tooltip={tooltip}, disabled={disabledStates}, close={closeBehavior}";

        return new GallerySelfTestResult(
            Theme: finalTheme,
            Density: densityAfterRoundTrip,
            FocusBrush: focusBrush,
            ControlBorderBrush: controlBorder,
            WindowBackground: windowBackground,
            TextPrimary: textPrimary,
            ButtonHeight: finalButtonHeight,
            TextBoxHeight: finalTextBoxHeight,
            ComboBoxHeight: finalComboBoxHeight,
            ControlPadding: finalPadding,
            ThemeAndVisuals: themeAndVisuals,
            DensityAndLayout: densityAndLayout,
            Keyboard: keyboard,
            Tooltip: tooltip,
            DisabledStates: disabledStates,
            CloseBehavior: closeBehavior,
            Notes: notes.ToString());
    }

    private bool TryToggleTheme()
    {
        if (WpfApplication.Current is not App app)
            return false;

        AppTheme before = app.ThemeService.CurrentTheme;
        BtnToggleTheme_Click(BtnToggleTheme, new RoutedEventArgs(WpfButton.ClickEvent, BtnToggleTheme));
        UpdateLayout();
        return app.ThemeService.CurrentTheme != before;
    }

    private bool TryToggleDensity()
    {
        if (WpfApplication.Current is not App app)
            return false;

        double before = PrimaryButtonSample.ActualHeight;
        UiDensity previousDensity = app.ThemeService.CurrentDensity;
        BtnToggleDensity_Click(BtnToggleDensity, new RoutedEventArgs(WpfButton.ClickEvent, BtnToggleDensity));
        UpdateLayout();
        return app.ThemeService.CurrentDensity != previousDensity || PrimaryButtonSample.ActualHeight != before;
    }

    private static bool ObserveTabIndexOrder(IReadOnlyList<WpfControl> controls, StringBuilder notes)
    {
        int previousTabIndex = -1;
        foreach (WpfControl control in controls)
        {
            if (control.TabIndex < previousTabIndex)
            {
                notes.Append($"Sampled TabIndex values decrease at {control.Name}. ");
                return false;
            }

            previousTabIndex = control.TabIndex;
        }

        return true;
    }

    private bool ObserveSyntheticRadioSelection(StringBuilder notes)
    {
        RadioOptionA.Focus();
        RadioOptionA.RaiseEvent(new WpfKeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(this)!, 0, System.Windows.Input.Key.Right)
        {
            RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent
        });
        UpdateLayout();

        if (RadioOptionB.IsChecked != true)
        {
            notes.Append("Synthetic Right-event observation did not leave RadioOptionB selected. ");
            return false;
        }

        return true;
    }

    private bool ObserveProgrammaticComboSelection(StringBuilder notes)
    {
        ComboBoxSample.Focus();
        ComboBoxSample.IsDropDownOpen = true;
        UpdateLayout();

        if (!ComboBoxSample.IsDropDownOpen)
        {
            notes.Append("ComboBox did not retain programmatic IsDropDownOpen=true. ");
            return false;
        }

        ComboBoxSample.SelectedIndex = 1;
        ComboBoxSample.IsDropDownOpen = false;
        UpdateLayout();

        if (ComboBoxSample.SelectedIndex != 1 || ComboBoxSample.IsDropDownOpen)
        {
            notes.Append("ComboBox did not retain the programmatic selection/close state. ");
            return false;
        }

        return true;
    }

    private bool VerifyTooltip(StringBuilder notes)
    {
        TooltipSampleButton.Focus();
        if (TooltipSampleButton.ToolTip is not WpfToolTip toolTip)
        {
            notes.Append("Tooltip sample is missing. ");
            return false;
        }

        toolTip.IsOpen = true;
        UpdateLayout();
        bool open = toolTip.IsOpen;
        toolTip.IsOpen = false;
        UpdateLayout();

        if (!open)
            notes.Append("Tooltip did not open during verification. ");

        return open;
    }

    private bool VerifyDisabledStates(StringBuilder notes)
    {
        FrameworkElement? disabledTextBox = GetNamedFrameworkElement("DisabledTextBoxSample");
        FrameworkElement? disabledCheckBox = GetNamedFrameworkElement("DisabledCheckBoxSample");
        FrameworkElement? disabledComboBox = GetNamedFrameworkElement("DisabledComboBoxSample");

        bool valid = disabledTextBox?.IsEnabled == false
            && disabledCheckBox?.IsEnabled == false
            && disabledComboBox?.IsEnabled == false;

        if (!valid)
            notes.Append("One or more disabled-state samples became enabled unexpectedly. ");

        return valid;
    }

    private void RefreshDiagnostics()
    {
        if (WpfApplication.Current is not App app)
            return;

        ThemeStateText.Text = $"Theme: {app.ThemeService.CurrentTheme}";
        DensityStateText.Text = $"Density: {app.ThemeService.CurrentDensity}";
        FocusBrushText.Text = $"Focus Brush: {GetBrushHex("ScoreGrid.Brush.Focus")}";
        ControlBorderBrushText.Text = $"Control Border Brush: {GetBrushHex("ScoreGrid.Brush.ControlBorder")}";
        WindowBackgroundText.Text = $"Window Background: {GetBrushHex("ScoreGrid.Brush.Background")}";
        TextPrimaryText.Text = $"Primary Text Brush: {GetBrushHex("ScoreGrid.Brush.TextPrimary")}";
        ControlMetricsText.Text = $"Metrics — Button Height: {PrimaryButtonSample.ActualHeight:N0}, TextBox Height: {TextBoxSample.ActualHeight:N0}, ComboBox Height: {ComboBoxSample.ActualHeight:N0}, Padding Token: {FindResource("ScoreGrid.Padding.Control")}";
    }

    private FrameworkElement? GetNamedFrameworkElement(string name)
        => FindName(name) as FrameworkElement;

    private System.Windows.Controls.TextBlock? GetNamedTextBlock(string name)
        => FindName(name) as System.Windows.Controls.TextBlock;

    private string GetBrushHex(string resourceKey)
    {
        if (TryFindResource(resourceKey) is SolidColorBrush brush)
            return brush.Color.ToString();

        return "unavailable";
    }
}

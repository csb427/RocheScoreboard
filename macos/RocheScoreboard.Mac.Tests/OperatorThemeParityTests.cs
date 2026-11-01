using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

/// <summary>Checks the real theme adapter against the authored operator palette, not contrast alone.</summary>
public sealed class OperatorThemeParityTests
{
    private static async Task Settle()
    {
        await Task.Delay(120);
        Dispatcher.UIThread.RunJobs();
    }

    // Values are the existing Windows OperatorDesign.SetTheme contract.
    private static readonly (string Key, string Dark, string Light)[] Palette =
    [
        ("CanvasBrush", "#09090B", "#EFEFF3"),
        ("SurfaceBrush", "#19191C", "#FAFAFC"),
        ("RaisedBrush", "#29292D", "#E1E1E8"),
        ("TextBrush", "#FAFAFC", "#19191E"),
        ("SecondaryTextBrush", "#B6B6C0", "#545461"),
        ("BorderBrush", "#44444B", "#858590"),
        ("SelectionBrush", "#253D55", "#D0E5FF"),
        ("AccentBrush", "#7BB8FF", "#145699")
    ];

    private static Color Expected(AppTheme theme, string key)
    {
        var entry = Palette.Single(item => item.Key == key);
        return Color.Parse(theme == AppTheme.Dark ? entry.Dark : entry.Light);
    }

    private static Color BrushColor(object? value) => Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;

    private static void AssertPalette(Control root, AppTheme theme)
    {
        foreach (var entry in Palette)
        {
            var actual = BrushColor(root.Resources["ControlCentre." + entry.Key]);
            Assert.True(actual == Expected(theme, entry.Key),
                $"{root.GetType().Name} {entry.Key}: expected {Expected(theme, entry.Key)} for {theme}, got {actual}.");
        }
    }

    private static void AssertDensity(Control root, UiDensity density)
    {
        bool compact = density == UiDensity.Compact;
        Assert.Equal(compact ? 14d : 15d, Assert.IsType<double>(root.Resources["ControlCentre.BodySize"]));
        Assert.Equal(compact ? 12d : 13d, Assert.IsType<double>(root.Resources["ControlCentre.CaptionSize"]));
        Assert.Equal(compact ? 36d : 42d, Assert.IsType<double>(root.Resources["ControlCentre.ControlHeight"]));
        Assert.Equal(compact ? new Thickness(12, 7, 12, 7) : new Thickness(16, 10, 16, 10),
            Assert.IsType<Thickness>(root.Resources["ControlCentre.ControlPadding"]));
    }

    private sealed class OperatorHost : IDisposable
    {
        internal readonly TextBlock Text = new() { Text = "Existing operator content" };
        internal readonly Border Surface = new();
        internal readonly UserControl Panel = new();
        internal readonly NativeWindow Window;

        internal OperatorHost(Action<OperatorHost>? beforeShow = null)
        {
            Surface.Child = Text;
            Surface.SetResourceReference(Border.BackgroundProperty, "ControlCentre.SurfaceBrush");
            Text.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.TextBrush");
            Text.SetResourceReference(TextBlock.FontSizeProperty, "ControlCentre.BodySize");
            Panel.Content = new Grid { Children = { Surface } };
            Window = new NativeWindow { Width = 680, Height = 520, Content = Panel };
            OperatorDesign.Apply(Window);
            OperatorDesign.Apply(Panel);
            beforeShow?.Invoke(this);
            Window.Show();
        }

        public void Dispose() => Window.Close();
    }

    private static readonly string[] DensityKeys = ["BodySize", "CaptionSize", "ControlHeight", "ControlPadding"];

    private static Dictionary<string, object?> ResourceIdentities(Control root) =>
        Palette.Select(item => item.Key).Concat(["GlassBrush"]).Concat(DensityKeys)
            .ToDictionary(key => "ControlCentre." + key, key => root.Resources["ControlCentre." + key]);

    private static void AssertSameResources(Control root, Dictionary<string, object?> before)
    {
        foreach (var (key, value) in before)
            Assert.True(ReferenceEquals(value, root.Resources[key]),
                root.GetType().Name + " must retain the actual resource object for " + key + " on an identical request.");
    }

    private static (TextBox Draft, CheckBox Choice) AddDraftControls(OperatorHost host)
    {
        var stage = Assert.IsType<Grid>(host.Panel.Content);
        var draft = new TextBox { Text = "Unsubmitted operator draft", Width = 240,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        var choice = new CheckBox { Content = "Keep this choice", IsChecked = true,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        stage.Children.Add(draft);
        stage.Children.Add(choice);
        return (draft, choice);
    }

    private static void AssertDraftState(TextBox draft, CheckBox choice)
    {
        Assert.True(draft.IsFocused);
        Assert.Equal("Unsubmitted operator draft", draft.Text);
        Assert.Equal(2, draft.SelectionStart);
        Assert.Equal(12, draft.SelectionEnd);
        Assert.True(choice.IsChecked);
    }

    [AvaloniaTheory]
    [InlineData(AppTheme.Dark, UiDensity.Comfortable)]
    [InlineData(AppTheme.Light, UiDensity.Compact)]
    public async Task IdenticalFreshInitializationPreservesExistingResourceObjectsAndDoesNotStartDensityMotion(AppTheme theme, UiDensity density)
    {
        using var state = new ThemeState();
        bool? previousAnimation = OperatorMotion.AnimationOverride;
        try
        {
            state.App.ThemeService.SetTheme(theme);
            state.App.ThemeService.SetDensity(density);
            using var host = new OperatorHost();
            var (draft, choice) = AddDraftControls(host);
            await Settle();
            Assert.True(draft.Focus(NavigationMethod.Tab));
            draft.SelectionStart = 2; draft.SelectionEnd = 12;
            OperatorMotion.AnimationOverride = true; // An unwanted transition cannot hide behind reduced motion.
            var content = Assert.IsAssignableFrom<Control>(host.Window.Content);
            Assert.False(OperatorDensityTransition.IsAnimating(content));
            AssertPalette(host.Window, theme); AssertPalette(host.Panel, theme);
            AssertDensity(host.Window, density); AssertDensity(host.Panel, density);
            var windowResources = ResourceIdentities(host.Window);
            var panelResources = ResourceIdentities(host.Panel);
            AssertSameResources(host.Window, windowResources);
            AssertSameResources(host.Panel, panelResources);
            var dictionaries = state.App.Resources.MergedDictionaries.ToArray();
            try
            {
                // A new native adapter models another isolated application
                // initialization with the already accepted global targets.
                var service = new ThemeService();
                service.Initialize(theme, density);
                Assert.Equal(theme, service.CurrentTheme);
                Assert.Equal(density, service.CurrentDensity);
                AssertSameResources(host.Window, windowResources);
                AssertSameResources(host.Panel, panelResources);
                Assert.False(OperatorDensityTransition.IsAnimating(content));
                AssertDraftState(draft, choice);
                await Settle();
                AssertSameResources(host.Window, windowResources);
                AssertSameResources(host.Panel, panelResources);
                Assert.False(OperatorDensityTransition.IsAnimating(content));
                AssertDraftState(draft, choice);
            }
            finally
            {
                state.App.Resources.MergedDictionaries.Clear();
                foreach (var dictionary in dictionaries) state.App.Resources.MergedDictionaries.Add(dictionary);
            }
        }
        finally { OperatorMotion.AnimationOverride = previousAnimation; }
    }

    [AvaloniaTheory]
    [InlineData(AppTheme.Dark, UiDensity.Comfortable, AppTheme.Light, UiDensity.Compact)]
    [InlineData(AppTheme.Light, UiDensity.Compact, AppTheme.Dark, UiDensity.Comfortable)]
    public async Task ChangedRequestsStillUpdateRealResourcesAndKeepFocusedDraftAndChoice(
        AppTheme firstTheme, UiDensity firstDensity, AppTheme nextTheme, UiDensity nextDensity)
    {
        using var state = new ThemeState();
        bool? previousAnimation = OperatorMotion.AnimationOverride;
        try
        {
            state.App.ThemeService.SetTheme(firstTheme);
            state.App.ThemeService.SetDensity(firstDensity);
            using var host = new OperatorHost();
            // Give the real text a neutral transform so a true density change
            // has an eligible live optical reflow, rather than a mocked clock.
            host.Text.RenderTransform = new MatrixTransform(Matrix.Identity);
            var (draft, choice) = AddDraftControls(host);
            await Settle();
            Assert.True(draft.Focus(NavigationMethod.Tab));
            draft.SelectionStart = 2; draft.SelectionEnd = 12;
            var original = ResourceIdentities(host.Panel);
            var content = Assert.IsAssignableFrom<Control>(host.Window.Content);
            OperatorMotion.AnimationOverride = true;
            state.App.ThemeService.SetTheme(nextTheme);
            state.App.ThemeService.SetDensity(nextDensity);
            Assert.True(OperatorDensityTransition.IsAnimating(content), "An actual density change must still own its real reflow.");
            AssertPalette(host.Window, nextTheme); AssertPalette(host.Panel, nextTheme);
            AssertDensity(host.Window, nextDensity); AssertDensity(host.Panel, nextDensity);
            foreach (var entry in Palette)
                Assert.NotSame(original["ControlCentre." + entry.Key], host.Panel.Resources["ControlCentre." + entry.Key]);
            Assert.Equal(Expected(nextTheme, "SurfaceBrush"), BrushColor(host.Surface.Background));
            Assert.Equal(Expected(nextTheme, "TextBrush"), BrushColor(host.Text.Foreground));
            Assert.Equal(nextDensity == UiDensity.Compact ? 14d : 15d, host.Text.FontSize);
            AssertDraftState(draft, choice);
            await Settle();
            AssertDraftState(draft, choice);
            Assert.Equal(nextTheme, state.App.ThemeService.CurrentTheme);
            Assert.Equal(nextDensity, state.App.ThemeService.CurrentDensity);
        }
        finally { OperatorMotion.AnimationOverride = previousAnimation; }
    }

    [AvaloniaFact]
    public async Task ExplicitSameThemeRefreshReplacesPaletteWithoutReflowOrDraftMutation()
    {
        using var state = new ThemeState();
        using var host = new OperatorHost();
        var (draft, choice) = AddDraftControls(host);
        await Settle();
        Assert.True(draft.Focus(NavigationMethod.Tab));
        draft.SelectionStart = 2; draft.SelectionEnd = 12;
        var theme = state.App.ThemeService.CurrentTheme;
        var density = state.App.ThemeService.CurrentDensity;
        var before = ResourceIdentities(host.Panel);
        var content = Assert.IsAssignableFrom<Control>(host.Window.Content);
        Assert.False(OperatorDensityTransition.IsAnimating(content));

        // The real HighContrast listener needs this refresh path even when
        // AppTheme is unchanged. No operating-system setting is mutated.
        OperatorDesign.UpdateTheme(theme, force: true);
        foreach (var entry in Palette)
            Assert.NotSame(before["ControlCentre." + entry.Key], host.Panel.Resources["ControlCentre." + entry.Key]);
        foreach (string key in DensityKeys)
            Assert.Same(before["ControlCentre." + key], host.Panel.Resources["ControlCentre." + key]);
        AssertPalette(host.Window, theme); AssertPalette(host.Panel, theme);
        AssertDensity(host.Window, density); AssertDensity(host.Panel, density);
        Assert.False(OperatorDensityTransition.IsAnimating(content));
        AssertDraftState(draft, choice);
        Assert.Equal(Expected(theme, "SurfaceBrush"), BrushColor(host.Surface.Background));
        Assert.Equal(Expected(theme, "TextBrush"), BrushColor(host.Text.Foreground));
    }

    [AvaloniaFact]
    public async Task LoadedAndReloadedRootsStillApplyAcceptedStateAfterLocalResourceReplacement()
    {
        using var state = new ThemeState();
        state.App.ThemeService.SetTheme(AppTheme.Light);
        state.App.ThemeService.SetDensity(UiDensity.Compact);
        using var host = new OperatorHost(pending =>
        {
            // Model XAML assigning Resources after its attached root flag.
            pending.Window.Resources = new ResourceDictionary();
            pending.Panel.Resources = new ResourceDictionary();
        });
        await Settle();
        AssertPalette(host.Window, AppTheme.Light); AssertPalette(host.Panel, AppTheme.Light);
        AssertDensity(host.Window, UiDensity.Compact); AssertDensity(host.Panel, UiDensity.Compact);

        var originalHost = host.Window.Content;
        host.Window.Content = null;
        await Settle();
        host.Panel.Resources = new ResourceDictionary();
        // Same global requests are intentionally not a resource-repair API;
        // this root's own Loaded handler must provide the current resources.
        OperatorDesign.UpdateTheme(AppTheme.Light);
        OperatorDesign.UpdateDensity(UiDensity.Compact);
        host.Window.Content = originalHost;
        await Settle();
        AssertPalette(host.Panel, AppTheme.Light);
        AssertDensity(host.Panel, UiDensity.Compact);
        Assert.Equal(Expected(AppTheme.Light, "SurfaceBrush"), BrushColor(host.Surface.Background));
        Assert.Equal(Expected(AppTheme.Light, "TextBrush"), BrushColor(host.Text.Foreground));
        Assert.Equal(14d, host.Text.FontSize);
    }

    [AvaloniaTheory]
    [InlineData(AppTheme.Dark, AppTheme.Light)]
    [InlineData(AppTheme.Light, AppTheme.Dark)]
    public async Task LiveThemeChangesUpdateExistingWindowPanelAndDynamicReferences(AppTheme first, AppTheme second)
    {
        using var state = new ThemeState();
        using var host = new OperatorHost();

        foreach (var theme in new[] { first, second, first })
        {
            state.App.ThemeService.SetTheme(theme);
            await Settle();

            Assert.Equal(theme, state.App.ThemeService.CurrentTheme);
            Assert.Equal(theme == AppTheme.Dark ? ThemeVariant.Dark : ThemeVariant.Light, state.App.RequestedThemeVariant);
            AssertPalette(host.Window, theme);
            AssertPalette(host.Panel, theme);
            Assert.Equal(Expected(theme, "SurfaceBrush"), BrushColor(host.Surface.Background));
            Assert.Equal(Expected(theme, "TextBrush"), BrushColor(host.Text.Foreground));
        }
    }

    [AvaloniaFact]
    public async Task LiveDarkLightDarkUpdatesTheExistingCloudPopupTitleAndHeader()
    {
        using var state = new ThemeState();
        var launcher = new CloudRemoteLauncher { Width = 300 };
        var window = new NativeWindow { Width = 680, Height = 680, Content = new Grid { Children = { launcher } } };
        OperatorDesign.Apply(window);
        window.Show();
        try
        {
            await Settle();
            launcher.Expand();
            await Settle();
            Assert.True(launcher.IsExpanded);
            var header = Assert.IsAssignableFrom<Border>(launcher.FindControl<Border>("HeaderSurface"));
            var popupHeader = Assert.IsAssignableFrom<Grid>(launcher.FindControl<Grid>("DetailsHeader"));
            var title = Assert.Single(popupHeader.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Cloud remote");

            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light, AppTheme.Dark })
            {
                state.App.ThemeService.SetTheme(theme);
                await Settle();

                Assert.True(launcher.IsExpanded);
                Assert.Equal(theme, state.App.ThemeService.CurrentTheme);
                Assert.Equal(Expected(theme, "SurfaceBrush"), BrushColor(header.Background));
                Assert.Equal(Expected(theme, "TextBrush"), BrushColor(title.Foreground));
            }
        }
        finally
        {
            launcher.Collapse();
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(UiDensity.Comfortable, UiDensity.Compact)]
    [InlineData(UiDensity.Compact, UiDensity.Comfortable)]
    public async Task LiveDensityChangesUpdateExistingOperatorResourcesAndDynamicText(UiDensity first, UiDensity second)
    {
        using var state = new ThemeState();
        using var host = new OperatorHost();

        foreach (var density in new[] { first, second, first })
        {
            state.App.ThemeService.SetDensity(density);
            await Settle();

            Assert.Equal(density, state.App.ThemeService.CurrentDensity);
            AssertDensity(host.Window, density);
            AssertDensity(host.Panel, density);
            Assert.Equal(density == UiDensity.Compact ? 14d : 15d, host.Text.FontSize);
        }
    }

    [AvaloniaTheory]
    [InlineData(AppTheme.Light, UiDensity.Compact)]
    [InlineData(AppTheme.Dark, UiDensity.Comfortable)]
    public async Task FreshThemeServiceInitializationSuppliesTheRequestedOperatorPaletteAndDensity(AppTheme theme, UiDensity density)
    {
        using var state = new ThemeState();
        var dictionaries = state.App.Resources.MergedDictionaries.ToArray();
        try
        {
            // A separate adapter models startup; its dictionaries are removed in finally.
            // No direct OperatorDesign palette or density update is used to prepare assertions.
            var service = new ThemeService();
            service.Initialize(theme, density);
            using var host = new OperatorHost();
            await Settle();

            Assert.Equal(theme, service.CurrentTheme);
            Assert.Equal(density, service.CurrentDensity);
            AssertPalette(host.Window, theme);
            AssertPalette(host.Panel, theme);
            AssertDensity(host.Window, density);
            AssertDensity(host.Panel, density);
            Assert.Equal(Expected(theme, "SurfaceBrush"), BrushColor(host.Surface.Background));
            Assert.Equal(Expected(theme, "TextBrush"), BrushColor(host.Text.Foreground));
            Assert.Equal(density == UiDensity.Compact ? 14d : 15d, host.Text.FontSize);
        }
        finally
        {
            state.App.Resources.MergedDictionaries.Clear();
            foreach (var dictionary in dictionaries) state.App.Resources.MergedDictionaries.Add(dictionary);
        }
    }

    private sealed class ThemeState : IDisposable
    {
        internal readonly App App = Assert.IsType<App>(Application.Current);
        private readonly AppTheme _serviceTheme;
        private readonly UiDensity _serviceDensity;
        private readonly ThemeVariant? _variant;
        private readonly AppTheme _operatorTheme;
        private readonly UiDensity _operatorDensity;

        internal ThemeState()
        {
            _serviceTheme = App.ThemeService.CurrentTheme;
            _serviceDensity = App.ThemeService.CurrentDensity;
            _variant = App.RequestedThemeVariant;
            _operatorTheme = ReadOperatorState<AppTheme>("_theme");
            _operatorDensity = ReadOperatorState<UiDensity>("_density");
        }

        private static T ReadOperatorState<T>(string name) => Assert.IsType<T>(
            typeof(OperatorDesign).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null));

        public void Dispose()
        {
            App.ThemeService.SetTheme(_serviceTheme);
            App.ThemeService.SetDensity(_serviceDensity);
            App.RequestedThemeVariant = _variant;
            // Cleanup only: the broken baseline can have service and operator state diverge.
            // Restore both original values without forcing the palette during any test action.
            OperatorDesign.UpdateTheme(_operatorTheme);
            OperatorDesign.UpdateDensity(_operatorDensity);
            Dispatcher.UIThread.RunJobs();
        }
    }
}

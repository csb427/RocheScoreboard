using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Roche_Scoreboard.Services;

namespace Roche_Scoreboard.Views;

/// <summary>Isolated native controls and Home input checks; never activates a match or output window.</summary>
internal static class OperatorControlsSelfTestChecks
{
    internal static async Task<Dictionary<string, bool>> RunAsync()
    {
        var checks = new Dictionary<string, bool>();
        var nested = new ResourceDictionary();
        var frozenAccent = new SolidColorBrush(Colors.Magenta);
        var frozenSelection = new SolidColorBrush(Colors.Cyan);
        frozenAccent.Freeze();
        frozenSelection.Freeze();
        nested["ControlCentre.AccentBrush"] = frozenAccent;
        nested["ControlCentre.SelectionBrush"] = frozenSelection;
        var resources = new ResourceDictionary();
        resources.MergedDictionaries.Add(nested);

        SolidColorBrush accent = OperatorAccentPreferences.AccentBrushResource;
        SolidColorBrush selection = OperatorAccentPreferences.SelectionBrushResource;
        OperatorAccentPreferences.UpdateMergedAccentBrushes(resources, accent, selection);
        checks["nested-frozen-resource-brushes-are-replaced-with-current-palette"] =
            frozenAccent.IsFrozen && frozenSelection.IsFrozen
            && ReferenceEquals(nested["ControlCentre.AccentBrush"], accent)
            && ReferenceEquals(nested["ControlCentre.SelectionBrush"], selection);

        bool originalGlass = OperatorAppearance.GlassEnabled;
        bool originalReducedMotion = OperatorAppearance.ReduceMotion;
        bool? originalMotionOverride = OperatorMotion.AnimationOverride;
        string appearancePath = OperatorAppearance.StoragePath;
        string directory = Path.GetDirectoryName(appearancePath)!;
        bool directoryExisted = Directory.Exists(directory);
        var savedFiles = new[] { "", ".bak", ".tmp" }.ToDictionary(suffix => appearancePath + suffix,
            path => File.Exists(path) ? File.ReadAllBytes(path) : null);
        var window = new Window { Width = 720, Height = 560, ShowInTaskbar = false, Title = "Isolated settings toggle check" };
        async Task Pump() => await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.Render);
        async Task Settle(TranslateTransform? transform, double expected)
        {
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (deadline.ElapsedMilliseconds < 1200 && (transform is null || Math.Abs(transform.X - expected) > .01))
            {
                await Pump();
                await Task.Delay(16);
            }
        }

        try
        {
            OperatorMotion.AnimationOverride = false;
            OperatorAppearance.Configure(true, false);
            OperatorDesign.Apply(window);
            var settings = new GlobalSettingsScreen();
            if (System.Windows.Application.Current is App app)
                settings.SetApplicationPreferences(app.ThemeService.CurrentTheme, app.ThemeService.CurrentDensity, true);
            settings.GlassMaterialToggle.IsChecked = true;
            window.Content = settings;
            window.Show();
            await Task.Delay(80);
            await Pump();
            System.Windows.Controls.CheckBox toggle = settings.GlassMaterialToggle;
            toggle.ApplyTemplate();
            TranslateTransform? thumb = toggle.Template?.FindName("Thumb", toggle) as TranslateTransform;
            await Settle(thumb, 20);
            checks["settings-toggle-initial-checked-state-puts-thumb-on-right"] = toggle.IsChecked == true && thumb is not null && Math.Abs(thumb.X - 20) < .01;

            toggle.ApplyTemplate();
            var previousThumb = thumb;
            var previousTemplate = toggle.Template;
            toggle.Template = null;
            toggle.ApplyTemplate();
            toggle.Template = previousTemplate;
            toggle.ApplyTemplate();
            await Pump();
            thumb = toggle.Template?.FindName("Thumb", toggle) as TranslateTransform;
            checks["settings-toggle-retemplate-creates-new-thumb"] = thumb is not null && !ReferenceEquals(previousThumb, thumb);
            await Settle(thumb, 20);
            checks["settings-toggle-retemplate-preserves-checked-thumb-position"] = toggle.IsChecked == true && thumb is not null && Math.Abs(thumb.X - 20) < .01;

            toggle.IsChecked = false;
            await Settle(thumb, 0);
            bool returnedLeft = toggle.IsChecked == false && thumb is not null && Math.Abs(thumb.X) < .01;
            toggle.IsChecked = true;
            await Settle(thumb, 20);
            checks["settings-toggle-reversal-returns-and-restores-thumb"] = returnedLeft
                && toggle.IsChecked == true && thumb is not null && Math.Abs(thumb.X - 20) < .01;
        }
        finally
        {
            window.Close();
            OperatorAppearance.Configure(originalGlass, originalReducedMotion);
            OperatorMotion.AnimationOverride = originalMotionOverride;
            foreach (var (path, bytes) in savedFiles)
            {
                if (bytes is not null) { Directory.CreateDirectory(directory); File.WriteAllBytes(path, bytes); }
                else if (File.Exists(path)) File.Delete(path);
            }
            if (!directoryExisted && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }

        checks["operator-appearance-preferences-use-isolated-self-test-storage"] = SelfTestStorage.IsEnabled
            && appearancePath.StartsWith(SelfTestStorage.RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        // Construct only: no Loaded handler, route activation, or production window.
        // History visibility is activated by Windows layout, not by construction.
        var home = new SportSelectionScreen();
        var history = (System.Windows.Controls.Button)home.FindName("HistoryButton");
        var commands = (System.Windows.Controls.Button)home.FindName("HomeCommandsButton");
        var start = (System.Windows.Controls.Button)home.FindName("AflLaunchAction");
        int historyIndex = System.Windows.Input.KeyboardNavigation.GetTabIndex(history);
        checks["home-history-tab-order-follows-commands-and-precedes-start"] =
            history.Focusable && history.IsEnabled && history.IsTabStop
            && historyIndex == 3
            && System.Windows.Input.KeyboardNavigation.GetTabIndex(commands) < historyIndex
            && historyIndex < System.Windows.Input.KeyboardNavigation.GetTabIndex(start);
        await CheckWizardFootersAsync(checks);
#if WINDOWS
        foreach (var (name, passed) in await MainWindow.RunHomeDecisionRootGuardChecksAsync())
            checks.Add(name, passed);
#endif
        return checks;
    }

    private static async Task CheckWizardFootersAsync(Dictionary<string, bool> checks)
    {
        if (!SelfTestStorage.IsEnabled) throw new InvalidOperationException("Wizard checks require isolated storage.");
        bool? originalMotion = OperatorMotion.AnimationOverride;
        var window = new Window { ShowInTaskbar = false, Title = "Isolated wizard footer check" };
        async Task Pump() => await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
        void CheckFooter(System.Windows.Controls.UserControl wizard, string name, bool cancelExpected, bool backExpected)
        {
            var nav = (Grid)wizard.FindName("NavBar");
            var cancel = (System.Windows.Controls.Button)wizard.FindName("CancelSetupButton");
            var back = (System.Windows.Controls.Button)wizard.FindName("BackButton");
            var next = (System.Windows.Controls.Button)wizard.FindName("NextButton");
            Rect Bounds(FrameworkElement element) => element.TransformToAncestor(nav).TransformBounds(new Rect(element.RenderSize));
            bool Fits(FrameworkElement element)
            {
                Rect bounds = Bounds(element);
                Geometry? clip = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(element);
                return element.IsVisible && element.Opacity == 1 && element.ActualWidth >= 44 && element.ActualHeight >= 44
                    && bounds.Left >= -.1 && bounds.Top >= -.1 && bounds.Right <= nav.ActualWidth + .1 && bounds.Bottom <= nav.ActualHeight + .1
                    && (clip is null || (clip.Bounds.Width >= element.ActualWidth - .1 && clip.Bounds.Height >= element.ActualHeight - .1));
            }
            checks[name + "-continue-fits-footer"] = Fits(next);
            checks[name + "-back-layout-preserved"] = backExpected
                ? Fits(back) && Bounds(back).Right <= Bounds(next).Left : back.Visibility == Visibility.Collapsed;
            checks[name + "-cancel-layout"] = cancelExpected
                ? Fits(cancel) && nav.ColumnDefinitions[Grid.GetColumn(cancel)].ActualWidth >= cancel.ActualWidth
                    && Bounds(cancel).Right <= Bounds(next).Left
                    && (!backExpected || Bounds(back).Right <= Bounds(cancel).Left)
                : cancel.Visibility == Visibility.Collapsed;
        }
        try
        {
            OperatorMotion.AnimationOverride = false;
            window.Show();
            foreach (var (width, height) in new[] { (1550, 1050), (1100, 750), (580, 460) })
            {
                window.Width = width; window.Height = height;
                string size = width + "x" + height;
                var wizard = new SetupWizard(); window.Content = wizard; await Pump();
                CheckFooter(wizard, "legacy-afl-" + size, false, true);
                wizard.ShowSportChoice(Models.SportMode.Cricket); await Pump();
                CheckFooter(wizard, "unified-sport-" + size, true, false);
                wizard.ContinueWithAfl(); await Pump();
                CheckFooter(wizard, "unified-afl-teams-" + size, true, true);
                wizard.ShowSportChoice(Models.SportMode.Cricket); await Pump();
                CheckFooter(wizard, "unified-sport-reopened-" + size, true, false);
                checks["unified-sport-reopened-selection-" + size] = wizard.CricketSportChoice.IsChecked == true && wizard.AflSportChoice.IsChecked == false;
                var cricket = new CricketSetupWizard(); window.Content = cricket; await Pump();
                CheckFooter(cricket, "legacy-cricket-" + size, false, true);
                cricket.EnableUnifiedSetup(); cricket.ContinueWithCricket(); await Pump();
                CheckFooter(cricket, "unified-cricket-teams-" + size, true, true);
                window.Content = null; await Pump();
            }
        }
        finally { window.Close(); OperatorMotion.AnimationOverride = originalMotion; }
    }
}

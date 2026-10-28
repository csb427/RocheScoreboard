using Brushes = System.Windows.Media.Brushes;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Roche_Scoreboard.Services;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TabControl = System.Windows.Controls.TabControl;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    internal async Task<Dictionary<string, bool>> RunControlCentreChecksAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new Dictionary<string, bool>();
        var originalDensity = ((App)System.Windows.Application.Current).ThemeService.CurrentDensity;
        var root = new Grid { Background = Brushes.Black, Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        var fields = new StackPanel { Orientation = Orientation.Horizontal };
        var button = new Button { Content = "Record score", Margin = new Thickness(0, 0, 12, 0) };
        var toggle = new CheckBox { Content = "Automatic overlays" };
        var input = new TextBox { Text = "Northern Cricket Club", Width = 230, Margin = new Thickness(10, 0, 0, 0) };
        fields.Children.Add(button); fields.Children.Add(toggle); fields.Children.Add(input); root.Children.Add(fields);
        var tabs = new TabControl(); Grid.SetRow(tabs, 1); root.Children.Add(tabs);
        var preview = new StackPanel { Margin = new Thickness(20) };
        preview.Children.Add(new TextBlock { Text = "Operator controls", FontSize = 28, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 20) });
        preview.Children.Add(new Button { Content = "Start next innings", HorizontalAlignment = System.Windows.HorizontalAlignment.Left });
        tabs.Items.Add(new TabItem { Header = "Match", Content = preview });
        tabs.Items.Add(new TabItem { Header = "Scorecard", Content = new TextBlock { Text = "Scorecard", Foreground = Brushes.White } });
        tabs.Items.Add(new TabItem { Header = "Output", Content = new TextBlock { Text = "Output", Foreground = Brushes.White } });
        tabs.Items.Add(new TabItem { Header = "Settings", Content = new TextBlock { Text = "Settings", Foreground = Brushes.White } });
        OperatorDesign.SetIsEnabled(root, true);
        button.SetResourceReference(StyleProperty, "ControlCentre.PrimaryButton");
        tabs.SetResourceReference(StyleProperty, "OperatorTabs");
        var fixture = new Window { Title = "Control Centre QA", Width = 960, Height = 620, Content = new AdornerDecorator { Child = root } };
        try
        {
            fixture.Show(); await Pause(); root.UpdateLayout();
            checks["local-button-template"] = button.Template.FindName("PART_Surface", button) is Grid;
            checks["one-motion-owner"] = OperatorControlMotion.GetEnabled(button) && !CricketActionMotion.GetEnabled(button);
            checks["floating-dock"] = tabs.Template.FindName("PART_OperatorDock", tabs) is Border;
            checks["liquid-lens"] = tabs.Template.FindName("PART_OperatorLens", tabs) is OperatorSelectionLens;
            input.Focus(); input.CaretIndex = 7; input.Select(3, 4);
            OperatorDesign.UpdateDensity(UiDensity.Compact); await Pause();
            checks["density-updates-locally"] = Math.Abs(button.MinHeight - 36) < .01;
            checks["density-preserves-selection"] = input.SelectionStart == 3 && input.SelectionLength == 4;
            OperatorDesign.UpdateDensity(UiDensity.Comfortable); await Pause();
            checks["comfortable-restores"] = Math.Abs(button.MinHeight - 42) < .01;
            for (int i = 0; i < 16; i++) { tabs.SelectedIndex = i % 4; await Task.Delay(16); }
            tabs.SelectedIndex = 0; await Task.Delay(650);
            checks["rapid-navigation-selection"] = tabs.SelectedIndex == 0 && tabs.SelectedContent == preview;
            var lens = (OperatorSelectionLens)tabs.Template.FindName("PART_OperatorLens", tabs);
            var settleDeadline=System.Diagnostics.Stopwatch.StartNew();
            while(lens.IsAnimating && settleDeadline.ElapsedMilliseconds<1500)
            {await fixture.Dispatcher.InvokeAsync(()=>fixture.UpdateLayout(),System.Windows.Threading.DispatcherPriority.Render);await Task.Delay(16);}
            var selectedTab = (TabItem)tabs.Items[0];
            double expectedLeft = selectedTab.TranslatePoint(new System.Windows.Point(), lens).X + 2;
            checks["lens-reaches-selected-tab-and-stops"] = Math.Abs(lens.LensBounds.Left - expectedLeft) < .2 && !lens.IsAnimating;
            if(!checks["lens-reaches-selected-tab-and-stops"])
                File.WriteAllText(Path.Combine(directory,"lens-diagnostic.json"),System.Text.Json.JsonSerializer.Serialize(new { expectedLeft, left=lens.LensBounds.Left, width=lens.LensBounds.Width, animating=lens.IsAnimating, selectedWidth=selectedTab.ActualWidth }));
            button.Focus();
            button.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(fixture)!, 0, System.Windows.Input.Key.Space) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
            input.Focus(); await Task.Delay(450);
            checks["focus-loss-releases-pressed-surface"] = button.Template.FindName("TbSc", button) is ScaleTransform press && Math.Abs(press.ScaleX - 1) < .001;
            toggle.IsChecked = true; await Task.Delay(500); toggle.ApplyTemplate();
            for(int frame=0;frame<40 && toggle.Template.FindName("Thumb",toggle) is TranslateTransform moving && Math.Abs(moving.X-20)>=.1;frame++)await Task.Delay(16);
            checks["toggle-completes"] = toggle.Template.FindName("Thumb", toggle) is TranslateTransform thumb && Math.Abs(thumb.X - 20) < .1;
            bool? motion = OperatorMotion.AnimationOverride;
            try { OperatorMotion.AnimationOverride = false; toggle.IsChecked = false; tabs.SelectedIndex = 2; await Pause(); checks["reduced-motion-final-state"] = toggle.Template.FindName("Thumb", toggle) is TranslateTransform still && Math.Abs(still.X) < .01 && tabs.SelectedIndex == 2; }
            finally { OperatorMotion.AnimationOverride = motion; }
            CaptureCricketQa(root, Path.Combine(directory, "control-centre-foundation.png"));
            var publicScorebug = new Roche_Scoreboard.Views.CricketScorebugControl();
            var output = new Window { WindowStyle = WindowStyle.None, Width = 386, Height = 193, ShowInTaskbar = false, Content = publicScorebug };
            try
            {
                output.Show(); await Pause();
                checks["public-resource-boundary"] = output.TryFindResource("ControlCentre.Button") is null && publicScorebug.TryFindResource("ControlCentre.Button") is null && !OperatorDesign.GetIsEnabled(publicScorebug);
            }
            finally { output.Close(); }
        }
        finally { fixture.Close(); OperatorDesign.UpdateDensity(originalDensity); }
        foreach (var check in await RunOperatorViewportChecksAsync(directory)) checks[check.Key] = check.Value;
        foreach (var check in await RunSetupReflowChecksAsync(directory)) checks[check.Key] = check.Value;
        foreach (var check in await RunSettingsVisualChecksAsync(directory)) checks[check.Key] = check.Value;
        await CaptureControlCentrePages(directory, checks);
        await CaptureControlCentreDecisions(directory, checks);
        foreach (var check in await RunOperatorGlassChecksAsync(directory)) checks[check.Key] = check.Value;
        foreach (var check in await RunAppleInteractionChecksAsync(directory)) checks[check.Key] = check.Value;
        return checks;
        static async Task Pause() { await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); await Task.Delay(60); }
    }

    private async Task CaptureControlCentreDecisions(string directory, Dictionary<string, bool> checks)
    {
        var match = CricketReliabilityChecks.CreateMatch(true);
        var resume = new Roche_Scoreboard.Views.ResumeMatchPrompt(new GameState());
        var host = new Window { Title = "Operator decisions QA", Width = 480, Height = 460, Owner = this, Content = resume, Background = Brushes.Black };
        try
        {
            host.Show(); await Task.Delay(350); host.UpdateLayout();
            checks["resume-primary-command-has-shared-surface"] = resume.ResumeButton.Template.FindName("PART_Surface", resume.ResumeButton) is Grid;
            checks["resume-footer-within-sheet"] = CricketQaBoundsWithin(resume.DiscardButton, resume);
            CaptureCricketQa(resume, Path.Combine(directory, "resume-decision.png"));
            var delivery = new Roche_Scoreboard.Views.CricketDeliveryDialog(match, initialType: Roche_Scoreboard.Models.CricketDeliveryType.NoBall);
            host.Content = delivery; host.Width = 620; host.Height = 700; await Task.Delay(250); host.UpdateLayout();
            checks["delivery-primary-contrast"] = delivery.ApplyButton.Foreground is SolidColorBrush brush && brush.Color == Colors.White;
            CaptureCricketQa(delivery, Path.Combine(directory, "delivery-decision.png"));
            host.Content = new Grid();
            var sheetHost = (Grid)host.Content;
            var trigger = new Button { Content = "Match options", HorizontalAlignment = System.Windows.HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            sheetHost.Children.Add(trigger); await host.Dispatcher.InvokeAsync(()=>host.UpdateLayout(),DispatcherPriority.Loaded); await Task.Delay(40); trigger.Focus();
            checks["integrated-sheet-trigger-can-receive-focus"] = trigger.IsKeyboardFocused;
            var choice = Roche_Scoreboard.Views.CricketPrompt.ChooseAsync(sheetHost, "Start the next innings?", "The current innings will be kept in the scorecard.", new("cancel", "Keep scoring"), new("start", "Start next innings", true));
            await Task.Delay(300); host.UpdateLayout(); CaptureCricketQa(sheetHost, Path.Combine(directory, "integrated-decision.png"));
            Roche_Scoreboard.Views.CricketPrompt.CancelAll(sheetHost); await choice;
            await host.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            checks["integrated-sheet-cancel-cleans-host"] = sheetHost.Children.Count == 1 && ReferenceEquals(sheetHost.Children[0],trigger);
            checks["integrated-sheet-restores-trigger-focus"] = !host.IsActive || trigger.IsKeyboardFocused;
        }
        finally { host.Close(); }
        var update = new Roche_Scoreboard.Views.UpdatePromptWindow(new AutoUpdateService(), new GitHubRelease { TagName = "v4.1.4", Body = "New\n- Operator interface preview\n\nFixed\n- More reliable navigation", Assets = [] }) { Owner = this };
        try { update.Show(); await Task.Delay(350); update.UpdateLayout(); CaptureCricketQa((FrameworkElement)update.Content, Path.Combine(directory, "update-decision.png")); checks["updater-preview-remains-unapplied"] = !update.UpdateApplied; }
        finally { update.Close(); }
        var colour = (Roche_Scoreboard.Views.ColorPickerWindow)Activator.CreateInstance(typeof(Roche_Scoreboard.Views.ColorPickerWindow), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new object[] { Colors.CornflowerBlue }, null)!;
        try { colour.Owner = this; colour.Show(); await Task.Delay(300); colour.UpdateLayout(); CaptureCricketQa((FrameworkElement)colour.Content, Path.Combine(directory, "colour-decision.png")); checks["colour-dialog-footer-reachable"] = colour.ActualHeight <= SystemParameters.WorkArea.Height; }
        finally { colour.Close(); }
    }

    private async Task CaptureControlCentrePages(string directory, Dictionary<string, bool> checks)
    {
        var app = (App)System.Windows.Application.Current;
        var originalTheme = app.ThemeService.CurrentTheme;
        var originalDensity = app.ThemeService.CurrentDensity;
        var host = new Window { Width = 1000, Height = 820, ShowInTaskbar = false };
        try
        {
            host.Show();
            var settings = new Roche_Scoreboard.Views.GlobalSettingsScreen(); host.Content = settings;
            settings.ThemeChanged += app.ThemeService.SetTheme; settings.DensityChanged += app.ThemeService.SetDensity;
            foreach (var size in new[] { (640d, 480d), (1000d, 820d) })
            {
                host.Width = size.Item1; host.Height = size.Item2; await Task.Delay(300); settings.UpdateLayout();
                checks[$"global-settings-fit-{size.Item1}x{size.Item2}"] = settings.ActualWidth <= host.ActualWidth && settings.ActualHeight <= host.ActualHeight;
                CaptureCricketQa(settings, Path.Combine(directory, $"global-settings-{size.Item1}x{size.Item2}.png"));
            }
            var cricket = new Roche_Scoreboard.Views.CricketControlPanel();
            var match = CricketReliabilityChecks.CreateMatch(true); cricket.SetMatch(match); host.Content = cricket;
            foreach (var size in new[] { (420d, 600d), (1000d, 820d), (1550d, 1000d) })
            {
                host.Width = size.Item1; host.Height = size.Item2;
                for (int index = 0; index < cricket.MainTabControl.Items.Count; index++)
                {
                    cricket.MainTabControl.SelectedIndex = index; await Task.Delay(220); cricket.UpdateLayout();
                    var dock = (Border)cricket.MainTabControl.Template.FindName("PART_OperatorDock", cricket.MainTabControl);
                    checks[$"cricket-dock-reachable-{size.Item1}-{index}"] = CricketQaBoundsWithin(dock, cricket);
                    CaptureCricketQa(cricket, Path.Combine(directory, $"cricket-control-{size.Item1}-tab-{index}.png"));
                }
            }
            cricket.SetMatch(match);
            var training = new Roche_Scoreboard.Views.TrainingControlPanel();
            var session = new Roche_Scoreboard.Models.TrainingSession();
            session.AddTimer(new Roche_Scoreboard.Models.TrainingTimer("Warm up", TimeSpan.FromMinutes(5)));
            training.Bind(session); host.Content = training;
            foreach (var width in new[] { 420d, 1000d }) { host.Width = width; host.Height = 700; await Task.Delay(250); CaptureCricketQa(training, Path.Combine(directory, $"training-control-{width}.png")); }
        }
        finally { host.Close(); app.ThemeService.SetTheme(originalTheme); app.ThemeService.SetDensity(originalDensity); }
        OnSportSelected(Models.SportMode.AFL);
        FinalizeAflSetup(new Models.SetupResult { HomeName = "Northern Football Club", AwayName = "Southern Football Club" });
        await WaitCricketQaStateAsync(this,()=>MainContent.IsVisible && SetupWizardPanel.Visibility==Visibility.Collapsed);
        bool guided = _uxPreferences.GuidedOperatorMode;
        try
        {
            _uxPreferences.GuidedOperatorMode = false; ApplyGuidedOperatorMode();
            foreach (var width in new[] { 420d, 900d, 1550d })
            {
                Width = width; Height = width < 600 ? 650 : 1000;
                for (int index = 0; index < MainTabControl.Items.Count; index++)
                {
                    MainTabControl.SelectedIndex = index; await Task.Delay(220); UpdateLayout();
                    var dock = (Border)MainTabControl.Template.FindName("PART_OperatorDock", MainTabControl);
                    checks[$"afl-dock-reachable-{width}-{index}"] = CricketQaBoundsWithin(dock, (FrameworkElement)Content);
                    CaptureCricketQa((FrameworkElement)Content, Path.Combine(directory, $"afl-control-{width}-tab-{index}.png"));
                }
            }
            foreach (var output in OutputWindows()) checks["actual-output-unscoped-" + output.GetType().Name] = !OperatorDesign.GetIsEnabled(output) && output.TryFindResource("ControlCentre.Button") is null;
        }
        finally { _uxPreferences.GuidedOperatorMode = guided; ApplyGuidedOperatorMode(); }
    }
}

using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Size = System.Windows.Size;

namespace Roche_Scoreboard.Services;

/// <summary>Dispatcher-only interaction checks; no match scoring or real operator data writes.</summary>
internal static class OperatorSetupTrainingChecks
{
    internal static async Task<Dictionary<string, bool>> RunAsync()
    {
        if (!SelfTestStorage.IsEnabled) throw new InvalidOperationException("Setup/training checks require isolated storage.");
        var checks = new Dictionary<string, bool>();
        bool? originalMotion = OperatorMotion.AnimationOverride;
        var window = new Window { Width = 1100, Height = 820, ShowInTaskbar = false };
        async Task Pump() => await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.Loaded);
        void Click(System.Windows.Controls.Primitives.ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        try
        {
            OperatorMotion.AnimationOverride = false;
            var wizard = new SetupWizard(); window.Content = wizard; window.Show(); await Pump();
            var frozenTrack = new SolidColorBrush(Colors.Magenta);
            frozenTrack.Freeze();
            var track = (Border)wizard.FindName("ControlPanelCountdownToggleTrack");
            var updateTrack = typeof(SetupWizard).GetMethod("UpdateControlPanelCountdownOptionVisuals", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (bool motion in new[] { false, true })
            {
                OperatorMotion.AnimationOverride = motion;
                track.Background = frozenTrack;
                updateTrack.Invoke(wizard, null);
                checks["afl-countdown-frozen-track-is-private-and-mutable-" + motion] =
                    track.Background is SolidColorBrush owned && !owned.IsFrozen && !ReferenceEquals(owned, frozenTrack)
                    && frozenTrack.IsFrozen && frozenTrack.Color == Colors.Magenta;
            }
            OperatorMotion.AnimationOverride = false;
            var sharedTrack = new SolidColorBrush(Colors.Cyan);
            track.Background = sharedTrack;
            updateTrack.Invoke(wizard, null);
            checks["afl-countdown-mutable-shared-track-remains-unchanged"] = sharedTrack.Color == Colors.Cyan
                && !sharedTrack.HasAnimatedProperties && !ReferenceEquals(track.Background, sharedTrack);
            track.Background = sharedTrack;
            typeof(SetupWizard).GetMethod("SettleWizardMotion", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(wizard, null);
            checks["afl-countdown-settle-preserves-mutable-shared-track"] = sharedTrack.Color == Colors.Cyan
                && !sharedTrack.HasAnimatedProperties && !ReferenceEquals(track.Background, sharedTrack);
            checks["afl-wizard-navigation-uses-native-focusable-buttons"] = wizard.NextButton.Focusable && wizard.BackButton.Focusable
                && wizard.Step1Navigation.Focusable && wizard.Step2Navigation.Focusable && wizard.Step3Navigation.Focusable;
            Click(wizard.NextButton); await Pump();
            checks["afl-wizard-reduced-motion-switches-immediately-to-readable-step"] = wizard.Step3Panel.IsVisible && wizard.Step3Panel.Opacity == 1
                && !wizard.TeamsStepContainer.IsVisible && !DependencyPropertyHelper.GetValueSource(wizard.Step3Panel, UIElement.OpacityProperty).IsAnimated;
            double nextWidth = wizard.NextButton.ActualWidth;
            Click(wizard.Step3Navigation); await Pump();
            checks["afl-wizard-start-caption-keeps-navigation-hit-target-width"] = Math.Abs(nextWidth - wizard.NextButton.ActualWidth) < .1;
            OperatorMotion.AnimationOverride = true;
            Click(wizard.Step1Navigation); Click(wizard.Step2Navigation); Click(wizard.Step3Navigation); Click(wizard.BackButton);
            await Task.Delay(340); await Pump();
            for(int frame=0;frame<60 && (wizard.Step3Panel.Opacity!=1 || !wizard.Step3Panel.RenderTransform.Value.IsIdentity);frame++){await Task.Delay(16);await Pump();}
            checks["afl-wizard-rapid-navigation-leaves-one-settled-page"] = wizard.Step3Panel.IsVisible && wizard.Step3Panel.Opacity == 1
                && !wizard.TeamsStepContainer.IsVisible && !wizard.Step4Panel.IsVisible && wizard.Step3Panel.RenderTransform.Value.IsIdentity;
            Click(wizard.Step1Navigation); window.Content = null; await Task.Delay(300);
            checks["afl-wizard-unload-cancels-page-and-ambient-clocks"] = !DependencyPropertyHelper.GetValueSource(wizard.HomeGlow, UIElement.OpacityProperty).IsAnimated
                && typeof(SetupWizard).GetField("_stepTransitionStoryboard", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(wizard) is null;

            OperatorMotion.AnimationOverride = false;
            var session = new TrainingSession(); session.AddTimer(new TrainingTimer("Warm up", TimeSpan.FromMinutes(5)));
            session.AddTimer(new TrainingTimer("Recovery", TimeSpan.FromSeconds(30)));
            var training = new TrainingControlPanel(); training.Bind(session); window.Content = training; await Pump();
            await CheckTrainingNavigationLayoutAsync(training, session, window, checks);
            training.StartPauseButton.ApplyTemplate();
            checks["training-actions-share-stable-inner-surface-motion-parts"] = training.StartPauseButton.Template.FindName("TbSc", training.StartPauseButton) is ScaleTransform
                && training.StartPauseButton.Template.FindName("ActionGlow", training.StartPauseButton) is UIElement;
            checks["training-reduced-motion-queue-is-readable-without-entry-clocks"] = training.QueueItems.Items.OfType<Border>().All(row => row.Opacity == 1
                && !DependencyPropertyHelper.GetValueSource(row, UIElement.OpacityProperty).IsAnimated && row.RenderTransform.Value.IsIdentity);
            training.AutoAdvanceToggle.Focus();
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Space) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            training.AutoAdvanceToggle.RaiseEvent(key);
            checks["training-focused-checkbox-keeps-space-instead-of-starting-timer"] = !session.IsRunning && !key.Handled;
            var toggle = new System.Windows.Automation.Peers.CheckBoxAutomationPeer(training.AutoAdvanceToggle);
            ((System.Windows.Automation.Provider.IToggleProvider)toggle).Toggle();
            checks["training-auto-advance-retains-native-check-semantics"] = !session.AutoAdvance && !session.IsRunning;
            var retainedRows = training.QueueItems.Items.Cast<object>().ToArray();
            int policyChanges = 0, structureChanges = 0;
            session.Changed += () => policyChanges++;
            session.StructuralChanged += () => structureChanges++;
            session.AutoAdvance = true;
            session.AutoAdvance = true;
            await Pump();
            checks["training-public-policy-refreshes-checkbox-once-without-rebuilding-queue"] =
                training.AutoAdvanceToggle.IsChecked == true && policyChanges == 1 && structureChanges == 0
                && retainedRows.SequenceEqual(training.QueueItems.Items.Cast<object>());
            training.NewTitleBox.Text = "Retained correction title";
            bool movedTitleFocus = training.NewMinutesBox.Focus() && !training.NewTitleBox.IsKeyboardFocusWithin;
            var pointer = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = training.NewTitleBox };
            training.NewTitleBox.RaiseEvent(pointer);
            checks["training-wpf-preview-pointer-never-clears-an-edited-title"] =
                movedTitleFocus && training.NewTitleBox.Text == "Retained correction title" && session.Timers.Count == 2 && !session.IsRunning;
            OperatorMotion.AnimationOverride = true;
            typeof(TrainingControlPanel).GetMethod("RebuildQueueList", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(training, null);
            await Pump();
            checks["training-rebuilding-existing-queue-does-not-replay-row-entry"] = training.QueueItems.Items.OfType<Border>().All(row => !DependencyPropertyHelper.GetValueSource(row, UIElement.OpacityProperty).IsAnimated);
            session.AddTimer(new TrainingTimer("New drill", TimeSpan.FromMinutes(2))); await Pump();
            window.Content = null; await Task.Delay(300);
            checks["training-unload-settles-queue-and-stops-ui-refresh"] = training.QueueItems.Items.OfType<Border>().All(row => row.Opacity == 1 && row.RenderTransform.Value.IsIdentity)
                && typeof(TrainingControlPanel).GetField("_ui", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(training) is DispatcherTimer { IsEnabled: false };
        }
        finally { window.Close(); OperatorMotion.AnimationOverride = originalMotion; }
        return checks;
    }

    private static async Task CheckTrainingNavigationLayoutAsync(TrainingControlPanel training, TrainingSession session,
        Window window, Dictionary<string, bool> checks)
    {
        var buttons = new[] { training.OperatorHomeButton, training.OperatorSettingsButton, training.OperatorCommandSearchButton };
        int home = 0, settings = 0, commands = 0, changes = 0;
        void Home() => home++;
        void Settings() => settings++;
        void Commands() => commands++;
        void Changed() => changes++;
        training.HomeNavigationRequested += Home;
        training.ApplicationPreferencesRequested += Settings;
        training.CommandSearchRequested += Commands;
        session.Changed += Changed;
        var originalTimers = session.Timers.ToArray();
        var originalRemaining = originalTimers.Select(timer => timer.Remaining).ToArray();
        var originalWindows = System.Windows.Application.Current.Windows.Cast<Window>().ToArray();
        int originalIndex = session.ActiveIndex;
        Rect Bounds(FrameworkElement element, FrameworkElement ancestor)
            => element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));
        bool Fits(FrameworkElement element, FrameworkElement ancestor)
        {
            Rect bounds = Bounds(element, ancestor);
            return element.ActualWidth > 0 && element.ActualHeight > 0
                && bounds.Left >= -.1 && bounds.Top >= -.1
                && bounds.Right <= ancestor.ActualWidth + .1 && bounds.Bottom <= ancestor.ActualHeight + .1;
        }
        try
        {
            // Set the content's dimensions, not the decorated Window's outer size.
            // The stopped synthetic session has no output or root navigation owner.
            int activation = 0;
            foreach (var size in new[] { new Size(420, 700), new Size(600, 700), new Size(800, 700), new Size(420, 460) })
            {
                training.Width = size.Width;
                training.Height = size.Height;
                await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
                string name = $"training-navigation-{size.Width:0}x{size.Height:0}";
                checks[name + "-uses-exact-panel-size"] = Math.Abs(training.ActualWidth - size.Width) < .1
                    && Math.Abs(training.ActualHeight - size.Height) < .1;
                checks[name + "-all-buttons-fit-header-and-viewport"] = buttons.All(button => button.IsVisible && button.IsEnabled
                    && button.Focusable && button.ActualWidth >= 44 && button.ActualHeight >= 36
                    && Fits(button, training.TrainingHeaderGrid) && Fits(button, training)
                    && (System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(button) is not Geometry clip
                        || (clip.Bounds.Width >= button.ActualWidth - .1 && clip.Bounds.Height >= button.ActualHeight - .1)));
                var buttonBounds = buttons.Select(button => Bounds(button, training.TrainingHeaderGrid)).ToArray();
                bool buttonsSeparate = !buttonBounds[0].IntersectsWith(buttonBounds[1])
                    && !buttonBounds[0].IntersectsWith(buttonBounds[2]) && !buttonBounds[1].IntersectsWith(buttonBounds[2]);
                bool readoutExpected = size.Height >= 500;
                checks[name + "-readout-preserves-responsive-visibility-and-bounds"] = readoutExpected
                    ? training.TrainingSessionReadout.IsVisible && Fits(training.TrainingSessionReadout, training.TrainingHeaderGrid)
                        && Fits(training.TrainingSessionReadout, training)
                    : training.TrainingSessionReadout.Visibility == Visibility.Collapsed;
                checks[name + "-navigation-does-not-overlap-session-readout"] = buttonsSeparate
                    && (!readoutExpected || buttonBounds.All(bounds => !bounds.IntersectsWith(Bounds(training.TrainingSessionReadout, training.TrainingHeaderGrid))));
                buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                bool correctCallbacks = home == activation + 1 && settings == activation && commands == activation;
                buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                correctCallbacks &= home == activation + 1 && settings == activation + 1 && commands == activation;
                buttons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                activation++;
                checks[name + "-each-navigation-callback-fires-once-without-session-or-output-changes"] =
                    correctCallbacks && home == activation && settings == activation && commands == activation && changes == 0
                    && !session.IsRunning && session.ActiveIndex == originalIndex
                    && originalTimers.SequenceEqual(session.Timers)
                    && originalRemaining.SequenceEqual(session.Timers.Select(timer => timer.Remaining))
                    && originalWindows.SequenceEqual(System.Windows.Application.Current.Windows.Cast<Window>());
            }
        }
        finally
        {
            training.HomeNavigationRequested -= Home;
            training.ApplicationPreferencesRequested -= Settings;
            training.CommandSearchRequested -= Commands;
            session.Changed -= Changed;
            training.Width = training.Height = double.NaN;
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
        }
    }
}

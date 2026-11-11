using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Roche_Scoreboard.Services;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    // Runs through the existing WPF workflow harness on a Windows dispatcher.
    // Compilation on another platform is not evidence this GUI check ran.
    private Dictionary<string, bool> RunHomeCommandChecks()
    {
        if (!SelfTestStorage.IsEnabled || System.Windows.Application.Current is not App { IsSelfTestMode: true })
            throw new System.InvalidOperationException("Home command checks require the isolated self-test app.");
        var checks = new Dictionary<string, bool>();
        bool? previousMotionOverride = OperatorMotion.AnimationOverride;
        OperatorMotion.AnimationOverride = false;
        var fixture = new MainWindow();
        try
        {
            fixture.SportSelectionPanel.Visibility = Visibility.Visible;
            fixture.MainContent.Visibility = Visibility.Collapsed;
#if WINDOWS
            fixture.SportSelectionPanel.PrepareForDisplay();
            fixture.Show(); fixture.UpdateLayout();
            checks["home-history-entry-is-visible-and-labelled"] =
                fixture.SportSelectionPanel.HistoryButton.Visibility == Visibility.Visible
                && fixture.SportSelectionPanel.HistoryButton.Content?.ToString()?.StartsWith("Match history", System.StringComparison.Ordinal) == true;
            var completedState = new GameState
            {
                Sport = "afl",
                MatchState = new Roche_Scoreboard.Models.MatchManager().ToState(),
                Messages = [],
                TickerSlots = []
            };
            completedState.MatchState.CompletedQuarterCount = 4;
            completedState.MatchState.QuarterSnapshots =
            [
                new Roche_Scoreboard.Models.QuarterSnapshot(), new Roche_Scoreboard.Models.QuarterSnapshot(),
                new Roche_Scoreboard.Models.QuarterSnapshot(), new Roche_Scoreboard.Models.QuarterSnapshot()
            ];
            var checkpoint = new MatchCheckpointStore.Checkpoint(1, "qa", System.DateTimeOffset.UtcNow,
                "Harbour v Rovers", "AFL · full time", completedState);
            var history = new Views.MatchHistoryView(true, [checkpoint]);
            history.ActionChosen += (_, _) => false;
            try
            {
                fixture.SportSelectionPanel.ShowHistory(history); fixture.UpdateLayout();
                checks["history-is-hosted-in-home-window"] = ReferenceEquals(Window.GetWindow(history), fixture)
                    && fixture.SportSelectionPanel.IsHistoryActive(history)
                    && fixture.SportSelectionPanel.HistoryViewHost.Content == history;
                checks["history-uses-shared-operator-theme-and-fills-home-sheet"] =
                    OperatorDesign.GetIsEnabled(fixture.SportSelectionPanel)
                    && fixture.SportSelectionPanel.HistoryOverlay.Visibility == Visibility.Visible
                    && fixture.SportSelectionPanel.HistoryPanel.ActualWidth >= 500
                    && fixture.SportSelectionPanel.HistoryPanel.ActualHeight >= 320;
                checks["history-completed-checkpoint-is-reviewable-and-restartable-but-not-continuable"] =
                    history.ActionButtons.Count == 3 && history.ActionButtons[0].Content?.ToString() == "Review"
                    && !history.ActionButtons[1].IsEnabled && history.ActionButtons[2].IsEnabled;
                var stateBeforeReview = checkpoint.State.MatchState!.CompletedQuarterCount;
                history.ActionButtons[0].Focus();
                history.ActionButtons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                checks["history-review-is-inline-and-read-only"] = history.IsReviewing
                    && checkpoint.State.MatchState.CompletedQuarterCount == stateBeforeReview
                    && ReferenceEquals(Window.GetWindow(history), fixture);
                var escape = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(fixture)!, 0, System.Windows.Input.Key.Escape)
                { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
                fixture.SportSelectionPanel.HistoryOverlay.RaiseEvent(escape);
                checks["history-review-first-escape-returns-to-list-and-origin"] = escape.Handled
                    && !history.IsReviewing && fixture.SportSelectionPanel.IsHistoryActive(history)
                    && ReferenceEquals(System.Windows.Input.Keyboard.FocusedElement, history.ActionButtons[0]);
                history.ActionButtons[0].Focus();
                history.ActionButtons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                checks["history-review-back-is-top-level-and-focuses-origin"] = history.IsReviewing
                    && history.ReviewBackButton is { IsVisible: true, IsEnabled: true }
                    && System.Windows.Controls.Grid.GetRow(history.ReviewBackButton) == 0
                    && ReferenceEquals(System.Windows.Input.Keyboard.FocusedElement, history.ReviewBackButton);
                history.ReviewBackButton!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                checks["history-review-back-restores-the-opening-review-action"] = !history.IsReviewing
                    && ReferenceEquals(System.Windows.Input.Keyboard.FocusedElement, history.ActionButtons[0]);
                history.ActionButtons[0].Focus();
                history.ActionButtons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                history.ActionButtons[2].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                checks["history-restart-declines-without-closing-or-changing-current-view"] = history.IsReviewing
                    && fixture.SportSelectionPanel.IsHistoryActive(history);
                escape = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(fixture)!, 0, System.Windows.Input.Key.Escape)
                { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
                fixture.SportSelectionPanel.HistoryOverlay.RaiseEvent(escape);
                var closeEscape = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(fixture)!, 0, System.Windows.Input.Key.Escape)
                { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
                fixture.SportSelectionPanel.HistoryOverlay.RaiseEvent(closeEscape);
                checks["history-second-escape-closes-sheet-after-returning-to-list"] = escape.Handled && closeEscape.Handled
                    && !fixture.SportSelectionPanel.IsHistoryActive(history);
            }
            finally { fixture.SportSelectionPanel.CloseHistory(history, restoreFocus: false); }
            var emptyHistory = new Views.MatchHistoryView(true, []);
            try
            {
                fixture.SportSelectionPanel.ShowHistory(emptyHistory); fixture.UpdateLayout();
                checks["history-empty-state-is-visible-in-home-sheet"] = emptyHistory.HasEmptyState
                    && ReferenceEquals(Window.GetWindow(emptyHistory), fixture);
            }
            finally { fixture.SportSelectionPanel.CloseHistory(emptyHistory, restoreFocus: false); }
            fixture.CheckHomeDecisionEarlyRootGuard(checks);
#endif
            checks["command-bar-opens-once"] = fixture.OpenCommandBar() && !fixture.OpenCommandBar();
            fixture.CommandSearch.Text = "zzzzzz";
            checks["command-empty-result-does-not-execute"] = fixture.CommandResults.Items.Count == 0 && !fixture.ExecuteCommand("does-not-exist");
            fixture.OpenCommandBar();
            fixture.CommandSearch.Text = "sound";
            checks["command-synonym-is-discoverable"] = fixture._visibleCommands.Any(c => c.Id == "settings");
            fixture.CloseCommandBar();
            checks["command-close-restores-input"] = fixture.SportSelectionPanel.IsEnabled && fixture.CommandOverlay.Visibility == Visibility.Collapsed;
            checks["home-does-not-activate-output"] = fixture._outputActivationRequired;
            checks["command-typo-uses-local-ranker"] = OperatorCommandSearch.Rank("bolwer",
                [new("bowler", "Change bowler", "bowling change")]).Single().Approximate;
        }
        finally { fixture.CloseCommandBar(); fixture.Close(); OperatorMotion.AnimationOverride = previousMotionOverride; }
        return checks;
    }

#if WINDOWS
    internal static async System.Threading.Tasks.Task<Dictionary<string, bool>> RunHomeDecisionRootGuardChecksAsync()
    {
        if (!SelfTestStorage.IsEnabled || System.Windows.Application.Current is not App { IsSelfTestMode: true } app
            || app.ShutdownMode != ShutdownMode.OnExplicitShutdown)
            throw new System.InvalidOperationException("Home root checks require the isolated, explicitly shut down self-test app.");
        app.Dispatcher.VerifyAccess();
        var checks = new Dictionary<string, bool>();
        bool? previousMotionOverride = OperatorMotion.AnimationOverride;
        MainWindow? fixture = null;
        try
        {
            OperatorMotion.AnimationOverride = false;
            fixture = new MainWindow { ShowInTaskbar = false };
            fixture.MainContent.Visibility = Visibility.Collapsed;
            fixture.SportSelectionPanel.Visibility = Visibility.Visible;
            fixture.SportSelectionPanel.PrepareForDisplay();
            fixture.Show();
            await fixture.Dispatcher.InvokeAsync(() => fixture.UpdateLayout(), System.Windows.Threading.DispatcherPriority.ContextIdle);
            // Do not call the broader Home/history/commands or cricket workflow.
            // This helper retains hidden AFL as its independent failure backstop.
            fixture.CheckHomeDecisionEarlyRootGuard(checks);
            return checks;
        }
        finally
        {
            try { fixture?.Close(); }
            finally { OperatorMotion.AnimationOverride = previousMotionOverride; }
        }
    }

    private void CheckHomeDecisionEarlyRootGuard(Dictionary<string, bool> checks)
    {
        // This is deliberately an early-dispatch guard test, not a live scoring
        // acceptance test. Hidden, unconfigured AFL is the independent safety
        // backstop if the guard regresses: a leaked score letter must not reach
        // EnsureScoreboardWindow, media creation, scoring or match completion.
        if (!SelfTestStorage.IsEnabled || System.Windows.Application.Current is not App { IsSelfTestMode: true }
            || MainContent.IsVisible || _aflMatchReady || OutputWindows().Any())
            throw new System.InvalidOperationException("Root guard checks require Home with no session or output.");

        var enabled = ApplicationSurface.Children.OfType<UIElement>().ToDictionary(child => child, child => child.IsEnabled);
        var initialWindows = System.Windows.Application.Current.Windows.Cast<Window>().ToArray();
        string initialMatch = System.Text.Json.JsonSerializer.Serialize(_match.ToState());
        string initialPreferences = System.Text.Json.JsonSerializer.Serialize(_uxPreferences);
        var initialTab = MainTabControl.SelectedItem;
        string initialPresentation = _activeScreenKey;
        var initialBindings = _aflKeybinds.ToArray();
        System.Windows.Input.KeyEventArgs KeyEvent(System.Windows.Input.Key key, RoutedEvent route)
            => new(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(this)!, 0, key) { RoutedEvent = route };
        void ArmSentinel()
        {
            // The very next root-handler statement would retire this invalid
            // capture. Its survival detects a missing or misplaced modal guard
            // even though hidden AFL safely prevents downstream score routing.
            _pendingKeybindAction = AflKeybindSettings.AflAction.HomeGoal;
            _pendingKeybindSource = null;
            _pendingKeybindSection = null;
        }
        try
        {
            _homeNavigationDecisionActive = true;
            foreach (var child in enabled.Keys) child.IsEnabled = false;
            var decision = Views.CricketPrompt.ShowValidatedAsync(ApplicationSurface, "Return to Home?", "",
                new System.Windows.Controls.TextBlock { Text = "Isolated root input check" }, () => true,
                new("stay", "Stay in session", true), new("apply", "Return to Home"));
            UpdateLayout();
            var safeButton = HomeGuardDescendants(ApplicationSurface).OfType<System.Windows.Controls.Button>()
                .Single(button => Equals(button.Tag, "stay"));
            checks["home-decision-early-root-guard-has-focused-sheet-target"] = safeButton.Focus()
                && ReferenceEquals(System.Windows.Input.Keyboard.FocusedElement, safeButton)
                && safeButton.IsDefault && enabled.Keys.All(child => !child.IsEnabled);
            var keys = _aflKeybinds.Select(binding => (Name: "binding-" + binding.Key, Key: binding.Value)).Concat(new[]
            {
                System.Windows.Input.Key.D1, System.Windows.Input.Key.D2, System.Windows.Input.Key.D3,
                System.Windows.Input.Key.D4, System.Windows.Input.Key.D5, System.Windows.Input.Key.D6,
                System.Windows.Input.Key.NumPad1, System.Windows.Input.Key.NumPad2, System.Windows.Input.Key.NumPad3,
                System.Windows.Input.Key.NumPad4, System.Windows.Input.Key.NumPad5, System.Windows.Input.Key.NumPad6,
                System.Windows.Input.Key.Z, System.Windows.Input.Key.Tab, System.Windows.Input.Key.Escape, System.Windows.Input.Key.Enter
            }.Select(key => (Name: "key-" + key, Key: key)));
            foreach (var (name, key) in keys)
            {
                ArmSentinel();
                var preview = KeyEvent(key, System.Windows.Input.Keyboard.PreviewKeyDownEvent);
                safeButton.RaiseEvent(preview);
                checks["home-decision-early-root-guard-delegates-" + name] =
                    _pendingKeybindAction == AflKeybindSettings.AflAction.HomeGoal && !preview.Handled && !decision.IsCompleted;
            }
            checks["home-decision-early-root-guard-preserves-fixture-state"] =
                initialMatch == System.Text.Json.JsonSerializer.Serialize(_match.ToState())
                && initialPreferences == System.Text.Json.JsonSerializer.Serialize(_uxPreferences)
                && ReferenceEquals(initialTab, MainTabControl.SelectedItem) && initialPresentation == _activeScreenKey
                && initialBindings.SequenceEqual(_aflKeybinds) && _outputActivationRequired && !OutputWindows().Any()
                && initialWindows.SequenceEqual(System.Windows.Application.Current.Windows.Cast<Window>());

            safeButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            checks["home-decision-sheet-safe-button-stays-in-session"] = decision.IsCompletedSuccessfully && decision.Result == "stay";
            decision = Views.CricketPrompt.ShowValidatedAsync(ApplicationSurface, "Return to Home?", "",
                new System.Windows.Controls.TextBlock { Text = "Isolated Escape check" }, () => true,
                new("stay", "Stay in session", true), new("apply", "Return to Home"));
            UpdateLayout();
            safeButton = HomeGuardDescendants(ApplicationSurface).OfType<System.Windows.Controls.Button>()
                .Single(button => Equals(button.Tag, "stay"));

            // Synthetic PreviewKeyDown does not synthesize a later KeyDown.
            // Route that bubble explicitly to verify the actual sheet's Escape
            // handler still cancels after the root delegated the preview.
            var escape = KeyEvent(System.Windows.Input.Key.Escape, System.Windows.Input.Keyboard.KeyDownEvent);
            safeButton.RaiseEvent(escape);
            checks["home-decision-sheet-escape-remains-owned-by-sheet"] = escape.Handled
                && decision.IsCompletedSuccessfully && decision.Result is null;
        }
        finally
        {
            Views.CricketPrompt.CancelAll(ApplicationSurface);
            foreach (var (child, wasEnabled) in enabled) child.IsEnabled = wasEnabled;
            _homeNavigationDecisionActive = false;
            RetireKeybindCapture();
        }

        try
        {
            _newSessionDecisionActive = true;
            ArmSentinel();
            var whileNewSession = KeyEvent(System.Windows.Input.Key.None, System.Windows.Input.Keyboard.PreviewKeyDownEvent);
            SportSelectionPanel.RaiseEvent(whileNewSession);
            checks["new-session-early-root-guard-remains-intact"] =
                _pendingKeybindAction == AflKeybindSettings.AflAction.HomeGoal && !whileNewSession.Handled;
            _newSessionDecisionActive = false;
            ArmSentinel();
            var afterCancel = KeyEvent(System.Windows.Input.Key.None, System.Windows.Input.Keyboard.PreviewKeyDownEvent);
            SportSelectionPanel.RaiseEvent(afterCancel);
            checks["home-decision-cancel-restores-root-input-and-surfaces"] = _pendingKeybindAction is null
                && !afterCancel.Handled && enabled.All(pair => pair.Key.IsEnabled == pair.Value)
                && SportSelectionPanel.IsVisible && !MainContent.IsVisible && !OutputWindows().Any();
        }
        finally { _newSessionDecisionActive = false; RetireKeybindCapture(); }
    }

    private static IEnumerable<DependencyObject> HomeGuardDescendants(DependencyObject parent)
    {
        for (int index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in HomeGuardDescendants(child)) yield return descendant;
        }
    }
#endif
}

// Generated from MainWindow.HomeCommands.cs; edit translator/adapters.
// Source SHA-256: f4945d319be30366a40a41e15dc69ecc24fc256d3146121dc5c5f4e24522d4a4
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.Linq;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using HorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using Button = Avalonia.Controls.Button;
using KeyEventArgs = Avalonia.Input.KeyEventArgs;
using TextBlock = Avalonia.Controls.TextBlock;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    // Output activation is independent of restoration/navigation. Creating an
    // output view for previews must never put a recovered match on air.
    private bool _outputActivationRequired = true;
    private readonly Dictionary<Control, bool> _homeReturn = new();
    private SportMode _homeReturnMode;
    private Control? _homeReturnFocus;
    private Control? _globalSettingsReturnFocus;
    private bool _homeNavigationDecisionActive;
    private int _homeNavigationDecisionGeneration;
    private readonly Dictionary<Control, bool> _commandEnabled = new();
    private IInputElement? _commandPreviousFocus;
    private bool _commandExecuting;
    private int _commandTabFocusGeneration;
    private bool _newSessionDecisionActive;
    private CricketMatchManager? _commandObservedCricket;
    private Control? _commandPreferencesReturn;
    private sealed record OperatorCommand(string Id, string Label, string Search, Action Run, string? Unavailable = null);
    private List<OperatorCommand> _visibleCommands = new();

    private bool HasCurrentSession => _sportMode switch
    {
        SportMode.Cricket => _cricketMatch is not null,
        SportMode.Training => _trainingSession is not null,
        SportMode.AFL => _aflMatchReady,
        _ => false
    };
    private bool CommandContextBlocked => _newSessionDecisionActive || _homeNavigationDecisionActive || StoppagePopup?.IsOpen==true || _recoveryPromptActive || _resetDecisionActive || MatchLoadingPanel.IsVisible || SportSelectionPanel.HasOpenSheet ||
        (TrainingControlPanelView.IsVisible && (TrainingControlPanelView.HasActiveClearDecision || TrainingControlPanelView.HasActiveExitDecision)) ||
        NativeKeyboard.FocusedElement is Avalonia.Controls.MenuItem ||
        OwnedWindows.Cast<NativeWindow>().Any(w => w.IsVisible && !OutputWindows().Contains(w)) ||
        (CricketControlPanelView.IsVisible && (CricketControlPanelView.HasActiveDecision ||
         CricketControlPanelView.TossOverlay.IsVisible || CricketControlPanelView.BatterSelectionOverlay.IsVisible ||
         CricketControlPanelView.BowlerSelectionOverlay.IsVisible || CricketControlPanelView.DeliveryEntryOverlay.IsVisible));

    internal bool CanOpenTrainingClearReview => _trainingSession is not null && TrainingControlPanelView.IsVisible
        && CommandOverlay.IsVisible != true && !CommandContextBlocked;
    internal bool CanOpenTrainingExitReview => _trainingSession is not null && TrainingControlPanelView.IsVisible
        && CommandOverlay.IsVisible != true && !CommandContextBlocked;

    private void GoHome()
    {
        if (SportSelectionPanel.IsVisible) return;
        bool capture = _homeReturn.Count == 0;
        if (capture) { _homeReturnMode = _sportMode; _homeReturnFocus = NativeKeyboard.FocusedElement as Control; }
        Control? settingsReturn = _commandPreferencesReturn ?? (_returnToCricketFromGlobalSettings ? CricketControlPanelView : null);
        foreach (var surface in ApplicationSurface.Children.OfType<Control>())
        {
            if (surface == SportSelectionPanel || surface == CommandOverlay || surface == RecoveryOverlay) continue;
            if (capture) _homeReturn[surface] = surface == settingsReturn || (surface != GlobalSettingsPanel && surface.IsVisible == true);
            surface.IsVisible = false;
        }
        _commandPreferencesReturn = null;
        _returnToCricketFromGlobalSettings = false;
        _outputActivationRequired = true;
        InvalidatePresentationWork();
        foreach (var window in OutputWindows()) window.Hide();
        SportSelectionPanel.SetCurrentSession(HasCurrentSession ? CurrentSessionTitle() : null, CurrentSessionDetail());
        SportSelectionPanel.PrepareForDisplay();
        SportSelectionPanel.IsVisible = true;
        SportSelectionPanel.Opacity = 1;
    }

    private void OperatorHomeNavigation_Click(object? sender, RoutedEventArgs e) => RequestOperatorHome();
    private void OperatorSettingsNavigation_Click(object? sender, RoutedEventArgs e) => RequestOperatorSettings();
    private void OperatorCommandNavigation_Click(object? sender, RoutedEventArgs e) => OpenCommandBar();

    private bool TryGetOperatorNavigationCommand(string id, out OperatorCommand? command)
    {
        command = null;
        if (CommandContextBlocked) return false;
        command = Commands().FirstOrDefault(candidate => candidate.Id == id);
        if (command is null) return false;
        if (command.Unavailable is not { } reason) return true;
        if (OpenCommandBar()) CommandHint.Text = reason;
        return false;
    }

    private void RequestOperatorSettings()
    {
        if (GlobalSettingsPanel.IsVisible) return;
        if (!TryGetOperatorNavigationCommand("settings", out var command)) return;
        _globalSettingsReturnFocus = NativeKeyboard.FocusedElement as Control;
        command!.Run();
    }

    private async void RequestOperatorHome()
    {
        if (SportSelectionPanel.IsVisible || !TryGetOperatorNavigationCommand("home", out _)) return;
        if (!OutputWindows().Any(window => window.IsVisible)) { GoHome(); return; }
        if (_homeNavigationDecisionActive || !IsLoaded || !IsVisible) return;

        int generation = ++_homeNavigationDecisionGeneration;
        int setupGeneration = _setupFlowGeneration, replacementGeneration = _sessionReplacementGeneration;
        SportMode sport = _sportMode;
        var cricket = _cricketMatch; var training = _trainingSession; bool aflReady = _aflMatchReady;
        bool Current() => generation == _homeNavigationDecisionGeneration && IsLoaded && IsVisible
            && setupGeneration == _setupFlowGeneration && replacementGeneration == _sessionReplacementGeneration
            && sport == _sportMode && ReferenceEquals(cricket, _cricketMatch)
            && ReferenceEquals(training, _trainingSession) && aflReady == _aflMatchReady;
        var notice = new NativeTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, IsVisible = false };
        notice.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.Brush.DangerForeground");
        bool Validate()
        {
            if (Current()) return true;
            notice.Text = "The active session changed. Stay here and review it before returning Home.";
            notice.IsVisible = true;
            return false;
        }
        void ClosingOwner(object? sender, System.ComponentModel.CancelEventArgs args) => Views.CricketPrompt.CancelAll(ApplicationSurface);
        var enabled = ApplicationSurface.Children.OfType<Control>().ToDictionary(child => child, child => child.IsEnabled);
        _homeNavigationDecisionActive = true;
        Closing += ClosingOwner;
        bool accepted = false;
        try
        {
            foreach (var child in enabled.Keys) child.IsEnabled = false;
            string explanation = _sportMode == SportMode.Training
                ? "Live training output will turn off. Your training session and timer keep running, and the session stays open when you return from Home. Output stays off until you choose to show it again."
                : "Live scoreboard output will turn off. Your match, score and any running clock stay active. Return to the session from Home; output stays off until you choose to show it again.";
            var body = new StackPanel();
            var detail = new NativeTextBlock { Text = explanation, TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new Thickness(0, 8, 0, 8) };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.TextBrush");
            body.Children.Add(detail); body.Children.Add(notice);
            accepted = await Views.CricketPrompt.ShowValidatedAsync(ApplicationSurface, "Return to Home?", "", body, Validate,
                new("stay", "Stay in session", true), new("apply", "Return to Home")) == "apply";
        }
        finally
        {
            Closing -= ClosingOwner;
            foreach (var (child, wasEnabled) in enabled) child.IsEnabled = wasEnabled;
            _homeNavigationDecisionActive = false;
        }
        if (accepted && Current()) GoHome();
    }

    private void RestoreGlobalSettingsReturnFocus()
    {
        Control? target = _globalSettingsReturnFocus;
        _globalSettingsReturnFocus = null;
        if (target is null) return;
        Dispatcher.UIThread.Post(Avalonia.Threading.DispatcherPriority.Input, new Action(() =>
        {
            if (GlobalSettingsPanel.IsVisible || CommandOverlay.IsVisible || !IsVisible
                || !target.IsLoaded || !target.IsVisible || !target.IsEnabled || !target.Focusable
                || !ReferenceEquals(NativeWindow.GetWindow(target), this)) return;
            target.Focus(); NativeKeyboard.Focus(target);
        }));
    }

    private string CurrentSessionTitle() => (_homeReturn.Count > 0 ? _homeReturnMode : _sportMode) == SportMode.Cricket && _cricketMatch is { } cricket
        ? $"{cricket.TeamAName} v {cricket.TeamBName}"
        : (_homeReturn.Count > 0 ? _homeReturnMode : _sportMode) == SportMode.Training ? "Current training session" : $"{_match.HomeName} v {_match.AwayName}";

    private string CurrentSessionDetail() => _homeReturnMode == SportMode.Cricket && _cricketMatch is { } cricket
        ? $"Current cricket session · innings {cricket.CurrentInningsNumber} · {cricket.TotalRuns}/{cricket.TotalWickets} · includes unsaved changes"
        : _homeReturnMode == SportMode.Training ? "Current training session · kept in this open app only; not saved after closing"
        : $"Current AFL session · quarter {_match.Quarter} · {_match.HomeTotal}–{_match.AwayTotal} · includes unsaved changes";

    private void RefreshHomeCurrentSession()
    {
        if (SportSelectionPanel.IsVisible && _homeReturn.Count > 0 && HasCurrentSession)
            SportSelectionPanel.SetCurrentSession(CurrentSessionTitle(), CurrentSessionDetail());
    }

    private void ResumeFromHome()
    {
        if (_homeReturn.Count > 0 && HasCurrentSession)
        {
            foreach (var (surface, visibility) in _homeReturn) surface.IsVisible = visibility ? true : false;
            _homeReturn.Clear();
            _sportMode = _homeReturnMode;
            SportSelectionPanel.IsVisible = false;
            SportSelectionPanel.SetCurrentSession(null);
            Control? target = _homeReturnFocus; _homeReturnFocus = null;
            if (target is not null) Dispatcher.UIThread.Post(Avalonia.Threading.DispatcherPriority.Input, new Action(() =>
            {
                if (SportSelectionPanel.IsVisible || !target.IsLoaded || !target.IsVisible || !target.IsEnabled || !target.Focusable
                    || !ReferenceEquals(NativeWindow.GetWindow(target), this)) return;
                target.Focus(); NativeKeyboard.Focus(target);
            }));
            return;
        }
        ResumeDeferredMatch();
    }

    private int _sessionReplacementGeneration;
    private bool RetireSessionForReplacement()
    {
        int generation = ++_sessionReplacementGeneration, setup = _setupFlowGeneration;
        var mode = _sportMode;
        _match.RetireLifecycle();
        InvalidateStoppageEditor();
        _outputActivationRequired = true;
        InvalidatePresentationWork();
        _match.PauseClock();
        StopBreakRotation(); StopStatsBarTimer(); ResetScoreAnimationQueue(); CancelQueuedOverlays();
        _displayWindow?.Hide();
        if (_trainingSession is not null) ExitTrainingModeInternal(suppressUiTransition: true);
        // The captured old Training Pause can synchronously start another mode.
        // A superseded outer setup must not continue over that replacement.
        if (generation != _sessionReplacementGeneration || setup != _setupFlowGeneration || mode != _sportMode) return false;
        if (_cricketMatch is not null)
        {
            _cricketMatch.MatchChanged -= OnCricketMatchChanged;
            CricketControlPanelView.PresentationChanged -= OnCricketPresentationChanged;
            CricketControlPanelView.ClearMatch();
            _cricketScorebug?.Dispose(); _cricketDisplayWindow?.Close();
            _cricketDisplayWindow = null; _cricketScorebug = null; _cricketSummary = null; _cricketMatch = null;
        }
        TrainingControlPanelView.IsVisible = CricketControlPanelView.IsVisible = MainContent.IsVisible = false;
        _aflMatchReady = false;
        _cricketRecoveryWriter?.InvalidatePending();
        return generation == _sessionReplacementGeneration && setup == _setupFlowGeneration && mode == _sportMode;
    }

    private async Task<bool> ConfirmNewSessionAsync(SportMode mode)
    {
        if (_newSessionDecisionActive) return false;
        if (mode == SportMode.Training && !HasCurrentSession) return true;
        if (mode == SportMode.Settings || (!HasCurrentSession && RecoverySession.Describe(GameStateService.Load()) is null)) return true;
        if (!IsLoaded || !IsVisible) return false;
        int setup = _setupFlowGeneration, replacement = _sessionReplacementGeneration;
        var sport = _sportMode; var cricket = _cricketMatch; var training = _trainingSession;
        bool ready = _aflMatchReady, changed = false, accepted = false;
        string? cricketToken = cricket?.WorkflowToken;
        var notice = new NativeTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, IsVisible = false };
        notice.SetResourceReference(TextBlock.ForegroundProperty, "ScoreGrid.Brush.DangerForeground");
        bool Current() => IsLoaded && IsVisible && setup == _setupFlowGeneration && replacement == _sessionReplacementGeneration
            && sport == _sportMode && ready == _aflMatchReady && !changed
            && ReferenceEquals(cricket, _cricketMatch) && ReferenceEquals(training, _trainingSession)
            && cricketToken == _cricketMatch?.WorkflowToken;
        bool Validate()
        {
            if (Current()) return true;
            notice.Text = "The session changed. Cancel and review it before starting a new session.";
            notice.IsVisible = true; return false;
        }
        void Changed() => changed = true;
        void ClosingOwner(object? sender, System.ComponentModel.CancelEventArgs args) => Views.CricketPrompt.CancelAll(ApplicationSurface);
        var enabled = ApplicationSurface.Children.OfType<Control>().ToDictionary(child => child, child => child.IsEnabled);
        _newSessionDecisionActive = true;
        _match.MatchChanged += Changed;
        if (training is not null) training.StructuralChanged += Changed;
        Closing += ClosingOwner;
        try
        {
            foreach (var child in enabled.Keys) child.IsEnabled = false;
            var body = new StackPanel();
            string consequence = mode == SportMode.Training
                ? "The current session will close. Training is kept only while the app is open; the last saved match remains available after exiting."
                : "Starting the new session replaces the current or saved session. Cancel to keep it available through Resume on Home.";
            var consequenceText = new NativeTextBlock { Text = consequence, TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new Thickness(0, 8, 0, 8) };
            consequenceText.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.TextBrush");
            body.Children.Add(consequenceText); body.Children.Add(notice);
            accepted = await Views.CricketPrompt.ShowValidatedAsync(ApplicationSurface, "New session", "", body, Validate,
                new("cancel", "Cancel"), new("stay", "Cancel", true),
                new("apply", mode == SportMode.Training ? "Start training" : "Start new session", false, true)) == "apply";
        }
        finally
        {
            foreach (var (child, wasEnabled) in enabled) child.IsEnabled = wasEnabled;
            _match.MatchChanged -= Changed;
            if (training is not null) training.StructuralChanged -= Changed;
            Closing -= ClosingOwner;
            _newSessionDecisionActive = false;
        }
        return accepted && Current();
    }

    private void OpenOperatorPreferences()
    {
        if (GlobalSettingsPanel.IsVisible) return;
        if (CricketControlPanelView.IsVisible && _cricketMatch is not null) { OpenCricketApplicationPreferences(); return; }
        _globalSettingsReturnFocus ??= NativeKeyboard.FocusedElement as Control;
        _commandPreferencesReturn = MainContent.IsVisible ? MainContent : TrainingControlPanelView.IsVisible ? TrainingControlPanelView : null;
        if (_commandPreferencesReturn is not null)
        {
            _commandPreferencesReturn.IsVisible = false;
            RefreshGlobalSettingsOutputPreferences();
            GlobalSettingsPanel.SetBackCaption("←  Back to session");
            GlobalSettingsPanel.IsVisible = true;
            GlobalSettingsPanel.FocusBackButton();
        }
        else OnSportSelected(SportMode.Settings);
    }

    private void EnableOperatorOutput()
    {
        if (_sportMode == SportMode.Cricket && _cricketMatch is not null)
        { _outputActivationRequired = false; EnsureCricketDisplayWindow(); }
        else if (_sportMode == SportMode.Training && _trainingSession is not null)
        { _outputActivationRequired = false; EnsureTrainingDisplayWindow(); }
        else
        {
            if (!_outputActivationRequired && _displayWindow is { IsVisible: true }) return;
            // Prepare one complete, current frame before exposing a recreated
            // output. In particular do not flash the default scorebug behind a
            // restored chart or replay a disposed media player automatically.
            _outputActivationRequired = true;
            EnsureScoreboardWindow(); PopulateScorebugState(includeHidden: true);
            RestoreAflOutputPresentation();
            _outputActivationRequired = false;
            _displayWindow?.Show();
            if (_activeScreenKey == "screensaver") _outputScreensaver?.Configure(_screensaverMediaPath);
            PushAllToScoreboard(); UpdateActiveDisplayLabel(_activeScreenKey);
        }
    }

    private IEnumerable<OperatorCommand> Commands()
    {
        bool home = SportSelectionPanel.IsVisible;
        bool cricket = CricketControlPanelView.IsVisible && _cricketMatch is not null;
        bool afl = MainContent.IsVisible && _aflMatchReady;
        bool training = TrainingControlPanelView.IsVisible && _trainingSession is not null;
        bool editing = cricket && CricketControlPanelView.HasPendingOperatorEdits;
        bool setup = SetupWizardPanel.IsVisible || CricketSetupWizardPanel.IsVisible;
        string? changePage = editing ? "Apply or cancel the scorecard edits first."
            : setup ? "Finish setup or use its Back / cancel control first." : null;
        if (!home) yield return new("home", "Home / resume", "landing saved session match clock carries on", RequestOperatorHome, changePage);
        yield return new("settings", "Application settings", "general cricket afl sound volume mute audio appearance preferences settings", OpenOperatorPreferences, changePage);
        if (home)
        {
            yield return new("resume", "Resume saved / current session", "continue recovery", ResumeFromHome,
                HasCurrentSession || RecoverySession.Describe(GameStateService.Load()) is not null ? null : "No recoverable session is available.");
            yield return new("start-game", "Start Game", "new match setup afl footy football cricket choose sport", BeginUnifiedSetup);
            yield return new("training", "Start training", "timer drills", () => OnSportSelected(SportMode.Training));
        }
        if (cricket)
        {
            yield return new("scorecard", "Cricket scorecard", "edit batting bowling figures", () => CricketControlPanelView.MainTabControl.SelectedItem = CricketControlPanelView.ScorecardTab);
            yield return new("bowler", "Change bowler", "cricket choose bowling change bowler", () => CricketControlPanelView.OpenCommandBowler(),
                changePage ?? (_cricketMatch!.TossPending || _cricketMatch.IsMatchFinalized || _cricketMatch.CurrentInnings?.IsComplete != false ? "Bowler selection is unavailable at this stage." : null));
            yield return new("cricket-output", "Cricket presentation controls", "scorebug manhattan scoreworm overlays display", () => CricketControlPanelView.MainTabControl.SelectedItem = CricketControlPanelView.OutputTab, changePage);
        }
        if (afl)
        {
            yield return new("afl-match", "AFL match controls", "score clock quarter", () => MainTabControl.SelectedIndex = 0);
            string? outputUnavailable = _uxPreferences.GuidedOperatorMode
                ? "Select Advanced Tools to reveal Output controls." : null;
            yield return new("afl-output", "AFL presentation controls", "scorebug output display", () => MainTabControl.SelectedItem = OutputStudioTab, outputUnavailable);
            yield return new("afl-settings", "AFL match settings", "sound volume audio goal animations options settings", () => MainTabControl.SelectedItem = MatchSetupTab);
        }
        if (cricket || afl || training)
            yield return new("enable-output", "Show output window", "enable presentation display on air", EnableOperatorOutput, changePage);
    }

    internal bool OpenCommandBar()
    {
        if (CommandOverlay.IsVisible == true || _commandExecuting || CommandContextBlocked) return false;
        // Disabling the panel retires its floating menu. Return to the live
        // launcher, rather than retaining a menu row that is about to detach.
        _commandPreviousFocus = CricketControlPanelView.IsVisible
            ? CricketControlPanelView.PlayerMenuReturnFocus ?? NativeKeyboard.FocusedElement
            : NativeKeyboard.FocusedElement;
        _commandEnabled.Clear();
        foreach (Control child in ApplicationSurface.Children)
        {
            if (child == CommandOverlay) continue;
            _commandEnabled[child] = child.IsEnabled;
            child.IsEnabled = false;
        }
        _match.MatchChanged += RefreshCommands;
        _commandObservedCricket = _cricketMatch;
        if (_commandObservedCricket is not null) _commandObservedCricket.MatchChanged += RefreshCommands;
        Views.SharedSurfaceMotion.AttachFrom(CommandSurface,SportSelectionPanel.IsVisible?SportSelectionPanel.FindName("HomeCommandsButton") as Control:null);
        CommandOverlay.IsVisible = true;
        CommandSearch.Text = "";
        RefreshCommands();
        SizeCommandBar();
        Views.SharedSurfaceMotion.Attach(CommandSurface);
        Views.SharedSurfaceMotion.Reveal(CommandSurface);
        Views.SharedSurfaceMotion.RevealSequence(CommandResults.Items.OfType<Control>());
        CommandSearch.Focus();
        return true;
    }

    private void CloseCommandBar(bool restoreFocus = true)
    {
        _match.MatchChanged -= RefreshCommands;
        if (_commandObservedCricket is not null) _commandObservedCricket.MatchChanged -= RefreshCommands;
        _commandObservedCricket = null;
        CommandOverlay.IsVisible = false;
        foreach (var (child, enabled) in _commandEnabled) child.IsEnabled = enabled;
        _commandEnabled.Clear();
        if (restoreFocus && _commandPreviousFocus is Control { IsVisible: true, IsEnabled: true } previous) previous.Focus();
        _commandPreviousFocus = null;
    }

    private static bool CommandChangesOperatorTab(string id) => id is "scorecard" or "cricket-output" or "afl-match" or "afl-output" or "afl-settings";

    private void FocusSelectedOperatorTab(string id)
    {
        Avalonia.Controls.TabControl? tabs = id is "scorecard" or "cricket-output"
            ? CricketControlPanelView.MainTabControl : MainTabControl;
        if (tabs?.SelectedItem is not Avalonia.Controls.TabItem target) return;
        int generation = ++_commandTabFocusGeneration;
        Dispatcher.UIThread.Post(Avalonia.Threading.DispatcherPriority.Input, new Action(() =>
        {
            if (generation != _commandTabFocusGeneration || CommandOverlay.IsVisible
                || !ReferenceEquals(tabs.SelectedItem, target) || !target.IsVisible || !target.IsEnabled) return;
            // A tab command can leave focus in the page it just hid. Restore
            // focus to the selected destination; the selected-item and
            // generation checks reject callbacks made stale by later routing.
            target.Focus(); NativeKeyboard.Focus(target);
        }));
    }

    private void RefreshCommands()
    {
        if (CommandOverlay.IsVisible != true) return;
        string? selectedId = CommandResults.SelectedIndex >= 0 && CommandResults.SelectedIndex < _visibleCommands.Count
            ? _visibleCommands[CommandResults.SelectedIndex].Id : null;
        var commands = Commands().ToDictionary(c => c.Id);
        var hits = OperatorCommandSearch.Rank((CommandSearch.Text ?? string.Empty), commands.Values.Select(c =>
            new CommandSearchCandidate(c.Id, c.Label, c.Search,
                c.Id is "scorecard" or "afl-match" ? 30 : c.Id == "resume" ? (c.Unavailable is null ? 28 : 0)
                    : c.Id == "start-game" ? 25 : c.Id == "training" ? 20 : c.Id == "home" ? 16 : 5, c.Unavailable is null)));
        _visibleCommands = hits.Select(hit => commands[hit.Id]).ToList();
        CommandResults.Items.Clear();
        foreach (var command in _visibleCommands)
        {
            var body = new StackPanel { Margin = new Thickness(6) };
            var title = new NativeTextBlock { Text = command.Label, FontSize = 16, TextWrapping = TextWrapping.Wrap };
            title.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.TextBrush"); body.Children.Add(title);
            string? reason = command.Unavailable;
            if (hits.First(h => h.Id == command.Id).Approximate) reason = "Approximate match" + (reason is null ? " · check the action before running" : " · " + reason);
            if (reason is not null)
            {
                var detail = new NativeTextBlock { Text = reason, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
                detail.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.SecondaryTextBrush"); body.Children.Add(detail);
            }
            var row=new Grid { MinHeight=52 };
            var selection=new OperatorChoiceSurface { Opacity=.18 };
            var content=new Grid { Margin=new Thickness(8,4,8,4) };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(32) });content.ColumnDefinitions.Add(new ColumnDefinition());
            var symbol=new OperatorSymbol { Kind=CommandSymbol(command.Id),Width=22,Height=22,VerticalAlignment=VerticalAlignment.Center };
            symbol.SetResourceReference(OperatorSymbol.ForegroundProperty,"ControlCentre.TextBrush");content.Children.Add(symbol);
            Grid.SetColumn(body,1);content.Children.Add(body);row.Children.Add(selection);row.Children.Add(content);
            var item = new ListBoxItem { Content = row, Tag=command.Id, Padding=new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            symbol.SetBinding(OperatorSymbol.IsActiveProperty,new Avalonia.Data.Binding(nameof(ListBoxItem.IsSelected)){Source=item});
            selection.Bind(OperatorChoiceSurface.ProgressProperty,item.GetObservable(OperatorChoiceMotion.ProgressProperty));
            OperatorChoiceMotion.SetEnabled(item,true);
            Avalonia.Automation.AutomationProperties.SetName(item, command.Label + (reason is null ? "" : ". " + reason));
            item.PointerReleased += (_, args) => { args.Handled = true; ExecuteCommand(command.Id); };
            CommandResults.Items.Add(item);
        }
        int retained = selectedId is null ? -1 : _visibleCommands.FindIndex(c => c.Id == selectedId);
        CommandResults.SelectedIndex = retained >= 0 ? retained : _visibleCommands.Count > 0 ? 0 : -1;
        CommandHint.Text = _visibleCommands.Count == 0 ? "No commands found. Try a mode, screen or setting name." : "↑ ↓ to choose · Enter to run · Esc to close";
    }

    private static string CommandSymbol(string id)=>id switch
    {
        "home"=>"system", "resume"=>"resume", "start-game"=>"launch", "training"=>"training",
        "scorecard"=>"scorecard", "bowler"=>"ball", "afl-match"=>"afl",
        "cricket-output" or "afl-output" or "enable-output"=>"display", _=>"sliders"
    };
    private void CommandResults_SelectionChanged(object sender,SelectionChangedEventArgs args)
    {
        if(!ReferenceEquals(args.Source,CommandResults))return;
        foreach(var item in CommandResults.Items.OfType<ListBoxItem>())OperatorChoiceMotion.SetSelected(item,ReferenceEquals(item,CommandResults.SelectedItem));
    }

    internal bool ExecuteCommand(string id)
    {
        if (CommandOverlay.IsVisible != true || _commandExecuting) return false;
        // Re-query live state: a remote command may have ended/replaced the match
        // while the operator searched. Never execute a stale availability snapshot.
        var command = Commands().FirstOrDefault(c => c.Id == id);
        if (CommandContextBlocked || command is null) { CloseCommandBar(); return false; }
        if (command.Unavailable is { } reason) { CommandHint.Text = reason; return false; }
        _commandExecuting = true;
        try
        {
            bool changesTab = CommandChangesOperatorTab(command.Id);
            CloseCommandBar(restoreFocus: !changesTab);
            command.Run();
            if (changesTab) FocusSelectedOperatorTab(command.Id);
            return true;
        }
        finally { _commandExecuting = false; }
    }

    private void SizeCommandBar()
    {
        double height = CommandOverlay.NativeHeight() > 0 ? CommandOverlay.NativeHeight() : Bounds.Height;
        CommandSurface.MaxHeight = Math.Max(160, height - 36);
        CommandResults.MaxHeight = Math.Max(60, height - 210);
    }
    private void CommandOverlay_SizeChanged(object? sender, SizeChangedEventArgs args)
    {
        if (CommandOverlay.IsVisible == true) SizeCommandBar();
    }

    private void CommandSearch_TextChanged(object? sender, TextChangedEventArgs args) => RefreshCommands();
    private void CommandClose_Click(object? sender, RoutedEventArgs args) => CloseCommandBar();
    private void CommandBackdrop_Click(object? sender, PointerEventArgs args) { args.Handled = true; CloseCommandBar(); }
    private void CommandOverlay_KeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape) { CloseCommandBar(); args.Handled = true; }
        else if (args.Key is Key.Up or Key.Down)
        {
            if (_visibleCommands.Count > 0)
            {
                CommandResults.SelectedIndex = Math.Clamp(CommandResults.SelectedIndex + (args.Key == Key.Down ? 1 : -1), 0, _visibleCommands.Count - 1);
                if (CommandResults.SelectedItem is { } selected) CommandResults.ScrollIntoView(selected);
            }
            args.Handled = true;
        }
        else if (args.Key == Key.Enter && NativeKeyboard.FocusedElement is not Avalonia.Controls.Button)
        {
            if (CommandResults.SelectedIndex >= 0 && CommandResults.SelectedIndex < _visibleCommands.Count) ExecuteCommand(_visibleCommands[CommandResults.SelectedIndex].Id);
            args.Handled = true;
        }
    }
}

// Generated from MainWindow.UnifiedSetup.cs; edit translator/adapters.
// Source SHA-256: 5fa1c048d41322b4feaa7be792235cd41023f2da677d226eeff20db2a875aca4
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
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private bool _setupFlowActive;
    private int _setupFlowGeneration;
    private SportMode? _setupSelectedSport;
    private bool _setupCompletionAuthorized;

    private void InitializeUnifiedSetup()
    {
        SetupWizardPanel.EnableUnifiedSetup();
        CricketSetupWizardPanel.EnableUnifiedSetup();
        SetupWizardPanel.SportChosen += ContinueUnifiedSetup;
        SetupWizardPanel.CancelSetupRequested += CancelUnifiedSetup;
        CricketSetupWizardPanel.CancelSetupRequested += CancelUnifiedSetup;
        SetupWizardPanel.ConfirmCompletion = () => AuthorizeSetupCompletion(SportMode.AFL);
        CricketSetupWizardPanel.ConfirmCompletion = () => AuthorizeSetupCompletion(SportMode.Cricket);
    }

    private void BeginUnifiedSetup()
    {
        if (CommandContextBlocked) return;
        if (_setupFlowActive) { ReturnToSportChoice(); return; }
        if (!SportSelectionPanel.IsVisible) GoHome();
        _setupFlowActive = true; ++_setupFlowGeneration;
        _setupCompletionAuthorized = false;
        _setupSelectedSport = null;
        SetupWizardPanel.Reset(); CricketSetupWizardPanel.Reset();
        ReturnToSportChoice();
    }

    private void ReturnToSportChoice()
    {
        if (!_setupFlowActive) return;
        _setupCompletionAuthorized = false; ++_setupFlowGeneration;
        SportMode selected = _setupSelectedSport ?? (_sportMode == SportMode.Cricket ? SportMode.Cricket : SportMode.AFL);
        _setupSelectedSport = null;
        CricketSetupWizardPanel.IsVisible = false;
        SportSelectionPanel.IsVisible = false;
        SetupWizardPanel.Opacity = 1;
        SetupWizardPanel.IsVisible = true;
        SetupWizardPanel.ShowSportChoice(selected);
    }

    private void ContinueUnifiedSetup(SportMode mode)
    {
        if (!_setupFlowActive || _setupSelectedSport is not null || mode is not (SportMode.AFL or SportMode.Cricket)) return;
        _setupSelectedSport = mode; ++_setupFlowGeneration;
        _setupCompletionAuthorized = false;
        if (mode == SportMode.AFL)
        {
            CricketSetupWizardPanel.IsVisible = false;
            SetupWizardPanel.IsVisible = true;
            SetupWizardPanel.ContinueWithAfl();
        }
        else
        {
            SetupWizardPanel.IsVisible = false;
            CricketSetupWizardPanel.Opacity = 1;
            CricketSetupWizardPanel.IsVisible = true;
            CricketSetupWizardPanel.ContinueWithCricket();
        }
    }

    private void CancelUnifiedSetup()
    {
        if (!_setupFlowActive) return;
        _setupFlowActive = false; ++_setupFlowGeneration;
        _setupSelectedSport = null; _setupCompletionAuthorized = false;
        SetupWizardPanel.IsVisible = CricketSetupWizardPanel.IsVisible = false;
        // Current match, its running clock and recovery slot were never changed.
        SportSelectionPanel.SetCurrentSession(HasCurrentSession ? CurrentSessionTitle() : null, CurrentSessionDetail());
        SportSelectionPanel.PrepareForDisplay();
        SportSelectionPanel.Opacity = 1; SportSelectionPanel.IsVisible = true;
        SportSelectionPanel.HomeStartGameFocus();
    }

    private bool AuthorizeSetupCompletion(SportMode mode)
    {
        if (!_setupFlowActive || _setupSelectedSport != mode ||
            (mode == SportMode.AFL ? !SetupWizardPanel.IsVisible : !CricketSetupWizardPanel.IsVisible)) return false;
        int generation = _setupFlowGeneration;
        var cricket = _cricketMatch; var training = _trainingSession; bool aflReady = _aflMatchReady;
        if (!ConfirmNewSession(mode)) return false;
        // Owned confirmations pump the dispatcher. A superseded setup or changed
        // session must not persist defaults or replace the new operator state.
        if (!_setupFlowActive || generation != _setupFlowGeneration || _setupSelectedSport != mode ||
            !ReferenceEquals(cricket, _cricketMatch) || !ReferenceEquals(training, _trainingSession) || aflReady != _aflMatchReady) return false;
        _setupCompletionAuthorized = true;
        return true;
    }

    private bool ConsumeSetupCompletion(SportMode mode, object result)
    {
        object? expected = mode == SportMode.AFL ? SetupWizardPanel.Result : CricketSetupWizardPanel.Result;
        if (!_setupFlowActive || !_setupCompletionAuthorized || _setupSelectedSport != mode || !ReferenceEquals(result, expected)) return false;
        _setupCompletionAuthorized = false; _setupFlowActive = false; _setupSelectedSport = null; ++_setupFlowGeneration;
        return true;
    }
}

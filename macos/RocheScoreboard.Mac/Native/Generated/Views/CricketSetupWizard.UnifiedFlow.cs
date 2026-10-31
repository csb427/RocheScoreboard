// Generated from Views/CricketSetupWizard.UnifiedFlow.cs; edit translator/adapters.
// Source SHA-256: ac65d803cb3f81d0c4312acf840d79af7037b6d1a6ad11c8067f1489a62dc1d3
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System;

namespace Roche_Scoreboard.Views;

public partial class CricketSetupWizard
{
    internal bool UnifiedSetupEnabled { get; private set; }
    internal Func<bool>? ConfirmCompletion { get; set; }
    private bool _completionPending;
    public event Action? CancelSetupRequested;
    internal void EnableUnifiedSetup()
    {
        UnifiedSetupEnabled = true;
        SportStepNavigation.IsVisible = CancelSetupButton.IsVisible = true;
    }
    internal void ContinueWithCricket() => ShowStep(1);
    private void GoToSportStep_Click(object? sender, RoutedEventArgs args) => BackToSportSelection?.Invoke();
    private void CancelSetup_Click(object? sender, RoutedEventArgs args) => CancelSetupRequested?.Invoke();
    private void UnifiedSetup_KeyDown(object? sender, Avalonia.Input.KeyEventArgs args)
    {
        if (UnifiedSetupEnabled) WizardFlowDesign.TryCancelOnEscape(this, args, () => CancelSetupRequested?.Invoke());
    }
    private void FinishWizard()
    {
        if (Result is not null || _completionPending || (ConfirmCompletion is not null && !IsVisible)) return;
        _completionPending = true;
        try { FinishWizardCore(); }
        finally { _completionPending = false; }
    }
}

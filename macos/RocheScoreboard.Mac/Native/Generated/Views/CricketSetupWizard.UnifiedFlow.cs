// Generated from Views/CricketSetupWizard.UnifiedFlow.cs; edit translator/adapters.
// Source SHA-256: 964e4f2c9551dd5c1c4d345992865761ad72d988378a1fb6e7c9066b1f525dc3
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
    internal Func<Task<bool>>? ConfirmCompletionAsync { get; set; }
    private bool _completionPending;
    private int _completionGeneration;
    public event Action? CancelSetupRequested;
    internal void EnableUnifiedSetup()
    {
        UnifiedSetupEnabled = true;
        SportStepNavigation.IsVisible = CancelSetupButton.IsVisible = true;
    }
    internal void ContinueWithCricket() => ShowStep(1, false);
    private void GoToSportStep_Click(object? sender, RoutedEventArgs args) => BackToSportSelection?.Invoke();
    private void CancelSetup_Click(object? sender, RoutedEventArgs args) => CancelSetupRequested?.Invoke();
    private void UnifiedSetup_KeyDown(object? sender, Avalonia.Input.KeyEventArgs args)
    {
        if (TryCancelPresetDragOnEscape(args)) return;
        if (UnifiedSetupEnabled) WizardFlowDesign.TryCancelOnEscape(this, args, () => CancelSetupRequested?.Invoke());
    }
    private async void FinishWizard()
    {
        if (Result is not null || _completionPending || ((ConfirmCompletion is not null || ConfirmCompletionAsync is not null) && !IsVisible)) return;
        _completionPending = true;
        int generation = _completionGeneration;
        try { await FinishWizardCoreAsync(generation); }
        finally { if (generation == _completionGeneration) _completionPending = false; }
    }
}

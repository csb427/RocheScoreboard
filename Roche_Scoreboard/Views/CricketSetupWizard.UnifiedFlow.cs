using System;
using System.Windows;

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
        SportStepNavigation.Visibility = CancelSetupButton.Visibility = Visibility.Visible;
    }
    internal void ContinueWithCricket() => ShowStep(1);
    private void GoToSportStep_Click(object sender, RoutedEventArgs args) => BackToSportSelection?.Invoke();
    private void CancelSetup_Click(object sender, RoutedEventArgs args) => CancelSetupRequested?.Invoke();
    private void UnifiedSetup_KeyDown(object sender, System.Windows.Input.KeyEventArgs args)
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

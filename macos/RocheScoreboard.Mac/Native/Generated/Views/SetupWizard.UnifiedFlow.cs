// Generated from Views/SetupWizard.UnifiedFlow.cs; edit translator/adapters.
// Source SHA-256: d8390e2b5500a20ca1e0f80247e59df75d210989e578a0661bdf84faf47e8915
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Views;

public partial class SetupWizard
{
    internal bool UnifiedSetupEnabled { get; private set; }
    internal Func<bool>? ConfirmCompletion { get; set; }
    internal Func<Task<bool>>? ConfirmCompletionAsync { get; set; }
    private bool _completionPending;
    private int _completionGeneration;
    private int _sportFocusGeneration;
    public event Action<SportMode>? SportChosen;
    public event Action? CancelSetupRequested;
    internal void EnableUnifiedSetup()
    {
        UnifiedSetupEnabled = true;
        SportStepNavigation.IsVisible = CancelSetupButton.IsVisible = true;
    }
    internal void ShowSportChoice(SportMode selected)
    {
        EnableUnifiedSetup();
        AflSportChoice.IsChecked = selected != SportMode.Cricket;
        CricketSportChoice.IsChecked = selected == SportMode.Cricket;
        ShowStep(0, false);
        SharedSurfaceMotion.Reveal(SportStepContainer);
        int generation = ++_sportFocusGeneration;
        Dispatcher.UIThread.Post(new Action(() =>
        {
            if (generation != _sportFocusGeneration || _currentStep != 0 || !IsVisible) return;
            var choice = CricketSportChoice.IsChecked == true ? CricketSportChoice : AflSportChoice;
            choice.Focus();
            WizardViewport.RevealOperatorTarget(choice, () => generation == _sportFocusGeneration && _currentStep == 0 && IsVisible);
        }));
    }
    internal void ContinueWithAfl() => ShowStep(1, false);
    private void GoToSportStep_Click(object? sender, RoutedEventArgs args) => BackToSportSelection?.Invoke();
    private void CancelSetup_Click(object? sender, RoutedEventArgs args) => CancelSetupRequested?.Invoke();
    // Listen to both checked and unchecked: native frameworks can notify the
    // arriving choice before retiring its sibling. The final state must drive
    // the counter and step descriptions, including rapid reversals.
    private void SportChoice_Changed(object? sender, RoutedEventArgs args) => RefreshSportChoiceChrome();
    private void RefreshSportChoiceChrome()
    {
        if (WizardStepCounter is null || Step2Label is null) return;
        bool cricket = _currentStep == 0 && CricketSportChoice?.IsChecked == true;
        Step2Label.Text = cricket ? "Players" : "Clock"; Step2Symbol.Kind = cricket ? "players" : "clock";
        Step3Label.Text = cricket ? "Format" : "Options"; Step3Symbol.Kind = cricket ? "format" : "settings";
        SportOptionsPreview.IsVisible = cricket ? true : false;
        if (_currentStep == 0) WizardStepCounter.Text = cricket ? "1 of 5" : "1 of 4";
    }
    private bool ValidateQuarterEntry()
    {
        if (int.TryParse((SetupQuarterMinutes.Text ?? string.Empty), out int minutes) && minutes is >= 1 and <= 60 &&
            int.TryParse((SetupQuarterSeconds.Text ?? string.Empty), out int seconds) && seconds is >= 0 and <= 59)
        { SetupValidationMessage.IsVisible = false; return true; }
        SetupValidationMessage.Text = "Enter 1–60 minutes and 0–59 seconds for the quarter duration.";
        SetupValidationMessage.IsVisible = true;
        ShowStep(2, false);
        var field = !int.TryParse((SetupQuarterMinutes.Text ?? string.Empty), out minutes) || minutes is < 1 or > 60
            ? SetupQuarterMinutes : SetupQuarterSeconds;
        field.Focus(); field.BringIntoView(); field.SelectAll();
        return false;
    }
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

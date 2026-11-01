using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    // Optional native WPF dispatcher checks in the existing explicit QA entry
    // point. Compiling this file on Linux does not execute these GUI checks.
    private async Task<Dictionary<string, bool>> RunUnifiedSetupChecksAsync()
    {
        var checks = new Dictionary<string, bool>();
        var fixture = new MainWindow(); fixture.Show();
        try
        {
            await Task.Delay(120);
            fixture.BeginUnifiedSetup();
            checks["unified-start-opens-sport-step-without-a-second-home"] = !fixture.SportSelectionPanel.IsVisible && fixture.SetupWizardPanel.SportStepContainer.IsVisible;
            fixture.ContinueUnifiedSetup(SportMode.AFL);
            fixture.SetupWizardPanel.SetupHomeNameBox.Text = "Retained AFL draft";
            fixture.ReturnToSportChoice(); fixture.ContinueUnifiedSetup(SportMode.Cricket);
            checks["unified-cricket-does-not-read-the-afl-draft"] = fixture.CricketSetupWizardPanel.SetupHomeNameBox.Text != "Retained AFL draft";
            fixture.ReturnToSportChoice(); fixture.ContinueUnifiedSetup(SportMode.AFL);
            checks["unified-back-retains-the-afl-draft"] = fixture.SetupWizardPanel.SetupHomeNameBox.Text == "Retained AFL draft";
            string preferences = JsonSerializer.Serialize(OperatorWorkflowPreferences.Load());
            fixture.CancelUnifiedSetup();
            checks["unified-cancel-has-one-home-and-no-game-output"] = fixture.SportSelectionPanel.IsVisible && !fixture.SetupWizardPanel.IsVisible
                && !fixture.CricketSetupWizardPanel.IsVisible && fixture.OutputWindows().All(w => !w.IsVisible);
            typeof(SetupWizard).GetMethod("FinishWizard", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fixture.SetupWizardPanel, null);
            checks["unified-cancel-invalidates-retained-completion"] = fixture.SetupWizardPanel.Result is null && !fixture._aflMatchReady;
            checks["unified-rejected-completion-does-not-persist-settings"] = preferences == JsonSerializer.Serialize(OperatorWorkflowPreferences.Load());
        }
        finally { fixture.Close(); }
        return checks;
    }
}

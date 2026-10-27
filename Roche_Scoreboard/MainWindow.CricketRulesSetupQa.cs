using System.IO;
using System.Windows;
using System.Windows.Controls;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Button=System.Windows.Controls.Button;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private async Task<Dictionary<string,bool>> RunCricketRulesSetupChecksAsync(string directory)
    {
        var checks=new Dictionary<string,bool>();
        var window=new Window {Width=1000,Height=760,ShowInTaskbar=false};window.Show();
        var wizard=new CricketSetupWizard();window.Content=wizard;
        try
        {
            await Task.Delay(1000);
            checks["setup-delivery-rules-default-to-replacement-balls-and-no-free-hit"] = wizard.ReplaceWideBallsToggle.IsChecked==true && wizard.ReplaceNoBallsToggle.IsChecked==true && wizard.FreeHitsToggle.IsChecked==false;
            InvokeCricketQa(wizard,"ShowStep",3,false);
            foreach(var size in new[]{(1000d,760d),(420d,520d)})
            {
                window.Width=size.Item1;window.Height=size.Item2;await Task.Delay(120);
                var viewport=FindQaVisual<WizardViewport>(wizard)!;viewport.ScrollToBottom();await Task.Delay(80);
                checks[$"setup-delivery-rules-and-navigation-reachable-{size.Item1}"] = CricketQaBoundsWithin(wizard.FreeHitsToggle,viewport) && CricketQaBoundsWithin(wizard.NavBar,wizard) && viewport.ScrollableWidth<1;
                CaptureCricketQa(wizard,Path.Combine(directory,$"setup-delivery-rules-{size.Item1}.png"));
            }
            wizard.ReplaceWideBallsToggle.IsChecked=false;wizard.ReplaceNoBallsToggle.IsChecked=false;wizard.FreeHitsToggle.IsChecked=true;
            InvokeCricketQa(wizard,"FinishWizard");
            checks["setup-result-carries-selected-delivery-rules"] = wizard.Result is {ReplaceWideBalls:false,ReplaceNoBalls:false,FreeHitsEnabled:true};
            if(_cricketMatch is null && wizard.Result is {} setup)
            {
                FinalizeCricketSetup(setup,promptForInitialSelection:false);
                checks["starting-cricket-match-copies-rules-into-engine"] = _cricketMatch is {ReplaceWideBalls:false,ReplaceNoBalls:false,FreeHitsEnabled:true};
                OnCricketResetRequested();
            }
            wizard.Reset();
            checks["reset-setup-restores-standard-delivery-rule-defaults"] = wizard.ReplaceWideBallsToggle.IsChecked==true && wizard.ReplaceNoBallsToggle.IsChecked==true && wizard.FreeHitsToggle.IsChecked==false;

            var fixture=CricketReliabilityChecks.CreateMatch(true);fixture.FreeHitsEnabled=true;fixture.RecordDelivery(1,CricketDeliveryType.NoBall);
            var panel=new CricketControlPanel();panel.SetMatch(fixture);window.Width=1000;window.Height=760;window.Content=panel;await Task.Delay(120);
            checks["operator-scorer-shows-pending-free-hit"] = panel.OperatorFreeHitText.IsVisible && fixture.FreeHitPending;
            CaptureCricketQa(panel,Path.Combine(directory,"operator-free-hit-pending.png"));panel.ClearMatch();
            var entry=new CricketDeliveryDialog(fixture);window.Content=entry;await Task.Delay(120);
            var choices=entry.DismissalTiles.Children.OfType<Button>().ToArray();
            checks["free-hit-wicket-entry-disables-protected-dismissals-and-explains-rule"] = choices[0].IsEnabled==false && choices[1].IsEnabled==false && choices[3].IsEnabled && entry.FreeHitEntryHint.IsVisible && entry.DismissalChoice.SelectedIndex==3;
            CaptureCricketQa(entry,Path.Combine(directory,"free-hit-wicket-entry.png"));
            int count=fixture.CurrentInnings!.Deliveries.Count;
            entry.DismissalChoice.SelectedIndex=0;InvokeCricketQa(entry,"Apply_Click",entry.ApplyButton,new RoutedEventArgs());
            checks["free-hit-wicket-rejection-leaves-delivery-and-pending-rule-intact"] = fixture.CurrentInnings.Deliveries.Count==count && fixture.FreeHitPending && entry.ErrorText.Text.Length>0 && !entry.ApplyButton.IsEnabled;
            entry.DismissalChoice.SelectedIndex=3;InvokeCricketQa(entry,"Apply_Click",entry.ApplyButton,new RoutedEventArgs());
            checks["free-hit-runout-can-be-recorded-once-and-consumes-legal-free-hit"] = fixture.TotalWickets==1 && fixture.CurrentInnings.Deliveries.Count==count+1 && !fixture.FreeHitPending;
            entry.Cancel();
        }
        finally {window.Close();}
        return checks;
    }
}

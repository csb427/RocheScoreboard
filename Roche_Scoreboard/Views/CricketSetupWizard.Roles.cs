using System.Windows;
using System.Windows.Input;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Views;

public partial class CricketSetupWizard
{
    private void HomeKeeper_Click(object sender, RoutedEventArgs e) => ToggleHomeWicketkeeper_Click(sender, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left));
    private void AwayKeeper_Click(object sender, RoutedEventArgs e) => ToggleAwayWicketkeeper_Click(sender, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left));
    private void HomeCaptain_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CricketPlayer player }) return;
        bool next = !player.IsCaptain; foreach (var member in _homePlayers) member.IsCaptain = false; player.IsCaptain = next; 
    }
    private void AwayCaptain_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CricketPlayer player }) return;
        bool next = !player.IsCaptain; foreach (var member in _awayPlayers) member.IsCaptain = false; player.IsCaptain = next; 
    }
}

// Generated from Views/CricketSetupWizard.Roles.cs; edit translator/adapters.
// Source SHA-256: c6dc8cd6c394e661eb17aae5c0d856a5c9f75bdf1607f205cdc8a577dd5d9568
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Views;

public partial class CricketSetupWizard
{
    private void HomeKeeper_Click(object? sender, RoutedEventArgs e) => ToggleHomeWicketkeeper_Click(sender, e);
    private void AwayKeeper_Click(object? sender, RoutedEventArgs e) => ToggleAwayWicketkeeper_Click(sender, e);
    private void HomeCaptain_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: CricketPlayer player }) return;
        bool next = !player.IsCaptain; foreach (var member in _homePlayers) member.IsCaptain = false; player.IsCaptain = next; 
    }
    private void AwayCaptain_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: CricketPlayer player }) return;
        bool next = !player.IsCaptain; foreach (var member in _awayPlayers) member.IsCaptain = false; player.IsCaptain = next; 
    }
}

using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Window = System.Windows.Window;

namespace Roche_Scoreboard.Views;

public partial class ResumeGameDialog : Window
{
    private readonly DispatcherTimer _discardConfirmationTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _discardArmed;
    public bool ResumeChosen { get; private set; }
    public bool DiscardChosen { get; private set; }
    public bool DeferredChosen { get; private set; }

    internal ResumeGameDialog(GameState saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        InitializeComponent(); InteractionMotion.Enable(this);
        SharedSurfaceMotion.Attach(DialogShell);
        MaxHeight = Math.Max(310, SystemParameters.WorkArea.Height - 32);
        MaxWidth = Math.Max(340, SystemParameters.WorkArea.Width - 32);
        Height = Math.Min(Height, MaxHeight); Width = Math.Min(Width, MaxWidth);
        bool cricket = saved.Sport?.Equals("cricket", StringComparison.OrdinalIgnoreCase) == true && saved.CricketMatchState != null;
        HomeNameText.Text = cricket ? saved.CricketMatchState!.TeamAName : saved.MatchState?.HomeName ?? "HOME";
        AwayNameText.Text = cricket ? saved.CricketMatchState!.TeamBName : saved.MatchState?.AwayName ?? "AWAY";
        PhaseText.Text = cricket ? $"CRICKET · INNINGS {Math.Max(1, saved.CricketMatchState!.CurrentInningsNumber)}"
            : $"AFL · QUARTER {Math.Max(1, saved.MatchState?.Quarter ?? 1)}";
        string? homeLogo = cricket ? saved.CricketMatchState!.TeamALogoPath : saved.HomeLogoPath;
        string? awayLogo = cricket ? saved.CricketMatchState!.TeamBLogoPath : saved.AwayLogoPath;
        HomeLogoImage.Source = TeamVisuals.LoadLogo(homeLogo); AwayLogoImage.Source = TeamVisuals.LoadLogo(awayLogo);
        string home = cricket ? saved.CricketMatchState!.TeamAPrimaryColor : saved.HomePrimaryColor ?? "#4488FF";
        string away = cricket ? saved.CricketMatchState!.TeamBPrimaryColor : saved.AwayPrimaryColor ?? "#FF6644";
        HomeTeamCard.Background = TeamVisuals.Panel(home, home, .35); HomeTeamCard.BorderBrush = TeamVisuals.Accent(home);
        AwayTeamCard.Background = TeamVisuals.Panel(away, away, .35); AwayTeamCard.BorderBrush = TeamVisuals.Accent(away);
        _discardConfirmationTimer.Tick += (_,_) => ResetDiscardConfirmation();
        Closed += (_,_) => _discardConfirmationTimer.Stop();
    }
    private void Resume_Click(object sender, RoutedEventArgs args) { ResumeChosen = true; DialogResult = true; }
    private void KeepSaved_Click(object sender, RoutedEventArgs args) { DeferredChosen = true; ResetDiscardConfirmation(); Close(); }
    private void Discard_Click(object sender, RoutedEventArgs args)
    {
        if (!_discardArmed)
        {
            _discardArmed = true; DiscardButton.Content = "CONFIRM DISCARD";
            _discardConfirmationTimer.Stop(); _discardConfirmationTimer.Start(); return;
        }
        DiscardChosen = true; DialogResult = false;
    }
    private void ResetDiscardConfirmation()
    {
        _discardConfirmationTimer.Stop(); _discardArmed = false;
        DiscardButton.Content = "DISCARD SAVE";
    }
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs args)
    {
        if (args.Key != Key.Escape) return;
        args.Handled = true; ResetDiscardConfirmation(); Close();
    }
}

using RocheScoreboard.Mac.Production;
// Generated from Views/ResumeGameDialog.xaml.cs; edit translator/adapters.
// Source SHA-256: 15e6cff730f0daf4e04a43129ccec5f23f2d2fbb2dab7ed75ee5d3e6b3dceeb6
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Input;
using RocheScoreboard.Mac.Native;
using System;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Window = RocheScoreboard.Mac.Native.NativeWindow;

namespace Roche_Scoreboard.Views;

public partial class ResumeGameDialog : NativeWindow
{
    private readonly DispatcherTimer _discardConfirmationTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _discardArmed;
    public bool ResumeChosen { get; private set; }
    public bool DiscardChosen { get; private set; }
    public bool DeferredChosen { get; private set; }

    internal ResumeGameDialog(GameState saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        NativePointer.EnsureInitialized(); ProductionNames.BeginLoad(this); try { InitializeComponent(); } finally { ProductionNames.CompleteLoad(this); } InteractionMotion.Enable(this);
        SharedSurfaceMotion.Attach(DialogShell);
        MaxHeight = Math.Max(310, NativeDesktop.WorkArea.Height - 32);
        MaxWidth = Math.Max(340, NativeDesktop.WorkArea.Width - 32);
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
    private void Resume_Click(object? sender, RoutedEventArgs args) { ResumeChosen = true; DialogResult = true; }
    private void KeepSaved_Click(object? sender, RoutedEventArgs args) { DeferredChosen = true; ResetDiscardConfirmation(); Close(); }
    private void Discard_Click(object? sender, RoutedEventArgs args)
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
    private void Window_PreviewKeyDown(object? sender, Avalonia.Input.KeyEventArgs args)
    {
        if (args.Key != Key.Escape) return;
        args.Handled = true; ResetDiscardConfirmation(); Close();
    }
}

public partial class ResumeGameDialog
{
    private void Native_Window_PreviewKeyDown(object? sender, KeyEventArgs e) { Window_PreviewKeyDown(sender!, e); }
}

using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using UserControl = System.Windows.Controls.UserControl;

namespace Roche_Scoreboard.Views;

public partial class ResumeMatchPrompt : UserControl
{
    private readonly DispatcherTimer _discardConfirmationTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _discardArmed;
    public event Action? Completed;
    private bool _completed;
    private bool _completionRaised;
    private void Complete()
    {
        if (_completionRaised) return;
        _completionRaised = true;
        BeginAnimation(OpacityProperty, null);
        Completed?.Invoke();
    }
    private void Finish()
    {
        if (_completed) return;
        _completed = true; _discardConfirmationTimer.Stop(); IsHitTestVisible = false;
        DiscardConfirmationProgress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
        if (!OperatorMotion.Enabled || !IsLoaded || !IsVisible) { Complete(); return; }
        var scale = new ScaleTransform(1, 1);
        var travel = new TranslateTransform();
        var transform = new TransformGroup(); transform.Children.Add(RenderTransform); transform.Children.Add(scale); transform.Children.Add(travel);
        RenderTransformOrigin = new System.Windows.Point(.5, .5); RenderTransform = transform;
        var duration = TimeSpan.FromMilliseconds(150);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.97, duration) { EasingFunction = OperatorMotion.SmoothOut });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.97, duration) { EasingFunction = OperatorMotion.SmoothOut });
        travel.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, duration) { EasingFunction = OperatorMotion.SmoothOut });
        var fade = new DoubleAnimation(Opacity, 0, duration) { EasingFunction = OperatorMotion.SmoothOut };
        fade.Completed += (_, _) => Complete();
        BeginAnimation(OpacityProperty, fade);
    }
    public bool ResumeChosen { get; private set; }
    public bool DiscardChosen { get; private set; }
    public bool DeferredChosen { get; private set; }

    internal ResumeMatchPrompt(GameState saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        InitializeComponent(); InteractionMotion.Enable(this);
        SharedSurfaceMotion.Attach(DialogShell);
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
        Unloaded += (_,_) => { ResetDiscardConfirmation(); if (_completed) Complete(); };
    }
    private void Prompt_SizeChanged(object sender,SizeChangedEventArgs args)
    {
        if(DialogShell is null || ResumeButton is null) return;
        bool compact=args.NewSize.Height<320;
        DialogShell.Padding=new Thickness(compact ? 12 : 20);
        PromptHeading.FontSize=compact ? 20 : 24; PhaseText.FontSize=compact ? 12 : 14;
        HomeNameText.FontSize=AwayNameText.FontSize=compact ? 18 : 21;
        HomeLogoImage.Height=AwayLogoImage.Height=compact ? 24 : 38;
        HomeTeamCard.Margin=AwayTeamCard.Margin=new Thickness(0,0,0,compact ? 4 : 8);
        PhaseText.Margin=compact ? new Thickness(0,4,0,8) : new Thickness(0,8,0,12);
        HomeTeamCard.Padding=AwayTeamCard.Padding=new Thickness(compact ? 6 : 12);
        ResumeButton.Height=LaterButton.Height=compact ? 36 : 46; DiscardButton.Height=compact ? 32 : 38;
    }
    private void Resume_Click(object sender, RoutedEventArgs args) { if (_completed) return; ResumeChosen = true; Finish(); }
    private void KeepSaved_Click(object sender, RoutedEventArgs args) { if (_completed) return; DeferredChosen = true; ResetDiscardConfirmation(); Finish(); }
    private void Discard_Click(object sender, RoutedEventArgs args)
    {
        if (_completed) return;
        if (!_discardArmed)
        {
            _discardArmed = true; DiscardButton.Content = "CONFIRM DISCARD";
            DiscardButton.Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(94,29,43));
            DiscardButton.BorderBrush=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255,155,163));
            DiscardConfirmationProgress.Visibility = Visibility.Visible;
            DiscardConfirmationProgress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,
                new DoubleAnimation(1, 0, TimeSpan.FromSeconds(5)) { FillBehavior = FillBehavior.Stop });
            System.Windows.Automation.AutomationProperties.SetHelpText(DiscardButton, "Press again within five seconds to discard the saved match.");
            _discardConfirmationTimer.Stop(); _discardConfirmationTimer.Start(); return;
        }
        DiscardChosen = true; Finish();
    }
    private void ResetDiscardConfirmation()
    {
        _discardConfirmationTimer.Stop(); _discardArmed = false;
        DiscardConfirmationProgress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
        DiscardConfirmationProgress.Visibility = Visibility.Hidden;
        System.Windows.Automation.AutomationProperties.SetHelpText(DiscardButton, "Discard the saved match. Confirmation is required.");
        DiscardButton.Content = "DISCARD SAVE";
        DiscardButton.Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(39,25,31));
        DiscardButton.BorderBrush=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(125,70,82));
    }
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs args)
    {
        if (args.Key != Key.Escape) return;
        args.Handled = true;
        if (_completed) return;
        DeferredChosen = true; ResetDiscardConfirmation(); Finish();
    }
}

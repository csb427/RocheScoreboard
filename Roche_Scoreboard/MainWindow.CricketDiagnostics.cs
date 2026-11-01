#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TabControl = System.Windows.Controls.TabControl;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private Window? _cricketDiagnostics;
    private void CricketDiagnostics_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || CommandOverlay.Visibility == Visibility.Visible || _recoveryPromptActive) return;
        if (e.Key != Key.D || Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Alt) || _cricketScorebug is null || _cricketMatch is null) return;
        e.Handled = true;
        OpenCricketDiagnostics();
    }
    private void OpenCricketDiagnostics()
    {
        if (_cricketDiagnostics is not null) { _cricketDiagnostics.Activate(); return; }
        var layout=new Grid {Margin=new Thickness(18)};
        layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});layout.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var intro=new StackPanel();intro.Children.Add(new TextBlock { Text = "CRICKET PREVIEW TOOLS", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
        intro.Children.Add(new TextBlock { Text = "Practice scores are isolated. Restore your match below or close these tools to return.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 8, 0, 14) });layout.Children.Add(intro);
        var tabs=new TabControl {Style=(Style)CricketControlPanelView.FindResource("OperatorTabs"),ItemContainerStyle=(Style)CricketControlPanelView.FindResource("PanelTab"),Tag="cricket-diagnostics-categories"};Grid.SetRow(tabs,1);layout.Children.Add(tabs);
        StackPanel Page(string label)
        {var page=new StackPanel {Margin=new Thickness(4,12,4,4)};tabs.Items.Add(new TabItem {Header=label,Tag=label=="SCENARIOS"?"MATCH SITUATIONS":label=="ANIMATIONS"?"SCORE CELEBRATIONS":"LIVE INFORMATION",Style=(Style)CricketControlPanelView.FindResource("PanelTab"),Content=new ScrollViewer {Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}});return page;}
        var scenarios=Page("SCENARIOS");var animations=Page("ANIMATIONS");var context=Page("CONTEXT");
        var status = new TextBlock { Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
        void Add(StackPanel page,string label, Action action)
        {
            var button = new Button { Content = label, ContentTemplate=Views.CricketPrompt.ButtonContent, Style=(Style)CricketControlPanelView.FindResource("ToolBtn"), MinHeight = 44,Height=double.NaN,FontSize=14, Margin = new Thickness(0, 0, 0, 8), Background = new SolidColorBrush(Color.FromRgb(30, 54, 74)), Foreground = Brushes.White, BorderBrush = Brushes.SlateGray, FontWeight = FontWeights.Bold };
            button.Click += (_, _) => { if (_cricketScorebug is null) { status.Text = "Open a cricket match first."; return; } action(); };
            page.Children.Add(button);
        }
        void Heading(string text) => scenarios.Children.Add(new TextBlock {Text=text,FontSize=12,FontWeight=FontWeights.Bold,Foreground=Brushes.LightSkyBlue,Margin=new Thickness(0,14,0,10)});
        Add(context,"Bowler economy + extras", () => status.Text = _cricketScorebug!.PreviewBowlerDetails() ? "Shown for 16 seconds; a delivery restores the balls immediately." : "Select a bowler and bowl a legal delivery first. Cancel any overlay or celebration.");
        Add(context,"Next rail context", () => { _cricketScorebug!.PreviewNextContext(); status.Text = "Advanced to the next context supported by this match format."; });
        Add(context,"Next break presentation",()=>{CricketControlPanelView.PreviewNextBreakPresentation();status.Text="Advanced if a break and automatic break rotation are active.";});
        foreach (var moment in new[] { CricketDeliveryType.Four, CricketDeliveryType.Six, CricketDeliveryType.Wicket })
            Add(animations,"Preview " + moment.ToString().ToUpperInvariant(), () => _cricketScorebug!.PreviewMoment(moment));
        Add(animations,"Preview 50", () => _cricketScorebug!.PreviewMilestone(50));
        Add(animations,"Preview 100", () => _cricketScorebug!.PreviewMilestone(100));
        Add(animations,"Stop celebration", () => _cricketScorebug!.StopPreviewMoment());
        string lastCategory="";
        foreach (var scenario in new[] { ("ready", "Opening over · ready to score","OPENING & FIRST INNINGS"), ("middle", "Middle overs · 4/95 after 12","OPENING & FIRST INNINGS"), ("extras", "Extras · six deliveries, four legal balls","OPENING & FIRST INNINGS"), ("collapse", "Recovery test · eight wickets down","OPENING & FIRST INNINGS"), ("large", "High scoring · 3/248","OPENING & FIRST INNINGS"), ("firstbreak", "Innings interval · 6/171","OPENING & FIRST INNINGS"), ("second", "Second innings · opening chase","CHASES & FINISHES"), ("chase", "Chase · 3/118 after 15 overs","CHASES & FINISHES"), ("lastover", "Final over · 8 needed from 6 balls","CHASES & FINISHES"), ("target", "Winning six · completion choices","CHASES & FINISHES"), ("third", "Third innings · building a lead","MULTI-INNINGS & INTERRUPTIONS"), ("fourth", "Follow-on · fourth innings chase","MULTI-INNINGS & INTERRUPTIONS"), ("rain", "Rain delay · paused after 12 overs","MULTI-INNINGS & INTERRUPTIONS") })
        {
            if(lastCategory!=scenario.Item3) {Heading(scenario.Item3);lastCategory=scenario.Item3;}
            var button = new Button { Content = scenario.Item2, ContentTemplate=Views.CricketPrompt.ButtonContent,Style=(Style)CricketControlPanelView.FindResource("ToolBtn"),MinHeight = 44,Height=double.NaN,FontSize=14, Margin = new Thickness(0, 0, 0, 8), Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(30, 54, 74)) };
            button.Click += async (_, _) =>
            {
                button.IsEnabled=false;status.Text="Preparing practice scenario…";
                try {if (await LoadCricketPreviewAsync(scenario.Item1) && _cricketMatch?.CurrentInnings is { } innings)
                {
                    string chase = _cricketMatch.RunsRequired is int needed && needed > 0 ? $" · {needed} needed" : "";
                    status.Text = $"PRACTICE · {innings.BattingTeamName} {innings.TotalWickets}/{innings.TotalRuns} · {innings.OversDisplay} overs{chase}. Close this window to restore your match.";
                } else status.Text="Practice scenario was not changed.";} finally {button.IsEnabled=true;}
            };
            scenarios.Children.Add(button);
        }
        var recovery=new StackPanel();Grid.SetRow(recovery,2);layout.Children.Add(recovery);
        Add(recovery,"Restore original match", () => { RestoreCricketPreview(); status.Text = "Original match restored. Normal saving has resumed."; });recovery.Children.Add(status);
        var shell=new Grid();shell.Children.Add(layout);
        _cricketDiagnostics = new Window { Owner = this, Title = "Cricket developer previews", Width = 510, Height = 740,MinWidth=360,MinHeight=360, Content = shell, Background = new SolidColorBrush(Color.FromRgb(12, 18, 26)), WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        _cricketDiagnostics.Closed += (_, _) => { RestoreCricketPreview(); _cricketDiagnostics = null; };
        _cricketDiagnostics.Show();
    }
}
#endif

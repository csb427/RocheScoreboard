using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Roche_Scoreboard.Views;

public partial class CricketControlPanel
{
    private async Task<string?> InningsContextDecisionAsync(string title,string message,CricketMatchManager match,CricketInnings innings,params CricketPrompt.Choice[] choices)
    {
        if(_workflowBusy)return null;
        _workflowBusy=true;int generation=_workflowGeneration;
        var source=Keyboard.FocusedElement as FrameworkElement;
        if(source is not {IsLoaded:true,IsVisible:true} || !CricketRoot.IsAncestorOf(source))source=MatchOptionsButton;
        try
        {
            var answer=await CricketPrompt.ChooseWithContextAsync(CricketRoot,title,message,CreateInningsDecisionSnapshot(match,innings),source,choices);
            return generation==_workflowGeneration?answer:null;
        }
        finally{ReleaseWorkflow(generation);}
    }
    private static FrameworkElement CreateInningsDecisionSnapshot(CricketMatchManager match,CricketInnings innings)
    {
        string colour=innings.BattingTeamName==match.TeamAName?match.TeamAPrimaryColor:match.TeamBPrimaryColor;
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        for(int i=0;i<3;i++)grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        TextBlock Label(string text,int row,double size)
        {
            var label=new TextBlock{Text=text,FontSize=size,Margin=new Thickness(0,2,12,2),TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=text};
            label.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");Grid.SetRow(label,row);grid.Children.Add(label);return label;
        }
        Label($"INNINGS {innings.InningsNumber} · AT OPENING",0,12).Opacity=.8;
        Label(innings.BattingTeamName,1,17).FontWeight=FontWeights.SemiBold;
        Label(innings.OversDisplay+" overs · "+(innings.IsComplete?"Closed":"In progress"),2,13).Opacity=.85;
        if(match.Format==CricketFormat.LimitedOvers && !innings.IsComplete && !innings.IgnoreOverLimit
            && match.CurrentOverLimit>0 && innings.LegalBallsBowled<match.CurrentOverLimit*6)
        {
            int available=match.CurrentOverLimit*6-innings.LegalBallsBowled;
            grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
            Label($"{available/6}.{available%6} overs available",3,13).Opacity=.85;
        }
        var score=new TextBlock{Tag="innings-decision-score",Text=$"{innings.TotalWickets}/{innings.TotalRuns}",FontSize=32,FontWeight=FontWeights.Bold,MinWidth=70,TextAlignment=TextAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
        score.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");
        score.ToolTip=$"{innings.TotalRuns} runs, {innings.TotalWickets} wickets";
        System.Windows.Automation.AutomationProperties.SetName(score,$"{innings.TotalRuns} runs, {innings.TotalWickets} wickets");
        System.Windows.Documents.Typography.SetNumeralAlignment(score,FontNumeralAlignment.Tabular);
        Grid.SetColumn(score,1);Grid.SetRowSpan(score,grid.RowDefinitions.Count);grid.Children.Add(score);
        return new Border{Tag="innings-decision-context",Child=grid,CornerRadius=new CornerRadius(18),Padding=new Thickness(14),Background=TeamVisuals.Panel(colour,colour,.2),BorderBrush=TeamVisuals.Accent(colour),BorderThickness=new Thickness(1)};
    }
}

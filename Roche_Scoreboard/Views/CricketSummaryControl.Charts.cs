using System.Windows;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
namespace Roche_Scoreboard.Views;
public partial class CricketSummaryControl
{
    public void ShowChart(CricketMatchManager match,string mode)
    {if(mode is not ("worm" or "manhattan") || match.CurrentInnings is null)return;SetPagedMode(match,mode);PopulateChart(match,mode);ShowSummary();}
    public void RefreshChart(CricketMatchManager match,string mode)
    {if(!IsVisible || match.CurrentInnings is null)return;SetPagedMode(match,mode);PopulateChart(match,mode);}
    private void PopulateChart(CricketMatchManager match,string mode)
    {
        SummaryHeader(match,"BATTING",match.BattingTeamName,match.BattingTeamPrimaryColor);
        HeaderText.Text=mode=="worm"?"SCOREWORM":"MANHATTAN";
        SummaryColumnPanel.Visibility=Visibility.Collapsed;RootGrid.RowDefinitions[1].Height=new GridLength(0);
        RowsPanel.Children.Clear();RowsPanel.Rows=1;
        RowsPanel.Children.Add(new CricketChartVisual(CricketChartData.Build(match),mode=="manhattan",_pageIndex));
    }
}

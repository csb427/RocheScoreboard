// Generated from Views/CricketSummaryControl.Charts.cs; edit translator/adapters.
// Source SHA-256: bf2d42c65604844decd4372a1d42ee79c98b63ff49ef99b4a6dd27040f3612c6
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
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
        SummaryColumnPanel.IsVisible=false;RootGrid.RowDefinitions[1].Height=new GridLength(0);
        RowsPanel.Children.Clear();RowsPanel.Rows=1;
        RowsPanel.Children.Add(new CricketChartVisual(CricketChartData.Build(match),mode=="manhattan",_pageIndex));
    }
}

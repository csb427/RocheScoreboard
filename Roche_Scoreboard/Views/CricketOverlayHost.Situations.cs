using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using Brush = System.Windows.Media.Brush;
using Grid = System.Windows.Controls.Grid;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Roche_Scoreboard.Views;

public sealed partial class CricketOverlayHost
{
    // Keep figures steady while the existing scorebug handoff animates the shell.
    // Each family has its own hierarchy; no foreground blur or looping motion.
    private bool RenderSituation(CricketOverlayFact fact, Brush emphasis)
    {
        if(fact.Kind is not (CricketOverlayKind.Partnership or CricketOverlayKind.MatchState or CricketOverlayKind.InningsRecap))return false;
        bool recap=fact.Kind==CricketOverlayKind.InningsRecap;
        _foreground.ColumnDefinitions.Add(new(){Width=new GridLength(recap?.68:1,GridUnitType.Star)});
        _foreground.ColumnDefinitions.Add(new(){Width=new GridLength(recap?3.32:2.2,GridUnitType.Star)});
        var title=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,16,0),Tag="situation-"+fact.Kind};
        Viewbox Fit(string text,double size,Brush? ink=null,string? tag=null)
        {
            var line=Text(size);line.Text=text;line.TextAlignment=TextAlignment.Center;
            if(ink is not null)line.Foreground=ink;
            return new Viewbox{Child=line,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,Tag=tag};
        }
        title.Children.Add(Fit(recap?"INNINGS\nRECAP":fact.Title,recap?48:34));
        if(fact.Kind==CricketOverlayKind.Partnership)
        {
            title.Children.Add(Fit("RUNS (BALLS)",28));_foreground.Children.Add(title);
            var figure=Fit(fact.Metrics[0].Value,120,emphasis,"situation-value");
            SetColumn(figure,1);_foreground.Children.Add(figure);return true;
        }
        if(!recap)title.Children.Add(Fit(fact.Detail,25));
        _foreground.Children.Add(title);
        var columns=new System.Windows.Controls.Primitives.UniformGrid{Columns=Math.Max(1,fact.Metrics.Count),Rows=1};
        foreach(var metric in fact.Metrics)
        {
            bool declared=recap && metric.Value.EndsWith(" dec",StringComparison.Ordinal);
            string score=declared?metric.Value[..^4]:metric.Value;
            double cellWidth=(_design.Width-36)*(recap?3.32/4:2.2/3.2)/Math.Max(1,fact.Metrics.Count)-16;
            var cell=new Grid{Width=Math.Max(1,cellWidth),Margin=new Thickness(8,0,8,0),VerticalAlignment=VerticalAlignment.Center,Height=130};
            bool longCaption=metric.Label.Length>24;
            cell.RowDefinitions.Add(new(){Height=new GridLength(longCaption?50:30)});
            cell.RowDefinitions.Add(new(){Height=new GridLength(longCaption?60:78)});
            cell.RowDefinitions.Add(new(){Height=new GridLength(longCaption?20:22)});
            // Long club names lose optional trailing characters, never the
            // figure's reserved width or the caption's readable type size.
            string caption=metric.Label,secondary="";
            if(longCaption)
            {
                if(recap)
                {
                    int separator=caption.IndexOf(" · ",StringComparison.Ordinal);
                    if(separator>=0 && caption.Length>separator+7)
                    {secondary=caption[..separator]+" · "+caption[^3..];caption=caption[(separator+3)..^4];}
                }
                else foreach(string requirement in new[]{" RUNS TO WIN"," WICKETS TO WIN"," AGGREGATE"," LEAD BY"," TRAIL BY"})
                    if(caption.EndsWith(requirement,StringComparison.Ordinal)){secondary=requirement.Trim();caption=caption[..^requirement.Length];break;}
            }
            var label=Text(longCaption?23:recap?30:32);label.Text=caption;label.TextAlignment=TextAlignment.Center;
            label.TextTrimming=TextTrimming.CharacterEllipsis;label.ToolTip=metric.Label;
            if(longCaption)
            {
                var captionRows=new Grid();captionRows.RowDefinitions.Add(new());captionRows.RowDefinitions.Add(new());
                captionRows.Children.Add(label);var meaning=Fit(secondary,23);SetRow(meaning,1);captionRows.Children.Add(meaning);cell.Children.Add(captionRows);
            }
            else cell.Children.Add(label);
            var value=Fit(score,recap?78:92,emphasis,"situation-value");SetRow(value,1);cell.Children.Add(value);
            if(declared){var status=Fit("DECLARED",22);SetRow(status,2);cell.Children.Add(status);}
            columns.Children.Add(cell);
        }
        SetColumn(columns,1);_foreground.Children.Add(columns);return true;
    }
}

// Generated from Views/CricketControlPanel.CorrectionLayout.cs; edit translator/adapters.
// Source SHA-256: e6c11e3d64c1cb5bde45fbf60711fc47ad2a4e1cc8da45481c27ac1f9b0fe3d9
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Roche_Scoreboard.Services;

namespace Roche_Scoreboard.Views;

public partial class CricketControlPanel
{
    private (Expander Group,StackPanel Body,TextBlock Detail) CorrectionSection(string title,string kind,string tag)
    {
        var content=new StackPanel();
        var header=new Grid();header.ColumnDefinitions.Add(new(){Width=new GridLength(30)});header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new(){Width=new GridLength(20)});
        var icon=new OperatorSymbol{Kind=kind,Width=21,Height=21,VerticalAlignment=VerticalAlignment.Center};icon.SetResourceReference(OperatorSymbol.ForegroundProperty,"ControlCentre.TextBrush");header.Children.Add(icon);
        var labels=new StackPanel();labels.Children.Add(WorkflowText(title,16));var detail=WorkflowText("",12);detail.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SecondaryTextBrush");labels.Children.Add(detail);Grid.SetColumn(labels,1);header.Children.Add(labels);
        var turn=new RotateTransform(90);var chevron=new OperatorSymbol{Kind="forward",Width=14,Height=14,RenderTransform=turn,RenderTransformOrigin=new RelativePoint(.5,.5,RelativeUnit.Relative),VerticalAlignment=VerticalAlignment.Center};chevron.SetResourceReference(OperatorSymbol.ForegroundProperty,"ControlCentre.SecondaryTextBrush");Grid.SetColumn(chevron,2);header.Children.Add(chevron);
        var group=new Expander{Header=header,Content=content,IsExpanded=true,Tag="scorecard-correction-"+tag};group.SetResourceReference(ThemeProperty,"ScorecardCorrectionGroup");
        Avalonia.Automation.AutomationProperties.SetName(group,title+" correction fields");
        void SetChevron()
        {
            double angle=group.IsExpanded?90:0;
            if(!OperatorMotion.Enabled){turn.BeginAnimation(RotateTransform.AngleProperty,null);turn.Angle=angle;return;}
            turn.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(angle,OperatorMotion.SurfaceOpen){EasingFunction=OperatorMotion.SmoothOut});
        }
        void PreferenceChanged(){if(!OperatorMotion.Enabled)SetChevron();}
        void RestChevron(){turn.BeginAnimation(RotateTransform.AngleProperty,null);turn.Angle=group.IsExpanded?90:0;}
        group.Expanded+=(_,_)=>SetChevron();group.Collapsed+=(_,_)=>SetChevron();
        group.Loaded+=(_,_)=>{OperatorAppearance.Changed-=PreferenceChanged;OperatorAppearance.Changed+=PreferenceChanged;RestChevron();};
        group.Unloaded+=(_,_)=>{OperatorAppearance.Changed-=PreferenceChanged;RestChevron();};
        return(group,content,detail);
    }
}

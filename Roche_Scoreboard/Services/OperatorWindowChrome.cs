using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using System.Windows.Media;
using Roche_Scoreboard.Views;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;

namespace Roche_Scoreboard.Services;

/// <summary>Integrated operator window controls; public output HWNDs are never changed.</summary>
internal static class OperatorWindowChrome
{
    private static readonly ConditionalWeakTable<Window,object> Applied = new();
    internal static void Enable(Window window)
    {
        if(window is not MainWindow || Applied.TryGetValue(window,out _) || window.Content is not FrameworkElement original)return;
        Applied.Add(window,new object());
        window.WindowStyle=WindowStyle.None;
        WindowChrome.SetWindowChrome(window,new WindowChrome { CaptionHeight=36,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(14),UseAeroCaptionButtons=false });
        var shell=new Grid();shell.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty,"ControlCentre.CanvasBrush");
        shell.RowDefinitions.Add(new(){Height=new GridLength(36)});shell.RowDefinitions.Add(new());
        var titlebar=new Grid { Margin=new Thickness(14,0,6,0) };
        titlebar.ColumnDefinitions.Add(new());titlebar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var title=new TextBlock { FontSize=12,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,0,12,0) };
        title.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding(nameof(Window.Title)){Source=window});
        title.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SecondaryTextBrush");titlebar.Children.Add(title);
        var actions=new StackPanel { Orientation=System.Windows.Controls.Orientation.Horizontal };Grid.SetColumn(actions,1);titlebar.Children.Add(actions);
        Button Command(string name,string path,Action action,bool destructive=false)
        {
            var glyph=new System.Windows.Shapes.Path { Data=Geometry.Parse(path),Width=11,Height=11,Stretch=Stretch.Uniform,StrokeThickness=1.2,Stroke=destructive?new SolidColorBrush(Colors.LightCoral):Brushes.White };
            if(!destructive)glyph.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"ControlCentre.TextBrush");
            var button=new Button { Content=glyph,Width=38,Height=28,MinHeight=0,Padding=new Thickness(0),Margin=new Thickness(2,0,2,0),Background=Brushes.Transparent,BorderThickness=new Thickness(0),ToolTip=name };
            button.SetResourceReference(FrameworkElement.StyleProperty,"ControlCentre.Button");
            System.Windows.Automation.AutomationProperties.SetName(button,name);WindowChrome.SetIsHitTestVisibleInChrome(button,true);
            button.Click+=(_,_)=>action();actions.Children.Add(button);return button;
        }
        Command("Minimize","M1,6 L11,6",()=>window.WindowState=WindowState.Minimized);
        var maximize=Command("Maximize or restore","M1,1 L11,1 11,11 1,11 Z",()=>window.WindowState=window.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized);
        window.StateChanged+=(_,_)=>maximize.ToolTip=window.WindowState==WindowState.Maximized?"Restore window":"Maximize window";
        Command("Close","M1,1 L11,11 M11,1 L1,11",window.Close,true);
        window.Content=null;Grid.SetRow(original,1);shell.Children.Add(titlebar);shell.Children.Add(original);window.Content=shell;
    }
}

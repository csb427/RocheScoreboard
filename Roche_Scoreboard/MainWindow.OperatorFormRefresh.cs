using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Border=System.Windows.Controls.Border;
using Control=System.Windows.Controls.Control;
using TextBox=System.Windows.Controls.TextBox;
using RadioButton=System.Windows.Controls.RadioButton;
using Binding=System.Windows.Data.Binding;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private static void RefreshOperatorForm(FrameworkElement root)
    {
        foreach(var element in FormChildren(root))
        {
            if(element is TextBlock text)
            {
                text.SetResourceReference(TextBlock.FontFamilyProperty,"ControlCentre.Font");
                // Keep named live values and symbolic glyphs; replace the old tiny form copy.
                if(text.Text.Length>3)
                {
                    text.FontSize=Math.Max(13,text.FontSize);text.TextWrapping=TextWrapping.Wrap;text.FontWeight=FontWeights.Normal;
                    if(string.IsNullOrEmpty(text.Name) && !InsideAction(text))text.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SecondaryTextBrush");
                    string? title=text.Text switch{"TEAMS"=>"Teams","MATCH"=>"Match clock","KEYBINDS"=>"Keyboard shortcuts","DISPLAY"=>"Scoreboard","OVERLAYS"=>"Automation","MEDIA"=>"Media","ADVANCED"=>"Application","LIVE OUTPUT ROUTER"=>"Presentations","LIVE TRIGGERS"=>"Manual overlays",_=>null};
                    if(title is not null){text.Text=title;text.FontSize=21;text.FontWeight=FontWeights.SemiBold;text.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");}
                }
            }
            if(element is Control control)
            {
                control.SetResourceReference(Control.FontFamilyProperty,"ControlCentre.Font");
                control.FontSize=Math.Max(14,control.FontSize);
                if(control is TextBox input && (double.IsNaN(input.Width)||input.Width>=70))
                {input.MinHeight=44;input.SetResourceReference(Control.BackgroundProperty,"ControlCentre.RaisedBrush");input.SetResourceReference(Control.ForegroundProperty,"ControlCentre.TextBrush");}
            }
            if(element is Border border && border.Child is Grid && border.Padding.Left>=10)
            {border.CornerRadius=new CornerRadius(20);border.BorderThickness=new Thickness(0);border.SetResourceReference(Border.BackgroundProperty,"ControlCentre.RaisedBrush");}
        }
    }
    private static bool InsideAction(FrameworkElement element)
    {
        for(var parent=element.Parent as FrameworkElement;parent is not null;parent=parent.Parent as FrameworkElement)
            if(parent is System.Windows.Controls.Primitives.ButtonBase)return true;
        return false;
    }
    private static IEnumerable<DependencyObject> FormChildren(DependencyObject root)
    {
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {yield return child;foreach(var nested in FormChildren(child))yield return nested;}
    }
    private void RefreshAflClockChoices()
    {
        foreach(var (card,source,label,kind) in new[]{(SettingsCountUpCard,ClockModeCountUp,"Count up","clock"),(SettingsCountdownCard,ClockModeCountdown,"Count down","timer")})
        {
            if(card.Child is not Grid grid)continue;
            foreach(var old in grid.Children.OfType<UIElement>())old.Visibility=Visibility.Collapsed;
            var symbol=new OperatorSymbol{Kind=kind,Width=28,Height=28,HorizontalAlignment=System.Windows.HorizontalAlignment.Center,Margin=new Thickness(0,0,0,7)};
            var content=new StackPanel();content.Children.Add(symbol);var caption=new TextBlock{Text=label,FontSize=16,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center};content.Children.Add(caption);
            var choice=new RadioButton{Content=content,GroupName="AflLiveClockDesign",Padding=new Thickness(14),MinHeight=96};choice.SetResourceReference(StyleProperty,"SettingsCommand");
            symbol.SetBinding(OperatorSymbol.ForegroundProperty,new Binding("Foreground"){Source=choice});caption.SetBinding(TextBlock.ForegroundProperty,new Binding("Foreground"){Source=choice});
            choice.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,new Binding("IsChecked"){Source=source,Mode=BindingMode.TwoWay});
            grid.Children.Add(choice);card.CornerRadius=new CornerRadius(24);card.BorderThickness=new Thickness(0);
        }
        if(SettingsCountUpCard.Parent is Grid modes){modes.Height=double.NaN;modes.MinHeight=104;WizardPair.SetMinimumColumnWidth(modes,180);}
        if(HomeNameBox.Parent is Grid names && names.Parent is StackPanel fields && fields.Parent is Grid team && team.Parent is Border home && home.Parent is Grid pair)
            WizardPair.SetMinimumColumnWidth(pair,280);
    }
    private void RefreshAflPresentationChoices()
    {
        foreach(var (card,label,kind) in new[]{(PresCardScorebug,"Live scorebug","display"),(PresCardSummary,"Match card","scorecard"),
            (PresCardWorm,"Margin flow","match"),(PresCardStats,"Match stats","match"),(PresCardWeather,"Weather","weather"),
            (PresCardQuarterBreak,"Quarter break","clock"),(PresCardScreensaver,"Screensaver","media")})
        {
            if(card.Child is not Grid grid)continue;
            foreach(var old in grid.Children.OfType<StackPanel>())old.Visibility=Visibility.Collapsed;
            var body=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,16,12,16)};
            var symbol=new OperatorSymbol{Kind=kind,Width=29,Height=29,HorizontalAlignment=System.Windows.HorizontalAlignment.Center,Margin=new Thickness(0,0,0,10)};
            symbol.SetResourceReference(OperatorSymbol.ForegroundProperty,"ControlCentre.TextBrush");body.Children.Add(symbol);
            var text=new TextBlock{Text=label,FontSize=16,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap};
            text.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");body.Children.Add(text);grid.Children.Add(body);
            card.CornerRadius=new CornerRadius(24);grid.MinHeight=116;
        }
    }
}

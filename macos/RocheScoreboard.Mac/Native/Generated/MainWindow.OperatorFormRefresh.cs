// Generated from MainWindow.OperatorFormRefresh.cs; edit translator/adapters.
// Source SHA-256: 4bf5c31deacaf08ab229ded15ea41aa52f3018a79f6a5fad34d497a84827faad
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Data;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Border=Avalonia.Controls.Border;
using TemplatedControl = Avalonia.Controls.Primitives.TemplatedControl;
using TextBox=Avalonia.Controls.TextBox;
using RadioButton=Avalonia.Controls.RadioButton;
using Binding=Avalonia.Data.Binding;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private static void RefreshOperatorForm(Control root)
    {
        foreach(var element in FormChildren(root))
        {
            if(element is TextBlock text)
            {
                text.SetResourceReference(TextBlock.FontFamilyProperty,"ControlCentre.Font");
                // Keep named live values and symbolic glyphs; replace the old tiny form copy.
                if((text.Text ?? string.Empty).Length>3)
                {
                    text.FontSize=Math.Max(13,text.FontSize);text.TextWrapping=TextWrapping.Wrap;text.FontWeight=FontWeight.Normal;
                    if(string.IsNullOrEmpty((text.Name ?? string.Empty)) && !InsideAction(text))text.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SecondaryTextBrush");
                    string? title=(text.Text ?? string.Empty) switch{"TEAMS"=>"Teams","MATCH"=>"Match clock","KEYBINDS"=>"Keyboard shortcuts","DISPLAY"=>"Scoreboard","OVERLAYS"=>"Automation","MEDIA"=>"Media","ADVANCED"=>"Application","LIVE OUTPUT ROUTER"=>"Presentations","LIVE TRIGGERS"=>"Manual overlays",_=>null};
                    if(title is not null){text.Text=title;text.FontSize=21;text.FontWeight=FontWeight.SemiBold;text.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");}
                }
            }
            if(element is TemplatedControl control)
            {
                control.SetResourceReference(TextBlock.FontFamilyProperty,"ControlCentre.Font");
                control.FontSize=Math.Max(14,control.FontSize);
                if(control is TextBox input && (double.IsNaN(input.Width)||input.Width>=70))
                {input.MinHeight=44;input.SetResourceReference(TemplatedControl.BackgroundProperty,"ControlCentre.RaisedBrush");input.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");}
            }
            if(element is Border border && border.Child is Grid && border.Padding.Left>=10)
            {border.CornerRadius=new CornerRadius(20);border.BorderThickness=new Thickness(0);border.SetResourceReference(Border.BackgroundProperty,"ControlCentre.RaisedBrush");}
        }
    }
    private static bool InsideAction(Control element)
    {
        for(var parent=element.Parent as Control;parent is not null;parent=parent.Parent as Control)
            if(parent is Avalonia.Controls.Button)return true;
        return false;
    }
    private static IEnumerable<AvaloniaObject> FormChildren(AvaloniaObject root)
    {
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<AvaloniaObject>())
        {yield return child;foreach(var nested in FormChildren(child))yield return nested;}
    }
    private void RefreshAflClockChoices()
    {
        foreach(var (card,source,label,kind) in new[]{(SettingsCountUpCard,ClockModeCountUp,"Count up","clock"),(SettingsCountdownCard,ClockModeCountdown,"Count down","timer")})
        {
            if(card.Child is not Grid grid)continue;
            foreach(var old in grid.Children.OfType<Control>())old.IsVisible=false;
            var symbol=new OperatorSymbol{Kind=kind,Width=28,Height=28,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center,Margin=new Thickness(0,0,0,7)};
            var content=new StackPanel();content.Children.Add(symbol);var caption=new NativeTextBlock{Text=label,FontSize=16,FontWeight=FontWeight.SemiBold,TextAlignment=TextAlignment.Center};content.Children.Add(caption);
            var choice=new RadioButton{Content=content,GroupName="AflLiveClockDesign",Padding=new Thickness(14),MinHeight=96};choice.SetResourceReference(ThemeProperty,"SettingsCommand");
            symbol.SetBinding(OperatorSymbol.ForegroundProperty,new Binding("Foreground"){Source=choice});caption.SetBinding(TextBlock.ForegroundProperty,new Binding("Foreground"){Source=choice});
            choice.SetBinding(Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty,new Binding("IsChecked"){Source=source,Mode=BindingMode.TwoWay});
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
            foreach(var old in grid.Children.OfType<StackPanel>())old.IsVisible=false;
            var body=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,16,12,16)};
            var symbol=new OperatorSymbol{Kind=kind,Width=29,Height=29,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center,Margin=new Thickness(0,0,0,10)};
            symbol.SetResourceReference(OperatorSymbol.ForegroundProperty,"ControlCentre.TextBrush");body.Children.Add(symbol);
            var text=new NativeTextBlock{Text=label,FontSize=16,FontWeight=FontWeight.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap};
            text.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");body.Children.Add(text);grid.Children.Add(body);
            card.CornerRadius=new CornerRadius(24);grid.MinHeight=116;
        }
    }
}

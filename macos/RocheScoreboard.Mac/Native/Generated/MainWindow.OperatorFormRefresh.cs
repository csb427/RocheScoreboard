// Generated from MainWindow.OperatorFormRefresh.cs; edit translator/adapters.
// Source SHA-256: 6c775a815a3e417e5e431db3af53f99efedea09d96e72e10da5fb57c491b60a0
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
        foreach(var (card,choice,label,description,kind,check) in new[]
        {
            (SettingsCountUpCard,ClockModeCountUp,"Count up","Elapsed playing time","clock-up",SettingsCountUpCheck),
            (SettingsCountdownCard,ClockModeCountdown,"Count down","Time remaining in the quarter","clock-down",SettingsCountdownCheck)
        })
        {
            if(card.Child is not Grid grid || ReferenceEquals(choice.Parent,grid))continue;
            // Reuse the original grouped radio and its existing Checked handler.
            // A second radio plus an independently actionable parent gives one
            // choice multiple keyboard/automation targets.
            if(choice.Parent is Avalonia.Controls.Panel panel)panel.Children.Remove(choice);
            foreach(var old in grid.Children.OfType<Control>())old.IsVisible=false;
            grid.Children.Remove(check);
            check.IsHitTestVisible=false;check.VerticalAlignment=VerticalAlignment.Center;
            check.HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Right;
            check.CornerRadius=new CornerRadius(10);check.Padding=new Thickness(6,3,6,3);
            check.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SelectionBrush");
            if(check.Child is TextBlock tick)
            {tick.FontSize=13;tick.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SelectedTextBrush");}
            var symbol=new OperatorSymbol{Kind=kind,Width=28,Height=28,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)};
            symbol.SetResourceReference(OperatorSymbol.ForegroundProperty,"ControlCentre.TextBrush");
            var content=new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            content.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            content.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(28)});
            content.Children.Add(symbol);
            var copy=new StackPanel{VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(copy,1);
            var caption=new NativeTextBlock{Text=label,FontSize=16,FontWeight=FontWeight.SemiBold};
            caption.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");copy.Children.Add(caption);
            var detail=new NativeTextBlock{Text=description,FontSize=13,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,8,0)};
            detail.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SecondaryTextBrush");copy.Children.Add(detail);
            content.Children.Add(copy);Grid.SetColumn(check,2);content.Children.Add(check);
            // This choice owns its filled selection and independent hand gesture.
            // Retire generic whole-content inflation, including implicit themes.
            OperatorControlMotion.SetEnabled(choice,false);OperatorFeedback.SetManaged(choice,true);
            choice.SetResourceReference(ThemeProperty,"WizardClockChoice");
            choice.Content=content;choice.IsVisible=true;choice.Margin=new Thickness(0);
            choice.KeyDown-=AflClockChoice_KeyDown;choice.KeyDown+=AflClockChoice_KeyDown;
            choice.Padding=new Thickness(14);choice.MinHeight=96;
            Avalonia.Automation.AutomationProperties.SetName(choice,label+": "+description);
            grid.Children.Add(choice);card.CornerRadius=new CornerRadius(24);card.BorderThickness=new Thickness(0);
        }
        if(SettingsCountUpCard.Parent is Grid modes)
        {
            modes.Height=double.NaN;modes.MinHeight=104;WizardPair.SetMinimumColumnWidth(modes,180);
            Avalonia.Input.KeyboardNavigation.SetTabNavigation(modes,Avalonia.Input.KeyboardNavigationMode.Once);
        }
        if(HomeNameBox.Parent is Grid names && names.Parent is StackPanel fields && fields.Parent is Grid team && team.Parent is Border home && home.Parent is Grid pair)
            WizardPair.SetMinimumColumnWidth(pair,280);        // Install the steady cue and border before the first visible frame,
        // without restarting the separate stoppage-option animation.
        UpdateAflClockChoiceState(ClockModeCountUp.IsChecked==true);
    }
    private void UpdateAflClockChoiceState(bool countUp)
    {
        SettingsCountUpCard.BorderThickness=SettingsCountdownCard.BorderThickness=new Thickness(1);
        SettingsCountUpCard.SetResourceReference(Border.BackgroundProperty,"ControlCentre.RaisedBrush");
        SettingsCountdownCard.SetResourceReference(Border.BackgroundProperty,"ControlCentre.RaisedBrush");
        SettingsCountUpCard.SetResourceReference(Border.BorderBrushProperty,countUp?"ControlCentre.AccentBrush":"ControlCentre.BorderBrush");
        SettingsCountdownCard.SetResourceReference(Border.BorderBrushProperty,countUp?"ControlCentre.BorderBrush":"ControlCentre.AccentBrush");
        // The original native radio owns selection; retain a steady checked
        // cue as well as the directional icon and filled choice surface.
        SettingsCountUpCheck.IsVisible=countUp?true:false;
        SettingsCountdownCheck.IsVisible=countUp?false:true;
        SettingsCountUpGlow.Opacity=SettingsCountdownGlow.Opacity=0;
    }
    private void AflClockChoice_KeyDown(object sender,Avalonia.Input.KeyEventArgs e)
    {
        // These two radios live in separate responsive cards. Keep ordinary
        // radio arrow traversal within the pair on either UI framework.
        if(NativeKeyboard.Modifiers!=Avalonia.Input.KeyModifiers.None
            || sender is not RadioButton current || !current.IsFocused
            || (!ReferenceEquals(current,ClockModeCountUp) && !ReferenceEquals(current,ClockModeCountdown)))return;
        if(e.Key is not (Avalonia.Input.Key.Left or Avalonia.Input.Key.Right
            or Avalonia.Input.Key.Up or Avalonia.Input.Key.Down))return;
        var next=ReferenceEquals(current,ClockModeCountUp)?ClockModeCountdown:ClockModeCountUp;
        if(!next.IsVisible || !next.IsEnabled || !next.Focus())return;
        next.IsChecked=true;e.Handled=true;
    }
    private void RefreshAflPresentationChoices()
    {
        foreach(var (card,label,kind) in new[]{(PresCardScorebug,"Live scorebug","display"),(PresCardSummary,"Match card","scorecard"),
            (PresCardWorm,"Margin flow","match"),(PresCardStats,"Match stats","match"),(PresCardWeather,"Weather","weather"),
            (PresCardQuarterBreak,"Quarter break","clock"),(PresCardScreensaver,"Screensaver","media")})
        {
            if(ReferenceEquals(card,PresCardScorebug)){InstallAflLiveScorebugCardVisual();continue;}
            if(ReferenceEquals(card,PresCardSummary)){InstallAflQuarterSummaryCardVisual();continue;}
            if(ReferenceEquals(card,PresCardWorm)){InstallAflMarginFlowCardVisual();continue;}
            if(ReferenceEquals(card,PresCardStats)){InstallAflMatchStatsCardVisual();continue;}
            if(ReferenceEquals(card,PresCardWeather)){InstallAflWeatherCardVisual();continue;}
            if(ReferenceEquals(card,PresCardQuarterBreak)){InstallAflQuarterBreakCardVisual();continue;}
            if(ReferenceEquals(card,PresCardScreensaver)){InstallAflScreensaverCardVisual();continue;}
            if(card.Child is not Grid grid)continue;
            foreach(var old in grid.Children.OfType<StackPanel>())old.IsVisible=false;
            var body=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,16,12,16)};
            var symbol=new OperatorSymbol{Kind=kind,Width=29,Height=29,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center,Margin=new Thickness(0,0,0,10)};
            symbol.SetResourceReference(OperatorSymbol.ForegroundProperty,"ControlCentre.TextBrush");body.Children.Add(symbol);
            var text=new NativeTextBlock{Text=label,FontSize=16,FontWeight=FontWeight.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap};
            text.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");body.Children.Add(text);grid.Children.Add(body);
            card.CornerRadius=new CornerRadius(24);grid.MinHeight=116;
        }
        InstallAflOutputWindowCardVisual();
    }
}

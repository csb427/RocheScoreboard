// Generated from MainWindow.AflMatchStatsCard.cs; edit translator/adapters.
// Source SHA-256: 536dd4eeb7dbc0ca69edc59ff0820cf1affe5a6cc4df2525469217650ab83bbf
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.ComponentModel;
using Avalonia.Data;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Border=Avalonia.Controls.Border;
using Binding=Avalonia.Data.Binding;

namespace Roche_Scoreboard;

// Match-stats selection parts follow the original presentation authority.
public partial class MainWindow
{
    private AflMatchStatsGlyph? _aflMatchStatsGlyph;
    private bool _matchStatsCardHooks,_matchStatsPolicyAttached,_matchStatsOwnerClosed;
    private void InstallAflMatchStatsCardVisual()
    {
        if(_aflMatchStatsGlyph is not null)return;
        if(PresCardStats.Child is not Grid grid)return;
        foreach(var old in grid.Children.OfType<StackPanel>())old.IsVisible=false;
        var body=new StackPanel{Name="MatchStatsCardBody",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,16,12,16),IsHitTestVisible=false};
        _aflMatchStatsGlyph=new AflMatchStatsGlyph{HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center,Margin=new Thickness(0,0,0,10)};
        var item=PresCardStats;var selection=_aflMatchStatsGlyph.SelectionBridge;
        // Exact existing attached-progress bridge; ordinary CLR binding then
        // drives these authored parts without a new clock or parent-tab owner.
        selection.Bind(OperatorChoiceSurface.ProgressProperty,item.GetObservable(OperatorChoiceMotion.ProgressProperty));
        _aflMatchStatsGlyph.SetBinding(AflMatchStatsGlyph.ProgressProperty,new Binding(nameof(OperatorChoiceSurface.Progress)){Source=selection});
        body.Children.Add(_aflMatchStatsGlyph);
        var title=new NativeTextBlock{Name="MatchStatsCardTitle",Text="Match stats",FontSize=16,FontWeight=FontWeight.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap};
        title.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");body.Children.Add(title);grid.Children.Add(body);
        PresCardStats.CornerRadius=new CornerRadius(24);grid.MinHeight=116;
        PresCheckStats.IsHitTestVisible=false;
        // Establish current selection before enabling motion: no restore/show
        // celebration, including an already-selected off-air presentation.
        OperatorChoiceMotion.SetSelected(PresCardStats,_activeScreenKey=="stats");
        OperatorChoiceMotion.SetEnabled(PresCardStats,true);
        InstallMatchStatsCardOwnership();UpdateAflMatchStatsCardSelection(_activeScreenKey=="stats");
    }
    private void InstallMatchStatsCardOwnership()
    {
        if(_matchStatsCardHooks)return;_matchStatsCardHooks=true;
        PresCardStats.Loaded+=MatchStatsCardLoaded;PresCardStats.Unloaded+=MatchStatsCardUnloaded;
        NativeEvents.WatchProperty(PresCardStats,Avalonia.Controls.Control.IsVisibleProperty,true,MatchStatsAvailabilityChanged);
        NativeEvents.WatchProperty(PresCardStats,Avalonia.Controls.Control.IsEnabledProperty,true,MatchStatsAvailabilityChanged);
        NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsVisibleProperty,true,MatchStatsAvailabilityChanged);NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsEnabledProperty,true,MatchStatsAvailabilityChanged);
        NativeEvents.WatchProperty(MainContent,Avalonia.Controls.Control.IsVisibleProperty,true,MatchStatsAvailabilityChanged);NativeEvents.WatchProperty(MainContent,Avalonia.Controls.Control.IsEnabledProperty,true,MatchStatsAvailabilityChanged);
        MainTabControl.SelectionChanged+=MatchStatsTabChanged;
        Closed+=MatchStatsOwnerClosed;
        if(PresCardStats.IsLoaded)MatchStatsCardLoaded(PresCardStats,new RoutedEventArgs());
    }
    private void MatchStatsOwnerClosed(object? sender,EventArgs args)
    {
        _matchStatsOwnerClosed=true;DetachMatchStatsSystemPolicy();
        OperatorChoiceMotion.SetEnabled(PresCardStats,false);
        Closed-=MatchStatsOwnerClosed;
    }
    private void MatchStatsCardLoaded(object? sender,RoutedEventArgs args)
    {
        if(_matchStatsOwnerClosed)return;
        if(!_matchStatsPolicyAttached){NativeSystemPreferences.StaticPropertyChanged+=MatchStatsSystemPolicyChanged;_matchStatsPolicyAttached=true;}
        SettleAflMatchStatsCard();
    }
    private void MatchStatsCardUnloaded(object? sender,RoutedEventArgs args){DetachMatchStatsSystemPolicy();SettleAflMatchStatsCard();}
    private void DetachMatchStatsSystemPolicy(){NativeSystemPreferences.StaticPropertyChanged-=MatchStatsSystemPolicyChanged;_matchStatsPolicyAttached=false;}
    private void MatchStatsSystemPolicyChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(args.PropertyName!=nameof(NativeSystemPreferences.ClientAreaAnimation))return;
        if(Dispatcher.UIThread.CheckAccess()){if(!_matchStatsOwnerClosed&&!OperatorMotion.Enabled)SettleAflMatchStatsCard();}
        else Dispatcher.UIThread.Post(new Action(()=>{if(!_matchStatsOwnerClosed&&_matchStatsPolicyAttached&&!OperatorMotion.Enabled)SettleAflMatchStatsCard();}));
    }
    private void MatchStatsAvailabilityChanged(object? sender,AvaloniaPropertyChangedEventArgs args)
    {if(args.Property.Name is nameof(Control.IsVisible) or nameof(Control.IsEnabled))SettleAflMatchStatsCard();}
    private void MatchStatsTabChanged(object? sender,SelectionChangedEventArgs args)
    {if(args.Source==MainTabControl)SettleAflMatchStatsCard();}
    private void SettleAflMatchStatsCard(bool? requestedSelection=null)
    {
        // Synchronous refresh callers may paint before assigning the backing
        // screen field. Lifecycle callbacks still settle from current authority.
        bool selected=requestedSelection??(_activeScreenKey=="stats");OperatorChoiceMotion.SetSelected(PresCardStats,selected);
        PresCardStats.BeginAnimation(OperatorChoiceMotion.ProgressProperty,null);OperatorChoiceMotion.SetProgress(PresCardStats,selected?1:0);
    }
    private void UpdateAflMatchStatsCardSelection(bool selected)
    {
        PresCardStats.SetResourceReference(Border.BackgroundProperty,selected?"ControlCentre.SelectionBrush":"ControlCentre.RaisedBrush");
        PresCardStats.SetResourceReference(Border.BorderBrushProperty,selected?"ControlCentre.AccentBrush":"ControlCentre.BorderBrush");
        PresCardStats.BorderThickness=new Thickness(2);
        PresCheckStats.IsVisible=true;PresCheckStats.Opacity=selected?1:0;
        PresCheckStats.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SelectionBrush");
        if(PresCheckStats.Child is TextBlock tick)tick.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SelectedTextBrush");
        foreach(var title in FindVisualChildren<TextBlock>(PresCardStats).Where(text=>(text.Name ?? string.Empty)=="MatchStatsCardTitle"))
            title.SetResourceReference(TextBlock.ForegroundProperty,selected?"ControlCentre.SelectedTextBrush":"ControlCentre.TextBrush");
        _aflMatchStatsGlyph?.SetSelectedInk(selected);
        Avalonia.Automation.AutomationProperties.SetName(PresCardStats,"Match stats");
        Avalonia.Automation.AutomationProperties.SetItemStatus(PresCardStats,selected?"Selected":"Not selected");
        Avalonia.Automation.AutomationProperties.SetHelpText(PresCardStats,
            "Choose both teams' scoring shots, accuracy, time in front and largest lead. Selection does not by itself mean the output window is open.");
        OperatorChoiceMotion.SetSelected(PresCardStats,selected);
        bool animate=!_matchStatsOwnerClosed&&OperatorMotion.Enabled&&IsVisible&&IsEnabled&&MainContent.IsVisible&&MainContent.IsEnabled
            &&ReferenceEquals(MainTabControl.SelectedItem,OutputStudioTab)&&PresCardStats.IsLoaded&&PresCardStats.IsVisible&&PresCardStats.IsEnabled
            &&!OperatorInputFocus.GetIsKeyboardMode(PresCardStats);
        if(!animate)SettleAflMatchStatsCard(selected);
    }
}

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Border=System.Windows.Controls.Border;
using Binding=System.Windows.Data.Binding;

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
        foreach(var old in grid.Children.OfType<StackPanel>())old.Visibility=Visibility.Collapsed;
        var body=new StackPanel{Name="MatchStatsCardBody",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,16,12,16),IsHitTestVisible=false};
        _aflMatchStatsGlyph=new AflMatchStatsGlyph{HorizontalAlignment=System.Windows.HorizontalAlignment.Center,Margin=new Thickness(0,0,0,10)};
        var item=PresCardStats;var selection=_aflMatchStatsGlyph.SelectionBridge;
        // Exact existing attached-progress bridge; ordinary CLR binding then
        // drives these authored parts without a new clock or parent-tab owner.
        selection.SetBinding(OperatorChoiceSurface.ProgressProperty,new System.Windows.Data.Binding
            { Source=item,Path=new PropertyPath(OperatorChoiceMotion.ProgressProperty) });
        _aflMatchStatsGlyph.SetBinding(AflMatchStatsGlyph.ProgressProperty,new Binding(nameof(OperatorChoiceSurface.Progress)){Source=selection});
        body.Children.Add(_aflMatchStatsGlyph);
        var title=new TextBlock{Name="MatchStatsCardTitle",Text="Match stats",FontSize=16,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap};
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
        PresCardStats.IsVisibleChanged+=MatchStatsAvailabilityChanged;
        PresCardStats.IsEnabledChanged+=MatchStatsAvailabilityChanged;
        IsVisibleChanged+=MatchStatsAvailabilityChanged;IsEnabledChanged+=MatchStatsAvailabilityChanged;
        MainContent.IsVisibleChanged+=MatchStatsAvailabilityChanged;MainContent.IsEnabledChanged+=MatchStatsAvailabilityChanged;
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
        if(!_matchStatsPolicyAttached){SystemParameters.StaticPropertyChanged+=MatchStatsSystemPolicyChanged;_matchStatsPolicyAttached=true;}
        SettleAflMatchStatsCard();
    }
    private void MatchStatsCardUnloaded(object? sender,RoutedEventArgs args){DetachMatchStatsSystemPolicy();SettleAflMatchStatsCard();}
    private void DetachMatchStatsSystemPolicy(){SystemParameters.StaticPropertyChanged-=MatchStatsSystemPolicyChanged;_matchStatsPolicyAttached=false;}
    private void MatchStatsSystemPolicyChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(args.PropertyName!=nameof(SystemParameters.ClientAreaAnimation))return;
        if(Dispatcher.CheckAccess()){if(!_matchStatsOwnerClosed&&!OperatorMotion.Enabled)SettleAflMatchStatsCard();}
        else Dispatcher.BeginInvoke(new Action(()=>{if(!_matchStatsOwnerClosed&&_matchStatsPolicyAttached&&!OperatorMotion.Enabled)SettleAflMatchStatsCard();}));
    }
    private void MatchStatsAvailabilityChanged(object? sender,DependencyPropertyChangedEventArgs args)
    {if(args.Property.Name is nameof(UIElement.IsVisible) or nameof(UIElement.IsEnabled))SettleAflMatchStatsCard();}
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
        PresCheckStats.Visibility=Visibility.Visible;PresCheckStats.Opacity=selected?1:0;
        PresCheckStats.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SelectionBrush");
        if(PresCheckStats.Child is TextBlock tick)tick.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SelectedTextBrush");
        foreach(var title in FindVisualChildren<TextBlock>(PresCardStats).Where(text=>text.Name=="MatchStatsCardTitle"))
            title.SetResourceReference(TextBlock.ForegroundProperty,selected?"ControlCentre.SelectedTextBrush":"ControlCentre.TextBrush");
        _aflMatchStatsGlyph?.SetSelectedInk(selected);
        System.Windows.Automation.AutomationProperties.SetName(PresCardStats,"Match stats");
        System.Windows.Automation.AutomationProperties.SetItemStatus(PresCardStats,selected?"Selected":"Not selected");
        System.Windows.Automation.AutomationProperties.SetHelpText(PresCardStats,
            "Choose both teams' scoring shots, accuracy, time in front and largest lead. Selection does not by itself mean the output window is open.");
        OperatorChoiceMotion.SetSelected(PresCardStats,selected);
        bool animate=!_matchStatsOwnerClosed&&OperatorMotion.Enabled&&IsVisible&&IsEnabled&&MainContent.IsVisible&&MainContent.IsEnabled
            &&ReferenceEquals(MainTabControl.SelectedItem,OutputStudioTab)&&PresCardStats.IsLoaded&&PresCardStats.IsVisible&&PresCardStats.IsEnabled
            &&!OperatorInputFocus.GetIsKeyboardMode(PresCardStats);
        if(!animate)SettleAflMatchStatsCard(selected);
    }
}

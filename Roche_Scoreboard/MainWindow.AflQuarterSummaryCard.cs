using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Border=System.Windows.Controls.Border;
using Binding=System.Windows.Data.Binding;

namespace Roche_Scoreboard;

// Quarter-summary selection parts follow the original presentation authority.
public partial class MainWindow
{
    private AflQuarterSummaryGlyph? _aflQuarterSummaryGlyph;
    private bool _quarterSummaryCardHooks,_quarterSummaryPolicyAttached,_quarterSummaryOwnerClosed;
    private void InstallAflQuarterSummaryCardVisual()
    {
        if(_aflQuarterSummaryGlyph is not null)return;
        if(PresCardSummary.Child is not Grid grid)return;
        foreach(var old in grid.Children.OfType<StackPanel>())old.Visibility=Visibility.Collapsed;
        var body=new StackPanel{Name="QuarterSummaryCardBody",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,16,12,16),IsHitTestVisible=false};
        _aflQuarterSummaryGlyph=new AflQuarterSummaryGlyph{HorizontalAlignment=System.Windows.HorizontalAlignment.Center,Margin=new Thickness(0,0,0,10)};
        var item=PresCardSummary;var selection=_aflQuarterSummaryGlyph.SelectionBridge;
        // Exact existing attached-progress bridge; ordinary CLR binding then
        // drives these authored parts without a new clock or parent-tab owner.
        selection.SetBinding(OperatorChoiceSurface.ProgressProperty,new System.Windows.Data.Binding
            { Source=item,Path=new PropertyPath(OperatorChoiceMotion.ProgressProperty) });
        _aflQuarterSummaryGlyph.SetBinding(AflQuarterSummaryGlyph.ProgressProperty,new Binding(nameof(OperatorChoiceSurface.Progress)){Source=selection});
        body.Children.Add(_aflQuarterSummaryGlyph);
        var title=new TextBlock{Name="QuarterSummaryCardTitle",Text="Quarter summary",FontSize=16,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap};
        title.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");body.Children.Add(title);grid.Children.Add(body);
        PresCardSummary.CornerRadius=new CornerRadius(24);grid.MinHeight=116;
        PresCheckSummary.IsHitTestVisible=false;
        // Establish current selection before enabling motion: no restore/show
        // celebration, including an already-selected off-air presentation.
        OperatorChoiceMotion.SetSelected(PresCardSummary,_activeScreenKey=="summary");
        OperatorChoiceMotion.SetEnabled(PresCardSummary,true);
        InstallQuarterSummaryCardOwnership();UpdateAflQuarterSummaryCardSelection(_activeScreenKey=="summary");
    }
    private void InstallQuarterSummaryCardOwnership()
    {
        if(_quarterSummaryCardHooks)return;_quarterSummaryCardHooks=true;
        PresCardSummary.Loaded+=QuarterSummaryCardLoaded;PresCardSummary.Unloaded+=QuarterSummaryCardUnloaded;
        PresCardSummary.IsVisibleChanged+=QuarterSummaryAvailabilityChanged;
        PresCardSummary.IsEnabledChanged+=QuarterSummaryAvailabilityChanged;
        IsVisibleChanged+=QuarterSummaryAvailabilityChanged;IsEnabledChanged+=QuarterSummaryAvailabilityChanged;
        MainContent.IsVisibleChanged+=QuarterSummaryAvailabilityChanged;MainContent.IsEnabledChanged+=QuarterSummaryAvailabilityChanged;
        MainTabControl.SelectionChanged+=QuarterSummaryTabChanged;
        Closed+=QuarterSummaryOwnerClosed;
        if(PresCardSummary.IsLoaded)QuarterSummaryCardLoaded(PresCardSummary,new RoutedEventArgs());
    }
    private void QuarterSummaryOwnerClosed(object? sender,EventArgs args)
    {
        _quarterSummaryOwnerClosed=true;DetachQuarterSummarySystemPolicy();
        OperatorChoiceMotion.SetEnabled(PresCardSummary,false);
        Closed-=QuarterSummaryOwnerClosed;
    }
    private void QuarterSummaryCardLoaded(object? sender,RoutedEventArgs args)
    {
        if(_quarterSummaryOwnerClosed)return;
        if(!_quarterSummaryPolicyAttached){SystemParameters.StaticPropertyChanged+=QuarterSummarySystemPolicyChanged;_quarterSummaryPolicyAttached=true;}
        SettleAflQuarterSummaryCard();
    }
    private void QuarterSummaryCardUnloaded(object? sender,RoutedEventArgs args){DetachQuarterSummarySystemPolicy();SettleAflQuarterSummaryCard();}
    private void DetachQuarterSummarySystemPolicy(){SystemParameters.StaticPropertyChanged-=QuarterSummarySystemPolicyChanged;_quarterSummaryPolicyAttached=false;}
    private void QuarterSummarySystemPolicyChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(args.PropertyName!=nameof(SystemParameters.ClientAreaAnimation))return;
        if(Dispatcher.CheckAccess()){if(!_quarterSummaryOwnerClosed&&!OperatorMotion.Enabled)SettleAflQuarterSummaryCard();}
        else Dispatcher.BeginInvoke(new Action(()=>{if(!_quarterSummaryOwnerClosed&&_quarterSummaryPolicyAttached&&!OperatorMotion.Enabled)SettleAflQuarterSummaryCard();}));
    }
    private void QuarterSummaryAvailabilityChanged(object? sender,DependencyPropertyChangedEventArgs args)
    {if(args.Property.Name is nameof(UIElement.IsVisible) or nameof(UIElement.IsEnabled))SettleAflQuarterSummaryCard();}
    private void QuarterSummaryTabChanged(object? sender,SelectionChangedEventArgs args)
    {if(args.Source==MainTabControl)SettleAflQuarterSummaryCard();}
    private void SettleAflQuarterSummaryCard(bool? requestedSelection=null)
    {
        // Synchronous refresh callers may paint before assigning the backing
        // screen field. Lifecycle callbacks still settle from current authority.
        bool selected=requestedSelection??(_activeScreenKey=="summary");OperatorChoiceMotion.SetSelected(PresCardSummary,selected);
        PresCardSummary.BeginAnimation(OperatorChoiceMotion.ProgressProperty,null);OperatorChoiceMotion.SetProgress(PresCardSummary,selected?1:0);
    }
    private void UpdateAflQuarterSummaryCardSelection(bool selected)
    {
        PresCardSummary.SetResourceReference(Border.BackgroundProperty,selected?"ControlCentre.SelectionBrush":"ControlCentre.RaisedBrush");
        PresCardSummary.SetResourceReference(Border.BorderBrushProperty,selected?"ControlCentre.AccentBrush":"ControlCentre.BorderBrush");
        PresCardSummary.BorderThickness=new Thickness(2);
        PresCheckSummary.Visibility=Visibility.Visible;PresCheckSummary.Opacity=selected?1:0;
        PresCheckSummary.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SelectionBrush");
        if(PresCheckSummary.Child is TextBlock tick)tick.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SelectedTextBrush");
        foreach(var title in FindVisualChildren<TextBlock>(PresCardSummary).Where(text=>text.Name=="QuarterSummaryCardTitle"))
            title.SetResourceReference(TextBlock.ForegroundProperty,selected?"ControlCentre.SelectedTextBrush":"ControlCentre.TextBrush");
        _aflQuarterSummaryGlyph?.SetSelectedInk(selected);
        System.Windows.Automation.AutomationProperties.SetName(PresCardSummary,"Quarter summary");
        System.Windows.Automation.AutomationProperties.SetItemStatus(PresCardSummary,selected?"Selected":"Not selected");
        System.Windows.Automation.AutomationProperties.SetHelpText(PresCardSummary,
            "Choose the quarter-by-quarter summary for both teams. Selection does not by itself mean the output window is open.");
        OperatorChoiceMotion.SetSelected(PresCardSummary,selected);
        bool animate=!_quarterSummaryOwnerClosed&&OperatorMotion.Enabled&&IsVisible&&IsEnabled&&MainContent.IsVisible&&MainContent.IsEnabled
            &&ReferenceEquals(MainTabControl.SelectedItem,OutputStudioTab)&&PresCardSummary.IsLoaded&&PresCardSummary.IsVisible&&PresCardSummary.IsEnabled
            &&!OperatorInputFocus.GetIsKeyboardMode(PresCardSummary);
        if(!animate)SettleAflQuarterSummaryCard(selected);
    }
}

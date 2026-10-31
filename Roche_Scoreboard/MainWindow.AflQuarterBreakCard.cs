using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Border=System.Windows.Controls.Border;
using Binding=System.Windows.Data.Binding;

namespace Roche_Scoreboard;

// Quarter-break selection parts follow the original presentation authority.
public partial class MainWindow
{
    private AflQuarterBreakGlyph? _aflQuarterBreakPresentationGlyph;
    private bool _quarterBreakPresentationCardHooks,_quarterBreakPresentationPolicyAttached,_quarterBreakPresentationOwnerClosed;
    private void InstallAflQuarterBreakCardVisual()
    {
        if(_aflQuarterBreakPresentationGlyph is not null)return;
        if(PresCardQuarterBreak.Child is not Grid grid)return;
        foreach(var old in grid.Children.OfType<StackPanel>())old.Visibility=Visibility.Collapsed;
        var body=new StackPanel{Name="QuarterBreakPresentationCardBody",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,16,12,16),IsHitTestVisible=false};
        _aflQuarterBreakPresentationGlyph=new AflQuarterBreakGlyph{HorizontalAlignment=System.Windows.HorizontalAlignment.Center,Margin=new Thickness(0,0,0,10)};
        var item=PresCardQuarterBreak;var selection=_aflQuarterBreakPresentationGlyph.SelectionBridge;
        // This exact card owns selection progress. The typed bridge only carries
        // that existing signal to paired-panel parts; it owns no clock or input.
        selection.SetBinding(OperatorChoiceSurface.ProgressProperty,new System.Windows.Data.Binding
            { Source=item,Path=new PropertyPath(OperatorChoiceMotion.ProgressProperty) });
        _aflQuarterBreakPresentationGlyph.SetBinding(AflQuarterBreakGlyph.ProgressProperty,new Binding(nameof(OperatorChoiceSurface.Progress)){Source=selection});
        body.Children.Add(_aflQuarterBreakPresentationGlyph);
        var title=new TextBlock{Name="QuarterBreakPresentationCardTitle",Text="Quarter break",FontSize=16,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap};
        title.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");body.Children.Add(title);grid.Children.Add(body);
        PresCardQuarterBreak.CornerRadius=new CornerRadius(24);grid.MinHeight=116;
        PresCheckQuarterBreak.IsHitTestVisible=false;
        // Establish current selection before enabling motion: no restore/show
        // celebration, including an already-selected off-air presentation.
        OperatorChoiceMotion.SetSelected(PresCardQuarterBreak,_activeScreenKey=="quarterbreak");
        OperatorChoiceMotion.SetEnabled(PresCardQuarterBreak,true);
        InstallQuarterBreakPresentationCardOwnership();UpdateAflQuarterBreakCardSelection(_activeScreenKey=="quarterbreak");
    }
    private void InstallQuarterBreakPresentationCardOwnership()
    {
        if(_quarterBreakPresentationCardHooks)return;_quarterBreakPresentationCardHooks=true;
        PresCardQuarterBreak.Loaded+=QuarterBreakPresentationCardLoaded;PresCardQuarterBreak.Unloaded+=QuarterBreakPresentationCardUnloaded;
        PresCardQuarterBreak.IsVisibleChanged+=QuarterBreakPresentationAvailabilityChanged;
        PresCardQuarterBreak.IsEnabledChanged+=QuarterBreakPresentationAvailabilityChanged;
        IsVisibleChanged+=QuarterBreakPresentationAvailabilityChanged;IsEnabledChanged+=QuarterBreakPresentationAvailabilityChanged;
        MainContent.IsVisibleChanged+=QuarterBreakPresentationAvailabilityChanged;MainContent.IsEnabledChanged+=QuarterBreakPresentationAvailabilityChanged;
        MainTabControl.SelectionChanged+=QuarterBreakPresentationTabChanged;
        Closed+=QuarterBreakPresentationOwnerClosed;
        if(PresCardQuarterBreak.IsLoaded)QuarterBreakPresentationCardLoaded(PresCardQuarterBreak,new RoutedEventArgs());
    }
    private void QuarterBreakPresentationOwnerClosed(object? sender,EventArgs args)
    {
        _quarterBreakPresentationOwnerClosed=true;DetachQuarterBreakPresentationSystemPolicy();
        OperatorChoiceMotion.SetEnabled(PresCardQuarterBreak,false);
        Closed-=QuarterBreakPresentationOwnerClosed;
    }
    private void QuarterBreakPresentationCardLoaded(object? sender,RoutedEventArgs args)
    {
        if(_quarterBreakPresentationOwnerClosed)return;
        if(!_quarterBreakPresentationPolicyAttached){SystemParameters.StaticPropertyChanged+=QuarterBreakPresentationSystemPolicyChanged;_quarterBreakPresentationPolicyAttached=true;}
        SettleAflQuarterBreakCard();
    }
    private void QuarterBreakPresentationCardUnloaded(object? sender,RoutedEventArgs args){DetachQuarterBreakPresentationSystemPolicy();SettleAflQuarterBreakCard();}
    private void DetachQuarterBreakPresentationSystemPolicy(){SystemParameters.StaticPropertyChanged-=QuarterBreakPresentationSystemPolicyChanged;_quarterBreakPresentationPolicyAttached=false;}
    private void QuarterBreakPresentationSystemPolicyChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(args.PropertyName!=nameof(SystemParameters.ClientAreaAnimation))return;
        if(Dispatcher.CheckAccess()){if(!_quarterBreakPresentationOwnerClosed&&!OperatorMotion.Enabled)SettleAflQuarterBreakCard();}
        else Dispatcher.BeginInvoke(new Action(()=>{if(!_quarterBreakPresentationOwnerClosed&&_quarterBreakPresentationPolicyAttached&&!OperatorMotion.Enabled)SettleAflQuarterBreakCard();}));
    }
    private void QuarterBreakPresentationAvailabilityChanged(object? sender,DependencyPropertyChangedEventArgs args)
    {if(args.Property.Name is nameof(UIElement.IsVisible) or nameof(UIElement.IsEnabled))SettleAflQuarterBreakCard();}
    private void QuarterBreakPresentationTabChanged(object? sender,SelectionChangedEventArgs args)
    {if(args.Source==MainTabControl)SettleAflQuarterBreakCard();}
    private void SettleAflQuarterBreakCard(bool? requestedSelection=null)
    {
        // Synchronous refresh callers may paint before assigning the backing
        // screen field. Lifecycle callbacks still settle from current authority.
        bool selected=requestedSelection??(_activeScreenKey=="quarterbreak");OperatorChoiceMotion.SetSelected(PresCardQuarterBreak,selected);
        PresCardQuarterBreak.BeginAnimation(OperatorChoiceMotion.ProgressProperty,null);OperatorChoiceMotion.SetProgress(PresCardQuarterBreak,selected?1:0);
    }
    private void UpdateAflQuarterBreakCardSelection(bool selected)
    {
        PresCardQuarterBreak.SetResourceReference(Border.BackgroundProperty,selected?"ControlCentre.SelectionBrush":"ControlCentre.RaisedBrush");
        PresCardQuarterBreak.SetResourceReference(Border.BorderBrushProperty,selected?"ControlCentre.AccentBrush":"ControlCentre.BorderBrush");
        PresCardQuarterBreak.BorderThickness=new Thickness(2);
        PresCheckQuarterBreak.Visibility=Visibility.Visible;PresCheckQuarterBreak.Opacity=selected?1:0;
        PresCheckQuarterBreak.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SelectionBrush");
        if(PresCheckQuarterBreak.Child is TextBlock tick)tick.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SelectedTextBrush");
        foreach(var title in FindVisualChildren<TextBlock>(PresCardQuarterBreak).Where(text=>text.Name=="QuarterBreakPresentationCardTitle"))
            title.SetResourceReference(TextBlock.ForegroundProperty,selected?"ControlCentre.SelectedTextBrush":"ControlCentre.TextBrush");
        _aflQuarterBreakPresentationGlyph?.SetSelectedInk(selected);
        System.Windows.Automation.AutomationProperties.SetName(PresCardQuarterBreak,"Quarter break");
        System.Windows.Automation.AutomationProperties.SetItemStatus(PresCardQuarterBreak,selected?"Selected":"Not selected");
        System.Windows.Automation.AutomationProperties.SetHelpText(PresCardQuarterBreak,
            "Choose the paired-team score presentation. This does not end a quarter, pause the clock or by itself open the output window.");
        OperatorChoiceMotion.SetSelected(PresCardQuarterBreak,selected);
        bool animate=!_quarterBreakPresentationOwnerClosed&&OperatorMotion.Enabled&&IsVisible&&IsEnabled&&MainContent.IsVisible&&MainContent.IsEnabled
            &&ReferenceEquals(MainTabControl.SelectedItem,OutputStudioTab)&&PresCardQuarterBreak.IsLoaded&&PresCardQuarterBreak.IsVisible&&PresCardQuarterBreak.IsEnabled
            &&!OperatorInputFocus.GetIsKeyboardMode(PresCardQuarterBreak);
        if(!animate)SettleAflQuarterBreakCard(selected);
    }
}

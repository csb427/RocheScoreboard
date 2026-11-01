using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Border=System.Windows.Controls.Border;
using Binding=System.Windows.Data.Binding;

namespace Roche_Scoreboard;

// Live-scorebug selection parts follow the original presentation authority.
public partial class MainWindow
{
    private AflLiveScorebugGlyph? _aflLiveScorebugGlyph;
    private bool _liveScorebugCardHooks,_liveScorebugPolicyAttached,_liveScorebugOwnerClosed;
    private void InstallAflLiveScorebugCardVisual()
    {
        if(_aflLiveScorebugGlyph is not null)return;
        if(PresCardScorebug.Child is not Grid grid)return;
        foreach(var old in grid.Children.OfType<StackPanel>())old.Visibility=Visibility.Collapsed;
        var body=new StackPanel{Name="LiveScorebugCardBody",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,16,12,16),IsHitTestVisible=false};
        _aflLiveScorebugGlyph=new AflLiveScorebugGlyph{HorizontalAlignment=System.Windows.HorizontalAlignment.Center,Margin=new Thickness(0,0,0,10)};
        var item=PresCardScorebug;var selection=_aflLiveScorebugGlyph.SelectionSurface;
        // Exact existing attached-progress bridge; ordinary CLR binding then
        // drives these authored parts without a new clock or parent-tab owner.
        selection.SetBinding(OperatorChoiceSurface.ProgressProperty,new System.Windows.Data.Binding
            { Source=item,Path=new PropertyPath(OperatorChoiceMotion.ProgressProperty) });
        _aflLiveScorebugGlyph.SetBinding(AflLiveScorebugGlyph.ProgressProperty,new Binding(nameof(OperatorChoiceSurface.Progress)){Source=selection});
        body.Children.Add(_aflLiveScorebugGlyph);
        var title=new TextBlock{Name="LiveScorebugCardTitle",Text="Live scorebug",FontSize=16,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap};
        title.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");body.Children.Add(title);grid.Children.Add(body);
        PresCardScorebug.CornerRadius=new CornerRadius(24);grid.MinHeight=116;
        PresCheckScorebug.IsHitTestVisible=false;
        // Establish current selection before enabling motion: no restore/show
        // celebration, including an already-selected off-air presentation.
        OperatorChoiceMotion.SetSelected(PresCardScorebug,_activeScreenKey=="scorebug");
        OperatorChoiceMotion.SetEnabled(PresCardScorebug,true);
        InstallLiveScorebugCardOwnership();UpdateAflLiveScorebugCardSelection(_activeScreenKey=="scorebug");
    }
    private void InstallLiveScorebugCardOwnership()
    {
        if(_liveScorebugCardHooks)return;_liveScorebugCardHooks=true;
        PresCardScorebug.Loaded+=LiveScorebugCardLoaded;PresCardScorebug.Unloaded+=LiveScorebugCardUnloaded;
        PresCardScorebug.IsVisibleChanged+=LiveScorebugAvailabilityChanged;
        PresCardScorebug.IsEnabledChanged+=LiveScorebugAvailabilityChanged;
        IsVisibleChanged+=LiveScorebugAvailabilityChanged;IsEnabledChanged+=LiveScorebugAvailabilityChanged;
        MainContent.IsVisibleChanged+=LiveScorebugAvailabilityChanged;MainContent.IsEnabledChanged+=LiveScorebugAvailabilityChanged;
        MainTabControl.SelectionChanged+=LiveScorebugTabChanged;
        Closed+=LiveScorebugOwnerClosed;
        if(PresCardScorebug.IsLoaded)LiveScorebugCardLoaded(PresCardScorebug,new RoutedEventArgs());
    }
    private void LiveScorebugOwnerClosed(object? sender,EventArgs args)
    {
        _liveScorebugOwnerClosed=true;DetachLiveScorebugSystemPolicy();
        OperatorChoiceMotion.SetEnabled(PresCardScorebug,false);
        Closed-=LiveScorebugOwnerClosed;
    }
    private void LiveScorebugCardLoaded(object? sender,RoutedEventArgs args)
    {
        if(_liveScorebugOwnerClosed)return;
        if(!_liveScorebugPolicyAttached){SystemParameters.StaticPropertyChanged+=LiveScorebugSystemPolicyChanged;_liveScorebugPolicyAttached=true;}
        SettleAflLiveScorebugCard();
    }
    private void LiveScorebugCardUnloaded(object? sender,RoutedEventArgs args){DetachLiveScorebugSystemPolicy();SettleAflLiveScorebugCard();}
    private void DetachLiveScorebugSystemPolicy(){SystemParameters.StaticPropertyChanged-=LiveScorebugSystemPolicyChanged;_liveScorebugPolicyAttached=false;}
    private void LiveScorebugSystemPolicyChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(args.PropertyName!=nameof(SystemParameters.ClientAreaAnimation))return;
        if(Dispatcher.CheckAccess()){if(!_liveScorebugOwnerClosed&&!OperatorMotion.Enabled)SettleAflLiveScorebugCard();}
        else Dispatcher.BeginInvoke(new Action(()=>{if(!_liveScorebugOwnerClosed&&_liveScorebugPolicyAttached&&!OperatorMotion.Enabled)SettleAflLiveScorebugCard();}));
    }
    private void LiveScorebugAvailabilityChanged(object? sender,DependencyPropertyChangedEventArgs args)
    {if(args.Property.Name is nameof(UIElement.IsVisible) or nameof(UIElement.IsEnabled))SettleAflLiveScorebugCard();}
    private void LiveScorebugTabChanged(object? sender,SelectionChangedEventArgs args)
    {if(args.Source==MainTabControl)SettleAflLiveScorebugCard();}
    private void SettleAflLiveScorebugCard(bool? requestedSelection=null)
    {
        // Synchronous refresh callers may paint before assigning the backing
        // screen field. Lifecycle callbacks still settle from current authority.
        bool selected=requestedSelection??(_activeScreenKey=="scorebug");OperatorChoiceMotion.SetSelected(PresCardScorebug,selected);
        PresCardScorebug.BeginAnimation(OperatorChoiceMotion.ProgressProperty,null);OperatorChoiceMotion.SetProgress(PresCardScorebug,selected?1:0);
    }
    private void UpdateAflLiveScorebugCardSelection(bool selected)
    {
        PresCardScorebug.SetResourceReference(Border.BackgroundProperty,selected?"ControlCentre.SelectionBrush":"ControlCentre.RaisedBrush");
        PresCardScorebug.SetResourceReference(Border.BorderBrushProperty,selected?"ControlCentre.AccentBrush":"ControlCentre.BorderBrush");
        PresCardScorebug.BorderThickness=new Thickness(2);
        PresCheckScorebug.Visibility=Visibility.Visible;PresCheckScorebug.Opacity=selected?1:0;
        PresCheckScorebug.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SelectionBrush");
        if(PresCheckScorebug.Child is TextBlock tick)tick.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SelectedTextBrush");
        PresGlowScorebug.Opacity=0;
        foreach(var title in FindVisualChildren<TextBlock>(PresCardScorebug).Where(text=>text.Name=="LiveScorebugCardTitle"))
            title.SetResourceReference(TextBlock.ForegroundProperty,selected?"ControlCentre.SelectedTextBrush":"ControlCentre.TextBrush");
        _aflLiveScorebugGlyph?.SetSelectedInk(selected);
        System.Windows.Automation.AutomationProperties.SetName(PresCardScorebug,"Live scorebug");
        System.Windows.Automation.AutomationProperties.SetItemStatus(PresCardScorebug,selected?"Selected":"Not selected");
        System.Windows.Automation.AutomationProperties.SetHelpText(PresCardScorebug,
            "Choose the live scorebug presentation. Selection does not by itself mean the output window is open.");
        OperatorChoiceMotion.SetSelected(PresCardScorebug,selected);
        bool animate=!_liveScorebugOwnerClosed&&OperatorMotion.Enabled&&IsVisible&&IsEnabled&&MainContent.IsVisible&&MainContent.IsEnabled
            &&ReferenceEquals(MainTabControl.SelectedItem,OutputStudioTab)&&PresCardScorebug.IsLoaded&&PresCardScorebug.IsVisible&&PresCardScorebug.IsEnabled
            &&!OperatorInputFocus.GetIsKeyboardMode(PresCardScorebug);
        if(!animate)SettleAflLiveScorebugCard(selected);
    }
}

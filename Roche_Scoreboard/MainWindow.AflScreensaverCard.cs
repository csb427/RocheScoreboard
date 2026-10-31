// OUTSIDE CANDIDATE — UNCOMPILED/UNRUN; root owns integration/build/generation.
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Border=System.Windows.Controls.Border;
using Binding=System.Windows.Data.Binding;

namespace Roche_Scoreboard;

// Screensaver selection parts follow the original presentation authority.
public partial class MainWindow
{
    private AflScreensaverGlyph? _aflScreensaverGlyph;
    private bool _screensaverCardHooks,_screensaverPolicyAttached,_screensaverOwnerClosed;
    private void InstallAflScreensaverCardVisual()
    {
        if(_aflScreensaverGlyph is not null)return;
        if(PresCardScreensaver.Child is not Grid grid)return;
        foreach(var old in grid.Children.OfType<StackPanel>())old.Visibility=Visibility.Collapsed;
        var body=new StackPanel{Name="ScreensaverCardBody",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,16,12,16),IsHitTestVisible=false};
        _aflScreensaverGlyph=new AflScreensaverGlyph{HorizontalAlignment=System.Windows.HorizontalAlignment.Center,Margin=new Thickness(0,0,0,10)};
        var item=PresCardScreensaver;var selection=_aflScreensaverGlyph.SelectionBridge;
        // Exact existing attached-progress bridge; ordinary CLR binding then
        // drives these authored parts without a new clock or parent-tab owner.
        selection.SetBinding(OperatorChoiceSurface.ProgressProperty,new System.Windows.Data.Binding
            { Source=item,Path=new PropertyPath(OperatorChoiceMotion.ProgressProperty) });
        _aflScreensaverGlyph.SetBinding(AflScreensaverGlyph.ProgressProperty,new Binding(nameof(OperatorChoiceSurface.Progress)){Source=selection});
        body.Children.Add(_aflScreensaverGlyph);
        var title=new TextBlock{Name="ScreensaverCardTitle",Text="Screensaver",FontSize=16,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap};
        title.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");body.Children.Add(title);grid.Children.Add(body);
        PresCardScreensaver.CornerRadius=new CornerRadius(24);grid.MinHeight=116;
        PresCheckScreensaver.IsHitTestVisible=false;
        // Establish current selection before enabling motion: no restore/show
        // celebration, including an already-selected off-air presentation.
        OperatorChoiceMotion.SetSelected(PresCardScreensaver,_activeScreenKey=="screensaver");
        OperatorChoiceMotion.SetEnabled(PresCardScreensaver,true);
        InstallScreensaverCardOwnership();UpdateAflScreensaverCardSelection(_activeScreenKey=="screensaver");
    }
    private void InstallScreensaverCardOwnership()
    {
        if(_screensaverCardHooks)return;_screensaverCardHooks=true;
        PresCardScreensaver.Loaded+=ScreensaverCardLoaded;PresCardScreensaver.Unloaded+=ScreensaverCardUnloaded;
        PresCardScreensaver.IsVisibleChanged+=ScreensaverAvailabilityChanged;
        PresCardScreensaver.IsEnabledChanged+=ScreensaverAvailabilityChanged;
        IsVisibleChanged+=ScreensaverAvailabilityChanged;IsEnabledChanged+=ScreensaverAvailabilityChanged;
        MainContent.IsVisibleChanged+=ScreensaverAvailabilityChanged;MainContent.IsEnabledChanged+=ScreensaverAvailabilityChanged;
        MainTabControl.SelectionChanged+=ScreensaverTabChanged;
        Closed+=ScreensaverOwnerClosed;
        if(PresCardScreensaver.IsLoaded)ScreensaverCardLoaded(PresCardScreensaver,new RoutedEventArgs());
    }
    private void ScreensaverOwnerClosed(object? sender,EventArgs args)
    {
        _screensaverOwnerClosed=true;DetachScreensaverSystemPolicy();
        OperatorChoiceMotion.SetEnabled(PresCardScreensaver,false);
        Closed-=ScreensaverOwnerClosed;
    }
    private void ScreensaverCardLoaded(object? sender,RoutedEventArgs args)
    {
        if(_screensaverOwnerClosed)return;
        if(!_screensaverPolicyAttached){SystemParameters.StaticPropertyChanged+=ScreensaverSystemPolicyChanged;_screensaverPolicyAttached=true;}
        SettleAflScreensaverCard();
    }
    private void ScreensaverCardUnloaded(object? sender,RoutedEventArgs args){DetachScreensaverSystemPolicy();SettleAflScreensaverCard();}
    private void DetachScreensaverSystemPolicy(){SystemParameters.StaticPropertyChanged-=ScreensaverSystemPolicyChanged;_screensaverPolicyAttached=false;}
    private void ScreensaverSystemPolicyChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(args.PropertyName!=nameof(SystemParameters.ClientAreaAnimation))return;
        if(Dispatcher.CheckAccess()){if(!_screensaverOwnerClosed&&!OperatorMotion.Enabled)SettleAflScreensaverCard();}
        else Dispatcher.BeginInvoke(new Action(()=>{if(!_screensaverOwnerClosed&&_screensaverPolicyAttached&&!OperatorMotion.Enabled)SettleAflScreensaverCard();}));
    }
    private void ScreensaverAvailabilityChanged(object? sender,DependencyPropertyChangedEventArgs args)
    {if(args.Property.Name is nameof(UIElement.IsVisible) or nameof(UIElement.IsEnabled))SettleAflScreensaverCard();}
    private void ScreensaverTabChanged(object? sender,SelectionChangedEventArgs args)
    {if(args.Source==MainTabControl)SettleAflScreensaverCard();}
    private void SettleAflScreensaverCard(bool? requestedSelection=null)
    {
        // Synchronous refresh callers may paint before assigning the backing
        // screen field. Lifecycle callbacks still settle from current authority.
        bool selected=requestedSelection??(_activeScreenKey=="screensaver");OperatorChoiceMotion.SetSelected(PresCardScreensaver,selected);
        PresCardScreensaver.BeginAnimation(OperatorChoiceMotion.ProgressProperty,null);OperatorChoiceMotion.SetProgress(PresCardScreensaver,selected?1:0);
    }
    private void UpdateAflScreensaverCardSelection(bool selected)
    {
        PresCardScreensaver.SetResourceReference(Border.BackgroundProperty,selected?"ControlCentre.SelectionBrush":"ControlCentre.RaisedBrush");
        PresCardScreensaver.SetResourceReference(Border.BorderBrushProperty,selected?"ControlCentre.AccentBrush":"ControlCentre.BorderBrush");
        PresCardScreensaver.BorderThickness=new Thickness(2);
        PresCheckScreensaver.Visibility=Visibility.Visible;PresCheckScreensaver.Opacity=selected?1:0;
        PresCheckScreensaver.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SelectionBrush");
        if(PresCheckScreensaver.Child is TextBlock tick)tick.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SelectedTextBrush");
        foreach(var title in FindVisualChildren<TextBlock>(PresCardScreensaver).Where(text=>text.Name=="ScreensaverCardTitle"))
            title.SetResourceReference(TextBlock.ForegroundProperty,selected?"ControlCentre.SelectedTextBrush":"ControlCentre.TextBrush");
        _aflScreensaverGlyph?.SetSelectedInk(selected);
        System.Windows.Automation.AutomationProperties.SetName(PresCardScreensaver,"Screensaver");
        System.Windows.Automation.AutomationProperties.SetItemStatus(PresCardScreensaver,selected?"Selected":"Not selected");
        System.Windows.Automation.AutomationProperties.SetHelpText(PresCardScreensaver,
            "Choose the configured venue standby presentation after opening output. Selection does not by itself mean the output window is open.");
        OperatorChoiceMotion.SetSelected(PresCardScreensaver,selected);
        bool animate=!_screensaverOwnerClosed&&OperatorMotion.Enabled&&IsVisible&&IsEnabled&&MainContent.IsVisible&&MainContent.IsEnabled
            &&ReferenceEquals(MainTabControl.SelectedItem,OutputStudioTab)&&PresCardScreensaver.IsLoaded&&PresCardScreensaver.IsVisible&&PresCardScreensaver.IsEnabled
            &&!OperatorInputFocus.GetIsKeyboardMode(PresCardScreensaver);
        if(!animate)SettleAflScreensaverCard(selected);
    }
}

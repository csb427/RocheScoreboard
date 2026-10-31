// Generated from MainWindow.AflMarginFlowCard.cs; edit translator/adapters.
// Source SHA-256: 1a214f19bf18609e972af06778ae7e4abd173d172fce5fc51e051dc61005240a
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

// Margin-flow selection parts follow the original presentation authority.
public partial class MainWindow
{
    private AflMarginFlowGlyph? _aflMarginFlowGlyph;
    private bool _marginFlowCardHooks,_marginFlowPolicyAttached,_marginFlowOwnerClosed;
    private void InstallAflMarginFlowCardVisual()
    {
        if(_aflMarginFlowGlyph is not null)return;
        if(PresCardWorm.Child is not Grid grid)return;
        foreach(var old in grid.Children.OfType<StackPanel>())old.IsVisible=false;
        var body=new StackPanel{Name="MarginFlowCardBody",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,16,12,16),IsHitTestVisible=false};
        _aflMarginFlowGlyph=new AflMarginFlowGlyph{HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center,Margin=new Thickness(0,0,0,10)};
        var item=PresCardWorm;var selection=_aflMarginFlowGlyph.SelectionBridge;
        // Exact existing attached-progress bridge; ordinary CLR binding then
        // drives these authored parts without a new clock or parent-tab owner.
        selection.Bind(OperatorChoiceSurface.ProgressProperty,item.GetObservable(OperatorChoiceMotion.ProgressProperty));
        _aflMarginFlowGlyph.SetBinding(AflMarginFlowGlyph.ProgressProperty,new Binding(nameof(OperatorChoiceSurface.Progress)){Source=selection});
        body.Children.Add(_aflMarginFlowGlyph);
        var title=new NativeTextBlock{Name="MarginFlowCardTitle",Text="Margin flow",FontSize=16,FontWeight=FontWeight.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap};
        title.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");body.Children.Add(title);grid.Children.Add(body);
        PresCardWorm.CornerRadius=new CornerRadius(24);grid.MinHeight=116;
        PresCheckWorm.IsHitTestVisible=false;
        // Establish current selection before enabling motion: no restore/show
        // celebration, including an already-selected off-air presentation.
        OperatorChoiceMotion.SetSelected(PresCardWorm,_activeScreenKey=="worm");
        OperatorChoiceMotion.SetEnabled(PresCardWorm,true);
        InstallMarginFlowCardOwnership();UpdateAflMarginFlowCardSelection(_activeScreenKey=="worm");
    }
    private void InstallMarginFlowCardOwnership()
    {
        if(_marginFlowCardHooks)return;_marginFlowCardHooks=true;
        PresCardWorm.Loaded+=MarginFlowCardLoaded;PresCardWorm.Unloaded+=MarginFlowCardUnloaded;
        NativeEvents.WatchProperty(PresCardWorm,Avalonia.Controls.Control.IsVisibleProperty,true,MarginFlowAvailabilityChanged);
        NativeEvents.WatchProperty(PresCardWorm,Avalonia.Controls.Control.IsEnabledProperty,true,MarginFlowAvailabilityChanged);
        NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsVisibleProperty,true,MarginFlowAvailabilityChanged);NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsEnabledProperty,true,MarginFlowAvailabilityChanged);
        NativeEvents.WatchProperty(MainContent,Avalonia.Controls.Control.IsVisibleProperty,true,MarginFlowAvailabilityChanged);NativeEvents.WatchProperty(MainContent,Avalonia.Controls.Control.IsEnabledProperty,true,MarginFlowAvailabilityChanged);
        MainTabControl.SelectionChanged+=MarginFlowTabChanged;
        Closed+=MarginFlowOwnerClosed;
        if(PresCardWorm.IsLoaded)MarginFlowCardLoaded(PresCardWorm,new RoutedEventArgs());
    }
    private void MarginFlowOwnerClosed(object? sender,EventArgs args)
    {
        _marginFlowOwnerClosed=true;DetachMarginFlowSystemPolicy();
        OperatorChoiceMotion.SetEnabled(PresCardWorm,false);
        Closed-=MarginFlowOwnerClosed;
    }
    private void MarginFlowCardLoaded(object? sender,RoutedEventArgs args)
    {
        if(_marginFlowOwnerClosed)return;
        if(!_marginFlowPolicyAttached){NativeSystemPreferences.StaticPropertyChanged+=MarginFlowSystemPolicyChanged;_marginFlowPolicyAttached=true;}
        SettleAflMarginFlowCard();
    }
    private void MarginFlowCardUnloaded(object? sender,RoutedEventArgs args){DetachMarginFlowSystemPolicy();SettleAflMarginFlowCard();}
    private void DetachMarginFlowSystemPolicy(){NativeSystemPreferences.StaticPropertyChanged-=MarginFlowSystemPolicyChanged;_marginFlowPolicyAttached=false;}
    private void MarginFlowSystemPolicyChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(args.PropertyName!=nameof(NativeSystemPreferences.ClientAreaAnimation))return;
        if(Dispatcher.UIThread.CheckAccess()){if(!_marginFlowOwnerClosed&&!OperatorMotion.Enabled)SettleAflMarginFlowCard();}
        else Dispatcher.UIThread.Post(new Action(()=>{if(!_marginFlowOwnerClosed&&_marginFlowPolicyAttached&&!OperatorMotion.Enabled)SettleAflMarginFlowCard();}));
    }
    private void MarginFlowAvailabilityChanged(object? sender,AvaloniaPropertyChangedEventArgs args)
    {if(args.Property.Name is nameof(Control.IsVisible) or nameof(Control.IsEnabled))SettleAflMarginFlowCard();}
    private void MarginFlowTabChanged(object? sender,SelectionChangedEventArgs args)
    {if(args.Source==MainTabControl)SettleAflMarginFlowCard();}
    private void SettleAflMarginFlowCard(bool? requestedSelection=null)
    {
        // Synchronous refresh callers may paint before assigning the backing
        // screen field. Lifecycle callbacks still settle from current authority.
        bool selected=requestedSelection??(_activeScreenKey=="worm");OperatorChoiceMotion.SetSelected(PresCardWorm,selected);
        PresCardWorm.BeginAnimation(OperatorChoiceMotion.ProgressProperty,null);OperatorChoiceMotion.SetProgress(PresCardWorm,selected?1:0);
    }
    private void UpdateAflMarginFlowCardSelection(bool selected)
    {
        PresCardWorm.SetResourceReference(Border.BackgroundProperty,selected?"ControlCentre.SelectionBrush":"ControlCentre.RaisedBrush");
        PresCardWorm.SetResourceReference(Border.BorderBrushProperty,selected?"ControlCentre.AccentBrush":"ControlCentre.BorderBrush");
        PresCardWorm.BorderThickness=new Thickness(2);
        PresCheckWorm.IsVisible=true;PresCheckWorm.Opacity=selected?1:0;
        PresCheckWorm.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SelectionBrush");
        if(PresCheckWorm.Child is TextBlock tick)tick.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SelectedTextBrush");
        foreach(var title in FindVisualChildren<TextBlock>(PresCardWorm).Where(text=>(text.Name ?? string.Empty)=="MarginFlowCardTitle"))
            title.SetResourceReference(TextBlock.ForegroundProperty,selected?"ControlCentre.SelectedTextBrush":"ControlCentre.TextBrush");
        _aflMarginFlowGlyph?.SetSelectedInk(selected);
        Avalonia.Automation.AutomationProperties.SetName(PresCardWorm,"Margin flow");
        Avalonia.Automation.AutomationProperties.SetItemStatus(PresCardWorm,selected?"Selected":"Not selected");
        Avalonia.Automation.AutomationProperties.SetHelpText(PresCardWorm,
            "Choose the score-margin history by scoring event. Selection does not by itself mean the output window is open.");
        OperatorChoiceMotion.SetSelected(PresCardWorm,selected);
        bool animate=!_marginFlowOwnerClosed&&OperatorMotion.Enabled&&IsVisible&&IsEnabled&&MainContent.IsVisible&&MainContent.IsEnabled
            &&ReferenceEquals(MainTabControl.SelectedItem,OutputStudioTab)&&PresCardWorm.IsLoaded&&PresCardWorm.IsVisible&&PresCardWorm.IsEnabled
            &&!OperatorInputFocus.GetIsKeyboardMode(PresCardWorm);
        if(!animate)SettleAflMarginFlowCard(selected);
    }
}

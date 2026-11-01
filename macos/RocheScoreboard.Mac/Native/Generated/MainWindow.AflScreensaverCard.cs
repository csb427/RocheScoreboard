// Generated from MainWindow.AflScreensaverCard.cs; edit translator/adapters.
// Source SHA-256: 18157ab6f338880acbae714056d547887e06c7f4d5a1ecd03a21b6739fa9b3ae
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
// OUTSIDE CANDIDATE — UNCOMPILED/UNRUN; root owns integration/build/generation.
using System.ComponentModel;
using Avalonia.Data;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Border=Avalonia.Controls.Border;
using Binding=Avalonia.Data.Binding;

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
        foreach(var old in grid.Children.OfType<StackPanel>())old.IsVisible=false;
        var body=new StackPanel{Name="ScreensaverCardBody",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,16,12,16),IsHitTestVisible=false};
        _aflScreensaverGlyph=new AflScreensaverGlyph{HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center,Margin=new Thickness(0,0,0,10)};
        var item=PresCardScreensaver;var selection=_aflScreensaverGlyph.SelectionBridge;
        // Exact existing attached-progress bridge; ordinary CLR binding then
        // drives these authored parts without a new clock or parent-tab owner.
        selection.Bind(OperatorChoiceSurface.ProgressProperty,item.GetObservable(OperatorChoiceMotion.ProgressProperty));
        _aflScreensaverGlyph.SetBinding(AflScreensaverGlyph.ProgressProperty,new Binding(nameof(OperatorChoiceSurface.Progress)){Source=selection});
        body.Children.Add(_aflScreensaverGlyph);
        var title=new NativeTextBlock{Name="ScreensaverCardTitle",Text="Screensaver",FontSize=16,FontWeight=FontWeight.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap};
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
        NativeEvents.WatchProperty(PresCardScreensaver,Avalonia.Controls.Control.IsVisibleProperty,true,ScreensaverAvailabilityChanged);
        NativeEvents.WatchProperty(PresCardScreensaver,Avalonia.Controls.Control.IsEnabledProperty,true,ScreensaverAvailabilityChanged);
        NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsVisibleProperty,true,ScreensaverAvailabilityChanged);NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsEnabledProperty,true,ScreensaverAvailabilityChanged);
        NativeEvents.WatchProperty(MainContent,Avalonia.Controls.Control.IsVisibleProperty,true,ScreensaverAvailabilityChanged);NativeEvents.WatchProperty(MainContent,Avalonia.Controls.Control.IsEnabledProperty,true,ScreensaverAvailabilityChanged);
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
        if(!_screensaverPolicyAttached){NativeSystemPreferences.StaticPropertyChanged+=ScreensaverSystemPolicyChanged;_screensaverPolicyAttached=true;}
        SettleAflScreensaverCard();
    }
    private void ScreensaverCardUnloaded(object? sender,RoutedEventArgs args){DetachScreensaverSystemPolicy();SettleAflScreensaverCard();}
    private void DetachScreensaverSystemPolicy(){NativeSystemPreferences.StaticPropertyChanged-=ScreensaverSystemPolicyChanged;_screensaverPolicyAttached=false;}
    private void ScreensaverSystemPolicyChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(args.PropertyName!=nameof(NativeSystemPreferences.ClientAreaAnimation))return;
        if(Dispatcher.UIThread.CheckAccess()){if(!_screensaverOwnerClosed&&!OperatorMotion.Enabled)SettleAflScreensaverCard();}
        else Dispatcher.UIThread.Post(new Action(()=>{if(!_screensaverOwnerClosed&&_screensaverPolicyAttached&&!OperatorMotion.Enabled)SettleAflScreensaverCard();}));
    }
    private void ScreensaverAvailabilityChanged(object? sender,AvaloniaPropertyChangedEventArgs args)
    {if(args.Property.Name is nameof(Control.IsVisible) or nameof(Control.IsEnabled))SettleAflScreensaverCard();}
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
        PresCheckScreensaver.IsVisible=true;PresCheckScreensaver.Opacity=selected?1:0;
        PresCheckScreensaver.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SelectionBrush");
        if(PresCheckScreensaver.Child is TextBlock tick)tick.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SelectedTextBrush");
        foreach(var title in FindVisualChildren<TextBlock>(PresCardScreensaver).Where(text=>(text.Name ?? string.Empty)=="ScreensaverCardTitle"))
            title.SetResourceReference(TextBlock.ForegroundProperty,selected?"ControlCentre.SelectedTextBrush":"ControlCentre.TextBrush");
        _aflScreensaverGlyph?.SetSelectedInk(selected);
        Avalonia.Automation.AutomationProperties.SetName(PresCardScreensaver,"Screensaver");
        Avalonia.Automation.AutomationProperties.SetItemStatus(PresCardScreensaver,selected?"Selected":"Not selected");
        Avalonia.Automation.AutomationProperties.SetHelpText(PresCardScreensaver,
            "Choose the configured venue standby presentation after opening output. Selection does not by itself mean the output window is open.");
        OperatorChoiceMotion.SetSelected(PresCardScreensaver,selected);
        bool animate=!_screensaverOwnerClosed&&OperatorMotion.Enabled&&IsVisible&&IsEnabled&&MainContent.IsVisible&&MainContent.IsEnabled
            &&ReferenceEquals(MainTabControl.SelectedItem,OutputStudioTab)&&PresCardScreensaver.IsLoaded&&PresCardScreensaver.IsVisible&&PresCardScreensaver.IsEnabled
            &&!OperatorInputFocus.GetIsKeyboardMode(PresCardScreensaver);
        if(!animate)SettleAflScreensaverCard(selected);
    }
}

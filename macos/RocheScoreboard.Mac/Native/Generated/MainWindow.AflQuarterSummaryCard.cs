// Generated from MainWindow.AflQuarterSummaryCard.cs; edit translator/adapters.
// Source SHA-256: bc00a0677294e792f1f0bc50c4bce6d550302185bbcda245982a8f0b8f3cb80f
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

// Quarter-summary selection parts follow the original presentation authority.
public partial class MainWindow
{
    private AflQuarterSummaryGlyph? _aflQuarterSummaryGlyph;
    private bool _quarterSummaryCardHooks,_quarterSummaryPolicyAttached,_quarterSummaryOwnerClosed;
    private void InstallAflQuarterSummaryCardVisual()
    {
        if(_aflQuarterSummaryGlyph is not null)return;
        if(PresCardSummary.Child is not Grid grid)return;
        foreach(var old in grid.Children.OfType<StackPanel>())old.IsVisible=false;
        var body=new StackPanel{Name="QuarterSummaryCardBody",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,16,12,16),IsHitTestVisible=false};
        _aflQuarterSummaryGlyph=new AflQuarterSummaryGlyph{HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center,Margin=new Thickness(0,0,0,10)};
        var item=PresCardSummary;var selection=_aflQuarterSummaryGlyph.SelectionBridge;
        // Exact existing attached-progress bridge; ordinary CLR binding then
        // drives these authored parts without a new clock or parent-tab owner.
        selection.Bind(OperatorChoiceSurface.ProgressProperty,item.GetObservable(OperatorChoiceMotion.ProgressProperty));
        _aflQuarterSummaryGlyph.SetBinding(AflQuarterSummaryGlyph.ProgressProperty,new Binding(nameof(OperatorChoiceSurface.Progress)){Source=selection});
        body.Children.Add(_aflQuarterSummaryGlyph);
        var title=new NativeTextBlock{Name="QuarterSummaryCardTitle",Text="Quarter summary",FontSize=16,FontWeight=FontWeight.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap};
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
        NativeEvents.WatchProperty(PresCardSummary,Avalonia.Controls.Control.IsVisibleProperty,true,QuarterSummaryAvailabilityChanged);
        NativeEvents.WatchProperty(PresCardSummary,Avalonia.Controls.Control.IsEnabledProperty,true,QuarterSummaryAvailabilityChanged);
        NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsVisibleProperty,true,QuarterSummaryAvailabilityChanged);NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsEnabledProperty,true,QuarterSummaryAvailabilityChanged);
        NativeEvents.WatchProperty(MainContent,Avalonia.Controls.Control.IsVisibleProperty,true,QuarterSummaryAvailabilityChanged);NativeEvents.WatchProperty(MainContent,Avalonia.Controls.Control.IsEnabledProperty,true,QuarterSummaryAvailabilityChanged);
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
        if(!_quarterSummaryPolicyAttached){NativeSystemPreferences.StaticPropertyChanged+=QuarterSummarySystemPolicyChanged;_quarterSummaryPolicyAttached=true;}
        SettleAflQuarterSummaryCard();
    }
    private void QuarterSummaryCardUnloaded(object? sender,RoutedEventArgs args){DetachQuarterSummarySystemPolicy();SettleAflQuarterSummaryCard();}
    private void DetachQuarterSummarySystemPolicy(){NativeSystemPreferences.StaticPropertyChanged-=QuarterSummarySystemPolicyChanged;_quarterSummaryPolicyAttached=false;}
    private void QuarterSummarySystemPolicyChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(args.PropertyName!=nameof(NativeSystemPreferences.ClientAreaAnimation))return;
        if(Dispatcher.UIThread.CheckAccess()){if(!_quarterSummaryOwnerClosed&&!OperatorMotion.Enabled)SettleAflQuarterSummaryCard();}
        else Dispatcher.UIThread.Post(new Action(()=>{if(!_quarterSummaryOwnerClosed&&_quarterSummaryPolicyAttached&&!OperatorMotion.Enabled)SettleAflQuarterSummaryCard();}));
    }
    private void QuarterSummaryAvailabilityChanged(object? sender,AvaloniaPropertyChangedEventArgs args)
    {if(args.Property.Name is nameof(Control.IsVisible) or nameof(Control.IsEnabled))SettleAflQuarterSummaryCard();}
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
        PresCheckSummary.IsVisible=true;PresCheckSummary.Opacity=selected?1:0;
        PresCheckSummary.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SelectionBrush");
        if(PresCheckSummary.Child is TextBlock tick)tick.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SelectedTextBrush");
        foreach(var title in FindVisualChildren<TextBlock>(PresCardSummary).Where(text=>(text.Name ?? string.Empty)=="QuarterSummaryCardTitle"))
            title.SetResourceReference(TextBlock.ForegroundProperty,selected?"ControlCentre.SelectedTextBrush":"ControlCentre.TextBrush");
        _aflQuarterSummaryGlyph?.SetSelectedInk(selected);
        Avalonia.Automation.AutomationProperties.SetName(PresCardSummary,"Quarter summary");
        Avalonia.Automation.AutomationProperties.SetItemStatus(PresCardSummary,selected?"Selected":"Not selected");
        Avalonia.Automation.AutomationProperties.SetHelpText(PresCardSummary,
            "Choose the quarter-by-quarter summary for both teams. Selection does not by itself mean the output window is open.");
        OperatorChoiceMotion.SetSelected(PresCardSummary,selected);
        bool animate=!_quarterSummaryOwnerClosed&&OperatorMotion.Enabled&&IsVisible&&IsEnabled&&MainContent.IsVisible&&MainContent.IsEnabled
            &&ReferenceEquals(MainTabControl.SelectedItem,OutputStudioTab)&&PresCardSummary.IsLoaded&&PresCardSummary.IsVisible&&PresCardSummary.IsEnabled
            &&!OperatorInputFocus.GetIsKeyboardMode(PresCardSummary);
        if(!animate)SettleAflQuarterSummaryCard(selected);
    }
}

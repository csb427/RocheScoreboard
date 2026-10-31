// Generated from MainWindow.AflWeatherCard.cs; edit translator/adapters.
// Source SHA-256: 20bc6355d450420be94e9bbe92417b01ffa5a667ee2f0b9d04fdbcd1639de6de
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

// Schematic weather-selection parts follow the original presentation authority; no live forecast is inferred.
public partial class MainWindow
{
    private AflWeatherGlyph? _aflWeatherGlyph;
    private bool _weatherCardHooks,_weatherPolicyAttached,_weatherOwnerClosed;
    private void InstallAflWeatherCardVisual()
    {
        if(_aflWeatherGlyph is not null)return;
        if(PresCardWeather.Child is not Grid grid)return;
        foreach(var old in grid.Children.OfType<StackPanel>())old.IsVisible=false;
        var body=new StackPanel{Name="WeatherCardBody",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,16,12,16),IsHitTestVisible=false};
        _aflWeatherGlyph=new AflWeatherGlyph{HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center,Margin=new Thickness(0,0,0,10)};
        var item=PresCardWeather;var selection=_aflWeatherGlyph.SelectionBridge;
        // Exact existing attached-progress bridge; ordinary CLR binding then
        // drives these authored parts without a new clock or parent-tab owner.
        selection.Bind(OperatorChoiceSurface.ProgressProperty,item.GetObservable(OperatorChoiceMotion.ProgressProperty));
        _aflWeatherGlyph.SetBinding(AflWeatherGlyph.ProgressProperty,new Binding(nameof(OperatorChoiceSurface.Progress)){Source=selection});
        body.Children.Add(_aflWeatherGlyph);
        var title=new NativeTextBlock{Name="WeatherCardTitle",Text="Weather",FontSize=16,FontWeight=FontWeight.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap};
        title.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");body.Children.Add(title);grid.Children.Add(body);
        PresCardWeather.CornerRadius=new CornerRadius(24);grid.MinHeight=116;
        PresCheckWeather.IsHitTestVisible=false;
        // Establish current selection before enabling motion: no restore/show
        // celebration, including an already-selected off-air presentation.
        OperatorChoiceMotion.SetSelected(PresCardWeather,_activeScreenKey=="weather");
        OperatorChoiceMotion.SetEnabled(PresCardWeather,true);
        InstallWeatherCardOwnership();UpdateAflWeatherCardSelection(_activeScreenKey=="weather");
    }
    private void InstallWeatherCardOwnership()
    {
        if(_weatherCardHooks)return;_weatherCardHooks=true;
        PresCardWeather.Loaded+=WeatherCardLoaded;PresCardWeather.Unloaded+=WeatherCardUnloaded;
        NativeEvents.WatchProperty(PresCardWeather,Avalonia.Controls.Control.IsVisibleProperty,true,WeatherAvailabilityChanged);
        NativeEvents.WatchProperty(PresCardWeather,Avalonia.Controls.Control.IsEnabledProperty,true,WeatherAvailabilityChanged);
        NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsVisibleProperty,true,WeatherAvailabilityChanged);NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsEnabledProperty,true,WeatherAvailabilityChanged);
        NativeEvents.WatchProperty(MainContent,Avalonia.Controls.Control.IsVisibleProperty,true,WeatherAvailabilityChanged);NativeEvents.WatchProperty(MainContent,Avalonia.Controls.Control.IsEnabledProperty,true,WeatherAvailabilityChanged);
        MainTabControl.SelectionChanged+=WeatherTabChanged;
        Closed+=WeatherOwnerClosed;
        if(PresCardWeather.IsLoaded)WeatherCardLoaded(PresCardWeather,new RoutedEventArgs());
    }
    private void WeatherOwnerClosed(object? sender,EventArgs args)
    {
        _weatherOwnerClosed=true;DetachWeatherSystemPolicy();
        OperatorChoiceMotion.SetEnabled(PresCardWeather,false);
        Closed-=WeatherOwnerClosed;
    }
    private void WeatherCardLoaded(object? sender,RoutedEventArgs args)
    {
        if(_weatherOwnerClosed)return;
        if(!_weatherPolicyAttached){NativeSystemPreferences.StaticPropertyChanged+=WeatherSystemPolicyChanged;_weatherPolicyAttached=true;}
        SettleAflWeatherCard();
    }
    private void WeatherCardUnloaded(object? sender,RoutedEventArgs args){DetachWeatherSystemPolicy();SettleAflWeatherCard();}
    private void DetachWeatherSystemPolicy(){NativeSystemPreferences.StaticPropertyChanged-=WeatherSystemPolicyChanged;_weatherPolicyAttached=false;}
    private void WeatherSystemPolicyChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(args.PropertyName!=nameof(NativeSystemPreferences.ClientAreaAnimation))return;
        if(Dispatcher.UIThread.CheckAccess()){if(!_weatherOwnerClosed&&!OperatorMotion.Enabled)SettleAflWeatherCard();}
        else Dispatcher.UIThread.Post(new Action(()=>{if(!_weatherOwnerClosed&&_weatherPolicyAttached&&!OperatorMotion.Enabled)SettleAflWeatherCard();}));
    }
    private void WeatherAvailabilityChanged(object? sender,AvaloniaPropertyChangedEventArgs args)
    {if(args.Property.Name is nameof(Control.IsVisible) or nameof(Control.IsEnabled))SettleAflWeatherCard();}
    private void WeatherTabChanged(object? sender,SelectionChangedEventArgs args)
    {if(args.Source==MainTabControl)SettleAflWeatherCard();}
    private void SettleAflWeatherCard(bool? requestedSelection=null)
    {
        // Synchronous refresh callers may paint before assigning the backing
        // screen field. Lifecycle callbacks still settle from current authority.
        bool selected=requestedSelection??(_activeScreenKey=="weather");OperatorChoiceMotion.SetSelected(PresCardWeather,selected);
        PresCardWeather.BeginAnimation(OperatorChoiceMotion.ProgressProperty,null);OperatorChoiceMotion.SetProgress(PresCardWeather,selected?1:0);
    }
    private void UpdateAflWeatherCardSelection(bool selected)
    {
        PresCardWeather.SetResourceReference(Border.BackgroundProperty,selected?"ControlCentre.SelectionBrush":"ControlCentre.RaisedBrush");
        PresCardWeather.SetResourceReference(Border.BorderBrushProperty,selected?"ControlCentre.AccentBrush":"ControlCentre.BorderBrush");
        PresCardWeather.BorderThickness=new Thickness(2);
        PresCheckWeather.IsVisible=true;PresCheckWeather.Opacity=selected?1:0;
        PresCheckWeather.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SelectionBrush");
        if(PresCheckWeather.Child is TextBlock tick)tick.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SelectedTextBrush");
        foreach(var title in FindVisualChildren<TextBlock>(PresCardWeather).Where(text=>(text.Name ?? string.Empty)=="WeatherCardTitle"))
            title.SetResourceReference(TextBlock.ForegroundProperty,selected?"ControlCentre.SelectedTextBrush":"ControlCentre.TextBrush");
        _aflWeatherGlyph?.SetSelectedInk(selected);
        Avalonia.Automation.AutomationProperties.SetName(PresCardWeather,"Weather");
        Avalonia.Automation.AutomationProperties.SetItemStatus(PresCardWeather,selected?"Selected":"Not selected");
        Avalonia.Automation.AutomationProperties.SetHelpText(PresCardWeather,
            "Choose the weather presentation. It shows available weather data or an unavailable notice. Show output separately when ready.");
        OperatorChoiceMotion.SetSelected(PresCardWeather,selected);
        bool animate=!_weatherOwnerClosed&&OperatorMotion.Enabled&&IsVisible&&IsEnabled&&MainContent.IsVisible&&MainContent.IsEnabled
            &&ReferenceEquals(MainTabControl.SelectedItem,OutputStudioTab)&&PresCardWeather.IsLoaded&&PresCardWeather.IsVisible&&PresCardWeather.IsEnabled
            &&!OperatorInputFocus.GetIsKeyboardMode(PresCardWeather);
        if(!animate)SettleAflWeatherCard(selected);
    }
}

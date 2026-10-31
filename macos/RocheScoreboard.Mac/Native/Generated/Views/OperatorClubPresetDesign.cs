// Generated from Views/OperatorClubPresetDesign.cs; edit translator/adapters.
// Source SHA-256: d17b13bf36a9bbd7a1efe4fbf5ef8f8b7e2bbd0c7c91cf62c5f61261178d157f
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System.Runtime.CompilerServices;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Border=Avalonia.Controls.Border;
using TextBlock=Avalonia.Controls.TextBlock;
using Color=Avalonia.Media.Color;

namespace Roche_Scoreboard.Views;

internal static class OperatorClubPresetDesign
{
    private sealed class State
    {
        internal string Signature="",Colour="";
        internal Border? Mark;
        internal Avalonia.Controls.Shapes.Path? Check;
        internal ScaleTransform Pop=new(1,1);
        internal bool Selected,Hovered,Installed;
        private Border _tile=null!;
        private Window? _owner;
        private bool _watching,_ownerClosed;
        internal bool CanAnimate=>!_ownerClosed&&_tile.IsLoaded&&_tile.IsVisible&&_tile.IsEnabled
            &&(_owner is null||(_owner.IsVisible&&_owner.IsEnabled));
        internal void Install(Border tile)
        {
            _tile=tile;tile.Loaded+=Loaded;tile.Unloaded+=Unloaded;
            NativeEvents.WatchProperty(tile,Avalonia.Controls.Control.IsVisibleProperty,true,AvailabilityChanged);NativeEvents.WatchProperty(tile,Avalonia.Controls.Control.IsEnabledProperty,true,AvailabilityChanged);
            Refresh();
        }
        private void Loaded(object? sender,RoutedEventArgs args){_ownerClosed=false;Refresh();DrawSelection(this,_tile,Selected);}
        private void Unloaded(object? sender,RoutedEventArgs args){Suspend();DrawSelection(this,_tile,false);}
        private void AvailabilityChanged(object? sender,AvaloniaPropertyChangedEventArgs args)
        {Refresh();DrawSelection(this,_tile,false);}
        private void Refresh()
        {
            if(!_tile.IsLoaded){Suspend();return;}
            if(!_ownerClosed)
            {
                var owner=NativeWindow.GetWindow(_tile);
                if(!ReferenceEquals(owner,_owner))
                {
                    DetachOwner();_owner=owner;
                    if(owner is not null){NativeEvents.WatchProperty(owner,Avalonia.Controls.Control.IsVisibleProperty,true,AvailabilityChanged);NativeEvents.WatchProperty(owner,Avalonia.Controls.Control.IsEnabledProperty,true,AvailabilityChanged);owner.Closed+=OwnerClosed;}
                }
            }
            Watch(CanAnimate);
        }
        private void Watch(bool enabled)
        {
            if(enabled==_watching)return;_watching=enabled;
            if(enabled){OperatorAppearance.Changed+=PreferenceChanged;NativeSystemPreferences.StaticPropertyChanged+=SystemPreferenceChanged;}
            else{OperatorAppearance.Changed-=PreferenceChanged;NativeSystemPreferences.StaticPropertyChanged-=SystemPreferenceChanged;}
        }
        private void PreferenceChanged()
        {
            if(!_watching)return;
            if(!Dispatcher.UIThread.CheckAccess()){Dispatcher.UIThread.InvokeAsync(new Action(PreferenceChanged));return;}
            if(_watching&&CanAnimate&&!OperatorMotion.Enabled)DrawSelection(this,_tile,false);
        }
        private void SystemPreferenceChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs args)
        {if(args.PropertyName==nameof(NativeSystemPreferences.ClientAreaAnimation))PreferenceChanged();}
        private void OwnerClosed(object? sender,EventArgs args){_ownerClosed=true;Suspend();DrawSelection(this,_tile,false);}
        private void DetachOwner()
        {
            if(_owner is not null){NativeEvents.WatchProperty(_owner,Avalonia.Controls.Control.IsVisibleProperty,false,AvailabilityChanged);NativeEvents.WatchProperty(_owner,Avalonia.Controls.Control.IsEnabledProperty,false,AvailabilityChanged);_owner.Closed-=OwnerClosed;}
            _owner=null;
        }
        private void Suspend(){Watch(false);DetachOwner();}
    }
    private static readonly ConditionalWeakTable<Border,State> States=new();
    internal static void Update(Border tile,TeamPreset team,bool selected)
    {
        var state=States.GetOrCreateValue(tile);
        if(!state.Installed)
        {
            state.Installed=true;OperatorFeedback.SetManaged(tile,true);OperatorRoundedClip.SetRadius(tile,20);
            tile.SetBinding(NativeToolTipPolicy.EnabledProperty,new Avalonia.Data.Binding{Source=tile,Path=new PropertyPath(OperatorGuidance.ShowHintsProperty)});
            state.Install(tile);
            tile.GotFocus+=(_,_)=>Hover(tile,true);tile.LostFocus+=(_,_)=>Hover(tile,tile.IsPointerOver);
        }
        string signature=string.Join("|",team.TeamName,team.LogoPath,team.PrimaryColor);
        bool replaced=signature!=state.Signature;
        if(replaced)
        {
            state.Signature=signature;state.Colour=team.PrimaryColor;
            var content=new Grid{IsHitTestVisible=false};content.ColumnDefinitions.Add(new(){Width=new GridLength(32)});content.ColumnDefinitions.Add(new());
            var logo=TeamVisuals.LoadLogo(team.LogoPath);
            Control crest=logo is not null?new Avalonia.Controls.Image{Source=logo,Width=28,Height=34,Stretch=Stretch.Uniform}:new OperatorSymbol{Kind="teams",Width=25,Height=25,Foreground=Avalonia.Media.Brushes.White};content.Children.Add(crest);
            var name=new NativeTextBlock{Text=string.IsNullOrWhiteSpace(team.TeamName)?"Unnamed club":team.TeamName,FontSize=12,FontWeight=FontWeight.SemiBold,Foreground=Avalonia.Media.Brushes.White,TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,MaxHeight=34,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(5,0,13,0)};
            Grid.SetColumn(name,1);content.Children.Add(name);
            state.Check=new Avalonia.Controls.Shapes.Path{Tag="preset-selection-marker",Data=Geometry.Parse("M3,8 L6,11 L12,3"),Stroke=Avalonia.Media.Brushes.White,StrokeThickness=1.8,Width=15,Height=15,StrokeLineCap=PenLineCap.Round,StrokeDashArray=new Avalonia.Collections.AvaloniaList<double>{10,10}};
            state.Pop=new ScaleTransform(1,1);
            state.Mark=new NativeBorder{Width=18,Height=18,Padding=new Thickness(1),CornerRadius=new CornerRadius(9),HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Background=new SolidColorBrush(Color.FromRgb(27,134,80)),Child=state.Check,RenderTransform=state.Pop,RenderTransformOrigin=new RelativePoint(.5,.5,RelativeUnit.Relative)};
            Grid.SetColumnSpan(state.Mark,2);content.Children.Add(state.Mark);tile.Child=content;
        }
        tile.Width=152;tile.Height=62;tile.Padding=new Thickness(8);tile.CornerRadius=new CornerRadius(20);tile.BorderThickness=new Thickness(1);
        bool changed=state.Selected!=selected;state.Selected=selected;
        Paint(tile,state);
        if(changed||replaced)DrawSelection(state,tile,selected&&tile.IsLoaded);
        Avalonia.Automation.AutomationProperties.SetItemStatus(tile,selected?"Selected":"Not selected");
    }
    private static void Paint(Border tile,State state)
    {
        tile.BorderBrush=new SolidColorBrush(state.Selected?Color.FromRgb(99,231,165):state.Hovered?Color.FromArgb(200,230,239,249):Color.FromArgb(70,255,255,255));
        tile.Background=TeamVisuals.Panel(state.Colour,state.Colour,state.Hovered?.36:.28);
    }
    private static void DrawSelection(State state,Border tile,bool animate)
    {
        if(state.Mark is null||state.Check is null)return;
        state.Mark.BeginAnimation(Control.OpacityProperty,null);state.Mark.Opacity=state.Selected?1:0;
        state.Check.BeginAnimation(Avalonia.Controls.Shapes.Shape.StrokeDashOffsetProperty,null);state.Check.StrokeDashOffset=0;
        state.Pop.BeginAnimation(ScaleTransform.ScaleXProperty,null);state.Pop.BeginAnimation(ScaleTransform.ScaleYProperty,null);state.Pop.ScaleX=state.Pop.ScaleY=1;
        if(!animate||!state.Selected||!OperatorMotion.Enabled||!state.CanAnimate)return;
        state.Check.BeginAnimation(Avalonia.Controls.Shapes.Shape.StrokeDashOffsetProperty,new DoubleAnimation(10,0,OperatorMotion.CheckDraw){FillBehavior=FillBehavior.Stop,EasingFunction=OperatorMotion.SmoothOut});
        var arrival=new DoubleAnimation(.55,1,OperatorMotion.SurfaceOpen){FillBehavior=FillBehavior.Stop,EasingFunction=new BackEase{Amplitude=.2,EasingMode=EasingMode.EaseOut}};
        state.Pop.BeginAnimation(ScaleTransform.ScaleXProperty,arrival);state.Pop.BeginAnimation(ScaleTransform.ScaleYProperty,arrival.Clone());
    }
    internal static void Hover(Border tile,bool active)
    {
        active|=tile.IsKeyboardFocusWithin;
        if(!States.TryGetValue(tile,out var state)||state.Hovered==active)return;
        state.Hovered=active;Paint(tile,state);
    }
}

using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Border=System.Windows.Controls.Border;
using TextBlock=System.Windows.Controls.TextBlock;
using Color=System.Windows.Media.Color;

namespace Roche_Scoreboard.Views;

internal static class OperatorClubPresetDesign
{
    private sealed class State
    {
        internal string Signature="",Colour="";
        internal Border? Mark;
        internal System.Windows.Shapes.Path? Check;
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
            tile.IsVisibleChanged+=AvailabilityChanged;tile.IsEnabledChanged+=AvailabilityChanged;
            Refresh();
        }
        private void Loaded(object? sender,RoutedEventArgs args){_ownerClosed=false;Refresh();DrawSelection(this,_tile,Selected);}
        private void Unloaded(object? sender,RoutedEventArgs args){Suspend();DrawSelection(this,_tile,false);}
        private void AvailabilityChanged(object? sender,DependencyPropertyChangedEventArgs args)
        {Refresh();DrawSelection(this,_tile,false);}
        private void Refresh()
        {
            if(!_tile.IsLoaded){Suspend();return;}
            if(!_ownerClosed)
            {
                var owner=Window.GetWindow(_tile);
                if(!ReferenceEquals(owner,_owner))
                {
                    DetachOwner();_owner=owner;
                    if(owner is not null){owner.IsVisibleChanged+=AvailabilityChanged;owner.IsEnabledChanged+=AvailabilityChanged;owner.Closed+=OwnerClosed;}
                }
            }
            Watch(CanAnimate);
        }
        private void Watch(bool enabled)
        {
            if(enabled==_watching)return;_watching=enabled;
            if(enabled){OperatorAppearance.Changed+=PreferenceChanged;SystemParameters.StaticPropertyChanged+=SystemPreferenceChanged;}
            else{OperatorAppearance.Changed-=PreferenceChanged;SystemParameters.StaticPropertyChanged-=SystemPreferenceChanged;}
        }
        private void PreferenceChanged()
        {
            if(!_watching)return;
            if(!_tile.Dispatcher.CheckAccess()){_tile.Dispatcher.BeginInvoke(new Action(PreferenceChanged));return;}
            if(_watching&&CanAnimate&&!OperatorMotion.Enabled)DrawSelection(this,_tile,false);
        }
        private void SystemPreferenceChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs args)
        {if(args.PropertyName==nameof(SystemParameters.ClientAreaAnimation))PreferenceChanged();}
        private void OwnerClosed(object? sender,EventArgs args){_ownerClosed=true;Suspend();DrawSelection(this,_tile,false);}
        private void DetachOwner()
        {
            if(_owner is not null){_owner.IsVisibleChanged-=AvailabilityChanged;_owner.IsEnabledChanged-=AvailabilityChanged;_owner.Closed-=OwnerClosed;}
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
            tile.SetBinding(ToolTipService.IsEnabledProperty,new System.Windows.Data.Binding{Source=tile,Path=new PropertyPath(OperatorGuidance.ShowHintsProperty)});
            state.Install(tile);
            tile.GotKeyboardFocus+=(_,_)=>Hover(tile,true);tile.LostKeyboardFocus+=(_,_)=>Hover(tile,tile.IsMouseOver);
        }
        string signature=string.Join("|",team.TeamName,team.LogoPath,team.PrimaryColor);
        bool replaced=signature!=state.Signature;
        if(replaced)
        {
            state.Signature=signature;state.Colour=team.PrimaryColor;
            var content=new Grid{IsHitTestVisible=false};content.ColumnDefinitions.Add(new(){Width=new GridLength(32)});content.ColumnDefinitions.Add(new());
            var logo=TeamVisuals.LoadLogo(team.LogoPath);
            UIElement crest=logo is not null?new System.Windows.Controls.Image{Source=logo,Width=28,Height=34,Stretch=Stretch.Uniform}:new OperatorSymbol{Kind="teams",Width=25,Height=25,Foreground=System.Windows.Media.Brushes.White};content.Children.Add(crest);
            var name=new TextBlock{Text=string.IsNullOrWhiteSpace(team.TeamName)?"Unnamed club":team.TeamName,FontSize=12,FontWeight=FontWeights.SemiBold,Foreground=System.Windows.Media.Brushes.White,TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,MaxHeight=34,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(5,0,13,0)};
            Grid.SetColumn(name,1);content.Children.Add(name);
            state.Check=new System.Windows.Shapes.Path{Tag="preset-selection-marker",Data=Geometry.Parse("M3,8 L6,11 L12,3"),Stroke=System.Windows.Media.Brushes.White,StrokeThickness=1.8,Width=15,Height=15,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeDashArray=new DoubleCollection{10,10}};
            state.Pop=new ScaleTransform(1,1);
            state.Mark=new Border{Width=18,Height=18,Padding=new Thickness(1),CornerRadius=new CornerRadius(9),HorizontalAlignment=System.Windows.HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Background=new SolidColorBrush(Color.FromRgb(27,134,80)),Child=state.Check,RenderTransform=state.Pop,RenderTransformOrigin=new System.Windows.Point(.5,.5)};
            Grid.SetColumnSpan(state.Mark,2);content.Children.Add(state.Mark);tile.Child=content;
        }
        tile.Width=152;tile.Height=62;tile.Padding=new Thickness(8);tile.CornerRadius=new CornerRadius(20);tile.BorderThickness=new Thickness(1);
        bool changed=state.Selected!=selected;state.Selected=selected;
        Paint(tile,state);
        if(changed||replaced)DrawSelection(state,tile,selected&&tile.IsLoaded);
        System.Windows.Automation.AutomationProperties.SetItemStatus(tile,selected?"Selected":"Not selected");
    }
    private static void Paint(Border tile,State state)
    {
        tile.BorderBrush=new SolidColorBrush(state.Selected?Color.FromRgb(99,231,165):state.Hovered?Color.FromArgb(200,230,239,249):Color.FromArgb(70,255,255,255));
        tile.Background=TeamVisuals.Panel(state.Colour,state.Colour,state.Hovered?.36:.28);
    }
    private static void DrawSelection(State state,Border tile,bool animate)
    {
        if(state.Mark is null||state.Check is null)return;
        state.Mark.BeginAnimation(UIElement.OpacityProperty,null);state.Mark.Opacity=state.Selected?1:0;
        state.Check.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty,null);state.Check.StrokeDashOffset=0;
        state.Pop.BeginAnimation(ScaleTransform.ScaleXProperty,null);state.Pop.BeginAnimation(ScaleTransform.ScaleYProperty,null);state.Pop.ScaleX=state.Pop.ScaleY=1;
        if(!animate||!state.Selected||!OperatorMotion.Enabled||!state.CanAnimate)return;
        state.Check.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty,new DoubleAnimation(10,0,OperatorMotion.CheckDraw){FillBehavior=FillBehavior.Stop,EasingFunction=OperatorMotion.SmoothOut});
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

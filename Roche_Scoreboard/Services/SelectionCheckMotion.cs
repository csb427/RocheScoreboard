using System.Windows;
using System.Runtime.CompilerServices;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shape=System.Windows.Shapes.Shape;
using Control=System.Windows.Controls.Control;
using ListBoxItem=System.Windows.Controls.ListBoxItem;

namespace Roche_Scoreboard.Services;

/// <summary>Draw confirmation inside a fixed selection marker, without moving the choice.</summary>
public static class SelectionCheckMotion
{
    private static readonly ConditionalWeakTable<Control, PolicyState> States = new();
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(SelectionCheckMotion),new PropertyMetadata(false,Changed));
    private static readonly DependencyProperty GenerationProperty=DependencyProperty.RegisterAttached("Generation",typeof(int),typeof(SelectionCheckMotion),new PropertyMetadata(0));
    public static void SetEnabled(DependencyObject target,bool value)=>target.SetValue(EnabledProperty,value);
    public static bool GetEnabled(DependencyObject target)=>(bool)target.GetValue(EnabledProperty);
    private static void Changed(DependencyObject target,DependencyPropertyChangedEventArgs args)
    {
        if(target is not Control choice || choice is not (ToggleButton or ListBoxItem))return;
        if(choice is ToggleButton toggle){toggle.Checked-=Selected;toggle.Unchecked-=Selected;}
        if(choice is ListBoxItem item){item.Selected-=Selected;item.Unselected-=Selected;}
        choice.Loaded-=Loaded;
        choice.Unloaded-=Unloaded;choice.IsVisibleChanged-=VisibilityChanged;
        if(args.NewValue is not true)
        {
            if(States.TryGetValue(choice,out var previous)){previous.Dispose();States.Remove(choice);}
            Paint(choice,false);return;
        }
        if(choice is ToggleButton enabledToggle){enabledToggle.Checked+=Selected;enabledToggle.Unchecked+=Selected;}
        if(choice is ListBoxItem enabledItem){enabledItem.Selected+=Selected;enabledItem.Unselected+=Selected;}
        choice.Loaded+=Loaded;
        choice.Unloaded+=Unloaded;choice.IsVisibleChanged+=VisibilityChanged;
        States.GetValue(choice,target=>new PolicyState(target)).Refresh();
        if(choice.IsLoaded)Paint(choice,false);
    }
    private static bool IsSelected(Control choice)=>choice is ToggleButton{IsChecked:true} or ListBoxItem{IsSelected:true};
    private static void Selected(object sender,RoutedEventArgs args){if(ReferenceEquals(sender,args.OriginalSource))Paint((Control)sender,true);}
    private static void Loaded(object sender,RoutedEventArgs args)
    {
        var choice=(Control)sender;
        if(States.TryGetValue(choice,out var state))state.Loaded();
        Paint(choice,false);
    }
    private static void Unloaded(object sender,RoutedEventArgs args)
    {
        var choice=(Control)sender;
        if(States.TryGetValue(choice,out var state))state.Suspend();
        Paint(choice,false);
    }
    private static void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs args)
    {
        var choice=(Control)sender;
        if(States.TryGetValue(choice,out var state))state.Refresh();
        Paint(choice,false);
    }
    private sealed class PolicyState
    {
        private readonly Control _choice;
        private Window? _owner;
        private bool _watching, _disposed, _ownerClosed;
        internal PolicyState(Control choice){_choice=choice;choice.IsEnabledChanged+=AvailabilityChanged;}
        internal bool CanAnimate=>!_disposed&&!_ownerClosed&&GetEnabled(_choice)
            &&_choice.IsLoaded&&_choice.IsVisible&&_choice.IsEnabled
            &&(_owner is null||(_owner.IsVisible&&_owner.IsEnabled));
        internal void Loaded(){_ownerClosed=false;Refresh();}
        internal void Refresh()
        {
            if(_disposed)return;
            if(!_choice.IsLoaded){Suspend();return;}
            if(!_ownerClosed)
            {
                var owner=Window.GetWindow(_choice);
                if(!ReferenceEquals(owner,_owner))
                {
                    DetachOwner();_owner=owner;
                    if(owner is not null){owner.IsVisibleChanged+=AvailabilityChanged;owner.IsEnabledChanged+=AvailabilityChanged;owner.Closed+=OwnerClosed;}
                }
            }
            Watch(CanAnimate);
        }
        private void AvailabilityChanged(object? sender,DependencyPropertyChangedEventArgs args)
        {if(_disposed)return;Refresh();Paint(_choice,false);}
        private void Watch(bool watch)
        {
            if(watch==_watching)return;_watching=watch;
            if(watch){OperatorAppearance.Changed+=PreferenceChanged;SystemParameters.StaticPropertyChanged+=SystemPreferenceChanged;}
            else{OperatorAppearance.Changed-=PreferenceChanged;SystemParameters.StaticPropertyChanged-=SystemPreferenceChanged;}
        }
        private void SystemPreferenceChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs args)
        {if(args.PropertyName==nameof(SystemParameters.ClientAreaAnimation))PreferenceChanged();}
        private void PreferenceChanged()
        {
            if(!_watching||_disposed)return;
            if(!_choice.Dispatcher.CheckAccess()){_choice.Dispatcher.BeginInvoke(new Action(PreferenceChanged));return;}
            if(!_watching||!CanAnimate||OperatorMotion.Enabled)return;
            Paint(_choice,false);
        }
        private void OwnerClosed(object? sender,EventArgs args)
        {_ownerClosed=true;Suspend();Paint(_choice,false);}
        private void DetachOwner()
        {
            if(_owner is not null){_owner.IsVisibleChanged-=AvailabilityChanged;_owner.IsEnabledChanged-=AvailabilityChanged;_owner.Closed-=OwnerClosed;}
            _owner=null;
        }
        internal void Suspend(){Watch(false);DetachOwner();}
        internal void Dispose(){_disposed=true;_choice.IsEnabledChanged-=AvailabilityChanged;Suspend();}
    }
    private static void Paint(Control choice,bool animate)
    {
        int generation=(int)choice.GetValue(GenerationProperty)+1;choice.SetValue(GenerationProperty,generation);
        choice.ApplyTemplate();
        if(choice.Template?.FindName("SelectionStroke",choice) is not Shape check)return;
        check.BeginAnimation(Shape.StrokeDashOffsetProperty,null);
        check.StrokeDashOffset=IsSelected(choice)?0:7;
        if(choice.Template.FindName("SelectionPop",choice) is not ScaleTransform pop)return;
        pop.BeginAnimation(ScaleTransform.ScaleXProperty,null);pop.BeginAnimation(ScaleTransform.ScaleYProperty,null);pop.ScaleX=pop.ScaleY=1;
        if(!animate||!IsSelected(choice)||!States.TryGetValue(choice,out var state)||!state.CanAnimate||!OperatorMotion.Enabled)return;
        var draw=new DoubleAnimation(7,0,OperatorMotion.CheckDraw){FillBehavior=FillBehavior.Stop,EasingFunction=OperatorMotion.SmoothOut};
        draw.Completed+=(_,_)=>
        {
            if((int)choice.GetValue(GenerationProperty)!=generation)return;
            check.BeginAnimation(Shape.StrokeDashOffsetProperty,null);
            pop.BeginAnimation(ScaleTransform.ScaleXProperty,null);pop.BeginAnimation(ScaleTransform.ScaleYProperty,null);
        };
        check.BeginAnimation(Shape.StrokeDashOffsetProperty,draw);
        var arrival=new DoubleAnimation(.72,1,OperatorMotion.SurfaceOpen){FillBehavior=FillBehavior.Stop,EasingFunction=new BackEase{Amplitude=.25,EasingMode=EasingMode.EaseOut}};
        pop.BeginAnimation(ScaleTransform.ScaleXProperty,arrival);pop.BeginAnimation(ScaleTransform.ScaleYProperty,arrival.Clone());
    }
}

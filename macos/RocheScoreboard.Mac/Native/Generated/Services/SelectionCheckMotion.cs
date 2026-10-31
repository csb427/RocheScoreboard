// Generated from Services/SelectionCheckMotion.cs; edit translator/adapters.
// Source SHA-256: 5b7981e3bb5cd2a9ef33ac7a0e3070f198ba735ed02f44a62979f0c8287729de
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System.Runtime.CompilerServices;
using Shape=Avalonia.Controls.Shapes.Shape;
using TemplatedControl = Avalonia.Controls.Primitives.TemplatedControl;
using ListBoxItem=Avalonia.Controls.ListBoxItem;

namespace Roche_Scoreboard.Services;

/// <summary>Draw confirmation inside a fixed selection marker, without moving the choice.</summary>
public static class SelectionCheckMotion
{
    private static readonly ConditionalWeakTable<TemplatedControl, PolicyState> States = new();
    public static readonly AttachedProperty<bool> EnabledProperty=(AttachedProperty<bool>)NativeProperty.RegisterAttached("Enabled",typeof(bool),typeof(SelectionCheckMotion),new PropertyMetadata(false,Changed));
    private static readonly AttachedProperty<int> GenerationProperty=(AttachedProperty<int>)NativeProperty.RegisterAttached("Generation",typeof(int),typeof(SelectionCheckMotion),new PropertyMetadata(0));
    public static void SetEnabled(AvaloniaObject target,bool value)=>target.SetValue(EnabledProperty,value);
    public static bool GetEnabled(AvaloniaObject target)=>(bool)target.GetValue(EnabledProperty);
    private static void Changed(AvaloniaObject target,AvaloniaPropertyChangedEventArgs args)
    {
        if(target is not TemplatedControl choice || choice is not (ToggleButton or ListBoxItem))return;
        if(choice is ToggleButton toggle){toggle.Checked-=Selected;toggle.Unchecked-=Selected;}
        if(choice is ListBoxItem item){NativeEvents.WatchSelection(item,false,true,Selected);NativeEvents.WatchSelection(item,false,false,Selected);}
        choice.Loaded-=Loaded;
        choice.Unloaded-=Unloaded;NativeEvents.WatchProperty(choice,Avalonia.Controls.Control.IsVisibleProperty,false,VisibilityChanged);
        if(args.NewValue is not true)
        {
            if(States.TryGetValue(choice,out var previous)){previous.Dispose();States.Remove(choice);}
            Paint(choice,false);return;
        }
        if(choice is ToggleButton enabledToggle){enabledToggle.Checked+=Selected;enabledToggle.Unchecked+=Selected;}
        if(choice is ListBoxItem enabledItem){NativeEvents.WatchSelection(enabledItem,true,true,Selected);NativeEvents.WatchSelection(enabledItem,true,false,Selected);}
        choice.Loaded+=Loaded;
        choice.Unloaded+=Unloaded;NativeEvents.WatchProperty(choice,Avalonia.Controls.Control.IsVisibleProperty,true,VisibilityChanged);
        States.GetValue(choice,target=>new PolicyState(target)).Refresh();
        if(choice.IsLoaded)Paint(choice,false);
    }
    private static bool IsSelected(TemplatedControl choice)=>choice is ToggleButton{IsChecked:true} or ListBoxItem{IsSelected:true};
    private static void Selected(object sender,RoutedEventArgs args){if(ReferenceEquals(sender,args.Source))Paint((TemplatedControl)sender,true);}
    private static void Loaded(object sender,RoutedEventArgs args)
    {
        var choice=(TemplatedControl)sender;
        if(States.TryGetValue(choice,out var state))state.Loaded();
        Paint(choice,false);
    }
    private static void Unloaded(object sender,RoutedEventArgs args)
    {
        var choice=(TemplatedControl)sender;
        if(States.TryGetValue(choice,out var state))state.Suspend();
        Paint(choice,false);
    }
    private static void VisibilityChanged(object sender,AvaloniaPropertyChangedEventArgs args)
    {
        var choice=(TemplatedControl)sender;
        if(States.TryGetValue(choice,out var state))state.Refresh();
        Paint(choice,false);
    }
    private sealed class PolicyState
    {
        private readonly TemplatedControl _choice;
        private Window? _owner;
        private bool _watching, _disposed, _ownerClosed;
        internal PolicyState(TemplatedControl choice){_choice=choice;NativeEvents.WatchProperty(choice,Avalonia.Controls.Control.IsEnabledProperty,true,AvailabilityChanged);}
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
                var owner=NativeWindow.GetWindow(_choice);
                if(!ReferenceEquals(owner,_owner))
                {
                    DetachOwner();_owner=owner;
                    if(owner is not null){NativeEvents.WatchProperty(owner,Avalonia.Controls.Control.IsVisibleProperty,true,AvailabilityChanged);NativeEvents.WatchProperty(owner,Avalonia.Controls.Control.IsEnabledProperty,true,AvailabilityChanged);owner.Closed+=OwnerClosed;}
                }
            }
            Watch(CanAnimate);
        }
        private void AvailabilityChanged(object? sender,AvaloniaPropertyChangedEventArgs args)
        {if(_disposed)return;Refresh();Paint(_choice,false);}
        private void Watch(bool watch)
        {
            if(watch==_watching)return;_watching=watch;
            if(watch){OperatorAppearance.Changed+=PreferenceChanged;NativeSystemPreferences.StaticPropertyChanged+=SystemPreferenceChanged;}
            else{OperatorAppearance.Changed-=PreferenceChanged;NativeSystemPreferences.StaticPropertyChanged-=SystemPreferenceChanged;}
        }
        private void SystemPreferenceChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs args)
        {if(args.PropertyName==nameof(NativeSystemPreferences.ClientAreaAnimation))PreferenceChanged();}
        private void PreferenceChanged()
        {
            if(!_watching||_disposed)return;
            if(!Dispatcher.UIThread.CheckAccess()){Dispatcher.UIThread.InvokeAsync(new Action(PreferenceChanged));return;}
            if(!_watching||!CanAnimate||OperatorMotion.Enabled)return;
            Paint(_choice,false);
        }
        private void OwnerClosed(object? sender,EventArgs args)
        {_ownerClosed=true;Suspend();Paint(_choice,false);}
        private void DetachOwner()
        {
            if(_owner is not null){NativeEvents.WatchProperty(_owner,Avalonia.Controls.Control.IsVisibleProperty,false,AvailabilityChanged);NativeEvents.WatchProperty(_owner,Avalonia.Controls.Control.IsEnabledProperty,false,AvailabilityChanged);_owner.Closed-=OwnerClosed;}
            _owner=null;
        }
        internal void Suspend(){Watch(false);DetachOwner();}
        internal void Dispose(){_disposed=true;NativeEvents.WatchProperty(_choice,Avalonia.Controls.Control.IsEnabledProperty,false,AvailabilityChanged);Suspend();}
    }
    private static void Paint(TemplatedControl choice,bool animate)
    {
        int generation=(int)choice.GetValue(GenerationProperty)+1;choice.SetValue(GenerationProperty,generation);
        choice.ApplyTemplate();
        if(NativeTemplates.FindObject(choice,"SelectionStroke") is not Shape check)return;
        check.BeginAnimation(Shape.StrokeDashOffsetProperty,null);
        check.StrokeDashOffset=IsSelected(choice)?0:7;
        if(NativeTemplates.FindObject(choice,"SelectionPop") is not ScaleTransform pop)return;
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

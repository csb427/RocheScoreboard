// Generated from Services/OperatorFeedback.cs; edit translator/adapters.
// Source SHA-256: b8adc289366351690a97074a513c87d32e1e585ac62b0476bc72aa22f21ca410
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using ContextMenu=Avalonia.Controls.ContextMenu;
using ToolTip=Avalonia.Controls.ToolTip;
using TabControl=Avalonia.Controls.TabControl;
using Cursors=RocheScoreboard.Mac.Native.NativeCursors;
using Pen=Avalonia.Media.Pen;
using System.Runtime.CompilerServices;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using ButtonBase=Avalonia.Controls.Button;
using TemplatedControl = Avalonia.Controls.Primitives.TemplatedControl;
using TextBox=Avalonia.Controls.TextBox;
using ComboBox=Avalonia.Controls.ComboBox;
using Panel=Avalonia.Controls.Panel;
using ListBox=Avalonia.Controls.ListBox;
using MenuItem=Avalonia.Controls.MenuItem;

namespace Roche_Scoreboard.Services;

/// <summary>Shared, event-driven visual feedback. The adorned hit target and layout never move.</summary>
internal static class OperatorFeedback
{
    private static readonly AttachedProperty<bool> ManagedProperty=(AttachedProperty<bool>)NativeProperty.RegisterAttached("Managed",typeof(bool),typeof(OperatorFeedback),new PropertyMetadata(false));
    internal static void SetManaged(AvaloniaObject target,bool value)=>target.SetValue(ManagedProperty,value);
    private static readonly ConditionalWeakTable<Control,Scope> Scopes=new();
    private static readonly ConditionalWeakTable<Control,Feedback> States=new();
    private static bool _installed;
    internal static void Install()
    {
        if(_installed)return;_installed=true;
        NativeEventManager.RegisterClassHandler(typeof(Window),Control.LoadedEvent,new EventHandler<RoutedEventArgs>((sender,_)=>Enable((Window)sender)));
        NativeEventManager.RegisterClassHandler(typeof(TabControl),Control.LoadedEvent,new EventHandler<RoutedEventArgs>((sender,_)=>OperatorTabMotion.Enable((TabControl)sender)));
        NativeEventManager.RegisterClassHandler(typeof(ContextMenu),ContextMenu.OpenedEvent,new EventHandler<RoutedEventArgs>((sender,_)=>
        {
            var menu=(ContextMenu)sender;
            if(menu.PlacementTarget is not null && NativeWindow.GetWindow(menu.PlacementTarget) is Window owner && IsOperatorWindow(owner))
            {OperatorDesign.Apply(menu);Scopes.GetValue(menu,key=>new Scope(key));Roche_Scoreboard.Views.SharedSurfaceMotion.Reveal(menu);}
        }));
        NativeEventManager.RegisterClassHandler(typeof(ToolTip),Control.LoadedEvent,new EventHandler<RoutedEventArgs>((sender,_)=>
        {var tip=(ToolTip)sender;if(NativeToolTipPolicy.PlacementTarget(tip) is not null&&NativeWindow.GetWindow(NativeToolTipPolicy.PlacementTarget(tip)) is Window owner&&IsOperatorWindow(owner)){OperatorDesign.Apply(tip);Roche_Scoreboard.Views.SharedSurfaceMotion.Reveal(tip);}}));
    }
    internal static bool IsOperatorWindow(Window window)
    {
        if(window is MainWindow)return true;
        string type=window.GetType().Name;
        if(type is "EmergencyVideoWindow" or "VideoPlayerWindow" or "AflPresentationGalleryWindow")return false;
        if(window.GetType().Namespace=="Roche_Scoreboard.Views" && !type.Contains("Display"))return true;
        return !((window as NativeWindow)?.IsOutputWindow??false);
    }
    internal static void Enable(Control root)
    {
        if(root is Window window && !IsOperatorWindow(window))return;
        if(root is not Window)
        {
            if(NativeWindow.GetWindow(root) is Window owner){Enable(owner);return;}
            root.Loaded+=AttachOwner;return;
        }
        // Explicit local scope prevents the operator theme leaking into public display windows.
        if(root.GetType().Namespace=="Roche_Scoreboard.Views" || root is MainWindow || root is Window { Owner: { } ownerWindow } && ownerWindow is Window owningWindow && IsOperatorWindow(owningWindow))OperatorDesign.Apply(root);
        Scopes.GetValue(root,key=>new Scope(key));
    }
    private static void AttachOwner(object sender,RoutedEventArgs args)
    {var root=(Control)sender;root.Loaded-=AttachOwner;if(NativeWindow.GetWindow(root) is Window owner)Enable(owner);}
    internal static void Pulse(Control target)
    {
        if(!target.IsVisible || !target.IsEnabled)return;
        Get(target)?.Confirm();
    }
    internal static int LiveAdorners(Control root)=>Scopes.TryGetValue(root,out var scope)?scope.Live.Count:0;
    private static bool HasOwnFeedback(Control target)=>(bool)target.GetValue(ManagedProperty)||OperatorControlMotion.GetEnabled(target)||CricketActionMotion.GetEnabled(target)||SettingsControlMotion.GetEnabled(target)
        || target is ListBox && OperatorDesign.GetIsEnabled(target)
        || target is TextBox input && OperatorDesign.GetIsEnabled(input);
    internal static Control? Target(AvaloniaObject? source,AvaloniaObject root)
    {
        Control? fallback=null;
        while(source is not null)
        {
            if(source is ButtonBase or TextBox or ComboBox or Slider or TabItem or ListBoxItem or MenuItem or Expander)return (Control)source;
            // Cursor inherits into template borders and icon boxes. They are
            // decoration, not separate controls to outline or highlight.
            if(source is Border border && border.Cursor==new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
                && NativeValueSource.GetValueSource(border,Control.CursorProperty).BaseValueSource is not (NativeBaseValueSource.Inherited or NativeBaseValueSource.Default))fallback??=border;
            if(ReferenceEquals(source,root))break;
            source=source is Visual ? VisualTreeHelper.GetParent(source):LogicalTreeHelper.GetParent(source);
        }
        return fallback;
    }
    private static Feedback? Get(Control target)
    {
        // A template that already animates its own surface owns its feedback.
        // Adding another adorner caused inset white boxes and double outlines.
        if(HasOwnFeedback(target))return null;
        if(Owner(target) is not Window window || !IsOperatorWindow(window))return null;
        Enable(window);
        if(AdornerLayer.GetAdornerLayer(target) is not {} layer)return null;
        var scope=Scopes.GetValue(window,key=>new Scope(key));
        return States.GetValue(target,key=>new Feedback(key,layer,scope));
    }
    private static Window? Owner(AvaloniaObject current)
    {
        var seen=new HashSet<AvaloniaObject>();
        while(seen.Add(current))
        {
            if(NativeWindow.GetWindow(current) is Window window)return window;
            if(current is ContextMenu menu && menu.PlacementTarget is {} target)return NativeWindow.GetWindow(target);
            AvaloniaObject? parent=current is Control element?element.Parent??element.TemplatedParent:null;
            if(parent is null&&current is MenuItem item)parent=ItemsControl.ItemsControlFromItemContainer(item);
            if(parent is null)return null;current=parent;
        }
        return null;
    }
    private sealed class Scope
    {
        internal readonly HashSet<Feedback> Live=new();
        private Control? _hovered;
        internal Scope(Control root)
        {
            // MouseEnter is direct in WPF. Track the routed pointer instead so
            // legacy controls receive feedback as well as templated buttons.
            root.AddHandler(Avalonia.Input.InputElement.PointerMovedEvent,new EventHandler<Avalonia.Input.PointerEventArgs>((_,e)=>
            {
                var next=Target(e.Source as AvaloniaObject,root);
                if(ReferenceEquals(next,_hovered))return;
                var previous=_hovered;_hovered=next;
                previous?.Let(t=>Get(t)?.Refresh());next?.Let(t=>Get(t)?.Refresh());
            }),true);
            root.AddHandler(Avalonia.Input.InputElement.PointerExitedEvent,new EventHandler<Avalonia.Input.PointerEventArgs>((_,_)=>
            {if(root.IsPointerOver)return;var previous=_hovered;_hovered=null;previous?.Let(t=>Get(t)?.Refresh());}),true);
            root.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent,new EventHandler<Avalonia.Input.PointerPressedEventArgs>((_,e)=>Target(e.Source as AvaloniaObject,root)?.Let(t=>Get(t)?.Press(true))),true);
            root.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent,new EventHandler<Avalonia.Input.PointerReleasedEventArgs>((_,e)=>Target(e.Source as AvaloniaObject,root)?.Let(t=>Get(t)?.Press(false))),true);
            root.AddHandler(InputElement.PointerCaptureLostEvent,new EventHandler<Avalonia.Input.PointerCaptureLostEventArgs>((_,e)=>Target(e.Source as AvaloniaObject,root)?.Let(t=>Get(t)?.Press(false))),true);
            root.AddHandler(Avalonia.Input.InputElement.GotFocusEvent,new EventHandler<RoutedEventArgs>((_,e)=>Target(e.Source as AvaloniaObject,root)?.Let(t=>Get(t)?.Refresh())),true);
            root.AddHandler(Avalonia.Input.InputElement.LostFocusEvent,new EventHandler<RoutedEventArgs>((_,e)=>Target(e.Source as AvaloniaObject,root)?.Let(t=>Get(t)?.Refresh())),true);
            root.AddHandler(ButtonBase.ClickEvent,new EventHandler<RoutedEventArgs>((_,e)=>Target(e.Source as AvaloniaObject,root)?.Let(Pulse)),true);
            root.AddHandler(Avalonia.Controls.Primitives.SelectingItemsControl.SelectionChangedEvent,new EventHandler<SelectionChangedEventArgs>((_,e)=>
            {if(e.Source is Control target && target is ComboBox or ListBox && (target.IsKeyboardFocusWithin||target.IsPointerOver))Pulse(target);}),true);
            root.AddHandler(RangeBase.ValueChangedEvent,new EventHandler<RangeBaseValueChangedEventArgs>((_,e)=>
            {if(e.Source is Slider target && (target.IsKeyboardFocusWithin||NativePointer.IsCaptureWithin(target)))Get(target)?.Refresh();}),true);
            void Clear(){_hovered=null;foreach(var state in Live.ToArray())state.Remove();}
            root.Unloaded+=(_,_)=>Clear();
            if(root is Window window)window.Deactivated+=(_,_)=>Clear();
        }
    }
    private sealed class Feedback:Adorner
    {
        internal static readonly AttachedProperty<double> RevealProperty=(AttachedProperty<double>)NativeProperty.Register("Reveal",typeof(double),typeof(Feedback),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
        internal static readonly AttachedProperty<double> FocusProperty=(AttachedProperty<double>)NativeProperty.Register("Focus",typeof(double),typeof(Feedback),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
        internal static readonly AttachedProperty<double> PressProperty=(AttachedProperty<double>)NativeProperty.Register("Pressure",typeof(double),typeof(Feedback),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
        internal static readonly AttachedProperty<double> ConfirmProperty=(AttachedProperty<double>)NativeProperty.Register("Confirm",typeof(double),typeof(Feedback),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
        private readonly Control _target;private readonly AdornerLayer _layer;private readonly Scope _scope;private bool _added;
        internal Feedback(Control target,AdornerLayer layer,Scope scope):base(target)
        {
            _target=target;_layer=layer;_scope=scope;IsHitTestVisible=false;Focusable=false;
            NativeEvents.WatchProperty(target,Avalonia.Controls.Control.IsVisibleProperty,true,Changed);NativeEvents.WatchProperty(target,Avalonia.Controls.Control.IsEnabledProperty,true,Changed);target.Unloaded+=TargetUnloaded;
        }
        private void TargetUnloaded(object sender,RoutedEventArgs args)
        {Remove();NativeEvents.WatchProperty(_target,Avalonia.Controls.Control.IsVisibleProperty,false,Changed);NativeEvents.WatchProperty(_target,Avalonia.Controls.Control.IsEnabledProperty,false,Changed);_target.Unloaded-=TargetUnloaded;States.Remove(_target);}
        private void Changed(object sender,AvaloniaPropertyChangedEventArgs args){if(args.NewValue is false)Remove();}
        private void Ensure(){if(_added)return;_layer.Add(this);_added=true;_scope.Live.Add(this);}
        internal void Refresh()
        {
            if(!_target.IsEnabled||!_target.IsVisible){Remove();return;}
            Ensure();Animate(RevealProperty,_target.IsPointerOver?1:0,OperatorMotion.HoverIn);
            Animate(FocusProperty,_target.IsKeyboardFocusWithin&&OperatorInputFocus.GetIsKeyboardMode(_target)?1:0,OperatorMotion.HoverIn);
            Animate(PressProperty,0,OperatorMotion.HoverOut);
            if(!OperatorMotion.Enabled)CleanIfIdle();
        }
        internal void Press(bool down)
        {if(!_target.IsEnabled||!_target.IsVisible)return;Ensure();Animate(PressProperty,down?1:0,down?OperatorMotion.Press:OperatorMotion.SurfaceClose);}
        internal void Confirm()
        {
            if(!_target.IsEnabled||!_target.IsVisible)return;Ensure();
            if(!OperatorMotion.Enabled){Refresh();return;}
            var pulse=new DoubleAnimation(1,0,OperatorMotion.CheckDraw){FillBehavior=FillBehavior.Stop,EasingFunction=OperatorMotion.SmoothOut};
            pulse.Completed+=(_,_)=>{this.BeginAnimation(ConfirmProperty,null);CleanIfIdle();};this.BeginAnimation(ConfirmProperty,pulse);
        }
        private void Animate(AvaloniaProperty property,double value,TimeSpan duration)
        {
            if(!OperatorMotion.Enabled){this.BeginAnimation(property,null);SetValue(property,value);return;}
            var animation=new DoubleAnimation(value,duration){EasingFunction=OperatorMotion.SmoothOut};
            animation.Completed+=(_,_)=>CleanIfIdle();this.BeginAnimation(property,animation);
        }
        private void CleanIfIdle()
        {
            if(!_target.IsPointerOver&&!_target.IsKeyboardFocusWithin && new[]{RevealProperty,FocusProperty,PressProperty,ConfirmProperty}.All(property=>(double)GetValue(property)<.001))Remove();
        }
        internal void Remove()
        {
            foreach(var prop in new[]{RevealProperty,FocusProperty,PressProperty,ConfirmProperty}){this.BeginAnimation(prop,null);SetValue(prop,0d);}
            if(_added){_layer.Remove(this);_scope.Live.Remove(this);_added=false;}
        }
        public override void Render(Avalonia.Media.DrawingContext nativeContext) { using var drawing = new NativeDrawingContext(nativeContext,Bounds.Size);
            var size=AdornedElement.Bounds.Size;if(size.Width<4||size.Height<4)return;
            double hover=(double)GetValue(RevealProperty),focus=(double)GetValue(FocusProperty),press=(double)GetValue(PressProperty),confirm=(double)GetValue(ConfirmProperty);
            double strength=Math.Max(hover*.65,Math.Max(focus,confirm));
            if(strength+press<.001)return;
            var rect=new Rect(1,1,Math.Max(0,size.Width-2),Math.Max(0,size.Height-2));
            var fill=new SolidColorBrush(Avalonia.Media.Color.FromArgb((byte)(Math.Min(1,hover*.04+press*.12+confirm*.10)*255),255,255,255));
            double radius=_target is Border surface?surface.CornerRadius.TopLeft:0;
            drawing.PushClip(new RectangleGeometry(new Rect(size),radius,radius));
            drawing.DrawRectangle(fill,null,rect);
            // Legacy controls retain one unobtrusive keyboard-focus cue;
            // pointer feedback never draws a second frame around the content.
            if(focus>.01)
            {var edge=new SolidColorBrush(Avalonia.Media.Color.FromRgb(160,213,255)){Opacity=focus};drawing.DrawLine(new Pen(edge,2),new Avalonia.Point(3,size.Height-2),new Avalonia.Point(size.Width-3,size.Height-2));}
            drawing.Pop();
        }
    }
    private static void Let<T>(this T value,Action<T> action)=>action(value);
}

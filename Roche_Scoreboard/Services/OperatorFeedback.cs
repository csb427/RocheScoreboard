using ContextMenu=System.Windows.Controls.ContextMenu;
using ToolTip=System.Windows.Controls.ToolTip;
using TabControl=System.Windows.Controls.TabControl;
using Cursors=System.Windows.Input.Cursors;
using Pen=System.Windows.Media.Pen;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ButtonBase=System.Windows.Controls.Primitives.ButtonBase;
using Control=System.Windows.Controls.Control;
using TextBox=System.Windows.Controls.TextBox;
using ComboBox=System.Windows.Controls.ComboBox;
using Panel=System.Windows.Controls.Panel;
using ListBox=System.Windows.Controls.ListBox;
using MenuItem=System.Windows.Controls.MenuItem;

namespace Roche_Scoreboard.Services;

/// <summary>Shared, event-driven visual feedback. The adorned hit target and layout never move.</summary>
internal static class OperatorFeedback
{
    private static readonly DependencyProperty ManagedProperty=DependencyProperty.RegisterAttached("Managed",typeof(bool),typeof(OperatorFeedback),new PropertyMetadata(false));
    internal static void SetManaged(DependencyObject target,bool value)=>target.SetValue(ManagedProperty,value);
    private static readonly ConditionalWeakTable<FrameworkElement,Scope> Scopes=new();
    private static readonly ConditionalWeakTable<FrameworkElement,Feedback> States=new();
    private static readonly ConditionalWeakTable<Window,object> Arrivals=new();
    private static bool _installed;
    internal static void Install()
    {
        if(_installed)return;_installed=true;
        EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>Enable((Window)sender)));
        EventManager.RegisterClassHandler(typeof(TabControl),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>OperatorTabMotion.Enable((TabControl)sender)));
        EventManager.RegisterClassHandler(typeof(ContextMenu),ContextMenu.OpenedEvent,new RoutedEventHandler((sender,_)=>
        {
            var menu=(ContextMenu)sender;
            if(menu.PlacementTarget is not null && Window.GetWindow(menu.PlacementTarget) is Window owner && IsOperatorWindow(owner))
            {OperatorDesign.Apply(menu);Scopes.GetValue(menu,key=>new Scope(key));Roche_Scoreboard.Views.SharedSurfaceMotion.Reveal(menu);}
        }));
        EventManager.RegisterClassHandler(typeof(ToolTip),ToolTip.OpenedEvent,new RoutedEventHandler((sender,_)=>
        {var tip=(ToolTip)sender;if(tip.PlacementTarget is not null&&Window.GetWindow(tip.PlacementTarget) is Window owner&&IsOperatorWindow(owner)){OperatorDesign.Apply(tip);Roche_Scoreboard.Views.SharedSurfaceMotion.Reveal(tip);}}));
    }
    internal static bool IsOperatorWindow(Window window)
    {
        if(window is MainWindow)return true;
        string type=window.GetType().Name;
        if(type is "EmergencyVideoWindow" or "VideoPlayerWindow" or "AflPresentationGalleryWindow")return false;
        if(window.GetType().Namespace=="Roche_Scoreboard.Views" && !type.Contains("Display"))return true;
        if(window.Title?.Contains("Display", StringComparison.OrdinalIgnoreCase)==true)return false;
        return window.WindowStyle!=WindowStyle.None;
    }
    internal static void Enable(FrameworkElement root)
    {
        if(root is Window window && !IsOperatorWindow(window))return;
        if(root is not Window)
        {
            if(Window.GetWindow(root) is Window owner){Enable(owner);return;}
            root.Loaded+=AttachOwner;return;
        }
        // Explicit local scope prevents the operator theme leaking into public display windows.
        if(root.GetType().Namespace=="Roche_Scoreboard.Views" || root is MainWindow || root is Window { Owner: { } ownerWindow } && IsOperatorWindow(ownerWindow))OperatorDesign.Apply(root);
        Scopes.GetValue(root,key=>new Scope(key));
        if(root is Window arriving && !Arrivals.TryGetValue(arriving,out _) && arriving.Content is FrameworkElement content)
        {
            Arrivals.Add(arriving,new object());
            Roche_Scoreboard.Views.SharedSurfaceMotion.Attach(content);
            if(content.IsLoaded)Roche_Scoreboard.Views.SharedSurfaceMotion.Reveal(content);
        }
    }
    private static void AttachOwner(object sender,RoutedEventArgs args)
    {var root=(FrameworkElement)sender;root.Loaded-=AttachOwner;if(Window.GetWindow(root) is Window owner)Enable(owner);}
    internal static void Pulse(FrameworkElement target)
    {
        if(!target.IsVisible || !target.IsEnabled)return;
        Get(target)?.Confirm();
    }
    internal static int LiveAdorners(FrameworkElement root)=>Scopes.TryGetValue(root,out var scope)?scope.Live.Count:0;
    private static bool HasOwnFeedback(FrameworkElement target)=>(bool)target.GetValue(ManagedProperty)||OperatorControlMotion.GetEnabled(target)||CricketActionMotion.GetEnabled(target)||SettingsControlMotion.GetEnabled(target)
        || target is ListBox && OperatorDesign.GetIsEnabled(target)
        || target is TextBox input && OperatorDesign.GetIsEnabled(input);
    internal static FrameworkElement? Target(DependencyObject? source,DependencyObject root)
    {
        FrameworkElement? fallback=null;
        while(source is not null)
        {
            if(source is ButtonBase or TextBox or ComboBox or Slider or TabItem or ListBoxItem or MenuItem or Expander)return (FrameworkElement)source;
            // Cursor inherits into template borders and icon boxes. They are
            // decoration, not separate controls to outline or highlight.
            if(source is Border border && border.Cursor==Cursors.Hand
                && DependencyPropertyHelper.GetValueSource(border,FrameworkElement.CursorProperty).BaseValueSource is not (BaseValueSource.Inherited or BaseValueSource.Default))fallback??=border;
            if(ReferenceEquals(source,root))break;
            source=source is Visual ? VisualTreeHelper.GetParent(source):LogicalTreeHelper.GetParent(source);
        }
        return fallback;
    }
    private static Feedback? Get(FrameworkElement target)
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
    private static Window? Owner(DependencyObject current)
    {
        var seen=new HashSet<DependencyObject>();
        while(seen.Add(current))
        {
            if(Window.GetWindow(current) is Window window)return window;
            if(current is ContextMenu menu && menu.PlacementTarget is {} target)return Window.GetWindow(target);
            DependencyObject? parent=current is FrameworkElement element?element.Parent??element.TemplatedParent:null;
            if(parent is null&&current is MenuItem item)parent=ItemsControl.ItemsControlFromItemContainer(item);
            if(parent is null)return null;current=parent;
        }
        return null;
    }
    private sealed class Scope
    {
        internal readonly HashSet<Feedback> Live=new();
        private FrameworkElement? _hovered;
        internal Scope(FrameworkElement root)
        {
            // MouseEnter is direct in WPF. Track the routed pointer instead so
            // legacy controls receive feedback as well as templated buttons.
            root.AddHandler(UIElement.PreviewMouseMoveEvent,new System.Windows.Input.MouseEventHandler((_,e)=>
            {
                var next=Target(e.OriginalSource as DependencyObject,root);
                if(ReferenceEquals(next,_hovered))return;
                var previous=_hovered;_hovered=next;
                previous?.Let(t=>Get(t)?.Refresh());next?.Let(t=>Get(t)?.Refresh());
            }),true);
            root.AddHandler(UIElement.MouseLeaveEvent,new System.Windows.Input.MouseEventHandler((_,_)=>
            {if(root.IsMouseOver)return;var previous=_hovered;_hovered=null;previous?.Let(t=>Get(t)?.Refresh());}),true);
            root.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,new MouseButtonEventHandler((_,e)=>Target(e.OriginalSource as DependencyObject,root)?.Let(t=>Get(t)?.Press(true))),true);
            root.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent,new MouseButtonEventHandler((_,e)=>Target(e.OriginalSource as DependencyObject,root)?.Let(t=>Get(t)?.Press(false))),true);
            root.AddHandler(UIElement.LostMouseCaptureEvent,new System.Windows.Input.MouseEventHandler((_,e)=>Target(e.OriginalSource as DependencyObject,root)?.Let(t=>Get(t)?.Press(false))),true);
            root.AddHandler(UIElement.GotKeyboardFocusEvent,new KeyboardFocusChangedEventHandler((_,e)=>Target(e.NewFocus as DependencyObject,root)?.Let(t=>Get(t)?.Refresh())),true);
            root.AddHandler(UIElement.LostKeyboardFocusEvent,new KeyboardFocusChangedEventHandler((_,e)=>Target(e.OldFocus as DependencyObject,root)?.Let(t=>Get(t)?.Refresh())),true);
            root.AddHandler(ButtonBase.ClickEvent,new RoutedEventHandler((_,e)=>Target(e.OriginalSource as DependencyObject,root)?.Let(Pulse)),true);
            root.AddHandler(Selector.SelectionChangedEvent,new SelectionChangedEventHandler((_,e)=>
            {if(e.OriginalSource is FrameworkElement target && target is ComboBox or ListBox && (target.IsKeyboardFocusWithin||target.IsMouseOver))Pulse(target);}),true);
            root.AddHandler(RangeBase.ValueChangedEvent,new RoutedPropertyChangedEventHandler<double>((_,e)=>
            {if(e.OriginalSource is Slider target && (target.IsKeyboardFocusWithin||target.IsMouseCaptureWithin))Get(target)?.Refresh();}),true);
            void Clear(){_hovered=null;foreach(var state in Live.ToArray())state.Remove();}
            root.Unloaded+=(_,_)=>Clear();
            if(root is Window window)window.Deactivated+=(_,_)=>Clear();
        }
    }
    private sealed class Feedback:Adorner
    {
        internal static readonly DependencyProperty RevealProperty=DependencyProperty.Register("Reveal",typeof(double),typeof(Feedback),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
        internal static readonly DependencyProperty FocusProperty=DependencyProperty.Register("Focus",typeof(double),typeof(Feedback),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
        internal static readonly DependencyProperty PressProperty=DependencyProperty.Register("Pressure",typeof(double),typeof(Feedback),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
        internal static readonly DependencyProperty ConfirmProperty=DependencyProperty.Register("Confirm",typeof(double),typeof(Feedback),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
        private readonly FrameworkElement _target;private readonly AdornerLayer _layer;private readonly Scope _scope;private bool _added;
        internal Feedback(FrameworkElement target,AdornerLayer layer,Scope scope):base(target)
        {
            _target=target;_layer=layer;_scope=scope;IsHitTestVisible=false;Focusable=false;
            target.IsVisibleChanged+=Changed;target.IsEnabledChanged+=Changed;target.Unloaded+=TargetUnloaded;
        }
        private void TargetUnloaded(object sender,RoutedEventArgs args)
        {Remove();_target.IsVisibleChanged-=Changed;_target.IsEnabledChanged-=Changed;_target.Unloaded-=TargetUnloaded;States.Remove(_target);}
        private void Changed(object sender,DependencyPropertyChangedEventArgs args){if(args.NewValue is false)Remove();}
        private void Ensure(){if(_added)return;_layer.Add(this);_added=true;_scope.Live.Add(this);}
        internal void Refresh()
        {
            if(!_target.IsEnabled||!_target.IsVisible){Remove();return;}
            Ensure();Animate(RevealProperty,_target.IsMouseOver?1:0,OperatorMotion.HoverIn);
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
            pulse.Completed+=(_,_)=>{BeginAnimation(ConfirmProperty,null);CleanIfIdle();};BeginAnimation(ConfirmProperty,pulse);
        }
        private void Animate(DependencyProperty property,double value,TimeSpan duration)
        {
            if(!OperatorMotion.Enabled){BeginAnimation(property,null);SetValue(property,value);return;}
            var animation=new DoubleAnimation(value,duration){EasingFunction=OperatorMotion.SmoothOut};
            animation.Completed+=(_,_)=>CleanIfIdle();BeginAnimation(property,animation);
        }
        private void CleanIfIdle()
        {
            if(!_target.IsMouseOver&&!_target.IsKeyboardFocusWithin && new[]{RevealProperty,FocusProperty,PressProperty,ConfirmProperty}.All(property=>(double)GetValue(property)<.001))Remove();
        }
        internal void Remove()
        {
            foreach(var prop in new[]{RevealProperty,FocusProperty,PressProperty,ConfirmProperty}){BeginAnimation(prop,null);SetValue(prop,0d);}
            if(_added){_layer.Remove(this);_scope.Live.Remove(this);_added=false;}
        }
        protected override void OnRender(DrawingContext drawing)
        {
            var size=AdornedElement.RenderSize;if(size.Width<4||size.Height<4)return;
            double hover=(double)GetValue(RevealProperty),focus=(double)GetValue(FocusProperty),press=(double)GetValue(PressProperty),confirm=(double)GetValue(ConfirmProperty);
            double strength=Math.Max(hover*.65,Math.Max(focus,confirm));
            if(strength+press<.001)return;
            var rect=new Rect(1,1,Math.Max(0,size.Width-2),Math.Max(0,size.Height-2));
            var fill=new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(Math.Min(1,hover*.04+press*.12+confirm*.10)*255),255,255,255));
            double radius=_target is Border surface?surface.CornerRadius.TopLeft:0;
            drawing.PushClip(new RectangleGeometry(new Rect(size),radius,radius));
            drawing.DrawRectangle(fill,null,rect);
            // Legacy controls retain one unobtrusive keyboard-focus cue;
            // pointer feedback never draws a second frame around the content.
            if(focus>.01)
            {var edge=new SolidColorBrush(System.Windows.Media.Color.FromRgb(160,213,255)){Opacity=focus};drawing.DrawLine(new Pen(edge,2),new System.Windows.Point(3,size.Height-2),new System.Windows.Point(size.Width-3,size.Height-2));}
            drawing.Pop();
        }
    }
    private static void Let<T>(this T value,Action<T> action)=>action(value);
}

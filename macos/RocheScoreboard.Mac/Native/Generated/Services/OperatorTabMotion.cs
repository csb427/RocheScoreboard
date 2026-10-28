// Generated from Services/OperatorTabMotion.cs; edit translator/adapters.
// Source SHA-256: 35a972d5945ca5e645810264cdc0b4516c4084f7d60267043d56d2f53a1845a9
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using TabControl=Avalonia.Controls.TabControl;
using System.Runtime.CompilerServices;
using Avalonia.Controls.Documents;

namespace Roche_Scoreboard.Services;

internal static class OperatorTabMotion
{
    private static readonly ConditionalWeakTable<TabControl,Controller> States=new();
    internal static void Enable(TabControl tabs)=>States.GetValue(tabs,key=>new Controller(key));
    private sealed class Controller
    {
        private readonly TabControl _tabs;private Trail? _trail;private AdornerLayer? _layer;private bool _ready;private int _version;
        internal Controller(TabControl tabs)
        {
            _tabs=tabs;tabs.Loaded+=(_,_)=>Queue(false);tabs.Unloaded+=(_,_)=>Clear();tabs.SizeChanged+=(_,_)=>Queue(false);NativeEvents.WatchProperty(tabs,Avalonia.Controls.Control.IsVisibleProperty,true,(_,e)=>{if(e.NewValue is false)Clear();else Queue(false);});
            tabs.SelectionChanged+=(_,e)=>{if(ReferenceEquals(e.Source,tabs))Queue(true);};
            tabs.LayoutUpdated+=(_,_)=>{if(!_ready)Queue(false);};Queue(false);
        }
        private void Queue(bool animate)
        {
            int version=++_version;
            Dispatcher.UIThread.InvokeAsync(new Action(()=>{if(version==_version)Update(animate);}),Avalonia.Threading.DispatcherPriority.Loaded);
        }
        private void Update(bool animate)
        {
            // The new template owns its liquid lens. Never stack the legacy underline on it.
            if(NativeTemplates.FindObject(_tabs,"PART_OperatorLens") is OperatorSelectionLens){Clear();return;}
            if(!_tabs.IsLoaded||!_tabs.IsVisible||NativeWindow.GetWindow(_tabs) is not Window window||!OperatorFeedback.IsOperatorWindow(window))return;
            if(_tabs.SelectedItem is null){Clear();return;}
            var selected=_tabs.SelectedItem as TabItem ?? _tabs.ContainerFromItem(_tabs.SelectedItem) as TabItem;
            if(selected is null||!selected.IsVisible||selected.NativeWidth()<1)return;
            if(AdornerLayer.GetAdornerLayer(_tabs) is not {} layer)return;
            _layer=layer;if(_trail is null){_trail=new Trail(_tabs);layer.Add(_trail);}
            var point=selected.NativeTranslatePoint(new Avalonia.Point(),_tabs);
            _trail.Move(point.X+3,point.Y+selected.NativeHeight()-3,Math.Max(1,selected.NativeWidth()-6),animate&&_ready);_ready=true;
        }
        private void Clear(){++_version;if(_trail is not null){_trail.Stop();_layer?.Remove(_trail);}_trail=null;_layer=null;_ready=false;}
    }
    private sealed class Trail:Adorner
    {
        private static readonly AttachedProperty<double> XProperty=(AttachedProperty<double>)NativeProperty.Register("X",typeof(double),typeof(Trail),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
        private static readonly AttachedProperty<double> YProperty=(AttachedProperty<double>)NativeProperty.Register("Y",typeof(double),typeof(Trail),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
        private static readonly AttachedProperty<double> BarWidthProperty=(AttachedProperty<double>)NativeProperty.Register("BarWidth",typeof(double),typeof(Trail),new FrameworkPropertyMetadata(1d,FrameworkPropertyMetadataOptions.AffectsRender));
        internal Trail(Control target):base(target){IsHitTestVisible=false;}
        internal void Move(double x,double y,double width,bool animate)
        {Set(XProperty,x,animate);Set(YProperty,y,animate);Set(BarWidthProperty,width,animate);}
        private void Set(AvaloniaProperty property,double value,bool animate)
        {if(!animate||!OperatorMotion.Enabled){this.BeginAnimation(property,null);SetValue(property,value);return;}this.BeginAnimation(property,new DoubleAnimation(value,OperatorMotion.Page){EasingFunction=OperatorMotion.SmoothOut});}
        internal void Stop(){foreach(var property in new[]{XProperty,YProperty,BarWidthProperty})this.BeginAnimation(property,null);}
        public override void Render(Avalonia.Media.DrawingContext nativeContext) { using var drawing = new NativeDrawingContext(nativeContext,Bounds.Size);drawing.DrawRoundedRectangle(new SolidColorBrush(Avalonia.Media.Color.FromRgb(115,192,255)),null,new Rect((double)GetValue(XProperty),(double)GetValue(YProperty),(double)GetValue(BarWidthProperty),3),1,1);}
    }
}

using TabControl=System.Windows.Controls.TabControl;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

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
            _tabs=tabs;tabs.Loaded+=(_,_)=>Queue(false);tabs.Unloaded+=(_,_)=>Clear();tabs.SizeChanged+=(_,_)=>Queue(false);tabs.IsVisibleChanged+=(_,e)=>{if(e.NewValue is false)Clear();else Queue(false);};
            tabs.SelectionChanged+=(_,e)=>{if(ReferenceEquals(e.OriginalSource,tabs))Queue(true);};
            tabs.ItemContainerGenerator.StatusChanged+=(_,_)=>{if(!_ready)Queue(false);};Queue(false);
        }
        private void Queue(bool animate)
        {
            int version=++_version;
            _tabs.Dispatcher.BeginInvoke(new Action(()=>{if(version==_version)Update(animate);}),System.Windows.Threading.DispatcherPriority.Loaded);
        }
        private void Update(bool animate)
        {
            // The new template owns its liquid lens. Never stack the legacy underline on it.
            if(_tabs.Template?.FindName("PART_OperatorLens",_tabs) is OperatorSelectionLens){Clear();return;}
            if(!_tabs.IsLoaded||!_tabs.IsVisible||Window.GetWindow(_tabs) is not Window window||!OperatorFeedback.IsOperatorWindow(window))return;
            if(_tabs.SelectedItem is null){Clear();return;}
            var selected=_tabs.SelectedItem as TabItem ?? _tabs.ItemContainerGenerator.ContainerFromItem(_tabs.SelectedItem) as TabItem;
            if(selected is null||!selected.IsVisible||selected.ActualWidth<1)return;
            if(AdornerLayer.GetAdornerLayer(_tabs) is not {} layer)return;
            _layer=layer;if(_trail is null){_trail=new Trail(_tabs);layer.Add(_trail);}
            var point=selected.TranslatePoint(new System.Windows.Point(),_tabs);
            _trail.Move(point.X+3,point.Y+selected.ActualHeight-3,Math.Max(1,selected.ActualWidth-6),animate&&_ready);_ready=true;
        }
        private void Clear(){++_version;if(_trail is not null){_trail.Stop();_layer?.Remove(_trail);}_trail=null;_layer=null;_ready=false;}
    }
    private sealed class Trail:Adorner
    {
        private static readonly DependencyProperty XProperty=DependencyProperty.Register("X",typeof(double),typeof(Trail),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
        private static readonly DependencyProperty YProperty=DependencyProperty.Register("Y",typeof(double),typeof(Trail),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
        private static readonly DependencyProperty BarWidthProperty=DependencyProperty.Register("BarWidth",typeof(double),typeof(Trail),new FrameworkPropertyMetadata(1d,FrameworkPropertyMetadataOptions.AffectsRender));
        internal Trail(UIElement target):base(target){IsHitTestVisible=false;}
        internal void Move(double x,double y,double width,bool animate)
        {Set(XProperty,x,animate);Set(YProperty,y,animate);Set(BarWidthProperty,width,animate);}
        private void Set(DependencyProperty property,double value,bool animate)
        {if(!animate||!OperatorMotion.Enabled){BeginAnimation(property,null);SetValue(property,value);return;}BeginAnimation(property,new DoubleAnimation(value,OperatorMotion.Page){EasingFunction=OperatorMotion.SmoothOut});}
        internal void Stop(){foreach(var property in new[]{XProperty,YProperty,BarWidthProperty})BeginAnimation(property,null);}
        protected override void OnRender(DrawingContext drawing)
        {drawing.DrawRoundedRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(115,192,255)),null,new Rect((double)GetValue(XProperty),(double)GetValue(YProperty),(double)GetValue(BarWidthProperty),3),1,1);}
    }
}

// Generated from Services/OperatorDensityTransition.cs; edit translator/adapters.
// Source SHA-256: 2e0e5ea552a73127f0c3eb9281eb5ede250833a93e159c60a39030d419fafb69
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System.Runtime.CompilerServices;

namespace Roche_Scoreboard.Services;

/// <summary>Native text reflow: preserve the old optical bounds, then smoothly settle into the new density.</summary>
internal static class OperatorDensityTransition
{
    private static readonly ConditionalWeakTable<Control,Reflow> Active=new();
    internal static bool IsAnimating(Control root)=>Active.TryGetValue(root,out _);
    internal static void Begin(Control root)
    {
        if(!root.IsLoaded || !root.IsVisible)return;
        // Capture presentation positions before releasing a previous transition.
        var before=new List<(TextBlock Text,Rect Bounds,double FontX,double FontY)>();
        if(OperatorMotion.Enabled)Collect(root,root,before);
        if(Active.TryGetValue(root,out var previous))previous.Stop();
        if(!OperatorMotion.Enabled || before.Count==0)return;
        var transition=new Reflow(root,before);Active.Add(root,transition);transition.Start();
    }
    private static void Collect(AvaloniaObject node,Control root,List<(TextBlock,Rect,double,double)> items)
    {
        if(items.Count>=320)return;
        if(node is TextBlock text && text.IsVisible && text.NativeWidth()>0 && text.NativeHeight()>0)
        {
            try
            {
                Rect rect=text.TransformToAncestor(root).TransformBounds(new Rect(text.Bounds.Size));
                var matrix=text.RenderTransform?.Value??Matrix.Identity;
                if(rect.IntersectsWith(new Rect(root.Bounds.Size)))items.Add((text,rect,text.FontSize*Math.Abs(matrix.M11),text.FontSize*Math.Abs(matrix.M22)));
            }
            catch(InvalidOperationException) { }
        }
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)Collect(VisualTreeHelper.GetChild(node,i),root,items);
    }
    private sealed class Reflow(Control root,List<(TextBlock Text,Rect Bounds,double FontX,double FontY)> before)
    {
        private readonly List<(TextBlock Text,ITransform Original,TransformGroup Applied)> _applied=new();
        private DispatcherOperation? _pending;
        private bool _stopped;
        internal void Start()
        {
            root.Unloaded+=Unloaded;OperatorAppearance.Changed+=AppearanceChanged;
            _pending=Dispatcher.UIThread.InvokeAsync(new Action(Apply),DispatcherPriority.Loaded);
        }
        private void Apply()
        {
            _pending=null;if(_stopped)return;root.UpdateLayout();
            if(!OperatorMotion.Enabled || !root.IsVisible){Stop();return;}
            foreach(var (text,old,fontX,fontY) in before)
            {
                if(!text.IsLoaded || !text.IsVisible || text.RenderTransform is not { } currentTransform || !currentTransform.Value.IsIdentity || text.NativeWidth()<1 || text.NativeHeight()<1)continue;
                try
                {
                    Rect next=text.TransformToAncestor(root).TransformBounds(new Rect(text.Bounds.Size));
                    // A stretched TextBlock's box may keep the same width even
                    // while its glyphs change size. Preserve the current type
                    // scale, including a partly completed preceding transition.
                    double sx=fontX/text.FontSize,sy=fontY/text.FontSize;
                    double align=text.TextAlignment switch {TextAlignment.Center=>.5,TextAlignment.Right=>1,_=>0};
                    double dx=old.Left-next.Left+(old.Width-next.Width*sx)*align+(sx-1)*text.RenderTransformOrigin.Point.X*text.NativeWidth();
                    double dy=old.Top-next.Top+(sy-1)*text.RenderTransformOrigin.Point.Y*text.NativeHeight();
                    if(Math.Abs(dx)>120 || Math.Abs(dy)>120 || sx<.6 || sx>1.6 || sy<.6 || sy>1.6 || Math.Abs(sx-1)+Math.Abs(sy-1)+Math.Abs(dx)+Math.Abs(dy)<.01)continue;
                    var original=text.RenderTransform;var group=new TransformGroup();var scale=new ScaleTransform(sx,sy);var offset=new TranslateTransform(dx,dy);group.Children.Add(scale);group.Children.Add(offset);
                    text.SetCurrentValue(Control.RenderTransformProperty,group);_applied.Add((text,original,group));
                    DoubleAnimation To(double value)=>new(value,TimeSpan.FromMilliseconds(340)){EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut}};
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty,To(1));scale.BeginAnimation(ScaleTransform.ScaleYProperty,To(1));offset.BeginAnimation(TranslateTransform.XProperty,To(0));
                    var finish=To(0);finish.Completed+=(_,_)=>{Restore(text,original,group);if(_applied.All(item=>!ReferenceEquals(item.Text.RenderTransform,item.Applied)))Stop();};offset.BeginAnimation(TranslateTransform.YProperty,finish);
                }
                catch(InvalidOperationException) { }
            }
            before.Clear();if(_applied.Count==0)Stop();
        }
        private static void Restore(TextBlock text,ITransform original,TransformGroup applied)
        {if(ReferenceEquals(text.RenderTransform,applied))text.SetCurrentValue(Control.RenderTransformProperty,original);}
        private void Unloaded(object sender,RoutedEventArgs args)=>Stop();
        private void AppearanceChanged(){if(!OperatorMotion.Enabled)Stop();}
        internal void Stop()
        {
            if(_stopped)return;_stopped=true;_pending?.Abort();_pending=null;
            root.Unloaded-=Unloaded;OperatorAppearance.Changed-=AppearanceChanged;
            foreach(var (text,original,group) in _applied){foreach(var transform in group.Children){if(transform is ScaleTransform scale){scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);}else if(transform is TranslateTransform translate){translate.BeginAnimation(TranslateTransform.XProperty,null);translate.BeginAnimation(TranslateTransform.YProperty,null);}}Restore(text,original,group);}
            _applied.Clear();before.Clear();Active.Remove(root);
        }
    }
}

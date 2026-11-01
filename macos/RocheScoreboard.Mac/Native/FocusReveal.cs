using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
namespace RocheScoreboard.Mac.Native;

/// <summary>Reveal keyboard focus through every nested viewport, from inside out.</summary>
internal static class NativeFocusReveal
{
    internal static void Reveal(Control target)
    {
        if(!target.IsEffectivelyVisible)return;
        foreach(var scroll in target.GetVisualAncestors().OfType<ScrollViewer>().ToArray())
        {
            var presenter=scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().FirstOrDefault(p=>ReferenceEquals(p.TemplatedParent,scroll));
            if(presenter is null||presenter.Bounds.Width<=0||presenter.Bounds.Height<=0)continue;
            if(scroll.Content is not Control content)continue;
            // Content coordinates remain stable when Offset has changed but
            // the presenter's render translation hasn't reached the next frame.
            // Adding the latest Offset to an old presenter position can double
            // the requested scroll, especially after expanding a long group.
            var location=target.TranslatePoint(default,content);if(location is null)continue;
            var rect=new Rect(location.Value+new Vector(content.Margin.Left,content.Margin.Top),target.Bounds.Size);var view=new Rect(presenter.Bounds.Size);
            double x=scroll.Offset.X,y=scroll.Offset.Y;
            if(scroll.HorizontalScrollBarVisibility!=ScrollBarVisibility.Disabled)
                x=rect.Left<x?rect.Left:rect.Right>x+view.Width?Math.Min(rect.Left,rect.Right-view.Width):x;
            if(scroll.VerticalScrollBarVisibility!=ScrollBarVisibility.Disabled)
                y=rect.Top<y?rect.Top:rect.Bottom>y+view.Height?Math.Min(rect.Top,rect.Bottom-view.Height):y;
            var offset=new Vector(Math.Clamp(x,0,Math.Max(0,scroll.Extent.Width-scroll.Viewport.Width)),Math.Clamp(y,0,Math.Max(0,scroll.Extent.Height-scroll.Viewport.Height)));
            if(offset==scroll.Offset)continue;
            scroll.Offset=offset;
            // The next outer viewport must see the adjusted descendant position.
            scroll.UpdateLayout();
        }
    }
}

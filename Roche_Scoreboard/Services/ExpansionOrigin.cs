using System.Windows;
using Size=System.Windows.Size;

namespace Roche_Scoreboard.Services;

/// <summary>Geometry only: preserves a control's source when its detail surface expands.
/// Callers own their shape, content choreography, backdrop and animation.</summary>
internal readonly record struct ExpansionOrigin(double ScaleX,double ScaleY,double OffsetX,double OffsetY)
{
    internal static ExpansionOrigin Default=>new(.94,.82,0,24);
    internal static ExpansionOrigin FromRect(Rect source,Size destination)
    {
        if(!double.IsFinite(source.X+source.Y+source.Width+source.Height+destination.Width+destination.Height)
            || source.Width<=0 || source.Height<=0 || destination.Width<=0 || destination.Height<=0)return Default;
        return new(Math.Clamp(source.Width/destination.Width,.04,1),Math.Clamp(source.Height/destination.Height,.04,1),
            source.X+source.Width*.5-destination.Width*.5,source.Y+source.Height*.5-destination.Height*.5);
    }
    internal static ExpansionOrigin From(FrameworkElement destination,FrameworkElement? source)
    {
        if(source is not {IsVisible:true,ActualWidth:>0,ActualHeight:>0} || destination.ActualWidth<=0 || destination.ActualHeight<=0
            || Window.GetWindow(source) is not { } owner || !ReferenceEquals(owner,Window.GetWindow(destination)))return Default;
        var location=source.TranslatePoint(new System.Windows.Point(),destination);
        return FromRect(new Rect(location,source.RenderSize),destination.RenderSize);
    }
    internal ExpansionOrigin WithTravel(double travel)=>new(1-(1-ScaleX)*travel,1-(1-ScaleY)*travel,OffsetX*travel,OffsetY*travel);
}

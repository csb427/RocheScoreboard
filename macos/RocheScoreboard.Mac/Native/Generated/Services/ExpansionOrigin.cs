// Generated from Services/ExpansionOrigin.cs; edit translator/adapters.
// Source SHA-256: ae89641a3160e3bae26d6d5b7576dd6da5af2b897e34c6eb48fe23f30e3ea4db
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Size=Avalonia.Size;

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
    internal static ExpansionOrigin From(Control destination,Control? source)
    {
        if(source is not {IsVisible:true,Bounds.Width:>0,Bounds.Height:>0} || destination.NativeWidth()<=0 || destination.NativeHeight()<=0
            || NativeWindow.GetWindow(source) is not { } owner || !ReferenceEquals(owner,NativeWindow.GetWindow(destination)))return Default;
        var location=source.NativeTranslatePoint(new Avalonia.Point(),destination);
        return FromRect(new Rect(location,source.Bounds.Size),destination.Bounds.Size);
    }
    internal ExpansionOrigin WithTravel(double travel)=>new(1-(1-ScaleX)*travel,1-(1-ScaleY)*travel,OffsetX*travel,OffsetY*travel);
}

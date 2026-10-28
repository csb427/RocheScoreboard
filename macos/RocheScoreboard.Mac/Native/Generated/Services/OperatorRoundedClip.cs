// Generated from Services/OperatorRoundedClip.cs; edit translator/adapters.
// Source SHA-256: 46a2e43faa8ac565a8bcca70f67250c1393c3b831043043b6b8f7eeb6e221f21
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace Roche_Scoreboard.Services;

/// <summary>A rounded visual boundary, including transformed children. Border corners alone do not clip.</summary>
public static class OperatorRoundedClip
{
    public static readonly AttachedProperty<double> RadiusProperty=(AttachedProperty<double>)NativeProperty.RegisterAttached("Radius",typeof(double),typeof(OperatorRoundedClip),new PropertyMetadata(0d,Changed));
    public static double GetRadius(AvaloniaObject target)=>(double)target.GetValue(RadiusProperty);
    public static void SetRadius(AvaloniaObject target,double value)=>target.SetValue(RadiusProperty,value);
    private static void Changed(AvaloniaObject target,AvaloniaPropertyChangedEventArgs args)
    {
        if(target is not Control element)return;
        element.SizeChanged-=Resized;
        if((double)args.NewValue>0){element.SizeChanged+=Resized;Update(element);}
        else element.ClearValue(Control.ClipProperty);
    }
    private static void Resized(object sender,SizeChangedEventArgs args)=>Update((Control)sender);
    private static void Update(Control element)
    {
        double radius=Math.Max(0,GetRadius(element));
        element.Clip=new RectangleGeometry(new Rect(0,0,Math.Max(0,element.NativeWidth()),Math.Max(0,element.NativeHeight())),radius,radius);
    }
}

using System.Windows;
using System.Windows.Media;

namespace Roche_Scoreboard.Services;

/// <summary>A rounded visual boundary, including transformed children. Border corners alone do not clip.</summary>
public static class OperatorRoundedClip
{
    public static readonly DependencyProperty RadiusProperty=DependencyProperty.RegisterAttached("Radius",typeof(double),typeof(OperatorRoundedClip),new PropertyMetadata(0d,Changed));
    public static double GetRadius(DependencyObject target)=>(double)target.GetValue(RadiusProperty);
    public static void SetRadius(DependencyObject target,double value)=>target.SetValue(RadiusProperty,value);
    private static void Changed(DependencyObject target,DependencyPropertyChangedEventArgs args)
    {
        if(target is not FrameworkElement element)return;
        element.SizeChanged-=Resized;
        if((double)args.NewValue>0){element.SizeChanged+=Resized;Update(element);}
        else element.ClearValue(UIElement.ClipProperty);
    }
    private static void Resized(object sender,SizeChangedEventArgs args)=>Update((FrameworkElement)sender);
    private static void Update(FrameworkElement element)
    {
        double radius=Math.Max(0,GetRadius(element));
        element.Clip=new RectangleGeometry(new Rect(0,0,Math.Max(0,element.ActualWidth),Math.Max(0,element.ActualHeight)),radius,radius);
    }
}

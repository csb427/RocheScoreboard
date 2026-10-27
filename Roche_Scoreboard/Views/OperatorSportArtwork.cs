using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brush=System.Windows.Media.Brush;
using Color=System.Windows.Media.Color;

namespace Roche_Scoreboard.Views;

/// <summary>Bundled AI-generated illustrated backgrounds; decoded once, shared and frozen.</summary>
public sealed class OperatorSportArtwork:FrameworkElement
{
    public static readonly DependencyProperty SportProperty=DependencyProperty.Register(nameof(Sport),typeof(string),typeof(OperatorSportArtwork),new FrameworkPropertyMetadata("afl",FrameworkPropertyMetadataOptions.AffectsRender));
    public string Sport{get=>(string)GetValue(SportProperty);set=>SetValue(SportProperty,value);}
    private static readonly Dictionary<string,BitmapSource?> Images=new();
    private static readonly Brush Base=Freeze(new SolidColorBrush(Color.FromRgb(5,15,31)));
    private static readonly Brush CopyShade=Freeze(new LinearGradientBrush(new GradientStopCollection{new(Color.FromArgb(120,3,8,16),0),new(Color.FromArgb(65,3,8,16),.35),new(Colors.Transparent,.75)},0));
    private static readonly Brush ImageFeather=Freeze(new LinearGradientBrush(new GradientStopCollection{new(Colors.Transparent,0),new(Colors.White,.32),new(Colors.White,1)},0));
    private static T Freeze<T>(T brush)where T:Freezable{brush.Freeze();return brush;}
    public OperatorSportArtwork(){IsHitTestVisible=false;}
    internal bool HasGeneratedArtwork=>Artwork(Sport) is not null;
    private static BitmapSource? Artwork(string sport)
    {
        if(Images.TryGetValue(sport,out var existing))return existing;
        if(sport is not ("afl" or "cricket" or "training"))return null;
        try
        {
            var image=new BitmapImage();image.BeginInit();image.UriSource=new Uri($"pack://application:,,,/Roche Scoreboard;component/Resources/SportArtwork/{sport}-cartoon-v2.png");
            image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=1536;image.EndInit();image.Freeze();return Images[sport]=image;
        }
        catch(Exception error)when(error is System.IO.IOException or NotSupportedException or InvalidOperationException){return Images[sport]=null;}
    }
    protected override void OnRender(DrawingContext drawing)
    {
        if(RenderSize.Width<1||RenderSize.Height<1)return;
        var bounds=new Rect(RenderSize);drawing.PushClip(new RectangleGeometry(bounds,28,28));drawing.DrawRectangle(Base,null,bounds);
        if(Artwork(Sport) is {} image)
        {
            // Preserve the subject's proportions at every card aspect ratio.
            double width=RenderSize.Height*image.PixelWidth/image.PixelHeight;
            drawing.PushOpacityMask(ImageFeather);
            drawing.DrawImage(image,new Rect(RenderSize.Width-width,0,width,RenderSize.Height));
            drawing.Pop();
        }
        drawing.DrawRectangle(CopyShade,null,bounds);drawing.Pop();
    }
}

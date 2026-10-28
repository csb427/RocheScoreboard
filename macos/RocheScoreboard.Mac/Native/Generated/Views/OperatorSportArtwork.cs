// Generated from Views/OperatorSportArtwork.cs; edit translator/adapters.
// Source SHA-256: c3b50332040609140a32be4456a059df50993d1c818900e8fdb60a81490631bd
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using Brush=Avalonia.Media.IBrush;
using Color=Avalonia.Media.Color;

namespace Roche_Scoreboard.Views;

/// <summary>Bundled AI-generated illustrated backgrounds; decoded once, shared and frozen.</summary>
public sealed class OperatorSportArtwork:Control
{
    public static readonly AttachedProperty<string> SportProperty=(AttachedProperty<string>)NativeProperty.Register(nameof(Sport),typeof(string),typeof(OperatorSportArtwork),new FrameworkPropertyMetadata("afl",FrameworkPropertyMetadataOptions.AffectsRender));
    public string Sport{get=>(string)GetValue(SportProperty);set=>SetValue(SportProperty,value);}
    private static readonly Dictionary<string,NativeBitmap?> Images=new();
    private static readonly Brush Base=Freeze(new SolidColorBrush(Color.FromRgb(5,15,31)));
    private static readonly Brush CopyShade=Freeze(NativeBrush.Linear(new GradientStops{new(Color.FromArgb(120,3,8,16),0),new(Color.FromArgb(65,3,8,16),.35),new(Colors.Transparent,.75)},0));
    private static readonly Brush ImageFeather=Freeze(NativeBrush.Linear(new GradientStops{new(Colors.Transparent,0),new(Colors.White,.32),new(Colors.White,1)},0));
    private static T Freeze<T>(T brush)where T:AvaloniaObject{brush.Freeze();return brush;}
    public OperatorSportArtwork(){IsHitTestVisible=false;}
    internal bool HasGeneratedArtwork=>Artwork(Sport) is not null;
    private static NativeBitmap? Artwork(string sport)
    {
        if(Images.TryGetValue(sport,out var existing))return existing;
        if(sport is not ("afl" or "cricket" or "training"))return null;
        try
        {
            var image=new NativeBitmap();image.BeginInit();image.UriSource=new Uri($"avares://RocheScoreboard.Mac/Assets/SportArtwork/{sport}-cartoon-v2.png");
            image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=1536;image.EndInit();image.Freeze();return Images[sport]=image;
        }
        catch(Exception error)when(error is System.IO.IOException or NotSupportedException or InvalidOperationException){return Images[sport]=null;}
    }
    public override void Render(Avalonia.Media.DrawingContext nativeContext) { using var drawing = new NativeDrawingContext(nativeContext,Bounds.Size);
        if(Bounds.Size.Width<1||Bounds.Size.Height<1)return;
        var bounds=new Rect(Bounds.Size);drawing.PushClip(new RectangleGeometry(bounds,28,28));drawing.DrawRectangle(Base,null,bounds);
        if(Artwork(Sport) is {} image)
        {
            // Preserve the subject's proportions at every card aspect ratio.
            double width=Bounds.Size.Height*image.PixelWidth/image.PixelHeight;
            drawing.PushOpacityMask(ImageFeather);
            drawing.DrawImage(image,new Rect(Bounds.Size.Width-width,0,width,Bounds.Size.Height));
            drawing.Pop();
        }
        drawing.DrawRectangle(CopyShade,null,bounds);drawing.Pop();
    }
}

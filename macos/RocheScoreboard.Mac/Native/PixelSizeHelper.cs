using System.Runtime.CompilerServices;
using Avalonia.Controls;
namespace Roche_Scoreboard.Services;
internal static class PixelSizeHelper
{
    private sealed class Dimensions
    {
        public int Width,Height;
        public bool Applying;
        public Dimensions(Window window)
        {
            window.Opened+=(_,_)=>Apply(window);
            window.ScalingChanged+=(_,_)=>Apply(window);
        }
        public void Apply(Window window)
        {
            if(Applying)return;Applying=true;
            try{double scale=window.RenderScaling;window.Width=Width/scale;window.Height=Height/scale;}
            finally{Applying=false;}
        }
    }
    private static readonly ConditionalWeakTable<Window,Dimensions> Requested=new();
    internal static (double X,double Y) GetDpi(Window window)=>(window.RenderScaling*96,window.RenderScaling*96);
    internal static void SetWindowPixels(Window window,int pixelWidth,int pixelHeight)
    {
        if(window is RocheScoreboard.Mac.Native.NativeWindow native)native.IsOutputWindow=true;
        var size=Requested.GetValue(window,w=>new Dimensions(w));size.Width=Math.Max(1,pixelWidth);size.Height=Math.Max(1,pixelHeight);size.Apply(window);
    }
    internal static (int Width,int Height) GetWindowPixels(Window window,double dipWidth,double dipHeight)
        =>((int)Math.Round(dipWidth*window.RenderScaling),(int)Math.Round(dipHeight*window.RenderScaling));
}

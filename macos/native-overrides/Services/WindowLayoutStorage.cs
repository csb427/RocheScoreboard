using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using RocheScoreboard.Mac.Native;
namespace Roche_Scoreboard.Services;

/// <summary>Native desktop positions are physical pixels; window sizes are logical units.</summary>
internal static class WindowLayoutStorage
{
    public const string ControlPanelKey="controlPanel", AflDisplayKey="displayAfl", CricketDisplayKey="displayCricket", TrainingDisplayKey="displayTraining";
    private static readonly JsonSerializerOptions JsonOptions=new(){WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
    // Optional pixel positions migrate the original four-field format without breaking old files.
    public sealed record WindowLayout(double Left,double Top,double Width,double Height,int? PixelLeft=null,int? PixelTop=null);
    private static string PathName
    {
        get{Directory.CreateDirectory(SelfTestStorage.DocumentsDirectory);return Path.Combine(SelfTestStorage.DocumentsDirectory,"window_layout.json");}
    }
    private static Dictionary<string,WindowLayout> Load()
    {try{return AtomicFile.TryReadJson<Dictionary<string,WindowLayout>>(PathName,JsonOptions)??new();}catch{return new();}}
    public static WindowLayout? Get(string key)=>Load().GetValueOrDefault(key);
    public static void Set(string key,WindowLayout layout)
    {try{var data=Load();data[key]=layout;AtomicFile.WriteAllText(PathName,JsonSerializer.Serialize(data,JsonOptions));}catch{/* Saving layout is best effort. */}}
    public static WindowLayout Capture(NativeWindow window)
    {
        bool restored=window.WindowState!=WindowState.Normal;
        var bounds=restored?window.RestoreBounds:new Rect(window.Left,window.Top,window.Bounds.Width,window.Bounds.Height);
        var position=restored?window.RestorePixelPosition:window.Position;
        return new(bounds.X,bounds.Y,bounds.Width,bounds.Height,position.X,position.Y);
    }
    public static void Apply(NativeWindow window,WindowLayout layout,double minWidth=200,double minHeight=120)
    {
        var primary=window.Screens.Primary??window.Screens.All.FirstOrDefault();
        double scale=primary?.Scaling??1;
        static int Physical(double value,double scaling)=>double.IsFinite(value)?(int)Math.Clamp(Math.Round(value*scaling),int.MinValue,int.MaxValue):0;
        var requested=new PixelPoint(layout.PixelLeft??Physical(layout.Left,scale),layout.PixelTop??Physical(layout.Top,scale));
        var screen=window.Screens.ScreenFromPoint(requested)??primary;
        if(screen is null){window.Width=double.IsFinite(layout.Width)?Math.Max(minWidth,layout.Width):900;window.Height=double.IsFinite(layout.Height)?Math.Max(minHeight,layout.Height):700;return;}
        var size=new Size(double.IsFinite(layout.Width)?Math.Max(minWidth,layout.Width):900,double.IsFinite(layout.Height)?Math.Max(minHeight,layout.Height):700);
        var fit=NativeWindowPlacement.Fit(requested,size,screen.WorkingArea,screen.Scaling,new Size(0,28));
        // OnOpened repeats this with the platform's measured decoration dimensions.
        window.MinWidth=Math.Min(window.MinWidth,fit.ClientSize.Width);window.MinHeight=Math.Min(window.MinHeight,fit.ClientSize.Height);
        window.Width=fit.ClientSize.Width;window.Height=fit.ClientSize.Height;window.Position=fit.Position;
    }
    public static void Save(string key,NativeWindow window)=>Set(key,Capture(window));
}

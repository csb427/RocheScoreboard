using System.Runtime.InteropServices;
namespace RocheScoreboard.Mac.Native;
public static class NativeSystemPreferences
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    [DllImport(ObjC)] private static extern nint objc_getClass(string name);
    [DllImport(ObjC)] private static extern nint sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint Send(nint target, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SendBool(nint target, nint selector);
    private static System.ComponentModel.PropertyChangedEventHandler? _changed;
    private static Avalonia.Threading.DispatcherTimer? _poll;
    public static event System.ComponentModel.PropertyChangedEventHandler StaticPropertyChanged
    {
        add
        {
            _changed+=value;
            if(_poll is not null)return;
            bool contrast=HighContrast,motion=ClientAreaAnimation;
            _poll=new Avalonia.Threading.DispatcherTimer(TimeSpan.FromSeconds(2),Avalonia.Threading.DispatcherPriority.Background,(_,_)=>
            {
                bool nextContrast=HighContrast,nextMotion=ClientAreaAnimation;
                if(nextContrast!=contrast){contrast=nextContrast;_changed?.Invoke(null,new(nameof(HighContrast)));}
                if(nextMotion!=motion){motion=nextMotion;_changed?.Invoke(null,new(nameof(ClientAreaAnimation)));}
            });_poll.Start();
        }
        remove{_changed-=value;if(_changed is null){_poll?.Stop();_poll=null;}}
    }
    public static bool HighContrast => OperatingSystem.IsMacOS() && SendBool(Send(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace")), sel_registerName("accessibilityDisplayShouldIncreaseContrast"));
    public static bool ClientAreaAnimation => !OperatingSystem.IsMacOS() || !SendBool(Send(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace")), sel_registerName("accessibilityDisplayShouldReduceMotion"));
}

using System.Runtime.InteropServices;
namespace RocheScoreboard.Mac.Native;
public static class NativeSystemPreferences
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    [DllImport(ObjC)] private static extern nint objc_getClass(string name);
    [DllImport(ObjC)] private static extern nint sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint Send(nint target, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SendBool(nint target, nint selector);
    public static bool ClientAreaAnimation => !OperatingSystem.IsMacOS() || !SendBool(Send(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace")), sel_registerName("accessibilityDisplayShouldReduceMotion"));
}

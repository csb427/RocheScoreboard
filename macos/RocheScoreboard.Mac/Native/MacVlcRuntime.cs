using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
namespace RocheScoreboard.Mac.Native;
internal static class MacVlcRuntime
{
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint="setenv", SetLastError=true)]
    private static extern int SetNativeEnvironment([MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int overwrite);

    private static void SetVlcEnvironment(string name,string value)
    {
        Environment.SetEnvironmentVariable(name,value);
        // .NET's Unix environment dictionary does not update libc getenv().
        // libVLC reads these paths natively while locating its bundled plugins.
        if(SetNativeEnvironment(name,value,1)!=0)
            throw new InvalidOperationException($"Could not configure {name} for VLC (errno {Marshal.GetLastPInvokeError()}).");
    }

    internal static void Initialize()
    {
        if (!OperatingSystem.IsMacOS()) { Core.Initialize(); return; }
        string? configured = Environment.GetEnvironmentVariable("ROCHE_VLC_HOME");
        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            configured,
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Frameworks", "vlc", "lib")),
            Path.Combine(AppContext.BaseDirectory, "libvlc"), AppContext.BaseDirectory,
            "/Applications/VLC.app/Contents/MacOS/lib",
            Path.Combine(user, "Applications", "VLC.app", "Contents", "MacOS", "lib"),
            "/opt/homebrew/lib", "/usr/local/lib"
        };
        foreach (string? value in candidates)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            string path = value.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? Path.Combine(value, "Contents", "MacOS", "lib") : value;
            string library = Path.Combine(path, "libvlc.dylib");
            if (!File.Exists(library)) continue;
            string plugins = Path.GetFullPath(Path.Combine(path, "..", "plugins"));
            if (Directory.Exists(plugins))
                SetVlcEnvironment("VLC_PLUGIN_PATH",string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VLC_PLUGIN_PATH"))
                    ? plugins : Environment.GetEnvironmentVariable("VLC_PLUGIN_PATH")!);
            string data = Path.GetFullPath(Path.Combine(path, "..", "share"));
            if (Directory.Exists(data)) SetVlcEnvironment("VLC_DATA_PATH", data);
            string core = Path.Combine(path, "libvlccore.dylib");
            if (File.Exists(core)) NativeLibrary.Load(core);
            Core.Initialize(path); return;
        }
        throw new DllNotFoundException("LibVLC for this Mac architecture was not found. Install VLC in Applications, or set ROCHE_VLC_HOME to its lib folder, then restart Roche Scoreboard.");
    }
}

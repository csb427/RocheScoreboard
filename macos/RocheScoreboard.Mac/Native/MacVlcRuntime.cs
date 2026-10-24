using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
namespace RocheScoreboard.Mac.Native;
internal static class MacVlcRuntime
{
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
            if (Directory.Exists(plugins) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VLC_PLUGIN_PATH")))
                Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", plugins);
            string data = Path.GetFullPath(Path.Combine(path, "..", "share"));
            if (Directory.Exists(data)) Environment.SetEnvironmentVariable("VLC_DATA_PATH", data);
            string core = Path.Combine(path, "libvlccore.dylib");
            if (File.Exists(core)) NativeLibrary.Load(core);
            Core.Initialize(path); return;
        }
        throw new DllNotFoundException("LibVLC for this Mac architecture was not found. Install VLC in Applications, or set ROCHE_VLC_HOME to its lib folder, then restart Roche Scoreboard.");
    }
}

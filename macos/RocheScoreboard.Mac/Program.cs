using Avalonia;

namespace RocheScoreboard.Mac;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .With(new Avalonia.Threading.DispatcherOptions { InputStarvationTimeout = TimeSpan.FromMilliseconds(50) })
        .WithInterFont().LogToTrace();
}

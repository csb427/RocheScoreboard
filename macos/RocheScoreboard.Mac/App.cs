using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using RocheScoreboard.Mac.Native;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;

namespace RocheScoreboard.Mac;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        NativeResources.CaptureFrameworkThemes(this);
        UxPreferences = Roche_Scoreboard.Services.UxPreferences.Load();
        ThemeService.Initialize(UxPreferences.Theme, UxPreferences.Density);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string[] args = desktop.Args ?? Array.Empty<string>();
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            IsSelfTestMode = args.Contains("--smoke-test");
            InstallExceptionHandlers(desktop);
            if (args.Contains("--production-scorebug-preview"))
            {
                var view = new ScorebugControl();
                view.SetTeams("OLD XAVERIANS", "OX", "OLD CAULFIELD", "OC");
                view.SetLayout(ScorebugLayout.Expanded); view.SetScores(0, 0, 0, 0); view.SetQuarter(1); view.SetClock("00:00");
                desktop.MainWindow = new NativeWindow { Title = "Roche Scoreboard · Production output", Width = 707, Height = 420, Content = view };
            }
            else if (args.Contains("--design-gallery")) desktop.MainWindow = new DesignSystemGalleryWindow();
            else if (args.Contains("--afl-presentation-gallery")) desktop.MainWindow = new AflPresentationGalleryWindow();
            else if (args.Contains("--legacy-demo"))
            {
                var session = new DemoSession(); var store = new SessionStore();
                string? recovery = store.TryRestore(session);
                var window = new ControlWindow(session, store);
                if (recovery is not null) window.SetStatus(recovery);
                desktop.MainWindow = window;
            }
            else
            {
                if (!IsSelfTestMode) UpdateCheckTask = Task.Run(async () =>
                {
                    Roche_Scoreboard.Services.AutoUpdateService.CleanupStaleStagingDirectories();
                    try { await UpdateService.CheckForUpdateAsync(); }
                    catch (Exception error) { WriteCrashLog("Update check", error); }
                });
                desktop.MainWindow = new Roche_Scoreboard.MainWindow();
            }
            try
            {
                using var icon = AssetLoader.Open(new Uri("avares://RocheScoreboard.Mac/Assets/app.png"));
                desktop.MainWindow.Icon = new WindowIcon(icon);
            }
            catch (IOException) { }
            if (IsSelfTestMode)
            {
                desktop.MainWindow.Opened += (_, _) => DispatcherTimer.RunOnce(desktop.MainWindow.Close, TimeSpan.FromSeconds(2));
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}

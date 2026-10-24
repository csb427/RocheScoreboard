using Avalonia.Controls.ApplicationLifetimes;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;

namespace RocheScoreboard.Mac;
public sealed partial class App
{
    internal AutoUpdateService UpdateService { get; } = new();
    internal ThemeService ThemeService { get; } = new();
    internal UxPreferencesData UxPreferences { get; private set; } = new();
    internal Task UpdateCheckTask { get; private set; } = Task.CompletedTask;
    internal bool IsSelfTestMode { get; set; }
    private bool _updatePromptOpen;
    internal bool PromptForUpdate()
    {
        if (_updatePromptOpen || !UpdateService.UpdateAvailable) return false;
        _updatePromptOpen = true;
        try
        {
            var prompt = new UpdatePromptWindow(UpdateService) { Owner = Native.NativeDesktop.MainWindow };
            prompt.ShowDialog();
            if (!prompt.UpdateApplied) return false;
            (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
            return true;
        }
        catch (Exception error) { WriteCrashLog("PromptForUpdate", error); return false; }
        finally { _updatePromptOpen = false; }
    }
}

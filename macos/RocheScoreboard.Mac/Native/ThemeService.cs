using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;

namespace Roche_Scoreboard.Services;

public sealed class ThemeService
{
    private IResourceProvider? _theme;
    private IResourceProvider? _density;
    public AppTheme CurrentTheme { get; private set; }
    public UiDensity CurrentDensity { get; private set; }
    private static ResourceInclude Load(string name) => new(new Uri("avares://RocheScoreboard.Mac/"))
        { Source = new Uri("avares://RocheScoreboard.Mac/Native/Generated/DesignSystem/" + name + ".axaml") };
    public void Initialize(AppTheme theme, UiDensity density)
    {
        var app = Application.Current ?? throw new InvalidOperationException("Theme initialization requires the native application.");
        if (_theme is null)
        {
            app.Resources.MergedDictionaries.Add(Load("Tokens"));
            _theme = Load(theme == AppTheme.Dark ? "DarkTheme" : "LightTheme");
            _density = Load(density == UiDensity.Compact ? "CompactDensity" : "ComfortableDensity");
            app.Resources.MergedDictionaries.Add(_theme); app.Resources.MergedDictionaries.Add(_density);
            foreach (string name in new[] { "Typography", "Icons", "Components" }) app.Resources.MergedDictionaries.Add(Load(name));
        }
        CurrentTheme = theme; CurrentDensity = density;
        app.RequestedThemeVariant = theme == AppTheme.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }
    public void SetTheme(AppTheme theme)
    {
        if (theme == CurrentTheme) return;
        var app = Application.Current!;
        var next = Load(theme == AppTheme.Dark ? "DarkTheme" : "LightTheme");
        int index = app.Resources.MergedDictionaries.IndexOf(_theme!);
        if (index < 0) throw new InvalidOperationException("The active source theme dictionary is missing.");
        app.Resources.MergedDictionaries[index] = next; _theme = next; CurrentTheme = theme;
        app.RequestedThemeVariant = theme == AppTheme.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }
    public void SetDensity(UiDensity density)
    {
        if (density == CurrentDensity) return;
        var resources = Application.Current!.Resources.MergedDictionaries;
        int index = resources.IndexOf(_density!);
        if (index < 0) throw new InvalidOperationException("The active source density dictionary is missing.");
        var next = Load(density == UiDensity.Compact ? "CompactDensity" : "ComfortableDensity");
        resources[index] = next; _density = next; CurrentDensity = density;
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace RocheScoreboard.Mac.Native;
public static class NativeResources
{
    public static object? FindOrNull(StyledElement element, object key) => element.TryFindResource(key, out var value) ? value : null;
    public static T Require<T>(StyledElement element, object key) where T : class
        => FindOrNull(element, key) as T ?? throw new InvalidOperationException($"Production resource {key} must be {typeof(T).Name}.");
    public static void CaptureFrameworkThemes(Application application)
    {
        foreach (Type type in new[] { typeof(Button), typeof(CheckBox), typeof(RadioButton), typeof(Slider), typeof(TextBox), typeof(ComboBox), typeof(ComboBoxItem), typeof(ListBox), typeof(ListBoxItem), typeof(TabControl), typeof(TabItem), typeof(ScrollViewer), typeof(Avalonia.Controls.Primitives.ToggleButton) })
            if (application.Styles.TryGetResource(type, application.ActualThemeVariant, out var resource) && resource is ControlTheme theme)
                application.Resources["Native.Base." + type.Name] = theme;
    }
}
public static class NativeStoragePaths
{
    public static string GetFolderPath(Environment.SpecialFolder folder)
    {
        string? root = Environment.GetEnvironmentVariable("ROCHE_DATA_HOME");
        return folder is Environment.SpecialFolder.LocalApplicationData or Environment.SpecialFolder.ApplicationData or Environment.SpecialFolder.MyDocuments && !string.IsNullOrWhiteSpace(root)
            ? Path.GetFullPath(root) : Environment.GetFolderPath(folder);
    }
}

using System;
using System.Windows;
using WpfApplication = System.Windows.Application;

namespace Roche_Scoreboard.Services;

/// <summary>
/// Manages runtime switching of Score Grid design-system theme and density.
/// Maintains direct references to active ResourceDictionaries and performs
/// safe replacement without filename Contains matching.
/// </summary>
public sealed class ThemeService
{
    private ResourceDictionary? _activeThemeDictionary;
    private ResourceDictionary? _activeDensityDictionary;
    private AppTheme _currentTheme = AppTheme.Light;
    private UiDensity _currentDensity = UiDensity.Comfortable;

    public AppTheme CurrentTheme => _currentTheme;
    public UiDensity CurrentDensity => _currentDensity;

    /// <summary>
    /// Initializes the theme service by capturing references to the active
    /// theme and density dictionaries from Application.Resources.
    /// MUST be called during App.OnStartup after dictionaries are loaded.
    /// </summary>
    /// <param name="theme">Initial theme to activate</param>
    /// <param name="density">Initial density to activate</param>
    public void Initialize(AppTheme theme, UiDensity density)
    {
        if (WpfApplication.Current?.Resources is not ResourceDictionary appResources)
            return;

        // Expected exact normalized source URIs for the design-system dictionaries
        Uri lightThemeUri = new("DesignSystem/LightTheme.xaml", UriKind.Relative);
        Uri darkThemeUri = new("DesignSystem/DarkTheme.xaml", UriKind.Relative);
        Uri comfortableDensityUri = new("DesignSystem/ComfortableDensity.xaml", UriKind.Relative);
        Uri compactDensityUri = new("DesignSystem/CompactDensity.xaml", UriKind.Relative);

        ResourceDictionary? foundTheme = null;
        ResourceDictionary? foundDensity = null;
        int themeCount = 0;
        int densityCount = 0;

        // Locate the active theme and density dictionaries using exact normalized source-URI equality
        foreach (ResourceDictionary dict in appResources.MergedDictionaries)
        {
            if (dict.Source is null)
                continue;

            // Normalize both URIs for comparison
            Uri normalizedSource = new(dict.Source.OriginalString, UriKind.RelativeOrAbsolute);
            if (!normalizedSource.IsAbsoluteUri)
            {
                // Compare normalized relative URIs
                string normalized = normalizedSource.OriginalString.Replace('\\', '/');

                if (normalized.Equals(lightThemeUri.OriginalString, StringComparison.OrdinalIgnoreCase))
                {
                    foundTheme = dict;
                    _currentTheme = AppTheme.Light;
                    themeCount++;
                }
                else if (normalized.Equals(darkThemeUri.OriginalString, StringComparison.OrdinalIgnoreCase))
                {
                    foundTheme = dict;
                    _currentTheme = AppTheme.Dark;
                    themeCount++;
                }
                else if (normalized.Equals(comfortableDensityUri.OriginalString, StringComparison.OrdinalIgnoreCase))
                {
                    foundDensity = dict;
                    _currentDensity = UiDensity.Comfortable;
                    densityCount++;
                }
                else if (normalized.Equals(compactDensityUri.OriginalString, StringComparison.OrdinalIgnoreCase))
                {
                    foundDensity = dict;
                    _currentDensity = UiDensity.Compact;
                    densityCount++;
                }
            }
        }

        // Fail explicitly if missing or duplicate dictionaries are found
        if (themeCount != 1)
            throw new InvalidOperationException($"Expected exactly one theme dictionary, found {themeCount}. Ensure App.xaml merges exactly one of LightTheme.xaml or DarkTheme.xaml.");

        if (densityCount != 1)
            throw new InvalidOperationException($"Expected exactly one density dictionary, found {densityCount}. Ensure App.xaml merges exactly one of ComfortableDensity.xaml or CompactDensity.xaml.");

        _activeThemeDictionary = foundTheme;
        _activeDensityDictionary = foundDensity;

        // If requested theme/density differs from what was loaded, switch now
        if (theme != _currentTheme)
            SetTheme(theme);

        if (density != _currentDensity)
            SetDensity(density);
        OperatorDesign.UpdateTheme(_currentTheme);
        OperatorDesign.UpdateDensity(_currentDensity);
    }

    /// <summary>
    /// Switches the active theme by replacing the theme ResourceDictionary.
    /// </summary>
    public void SetTheme(AppTheme newTheme)
    {
        if (newTheme == _currentTheme || WpfApplication.Current?.Resources is not ResourceDictionary appResources)
            return;

        string newThemePath = newTheme switch
        {
            AppTheme.Light => "DesignSystem/LightTheme.xaml",
            AppTheme.Dark => "DesignSystem/DarkTheme.xaml",
            _ => "DesignSystem/LightTheme.xaml"
        };

        try
        {
            ResourceDictionary newDict = new()
            {
                Source = new Uri(newThemePath, UriKind.Relative)
            };

            // Find the index of the current theme dictionary
            int themeIndex = -1;
            if (_activeThemeDictionary is not null)
                themeIndex = appResources.MergedDictionaries.IndexOf(_activeThemeDictionary);

            if (themeIndex >= 0)
            {
                // Replace at the exact index
                appResources.MergedDictionaries[themeIndex] = newDict;
            }
            else
            {
                // Fallback: insert after Tokens.xaml (index 0)
                if (appResources.MergedDictionaries.Count > 1)
                    appResources.MergedDictionaries.Insert(1, newDict);
                else
                    appResources.MergedDictionaries.Add(newDict);
            }

            _activeThemeDictionary = newDict;
            _currentTheme = newTheme;
            OperatorDesign.UpdateTheme(newTheme);
        }
        catch
        {
            // If replacement fails, leave the previous theme active
        }
    }

    /// <summary>
    /// Switches the active UI density by replacing the density ResourceDictionary.
    /// </summary>
    public void SetDensity(UiDensity newDensity)
    {
        if (newDensity == _currentDensity || WpfApplication.Current?.Resources is not ResourceDictionary appResources)
            return;

        string newDensityPath = newDensity switch
        {
            UiDensity.Comfortable => "DesignSystem/ComfortableDensity.xaml",
            UiDensity.Compact => "DesignSystem/CompactDensity.xaml",
            _ => "DesignSystem/ComfortableDensity.xaml"
        };

        try
        {
            ResourceDictionary newDict = new()
            {
                Source = new Uri(newDensityPath, UriKind.Relative)
            };

            // Find the index of the current density dictionary
            int densityIndex = -1;
            if (_activeDensityDictionary is not null)
                densityIndex = appResources.MergedDictionaries.IndexOf(_activeDensityDictionary);

            if (densityIndex >= 0)
            {
                // Replace at the exact index
                appResources.MergedDictionaries[densityIndex] = newDict;
            }
            else
            {
                // Fallback: insert after theme (index 1)
                if (appResources.MergedDictionaries.Count > 2)
                    appResources.MergedDictionaries.Insert(2, newDict);
                else
                    appResources.MergedDictionaries.Add(newDict);
            }

            _activeDensityDictionary = newDict;
            _currentDensity = newDensity;
            OperatorDesign.UpdateDensity(newDensity);
        }
        catch
        {
            // If replacement fails, leave the previous density active
        }
    }
}

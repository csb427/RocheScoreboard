using RocheScoreboard.Mac.Production;
// Generated from Views/TeamLibraryWindow.xaml.cs; edit translator/adapters.
// Source SHA-256: f94e9078b88c85ec56142ee7c5998cc017572aa8d3cddcb1851ce356cca24c45
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Input;
using RocheScoreboard.Mac.Native;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using MediaBrush = Avalonia.Media.IBrush;
using MediaBrushes = Avalonia.Media.Brushes;
using MediaColor = Avalonia.Media.Color;
using MediaColorConverter = RocheScoreboard.Mac.Native.ColorConverter;

namespace Roche_Scoreboard.Views;

public partial class TeamLibraryWindow : NativeWindow
{
    private readonly string _targetLabel;
    private TeamCatalogEntry? _selectedTeam;
    private string? _logoPath;
    private int _selectionVersion;
    private readonly HashSet<string> _favouriteIds;
    private bool _restoringFilters = true;

    public TeamLibrarySelection? Selection { get; private set; }
    public bool PresetsChanged { get; private set; }

    public TeamLibraryWindow(string targetLabel)
    {
        NativePointer.EnsureInitialized(); InitializeComponent(); ProductionNames.CompleteLoad(this);
        _targetLabel = targetLabel;
        _favouriteIds = PresetStorage.LoadAll()
            .Where(p => !string.IsNullOrWhiteSpace(p.LibraryTeamId))
            .Select(p => p.LibraryTeamId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        TargetHeading.Text = $"Choose the {targetLabel.ToLowerInvariant()} team";
        RestoreFilters();
        _restoringFilters = false;
        RefreshResults();
        SearchBox.Focus();
    }

    private void Filter_Changed(object? sender, EventArgs e)
    {
        if (_restoringFilters || (!IsLoaded && TeamList is null)) return;
        RememberFilters();
        RefreshResults();
    }

    private void RestoreFilters()
    {
        OperatorWorkflowPreferencesData remembered = OperatorWorkflowPreferences.Load();
        SelectComboItem(LeagueFilter, remembered.TeamLibraryLeague);
        SelectComboItem(StateFilter, remembered.TeamLibraryState);
        SelectComboItem(SortFilter, remembered.TeamLibrarySort);
        FavouriteOnly.IsChecked = remembered.TeamLibraryFavouritesOnly;
    }

    private void RememberFilters()
    {
        string league = SelectedContent(LeagueFilter, "All leagues");
        string state = SelectedContent(StateFilter, "All states");
        string sort = SelectedContent(SortFilter, "A–Z");
        bool favouritesOnly = FavouriteOnly?.IsChecked == true;
        OperatorWorkflowPreferences.Update(remembered =>
        {
            remembered.TeamLibraryLeague = league;
            remembered.TeamLibraryState = state;
            remembered.TeamLibrarySort = sort;
            remembered.TeamLibraryFavouritesOnly = favouritesOnly;
        });
    }

    private static void SelectComboItem(Avalonia.Controls.ComboBox comboBox, string content)
    {
        foreach (object? item in comboBox.Items)
        {
            if (item is ComboBoxItem comboItem &&
                string.Equals(comboItem.Content?.ToString(), content, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = comboItem;
                return;
            }
        }
        comboBox.SelectedIndex = 0;
    }

    private static string SelectedContent(Avalonia.Controls.ComboBox? comboBox, string fallback)
        => (comboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? fallback;

    private void RefreshResults()
    {
        string query = (SearchBox?.Text ?? string.Empty).Trim() ?? string.Empty;
        string league = (LeagueFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All leagues";
        string state = (StateFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All states";
        string sort = (SortFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "A–Z";
        bool favouritesOnly = FavouriteOnly?.IsChecked == true;
        IEnumerable<TeamCatalogEntry> filtered = TeamCatalogService.GetTeams().Where(team =>
            (league == "All leagues" || team.League == league) &&
            (state == "All states" || team.State == state) &&
            (!favouritesOnly || _favouriteIds.Contains(team.Id)) &&
            (query.Length == 0 || team.TeamName.Contains(query, StringComparison.OrdinalIgnoreCase)
                               || team.Abbreviation.Contains(query, StringComparison.OrdinalIgnoreCase)));
        foreach (TeamCatalogEntry team in filtered)
            team.IsFavourite = _favouriteIds.Contains(team.Id);
        filtered = sort switch
        {
            "League" => filtered.OrderBy(team => team.League).ThenBy(team => team.TeamName),
            "State" => filtered.OrderBy(team => team.State).ThenBy(team => team.TeamName),
            "Favourites" => filtered.OrderByDescending(team => team.IsFavourite).ThenBy(team => team.TeamName),
            _ => filtered.OrderBy(team => team.TeamName)
        };
        List<TeamCatalogEntry> teams = filtered.ToList();
        TeamList.ItemsSource = teams;
        ResultCount.Text = $"{teams.Count} club{(teams.Count == 1 ? string.Empty : "s")}  ·  {_favouriteIds.Count} saved";
    }

    private async void TeamList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectedTeam = TeamList.SelectedItem as TeamCatalogEntry;
        _logoPath = null;
        UseButton.IsEnabled = FavouriteButton.IsEnabled = false;
        LogoPreview.Source = null;
        LogoPlaceholder.IsVisible = true;
        if (_selectedTeam is null) return;

        int version = ++_selectionVersion;
        SelectedName.Text = _selectedTeam.TeamName;
        SelectedMeta.Text = $"{_selectedTeam.Abbreviation}  ·  {_selectedTeam.League}  ·  {_selectedTeam.State}";
        PrimarySwatch.Background = ColorBrush(_selectedTeam.PrimaryColor);
        SecondarySwatch.Background = ColorBrush(_selectedTeam.SecondaryColor);
        StatusText.Text = "Preparing transparent logo…";
        AnimateDetails();

        try
        {
            string path = await TeamLogoService.GetTransparentLogoAsync(_selectedTeam);
            if (version != _selectionVersion) return;
            _logoPath = path;
            LogoPreview.Source = ImageLoadHelper.LoadTrimmed(path, 512);
            LogoPlaceholder.IsVisible = false;
            StatusText.Text = "Transparent logo ready and cached locally.";
            UseButton.IsEnabled = FavouriteButton.IsEnabled = true;
            RefreshFavouriteButton();
        }
        catch (Exception ex)
        {
            if (version != _selectionVersion) return;
            StatusText.Text = "Logo could not be prepared. Check the internet connection and try again.\n" + ex.Message;
        }
    }

    private async void Favourite_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedTeam is null || string.IsNullOrWhiteSpace(_logoPath)) return;
        List<TeamPreset> presets = await PresetStorage.LoadAllAsync();
        TeamPreset? existing = presets.FirstOrDefault(p => p.LibraryTeamId == _selectedTeam.Id);
        if (existing is null)
        {
            presets.Add(CreatePreset(_selectedTeam, _logoPath));
            _favouriteIds.Add(_selectedTeam.Id);
            StatusText.Text = "Added to saved team presets.";
        }
        else
        {
            presets.Remove(existing);
            _favouriteIds.Remove(_selectedTeam.Id);
            StatusText.Text = "Removed from saved team presets.";
        }
        await PresetStorage.SaveAllAsync(presets);
        PresetsChanged = true;
        RefreshFavouriteButton();
        RefreshResults();
    }

    private void RefreshFavouriteButton()
    {
        bool favourite = _selectedTeam is not null && _favouriteIds.Contains(_selectedTeam.Id);
        FavouriteButton.Content = favourite ? "★  SAVED IN PRESETS" : "☆  ADD TO PRESETS";
    }

    private void AnimateDetails()
    {
        DetailContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        DetailTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void Use_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedTeam is null || string.IsNullOrWhiteSpace(_logoPath)) return;
        Selection = new TeamLibrarySelection(_selectedTeam, _logoPath);
        DialogResult = true;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private static TeamPreset CreatePreset(TeamCatalogEntry team, string logoPath) => new()
    {
        PresetName = team.TeamName,
        TeamName = team.TeamName,
        Abbreviation = team.Abbreviation,
        PrimaryColor = team.PrimaryColor,
        SecondaryColor = team.SecondaryColor,
        LogoPath = logoPath,
        LibraryTeamId = team.Id
    };

    private static MediaBrush ColorBrush(string value)
    {
        try { return new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(value)); }
        catch { return MediaBrushes.Transparent; }
    }
}

public partial class TeamLibraryWindow
{
    private TranslateTransform DetailTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["DetailTranslate"];
    private void Native_Filter_Changed(object? sender, RoutedEventArgs e) { Filter_Changed(sender!, e); }
    private void Native_Filter_Changed(object? sender, SelectionChangedEventArgs e) { Filter_Changed(sender!, e); }
    private void Native_Filter_Changed(object? sender, TextChangedEventArgs e) { Filter_Changed(sender!, e); }
    private void Native_TeamList_SelectionChanged(object? sender, SelectionChangedEventArgs e) { TeamList_SelectionChanged(sender!, e); }
}

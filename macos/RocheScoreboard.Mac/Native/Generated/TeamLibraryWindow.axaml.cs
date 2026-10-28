using RocheScoreboard.Mac.Production;
// Generated from Views/TeamLibraryWindow.xaml.cs; edit translator/adapters.
// Source SHA-256: 25cdd4a1a725ae7696d28b4182f21e4baca71139d3cb1db845e1b06820456380
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
    private readonly TeamLibrarySport _sport;
    private TeamCatalogEntry? _selectedTeam;
    private string? _logoPath;
    private int _selectionVersion;
    private readonly HashSet<string> _favouriteIds;
    private bool _restoringFilters = true;
    private CancellationTokenSource? _logoCancellation;
    private bool _savingFavourite;

    public TeamLibrarySelection? Selection { get; private set; }
    public bool PresetsChanged { get; private set; }

    public TeamLibraryWindow(string targetLabel, TeamLibrarySport sport = TeamLibrarySport.AustralianFootball)
    {
        NativePointer.EnsureInitialized(); ProductionNames.BeginLoad(this); try { InitializeComponent(); } finally { ProductionNames.CompleteLoad(this); }
        SharedSurfaceMotion.Attach(LibrarySurface);
        _sport = sport;
        _targetLabel = targetLabel;
        _favouriteIds = (sport == TeamLibrarySport.Cricket ? PresetStorage.LoadAllCricket() : PresetStorage.LoadAll())
            .Where(p => !string.IsNullOrWhiteSpace(p.LibraryTeamId))
            .Select(p => p.LibraryTeamId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        TargetHeading.Text = $"Choose the {targetLabel.ToLowerInvariant()} team";
        Title = sport == TeamLibrarySport.Cricket ? "Cricket Club Library" : "Australian Football Club Library";
        CatalogueDescription.Text = sport == TeamLibrarySport.Cricket ? "Victorian Premier · VSDCA · official crests available offline" : "AFL · VAFA · APS · AGSV · transparent club crests";
        LeagueFilter.Items.Clear(); LeagueFilter.Items.Add(new ComboBoxItem { Content = "All leagues" });
        foreach (string league in TeamCatalogService.GetTeams(sport).Select(team => team.League).Distinct().OrderBy(league => league))
            LeagueFilter.Items.Add(new ComboBoxItem { Content = league });
        LeagueFilter.SelectedIndex = 0;
        RestoreFilters();
        _restoringFilters = false;
        RefreshResults();
        SearchBox.Focus();
        SizeChanged += (_, _) => ArrangeLibrary();
        Loaded += (_, _) => ArrangeLibrary();
        Closed += (_, _) => { ++_selectionVersion; _logoCancellation?.Cancel(); _logoCancellation?.Dispose(); };
    }

    private void Window_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    { if (e.Key == Avalonia.Input.Key.Escape) { e.Handled = true; Close(); } }

    private void ArrangeLibrary()
    {
        bool narrow = Bounds.Width < 820;
        Grid.SetColumn(DetailSurface, narrow ? 0 : 1); Grid.SetRow(DetailSurface, narrow ? 1 : 0);
        ListColumn.Width = new GridLength(narrow ? 1 : 1.2, GridUnitType.Star);
        DetailColumn.Width = narrow ? new GridLength(0) : new GridLength(.8, GridUnitType.Star);
        DetailSurface.Margin = narrow ? new Thickness(0, 12, 0, 0) : new Thickness(16, 0, 0, 0);
        TeamList.Height = narrow ? 240 : Math.Clamp(Bounds.Height - 330, 160, 620);
    }

    private void Search_Changed(object? sender, Avalonia.Controls.TextChangedEventArgs e) => Filter_Changed(sender,e);
    private void Filter_Changed(object? sender, EventArgs e)
    {
        if (_restoringFilters || (!IsLoaded && TeamList is null)) return;
        if (!ReferenceEquals(sender, SearchBox)) RememberFilters();
        RefreshResults();
    }

    private void RestoreFilters()
    {
        OperatorWorkflowPreferencesData remembered = OperatorWorkflowPreferences.Load();
        bool cricket = _sport == TeamLibrarySport.Cricket;
        SelectComboItem(LeagueFilter, cricket ? remembered.CricketTeamLibraryLeague : remembered.TeamLibraryLeague);
        SelectComboItem(StateFilter, cricket ? remembered.CricketTeamLibraryState : remembered.TeamLibraryState);
        SelectComboItem(SortFilter, cricket ? remembered.CricketTeamLibrarySort : remembered.TeamLibrarySort);
        FavouriteOnly.IsChecked = cricket ? remembered.CricketTeamLibraryFavouritesOnly : remembered.TeamLibraryFavouritesOnly;
    }

    private void RememberFilters()
    {
        if (Avalonia.Application.Current is RocheScoreboard.Mac.App { IsSelfTestMode: true }) return;
        string league = SelectedContent(LeagueFilter, "All leagues");
        string state = SelectedContent(StateFilter, "All states");
        string sort = SelectedContent(SortFilter, "A–Z");
        bool favouritesOnly = FavouriteOnly?.IsChecked == true;
        OperatorWorkflowPreferences.Update(remembered =>
        {
            if (_sport == TeamLibrarySport.Cricket)
            {
                remembered.CricketTeamLibraryLeague = league;
                remembered.CricketTeamLibraryState = state;
                remembered.CricketTeamLibrarySort = sort;
                remembered.CricketTeamLibraryFavouritesOnly = favouritesOnly;
                return;
            }
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
        IEnumerable<TeamCatalogEntry> filtered = TeamCatalogService.GetTeams(_sport).Where(team =>
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
        EmptyResults.IsVisible = teams.Count == 0 ? true : false;
        ResultCount.Text = $"{teams.Count} club{(teams.Count == 1 ? string.Empty : "s")}  ·  {_favouriteIds.Count} saved";
    }

    private async void TeamList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        int version = ++_selectionVersion;
        _logoCancellation?.Cancel(); _logoCancellation?.Dispose(); _logoCancellation = new();
        _selectedTeam = TeamList.SelectedItem as TeamCatalogEntry;
        _logoPath = null;
        UseButton.IsEnabled = FavouriteButton.IsEnabled = false;
        LogoPreview.Source = null;
        LogoPlaceholder.IsVisible = true;
        if (_selectedTeam is null)
        {
            SelectedName.Text = "Choose a club from the list"; SelectedMeta.Text = ""; StatusText.Text = "";
            PrimarySwatch.Background = SecondarySwatch.Background = MediaBrushes.Transparent;
            return;
        }

        SelectedName.Text = _selectedTeam.TeamName;
        SelectedMeta.Text = $"{_selectedTeam.Abbreviation}  ·  {_selectedTeam.League}  ·  {_selectedTeam.State}";
        PrimarySwatch.Background = ColorBrush(_selectedTeam.PrimaryColor);
        SecondarySwatch.Background = ColorBrush(_selectedTeam.SecondaryColor);
        StatusText.Text = "Preparing transparent logo…";
        AnimateDetails();

        try
        {
            string path = await TeamLogoService.GetTransparentLogoAsync(_selectedTeam, _logoCancellation.Token);
            if (version != _selectionVersion) return;
            _logoPath = path;
            LogoPreview.Source = ImageLoadHelper.LoadTrimmed(path, 512);
            LogoPlaceholder.IsVisible = false;
            StatusText.Text = "Ready to select";
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
        if (_savingFavourite || _selectedTeam is null || string.IsNullOrWhiteSpace(_logoPath)) return;
        _savingFavourite = true;
        try
        {
            var team = _selectedTeam; string path = _logoPath; FavouriteButton.IsEnabled = false;
            List<TeamPreset> presets = _sport == TeamLibrarySport.Cricket ? await PresetStorage.LoadAllCricketAsync() : await PresetStorage.LoadAllAsync();
            TeamPreset? existing = presets.FirstOrDefault(p => p.LibraryTeamId == team.Id);
            if (existing is null)
            {
                presets.Add(CreatePreset(team, path));
                _favouriteIds.Add(team.Id);
                StatusText.Text = "Added to saved team presets.";
            }
            else
            {
                presets.Remove(existing);
                _favouriteIds.Remove(team.Id);
                StatusText.Text = "Removed from saved team presets.";
            }
            if (_sport == TeamLibrarySport.Cricket) await PresetStorage.SaveAllCricketAsync(presets); else await PresetStorage.SaveAllAsync(presets);
            var persisted = _sport == TeamLibrarySport.Cricket ? await PresetStorage.LoadAllCricketAsync() : await PresetStorage.LoadAllAsync();
            if (persisted.Any(preset => preset.LibraryTeamId == team.Id) != (existing is null))
            {
                _favouriteIds.Clear(); foreach (var preset in persisted) if (preset.LibraryTeamId is { } id) _favouriteIds.Add(id);
                throw new System.IO.IOException("The preset file could not be updated.");
            }
            PresetsChanged = true;
            RefreshFavouriteButton();
            RefreshResults();
            TeamList.SelectedItem = TeamList.Items.Cast<TeamCatalogEntry>().FirstOrDefault(item => item.Id == team.Id);
        }
        catch (Exception error) { StatusText.Text = "Could not save this club. Please try again. " + error.Message; }
        finally { _savingFavourite = false; FavouriteButton.IsEnabled = _selectedTeam is not null && !string.IsNullOrWhiteSpace(_logoPath); }
    }

    private void RefreshFavouriteButton()
    {
        bool favourite = _selectedTeam is not null && _favouriteIds.Contains(_selectedTeam.Id);
        FavouriteButton.Tag = favourite;
        FavouriteButton.Content = favourite ? "Remove from presets" : "Save to presets";
        Avalonia.Automation.AutomationProperties.SetItemStatus(FavouriteButton, favourite ? "Saved in presets" : "Not saved");
    }

    private void AnimateDetails()
    {
        SharedSurfaceMotion.Reveal(DetailContent);
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
    private ColumnDefinition ListColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["ListColumn"];
    private ColumnDefinition DetailColumn => (ColumnDefinition)ProductionNames.RegistryFor(this)["DetailColumn"];
    private ScaleTransform TbSc => (ScaleTransform)ProductionNames.RegistryFor(this)["TbSc"];
    private TranslateTransform DetailTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["DetailTranslate"];
    private void Native_Filter_Changed(object? sender, RoutedEventArgs e) { Filter_Changed(sender!, e); }
    private void Native_Filter_Changed(object? sender, SelectionChangedEventArgs e) { Filter_Changed(sender!, e); }
    private void Native_Search_Changed(object? sender, TextChangedEventArgs e) { Search_Changed(sender!, e); }
    private void Native_TeamList_SelectionChanged(object? sender, SelectionChangedEventArgs e) { TeamList_SelectionChanged(sender!, e); }
}

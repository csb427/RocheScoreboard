using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace Roche_Scoreboard.Views;

public partial class TeamLibraryWindow : Window
{
    private readonly string _targetLabel;
    private readonly TeamLibrarySport _sport;
    private TeamCatalogEntry? _selectedTeam;
    private string? _logoPath;
    private int _selectionVersion;
    private readonly HashSet<string> _favouriteIds;
    private bool _restoringFilters = true;
    private CancellationTokenSource? _logoCancellation;
    private bool _savingFavourite, _closed, _refreshingResults, _membershipLoading;
    private int _presetReadVersion, _operationSelectionVersion;
    private long _presetRevision;
    private string? _operationTeamId;
    private string _operationMessage = "";
    private readonly List<Task<PresetCommitResult>> _acceptedPresetChanges = new();

    public TeamLibrarySelection? Selection { get; private set; }
    public bool PresetsChanged { get; private set; }
    // Snapshot of accepted work; callers capture this at modal return. Storage
    // owns completion and has no dependency on this Window or its dispatcher.
    public Task<PresetCommitResult[]> AcceptedPresetChanges => Task.WhenAll(_acceptedPresetChanges.ToArray());

    public TeamLibraryWindow(string targetLabel, TeamLibrarySport sport = TeamLibrarySport.AustralianFootball)
    {
        InitializeComponent();
        SharedSurfaceMotion.Attach(LibrarySurface);
        _sport = sport;
        _targetLabel = targetLabel;
        PresetReadResult initial = PresetStorage.ReadSnapshot(sport);
        _presetRevision = initial.Revision;
        _membershipLoading = initial.Pending;
        _favouriteIds = initial.Presets.Where(p => !string.IsNullOrWhiteSpace(p.LibraryTeamId))
            .Select(p => p.LibraryTeamId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (initial.Pending) { PresetStatus.Text = "Loading saved clubs…"; PresetStatus.Visibility = Visibility.Visible; }
        if (!initial.Succeeded) { PresetStatus.Text = initial.Error; PresetStatus.Visibility = Visibility.Visible; }
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
        Loaded += (_, _) => { ArrangeLibrary(); RefreshSavedMembership(); };
        IsVisibleChanged += (_, args) =>
        {
            if (_closed) return;
            if (args.NewValue is false)
            {
                ++_selectionVersion; ++_presetReadVersion;
                _logoCancellation?.Cancel();
            }
            else if (IsLoaded && !_closed)
            {
                RefreshSavedMembership();
                if (_selectedTeam is not null)
                {
                    if (string.IsNullOrWhiteSpace(_logoPath)) LoadSelectedTeam();
                    else { StatusText.Text = "Ready to select"; RenderOperationStatus(); }
                }
            }
        };
        Closed += (_, _) => { _closed = true; ++_selectionVersion; ++_presetReadVersion; _logoCancellation?.Cancel(); _logoCancellation?.Dispose(); };
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; Close(); } }

    private void ArrangeLibrary()
    {
        bool narrow = ActualWidth < 820;
        Grid.SetColumn(DetailSurface, narrow ? 0 : 1); Grid.SetRow(DetailSurface, narrow ? 1 : 0);
        ListColumn.Width = new GridLength(narrow ? 1 : 1.2, GridUnitType.Star);
        DetailColumn.Width = narrow ? new GridLength(0) : new GridLength(.8, GridUnitType.Star);
        DetailSurface.Margin = narrow ? new Thickness(0, 12, 0, 0) : new Thickness(16, 0, 0, 0);
        TeamList.Height = narrow ? 240 : Math.Clamp(ActualHeight - 330, 160, 620);
    }

    private void Search_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => Filter_Changed(sender,e);
    private void Filter_Changed(object sender, EventArgs e)
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
        if (System.Windows.Application.Current is App { IsSelfTestMode: true }) return;
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

    private static void SelectComboItem(System.Windows.Controls.ComboBox comboBox, string content)
    {
        foreach (object item in comboBox.Items)
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

    private static string SelectedContent(System.Windows.Controls.ComboBox? comboBox, string fallback)
        => (comboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? fallback;

    private void RefreshResults()
    {
        string? selectedId = _selectedTeam?.Id;
        bool restoreListFocus = TeamList.IsKeyboardFocusWithin;
        ScrollViewer? scroll = EnumerateDescendantVisuals(TeamList).OfType<ScrollViewer>().FirstOrDefault();
        double offset = scroll?.VerticalOffset ?? 0;
        string query = SearchBox?.Text.Trim() ?? string.Empty;
        string league = (LeagueFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All leagues";
        string state = (StateFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All states";
        string sort = (SortFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "A–Z";
        bool favouritesOnly = FavouriteOnly?.IsChecked == true;
        IEnumerable<TeamCatalogEntry> filtered = TeamCatalogService.GetTeams(_sport).Where(team =>
            (league == "All leagues" || team.League == league) &&
            (state == "All states" || team.State == state) &&
            (!favouritesOnly || _favouriteIds.Contains(team.Id)) &&
            (query.Length == 0 || team.TeamName.Contains(query, StringComparison.OrdinalIgnoreCase)
                               || team.Abbreviation.Contains(query, StringComparison.OrdinalIgnoreCase)))
            // IsFavourite is not notifying. Detached presentation records let
            // recycled templates observe the new value without mutating the
            // shared catalogue or restarting the selected club's logo loader.
            .Select(team => team with { IsFavourite = _favouriteIds.Contains(team.Id) });
        filtered = sort switch
        {
            "League" => filtered.OrderBy(team => team.League).ThenBy(team => team.TeamName),
            "State" => filtered.OrderBy(team => team.State).ThenBy(team => team.TeamName),
            "Favourites" => filtered.OrderByDescending(team => team.IsFavourite).ThenBy(team => team.TeamName),
            _ => filtered.OrderBy(team => team.TeamName)
        };
        List<TeamCatalogEntry> teams = filtered.ToList();
        TeamCatalogEntry? selected = teams.FirstOrDefault(team => team.Id == selectedId);
        _refreshingResults = true;
        try { TeamList.ItemsSource = teams; TeamList.SelectedItem = selected; }
        finally { _refreshingResults = false; }
        if (selected is null && _selectedTeam is not null) LoadSelectedTeam();
        else if (selected is not null) _selectedTeam = selected;
        EmptyResults.Visibility = teams.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ResultCount.Text = $"{teams.Count} club{(teams.Count == 1 ? string.Empty : "s")}  ·  {_favouriteIds.Count} saved";
        RefreshFavouriteButton();
        RenderOperationStatus();
        if (scroll is not null) scroll.ScrollToVerticalOffset(offset);
        if (restoreListFocus && selected is not null && IsLoaded && IsVisible && !_closed)
        {
            TeamList.UpdateLayout();
            EnumerateDescendantVisuals(TeamList).OfType<ListBoxItem>().FirstOrDefault(row =>
                row.DataContext is TeamCatalogEntry team && team.Id == selected.Id)?.Focus();
            if (scroll is not null) scroll.ScrollToVerticalOffset(offset);
        }
    }

    private static IEnumerable<DependencyObject> EnumerateDescendantVisuals(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (DependencyObject descendant in EnumerateDescendantVisuals(child)) yield return descendant;
        }
    }

    private async void RefreshSavedMembership()
    {
        if (_closed || !IsLoaded || !IsVisible) return;
        int read = ++_presetReadVersion;
        _membershipLoading = true;
        RefreshFavouriteButton();
        PresetReadResult result = await PresetStorage.ReadAsync(_sport);
        if (_closed || read != _presetReadVersion || !IsLoaded || !IsVisible) return;
        _membershipLoading = false;
        if (result.Succeeded)
        {
            if (string.IsNullOrWhiteSpace(_operationMessage)) { PresetStatus.Text = ""; PresetStatus.Visibility = Visibility.Collapsed; }
            PublishMembership(result);
        }
        else { PresetStatus.Text = result.Error; PresetStatus.Visibility = Visibility.Visible; }
        RefreshFavouriteButton();
    }

    private void PublishMembership(PresetReadResult result)
    {
        if (!result.Succeeded || result.Revision < _presetRevision) return;
        _presetRevision = result.Revision;
        _favouriteIds.Clear();
        foreach (TeamPreset preset in result.Presets)
            if (!string.IsNullOrWhiteSpace(preset.LibraryTeamId)) _favouriteIds.Add(preset.LibraryTeamId!);
        RefreshResults();
    }

    private void TeamList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshingResults && !_closed) LoadSelectedTeam();
    }

    private async void LoadSelectedTeam()
    {
        int version = ++_selectionVersion;
        _logoCancellation?.Cancel(); _logoCancellation?.Dispose(); _logoCancellation = new();
        _selectedTeam = TeamList.SelectedItem as TeamCatalogEntry;
        _logoPath = null;
        UseButton.IsEnabled = FavouriteButton.IsEnabled = false;
        LogoPreview.Source = null;
        LogoPlaceholder.Visibility = Visibility.Visible;
        RenderOperationStatus();
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
            if (_closed || version != _selectionVersion) return;
            _logoPath = path;
            LogoPreview.Source = ImageLoadHelper.LoadTrimmed(path, 512);
            LogoPlaceholder.Visibility = Visibility.Collapsed;
            StatusText.Text = "Ready to select";
            UseButton.IsEnabled = true;
            RefreshFavouriteButton();
            RenderOperationStatus();
        }
        catch (Exception ex)
        {
            if (_closed || version != _selectionVersion) return;
            StatusText.Text = "Logo could not be prepared. Check the internet connection and try again.\n" + ex.Message;
        }
    }

    private async void Favourite_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _savingFavourite || _membershipLoading || _selectedTeam is null || string.IsNullOrWhiteSpace(_logoPath)) return;
        TeamCatalogEntry team = _selectedTeam;
        string path = _logoPath;
        bool save = !_favouriteIds.Contains(team.Id);
        _savingFavourite = true;
        _operationTeamId = team.Id; _operationSelectionVersion = _selectionVersion;
        _operationMessage = save ? $"Saving {team.TeamName}…" : $"Removing {team.TeamName}…";
        RefreshFavouriteButton(); RenderOperationStatus();
        // Register the actual storage Task before yielding. Close/Use only
        // cancels UI observation; neither can cancel or undo accepted IO.
        Task<PresetCommitResult> accepted = PresetStorage.SetLibraryMembershipAsync(_sport, CreatePreset(team, path), save);
        _acceptedPresetChanges.Add(accepted);
        PresetCommitResult result = await accepted;
        if (result.Changed) PresetsChanged = true;
        _savingFavourite = false;
        // The operation outcome outlives visibility. Remember its text without
        // touching hidden controls; a later Show renders the completed result.
        if (!result.Succeeded)
            _operationMessage = result.Error.StartsWith("Could not save", StringComparison.OrdinalIgnoreCase)
                ? result.Error : "Could not save this club. " + result.Error;
        else if (!result.Changed) _operationMessage = "Saved clubs are up to date.";
        else _operationMessage = save ? $"{team.TeamName} added to saved team presets." : $"{team.TeamName} removed from saved team presets.";
        if (_closed || !IsLoaded || !IsVisible) return;
        PublishMembership(result.Snapshot);
        RefreshFavouriteButton(); RenderOperationStatus();
    }

    private void RenderOperationStatus()
    {
        if (_closed || string.IsNullOrWhiteSpace(_operationMessage)) return;
        bool ownsSelection = _selectedTeam?.Id == _operationTeamId && _selectionVersion == _operationSelectionVersion
            && !string.IsNullOrWhiteSpace(_logoPath);
        PresetStatus.Text = _operationMessage;
        PresetStatus.Visibility = ownsSelection ? Visibility.Collapsed : Visibility.Visible;
        if (ownsSelection) StatusText.Text = _operationMessage;
    }

    private void RefreshFavouriteButton()
    {
        bool favourite = _selectedTeam is not null && _favouriteIds.Contains(_selectedTeam.Id);
        FavouriteButton.IsEnabled = !_closed && !_savingFavourite && !_membershipLoading
            && _selectedTeam is not null && !string.IsNullOrWhiteSpace(_logoPath);
        FavouriteButton.Tag = favourite;
        OperatorChoiceMotion.SetSelected(FavouriteButton, favourite);
        FavouriteButton.Content = favourite ? "Remove from presets" : "Save to presets";
        System.Windows.Automation.AutomationProperties.SetItemStatus(FavouriteButton, favourite ? "Saved in presets" : "Not saved");
    }

    private void AnimateDetails()
    {
        SharedSurfaceMotion.Reveal(DetailContent);
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _selectedTeam is null || string.IsNullOrWhiteSpace(_logoPath)) return;
        Selection = new TeamLibrarySelection(_selectedTeam, _logoPath);
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

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

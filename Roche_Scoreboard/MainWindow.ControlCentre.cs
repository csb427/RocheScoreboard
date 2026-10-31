using System.Windows;
using System.Windows.Controls;
using Grid = System.Windows.Controls.Grid;
using ScrollViewer = System.Windows.Controls.ScrollViewer;

namespace Roche_Scoreboard;

/// <summary>Operator-only responsive layout. Public display geometry is independent.</summary>
public partial class MainWindow
{
    private bool? _controlCentreStacked;
    private bool _controlCentreWired;
    private System.Windows.Controls.Primitives.Popup? _aflSettingsCategoryMenu;
    private ScrollViewer? _aflSettingsMenuScroll;
    private Grid? _aflSettingsRailGrid;
    private Views.SettingsWorkspace? _aflSettingsWorkspace;

    private void InstallAflSettingsWorkspace()
    {
        if(_aflSettingsWorkspace is not null)return;
        var workspace=new Views.SettingsWorkspace();
        foreach(var (section,category) in new[]{(SettingsSection_Teams,"MATCH"),(SettingsSection_Match,"MATCH"),
            (FindElement<System.Windows.Controls.Border>("SettingsSection_Keybinds"),"CONTROLS"),
            (SettingsSection_Display,"SCOREBOARD"),(SettingsSection_Overlays,"AUTOMATION"),
            (SettingsSection_Media,"ASSETS"),(SettingsSection_Advanced,"SYSTEM")})
        {
            if(section is null)continue;
            if(section.Parent is System.Windows.Controls.Panel panel)panel.Children.Remove(section);
            section.Tag=category;section.BeginAnimation(OpacityProperty,null);section.Opacity=1;section.RenderTransform=System.Windows.Media.Transform.Identity;
            section.SetResourceReference(StyleProperty,"SettingsSectionSurface");
            section.Background=TryFindResource("ControlCentre.SurfaceBrush") as System.Windows.Media.Brush;
            section.BorderThickness=new Thickness(0);section.Padding=new Thickness(22);section.CornerRadius=new CornerRadius(26);
            RefreshOperatorForm(section);
            workspace.Sections.Add(section);
        }
        _aflSettingsWorkspace=workspace;
        RefreshAflClockChoices();
        RefreshOperatorForm(AflOutputColumns);
        RefreshAflPresentationChoices();
        workspace.CategoryChanged+=category=>{RetireKeybindCapture();_settingsActiveCategory=category.ToLowerInvariant();_uxPreferences.LastSettingsCategory=_settingsActiveCategory;QueueUxPreferencesSave();};
        Grid.SetColumnSpan(workspace,2);Grid.SetRowSpan(workspace,2);AflSettingsColumns.Children.Add(workspace);
    }

    private void ControlCentre_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_controlCentreWired)
        {
            _controlCentreWired = true;
            AflMatchPageViewport.SizeChanged += (_, _) => ArrangeControlCentre();
            AflOutputPageViewport.SizeChanged += (_, _) => ArrangeControlCentre();
            AflMediaPageViewport.SizeChanged += (_, _) => ArrangeControlCentre();
            AflSettingsPageViewport.SizeChanged += (_, _) => ArrangeControlCentre();
            _aflSettingsRailGrid = AflSettingsCategoryList.Parent as Grid;
            _aflSettingsMenuScroll = new ScrollViewer { MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            _aflSettingsCategoryMenu = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = AflSettingsCompactChoice,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                AllowsTransparency = true, StaysOpen = false, VerticalOffset = 8,
                Child = new Views.OperatorGlassSurface { Material = Views.GlassMaterial.Regular, CornerRadius = new CornerRadius(24), Padding = new Thickness(10), Width = 250, Child = _aflSettingsMenuScroll }
            };
            AflOperatorCanvas.Children.Add(_aflSettingsCategoryMenu);
            _aflSettingsMenuScroll.PreviewKeyDown += (_, args) =>
            {
                if (args.Key != System.Windows.Input.Key.Escape) return;
                _aflSettingsCategoryMenu.IsOpen = false; AflSettingsCompactChoice.Focus(); args.Handled = true;
            };
        }
        _controlCentreStacked = null;
        InstallAflSettingsWorkspace();
        ArrangeControlCentre();
    }

    private void ControlCentre_SizeChanged(object sender, SizeChangedEventArgs e)
        => ArrangeControlCentre();

    private void ArrangeControlCentre()
    {
        if (MainContent is null || AflScoringColumns is null || AflOutputColumns is null || AflSettingsColumns is null) return;
        bool stacked = ActualWidth < 900;
        MainContent.MinimumCanvasWidth = 380;
        MainContent.MinimumCanvasHeight = 0;
        MainContent.NaturalHeight = false;
        AflOperatorCanvas.MinHeight = 0;

        if (_controlCentreStacked != stacked)
        {
            ArrangeColumns(AflScoringColumns, stacked, new GridLength(1, GridUnitType.Star), new GridLength(.34, GridUnitType.Star), 330);
            ArrangeColumns(AflOutputColumns, stacked, new GridLength(1.65, GridUnitType.Star), new GridLength(1, GridUnitType.Star), double.NaN);
            ArrangeSupplementaryPages(stacked);
            _controlCentreStacked = stacked;
        }

        // A page owns vertical scrolling; the navigation dock stays outside it.
        SetPageHeight(AflScoringColumns, AflMatchPageViewport, stacked, 24);
        SetPageHeight(AflOutputColumns, AflOutputPageViewport, stacked, 24);
        SetPageHeight(AflMediaColumns, AflMediaPageViewport, stacked, 24);
        // Keep a usable inner form viewport when a short window leaves less
        // space than the category/search header. The page then scrolls as a
        // whole, so its header can leave room for fields without covering the
        // fixed navigation dock.
        const double minimumSettingsPageHeight = 320;
        double settingsHeight = AflSettingsPageViewport.ActualHeight;
        AflSettingsColumns.Height = Math.Max(minimumSettingsPageHeight, settingsHeight);
        AflSettingsPageViewport.VerticalScrollBarVisibility = settingsHeight < minimumSettingsPageHeight
            ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        MatchLogPanel.MaxHeight = stacked ? 330 : double.PositiveInfinity;
        AflHeaderMarginSurface.Visibility = ActualWidth < 1100 ? Visibility.Collapsed : Visibility.Visible;
        StatusBadge.Visibility = ActualWidth < 560 ? Visibility.Collapsed : Visibility.Visible;
        EssentialsModeBadge.Visibility = ActualWidth < 620 ? Visibility.Collapsed : Visibility.Visible;
        LastEventText.Visibility = ActualWidth < 760 ? Visibility.Collapsed : Visibility.Visible;
        HeaderQuarterClock.FontSize = ActualWidth < 560 ? 23 : 27;
        HeaderScore.FontSize = ActualWidth < 560 ? 24 : 27;
        HomeScoreDisplay.FontSize = AwayScoreDisplay.FontSize = ActualWidth < 560 ? 30 : ActualWidth < 900 ? 44 : 58;
        HomeOperatorTotal.FontSize = AwayOperatorTotal.FontSize = ActualWidth < 560 ? 52 : ActualWidth < 900 ? 74 : 94;
        HomeOperatorDetails.FontSize = AwayOperatorDetails.FontSize = ActualWidth < 560 ? 17 : 21;
        HomeOperatorLogo.Width = HomeOperatorLogo.Height = AwayOperatorLogo.Width = AwayOperatorLogo.Height = ActualWidth < 560 ? 32 : 64;
        double commandWidth = Math.Min(HomeGoalButton.ActualWidth, AwayGoalButton.ActualWidth);
        HomeGoalButton.FontSize = AwayGoalButton.FontSize = Math.Clamp((commandWidth - 64) / 7, 16, 28);
        HomeBehindButton.FontSize = AwayBehindButton.FontSize = Math.Clamp((commandWidth - 60) / 9, 16, 24);
        AflPresentationGrid.Columns = ActualWidth < 650 ? 2 : ActualWidth < 1100 ? 3 : 4;
        MainContent.RefreshViewport();
        if(_aflSettingsWorkspace is not null)
        {
            AflSettingsRail.Visibility=AflSettingsCompactBar.Visibility=SettingsScrollViewer.Visibility=SettingsNoResults.Visibility=Visibility.Collapsed;
            AflSettingsColumns.ColumnDefinitions[0].Width=new GridLength(1,GridUnitType.Star);
            AflSettingsColumns.ColumnDefinitions[1].Width=new GridLength(0);
        }
    }

    private void ArrangeSupplementaryPages(bool stacked)
    {
        AflMediaColumns.RowDefinitions.Clear();
        AflMediaColumns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AflMediaColumns.RowDefinitions.Add(new RowDefinition { Height = stacked ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });
        if (stacked) AflMediaColumns.RowDefinitions.Add(new RowDefinition { Height = new GridLength(300) });
        AflMediaColumns.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        AflMediaColumns.ColumnDefinitions[1].Width = new GridLength(stacked ? 0 : 20);
        AflMediaColumns.ColumnDefinitions[2].Width = stacked ? new GridLength(0) : new GridLength(1.2, GridUnitType.Star);
        Grid.SetColumn(AflMediaPreviewCard, stacked ? 0 : 2);
        Grid.SetRow(AflMediaPreviewCard, stacked ? 2 : 1);
        AflMediaPreviewCard.Margin = new Thickness(0, stacked ? 16 : 0, 0, 0);

        AflSettingsColumns.RowDefinitions.Clear();
        if (stacked)
        {
            AflSettingsColumns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AflSettingsColumns.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }
        AflSettingsColumns.ColumnDefinitions[0].Width = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(238);
        AflSettingsColumns.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        foreach (FrameworkElement child in new FrameworkElement[] { SettingsScrollViewer, SettingsNoResults })
        {
            Grid.SetColumn(child, stacked ? 0 : 1);
            Grid.SetRow(child, stacked ? 1 : 0);
        }
        AflSettingsRail.Visibility = stacked ? Visibility.Collapsed : Visibility.Visible;
        AflSettingsCompactBar.Visibility = stacked ? Visibility.Visible : Visibility.Collapsed;
        AflSettingsCategoryList.Orientation = System.Windows.Controls.Orientation.Vertical;
        AflSettingsCategoryList.ItemWidth = 202;
        SettingsScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        if (_aflSettingsCategoryMenu is not null && _aflSettingsMenuScroll is not null && _aflSettingsRailGrid is not null)
        {
            _aflSettingsCategoryMenu.IsOpen = false;
            if (stacked)
            {
                _aflSettingsRailGrid.Children.Remove(AflSettingsCategoryList);
                _aflSettingsRailGrid.Children.Remove(SettingsSearchHost);
                _aflSettingsMenuScroll.Content = AflSettingsCategoryList;
                AflSettingsCompactSearch.Children.Add(SettingsSearchHost);
                SettingsSearchHost.Margin = new Thickness(0);
            }
            else
            {
                _aflSettingsMenuScroll.Content = null;
                AflSettingsCompactSearch.Children.Remove(SettingsSearchHost);
                if (!_aflSettingsRailGrid.Children.Contains(AflSettingsCategoryList)) _aflSettingsRailGrid.Children.Add(AflSettingsCategoryList);
                if (!_aflSettingsRailGrid.Children.Contains(SettingsSearchHost)) _aflSettingsRailGrid.Children.Add(SettingsSearchHost);
                SettingsSearchHost.Margin = new Thickness(0,12,0,0);
            }
        }
    }

    private void AflSettingsCategories_Click(object sender, RoutedEventArgs e)
    {
        if (_aflSettingsCategoryMenu is null) return;
        _aflSettingsCategoryMenu.IsOpen = !_aflSettingsCategoryMenu.IsOpen;
        if (_aflSettingsCategoryMenu.IsOpen && _aflSettingsCategoryMenu.Child is FrameworkElement sheet)
            Views.SharedSurfaceMotion.Reveal(sheet);
    }

    private static void SetPageHeight(Grid grid, ScrollViewer viewport, bool stacked, double inset)
    {
        double height = stacked ? double.NaN : Math.Max(260, viewport.ActualHeight - inset);
        if (double.IsNaN(height))
        {
            if (!double.IsNaN(grid.Height)) grid.Height = double.NaN;
        }
        else if (!double.IsFinite(grid.Height) || Math.Abs(grid.Height - height) > .5) grid.Height = height;
    }

    private static void ArrangeColumns(Grid grid, bool stacked, GridLength left, GridLength right, double lowerHeight)
    {
        grid.RowDefinitions.Clear();
        if (stacked)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = double.IsNaN(lowerHeight) ? GridLength.Auto : new GridLength(lowerHeight) });
        }
        grid.ColumnDefinitions[0].Width = stacked ? new GridLength(1, GridUnitType.Star) : left;
        grid.ColumnDefinitions[1].Width = new GridLength(stacked ? 0 : 12);
        grid.ColumnDefinitions[2].Width = stacked ? new GridLength(0) : right;
        grid.ColumnDefinitions[2].MinWidth = stacked ? 0 : 270;
        foreach (FrameworkElement child in grid.Children)
        {
            if (child.Tag as string == "ControlCentre.Secondary" || Grid.GetColumn(child) == 2)
            {
                child.Tag = "ControlCentre.Secondary";
                Grid.SetColumn(child, stacked ? 0 : 2);
                Grid.SetRow(child, stacked ? 1 : 0);
                child.Margin = new Thickness(0, stacked ? 16 : 0, 0, 0);
            }
            else if (child is ScrollViewer scorer)
                scorer.VerticalScrollBarVisibility = stacked ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        }
    }
}

/// <summary>Formats existing operator text only; the public scoreboard formatter is unchanged.</summary>
public sealed class OperatorScorePartConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        string text = value?.ToString() ?? "0.0.0";
        string[] parts = text.Split('.');
        return parameter as string == "detail" ? parts.Length > 1 ? string.Join(".", parts.Take(parts.Length - 1)) : "" : parts[^1];
    }
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}

public sealed class OperatorLogoConverter : System.Windows.Data.IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return null;
        try
        {
            using var stream = System.IO.File.OpenRead(path);
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit(); image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
            return image;
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or NotSupportedException or System.IO.FileFormatException or ArgumentException)
        { return null; }
    }
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Roche_Scoreboard.Services;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;
using Orientation = System.Windows.Controls.Orientation;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace Roche_Scoreboard.Views;

/// <summary>Shared operator preferences: floating navigation, contextual search and quiet content.</summary>
[ContentProperty(nameof(Sections))]
public sealed class SettingsWorkspace : UserControl
{
    internal event Action<string>? CategoryChanged;
    internal void NavigateCategory(string category) => SelectCategory(category.ToUpperInvariant());
    private readonly Grid _layout = new();
    private readonly OperatorGlassSurface _navigation = new() { Material = GlassMaterial.Regular, CornerRadius = new CornerRadius(28), Padding = new Thickness(12), Margin = new Thickness(14,14,8,14) };
    private readonly StackPanel _categories = new();
    private readonly StackPanel _sections = new();
    private readonly TextBlock _heading = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _subtitle = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,5,0,0) };
    private readonly TextBlock _contextHeading = new() { FontSize = 30, FontWeight = FontWeights.SemiBold };
    private readonly OperatorSearchBox _search = new() { Placeholder = "Search settings", ToolTip = "Search settings", Height = 44, MinHeight = 0 };
    private readonly TextBlock _empty = new() { Text = "No matching settings", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20), Visibility = Visibility.Collapsed };
    private readonly Dictionary<string, Button> _buttons = new();
    private readonly Dictionary<string, double> _scrollPositions = new();
    private readonly Grid _header = new();
    private readonly StackPanel _contextTitle = new();
    private readonly Grid _searchHost = new();
    private readonly Button _compactChoice = new();
    private readonly TextBlock _compactLabel = new();
    private readonly Popup _compactMenu = new() { AllowsTransparency = true, StaysOpen = false, Placement = PlacementMode.Bottom, VerticalOffset = 8 };
    private readonly StackPanel _compactItems = new();
    private readonly Dictionary<string, Button> _compactButtons = new();
    private bool? _compact;
    private int _navigationGeneration;
    private bool _materialSectionAdded;
    private string _category = "ALL";
    public ScrollViewer Scroller { get; } = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(8,0,18,18) };
    public UIElementCollection Sections => _sections.Children;
    public string Heading { get => _heading.Text; set => _heading.Text = value; }
    public string Subtitle { get => _subtitle.Text; set => _subtitle.Text = value; }

    public SettingsWorkspace()
    {
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Roche Scoreboard;component/Resources/OperatorControls.xaml", UriKind.Relative) });
        OperatorDesign.SetIsEnabled(this, true);
        SetResourceReference(BackgroundProperty, "ControlCentre.CanvasBrush");
        foreach (var text in new[] { _heading, _contextHeading, _compactLabel }) text.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.TextBrush");
        foreach (var text in new[] { _subtitle, _empty }) text.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.SecondaryTextBrush");
        _search.SetResourceReference(StyleProperty, typeof(OperatorSearchBox));
        System.Windows.Automation.AutomationProperties.SetName(_search, "Search settings");
        _layout.ColumnDefinitions.Add(new() { Width = new GridLength(264) });
        _layout.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var rail = new Grid(); rail.RowDefinitions.Add(new() { Height = GridLength.Auto }); rail.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var railHeading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10,10,8,20) };
        railHeading.Children.Add(Glyph("CONTROLS", 30)); _heading.Margin = new Thickness(10,0,0,0); _heading.VerticalAlignment = VerticalAlignment.Center; railHeading.Children.Add(_heading); rail.Children.Add(railHeading);
        var railScroll = new ScrollViewer { Content = _categories, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(railScroll,1); rail.Children.Add(railScroll);
        _navigation.Child = rail; _layout.Children.Add(_navigation);
        var content = new Grid(); content.RowDefinitions.Add(new() { Height = GridLength.Auto }); content.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); Grid.SetColumn(content,1); _layout.Children.Add(content);
        _header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); _header.ColumnDefinitions.Add(new() { Width = new GridLength(250) }); _header.Margin = new Thickness(24,24,24,18);
        _contextTitle.Children.Add(_contextHeading); _contextTitle.Children.Add(_subtitle); _header.Children.Add(_contextTitle);
        _searchHost.Children.Add(_search);
        Grid.SetColumn(_searchHost,1); _header.Children.Add(_searchHost);
        _compactChoice.SetResourceReference(StyleProperty,"ToolBtn"); _compactChoice.HorizontalContentAlignment = HorizontalAlignment.Left; _compactChoice.Height = 44; _compactChoice.MinHeight = 0; _compactChoice.Padding = new Thickness(12,8,12,8);
        var compactContent = new DockPanel(); var chevron = new TextBlock { Text = "⌄", FontSize = 18, Margin = new Thickness(12,0,0,0), VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(chevron,Dock.Right); compactContent.Children.Add(chevron); _compactLabel.FontSize = 15; _compactLabel.FontWeight = FontWeights.SemiBold; compactContent.Children.Add(_compactLabel); _compactChoice.Content = compactContent; _header.Children.Add(_compactChoice);
        _layout.Children.Add(_compactMenu); _compactMenu.PlacementTarget = _compactChoice; _compactMenu.Child = new OperatorGlassSurface { Material = GlassMaterial.Regular, CornerRadius = new CornerRadius(22), Padding = new Thickness(8), Width = 270, Child = new ScrollViewer { Content = _compactItems, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        _compactChoice.Click += (_,_) => { _compactMenu.IsOpen = !_compactMenu.IsOpen; if (_compactMenu.IsOpen) SharedSurfaceMotion.Reveal((FrameworkElement)_compactMenu.Child); };
        _compactMenu.Opened += (_,_) => Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(() => { if (_compactMenu.IsOpen && _compactButtons.TryGetValue(_category,out var selected)) selected.Focus(); }));
        _compactItems.PreviewKeyDown += (_,e) =>
        {
            if (e.Key == Key.Escape) { _compactMenu.IsOpen = false; _compactChoice.Focus(); e.Handled = true; return; }
            if (e.Key is not (Key.Up or Key.Down or Key.Home or Key.End)) return;
            var choices = _compactButtons.Values.ToArray(); if (choices.Length == 0) return;
            int current = Array.IndexOf(choices,Keyboard.FocusedElement as Button);
            int next = e.Key == Key.Home ? 0 : e.Key == Key.End ? choices.Length - 1 : (current + (e.Key == Key.Down ? 1 : -1) + choices.Length) % choices.Length;
            choices[next].Focus(); e.Handled = true;
        };
        System.Windows.Automation.AutomationProperties.SetName(_compactChoice,"Settings categories");
        content.Children.Add(_header); var body = new StackPanel(); body.Children.Add(_sections); body.Children.Add(_empty); Scroller.Content = body; Grid.SetRow(Scroller,1); content.Children.Add(Scroller); Content = _layout;
        _search.TextChanged += (_,_) => Filter();
        PreviewKeyDown += (_,e) => { if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { _search.Focus(); _search.SelectAll(); e.Handled = true; } };
        SharedSurfaceMotion.Attach(Scroller);
        Loaded += (_,_) => { EnsureMaterialPreferences(); BuildNavigation(); Reflow(); if(railHeading.Children[0] is Border { Child: Roche_Scoreboard.Services.OperatorSymbol symbol })symbol.Activate(); };
        Unloaded += (_,_) => { _compactMenu.IsOpen = false; ++_navigationGeneration; };
        SizeChanged += (_,_) => Reflow();
    }

    private void EnsureMaterialPreferences()
    {
        if(_materialSectionAdded)return;_materialSectionAdded=true;
        var section=new Border{Tag="APPEARANCE|glass intensity soft balanced vivid backdrop black colour neutral",Child=new OperatorMaterialPreferences()};
        section.SetResourceReference(StyleProperty,"SettingsSectionSurface");
        int index=0;for(int i=0;i<Sections.Count;i++)if(Sections[i] is FrameworkElement item && Category(item)=="APPEARANCE")index=i+1;
        Sections.Insert(index,section);
    }

    private static Border Glyph(string category, double size = 26)
    {
        string color = category switch { "APPEARANCE" => "#3582EF", "CONTROLS" => "#29AC76", "SCOREBOARD" => "#ED8A2C", "AUTOMATION" => "#9D65E9", "SYSTEM" => "#D7A52A", "MATCH" => "#28A5AE", _ => "#697788" };
        var ink = category is "CONTROLS" or "SCOREBOARD" or "SYSTEM" or "MATCH" ? new SolidColorBrush(Color.FromRgb(0x10,0x14,0x18)) : System.Windows.Media.Brushes.White;
        return new Border { Width=size,Height=size,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,CornerRadius=new CornerRadius(size*.29),Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),Child=new Roche_Scoreboard.Services.OperatorSymbol {Kind=category=="SCOREBOARD"?"display":category=="CONTROLS"?"settings":category,Foreground=ink,Width=size*.66,Height=size*.66,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center} };
    }
    private void BuildNavigation()
    {
        _categories.Children.Clear(); _compactItems.Children.Clear(); _buttons.Clear(); _compactButtons.Clear();
        var categories = new[] { "ALL" }.Concat(Sections.OfType<FrameworkElement>().Select(Category).Distinct().OrderBy(c => c switch { "APPEARANCE" => 0, "MATCH" => 1, "CONTROLS" => 2, "SCOREBOARD" => 3, "AUTOMATION" => 4, _ => 5 })).ToArray();
        if (!categories.Contains(_category)) _category = "ALL";
        foreach (string category in categories)
        {
            Button MakeChoice()
            {
                var row = new Grid { Height=30,VerticalAlignment=VerticalAlignment.Center }; row.ColumnDefinitions.Add(new() { Width = new GridLength(36) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.Children.Add(Glyph(category));
                var label = new TextBlock { Text = CategoryLabel(category), FontSize = 15, VerticalAlignment = VerticalAlignment.Center }; label.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush"); Grid.SetColumn(label,1); row.Children.Add(label);
                var button = new Button { Content = row, Tag = category, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment=VerticalAlignment.Center, Padding = new Thickness(10,8,10,8), Margin = new Thickness(0,0,0,5), Height = 50, MinHeight = 0, BorderThickness = new Thickness(1), Background = System.Windows.Media.Brushes.Transparent, BorderBrush = System.Windows.Media.Brushes.Transparent };
                button.SetResourceReference(StyleProperty,"ToolBtn"); System.Windows.Automation.AutomationProperties.SetName(button,CategoryLabel(category)); button.Click += (_,_) => SelectCategory(category); return button;
            }
            var button = MakeChoice(); _buttons[category] = button; _categories.Children.Add(button); var compact = MakeChoice(); _compactButtons[category] = compact; _compactItems.Children.Add(compact);
        }
        Filter();
    }
    private void SelectCategory(string category)
    {
        _compactMenu.IsOpen = false;
        if (_compact == true) _compactChoice.Focus();
        if (_category == category && _search.Text.Length == 0) return;
        _scrollPositions[_category] = Scroller.VerticalOffset; _category = category;
        CategoryChanged?.Invoke(category);
        if (_search.Text.Length > 0) _search.Clear();
        Filter(); int generation = ++_navigationGeneration;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(() => { if (generation != _navigationGeneration || !IsLoaded) return; Scroller.ScrollToVerticalOffset(_scrollPositions.GetValueOrDefault(category)); SharedSurfaceMotion.Reveal(Scroller); }));
    }
    private static string CategoryLabel(string category) => category switch { "ALL" => "All settings", "MATCH" => "Match", "APPEARANCE" => "Appearance", "CONTROLS" => "Interaction", "SCOREBOARD" => "Output defaults", "AUTOMATION" => "Automation", "SYSTEM" => "Application", _ => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(category.ToLowerInvariant()) };
    private static string Category(FrameworkElement element) => (element.Tag as string ?? "SYSTEM").Split('|')[0];
    private void Filter()
    {
        string query = _search.Text.Trim(); int visible = 0;
        foreach (var section in Sections.OfType<FrameworkElement>()) { bool show = query.Length > 0 ? Matches(section,query) : _category == "ALL" || Category(section) == _category; section.Visibility = show ? Visibility.Visible : Visibility.Collapsed; if (show) visible++; }
        _empty.Visibility = visible == 0 ? Visibility.Visible : Visibility.Collapsed;
        _contextHeading.Text = query.Length > 0 ? "Search results" : CategoryLabel(_category); _compactLabel.Text = query.Length > 0 ? "Search results" : CategoryLabel(_category);
        foreach (var pair in _buttons.Concat(_compactButtons))
        {
            bool selected = query.Length == 0 && pair.Key == _category; var button = pair.Value;
            if (selected) button.SetResourceReference(BackgroundProperty,"ControlCentre.GlassBrush"); else button.Background = System.Windows.Media.Brushes.Transparent; button.BorderBrush = selected ? new SolidColorBrush(Color.FromArgb(115,190,210,230)) : System.Windows.Media.Brushes.Transparent;
            if(button.Content is Grid row && row.Children.OfType<Border>().FirstOrDefault()?.Child is Roche_Scoreboard.Services.OperatorSymbol symbol)symbol.IsActive=selected;
            System.Windows.Automation.AutomationProperties.SetItemStatus(button,selected ? "Selected category" : "Not selected");
        }
    }
    private static bool Matches(DependencyObject root,string query)
    {
        if (root is FrameworkElement { Tag: string tag } && tag.Contains(query,StringComparison.OrdinalIgnoreCase)) return true;
        if (root is TextBlock text && text.Text.Contains(query,StringComparison.OrdinalIgnoreCase)) return true;
        if (root is ContentControl { Content: string caption } && caption.Contains(query,StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) if (Matches(child,query)) return true;
        return false;
    }
    private void Reflow()
    {
        bool compact = ActualWidth < 820;
        bool stackedHeader = ActualWidth < 560;
        int rows=stackedHeader?2:1;
        while(_header.RowDefinitions.Count<rows)_header.RowDefinitions.Add(new(){Height=GridLength.Auto});
        while(_header.RowDefinitions.Count>rows)_header.RowDefinitions.RemoveAt(_header.RowDefinitions.Count-1);
        Grid.SetColumnSpan(_compactChoice,stackedHeader?2:1);
        Grid.SetRow(_searchHost,stackedHeader?1:0);Grid.SetColumn(_searchHost,stackedHeader?0:1);Grid.SetColumnSpan(_searchHost,stackedHeader?2:1);
        _layout.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 264); _navigation.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        _contextTitle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible; _compactChoice.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        _header.ColumnDefinitions[0].Width = new GridLength(1,GridUnitType.Star); _header.ColumnDefinitions[1].Width = compact ? new GridLength(1,GridUnitType.Star) : new GridLength(250);
        _header.Margin = compact ? new Thickness(14,12,14,12) : new Thickness(24,24,24,18); _searchHost.Margin = stackedHeader ? new Thickness(0,10,0,0) : new Thickness(compact ? 8 : 20,0,0,0);
        Scroller.Margin = compact ? new Thickness(14,0,14,14) : new Thickness(16,0,24,24);
        if (_compact != compact) { _compact = compact; _compactMenu.IsOpen = false; }
    }
}

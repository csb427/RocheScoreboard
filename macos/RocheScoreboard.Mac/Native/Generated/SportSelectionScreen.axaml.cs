using RocheScoreboard.Mac.Production;
// Generated from Views/SportSelectionScreen.xaml.cs; edit translator/adapters.
// Source SHA-256: 0e1373748244eb938fbfc82b677ac01690a0fcf41896868c3adc9e58e66959d7
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Input;
using RocheScoreboard.Mac.Native;
using System;
using HorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using System.Collections.Generic;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Color = Avalonia.Media.Color;
using ColorConverter = RocheScoreboard.Mac.Native.ColorConverter;
using MouseEventArgs = Avalonia.Input.PointerEventArgs;
using KeyEventArgs = Avalonia.Input.KeyEventArgs;
using Orientation = Avalonia.Layout.Orientation;
using UserControl = Avalonia.Controls.UserControl;

namespace Roche_Scoreboard.Views;

public partial class SportSelectionScreen : UserControl
{
    private bool _selectionPending;
    private bool? _sportCardsStacked;
    private bool? _sportCardsAcross;
    private void SportViewport_SizeChanged(object? sender, SizeChangedEventArgs e) => ReflowHome(e.NewSize);

    private void ReflowHome(Avalonia.Size size)
    {
        if(SportCardsGrid is null || HomeLayout is null)return;
        InstallIllustratedLanding();
        double width=size.Width,height=size.Height;bool compact=width<620,shortWindow=height<440;
        if(_sportCardsStacked!=true)
        {
            _sportCardsStacked=true;_sportCardsAcross=false;SportCardsGrid.ColumnDefinitions.Clear();SportCardsGrid.RowDefinitions.Clear();SportCardsGrid.ColumnDefinitions.Add(new());
            var cards=new[]{AflCard,CricketCard,TrainingCard};
            for(int i=0;i<cards.Length;i++){SportCardsGrid.RowDefinitions.Add(new(){Height=new GridLength(i==2?.8:1,GridUnitType.Star)});Grid.SetRow(cards[i],i);Grid.SetColumn(cards[i],0);Grid.SetColumnSpan(cards[i],1);}
        }
        for(int row=0;row<SportCardsGrid.RowDefinitions.Count;row++)SportCardsGrid.RowDefinitions[row].Height=new GridLength(shortWindow?1:row==2?.8:1,GridUnitType.Star);
        HomeLayout.Margin=new Thickness(compact?12:30,shortWindow?10:22,compact?12:30,shortWindow?10:22);
        SettingsSubtitle.IsVisible=width>=900 && !shortWindow?true:false;
        SettingsActionCaption.IsVisible=width<390?false:true;UpdatesActionIcon.IsVisible=width<390?false:true;
        SettingsTile.Padding=new Thickness(width<390?10:14,8,width<390?2:14,8);
        VersionLabel.IsVisible=width>=700 && !shortWindow?true:false;ResumeSavedMatchButton.Content=width<600?"Continue":"Continue match";
        HeaderPanel.IsVisible=height<340?false:true;HeaderPanel.Margin=new Thickness(0,shortWindow?12:22,0,shortWindow?12:20);
        HomeHeading.FontSize=compact?27:34;HomeSubtitle.IsVisible=shortWindow?false:true;
        // The star row owns the actual remaining space. Estimating header
        // height clipped Training's lower corners as text/density changed.
        SportCardsGrid.MaxWidth=1120;SportCardsGrid.Height=double.NaN;SportCardsGrid.VerticalAlignment=VerticalAlignment.Stretch;
        foreach(var card in new[]{AflCard,CricketCard,TrainingCard})card.Margin=new Thickness(0,height<340?2:4,0,height<340?2:4);
        foreach(var text in new[]{AflTextPanel,CricketTextPanel,TrainingTextPanel})
        {
            text.Margin=new Thickness(compact?17:28,height<340?3:7,14,height<340?3:7);text.MaxWidth=Math.Max(100,width*.65);
            if(text.Children[0] is TextBlock title)title.FontSize=height<340?20:compact?25:34;
        }
        foreach(var description in new[]{AflDescription,CricketDescription,TrainingDescription})description.IsVisible=height>=520?true:false;
        foreach(var cta in _landingCtas)if(cta.Parent is Border pill)pill.IsVisible=height>=650?true:false;
        ChangelogPanel.Width=double.NaN;ChangelogPanel.Height=double.NaN;ChangelogPanel.MaxWidth=1120;ChangelogPanel.MaxHeight=Math.Max(180,height-24);ChangelogPanel.Margin=new Thickness(compact?10:22);
        ReleaseSheetSubtitle.IsVisible=height<380?false:true;ReleaseResultCount.IsVisible=width<520?false:true;
        ReflowReleaseBrowser(width,height);_homeLens?.Reflow();
    }

    private void ReflowReleaseBrowser(double width, double height)
    {
        bool compact = width < 780, shortWindow = height < 440;
        bool changed = compact != _releaseCompact || shortWindow != _releaseShort;
        _releaseCompact = compact; _releaseShort = shortWindow;
        ReleaseLayout.ColumnDefinitions[0].Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(176);
        ReleaseLayout.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetRowSpan(ReleaseVersionRail, compact ? 1 : 2);
        Grid.SetColumn(ReleaseScroller, compact ? 0 : 1); Grid.SetRow(ReleaseScroller, compact ? 1 : 0); Grid.SetRowSpan(ReleaseScroller, compact ? 1 : 2);
        ReleaseVersionRail.IsVisible = compact && shortWindow ? false : true;
        ReleaseScroller.Margin = compact ? new Thickness(0, shortWindow ? 0 : 12, 0, 0) : new Thickness(20, 0, 0, 0);
        ReleaseHistoryHeading.IsVisible = compact ? false : true;
        ReleaseVersionButtons.Orientation = compact ? Orientation.Horizontal : Orientation.Vertical;
        ReleaseHistoryScroller.MaxHeight = compact ? 52 : double.PositiveInfinity;
        ReleaseVersionChoice.IsVisible = compact && shortWindow ? true : false;
        ReleaseVersionChoice.Width = width < 390 ? 88 : 114;
        ReleaseSheetHeading.FontSize = compact && shortWindow ? width < 390 ? 20 : 23 : 30;
        ReleaseSheetBody.Margin = new Thickness(shortWindow ? 12 : compact ? 16 : 26);
        ReleaseSearchHost.Margin = new Thickness(0, shortWindow ? 10 : 18, 0, shortWindow ? 6 : 10);
        ReleaseFilterHost.Margin = new Thickness(0, 0, 0, shortWindow ? 8 : 16);
        if (changed && _changelogOpen && IsLoaded) PopulateChangelog();
    }
    private DispatcherTimer? _selectionTimer;
    private void ResponsiveScrollHost_PreviewMouseWheel(object? sender, PointerWheelEventArgs e)
        => NestedScrollRouting.RoutePreviewMouseWheel(ResponsiveScrollHost, e);

    public event Action<SportMode>? SportSelected;
    public event Action? ResumeSavedMatchRequested;
    private bool _deferredResumeAvailable;
    public void SetDeferredResumeAvailable(bool available)
    {
        _deferredResumeAvailable = available;
        ResumeSavedMatchButton.IsVisible = available ? true : false;
    }

    private void ResumeSavedMatch_Click(object? sender, RoutedEventArgs e)
        => ResumeSavedMatchRequested?.Invoke();

    // ── Easing curves ──
    private static readonly CubicEase Smooth = new() { EasingMode = EasingMode.EaseOut };
    private static readonly SineEase Sine = new() { EasingMode = EasingMode.EaseInOut };

    // ── Timing ──
    private static readonly TimeSpan HoverIn = OperatorMotion.HoverIn;
    private static readonly TimeSpan HoverOut = OperatorMotion.HoverOut;
    private static readonly TimeSpan FlashDur = OperatorMotion.Press;

    public SportSelectionScreen()
    {
        NativePointer.EnsureInitialized(); ProductionNames.BeginLoad(this); try { InitializeComponent(); } finally { ProductionNames.CompleteLoad(this); }
        InitializeLaunchInteractions();
        Loaded += OnLoaded;
    }

    // ═══════════════════════════════════════════════════════
    //  ENTRY ANIMATION — staggered cinematic reveal
    // ═══════════════════════════════════════════════════════

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        PrepareForDisplay();
        PopulateChangelog();
        if(!_entryPlayed) {_entryPlayed=true;PlayEntryAnimation();} else RestoreLandingVisuals();
        ReflowHome(Bounds.Size);
    }

    /// <summary>
    /// Restores pointer input whenever the host navigates back to this already-loaded
    /// control. bool changes do not raise Loaded again, so relying on Loaded left
    /// the whole sport picker non-interactive after a quick select-and-back sequence.
    /// </summary>
    public void PrepareForDisplay()
    {
        ++_selectionGeneration;
        _selectionTimer?.Stop();
        _selectionTimer = null;
        _selectionPending = false;
        IsHitTestVisible = true;
        if(_entryPlayed) RestoreLandingVisuals();
        ReacquireLandingPointer();
        ResumeSavedMatchButton.IsVisible = _deferredResumeAvailable && GameStateService.HasSavedState()
            ? true : false;
    }

    private void PlayEntryAnimation()
    {
        ++_landingMotionGeneration;
        RestoreLandingVisuals();
        if(!OperatorMotion.Enabled) return;
        var cards=new Control[] {AflCard,CricketCard,TrainingCard};
        for(int i=0;i<cards.Length;i++)
        {
            cards[i].Opacity=0;
            Animate(cards[i],OpacityProperty,1,(int)OperatorMotion.Page.TotalMilliseconds,
                i*(int)OperatorMotion.Stagger.TotalMilliseconds,Smooth);
        }
        foreach (var tile in _homeTiles.Values) tile.Enter();
        ReacquireLandingPointer();
    }

    private void StartAmbientAnimations()
    {
        // Light and material react to interaction. No perpetual hover or glow clocks.
    }

    // ═══════════════════════════════════════════════════════
    //  HOVER — scale, glow, dim the other half
    // ═══════════════════════════════════════════════════════

    private void Card_MouseEnter(object? sender, RoutedEventArgs e)
    {
        if (sender is Border card) EngageHomeTile(card, true);
    }

    private void Card_MouseLeave(object? sender, RoutedEventArgs e)
    {
        if (sender is not Border card) return;
        if (_launchActions.TryGetValue(card, out var action) && action.IsKeyboardFocusWithin) return;
        EngageHomeTile(card, false);
    }

    private void RestoreAmbientGlow(Border glow, bool isAfl)
    {
        glow.BeginAnimation(OpacityProperty, null);
        glow.Opacity = .12;
    }

    // ═══════════════════════════════════════════════════════
    //  CLICK — punch + flash → raise event
    // ═══════════════════════════════════════════════════════

    private void AFL_Click(object? sender, PointerEventArgs e)=>ActivateSport(SportMode.AFL);
    private void Cricket_Click(object? sender, PointerEventArgs e)=>ActivateSport(SportMode.Cricket);
    private void Training_Click(object? sender, PointerEventArgs e)=>ActivateSport(SportMode.Training);
    private void Settings_Click(object? sender, PointerEventArgs e)=>ActivateSport(SportMode.Settings);
    private bool TryBeginSelection()
    {
        if (_selectionPending) return false;
        ++_selectionGeneration;
        _selectionPending = true;
        IsHitTestVisible = false;
        return true;
    }

    private void ScheduleSelection(SportMode mode, int delayMilliseconds)
    {
        _selectionTimer?.Stop();
        int generation=_selectionGeneration;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMilliseconds) };
        _selectionTimer=timer;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if(ReferenceEquals(_selectionTimer,timer)) _selectionTimer = null;
            CompleteSelection(mode,generation);
        };
        timer.Start();
    }

    private void CompleteSelection(SportMode mode,int generation)
    {
        if (!_selectionPending || generation!=_selectionGeneration || !IsVisible) return;
        ++_selectionGeneration;_selectionPending=false;IsHitTestVisible=true;
        SportSelected?.Invoke(mode);
    }

    private void TrainingTile_MouseEnter(object? sender, RoutedEventArgs e) => EngageHomeTile(TrainingCard, true);

    private void TrainingTile_MouseLeave(object? sender, RoutedEventArgs e)
    {
        if (_launchActions.TryGetValue(TrainingCard, out var action) && action.IsKeyboardFocusWithin) return;
        EngageHomeTile(TrainingCard, false);
    }

    private void SettingsTile_MouseEnter(object? sender, RoutedEventArgs e)
    {
        Animate(_settingsContentScale, ScaleTransform.ScaleXProperty, .98, (int)OperatorMotion.HoverIn.TotalMilliseconds, easing: Smooth);
        Animate(_settingsContentScale, ScaleTransform.ScaleYProperty, .98, (int)OperatorMotion.HoverIn.TotalMilliseconds, easing: Smooth);
    }

    private void SettingsTile_MouseLeave(object? sender, RoutedEventArgs e)
    {
        Animate(_settingsContentScale, ScaleTransform.ScaleXProperty, 1, (int)OperatorMotion.HoverOut.TotalMilliseconds, easing: Smooth);
        Animate(_settingsContentScale, ScaleTransform.ScaleYProperty, 1, (int)OperatorMotion.HoverOut.TotalMilliseconds, easing: Smooth);
    }

    private void PlayClickFlash(Border flash, ScaleTransform iconScale, Action onComplete)
    {
        int generation=_selectionGeneration;
        if(!OperatorMotion.Enabled)
        {
            flash.BeginAnimation(OpacityProperty,null);flash.Opacity=0;
            iconScale.BeginAnimation(ScaleTransform.ScaleXProperty,null);iconScale.BeginAnimation(ScaleTransform.ScaleYProperty,null);iconScale.ScaleX=iconScale.ScaleY=1;
            onComplete();return;
        }
        // Icon punch
        iconScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1.18, 1.0, TimeSpan.FromMilliseconds(250)) { EasingFunction = Smooth });
        iconScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1.18, 1.0, TimeSpan.FromMilliseconds(250)) { EasingFunction = Smooth });

        // White flash
        var fadeIn = new DoubleAnimation(0.2, FlashDur) { EasingFunction = Smooth };
        fadeIn.Completed += (_, _) =>
        {
            if(generation!=_selectionGeneration || !_selectionPending || !IsVisible)return;
            var fadeOut = new DoubleAnimation(0, OperatorMotion.SurfaceClose);
            fadeOut.Completed += (_, _) => onComplete();
            flash.BeginAnimation(OpacityProperty, fadeOut);
        };
        flash.BeginAnimation(OpacityProperty, fadeIn);
    }

    // ═══════════════════════════════════════════════════════
    //  CHANGELOG — populate and hover expand/collapse
    // ═══════════════════════════════════════════════════════

    private bool _changelogOpen;
    private string _changelogFilter = "All";

    // ── Tag colour mapping for changelog entries ──
    private static readonly Dictionary<string, (string bg, string fg, string border)> TagColors = new()
    {
        ["New"] = ("#0D2818", "#3FB950", "#1A4028"),
        ["Fixed"] = ("#2D1A0D", "#F0C040", "#3D2A1A"),
        ["Changed"] = ("#1A0D2D", "#C084FC", "#2A1A3D"),
    };

    private sealed record ReleasePage(string Version, string Date, IReadOnlyList<(string Kind, string Text)> Changes);
    private string _selectedReleaseVersion = string.Empty;
    private bool _buildingReleaseNavigation, _releaseCompact, _releaseShort;
    private int _releaseNavigationGeneration;

    private static SolidColorBrush ReleaseBrush(string value) => new((Color)ColorConverter.ConvertFromString(value));

    private string? _releaseRenderedSignature;
    private void PopulateChangelog()
    {
        string signature=string.Join("|",_changelogFilter,_selectedReleaseVersion,ChangelogSearchBox?.Text,_releaseCompact,_releaseShort);
        if(signature==_releaseRenderedSignature && ChangelogEntries.Children.Count>0)return;
        VersionLabel.Text = $"v{ProductionVersion.CurrentVersion.ToString(3)}";
        ChangelogEntries.Children.Clear();
        string query = ChangelogSearchBox?.Text?.Trim() ?? string.Empty;
        foreach (var button in new[] { AllReleaseFilter, NewReleaseFilter, FixedReleaseFilter })
        {
            bool selected = Equals(button.Tag, _changelogFilter);
            button.Background = ReleaseBrush(selected ? "#F0F4FB" : "#163D4B60");
            button.Foreground = ReleaseBrush(selected ? "#101923" : "#F2F6FD");
            button.BorderBrush = ReleaseBrush(selected ? "#E6FFFFFF" : "#30D0DDEF");
            Avalonia.Automation.AutomationProperties.SetItemStatus(button, selected ? "Selected filter" : "Not selected");
        }
        var pages = new List<ReleasePage>();
        foreach (var version in ChangelogService.Versions)
        {
            var changes = new List<(string Kind, string Text)>();
            foreach (string change in version.Changes)
            {
                string kind = change.StartsWith("[Fixed]", StringComparison.OrdinalIgnoreCase) ? "Fixed" : "New";
                int tagEnd = change.StartsWith('[') ? change.IndexOf(']') : -1;
                string caption = tagEnd > 0 ? change[(tagEnd + 1)..].TrimStart() : change;
                bool selectedKind = _changelogFilter == "All" || _changelogFilter == kind;
                bool matches = query.Length == 0 || caption.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || version.Version.Contains(query, StringComparison.OrdinalIgnoreCase) || version.Date.Contains(query, StringComparison.OrdinalIgnoreCase);
                if (selectedKind && matches) changes.Add((kind, caption));
            }
            if (changes.Count > 0) pages.Add(new ReleasePage(version.Version, version.Date, changes));
        }
        var page = pages.FirstOrDefault(p => p.Version == _selectedReleaseVersion) ?? pages.FirstOrDefault();
        if (page is not null) _selectedReleaseVersion = page.Version;
        _releaseRenderedSignature=string.Join("|",_changelogFilter,_selectedReleaseVersion,ChangelogSearchBox?.Text,_releaseCompact,_releaseShort);
        PopulateReleaseNavigation(pages, page?.Version);
        ReleaseResultCount.Text = pages.Count == 1 ? "1 release" : $"{pages.Count} releases";
        if (page is null)
        {
            var empty = new StackPanel { Margin = new Thickness(18, 30, 18, 24) };
            empty.Children.Add(new OperatorSymbol { Kind = "Search", Width = 44, Height = 44, Foreground = ReleaseBrush("#A7D9FF"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 18) });
            empty.Children.Add(new NativeTextBlock { Text = "No matching changes", FontSize = 25, FontWeight = FontWeight.SemiBold, Foreground = Avalonia.Media.Brushes.White, TextWrapping = TextWrapping.Wrap });
            empty.Children.Add(new NativeTextBlock { Text = "Try another word, version number or filter.", FontSize = 15, Foreground = ReleaseBrush("#B6C5DA"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 9, 0, 18) });
            var clear = new Avalonia.Controls.Button { Content = "Show all changes", Theme = NativeResources.Require<Avalonia.Styling.ControlTheme>(this, "ToolBtn"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            clear.Click += (_, _) =>
            {
                if (ChangelogSearchBox is not { } search) return;
                _changelogFilter = "All"; search.Clear(); PopulateChangelog(); search.Focus();
            };
            empty.Children.Add(clear); ChangelogEntries.Children.Add(empty); return;
        }
        ChangelogEntries.Children.Add(BuildReleaseSpotlight(page));
        foreach (string kind in new[] { "New", "Fixed" })
        {
            var changes = page.Changes.Where(c => c.Kind == kind).ToArray();
            if (changes.Length > 0) ChangelogEntries.Children.Add(BuildReleaseSection(kind, changes, query.Length > 0));
        }
    }

    private void PopulateReleaseNavigation(IReadOnlyList<ReleasePage> pages, string? selectedVersion)
    {
        bool restoreNavigationFocus = ReleaseVersionButtons.IsKeyboardFocusWithin;
        int generation = ++_releaseNavigationGeneration;
        _buildingReleaseNavigation = true;
        try
        {
            ReleaseVersionButtons.Children.Clear();
            ReleaseVersionChoice.Items.Clear();
            ReleaseVersionChoice.IsEnabled = pages.Count > 0;
            foreach (var page in pages)
            {
                bool selected = page.Version == selectedVersion;
                var label = new StackPanel();
                label.Children.Add(new NativeTextBlock { Text = page.Version, FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = ReleaseBrush(selected ? "#102438" : "#EDF5FF") });
                if (!_releaseCompact)
                    label.Children.Add(new NativeTextBlock { Text = FormatReleaseDate(page.Date, true), FontSize = 11, Foreground = ReleaseBrush(selected ? "#40546B" : "#AABCD2"), Margin = new Thickness(0, 3, 0, 0) });
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                if (!_releaseCompact) content.Children.Add(new OperatorSymbol { Kind = "Updates", IsActive = selected, Width = 20, Height = 20, Foreground = ReleaseBrush(selected ? "#214F79" : "#BBCFE6"), Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
                content.Children.Add(label);
                var button = new Avalonia.Controls.Button
                {
                    Content = content, Tag = page.Version, Theme = NativeResources.Require<Avalonia.Styling.ControlTheme>(this, "ToolBtn"),
                    Width = _releaseCompact ? 92 : 148, MinHeight = _releaseCompact ? 40 : 60,
                    Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(2, 2, 2, 5),
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    Background = ReleaseBrush(selected ? "#EEEBF4FF" : "#08FFFFFF"),
                    BorderBrush = ReleaseBrush(selected ? "#D9EEFF" : "#00FFFFFF")
                };
                Avalonia.Automation.AutomationProperties.SetName(button, $"Release {page.Version}");
                Avalonia.Automation.AutomationProperties.SetItemStatus(button, selected ? "Selected release" : "Not selected");
                button.Click += (_, _) => SelectRelease(page.Version);
                button.KeyDown += (_, e) =>
                {
                    if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)) return;
                    var buttons = ReleaseVersionButtons.Children.OfType<Avalonia.Controls.Button>().ToArray();
                    int index = Array.IndexOf(buttons, button);
                    int next = e.Key == Key.Home ? 0 : e.Key == Key.End ? buttons.Length - 1
                        : Math.Clamp(index + (e.Key is Key.Left or Key.Up ? -1 : 1), 0, buttons.Length - 1);
                    if (buttons.Length > 0) buttons[next].Focus();
                    e.Handled = true;
                };
                ReleaseVersionButtons.Children.Add(button);
                ReleaseVersionChoice.Items.Add(NativeToolTipPolicy.WithTip(new Avalonia.Controls.ComboBoxItem { Content = page.Version, Tag = page.Version },FormatReleaseDate(page.Date, false)));
            }
            ReleaseVersionChoice.SelectedValue = selectedVersion;
        }
        finally { _buildingReleaseNavigation = false; }
        if (restoreNavigationFocus)
            Dispatcher.UIThread.Post(DispatcherPriority.Loaded, new Action(() =>
            {
                if (generation != _releaseNavigationGeneration || !_changelogOpen || !IsVisible) return;
                ReleaseVersionButtons.Children.OfType<Avalonia.Controls.Button>().FirstOrDefault(button => Equals(button.Tag, selectedVersion))?.Focus();
            }));
    }

    private void SelectRelease(string version)
    {
        if (_selectedReleaseVersion == version) return;
        _selectedReleaseVersion = version; PopulateChangelog(); ReleaseScroller.ScrollToHome();
        SharedSurfaceMotion.Reveal(ChangelogEntries);
    }

    private void ReleaseVersionChoice_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_buildingReleaseNavigation && ReleaseVersionChoice.SelectedValue is string version)
        {
            ReleaseVersionChoice.IsDropDownOpen = false;
            SelectRelease(version);
        }
    }

    private static string FormatReleaseDate(string date, bool compact) =>
        DateTime.TryParse(date, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed)
            ? parsed.ToString(compact ? "d MMM yyyy" : "d MMMM yyyy", System.Globalization.CultureInfo.CurrentCulture) : date;

    private Control BuildReleaseSpotlight(ReleasePage page)
    {
        bool current = page.Version == ProductionVersion.CurrentVersion.ToString(3);
        var copy = new StackPanel();
        copy.Children.Add(new NativeTextBlock { Text = current ? "CURRENT RELEASE" : "RELEASE NOTES", Foreground = ReleaseBrush(current ? "#A4D8FF" : "#BBC9DC"), FontSize = 12, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        copy.Children.Add(new NativeTextBlock { Text = page.Version, Foreground = Avalonia.Media.Brushes.White, FontSize = _releaseShort ? 42 : _releaseCompact ? 54 : 64, FontWeight = FontWeight.SemiBold, LineHeight = _releaseShort ? 48 : 70 });
        copy.Children.Add(new NativeTextBlock { Text = FormatReleaseDate(page.Date, false), Foreground = ReleaseBrush("#BDCADB"), FontSize = 14, Margin = new Thickness(0, 8, 0, 0) });
        var hero = new Grid();
        hero.ColumnDefinitions.Add(new ColumnDefinition()); hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hero.Children.Add(copy);
        var emblem = new NativeBorder { Width = _releaseShort ? 48 : 66, Height = _releaseShort ? 48 : 66, CornerRadius = new CornerRadius(_releaseShort ? 17 : 22), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Background = NativeBrush.Linear((Color)ColorConverter.ConvertFromString("#259DFF"), (Color)ColorConverter.ConvertFromString("#1244A5"), 90),
            Child = new OperatorSymbol { Kind = "Updates", IsActive = true, Width = _releaseShort ? 27 : 37, Height = _releaseShort ? 27 : 37, Foreground = Avalonia.Media.Brushes.White } };
        Grid.SetColumn(emblem, 1); hero.Children.Add(emblem);
        return new NativeBorder { Child = hero, Background = NativeBrush.Linear((Color)ColorConverter.ConvertFromString("#D8212E42"), (Color)ColorConverter.ConvertFromString("#B0121925"), 30), BorderBrush = ReleaseBrush("#32DBE8FA"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(28), Padding = new Thickness(_releaseShort ? 18 : 24), Margin = new Thickness(0, 0, 0, _releaseShort ? 24 : 30) };
    }

    private Control BuildReleaseSection(string kind, IReadOnlyList<(string Kind, string Text)> changes, bool searching)
    {
        string color = kind == "New" ? "#9BD8FF" : "#90E8BC";
        var section = new StackPanel { Margin = new Thickness(8, 0, 8, 26) };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) };
        heading.Children.Add(new OperatorSymbol { Kind = kind == "New" ? "Updates" : "Check", IsActive = true, Width = 23, Height = 23, Foreground = ReleaseBrush(color), Margin = new Thickness(0, 0, 11, 0) });
        heading.Children.Add(new NativeTextBlock { Text = kind, FontSize = 23, FontWeight = FontWeight.SemiBold, Foreground = Avalonia.Media.Brushes.White });
        section.Children.Add(heading);
        int shown = searching ? changes.Count : Math.Min(4, changes.Count);
        Control Line((string Kind, string Text) change)
        {
            var row = new Grid { Margin = new Thickness(34, 11, 0, 0) };
            var text = new NativeTextBlock { Text = (change.Text ?? string.Empty), FontSize = 15, Foreground = ReleaseBrush("#ECF2FA"), TextWrapping = TextWrapping.Wrap, LineHeight = 23 };
            text.SetResourceReference(TextBlock.FontSizeProperty, "ControlCentre.BodySize");
            row.Children.Add(text); return row;
        }
        for (int i = 0; i < shown; i++) section.Children.Add(Line(changes[i]));
        if (shown < changes.Count)
        {
            var rest = new StackPanel();
            for (int i = shown; i < changes.Count; i++) rest.Children.Add(Line(changes[i]));
            var disclosure = new Expander { Header = $"Show all {changes.Count} {kind.ToLowerInvariant()} changes", Content = rest, Theme = NativeResources.Require<Avalonia.Styling.ControlTheme>(this, "ReleaseDisclosure") };
            disclosure.Expanded += (_, _) => disclosure.Header = "Show fewer";
            disclosure.Collapsed += (_, _) => disclosure.Header = $"Show all {changes.Count} {kind.ToLowerInvariant()} changes";
            section.Children.Add(disclosure);
        }
        return section;
    }


    private void ChangelogToggle_Click(object? sender, RoutedEventArgs e)
    {
        ShowChangelog();
    }

    private void ChangelogClose_Click(object? sender, RoutedEventArgs e) => HideChangelog();

    private void ChangelogBackdrop_Click(object? sender, PointerEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender)) HideChangelog();
    }

    private void ChangelogOverlay_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideChangelog();
            e.Handled = true;
        }
    }

    private void ChangelogSearch_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (IsLoaded) { PopulateChangelog(); ReleaseScroller.ScrollToHome(); }
    }

    private void ChangelogFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.Button { Tag: string filter })
        {
            _changelogFilter = filter;
            PopulateChangelog();
        }
    }

    private void ShowChangelog()
    {
        if (_changelogOpen || _selectionPending) return;
        ++_changelogMotionGeneration;
        if (ChangelogOverlay.IsVisible != true)
            _changelogPreviousFocus = NativeKeyboard.FocusedElement;
        _changelogOpen = true;
        PopulateChangelog();
        ChangelogOverlay.IsVisible = true;
        ChangelogOverlay.Focus();
        _releaseSheetMotion?.Move(true);
        ChangelogSearchBox.Focus();
    }

    private void HideChangelog()
    {
        if (!_changelogOpen) return;
        _changelogOpen = false;
        int generation=++_changelogMotionGeneration;
        void CompleteClose()
        {
            if(generation!=_changelogMotionGeneration || _changelogOpen)return;
            var restoreFocus = _changelogPreviousFocus;
            NativeFocusManager.SetFocusedElement(ChangelogOverlay, null);
            ChangelogOverlay.IsVisible = false;
            ChangelogPanelScale.BeginAnimation(ScaleTransform.ScaleXProperty,null);ChangelogPanelScale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
            ChangelogPanelTranslate.BeginAnimation(TranslateTransform.YProperty,null);
            ChangelogPanelScale.ScaleX = ChangelogPanelScale.ScaleY = 0.96;
            ChangelogPanelTranslate.Y = 24;
            void RestoreFocus()
            {
                if (generation != _changelogMotionGeneration || _changelogOpen || !IsVisible) return;
                Control target = restoreFocus is Control { IsVisible: true, IsEnabled: true, Focusable: true } previous
                    ? previous : ChangelogToggle;
                NativeFocusManager.SetFocusedElement(NativeFocusManager.GetFocusScope(target), target);
                NativeKeyboard.Focus(target);
                _changelogPreviousFocus = null;
            }
            // Collapsing a focus scope schedules WPF's own focus recovery. Restore after
            // that layout/input work, so it cannot overwrite the operator's original target.
            Dispatcher.UIThread.Post(DispatcherPriority.Input, new Action(RestoreFocus));
        }
        _releaseSheetMotion?.Move(false, CompleteClose);
    }

    // ═══════════════════════════════════════════════════════
    //  ANIMATION HELPERS
    // ═══════════════════════════════════════════════════════

    private static DoubleAnimation Anim(double to, TimeSpan duration, IEasingFunction? easing = null)
    {
        return new DoubleAnimation(to, OperatorMotion.Enabled ? duration : TimeSpan.Zero) { EasingFunction = easing };
    }

    private static void Animate(AvaloniaObject target, AvaloniaProperty prop,
        double to, int durationMs, int delay = 0, IEasingFunction? easing = null)
    {
        if(!OperatorMotion.Enabled)
        {
            target.BeginAnimation(prop,null);
            if(target is AvaloniaObject element)element.SetValue(prop,to);
            return;
        }
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(durationMs))
        {
            // A null BeginTime disables a WPF timeline entirely. Zero-delay
            // interactions must start at TimeSpan.Zero or a visible modal can
            // remain transparent while still intercepting every input event.
            BeginTime = delay > 0 ? TimeSpan.FromMilliseconds(delay) : TimeSpan.Zero,
            EasingFunction = easing
        };
        target.BeginAnimation(prop, anim);
    }

    private static void PulseForever(AvaloniaObject target, AvaloniaProperty prop,
        double from, double to, int periodMs)
    {
        if(!OperatorMotion.Enabled)
        {
            target.BeginAnimation(prop,null);
            if(target is AvaloniaObject element)element.SetValue(prop,prop==OpacityProperty ? .3 : prop==TranslateTransform.YProperty ? 0d : from);
            return;
        }
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(periodMs))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = Sine
        };
        target.BeginAnimation(prop, anim);
    }
}

public partial class SportSelectionScreen
{
    private ScaleTransform TbSc => (ScaleTransform)ProductionNames.RegistryFor(this)["TbSc"];
    private ScaleTransform SettingsTileScale => (ScaleTransform)ProductionNames.RegistryFor(this)["SettingsTileScale"];
    private TranslateTransform SettingsTileTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["SettingsTileTranslate"];
    private DropShadowEffect SettingsTileGlow => (DropShadowEffect)ProductionNames.RegistryFor(this)["SettingsTileGlow"];
    private TranslateTransform HeaderTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["HeaderTranslate"];
    private TranslateTransform HomeLensTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["HomeLensTranslate"];
    private ScaleTransform AflCardScale => (ScaleTransform)ProductionNames.RegistryFor(this)["AflCardScale"];
    private TranslateTransform AflCardTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["AflCardTranslate"];
    private TranslateTransform AflBeamTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["AflBeamTranslate"];
    private ScaleTransform AflIconScale => (ScaleTransform)ProductionNames.RegistryFor(this)["AflIconScale"];
    private TranslateTransform AflIconFloat => (TranslateTransform)ProductionNames.RegistryFor(this)["AflIconFloat"];
    private TranslateTransform AflIconHover => (TranslateTransform)ProductionNames.RegistryFor(this)["AflIconHover"];
    private TranslateTransform AflTextTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["AflTextTranslate"];
    private ScaleTransform CricketCardScale => (ScaleTransform)ProductionNames.RegistryFor(this)["CricketCardScale"];
    private TranslateTransform CricketCardTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["CricketCardTranslate"];
    private TranslateTransform CricketBeamTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["CricketBeamTranslate"];
    private ScaleTransform CricketIconScale => (ScaleTransform)ProductionNames.RegistryFor(this)["CricketIconScale"];
    private TranslateTransform CricketIconFloat => (TranslateTransform)ProductionNames.RegistryFor(this)["CricketIconFloat"];
    private TranslateTransform CricketIconHover => (TranslateTransform)ProductionNames.RegistryFor(this)["CricketIconHover"];
    private TranslateTransform CricketTextTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["CricketTextTranslate"];
    private ScaleTransform TrainingTileScale => (ScaleTransform)ProductionNames.RegistryFor(this)["TrainingTileScale"];
    private TranslateTransform TrainingTileTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["TrainingTileTranslate"];
    private DropShadowEffect TrainingTileGlow => (DropShadowEffect)ProductionNames.RegistryFor(this)["TrainingTileGlow"];
    private ScaleTransform TrainingContentScale => (ScaleTransform)ProductionNames.RegistryFor(this)["TrainingContentScale"];
    private ScaleTransform TrainingIconMotionScale => (ScaleTransform)ProductionNames.RegistryFor(this)["TrainingIconMotionScale"];
    private TranslateTransform TrainingIconMotionShift => (TranslateTransform)ProductionNames.RegistryFor(this)["TrainingIconMotionShift"];
    private ScaleTransform DividerScale => (ScaleTransform)ProductionNames.RegistryFor(this)["DividerScale"];
    private ScaleTransform ChangelogPanelScale => (ScaleTransform)ProductionNames.RegistryFor(this)["ChangelogPanelScale"];
    private TranslateTransform ChangelogPanelTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["ChangelogPanelTranslate"];
    private void Native_AFL_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; AFL_Click(sender!, e); }
    private void Native_Card_MouseEnter(object? sender, PointerEventArgs e) { Card_MouseEnter(sender!, e); }
    private void Native_Card_MouseLeave(object? sender, PointerEventArgs e) { Card_MouseLeave(sender!, e); }
    private void Native_ChangelogBackdrop_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; ChangelogBackdrop_Click(sender!, e); }
    private void Native_ChangelogOverlay_PreviewKeyDown(object? sender, KeyEventArgs e) { ChangelogOverlay_PreviewKeyDown(sender!, e); }
    private void Native_ChangelogSearch_TextChanged(object? sender, TextChangedEventArgs e) { ChangelogSearch_TextChanged(sender!, e); }
    private void Native_Cricket_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; Cricket_Click(sender!, e); }
    private void Native_ReleaseVersionChoice_SelectionChanged(object? sender, SelectionChangedEventArgs e) { ReleaseVersionChoice_SelectionChanged(sender!, e); }
    private void Native_ResponsiveScrollHost_PreviewMouseWheel(object? sender, PointerWheelEventArgs e) { ResponsiveScrollHost_PreviewMouseWheel(sender!, e); }
    private void Native_SettingsTile_MouseEnter(object? sender, PointerEventArgs e) { SettingsTile_MouseEnter(sender!, e); }
    private void Native_SettingsTile_MouseLeave(object? sender, PointerEventArgs e) { SettingsTile_MouseLeave(sender!, e); }
    private void Native_Settings_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; Settings_Click(sender!, e); }
    private void Native_TrainingTile_MouseEnter(object? sender, PointerEventArgs e) { TrainingTile_MouseEnter(sender!, e); }
    private void Native_TrainingTile_MouseLeave(object? sender, PointerEventArgs e) { TrainingTile_MouseLeave(sender!, e); }
    private void Native_Training_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; Training_Click(sender!, e); }
}

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using System.Globalization;

namespace RocheScoreboard.Mac.Production;

/// <summary>
/// Production visual tree being migrated from WPF. This surface is deliberately
/// separate from the superseded demo until its production behavior is ported.
/// </summary>
public sealed partial class ProductionScorebug : UserControl
{
    private Dictionary<string, AvaloniaObject> NamedObjects => ProductionNames.RegistryFor(this);
    private bool _compact;
    private bool _layoutQueued;
    private bool _distance;
    private bool _ticker;

    public ProductionScorebug()
    {
        AvaloniaXamlLoader.Load(this);
        ProductionNames.CompleteLoad(this);
        Loaded += (_, _) => QueueLayout();
        SizeChanged += (_, _) => QueueLayout();
        foreach (string name in new[] { "ExpHomeGoalsCell", "ExpHomeBehindsCell", "ExpHomeTotalCell", "ExpAwayGoalsCell", "ExpAwayBehindsCell", "ExpAwayTotalCell" })
            ProductionObject<Control>(name).SizeChanged += (_, _) => QueueLayout();
    }

    public T ProductionObject<T>(string name) where T : AvaloniaObject =>
        NamedObjects.TryGetValue(name, out var value) ? (T)value : this.FindControl<Control>(name) as T
        ?? throw new InvalidOperationException($"Missing production element: {name}; registered objects: {NamedObjects.Count}");

    public void RefreshFrom(DemoSession session)
    {
        var match = session.Afl;
        SetText("ExpHomeNameText", match.HomeName.ToUpperInvariant());
        SetText("ExpAwayNameText", match.AwayName.ToUpperInvariant());
        SetText("ExpHomeLogoFallback", match.HomeAbbr.ToUpperInvariant());
        SetText("ExpAwayLogoFallback", match.AwayAbbr.ToUpperInvariant());
        foreach (string prefix in new[] { "", "Exp" })
        {
            SetText(prefix + "HomeGoalsText", match.HomeGoals.ToString(CultureInfo.InvariantCulture));
            SetText(prefix + "HomeBehindsText", match.HomeBehinds.ToString(CultureInfo.InvariantCulture));
            SetText(prefix + "HomeTotalText", match.HomeTotal.ToString(CultureInfo.InvariantCulture));
            SetText(prefix + "AwayGoalsText", match.AwayGoals.ToString(CultureInfo.InvariantCulture));
            SetText(prefix + "AwayBehindsText", match.AwayBehinds.ToString(CultureInfo.InvariantCulture));
            SetText(prefix + "AwayTotalText", match.AwayTotal.ToString(CultureInfo.InvariantCulture));
        }
        foreach (string name in new[] { "ClockText", "ExpClockText", "ExpCompactClockText" })
            SetText(name, DemoSession.Clock(match.DisplayClock));
        foreach (string name in new[] { "QuarterNumberText", "ExpQuarterNumberText", "ExpCompactQuarterText" })
            SetText(name, session.QuarterLabel);
        var now = DateTime.Now;
        foreach (string name in new[] { "TopTimeText", "ExpTopTimeText", "ExpTopClockText", "ExpCompactWallTimeText" })
            SetText(name, now.ToString("h:mm tt", CultureInfo.CurrentCulture));
        SetText("ExpPanelWallTimeText", now.ToString("h:mm", CultureInfo.CurrentCulture));
        SetText("ExpPanelWallTimeSuffixText", now.ToString("tt", CultureInfo.CurrentCulture));
        SetText("ExpTopDateText", now.ToString("ddd d MMM", CultureInfo.CurrentCulture));
        ApplyTeamPalette(true, session.HomeColor, session.HomeTextColor);
        ApplyTeamPalette(false, session.AwayColor, session.AwayTextColor);
    }

    public void SetDistanceLayout(bool distance)
    {
        _distance = distance;
        ProductionObject<Control>("ClassicLayout").IsVisible = false;
        ProductionObject<Control>("ExpandedLayout").IsVisible = true;
        QueueLayout();
    }

    public void SetTickerVisibility(bool visible)
    {
        _ticker = visible;
        QueueLayout();
    }

    private void SetText(string name, string value) => ProductionObject<TextBlock>(name).Text = value;

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private void ApplyTeamPalette(bool home, string primary, string secondary)
    {
        string side = home ? "Home" : "Away";
        if (!Color.TryParse(primary, out var color)) color = Color.Parse(home ? "#0A2A6A" : "#7A1A1A");
        if (!Color.TryParse(secondary, out var text)) text = Colors.White;
        var dark = Mix(color, Colors.Black, 0.30);
        ProductionObject<GradientStop>($"Exp{side}LogoCellGradStart").Color = dark;
        ProductionObject<GradientStop>($"Exp{side}LogoCellGradEnd").Color = color;
        ProductionObject<GradientStop>($"Exp{side}PanelGradStart").Color = color;
        ProductionObject<GradientStop>($"Exp{side}PanelGradEnd").Color = dark;
        foreach (string suffix in new[] { "NameText", "GoalsText", "BehindsText", "TotalText" })
            ProductionObject<TextBlock>($"Exp{side}{suffix}").Foreground = new SolidColorBrush(text);
        ProductionObject<Border>($"Exp{side}LogoSquare").Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops { new(Mix(color, Colors.Black, 0.62), 0), new(Mix(color, Colors.Black, 0.38), 1) }
        };
    }

    private void QueueLayout()
    {
        if (_layoutQueued) return;
        _layoutQueued = true;
        Dispatcher.UIThread.Post(() => { _layoutQueued = false; ApplyResponsiveLayout(); }, DispatcherPriority.Render);
    }

    // The production ratios, thresholds and fitting formula are retained from
    // ScorebugControl.ApplyExpandedResponsiveProportions / FitScoreFont.
    private void ApplyResponsiveLayout()
    {
        var expanded = ProductionObject<Grid>("ExpandedLayout");
        double width = expanded.Bounds.Width, height = expanded.Bounds.Height;
        if (width <= 0 || height <= 0) return;
        _compact = width / height < (_compact ? 1.35 : 1.30);
        bool simplify = !_distance && width < 320;
        ProductionObject<ColumnDefinition>("ExpTopWeatherColumn").Width = new GridLength(simplify ? 0.85 : 1, GridUnitType.Star);
        ProductionObject<ColumnDefinition>("ExpTopClockColumn").Width = new GridLength(simplify ? 1.15 : 1, GridUnitType.Star);
        ProductionObject<ColumnDefinition>("ExpTopDateColumn").Width = simplify ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        ProductionObject<Control>("ExpTopDateHost").IsVisible = !simplify;
        var clockColumn = ProductionObject<ColumnDefinition>("ExpClockColumn");
        clockColumn.MinWidth = _compact ? 0 : Math.Clamp(width * 0.12, 38, 80);
        clockColumn.Width = _compact ? new GridLength(0) : new GridLength(_distance && width < 340 ? 230 : 225, GridUnitType.Star);
        var logoColumn = ProductionObject<ColumnDefinition>("ExpLogoColumn");
        logoColumn.MinWidth = Math.Clamp(width * 0.09, 30, 50);
        logoColumn.Width = new GridLength(_distance ? width < 340 ? 160 : 170 : 165, GridUnitType.Star);
        ProductionObject<ColumnDefinition>("ExpScoresColumn").Width = new GridLength(_distance ? width < 340 ? 620 : 625 : 610, GridUnitType.Star);
        ProductionObject<Control>("ExpClockPanel").IsVisible = !_compact;
        ProductionObject<Control>("ExpCompactClockBar").IsVisible = _compact;
        ProductionObject<Control>("ExpMarqueeCanvas").IsVisible = !_compact;
        ProductionObject<Control>("ExpTopInfoBar").IsVisible = !_distance;
        var top = ProductionObject<RowDefinition>("ExpTopInfoRow");
        top.Height = _distance ? new GridLength(0) : new GridLength(58, GridUnitType.Star);
        top.MinHeight = _distance ? 0 : Math.Clamp(height * 0.145, 16, 32);
        ProductionObject<RowDefinition>("ExpPanelWallTimeRow").Height = _distance ? new GridLength(0.48, GridUnitType.Star) : new GridLength(0);
        ProductionObject<Control>("ExpPanelWallTimeView").IsVisible = _distance;
        ProductionObject<ColumnDefinition>("ExpCompactWallTimeColumn").Width = new GridLength(_distance ? 0.8 : 0, GridUnitType.Star);
        var bottom = ProductionObject<Border>("ExpBottomBarBorder");
        bottom.Height = _compact || _ticker ? 46 : 0;
        bottom.Opacity = _compact || _ticker ? 1 : 0;
        ProductionObject<TranslateTransform>("ExpBottomBarSlide").Y = 0;
        ProductionObject<RowDefinition>("ExpBottomBarRow").Height = GridLength.Auto;
        ProductionObject<RowDefinition>("ExpBottomBarRow").MinHeight = 0;
        double progress = Math.Clamp((height - 110) / 120, 0, 1);
        foreach (string side in new[] { "Home", "Away" })
        {
            ProductionObject<RowDefinition>($"Exp{side}NameRow").Height = new GridLength((_distance ? 1.08 : 0.97) + 0.50 * progress, GridUnitType.Star);
            ProductionObject<RowDefinition>($"Exp{side}ScoreRow").Height = new GridLength(2.85 - 0.35 * progress, GridUnitType.Star);
            foreach (string kind in new[] { "Goals", "Behinds" })
                ProductionObject<Control>($"Exp{side}{kind}Cell").Margin = new Thickness(Math.Clamp(width / 280, 1, 4));
            ProductionObject<Control>($"Exp{side}LogoHost").Margin = new Thickness(Math.Clamp(width / 160, 2, 6));
        }
        double gb = new[] { "HomeGoals", "HomeBehinds", "AwayGoals", "AwayBehinds" }.Min(v => FitScoreFont("Exp" + v + "Cell", "Exp" + v + "Text", "88"));
        double total = new[] { "Home", "Away" }.Min(v => FitScoreFont("Exp" + v + "TotalCell", "Exp" + v + "TotalText", "888"));
        if (gb > 0)
            foreach (string name in new[] { "ExpHomeGoalsText", "ExpHomeBehindsText", "ExpAwayGoalsText", "ExpAwayBehindsText" }) ProductionObject<TextBlock>(name).FontSize = gb;
        if (total > 0)
            foreach (string name in new[] { "ExpHomeTotalText", "ExpAwayTotalText" }) ProductionObject<TextBlock>(name).FontSize = total;
    }

    private double FitScoreFont(string cellName, string sampleName, string worstCase)
    {
        var cell = ProductionObject<Control>(cellName); var sample = ProductionObject<TextBlock>(sampleName);
        double width = Math.Max(0, cell.Bounds.Width - 6), height = Math.Max(0, cell.Bounds.Height - 4);
        if (width <= 0 || height <= 0) return 0;
        var text = new FormattedText(worstCase, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(sample.FontFamily, sample.FontStyle, sample.FontWeight, sample.FontStretch), 100, Brushes.White);
        return Math.Floor(Math.Min(width / text.WidthIncludingTrailingWhitespace, height / text.Height) * 100 * 0.985);
    }
}

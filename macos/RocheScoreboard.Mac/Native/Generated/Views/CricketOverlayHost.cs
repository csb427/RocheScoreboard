// Generated from Views/CricketOverlayHost.cs; edit translator/adapters.
// Source SHA-256: 1acbfd98cfb3a4cc50011367651b49e3f3c34f9cb101bcde8a595bd4f5ebe36f
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System.Diagnostics;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Grid = Avalonia.Controls.Grid;
using Image = Avalonia.Controls.Image;
using Orientation = Avalonia.Layout.Orientation;
using Brushes = Avalonia.Media.Brushes;
using Color = Avalonia.Media.Color;
using FontFamily = Avalonia.Media.FontFamily;
using HorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using VerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace Roche_Scoreboard.Views;

/// <summary>One animated shell for every cricket presentation and every overlay family.</summary>
public sealed partial class CricketOverlayHost : Grid, IDisposable
{
    private readonly Border _shell = new NativeBorder() { Height = 0, IsVisible = false, ClipToBounds = true, Background = Brushes.Black };
    private readonly Viewbox _view = new() { Stretch = Stretch.Uniform };
    private readonly Grid _design = new() { Width = 960, Height = 148, ClipToBounds = true };
    private readonly Image _home = new() { Stretch = Stretch.UniformToFill, Opacity = .18 };
    private readonly Image _away = new() { Stretch = Stretch.UniformToFill, Opacity = .18 };
    private readonly Border _backgroundA = new NativeBorder(), _backgroundB = new();
    private readonly Border _accent = new NativeBorder() { Height = 4, VerticalAlignment = VerticalAlignment.Bottom };
    private string? _renderSignature;
    private readonly Grid _foreground = new() { Margin = new Thickness(18, 6, 18, 7) };
    private readonly TranslateTransform _shellMotion = new();
    private int _motionGeneration;
    private readonly Avalonia.Controls.Primitives.UniformGrid _metrics = new() { Rows = 1 };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly CricketOverlayScheduler _scheduler = new();
    private CricketMatchManager? _match;
    private IReadOnlyList<CricketOverlayFact> _facts = [];
    private CricketOverlayFact? _active;
    private CricketOverlayKind? _pending;
    private bool _manual, _exiting, _disposed;
    private double _hideAt;
    internal string? ThemedTeam { get; private set; }
    private readonly DispatcherTimer _handoff = new() { Interval = BroadcastMotion.OverlayHandoffGap };
    private string? _logoA, _logoB;
    private int _displayInnings = -1;
    private CricketBreakKind _lastBreak;
    private double _resumeQuietUntil;
    private IImage? _teamALogo, _teamBLogo;
    private Action? _momentReady;
    private readonly HashSet<CricketScorebugControl> _guardedScorebugs=new();
    public Grid Scene { get; } = new() { ClipToBounds = true };
    private bool _motionEnabled=true;
    public bool MotionEnabled {get=>_motionEnabled;set {_motionEnabled=value;if(!value)StopFreeHitMotion();}}
    public bool AutomaticEnabled { get => _scheduler.AutomaticEnabled; set { _scheduler.AutomaticEnabled = value; if (!value && !_manual && !_freeHitAlarm && !_pendingFreeHitAlarm) Cancel(); } }
    public event Action? StateChanged;
    public string Status => _exiting ? "CHANGING OVERLAY" : _freeHitAlarm ? "ON AIR · FREE HIT" : (_pending is not null || _pendingFreeHitAlarm) && MomentInProgress ? "WAITING FOR SCORE ANIMATION" : _active is null ? "NO OVERLAY ON AIR" : $"ON AIR · {_active.Title}";
    public IReadOnlyList<CricketOverlayFact> Facts => _facts;
    internal bool UsesBothTeamBackgrounds => _backgroundB.IsVisible == true;
    internal static double DisplaySeconds(CricketOverlayKind kind) => kind switch
    {
        CricketOverlayKind.LastWicket or CricketOverlayKind.Toss or CricketOverlayKind.Projection => 18,
        CricketOverlayKind.Extras or CricketOverlayKind.Comparison or CricketOverlayKind.ScoringWindow => 16,
        _ => 14
    };
    public bool IsShowing => _active != null || _freeHitAlarm || _exiting;
    public CricketOverlayHost(CricketMatchManager match)
    {
        ClipToBounds = true;
        Children.Add(Scene); Children.Add(_shell);
        _shell.VerticalAlignment = VerticalAlignment.Top; _shell.RenderTransform = _shellMotion;
        _shell.Child = _view; _view.Child = _design;
        var background = new Grid(); background.ColumnDefinitions.Add(new()); background.ColumnDefinitions.Add(new());
        var homeSurface=new Grid(); var awaySurface=new Grid(); homeSurface.Children.Add(_home); awaySurface.Children.Add(_away);
        _home.IsVisible=_away.IsVisible=false;
        _backgroundA.Child = homeSurface; _backgroundB.Child = awaySurface; SetColumn(_backgroundB, 1);
        background.Children.Add(_backgroundA); background.Children.Add(_backgroundB); _design.Children.Add(background);
        _design.Children.Add(_foreground); _design.Children.Add(_accent);
        _match = match; match.MatchChanged += Refresh; Refresh();
        _handoff.Tick += (_, _) =>
        {
            _handoff.Stop(); if (_disposed) return; _active = null; StopFreeHitMotion(); _freeHitBackdrop.IsVisible=false; _freeHitAlarm=false; _exiting = false;
            if(_momentReady is Action ready) { _momentReady=null; CompactScorebug(false); ready(); }
            else { ShowPending(); if (_active == null && !_freeHitAlarm) CompactScorebug(false); }
            StateChanged?.Invoke();
        };
        _tick.Tick += Tick; Loaded += (_, _) => { AttachMomentGuards(); if (_freeHitAlarm && _clock.Elapsed.TotalSeconds<_hideAt) RenderFreeHitAlarm(); if (!_disposed) _tick.Start(); };
        Unloaded += (_, _) => {_tick.Stop();StopFreeHitMotion();};
        SizeChanged += (_, _) => { SizeOverlay(); if ((_active != null || _freeHitAlarm) && !_exiting) _shell.Height = OverlayHeight; };
    }
    private double OverlayHeight => Math.Max(1, Bounds.Height * CricketBroadcastStyle.OverlayFraction);
    private void SizeOverlay()
    {
        _view.Height = OverlayHeight; _shell.Margin = new Thickness(0);
        _design.Width = Math.Max(1, 148 * Math.Max(1, Bounds.Width) / OverlayHeight);
        ResizeFreeHitWarningField();
    }
    private static TextBlock Text(double size) => new() { FontFamily = CricketBroadcastStyle.Typeface, FontSize = size, FontWeight = FontWeight.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
    private static readonly SolidColorBrush WarmGold = new(Color.FromRgb(0xFF, 0xDA, 0x7B));
    private Avalonia.Media.IBrush EmphasisInk()
    {
        // A white crest is the brightest possible background under the .18 logo
        // wash. Gold is used only when it still meets normal-text contrast;
        // otherwise size/weight retain the hierarchy with safe white lettering.
        foreach (var surface in new[] { _backgroundA, _backgroundB })
        {
            if (surface == _backgroundB && surface.IsVisible != true) continue;
            if (surface.Background is not LinearGradientBrush gradient) continue;
            foreach (var stop in gradient.GradientStops)
            {
                var c = stop.Color;
                var composite = Color.FromRgb((byte)(c.R * .82 + 255 * .18), (byte)(c.G * .82 + 255 * .18), (byte)(c.B * .82 + 255 * .18));
                if (ContrastHelper.ContrastRatio(WarmGold.Color, composite) < 4.5) return Brushes.White;
            }
        }
        return WarmGold;
    }
    private void Refresh()
    {
        if (_match is null || _disposed) return;
        if(_lastBreak!=_match.BreakKind)
        {
            _lastBreak=_match.BreakKind;
            if(_lastBreak==CricketBreakKind.None) _resumeQuietUntil=_clock.Elapsed.TotalSeconds+18;
            else if(!_manual && _active is not null) Cancel();
        }
        if (_displayInnings >= 0 && _displayInnings != _match.CurrentInningsNumber) Cancel();
        _displayInnings = _match.CurrentInningsNumber;
        _scheduler.Observe(_match, _clock.Elapsed.TotalSeconds); _facts = CricketOverlayAnalysis.Build(_match);
        if (_logoA != _match.TeamALogoPath) { _logoA = _match.TeamALogoPath; _teamALogo = TeamVisuals.LoadLogo(_logoA); }
        if (_logoB != _match.TeamBLogoPath) { _logoB = _match.TeamBLogoPath; _teamBLogo = TeamVisuals.LoadLogo(_logoB); }
        ObserveFreeHit();
        if (_active != null && !_exiting)
        {
            var current = _facts.FirstOrDefault(f => f.Kind == _active.Kind);
            if (current is null || !current.Available || (!_manual && !current.Automatic)) Cancel();
            else { _active = current; Render(current); }
        }
        StateChanged?.Invoke();
    }
    public bool Request(CricketOverlayKind kind)
    {
        if (_disposed || !_facts.Any(f => f.Kind == kind && f.Available)) return false;
        if(MomentInProgress || _momentReady is not null) { _pending=kind; StateChanged?.Invoke(); return true; }
        if (!_exiting && _active?.Kind == kind)
        {
            // A repeated click keeps the current, already readable card on air.
            // Do not close it and run a redundant entry/scorebug push cycle.
            _manual = true; _hideAt = _clock.Elapsed.TotalSeconds + DisplaySeconds(kind);
            _scheduler.Cancel(_clock.Elapsed.TotalSeconds); StateChanged?.Invoke(); return true;
        }
        _pending = kind;
        if (_exiting) CompactScorebug(true);
        if (_active != null || _freeHitAlarm || _exiting) Exit(); else ShowPending();
        return true;
    }
    public void Cancel()
    {
        _pending = null; _pendingFreeHitAlarm=false; _scheduler.Cancel(_clock.Elapsed.TotalSeconds); CompactScorebug(false); Exit();
    }
    private void Exit()
    {
        if (_disposed || _exiting) return;
        if (_active is null && !_freeHitAlarm) return;
        _exiting = true;
        if (_pending is null && !_pendingFreeHitAlarm) CompactScorebug(false);
        AnimateShell(false, () => { if (!_disposed) _handoff.Start(); }); StateChanged?.Invoke();
    }
    private void Tick(object? sender, EventArgs args)
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (_exiting || MomentInProgress || _momentReady is not null) return;
        if (_active != null || _freeHitAlarm)
        {
            if (now >= _hideAt) Exit();
            return;
        }
        if(_pending is not null || _pendingFreeHitAlarm) { ShowPending(); return; }
        if(_match?.BreakKind!=CricketBreakKind.None || now<_resumeQuietUntil) return;
        var next = _scheduler.Select(_facts, now);
        if (next != null) Show(next, false);
    }
    private void ShowPending()
    {
        if(_pendingFreeHitAlarm)
        {
            _pendingFreeHitAlarm=false;
            if(_match?.FreeHitPending==true && _match.FreeHitsEnabled && _match.BreakKind==CricketBreakKind.None) { ShowFreeHitAlarm(); return; }
        }
        var kind = _pending; _pending = null;
        var fact = _facts.FirstOrDefault(f => f.Kind == kind && f.Available);
        if (fact != null) Show(fact, true);
    }
    private void Show(CricketOverlayFact fact, bool manual)
    {
        // Construct and measure the incoming card before the scorebug begins
        // moving, so the first compositor frame contains the complete shell.
        _active = fact; _freeHitAlarm=false; _manual = manual; SizeOverlay(); Render(fact);
        // Give the complete overlay shell its first pose before synchronous
        // scorebug layout work starts the panel clocks. Otherwise the panels
        // can expose the black stage while the shell still has zero height.
        AnimateShell(true); CompactScorebug(true);
        _hideAt = _clock.Elapsed.TotalSeconds + DisplaySeconds(fact.Kind);
        _scheduler.MarkShown(fact, _clock.Elapsed.TotalSeconds, !manual);
        StateChanged?.Invoke();
    }
    private void AnimateShell(bool enter, Action? finished = null)
    {
        int generation = ++_motionGeneration;
        double from = _shellMotion.Y;
        _shellMotion.BeginAnimation(TranslateTransform.YProperty, null);
        _shell.Height = OverlayHeight; _shell.Opacity = 1;
        _shell.IsVisible = true;
        double target = enter ? 0 : -OverlayHeight;
        if (enter) from = -OverlayHeight;
        _shellMotion.Y = target;
        if (!MotionEnabled || !NativeSystemPreferences.ClientAreaAnimation) { if (!enter) _shell.IsVisible = false; finished?.Invoke(); return; }
        var animation = new DoubleAnimation(from, target, CricketBroadcastStyle.OverlayMotionDuration) { FillBehavior = FillBehavior.Stop, EasingFunction = CricketBroadcastStyle.OverlayMotionEase() };
        animation.Completed += (_, _) => { if (_disposed || generation != _motionGeneration) return; if (!enter) _shell.IsVisible = false; finished?.Invoke(); };
        _shellMotion.BeginAnimation(TranslateTransform.YProperty, animation);
    }
    private void CompactScorebug(bool compact)
    {
        AttachMomentGuards();
        foreach (var bug in Scene.Children.OfType<CricketScorebugControl>()) {bug.OverlayMotionEnabled=MotionEnabled;bug.SetOverlayCompact(compact);}
        foreach (var summary in Scene.Children.OfType<CricketSummaryControl>()) {summary.OverlayMotionEnabled=MotionEnabled;summary.SetOverlayCompact(compact);}
    }
    private bool MomentInProgress => Scene.Children.OfType<CricketScorebugControl>().Any(bug=>bug.EventShowing || bug.MomentAwaitingLayout);
    private void AttachMomentGuards()
    {
        foreach(var bug in Scene.Children.OfType<CricketScorebugControl>())
            if(_guardedScorebugs.Add(bug)) bug.PrepareMomentHandoff=PrepareMoment;
    }
    private bool PrepareMoment(Action ready)
    {
        if(_disposed || !IsShowing) return false;
        _momentReady=ready;
        _resumeQuietUntil=_clock.Elapsed.TotalSeconds+24;
        // Cancel owns one exit/restoration sequence. The callback is released
        // only after both the shell and the scorebug have reached rest.
        Cancel();
        return true;
    }
    private void Render(CricketOverlayFact fact)
    {
        string signature = string.Join("|", fact.Kind, fact.Title, fact.Detail, string.Join(";", fact.Metrics.Select(m => m.Label + "=" + m.Value)), _match?.BattingTeamName, _match?.BowlingTeamName, _match?.TeamAPrimaryColor, _match?.TeamASecondaryColor, _match?.TeamBPrimaryColor, _match?.TeamBSecondaryColor, _logoA, _logoB);
        if (signature == _renderSignature) return; _renderSignature = signature;
        StopFreeHitMotion(); _freeHitBackdrop.IsVisible=false;
        if (_match != null)
        {
            bool both = false;
            bool teamA = fact.Kind == CricketOverlayKind.Toss ? _match.TossWinner == _match.TeamAName
                : fact.Kind==CricketOverlayKind.Comparison ? fact.Detail==_match.TeamAName
                : fact.Kind is CricketOverlayKind.SinceWicket or CricketOverlayKind.Extras ? _match.BowlingTeamName == _match.TeamAName : _match.BattingTeamName == _match.TeamAName;
            SetColumnSpan(_backgroundA, both ? 1 : 2); _backgroundB.IsVisible = both ? true : false;
            _backgroundA.Background = CricketBroadcastStyle.Gradient(both || teamA ? _match.TeamAPrimaryColor : _match.TeamBPrimaryColor, both || teamA ? _match.TeamASecondaryColor : _match.TeamBSecondaryColor);
            ThemedTeam = both ? null : teamA ? _match.TeamAName : _match.TeamBName;
            _home.Source = both || teamA ? _teamALogo : _teamBLogo;
            _away.Source = _teamBLogo;
            ((Grid)_home.Parent).Background=CricketBroadcastStyle.CenterLogo(_home.Source,.18);
            ((Grid)_away.Parent).Background=CricketBroadcastStyle.CenterLogo(_away.Source,.18);
            _backgroundB.Background = CricketBroadcastStyle.Gradient(_match.TeamBPrimaryColor, _match.TeamBSecondaryColor);
        }
        _accent.Background = new SolidColorBrush(fact.Group switch { CricketOverlayGroup.Review => Colors.DodgerBlue, CricketOverlayGroup.Pressure => Colors.Orange, CricketOverlayGroup.Context => Colors.Turquoise, _ => Colors.MediumPurple });
        var emphasis = EmphasisInk();
        _foreground.Children.Clear(); _foreground.ColumnDefinitions.Clear();
        _foreground.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _foreground.ColumnDefinitions.Add(new() { Width = new GridLength(2.2, GridUnitType.Star) });
        var heading = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
        void Line(string text, double size) { var line = Text(size); line.Text = text; heading.Children.Add(new Viewbox { StretchDirection = StretchDirection.DownOnly, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Child = line }); }
        if(fact.Kind==CricketOverlayKind.Comparison)
        {
            _foreground.ColumnDefinitions[0].Width=new GridLength(1.6,GridUnitType.Star);
            _foreground.ColumnDefinitions[1].Width=new GridLength(1.4,GridUnitType.Star);
            Line(fact.Title,50);
            string name=fact.Detail.ToUpperInvariant();
            if(_match is not null && name.Length>20)name=(fact.Detail==_match.TeamAName?_match.TeamAAbbr:_match.TeamBAbbr).ToUpperInvariant();
            Line(name+" WAS",54);_foreground.Children.Add(heading);
            var value=Text(122);value.Text=fact.Metrics[0].Value;value.Foreground=emphasis;
            var fit=new Viewbox{Tag="comparison-opponent-score",Child=value,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,Margin=new Thickness(8,0,0,0)};
            SetColumn(fit,1);_foreground.Children.Add(fit);return;
        }
        if (fact.Kind is CricketOverlayKind.SinceScore or CricketOverlayKind.SinceWicket or CricketOverlayKind.SinceBoundary)
        {
            var cluster = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var value = Text(120); value.Text = fact.Metrics[0].Value; value.Foreground = emphasis; value.Margin = new Thickness(0, 0, 26, 0); cluster.Children.Add(value);
            string units=fact.Metrics[0].Label=="OVERS" ? "OVERS" : "BALLS";
            var label = Text(52); label.Text = units+" SINCE\n"+(fact.Kind switch { CricketOverlayKind.SinceScore => "LAST SCORE", CricketOverlayKind.SinceWicket => "LAST WICKET", _ => "LAST BOUNDARY" });
            cluster.Children.Add(label);
            var centered = new Viewbox { Tag = "pressure-cluster", Child = cluster, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
            SetColumnSpan(centered, 2); _foreground.Children.Add(centered); return;
        }
        if (fact.Kind == CricketOverlayKind.Toss)
        {
            var sentence = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            string winnerName=(_match?.TossWinner ?? "").ToUpperInvariant();
            var winnerMeasure=new NativeFormattedText(winnerName,System.Globalization.CultureInfo.InvariantCulture,Avalonia.Media.FlowDirection.LeftToRight,
                new Typeface(CricketBroadcastStyle.Typeface,FontStyle.Normal,FontWeight.Bold,FontStretch.Normal),64,Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            if(_match is not null && winnerMeasure.WidthIncludingTrailingWhitespace>_design.Width-60)
                winnerName=(_match.TossWinner==_match.TeamAName ? _match.TeamAAbbr : _match.TeamBAbbr).ToUpperInvariant();
            var winner = Text(64); winner.Text = winnerName; winner.TextAlignment = TextAlignment.Center;
            var decision = Text(48); decision.TextAlignment = TextAlignment.Center;
            decision.Inlines!.Add(new Avalonia.Controls.Documents.Run("WON THE TOSS · "));
            decision.Inlines!.Add(new Avalonia.Controls.Documents.Run(_match?.TossWinnerElectedToBat == true ? "BAT FIRST" : "BOWL FIRST")
                { Foreground = emphasis });
            sentence.Children.Add(winner); sentence.Children.Add(decision);
            var fit = new Viewbox { Tag = "toss-sentence", Child = sentence, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
            SetColumnSpan(fit, 2); _foreground.Children.Add(fit); return;
        }
        if (fact.Kind == CricketOverlayKind.LastWicket)
        {
            _foreground.ColumnDefinitions[0].Width = new GridLength(.9, GridUnitType.Star);
            _foreground.ColumnDefinitions[1].Width = new GridLength(3.1, GridUnitType.Star);
            Line("LAST\nWICKET", 48); _foreground.Children.Add(heading);
            var detail = new Grid { Tag = "last-wicket-player", Margin = new Thickness(8, 0, 0, 0) };
            detail.ColumnDefinitions.Add(new() { Width = new GridLength(1.5, GridUnitType.Star) });
            detail.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var name = Text(60); name.Text = fact.Detail.ToUpperInvariant(); name.TextAlignment = TextAlignment.Center;
            var nameFit = new Viewbox { Child = name, Margin = new Thickness(8, 0, 18, 0), Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
            detail.Children.Add(nameFit);
            if (fact.Metrics.Count >= 2)
            {
                var score = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var runs = Text(100); runs.Text = fact.Metrics[0].Value; runs.Foreground = emphasis;
                var faced = Text(62); faced.Text = "(" + fact.Metrics[1].Value + ")"; faced.Margin = new Thickness(10, 0, 0, 0);
                score.Children.Add(runs); score.Children.Add(faced);
                var figureFit = new Viewbox { Child = score, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
                SetColumn(figureFit, 1); detail.Children.Add(figureFit);
            }
            else SetColumnSpan(nameFit, 2);
            SetColumn(detail, 1); _foreground.Children.Add(detail); return;
        }
        Line(fact.Kind == CricketOverlayKind.LastOver ? "LAST OVER" : fact.Kind == CricketOverlayKind.Boundaries?"INNINGS\nBOUNDARIES":fact.Kind == CricketOverlayKind.Projection ? "PROJECTED\nSCORE" : fact.Title, fact.Kind==CricketOverlayKind.Projection ? 56 : 48);
        if (fact.Kind == CricketOverlayKind.LastOver) Line(fact.Title.Contains('·') ? "OVER " + fact.Title.Split('·')[1].Trim() : "NOT COMPLETED", 36);
        else if (fact.Kind == CricketOverlayKind.Comparison) Line(fact.Detail.ToUpperInvariant(), 38);
        _foreground.Children.Add(heading);
        if (fact.Kind == CricketOverlayKind.Projection) { _foreground.ColumnDefinitions[0].Width = new GridLength(1.2, GridUnitType.Star); _foreground.ColumnDefinitions[1].Width = new GridLength(2.8, GridUnitType.Star); }
        var visibleMetrics = fact.Metrics.ToArray();
        _metrics.Children.Clear(); _metrics.Columns = Math.Max(1, visibleMetrics.Length);
        foreach (var metric in visibleMetrics)
        {
            double share=_foreground.ColumnDefinitions[1].Width.Value/(_foreground.ColumnDefinitions[0].Width.Value+_foreground.ColumnDefinitions[1].Width.Value);
            var stack = new Grid { Width=Math.Max(1,(_design.Width-36)*share/Math.Max(1,visibleMetrics.Length)-14),Height=135, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 7, 0) };
            stack.RowDefinitions.Add(new() { Height=new GridLength(92) }); stack.RowDefinitions.Add(new() { Height=new GridLength(43) });
            var value = Text(88); value.Text = metric.Value; value.TextAlignment = TextAlignment.Center; value.Foreground = emphasis;
            var label = Text(fact.Kind is CricketOverlayKind.Projection or CricketOverlayKind.Extras ? 34 : 38); label.Text = metric.Label.Replace("CURRENT ", "CRR "); label.TextAlignment = TextAlignment.Center;
            var valueFit=new Viewbox {Child=value,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,Tag="metric-value-row"};
            var labelFit=new Viewbox {Child=label,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,Tag="metric-label-row"}; SetRow(labelFit,1);
            stack.Children.Add(valueFit); stack.Children.Add(labelFit);
            _metrics.Children.Add(new Viewbox { Tag = "overlay-metric", Child = stack, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly });
        }
        SetColumn(_metrics, 1); _foreground.Children.Add(_metrics);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _tick.Stop(); _handoff.Stop(); _tick.Tick -= Tick;
        StopFreeHitMotion(); _freeHitBackdrop.IsVisible=false; _pendingFreeHitAlarm=false;_freeHitAlarm=false;
        _momentReady=null;
        foreach(var bug in _guardedScorebugs) if(bug.PrepareMomentHandoff?.Target==this) { bug.PrepareMomentHandoff=null; bug.CancelMomentHandoff(); }
        _guardedScorebugs.Clear();
        ++_motionGeneration; _shellMotion.BeginAnimation(TranslateTransform.YProperty, null); _shell.IsVisible = false;
        if (_match != null) _match.MatchChanged -= Refresh;
        CompactScorebug(false); _match = null; _pending = null; _active = null; StateChanged = null;
    }
}

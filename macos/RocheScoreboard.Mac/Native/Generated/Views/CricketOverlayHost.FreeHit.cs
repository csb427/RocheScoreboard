// Generated from Views/CricketOverlayHost.FreeHit.cs; edit translator/adapters.
// Source SHA-256: 1b7f9962612df1fde55aafdefdb8ec95df5de61279e1df19877b88a7fdf69a56
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Brushes = Avalonia.Media.Brushes;
using Grid = Avalonia.Controls.Grid;
using Color = Avalonia.Media.Color;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;
using Canvas = Avalonia.Controls.Canvas;

namespace Roche_Scoreboard.Views;

public sealed partial class CricketOverlayHost
{
    private const double FreeHitAlarmSeconds = 8;
    private bool _freeHitAlarm, _pendingFreeHitAlarm;
    private CricketInnings? _freeHitInnings;
    private int _freeHitDeliveryCount = -1, _freeHitRevision = -1;
    private readonly List<TranslateTransform> _freeHitMotion=new();
    private readonly Canvas _freeHitBackdrop=new() {Tag="free-hit-warning-field",ClipToBounds=true,IsHitTestVisible=false,IsVisible=false};
    internal int FreeHitMotionClockCount=>_freeHitMotion.Count(move=>Motion.IsAnimating(move));
    internal bool FreeHitAlarmShowing => _freeHitAlarm && !_exiting;
    internal double FreeHitAlarmRemaining => Math.Max(0, _hideAt - _clock.Elapsed.TotalSeconds);

    private void ObserveFreeHit()
    {
        if (_match is null) return;
        var innings = _match.CurrentInnings;
        int count = innings?.Deliveries.Count ?? 0;
        bool changedInnings = !ReferenceEquals(innings, _freeHitInnings);
        bool correction = _freeHitRevision != _match.DeliveryRevision;
        bool appended = !changedInnings && !correction && count > _freeHitDeliveryCount;
        _freeHitInnings = innings; _freeHitRevision = _match.DeliveryRevision; _freeHitDeliveryCount = count;
        bool available = OutputActive && AllowsOverlays && _match.FreeHitsEnabled && _match.FreeHitPending && !_match.IsMatchFinalized && _match.BreakKind == CricketBreakKind.None;
        if (!available || changedInnings || correction)
        {
            // Recovery and history edits retain the domain's pending indication,
            // but never replay an old alarm. A real new no-ball arms the alarm.
            _pendingFreeHitAlarm = false;
            if (_freeHitAlarm) Exit();
            return;
        }
        if (appended && innings?.Deliveries.LastOrDefault()?.ScoringType == CricketDeliveryType.NoBall)
            QueueFreeHitAlarm();
    }

    private void QueueFreeHitAlarm()
    {
        if (_disposed || !OutputActive || !AllowsOverlays || !IsLoaded || !IsVisible) return;
        _scheduler.Cancel(_clock.Elapsed.TotalSeconds);
        if (_freeHitAlarm && !_exiting)
        {
            _hideAt = _clock.Elapsed.TotalSeconds + FreeHitAlarmSeconds;
            StateChanged?.Invoke(); return;
        }
        _pendingFreeHitAlarm = true;
        if (MomentInProgress || _momentReady is not null) return;
        _pending = null;
        if (IsShowing) Exit();
        else ShowPending();
    }

    private void ShowFreeHitAlarm()
    {
        if (!AllowsOverlays) return;
        _active = null; _freeHitAlarm = true; _manual = false; _renderSignature = null;
        _hideAt = _clock.Elapsed.TotalSeconds + FreeHitAlarmSeconds;
        _resumeQuietUntil = _clock.Elapsed.TotalSeconds + 24;
        SizeOverlay(); RenderFreeHitAlarm(); AnimateShell(true); CompactScorebug(true);
        StateChanged?.Invoke();
    }

    private void RenderFreeHitAlarm()
    {
        StopFreeHitMotion();
        ThemedTeam = null;
        SetColumnSpan(_backgroundA, 2); _backgroundB.IsVisible = false;
        _backgroundA.Background = CricketBroadcastStyle.Gradient("#7F2D00", "#7F2D00");
        ((Grid)_home.Parent).Background = null; ((Grid)_away.Parent).Background = null;
        _accent.Background = Brushes.Gold;
        BuildFreeHitWarningField();
        _foreground.Children.Clear(); _foreground.ColumnDefinitions.Clear();
        var line = new StackPanel { Tag = "free-hit-alarm", Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var title = Text(126); title.Text = "FREE HIT"; title.Margin = new Thickness(34, 0, 34, 0);
        var left = Text(88); left.Text = "››"; left.Foreground = WarmGold;
        var right = Text(88); right.Text = "‹‹"; right.Foreground = WarmGold;
        line.Children.Add(left); line.Children.Add(title); line.Children.Add(right);
        _foreground.Children.Add(new Viewbox { Child = line, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly });
        if (!MotionEnabled || !NativeSystemPreferences.ClientAreaAnimation) return;
        foreach (var pair in new[] { (Text: left, Start: -18d), (Text: right, Start: 18d) })
        {
            var move = new TranslateTransform(pair.Start, 0); pair.Text.RenderTransform = move;
            _freeHitMotion.Add(move);
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(pair.Start, 0, TimeSpan.FromMilliseconds(520))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        }
    }
    private void BuildFreeHitWarningField()
    {
        if(_freeHitBackdrop.Parent is null) _design.Children.Insert(1,_freeHitBackdrop);
        _freeHitBackdrop.Children.Clear(); _freeHitBackdrop.Width=_design.Width; _freeHitBackdrop.Height=148; _freeHitBackdrop.IsVisible=true;
        // A repeating, directional warning field communicates an active rule.
        // One cached vector tile moves behind the fixed headline; no particles,
        // bitmap/video decoding, layout animation, or per-frame event handler.
        var stripe=new StreamGeometry();
        using(var drawing=stripe.Open())
        {
            drawing.BeginFigure(new Avalonia.Point(0,0),true,true);
            drawing.LineTo(new Avalonia.Point(34,0),true,false);
            drawing.LineTo(new Avalonia.Point(116,148),true,false);
            drawing.LineTo(new Avalonia.Point(82,148),true,false);
        }
        stripe.Freeze();
        var tile=new DrawingBrush(NativeDrawing.Geometry(new SolidColorBrush(Color.FromArgb(32,255,208,90)),null,stripe))
        {TileMode=TileMode.Tile,DestinationRect=new RelativeRect(0,0,160,148,RelativeUnit.Absolute),SourceRect = new RelativeRect(new Rect(0,0,160,148), RelativeUnit.Absolute),Stretch=Stretch.Fill};
        tile.Freeze();
        var warning=new Rectangle {Tag="free-hit-direction-field",Width=_design.Width+160,Height=148,Fill=tile};
        var flow=new TranslateTransform(-160,0); warning.RenderTransform=flow; _freeHitBackdrop.Children.Add(warning); _freeHitMotion.Add(flow);
        var light=NativeBrush.Linear();
        light.GradientStops.Add(new GradientStop(Color.FromArgb(0,255,255,255),0));
        light.GradientStops.Add(new GradientStop(Color.FromArgb(13,255,255,255),.5));
        light.GradientStops.Add(new GradientStop(Color.FromArgb(0,255,255,255),1)); light.Freeze();
        double lightWidth=_design.Width*.38;
        var sweep=new Rectangle {Tag="free-hit-warning-sweep",Width=lightWidth,Height=148,Fill=light};
        var scan=new TranslateTransform(-lightWidth,0); sweep.RenderTransform=scan; _freeHitBackdrop.Children.Add(sweep); _freeHitMotion.Add(scan);
        if(!MotionEnabled || !NativeSystemPreferences.ClientAreaAnimation) {flow.X=0;scan.X=(_design.Width-lightWidth)/2;return;}
        flow.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(-160,0,TimeSpan.FromMilliseconds(1550)) {RepeatBehavior=RepeatBehavior.Forever});
        scan.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(-lightWidth,_design.Width,TimeSpan.FromMilliseconds(2700)) {RepeatBehavior=RepeatBehavior.Forever});
    }
    private void StopFreeHitMotion()
    {
        foreach(var move in _freeHitMotion) {move.BeginAnimation(TranslateTransform.XProperty,null);move.X=0;}
        _freeHitMotion.Clear();
    }
    private void ResizeFreeHitWarningField()
    {
        _freeHitBackdrop.Width=_design.Width;
        foreach(var panel in _freeHitBackdrop.Children.OfType<Rectangle>())
            panel.Width=Equals(panel.Tag,"free-hit-direction-field") ? _design.Width+160 : _design.Width*.38;
    }
}

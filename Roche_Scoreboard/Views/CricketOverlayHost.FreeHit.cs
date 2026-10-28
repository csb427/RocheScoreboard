using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Brushes = System.Windows.Media.Brushes;
using Grid = System.Windows.Controls.Grid;
using Color = System.Windows.Media.Color;
using Rectangle = System.Windows.Shapes.Rectangle;
using Canvas = System.Windows.Controls.Canvas;

namespace Roche_Scoreboard.Views;

public sealed partial class CricketOverlayHost
{
    private const double FreeHitAlarmSeconds = 8;
    private bool _freeHitAlarm, _pendingFreeHitAlarm;
    private CricketInnings? _freeHitInnings;
    private int _freeHitDeliveryCount = -1, _freeHitRevision = -1;
    private readonly List<TranslateTransform> _freeHitMotion=new();
    private readonly Canvas _freeHitBackdrop=new() {Tag="free-hit-warning-field",ClipToBounds=true,IsHitTestVisible=false,Visibility=Visibility.Collapsed};
    internal int FreeHitMotionClockCount=>_freeHitMotion.Count(move=>move.HasAnimatedProperties);
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
        bool available = AllowsOverlays && _match.FreeHitsEnabled && _match.FreeHitPending && !_match.IsMatchFinalized && _match.BreakKind == CricketBreakKind.None;
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
        if (_disposed || !AllowsOverlays || !IsLoaded || !IsVisible) return;
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
        SetColumnSpan(_backgroundA, 2); _backgroundB.Visibility = Visibility.Collapsed;
        _backgroundA.Background = CricketBroadcastStyle.Gradient("#7F2D00", "#7F2D00");
        ((Grid)_home.Parent).Background = null; ((Grid)_away.Parent).Background = null;
        _accent.Background = Brushes.Gold;
        BuildFreeHitWarningField();
        _foreground.Children.Clear(); _foreground.ColumnDefinitions.Clear();
        var line = new StackPanel { Tag = "free-hit-alarm", Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var title = Text(126); title.Text = "FREE HIT"; title.Margin = new Thickness(34, 0, 34, 0);
        var left = Text(88); left.Text = "››"; left.Foreground = WarmGold;
        var right = Text(88); right.Text = "‹‹"; right.Foreground = WarmGold;
        line.Children.Add(left); line.Children.Add(title); line.Children.Add(right);
        _foreground.Children.Add(new Viewbox { Child = line, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly });
        if (!MotionEnabled || !SystemParameters.ClientAreaAnimation) return;
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
        _freeHitBackdrop.Children.Clear(); _freeHitBackdrop.Width=_design.Width; _freeHitBackdrop.Height=148; _freeHitBackdrop.Visibility=Visibility.Visible;
        // A repeating, directional warning field communicates an active rule.
        // One cached vector tile moves behind the fixed headline; no particles,
        // bitmap/video decoding, layout animation, or per-frame event handler.
        var stripe=new StreamGeometry();
        using(var drawing=stripe.Open())
        {
            drawing.BeginFigure(new System.Windows.Point(0,0),true,true);
            drawing.LineTo(new System.Windows.Point(34,0),true,false);
            drawing.LineTo(new System.Windows.Point(116,148),true,false);
            drawing.LineTo(new System.Windows.Point(82,148),true,false);
        }
        stripe.Freeze();
        var tile=new DrawingBrush(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(32,255,208,90)),null,stripe))
        {TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,160,148),ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,160,148),Stretch=Stretch.Fill};
        tile.Freeze();
        var warning=new Rectangle {Tag="free-hit-direction-field",Width=_design.Width+160,Height=148,Fill=tile};
        var flow=new TranslateTransform(-160,0); warning.RenderTransform=flow; _freeHitBackdrop.Children.Add(warning); _freeHitMotion.Add(flow);
        var light=new LinearGradientBrush();
        light.GradientStops.Add(new GradientStop(Color.FromArgb(0,255,255,255),0));
        light.GradientStops.Add(new GradientStop(Color.FromArgb(13,255,255,255),.5));
        light.GradientStops.Add(new GradientStop(Color.FromArgb(0,255,255,255),1)); light.Freeze();
        double lightWidth=_design.Width*.38;
        var sweep=new Rectangle {Tag="free-hit-warning-sweep",Width=lightWidth,Height=148,Fill=light};
        var scan=new TranslateTransform(-lightWidth,0); sweep.RenderTransform=scan; _freeHitBackdrop.Children.Add(sweep); _freeHitMotion.Add(scan);
        if(!MotionEnabled || !SystemParameters.ClientAreaAnimation) {flow.X=0;scan.X=(_design.Width-lightWidth)/2;return;}
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

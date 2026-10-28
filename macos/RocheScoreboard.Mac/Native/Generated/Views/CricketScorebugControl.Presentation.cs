// Generated from Views/CricketScorebugControl.Presentation.cs; edit translator/adapters.
// Source SHA-256: 7a6e1959809fac103b1ebcd2e1d257aa5af29bee9221daccfd3ed1f3bd6cc9ba
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Grid = Avalonia.Controls.Grid;
using Image = Avalonia.Controls.Image;
using TextBlock = Avalonia.Controls.TextBlock;

namespace Roche_Scoreboard.Views;

public partial class CricketScorebugControl
{
    private bool? _pregamePending;
    private int _pregameGeneration, _detailGeneration;
    private string? _pregameLogoA, _pregameLogoB;
    private readonly Dictionary<Grid, Image> _actorGhosts = new();
    private readonly Avalonia.Controls.Canvas _strikeLayer=new() { IsHitTestVisible=false };
    private readonly TextBlock _movingStrikeIndicator=new() { Text="▶",FontFamily=CricketBroadcastStyle.Typeface,FontSize=38,FontWeight=FontWeight.Bold,Tag="moving-strike-indicator",IsHitTestVisible=false };
    private readonly TranslateTransform _strikeTravel=new();
    private bool _strikePositioned;
    private double _strikeDestinationY;
    private int _strikeMotionGeneration;
    private readonly Stopwatch _detailsClock = Stopwatch.StartNew();
    private double _nextBowlerDetailAt = 60, _hideBowlerDetailAt;
    private int _detailsDeliveryCount = -1;
    private bool _bowlerDetailsEnabled = true;
    public bool BowlerDetailsEnabled
    {
        get => _bowlerDetailsEnabled;
        set { _bowlerDetailsEnabled = value; if (!value) HideBowlerDetails(); _nextBowlerDetailAt = _detailsClock.Elapsed.TotalSeconds + 60; }
    }
    private void UpdatePregame(CricketMatchManager match)
    {
        PregameHomeName.Text = match.TeamAName.ToUpperInvariant(); PregameAwayName.Text = match.TeamBName.ToUpperInvariant();
        PregameHome.Background = CricketBroadcastStyle.Gradient(match.TeamAPrimaryColor, match.TeamASecondaryColor);
        PregameAway.Background = CricketBroadcastStyle.Gradient(match.TeamBPrimaryColor, match.TeamBSecondaryColor);
        if (_pregameLogoA != match.TeamALogoPath) { _pregameLogoA = match.TeamALogoPath; PregameHomeLogo.Source = TeamVisuals.LoadLogo(_pregameLogoA); CenterLogoBackground(PregameHomeLogoWash,PregameHomeLogo.Source,.12); }
        if (_pregameLogoB != match.TeamBLogoPath) { _pregameLogoB = match.TeamBLogoPath; PregameAwayLogo.Source = TeamVisuals.LoadLogo(_pregameLogoB); CenterLogoBackground(PregameAwayLogoWash,PregameAwayLogo.Source,.12); }
        PregameHomeFallback.Text = match.TeamAAbbr; PregameAwayFallback.Text = match.TeamBAbbr;
        PregameHomeFallback.IsVisible = PregameHomeLogo.Source is null ? true : false;
        PregameAwayFallback.IsVisible = PregameAwayLogo.Source is null ? true : false;
        if (_pregamePending == match.TossPending) return;
        bool reveal = _pregamePending == true && !match.TossPending; _pregamePending = match.TossPending;
        int generation = ++_pregameGeneration;
        MainScoreboard.BeginAnimation(OpacityProperty, null); MainScoreboard.Opacity = 1;
        foreach (var panel in new[] { PregameHome, PregameAway }) { panel.BeginAnimation(OpacityProperty, null); panel.Opacity = 1; panel.RenderTransform = new MatrixTransform(Matrix.Identity); }
        MainScoreboard.IsVisible = match.TossPending ? false : true;
        PregameScreen.IsVisible = match.TossPending ? true : false;
        if (!reveal || !IsVisible || !NativeSystemPreferences.ClientAreaAnimation) return;
        PregameScreen.IsVisible = true;
        var duration = TimeSpan.FromMilliseconds(700);
        for (int i = 0; i < 2; i++)
        {
            var panel = i == 0 ? PregameHome : PregameAway; var shift = new TranslateTransform(); panel.RenderTransform = shift;
            var open = new DoubleAnimation(0, (i == 0 ? -1 : 1) * CricketDesignRoot.Width * .55, duration) { FillBehavior = FillBehavior.HoldEnd, EasingFunction = BroadcastMotion.CreateOverlayEase() };
            if (i == 1) open.Completed += (_, _) => { if (generation == _pregameGeneration && !_isDisposed) PregameScreen.IsVisible = false; };
            shift.BeginAnimation(TranslateTransform.XProperty, open);
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, duration) { FillBehavior = FillBehavior.HoldEnd });
        }
        MainScoreboard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { FillBehavior = FillBehavior.Stop });
    }
    private static void CenterLogoBackground(Image endpoint,IImage? source,double opacity)
    {
        endpoint.Source=source; endpoint.IsVisible=false;
        if(endpoint.Parent is Grid surface) surface.Background=CricketBroadcastStyle.CenterLogo(source,opacity);
    }
    private void CancelPregameMotion()
    {
        ++_pregameGeneration;
        foreach (var panel in new[] { PregameHome, PregameAway }) { panel.BeginAnimation(OpacityProperty, null); panel.Opacity = 1; panel.RenderTransform = new MatrixTransform(Matrix.Identity); }
        MainScoreboard.BeginAnimation(OpacityProperty, null); MainScoreboard.Opacity = 1;
        PregameScreen.IsVisible = _pregamePending == true ? true : false;
        MainScoreboard.IsVisible = _pregamePending == true ? false : true;
    }
    private sealed class ActorTransition
    {
        internal required Grid Wrapper;
        internal required RectangleGeometry IncomingMask;
        internal RectangleGeometry? OutgoingMask;
        internal Geometry? OriginalClip;
        internal Image? Ghost;
        internal Border? Seam;
    }
    private readonly Dictionary<Grid,ActorTransition> _actorTransitions=new();

    private IImage? CaptureActor(Grid content)
    {
        if(!OverlayMotionEnabled) return null;
        // During an interrupted replacement, the visible row is a composition
        // of both actors. Capturing only the incoming Grid dropped the outgoing
        // half and produced a flash on rapid selections.
        if(_actorTransitions.TryGetValue(content,out var transition))
            return CaptureLocalVisual(transition.Wrapper,new Rect(content.Margin.Left,content.Margin.Top,content.NativeWidth(),content.NativeHeight()));
        return CaptureLocalVisual(content);
    }
    private IImage? CaptureLocalVisual(Control content,Rect? viewport=null)
    {
        if (!IsVisible || !NativeSystemPreferences.ClientAreaAnimation || content.NativeWidth() < 1 || content.NativeHeight() < 1) return null;
        Rect source=viewport ?? new Rect(0,0,content.NativeWidth(),content.NativeHeight());
        if(source.Width<1 || source.Height<1) return null;
        var drawing=new NativeDrawingVisual();
        using(var context=drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(content) { Stretch=Stretch.Fill,AlignmentX=AlignmentX.Left,AlignmentY=AlignmentY.Top,SourceRect=new RelativeRect(source,RelativeUnit.Absolute) },null,new Rect(0,0,source.Width,source.Height));
        var snapshot = new NativeRenderTargetBitmap((int)Math.Ceiling(source.Width), (int)Math.Ceiling(source.Height), 96, 96, PixelFormats.Bgra32);
        snapshot.Render(drawing); snapshot.Freeze(); return snapshot;
    }
    private void AnimateActor(Grid content, IImage? previous, bool incoming)
    {
        if(previous is null && !incoming) return;
        ClearActorTransition(content);
        if(!IsVisible || !OverlayMotionEnabled || !NativeSystemPreferences.ClientAreaAnimation || content.NativeWidth()<1 || content.NativeHeight()<1) return;
        Grid wrapper;
        if(content.Parent is Border owner)
        {owner.Child=null;wrapper=new Grid {ClipToBounds=true};wrapper.Children.Add(content);owner.Child=wrapper;}
        else if(content.Parent is Grid existing) wrapper=existing;
        else return;
        double width=content.NativeWidth(),height=content.NativeHeight();
        bool bowling=ReferenceEquals(content,BowlerContents);
        var full=new Rect(0,0,width,height);
        var incomingStart=new Rect(bowling ? width : 0,0,0,height);
        var outgoingEnd=new Rect(bowling ? 0 : width,0,0,height);
        var state=new ActorTransition {Wrapper=wrapper,IncomingMask=new RectangleGeometry(incomingStart),OriginalClip=content.Clip};
        _actorTransitions[content]=state;
        content.BeginAnimation(OpacityProperty,null);content.Opacity=1;content.RenderTransform=new MatrixTransform(Matrix.Identity);content.Clip=state.IncomingMask;
        if(previous is not null)
        {
            var ghost=new Image {Tag="actor-change-ghost",Source=previous,Stretch=Stretch.Fill,Margin=content.Margin,IsHitTestVisible=false};
            
            state.Ghost=ghost;state.OutgoingMask=new RectangleGeometry(full);ghost.Clip=state.OutgoingMask;
            wrapper.Children.Add(ghost);_actorGhosts[content]=ghost;
        }
        var seam=new NativeBorder {Tag="actor-change-seam",Width=3,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Left,Margin=new Thickness(content.Margin.Left,0,0,0),
            Background=(wrapper.Parent as Border)?.BorderBrush ?? new SolidColorBrush(Avalonia.Media.Brushes.White.Color),Opacity=.85,IsHitTestVisible=false};
        state.Seam=seam;wrapper.Children.Add(seam);
        var sweep=new TranslateTransform(bowling ? width : 0,0);seam.RenderTransform=sweep;
        // The baselines, figures and fonts remain fixed. Only complementary
        // vector masks and the team-coloured handoff edge travel across the row.
        var duration=CricketBroadcastStyle.OverlayMotionDuration;
        sweep.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(bowling ? width : 0,bowling ? 0 : width,duration)
        {FillBehavior=FillBehavior.HoldEnd,EasingFunction=CricketBroadcastStyle.OverlayMotionEase()});
        state.OutgoingMask?.BeginAnimation(RectangleGeometry.RectProperty,new RectAnimation(full,outgoingEnd,duration)
        {FillBehavior=FillBehavior.HoldEnd,EasingFunction=CricketBroadcastStyle.OverlayMotionEase()});
        var reveal=new RectAnimation(incomingStart,full,duration) {FillBehavior=FillBehavior.HoldEnd,EasingFunction=CricketBroadcastStyle.OverlayMotionEase()};
        reveal.Completed+=(_,_)=> {if(_actorTransitions.GetValueOrDefault(content)==state)ClearActorTransition(content);};
        state.IncomingMask.BeginAnimation(RectangleGeometry.RectProperty,reveal);
    }
    private void ClearActorTransition(Grid content)
    {
        if(!_actorTransitions.Remove(content,out var state)) return;
        state.IncomingMask.BeginAnimation(RectangleGeometry.RectProperty,null);
        state.OutgoingMask?.BeginAnimation(RectangleGeometry.RectProperty,null);
        content.Clip=state.OriginalClip;content.BeginAnimation(OpacityProperty,null);content.Opacity=1;content.RenderTransform=new MatrixTransform(Matrix.Identity);
        if(state.Ghost is not null) state.Wrapper.Children.Remove(state.Ghost);
        if(state.Seam is not null)
        {
            if(state.Seam.RenderTransform is TranslateTransform sweep)sweep.BeginAnimation(TranslateTransform.XProperty,null);
            state.Wrapper.Children.Remove(state.Seam);
        }
        _actorGhosts.Remove(content);
    }
    private void CancelPlayerMotion()
    {
        foreach(var content in _actorTransitions.Keys.ToArray())ClearActorTransition(content);
        foreach (var image in _actorGhosts.Values) (image.Parent as Grid)?.Children.Remove(image);
        _actorGhosts.Clear();
        foreach (var content in new[] { StrikerContents, NonStrikerContents, BowlerContents }) { content.BeginAnimation(OpacityProperty, null); content.Opacity = 1; content.RenderTransform = new MatrixTransform(Matrix.Identity); }
        _strikeTravel.BeginAnimation(TranslateTransform.YProperty,null); _movingStrikeIndicator.BeginAnimation(OpacityProperty,null); _movingStrikeIndicator.IsVisible=false; _strikePositioned=false;
        ++_strikeMotionGeneration;
    }
    private void UpdateStrikeArrow(bool animate=true,bool compactHandoff=false)
    {
        if(!IsVisible || MainScoreboard.NativeHeight()<1) { _movingStrikeIndicator.IsVisible=false; _strikePositioned=false; return; }
        if(_strikeLayer.Parent is null)
        {
            _strikeLayer.Children.Add(_movingStrikeIndicator); Avalonia.Controls.Grid.SetColumnSpan(_strikeLayer,2); MainScoreboard.Children.Add(_strikeLayer);
            _movingStrikeIndicator.RenderTransform=_strikeTravel;
        }
        StrikerIndicator.Opacity=NonStrikerIndicator.Opacity=0;
        TextBlock? target=(StrikerIndicator.Text ?? string.Empty)=="▶" ? StrikerIndicator : (NonStrikerIndicator.Text ?? string.Empty)=="▶" ? NonStrikerIndicator : null;
        if(target is null) { _movingStrikeIndicator.IsVisible=false; _strikePositioned=false; return; }
        var point=target.NativeTranslatePoint(new Avalonia.Point(),MainScoreboard);
        var band=target==StrikerIndicator ? StrikerBand : NonStrikerBand;
        if(band.RenderTransform is TranslateTransform motion) point=new Avalonia.Point(point.X,point.Y-motion.Y);
        bool allowMotion=animate && OverlayMotionEnabled && NativeSystemPreferences.ClientAreaAnimation;
        bool sameDestination=_strikePositioned && Math.Abs(_strikeDestinationY-point.Y)<.2;
        Avalonia.Controls.Canvas.SetLeft(_movingStrikeIndicator,point.X);
        _movingStrikeIndicator.Foreground=target.Foreground;_movingStrikeIndicator.IsVisible=true;
        if(allowMotion && sameDestination)return;
        double from=_strikePositioned ? _strikeTravel.Y : point.Y;
        int generation=++_strikeMotionGeneration;_strikeDestinationY=point.Y;
        bool moving=allowMotion && _strikePositioned && IsVisible && Math.Abs(from-point.Y)>.2;
        _strikeTravel.BeginAnimation(TranslateTransform.YProperty,null); _strikeTravel.Y=moving ? from : point.Y;
        Avalonia.Controls.Canvas.SetLeft(_movingStrikeIndicator,point.X);
        _movingStrikeIndicator.Foreground=target.Foreground; _movingStrikeIndicator.IsVisible=true;
        if(moving)
        {
            var travel=new DoubleAnimation(from,point.Y,compactHandoff ? CricketBroadcastStyle.OverlayMotionDuration : TimeSpan.FromMilliseconds(680))
            { FillBehavior=FillBehavior.HoldEnd,EasingFunction=compactHandoff ? CricketBroadcastStyle.OverlayMotionEase() : BroadcastMotion.CreateOverlayEase() };
            travel.Completed+=(_,_)=> {if(generation==_strikeMotionGeneration){_strikeTravel.BeginAnimation(TranslateTransform.YProperty,null);_strikeTravel.Y=point.Y;}};
            _strikeTravel.BeginAnimation(TranslateTransform.YProperty,travel);
        }
        _strikePositioned=true;
    }
    private void TickBowlerDetails()
    {
        if (!IsVisible || !_bowlerDetailsEnabled || !NativeSystemPreferences.ClientAreaAnimation || EventShowing || _overlayCompact || _lastMatch?.BreakKind!=CricketBreakKind.None) return;
        double now = _detailsClock.Elapsed.TotalSeconds;
        if (BowlerDetailLane.IsVisible == true) { if (now >= _hideBowlerDetailAt) HideBowlerDetails(); }
        else if (now >= _nextBowlerDetailAt) ShowBowlerDetails();
    }
    private bool ShowBowlerDetails()
    {
        var inn = _lastMatch?.CurrentInnings; var bowler = inn?.CurrentBowler;
        if (!_bowlerDetailsEnabled || inn is null || bowler is null || bowler.BallsBowled == 0 || inn.NeedsBowlerSelection || inn.IsComplete || _overlayCompact || EventShowing || _lastMatch?.BreakKind!=CricketBreakKind.None) return false;
        string detail = $"ECONOMY {bowler.Economy:0.00}";
        var extras = new List<string>();
        if (bowler.NoBalls > 0) extras.Add($"{bowler.NoBalls} NO-BALL{(bowler.NoBalls == 1 ? "" : "S")}");
        if (bowler.Wides > 0) extras.Add($"{bowler.Wides} WIDE{(bowler.Wides == 1 ? "" : "S")}");
        if (extras.Count > 0) detail += "\n" + string.Join(" · ", extras);
        ++_detailGeneration;
        BowlerDetailText.Text = detail; BowlerDetailLane.IsVisible = true;
        BowlerDetailLane.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)) { FillBehavior = FillBehavior.HoldEnd });
        OverBallsView.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400)) { FillBehavior = FillBehavior.HoldEnd });
        _hideBowlerDetailAt = _detailsClock.Elapsed.TotalSeconds + 16; _nextBowlerDetailAt = _detailsClock.Elapsed.TotalSeconds + 90; return true;
    }
    private void HideBowlerDetails()
    {
        int generation = ++_detailGeneration;
        if (BowlerDetailLane.IsVisible != true || !IsVisible || _isDisposed || !NativeSystemPreferences.ClientAreaAnimation)
        {
            BowlerDetailLane.BeginAnimation(OpacityProperty, null); BowlerDetailLane.Opacity = 0; BowlerDetailLane.IsVisible = false;
            OverBallsView.BeginAnimation(OpacityProperty, null); OverBallsView.Opacity = 1; return;
        }
        var fade = new DoubleAnimation(BowlerDetailLane.Opacity, 0, TimeSpan.FromMilliseconds(320)) { FillBehavior = FillBehavior.HoldEnd };
        fade.Completed += (_, _) => { if (generation == _detailGeneration) BowlerDetailLane.IsVisible = false; };
        BowlerDetailLane.BeginAnimation(OpacityProperty, fade);
        OverBallsView.BeginAnimation(OpacityProperty, new DoubleAnimation(OverBallsView.Opacity, 1, TimeSpan.FromMilliseconds(320)) { FillBehavior = FillBehavior.HoldEnd });
    }
}

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;

namespace Roche_Scoreboard.Views;

public partial class CricketScorebugControl
{
    private CricketMatchManager? _broadcastMatch;
    private CricketInnings? _eventInnings;
    private int _eventRevision, _eventCount, _eventGeneration;
    internal const int MomentHoldMilliseconds=2800;
    private const double MaximumMomentBurstSeconds=4.8;
    private readonly DispatcherTimer _eventHold = new() { Interval = TimeSpan.FromMilliseconds(MomentHoldMilliseconds) };
    private bool _eventsEnabled = true;
    private CricketDeliveryType _broadcastMoment;
    private bool _eventExiting;
    private readonly System.Diagnostics.Stopwatch _momentClock = System.Diagnostics.Stopwatch.StartNew();
    private double _burstStarted;
    internal Func<Action,bool>? PrepareMomentHandoff { get; set; }
    private bool _momentAwaitingLayout;
    private CricketDelivery? _queuedMoment;
    private CricketDelivery? _lastMomentDelivery;
    private CricketMilestone? _pendingMilestone,_activeMilestone;
    private int _momentHandoffGeneration;
    internal bool MomentAwaitingLayout => _momentAwaitingLayout;
    internal void CancelMomentHandoff() { if(_momentAwaitingLayout) CancelBroadcast(true); }
    public bool EventAnimationsEnabled
    {
        get => _eventsEnabled;
        set { _eventsEnabled = value; if (!value) CancelBroadcast(true); }
    }
    private bool _rollingFiguresEnabled = true;
    public bool RollingFiguresEnabled
    {
        get => _rollingFiguresEnabled;
        set { _rollingFiguresEnabled = value; if (!value) { ScoreRollVisual.Stop(); foreach (var reel in _numberReels.Values) reel.Stop(); } }
    }
    internal bool EventShowing => EventPresentation.Visibility == Visibility.Visible;

    private bool DeferForBroadcast(CricketMatchManager match)
    {
        if (!ReferenceEquals(_broadcastMatch, match))
        {
            ReleaseBroadcast(); _broadcastMatch = match; match.DeliveryAdded += BroadcastDelivery;
        }
        if((match.BreakKind!=CricketBreakKind.None || match.IsMatchFinalized) && (EventShowing || _momentAwaitingLayout)) { CancelBroadcast(false); return false; }
        if (!EventShowing && !_momentAwaitingLayout) return false;
        if (!ReferenceEquals(_eventInnings, match.CurrentInnings) || _eventRevision != match.DeliveryRevision
            || match.CurrentInnings is null || match.CurrentInnings.Deliveries.Count < _eventCount)
        { CancelBroadcast(false); return false; }
        return true;
    }
    private void BroadcastDelivery(CricketDelivery delivery)
    {
        if (_isDisposed || !_eventsEnabled || !IsVisible || !SystemParameters.ClientAreaAnimation || _broadcastMatch is null) return;
        var moment = delivery.Type == CricketDeliveryType.NoBall && !delivery.CompletedRunsOnly && delivery.NoBallRuns == CricketNoBallRuns.Bat && delivery.Runs is 5 or 7
            ? (delivery.Runs == 5 ? CricketDeliveryType.Four : CricketDeliveryType.Six) : delivery.Type;
        var milestone=CricketMilestone.FromDelivery(delivery,_broadcastMatch.CurrentInnings);
        bool boundaryOrWicket=moment is CricketDeliveryType.Four or CricketDeliveryType.Six or CricketDeliveryType.Wicket;
        if(!ReferenceEquals(_lastMomentDelivery,delivery))
        {
            _lastMomentDelivery=delivery;
            if(milestone is not null)_pendingMilestone=milestone;
        }
        if(!boundaryOrWicket && milestone is null)return;
        _eventInnings = _broadcastMatch.CurrentInnings; _eventRevision = _broadcastMatch.DeliveryRevision;
        _eventCount = _eventInnings?.Deliveries.Count ?? 0;
        if (!EventShowing && PrepareMomentHandoff is not null)
        {
            _queuedMoment=delivery;
            if(_momentAwaitingLayout) return;
            _momentAwaitingLayout=true;
            int generation=++_momentHandoffGeneration;
            bool deferred=PrepareMomentHandoff(()=>
            {
                if(_isDisposed || generation!=_momentHandoffGeneration || !_momentAwaitingLayout || _queuedMoment is null || _broadcastMatch is null) return;
                if(!ReferenceEquals(_eventInnings,_broadcastMatch.CurrentInnings) || _eventRevision!=_broadcastMatch.DeliveryRevision || _broadcastMatch.BreakKind!=CricketBreakKind.None || _broadcastMatch.IsMatchFinalized)
                { CancelBroadcast(true); return; }
                var latest=_queuedMoment; _queuedMoment=null; _momentAwaitingLayout=false;
                BroadcastDelivery(latest);
            });
            if(deferred) return;
            _momentAwaitingLayout=false; _queuedMoment=null;
        }
        bool alreadyShowing = EventShowing;
        if(!boundaryOrWicket && !alreadyShowing && _pendingMilestone is {} direct)
        { _pendingMilestone=null;ShowMilestone(direct);return; }
        if(!boundaryOrWicket && alreadyShowing && _pendingMilestone is {} fresh)
        { _pendingMilestone=null;ShowMilestone(fresh);return; }
        if(!boundaryOrWicket)return;
        bool replacingMilestone=_activeMilestone is not null;
        _activeMilestone=null;
        // A burst coalesces into the latest moment, rather than building a stale
        // animation queue that leaves the public score several deliveries behind.
        EventWord.Text = moment switch { CricketDeliveryType.Four => "FOUR", CricketDeliveryType.Six => "SIX", _ => "WICKET" };
        bool bowling = moment == CricketDeliveryType.Wicket;
        EventPresentation.Background = CricketBroadcastStyle.Gradient(bowling ? _broadcastMatch.BowlingTeamPrimaryColor : _broadcastMatch.BattingTeamPrimaryColor,
            bowling ? _broadcastMatch.BowlingTeamSecondaryColor : _broadcastMatch.BattingTeamSecondaryColor);
        CenterLogoBackground(EventLogo,bowling ? BowlingHeaderLogo.Source : BattingHeaderLogo.Source,.16);
        if (alreadyShowing)
        {
            bool replacement=_broadcastMoment!=moment || replacingMilestone;
            double remaining = MaximumMomentBurstSeconds - (_momentClock.Elapsed.TotalSeconds - _burstStarted);
            if(replacement && remaining<1.2) { CancelBroadcast(true); return; }
            if (replacement) AnimateCricketMoment(moment); _broadcastMoment = moment;
            if (_eventExiting || replacement)
            {
                if (remaining <= .5) { CancelBroadcast(true); return; }
                ++_eventGeneration; _eventExiting = false;
                double opacity = EventPresentation.Opacity; EventPresentation.BeginAnimation(OpacityProperty, null); EventPresentation.Opacity = 1;
                EventPresentation.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, 1, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop });
                _eventHold.Stop(); _eventHold.Interval = TimeSpan.FromSeconds(Math.Min(2.15, remaining - .36)); _eventHold.Start();
            }
            return;
        }
        _broadcastMoment = moment; // The existing deadline is never extended by rapid clicks.
        _burstStarted = _momentClock.Elapsed.TotalSeconds; _eventExiting = false; _eventHold.Interval = TimeSpan.FromMilliseconds(MomentHoldMilliseconds);
        ++_eventGeneration; EventPresentation.Visibility = Visibility.Visible; EventPresentation.Opacity = 1;
        EventPresentation.RenderTransform = Transform.Identity;
        EventPresentation.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { FillBehavior = FillBehavior.Stop });
        AnimateCricketMoment(moment);
        _eventHold.Tick -= EndBroadcast; _eventHold.Tick += EndBroadcast; _eventHold.Start();
    }
    private void EndBroadcast(object? sender, EventArgs args)
    {
        _eventHold.Stop(); _eventExiting = true; int generation = _eventGeneration;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(360)) { FillBehavior = FillBehavior.Stop };
        fade.Completed += (_, _) =>
        {
            if(generation!=_eventGeneration || _isDisposed)return;
            if(_pendingMilestone is {} milestone && _activeMilestone is null && _broadcastMatch is not null
                && ReferenceEquals(_eventInnings,_broadcastMatch.CurrentInnings) && _eventRevision==_broadcastMatch.DeliveryRevision)
            { _pendingMilestone=null;ShowMilestone(milestone); }
            else CancelBroadcast(true);
        };
        EventPresentation.BeginAnimation(OpacityProperty, fade);
    }
    private void CancelBroadcast(bool catchUp)
    {
        ++_eventGeneration; _eventExiting = false; _eventHold.Stop();
        ++_momentHandoffGeneration; _momentAwaitingLayout=false; _queuedMoment=null;
        _pendingMilestone=_activeMilestone=null;
        EventAccents.Children.Clear(); EventPresentation.BeginAnimation(OpacityProperty, null); EventPresentation.Opacity = 1;
        EventPresentation.Visibility = Visibility.Collapsed; EventPresentation.RenderTransform = Transform.Identity;
        _eventInnings = null;
        if (catchUp && _broadcastMatch is not null && !_isDisposed) UpdateFromMatch(_broadcastMatch);
    }
    private void ReleaseBroadcast()
    {
        CancelBroadcast(false);
        if (_broadcastMatch != null) _broadcastMatch.DeliveryAdded -= BroadcastDelivery;
        _broadcastMatch = null; _eventHold.Tick -= EndBroadcast;
        _lastMomentDelivery=null;
    }
}

// Generated from Services/CricketOverlayScheduler.cs; edit translator/adapters.
// Source SHA-256: f8c9e5412ae60c71935295c57892106fbe7d6b0ccb3e405d59ebe47794dec586
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

/// <summary>Deterministic policy; presentation owns the smooth handoff. Automatic items are never queued stale.</summary>
public sealed class CricketOverlayScheduler
{
    private readonly Dictionary<CricketOverlayKind, (double Time, string Fingerprint)> _shown = new();
    private readonly Dictionary<CricketOverlayGroup,double> _groups = new();
    private double _quietUntil = 18, _nextAutomatic = 18;
    private readonly Queue<double> _airtime = new();
    private bool _tossPending;
    private string? _innings;
    private int _count = -1, _revision = -1;
    private bool _blocked;
    private CricketMatchManager? _observedMatch;
    private CricketDelivery? _lastWicket;
    private double _lastWicketAt=double.PositiveInfinity;
    private int _wicketRevision=-1;
    private CricketDelivery? _lastBoundary;
    private double _lastBoundaryAt=double.PositiveInfinity;
    public bool AutomaticEnabled { get; set; } = true;
    public void Observe(CricketMatchManager match, double now)
    {
        string innings = match.CurrentInningsNumber.ToString();
        bool changedMatch=!ReferenceEquals(_observedMatch,match);
        int count=match.CurrentInnings?.Deliveries.Count??0;
        var boundary=match.CurrentInnings?.Deliveries.LastOrDefault(CricketOverlayAnalysis.IsBoundary);
        if(changedMatch || _innings!=innings || _revision!=match.DeliveryRevision)
        { _lastBoundary=boundary;_lastBoundaryAt=double.PositiveInfinity; }
        else if(!ReferenceEquals(_lastBoundary,boundary))
        { _lastBoundary=boundary;_lastBoundaryAt=count>_count && boundary is not null?now:double.PositiveInfinity; }
        var wicket=match.CurrentInnings?.Deliveries.LastOrDefault(delivery=>delivery.Type==CricketDeliveryType.Wicket);
        if(changedMatch || _innings!=innings || !ReferenceEquals(_lastWicket,wicket) || _wicketRevision!=match.DeliveryRevision)
        { _lastWicket=wicket; _lastWicketAt=wicket is null ? double.PositiveInfinity : now; _wicketRevision=match.DeliveryRevision; }
        _observedMatch=match;
        bool blocked = match.TossPending || match.IsMatchFinalized || match.BreakKind != CricketBreakKind.None
            || match.CurrentInnings is null || match.CurrentInnings.IsComplete;
        if (_blocked && !blocked) _quietUntil = Math.Max(_quietUntil, now + 20);
        _blocked = blocked;
        if (changedMatch || _innings != innings || _tossPending && !match.TossPending) { _shown.Clear(); _groups.Clear(); _airtime.Clear(); _nextAutomatic = now+18; _quietUntil = now+18; }
        else if (_count != count || _revision != match.DeliveryRevision)
        {
            bool correction = count < _count || match.DeliveryRevision != _revision;
            bool latestIsWicket = match.CurrentInnings?.Deliveries.LastOrDefault()?.Type == CricketDeliveryType.Wicket;
            bool latestBoundary=match.CurrentInnings?.Deliveries.LastOrDefault() is {} latest && CricketOverlayAnalysis.IsBoundary(latest);
            _quietUntil = Math.Max(_quietUntil,now+(correction ? 20 : latestIsWicket ? 15 : latestBoundary?8:12));
        }
        _innings = innings; _count = count; _revision = match.DeliveryRevision; _tossPending=match.TossPending;
    }
    public CricketOverlayFact? Select(IReadOnlyList<CricketOverlayFact> facts, double now)
    {
        while(_airtime.TryPeek(out double oldest) && now-oldest >= 300) _airtime.Dequeue();
        if (!AutomaticEnabled || _blocked || now < _quietUntil || now < _nextAutomatic || _airtime.Count>=2) return null;
        return facts.Where(f => f.Available && f.Automatic && Eligible(f,now))
            .OrderByDescending(f => f.Priority).ThenBy(f => _shown.TryGetValue(f.Kind,out var last) ? last.Time : double.MinValue).FirstOrDefault();
    }
    private bool Eligible(CricketOverlayFact fact, double now)
    {
        // A wicket celebration and player change need quiet first. The review
        // belongs two to five minutes later; wall-clock timestamps and scoring
        // rate never make it appear immediately after recovery or correction.
        if(fact.Kind==CricketOverlayKind.LastWicket && (now-_lastWicketAt<120 || now-_lastWicketAt>300)) return false;
        if(fact.Kind==CricketOverlayKind.Boundaries && (now-_lastBoundaryAt<8 || now-_lastBoundaryAt>40))return false;
        if (_groups.TryGetValue(fact.Group,out double time) && now-time < 100) return false;
        if (!_shown.TryGetValue(fact.Kind,out var previous)) return true;
        if (fact.Kind is CricketOverlayKind.LastWicket or CricketOverlayKind.LastOver or CricketOverlayKind.Boundaries && fact.Fingerprint == previous.Fingerprint) return false;
        double cooldown = fact.Kind switch { CricketOverlayKind.Toss => fact.Priority >= 70 ? 180 : fact.Priority >= 40 ? 300 : 480,
            CricketOverlayKind.ScoringWindow => 360, CricketOverlayKind.LastWicket => 180, CricketOverlayKind.LastOver => 240, _ => 240 };
        return now-previous.Time >= cooldown;
    }
    public void MarkShown(CricketOverlayFact fact, double now, bool automatic = true)
    {
        _shown[fact.Kind] = (now,fact.Fingerprint); _groups[fact.Group] = now;
        // Manual requests still earn a long quiet period and suppress repeated
        // facts, but do not consume the two-card automatic airtime budget.
        if (automatic) _airtime.Enqueue(now);
        _nextAutomatic = now+110;
    }
    public void Cancel(double now) { _nextAutomatic = now+110; }
}

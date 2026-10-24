// Generated from Services/OverlayScheduler.cs; edit translator/adapters.
// Source SHA-256: 418e9f219d0985724d554c4255ec17371379af340519f2266b8b280fc2f54419
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Roche_Scoreboard.Services;

/// <summary>Identifies the phase of a quarter for overlay scheduling purposes.</summary>
public enum QuarterPhase
{
    /// <summary>First ~90-120 seconds — no informational overlays allowed.</summary>
    EarlyBuffer,

    /// <summary>Mid-quarter — overlays may appear per normal rotation.</summary>
    Active,

    /// <summary>Last ~2 minutes — reduced frequency, only high-priority overlays.</summary>
    LateReduced,

    /// <summary>Within final 30 seconds or quarter ended — overlays suppressed.</summary>
    Suppressed
}

/// <summary>
/// Identifies a scheduled informational overlay. The scheduler rotates through
/// these using a staleness-based fairness algorithm so all overlays appear regularly.
/// </summary>
public enum OverlayKind
{
    StatsBar,
    WinProbability,
    Forecast,
    Weather,
    Rain,
    QuarterScores,
    RecentScores
}

public enum OverlayFamily
{
    MatchStory,
    Conditions
}

/// <summary>
/// Metadata for a single informational overlay type, describing how long it
/// should stay visible, how frequently it may appear, its relative priority,
/// and a delegate that decides whether it is relevant right now.
/// </summary>
public sealed class OverlayDefinition
{
    public required OverlayKind Kind { get; init; }

    /// <summary>How long the overlay remains visible.</summary>
    public required TimeSpan DisplayDuration { get; init; }

    /// <summary>Minimum spacing between consecutive appearances of this overlay.</summary>
    public required TimeSpan MinInterval { get; init; }

    /// <summary>Higher values are shown more readily in late-quarter reduced mode.</summary>
    public required int Priority { get; init; }

    /// <summary>
    /// Returns true when the overlay has meaningful content to show. The scheduler
    /// skips overlays whose relevance check returns false (e.g. rain when there is
    /// no precipitation probability).
    /// </summary>
    public required Func<bool> IsRelevant { get; init; }

    /// <summary>Used to prevent visually similar cards clustering together.</summary>
    public OverlayFamily Family { get; init; } = OverlayFamily.MatchStory;

    /// <summary>UTC timestamp of the last time this overlay was shown.</summary>
    internal DateTime LastShownUtc { get; set; } = DateTime.MinValue;

    /// <summary>Number of times this overlay has been shown in the current quarter.</summary>
    internal int ShowCountThisQuarter { get; set; }
}

/// <summary>
/// Drives a calm, adaptive, fairness-based rotation of informational overlays
/// on the scorebug. Only one informational overlay is ever active at a time.
/// <para>
/// The scheduler adapts dynamically to variable quarter lengths (15-30+ minutes)
/// by computing overlay spacing from the quarter duration rather than using fixed
/// intervals. It tracks game activity (event-driven overlays like scoring runs,
/// droughts, lead changes, G/B counts, and the 5-minute warning) to avoid
/// competing with busy moments and to fill quiet periods proactively.
/// </para>
/// <para>
/// Overlay selection uses a staleness score — overlays that have not appeared for
/// the longest time are prioritised, ensuring fair rotation across all five
/// informational overlays. The system respects early-quarter buffer periods,
/// scoring event cooldowns, goal animation windows, event-driven overlay
/// cooldowns, and late-quarter suppression.
/// </para>
/// </summary>
public sealed class OverlayScheduler
{
    private readonly Func<DateTime> _utcNow;

    public OverlayScheduler(Func<DateTime>? utcNow = null)
    {
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    // ── Fixed timing constants ──

    /// <summary>No overlays during the first N seconds of a quarter.</summary>
    private static readonly TimeSpan EarlyBufferDuration = TimeSpan.FromSeconds(75);

    /// <summary>Suppress or heavily reduce overlays in the final N seconds.</summary>
    private static readonly TimeSpan LateQuarterThreshold = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Hard minimum gap between any two informational overlays regardless of
    /// adaptive spacing. Prevents back-to-back informational overlays even in
    /// very long quarters.
    /// </summary>
    private static readonly TimeSpan HardMinSpacing = TimeSpan.FromSeconds(75);

    /// <summary>Cooldown after a scoring event before informational overlays resume.</summary>
    private static readonly TimeSpan ScoringCooldown = TimeSpan.FromSeconds(22);

    /// <summary>Cooldown after a goal animation finishes.</summary>
    private static readonly TimeSpan GoalAnimationCooldown = TimeSpan.FromSeconds(12);

    /// <summary>
    /// Cooldown after an event-driven overlay (scoring run, drought, lead change,
    /// G/B counts, 5-minute warning) before informational overlays resume.
    /// </summary>
    private static readonly TimeSpan EventDrivenCooldown = TimeSpan.FromSeconds(35);

    /// <summary>Solid cooldown after the previous overlay finishes before the next may start.</summary>
    private static readonly TimeSpan PostOverlayBuffer = TimeSpan.FromSeconds(35);

    /// <summary>Late-quarter mode only allows overlays with priority at or above this.</summary>
    private const int LateQuarterMinPriority = 80;

    // ── Adaptive spacing parameters ──

    /// <summary>
    /// Target number of informational overlay slots per quarter. The scheduler
    /// divides the available active window (after early buffer and before late
    /// suppression) into this many evenly-spaced slots.
    /// </summary>
    private const int TargetOverlaysPerQuarter = 7;

    /// <summary>
    /// When the game has been quiet (no overlays of any kind) for this long,
    /// the spacing requirement is reduced to fill the silence.
    /// </summary>
    private static readonly TimeSpan QuietThreshold = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The reduced spacing applied during quiet periods to ensure the display
    /// stays active and informative.
    /// </summary>
    private static readonly TimeSpan QuietModeSpacing = TimeSpan.FromSeconds(95);

    /// <summary>
    /// When multiple event-driven overlays have fired recently, the scheduler
    /// extends the spacing to let the game breathe. An overlay counts as
    /// "recent" if it occurred within this window.
    /// </summary>
    private static readonly TimeSpan BusyActivityWindow = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Extra spacing added on top of the adaptive interval when the game is
    /// detected as busy (many event-driven overlays recently).
    /// </summary>
    private static readonly TimeSpan BusyExtraDelay = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Number of event-driven overlays within <see cref="BusyActivityWindow"/>
    /// that triggers the busy-game detection.
    /// </summary>
    private const int BusyOverlayThreshold = 2;

    // ── State ──

    private readonly List<OverlayDefinition> _definitions = new();

    /// <summary>
    /// Deterministic cursor used for fair ties. Public output should be
    /// predictable during rehearsal; random selection made identical match
    /// states produce different overlay sequences.
    /// </summary>
    private int _selectionCursor;
    private OverlayFamily? _lastShownFamily;

    /// <summary>UTC time the last informational overlay was shown (any kind).</summary>
    private DateTime _lastOverlayShownUtc = DateTime.MinValue;

    /// <summary>UTC time the last informational overlay finished hiding.</summary>
    private DateTime _lastOverlayHiddenUtc = DateTime.MinValue;

    /// <summary>UTC time the most recent scoring event occurred.</summary>
    private DateTime _lastScoringEventUtc = DateTime.MinValue;

    /// <summary>UTC time the most recent goal animation ended (estimated).</summary>
    private DateTime _goalAnimationEndUtc = DateTime.MinValue;
    private DateTime _goalAnimationStartedUtc = DateTime.MinValue;

    /// <summary>Whether an informational overlay is currently being displayed.</summary>
    private bool _overlayActive;

    /// <summary>UTC time the current quarter started (for phase detection in count-up mode).</summary>
    private DateTime _quarterStartUtc = DateTime.MinValue;

    /// <summary>
    /// Timestamps of recent event-driven overlay appearances, used to detect
    /// busy game periods. Entries older than <see cref="BusyActivityWindow"/>
    /// are pruned lazily.
    /// </summary>
    private readonly List<DateTime> _recentEventOverlays = new();

    /// <summary>
    /// UTC time the most recent event-driven overlay appeared.
    /// Used for the <see cref="EventDrivenCooldown"/>.
    /// </summary>
    private DateTime _lastEventDrivenOverlayUtc = DateTime.MinValue;

    /// <summary>
    /// UTC time that *any* overlay (informational or event-driven) last appeared
    /// or finished. Used for quiet-period detection.
    /// </summary>
    private DateTime _lastAnyOverlayActivityUtc = DateTime.MinValue;

    // ── Configuration ──

    /// <summary>
    /// Registers all overlay definitions. Call once during initialisation.
    /// The order of registration is used only as a tiebreaker when multiple
    /// overlays have identical staleness scores.
    /// </summary>
    public void RegisterOverlays(IEnumerable<OverlayDefinition> overlays)
    {
        _definitions.Clear();
        HashSet<OverlayKind> registeredKinds = [];
        foreach (OverlayDefinition overlay in overlays)
        {
            // Duplicate registrations otherwise create two independently aged
            // entries for the same visual and can make one card dominate the
            // rotation after a control is recreated.
            if (registeredKinds.Add(overlay.Kind))
                _definitions.Add(overlay);
        }
        Reset();
    }

    /// <summary>Returns the registered definitions (read-only).</summary>
    public IReadOnlyList<OverlayDefinition> Definitions => _definitions;

    // ── Event notifications ──

    /// <summary>Call when a score event occurs (goal or behind).</summary>
    public void NotifyScoreEvent()
    {
        _lastScoringEventUtc = _utcNow();
    }

    /// <summary>
    /// Call when a goal animation begins. Pass the estimated animation duration
    /// so the scheduler can compute when it is safe to resume overlays.
    /// </summary>
    public void NotifyGoalAnimation(TimeSpan animationDuration)
    {
        DateTime now = _utcNow();
        if (now < _goalAnimationStartedUtc)
            _goalAnimationEndUtc = DateTime.MinValue;
        _goalAnimationStartedUtc = now;
        // Bad media metadata must neither overflow DateTime nor shorten an
        // existing animation reservation. Negative durations mean zero.
        long ticks = Math.Clamp(animationDuration.Ticks, 0, (DateTime.MaxValue - now).Ticks);
        DateTime endEstimate = now.AddTicks(ticks);
        if (endEstimate > _goalAnimationEndUtc)
        {
            _goalAnimationEndUtc = endEstimate;
        }
    }

    /// <summary>
    /// Call when an event-driven overlay fires (scoring run, drought, lead
    /// change, G/B counts, five-minute warning). This tells the scheduler to
    /// pause informational overlays and records the activity for busy-game
    /// detection.
    /// </summary>
    public void NotifyEventDrivenOverlay()
    {
        DateTime now = _utcNow();
        _lastEventDrivenOverlayUtc = now;
        _lastAnyOverlayActivityUtc = now;
        PruneRecentEventOverlays(now);
        _recentEventOverlays.Add(now);
    }

    /// <summary>Call when an informational overlay begins displaying.</summary>
    public void NotifyOverlayShown(OverlayKind kind)
    {
        DateTime now = _utcNow();
        _overlayActive = true;
        _lastOverlayShownUtc = now;
        _lastAnyOverlayActivityUtc = now;

        OverlayDefinition? def = _definitions.FirstOrDefault(d => d.Kind == kind);
        if (def is not null)
        {
            def.LastShownUtc = now;
            def.ShowCountThisQuarter++;
            _lastShownFamily = def.Family;
            int index = _definitions.IndexOf(def);
            _selectionCursor = _definitions.Count == 0 ? 0 : (index + 1) % _definitions.Count;
        }
    }

    /// <summary>Call when the current informational overlay finishes hiding.</summary>
    public void NotifyOverlayHidden()
    {
        _overlayActive = false;
        DateTime now = _utcNow();
        _lastOverlayHiddenUtc = now;
        _lastAnyOverlayActivityUtc = now;
    }

    /// <summary>Call when a new quarter starts or the clock is first started.</summary>
    public void NotifyQuarterStart()
    {
        DateTime now = _utcNow();
        _quarterStartUtc = now;
        _lastOverlayShownUtc = DateTime.MinValue;
        _lastOverlayHiddenUtc = DateTime.MinValue;
        _lastScoringEventUtc = DateTime.MinValue;
        _goalAnimationEndUtc = DateTime.MinValue;
        _goalAnimationStartedUtc = DateTime.MinValue;
        _lastEventDrivenOverlayUtc = DateTime.MinValue;
        _lastAnyOverlayActivityUtc = now;
        _overlayActive = false;
        _recentEventOverlays.Clear();
        _selectionCursor = 0;
        _lastShownFamily = null;

        foreach (OverlayDefinition def in _definitions)
        {
            // Fairness is quarter-local. A card shown at the end of the prior
            // quarter must not begin the next quarter looking less eligible.
            def.LastShownUtc = DateTime.MinValue;
            def.ShowCountThisQuarter = 0;
        }
    }

    /// <summary>
    /// Call when a saved game is resumed mid-quarter. Stamps the wall-clock
    /// "quarter start" and "last overlay hidden" markers to <em>now</em> so
    /// the early-buffer and post-overlay gates kick in, preventing an
    /// informational overlay from firing the instant the clock resumes. Unlike
    /// <see cref="NotifyQuarterStart"/>, this does NOT wipe per-quarter show
    /// counts — the saved match's rotation history is preserved.
    /// </summary>
    public void NotifyGameResumed()
    {
        DateTime now = _utcNow();
        // Transient visuals do not survive restore. Retaining the active flag
        // can otherwise block the rotation forever waiting for a lost hide event.
        _overlayActive = false;
        _goalAnimationEndUtc = DateTime.MinValue;
        _goalAnimationStartedUtc = DateTime.MinValue;
        _recentEventOverlays.Clear();
        _quarterStartUtc = now;
        _lastOverlayHiddenUtc = now;
        _lastAnyOverlayActivityUtc = now;
    }

    /// <summary>Resets all scheduler state (match reset or quarter end).</summary>
    public void Reset()
    {
        _lastOverlayShownUtc = DateTime.MinValue;
        _lastOverlayHiddenUtc = DateTime.MinValue;
        _lastScoringEventUtc = DateTime.MinValue;
        _goalAnimationEndUtc = DateTime.MinValue;
        _goalAnimationStartedUtc = DateTime.MinValue;
        _lastEventDrivenOverlayUtc = DateTime.MinValue;
        _lastAnyOverlayActivityUtc = DateTime.MinValue;
        _overlayActive = false;
        _quarterStartUtc = DateTime.MinValue;
        _recentEventOverlays.Clear();
        _selectionCursor = 0;
        _lastShownFamily = null;

        foreach (OverlayDefinition def in _definitions)
        {
            def.LastShownUtc = DateTime.MinValue;
            def.ShowCountThisQuarter = 0;
        }
    }

    // ── Phase detection ──

    /// <summary>
    /// Determines the current quarter phase given elapsed time and quarter duration.
    /// </summary>
    public static QuarterPhase GetPhase(TimeSpan elapsed, TimeSpan quarterDuration)
    {
        if (elapsed < EarlyBufferDuration)
            return QuarterPhase.EarlyBuffer;

        TimeSpan remaining = quarterDuration - elapsed;
        if (remaining <= TimeSpan.FromSeconds(30))
            return QuarterPhase.Suppressed;

        if (remaining <= LateQuarterThreshold)
            return QuarterPhase.LateReduced;

        return QuarterPhase.Active;
    }

    // ── Adaptive spacing ──

    private static TimeSpan SafeElapsed(DateTime now, DateTime then)
    {
        if (then == DateTime.MinValue) return TimeSpan.MaxValue;
        TimeSpan elapsed = now - then;
        // Windows can correct its wall clock backwards (NTP, sleep/resume or
        // an operator time change). Treat that as an expired cooldown instead
        // of freezing automatic overlays until the old timestamp catches up.
        return elapsed < TimeSpan.Zero ? TimeSpan.MaxValue : elapsed;
    }

    /// <summary>
    /// Computes the ideal spacing between informational overlays based on the
    /// quarter duration and current game activity. Adapts to any quarter length
    /// (15–30+ minutes) rather than relying on hardcoded intervals.
    /// </summary>
    private TimeSpan ComputeAdaptiveSpacing(TimeSpan quarterDuration, DateTime now)
    {
        // Available active window = quarter duration minus early buffer minus late threshold
        double activeSeconds = Math.Max(0,
            quarterDuration.TotalSeconds - EarlyBufferDuration.TotalSeconds - LateQuarterThreshold.TotalSeconds);

        // Divide active window into even slots for the target overlays
        double slotSeconds = activeSeconds / Math.Max(1, TargetOverlaysPerQuarter);

        // Clamp between the hard minimum and a reasonable maximum
        TimeSpan adaptiveSpacing = TimeSpan.FromSeconds(Math.Clamp(slotSeconds, HardMinSpacing.TotalSeconds, 300));

        // If the game has been quiet (no overlays at all for a while), shorten the
        // spacing so the display doesn't sit idle for too long.
        if (_lastAnyOverlayActivityUtc != DateTime.MinValue &&
            SafeElapsed(now, _lastAnyOverlayActivityUtc) > QuietThreshold &&
            adaptiveSpacing > QuietModeSpacing)
        {
            adaptiveSpacing = QuietModeSpacing;
        }

        // If the game is busy (many event-driven overlays recently), extend the
        // spacing so informational overlays stay out of the way.
        PruneRecentEventOverlays(now);
        if (_recentEventOverlays.Count >= BusyOverlayThreshold)
        {
            adaptiveSpacing += BusyExtraDelay;
        }

        return adaptiveSpacing;
    }

    /// <summary>Removes entries from <see cref="_recentEventOverlays"/> that are older than the activity window.</summary>
    private void PruneRecentEventOverlays(DateTime now)
    {
        _recentEventOverlays.RemoveAll(t => SafeElapsed(now, t) > BusyActivityWindow);
    }

    // ── Staleness-based selection ──

    /// <summary>
    /// Computes a staleness score for an overlay definition. Higher scores
    /// indicate the overlay is more "overdue" and should be prioritised.
    /// The score combines time since last shown with the per-overlay show
    /// count to ensure fair rotation.
    /// </summary>
    private static double ComputeStaleness(OverlayDefinition def, DateTime now)
    {
        double timeSinceShown = def.LastShownUtc == DateTime.MinValue
            ? 600.0  // never shown this quarter — treat as very stale
            : SafeElapsed(now, def.LastShownUtc).TotalSeconds;

        // Penalise overlays that have already been shown many times this quarter
        // so less-shown overlays are promoted. Each prior show reduces staleness
        // by a diminishing amount.
        double countPenalty = def.ShowCountThisQuarter * 60.0;

        return timeSinceShown - countPenalty;
    }

    // ── Core scheduling ──

    /// <summary>
    /// Evaluates whether the next informational overlay should be triggered.
    /// Returns the <see cref="OverlayDefinition"/> to display, or <c>null</c>
    /// if no overlay should appear right now.
    /// </summary>
    /// <param name="elapsed">Time elapsed in the current quarter.</param>
    /// <param name="quarterDuration">Total quarter duration.</param>
    /// <param name="clockRunning">Whether the game clock is currently running.</param>
    /// <param name="isBreakScreen">Whether a break screen is active.</param>
    public OverlayDefinition? TryGetNextOverlay(
        TimeSpan elapsed,
        TimeSpan quarterDuration,
        bool clockRunning,
        bool isBreakScreen)
    {
        // Hard stops — never show overlays in these conditions
        if (!clockRunning || isBreakScreen || _overlayActive)
            return null;

        if (_definitions.Count == 0)
            return null;

        DateTime now = _utcNow();

        // Phase check
        QuarterPhase phase = GetPhase(elapsed, quarterDuration);
        if (phase == QuarterPhase.EarlyBuffer || phase == QuarterPhase.Suppressed)
            return null;

        // Adaptive spacing since last informational overlay
        TimeSpan adaptiveSpacing = ComputeAdaptiveSpacing(quarterDuration, now);
        if (_lastOverlayShownUtc != DateTime.MinValue &&
            SafeElapsed(now, _lastOverlayShownUtc) < adaptiveSpacing)
        {
            return null;
        }

        // Post-overlay buffer — wait a few seconds after the previous overlay hid
        if (_lastOverlayHiddenUtc != DateTime.MinValue &&
            SafeElapsed(now, _lastOverlayHiddenUtc) < PostOverlayBuffer)
        {
            return null;
        }

        // Scoring event cooldown
        if (_lastScoringEventUtc != DateTime.MinValue &&
            SafeElapsed(now, _lastScoringEventUtc) < ScoringCooldown)
        {
            return null;
        }

        // Goal animation cooldown
        if (now < _goalAnimationStartedUtc)
        {
            _goalAnimationEndUtc = DateTime.MinValue;
            _goalAnimationStartedUtc = DateTime.MinValue;
        }
        if (_goalAnimationEndUtc != DateTime.MinValue &&
            (now < _goalAnimationEndUtc || now - _goalAnimationEndUtc < GoalAnimationCooldown))
            return null;

        // Event-driven overlay cooldown
        if (_lastEventDrivenOverlayUtc != DateTime.MinValue &&
            SafeElapsed(now, _lastEventDrivenOverlayUtc) < EventDrivenCooldown)
        {
            return null;
        }

        // Quarter-start wall-clock buffer (provides a hard guarantee even if
        // elapsed resets oddly)
        if (_quarterStartUtc != DateTime.MinValue &&
            SafeElapsed(now, _quarterStartUtc) < EarlyBufferDuration)
        {
            return null;
        }

        // Build a list of eligible candidates scored by staleness
        OverlayDefinition? best = null;
        double bestScore = double.MinValue;

        for (int index = 0; index < _definitions.Count; index++)
        {
            OverlayDefinition candidate = _definitions[index];
            // A data provider (weather, forecast, stats) may briefly be
            // unavailable while state changes. Treat that overlay as irrelevant
            // for this pass rather than allowing a scheduler tick to escape.
            bool isRelevant;
            try
            {
                isRelevant = candidate.IsRelevant();
            }
            catch
            {
                isRelevant = false;
            }
            if (!isRelevant)
                continue;

            // Per-overlay minimum interval
            if (candidate.LastShownUtc != DateTime.MinValue &&
                SafeElapsed(now, candidate.LastShownUtc) < candidate.MinInterval)
            {
                continue;
            }

            // Late-quarter: only high-priority overlays
            if (phase == QuarterPhase.LateReduced && candidate.Priority < LateQuarterMinPriority)
                continue;

            int cyclicDistance = (index - _selectionCursor + _definitions.Count) % _definitions.Count;
            double familyPenalty = _lastShownFamily == candidate.Family ? 50 : 0;
            double score = ComputeStaleness(candidate, now)
                         + candidate.Priority * 0.10
                         - familyPenalty
                         - cyclicDistance * 0.01;
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }
}

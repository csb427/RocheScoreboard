// Generated from Views/CricketControlPanel.BreakPresentations.cs; edit translator/adapters.
// Source SHA-256: 35c5c3d2f94464eee87114fb512f466ad616208cc30696c9fa3e22571069b297
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

namespace Roche_Scoreboard.Views;

/// <summary>Read-only, bounded break rotation; one timer owns every automatic switch.</summary>
public partial class CricketControlPanel
{
    private const double BreakPresentationDwellSeconds = 24;
    private readonly DispatcherTimer _breakPresentationTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch _breakPresentationClock = Stopwatch.StartNew();
    private CricketMatchManager? _breakPresentationMatch;
    private CricketBreakKind _observedPresentationBreak;
    private double _nextBreakPresentationAt;
    private bool _automaticBreakSwitch, _breakPresentationInitialized, _breakRotationEnabled = true;

    public bool BreakPresentationRotationEnabled
    {
        get => _breakRotationEnabled;
        set
        {
            if (_breakRotationEnabled == value) return;
            _breakRotationEnabled = value;
            _nextBreakPresentationAt = _breakPresentationClock.Elapsed.TotalSeconds + BreakPresentationDwellSeconds;
            UpdateBreakPresentations();
        }
    }

    private void InitializeBreakPresentations()
    {
        if (_breakPresentationInitialized) return;
        _breakPresentationInitialized = true;
        _breakRotationEnabled = OperatorWorkflowPreferences.Load().AflBreakScreenEnabled;
        _breakPresentationTimer.Tick += (_, _) => TickBreakPresentations();
        PresentationChanged += target =>
        {
            if (!_automaticBreakSwitch && target is "scorebug" or "batting" or "bowling" or "summary" or "worm" or "manhattan")
                _nextBreakPresentationAt = _breakPresentationClock.Elapsed.TotalSeconds + BreakPresentationDwellSeconds;
        };
        Loaded += (_, _) =>
        {
            _nextBreakPresentationAt = _breakPresentationClock.Elapsed.TotalSeconds + BreakPresentationDwellSeconds;
            UpdateBreakPresentations();
        };
        Unloaded += (_, _) => _breakPresentationTimer.Stop();
    }

    private void ResetBreakPresentations()
    {
        _breakPresentationTimer.Stop();
        _breakPresentationMatch = null;
        _observedPresentationBreak = CricketBreakKind.None;
        _nextBreakPresentationAt = 0;
        _automaticBreakSwitch = false;
    }

    private void UpdateBreakPresentations()
    {
        bool changedMatch = !ReferenceEquals(_breakPresentationMatch, _match);
        var previous = _observedPresentationBreak;
        _breakPresentationMatch = _match;
        _observedPresentationBreak = _match?.BreakKind ?? CricketBreakKind.None;
        if (changedMatch || previous != _observedPresentationBreak)
            _nextBreakPresentationAt = _breakPresentationClock.Elapsed.TotalSeconds + BreakPresentationDwellSeconds;
        if (_match is null || _observedPresentationBreak == CricketBreakKind.None || _match.IsMatchFinalized || _match.TossPending)
        {
            _breakPresentationTimer.Stop();
            // A manual scorecard selection during play remains manual. Only a
            // genuine break-to-play handoff returns to the live scoreboard.
            if (!changedMatch && previous != CricketBreakKind.None && _match is not null)
                SwitchBreakPresentation("scorebug");
            return;
        }
        if (_breakRotationEnabled) _breakPresentationTimer.Start();
        else _breakPresentationTimer.Stop();
    }

    private void TickBreakPresentations()
    {
        UpdateBreakPresentations();
        if (!_breakPresentationTimer.IsEnabled || _match?.CurrentInnings is not CricketInnings innings
            || _summaryControl is null || _scorebug is null || _breakPresentationClock.Elapsed.TotalSeconds < _nextBreakPresentationAt) return;
        var modes = new List<string> { "scorebug" };
        if (innings.BattingOrder.Any(player => player.BallsFaced > 0 || player.Runs > 0 || player.IsOut)) modes.Add("batting");
        if (CricketSummaryControl.BowledPlayers(innings).Count > 0) modes.Add("bowling");
        if(innings.Deliveries.Count>0){modes.Add("worm");modes.Add("manhattan");}
        modes.Add("summary");
        int index = modes.IndexOf(_currentPresentation);
        SwitchBreakPresentation(modes[(index + 1 + modes.Count) % modes.Count]);
        _nextBreakPresentationAt = _breakPresentationClock.Elapsed.TotalSeconds + BreakPresentationDwellSeconds;
    }

    private void SwitchBreakPresentation(string target)
    {
        _automaticBreakSwitch = true;
        try { SwitchPresentation(target); }
        finally { _automaticBreakSwitch = false; }
    }

#if DEBUG
    internal void PreviewNextBreakPresentation()
    {
        _nextBreakPresentationAt = 0;
        TickBreakPresentations();
    }
#endif
}

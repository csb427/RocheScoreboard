// Generated from Services/Cloud/RemoteCommandDispatcher.cs; edit translator/adapters.
// Source SHA-256: 9d97292ee2444266a5c63d4316dc173ce80e1998886d25ce09d44804c94d0d60
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Roche_Scoreboard.Services.Cloud;

/// <summary>
/// Contract the WPF <c>MainWindow</c> implements so the cloud dispatcher can
/// invoke operator actions without taking a direct reference to the window.
/// Every method runs on the UI thread.
///
/// The signatures intentionally mirror the existing local click handlers so
/// the dispatcher is "press the button" rather than "mutate model" — that
/// way every animation, validation rule, and snapshot continues to work
/// exactly as it does for a local click.
/// </summary>
public interface IRemoteCommandTarget
{
    // Scoring
    void HomeGoal();
    void HomeBehind();
    void AwayGoal();
    void AwayBehind();
    void Undo();

    // Clock
    void ToggleClock();
    void SetClockMode(string mode, int minutes, int seconds);
    void SetElapsedClock(int minutes, int seconds);
    void Siren();

    // Quarter
    void EndQuarter();
    void ContinueQuarter();

    // Match
    void ResetMatch();
    void SetTeams(string homeName, string homeAbbr, string awayName, string awayAbbr);
    void SetColors(string homePrimary, string homeSecondary, string awayPrimary, string awaySecondary);
    void SetFinalsMode(bool enabled);
    void SetWeatherLocation(string location);

    // Presentation
    void SwitchScreen(string screen);
    void ShowScoreboardWindow();

    // Marquee
    void AddMessage(string text);
    void RemoveMessage(int index);
    void SetMessages(string[] messages);

    // Layout / animation
    void SetLayout(string layout);
    void SetGoalAnimation(string style);

    // Training
    void TrainingStartPause();
    void TrainingSkip();
    void TrainingResetActive();
    void TrainingAddTimer(string title, int seconds, TrainingIntervalSpec? interval);
    void TrainingUpdateTimer(Guid id, string title, int seconds);
    void TrainingRemoveTimer(Guid id);
    void TrainingClearQueue();
    void TrainingSetAutoAdvance(bool enabled);
    void TrainingReorder(Guid id, string direction);
    void TrainingAdjustActive(int seconds);
    void TrainingExit();

    // Cricket
    void CricketDelivery(string type, int runs, string? dismissal);
    void CricketUndo();
    void CricketSelectBatter(int index, string role);
    void CricketSelectBowler(int index);
    void CricketNextInnings();
}

// Optional local execution authority. Capture may run on the receive thread;
// the returned predicate is evaluated only on the actual UI dispatch path.
internal interface IRemoteCommandScope
{
    Func<bool>? CaptureExecutionGuard(string eventType);
}

/// <summary>
/// Interval-builder payload accepted by
/// <see cref="IRemoteCommandTarget.TrainingAddTimer"/>. When non-null,
/// the host should expand it into N pairs of work + rest timers.
/// </summary>
public sealed record TrainingIntervalSpec(int Rounds, int WorkSeconds, int RestSeconds, string TitlePrefix);

/// <summary>
/// Marshals incoming <see cref="RemoteEvent"/> envelopes onto the WPF UI
/// thread and invokes the matching <see cref="IRemoteCommandTarget"/> hook.
/// Deduplicates by <see cref="RemoteEvent.Id"/> so realtime replays
/// (reconnect, refresh) never double-apply a goal.
/// </summary>
public sealed class RemoteCommandDispatcher
{
    private readonly IRemoteCommandTarget _target;
    private readonly Action<Action> _enqueue;
    private readonly LruIdCache _processed = new(capacity: 512);

    public RemoteCommandDispatcher(IRemoteCommandTarget target, Dispatcher uiDispatcher)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        _target = target;
        _enqueue = callback => { _ = uiDispatcher.InvokeAsync(callback); };
    }

    // Tests may hold the actual dispatch closure across a public UI boundary.
    // The caller must drain it on the UI thread; command application and
    // deduplication stay owned by this same production dispatcher.
    internal RemoteCommandDispatcher(IRemoteCommandTarget target, Action<Action> enqueue)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(enqueue);
        _target = target;
        _enqueue = enqueue;
    }

    /// <summary>
    /// Entry point for the realtime client. Safe to call from any thread.
    /// </summary>
    public void Dispatch(RemoteEvent ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        if (string.IsNullOrEmpty(ev.EventType)) return;
        if (!_processed.TryAdd(ev.Id)) return;

        var guard = (_target as IRemoteCommandScope)?.CaptureExecutionGuard(ev.EventType);
        _enqueue(() => ApplyOnUiThread(ev, guard));
    }

    private void ApplyOnUiThread(RemoteEvent ev, Func<bool>? guard)
    {
        try
        {
            if (guard is not null && !guard()) return;
            switch (ev.EventType)
            {
                // Scoring
                case RemoteEventTypes.HomeGoal:    _target.HomeGoal(); break;
                case RemoteEventTypes.HomeBehind:  _target.HomeBehind(); break;
                case RemoteEventTypes.AwayGoal:    _target.AwayGoal(); break;
                case RemoteEventTypes.AwayBehind:  _target.AwayBehind(); break;
                case RemoteEventTypes.Undo:        _target.Undo(); break;

                // Clock
                case RemoteEventTypes.ClockToggle: _target.ToggleClock(); break;
                case RemoteEventTypes.Siren:       _target.Siren(); break;
                case RemoteEventTypes.ClockModeSet:
                    _target.SetClockMode(
                        GetString(ev.Payload, "mode", "CountUp"),
                        GetInt(ev.Payload, "minutes", 20),
                        GetInt(ev.Payload, "seconds", 0));
                    break;
                case RemoteEventTypes.ClockSet:
                    _target.SetElapsedClock(
                        GetInt(ev.Payload, "minutes", 0),
                        GetInt(ev.Payload, "seconds", 0));
                    break;

                // Quarter
                case RemoteEventTypes.EndQuarter:      _target.EndQuarter(); break;
                case RemoteEventTypes.ContinueQuarter: _target.ContinueQuarter(); break;

                // Match
                case RemoteEventTypes.MatchReset: _target.ResetMatch(); break;
                case RemoteEventTypes.SetTeams:
                    _target.SetTeams(
                        GetString(ev.Payload, "homeName", "HOME"),
                        GetString(ev.Payload, "homeAbbr", "HOM"),
                        GetString(ev.Payload, "awayName", "AWAY"),
                        GetString(ev.Payload, "awayAbbr", "AWY"));
                    break;
                case RemoteEventTypes.SetColors:
                    _target.SetColors(
                        GetString(ev.Payload, "homePrimary",   "#000000"),
                        GetString(ev.Payload, "homeSecondary", "#FFFFFF"),
                        GetString(ev.Payload, "awayPrimary",   "#FFFFFF"),
                        GetString(ev.Payload, "awaySecondary", "#000000"));
                    break;
                case RemoteEventTypes.SetFinalsMode:
                    _target.SetFinalsMode(GetBool(ev.Payload, "enabled", false));
                    break;
                case RemoteEventTypes.SetWeatherLocation:
                    _target.SetWeatherLocation(GetString(ev.Payload, "location", ""));
                    break;

                // Presentation
                case RemoteEventTypes.SwitchScreen:
                    _target.SwitchScreen(GetString(ev.Payload, "screen", "scorebug"));
                    break;
                case RemoteEventTypes.ShowScoreboardWindow:
                    _target.ShowScoreboardWindow();
                    break;

                // Marquee
                case RemoteEventTypes.MessageAdd:
                    _target.AddMessage(GetString(ev.Payload, "text", ""));
                    break;
                case RemoteEventTypes.MessageRemove:
                    _target.RemoveMessage(GetInt(ev.Payload, "index", -1));
                    break;
                case RemoteEventTypes.MessagesSet:
                    _target.SetMessages(GetStringArray(ev.Payload, "messages"));
                    break;

                // Layout / animation
                case RemoteEventTypes.SetLayout:
                    _target.SetLayout(GetString(ev.Payload, "layout", "classic"));
                    break;
                case RemoteEventTypes.SetGoalAnimation:
                    _target.SetGoalAnimation(GetString(ev.Payload, "style", "broadcast"));
                    break;

                // Cricket
                case RemoteEventTypes.CricketDelivery:
                    _target.CricketDelivery(
                        GetString(ev.Payload, "type", "Dot"),
                        GetInt(ev.Payload, "runs", 0),
                        GetNullableString(ev.Payload, "dismissal"));
                    break;
                case RemoteEventTypes.CricketUndo:
                    _target.CricketUndo();
                    break;
                case RemoteEventTypes.CricketSelectBatter:
                    _target.CricketSelectBatter(
                        GetInt(ev.Payload, "index", -1),
                        GetString(ev.Payload, "role", "new"));
                    break;
                case RemoteEventTypes.CricketSelectBowler:
                    _target.CricketSelectBowler(GetInt(ev.Payload, "index", -1));
                    break;
                case RemoteEventTypes.CricketNextInnings:
                    _target.CricketNextInnings();
                    break;

                // Training
                case RemoteEventTypes.TrainingStartPause:  _target.TrainingStartPause(); break;
                case RemoteEventTypes.TrainingSkip:        _target.TrainingSkip(); break;
                case RemoteEventTypes.TrainingResetActive: _target.TrainingResetActive(); break;
                case RemoteEventTypes.TrainingClearQueue:  _target.TrainingClearQueue(); break;
                case RemoteEventTypes.TrainingSetAutoAdvance:
                    _target.TrainingSetAutoAdvance(GetBool(ev.Payload, "enabled", true));
                    break;
                case RemoteEventTypes.TrainingAddTimer:
                    _target.TrainingAddTimer(
                        GetString(ev.Payload, "title", "Timer"),
                        GetInt(ev.Payload, "seconds", 0),
                        TryGetInterval(ev.Payload));
                    break;
                case RemoteEventTypes.TrainingUpdateTimer:
                    _target.TrainingUpdateTimer(
                        GetGuid(ev.Payload, "id"),
                        GetString(ev.Payload, "title", "Timer"),
                        GetInt(ev.Payload, "seconds", 0));
                    break;
                case RemoteEventTypes.TrainingRemoveTimer:
                    _target.TrainingRemoveTimer(GetGuid(ev.Payload, "id"));
                    break;
                case RemoteEventTypes.TrainingReorder:
                    _target.TrainingReorder(
                        GetGuid(ev.Payload, "id"),
                        GetString(ev.Payload, "direction", "down"));
                    break;
                case RemoteEventTypes.TrainingAdjustActive:
                    _target.TrainingAdjustActive(GetInt(ev.Payload, "seconds", 0));
                    break;
                case RemoteEventTypes.TrainingExit:
                    _target.TrainingExit();
                    break;

                // Lifecycle (informational only)
                case RemoteEventTypes.SessionStarted:
                case RemoteEventTypes.SessionEnded:
                case RemoteEventTypes.Heartbeat:
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[CloudRemote] dispatch error '{ev.EventType}': {ex.Message}");
        }
    }

    // ── JSON helpers ──

    private static string GetString(JsonElement payload, string name, string fallback)
    {
        if (payload.ValueKind != JsonValueKind.Object) return fallback;
        if (!payload.TryGetProperty(name, out var el)) return fallback;
        return el.ValueKind == JsonValueKind.String ? (el.GetString() ?? fallback) : fallback;
    }

    private static string? GetNullableString(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var el)) return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

    private static int GetInt(JsonElement payload, string name, int fallback)
    {
        if (payload.ValueKind != JsonValueKind.Object) return fallback;
        if (!payload.TryGetProperty(name, out var el)) return fallback;
        return el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out int v) ? v : fallback;
    }

    private static bool GetBool(JsonElement payload, string name, bool fallback)
    {
        if (payload.ValueKind != JsonValueKind.Object) return fallback;
        if (!payload.TryGetProperty(name, out var el)) return fallback;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }

    private static string[] GetStringArray(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object) return Array.Empty<string>();
        if (!payload.TryGetProperty(name, out var el)) return Array.Empty<string>();
        if (el.ValueKind != JsonValueKind.Array) return Array.Empty<string>();

        var result = new string[el.GetArrayLength()];
        int i = 0;
        foreach (var item in el.EnumerateArray())
            result[i++] = item.ValueKind == JsonValueKind.String ? (item.GetString() ?? "") : "";
        return result;
    }

    private static Guid GetGuid(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object) return Guid.Empty;
        if (!payload.TryGetProperty(name, out var el)) return Guid.Empty;
        if (el.ValueKind != JsonValueKind.String) return Guid.Empty;
        return Guid.TryParse(el.GetString(), out var g) ? g : Guid.Empty;
    }

    private static TrainingIntervalSpec? TryGetInterval(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return null;
        if (!payload.TryGetProperty("interval", out var spec)) return null;
        if (spec.ValueKind != JsonValueKind.Object) return null;

        int rounds = GetInt(spec, "rounds", 0);
        int work   = GetInt(spec, "workSeconds", 0);
        int rest   = GetInt(spec, "restSeconds", 0);
        string prefix = GetString(spec, "titlePrefix", "Set");
        if (rounds <= 0 || work <= 0) return null;
        return new TrainingIntervalSpec(rounds, work, rest, prefix);
    }

    /// <summary>
    /// Small fixed-size set used to dedupe by event id without unbounded
    /// memory growth. When full it drops the oldest insert.
    /// </summary>
    private sealed class LruIdCache
    {
        private readonly int _capacity;
        private readonly System.Collections.Generic.LinkedList<Guid> _order = new();
        private readonly System.Collections.Generic.Dictionary<Guid, System.Collections.Generic.LinkedListNode<Guid>> _index = new();
        private readonly object _gate = new();

        public LruIdCache(int capacity) => _capacity = capacity;

        public bool TryAdd(Guid id)
        {
            lock (_gate)
            {
                if (_index.ContainsKey(id)) return false;

                if (_order.Count >= _capacity && _order.First is { } oldest)
                {
                    _order.RemoveFirst();
                    _index.Remove(oldest.Value);
                }

                var node = _order.AddLast(id);
                _index[id] = node;
                return true;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Roche_Scoreboard.Services;

/// <summary>
/// Holds operator-customisable overrides for the centre "quarter" label used
/// on the scorebug, presentation bar, and break screens. Overrides are keyed
/// by display state (e.g. "Q1", "HT") so the operator can rename any single
/// state without affecting the others, and a reset clears the override and
/// falls back to the default mapping.
/// </summary>
internal static class QuarterLabelService
{
    /// <summary>Fired whenever any override changes (set or reset).</summary>
    public static event Action? LabelsChanged;

    private static readonly object _lock = new();
    private static Dictionary<string, string> _overrides = Load();

    // A single override applied to whatever centre label is currently displayed
    // (current quarter OR current break). It is intentionally not persisted —
    // operators set it for the live state and it auto-clears when the state
    // transitions to a new quarter/break.
    private static string? _currentOverride;

    /// <summary>
    /// Returns the live operator override applied to the currently displayed
    /// label, or null when no override is active.
    /// </summary>
    public static string? CurrentOverride
    {
        get { lock (_lock) { return _currentOverride; } }
    }

    /// <summary>
    /// Sets (or clears, when text is empty/whitespace) the live override that
    /// replaces whatever centre label is currently on-screen. Raises
    /// <see cref="LabelsChanged"/> when the value changes.
    /// </summary>
    public static void SetCurrentOverride(string? text)
    {
        string? normalized = Normalize(text);
        bool changed;
        lock (_lock)
        {
            changed = !string.Equals(_currentOverride, normalized, StringComparison.Ordinal);
            _currentOverride = normalized;
        }
        if (changed) LabelsChanged?.Invoke();
    }

    /// <summary>
    /// Clears the live current-label override without raising events for the
    /// "no change" case. Used when the scoreboard transitions to a new
    /// quarter or break so that label is shown from defaults.
    /// </summary>
    public static void ClearCurrentOverride()
    {
        bool changed;
        lock (_lock)
        {
            changed = _currentOverride is not null;
            _currentOverride = null;
        }
        if (changed) LabelsChanged?.Invoke();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string GetStoragePath()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Roche_Scoreboard");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "quarter_labels.json");
    }

    private static Dictionary<string, string> Load()
    {
        try
        {
            string path = GetStoragePath();
            if (!File.Exists(path)) return new();
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
                   ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(_overrides, JsonOptions);
            File.WriteAllText(GetStoragePath(), json);
        }
        catch
        {
            // Best effort — never crash over a settings save.
        }
    }

    /// <summary>Default in-quarter label (Q1, Q2, Q3, Q4).</summary>
    public static string GetDefaultQuarterLabel(int quarter)
    {
        quarter = Math.Clamp(quarter, 1, 4);
        return $"Q{quarter}";
    }

    /// <summary>Default break label after the given quarter (QT, HT, 3QT, FT).</summary>
    public static string GetDefaultBreakLabel(int endedQuarter) => endedQuarter switch
    {
        1 => "QT",
        2 => "HT",
        3 => "3QT",
        _ => "FT"
    };

    private static string QuarterKey(int quarter) => $"Q{Math.Clamp(quarter, 1, 4)}";
    private static string BreakKey(int endedQuarter) => "B" + GetDefaultBreakLabel(endedQuarter);

    /// <summary>
    /// Returns the operator override for the in-quarter label, or the default
    /// when no override has been set.
    /// </summary>
    public static string GetQuarterLabel(int quarter)
    {
        lock (_lock)
        {
            return _overrides.TryGetValue(QuarterKey(quarter), out string? v) && !string.IsNullOrWhiteSpace(v)
                ? v
                : GetDefaultQuarterLabel(quarter);
        }
    }

    /// <summary>
    /// Returns the operator override for the break label, or the default
    /// when no override has been set.
    /// </summary>
    public static string GetBreakLabel(int endedQuarter)
    {
        lock (_lock)
        {
            return _overrides.TryGetValue(BreakKey(endedQuarter), out string? v) && !string.IsNullOrWhiteSpace(v)
                ? v
                : GetDefaultBreakLabel(endedQuarter);
        }
    }

    /// <summary>Returns true when the operator has set a custom label for the given quarter.</summary>
    public static bool HasQuarterOverride(int quarter)
    {
        lock (_lock) { return _overrides.ContainsKey(QuarterKey(quarter)); }
    }

    /// <summary>Returns true when the operator has set a custom label for the given break.</summary>
    public static bool HasBreakOverride(int endedQuarter)
    {
        lock (_lock) { return _overrides.ContainsKey(BreakKey(endedQuarter)); }
    }

    /// <summary>
    /// Caps custom labels at this many characters so the operator cannot enter
    /// text that overflows the scorebug centre panel. Long words still fit in
    /// the panel because the consuming UI uses TextTrimming/Viewbox scaling.
    /// </summary>
    public const int MaxLabelLength = 12;

    private static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string trimmed = text.Trim();
        if (trimmed.Length > MaxLabelLength) trimmed = trimmed[..MaxLabelLength];
        return trimmed;
    }

    public static void SetQuarterLabel(int quarter, string? text)
    {
        string? normalized = Normalize(text);
        bool changed;
        lock (_lock)
        {
            string key = QuarterKey(quarter);
            if (normalized is null)
            {
                changed = _overrides.Remove(key);
            }
            else
            {
                changed = !_overrides.TryGetValue(key, out string? existing) || existing != normalized;
                _overrides[key] = normalized;
            }
            if (changed) Save();
        }
        if (changed) LabelsChanged?.Invoke();
    }

    public static void SetBreakLabel(int endedQuarter, string? text)
    {
        string? normalized = Normalize(text);
        bool changed;
        lock (_lock)
        {
            string key = BreakKey(endedQuarter);
            if (normalized is null)
            {
                changed = _overrides.Remove(key);
            }
            else
            {
                changed = !_overrides.TryGetValue(key, out string? existing) || existing != normalized;
                _overrides[key] = normalized;
            }
            if (changed) Save();
        }
        if (changed) LabelsChanged?.Invoke();
    }

    public static void ResetQuarter(int quarter) => SetQuarterLabel(quarter, null);
    public static void ResetBreak(int endedQuarter) => SetBreakLabel(endedQuarter, null);

    /// <summary>Clears every custom label override.</summary>
    public static void ResetAll()
    {
        bool changed;
        lock (_lock)
        {
            changed = _overrides.Count > 0;
            _overrides.Clear();
            if (changed) Save();
        }
        if (changed) LabelsChanged?.Invoke();
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Roche_Scoreboard.Services;

/// <summary>
/// Persists the operator's most recently used training timers (title + duration)
/// across app launches. Stored as JSON in the user's Documents folder so it
/// survives reinstalls and updates.
/// </summary>
public static class RecentTrainingTimerStorage
{
    private const int MaxEntries = 12;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public sealed class Entry
    {
        public string Title { get; set; } = "TIMER";
        public int Seconds { get; set; }
        public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;
    }

    private static string GetPath()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Roche_Scoreboard");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "training_recent_timers.json");
    }

    public static List<Entry> Load()
    {
        try
        {
            string path = GetPath();
            if (!File.Exists(path)) return new List<Entry>();
            string json = File.ReadAllText(path);
            var entries = JsonSerializer.Deserialize<List<Entry>>(json, Options) ?? new List<Entry>();
            return entries
                .Where(e => e.Seconds > 0)
                .OrderByDescending(e => e.LastUsedUtc)
                .Take(MaxEntries)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"RecentTrainingTimerStorage.Load failed: {ex.Message}");
            return new List<Entry>();
        }
    }

    private static void Save(List<Entry> entries)
    {
        try
        {
            File.WriteAllText(GetPath(), JsonSerializer.Serialize(entries, Options));
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"RecentTrainingTimerStorage.Save failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Records the given title/duration as most-recently-used. Identical
    /// (title, seconds) entries are deduplicated by moving the existing
    /// entry to the front. List is trimmed to <see cref="MaxEntries"/>.
    /// </summary>
    public static void Record(string title, int seconds)
    {
        if (string.IsNullOrWhiteSpace(title)) title = "TIMER";
        if (seconds <= 0) return;

        string normalizedTitle = title.Trim();

        var entries = Load();
        entries.RemoveAll(e =>
            string.Equals(e.Title, normalizedTitle, StringComparison.OrdinalIgnoreCase)
            && e.Seconds == seconds);

        entries.Insert(0, new Entry
        {
            Title = normalizedTitle,
            Seconds = seconds,
            LastUsedUtc = DateTime.UtcNow
        });

        if (entries.Count > MaxEntries)
            entries = entries.Take(MaxEntries).ToList();

        Save(entries);
    }
}

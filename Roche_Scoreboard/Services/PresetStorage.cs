using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

public enum PresetReadStatus { Missing, Loaded, RecoveredBackup, Failed }
public enum PresetCommitStatus { Committed, Unchanged, Failed, Conflict }

public sealed class PresetReadResult
{
    public PresetReadStatus Status { get; }
    public IReadOnlyList<TeamPreset> Presets { get; }
    public long Revision { get; }
    public string Error { get; }
    public bool Pending { get; }
    public bool Succeeded => Status != PresetReadStatus.Failed;
    internal PresetReadResult(PresetReadStatus status, IEnumerable<TeamPreset> presets, long revision = 0,
        string error = "", bool pending = false)
    { Status = status; Presets = PresetStorage.Copy(presets); Revision = revision; Error = error; Pending = pending; }
}

public sealed class PresetCommitResult
{
    public PresetCommitStatus Status { get; }
    public PresetReadResult Snapshot { get; }
    public string Error { get; }
    public bool Succeeded => Status is PresetCommitStatus.Committed or PresetCommitStatus.Unchanged;
    public bool Changed => Status == PresetCommitStatus.Committed;
    internal PresetCommitResult(PresetCommitStatus status, PresetReadResult snapshot, string error = "")
    { Status = status; Snapshot = snapshot; Error = error; }
}

// The backend deliberately has no view cancellation token. An accepted write
// finishes independently of the window that submitted it. Tests can gate IO
// without changing permissions, handler routes, or the production file format.
internal interface IPresetStorageIo
{
    Task<string> ReadAllTextAsync(string path);
    Task WriteAllTextAsync(string path, string content);
}

public static class PresetStorage
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private sealed class FileIo : IPresetStorageIo
    {
        public Task<string> ReadAllTextAsync(string path) => File.ReadAllTextAsync(path);
        public Task WriteAllTextAsync(string path, string content) => AtomicFile.WriteAllTextAsync(path, content);
    }
    private sealed class StoreQueue
    {
        internal readonly object Sync = new();
        internal Task Tail = Task.CompletedTask;
        internal PresetReadResult? Confirmed;
        internal long Revision;
    }
    private sealed class Location
    {
        internal readonly string Primary, Shared;
        internal readonly bool Cricket;
        internal Location(string primary, string shared, bool cricket) { Primary = primary; Shared = shared; Cricket = cricket; }
    }
    private static readonly ConcurrentDictionary<string, StoreQueue> Queues = new(StringComparer.OrdinalIgnoreCase);
    private static IPresetStorageIo _io = new FileIo();
    internal static IPresetStorageIo CurrentIoForTests => _io;

    // Scoped, app-owned test seam. Install before submitting work and dispose
    // after draining accepted Tasks. Every submission captures its backend.
    internal static IDisposable UseIoForTests(IPresetStorageIo io)
    {
        ArgumentNullException.ThrowIfNull(io);
        IPresetStorageIo previous = _io; _io = io;
        return new RestoreIo(previous, io);
    }
    private sealed class RestoreIo : IDisposable
    {
        private readonly IPresetStorageIo previous, installed;
        internal RestoreIo(IPresetStorageIo previous, IPresetStorageIo installed) { this.previous = previous; this.installed = installed; }
        public void Dispose() { if (ReferenceEquals(_io, installed)) _io = previous; }
    }

    private static Location Locate(TeamLibrarySport sport)
    {
        string directory = System.Windows.Application.Current is App { IsSelfTestMode: true }
            ? Path.Combine(Path.GetTempPath(), "RocheScoreboard-SelfTests", Environment.ProcessId.ToString(), "presets")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Roche_Scoreboard");
        string shared = Path.Combine(directory, "team_presets.json");
        bool cricket = sport == TeamLibrarySport.Cricket;
        return new Location(cricket ? Path.Combine(directory, "cricket_team_presets.json") : shared, shared, cricket);
    }
    private static StoreQueue Queue(Location location) => Queues.GetOrAdd(location.Primary, _ => new StoreQueue());
    private static Task<T> Enqueue<T>(Location location, Func<StoreQueue, Task<T>> operation)
    {
        StoreQueue queue = Queue(location);
        lock (queue.Sync)
        {
            Task preceding = queue.Tail;
            // Default scheduler and ConfigureAwait(false) keep persistence
            // independent of the dispatcher, including synchronous legacy reads.
            Task<T> task = preceding.ContinueWith(async _ => await operation(queue).ConfigureAwait(false),
                default, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
            queue.Tail = task.ContinueWith(_ => { }, default, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }
    private static bool Expected(Exception error) => error is IOException or UnauthorizedAccessException
        or JsonException or NotSupportedException or ArgumentException;
    private static bool IsCricketLibraryPreset(TeamPreset preset)
        => preset.LibraryTeamId?.StartsWith("cricket-", StringComparison.OrdinalIgnoreCase) == true;
    private static bool IsLegacyCricketPreset(TeamPreset preset)
        => IsCricketLibraryPreset(preset) || preset.CricketPlayers is { Count: > 0 };

    private static async Task<PresetReadResult> ReadFileAsync(string path, IPresetStorageIo io)
    {
        bool failed = false;
        foreach (string candidate in new[] { path, path + ".bak" })
        {
            try
            {
                string json = await io.ReadAllTextAsync(candidate).ConfigureAwait(false);
                List<TeamPreset>? presets = JsonSerializer.Deserialize<List<TeamPreset>>(json, Options);
                if (presets is null || presets.Any(p => p is null || p.CricketPlayers?.Any(player => player is null) == true))
                    throw new JsonException("Invalid preset list or player entry.");
                return new PresetReadResult(candidate == path ? PresetReadStatus.Loaded : PresetReadStatus.RecoveredBackup, presets);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (Exception error) when (Expected(error)) { failed = true; }
        }
        return failed
            ? new PresetReadResult(PresetReadStatus.Failed, Array.Empty<TeamPreset>(), error: "Saved clubs could not be read. The existing files have been kept. Try again.")
            : new PresetReadResult(PresetReadStatus.Missing, Array.Empty<TeamPreset>());
    }
    private static async Task<PresetReadResult> ReadStoreAsync(Location location, IPresetStorageIo io)
    {
        // Keep the existing sport separation and legacy fallback: an existing
        // empty cricket file is authoritative and never resurrects legacy clubs.
        // A directory at its filename is a failed store, not a missing store.
        bool legacy = location.Cricket && !File.Exists(location.Primary) && !Directory.Exists(location.Primary);
        if (legacy)
        {
            StoreQueue shared = Queues.GetOrAdd(location.Shared, _ => new StoreQueue());
            Task pending; lock (shared.Sync) pending = shared.Tail;
            await pending.ConfigureAwait(false);
        }
        PresetReadResult result = await ReadFileAsync(legacy ? location.Shared : location.Primary, io).ConfigureAwait(false);
        IEnumerable<TeamPreset> presets = result.Presets;
        if (legacy) presets = presets.Where(IsLegacyCricketPreset);
        else if (!location.Cricket) presets = presets.Where(p => !IsCricketLibraryPreset(p));
        return new PresetReadResult(result.Status, presets, error: result.Error);
    }
    private static PresetReadResult Remember(StoreQueue queue, PresetReadResult result)
    {
        lock (queue.Sync)
        {
            var stamped = new PresetReadResult(result.Status, result.Presets, queue.Revision, result.Error);
            if (stamped.Succeeded)
                queue.Confirmed = new PresetReadResult(stamped.Status, stamped.Presets, stamped.Revision, stamped.Error);
            return stamped;
        }
    }
    public static Task<PresetReadResult> ReadAsync(TeamLibrarySport sport)
    {
        Location location = Locate(sport); IPresetStorageIo io = _io;
        return Enqueue(location, async queue => Remember(queue, await ReadStoreAsync(location, io).ConfigureAwait(false)));
    }
    // Initial construction/reset stays synchronous only when no accepted work
    // is outstanding. Mounted views follow this snapshot with ReadAsync so a
    // pending commit is observed without blocking the dispatcher or cancelling it.
    public static PresetReadResult ReadSnapshot(TeamLibrarySport sport)
    {
        Location location = Locate(sport); StoreQueue queue = Queue(location);
        bool legacyPending = false;
        if (location.Cricket && !File.Exists(location.Primary) && !Directory.Exists(location.Primary))
        {
            StoreQueue shared = Queues.GetOrAdd(location.Shared, _ => new StoreQueue());
            lock (shared.Sync) legacyPending = !shared.Tail.IsCompleted;
        }
        Task<PresetReadResult> read;
        lock (queue.Sync)
        {
            if (!queue.Tail.IsCompleted || legacyPending)
            {
                PresetReadResult? known = queue.Confirmed;
                return new PresetReadResult(known?.Status ?? PresetReadStatus.Missing,
                    known?.Presets ?? Array.Empty<TeamPreset>(), known?.Revision ?? 0, pending: true);
            }
            IPresetStorageIo io = _io;
            read = Enqueue(location, async owner => Remember(owner, await ReadStoreAsync(location, io).ConfigureAwait(false)));
        }
        return read.GetAwaiter().GetResult();
    }
    private static Task<PresetCommitResult> MutateAsync(TeamLibrarySport sport, Func<List<TeamPreset>, long, string?> mutate)
    {
        Location location = Locate(sport); IPresetStorageIo io = _io;
        return Enqueue(location, async queue =>
        {
            PresetReadResult before = Remember(queue, await ReadStoreAsync(location, io).ConfigureAwait(false));
            if (!before.Succeeded) return new PresetCommitResult(PresetCommitStatus.Failed, before, before.Error);
            List<TeamPreset> staged = Copy(before.Presets);
            string? conflict = mutate(staged, before.Revision);
            if (conflict is not null) return new PresetCommitResult(PresetCommitStatus.Conflict, before, conflict);
            if (SameContents(staged, before.Presets) && File.Exists(location.Primary))
                return new PresetCommitResult(PresetCommitStatus.Unchanged, before);
            try
            {
                string json = JsonSerializer.Serialize(staged, Options);
                await io.WriteAllTextAsync(location.Primary, json).ConfigureAwait(false);
                // AtomicFile's completed replacement is the commit receipt.
                // A later unreadable backup/verification cannot turn it into a
                // fictitious rollback. No target-only readback is used as proof.
                lock (queue.Sync) ++queue.Revision;
                PresetReadResult after = Remember(queue, new PresetReadResult(PresetReadStatus.Loaded, staged));
                return new PresetCommitResult(PresetCommitStatus.Committed, after);
            }
            catch (Exception error) when (Expected(error))
            {
                System.Diagnostics.Debug.WriteLine($"Could not save presets: {error}");
                return new PresetCommitResult(PresetCommitStatus.Failed, before, "Could not save this club. Your saved clubs and current team have been kept. Try again.");
            }
        });
    }
    public static Task<PresetCommitResult> SavePresetAsync(TeamLibrarySport sport, TeamPreset draft, bool includeGoalVideo)
    {
        TeamPreset captured = Clone(draft);
        return MutateAsync(sport, (presets, _) =>
        {
            TeamPreset? target = presets.FirstOrDefault(p => string.Equals(p.PresetName, captured.PresetName, StringComparison.OrdinalIgnoreCase));
            if (target is null) { target = Clone(captured); presets.Add(target); }
            target.PresetName = captured.PresetName; target.TeamName = captured.TeamName;
            target.Abbreviation = captured.Abbreviation; target.PrimaryColor = captured.PrimaryColor;
            target.SecondaryColor = captured.SecondaryColor; target.LogoPath = captured.LogoPath;
            if (includeGoalVideo) target.GoalVideoPath = captured.GoalVideoPath;
            return null;
        });
    }
    public static Task<PresetCommitResult> DeletePresetAsync(TeamLibrarySport sport,
        IReadOnlyList<TeamPreset> expectedBasis, int selectedIndex, long expectedRevision)
    {
        List<TeamPreset> basis = Copy(expectedBasis);
        TeamPreset? captured = selectedIndex >= 0 && selectedIndex < basis.Count ? basis[selectedIndex] : null;
        return MutateAsync(sport, (presets, revision) =>
        {
            const string conflict = "This saved club changed before it could be removed. Select the current saved club and try again.";
            if (captured is null) return conflict;
            // A still-current basis preserves the selected position even for
            // identical legacy duplicates. With a newer basis, only a unique,
            // unchanged captured record is safe to identify without a new ID.
            if (revision == expectedRevision && SameContents(presets, basis))
            { presets.RemoveAt(selectedIndex); return null; }
            bool Matches(TeamPreset candidate) => SameContents(new[] { candidate }, new[] { captured });
            if (basis.Count(Matches) != 1) return conflict;
            List<int> matches = Enumerable.Range(0, presets.Count).Where(index => Matches(presets[index])).ToList();
            if (matches.Count != 1) return conflict;
            presets.RemoveAt(matches[0]);
            return null;
        });
    }
    public static Task<PresetCommitResult> ReorderAsync(TeamLibrarySport sport, IReadOnlyList<string> originalOrder, int from, int to)
    {
        string[] captured = originalOrder.ToArray();
        return MutateAsync(sport, (presets, _) =>
        {
            if (!presets.Select(p => p.PresetName).SequenceEqual(captured, StringComparer.OrdinalIgnoreCase)
                || from < 0 || from >= presets.Count || to < 0 || to >= presets.Count)
                return "Saved clubs changed while you were arranging them. Try again with the current order.";
            TeamPreset moved = presets[from]; presets.RemoveAt(from); presets.Insert(to, moved); return null;
        });
    }
    public static Task<PresetCommitResult> SetLibraryMembershipAsync(TeamLibrarySport sport, TeamPreset preset, bool saved)
    {
        TeamPreset captured = Clone(preset);
        return MutateAsync(sport, (presets, _) =>
        {
            TeamPreset? existing = presets.FirstOrDefault(p => p.LibraryTeamId == captured.LibraryTeamId);
            if (saved && existing is null) presets.Add(Clone(captured));
            else if (!saved && existing is not null) presets.Remove(existing);
            return null;
        });
    }
    internal static List<TeamPreset> Copy(IEnumerable<TeamPreset> presets) => presets.Select(Clone).ToList();
    public static TeamPreset Clone(TeamPreset preset) => new()
    {
        PresetName = preset.PresetName, TeamName = preset.TeamName, Abbreviation = preset.Abbreviation,
        PrimaryColor = preset.PrimaryColor, SecondaryColor = preset.SecondaryColor, LogoPath = preset.LogoPath,
        GoalVideoPath = preset.GoalVideoPath, LibraryTeamId = preset.LibraryTeamId,
        CricketPlayers = preset.CricketPlayers?.Select(p => new CricketPlayerEntry { FirstName = p.FirstName, LastName = p.LastName }).ToList()
    };
    public static bool SameContents(IEnumerable<TeamPreset> first, IEnumerable<TeamPreset> second)
        => JsonSerializer.Serialize(first, Options) == JsonSerializer.Serialize(second, Options);

    // Compatibility APIs are explicit whole-file replacements. UI mutations use
    // the fresh-basis operations above. Legacy callers now receive IO failure
    // rather than a swallowed successful return.
    public static List<TeamPreset> LoadAll() => Copy(ReadSnapshot(TeamLibrarySport.AustralianFootball).Presets);
    public static List<TeamPreset> LoadAllCricket() => Copy(ReadSnapshot(TeamLibrarySport.Cricket).Presets);
    public static async Task<List<TeamPreset>> LoadAllAsync() => Copy((await ReadAsync(TeamLibrarySport.AustralianFootball).ConfigureAwait(false)).Presets);
    public static async Task<List<TeamPreset>> LoadAllCricketAsync() => Copy((await ReadAsync(TeamLibrarySport.Cricket).ConfigureAwait(false)).Presets);
    private static Task<PresetCommitResult> ReplaceAsync(TeamLibrarySport sport, List<TeamPreset> presets)
    {
        List<TeamPreset> captured = Copy(presets);
        return MutateAsync(sport, (current, _) => { current.Clear(); current.AddRange(Copy(captured)); return null; });
    }
    private static void RequireCommit(PresetCommitResult result)
    { if (!result.Succeeded) throw new IOException(result.Error); }
    public static void SaveAll(List<TeamPreset> presets) => RequireCommit(ReplaceAsync(TeamLibrarySport.AustralianFootball, presets).GetAwaiter().GetResult());
    public static void SaveAllCricket(List<TeamPreset> presets) => RequireCommit(ReplaceAsync(TeamLibrarySport.Cricket, presets).GetAwaiter().GetResult());
    public static async Task SaveAllAsync(List<TeamPreset> presets) => RequireCommit(await ReplaceAsync(TeamLibrarySport.AustralianFootball, presets).ConfigureAwait(false));
    public static async Task SaveAllCricketAsync(List<TeamPreset> presets) => RequireCommit(await ReplaceAsync(TeamLibrarySport.Cricket, presets).ConfigureAwait(false));
}

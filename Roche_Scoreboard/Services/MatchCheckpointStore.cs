using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

/// <summary>Local, append-only snapshots of previously saved matches.</summary>
internal static class MatchCheckpointStore
{
    private const int FormatVersion = 1;
    private const int MaximumCheckpoints = 100;
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string DirectoryPath => SelfTestStorage.IsEnabled
        ? Path.GetDirectoryName(SelfTestStorage.PathFor("history", "checkpoints.json"))!
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roche_Scoreboard", "match-history");

    internal sealed record Checkpoint(int Version, string Id, DateTimeOffset SavedAtUtc, string Title, string Detail, GameState State);

    internal static int Count
    {
        get
        {
            try { return Directory.Exists(DirectoryPath) ? Math.Min(MaximumCheckpoints, Directory.EnumerateFiles(DirectoryPath, "*.json").Take(MaximumCheckpoints).Count()) : 0; }
            catch { return 0; }
        }
    }

    internal static IReadOnlyList<Checkpoint> Load(string? directoryOverride = null)
    {
        lock (Gate)
        {
            try
            {
                string directory = directoryOverride ?? DirectoryPath;
                if (!Directory.Exists(directory)) return Array.Empty<Checkpoint>();
                return Directory.EnumerateFiles(directory, "*.json")
                    .Select(Read)
                    .Where(item => item is not null)
                    .Cast<Checkpoint>()
                    .OrderByDescending(item => item.SavedAtUtc)
                    .Take(MaximumCheckpoints)
                    .ToArray();
            }
            catch { return Array.Empty<Checkpoint>(); }
        }
    }

    /// <summary>Stores a detached state before recovery can be retired.</summary>
    internal static bool Archive(GameState state, string? directoryOverride = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (Gate)
        {
            string? temporary = null;
            try
            {
                string directory = directoryOverride ?? DirectoryPath;
                Directory.CreateDirectory(directory);
                state.SavedAtUtc ??= DateTimeOffset.UtcNow;
                var summary = RecoverySession.Describe(state);
                if (summary is null) return false;
                string fingerprint = Fingerprint(state);
                string fingerprintPath = Path.Combine(directory, "latest.sha256");
                string? previousFingerprint = null;
                try
                {
                    string[] marker = File.Exists(fingerprintPath) ? File.ReadAllLines(fingerprintPath) : [];
                    if (marker.Length == 2 && File.Exists(Path.Combine(directory, marker[0] + ".json"))
                        && Read(Path.Combine(directory, marker[0] + ".json")) is not null)
                        previousFingerprint = marker[1].Trim();
                }
                catch (Exception error) when (!UnhandledExceptionPolicy.IsFatalResourceFailure(error)) { }
                if (previousFingerprint is null)
                {
                    string? latestPath = Directory.EnumerateFiles(directory, "*.json").OrderByDescending(path => Path.GetFileName(path), StringComparer.Ordinal).FirstOrDefault();
                    if (latestPath is not null && Read(latestPath) is { } latest)
                        previousFingerprint = Fingerprint(latest.State);
                }
                if (string.Equals(previousFingerprint, fingerprint, StringComparison.Ordinal)) return true;

                string id = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmssfffffff'Z'") + "-" + Guid.NewGuid().ToString("N");
                var entry = new Checkpoint(FormatVersion, id, state.SavedAtUtc.Value, summary.Title, summary.Detail, state);
                string target = Path.Combine(directory, id + ".json");
                temporary = target + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(entry, Options), new UTF8Encoding(false));
                File.Move(temporary, target);
                // The archive file is authoritative. This small marker only
                // avoids deserializing a large snapshot on every autosave.
                string markerTemporary = fingerprintPath + ".tmp";
                try
                {
                    File.WriteAllText(markerTemporary, id + Environment.NewLine + fingerprint, new UTF8Encoding(false));
                    File.Move(markerTemporary, fingerprintPath, overwrite: true);
                }
                catch (Exception error) when (!UnhandledExceptionPolicy.IsFatalResourceFailure(error))
                {
                    try { if (File.Exists(markerTemporary)) File.Delete(markerTemporary); }
                    catch (Exception cleanupError) when (!UnhandledExceptionPolicy.IsFatalResourceFailure(cleanupError)) { }
                }
                Prune(directory);
                return true;
            }
            catch (Exception error) when (!UnhandledExceptionPolicy.IsFatalResourceFailure(error)) { return false; }
            finally
            {
                try { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception error) when (!UnhandledExceptionPolicy.IsFatalResourceFailure(error)) { }
            }
        }
    }

    internal static bool ArchiveCurrentRecovery(string? recoveryPath = null, string? legacyPath = null, string? checkpointDirectory = null)
    {
        GameState? state = recoveryPath is null && legacyPath is null
            ? GameStateService.Load()
            : GameStateService.LoadFrom(recoveryPath ?? GameStateService.RecoveryStatePath, legacyPath);
        return state is null || Archive(state, checkpointDirectory);
    }

    private static Checkpoint? Read(string path)
    {
        try
        {
            var entry = JsonSerializer.Deserialize<Checkpoint>(File.ReadAllText(path), Options);
            if (entry is null || entry.Version != FormatVersion || string.IsNullOrWhiteSpace(entry.Id)
                || RecoverySession.Describe(entry.State) is null) return null;
            return entry;
        }
        catch (Exception error) when (!UnhandledExceptionPolicy.IsFatalResourceFailure(error)) { return null; }
    }

    private static string Fingerprint(GameState state)
    {
        var clone = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state, Options), Options) ?? state;
        clone.SavedAtUtc = null;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(clone, Options))));
    }

    private static void Prune(string directory)
    {
        foreach (var path in Directory.EnumerateFiles(directory, "*.json")
                     .Select(path => (Path: path, Time: File.GetLastWriteTimeUtc(path)))
                     .OrderByDescending(item => item.Time).Skip(MaximumCheckpoints))
        {
            try { File.Delete(path.Path); }
            catch (Exception error) when (!UnhandledExceptionPolicy.IsFatalResourceFailure(error)) { }
        }
    }
}

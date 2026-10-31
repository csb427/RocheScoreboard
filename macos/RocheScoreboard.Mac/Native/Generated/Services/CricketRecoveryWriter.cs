// Generated from Services/CricketRecoveryWriter.cs; edit translator/adapters.
// Source SHA-256: 03cdc51f760073fcf9f8d04fc30a69014b9d353243fcebc4dfad8d191189137c
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Roche_Scoreboard.Services;

/// <summary>
/// One serial recovery writer with a single replaceable pending snapshot. Large
/// JSON serialization and temporary-file writes never execute on the UI thread.
/// A generation check immediately before atomic commit prevents a reset/clear
/// from being undone by a previously captured snapshot.
/// </summary>
internal sealed class CricketRecoveryWriter
{
    private readonly object _gate = new();
    private readonly object _commitGate = new();
    private readonly string _path;
    private readonly Action _clearFiles;
    private readonly Action<Exception>? _onError;
    private readonly Action? _beforeCommit;
    private (long Generation, GameState State)? _pending;
    private Task? _worker;
    private long _generation;
    private Exception? _lastError;
    private int _completedWrites;

    internal CricketRecoveryWriter(string? path = null, Action<Exception>? onError = null, Action? beforeCommit = null)
    {
        _path = path ?? GameStateService.RecoveryStatePath;
        _onError = onError;
        _beforeCommit = beforeCommit;
        _clearFiles = path is null ? GameStateService.Clear : () =>
        {
            if (File.Exists(_path)) File.Delete(_path);
            if (File.Exists(_path + ".bak")) File.Delete(_path + ".bak");
        };
    }
    internal Exception? LastError { get { lock (_gate) return _lastError; } }
    internal int CompletedWrites { get { lock (_gate) return _completedWrites; } }

    internal void Queue(GameState snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            _pending = (_generation, snapshot);
            if (_worker is null) _worker = Task.Run(Drain);
        }
    }
    internal void Clear()
    {
        // The potentially large write happens before this lock. Only the atomic
        // replace and deletion are serialized with clear, so reset does not wait
        // for JSON serialization or a complete multi-megabyte file write.
        lock (_gate) { ++_generation; _pending = null; }
        lock (_commitGate)
        {
            try
            {
                _clearFiles();
                if (File.Exists(_path) || File.Exists(_path + ".bak"))
                    throw new IOException("The previous cricket recovery could not be cleared.");
                lock (_gate) _lastError = null;
            }
            catch (Exception error) { lock (_gate) _lastError = error; Report(error); }
        }
    }
    /// <summary>Retire this session's writes without deleting its last committed recovery.</summary>
    internal void InvalidatePending()
    {
        lock (_gate) { ++_generation; _pending = null; }
        // Wait only for an atomic commit already in progress. Once this returns,
        // an old session cannot overwrite the replacement session's next save.
        lock (_commitGate) { }
    }
    internal bool Flush(TimeSpan timeout)
    {
        Task? worker;
        lock (_gate) worker = _worker;
        try
        {
            if (worker is not null && !worker.Wait(timeout)) return false;
            lock (_gate) return _pending is null && _lastError is null;
        }
        catch (AggregateException error) { Report(error); return false; }
    }
    private void Drain()
    {
        while (true)
        {
            (long Generation, GameState State) item;
            lock (_gate)
            {
                if (_pending is not { } next) { _worker = null; return; }
                item = next; _pending = null;
            }
            string? temporary = null;
            try
            {
                string json = GameStateService.SerializeRecoverySnapshot(item.State);
                string directory = Path.GetDirectoryName(_path) ?? throw new IOException("Recovery path has no parent directory.");
                Directory.CreateDirectory(directory);
                temporary = _path + "." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                _beforeCommit?.Invoke();
                lock (_commitGate)
                {
                    lock (_gate) if (item.Generation != _generation) continue;
                    Commit(temporary);
                    lock (_gate) { _lastError = null; ++_completedWrites; }
                }
            }
            catch (Exception error)
            {
                lock (_gate) if (item.Generation == _generation) _lastError = error;
                Report(error);
            }
            finally
            {
                try { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception error) { Report(error); }
            }
        }
    }
    private void Commit(string temporary)
    {
        if (!File.Exists(_path)) { File.Move(temporary, _path); return; }
        try { File.Replace(temporary, _path, _path + ".bak", ignoreMetadataErrors: true); }
        catch (PlatformNotSupportedException)
        {
            File.Copy(_path, _path + ".bak", overwrite: true);
            File.Move(temporary, _path, overwrite: true);
        }
    }
    private void Report(Exception error)
    {
        Debug.WriteLine("[CricketRecovery] " + error);
        try { _onError?.Invoke(error); }
        catch (Exception reportingError) { Debug.WriteLine("[CricketRecovery] Reporting failed: " + reportingError); }
    }
}

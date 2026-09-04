using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Roche_Scoreboard.Services;

/// <summary>
/// Crash-resistant text persistence. The new payload is fully written beside
/// the destination before it replaces the live file, while the previous valid
/// file remains available as a backup for recovery.
/// </summary>
internal static class AtomicFile
{
    public static T? TryReadJson<T>(string path, JsonSerializerOptions? options = null)
    {
        foreach (string candidate in ReadCandidates(path))
        {
            try
            {
                T? value = JsonSerializer.Deserialize<T>(candidate, options);
                if (value is not null) return value;
            }
            catch (JsonException) { }
            catch (NotSupportedException) { }
        }

        return default;
    }

    public static async Task<T?> TryReadJsonAsync<T>(string path, JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        foreach (string candidatePath in new[] { path, path + ".bak" })
        {
            try
            {
                if (!File.Exists(candidatePath)) continue;
                string json = await File.ReadAllTextAsync(candidatePath, cancellationToken).ConfigureAwait(false);
                T? value = JsonSerializer.Deserialize<T>(json, options);
                if (value is not null) return value;
            }
            catch (JsonException) { }
            catch (NotSupportedException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return default;
    }

    public static void WriteAllText(string path, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("A destination directory is required.", nameof(path));
        Directory.CreateDirectory(directory);
        string temporaryPath = CreateTemporaryPath(path);
        try
        {
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
            Replace(path, temporaryPath);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public static async Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("A destination directory is required.", nameof(path));
        Directory.CreateDirectory(directory);
        string temporaryPath = CreateTemporaryPath(path);
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);
            Replace(path, temporaryPath);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public static string? TryReadAllText(string path)
    {
        try
        {
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        catch { }

        try
        {
            string backupPath = path + ".bak";
            return File.Exists(backupPath) ? File.ReadAllText(backupPath) : null;
        }
        catch { return null; }
    }

    private static IEnumerable<string> ReadCandidates(string path)
    {
        foreach (string candidatePath in new[] { path, path + ".bak" })
        {
            string? content = null;
            try
            {
                if (File.Exists(candidatePath)) content = File.ReadAllText(candidatePath);
            }
            catch { }
            if (content is not null) yield return content;
        }
    }

    public static async Task<string?> TryReadAllTextAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            if (File.Exists(path))
                return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch { }

        try
        {
            string backupPath = path + ".bak";
            return File.Exists(backupPath)
                ? await File.ReadAllTextAsync(backupPath, cancellationToken).ConfigureAwait(false)
                : null;
        }
        catch { return null; }
    }

    private static string CreateTemporaryPath(string path) =>
        $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";

    private static void Replace(string path, string temporaryPath)
    {
        if (!File.Exists(path))
        {
            File.Move(temporaryPath, path);
            return;
        }

        string backupPath = path + ".bak";
        try
        {
            File.Replace(temporaryPath, path, backupPath, ignoreMetadataErrors: true);
        }
        catch (PlatformNotSupportedException)
        {
            File.Copy(path, backupPath, overwrite: true);
            File.Move(temporaryPath, path, overwrite: true);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }
}

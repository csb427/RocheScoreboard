using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Roche_Scoreboard.Services
{
    /// <summary>
    /// Generates a lightweight, playback-friendly copy of imported MP4 videos
    /// in the background. The optimised file is cached under the user's
    /// LocalAppData folder and reused on subsequent imports of the same
    /// source. When <c>ffmpeg.exe</c> is not available on the PATH the
    /// optimizer silently falls back to the original file.
    /// </summary>
    public static class VideoOptimizer
    {
        // Soft thresholds. Files that exceed any of these are re-encoded.
        private const long MaxAcceptableBytes = 25L * 1024 * 1024; // 25 MB
        private const int MaxAcceptableHeight = 720;
        private const int TargetHeight = 720;
        private const int TargetFps = 30;
        private const string TargetVideoBitrate = "3500k";
        private const string TargetAudioBitrate = "128k";

        private static readonly ConcurrentDictionary<string, Task<string>> InFlight = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Lazy<string?> FfmpegPath = new(LocateFfmpeg);
        private static readonly Lazy<string> CacheDir = new(InitCacheDir);

        /// <summary>
        /// Returns the path that should be used for playback. If no optimised
        /// copy exists yet, optimisation is kicked off in the background and
        /// the original path is returned for the current playback session.
        /// </summary>
        public static string GetPlaybackPath(string originalPath)
        {
            if (string.IsNullOrWhiteSpace(originalPath) || !File.Exists(originalPath))
                return originalPath;

            // Don't re-optimise something we already produced.
            if (originalPath.StartsWith(CacheDir.Value, StringComparison.OrdinalIgnoreCase))
                return originalPath;

            string optimised = ComputeOptimisedPath(originalPath);
            if (File.Exists(optimised) && IsUpToDate(originalPath, optimised))
                return optimised;

            if (!NeedsOptimisation(originalPath))
                return originalPath;

            // Kick the conversion off in the background; current playback uses original.
            _ = EnsureOptimisedAsync(originalPath);
            return originalPath;
        }

        /// <summary>
        /// Asynchronously produces an optimised copy if one doesn't already
        /// exist. Returns the optimised path on success, otherwise the
        /// original path.
        /// </summary>
        public static Task<string> EnsureOptimisedAsync(string originalPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(originalPath) || !File.Exists(originalPath))
                return Task.FromResult(originalPath);

            string optimised = ComputeOptimisedPath(originalPath);
            if (File.Exists(optimised) && IsUpToDate(originalPath, optimised))
                return Task.FromResult(optimised);

            if (!NeedsOptimisation(originalPath))
                return Task.FromResult(originalPath);

            return InFlight.GetOrAdd(optimised, _ => Task.Run(() => RunOptimisation(originalPath, optimised, cancellationToken), cancellationToken));
        }

        private static string RunOptimisation(string source, string destination, CancellationToken ct)
        {
            try
            {
                string? ffmpeg = FfmpegPath.Value;
                if (string.IsNullOrEmpty(ffmpeg))
                    return source;

                string tmp = destination + ".part.mp4";
                if (File.Exists(tmp))
                {
                    try { File.Delete(tmp); } catch { }
                }

                string args =
                    $"-y -hide_banner -loglevel error -i \"{source}\" " +
                    $"-vf \"scale='min(iw,trunc(oh*iw/ih/2)*2)':'min(ih,{TargetHeight})':force_original_aspect_ratio=decrease,fps={TargetFps}\" " +
                    $"-c:v libx264 -preset veryfast -profile:v high -pix_fmt yuv420p -b:v {TargetVideoBitrate} -maxrate {TargetVideoBitrate} -bufsize 7000k " +
                    $"-movflags +faststart -c:a aac -b:a {TargetAudioBitrate} \"{tmp}\"";

                var psi = new ProcessStartInfo(ffmpeg, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };

                using var p = Process.Start(psi);
                if (p is null) return source;

                while (!p.WaitForExit(250))
                {
                    if (ct.IsCancellationRequested)
                    {
                        try { p.Kill(true); } catch { }
                        try { File.Delete(tmp); } catch { }
                        return source;
                    }
                }

                if (p.ExitCode != 0 || !File.Exists(tmp))
                {
                    try { File.Delete(tmp); } catch { }
                    return source;
                }

                try { File.Move(tmp, destination, overwrite: true); }
                catch { return source; }

                // Stamp the source last-write into the optimised file so we
                // can detect when the source was replaced and re-run later.
                try { File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source)); } catch { }
                return destination;
            }
            catch
            {
                return source;
            }
            finally
            {
                InFlight.TryRemove(destination, out _);
            }
        }

        private static bool NeedsOptimisation(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length > MaxAcceptableBytes) return true;
                // Without parsing the container we can't cheaply read FPS/res.
                // The size heuristic catches the most common case (large
                // 1080p/4K clips) without adding an extra dependency.
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsUpToDate(string source, string optimised)
        {
            try
            {
                return File.GetLastWriteTimeUtc(optimised) >= File.GetLastWriteTimeUtc(source);
            }
            catch { return false; }
        }

        private static string ComputeOptimisedPath(string originalPath)
        {
            string full = Path.GetFullPath(originalPath);
            string key = full.ToLowerInvariant();
            byte[] bytes = Encoding.UTF8.GetBytes(key);
            byte[] hash = SHA1.HashData(bytes);
            var sb = new StringBuilder(2 * hash.Length);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));

            string name = Path.GetFileNameWithoutExtension(originalPath);
            string safeName = string.Concat(name.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(CacheDir.Value, $"{safeName}.{sb.ToString().Substring(0, 16)}.opt.mp4");
        }

        private static string InitCacheDir()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(root, "Roche_Scoreboard", "OptimisedVideos");
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }

        private static string? LocateFfmpeg()
        {
            string? envPath = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(envPath)) return null;

            foreach (string p in envPath.Split(Path.PathSeparator))
            {
                try
                {
                    string candidate = Path.Combine(p, "ffmpeg.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }

            // Also check alongside the executable.
            try
            {
                string? baseDir = AppContext.BaseDirectory;
                if (!string.IsNullOrEmpty(baseDir))
                {
                    string local = Path.Combine(baseDir, "ffmpeg.exe");
                    if (File.Exists(local)) return local;
                }
            }
            catch { }

            return null;
        }
    }
}

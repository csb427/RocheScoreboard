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
        // The threshold is intentionally aggressive: any clip larger than
        // ~8 MB is almost certainly high-res / high-bitrate and will benefit
        // from a lighter playback copy.
        private const long MaxAcceptableBytes = 8L * 1024 * 1024; // 8 MB
        private const int MaxAcceptableHeight = 540;
        private const int TargetHeight = 540;
        private const int TargetFps = 30;
        private const string TargetVideoBitrate = "2200k";
        private const string TargetAudioBitrate = "96k";

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

            return InFlight.GetOrAdd(optimised, _ => Task.Run(() => RunOptimisationAsync(originalPath, optimised, cancellationToken), cancellationToken));
        }

        private static async Task<string> RunOptimisationAsync(string source, string destination, CancellationToken ct)
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

                // Playback-friendly encode profile:
                //   -preset superfast  : low encoder CPU, combined with Idle
                //                        priority below keeps ffmpeg from
                //                        stealing cycles from playback.
                //   -tune fastdecode   : disables CABAC, in-loop deblocking
                //                        tweaks, etc. so the *decoder* burns
                //                        far less CPU during playback — the
                //                        single biggest win for high-framerate
                //                        scoreboard playback.
                //   -bf 0 -refs 1      : no B-frames and a single reference
                //                        frame so the decoder can render each
                //                        frame independently with minimal
                //                        buffering / motion-comp work.
                //   -g {fps*2}         : keyframe every ~2s; snappier seeking
                //                        and looping with negligible size cost.
                //   -threads 2         : cap the encoder to two threads so we
                //                        never saturate the box while the
                //                        scoreboard is live.
                int gop = TargetFps * 2;
                string args =
                    $"-y -hide_banner -loglevel error -nostats -threads 2 -i \"{source}\" " +
                    $"-vf \"scale='min(iw,trunc(oh*iw/ih/2)*2)':'min(ih,{TargetHeight})':force_original_aspect_ratio=decrease,fps={TargetFps}\" " +
                    $"-c:v libx264 -preset superfast -tune fastdecode -profile:v baseline -level 3.1 -pix_fmt yuv420p " +
                    $"-x264opts no-cabac:no-deblock:ref=1:bframes=0 " +
                    $"-bf 0 -refs 1 -g {gop} -sc_threshold 0 " +
                    $"-b:v {TargetVideoBitrate} -maxrate {TargetVideoBitrate} -bufsize 4400k " +
                    $"-movflags +faststart -c:a aac -b:a {TargetAudioBitrate} -ac 2 \"{tmp}\"";

                var psi = new ProcessStartInfo(ffmpeg, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    // Drain stderr; ffmpeg writes everything there. Without
                    // draining, the OS pipe buffer eventually fills and the
                    // encoder blocks indefinitely on its next write.
                    RedirectStandardError = true,
                    RedirectStandardOutput = false,
                };

                using var p = Process.Start(psi);
                if (p is null) return source;

                // Lower the encoder priority so it never steals CPU from the
                // foreground playback / WPF render thread. Idle is safe here
                // because the optimised file is only consumed on the *next*
                // playback — we explicitly fall back to the original file
                // until ffmpeg finishes, so it can run as slowly as it needs.
                try { p.PriorityClass = ProcessPriorityClass.Idle; } catch { }

                // Asynchronously read & discard stderr to keep the pipe
                // drained. Capturing into a buffer would just grow forever.
                p.ErrorDataReceived += static (_, _) => { };
                try { p.BeginErrorReadLine(); } catch { }

                try
                {
                    await p.WaitForExitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try { p.Kill(true); } catch { }
                    try { File.Delete(tmp); } catch { }
                    return source;
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
            int byteCount = Encoding.UTF8.GetByteCount(full);
            Span<byte> bytes = byteCount <= 512 ? stackalloc byte[byteCount] : new byte[byteCount];
            Encoding.UTF8.GetBytes(full.AsSpan(), bytes);
            // ASCII lower-case in place — file paths are ASCII-equivalent for
            // case-folding on Windows.
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                if (b >= (byte)'A' && b <= (byte)'Z') bytes[i] = (byte)(b + 32);
            }
            Span<byte> hash = stackalloc byte[20];
            SHA1.HashData(bytes, hash);
            string hex = Convert.ToHexString(hash[..8]); // 16 chars, was Substring(0,16) of the full SHA1

            string name = Path.GetFileNameWithoutExtension(originalPath);
            string safeName = string.Concat(name.Split(Path.GetInvalidFileNameChars()));
            // v3 suffix: bumped when the encode profile changes so older
            // cached copies (high-profile / CABAC / 720p) are bypassed and
            // a baseline / no-cabac / 540p copy is produced on next playback.
            return Path.Combine(CacheDir.Value, $"{safeName}.{hex.ToLowerInvariant()}.v3.opt.mp4");
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

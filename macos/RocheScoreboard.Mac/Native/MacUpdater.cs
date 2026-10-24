using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Xml.Linq;
using Roche_Scoreboard.Services;

namespace RocheScoreboard.Mac.Native;

internal static class MacUpdater
{
    internal static string ArchitectureName => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
    internal static GitHubReleaseAsset? FindAsset(GitHubRelease release) => release.Assets?
        .Where(a => a.Size > 0 && Uri.TryCreate(a.BrowserDownloadUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
        .Where(a => a.Name is { } name && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
            name.Contains("Roche", StringComparison.OrdinalIgnoreCase) && name.Contains("Scoreboard", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("Preview", StringComparison.OrdinalIgnoreCase) && !name.Contains("Demo", StringComparison.OrdinalIgnoreCase) &&
            (name.Contains("macos", StringComparison.OrdinalIgnoreCase) || name.Contains("osx", StringComparison.OrdinalIgnoreCase)) &&
            name.Contains(ArchitectureName, StringComparison.OrdinalIgnoreCase))
        .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();

    internal static async Task<bool> ApplyAsync(GitHubReleaseAsset? asset, IProgress<UpdateProgressInfo>? progress, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Mac application updates require macOS.");
        if (asset is null) throw new InvalidOperationException($"This release has no compatible Mac {ArchitectureName} ZIP package.");
        string installed = FindAppBundle(AppContext.BaseDirectory) ?? throw new InvalidOperationException("Install the packaged Roche Scoreboard application before using in-app updates. A source checkout cannot be replaced by an app bundle.");
        string parent = Path.GetDirectoryName(installed)!;
        string probe = Path.Combine(parent, ".roche-update-write-" + Guid.NewGuid().ToString("N"));
        using (File.Create(probe)) { } File.Delete(probe);
        string staging = Path.Combine(Path.GetTempPath(), "Roche_Scoreboard_Update_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        bool launched = false;
        string? prepared = null;
        void Report(UpdateProgressStage stage, double fraction, string message, long bytes = 0, int attempt = 1)
            => progress?.Report(new(stage, fraction, bytes, asset.Size, attempt, 3, message));
        try
        {
            Report(UpdateProgressStage.Preparing, 0, "Preparing a complete Mac application replacement.");
            string archive = Path.Combine(staging, "application.zip");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("RocheScoreboard-Mac-Updater/4.1");
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    using var response = await http.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellation);
                    response.EnsureSuccessStatusCode();
                    await using var input = await response.Content.ReadAsStreamAsync(cancellation);
                    await using var output = new FileStream(archive, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true);
                    byte[] buffer = new byte[131072]; long bytes = 0;
                    while (await input.ReadAsync(buffer, cancellation) is int read && read > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellation); bytes += read;
                        Report(UpdateProgressStage.Downloading, Math.Min(1, (double)bytes / asset.Size), "Downloading Mac package.", bytes, attempt);
                    }
                    if (bytes != asset.Size) throw new InvalidDataException($"The downloaded package has {bytes} bytes; GitHub advertised {asset.Size}.");
                    break;
                }
                catch (Exception ex) when (attempt < 3 && !cancellation.IsCancellationRequested && ex is HttpRequestException or IOException)
                { await Task.Delay(TimeSpan.FromSeconds(attempt), cancellation); }
            }
            Report(UpdateProgressStage.Verifying, 1, "Checking the package digest and archive.");
            if (asset.Digest is { Length: > 0 } digest)
            {
                if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The release uses an unsupported package digest.");
                await using var input = File.OpenRead(archive);
                string actual = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellation));
                if (!actual.Equals(digest[7..], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The Mac package SHA-256 digest does not match GitHub.");
            }
            string extracted = Path.Combine(staging, "extracted");
            Report(UpdateProgressStage.Extracting, 1, "Extracting the Mac application.");
            ExtractArchive(archive, extracted, cancellation);
            string[] apps = Directory.GetDirectories(extracted, "*.app", SearchOption.AllDirectories)
                .Where(a => File.Exists(Path.Combine(a, "Contents", "Info.plist"))).ToArray();
            if (apps.Length != 1) throw new InvalidDataException("The release must contain exactly one Mac application bundle.");
            string candidate = apps[0];
            ValidateBundle(candidate, installed);
            Report(UpdateProgressStage.Staging, 1, "Checking the Mac application signature.");
            var verify = new ProcessStartInfo("/usr/bin/codesign") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            foreach (string argument in new[] { "--verify", "--deep", "--strict", candidate }) verify.ArgumentList.Add(argument);
            using (var process = Process.Start(verify) ?? throw new IOException("Could not validate the application signature."))
            {
                Task<string> error = process.StandardError.ReadToEndAsync(cancellation);
                Task<string> output = process.StandardOutput.ReadToEndAsync(cancellation);
                await process.WaitForExitAsync(cancellation); await output;
                if (process.ExitCode != 0) throw new InvalidDataException("The Mac application signature is invalid: " + await error);
            }
            cancellation.ThrowIfCancellationRequested();
            prepared = Path.Combine(parent, ".RocheScoreboard-update-" + Guid.NewGuid().ToString("N") + ".app");
            CopyDirectory(candidate, prepared); // Stage on the install volume so the final rename is atomic.
            string script = Path.Combine(staging, "install.sh");
            await File.WriteAllTextAsync(script, InstallScript, cancellation);
            string log = Path.Combine(NativeStoragePaths.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roche_Scoreboard", "mac-update.log");
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, WorkingDirectory = staging };
            foreach (string argument in new[] { script, Environment.ProcessId.ToString(), prepared, installed, log }) start.ArgumentList.Add(argument);
            using var installer = Process.Start(start) ?? throw new IOException("The Mac update helper could not start.");
            launched = true;
            Report(UpdateProgressStage.Ready, 1, "Ready to restart. The previous application is retained for rollback.");
            return true;
        }
        finally
        {
            if (!launched)
            {
                try { Directory.Delete(staging, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                if (prepared is not null)
                    try { Directory.Delete(prepared, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
    internal static string? FindAppBundle(string path)
    {
        for (DirectoryInfo? directory = new(Path.GetFullPath(path)); directory is not null; directory = directory.Parent)
            if (directory.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return directory.FullName;
        return null;
    }
    internal static void ExtractArchive(string archive, string directory, CancellationToken cancellation)
    {
        Directory.CreateDirectory(directory);
        string root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Sum(e => e.Length) > 2L * 1024 * 1024 * 1024)
            throw new InvalidDataException("The expanded application package exceeds the 2 GiB limit.");
        foreach (var entry in zip.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            string name = entry.FullName.Replace('\\', '/');
            string destination = Path.GetFullPath(Path.Combine(root, name));
            if (!destination.StartsWith(root, StringComparison.Ordinal) || Path.IsPathRooted(name)) throw new InvalidDataException("The Mac update archive contains an invalid path.");
            int mode = entry.ExternalAttributes >> 16;
            if ((mode & 0xF000) == 0xA000) throw new InvalidDataException("Mac update packages must contain regular files rather than symbolic links.");
        }
        if (OperatingSystem.IsMacOS())
        {
            // .NET files carry signatures in extended attributes. Apple's ZIP
            // extractor restores the AppleDouble metadata produced by ditto.
            RunDitto("-x", "-k", archive, directory);
            cancellation.ThrowIfCancellationRequested();
            return;
        }
        foreach (var entry in zip.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            string name = entry.FullName.Replace('\\', '/');
            string destination = Path.GetFullPath(Path.Combine(root, name));
            int mode = entry.ExternalAttributes >> 16;
            if (name.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination);
            if (!OperatingSystem.IsWindows() && (mode & 0x1FF) != 0) File.SetUnixFileMode(destination, (UnixFileMode)(mode & 0x1FF));
        }
    }
    internal static void ValidateBundle(string candidate, string installed)
    {
        static string Value(string app, string key)
        {
            var dictionary = XDocument.Load(Path.Combine(app, "Contents", "Info.plist")).Root?.Element("dict") ?? throw new InvalidDataException("The application property list is missing.");
            return dictionary.Elements("key").FirstOrDefault(e => e.Value == key)?.ElementsAfterSelf().FirstOrDefault()?.Value
                ?? throw new InvalidDataException($"The application has no {key} value.");
        }
        if (Value(candidate, "CFBundleIdentifier") != Value(installed, "CFBundleIdentifier")) throw new InvalidDataException("The update belongs to a different Mac application.");
        string executable = Value(candidate, "CFBundleExecutable");
        if (Path.GetFileName(executable) != executable) throw new InvalidDataException("The update executable name is invalid.");
        string binary = Path.Combine(candidate, "Contents", "MacOS", executable);
        byte[] header = new byte[8]; using (var file = File.OpenRead(binary)) file.ReadExactly(header);
        uint cpu = ArchitectureName == "arm64" ? 0x0100000cU : 0x01000007U;
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0xfeedfacf || BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)) != cpu)
            throw new InvalidDataException($"The Mac executable does not match the running {ArchitectureName} architecture.");
        foreach (string library in new[] { "libcoreclr.dylib", "libhostfxr.dylib", "libSkiaSharp.dylib" })
            if (!File.Exists(Path.Combine(candidate, "Contents", "MacOS", library))) throw new InvalidDataException("The Mac package is incomplete: " + library);
        string vlc = Path.Combine(candidate, "Contents", "Frameworks", "vlc");
        foreach (string library in new[] { "libvlc.dylib", "libvlccore.dylib" })
            if (!File.Exists(Path.Combine(vlc, "lib", library))) throw new InvalidDataException("The Mac video runtime is incomplete: " + library);
        if (!Directory.Exists(Path.Combine(vlc, "plugins")) || Directory.GetFiles(Path.Combine(vlc, "plugins"), "*.dylib", SearchOption.AllDirectories).Length < 100)
            throw new InvalidDataException("The Mac video plugin set is incomplete.");
    }
    private static void CopyDirectory(string source, string destination)
    {
        if (OperatingSystem.IsMacOS())
        {
            RunDitto(source, destination); // Preserve the validated code-signing attributes.
            return;
        }
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (string directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        if (!OperatingSystem.IsWindows())
            foreach (string file in Directory.GetFiles(source)) File.SetUnixFileMode(Path.Combine(destination, Path.GetFileName(file)), File.GetUnixFileMode(file));
    }
    private static void RunDitto(params string[] arguments)
    {
        var start = new ProcessStartInfo("/usr/bin/ditto") { UseShellExecute = false, RedirectStandardError = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("The Mac archive service could not start.");
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120000)) { process.Kill(); throw new IOException("The Mac archive service timed out."); }
        if (process.ExitCode != 0) throw new IOException("The Mac archive service failed: " + error.GetAwaiter().GetResult());
    }
    private const string InstallScript = """
#!/bin/sh
set -eu
old_pid=$1
prepared=$2
installed=$3
log=$4
exec >> "$log" 2>&1
attempt=0
while kill -0 "$old_pid" 2>/dev/null; do
  attempt=$((attempt + 1))
  if [ "$attempt" -gt 60 ]; then echo 'The old application did not exit; update left staged.'; exit 1; fi
  sleep 1
done
backup="${installed}.previous"
if [ -e "$backup" ]; then mv "$backup" "${backup}.$(date +%s)"; fi
mv "$installed" "$backup"
if ! mv "$prepared" "$installed"; then mv "$backup" "$installed"; exit 1; fi
if ! /usr/bin/open "$installed"; then
  mv "$installed" "${installed}.failed.$(date +%s)"
  mv "$backup" "$installed"
  /usr/bin/open "$installed"
  exit 1
fi
""";
}

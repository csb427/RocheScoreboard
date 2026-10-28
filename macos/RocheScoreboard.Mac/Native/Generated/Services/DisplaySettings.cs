// Generated from Services/DisplaySettings.cs; edit translator/adapters.
// Source SHA-256: fcf79db846d74a2b0cd9a14963364504935897b130b91d24eacc94ff38e6c670
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System;
using System.IO;
using System.Text.Json;

namespace Roche_Scoreboard.Services;

/// <summary>
/// Persists display-window rendering preferences that the operator can flip
/// at runtime. Stored as JSON in the same MyDocuments\Roche_Scoreboard
/// folder as other user data so settings travel with the operator profile.
/// </summary>
internal static class DisplaySettings
{
    private sealed class SettingsData
    {
        // Nullable so we can distinguish "operator has never chosen" (null)
        // from "operator explicitly chose transparent" (false). New installs
        // get the opaque/high-framerate path by default; existing saved files
        // keep whatever the operator picked previously.
        public bool? OpaqueDisplayWindows { get; set; }
        public string? LastVideoPath { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string GetStoragePath()
    {
        string dir = SelfTestStorage.DocumentsDirectory;
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "display_settings.json");
    }

    private static SettingsData Load()
    {
        string path = GetStoragePath();
        try
        {
            return AtomicFile.TryReadJson<SettingsData>(path, JsonOptions) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static void Save(SettingsData data)
    {
        try
        {
            string json = JsonSerializer.Serialize(data, JsonOptions);
            AtomicFile.WriteAllText(GetStoragePath(), json);
        }
        catch
        {
            // Persisting settings is best-effort; never crash over it.
        }
    }

    /// <summary>
    /// When true, the AFL / Cricket / Training display windows are created
    /// with <c></c> + a solid black background and
    /// a normal (non-borderless) chrome. This restores WPF's GPU-accelerated
    /// composition path and is the primary mitigation for global framerate
    /// drops during MP4 playback on the scoreboard PC.
    /// </summary>
    /// <remarks>
    /// Default is <c>true</c> for new installs (no settings file yet) so the
    /// out-of-the-box experience gets the high-framerate composition path.
    /// Operators who previously toggled transparency on with Ctrl+Shift+T
    /// keep their saved choice unchanged.
    /// </remarks>
    public static bool OpaqueDisplayWindows
    {
        get => Load().OpaqueDisplayWindows ?? true;
        set
        {
            var data = Load();
            data.OpaqueDisplayWindows = value;
            Save(data);
        }
    }

    /// <summary>
    /// Last custom MP4 path selected on the VIDEO tab. Restored on startup
    /// so the operator does not need to re-pick the same clip every session.
    /// </summary>
    public static string? LastVideoPath
    {
        get => Load().LastVideoPath;
        set
        {
            var data = Load();
            data.LastVideoPath = value;
            Save(data);
        }
    }
}

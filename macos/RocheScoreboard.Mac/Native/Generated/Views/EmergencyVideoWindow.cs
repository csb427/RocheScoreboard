// Generated from Views/EmergencyVideoWindow.cs; edit translator/adapters.
// Source SHA-256: 4b0b3291bb421ba3680d24e54ea7fa2e3fa303dc3e567db4be9693677a7aafea
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System;

using LibVLCSharp.Shared;
using VideoView = RocheScoreboard.Mac.Native.NativeVideoSurface;
using Brushes = Avalonia.Media.Brushes;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace Roche_Scoreboard.Views;

/// <summary>
/// Emergency fallback presentation surface for catastrophic embedded
/// playback failures. When the normal in-window takeover model repeatedly
/// fails to produce a render (black screen, frozen pipeline, host instability),
/// the operator can route playback through this dedicated borderless window
/// instead. It is intentionally minimal: a pinned, opaque, sharp-cornered,
/// top-left window that hosts a single <see cref="VideoView"/> and nothing
/// else. The window matches the scoreboard output dimensions so the
/// operator sees the same on-air result without the WPF compositor in the
/// playback path.
/// </summary>
internal sealed class EmergencyVideoWindow : NativeWindow
{
    private readonly LibVLC _libVlc;
    private MediaPlayer? _player;
    private string? _sourcePath;

    public EmergencyVideoWindow(LibVLC libVlc, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(libVlc);
        _libVlc = libVlc;

        // Borderless, opaque, pinned top-left. No transparency, no rounded
        // corners. The intent is to look identical to the normal scoreboard
        // output surface so the operator cannot visually distinguish modes.
        SystemDecorations = SystemDecorations.Full;
        CanResize = false;
        ;
        Background = Brushes.Black;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0;
        Top = 0;
        Width = width;
        Height = height;
        Title = "Roche Scoreboard - Emergency Playback";

        VideoView videoView = new();
        Content = videoView;

        Loaded += (_, _) =>
        {
            try
            {
                _player = new MediaPlayer(_libVlc)
                {
                    EnableHardwareDecoding = false, // emergency mode = safest decoder
                    EnableKeyInput = false,
                    EnableMouseInput = false,
                };
                videoView.MediaPlayer = _player;

                if (_sourcePath is not null)
                    StartPlaybackInternal(_sourcePath);
            }
            catch (Exception ex)
            {
                Services.VideoDiagnostics.Error("EmergencyWindow",
                    "Emergency MediaPlayer construction failed.", ex);
            }
        };

        Closed += (_, _) => DisposePlayer();
    }

    /// <summary>
    /// Begins (or replaces) playback in the emergency window. Safe to call
    /// before the window has finished loading; the source is cached and
    /// applied once <see cref="VideoView"/> is ready.
    /// </summary>
    public void PlaySource(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        _sourcePath = filePath;
        Services.VideoDiagnostics.Warn("EmergencyWindow",
            $"Emergency playback engaged for {filePath}.");
        if (_player is not null) StartPlaybackInternal(filePath);
    }

    private void StartPlaybackInternal(string filePath)
    {
        if (_player is null) return;
        try
        {
            using Media media = new(_libVlc, filePath, FromType.FromPath, ":avcodec-hw=none");
            _player.Media = media;
            _player.Play();
        }
        catch (Exception ex)
        {
            Services.VideoDiagnostics.Error("EmergencyWindow",
                "Emergency playback start failed.", ex);
        }
    }

    private void DisposePlayer()
    {
        try
        {
            if (_player is not null)
            {
                try { _player.Stop(); } catch { }
                Media? media = _player.Media;
                _player.Media = null;
                media?.Dispose();
                _player.Dispose();
                _player = null;
            }
        }
        catch { /* best-effort */ }
    }
}

using System.Buffers;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LibVLCSharp.Shared;

namespace RocheScoreboard.Mac.Native;

/// <summary>
/// Composites VLC frames through Avalonia so output overlays and the operator's
/// live VisualBrush preview see the same video. Playback, audio, seeking, speed,
/// looping and decoder recovery remain owned by the production player control.
/// </summary>
public sealed class NativeVideoSurface : Control
{
    private readonly object _gate = new();
    private readonly MediaPlayer.LibVLCVideoLockCb _lock;
    private readonly MediaPlayer.LibVLCVideoUnlockCb _unlock;
    private readonly MediaPlayer.LibVLCVideoDisplayCb _display;
    private readonly MediaPlayer.LibVLCVideoFormatCb _format;
    private readonly MediaPlayer.LibVLCVideoCleanupCb _cleanup;
    private MediaPlayer? _player;
    private nint _allocation, _pixels;
    private int _width, _height, _pitch, _generation;
    private Frame? _pending;
    private bool _uploadQueued;
    private WriteableBitmap? _bitmap;
    internal int FramesPresented { get; private set; }
    private sealed record Frame(byte[] Bytes, int Width, int Height, int Pitch, int Generation);
    public NativeVideoSurface()
    {
        Unloaded += (_, _) => MediaPlayer = null;
        _lock = Lock; _unlock = Unlock; _display = Display; _format = Format; _cleanup = Cleanup;
    }
    public MediaPlayer? MediaPlayer
    {
        get => _player;
        set
        {
            Dispatcher.UIThread.VerifyAccess();
            if (ReferenceEquals(_player, value)) return;
            if (_player is { } previous)
            {
                // LibVLCSharp requires a non-null lock delegate. The production
                // owner disposes the stopped player immediately after detaching;
                // keep its delegates rooted until that disposal completes.
                try { previous.Stop(); }
                catch (ObjectDisposedException) { }
            }
            _player = value;
            lock (_gate)
            {
                _generation++;
                if (_pending is { } frame) ArrayPool<byte>.Shared.Return(frame.Bytes);
                _pending = null;
            }
            _bitmap?.Dispose(); _bitmap = null; InvalidateVisual();
            if (value is null) return;
            value.EnableHardwareDecoding = false;
            value.SetVideoFormatCallbacks(_format, _cleanup);
            value.SetVideoCallbacks(_lock, _unlock, _display);
        }
    }
    private uint Format(ref nint opaque, nint chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        try
        {
            if (width == 0 || height == 0 || width > 16384 || height > 16384) return 0;
            // Native scoreboard outputs are small. Bound the working frame while
            // retaining the source aspect ratio for high resolution imported clips.
            double ratio = Math.Min(1, 1920.0 / Math.Max(width, height));
            width = (uint)Math.Max(1, Math.Round(width * ratio)); height = (uint)Math.Max(1, Math.Round(height * ratio));
            pitches = (width * 4 + 31) & ~31U; lines = (height + 31) & ~31U;
            lock (_gate)
            {
                ReleasePixels();
                int length = checked((int)(pitches * lines));
                _allocation = Marshal.AllocHGlobal(checked(length + 31));
                _pixels = (nint)(((long)_allocation + 31) & ~31L);
                _width = checked((int)width); _height = checked((int)height); _pitch = checked((int)pitches);
                Marshal.Copy(new byte[] { (byte)'R', (byte)'V', (byte)'3', (byte)'2' }, 0, chroma, 4);
            }
            return 1;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("VLC frame allocation failed: " + ex.Message); return 0; }
    }
    private nint Lock(nint opaque, nint planes)
    {
        Monitor.Enter(_gate);
        Marshal.WriteIntPtr(planes, _pixels);
        return _pixels;
    }
    private void Unlock(nint opaque, nint picture, nint planes) => Monitor.Exit(_gate);
    private void Display(nint opaque, nint picture)
    {
        lock (_gate)
        {
            if (_pixels == 0 || _player is null) return;
            int size = checked(_pitch * _height);
            var bytes = ArrayPool<byte>.Shared.Rent(size);
            Marshal.Copy(_pixels, bytes, 0, size);
            if (_pending is { } previous) ArrayPool<byte>.Shared.Return(previous.Bytes);
            _pending = new(bytes, _width, _height, _pitch, _generation);
            if (_uploadQueued) return;
            _uploadQueued = true;
            Dispatcher.UIThread.Post(Upload, DispatcherPriority.Render);
        }
    }
    private void Cleanup(ref nint opaque)
    {
        lock (_gate) ReleasePixels();
    }
    private void ReleasePixels()
    {
        if (_allocation != 0) Marshal.FreeHGlobal(_allocation);
        _allocation = _pixels = 0;
    }
    private void Upload()
    {
        Frame? frame;
        lock (_gate) { frame = _pending; _pending = null; _uploadQueued = false; }
        if (frame is null) return;
        try
        {
            if (frame.Generation != _generation || _player is null) return;
            var size = new PixelSize(frame.Width, frame.Height);
            if (_bitmap is null || _bitmap.PixelSize != size)
            {
                _bitmap?.Dispose();
                _bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            }
            using (var pixels = _bitmap.Lock())
                for (int y = 0; y < frame.Height; y++)
                    Marshal.Copy(frame.Bytes, y * frame.Pitch, pixels.Address + y * pixels.RowBytes, frame.Width * 4);
            FramesPresented++;
            InvalidateVisual();
        }
        finally { ArrayPool<byte>.Shared.Return(frame.Bytes); }
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        if (_bitmap is null) return;
        double scale = Math.Min(Bounds.Width / _bitmap.Size.Width, Bounds.Height / _bitmap.Size.Height);
        var destination = new Rect((Bounds.Width - _bitmap.Size.Width * scale) / 2,
            (Bounds.Height - _bitmap.Size.Height * scale) / 2, _bitmap.Size.Width * scale, _bitmap.Size.Height * scale);
        context.DrawImage(_bitmap, new Rect(_bitmap.Size), destination);
    }
}

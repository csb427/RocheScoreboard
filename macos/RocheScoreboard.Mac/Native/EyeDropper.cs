using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Globalization;
using SkiaSharp;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using RocheScoreboard.Mac.Native;

namespace Roche_Scoreboard.Services;

internal static class EyeDropper
{
    private const string Graphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string Foundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    [StructLayout(LayoutKind.Sequential)] private readonly record struct CGPoint(double X, double Y);
    [StructLayout(LayoutKind.Sequential)] private readonly record struct CGSize(double Width, double Height);
    [StructLayout(LayoutKind.Sequential)] private readonly record struct CGRect(CGPoint Origin, CGSize Size);
    [DllImport(Graphics)] private static extern uint CGGetActiveDisplayList(uint count, [Out] uint[]? displays, out uint displayCount);
    [DllImport(Graphics)] private static extern CGRect CGDisplayBounds(uint display);
    [DllImport(Graphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGPreflightScreenCaptureAccess();
    [DllImport(Graphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGRequestScreenCaptureAccess();
    [DllImport(Graphics)] private static extern nint CGEventCreate(nint source);
    [DllImport(Graphics)] private static extern CGPoint CGEventGetLocation(nint cursorEvent);
    [DllImport(Foundation)] private static extern void CFRelease(nint value);
    private sealed record Snapshot(CGRect Bounds, int Width, int Height, byte[] Pixels)
    {
        public Color? Sample(CGPoint point)
        {
            double x = (point.X - Bounds.Origin.X) / Bounds.Size.Width;
            double y = (point.Y - Bounds.Origin.Y) / Bounds.Size.Height;
            if (x < 0 || x >= 1 || y < 0 || y >= 1) return null;
            int offset = ((int)(y * Height) * Width + (int)(x * Width)) * 4;
            return Color.FromRgb(Pixels[offset + 2], Pixels[offset + 1], Pixels[offset]);
        }
    }
    public static Color? Pick()
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Global screen sampling uses the macOS screen capture API.");
        if (!CGPreflightScreenCaptureAccess() && !CGRequestScreenCaptureAccess())
        {
            NativeMessageBox.Show("Screen Recording access is required to sample other windows. Enable Roche Scoreboard in System Settings → Privacy & Security → Screen Recording, then reopen the colour picker.", "Screen colour picker");
            return null;
        }
        var snapshots = Capture();
        var owner = NativeDesktop.MainWindow ?? throw new InvalidOperationException("An operator window is required.");
        var windows = new List<Window>();
        Color? result = null;
        var completion = new TaskCompletionSource<Color?>();
        Color Sample()
        {
            nint cursor = CGEventCreate(0);
            try { CGPoint point = CGEventGetLocation(cursor); return snapshots.Select(x => x.Sample(point)).FirstOrDefault(x => x.HasValue) ?? Colors.Black; }
            finally { if (cursor != 0) CFRelease(cursor); }
        }
        void Complete(Color? value) { if (!completion.TrySetResult(value)) return; foreach (var window in windows) window.Close(); }
        foreach (var screen in owner.Screens.All)
        {
            var overlay = new Window
            {
                SystemDecorations = SystemDecorations.None, ShowInTaskbar = false, Topmost = true,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                Position = screen.Bounds.Position, Width = screen.Bounds.Width / screen.Scaling, Height = screen.Bounds.Height / screen.Scaling,
                Cursor = new Cursor(StandardCursorType.Cross)
            };
            var preview = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(24), BorderBrush = Brushes.White, BorderThickness = new Thickness(3), IsVisible = false };
            var fill = new SolidColorBrush(Colors.Black); preview.Background = fill;
            var canvas = new Canvas { IsHitTestVisible = false }; canvas.Children.Add(preview);
            var grid = new Grid(); grid.Children.Add(canvas);
            grid.Children.Add(new TextBlock { Text = "Click to sample · Esc to cancel", FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), Padding = new Thickness(12, 6), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(0, 40, 0, 0) });
            overlay.Content = grid;
            overlay.PointerMoved += (_, e) => { var point = e.GetPosition(canvas); preview.IsVisible = true; Canvas.SetLeft(preview, point.X + 16); Canvas.SetTop(preview, point.Y + 16); fill.Color = Sample(); };
            overlay.PointerPressed += (_, e) => Complete(e.GetCurrentPoint(overlay).Properties.IsLeftButtonPressed ? Sample() : null);
            overlay.KeyDown += (_, e) => { if (e.Key == Key.Escape) Complete(null); };
            windows.Add(overlay); overlay.Show();
        }
        try { result = NativeAwait.Run(completion.Task); }
        finally { foreach (var window in windows) window.Close(); }
        return result;
    }
    private static List<Snapshot> Capture()
    {
        if (CGGetActiveDisplayList(0, null, out uint count) != 0) throw new InvalidOperationException("macOS displays could not be enumerated.");
        var ids = new uint[count];
        if (CGGetActiveDisplayList(count, ids, out count) != 0) throw new InvalidOperationException("macOS displays could not be enumerated.");
        var result = new List<Snapshot>();
        foreach (uint id in ids.Take((int)count))
        {
            // screencapture uses the current system capture service on macOS 15+;
            // CGDisplayCreateImage was removed from the supported API in macOS 15.
            CGRect bounds = CGDisplayBounds(id);
            string path = Path.Combine(Path.GetTempPath(), "roche-eyedropper-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                var start = new ProcessStartInfo("/usr/sbin/screencapture") { UseShellExecute = false, RedirectStandardError = true };
                string region = string.Join(",", new[] { bounds.Origin.X, bounds.Origin.Y, bounds.Size.Width, bounds.Size.Height }.Select(v => v.ToString("0", CultureInfo.InvariantCulture)));
                foreach (string argument in new[] { "-x", "-t", "png", "-R" + region, path }) start.ArgumentList.Add(argument);
                using var capture = Process.Start(start) ?? throw new IOException("The Mac screen capture service could not start.");
                var error = capture.StandardError.ReadToEndAsync();
                if (!capture.WaitForExit(10000)) { capture.Kill(); throw new IOException("Screen capture timed out."); }
                if (capture.ExitCode != 0 || !File.Exists(path)) throw new IOException("macOS did not return a screen image. " + error.GetAwaiter().GetResult());
                using var decoded = SKBitmap.Decode(path) ?? throw new InvalidDataException("The screen image could not be decoded.");
                using var bitmap = decoded.Copy(SKColorType.Bgra8888);
                int stride = checked(bitmap.Width * 4);
                var pixels = new byte[checked(stride * bitmap.Height)];
                for (int row = 0; row < bitmap.Height; row++) Marshal.Copy(bitmap.GetPixels() + row * bitmap.RowBytes, pixels, row * stride, stride);
                result.Add(new(bounds, bitmap.Width, bitmap.Height, pixels));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        return result;
    }
}

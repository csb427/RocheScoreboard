using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Roche_Scoreboard.Services;

/// <summary>Opt-in, native-only preview evidence. This is not a physical-screen capture.</summary>
internal sealed class OperatorPreviewDiagnosticsQa
{
    private const int MaximumWidth = 1600, MaximumHeight = 1200;
    private const int MaximumPngBytes = 8 * 1024 * 1024, MaximumJsonBytes = 64 * 1024;
    private readonly Window _window;
    private readonly RenderMode _requestedRenderMode;
    private readonly DispatcherTimer _timer;
    private readonly DateTimeOffset _attachedUtc = DateTimeOffset.UtcNow;
    private DateTimeOffset? _loadedUtc, _contentRenderedUtc;

    internal static void Attach(Window window, RenderMode requestedRenderMode)
    {
        // This helper must never enable isolation itself or accept an arbitrary output path.
        if (SelfTestStorage.IsEnabled)
            _ = new OperatorPreviewDiagnosticsQa(window, requestedRenderMode);
    }

    private OperatorPreviewDiagnosticsQa(Window window, RenderMode requestedRenderMode)
    {
        _window = window;
        _requestedRenderMode = requestedRenderMode;
        _timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, window.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        window.Loaded += OnLoaded;
        window.ContentRendered += OnContentRendered;
        window.Closed += OnClosed;
        _timer.Tick += CaptureOnce;
        _timer.Start();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => _loadedUtc ??= DateTimeOffset.UtcNow;
    private void OnContentRendered(object? sender, EventArgs e) => _contentRenderedUtc ??= DateTimeOffset.UtcNow;
    private void OnClosed(object? sender, EventArgs e) => Detach();

    private void Detach()
    {
        _timer.Stop();
        _timer.Tick -= CaptureOnce;
        _window.Loaded -= OnLoaded;
        _window.ContentRendered -= OnContentRendered;
        _window.Closed -= OnClosed;
    }

    private void CaptureOnce(object? sender, EventArgs e)
    {
        // Detach before doing any work. Capture failure must not schedule a retry.
        Detach();
        if (!SelfTestStorage.IsEnabled)
            return;

        var elapsed = Stopwatch.StartNew();
        string phase = "measure-before-snapshot";
        try
        {
            var source = PresentationSource.FromVisual(_window);
            var target = source?.CompositionTarget;
            double width = _window.ActualWidth, height = _window.ActualHeight;
            bool validSize = double.IsFinite(width) && double.IsFinite(height) && width > 0 && height > 0;
            double scale = validSize ? Math.Min(1, Math.Min(MaximumWidth / width, MaximumHeight / height)) : 1;
            int pixelWidth = validSize ? Math.Clamp((int)Math.Ceiling(width * scale), 1, MaximumWidth) : 0;
            int pixelHeight = validSize ? Math.Clamp((int)Math.Ceiling(height * scale), 1, MaximumHeight) : 0;
            var captureBounds = validSize ? new Rect(0, 0, width, height) : Rect.Empty;
            var descendantBounds = VisualTreeHelper.GetDescendantBounds(_window);
            using var process = Process.GetCurrentProcess();
            var state = new
            {
                recordedUtc = DateTimeOffset.UtcNow, attachedUtc = _attachedUtc,
                loadedEventUtc = _loadedUtc, contentRenderedEventUtc = _contentRenderedUtc,
                loadedEventObserved = _loadedUtc.HasValue,
                contentRenderedEventObserved = _contentRenderedUtc.HasValue,
                evidence = "Existing WPF visual state before offscreen rendering; not physical desktop evidence.",
                window = Describe(_window), content = Describe(_window.Content as FrameworkElement),
                windowState = _window.WindowState.ToString(), _window.IsActive, _window.AllowsTransparency,
                contentType = _window.Content?.GetType().FullName,
                presentation = new
                {
                    sourceType = source?.GetType().FullName, source?.IsDisposed,
                    rootVisualType = source?.RootVisual?.GetType().FullName,
                    windowHandle = source is HwndSource hwnd ? hwnd.Handle.ToInt64() : (long?)null,
                    compositionTargetType = target?.GetType().FullName,
                    deviceTransform = target?.TransformToDevice.ToString(),
                    targetRenderMode = target is HwndTarget hwndTarget ? hwndTarget.RenderMode.ToString() : null,
                    requestedProcessRenderMode = _requestedRenderMode.ToString(),
                    effectiveProcessRenderMode = RenderOptions.ProcessRenderMode.ToString(),
                    processRenderMode = RenderOptions.ProcessRenderMode.ToString(),
                    renderTier = RenderCapability.Tier >> 16,
                    dpi = VisualTreeHelper.GetDpi(_window)
                },
                session = new { process.SessionId, Environment.UserInteractive, SystemParameters.IsRemoteSession },
                snapshotPlan = new
                {
                    validSize, coordinateSpace = "Window-local visual coordinates, not screen coordinates",
                    sourceRectangle = DescribeBounds(captureBounds), pixelWidth, pixelHeight, scale,
                    dpi = 96 * scale, descendantsExtendOutsideCapture = !descendantBounds.IsEmpty
                        && !captureBounds.Contains(descendantBounds),
                    clipping = "Capture clips to the source rectangle; existing element clips remain in effect."
                },
                limits = new { MaximumWidth, MaximumHeight, MaximumPngBytes, MaximumJsonBytes }
            };
            // Persist the measured state before RenderTargetBitmap can affect render activity.
            WriteJson("state-before-snapshot.json", state);

            phase = "render-existing-window-visual";
            if (!validSize)
            {
                WriteJson("snapshot-result.json", new { status = "skipped-invalid-window-size", width, height });
                return;
            }
            // Changing bitmap DPI scales only this offscreen target. No layout, window size,
            // visual transform, process render mode or input is changed or forced here.
            var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            var brush = new VisualBrush(_window)
            {
                AutoLayoutContent = false, ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = captureBounds, Stretch = Stretch.Fill
            };
            var drawing = new DrawingVisual();
            try
            {
                // An explicit local Viewbox avoids including the source root's parent/screen offset.
                using (var context = drawing.RenderOpen())
                    context.DrawRectangle(brush, null, captureBounds);
                bitmap.Render(drawing);
            }
            finally { brush.Visual = null; }
            var pixels = new byte[pixelWidth * pixelHeight * 4];
            bitmap.CopyPixels(pixels, pixelWidth * 4, 0);
            var statistics = PixelStatistics(pixels);

            phase = "encode-png";
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var encoded = new MemoryStream();
            encoder.Save(encoded);
            if (encoded.Length > MaximumPngBytes)
                throw new InvalidOperationException("Preview PNG exceeded the fixed 8 MiB output limit.");
            phase = "write-png";
            using (var file = new FileStream(SelfTestStorage.PathFor("preview-diagnostics", "wpf-content.png"),
                       FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                encoded.Position = 0;
                encoded.CopyTo(file);
            }
            WriteJson("snapshot-result.json", new
            {
                status = "captured", completedUtc = DateTimeOffset.UtcNow, elapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds,
                target = "Existing Window WPF visual and content; excludes native non-client frame.",
                pixelWidth, pixelHeight, scale, dpi = 96 * scale, pngBytes = encoded.Length, statistics
            });
        }
        catch (Exception error) when (!UnhandledExceptionPolicy.IsFatalResourceFailure(error))
        {
            // Fatal render-resource failures still reach the app's terminal policy unchanged.
            try
            {
                string message = error.Message;
                WriteJson("snapshot-result.json", new
                {
                    status = "failed", phase, completedUtc = DateTimeOffset.UtcNow,
                    elapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds, errorType = error.GetType().FullName,
                    error.HResult, message = message.Length <= 2048 ? message : message[..2048]
                });
            }
            catch (Exception writeError) when (!UnhandledExceptionPolicy.IsFatalResourceFailure(writeError))
            { /* Missing output remains observable to the protected runner; never retry. */ }
        }
    }

    private static object? Describe(FrameworkElement? element) => element is null ? null : new
    {
        type = element.GetType().FullName, element.ActualWidth, element.ActualHeight,
        renderWidth = element.RenderSize.Width, renderHeight = element.RenderSize.Height,
        desiredWidth = element.DesiredSize.Width, desiredHeight = element.DesiredSize.Height,
        element.IsLoaded, element.IsVisible, visibility = element.Visibility.ToString(), element.Opacity,
        element.IsMeasureValid, element.IsArrangeValid, element.IsEnabled,
        layoutSlot = LayoutInformation.GetLayoutSlot(element).ToString(),
        visualOffset = VisualTreeHelper.GetOffset(element).ToString(),
        localContentBounds = DescribeBounds(VisualTreeHelper.GetContentBounds(element)),
        localDescendantBounds = DescribeBounds(VisualTreeHelper.GetDescendantBounds(element)),
        element.ClipToBounds, clipBounds = DescribeBounds(element.Clip?.Bounds ?? Rect.Empty),
        renderTransform = element.RenderTransform.Value.ToString(),
        layoutTransform = element.LayoutTransform.Value.ToString(),
        visualChildren = VisualTreeHelper.GetChildrenCount(element)
    };

    private static object DescribeBounds(Rect bounds) => bounds.IsEmpty
        ? new { empty = true, x = (double?)null, y = (double?)null, width = (double?)null, height = (double?)null }
        : new { empty = false, x = (double?)bounds.X, y = (double?)bounds.Y, width = (double?)bounds.Width, height = (double?)bounds.Height };

    private static object PixelStatistics(byte[] pixels)
    {
        int transparent = 0, opaque = 0, white = 0, nonWhiteVisible = 0, differsFromFirst = 0;
        int minAlpha = 255, maxAlpha = 0, minChannel = 255, maxChannel = 0;
        long totalAlpha = 0, totalBlue = 0, totalGreen = 0, totalRed = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2], a = pixels[i + 3];
            minAlpha = Math.Min(minAlpha, a); maxAlpha = Math.Max(maxAlpha, a);
            minChannel = Math.Min(minChannel, Math.Min(b, Math.Min(g, r)));
            maxChannel = Math.Max(maxChannel, Math.Max(b, Math.Max(g, r)));
            totalAlpha += a; totalBlue += b; totalGreen += g; totalRed += r;
            if (a == 0) transparent++;
            if (a == 255) opaque++;
            if (a == 255 && b >= 250 && g >= 250 && r >= 250) white++;
            else if (a > 0) nonWhiteVisible++;
            if (b != pixels[0] || g != pixels[1] || r != pixels[2] || a != pixels[3]) differsFromFirst++;
        }
        int count = pixels.Length / 4;
        return new
        {
            format = "Premultiplied BGRA32", count, transparent, opaque, nearWhiteOpaque = white,
            nonWhiteVisible, differsFromFirst, minAlpha, maxAlpha, minChannel, maxChannel,
            meanAlpha = (double)totalAlpha / count, meanBlue = (double)totalBlue / count,
            meanGreen = (double)totalGreen / count, meanRed = (double)totalRed / count
        };
    }

    private static void WriteJson(string name, object value)
    {
        string json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        if (Encoding.UTF8.GetByteCount(json) > MaximumJsonBytes)
            throw new InvalidOperationException("Preview JSON exceeded its fixed 64 KiB output limit.");
        string path = SelfTestStorage.PathFor("preview-diagnostics", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }
}

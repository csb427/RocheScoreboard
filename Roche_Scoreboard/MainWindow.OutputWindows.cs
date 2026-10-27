using System;
using System.Collections.Generic;
using System.Windows;
using Roche_Scoreboard.Services;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private (int Width, int Height)? _sharedOutputPixels;
    private bool _synchronizingOutputSize;

    private (int Width, int Height) SharedOutputPixels
    {
        get
        {
            if (_sharedOutputPixels is { } size) return size;
            var preferences = OperatorWorkflowPreferences.Load();
            return (_sharedOutputPixels = (preferences.OutputWindowWidth, preferences.OutputWindowHeight)).Value;
        }
    }

    private IEnumerable<Window> OutputWindows()
    {
        if (_displayWindow is { } afl) yield return afl;
        if (_cricketDisplayWindow is { } cricket) yield return cricket;
        if (_trainingDisplayWindow is { } training) yield return training;
    }

    private void ApplySharedOutputDimensions(int width, int height, bool remember = true)
    {
        width = Math.Clamp(width, 50, 7680);
        height = Math.Clamp(height, 50, 4320);
        _sharedOutputPixels = (width, height);
        _synchronizingOutputSize = true;
        try
        {
            foreach (var window in OutputWindows())
                PixelSizeHelper.SetWindowPixels(window, width, height);
        }
        finally { _synchronizingOutputSize = false; }
        UpdateDimensionBoxes(width, height);
        GlobalSettingsPanel.SetOutputDimensions(width, height);
        if (remember) QueueOutputDimensionsSave(width, height);
    }

    private static void ApplyOutputWindowChrome(Window window)
    {
        window.WindowStyle = WindowStyle.None;
        System.Windows.Shell.WindowChrome.SetWindowChrome(window, new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 0,
            GlassFrameThickness = new Thickness(0),
            ResizeBorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
        WindowChromeEnforcer.Attach(window);
    }
}

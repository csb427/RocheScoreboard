// Generated from MainWindow.OutputWindows.cs; edit translator/adapters.
// Source SHA-256: 674b7326c5a43465dfc524ec26f6ac1dcd5158b14a33ed6c532d92b1a929d3f7
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
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

    private IEnumerable<NativeWindow> OutputWindows()
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

    private static void ApplyOutputWindowChrome(NativeWindow window)
    {
        window.SystemDecorations=SystemDecorations.Full;
        
        OperatorWindowChrome.Enable(window);
    }
}

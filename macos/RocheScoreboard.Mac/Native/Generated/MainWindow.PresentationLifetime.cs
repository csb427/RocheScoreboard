// Generated from MainWindow.PresentationLifetime.cs; edit translator/adapters.
// Source SHA-256: a6de44eb682e0cc26684df6f15ad10277c638a034eb1b8bb6435f86bfbf39f52
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
using System.Linq;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private int _presentationLifetimeGeneration;
    private readonly Dictionary<DispatcherTimer, Action> _scheduledOverlayCancellations = new();
    private ScorebugControl? _overlayHiddenOwner;
    private Action? _overlayHiddenHandler;

    private void DetachOverlayQueueOwner()
    {
        if (_overlayHiddenOwner is not null && _overlayHiddenHandler is not null)
            _overlayHiddenOwner.OverlayHidden -= _overlayHiddenHandler;
        _overlayHiddenOwner = null; _overlayHiddenHandler = null; _overlayQueueWired = false;
    }

    private void CancelScheduledOverlayWork()
    {
        foreach (var cancel in _scheduledOverlayCancellations.Values.Distinct().ToArray()) cancel();
        _autoOverlayDelayTimer?.Stop(); _autoOverlayDelayTimer = null;
        _autoOverlayDelayCancellation = null;
        _scoringRunDelayTimer?.Stop(); _scoringRunDelayTimer = null;
        _periodicOverlayReserved = false;
    }

    private void InvalidatePresentationWork()
    {
        ++_presentationLifetimeGeneration; ++_screenTransitionVersion;
        DetachOverlayQueueOwner();
        CancelPresentationAnimations();
        CancelQueuedOverlays(); CancelScheduledOverlayWork();
        _breakScreenDelayTimer?.Stop(); _breakScreenDelayTimer = null;
        _scorebug?.ResetAllOverlayState();
        _overlayScheduler.NotifyOverlayHidden();
    }

    private void RestoreAflOutputPresentation()
    {
        Control? selected;
        switch (_activeScreenKey)
        {
            case "worm": PopulateScoreworm(); selected = _scoreworm; break;
            case "stats": PopulateStatsScreen(); selected = _statsScreen; break;
            case "summary": PopulateBreakScreen(); selected = _breakScreen; break;
            case "quarterbreak": PopulateQuarterBreakScorebug(); selected = _quarterBreakScorebug; break;
            case "weather": PopulateWeatherScreen(); selected = _weatherScreen; break;
            case "screensaver": selected = _outputScreensaver; break;
            case "video" when _videoPlayer?.HasSource == true: _videoPlayer.Pause(); selected = _videoPlayer; break;
            default: selected = _scorebug; _activeScreenKey = "scorebug"; break;
        }
        if (selected is null) return;
        ++_screenTransitionVersion; CancelPresentationAnimations();
        NormalizePresentationSurfaces(selected);
        _showingBreakScreen = !ReferenceEquals(selected, _scorebug);
        UpdatePresentationCards(_activeScreenKey);
    }

    private void RememberClosedOutput(NativeWindow output, string key, WindowLayoutStorage.WindowLayout? closingLayout = null)
    {
        var layout = closingLayout ?? WindowLayoutStorage.Capture(output);
        // Closing a never-shown/off-air window must not overwrite a useful
        // saved layout with an unmeasured zero-size backend.
        if (double.IsFinite(layout.Left) && double.IsFinite(layout.Top) && double.IsFinite(layout.Width) && double.IsFinite(layout.Height)
            && layout.Width > 0 && layout.Height > 0) WindowLayoutStorage.Set(key, layout);
        bool current = key == WindowLayoutStorage.AflDisplayKey && _sportMode == SportMode.AFL
            || key == WindowLayoutStorage.CricketDisplayKey && _sportMode == SportMode.Cricket
            || key == WindowLayoutStorage.TrainingDisplayKey && _sportMode == SportMode.Training;
        if (current) _outputActivationRequired = true;
        InvalidatePresentationWork();
        if (current) UpdateActiveDisplayLabel("");
    }
}

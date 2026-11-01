// Generated from MainWindow.ManualWeatherOwnership.cs; edit translator/adapters.
// Source SHA-256: e1fe2802c495d8224867e3423b880f454a7be1c3bfa9e35539ec82fc0859c8e5
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
using System.Threading.Tasks;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using NativeWindow = RocheScoreboard.Mac.Native.NativeWindow;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    // Per-instance controlled-operation seams. Null keeps the actual existing
    // provider and owned MessageBox paths. These do not publish or replace data.
    internal Func<WeatherService> WeatherServiceFactory { get; set; } = static () => new WeatherService();
    internal Func<string, Task<WeatherSnapshot?>>? ManualWeatherFetchAsyncOverride { get; set; }
    internal Action<string, string, bool>? ManualWeatherFeedbackOverride { get; set; }
    internal Action<Task>? ManualWeatherOperationObserved { get; set; }
    internal Action<Task>? WeatherLocationOperationObserved { get; set; }

    private long _manualWeatherIntentGeneration;
    private long _manualWeatherLocationGeneration;
    private string? _manualWeatherObservedLocation;
    private ManualWeatherRequest? _manualWeatherPendingRequest;

    private sealed record ManualWeatherRequest(
        long Intent, long LocationGeneration, string? Location, MatchManager Match,
        long ModelLifetime, int Session, int Setup, int Presentation, int Screen,
        SportMode Sport, NativeWindow? Output, ScorebugControl? Scorebug,
        bool OutputRequired, bool OutputVisible);

    private static string? NormalizeManualWeatherLocation(string? location)
        => string.IsNullOrWhiteSpace(location) ? null : location.Trim();

    private void InvalidateManualWeatherIntent()
    {
        ++_manualWeatherIntentGeneration;
        var pending = _manualWeatherPendingRequest;
        _manualWeatherPendingRequest = null;
        if (pending is null || _remoteCommandOwnerClosed) return;
        string? location = NormalizeManualWeatherLocation(_weatherLocation);
        SetWeatherLocationStatus(location is null ? "Weather disabled"
            : string.Equals(pending.Location, location, StringComparison.OrdinalIgnoreCase)
                ? "Weather overlay cancelled." : $"Weather set to {location}");
    }

    private void ObserveManualWeatherLocation(string? location)
    {
        string? normalized = NormalizeManualWeatherLocation(location);
        if (string.Equals(normalized, _manualWeatherObservedLocation, StringComparison.OrdinalIgnoreCase)) return;
        _manualWeatherObservedLocation = normalized;
        ++_manualWeatherLocationGeneration;
        InvalidateManualWeatherIntent();
    }

    private ManualWeatherRequest CaptureManualWeatherRequest()
    {
        ObserveManualWeatherLocation(_weatherLocation);
        return new(_manualWeatherIntentGeneration, _manualWeatherLocationGeneration,
            NormalizeManualWeatherLocation(_weatherLocation), _match, _match.LifecycleVersion,
            _sessionReplacementGeneration, _setupFlowGeneration, _presentationLifetimeGeneration,
            _screenTransitionVersion, _sportMode, _displayWindow, _scorebug,
            _outputActivationRequired, _displayWindow?.IsVisible == true);
    }

    private bool IsCurrentManualWeatherRequest(ManualWeatherRequest request)
        => !_remoteCommandOwnerClosed && IsVisible && _aflMatchReady
            && request.Intent == _manualWeatherIntentGeneration
            && request.LocationGeneration == _manualWeatherLocationGeneration
            && string.Equals(request.Location, NormalizeManualWeatherLocation(_weatherLocation), StringComparison.OrdinalIgnoreCase)
            && ReferenceEquals(request.Match, _match) && request.ModelLifetime == _match.LifecycleVersion
            && request.Session == _sessionReplacementGeneration && request.Setup == _setupFlowGeneration
            && request.Presentation == _presentationLifetimeGeneration && request.Screen == _screenTransitionVersion
            && request.Sport == _sportMode && _sportMode == SportMode.AFL
            && ReferenceEquals(request.Output, _displayWindow) && ReferenceEquals(request.Scorebug, _scorebug)
            && request.OutputRequired == _outputActivationRequired
            && request.OutputVisible == (_displayWindow?.IsVisible == true);

    private async Task<WeatherSnapshot?> FetchManualWeatherSnapshotAsync(string location)
    {
        var controlledFetch = ManualWeatherFetchAsyncOverride;
        if (controlledFetch is not null) return await controlledFetch(location);
        await StartOrUpdateWeatherServiceAsync(location);
        // StartOrUpdate clears replaced-venue caches and routes only the
        // current provider snapshot to the currently configured sport.
        return _scorebug?.GetWeatherSnapshot() ?? _weatherService?.LatestSnapshot;
    }

    private void ShowManualWeatherFeedback(string text, string caption, bool warning)
    {
        var controlledFeedback = ManualWeatherFeedbackOverride;
        if (controlledFeedback is not null) { controlledFeedback(text, caption, warning); return; }
        RocheScoreboard.Mac.Native.NativeMessageBox.Show(this, text, caption, NativeMessageBoxButton.OK,
            warning ? NativeMessageBoxImage.Warning : NativeMessageBoxImage.Information);
    }
}

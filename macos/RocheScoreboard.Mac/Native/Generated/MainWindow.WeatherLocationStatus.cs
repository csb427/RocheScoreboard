// Generated from MainWindow.WeatherLocationStatus.cs; edit translator/adapters.
// Source SHA-256: ca74ff070cad26f7a6cc4f6d7260ef6398cec3b6592977e7eb7766e8fb200486
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
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private long _weatherLocationStatusRequestGeneration;
    private long _weatherLocationStatusTextVersion;
    private WeatherLocationStatusOwner? _weatherLocationStatusOwner;

    private sealed record WeatherLocationStatusRequest(long Generation, SportMode Sport, string? Location,
        CricketMatchManager? CricketMatch);
    private sealed record WeatherLocationStatusOwner(WeatherLocationStatusRequest Request, WeatherService Service, long TextVersion,long? CricketRevision);

    private WeatherLocationStatusRequest CaptureWeatherLocationStatusRequest(string? location)
    {
        _weatherLocationStatusOwner = null;
        return new(++_weatherLocationStatusRequestGeneration, _sportMode, NormalizeManualWeatherLocation(location),
            _cricketMatch);
    }

    // Manual overlay feedback and explicit settings intent share this line.
    // Any newer writer retires the older background request's right to paint.
    private void SetWeatherLocationStatus(string text)
    {
        ++_weatherLocationStatusTextVersion;
        _weatherLocationStatusOwner = null;
        if (!_remoteCommandOwnerClosed) WeatherLocationStatus.Text = text;
    }

    private bool IsLatestWeatherLocationStatusRequest(WeatherLocationStatusRequest request)
        => !_remoteCommandOwnerClosed && request.Generation == _weatherLocationStatusRequestGeneration
            // Weather belongs to the retained match, not to a setup page or
            // score revision. Opening/cancelling setup must not strand Loading.
            && (request.Sport!=SportMode.Cricket || request.Sport==_sportMode && ReferenceEquals(request.CricketMatch,_cricketMatch));

    private bool IsCurrentWeatherLocationStatusRequest(WeatherLocationStatusRequest request)
        => IsLatestWeatherLocationStatusRequest(request) && request.Sport == _sportMode
            && (_sportMode == SportMode.AFL
                ?string.Equals(request.Location, NormalizeManualWeatherLocation(_weatherLocation), StringComparison.OrdinalIgnoreCase)
                :_sportMode==SportMode.Cricket && request.CricketMatch is not null
                    && CricketControlPanelView.IsWeatherVenueCurrent(request.CricketMatch,request.Location));

    private WeatherLocationStatusOwner? BeginWeatherLocationStatus(
        WeatherLocationStatusRequest request, WeatherService service, long textVersion)
    {
        if (request.Location is null || !IsCurrentWeatherLocationStatusRequest(request)
            || !ReferenceEquals(service, _weatherService)
            || !string.Equals(request.Location, _activeWeatherLocation, StringComparison.OrdinalIgnoreCase)
            || textVersion != _weatherLocationStatusTextVersion || request.Sport==SportMode.AFL && _manualWeatherPendingRequest is not null) return null;

        long? cricketRevision=null;
        if(request.Sport==SportMode.Cricket)
        {
            cricketRevision=CricketControlPanelView.BeginWeatherStatus(request.CricketMatch,request.Location,service.LatestSnapshot is not null);
            if(cricketRevision is null || !IsCurrentWeatherLocationStatusRequest(request)
                || !CricketControlPanelView.IsWeatherStatusCurrent(cricketRevision.Value,request.CricketMatch,request.Location))return null;
        }
        var owner = new WeatherLocationStatusOwner(request, service, textVersion,cricketRevision);
        _weatherLocationStatusOwner = owner;
        if(request.Sport==SportMode.Cricket)return owner;
        WeatherLocationStatus.Text = service.LatestSnapshot is null
            ? $"Loading weather for {request.Location}…"
            : $"Weather data available for {request.Location}";
        return owner;
    }

    private void CompleteWeatherLocationStatus(WeatherLocationStatusOwner? owner)
    {
        if (owner is null || !ReferenceEquals(owner, _weatherLocationStatusOwner)
            || owner.TextVersion != _weatherLocationStatusTextVersion
            || !IsCurrentWeatherLocationStatusRequest(owner.Request)
            || !ReferenceEquals(owner.Service, _weatherService)
            || !string.Equals(owner.Request.Location, _activeWeatherLocation, StringComparison.OrdinalIgnoreCase)
            || owner.Request.Sport==SportMode.AFL && _manualWeatherPendingRequest is not null) return;

        if(owner.Request.Sport==SportMode.Cricket)
        {
            if(owner.CricketRevision is long revision)CricketControlPanelView.CompleteWeatherStatus(revision,owner.Request.CricketMatch,
                owner.Request.Location!,owner.Service.LatestSnapshot is not null);
            return;
        }

        // Availability is about the current venue's data, not the score or
        // clock revision. Ordinary match changes do not invalidate these facts.
        WeatherLocationStatus.Text = owner.Service.LatestSnapshot is null
            ? $"Weather unavailable for {owner.Request.Location}. Check the location or try Update again."
            : $"Weather data available for {owner.Request.Location}";
    }

    private void DisableWeatherLocationStatus(WeatherLocationStatusRequest request, long textVersion)
    {
        // This line belongs to AFL settings. Cricket's blank venue feedback
        // stays in its own panel rather than relabelling retained AFL status.
        if (request.Sport==SportMode.AFL && request.Location is null && IsCurrentWeatherLocationStatusRequest(request)
            && textVersion == _weatherLocationStatusTextVersion)
            SetWeatherLocationStatus("Weather disabled");
    }
}

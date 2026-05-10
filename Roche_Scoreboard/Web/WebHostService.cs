using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Roche_Scoreboard.Web;

/// <summary>
/// Hosts an embedded Kestrel web server that serves the live view and
/// control panel websites, plus a SignalR hub for real-time state sync.
/// </summary>
public sealed class WebHostService : IAsyncDisposable
{
    private WebApplication? _app;
    private IHubContext<ScoreboardHub>? _hubContext;

    public int Port { get; private set; } = 5050;

    /// <summary>
    /// Last startup error, if any. Surfaced to the UI so the operator can
    /// see WHY the server failed instead of a generic "failed to start".
    /// </summary>
    public string? LastStartError { get; private set; }

    /// <summary>
    /// Starts the embedded web server on the configured port.
    /// Falls back to 5051..5059 if the preferred port is in use.
    /// </summary>
    public async Task StartAsync()
    {
        string wwwroot = FindWwwRoot();
        string contentRoot = AppContext.BaseDirectory;

        WebApplication? started = null;
        Exception? lastError = null;

        // Try a small range of ports to avoid conflicts. We catch every
        // exception so a non-bind error (e.g. missing AspNetCore assets,
        // permission denied, antivirus block) is reported instead of being
        // silently retried until the loop exits and we throw a generic
        // "could not bind" message.
        for (int port = 5050; port <= 5059; port++)
        {
            try
            {
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                {
                    // Pin the content root to the EXE folder so launching the
                    // app from a shortcut, taskbar, or any working directory
                    // resolves correctly. Without this, the host may use the
                    // shell's CWD (e.g. C:\Windows\System32) and crash before
                    // Kestrel even starts.
                    ContentRootPath = contentRoot,
                    Args = []
                });

                builder.WebHost.ConfigureKestrel(options =>
                {
                    options.ListenAnyIP(port);
                });

                builder.Services.AddSignalR();

                var app = builder.Build();

                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(wwwroot),
                    RequestPath = ""
                });

                app.MapHub<ScoreboardHub>("/hub");

                // Redirect root to the control panel — that's what an operator
                // hitting the URL on a phone wants to land on.
                app.MapGet("/", (HttpContext _) =>
                    Results.Redirect("/control.html"));

                await app.StartAsync();
                Port = port;
                started = app;
                LastStartError = null;
                break;
            }
            catch (IOException ioEx)
            {
                // Port in use — record and try the next one.
                lastError = ioEx;
            }
            catch (Exception ex)
            {
                // Non-bind failure (missing static asset, security policy,
                // single-file extraction, etc). Don't keep hammering the
                // remaining ports; surface immediately.
                lastError = ex;
                break;
            }
        }

        if (started is null)
        {
            LastStartError = lastError?.Message ?? "unknown error";
            throw new InvalidOperationException(
                $"Could not start embedded web server: {LastStartError}",
                lastError);
        }

        _app = started;
        _hubContext = _app.Services.GetRequiredService<IHubContext<ScoreboardHub>>();
    }

    /// <summary>
    /// Broadcasts the latest match state to all connected web clients.
    /// </summary>
    public async Task BroadcastStateAsync(ScoreboardState state)
    {
        ScoreboardHub.CurrentState = state;

        if (_hubContext is not null)
        {
            await _hubContext.Clients.All.SendAsync("StateUpdate", state);
        }
    }

    /// <summary>
    /// Broadcasts a captured display frame to all connected live-view clients.
    /// The frame is sent as a base64-encoded JPEG string.
    /// </summary>
    public async Task BroadcastFrameAsync(string base64Jpeg)
    {
        if (_hubContext is not null)
        {
            await _hubContext.Clients.All.SendAsync("FrameUpdate", base64Jpeg);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }
    }

    private static string FindWwwRoot()
    {
        // Check next to the executable first (published scenario)
        string exeDir = AppContext.BaseDirectory;
        string candidate = Path.Combine(exeDir, "wwwroot");
        if (Directory.Exists(candidate)) return candidate;

        // Development: walk up from the exe to find the project wwwroot
        DirectoryInfo? dir = new(exeDir);
        while (dir is not null)
        {
            candidate = Path.Combine(dir.FullName, "wwwroot");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the wwwroot folder for the embedded web server.");
    }
}

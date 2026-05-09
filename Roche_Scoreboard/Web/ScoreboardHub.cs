using Microsoft.AspNetCore.SignalR;

namespace Roche_Scoreboard.Web;

/// <summary>
/// SignalR hub that bridges the WPF scoreboard with web clients.
/// Web control panel clients call these methods; the server relays commands
/// back to the WPF thread for execution, then broadcasts updated state.
/// </summary>
public sealed class ScoreboardHub : Hub
{
    /// <summary>Raised on the WPF thread when a web client sends a command.</summary>
    public static event Action<string, string?>? CommandReceived;

    /// <summary>The latest state snapshot — set by the WPF app on every change.</summary>
    public static ScoreboardState? CurrentState { get; set; }

    public override async Task OnConnectedAsync()
    {
        // Immediately send the current state to the newly connected client
        if (CurrentState is not null)
        {
            await Clients.Caller.SendAsync("StateUpdate", CurrentState);
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Called by the web control panel to execute a scoring/clock/display command.
    /// </summary>
    public void SendCommand(string command, string? parameter = null)
    {
        CommandReceived?.Invoke(command, parameter);
    }
}

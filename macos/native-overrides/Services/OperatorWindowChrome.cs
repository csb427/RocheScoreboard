using Avalonia.Controls;
namespace Roche_Scoreboard.Services;
// Cocoa owns traffic lights, resizing, full screen and drag regions on Mac.
internal static class OperatorWindowChrome
{
    internal static void Enable(Window window) => window.SystemDecorations = SystemDecorations.Full;
}

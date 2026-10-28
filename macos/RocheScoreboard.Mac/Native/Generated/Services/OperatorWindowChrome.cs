// Generated from Services/OperatorWindowChrome.cs; native platform equivalent in macos/native-overrides.
// Source SHA-256: e3c72abad942be7b1eebc0cb8e4480d38c7ed4c7d943abe758e4684bea1b754b
using Avalonia.Controls;
namespace Roche_Scoreboard.Services;
// Cocoa owns traffic lights, resizing, full screen and drag regions on Mac.
internal static class OperatorWindowChrome
{
    internal static void Enable(Window window) => window.SystemDecorations = SystemDecorations.Full;
}

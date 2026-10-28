// Generated from Views/VideoPlayerControl.PresentationOverlay.cs; edit translator/adapters.
// Source SHA-256: 82888aef08874201ead268e9ae9e88a7ef9a14efdaae012ec216d78f74a4ff87
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
namespace Roche_Scoreboard.Views;

public partial class VideoPlayerControl
{
    // VideoView.Content uses LibVLCSharp's foreground surface. A normal WPF
    // sibling would be hidden behind the native video HWND (the airspace rule).
    internal Control? PresentationOverlay { get; private set; }
    internal void SetPresentationOverlay(Control overlay)
    { PresentationOverlay = overlay; VideoPlayer.Content = overlay; }
}

using System.Windows;
namespace Roche_Scoreboard.Views;

public partial class VideoPlayerControl
{
    // VideoView.Content uses LibVLCSharp's foreground surface. A normal WPF
    // sibling would be hidden behind the native video HWND (the airspace rule).
    internal UIElement? PresentationOverlay { get; private set; }
    internal void SetPresentationOverlay(UIElement overlay)
    { PresentationOverlay = overlay; VideoPlayer.Content = overlay; }
}

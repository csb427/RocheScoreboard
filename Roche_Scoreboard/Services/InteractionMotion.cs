using System.Windows;
namespace Roche_Scoreboard.Services;
/// <summary>One operator feedback scope per window; broadcast windows are excluded.</summary>
internal static class InteractionMotion
{
    public static void Enable(FrameworkElement root)=>OperatorFeedback.Enable(root);
    public static void Pulse(FrameworkElement target)=>OperatorFeedback.Pulse(target);
}

// Generated from Services/InteractionMotion.cs; edit translator/adapters.
// Source SHA-256: cdf8c75a1f1d184699886cc90ba308927cf2b323b4c63c0c21776df99e73540d
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
namespace Roche_Scoreboard.Services;
/// <summary>One operator feedback scope per window; broadcast windows are excluded.</summary>
internal static class InteractionMotion
{
    public static void Enable(Control root)=>OperatorFeedback.Enable(root);
    public static void Pulse(Control target)=>OperatorFeedback.Pulse(target);
}

using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace RocheScoreboard.Mac.Native;
public sealed class PopupMotion
{
    private sealed record Kind(string Value);
    private static readonly ConditionalWeakTable<Popup, Kind> Kinds = new();
    public static string GetKind(Popup popup) => Kinds.TryGetValue(popup, out var kind) ? kind.Value : "None";
    public static void SetKind(Popup popup, string value)
    {
        Kinds.Add(popup, new Kind(value));
        if (value == "None") return;
        popup.Opened += (_, _) =>
        {
            if (popup.Child is not { } child) return;
            var move = new TranslateTransform();
            var original = child.RenderTransform;
            child.RenderTransform = original is Transform t ? new TransformGroup { Children = { t, move } } : move;
            var animation = new DoubleAnimation(-8, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase(), FillBehavior = FillBehavior.Stop };
            animation.Completed += (_, _) => child.RenderTransform = original;
            move.BeginAnimation(TranslateTransform.YProperty, animation);
        };
    }
}

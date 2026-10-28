#if DEBUG
using Avalonia;
namespace Roche_Scoreboard;
public partial class MainWindow
{
    private static IEnumerable<AvaloniaObject> QaDescendants(AvaloniaObject root)
    {
        foreach(var child in RocheScoreboard.Mac.Native.LogicalTreeHelper.GetChildren(root).OfType<AvaloniaObject>())
        {yield return child;foreach(var nested in QaDescendants(child))yield return nested;}
    }
}
#endif

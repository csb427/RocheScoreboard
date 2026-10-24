using RocheScoreboard.Mac.Production;
// Generated from Views/CalibrationOverlay.xaml.cs; edit translator/adapters.
// Source SHA-256: aa8d8ee168d63b6b29879a813677cf8881cd3dac7897884eb3a68b2776a99171
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Input;
using RocheScoreboard.Mac.Native;
using System.Globalization;

namespace Roche_Scoreboard.Views;

public partial class CalibrationOverlay : Avalonia.Controls.UserControl
{
    public CalibrationOverlay()
    {
        NativePointer.EnsureInitialized(); InitializeComponent(); ProductionNames.CompleteLoad(this);
        SizeChanged += (_, _) => UpdateReadout();
    }

    private void UpdateReadout()
    {
        SizeReadout.Text = string.Format(
            CultureInfo.InvariantCulture,
            "{0:F0} x {1:F0}  ({2:F2}:1)",
            Bounds.Width,
            Bounds.Height,
            Bounds.Height > 0 ? Bounds.Width / Bounds.Height : 0);
    }
}

public partial class CalibrationOverlay
{

}

using System.Globalization;

namespace Roche_Scoreboard.Views;

public partial class CalibrationOverlay : System.Windows.Controls.UserControl
{
    public CalibrationOverlay()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateReadout();
    }

    private void UpdateReadout()
    {
        SizeReadout.Text = string.Format(
            CultureInfo.InvariantCulture,
            "{0:F0} x {1:F0}  ({2:F2}:1)",
            ActualWidth,
            ActualHeight,
            ActualHeight > 0 ? ActualWidth / ActualHeight : 0);
    }
}

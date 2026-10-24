// Generated from MainWindow.QuarterLabels.cs; edit translator/adapters.
// Source SHA-256: 598538888a9380c74407efc601418bd9ce384d16621ada797daf236b968529e7
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
using Roche_Scoreboard.Services;
using TextBox = Avalonia.Controls.TextBox;
using Button = Avalonia.Controls.Button;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private bool _suppressQuarterLabelTextChanged;
    private bool _quarterLabelHandlerHooked;

    /// <summary>
    /// Populates the live quarter-label textbox in settings from the
    /// service. Subscribes (once) to <see cref="QuarterLabelService.LabelsChanged"/>
    /// so the textbox stays in sync with the on-screen scoreboard label
    /// when quarter/break state transitions clear the override.
    /// </summary>
    private void LoadQuarterLabelsToSettings()
    {
        if (!_quarterLabelHandlerHooked)
        {
            QuarterLabelService.LabelsChanged += OnQuarterLabelServiceChanged;
            _quarterLabelHandlerHooked = true;
        }

        RefreshQuarterLabelSettingsUI();
    }

    private void OnQuarterLabelServiceChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(new System.Action(OnQuarterLabelServiceChanged));
            return;
        }
        RefreshQuarterLabelSettingsUI();
    }

    private void RefreshQuarterLabelSettingsUI()
    {
        if (QuarterLabelCurrentBox is null) return;

        _suppressQuarterLabelTextChanged = true;
        try
        {
            string? current = QuarterLabelService.CurrentOverride;
            // Show the live override when set; otherwise leave the box empty
            // and surface the active default as a hint underneath.
            QuarterLabelCurrentBox.Text = current ?? string.Empty;

            string activeDefault = GetActiveScoreboardLabel();
            Avalonia.Controls.ToolTip.SetTip(QuarterLabelCurrentBox, $"Current label on scoreboard: {activeDefault}");
            if (QuarterLabelCurrentHint is not null)
            {
                QuarterLabelCurrentHint.Text = current is null
                    ? $"Showing default \"{activeDefault}\". Type to override, max 12 characters."
                    : $"Override active. Default is \"{activeDefault}\". Reset to restore.";
            }
        }
        finally
        {
            _suppressQuarterLabelTextChanged = false;
        }
    }

    /// <summary>
    /// Returns the default centre label currently shown on the scorebug,
    /// taking break vs in-quarter state into account.
    /// </summary>
    private string GetActiveScoreboardLabel()
    {
        // The scorebug's centre label text already reflects the current state
        // (Q1..Q4 or QT/HT/3QT/FT). Read it back directly so we don't have to
        // duplicate the break/quarter detection logic here.
        if (_scorebug is not null)
        {
            try
            {
                var t = _scorebug.ExpQuarterNumberText?.Text;
                if (!string.IsNullOrWhiteSpace(t))
                {
                    // Strip any active override so the *default* shows in the hint.
                    string? ov = QuarterLabelService.CurrentOverride;
                    if (ov is null || !string.Equals(t, ov, System.StringComparison.Ordinal))
                        return t!;
                }
            }
            catch { /* ignore — fall through to quarter default */ }
        }

        return QuarterLabelService.GetQuarterLabel(_match?.Quarter ?? 1);
    }

    private void QuarterLabelCurrentBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressQuarterLabelTextChanged) return;
        if (sender is not TextBox box) return;

        QuarterLabelService.SetCurrentOverride((box.Text ?? string.Empty));
    }

    private void QuarterLabelCurrentReset_Click(object? sender, RoutedEventArgs e)
    {
        QuarterLabelService.ClearCurrentOverride();
        // RefreshQuarterLabelSettingsUI runs via LabelsChanged; nothing else to do.
    }
}

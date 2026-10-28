// Generated from Views/TrainingControlPanel.ControlCentre.cs; edit translator/adapters.
// Source SHA-256: 3a7fe3e9c4468eb4a5df2fa64167fd99db05f2a5ad1c461649752dd812a79794
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Grid = Avalonia.Controls.Grid;

namespace Roche_Scoreboard.Views;

/// <summary>Responsive operator shell; timer and output behavior remain independent.</summary>
public partial class TrainingControlPanel
{
    private bool? _controlCentreStacked;
    private bool _controlCentreWired;

    private void ControlCentre_Loaded(object? sender, RoutedEventArgs e)
    {
        if (!_controlCentreWired)
        {
            _controlCentreWired = true;
            TrainingPageViewport.SizeChanged += (_, _) => ArrangeControlCentre();
        }
        ArrangeControlCentre();
    }

    private void ControlCentre_SizeChanged(object? sender, SizeChangedEventArgs e)
        => ArrangeControlCentre();

    private void ArrangeControlCentre()
    {
        if (TrainingBodyColumns is null || TrainingViewport is null) return;
        bool stacked = Bounds.Width < 1000;
        TrainingViewport.MinimumCanvasWidth = 380;
        TrainingViewport.MinimumCanvasHeight = 0;
        TrainingViewport.NaturalHeight = false;
        if (_controlCentreStacked != stacked)
        {
            TrainingBodyColumns.RowDefinitions.Clear();
            if (stacked)
            {
                TrainingBodyColumns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                TrainingBodyColumns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            TrainingBodyColumns.ColumnDefinitions[0].Width = new GridLength(stacked ? 1 : 3, GridUnitType.Star);
            TrainingBodyColumns.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(2, GridUnitType.Star);
            TrainingBodyColumns.ColumnDefinitions[1].MinWidth = stacked ? 0 : 340;
            Grid.SetColumn(TrainingBuilderColumn, stacked ? 0 : 1);
            Grid.SetRow(TrainingBuilderColumn, stacked ? 1 : 0);
            TrainingBuilderColumn.Margin = new Thickness(stacked ? 0 : 10, stacked ? 16 : 0, 0, 0);
            TrainingActiveColumn.Margin = new Thickness(0, 0, stacked ? 0 : 10, 0);
            QueueScroller.Height = stacked ? 280 : double.NaN;
            _controlCentreStacked = stacked;
        }
        TrainingBodyColumns.Height = stacked ? double.NaN : Math.Max(260, TrainingPageViewport.NativeHeight() - 30);

        bool narrow = Bounds.Width < 720;
        TrainingSubtitle.IsVisible = TrainingTitleDivider.IsVisible = Bounds.Width < 1200 ? false : true;
        Grid.SetRow(TrainingSessionReadout, narrow ? 1 : 0);
        Grid.SetColumn(TrainingSessionReadout, narrow ? 0 : 2);
        Grid.SetColumnSpan(TrainingSessionReadout, narrow ? 5 : 1);
        TrainingSessionReadout.Margin = new Thickness(0, narrow ? 10 : 0, 18, 0);
        Grid.SetRow(TrainingCloudLauncher, Bounds.Width < 1400 ? narrow ? 2 : 1 : 0);
        Grid.SetColumn(TrainingCloudLauncher, Bounds.Width < 1400 ? 0 : 3);
        Grid.SetColumnSpan(TrainingCloudLauncher, Bounds.Width < 1400 ? 5 : 1);
        TrainingCloudLauncher.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        TrainingCloudLauncher.Margin = new Thickness(0, Bounds.Width < 1400 ? 10 : 0, 18, 0);
        TrainingQueueHint.IsVisible = Bounds.Width < 1400 ? false : true;
        Grid.SetRow(TrainingQueueActions, narrow ? 1 : 0);
        Grid.SetColumn(TrainingQueueActions, narrow ? 0 : 3);
        Grid.SetColumnSpan(TrainingQueueActions, narrow ? 4 : 1);
        TrainingQueueActions.Margin = new Thickness(narrow ? 10 : 0, 0, 8, narrow ? 8 : 0);

        // The large active timer gets its own row rather than squeezing the title.
        Grid.SetRow(ActiveRemainingText, narrow ? 1 : 0);
        Grid.SetColumn(ActiveRemainingText, narrow ? 0 : 1);
        Grid.SetColumnSpan(ActiveRemainingText, narrow ? 2 : 1);
        ActiveRemainingText.Margin = new Thickness(0, narrow ? 10 : 0, 0, 0);
        ActiveRemainingText.HorizontalAlignment = narrow ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Right;
        ActiveRemainingText.FontSize = narrow ? 64 : 84;
        ActiveTitleText.FontSize = narrow ? 28 : 42;
        TrainingViewport.RefreshViewport();
    }
}

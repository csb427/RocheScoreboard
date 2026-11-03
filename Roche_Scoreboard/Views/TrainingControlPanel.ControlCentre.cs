using System.Windows;
using System.Windows.Controls;
using Grid = System.Windows.Controls.Grid;

namespace Roche_Scoreboard.Views;

/// <summary>Responsive operator shell; timer and output behavior remain independent.</summary>
public partial class TrainingControlPanel
{
    private bool? _controlCentreStacked;
    private bool _controlCentreWired;

    private void ControlCentre_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_controlCentreWired)
        {
            _controlCentreWired = true;
            TrainingPageViewport.SizeChanged += (_, _) => ArrangeControlCentre();
        }
        ArrangeControlCentre();
    }

    private void ControlCentre_SizeChanged(object sender, SizeChangedEventArgs e)
        => ArrangeControlCentre();

    private void ArrangeControlCentre()
    {
        if (TrainingBodyColumns is null || TrainingViewport is null) return;
        bool shortWindow = ActualHeight < 500;
        bool stacked = ActualWidth < 1000 || ActualHeight < 600;
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
        TrainingBodyColumns.Height = stacked ? double.NaN : Math.Max(260, TrainingPageViewport.ActualHeight - 30);

        bool narrow = ActualWidth < 720;
        TrainingSubtitle.Visibility = TrainingTitleDivider.Visibility = Visibility.Collapsed;
        TrainingSessionReadout.Visibility = shortWindow ? Visibility.Collapsed : Visibility.Visible;
        TrainingHeaderSurface.Padding = shortWindow ? new Thickness(8,6,8,6) : new Thickness(16,12,16,12);
        TrainingTitleSurface.Padding = shortWindow ? new Thickness(8,4,8,4) : new Thickness(12,8,12,8);
        TrainingTitleText.FontSize = shortWindow ? 18 : 22;
        TrainingEntrySymbol.Width = TrainingEntrySymbol.Height = shortWindow ? 22 : 28;
        TrainingEntrySymbol.Margin = new Thickness(0,0,shortWindow ? 6 : 10,0);
        ExitButton.Padding = shortWindow ? new Thickness(8,6,8,6) : new Thickness(16,10,16,10);
        TrainingActiveSurface.Padding = shortWindow ? new Thickness(12) : new Thickness(24);
        TrainingTransportSurface.Padding = shortWindow ? new Thickness(10,8,10,0) : new Thickness(16);
        TrainingTransportSurface.Margin = new Thickness(0,shortWindow ? 8 : 12,0,0);
        ActiveStatusPill.Padding = shortWindow ? new Thickness(8,2,8,2) : new Thickness(12,4,12,4);
        ActiveStatusPill.Margin = new Thickness(0,shortWindow ? 4 : 8,0,0);
        foreach(var button in new[]{StartPauseButton,ResetActiveButton,SkipButton,AddOneMinButton,AddTenSecButton})
        {
            button.Height = shortWindow ? 36 : 46;
            button.Padding = shortWindow ? new Thickness(10,6,10,6) : ReferenceEquals(button,StartPauseButton) ? new Thickness(16,8,16,8) : new Thickness(16,10,16,10);
            button.FontSize = shortWindow ? 14 : ReferenceEquals(button,StartPauseButton) ? 18 : 14;
        }
        StartPauseButton.Width = shortWindow ? double.NaN : 152;
        Grid.SetRow(TrainingSessionReadout, narrow ? 1 : 0);
        Grid.SetColumn(TrainingSessionReadout, narrow ? 0 : 2);
        Grid.SetColumnSpan(TrainingSessionReadout, narrow ? 5 : 1);
        TrainingSessionReadout.Margin = new Thickness(0, narrow ? 10 : 0, 18, 0);
        TrainingQueueHint.Visibility = ActualWidth < 1400 ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetRow(TrainingQueueActions, narrow ? 1 : 0);
        Grid.SetColumn(TrainingQueueActions, narrow ? 0 : 3);
        Grid.SetColumnSpan(TrainingQueueActions, narrow ? 4 : 1);
        TrainingQueueActions.Margin = new Thickness(narrow ? 10 : 0, 0, 8, narrow ? 8 : 0);

        // The large active timer gets its own row rather than squeezing the title.
        Grid.SetRow(ActiveRemainingText, narrow && !shortWindow ? 1 : 0);
        Grid.SetColumn(ActiveRemainingText, narrow && !shortWindow ? 0 : 1);
        Grid.SetColumnSpan(ActiveRemainingText, narrow && !shortWindow ? 2 : 1);
        ActiveRemainingText.Margin = new Thickness(0, narrow && !shortWindow ? 10 : 0, 0, 0);
        ActiveRemainingText.HorizontalAlignment = narrow && !shortWindow ? System.Windows.HorizontalAlignment.Left : System.Windows.HorizontalAlignment.Right;
        ActiveRemainingText.FontSize = shortWindow ? 44 : narrow ? 64 : 84;
        ActiveTitleText.FontSize = shortWindow ? 20 : narrow ? 28 : 42;
        TrainingViewport.RefreshViewport();
    }
}

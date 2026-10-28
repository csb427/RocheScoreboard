using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Roche_Scoreboard.Views;

/// <summary>Explicit responsive pairing. Never guesses based on a form's column count.</summary>
public static class WizardPair
{
    public static void SettleHeaders(params FrameworkElement[] headers)
    {
        foreach (var header in headers)
        {
            header.BeginAnimation(UIElement.OpacityProperty, null); header.Opacity = 1;
            if (header.RenderTransform is System.Windows.Media.TranslateTransform shift)
            { shift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null); shift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null); shift.X = shift.Y = 0; }
        }
    }
    public static readonly DependencyProperty MinimumColumnWidthProperty = DependencyProperty.RegisterAttached(
        "MinimumColumnWidth", typeof(double), typeof(WizardPair), new PropertyMetadata(0d, Attach));
    public static double GetMinimumColumnWidth(DependencyObject target) => (double)target.GetValue(MinimumColumnWidthProperty);
    public static void SetMinimumColumnWidth(DependencyObject target, double value) => target.SetValue(MinimumColumnWidthProperty, value);
    private static readonly DependencyProperty ControllerProperty = DependencyProperty.RegisterAttached(
        "Controller", typeof(Controller), typeof(WizardPair));
    private static void Attach(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not Grid grid) return;
        var controller = (Controller?)grid.GetValue(ControllerProperty);
        if (controller is null) grid.SetValue(ControllerProperty, controller = new Controller(grid));
        controller.Schedule();
    }
    private sealed class Controller
    {
        private readonly Grid _grid;
        private GridLength[]? _columns;
        private RowDefinition[] _rows = [];
        private (FrameworkElement Element, int Column, int Row, Thickness Margin, Visibility Visibility)[] _children = [];
        private bool? _stacked;
        private bool _pending;
        public Controller(Grid grid)
        {
            _grid = grid;
            grid.Loaded += (_, _) => Schedule();
            grid.SizeChanged += (_, _) => Schedule();
        }
        public void Schedule()
        {
            if (_pending || !_grid.IsLoaded) return;
            _pending = true;
            _grid.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _pending = false;
                if (!_grid.IsLoaded) return;
                _columns ??= _grid.ColumnDefinitions.Select(c => c.Width).ToArray();
                if (_stacked is null)
                {
                    _rows = _grid.RowDefinitions.ToArray();
                    _children = _grid.Children.OfType<FrameworkElement>().Select(c => (c, Grid.GetColumn(c), Grid.GetRow(c), c.Margin, c.Visibility)).ToArray();
                }
                Adapt(GetMinimumColumnWidth(_grid));
            }));
        }
        private void Adapt(double minimum)
        {
            if (_grid.ActualWidth < 1 || _columns is not { Length: 3 }) return;
            double gap = _columns[1].IsAbsolute ? _columns[1].Value : 0;
            double threshold = minimum * 2 + gap;
            bool stacked = minimum > 0 && (_stacked == true ? _grid.ActualWidth < threshold + 16 : _grid.ActualWidth < threshold);
            if (stacked == _stacked) return;
            _stacked = stacked;
            _grid.RowDefinitions.Clear();
            if (stacked) { _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
            else foreach (var row in _rows) _grid.RowDefinitions.Add(row);
            for (int i = 0; i < 3; i++) _grid.ColumnDefinitions[i].Width = stacked ? new GridLength(i == 0 ? 1 : 0, GridUnitType.Star) : _columns[i];
            foreach (var child in _children)
            {
                Grid.SetColumn(child.Element, stacked ? 0 : child.Column); Grid.SetRow(child.Element, stacked && child.Column == 2 ? 1 : child.Row);
                child.Element.Margin = stacked && child.Column == 2 ? new Thickness(child.Margin.Left, child.Margin.Top + 14, child.Margin.Right, child.Margin.Bottom) : child.Margin;
                if (child.Column == 1) child.Element.Visibility = stacked ? Visibility.Collapsed : child.Visibility;
            }
        }
    }
}

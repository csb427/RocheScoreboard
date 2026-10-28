// Generated from Views/WizardPair.cs; edit translator/adapters.
// Source SHA-256: 8e30994fa183f81f86b38bd1cc08254e858e203af018cca57cb0cfad126c9de7
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace Roche_Scoreboard.Views;

/// <summary>Explicit responsive pairing. Never guesses based on a form's column count.</summary>
public static class WizardPair
{
    public static void SettleHeaders(params Control[] headers)
    {
        foreach (var header in headers)
        {
            header.BeginAnimation(Control.OpacityProperty, null); header.Opacity = 1;
            if (header.RenderTransform is Avalonia.Media.TranslateTransform shift)
            { shift.BeginAnimation(Avalonia.Media.TranslateTransform.XProperty, null); shift.BeginAnimation(Avalonia.Media.TranslateTransform.YProperty, null); shift.X = shift.Y = 0; }
        }
    }
    public static readonly AttachedProperty<double> MinimumColumnWidthProperty = (AttachedProperty<double>)NativeProperty.RegisterAttached("MinimumColumnWidth",typeof(double), typeof(WizardPair), new PropertyMetadata(0d, Attach));
    public static double GetMinimumColumnWidth(AvaloniaObject target) => (double)target.GetValue(MinimumColumnWidthProperty);
    public static void SetMinimumColumnWidth(AvaloniaObject target, double value) => target.SetValue(MinimumColumnWidthProperty, value);
    private static readonly AttachedProperty<Controller> ControllerProperty = (AttachedProperty<Controller>)NativeProperty.RegisterAttached("Controller",typeof(Controller), typeof(WizardPair));
    private static void Attach(AvaloniaObject target, AvaloniaPropertyChangedEventArgs args)
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
        private (Control Element, int Column, int Row, Thickness Margin, bool IsVisible)[] _children = [];
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
            Dispatcher.UIThread.InvokeAsync(DispatcherPriority.Loaded, new Action(() =>
            {
                _pending = false;
                if (!_grid.IsLoaded) return;
                _columns ??= _grid.ColumnDefinitions.Select(c => c.Width).ToArray();
                if (_stacked is null)
                {
                    _rows = _grid.RowDefinitions.ToArray();
                    _children = _grid.Children.OfType<Control>().Select(c => (c, Grid.GetColumn(c), Grid.GetRow(c), c.Margin, c.IsVisible)).ToArray();
                }
                Adapt(GetMinimumColumnWidth(_grid));
            }));
        }
        private void Adapt(double minimum)
        {
            if (_grid.NativeWidth() < 1 || _columns is not { Length: 3 }) return;
            double gap = _columns[1].IsAbsolute ? _columns[1].Value : 0;
            double threshold = minimum * 2 + gap;
            bool stacked = minimum > 0 && (_stacked == true ? _grid.NativeWidth() < threshold + 16 : _grid.NativeWidth() < threshold);
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
                if (child.Column == 1) child.Element.IsVisible = stacked ? false : child.IsVisible;
            }
        }
    }
}

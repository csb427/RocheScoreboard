using RocheScoreboard.Mac.Production;
// Generated from Views/LogoCropWindow.xaml.cs; edit translator/adapters.
// Source SHA-256: cc97acc8b085c536ed471c88822394dd310b80322cdf29529227bbfdd21c03a2
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Input;
using RocheScoreboard.Mac.Native;
using System;
using System.IO;
using Avalonia.Media.Imaging;

namespace Roche_Scoreboard.Views
{
    public partial class LogoCropWindow : NativeWindow
    {
        private bool _updating;
        private bool _dragging;
        private bool _draggingHome;
        private Avalonia.Point _dragStart;
        private double _startX;
        private double _startY;

        public string? HomeLogoPath { get; private set; }
        public string? AwayLogoPath { get; private set; }

        public double HomeZoom => HomeZoomSlider.Value;
        public double HomeOffsetX => HomeXSlider.Value;
        public double HomeOffsetY => HomeYSlider.Value;
        public double AwayZoom => AwayZoomSlider.Value;
        public double AwayOffsetX => AwayXSlider.Value;
        public double AwayOffsetY => AwayYSlider.Value;

        public LogoCropWindow(
            string? homeLogoPath,
            string? awayLogoPath,
            double homeZoom,
            double homeOffsetX,
            double homeOffsetY,
            double awayZoom,
            double awayOffsetX,
            double awayOffsetY)
        {
            NativePointer.EnsureInitialized(); InitializeComponent(); ProductionNames.CompleteLoad(this);

            HomeLogoPath = homeLogoPath;
            AwayLogoPath = awayLogoPath;

            SetHomeSource(LoadImage(HomeLogoPath));
            SetAwaySource(LoadImage(AwayLogoPath));

            _updating = true;
            HomeZoomSlider.Value = Clamp(homeZoom, HomeZoomSlider.Minimum, HomeZoomSlider.Maximum);
            HomeXSlider.Value = Clamp(homeOffsetX, HomeXSlider.Minimum, HomeXSlider.Maximum);
            HomeYSlider.Value = Clamp(homeOffsetY, HomeYSlider.Minimum, HomeYSlider.Maximum);
            AwayZoomSlider.Value = Clamp(awayZoom, AwayZoomSlider.Minimum, AwayZoomSlider.Maximum);
            AwayXSlider.Value = Clamp(awayOffsetX, AwayXSlider.Minimum, AwayXSlider.Maximum);
            AwayYSlider.Value = Clamp(awayOffsetY, AwayYSlider.Minimum, AwayYSlider.Maximum);
            _updating = false;

            UpdateTransforms();
        }

        private void SetHomeSource(IImage? source)
        {
            HomeWideImage.Source = source;
            HomeCropImage.Source = source;
            HomeEmptyHint.IsVisible = source is null ? true : false;
            HomeLogoPathText.Text = string.IsNullOrWhiteSpace(HomeLogoPath) ? "No file selected" : HomeLogoPath;
        }

        private void SetAwaySource(IImage? source)
        {
            AwayWideImage.Source = source;
            AwayCropImage.Source = source;
            AwayEmptyHint.IsVisible = source is null ? true : false;
            AwayLogoPathText.Text = string.IsNullOrWhiteSpace(AwayLogoPath) ? "No file selected" : AwayLogoPath;
        }

        private static IImage? LoadImage(string? path)
            => Services.ImageLoadHelper.Load(path);

        private static double Clamp(double value, double min, double max)
            => Math.Max(min, Math.Min(max, value));

        private static void ApplyTransform(Avalonia.Controls.Image image, double zoom, double x, double y)
        {
            image.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            TransformGroup group = new();
            group.Children.Add(new ScaleTransform(zoom, zoom));
            group.Children.Add(new TranslateTransform(x, y));
            image.RenderTransform = group;
        }

        private void UpdateTransforms()
        {
            ApplyTransform(HomeWideImage, HomeZoomSlider.Value, HomeXSlider.Value, HomeYSlider.Value);
            ApplyTransform(HomeCropImage, HomeZoomSlider.Value, HomeXSlider.Value, HomeYSlider.Value);
            ApplyTransform(AwayWideImage, AwayZoomSlider.Value, AwayXSlider.Value, AwayYSlider.Value);
            ApplyTransform(AwayCropImage, AwayZoomSlider.Value, AwayXSlider.Value, AwayYSlider.Value);
        }

        private void Slider_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            UpdateTransforms();
        }

        private void LoadHomeLogo_Click(object? sender, RoutedEventArgs e)
        {
            string? selectedPath = SelectLogoFile(HomeLogoPath);
            if (selectedPath is null)
            {
                return;
            }

            IImage? image = LoadImage(selectedPath);
            if (image is null)
            {
                RocheScoreboard.Mac.Native.NativeMessageBox.Show(this, "The selected image could not be loaded.", "Logo Crop Studio", RocheScoreboard.Mac.Native.NativeMessageBoxButton.OK, RocheScoreboard.Mac.Native.NativeMessageBoxImage.Warning);
                return;
            }

            HomeLogoPath = selectedPath;
            SetHomeSource(image);
        }

        private void LoadAwayLogo_Click(object? sender, RoutedEventArgs e)
        {
            string? selectedPath = SelectLogoFile(AwayLogoPath);
            if (selectedPath is null)
            {
                return;
            }

            IImage? image = LoadImage(selectedPath);
            if (image is null)
            {
                RocheScoreboard.Mac.Native.NativeMessageBox.Show(this, "The selected image could not be loaded.", "Logo Crop Studio", RocheScoreboard.Mac.Native.NativeMessageBoxButton.OK, RocheScoreboard.Mac.Native.NativeMessageBoxImage.Warning);
                return;
            }

            AwayLogoPath = selectedPath;
            SetAwaySource(image);
        }

        private static string? SelectLogoFile(string? initialPath)
        {
            NativeOpenFileDialog picker = new()
            {
                Title = "Select Logo Image",
                Filter = Services.ImageLoadHelper.LogoFilter,
                CheckFileExists = true,
                Multiselect = false
            };

            if (!string.IsNullOrWhiteSpace(initialPath) && File.Exists(initialPath))
            {
                picker.InitialDirectory = Path.GetDirectoryName(initialPath) ?? string.Empty;
                picker.FileName = Path.GetFileName(initialPath);
            }

            return picker.ShowDialog() == true ? picker.FileName : null;
        }

        private void Preview_MouseLeftButtonDown(object? sender, Avalonia.Input.PointerEventArgs e)
        {
            if (sender is not Avalonia.Controls.Grid area)
            {
                return;
            }

            _dragging = true;
            _draggingHome = area == HomeWideArea;
            _dragStart = e.GetPosition(area);
            _startX = _draggingHome ? HomeXSlider.Value : AwayXSlider.Value;
            _startY = _draggingHome ? HomeYSlider.Value : AwayYSlider.Value;
            NativePointer.Capture(area);
        }

        private void Preview_MouseMove(object? sender, Avalonia.Input.PointerEventArgs e)
        {
            if (!_dragging || sender is not Avalonia.Controls.Grid area)
            {
                return;
            }

            Avalonia.Point current = e.GetPosition(area);
            double dx = current.X - _dragStart.X;
            double dy = current.Y - _dragStart.Y;

            if (_draggingHome)
            {
                HomeXSlider.Value = Clamp(_startX + dx, HomeXSlider.Minimum, HomeXSlider.Maximum);
                HomeYSlider.Value = Clamp(_startY + dy, HomeYSlider.Minimum, HomeYSlider.Maximum);
            }
            else
            {
                AwayXSlider.Value = Clamp(_startX + dx, AwayXSlider.Minimum, AwayXSlider.Maximum);
                AwayYSlider.Value = Clamp(_startY + dy, AwayYSlider.Minimum, AwayYSlider.Maximum);
            }
        }

        private void Preview_MouseLeftButtonUp(object? sender, Avalonia.Input.PointerEventArgs e)
        {
            if (!_dragging || sender is not Avalonia.Controls.Grid area)
            {
                return;
            }

            _dragging = false;
            NativePointer.Release(area);
        }

        private void Preview_MouseLeave(object? sender, Avalonia.Input.PointerEventArgs e)
        {
            if (!_dragging || sender is not Avalonia.Controls.Grid area)
            {
                return;
            }

            _dragging = false;
            NativePointer.Release(area);
        }

        private void ResetHome_Click(object? sender, RoutedEventArgs e)
        {
            HomeZoomSlider.Value = 1.0;
            HomeXSlider.Value = 0;
            HomeYSlider.Value = 0;
        }

        private void ResetAway_Click(object? sender, RoutedEventArgs e)
        {
            AwayZoomSlider.Value = 1.0;
            AwayXSlider.Value = 0;
            AwayYSlider.Value = 0;
        }

        private void Apply_Click(object? sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

namespace Roche_Scoreboard.Views
{
public partial class LogoCropWindow
{
    private void Native_Preview_MouseLeave(object? sender, PointerEventArgs e) { Preview_MouseLeave(sender!, e); }
    private void Native_Preview_MouseLeftButtonDown(object? sender, PointerPressedEventArgs e) { Preview_MouseLeftButtonDown(sender!, e); }
    private void Native_Preview_MouseLeftButtonUp(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; Preview_MouseLeftButtonUp(sender!, e); }
    private void Native_Preview_MouseMove(object? sender, PointerEventArgs e) { Preview_MouseMove(sender!, e); }
}
}

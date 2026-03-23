using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Roche_Scoreboard.Views
{
    public partial class LogoCropWindow : Window
    {
        private bool _updating;
        private bool _dragging;
        private bool _draggingHome;
        private System.Windows.Point _dragStart;
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
            InitializeComponent();

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

        private void SetHomeSource(ImageSource? source)
        {
            HomeWideImage.Source = source;
            HomeCropImage.Source = source;
            HomeEmptyHint.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
            HomeLogoPathText.Text = string.IsNullOrWhiteSpace(HomeLogoPath) ? "No file selected" : HomeLogoPath;
        }

        private void SetAwaySource(ImageSource? source)
        {
            AwayWideImage.Source = source;
            AwayCropImage.Source = source;
            AwayEmptyHint.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
            AwayLogoPathText.Text = string.IsNullOrWhiteSpace(AwayLogoPath) ? "No file selected" : AwayLogoPath;
        }

        private static ImageSource? LoadImage(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                BitmapImage bitmap = new();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private static double Clamp(double value, double min, double max)
            => Math.Max(min, Math.Min(max, value));

        private static void ApplyTransform(System.Windows.Controls.Image image, double zoom, double x, double y)
        {
            image.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
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

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updating)
            {
                return;
            }

            UpdateTransforms();
        }

        private void LoadHomeLogo_Click(object sender, RoutedEventArgs e)
        {
            string? selectedPath = SelectLogoFile(HomeLogoPath);
            if (selectedPath is null)
            {
                return;
            }

            ImageSource? image = LoadImage(selectedPath);
            if (image is null)
            {
                System.Windows.MessageBox.Show(this, "The selected image could not be loaded.", "Logo Crop Studio", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            HomeLogoPath = selectedPath;
            SetHomeSource(image);
        }

        private void LoadAwayLogo_Click(object sender, RoutedEventArgs e)
        {
            string? selectedPath = SelectLogoFile(AwayLogoPath);
            if (selectedPath is null)
            {
                return;
            }

            ImageSource? image = LoadImage(selectedPath);
            if (image is null)
            {
                System.Windows.MessageBox.Show(this, "The selected image could not be loaded.", "Logo Crop Studio", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            AwayLogoPath = selectedPath;
            SetAwaySource(image);
        }

        private static string? SelectLogoFile(string? initialPath)
        {
            Microsoft.Win32.OpenFileDialog picker = new()
            {
                Title = "Select Logo Image",
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All Files|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (!string.IsNullOrWhiteSpace(initialPath) && File.Exists(initialPath))
            {
                picker.InitialDirectory = Path.GetDirectoryName(initialPath);
                picker.FileName = Path.GetFileName(initialPath);
            }

            return picker.ShowDialog() == true ? picker.FileName : null;
        }

        private void Preview_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Controls.Grid area)
            {
                return;
            }

            _dragging = true;
            _draggingHome = area == HomeWideArea;
            _dragStart = e.GetPosition(area);
            _startX = _draggingHome ? HomeXSlider.Value : AwayXSlider.Value;
            _startY = _draggingHome ? HomeYSlider.Value : AwayYSlider.Value;
            area.CaptureMouse();
        }

        private void Preview_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_dragging || sender is not System.Windows.Controls.Grid area)
            {
                return;
            }

            System.Windows.Point current = e.GetPosition(area);
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

        private void Preview_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_dragging || sender is not System.Windows.Controls.Grid area)
            {
                return;
            }

            _dragging = false;
            area.ReleaseMouseCapture();
        }

        private void Preview_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_dragging || sender is not System.Windows.Controls.Grid area)
            {
                return;
            }

            _dragging = false;
            area.ReleaseMouseCapture();
        }

        private void ResetHome_Click(object sender, RoutedEventArgs e)
        {
            HomeZoomSlider.Value = 1.0;
            HomeXSlider.Value = 0;
            HomeYSlider.Value = 0;
        }

        private void ResetAway_Click(object sender, RoutedEventArgs e)
        {
            AwayZoomSlider.Value = 1.0;
            AwayXSlider.Value = 0;
            AwayYSlider.Value = 0;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

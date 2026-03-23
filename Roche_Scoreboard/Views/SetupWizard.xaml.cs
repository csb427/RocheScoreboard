using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using WinForms = System.Windows.Forms;
using DrawingColor = System.Drawing.Color;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using TextBox = System.Windows.Controls.TextBox;
using Border = System.Windows.Controls.Border;

namespace Roche_Scoreboard.Views
{
    public partial class SetupWizard : System.Windows.Controls.UserControl
    {
        private List<TeamPreset> _presets;
        private string? _homeLogoPath;
        private string? _awayLogoPath;
        private string? _homeVideoPath;
        private string? _awayVideoPath;
        private int _currentStep = 1;

        // Track selected preset for delete
        private TeamPreset? _selectedHomePreset;
        private TeamPreset? _selectedAwayPreset;

        // Drag-and-drop reorder state
        private Border? _dragTile;
        private System.Windows.Point _dragStartPos;
        private int _dragOrigIndex;
        private int _dragCurrentIndex;
        private bool _isDragging;
        private bool _dragIsHome;
        private double[] _slotCentersX = [];
        private double[] _slotCentersY = [];
        private const double DragThreshold = 6;

        public SetupResult? Result { get; private set; }
        public event Action<SetupResult>? SetupCompleted;

        public SetupWizard()
        {
            InitializeComponent();
            _presets = PresetStorage.LoadAll();
            RefreshPresetPanels();
        }

        public void Reset()
        {
            Result = null;
            _homeLogoPath = null;
            _awayLogoPath = null;
            _homeVideoPath = null;
            _awayVideoPath = null;
            _currentStep = 1;

            SetupHomeNameBox.Text = "";
            SetupHomeAbbrBox.Text = "";
            SetupHomeColorBox.Text = "#0A2A6A";
            SetupHomeSecondaryBox.Text = "#FFFFFF";
            TrySetPreview(SetupHomeColorPreview, "#0A2A6A");
            TrySetPreview(SetupHomeSecondaryPreview, "#FFFFFF");
            SetupHomeLogoText.Text = "(none)";
            SetupHomeLogoImage.Source = null;
            SetupHomeLogoPreview.Visibility = Visibility.Collapsed;
            SetupHomeVideoText.Text = "(none)";

            SetupAwayNameBox.Text = "";
            SetupAwayAbbrBox.Text = "";
            SetupAwayColorBox.Text = "#7A1A1A";
            SetupAwaySecondaryBox.Text = "#FFFFFF";
            TrySetPreview(SetupAwayColorPreview, "#7A1A1A");
            TrySetPreview(SetupAwaySecondaryPreview, "#FFFFFF");
            SetupAwayLogoText.Text = "(none)";
            SetupAwayLogoImage.Source = null;
            SetupAwayLogoPreview.Visibility = Visibility.Collapsed;
            SetupAwayVideoText.Text = "(none)";

            SetupCountUp.IsChecked = true;
            SetupQuarterMinutes.Text = "20";
            SetupQuarterSeconds.Text = "00";

            SetupMessageList.Items.Clear();

            _presets = PresetStorage.LoadAll();
            RefreshPresetPanels();
            ShowStep(1);
        }

        // ----------------------------
        // Step navigation
        // ----------------------------
        private static readonly SolidColorBrush AccentBrush = new((Color)ColorConverter.ConvertFromString("#4F8EFF"));
        private static readonly SolidColorBrush Surface2Brush = new((Color)ColorConverter.ConvertFromString("#1A2332"));
        private static readonly SolidColorBrush TextMutedBrush = new((Color)ColorConverter.ConvertFromString("#5C6F85"));
        private static readonly SolidColorBrush WhiteBrush = new(Colors.White);
        private static readonly SolidColorBrush EdgeBrush = new((Color)ColorConverter.ConvertFromString("#2E3E54"));

        private const int TotalSteps = 4;

        private void ShowStep(int step)
        {
            _currentStep = step;

            var panels = new FrameworkElement[] { Step1Panel, Step2Panel, Step3Panel, Step4Panel };
            var activePanel = panels[step - 1];

            foreach (var p in panels)
            {
                if (p == activePanel)
                {
                    p.Visibility = Visibility.Visible;
                    p.Opacity = 0;
                    p.RenderTransform = new TranslateTransform(30, 0);
                }
                else
                {
                    p.Visibility = Visibility.Collapsed;
                    p.Opacity = 1;
                    p.RenderTransform = null;
                }
            }

            var sb = new System.Windows.Media.Animation.Storyboard();
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            System.Windows.Media.Animation.Storyboard.SetTarget(fadeIn, activePanel);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
            sb.Children.Add(fadeIn);

            var slideIn = new System.Windows.Media.Animation.DoubleAnimation(30, 0, TimeSpan.FromSeconds(0.22))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            System.Windows.Media.Animation.Storyboard.SetTarget(slideIn, activePanel);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(slideIn, new PropertyPath("RenderTransform.(TranslateTransform.X)"));
            sb.Children.Add(slideIn);

            sb.Begin();

            BackButton.Visibility = step > 1 ? Visibility.Visible : Visibility.Collapsed;
            NextButton.Content = step == TotalSteps ? "Start Match \u2192" : "Next \u2192";

            UpdateStepDot(Step1Dot, Step1Label, step >= 1, step == 1);
            UpdateStepDot(Step2Dot, Step2Label, step >= 2, step == 2);
            UpdateStepDot(Step3Dot, Step3Label, step >= 3, step == 3);
            UpdateStepDot(Step4Dot, Step4Label, step >= 4, step == 4);

            StepSubtitle.Text = step switch
            {
                1 => "Select your teams",
                2 => "Team colours, logos and videos",
                3 => "Set the game clock",
                4 => "Add scrolling messages",
                _ => ""
            };

            UpdatePreview();
        }

        private void UpdateStepDot(Border dot, TextBlock label, bool reached, bool active)
        {
            dot.Background = reached ? AccentBrush : Surface2Brush;
            dot.BorderBrush = reached ? AccentBrush : EdgeBrush;
            var dotText = (TextBlock)dot.Child;
            dotText.Foreground = reached ? WhiteBrush : TextMutedBrush;
            label.Foreground = active ? WhiteBrush : (reached ? AccentBrush : TextMutedBrush);
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep < TotalSteps)
                ShowStep(_currentStep + 1);
            else
                FinishWizard();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 1)
                ShowStep(_currentStep - 1);
        }

        private void GoToStep1_Click(object sender, MouseButtonEventArgs e) => ShowStep(1);
        private void GoToStep2_Click(object sender, MouseButtonEventArgs e) => ShowStep(2);
        private void GoToStep3_Click(object sender, MouseButtonEventArgs e) => ShowStep(3);
        private void GoToStep4_Click(object sender, MouseButtonEventArgs e) => ShowStep(4);

        // ----------------------------
        // Finish wizard
        // ----------------------------
        private void FinishWizard()
        {
            int.TryParse(SetupQuarterMinutes.Text, out int mins);
            int.TryParse(SetupQuarterSeconds.Text, out int secs);

            var messages = new List<string>();
            foreach (var item in SetupMessageList.Items)
            {
                string? s = item?.ToString();
                if (!string.IsNullOrWhiteSpace(s))
                    messages.Add(s);
            }

            string homeName = SetupHomeNameBox.Text.Trim();
            string homeAbbr = SetupHomeAbbrBox.Text.Trim();
            string awayName = SetupAwayNameBox.Text.Trim();
            string awayAbbr = SetupAwayAbbrBox.Text.Trim();

            Result = new SetupResult
            {
                HomeName = string.IsNullOrWhiteSpace(homeName) ? "Home Team" : homeName,
                HomeAbbr = string.IsNullOrWhiteSpace(homeAbbr) ? "HOM" : homeAbbr,
                HomePrimaryColor = SetupHomeColorBox.Text.Trim(),
                HomeSecondaryColor = SetupHomeSecondaryBox.Text.Trim(),
                HomeLogoPath = _homeLogoPath,
                HomeGoalVideoPath = _homeVideoPath,

                AwayName = string.IsNullOrWhiteSpace(awayName) ? "Away Team" : awayName,
                AwayAbbr = string.IsNullOrWhiteSpace(awayAbbr) ? "AWA" : awayAbbr,
                AwayPrimaryColor = SetupAwayColorBox.Text.Trim(),
                AwaySecondaryColor = SetupAwaySecondaryBox.Text.Trim(),
                AwayLogoPath = _awayLogoPath,
                AwayGoalVideoPath = _awayVideoPath,

                ClockMode = SetupCountdown.IsChecked == true ? ClockMode.Countdown : ClockMode.CountUp,
                QuarterMinutes = mins > 0 ? mins : 20,
                QuarterSeconds = secs >= 0 ? secs : 0,

                Messages = messages
            };

            SetupCompleted?.Invoke(Result);
        }

        // ----------------------------
        // Clock mode toggle
        // ----------------------------
        private void SetupClockMode_Changed(object sender, RoutedEventArgs e)
        {
            if (SetupDurationPanel != null)
                SetupDurationPanel.Visibility = SetupCountdown.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        // ----------------------------
        // Colour pickers
        // ----------------------------
        private void PickSetupHomeColor_Click(object sender, MouseButtonEventArgs e) => PickColor(SetupHomeColorBox, SetupHomeColorPreview);
        private void PickSetupHomeSecondary_Click(object sender, MouseButtonEventArgs e) => PickColor(SetupHomeSecondaryBox, SetupHomeSecondaryPreview);
        private void PickSetupAwayColor_Click(object sender, MouseButtonEventArgs e) => PickColor(SetupAwayColorBox, SetupAwayColorPreview);
        private void PickSetupAwaySecondary_Click(object sender, MouseButtonEventArgs e) => PickColor(SetupAwaySecondaryBox, SetupAwaySecondaryPreview);

        private void DropperSetupHomeColor_Click(object sender, MouseButtonEventArgs e) => DropColor(SetupHomeColorBox, SetupHomeColorPreview);
        private void DropperSetupHomeSecondary_Click(object sender, MouseButtonEventArgs e) => DropColor(SetupHomeSecondaryBox, SetupHomeSecondaryPreview);
        private void DropperSetupAwayColor_Click(object sender, MouseButtonEventArgs e) => DropColor(SetupAwayColorBox, SetupAwayColorPreview);
        private void DropperSetupAwaySecondary_Click(object sender, MouseButtonEventArgs e) => DropColor(SetupAwaySecondaryBox, SetupAwaySecondaryPreview);

        private static void PickColor(TextBox box, Border preview)
        {
            var dlg = new WinForms.ColorDialog { FullOpen = true, AnyColor = true };
            try
            {
                var wpf = (Color)ColorConverter.ConvertFromString(box.Text);
                dlg.Color = DrawingColor.FromArgb(wpf.R, wpf.G, wpf.B);
            }
            catch { /* ignore */ }

            if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
            var c = dlg.Color;
            string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            box.Text = hex;
            TrySetPreview(preview, hex);
        }

        private static void DropColor(TextBox box, Border preview)
        {
            var picked = EyeDropper.Pick();
            if (picked == null) return;
            var c = picked.Value;
            string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            box.Text = hex;
            TrySetPreview(preview, hex);
        }

        // ----------------------------
        // Logo pickers
        // ----------------------------
        private void ChooseSetupHomeLogo_Click(object sender, RoutedEventArgs e)
        {
            var path = PickLogoFile("Home");
            if (path == null) return;
            _homeLogoPath = path;
            SetupHomeLogoText.Text = System.IO.Path.GetFileName(path);
            ShowLogoPreview(SetupHomeLogoImage, SetupHomeLogoPreview, path);
        }

        private void ChooseSetupAwayLogo_Click(object sender, RoutedEventArgs e)
        {
            var path = PickLogoFile("Away");
            if (path == null) return;
            _awayLogoPath = path;
            SetupAwayLogoText.Text = System.IO.Path.GetFileName(path);
            ShowLogoPreview(SetupAwayLogoImage, SetupAwayLogoPreview, path);
        }

        private static string? PickLogoFile(string teamLabel)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"Select {teamLabel} Team Logo",
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All Files|*.*"
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        private static void ShowLogoPreview(System.Windows.Controls.Image image, Border container, string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            {
                image.Source = null;
                container.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.DecodePixelHeight = 160;
                bitmap.EndInit();
                bitmap.Freeze();
                image.Source = bitmap;
                container.Visibility = Visibility.Visible;
            }
            catch
            {
                image.Source = null;
                container.Visibility = Visibility.Collapsed;
            }
        }

        // ----------------------------
        // Goal video pickers
        // ----------------------------
        private void ChooseSetupHomeVideo_Click(object sender, RoutedEventArgs e)
        {
            var path = PickVideoFile("Home");
            if (path == null) return;
            _homeVideoPath = path;
            SetupHomeVideoText.Text = System.IO.Path.GetFileName(path);
        }

        private void ChooseSetupAwayVideo_Click(object sender, RoutedEventArgs e)
        {
            var path = PickVideoFile("Away");
            if (path == null) return;
            _awayVideoPath = path;
            SetupAwayVideoText.Text = System.IO.Path.GetFileName(path);
        }

        private static string? PickVideoFile(string teamLabel)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"Select {teamLabel} Goal Animation Video",
                Filter = "Video Files|*.mp4;*.wmv;*.avi;*.mov|All Files|*.*"
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        // ----------------------------
        // Messages
        // ----------------------------
        private void SetupAddMessage_Click(object sender, RoutedEventArgs e)
        {
            string? text = SetupNewMessageBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;
            SetupMessageList.Items.Add(text);
            SetupNewMessageBox.Text = "";
        }

        private void SetupRemoveMessage_Click(object sender, RoutedEventArgs e)
        {
            if (SetupMessageList.SelectedItem != null)
                SetupMessageList.Items.Remove(SetupMessageList.SelectedItem);
        }

        // ----------------------------
        // Presets
        // ----------------------------
        private void RefreshPresetPanels()
        {
            BuildPresetTiles(HomePresetPanel, HomePresetEmpty, true);
            BuildPresetTiles(AwayPresetPanel, AwayPresetEmpty, false);
        }

        private void BuildPresetTiles(System.Windows.Controls.WrapPanel panel, TextBlock emptyLabel, bool isHome)
        {
            panel.Children.Clear();

            if (_presets.Count == 0)
            {
                emptyLabel.Visibility = Visibility.Visible;
                return;
            }

            emptyLabel.Visibility = Visibility.Collapsed;

            for (int idx = 0; idx < _presets.Count; idx++)
            {
                TeamPreset preset = _presets[idx];
                Color bgColor = SafeParseColor(preset.PrimaryColor, "#333");
                Color fgColor = SafeParseColor(preset.SecondaryColor, "#FFF");

                bool isSelected = isHome
                    ? _selectedHomePreset?.PresetName == preset.PresetName
                    : _selectedAwayPreset?.PresetName == preset.PresetName;

                TextBlock abbrLabel = new()
                {
                    Text = preset.Abbreviation.ToUpperInvariant(),
                    Foreground = new SolidColorBrush(fgColor),
                    FontSize = 14,
                    FontWeight = FontWeights.Black,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    IsHitTestVisible = false
                };

                TextBlock nameLabel = new()
                {
                    Text = preset.TeamName,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xBB, fgColor.R, fgColor.G, fgColor.B)),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    IsHitTestVisible = false
                };

                StackPanel stack = new()
                {
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    IsHitTestVisible = false
                };
                stack.Children.Add(abbrLabel);
                stack.Children.Add(nameLabel);

                Grid tileContent = new() { IsHitTestVisible = false };
                tileContent.Children.Add(stack);

                if (isSelected)
                {
                    Border overlay = new()
                    {
                        Background = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00)),
                        IsHitTestVisible = false
                    };
                    tileContent.Children.Add(overlay);

                    TextBlock tickText = new()
                    {
                        Text = "✓",
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 18,
                        FontWeight = FontWeights.Black,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center,
                        IsHitTestVisible = false,
                        RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                        RenderTransform = new ScaleTransform(0, 0)
                    };
                    tileContent.Children.Add(tickText);

                    // Animate tick popping in
                    var scaleEase = new ElasticEase { Oscillations = 1, Springiness = 5, EasingMode = EasingMode.EaseOut };
                    var scaleIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)) { EasingFunction = scaleEase };
                    ((ScaleTransform)tickText.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
                    ((ScaleTransform)tickText.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn.Clone());

                    // Fade in the overlay
                    overlay.Opacity = 0;
                    overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                }

                Border tile = new()
                {
                    Background = new SolidColorBrush(bgColor),
                    Width = 90,
                    Height = 56,
                    Padding = new Thickness(4),
                    Margin = new Thickness(0, 0, 6, 6),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = preset.TeamName,
                    Child = tileContent,
                    Tag = preset,
                    BorderBrush = isSelected
                        ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E))
                        : new SolidColorBrush(Color.FromArgb(0x40, bgColor.R, bgColor.G, bgColor.B)),
                    BorderThickness = isSelected ? new Thickness(2) : new Thickness(1),
                    RenderTransform = new TranslateTransform(0, 0)
                };

                tile.MouseLeftButtonDown += (_, me) =>
                {
                    if (_isDragging) return;

                    _dragStartPos = me.GetPosition(panel);
                    _dragTile = tile;
                    _dragOrigIndex = panel.Children.IndexOf(tile);
                    _dragCurrentIndex = _dragOrigIndex;
                    _isDragging = false;
                    _dragIsHome = isHome;

                    // Snapshot layout position of each tile (independent of RenderTransform)
                    int count = panel.Children.Count;
                    _slotCentersX = new double[count];
                    _slotCentersY = new double[count];
                    for (int i = 0; i < count; i++)
                    {
                        if (panel.Children[i] is FrameworkElement fe)
                        {
                            Vector offset = VisualTreeHelper.GetOffset(fe);
                            _slotCentersX[i] = offset.X + fe.ActualWidth / 2;
                            _slotCentersY[i] = offset.Y + fe.ActualHeight / 2;
                        }
                    }

                    tile.CaptureMouse();
                    me.Handled = true;
                };

                tile.MouseMove += (_, me) =>
                {
                    if (_dragTile != tile || !tile.IsMouseCaptured) return;

                    System.Windows.Point pos = me.GetPosition(panel);
                    double dx = pos.X - _dragStartPos.X;
                    double dy = pos.Y - _dragStartPos.Y;

                    if (!_isDragging)
                    {
                        if (Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold) return;
                        _isDragging = true;
                        System.Windows.Controls.Panel.SetZIndex(tile, 100);
                        tile.Opacity = 0.85;
                    }

                    // Move dragged tile directly
                    if (tile.RenderTransform is TranslateTransform tt)
                    {
                        tt.BeginAnimation(TranslateTransform.XProperty, null);
                        tt.BeginAnimation(TranslateTransform.YProperty, null);
                        tt.X = dx;
                        tt.Y = dy;
                    }

                    // Find closest slot using 2D distance (handles row wrapping)
                    double dragCenterX = _slotCentersX[_dragOrigIndex] + dx;
                    double dragCenterY = _slotCentersY[_dragOrigIndex] + dy;
                    int newIndex = _dragOrigIndex;
                    double bestDist = double.MaxValue;
                    for (int i = 0; i < _slotCentersX.Length; i++)
                    {
                        double ddx = dragCenterX - _slotCentersX[i];
                        double ddy = dragCenterY - _slotCentersY[i];
                        double d = ddx * ddx + ddy * ddy;
                        if (d < bestDist) { bestDist = d; newIndex = i; }
                    }

                    if (newIndex != _dragCurrentIndex)
                    {
                        _dragCurrentIndex = newIndex;
                        ShiftTilesForDrag(panel, _dragOrigIndex, _dragCurrentIndex, tile);
                    }
                };

                tile.MouseLeftButtonUp += (_, me) =>
                {
                    if (_dragTile != tile) return;
                    CompleteDrag(panel, isHome, preset);
                    me.Handled = true;
                };

                tile.LostMouseCapture += (_, _) =>
                {
                    if (_dragTile != tile) return;
                    CompleteDrag(panel, isHome, preset);
                };

                tile.MouseEnter += (_, _) => { if (!_isDragging) tile.Opacity = 0.8; };
                tile.MouseLeave += (_, _) => { if (!_isDragging) tile.Opacity = 1.0; };

                panel.Children.Add(tile);
            }
        }

        private void CompleteDrag(System.Windows.Controls.WrapPanel panel, bool isHome, TeamPreset clickedPreset)
        {
            Border? tile = _dragTile;
            bool wasDragging = _isDragging;

            // Release capture first
            if (tile?.IsMouseCaptured == true)
                tile.ReleaseMouseCapture();

            if (wasDragging)
            {
                if (_dragOrigIndex != _dragCurrentIndex)
                {
                    TeamPreset moved = _presets[_dragOrigIndex];
                    _presets.RemoveAt(_dragOrigIndex);
                    _presets.Insert(_dragCurrentIndex, moved);
                    PresetStorage.SaveAll(_presets);
                }

                ResetAllTileTransforms(panel);
                _dragTile = null;
                _isDragging = false;
                RefreshPresetPanels();
            }
            else
            {
                _dragTile = null;
                _isDragging = false;

                if (isHome)
                    ApplyPresetToHome(clickedPreset);
                else
                    ApplyPresetToAway(clickedPreset);
            }
        }

        private static void ResetAllTileTransforms(System.Windows.Controls.WrapPanel panel)
        {
            foreach (UIElement child in panel.Children)
            {
                if (child is Border b && b.RenderTransform is TranslateTransform t)
                {
                    t.BeginAnimation(TranslateTransform.XProperty, null);
                    t.BeginAnimation(TranslateTransform.YProperty, null);
                    t.X = 0;
                    t.Y = 0;
                }
                System.Windows.Controls.Panel.SetZIndex(child, 0);
                child.Opacity = 1;
            }
        }

        /// <summary>
        /// Shift non-dragged tiles to visualise where the dragged tile would land.
        /// </summary>
        private void ShiftTilesForDrag(System.Windows.Controls.WrapPanel panel, int origIndex, int hoverIndex, Border draggedTile)
        {
            TimeSpan duration = TimeSpan.FromMilliseconds(160);
            CubicEase ease = new() { EasingMode = EasingMode.EaseOut };

            for (int i = 0; i < panel.Children.Count; i++)
            {
                if (panel.Children[i] is not Border child || child == draggedTile) continue;
                if (child.RenderTransform is not TranslateTransform tt) continue;

                int visualIndex = i;
                if (origIndex < hoverIndex)
                {
                    if (i > origIndex && i <= hoverIndex) visualIndex = i - 1;
                }
                else if (origIndex > hoverIndex)
                {
                    if (i >= hoverIndex && i < origIndex) visualIndex = i + 1;
                }

                double targetX = _slotCentersX[visualIndex] - _slotCentersX[i];
                double targetY = _slotCentersY[visualIndex] - _slotCentersY[i];

                DoubleAnimation animX = new(targetX, duration) { EasingFunction = ease };
                DoubleAnimation animY = new(targetY, duration) { EasingFunction = ease };
                tt.BeginAnimation(TranslateTransform.XProperty, animX);
                tt.BeginAnimation(TranslateTransform.YProperty, animY);
            }
        }

        private void ApplyPresetToHome(TeamPreset p)
        {
            _selectedHomePreset = p;
            SetupHomeNameBox.Text = p.TeamName;
            SetupHomeAbbrBox.Text = p.Abbreviation;
            SetupHomeColorBox.Text = p.PrimaryColor;
            SetupHomeSecondaryBox.Text = p.SecondaryColor;
            TrySetPreview(SetupHomeColorPreview, p.PrimaryColor);
            TrySetPreview(SetupHomeSecondaryPreview, p.SecondaryColor);
            if (!string.IsNullOrWhiteSpace(p.LogoPath) && System.IO.File.Exists(p.LogoPath))
            {
                _homeLogoPath = p.LogoPath;
                SetupHomeLogoText.Text = System.IO.Path.GetFileName(p.LogoPath);
                ShowLogoPreview(SetupHomeLogoImage, SetupHomeLogoPreview, p.LogoPath);
            }
            if (!string.IsNullOrWhiteSpace(p.GoalVideoPath) && System.IO.File.Exists(p.GoalVideoPath))
            {
                _homeVideoPath = p.GoalVideoPath;
                SetupHomeVideoText.Text = System.IO.Path.GetFileName(p.GoalVideoPath);
            }
            else
            {
                _homeVideoPath = null;
                SetupHomeVideoText.Text = "(none)";
            }

            BuildPresetTiles(HomePresetPanel, HomePresetEmpty, true);
            BuildPresetTiles(AwayPresetPanel, AwayPresetEmpty, false);
            UpdatePreview();
        }

        private void ApplyPresetToAway(TeamPreset p)
        {
            _selectedAwayPreset = p;
            SetupAwayNameBox.Text = p.TeamName;
            SetupAwayAbbrBox.Text = p.Abbreviation;
            SetupAwayColorBox.Text = p.PrimaryColor;
            SetupAwaySecondaryBox.Text = p.SecondaryColor;
            TrySetPreview(SetupAwayColorPreview, p.PrimaryColor);
            TrySetPreview(SetupAwaySecondaryPreview, p.SecondaryColor);
            if (!string.IsNullOrWhiteSpace(p.LogoPath) && System.IO.File.Exists(p.LogoPath))
            {
                _awayLogoPath = p.LogoPath;
                SetupAwayLogoText.Text = System.IO.Path.GetFileName(p.LogoPath);
                ShowLogoPreview(SetupAwayLogoImage, SetupAwayLogoPreview, p.LogoPath);
            }
            if (!string.IsNullOrWhiteSpace(p.GoalVideoPath) && System.IO.File.Exists(p.GoalVideoPath))
            {
                _awayVideoPath = p.GoalVideoPath;
                SetupAwayVideoText.Text = System.IO.Path.GetFileName(p.GoalVideoPath);
            }
            else
            {
                _awayVideoPath = null;
                SetupAwayVideoText.Text = "(none)";
            }

            BuildPresetTiles(HomePresetPanel, HomePresetEmpty, true);
            BuildPresetTiles(AwayPresetPanel, AwayPresetEmpty, false);
            UpdatePreview();
        }

        private void SaveHomePreset_Click(object sender, RoutedEventArgs e) => SavePreset(SetupHomeNameBox, SetupHomeAbbrBox, SetupHomeColorBox, SetupHomeSecondaryBox, _homeLogoPath, _homeVideoPath);
        private void DeleteHomePreset_Click(object sender, RoutedEventArgs e) => DeletePreset(_selectedHomePreset);

        private void SaveAwayPreset_Click(object sender, RoutedEventArgs e) => SavePreset(SetupAwayNameBox, SetupAwayAbbrBox, SetupAwayColorBox, SetupAwaySecondaryBox, _awayLogoPath, _awayVideoPath);
        private void DeleteAwayPreset_Click(object sender, RoutedEventArgs e) => DeletePreset(_selectedAwayPreset);

        private void SavePreset(TextBox nameBox, TextBox abbrBox, TextBox colorBox, TextBox secBox, string? logoPath, string? videoPath)
        {
            string name = nameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            var existing = _presets.FirstOrDefault(p => p.PresetName == name);
            if (existing != null)
            {
                existing.TeamName = name;
                existing.Abbreviation = abbrBox.Text.Trim();
                existing.PrimaryColor = colorBox.Text.Trim();
                existing.SecondaryColor = secBox.Text.Trim();
                existing.LogoPath = logoPath;
                existing.GoalVideoPath = videoPath;
            }
            else
            {
                _presets.Add(new TeamPreset
                {
                    PresetName = name,
                    TeamName = name,
                    Abbreviation = abbrBox.Text.Trim(),
                    PrimaryColor = colorBox.Text.Trim(),
                    SecondaryColor = secBox.Text.Trim(),
                    LogoPath = logoPath,
                    GoalVideoPath = videoPath
                });
            }

            PresetStorage.SaveAll(_presets);
            RefreshPresetPanels();
        }

        private void DeletePreset(TeamPreset? preset)
        {
            if (preset == null) return;
            _presets.Remove(preset);
            PresetStorage.SaveAll(_presets);
            RefreshPresetPanels();
        }

        private static Color SafeParseColor(string hex, string fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return (Color)ColorConverter.ConvertFromString(fallback); }
        }

        private static void TrySetPreview(Border preview, string hex)
        {
            try { preview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { /* ignore */ }
        }

        // Auto-update colour preview swatches as the user types hex values
        private void SetupHomeColorBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TrySetPreview(SetupHomeColorPreview, SetupHomeColorBox.Text.Trim());
            UpdatePreview();
        }

        private void SetupHomeSecondaryBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TrySetPreview(SetupHomeSecondaryPreview, SetupHomeSecondaryBox.Text.Trim());
            UpdatePreview();
        }

        private void SetupAwayColorBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TrySetPreview(SetupAwayColorPreview, SetupAwayColorBox.Text.Trim());
            UpdatePreview();
        }

        private void SetupAwaySecondaryBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TrySetPreview(SetupAwaySecondaryPreview, SetupAwaySecondaryBox.Text.Trim());
            UpdatePreview();
        }

        // ----------------------------
        // Live preview sidebar
        // ----------------------------
        private void UpdatePreview()
        {
            if (PreviewHomeBg == null) return;

            string homeName = SetupHomeNameBox.Text.Trim();
            string homeAbbr = SetupHomeAbbrBox.Text.Trim();
            string awayName = SetupAwayNameBox.Text.Trim();
            string awayAbbr = SetupAwayAbbrBox.Text.Trim();

            PreviewHomeAbbr.Text = string.IsNullOrWhiteSpace(homeAbbr) ? "HOM" : homeAbbr.ToUpper();
            PreviewHomeName.Text = string.IsNullOrWhiteSpace(homeName) ? "Home Team" : homeName;
            PreviewAwayAbbr.Text = string.IsNullOrWhiteSpace(awayAbbr) ? "AWA" : awayAbbr.ToUpper();
            PreviewAwayName.Text = string.IsNullOrWhiteSpace(awayName) ? "Away Team" : awayName;

            try { PreviewHomeBg.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(SetupHomeColorBox.Text.Trim())); } catch { }
            try { PreviewAwayBg.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(SetupAwayColorBox.Text.Trim())); } catch { }

            PreviewHomeDetail.Text = string.IsNullOrWhiteSpace(homeName)
                ? "—"
                : $"{homeName} ({homeAbbr})\n{SetupHomeColorBox.Text.Trim()}" + (_homeLogoPath != null ? "\n✓ Logo" : "");

            PreviewAwayDetail.Text = string.IsNullOrWhiteSpace(awayName)
                ? "—"
                : $"{awayName} ({awayAbbr})\n{SetupAwayColorBox.Text.Trim()}" + (_awayLogoPath != null ? "\n✓ Logo" : "");

            PreviewClockDetail.Text = SetupCountdown?.IsChecked == true
                ? $"Countdown from {SetupQuarterMinutes.Text}:{SetupQuarterSeconds.Text}"
                : "Count Up";

            PreviewMessageCount.Text = $"{SetupMessageList.Items.Count} message{(SetupMessageList.Items.Count == 1 ? "" : "s")}";
        }
    }
}

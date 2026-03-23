using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color;

namespace Roche_Scoreboard.Services
{
    /// <summary>
    /// Full-screen overlay eye dropper. Call <see cref="Pick"/> to let the user
    /// click anywhere on screen and sample the pixel colour.
    /// </summary>
    internal static class EyeDropper
    {
        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hdc, int x, int y);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT pt);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        /// <summary>
        /// Opens a full-screen transparent overlay. The user clicks to sample a pixel.
        /// Returns the sampled <see cref="Color"/>, or <c>null</c> if cancelled (right-click / Escape).
        /// </summary>
        public static Color? Pick()
        {
            Color? result = null;

            // Full-screen transparent window
            var overlay = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), // nearly transparent
                Topmost = true,
                ShowInTaskbar = false,
                Cursor = System.Windows.Input.Cursors.Cross,
                Left = SystemParameters.VirtualScreenLeft,
                Top = SystemParameters.VirtualScreenTop,
                Width = SystemParameters.VirtualScreenWidth,
                Height = SystemParameters.VirtualScreenHeight
            };

            // Preview circle that follows the mouse
            var previewBorder = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(24),
                BorderBrush = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(3),
                Background = new SolidColorBrush(Colors.Black),
                IsHitTestVisible = false,
                Visibility = Visibility.Hidden
            };
            var canvas = new Canvas { IsHitTestVisible = false };
            canvas.Children.Add(previewBorder);
            var grid = new Grid();
            grid.Children.Add(canvas);

            // Instruction hint
            var hint = new TextBlock
            {
                Text = "Click to sample · Esc to cancel",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 40, 0, 0)
            };
            grid.Children.Add(hint);
            overlay.Content = grid;

            overlay.MouseMove += (_, args) =>
            {
                previewBorder.Visibility = Visibility.Visible;
                var pos = args.GetPosition(canvas);
                Canvas.SetLeft(previewBorder, pos.X + 16);
                Canvas.SetTop(previewBorder, pos.Y + 16);

                // Sample the pixel under the cursor
                var screenColor = GetScreenPixel();
                previewBorder.Background = new SolidColorBrush(screenColor);
            };

            overlay.MouseLeftButtonDown += (_, __) =>
            {
                result = GetScreenPixel();
                overlay.Close();
            };

            overlay.MouseRightButtonDown += (_, __) => overlay.Close();
            overlay.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Escape) overlay.Close();
            };

            overlay.ShowDialog();
            return result;
        }

        private static Color GetScreenPixel()
        {
            GetCursorPos(out var pt);
            IntPtr hdc = GetDC(IntPtr.Zero);
            uint pixel = GetPixel(hdc, pt.X, pt.Y);
            ReleaseDC(IntPtr.Zero, hdc);

            byte r = (byte)(pixel & 0xFF);
            byte g = (byte)((pixel >> 8) & 0xFF);
            byte b = (byte)((pixel >> 16) & 0xFF);
            return Color.FromRgb(r, g, b);
        }
    }
}

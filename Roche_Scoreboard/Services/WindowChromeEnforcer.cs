using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Roche_Scoreboard.Services
{
    /// <summary>
    /// Forces Windows 11 DWM to render a window with SHARP corners and NO
    /// border accent colour. Windows 11 automatically rounds the corners of
    /// <see cref="WindowStyle.None"/> top-level windows and may draw a 1px
    /// accent-coloured border around them — this is the cause of the
    /// "curved edges and top border" regression seen on some machines even
    /// though the WPF window itself is borderless. Calling
    /// <see cref="ForceSharpNoBorder"/> after the HWND exists pins the
    /// behaviour off and keeps it off for the lifetime of the window.
    /// Safe no-op on Windows 10 and earlier.
    /// </summary>
    internal static class WindowChromeEnforcer
    {
        // DwmSetWindowAttribute attribute ids (dwmapi.h)
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;

        // DWM_WINDOW_CORNER_PREFERENCE values
        private const int DWMWCP_DONOTROUND = 1;

        // Sentinel telling DWM "no border colour at all"
        private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint pvAttribute, int cbAttribute);

        /// <summary>
        /// Apply sharp-corner / no-border DWM attributes to the given HWND.
        /// </summary>
        public static void ForceSharpNoBorder(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;

            try
            {
                int corner = DWMWCP_DONOTROUND;
                _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            }
            catch
            {
                // Older Windows builds don't support this attribute — that's fine,
                // they don't round our windows in the first place.
            }

            try
            {
                uint borderColor = DWMWA_COLOR_NONE;
                _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(uint));
            }
            catch
            {
                // Same fallback as above.
            }
        }

        /// <summary>
        /// Convenience overload. Hooks the window so the attributes are
        /// (re)applied at <see cref="Window.SourceInitialized"/> and on every
        /// <see cref="Window.Loaded"/>. Call this once, immediately after
        /// constructing the window.
        /// </summary>
        public static void Attach(Window window)
        {
            if (window is null) return;

            void Apply()
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                ForceSharpNoBorder(hwnd);
            }

            window.SourceInitialized += (_, __) => Apply();
            window.Loaded += (_, __) => Apply();

            // If the window already has an HWND, apply immediately too.
            if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
                Apply();
        }
    }
}

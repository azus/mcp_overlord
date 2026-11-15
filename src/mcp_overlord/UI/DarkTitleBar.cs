using System.Runtime.InteropServices;
using System.Windows.Media;

namespace mcp_overlord.UI
{
    /// <summary>
    /// Asks Windows to paint the title bar in the dark system color.
    /// A build that ignores the attribute keeps the dark client area and the system title bar.
    /// </summary>
    public static class DarkTitleBar
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;

        /// <summary>Dark blue used when the process is elevated. Matches <c>AdministratorTitleBarColor</c>.</summary>
        public static readonly Color AdministratorCaptionColor = Color.FromRgb(0x0B, 0x2A, 0x4A);

        /// <summary>
        /// Enables the immersive dark title bar for <paramref name="hwnd"/>.
        /// </summary>
        /// <param name="hwnd">Window handle.</param>
        public static void Apply(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            int enabled = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
        }

        /// <summary>
        /// Paints the caption <paramref name="color"/>. Windows 10 builds that do not know the attribute leave the system color.
        /// </summary>
        /// <param name="hwnd">Window handle.</param>
        /// <param name="color">Caption color.</param>
        public static void ApplyCaptionColor(IntPtr hwnd, Color color)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            int colorRef = color.R | (color.G << 8) | (color.B << 16);
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref colorRef, sizeof(int));
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}

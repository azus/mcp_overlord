using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace mcp_overlord.UI
{
    /// <summary>
    /// A click-through rectangle drawn around the window under the target cursor.
    /// </summary>
    public sealed class WindowHighlightOverlay : Window
    {
        private const int GwlExStyle = -20;
        private const int WsExTransparent = 0x00000020;
        private const int WsExLayered = 0x00080000;
        private const int WsExNoActivate = 0x08000000;
        private const int WsExToolWindow = 0x00000080;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpShowWindow = 0x0040;

        /// <summary>
        /// Creates the overlay. It is shown by <see cref="MoveTo"/>.
        /// </summary>
        public WindowHighlightOverlay()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            IsHitTestVisible = false;
            Width = 1;
            Height = 1;
            Content = new System.Windows.Controls.Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x7A, 0xFE)),
                BorderThickness = new Thickness(3),
                Background = new SolidColorBrush(Color.FromArgb(40, 0x3A, 0x7A, 0xFE)),
                IsHitTestVisible = false
            };
            SourceInitialized += OnSourceInitialized;
        }

        /// <summary>
        /// Moves the overlay to a rectangle in physical pixels and shows it.
        /// </summary>
        /// <param name="x">Left edge.</param>
        /// <param name="y">Top edge.</param>
        /// <param name="width">Width.</param>
        /// <param name="height">Height.</param>
        public void MoveTo(int x, int y, int width, int height)
        {
            if (width < 1 || height < 1)
            {
                HideHighlight();
                return;
            }

            if (!IsVisible)
            {
                Show();
            }

            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            SetWindowPos(handle, new IntPtr(-1), x, y, width, height, SwpNoActivate | SwpShowWindow);
        }

        /// <summary>Hides the overlay without closing it.</summary>
        public void HideHighlight()
        {
            if (IsVisible)
            {
                Hide();
            }
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            int style = GetWindowLong(handle, GwlExStyle);
            SetWindowLong(handle, GwlExStyle, style | WsExTransparent | WsExLayered | WsExNoActivate | WsExToolWindow);
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr window, int index, int value);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr window,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);
    }
}

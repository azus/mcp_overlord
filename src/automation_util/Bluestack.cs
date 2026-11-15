using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace game_automation
{
    public class Bluestack
    {
        // Define the WM_CLOSE constant
        private const int WM_CLOSE = 0x0010;

        // Import the SendMessage Function from user32.dll
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// Starts BlueStacks without instance arguments.
        /// </summary>
        public static void Start()
        {
            System.Diagnostics.Process.Start(@"C:\Program Files\BlueStacks_nxt\HD-Player.exe");
        }

        /// <summary>
        /// Starts BlueStacks and passes <paramref name="path"/> as the process arguments.
        /// </summary>
        /// <param name="path">Arguments for HD-Player, such as an instance and a package.</param>
        public static void Start(string path)
        {
            System.Diagnostics.Process.Start(@"C:\Program Files\BlueStacks_nxt\HD-Player.exe", path);
        }
        public static void Close(IntPtr hwnd)
        {
            SendMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            Thread.Sleep(3000);
            MouseEventClickHelper.ClickOnPoint(hwnd, 1376, 739);
        }
    }
}

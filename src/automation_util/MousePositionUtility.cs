using System;
using System.Runtime.InteropServices;

namespace game_automation
{
    public class MousePositionUtility
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;

            public POINT(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        public static void infiniteLoop(IntPtr hwnd)
        {
            while (true)
            {
                var pos = GetCurrentMousePosition(hwnd);
                if (pos.HasValue)
                {
                    Console.WriteLine($"X: {pos.Value.X}, Y: {pos.Value.Y}");
                }

                System.Threading.Thread.Sleep(200);
            }
        }

        public static POINT? GetCurrentMousePosition(IntPtr windowHandle)
        {
            // Get the mouse cursor position
            if (GetCursorPos(out POINT lpPoint))
            {
                // Convert screen coordinates to window/client coordinates
                ScreenToClient(windowHandle, ref lpPoint);
                return lpPoint;
            }

            // Return null if getting the cursor position failed
            return null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace game_automation
{
    public class Window
    {
        private readonly BitmapFinder bitmapFinder;
        private readonly nint hwnd;

        public Window(BitmapFinder bitmapFinder, IntPtr hwnd)
        {
            this.bitmapFinder = bitmapFinder;
            this.hwnd = hwnd;
        }

        public Rectangle FindBitmap(game_automation.Image bitmap, double v = 0.8)
        {
            return bitmapFinder.FindBitmap(bitmap, v);
        }

        public List<Rectangle> FindAllMatches(game_automation.Image bitmap, double v = 0.8)
        {
            return bitmapFinder.FindAllMatches(bitmap, v);
        }

        public BitmapFinder GetBitmap()
        {
            return bitmapFinder;
        }

        public struct RECT
        {
            public int left, top, right, bottom;
        }
        // get window rect from hwnd
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetWindowRect(IntPtr hWnd, ref RECT lpRect);

        // import the SetWindowPos function
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        public Rectangle rect()
        {
            RECT rect = new RECT();
            bool result = GetWindowRect(hwnd, ref rect);
            if (result)
                return new Rectangle(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
            return new Rectangle(-1, -1, -1, -1);
        }

        public void setRect(Rectangle rect)
        {
            // set the window rect using hwnd
            SetWindowPos(hwnd, 0, rect.X, rect.Y, rect.Width, rect.Height, 0);
        }

        public IntPtr Hwnd
        {
            get => hwnd;
        }

        public static IntPtr FindByTitle(string title)
        {
            IntPtr hWnd = IntPtr.Zero;
            foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcesses())
            {
                if (p.MainWindowTitle.Contains(title))
                {
                    hWnd = p.MainWindowHandle;
                    break;
                }
            }

            return hWnd;
        }

        public void Save(string path)
        {
            bitmapFinder.GetBitmap().Save(path);
        }

        public static Window Capture(IntPtr hwnd, ScreenshotGenerator screenshot_generator)
        {
            var bitmap = screenshot_generator.CaptureWindow(hwnd);
            var finder = new BitmapFinder(bitmap);
            return new Window(finder, hwnd);
        }
        public static Window Load(IntPtr hwnd, string path)
        {
            // load Btimap from path
            var bitmap = new Bitmap(path);
            var finder = new BitmapFinder(bitmap);
            return new Window(finder, hwnd);
        }

        // Define the WM_CLOSE constant
        private const int WM_CLOSE = 0x0010;

        // Import the SendMessage Function from user32.dll
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        public void Close()
        {
            SendMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        public static void Close(IntPtr hwnd)
        {
            SendMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
    }
}

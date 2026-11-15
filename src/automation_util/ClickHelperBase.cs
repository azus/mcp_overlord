using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace game_automation
{
    /// <summary>
    /// Shared gesture timing and client-to-screen conversion.
    /// Concrete backends only deliver a move, a button, or a wheel at a screen point.
    /// </summary>
    public abstract class ClickHelperBase : ClickHelper
    {
        /// <inheritdoc />
        public void MoveTo(IntPtr hwnd, int clientX, int clientY)
        {
            Point screen = ClientToScreenPoint(hwnd, clientX, clientY);
            MoveAtScreen(screen.X, screen.Y);
        }

        /// <inheritdoc />
        public void ButtonDown(IntPtr hwnd, int clientX, int clientY, MouseButton button)
        {
            Point screen = ClientToScreenPoint(hwnd, clientX, clientY);
            ButtonDownAtScreen(screen.X, screen.Y, button);
        }

        /// <inheritdoc />
        public void ButtonUp(IntPtr hwnd, int clientX, int clientY, MouseButton button)
        {
            Point screen = ClientToScreenPoint(hwnd, clientX, clientY);
            ButtonUpAtScreen(screen.X, screen.Y, button);
        }

        /// <inheritdoc />
        public void Click(IntPtr hwnd, int clientX, int clientY, MouseButton button, int holdMs, bool doubleClick)
        {
            PerformClick(hwnd, clientX, clientY, button, holdMs);
            if (doubleClick)
            {
                Delay(GetDoubleClickDelay());
                PerformClick(hwnd, clientX, clientY, button, holdMs);
            }
        }

        /// <inheritdoc />
        public void Scroll(IntPtr hwnd, int clientX, int clientY, int notches)
        {
            Point screen = ClientToScreenPoint(hwnd, clientX, clientY);
            MoveAtScreen(screen.X, screen.Y);
            ScrollAtScreen(screen.X, screen.Y, notches);
        }

        /// <inheritdoc />
        void ClickHelper.ClickOnPoint(IntPtr hwnd, int x, int y, int clickDelayInMilliseconds)
        {
            Click(hwnd, x, y, MouseButton.Left, clickDelayInMilliseconds, false);
        }

        /// <summary>
        /// Moves the cursor to a screen point.
        /// </summary>
        /// <param name="screenX">Screen X.</param>
        /// <param name="screenY">Screen Y.</param>
        protected abstract void MoveAtScreen(int screenX, int screenY);

        /// <summary>
        /// Presses a button at a screen point. The cursor should end on that point.
        /// </summary>
        /// <param name="screenX">Screen X.</param>
        /// <param name="screenY">Screen Y.</param>
        /// <param name="button">Button to press.</param>
        protected abstract void ButtonDownAtScreen(int screenX, int screenY, MouseButton button);

        /// <summary>
        /// Releases a button at a screen point. The cursor should end on that point.
        /// </summary>
        /// <param name="screenX">Screen X.</param>
        /// <param name="screenY">Screen Y.</param>
        /// <param name="button">Button to release.</param>
        protected abstract void ButtonUpAtScreen(int screenX, int screenY, MouseButton button);

        /// <summary>
        /// Scrolls the wheel at a screen point. The cursor should already be there.
        /// </summary>
        /// <param name="screenX">Screen X.</param>
        /// <param name="screenY">Screen Y.</param>
        /// <param name="notches">Wheel notches. Positive is up.</param>
        protected abstract void ScrollAtScreen(int screenX, int screenY, int notches);

        /// <summary>
        /// Waits between press and release, or between the two halves of a double click.
        /// </summary>
        /// <param name="milliseconds">Delay. Zero or negative does not wait.</param>
        protected virtual void Delay(int milliseconds)
        {
            if (milliseconds > 0)
            {
                System.Threading.Thread.Sleep(milliseconds);
            }
        }

        /// <summary>
        /// System double-click time, in milliseconds.
        /// </summary>
        /// <returns>The delay between the two clicks of a double click.</returns>
        protected virtual int GetDoubleClickDelay()
        {
            return (int)GetDoubleClickTime();
        }

        /// <summary>
        /// Converts a client point to a screen point. A zero handle is returned unchanged so tests can skip Win32.
        /// </summary>
        /// <param name="hwnd">Window handle, or zero.</param>
        /// <param name="clientX">Client X.</param>
        /// <param name="clientY">Client Y.</param>
        /// <returns>The screen point.</returns>
        protected virtual Point ClientToScreenPoint(IntPtr hwnd, int clientX, int clientY)
        {
            POINT point = new POINT(clientX, clientY);
            if (hwnd != IntPtr.Zero)
            {
                ClientToScreen(hwnd, ref point);
            }

            return new Point(point.X, point.Y);
        }

        private void PerformClick(IntPtr hwnd, int clientX, int clientY, MouseButton button, int holdMs)
        {
            MoveTo(hwnd, clientX, clientY);
            ButtonDown(hwnd, clientX, clientY, button);
            Delay(holdMs);
            ButtonUp(hwnd, clientX, clientY, button);
        }

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);

        [DllImport("user32.dll")]
        private static extern uint GetDoubleClickTime();

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;

            public POINT(int x, int y)
            {
                X = x;
                Y = y;
            }
        }
    }
}

using System;
using System.Runtime.InteropServices;

namespace game_automation
{
    /// <summary>
    /// Mouse backend that places the cursor with <c>SetCursorPos</c> and sends buttons with <c>mouse_event</c>.
    /// This is the default backend and the one existing games call.
    /// </summary>
    public sealed class MouseEventClickHelper : ClickHelperBase
    {
        private static readonly MouseEventClickHelper SharedInstance = new MouseEventClickHelper();

        /// <summary>
        /// Left-button click used by existing callers. Forwards to a shared instance.
        /// </summary>
        /// <param name="hwnd">Target window.</param>
        /// <param name="x">Client X in physical pixels.</param>
        /// <param name="y">Client Y in physical pixels.</param>
        /// <param name="clickDelayInMilliseconds">Delay between press and release. Defaults to 100.</param>
        public static void ClickOnPoint(IntPtr hwnd, int x, int y, int clickDelayInMilliseconds = 100)
        {
            SharedInstance.Click(hwnd, x, y, MouseButton.Left, clickDelayInMilliseconds, false);
        }

        /// <inheritdoc />
        protected override void MoveAtScreen(int screenX, int screenY)
        {
            SetCursorPos(screenX, screenY);
        }

        /// <inheritdoc />
        protected override void ButtonDownAtScreen(int screenX, int screenY, MouseButton button)
        {
            SetCursorPos(screenX, screenY);
            mouse_event(MouseInputEncoder.ButtonFlag(button, true), 0, 0, 0, 0);
        }

        /// <inheritdoc />
        protected override void ButtonUpAtScreen(int screenX, int screenY, MouseButton button)
        {
            SetCursorPos(screenX, screenY);
            mouse_event(MouseInputEncoder.ButtonFlag(button, false), 0, 0, 0, 0);
        }

        /// <inheritdoc />
        protected override void ScrollAtScreen(int screenX, int screenY, int notches)
        {
            SetCursorPos(screenX, screenY);
            MouseInputDescription wheel = MouseInputEncoder.Wheel(notches);
            mouse_event(wheel.Flags, 0, 0, wheel.MouseData, 0);
        }

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, int extraInfo);
    }
}

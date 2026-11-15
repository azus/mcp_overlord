using System;

namespace game_automation
{
    /// <summary>
    /// Mouse gestures delivered to a window in client coordinates.
    /// Each backend implements the full set. Callers never send raw Win32 input themselves.
    /// </summary>
    public interface ClickHelper
    {
        /// <summary>
        /// Moves the cursor to a client point without changing button state.
        /// </summary>
        /// <param name="hwnd">Target window.</param>
        /// <param name="clientX">Client X in physical pixels.</param>
        /// <param name="clientY">Client Y in physical pixels.</param>
        void MoveTo(IntPtr hwnd, int clientX, int clientY);

        /// <summary>
        /// Moves the cursor to a client point and presses a button. The button stays down.
        /// </summary>
        /// <param name="hwnd">Target window.</param>
        /// <param name="clientX">Client X in physical pixels.</param>
        /// <param name="clientY">Client Y in physical pixels.</param>
        /// <param name="button">Button to press.</param>
        void ButtonDown(IntPtr hwnd, int clientX, int clientY, MouseButton button);

        /// <summary>
        /// Moves the cursor to a client point and releases a button.
        /// </summary>
        /// <param name="hwnd">Target window.</param>
        /// <param name="clientX">Client X in physical pixels.</param>
        /// <param name="clientY">Client Y in physical pixels.</param>
        /// <param name="button">Button to release.</param>
        void ButtonUp(IntPtr hwnd, int clientX, int clientY, MouseButton button);

        /// <summary>
        /// Moves, presses, waits, and releases. When <paramref name="doubleClick"/> is true the click is repeated once.
        /// </summary>
        /// <param name="hwnd">Target window.</param>
        /// <param name="clientX">Client X in physical pixels.</param>
        /// <param name="clientY">Client Y in physical pixels.</param>
        /// <param name="button">Button to click.</param>
        /// <param name="holdMs">Delay between press and release, in milliseconds.</param>
        /// <param name="doubleClick">Repeat the click after the system double-click time.</param>
        void Click(IntPtr hwnd, int clientX, int clientY, MouseButton button, int holdMs, bool doubleClick);

        /// <summary>
        /// Moves the cursor and scrolls the wheel. Positive notches scroll up, away from the user.
        /// </summary>
        /// <param name="hwnd">Target window.</param>
        /// <param name="clientX">Client X in physical pixels.</param>
        /// <param name="clientY">Client Y in physical pixels.</param>
        /// <param name="notches">Wheel notches. Positive is up.</param>
        void Scroll(IntPtr hwnd, int clientX, int clientY, int notches);

        /// <summary>
        /// Left-button click used by existing callers: move, left down, wait, left up.
        /// </summary>
        /// <param name="hwnd">Target window.</param>
        /// <param name="x">Client X in physical pixels.</param>
        /// <param name="y">Client Y in physical pixels.</param>
        /// <param name="clickDelayInMilliseconds">Delay between press and release. Defaults to 100.</param>
        void ClickOnPoint(IntPtr hwnd, int x, int y, int clickDelayInMilliseconds = 100);
    }
}

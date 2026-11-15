using System;

namespace game_automation
{
    /// <summary>
    /// Bounds of the virtual desktop used to normalize absolute mouse coordinates.
    /// </summary>
    /// <param name="Left">Left edge in screen pixels.</param>
    /// <param name="Top">Top edge in screen pixels.</param>
    /// <param name="Width">Width in screen pixels.</param>
    /// <param name="Height">Height in screen pixels.</param>
    public readonly record struct VirtualScreen(int Left, int Top, int Width, int Height);

    /// <summary>
    /// One mouse event before it is packed into a native <c>INPUT</c> structure.
    /// </summary>
    /// <param name="Dx">Horizontal delta, or a 0–65535 absolute coordinate.</param>
    /// <param name="Dy">Vertical delta, or a 0–65535 absolute coordinate.</param>
    /// <param name="MouseData">Wheel delta or extra button data.</param>
    /// <param name="Flags">Win32 <c>MOUSEEVENTF_*</c> flags.</param>
    public readonly record struct MouseInputDescription(int Dx, int Dy, uint MouseData, uint Flags);

    /// <summary>
    /// Pure mapping from gestures to Win32 mouse flags and absolute coordinates.
    /// </summary>
    public static class MouseInputEncoder
    {
        /// <summary>Move flag.</summary>
        public const uint MOUSEEVENTF_MOVE = 0x0001;

        /// <summary>Left button down.</summary>
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;

        /// <summary>Left button up.</summary>
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;

        /// <summary>Right button down.</summary>
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;

        /// <summary>Right button up.</summary>
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;

        /// <summary>Middle button down.</summary>
        public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;

        /// <summary>Middle button up.</summary>
        public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;

        /// <summary>Wheel flag.</summary>
        public const uint MOUSEEVENTF_WHEEL = 0x0800;

        /// <summary>Absolute coordinate flag. The range 0–65535 covers the virtual screen.</summary>
        public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        /// <summary>
        /// Maps absolute coordinates onto the virtual desktop. Required because <see cref="Normalize"/> uses that desktop.
        /// Without it, Windows maps 0–65535 onto the primary monitor only.
        /// </summary>
        public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

        /// <summary>One wheel notch in Win32 units.</summary>
        public const int WHEEL_DELTA = 120;

        /// <summary>Inclusive maximum of the absolute coordinate range.</summary>
        public const int ABSOLUTE_RANGE = 65535;

        /// <summary>
        /// Maps a screen point into the 0–65535 absolute range used by <c>MOUSEEVENTF_ABSOLUTE</c>.
        /// </summary>
        /// <param name="screenX">Screen X.</param>
        /// <param name="screenY">Screen Y.</param>
        /// <param name="screen">Virtual screen the point belongs to.</param>
        /// <returns>Normalized coordinates clamped to 0–65535.</returns>
        public static (int X, int Y) Normalize(int screenX, int screenY, VirtualScreen screen)
        {
            if (screen.Width <= 1 || screen.Height <= 1)
            {
                return (0, 0);
            }

            double relativeX = (screenX - screen.Left) * (double)ABSOLUTE_RANGE / (screen.Width - 1);
            double relativeY = (screenY - screen.Top) * (double)ABSOLUTE_RANGE / (screen.Height - 1);
            int normalizedX = (int)Math.Round(relativeX);
            int normalizedY = (int)Math.Round(relativeY);
            return (Math.Clamp(normalizedX, 0, ABSOLUTE_RANGE), Math.Clamp(normalizedY, 0, ABSOLUTE_RANGE));
        }

        /// <summary>
        /// Builds an absolute move to a screen point.
        /// </summary>
        /// <param name="screenX">Screen X.</param>
        /// <param name="screenY">Screen Y.</param>
        /// <param name="screen">Virtual screen.</param>
        /// <returns>A move description with absolute flags.</returns>
        public static MouseInputDescription MoveAbsolute(int screenX, int screenY, VirtualScreen screen)
        {
            (int normalizedX, int normalizedY) = Normalize(screenX, screenY, screen);
            return new MouseInputDescription(
                normalizedX,
                normalizedY,
                0,
                MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK);
        }

        /// <summary>
        /// Builds a button down or up event. The cursor is expected to already be in place.
        /// </summary>
        /// <param name="button">Button.</param>
        /// <param name="isDown">True for a press, false for a release.</param>
        /// <returns>The button description.</returns>
        public static MouseInputDescription Button(MouseButton button, bool isDown)
        {
            return new MouseInputDescription(0, 0, 0, ButtonFlag(button, isDown));
        }

        /// <summary>
        /// Builds a wheel event. Positive notches scroll up.
        /// </summary>
        /// <param name="notches">Number of notches.</param>
        /// <returns>The wheel description.</returns>
        public static MouseInputDescription Wheel(int notches)
        {
            int delta = notches * WHEEL_DELTA;
            return new MouseInputDescription(0, 0, unchecked((uint)delta), MOUSEEVENTF_WHEEL);
        }

        /// <summary>
        /// Returns the <c>mouse_event</c> flag for a button transition.
        /// </summary>
        /// <param name="button">Button.</param>
        /// <param name="isDown">True for a press, false for a release.</param>
        /// <returns>The matching <c>MOUSEEVENTF_*</c> flag.</returns>
        public static uint ButtonFlag(MouseButton button, bool isDown)
        {
            switch (button)
            {
                case MouseButton.Left:
                    return isDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP;
                case MouseButton.Right:
                    return isDown ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP;
                case MouseButton.Middle:
                    return isDown ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP;
                default:
                    throw new ArgumentOutOfRangeException(nameof(button), button, "Unknown mouse button.");
            }
        }
    }
}

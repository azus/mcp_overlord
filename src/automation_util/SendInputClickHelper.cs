using System.Runtime.InteropServices;

namespace game_automation
{
    /// <summary>
    /// Mouse backend that sends move, button, and wheel events through <c>SendInput</c>.
    /// Moves use absolute coordinates normalized to the virtual screen, including <c>MOUSEEVENTF_VIRTUALDESK</c>.
    /// </summary>
    public sealed class SendInputClickHelper : ClickHelperBase
    {
        /// <inheritdoc />
        protected override void MoveAtScreen(int screenX, int screenY)
        {
            NativeInput.SendMouse(MouseInputEncoder.MoveAbsolute(screenX, screenY, ReadVirtualScreen()));
        }

        /// <inheritdoc />
        protected override void ButtonDownAtScreen(int screenX, int screenY, MouseButton button)
        {
            VirtualScreen screen = ReadVirtualScreen();
            NativeInput.SendMouse(
                MouseInputEncoder.MoveAbsolute(screenX, screenY, screen),
                MouseInputEncoder.Button(button, true));
        }

        /// <inheritdoc />
        protected override void ButtonUpAtScreen(int screenX, int screenY, MouseButton button)
        {
            VirtualScreen screen = ReadVirtualScreen();
            NativeInput.SendMouse(
                MouseInputEncoder.MoveAbsolute(screenX, screenY, screen),
                MouseInputEncoder.Button(button, false));
        }

        /// <inheritdoc />
        protected override void ScrollAtScreen(int screenX, int screenY, int notches)
        {
            VirtualScreen screen = ReadVirtualScreen();
            NativeInput.SendMouse(
                MouseInputEncoder.MoveAbsolute(screenX, screenY, screen),
                MouseInputEncoder.Wheel(notches));
        }

        private static VirtualScreen ReadVirtualScreen()
        {
            return new VirtualScreen(
                GetSystemMetrics(SM_XVIRTUALSCREEN),
                GetSystemMetrics(SM_YVIRTUALSCREEN),
                GetSystemMetrics(SM_CXVIRTUALSCREEN),
                GetSystemMetrics(SM_CYVIRTUALSCREEN));
        }

        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);
    }
}

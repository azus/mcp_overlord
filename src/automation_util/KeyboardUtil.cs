using game_automation;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace game_automation_in_csharp.util
{
    /// <summary>
    /// Focuses a window and sends keyboard input.
    /// </summary>
    public class KeyboardUtil
    {
        /// <summary>Key-up flag passed to <c>keybd_event</c>.</summary>
        public const int KEYEVENTF_KEYUP = 0x0002;

        /// <summary>Control virtual key.</summary>
        public const byte VK_CONTROL = 0x11;

        /// <summary>Shift virtual key.</summary>
        public const byte VK_SHIFT = 0x10;

        /// <summary>Digit 2 virtual key.</summary>
        public const byte VK_2 = 0x32;

        /// <summary>
        /// Focuses the window, presses each key, then releases them in reverse order.
        /// The release uses <see cref="KEYEVENTF_KEYUP"/> so the keys do not stay logically down.
        /// </summary>
        /// <param name="hwnd">Window to focus.</param>
        /// <param name="keys">Virtual keys to press together.</param>
        public static void PressAndRelease(IntPtr hwnd, byte[] keys)
        {
            SetForegroundWindow(hwnd);

            foreach (KeyboardInputDescription description in KeyboardInputEncoder.BuildPressAndRelease(keys))
            {
                keybd_event((byte)description.VirtualKey, 0, description.Flags, UIntPtr.Zero);
                Thread.Sleep(50);
            }
        }

        /// <summary>
        /// Focuses the window and sends a key down. The key stays down until <see cref="KeyUp"/>.
        /// </summary>
        /// <param name="hwnd">Window to focus.</param>
        /// <param name="virtualKey">Virtual-key code.</param>
        public static void KeyDown(IntPtr hwnd, byte virtualKey)
        {
            SetForegroundWindow(hwnd);
            NativeInput.SendKeyboard(KeyboardInputEncoder.KeyDown(virtualKey));
        }

        /// <summary>
        /// Focuses the window and sends a key up.
        /// </summary>
        /// <param name="hwnd">Window to focus.</param>
        /// <param name="virtualKey">Virtual-key code.</param>
        public static void KeyUp(IntPtr hwnd, byte virtualKey)
        {
            SetForegroundWindow(hwnd);
            NativeInput.SendKeyboard(KeyboardInputEncoder.KeyUp(virtualKey));
        }

        /// <summary>
        /// Focuses the window, presses a key, waits, and releases it.
        /// </summary>
        /// <param name="hwnd">Window to focus.</param>
        /// <param name="virtualKey">Virtual-key code.</param>
        /// <param name="holdMs">Time the key stays down, in milliseconds.</param>
        public static void Press(IntPtr hwnd, byte virtualKey, int holdMs)
        {
            SetForegroundWindow(hwnd);
            NativeInput.SendKeyboard(KeyboardInputEncoder.KeyDown(virtualKey));
            if (holdMs > 0)
            {
                Thread.Sleep(holdMs);
            }

            NativeInput.SendKeyboard(KeyboardInputEncoder.KeyUp(virtualKey));
        }

        /// <summary>
        /// Focuses the window and types text. Newlines become Return. Other characters use Unicode input.
        /// </summary>
        /// <param name="hwnd">Window to focus.</param>
        /// <param name="text">Text to type.</param>
        public static void TypeUnicode(IntPtr hwnd, string text)
        {
            SetForegroundWindow(hwnd);
            foreach (KeyboardInputDescription description in KeyboardInputEncoder.TypeText(text))
            {
                NativeInput.SendKeyboard(description);
            }
        }

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    }
}

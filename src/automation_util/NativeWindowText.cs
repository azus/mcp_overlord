using System;
using System.Runtime.InteropServices;
using System.Text;

namespace game_automation
{
    /// <summary>
    /// Reads a top-level window title without sending a message to that window.
    /// <c>GetWindowText</c> waits forever when the owner is not pumping messages.
    /// </summary>
    public static class NativeWindowText
    {
        /// <summary>Characters available for the title, including the terminating null.</summary>
        public const int BufferLength = 512;

        /// <summary>
        /// Returns the window title. An empty string means the window has no title or the handle is invalid.
        /// A title longer than <see cref="BufferLength"/> minus one character is cut.
        /// </summary>
        /// <param name="hwnd">Top-level window. Zero returns an empty string.</param>
        /// <returns>The title stored for the window.</returns>
        public static string Read(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return "";
            }

            var text = new StringBuilder(BufferLength);
            int copied = InternalGetWindowText(hwnd, text, text.Capacity);
            if (copied <= 0)
            {
                return "";
            }

            return text.ToString();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "InternalGetWindowText")]
        private static extern int InternalGetWindowText(IntPtr window, StringBuilder text, int maxCount);
    }
}

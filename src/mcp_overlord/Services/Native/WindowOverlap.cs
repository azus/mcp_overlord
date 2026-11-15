using System.Runtime.InteropServices;

namespace mcp_overlord.Services.Native
{
    /// <summary>
    /// Detects a visible window that overlaps a target from above. Owned windows, such as a drop shadow, do not count.
    /// </summary>
    internal static class WindowOverlap
    {
        private const uint GW_OWNER = 4;
        private const int DWMWA_CLOAKED = 14;

        /// <summary>
        /// True when a visible, non-minimized window above <paramref name="hwnd"/> intersects its rectangle.
        /// </summary>
        /// <param name="hwnd">Window to test.</param>
        /// <returns>True when a screen blit would include another window.</returns>
        public static bool IsCovered(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out RECT target))
            {
                return false;
            }

            if (target.Right <= target.Left || target.Bottom <= target.Top)
            {
                return false;
            }

            var scan = new Scan
            {
                Target = hwnd,
                TargetRect = target
            };
            EnumWindowsProc callback = scan.Visit;
            EnumWindows(callback, IntPtr.Zero);
            return scan.Covered;
        }

        private sealed class Scan
        {
            public IntPtr Target;

            public RECT TargetRect;

            public bool Covered;

            public bool Visit(IntPtr candidate, IntPtr state)
            {
                if (candidate == Target)
                {
                    return false;
                }

                if (!IsWindowVisible(candidate) || IsIconic(candidate) || IsCloaked(candidate))
                {
                    return true;
                }

                if (GetWindow(candidate, GW_OWNER) == Target)
                {
                    return true;
                }

                if (!GetWindowRect(candidate, out RECT other))
                {
                    return true;
                }

                if (other.Right <= other.Left || other.Bottom <= other.Top)
                {
                    return true;
                }

                if (Intersects(TargetRect, other))
                {
                    Covered = true;
                    return false;
                }

                return true;
            }
        }

        private static bool IsCloaked(IntPtr hwnd)
        {
            int cloaked = 0;
            int result = DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out cloaked, sizeof(int));
            return result == 0 && cloaked != 0;
        }

        private static bool Intersects(RECT first, RECT second)
        {
            return first.Left < second.Right
                && first.Right > second.Left
                && first.Top < second.Bottom
                && first.Bottom > second.Top;
        }

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr state);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr state);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}

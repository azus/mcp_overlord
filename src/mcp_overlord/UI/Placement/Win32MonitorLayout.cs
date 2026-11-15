using System.Runtime.InteropServices;

namespace mcp_overlord.UI.Placement
{
    /// <summary>
    /// Reads attached monitors through EnumDisplayMonitors. Monitors that are off or unplugged are omitted.
    /// </summary>
    public sealed class Win32MonitorLayout : IMonitorLayout
    {
        private const int MonitorInfoPrimary = 1;
        private const int EffectiveDpi = 0;

        /// <inheritdoc />
        public IReadOnlyList<MonitorInfo> GetMonitors()
        {
            var monitors = new List<MonitorInfo>();
            MonitorEnumProc callback = (IntPtr monitor, IntPtr deviceContext, IntPtr clip, IntPtr data) =>
            {
                AddMonitor(monitor, monitors);
                return true;
            };

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            return monitors;
        }

        #region Private Helpers

        private static void AddMonitor(IntPtr monitor, List<MonitorInfo> monitors)
        {
            var info = new MonitorInfoNative
            {
                StructureSize = Marshal.SizeOf<MonitorInfoNative>()
            };

            if (!GetMonitorInfo(monitor, ref info))
            {
                return;
            }

            PixelRect bounds = ToRect(info.MonitorBounds);
            PixelRect workArea = ToRect(info.WorkBounds);
            if (!workArea.HasArea)
            {
                return;
            }

            bool isPrimary = (info.Flags & MonitorInfoPrimary) != 0;
            monitors.Add(new MonitorInfo(bounds, workArea, ReadDpi(monitor), isPrimary));
        }

        private static int ReadDpi(IntPtr monitor)
        {
            try
            {
                if (GetDpiForMonitor(monitor, EffectiveDpi, out uint dpiX, out uint dpiY) == 0 && dpiX > 0)
                {
                    return (int)dpiX;
                }

                if (dpiY > 0)
                {
                    return (int)dpiY;
                }
            }
            catch (EntryPointNotFoundException)
            {
            }
            catch (DllNotFoundException)
            {
            }

            return 96;
        }

        private static PixelRect ToRect(RectNative rect)
        {
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < 0)
            {
                width = 0;
            }

            if (height < 0)
            {
                height = 0;
            }

            return new PixelRect(rect.Left, rect.Top, width, height);
        }

        private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr deviceContext, IntPtr clipRect, IntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        private struct RectNative
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfoNative
        {
            public int StructureSize;
            public RectNative MonitorBounds;
            public RectNative WorkBounds;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplayMonitors(
            IntPtr deviceContext,
            IntPtr clipRect,
            MonitorEnumProc callback,
            IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoNative info);

        [DllImport("Shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

        #endregion
    }
}

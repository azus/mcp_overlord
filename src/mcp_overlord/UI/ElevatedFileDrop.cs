using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using mcp_overlord.Services.Native;

namespace mcp_overlord.UI
{
    /// <summary>
    /// Lets Explorer drop a file onto one window while this process is elevated.
    /// OLE drag-and-drop cannot cross into a higher integrity process, so the elevated window
    /// accepts the shell <c>WM_DROPFILES</c> message instead. A normal process keeps OLE.
    /// </summary>
    public static class ElevatedFileDrop
    {
        private const int HighIntegrityLevel = 0x3000;
        private const int MaximumPathCharacters = 32767;
        private const uint DropFilesMessage = 0x0233;
        private const uint CopyDataMessage = 0x004A;
        private const uint CopyGlobalDataMessage = 0x0049;
        private const uint AllowMessage = 1;

        /// <summary>
        /// Turns off OLE drop and accepts a shell file drop when this process is elevated.
        /// </summary>
        /// <param name="window">Editor window.</param>
        /// <param name="onFileDropped">Called with the first dropped path.</param>
        /// <returns>True when the shell drop target was installed.</returns>
        public static bool EnableIfElevated(Window window, Action<string> onFileDropped)
        {
            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            if (onFileDropped == null)
            {
                throw new ArgumentNullException(nameof(onFileDropped));
            }

            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero || !IsHighIntegrity())
            {
                return false;
            }

            Attach(window, onFileDropped);
            return true;
        }

        /// <summary>
        /// Allows the three Explorer file-drop messages on <paramref name="windowHandle"/>.
        /// A failed call is reported and otherwise ignored.
        /// </summary>
        /// <param name="windowHandle">Window handle.</param>
        /// <returns>True when Windows accepted every per-window message.</returns>
        internal static bool AllowFileDrop(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                return false;
            }

            AllowProcess(DropFilesMessage);
            AllowProcess(CopyDataMessage);
            AllowProcess(CopyGlobalDataMessage);
            return AllowWindow(windowHandle, DropFilesMessage)
                && AllowWindow(windowHandle, CopyDataMessage)
                && AllowWindow(windowHandle, CopyGlobalDataMessage);
        }

        /// <summary>
        /// Removes the OLE drop target and listens for a shell file drop.
        /// </summary>
        /// <param name="window">Window that accepts the drop.</param>
        /// <param name="onFileDropped">Called with the first dropped path.</param>
        internal static void Attach(Window window, Action<string> onFileDropped)
        {
            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            if (onFileDropped == null)
            {
                throw new ArgumentNullException(nameof(onFileDropped));
            }

            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("The window handle does not exist yet.");
            }

            HwndSource? source = HwndSource.FromHwnd(handle);
            if (source == null)
            {
                throw new InvalidOperationException("The window does not have a window source.");
            }

            window.AllowDrop = false;
            AllowFileDrop(handle);
            RevokeDragDrop(handle);
            AcceptShellFiles(handle);
            ListenForShellDrop(window, source, onFileDropped);
        }

        private static void ListenForShellDrop(Window window, HwndSource source, Action<string> onFileDropped)
        {
            HwndSourceHook hook = (IntPtr windowHandle, int message, IntPtr wordParameter, IntPtr longParameter, ref bool handled) =>
                OnShellMessage(message, wordParameter, onFileDropped, ref handled);
            source.AddHook(hook);
            window.Loaded += (_, _) => AcceptShellFiles(new WindowInteropHelper(window).Handle);
            window.Closed += (_, _) => source.RemoveHook(hook);
        }

        private static IntPtr OnShellMessage(int message, IntPtr wordParameter, Action<string> onFileDropped, ref bool handled)
        {
            if (message == (int)DropFilesMessage)
            {
                ReadDroppedFile(wordParameter, onFileDropped);
                handled = true;
            }

            return IntPtr.Zero;
        }

        private static bool IsHighIntegrity()
        {
            int level = new Win32IntegrityApi().GetCurrentIntegrityLevel();
            return level >= HighIntegrityLevel;
        }

        private static void AcceptShellFiles(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            DragAcceptFiles(windowHandle, true);
        }

        private static void ReadDroppedFile(IntPtr dropHandle, Action<string> onFileDropped)
        {
            try
            {
                uint count = DragQueryFile(dropHandle, uint.MaxValue, IntPtr.Zero, 0);
                if (count == 0)
                {
                    return;
                }

                uint length = DragQueryFile(dropHandle, 0, IntPtr.Zero, 0);
                if (length == 0 || length > MaximumPathCharacters)
                {
                    return;
                }

                int bytes = ((int)length + 1) * sizeof(char);
                IntPtr buffer = Marshal.AllocHGlobal(bytes);
                try
                {
                    if (DragQueryFile(dropHandle, 0, buffer, length + 1) == 0)
                    {
                        return;
                    }

                    string path = Marshal.PtrToStringUni(buffer) ?? string.Empty;
                    if (path.Length > 0)
                    {
                        onFileDropped(path);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                DragFinish(dropHandle);
            }
        }

        #region Native

        private static void AllowProcess(uint message)
        {
            ChangeWindowMessageFilter(message, AllowMessage);
        }

        private static bool AllowWindow(IntPtr windowHandle, uint message)
        {
            var change = new ChangeFilter
            {
                Size = (uint)Marshal.SizeOf<ChangeFilter>()
            };
            return ChangeWindowMessageFilterEx(windowHandle, message, AllowMessage, ref change);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ChangeFilter
        {
            public uint Size;
            public uint ExtendedStatus;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ChangeWindowMessageFilter(uint message, uint flag);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ChangeWindowMessageFilterEx(
            IntPtr windowHandle,
            uint message,
            uint action,
            ref ChangeFilter change);

        [DllImport("ole32.dll")]
        private static extern int RevokeDragDrop(IntPtr windowHandle);

        [DllImport("shell32.dll")]
        private static extern void DragAcceptFiles(IntPtr windowHandle, [MarshalAs(UnmanagedType.Bool)] bool accept);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "DragQueryFileW")]
        private static extern uint DragQueryFile(IntPtr dropHandle, uint fileIndex, IntPtr fileName, uint bufferLength);

        [DllImport("shell32.dll")]
        private static extern void DragFinish(IntPtr dropHandle);

        #endregion
    }
}

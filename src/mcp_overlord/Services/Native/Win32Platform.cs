using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using game_automation;
using game_automation_in_csharp.util;

namespace mcp_overlord.Services.Native
{
    /// <summary>Enumerates top-level windows through <see cref="WindowSearch"/>.</summary>
    public sealed class Win32WindowEnumerator : IWindowEnumerator
    {
        /// <inheritdoc />
        public IReadOnlyList<WindowCandidate> Enumerate()
        {
            return WindowSearch.Enumerate();
        }
    }

    /// <summary>Process list and process start through <see cref="Process"/>.</summary>
    public sealed class Win32ProcessQuery : IProcessQuery
    {
        /// <inheritdoc />
        public IReadOnlyList<ProcessSnapshot> GetProcesses()
        {
            var processes = new List<ProcessSnapshot>();
            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    processes.Add(new ProcessSnapshot
                    {
                        Id = process.Id,
                        Name = process.ProcessName,
                        MainWindowHandle = process.MainWindowHandle,
                        MainWindowTitle = process.MainWindowTitle
                    });
                }
                catch (Exception)
                {
                    // The process can exit while it is being read.
                }
                finally
                {
                    process.Dispose();
                }
            }

            return processes;
        }

        /// <inheritdoc />
        public bool IsAlive(int processId)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                return !process.HasExited;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <inheritdoc />
        public string? GetProcessName(int processId)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                return process.ProcessName;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>Starts a process with <c>UseShellExecute = false</c> so the argument string is passed through.</summary>
    public sealed class Win32ProcessStarter : IProcessStarter
    {
        /// <inheritdoc />
        public int Start(ProcessStartRequest request)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = request.FileName,
                Arguments = request.Arguments ?? "",
                UseShellExecute = false
            };

            if (!string.IsNullOrEmpty(request.WorkingDirectory))
            {
                startInfo.WorkingDirectory = request.WorkingDirectory;
            }

            Process? process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Process.Start returned no process.");
            }

            return process.Id;
        }
    }

    /// <summary>Window geometry and restore.</summary>
    public sealed class Win32WindowApi : INativeWindowApi
    {
        private const int SW_RESTORE = 9;

        /// <inheritdoc />
        public bool IsWindow(IntPtr hwnd)
        {
            return IsWindowNative(hwnd);
        }

        /// <inheritdoc />
        public string GetTitle(IntPtr hwnd)
        {
            return NativeWindowText.Read(hwnd);
        }

        /// <inheritdoc />
        public int GetProcessId(IntPtr hwnd)
        {
            GetWindowThreadProcessId(hwnd, out uint processId);
            return (int)processId;
        }

        /// <inheritdoc />
        public bool IsMinimized(IntPtr hwnd)
        {
            return IsIconic(hwnd);
        }

        /// <inheritdoc />
        public bool IsMaximized(IntPtr hwnd)
        {
            return IsZoomed(hwnd);
        }

        /// <inheritdoc />
        public (int Width, int Height) GetClientSize(IntPtr hwnd)
        {
            RECT rect = new RECT();
            if (!GetClientRect(hwnd, ref rect))
            {
                return (0, 0);
            }

            return (Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
        }

        /// <inheritdoc />
        public (int X, int Y, int Width, int Height) GetWindowRect(IntPtr hwnd)
        {
            RECT rect = new RECT();
            if (!GetWindowRectNative(hwnd, ref rect))
            {
                return (0, 0, 0, 0);
            }

            return (rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
        }

        /// <inheritdoc />
        public WindowFrameGeometry GetFrameGeometry(IntPtr hwnd)
        {
            RECT window = new RECT();
            if (!GetWindowRectNative(hwnd, ref window))
            {
                return new WindowFrameGeometry(0, 0, 0, 0, 0, 0, 0, 0);
            }

            int frameWidth = Math.Max(0, window.Right - window.Left);
            int frameHeight = Math.Max(0, window.Bottom - window.Top);
            POINT origin = new POINT();
            bool hasOrigin = ClientToScreen(hwnd, ref origin);
            RECT client = new RECT();
            int clientWidth = 0;
            int clientHeight = 0;
            if (GetClientRect(hwnd, ref client))
            {
                clientWidth = Math.Max(0, client.Right - client.Left);
                clientHeight = Math.Max(0, client.Bottom - client.Top);
            }

            int offsetX = hasOrigin ? origin.X - window.Left : 0;
            int offsetY = hasOrigin ? origin.Y - window.Top : 0;
            return new WindowFrameGeometry(
                window.Left,
                window.Top,
                frameWidth,
                frameHeight,
                offsetX,
                offsetY,
                clientWidth,
                clientHeight);
        }

        /// <inheritdoc />
        public void Restore(IntPtr hwnd)
        {
            ShowWindow(hwnd, SW_RESTORE);
        }

        [DllImport("user32.dll", EntryPoint = "IsWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowNative(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsZoomed(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hwnd, ref RECT rect);

        [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRectNative(IntPtr hwnd, ref RECT rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hwnd, int command);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }
    }

    /// <summary>Window-device-context capture, PrintWindow, and screen blit.</summary>
    public sealed class Win32WindowCapture : IWindowCapture
    {
        private readonly ScreenshotGenerator _generator = new ScreenshotGenerator();
        private readonly FastWindowSnapshot _snapshot = new FastWindowSnapshot();

        /// <inheritdoc />
        public Bitmap CapturePrintWindow(IntPtr hwnd, uint flags)
        {
            return _generator.CaptureWindow(hwnd, flags);
        }

        /// <inheritdoc />
        public Bitmap CaptureScreen(IntPtr hwnd)
        {
            return _generator.CaptureWindowFromScreen(hwnd);
        }

        /// <inheritdoc />
        public Bitmap? CaptureOwned(IntPtr hwnd, out string captureMethod)
        {
            return _snapshot.CopyBitmap(hwnd, out captureMethod);
        }

        /// <inheritdoc />
        public bool IsCoveredByAnotherWindow(IntPtr hwnd)
        {
            return WindowOverlap.IsCovered(hwnd);
        }

        /// <inheritdoc />
        public bool TryCaptureFastPng(IntPtr hwnd, WindowFrameGeometry geometry, out byte[] png, out string captureMethod)
        {
            return _snapshot.TryCaptureFastPng(hwnd, geometry, out png, out captureMethod);
        }
    }

    /// <summary>Keyboard input through <see cref="KeyboardUtil"/>.</summary>
    public sealed class Win32KeyboardSender : IKeyboardSender
    {
        /// <inheritdoc />
        public void KeyDown(IntPtr hwnd, byte virtualKey)
        {
            KeyboardUtil.KeyDown(hwnd, virtualKey);
        }

        /// <inheritdoc />
        public void KeyUp(IntPtr hwnd, byte virtualKey)
        {
            KeyboardUtil.KeyUp(hwnd, virtualKey);
        }

        /// <inheritdoc />
        public void Press(IntPtr hwnd, byte virtualKey, int holdMilliseconds)
        {
            KeyboardUtil.Press(hwnd, virtualKey, holdMilliseconds);
        }

        /// <inheritdoc />
        public void TypeUnicode(IntPtr hwnd, string text)
        {
            KeyboardUtil.TypeUnicode(hwnd, text);
        }
    }

    /// <summary>Foreground Win32 calls, including AttachThreadInput.</summary>
    public sealed class Win32FocusApi : IFocusApi
    {
        /// <inheritdoc />
        public bool SetForegroundWindow(IntPtr hwnd)
        {
            return SetForegroundWindowNative(hwnd);
        }

        /// <inheritdoc />
        public IntPtr GetForegroundWindow()
        {
            return GetForegroundWindowNative();
        }

        /// <inheritdoc />
        public uint GetWindowThreadId(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return 0;
            }

            return GetWindowThreadProcessId(hwnd, out _);
        }

        /// <inheritdoc />
        public uint GetCurrentThreadId()
        {
            return GetCurrentThreadIdNative();
        }

        /// <inheritdoc />
        public bool AttachThreadInput(uint sourceThreadId, uint targetThreadId, bool attach)
        {
            return AttachThreadInputNative(sourceThreadId, targetThreadId, attach);
        }

        /// <inheritdoc />
        public void BringWindowToTop(IntPtr hwnd)
        {
            BringWindowToTopNative(hwnd);
        }

        /// <inheritdoc />
        public void SetFocus(IntPtr hwnd)
        {
            SetFocusNative(hwnd);
        }

        [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindowNative(IntPtr hwnd);

        [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
        private static extern IntPtr GetForegroundWindowNative();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
        private static extern uint GetCurrentThreadIdNative();

        [DllImport("user32.dll", EntryPoint = "AttachThreadInput")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachThreadInputNative(uint sourceThreadId, uint targetThreadId, bool attach);

        [DllImport("user32.dll", EntryPoint = "BringWindowToTop")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BringWindowToTopNative(IntPtr hwnd);

        [DllImport("user32.dll", EntryPoint = "SetFocus")]
        private static extern IntPtr SetFocusNative(IntPtr hwnd);
    }

    /// <summary>Reads the mandatory integrity label of a process token.</summary>
    public sealed class Win32IntegrityApi : IIntegrityApi
    {
        private const uint TOKEN_QUERY = 0x0008;
        private const int TOKEN_INTEGRITY_LEVEL = 25;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        /// <inheritdoc />
        public int GetIntegrityLevel(int processId)
        {
            IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (process == IntPtr.Zero)
            {
                return -1;
            }

            try
            {
                return ReadIntegrity(process);
            }
            finally
            {
                CloseHandle(process);
            }
        }

        /// <inheritdoc />
        public int GetCurrentIntegrityLevel()
        {
            return ReadIntegrity(GetCurrentProcess());
        }

        private static int ReadIntegrity(IntPtr process)
        {
            if (!OpenProcessToken(process, TOKEN_QUERY, out IntPtr token))
            {
                return -1;
            }

            try
            {
                GetTokenInformation(token, TOKEN_INTEGRITY_LEVEL, IntPtr.Zero, 0, out uint length);
                if (length == 0)
                {
                    return -1;
                }

                IntPtr buffer = Marshal.AllocHGlobal((int)length);
                try
                {
                    if (!GetTokenInformation(token, TOKEN_INTEGRITY_LEVEL, buffer, length, out _))
                    {
                        return -1;
                    }

                    IntPtr sid = Marshal.ReadIntPtr(buffer);
                    IntPtr countPointer = GetSidSubAuthorityCount(sid);
                    int count = Marshal.ReadByte(countPointer);
                    if (count <= 0)
                    {
                        return -1;
                    }

                    IntPtr authority = GetSidSubAuthority(sid, (uint)(count - 1));
                    return Marshal.ReadInt32(authority);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseHandle(token);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetTokenInformation(IntPtr token, int informationClass, IntPtr buffer, uint length, out uint returnLength);

        [DllImport("advapi32.dll")]
        private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);

        [DllImport("advapi32.dll")]
        private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }

    /// <summary>
    /// Workstation lock through the session lock flag, the input desktop, and <c>LockWorkStation</c>.
    /// The lock screen on Windows 10 and 11 stays on the Default desktop, so the session flag is required.
    /// A secure desktop such as a UAC prompt still fails the desktop open.
    /// </summary>
    public sealed class Win32WorkstationLock : IWorkstationLock
    {
        private const uint DESKTOP_READOBJECTS = 0x0001;
        private const uint DESKTOP_SWITCHDESKTOP = 0x0100;
        private const int UOI_NAME = 2;
        private const int NAME_BUFFER_BYTES = 512;
        private const int WTS_CURRENT_SESSION = -1;
        private const int WTS_SESSION_INFO_EX = 25;
        private const int WTS_INFO_LEVEL_1 = 1;
        private const int WTS_SESSIONSTATE_LOCK = 0;

        /// <summary>
        /// Byte offset of <c>SessionFlags</c> in <c>WTSINFOEXW</c>.
        /// The struct is 8-byte aligned: a 4-byte level, 4 bytes of padding, then session id, connect state, and flags.
        /// </summary>
        private const int SESSION_FLAGS_OFFSET = 16;

        /// <inheritdoc />
        public bool IsLocked()
        {
            int? sessionFlags = ReadSessionFlags();
            if (sessionFlags == WTS_SESSIONSTATE_LOCK)
            {
                return true;
            }

            bool desktopOpened = TryReadInputDesktopName(out string? desktopName);
            return IsLockedState(sessionFlags, desktopOpened, desktopName);
        }

        /// <summary>
        /// Combines the session lock flag with the input desktop.
        /// A null flag means the query failed or the state is unknown, which is not a lock by itself.
        /// </summary>
        /// <param name="sessionFlags">Session flags, or null when they could not be read.</param>
        /// <param name="inputDesktopOpened">True when the input desktop handle opened.</param>
        /// <param name="desktopName">Desktop name when the handle opened. Null when the name could not be read.</param>
        /// <returns>True when mouse and keyboard input must be refused.</returns>
        internal static bool IsLockedState(int? sessionFlags, bool inputDesktopOpened, string? desktopName)
        {
            if (sessionFlags == WTS_SESSIONSTATE_LOCK)
            {
                return true;
            }

            if (!inputDesktopOpened)
            {
                return true;
            }

            return !string.Equals(desktopName, "Default", StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public bool TryLock(out string? error)
        {
            if (LockWorkStation())
            {
                error = null;
                return true;
            }

            error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }

        private static bool TryReadInputDesktopName(out string? desktopName)
        {
            desktopName = null;
            IntPtr desktop = OpenInputDesktop(0, false, DESKTOP_READOBJECTS | DESKTOP_SWITCHDESKTOP);
            if (desktop == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                desktopName = ReadDesktopName(desktop);
                return true;
            }
            finally
            {
                CloseDesktop(desktop);
            }
        }

        private static string? ReadDesktopName(IntPtr desktop)
        {
            IntPtr buffer = Marshal.AllocHGlobal(NAME_BUFFER_BYTES);
            try
            {
                if (!GetUserObjectInformation(desktop, UOI_NAME, buffer, NAME_BUFFER_BYTES, out _))
                {
                    return null;
                }

                return Marshal.PtrToStringUni(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        /// Reads <c>WTSINFOEX</c> session flags for this session.
        /// Failure, a short buffer, and a level other than 1 return null so an unread zero is not treated as locked.
        /// </summary>
        private static int? ReadSessionFlags()
        {
            if (!WTSQuerySessionInformation(IntPtr.Zero, WTS_CURRENT_SESSION, WTS_SESSION_INFO_EX, out IntPtr buffer, out int bytesReturned))
            {
                return null;
            }

            try
            {
                if (buffer == IntPtr.Zero || bytesReturned < SESSION_FLAGS_OFFSET + sizeof(int))
                {
                    return null;
                }

                if (Marshal.ReadInt32(buffer) != WTS_INFO_LEVEL_1)
                {
                    return null;
                }

                return Marshal.ReadInt32(buffer, SESSION_FLAGS_OFFSET);
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    WTSFreeMemory(buffer);
                }
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint desiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseDesktop(IntPtr desktop);

        [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetUserObjectInformation(IntPtr desktop, int index, IntPtr information, int length, out int lengthNeeded);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LockWorkStation();

        [DllImport("wtsapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytesReturned);

        [DllImport("wtsapi32.dll")]
        private static extern void WTSFreeMemory(IntPtr memory);
    }

    /// <summary>Thread sleep used by launch polling and window restore.</summary>
    public sealed class ThreadDelay : IDelay
    {
        /// <inheritdoc />
        public void Wait(TimeSpan delay)
        {
            if (delay > TimeSpan.Zero)
            {
                Thread.Sleep(delay);
            }
        }
    }
}

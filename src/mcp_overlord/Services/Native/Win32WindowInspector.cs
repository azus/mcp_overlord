using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using game_automation;

namespace mcp_overlord.Services.Native
{
    /// <summary>
    /// Window and process inspection through user32 and the process environment block.
    /// The environment block supplies the command line and the working directory.
    /// </summary>
    public sealed class Win32WindowInspector : IWindowInspector
    {
        private const uint GaRoot = 2;
        private const uint ProcessQueryInformation = 0x0400;
        private const uint ProcessVmRead = 0x0010;
        private const int ProcessBasicInformationClass = 0;
        private const int ProcessWow64InformationClass = 26;

        /// <inheritdoc />
        public IntPtr GetTopLevelWindowAt(int x, int y)
        {
            var point = new PointNative
            {
                X = x,
                Y = y
            };
            IntPtr window = WindowFromPoint(point);
            if (window == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr root = GetAncestor(window, GaRoot);
            if (root == IntPtr.Zero)
            {
                root = window;
            }

            GetWindowThreadProcessId(root, out uint processId);
            if (processId == 0 || processId == (uint)Environment.ProcessId)
            {
                return IntPtr.Zero;
            }

            return root;
        }

        /// <inheritdoc />
        public bool TryDescribe(IntPtr hwnd, out WindowInfo info)
        {
            info = new WindowInfo();
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            {
                return false;
            }

            GetWindowThreadProcessId(hwnd, out uint processId);
            TryReadProcess((int)processId, out string executable, out string processName, out string? workingDirectory, out string? arguments);
            info = new WindowInfo
            {
                Title = ReadTitle(hwnd),
                ProcessName = processName,
                ExecutablePath = executable,
                WorkingDirectory = workingDirectory,
                Arguments = arguments
            };
            return true;
        }

        /// <inheritdoc />
        public bool TryGetWindowRect(IntPtr hwnd, out int x, out int y, out int width, out int height)
        {
            x = 0;
            y = 0;
            width = 0;
            height = 0;
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out RectNative rect))
            {
                return false;
            }

            width = rect.Right - rect.Left;
            height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            x = rect.Left;
            y = rect.Top;
            return true;
        }

        /// <inheritdoc />
        public bool TryReadProcess(
            int processId,
            out string executablePath,
            out string processName,
            out string? workingDirectory,
            out string? arguments)
        {
            executablePath = "";
            processName = ReadProcessName(processId);
            workingDirectory = null;
            arguments = null;
            if (processId <= 0)
            {
                return false;
            }

            IntPtr handle = OpenProcessHandle(processId);
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            bool closeHandle = processId != Environment.ProcessId;
            try
            {
                executablePath = ReadImagePath(handle);
                ReadEnvironment(handle, out workingDirectory, out string? commandLine);
                if (commandLine != null)
                {
                    arguments = CommandLineSplitter.GetArguments(commandLine);
                }

                return !string.IsNullOrEmpty(executablePath);
            }
            finally
            {
                if (closeHandle)
                {
                    CloseHandle(handle);
                }
            }
        }

        private static IntPtr OpenProcessHandle(int processId)
        {
            if (processId == Environment.ProcessId)
            {
                return GetCurrentProcess();
            }

            return OpenProcess(ProcessQueryInformation | ProcessVmRead, false, processId);
        }

        private static string ReadProcessName(int processId)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                return process.ProcessName;
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static string ReadTitle(IntPtr hwnd)
        {
            return NativeWindowText.Read(hwnd);
        }

        private static string ReadImagePath(IntPtr process)
        {
            int capacity = 1024;
            var buffer = new StringBuilder(capacity);
            if (!QueryFullProcessImageName(process, 0, buffer, ref capacity))
            {
                return "";
            }

            return buffer.ToString();
        }

        private static void ReadEnvironment(IntPtr process, out string? workingDirectory, out string? commandLine)
        {
            workingDirectory = null;
            commandLine = null;
            bool wow64 = IsWow64(process);
            IntPtr parameters = wow64 ? ReadWow64Parameters(process) : ReadNativeParameters(process);
            if (parameters == IntPtr.Zero)
            {
                return;
            }

            if (wow64)
            {
                workingDirectory = NormalizeDirectory(ReadUnicodeString(process, parameters + 0x24, true));
                commandLine = ReadUnicodeString(process, parameters + 0x40, true);
                return;
            }

            workingDirectory = NormalizeDirectory(ReadUnicodeString(process, parameters + 0x38, false));
            commandLine = ReadUnicodeString(process, parameters + 0x70, false);
        }

        private static IntPtr ReadNativeParameters(IntPtr process)
        {
            int size = IntPtr.Size * 6;
            byte[] buffer = new byte[size];
            if (NtQueryInformationProcess(process, ProcessBasicInformationClass, buffer, buffer.Length, out _) != 0)
            {
                return IntPtr.Zero;
            }

            IntPtr peb = ReadPointer(buffer, IntPtr.Size);
            if (peb == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            byte[] parameters = new byte[IntPtr.Size];
            if (!ReadProcessMemory(process, peb + 0x20, parameters, parameters.Length, out _))
            {
                return IntPtr.Zero;
            }

            return ReadPointer(parameters, 0);
        }

        private static IntPtr ReadWow64Parameters(IntPtr process)
        {
            byte[] pebBuffer = new byte[IntPtr.Size];
            if (NtQueryInformationProcess(process, ProcessWow64InformationClass, pebBuffer, pebBuffer.Length, out _) != 0)
            {
                return IntPtr.Zero;
            }

            IntPtr peb = ReadPointer(pebBuffer, 0);
            if (peb == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            byte[] parameters = new byte[4];
            if (!ReadProcessMemory(process, peb + 0x10, parameters, parameters.Length, out _))
            {
                return IntPtr.Zero;
            }

            return new IntPtr(BitConverter.ToInt32(parameters, 0));
        }

        private static string? ReadUnicodeString(IntPtr process, IntPtr address, bool wow64)
        {
            int headerSize = wow64 ? 8 : 16;
            byte[] header = new byte[headerSize];
            if (!ReadProcessMemory(process, address, header, header.Length, out _))
            {
                return null;
            }

            int length = BitConverter.ToUInt16(header, 0);
            if (length <= 0)
            {
                return "";
            }

            long bufferValue = wow64
                ? BitConverter.ToUInt32(header, 4)
                : BitConverter.ToInt64(header, 8);
            if (bufferValue == 0)
            {
                return "";
            }

            byte[] data = new byte[length];
            if (!ReadProcessMemory(process, new IntPtr(bufferValue), data, data.Length, out int read) || read <= 0)
            {
                return null;
            }

            return Encoding.Unicode.GetString(data, 0, read).TrimEnd('\0');
        }

        private static IntPtr ReadPointer(byte[] buffer, int offset)
        {
            if (IntPtr.Size == 8)
            {
                return new IntPtr(BitConverter.ToInt64(buffer, offset));
            }

            return new IntPtr(BitConverter.ToInt32(buffer, offset));
        }

        private static string? NormalizeDirectory(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.IsNullOrEmpty(path) ? path : null;
            }

            string value = path;
            if (value.StartsWith(@"\??\", StringComparison.Ordinal))
            {
                value = value.Substring(4);
            }

            if (value.Length > 3)
            {
                value = value.TrimEnd('\\', '/');
            }

            return value;
        }

        private static bool IsWow64(IntPtr process)
        {
            try
            {
                return IsWow64Process(process, out bool wow64) && wow64;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PointNative
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RectNative
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(PointNative point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, uint flags);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr window, out RectNative rect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out int read);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWow64Process(IntPtr process, out bool wow64);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(
            IntPtr process,
            int informationClass,
            byte[] buffer,
            int length,
            out int returnLength);
    }
}

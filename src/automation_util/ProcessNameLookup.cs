using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace game_automation
{
    /// <summary>
    /// Resolves a process name from its image path. The result is cached by process id.
    /// </summary>
    public static class ProcessNameLookup
    {
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        private static readonly ConcurrentDictionary<int, string> Names = new ConcurrentDictionary<int, string>();

        /// <summary>
        /// Returns the process name without the .exe extension. An empty string means the process could not be read.
        /// </summary>
        /// <param name="processId">Process id. Zero or negative returns an empty string.</param>
        /// <returns>The image name without extension.</returns>
        public static string Read(int processId)
        {
            if (processId <= 0)
            {
                return "";
            }

            if (Names.TryGetValue(processId, out string? cached))
            {
                return cached;
            }

            string name = Query(processId);
            Names[processId] = name;
            return name;
        }

        private static string Query(int processId)
        {
            IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
            if (handle == IntPtr.Zero)
            {
                return "";
            }

            try
            {
                var buffer = new StringBuilder(1024);
                int size = buffer.Capacity;
                if (!QueryFullProcessImageName(handle, 0, buffer, ref size) || size <= 0)
                {
                    return "";
                }

                return Path.GetFileNameWithoutExtension(buffer.ToString());
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}

using System.Runtime.InteropServices;
using System.Text;

namespace mcp_overlord.Services.Native
{
    /// <summary>
    /// Reads and writes .lnk files through IShellLinkW. Must be called on an STA thread.
    /// </summary>
    public sealed class Win32ShortcutReader : IShortcutReader
    {
        /// <inheritdoc />
        public bool TryRead(string path, out ShortcutInfo info, out string? error)
        {
            info = new ShortcutInfo();
            error = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "A shortcut path is required.";
                return false;
            }

            if (!File.Exists(path))
            {
                error = "Shortcut file was not found: " + path;
                return false;
            }

            try
            {
                var link = (IShellLinkW)new ShellLink();
                ((IPersistFile)link).Load(path, 0);
                string target = ReadPath(link);
                string arguments = ReadArguments(link);
                string workingDirectory = ReadWorkingDirectory(link);
                string description = ReadDescription(link);
                string? processName = string.IsNullOrEmpty(target)
                    ? null
                    : Path.GetFileNameWithoutExtension(target);
                info = new ShortcutInfo
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    TargetPath = string.IsNullOrEmpty(target) ? null : target,
                    Arguments = arguments,
                    WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? null : workingDirectory,
                    Description = string.IsNullOrEmpty(description) ? null : description,
                    ProcessName = processName
                };
                return true;
            }
            catch (Exception exception)
            {
                error = "The shortcut could not be read: " + exception.Message;
                return false;
            }
        }

        /// <summary>
        /// Writes a .lnk file. Tests use this to build a shortcut without a shell UI.
        /// </summary>
        /// <param name="path">Destination .lnk path.</param>
        /// <param name="targetPath">Executable the shortcut opens.</param>
        /// <param name="arguments">Argument string. Empty when there are none.</param>
        /// <param name="workingDirectory">Working directory. Empty when unset.</param>
        /// <param name="description">Description. Empty when unset.</param>
        public static void Write(string path, string targetPath, string arguments, string workingDirectory, string description)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A shortcut path is required.", nameof(path));
            }

            var link = (IShellLinkW)new ShellLink();
            link.SetPath(targetPath ?? "");
            link.SetArguments(arguments ?? "");
            link.SetWorkingDirectory(workingDirectory ?? "");
            link.SetDescription(description ?? "");
            ((IPersistFile)link).Save(path, true);
        }

        private static string ReadPath(IShellLinkW link)
        {
            var buffer = new StringBuilder(1024);
            link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);
            return buffer.ToString();
        }

        private static string ReadArguments(IShellLinkW link)
        {
            var buffer = new StringBuilder(1024);
            link.GetArguments(buffer, buffer.Capacity);
            return buffer.ToString();
        }

        private static string ReadWorkingDirectory(IShellLinkW link)
        {
            var buffer = new StringBuilder(1024);
            link.GetWorkingDirectory(buffer, buffer.Capacity);
            return buffer.ToString();
        }

        private static string ReadDescription(IShellLinkW link)
        {
            var buffer = new StringBuilder(1024);
            link.GetDescription(buffer, buffer.Capacity);
            return buffer.ToString();
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int capacity, IntPtr findData, uint flags);

            void GetIDList(out IntPtr idList);

            void SetIDList(IntPtr idList);

            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int capacity);

            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int capacity);

            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int capacity);

            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

            void GetHotkey(out short hotkey);

            void SetHotkey(short hotkey);

            void GetShowCmd(out int showCommand);

            void SetShowCmd(int showCommand);

            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int capacity, out int iconIndex);

            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);

            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);

            void Resolve(IntPtr window, uint flags);

            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("0000010B-0000-0000-C000-000000000046")]
        private interface IPersistFile
        {
            void GetClassID(out Guid classId);

            [PreserveSig]
            int IsDirty();

            void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);

            void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, bool remember);

            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);

            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
        }
    }
}

namespace mcp_overlord.Services
{
    /// <summary>
    /// Values taken from a shortcut or an executable the user dropped on the editor.
    /// A null property was not provided and should be left unchanged.
    /// </summary>
    public sealed class ShortcutInfo
    {
        /// <summary>Suggested application name.</summary>
        public string? Name { get; init; }

        /// <summary>Target executable.</summary>
        public string? TargetPath { get; init; }

        /// <summary>Arguments stored in the shortcut. Null when the file has none to apply.</summary>
        public string? Arguments { get; init; }

        /// <summary>Working directory.</summary>
        public string? WorkingDirectory { get; init; }

        /// <summary>Shortcut description.</summary>
        public string? Description { get; init; }

        /// <summary>Process name inferred from the target file. Null when there is no target.</summary>
        public string? ProcessName { get; init; }
    }

    /// <summary>Reads a .lnk file.</summary>
    public interface IShortcutReader
    {
        /// <summary>
        /// Reads <paramref name="path"/>.
        /// </summary>
        /// <param name="path">Shortcut path.</param>
        /// <param name="info">The shortcut when the method returns true.</param>
        /// <param name="error">Message when the method returns false.</param>
        /// <returns>True when the shortcut was read.</returns>
        bool TryRead(string path, out ShortcutInfo info, out string? error);
    }

    /// <summary>
    /// Turns a dropped .lnk or .exe into a <see cref="ShortcutInfo"/>.
    /// </summary>
    public static class ShortcutFile
    {
        /// <summary>
        /// Reads a shortcut, or builds the info for an executable.
        /// </summary>
        /// <param name="path">Dropped file.</param>
        /// <param name="reader">Shortcut reader used for .lnk files.</param>
        /// <param name="info">The values to apply.</param>
        /// <param name="error">Message when the file cannot be used.</param>
        /// <returns>True when <paramref name="info"/> is filled.</returns>
        public static bool TryRead(string path, IShortcutReader reader, out ShortcutInfo info, out string? error)
        {
            info = new ShortcutInfo();
            error = null;
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Drop a shortcut (.lnk) or an application (.exe).";
                return false;
            }

            string extension = Path.GetExtension(path);
            if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                string fullPath = Path.GetFullPath(path);
                info = new ShortcutInfo
                {
                    Name = Path.GetFileNameWithoutExtension(fullPath),
                    TargetPath = fullPath,
                    WorkingDirectory = Path.GetDirectoryName(fullPath),
                    ProcessName = Path.GetFileNameWithoutExtension(fullPath)
                };
                return true;
            }

            if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                return reader.TryRead(path, out info, out error);
            }

            error = "Drop a shortcut (.lnk) or an application (.exe).";
            return false;
        }
    }
}

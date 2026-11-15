namespace mcp_overlord.Services
{
    /// <summary>
    /// A top-level window and the process that owns it.
    /// Null strings could not be read and should be left unchanged in the editor.
    /// </summary>
    public sealed class WindowInfo
    {
        /// <summary>Window title.</summary>
        public string Title { get; init; } = "";

        /// <summary>Process name without .exe.</summary>
        public string ProcessName { get; init; } = "";

        /// <summary>Full executable path.</summary>
        public string ExecutablePath { get; init; } = "";

        /// <summary>Process working directory, or null when it could not be read.</summary>
        public string? WorkingDirectory { get; init; }

        /// <summary>Arguments from the process command line, or null when the command line could not be read.</summary>
        public string? Arguments { get; init; }
    }

    /// <summary>
    /// Identifies the window under a screen point and reads its process, the way Process Explorer does.
    /// </summary>
    public interface IWindowInspector
    {
        /// <summary>
        /// Returns the top-level window that contains the physical point.
        /// Windows owned by this process are ignored so the editor cannot capture itself.
        /// </summary>
        /// <param name="x">Physical screen x.</param>
        /// <param name="y">Physical screen y.</param>
        /// <returns>The window handle, or zero.</returns>
        IntPtr GetTopLevelWindowAt(int x, int y);

        /// <summary>
        /// Reads the title and the owning process.
        /// </summary>
        /// <param name="hwnd">Window handle.</param>
        /// <param name="info">The description when the method returns true.</param>
        /// <returns>True when the window still exists.</returns>
        bool TryDescribe(IntPtr hwnd, out WindowInfo info);

        /// <summary>
        /// Reads the outer rectangle in physical pixels.
        /// </summary>
        /// <param name="hwnd">Window handle.</param>
        /// <param name="x">Left edge.</param>
        /// <param name="y">Top edge.</param>
        /// <param name="width">Width.</param>
        /// <param name="height">Height.</param>
        /// <returns>True when the rectangle has an area.</returns>
        bool TryGetWindowRect(IntPtr hwnd, out int x, out int y, out int width, out int height);

        /// <summary>
        /// Reads an executable path, working directory, and arguments for a process.
        /// </summary>
        /// <param name="processId">Process id.</param>
        /// <param name="executablePath">Win32 path from QueryFullProcessImageName. Empty when it cannot be read.</param>
        /// <param name="processName">Process name without .exe.</param>
        /// <param name="workingDirectory">Current directory, or null.</param>
        /// <param name="arguments">Command-line arguments, or null when the command line cannot be read.</param>
        /// <returns>True when the executable path was read.</returns>
        bool TryReadProcess(
            int processId,
            out string executablePath,
            out string processName,
            out string? workingDirectory,
            out string? arguments);
    }
}

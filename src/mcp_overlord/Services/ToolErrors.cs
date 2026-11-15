namespace mcp_overlord.Services
{
    /// <summary>
    /// Tool error messages written for the model. Each one says what happened and the useful next step.
    /// </summary>
    public static class ToolErrors
    {
        /// <summary>No configuration has been loaded.</summary>
        public const string NoConfiguration = "No configuration is loaded.";

        /// <summary>
        /// The application name is not in the snapshot.
        /// </summary>
        /// <param name="name">The name the caller sent.</param>
        /// <param name="knownNames">Configured names.</param>
        /// <returns>The error text.</returns>
        public static string UnknownName(string name, IReadOnlyList<string> knownNames)
        {
            string known = knownNames.Count == 0 ? "(none)" : string.Join(", ", knownNames);
            return $"Unknown application '{name}'. Configured names: {known}.";
        }

        /// <summary>The entry has no executable.</summary>
        /// <param name="name">Application name.</param>
        /// <returns>The error text.</returns>
        public static string NotLaunchable(string name)
        {
            return $"Application '{name}' is not launchable. It has no executable in the configuration.";
        }

        /// <summary>Process.Start failed.</summary>
        /// <param name="name">Application name.</param>
        /// <param name="osMessage">Message from the operating system.</param>
        /// <returns>The error text.</returns>
        public static string StartFailed(string name, string osMessage)
        {
            return $"Failed to launch '{name}': {osMessage}";
        }

        /// <summary>A screenshot or input tool found no window.</summary>
        /// <param name="name">Application name.</param>
        /// <param name="launchable">True when the entry has an executable.</param>
        /// <returns>The error text.</returns>
        public static string WindowNotAvailable(string name, bool launchable)
        {
            if (launchable)
            {
                return $"The window for '{name}' was not found. Call launch_application, then retry.";
            }

            return $"The window for '{name}' was not found. Call find_window for the current state.";
        }

        /// <summary>The requested crop misses the window rectangle.</summary>
        /// <param name="windowWidth">Window width.</param>
        /// <param name="windowHeight">Window height.</param>
        /// <returns>The error text.</returns>
        public static string RegionMiss(int windowWidth, int windowHeight)
        {
            return $"The region does not intersect the window ({windowWidth}x{windowHeight}).";
        }

        /// <summary>The captured bitmap is not the size of <c>GetWindowRect</c>.</summary>
        /// <param name="bitmapWidth">Captured width.</param>
        /// <param name="bitmapHeight">Captured height.</param>
        /// <param name="windowWidth">Window width.</param>
        /// <param name="windowHeight">Window height.</param>
        /// <returns>The error text.</returns>
        public static string CaptureSizeMismatch(int bitmapWidth, int bitmapHeight, int windowWidth, int windowHeight)
        {
            return $"The capture is {bitmapWidth}x{bitmapHeight} but the window is {windowWidth}x{windowHeight}. The window may have changed size, or the capture DPI does not match the window. Retry take_screenshot.";
        }

        /// <summary>Encoding or capture failed.</summary>
        /// <param name="message">Exception message.</param>
        /// <returns>The error text.</returns>
        public static string CaptureFailed(string message)
        {
            return "Screenshot encode failed: " + message;
        }

        /// <summary>The key name is not in the table.</summary>
        /// <param name="key">The name the caller sent.</param>
        /// <returns>The error text.</returns>
        public static string UnknownKey(string key)
        {
            return $"Key '{key}' was not recognized.";
        }

        /// <summary>text is empty or longer than 500 characters.</summary>
        /// <param name="length">The length that was sent.</param>
        /// <returns>The error text.</returns>
        public static string TextLength(int length)
        {
            return $"text must be between 1 and 500 characters. The value has length {length}.";
        }

        /// <summary>The tool call exceeded the 15 second deadline.</summary>
        public const string TimedOut = "The tool did not finish within 15 seconds.";

        /// <summary>Mouse or keyboard input was refused because the workstation is locked.</summary>
        public const string ScreenLocked =
            "The workstation is locked, so mouse and keyboard input was not sent. Unlock the screen at the console. This server has no unlock tool.";

        /// <summary><c>LockWorkStation</c> failed.</summary>
        /// <param name="osMessage">Message from the operating system.</param>
        /// <returns>The error text.</returns>
        public static string LockFailed(string? osMessage)
        {
            string detail = string.IsNullOrWhiteSpace(osMessage) ? "LockWorkStation returned false." : osMessage;
            return "Failed to lock the workstation: " + detail;
        }

        /// <summary>The target process is at a higher integrity level.</summary>
        /// <returns>The error text.</returns>
        public static string HigherIntegrity()
        {
            return "The target process runs at a higher integrity level than mcp_overlord. Run mcp_overlord at the same integrity level as the game.";
        }

        /// <summary>The button name is not left, right, or middle.</summary>
        /// <param name="button">The name the caller sent.</param>
        /// <returns>The error text.</returns>
        public static string UnknownButton(string button)
        {
            return $"Button '{button}' was not recognized. Use left, right, or middle.";
        }

        /// <summary>The coordinate space is not image or window_rect.</summary>
        /// <param name="coordinateSpace">The value the caller sent.</param>
        /// <returns>The error text.</returns>
        public static string UnknownCoordinateSpace(string coordinateSpace)
        {
            return $"coordinateSpace '{coordinateSpace}' was not recognized. Use image or window_rect.";
        }

        /// <summary>The converted window point is outside the current window rectangle.</summary>
        /// <param name="windowX">Converted X in window pixels.</param>
        /// <param name="windowY">Converted Y in window pixels.</param>
        /// <param name="width">Current window width.</param>
        /// <param name="height">Current window height.</param>
        /// <returns>The error text.</returns>
        public static string PointOutsideWindow(int windowX, int windowY, int width, int height)
        {
            return $"The point ({windowX}, {windowY}) is outside the window ({width}x{height}).";
        }

        /// <summary>The image format is not png or jpeg.</summary>
        /// <param name="format">The value the caller sent.</param>
        /// <returns>The error text.</returns>
        public static string UnknownFormat(string format)
        {
            return $"format '{format}' was not recognized. Use png or jpeg.";
        }

        /// <summary>A tool handler threw.</summary>
        /// <param name="exception">The exception.</param>
        /// <returns>The error text.</returns>
        public static string Unexpected(Exception exception)
        {
            return $"The tool failed ({exception.GetType().Name}): {exception.Message}";
        }
    }
}

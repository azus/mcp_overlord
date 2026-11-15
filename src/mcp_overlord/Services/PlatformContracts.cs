using System.Drawing;
using game_automation;

namespace mcp_overlord.Services
{
    /// <summary>A process visible to the locator and the launcher.</summary>
    public sealed class ProcessSnapshot
    {
        /// <summary>Process id.</summary>
        public int Id { get; init; }

        /// <summary>Process name without the .exe extension.</summary>
        public string Name { get; init; } = "";

        /// <summary>Main window handle. Zero when the process has no main window yet.</summary>
        public IntPtr MainWindowHandle { get; init; }

        /// <summary>Main window title.</summary>
        public string MainWindowTitle { get; init; } = "";
    }

    /// <summary>Arguments for an allowlisted process start. The caller fills these from the configuration only.</summary>
    public sealed class ProcessStartRequest
    {
        /// <summary>Executable path.</summary>
        public string FileName { get; init; } = "";

        /// <summary>Raw argument string. Quotes are preserved.</summary>
        public string Arguments { get; init; } = "";

        /// <summary>Working directory. Null lets the process use its default.</summary>
        public string? WorkingDirectory { get; init; }
    }

    /// <summary>Reads the process list without starting anything.</summary>
    public interface IProcessQuery
    {
        /// <summary>Returns a snapshot of running processes.</summary>
        /// <returns>The processes that could be inspected.</returns>
        IReadOnlyList<ProcessSnapshot> GetProcesses();

        /// <summary>True when the process is still running.</summary>
        /// <param name="processId">Process id.</param>
        /// <returns>True when the process is alive.</returns>
        bool IsAlive(int processId);

        /// <summary>Process name without extension, or null when the process is gone.</summary>
        /// <param name="processId">Process id.</param>
        /// <returns>The process name, or null.</returns>
        string? GetProcessName(int processId);
    }

    /// <summary>Starts an allowlisted executable.</summary>
    public interface IProcessStarter
    {
        /// <summary>
        /// Starts a process and returns the child process id.
        /// </summary>
        /// <param name="request">File, arguments, and working directory from the configuration.</param>
        /// <returns>The new process id.</returns>
        int Start(ProcessStartRequest request);
    }

    /// <summary>Lists top-level windows for the locator.</summary>
    public interface IWindowEnumerator
    {
        /// <summary>Enumerates current top-level windows.</summary>
        /// <returns>The candidates, including z-order.</returns>
        IReadOnlyList<WindowCandidate> Enumerate();
    }

    /// <summary>Window geometry and restore operations the locator needs.</summary>
    public interface INativeWindowApi
    {
        /// <summary>True when the handle still refers to a window.</summary>
        /// <param name="hwnd">Window handle.</param>
        /// <returns>True when the window exists.</returns>
        bool IsWindow(IntPtr hwnd);

        /// <summary>Current window title.</summary>
        /// <param name="hwnd">Window handle.</param>
        /// <returns>The title. Empty when there is none.</returns>
        string GetTitle(IntPtr hwnd);

        /// <summary>Process id that owns the window.</summary>
        /// <param name="hwnd">Window handle.</param>
        /// <returns>The process id, or 0.</returns>
        int GetProcessId(IntPtr hwnd);

        /// <summary>True when the window is minimized.</summary>
        /// <param name="hwnd">Window handle.</param>
        /// <returns>True when iconic.</returns>
        bool IsMinimized(IntPtr hwnd);

        /// <summary>True when the window is maximized.</summary>
        /// <param name="hwnd">Window handle.</param>
        /// <returns>True when zoomed.</returns>
        bool IsMaximized(IntPtr hwnd);

        /// <summary>Client width and height in physical pixels.</summary>
        /// <param name="hwnd">Window handle.</param>
        /// <returns>The client size.</returns>
        (int Width, int Height) GetClientSize(IntPtr hwnd);

        /// <summary>Outer window rectangle in screen pixels.</summary>
        /// <param name="hwnd">Window handle.</param>
        /// <returns>Left, top, width, and height.</returns>
        (int X, int Y, int Width, int Height) GetWindowRect(IntPtr hwnd);

        /// <summary>Restores a minimized window.</summary>
        /// <param name="hwnd">Window handle.</param>
        void Restore(IntPtr hwnd);

        /// <summary>
        /// Reads <c>GetWindowRect</c>, the client size, and the offset from the window corner to the client origin.
        /// </summary>
        /// <param name="hwnd">Window handle.</param>
        /// <returns>The current geometry. Width and height are 0 when the rectangle cannot be read.</returns>
        WindowFrameGeometry GetFrameGeometry(IntPtr hwnd);
    }

    /// <summary>Captures a whole window rectangle.</summary>
    public interface IWindowCapture
    {
        /// <summary>Captures the window rectangle with PrintWindow. The bitmap matches <c>GetWindowRect</c>.</summary>
        /// <param name="hwnd">Window handle.</param>
        /// <param name="flags">PrintWindow flags. 0 is the first attempt. 2 asks for full content.</param>
        /// <returns>The bitmap. The caller disposes it.</returns>
        Bitmap CapturePrintWindow(IntPtr hwnd, uint flags);

        /// <summary>Captures the window rectangle from the screen. The origin and size match <c>GetWindowRect</c>.</summary>
        /// <param name="hwnd">Window handle.</param>
        /// <returns>The bitmap. The caller disposes it.</returns>
        Bitmap CaptureScreen(IntPtr hwnd);

        /// <summary>
        /// Captures the window's own pixels, excluding windows that overlap it.
        /// The bitmap matches <c>GetWindowRect</c>. Null when this capture cannot do that.
        /// </summary>
        /// <param name="hwnd">Window handle.</param>
        /// <param name="captureMethod"><c>windowVisual</c> or <c>windowDc</c>. Empty when this method returns null.</param>
        /// <returns>The bitmap, or null. The caller disposes it.</returns>
        Bitmap? CaptureOwned(IntPtr hwnd, out string captureMethod)
        {
            captureMethod = "";
            return null;
        }

        /// <summary>
        /// True when a visible window above <paramref name="hwnd"/> overlaps its rectangle.
        /// A screen blit would include that window.
        /// </summary>
        /// <param name="hwnd">Window handle.</param>
        /// <returns>True when the window is covered.</returns>
        bool IsCoveredByAnotherWindow(IntPtr hwnd)
        {
            return false;
        }

        /// <summary>
        /// Encodes a full-window PNG from the window's own pixels.
        /// False when the surface is missing, black, or the fast encoder is unavailable.
        /// </summary>
        /// <param name="hwnd">Window handle.</param>
        /// <param name="geometry">Expected window rectangle and client origin.</param>
        /// <param name="png">Encoded PNG. Empty when this method returns false.</param>
        /// <param name="captureMethod"><c>windowVisual</c> or <c>windowDc</c>. Empty when this method returns false.</param>
        /// <returns>True when <paramref name="png"/> is a non-black full-window image.</returns>
        bool TryCaptureFastPng(IntPtr hwnd, WindowFrameGeometry geometry, out byte[] png, out string captureMethod)
        {
            png = Array.Empty<byte>();
            captureMethod = "";
            return false;
        }
    }

    /// <summary>Sends keyboard input after the caller has focused the window.</summary>
    public interface IKeyboardSender
    {
        /// <summary>Sends a key down.</summary>
        /// <param name="hwnd">Window handle.</param>
        /// <param name="virtualKey">Virtual-key code.</param>
        void KeyDown(IntPtr hwnd, byte virtualKey);

        /// <summary>Sends a key up.</summary>
        /// <param name="hwnd">Window handle.</param>
        /// <param name="virtualKey">Virtual-key code.</param>
        void KeyUp(IntPtr hwnd, byte virtualKey);

        /// <summary>Presses and releases a key.</summary>
        /// <param name="hwnd">Window handle.</param>
        /// <param name="virtualKey">Virtual-key code.</param>
        /// <param name="holdMilliseconds">Time the key stays down.</param>
        void Press(IntPtr hwnd, byte virtualKey, int holdMilliseconds);

        /// <summary>Types text with Unicode events. A newline is Return.</summary>
        /// <param name="hwnd">Window handle.</param>
        /// <param name="text">Text to type.</param>
        void TypeUnicode(IntPtr hwnd, string text);
    }

    /// <summary>Foreground operations used by <see cref="FocusHelper"/>.</summary>
    public interface IFocusApi
    {
        /// <summary>Calls SetForegroundWindow.</summary>
        /// <param name="hwnd">Window handle.</param>
        /// <returns>True when the call reports success.</returns>
        bool SetForegroundWindow(IntPtr hwnd);

        /// <summary>Returns the foreground window.</summary>
        /// <returns>The foreground handle, or zero.</returns>
        IntPtr GetForegroundWindow();

        /// <summary>Thread id that owns the window.</summary>
        /// <param name="hwnd">Window handle.</param>
        /// <returns>The thread id, or 0.</returns>
        uint GetWindowThreadId(IntPtr hwnd);

        /// <summary>Thread id of the caller.</summary>
        /// <returns>The current thread id.</returns>
        uint GetCurrentThreadId();

        /// <summary>Attaches or detaches two thread input queues.</summary>
        /// <param name="sourceThreadId">Thread to attach.</param>
        /// <param name="targetThreadId">Thread to attach to.</param>
        /// <param name="attach">True to attach, false to detach.</param>
        /// <returns>True when the call reports success.</returns>
        bool AttachThreadInput(uint sourceThreadId, uint targetThreadId, bool attach);

        /// <summary>Brings the window to the top of the z-order.</summary>
        /// <param name="hwnd">Window handle.</param>
        void BringWindowToTop(IntPtr hwnd);

        /// <summary>Sets keyboard focus when a handle is available.</summary>
        /// <param name="hwnd">Window handle.</param>
        void SetFocus(IntPtr hwnd);
    }

    /// <summary>Reads integrity levels so input is not sent across a UIPI boundary.</summary>
    public interface IIntegrityApi
    {
        /// <summary>Integrity RID of a process, or -1 when it cannot be read.</summary>
        /// <param name="processId">Process id.</param>
        /// <returns>The integrity level, or -1.</returns>
        int GetIntegrityLevel(int processId);

        /// <summary>Integrity RID of this process, or -1 when it cannot be read.</summary>
        /// <returns>The integrity level, or -1.</returns>
        int GetCurrentIntegrityLevel();
    }

    /// <summary>
    /// Reads and sets the workstation lock. There is no unlock operation.
    /// </summary>
    public interface IWorkstationLock
    {
        /// <summary>
        /// True when the workstation session is locked, or when the input desktop is not the interactive Default desktop.
        /// That covers the lock screen and a secure desktop such as a UAC prompt.
        /// </summary>
        /// <returns>True when mouse and keyboard input must be refused.</returns>
        bool IsLocked();

        /// <summary>
        /// Locks the workstation. There is no matching unlock method.
        /// </summary>
        /// <param name="error">Operating-system message when the call fails. Null on success.</param>
        /// <returns>True when the workstation was locked.</returns>
        bool TryLock(out string? error);
    }

    /// <summary>Waits between polls and after restoring a window. Tests use a no-op.</summary>
    public interface IDelay
    {
        /// <summary>Waits for the given time.</summary>
        /// <param name="delay">How long to wait.</param>
        void Wait(TimeSpan delay);
    }
}

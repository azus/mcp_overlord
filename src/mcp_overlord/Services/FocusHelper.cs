namespace mcp_overlord.Services
{
    /// <summary>
    /// Brings a window to the foreground. If the direct call fails, the target thread is attached to the foreground thread and the call is retried.
    /// </summary>
    public sealed class FocusHelper
    {
        private readonly IFocusApi _api;

        /// <summary>
        /// Creates the helper.
        /// </summary>
        /// <param name="api">Foreground Win32 calls.</param>
        public FocusHelper(IFocusApi api)
        {
            _api = api;
        }

        /// <summary>
        /// Tries to focus <paramref name="hwnd"/>. A false result is not fatal. Input is still sent afterwards.
        /// </summary>
        /// <param name="hwnd">Window handle.</param>
        /// <returns>True when SetForegroundWindow reported success.</returns>
        public bool TryFocus(IntPtr hwnd)
        {
            if (_api.SetForegroundWindow(hwnd))
            {
                Raise(hwnd);
                return true;
            }

            uint targetThread = _api.GetWindowThreadId(hwnd);
            uint foregroundThread = _api.GetWindowThreadId(_api.GetForegroundWindow());
            bool attached = false;
            if (targetThread != 0 && foregroundThread != 0 && targetThread != foregroundThread)
            {
                attached = _api.AttachThreadInput(targetThread, foregroundThread, true);
            }

            try
            {
                bool focused = _api.SetForegroundWindow(hwnd);
                Raise(hwnd);
                return focused;
            }
            finally
            {
                if (attached)
                {
                    _api.AttachThreadInput(targetThread, foregroundThread, false);
                }
            }
        }

        private void Raise(IntPtr hwnd)
        {
            _api.BringWindowToTop(hwnd);
            _api.SetFocus(hwnd);
        }
    }
}

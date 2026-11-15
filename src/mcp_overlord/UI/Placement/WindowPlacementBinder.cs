using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace mcp_overlord.UI.Placement
{
    /// <summary>
    /// Restores a window from physical-pixel bounds before it is shown, and keeps the last
    /// normal rectangle so it can be saved again. The position is applied with SetWindowPos
    /// while the window is still hidden. A Win32 hook holds that rectangle through the DPI
    /// change WPF raises when the window arrives on another monitor, so the first frame is
    /// already in the right place.
    /// </summary>
    public sealed class WindowPlacementBinder
    {
        private const int WindowPosChanging = 0x0046;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;

        private readonly Window _window;
        private readonly IMonitorLayout _monitorLayout;
        private readonly EventHandler _displaySettingsChangedHandler;

        private PixelRect? _pendingBounds;
        private PixelRect? _normalBounds;
        private PixelRect _enforcedBounds;
        private HwndSource? _hookSource;
        private bool _dpiHooked;
        private bool _pendingMaximized;
        private bool _restoreMaximized;
        private bool _hasPendingRestore;
        private bool _tracking;
        private bool _applying;
        private bool _enforcing;

        /// <summary>
        /// Creates a binder. Nothing is subscribed until <see cref="Restore"/> runs.
        /// </summary>
        /// <param name="window">Window whose placement is persisted.</param>
        /// <param name="monitorLayout">Attached monitors used to clamp the rectangle.</param>
        public WindowPlacementBinder(Window window, IMonitorLayout monitorLayout)
        {
            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            if (monitorLayout == null)
            {
                throw new ArgumentNullException(nameof(monitorLayout));
            }

            _window = window;
            _monitorLayout = monitorLayout;
            _displaySettingsChangedHandler = OnDisplaySettingsChanged;
        }

        /// <summary>
        /// Restores <paramref name="normalBoundsPixels"/> and, when requested, maximizes afterwards.
        /// When the handle does not exist yet, the restore runs from SourceInitialized, which is
        /// before the window is visible.
        /// </summary>
        /// <param name="normalBoundsPixels">Outer bounds in physical pixels, or null to keep the default position.</param>
        /// <param name="maximized">True to maximize after the normal bounds are in place.</param>
        public void Restore(PixelRect? normalBoundsPixels, bool maximized)
        {
            _pendingMaximized = maximized;
            _restoreMaximized = maximized;
            _hasPendingRestore = true;
            _window.WindowStartupLocation = WindowStartupLocation.Manual;
            EnsureTracking();

            if (normalBoundsPixels is PixelRect requested && requested.HasArea)
            {
                PixelRect clamped = ClampToMonitors(requested);
                _pendingBounds = clamped;
                _normalBounds = clamped;
                _enforcedBounds = clamped;
                // DIP must be set before the HWND exists. WPF then creates the window on the
                // monitor that contains this point, using that monitor's DPI.
                AssignDipBounds(DpiFor(clamped), clamped);
            }
            else
            {
                _pendingBounds = null;
            }

            if (new WindowInteropHelper(_window).Handle == IntPtr.Zero)
            {
                _window.SourceInitialized -= OnSourceInitialized;
                _window.SourceInitialized += OnSourceInitialized;
                return;
            }

            ApplyPixelCorrection();
        }

        /// <summary>
        /// Reads the placement to write back. A maximized or minimized window keeps the last normal rectangle.
        /// </summary>
        /// <returns>The normal bounds and whether the window should reopen maximized.</returns>
        public WindowPlacementSnapshot Capture()
        {
            if (_window.WindowState == WindowState.Normal
                && TryGetOuterBounds(out PixelRect bounds)
                && bounds.HasArea)
            {
                _normalBounds = bounds;
            }

            bool maximized = _window.WindowState == WindowState.Maximized
                || (_window.WindowState == WindowState.Minimized && _restoreMaximized);
            return new WindowPlacementSnapshot(_normalBounds, maximized);
        }

        /// <summary>
        /// Reads the window's current outer bounds in physical pixels.
        /// </summary>
        /// <param name="bounds">The outer rectangle when the call returns true.</param>
        /// <returns>True when the native window exists and has a positive size.</returns>
        public bool TryGetOuterBounds(out PixelRect bounds)
        {
            bounds = default;
            IntPtr handle = new WindowInteropHelper(_window).Handle;
            if (handle == IntPtr.Zero || !GetWindowRect(handle, out RectNative rect))
            {
                return false;
            }

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            bounds = new PixelRect(rect.Left, rect.Top, width, height);
            return true;
        }

        #region Restore

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            _window.SourceInitialized -= OnSourceInitialized;
            ApplyPixelCorrection();
        }

        private void ApplyPixelCorrection()
        {
            if (!_hasPendingRestore)
            {
                return;
            }

            _hasPendingRestore = false;
            if (_pendingBounds is PixelRect bounds && bounds.HasArea && _window.WindowState != WindowState.Maximized)
            {
                StartEnforcing(bounds);
                StopEnforcing();
            }

            if (_pendingMaximized && _window.WindowState != WindowState.Maximized)
            {
                SetWindowState(WindowState.Maximized);
            }
        }

        private void StartEnforcing(PixelRect bounds)
        {
            _enforcedBounds = bounds;
            _normalBounds = bounds;
            _enforcing = true;

            IntPtr handle = new WindowInteropHelper(_window).Handle;
            if (handle == IntPtr.Zero)
            {
                _enforcing = false;
                return;
            }

            AttachHook(handle);
            AttachDpiHook();
            SetOuterBounds(handle, bounds);
            SetOuterBounds(handle, bounds);
            AssignDipBounds(DpiFor(bounds), bounds);
            SetOuterBounds(handle, bounds);
        }

        private void ScheduleStopEnforcing()
        {
            _window.Dispatcher.BeginInvoke(new Action(StopEnforcing), DispatcherPriority.ApplicationIdle);
        }

        private void StopEnforcing()
        {
            if (_enforcing)
            {
                IntPtr handle = new WindowInteropHelper(_window).Handle;
                AssignDipBounds(DpiFor(_enforcedBounds), _enforcedBounds);
                SetOuterBounds(handle, _enforcedBounds);
            }

            _enforcing = false;
            DetachDpiHook();
            if (_hookSource != null)
            {
                _hookSource.RemoveHook(OnWindowMessage);
                _hookSource = null;
            }
        }

        private void AttachHook(IntPtr handle)
        {
            if (_hookSource != null || handle == IntPtr.Zero)
            {
                return;
            }

            HwndSource? source = HwndSource.FromHwnd(handle);
            if (source == null)
            {
                return;
            }

            _hookSource = source;
            _hookSource.AddHook(OnWindowMessage);
        }

        private void OnDpiChanged(object sender, DpiChangedEventArgs e)
        {
            if (!_enforcing)
            {
                return;
            }

            int dpi = (int)Math.Round(e.NewDpi.PixelsPerInchX);
            if (dpi < 96)
            {
                dpi = DpiFor(_enforcedBounds);
            }

            AssignDipBounds(dpi, _enforcedBounds);
            SetOuterBounds(new WindowInteropHelper(_window).Handle, _enforcedBounds);
        }

        private void AttachDpiHook()
        {
            if (_dpiHooked)
            {
                return;
            }

            _window.DpiChanged += OnDpiChanged;
            _dpiHooked = true;
        }

        private void DetachDpiHook()
        {
            if (!_dpiHooked)
            {
                return;
            }

            _window.DpiChanged -= OnDpiChanged;
            _dpiHooked = false;
        }

        private int DpiFor(PixelRect bounds)
        {
            MonitorInfo? best = null;
            long bestArea = -1;
            foreach (MonitorInfo monitor in _monitorLayout.GetMonitors())
            {
                long area = bounds.IntersectionArea(monitor.WorkArea);
                if (area > bestArea)
                {
                    best = monitor;
                    bestArea = area;
                }
            }

            if (best != null && best.Dpi >= 96)
            {
                return best.Dpi;
            }

            return 96;
        }

        private void AssignDipBounds(int dpi, PixelRect bounds)
        {
            if (dpi < 96)
            {
                dpi = 96;
            }

            double scale = dpi / 96.0;
            _applying = true;
            try
            {
                _window.WindowStartupLocation = WindowStartupLocation.Manual;
                _window.Left = bounds.X / scale;
                _window.Top = bounds.Y / scale;
                _window.Width = bounds.Width / scale;
                _window.Height = bounds.Height / scale;
            }
            finally
            {
                _applying = false;
            }
        }

        private void SetWindowState(WindowState windowState)
        {
            _applying = true;
            try
            {
                _window.WindowState = windowState;
            }
            finally
            {
                _applying = false;
            }
        }

        private PixelRect ClampToMonitors(PixelRect bounds)
        {
            return WindowBoundsClamp.Clamp(
                bounds,
                _monitorLayout.GetMonitors(),
                _window.MinWidth,
                _window.MinHeight);
        }

        #endregion

        #region Tracking

        private void EnsureTracking()
        {
            if (_tracking)
            {
                return;
            }

            _tracking = true;
            _window.LocationChanged += OnLocationChanged;
            _window.SizeChanged += OnSizeChanged;
            _window.StateChanged += OnWindowStateChanged;
            _window.Closed += OnWindowClosed;
            SystemEvents.DisplaySettingsChanged += _displaySettingsChangedHandler;
        }

        private void OnLocationChanged(object? sender, EventArgs e)
        {
            RememberNormalBounds();
        }

        private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            RememberNormalBounds();
        }

        private void RememberNormalBounds()
        {
            if (_applying || _enforcing || _window.WindowState != WindowState.Normal)
            {
                return;
            }

            if (TryGetOuterBounds(out PixelRect bounds) && bounds.HasArea)
            {
                _normalBounds = bounds;
            }
        }

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            if (_applying || _enforcing)
            {
                return;
            }

            if (_window.WindowState == WindowState.Maximized)
            {
                _restoreMaximized = true;
                return;
            }

            if (_window.WindowState != WindowState.Normal)
            {
                return;
            }

            _restoreMaximized = false;
            ClampIfOutsideVisible();
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (_window.Dispatcher.HasShutdownStarted)
            {
                return;
            }

            _window.Dispatcher.BeginInvoke(new Action(ClampAfterDisplayChange));
        }

        private void ClampAfterDisplayChange()
        {
            if (_applying || _enforcing)
            {
                return;
            }

            if (_window.WindowState == WindowState.Minimized)
            {
                ClampRememberedBounds();
                return;
            }

            if (_window.WindowState == WindowState.Maximized)
            {
                MoveMaximizedWindowBack();
                return;
            }

            ClampIfOutsideVisible();
        }

        private void ClampRememberedBounds()
        {
            if (_normalBounds is not PixelRect saved || !saved.HasArea)
            {
                return;
            }

            _normalBounds = ClampToMonitors(saved);
        }

        private void MoveMaximizedWindowBack()
        {
            if (_normalBounds is not PixelRect saved || !saved.HasArea)
            {
                return;
            }

            PixelRect clamped = ClampToMonitors(saved);
            if (clamped.Equals(saved))
            {
                return;
            }

            SetWindowState(WindowState.Normal);
            StartEnforcing(clamped);
            StopEnforcing();
            SetWindowState(WindowState.Maximized);
        }

        private void ClampIfOutsideVisible()
        {
            if (!TryGetOuterBounds(out PixelRect current) || !current.HasArea)
            {
                return;
            }

            PixelRect clamped = ClampToMonitors(current);
            if (clamped.Equals(current))
            {
                _normalBounds = current;
                return;
            }

            StartEnforcing(clamped);
            ScheduleStopEnforcing();
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            StopEnforcing();
            _window.LocationChanged -= OnLocationChanged;
            _window.SizeChanged -= OnSizeChanged;
            _window.StateChanged -= OnWindowStateChanged;
            _window.Closed -= OnWindowClosed;
            SystemEvents.DisplaySettingsChanged -= _displaySettingsChangedHandler;
            _tracking = false;
        }

        #endregion

        #region Native

        private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (!_enforcing || message != WindowPosChanging || lParam == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            WindowPosNative position = Marshal.PtrToStructure<WindowPosNative>(lParam);
            position.X = _enforcedBounds.X;
            position.Y = _enforcedBounds.Y;
            position.Width = _enforcedBounds.Width;
            position.Height = _enforcedBounds.Height;
            position.Flags &= ~SwpNoMove;
            position.Flags &= ~SwpNoSize;
            Marshal.StructureToPtr(position, lParam, false);
            return IntPtr.Zero;
        }

        private static void SetOuterBounds(IntPtr handle, PixelRect bounds)
        {
            if (handle == IntPtr.Zero || !bounds.HasArea)
            {
                return;
            }

            SetWindowPos(
                handle,
                IntPtr.Zero,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                SwpNoZOrder | SwpNoActivate);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RectNative
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowPosNative
        {
            public IntPtr Hwnd;
            public IntPtr HwndInsertAfter;
            public int X;
            public int Y;
            public int Width;
            public int Height;
            public uint Flags;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr windowHandle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr windowHandle, out RectNative rect);

        #endregion
    }

    /// <summary>
    /// Normal bounds and maximized flag captured from a window.
    /// </summary>
    public sealed class WindowPlacementSnapshot
    {
        /// <summary>
        /// Creates a snapshot.
        /// </summary>
        /// <param name="normalBounds">Last normal outer bounds, in physical pixels.</param>
        /// <param name="isMaximized">True when the window should reopen maximized.</param>
        public WindowPlacementSnapshot(PixelRect? normalBounds, bool isMaximized)
        {
            NormalBounds = normalBounds;
            IsMaximized = isMaximized;
        }

        /// <summary>Last normal outer bounds, in physical pixels.</summary>
        public PixelRect? NormalBounds { get; }

        /// <summary>True when the window should reopen maximized.</summary>
        public bool IsMaximized { get; }
    }
}

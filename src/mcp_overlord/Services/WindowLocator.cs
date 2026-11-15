using game_automation;
using mcp_overlord.Configuration;
using mcp_overlord.Logging;

namespace mcp_overlord.Services
{
    /// <summary>
    /// Result of resolving a configured application to a window.
    /// </summary>
    public sealed class WindowLookup
    {
        /// <summary>True when a window was chosen.</summary>
        public bool Found { get; init; }

        /// <summary>Chosen window handle.</summary>
        public IntPtr Hwnd { get; init; }

        /// <summary>Handle formatted as at least eight hex digits.</summary>
        public string HwndHex
        {
            get { return $"0x{Hwnd.ToInt64():X8}"; }
        }

        /// <summary>Owning process id. Zero when nothing is running.</summary>
        public int ProcessId { get; init; }

        /// <summary>Owning process name.</summary>
        public string ProcessName { get; init; } = "";

        /// <summary>Chosen window title.</summary>
        public string Title { get; init; } = "";

        /// <summary>How many windows matched.</summary>
        public int MatchCount { get; init; }

        /// <summary>Client width in physical pixels.</summary>
        public int ClientWidth { get; init; }

        /// <summary>Client height in physical pixels.</summary>
        public int ClientHeight { get; init; }

        /// <summary>Window rectangle and the client origin inside it. Read when the lookup was built.</summary>
        public WindowFrameGeometry Frame { get; init; }

        /// <summary>Outer window left edge in screen pixels.</summary>
        public int ScreenX { get; init; }

        /// <summary>Outer window top edge in screen pixels.</summary>
        public int ScreenY { get; init; }

        /// <summary>Outer window width in screen pixels.</summary>
        public int ScreenWidth { get; init; }

        /// <summary>Outer window height in screen pixels.</summary>
        public int ScreenHeight { get; init; }

        /// <summary><c>normal</c>, <c>minimized</c>, or <c>maximized</c>.</summary>
        public string State { get; init; } = "normal";

        /// <summary>True when a matching process is running, with or without a window.</summary>
        public bool ProcessRunning { get; init; }

        /// <summary>True when the chosen window is minimized. Find does not restore it.</summary>
        public bool IsMinimized { get; init; }

        /// <summary>Text for the model when several windows matched or none did.</summary>
        public string? Message { get; init; }
    }

    /// <summary>
    /// Turns an application name into a window handle. Tools do not search the desktop themselves.
    /// </summary>
    public sealed class WindowLocator
    {
        private readonly IWindowEnumerator _enumerator;
        private readonly IProcessQuery _processes;
        private readonly INativeWindowApi _native;
        private readonly IDelay _delay;
        private readonly LogBuffer _log;
        private readonly object _cacheGate = new object();
        private readonly Dictionary<string, CachedWindow> _cache = new Dictionary<string, CachedWindow>(StringComparer.Ordinal);

        /// <summary>
        /// Creates the locator.
        /// </summary>
        /// <param name="enumerator">Desktop enumeration.</param>
        /// <param name="processes">Process list used when a process has no window yet.</param>
        /// <param name="native">Geometry, restore, and cache checks.</param>
        /// <param name="delay">Wait used after restoring a minimized window.</param>
        /// <param name="log">Receives a warning when a caller asks to report several matches.</param>
        public WindowLocator(IWindowEnumerator enumerator, IProcessQuery processes, INativeWindowApi native, IDelay delay, LogBuffer log)
        {
            _enumerator = enumerator;
            _processes = processes;
            _native = native;
            _delay = delay;
            _log = log;
        }

        /// <summary>Drops every cached handle. Called when the application list is reloaded.</summary>
        public void ClearCache()
        {
            lock (_cacheGate)
            {
                _cache.Clear();
            }
        }

        /// <summary>
        /// Resolves <paramref name="application"/> to one window.
        /// </summary>
        /// <param name="application">Configured application.</param>
        /// <param name="restoreIfMinimized">True for screenshot, focus, and input. False for find and list.</param>
        /// <param name="reportMultipleMatches">True when a tool should log a warning if several windows match.</param>
        /// <returns>The lookup. <see cref="WindowLookup.Found"/> is false when no window matches.</returns>
        public WindowLookup Find(ApplicationEntry application, bool restoreIfMinimized, bool reportMultipleMatches = false)
        {
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            if (TryUseCache(application, restoreIfMinimized, reportMultipleMatches, out WindowLookup? cached))
            {
                return cached;
            }

            string? processFilter = application.EffectiveProcessName;
            string? titleFilter = application.WindowTitle;
            IReadOnlyList<WindowCandidate> matches = WindowCandidateSelector.Filter(
                _enumerator.Enumerate(),
                processFilter,
                titleFilter);
            IReadOnlyList<ProcessSnapshot>? processes = null;
            if (matches.Count == 0)
            {
                processes = _processes.GetProcesses();
                matches = MainWindows(processes, processFilter, titleFilter);
            }

            WindowCandidate? chosen = ChooseCachedOrFresh(application.Name, matches);
            if (chosen == null)
            {
                return Missing(application, processFilter, processes);
            }

            if (restoreIfMinimized && chosen.IsMinimized)
            {
                _native.Restore(chosen.Hwnd);
                _delay.Wait(TimeSpan.FromMilliseconds(200));
            }

            WindowLookup lookup = Describe(chosen, matches.Count, restoreIfMinimized);
            if (reportMultipleMatches && lookup.MatchCount > 1 && lookup.Message != null)
            {
                _log.Append(LogLevel.Warning, "mcp", lookup.Message);
            }

            return lookup;
        }

        private bool TryUseCache(ApplicationEntry application, bool restoreIfMinimized, bool reportMultipleMatches, out WindowLookup lookup)
        {
            lookup = null!;
            CachedWindow? cached = ReadCache(application.Name);
            if (cached == null)
            {
                return false;
            }

            if (!CacheMatches(cached, application, out string title))
            {
                Forget(application.Name);
                return false;
            }

            if (restoreIfMinimized && _native.IsMinimized(cached.Hwnd))
            {
                _native.Restore(cached.Hwnd);
                _delay.Wait(TimeSpan.FromMilliseconds(200));
            }

            var candidate = new WindowCandidate
            {
                Hwnd = cached.Hwnd,
                ProcessId = cached.ProcessId,
                ProcessName = cached.ProcessName,
                Title = title,
                IsVisible = true,
                IsMinimized = _native.IsMinimized(cached.Hwnd),
                IsMaximized = _native.IsMaximized(cached.Hwnd)
            };
            lookup = Describe(candidate, cached.MatchCount, restoreIfMinimized);
            if (reportMultipleMatches && lookup.MatchCount > 1 && lookup.Message != null)
            {
                _log.Append(LogLevel.Warning, "mcp", lookup.Message);
            }

            return true;
        }

        private bool CacheMatches(CachedWindow cached, ApplicationEntry application, out string title)
        {
            title = "";
            if (!_native.IsWindow(cached.Hwnd))
            {
                return false;
            }

            if (_native.GetProcessId(cached.Hwnd) != cached.ProcessId)
            {
                return false;
            }

            string? processFilter = application.EffectiveProcessName;
            if (!string.IsNullOrEmpty(processFilter)
                && !string.Equals(cached.ProcessName, processFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            title = _native.GetTitle(cached.Hwnd);
            string? titleFilter = application.WindowTitle;
            if (!string.IsNullOrEmpty(titleFilter) && title.IndexOf(titleFilter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            return true;
        }

        private WindowCandidate? ChooseCachedOrFresh(string name, IReadOnlyList<WindowCandidate> matches)
        {
            CachedWindow? cached = ReadCache(name);
            WindowCandidate? chosen = null;
            if (cached != null)
            {
                foreach (WindowCandidate candidate in matches)
                {
                    if (candidate.Hwnd == cached.Hwnd)
                    {
                        chosen = candidate;
                        break;
                    }
                }
            }

            if (chosen == null)
            {
                chosen = WindowCandidateSelector.Choose(matches);
            }

            lock (_cacheGate)
            {
                if (chosen == null)
                {
                    _cache.Remove(name);
                }
                else
                {
                    _cache[name] = new CachedWindow
                    {
                        Hwnd = chosen.Hwnd,
                        ProcessId = chosen.ProcessId,
                        ProcessName = chosen.ProcessName,
                        MatchCount = matches.Count
                    };
                }
            }

            return chosen;
        }

        private IReadOnlyList<WindowCandidate> MainWindows(
            IReadOnlyList<ProcessSnapshot> processes,
            string? processFilter,
            string? titleFilter)
        {
            var matches = new List<WindowCandidate>();
            int zOrder = 0;
            foreach (ProcessSnapshot process in processes)
            {
                if (!TryReadMainWindow(process, processFilter, titleFilter, zOrder, out WindowCandidate? candidate))
                {
                    continue;
                }

                matches.Add(candidate!);
                zOrder++;
            }

            return matches;
        }

        private bool TryReadMainWindow(
            ProcessSnapshot process,
            string? processFilter,
            string? titleFilter,
            int zOrder,
            out WindowCandidate? candidate)
        {
            candidate = null;
            if (process.MainWindowHandle == IntPtr.Zero)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(processFilter)
                && !string.Equals(process.Name, processFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string title = _native.GetTitle(process.MainWindowHandle);
            if (!string.IsNullOrEmpty(titleFilter)
                && (string.IsNullOrEmpty(title) || title.IndexOf(titleFilter, StringComparison.OrdinalIgnoreCase) < 0))
            {
                return false;
            }

            bool minimized = _native.IsMinimized(process.MainWindowHandle);
            candidate = new WindowCandidate
            {
                Hwnd = process.MainWindowHandle,
                ProcessId = process.Id,
                ProcessName = process.Name,
                Title = title,
                IsVisible = !minimized,
                IsMinimized = minimized,
                IsMaximized = _native.IsMaximized(process.MainWindowHandle),
                ZOrder = zOrder
            };
            return true;
        }

        private CachedWindow? ReadCache(string name)
        {
            lock (_cacheGate)
            {
                return _cache.TryGetValue(name, out CachedWindow? cached) ? cached : null;
            }
        }

        private void Forget(string name)
        {
            lock (_cacheGate)
            {
                _cache.Remove(name);
            }
        }

        private WindowLookup Missing(ApplicationEntry application, string? processFilter, IReadOnlyList<ProcessSnapshot>? processes)
        {
            if (!string.IsNullOrEmpty(processFilter))
            {
                IReadOnlyList<ProcessSnapshot> list = processes ?? _processes.GetProcesses();
                foreach (ProcessSnapshot process in list)
                {
                    if (string.Equals(process.Name, processFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        string message = $"The process is running and has no window yet (process id {process.Id}).";
                        if (application.IsLaunchable)
                        {
                            message += " Call launch_application with waitForWindowSeconds, or retry find_window.";
                        }

                        return new WindowLookup
                        {
                            Found = false,
                            ProcessId = process.Id,
                            ProcessName = process.Name,
                            ProcessRunning = true,
                            Message = message
                        };
                    }
                }
            }

            string absent = "No matching window was found.";
            if (application.IsLaunchable)
            {
                absent += " The application is launchable; call launch_application.";
            }

            return new WindowLookup
            {
                Found = false,
                ProcessRunning = false,
                Message = absent
            };
        }

        private WindowLookup Describe(WindowCandidate chosen, int matchCount, bool restored)
        {
            WindowFrameGeometry frame = _native.GetFrameGeometry(chosen.Hwnd);
            bool minimized = restored ? _native.IsMinimized(chosen.Hwnd) : chosen.IsMinimized;
            bool maximized = _native.IsMaximized(chosen.Hwnd);
            string? message = null;
            if (matchCount > 1)
            {
                message = $"The configuration matches {matchCount} windows. Chose '{chosen.Title}'.";
            }

            return new WindowLookup
            {
                Found = true,
                Hwnd = chosen.Hwnd,
                ProcessId = chosen.ProcessId,
                ProcessName = chosen.ProcessName,
                Title = chosen.Title,
                MatchCount = matchCount,
                ClientWidth = frame.ClientWidth,
                ClientHeight = frame.ClientHeight,
                Frame = frame,
                ScreenX = frame.FrameX,
                ScreenY = frame.FrameY,
                ScreenWidth = frame.FrameWidth,
                ScreenHeight = frame.FrameHeight,
                State = minimized ? "minimized" : maximized ? "maximized" : "normal",
                ProcessRunning = true,
                IsMinimized = minimized,
                Message = message
            };
        }

        private sealed class CachedWindow
        {
            public IntPtr Hwnd { get; init; }

            public int ProcessId { get; init; }

            public string ProcessName { get; init; } = "";

            public int MatchCount { get; init; }
        }
    }
}

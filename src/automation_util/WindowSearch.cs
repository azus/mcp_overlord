using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace game_automation
{
    /// <summary>
    /// One top-level window discovered on the desktop.
    /// </summary>
    public sealed class WindowCandidate
    {
        /// <summary>Window handle.</summary>
        public IntPtr Hwnd { get; init; }

        /// <summary>Owning process id.</summary>
        public int ProcessId { get; init; }

        private string? _processName;
        private int _processNameResolved;

        /// <summary>
        /// Process name without the .exe extension.
        /// When the name was not supplied, the first read resolves it from the process image.
        /// </summary>
        public string ProcessName
        {
            get
            {
                if (_processNameResolved == 0)
                {
                    _processName = ProcessNameLookup.Read(ProcessId);
                    _processNameResolved = 1;
                }

                return _processName ?? "";
            }
            init
            {
                _processName = value ?? "";
                _processNameResolved = 1;
            }
        }

        /// <summary>Window title. Empty when the window has none.</summary>
        public string Title { get; init; } = "";

        /// <summary>True when the window is visible.</summary>
        public bool IsVisible { get; init; }

        /// <summary>True when the window is minimized.</summary>
        public bool IsMinimized { get; init; }

        /// <summary>True when the window is maximized.</summary>
        public bool IsMaximized { get; init; }

        /// <summary>True when this window is the foreground window.</summary>
        public bool IsForeground { get; init; }

        /// <summary>Z-order index. Smaller values are closer to the top.</summary>
        public int ZOrder { get; init; }
    }

    /// <summary>
    /// The window chosen by a search, plus how many windows matched.
    /// </summary>
    public sealed class WindowSearchResult
    {
        /// <summary>
        /// Creates a search result.
        /// </summary>
        /// <param name="found">True when a window was chosen.</param>
        /// <param name="chosen">The chosen window, or null.</param>
        /// <param name="matchCount">How many windows matched the filters.</param>
        /// <param name="matches">Every match, in discovery order.</param>
        public WindowSearchResult(bool found, WindowCandidate? chosen, int matchCount, IReadOnlyList<WindowCandidate> matches)
        {
            Found = found;
            Chosen = chosen;
            MatchCount = matchCount;
            Matches = matches;
        }

        /// <summary>True when a window was chosen.</summary>
        public bool Found { get; }

        /// <summary>The chosen window, or null when nothing matched.</summary>
        public WindowCandidate? Chosen { get; }

        /// <summary>How many windows matched the filters.</summary>
        public int MatchCount { get; }

        /// <summary>Every match.</summary>
        public IReadOnlyList<WindowCandidate> Matches { get; }
    }

    /// <summary>
    /// Filters and tie-breaks window candidates. These rules are pure and do not call Win32.
    /// </summary>
    public static class WindowCandidateSelector
    {
        /// <summary>
        /// Keeps candidates that satisfy every configured filter.
        /// Process name is an exact ordinal case-insensitive match. Title is an ordinal case-insensitive substring.
        /// </summary>
        /// <param name="candidates">Windows to filter.</param>
        /// <param name="processName">Process name without extension, or null to skip the filter.</param>
        /// <param name="titleSubstring">Title substring, or null to skip the filter.</param>
        /// <returns>The matches, in the original order.</returns>
        public static IReadOnlyList<WindowCandidate> Filter(
            IReadOnlyList<WindowCandidate> candidates,
            string? processName,
            string? titleSubstring)
        {
            if (candidates == null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            var matches = new List<WindowCandidate>();
            foreach (WindowCandidate candidate in candidates)
            {
                if (!PassesTitleFilter(candidate, titleSubstring))
                {
                    continue;
                }

                if (!PassesProcessFilter(candidate, processName))
                {
                    continue;
                }

                matches.Add(candidate);
            }

            return matches;
        }

        /// <summary>
        /// Picks one window. Visible non-minimized windows win, then the foreground window, then the top of the z-order.
        /// </summary>
        /// <param name="matches">Candidates that already passed the filters.</param>
        /// <returns>The chosen window, or null when the list is empty.</returns>
        public static WindowCandidate? Choose(IReadOnlyList<WindowCandidate> matches)
        {
            if (matches == null)
            {
                throw new ArgumentNullException(nameof(matches));
            }

            if (matches.Count == 0)
            {
                return null;
            }

            var visible = new List<WindowCandidate>();
            foreach (WindowCandidate candidate in matches)
            {
                if (candidate.IsVisible && !candidate.IsMinimized)
                {
                    visible.Add(candidate);
                }
            }

            IReadOnlyList<WindowCandidate> pool = visible.Count > 0 ? visible : matches;
            foreach (WindowCandidate candidate in pool)
            {
                if (candidate.IsForeground)
                {
                    return candidate;
                }
            }

            WindowCandidate chosen = pool[0];
            for (int index = 1; index < pool.Count; index++)
            {
                if (pool[index].ZOrder < chosen.ZOrder)
                {
                    chosen = pool[index];
                }
            }

            return chosen;
        }

        /// <summary>
        /// True when the candidate's process name matches, or when no process filter is set.
        /// </summary>
        /// <param name="candidate">Window to test.</param>
        /// <param name="processName">Required process name, or null.</param>
        /// <returns>True when the filter passes.</returns>
        public static bool PassesProcessFilter(WindowCandidate candidate, string? processName)
        {
            if (string.IsNullOrEmpty(processName))
            {
                return true;
            }

            return string.Equals(candidate.ProcessName, processName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when the title contains the substring, or when no title filter is set.
        /// </summary>
        /// <param name="candidate">Window to test.</param>
        /// <param name="titleSubstring">Required substring, or null.</param>
        /// <returns>True when the filter passes.</returns>
        public static bool PassesTitleFilter(WindowCandidate candidate, string? titleSubstring)
        {
            if (string.IsNullOrEmpty(titleSubstring))
            {
                return true;
            }

            if (string.IsNullOrEmpty(candidate.Title))
            {
                return false;
            }

            return candidate.Title.IndexOf(titleSubstring, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>
    /// Enumerates top-level windows, then applies <see cref="WindowCandidateSelector"/>.
    /// A process main window that enumeration missed is added only when a search matches nothing.
    /// <see cref="Window.FindByTitle"/> is unchanged and still returns the first main-window title match.
    /// </summary>
    public static class WindowSearch
    {
        /// <summary>
        /// Lists top-level windows. Process names are left unread until a caller asks for them.
        /// </summary>
        /// <returns>Discovered windows. Z-order follows enumeration order.</returns>
        public static IReadOnlyList<WindowCandidate> Enumerate()
        {
            var found = new List<WindowCandidate>();
            var seen = new HashSet<IntPtr>();
            int zOrder = 0;
            IntPtr foreground = GetForegroundWindow();

            EnumWindows((hwnd, state) =>
            {
                if (hwnd != IntPtr.Zero && seen.Add(hwnd))
                {
                    found.Add(ReadCandidate(hwnd, zOrder, foreground, null));
                    zOrder++;
                }

                return true;
            }, IntPtr.Zero);

            return found;
        }

        /// <summary>
        /// Enumerates the desktop and returns the chosen window plus the match count.
        /// When nothing matches, process main windows that enumeration missed are included and the filter runs again.
        /// </summary>
        /// <param name="processName">Process name without extension, or null.</param>
        /// <param name="titleSubstring">Title substring, or null.</param>
        /// <returns>The search result. <see cref="WindowSearchResult.Found"/> is false when nothing matches.</returns>
        public static WindowSearchResult Search(string? processName, string? titleSubstring)
        {
            IReadOnlyList<WindowCandidate> windows = Enumerate();
            IReadOnlyList<WindowCandidate> matches = WindowCandidateSelector.Filter(windows, processName, titleSubstring);
            if (matches.Count == 0)
            {
                matches = WindowCandidateSelector.Filter(AppendMissingMainWindows(windows), processName, titleSubstring);
            }

            WindowCandidate? chosen = WindowCandidateSelector.Choose(matches);
            return new WindowSearchResult(chosen != null, chosen, matches.Count, matches);
        }

        private static IReadOnlyList<WindowCandidate> AppendMissingMainWindows(IReadOnlyList<WindowCandidate> windows)
        {
            var found = new List<WindowCandidate>(windows);
            var seen = new HashSet<IntPtr>();
            foreach (WindowCandidate candidate in windows)
            {
                seen.Add(candidate.Hwnd);
            }

            int zOrder = windows.Count;
            IntPtr foreground = GetForegroundWindow();
            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    IntPtr mainWindow = process.MainWindowHandle;
                    if (mainWindow != IntPtr.Zero && seen.Add(mainWindow))
                    {
                        found.Add(ReadCandidate(mainWindow, zOrder, foreground, process));
                        zOrder++;
                    }
                }
                catch (Exception)
                {
                    // The process can exit between the snapshot and the handle read.
                }
                finally
                {
                    process.Dispose();
                }
            }

            return found;
        }

        private static WindowCandidate ReadCandidate(IntPtr hwnd, int zOrder, IntPtr foreground, Process? knownProcess)
        {
            GetWindowThreadProcessId(hwnd, out uint processId);
            if (knownProcess != null)
            {
                return new WindowCandidate
                {
                    Hwnd = hwnd,
                    ProcessId = (int)processId,
                    ProcessName = ReadKnownProcessName(knownProcess),
                    Title = ReadTitle(hwnd),
                    IsVisible = IsWindowVisible(hwnd),
                    IsMinimized = IsIconic(hwnd),
                    IsMaximized = IsZoomed(hwnd),
                    IsForeground = hwnd == foreground,
                    ZOrder = zOrder
                };
            }

            return new WindowCandidate
            {
                Hwnd = hwnd,
                ProcessId = (int)processId,
                Title = ReadTitle(hwnd),
                IsVisible = IsWindowVisible(hwnd),
                IsMinimized = IsIconic(hwnd),
                IsMaximized = IsZoomed(hwnd),
                IsForeground = hwnd == foreground,
                ZOrder = zOrder
            };
        }

        private static string ReadKnownProcessName(Process process)
        {
            try
            {
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

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr state);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr state);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsZoomed(IntPtr hwnd);
    }
}

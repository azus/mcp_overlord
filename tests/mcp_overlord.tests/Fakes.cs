using System.Drawing;
using System.Drawing.Imaging;
using game_automation;
using mcp_overlord.Configuration;
using mcp_overlord.Services;
using mcp_overlord.Shell;
using mcp_overlord.UI;

namespace mcp_overlord.tests
{
    internal sealed class FakeWorld
    {
        public List<WindowCandidate> Windows { get; } = new List<WindowCandidate>();

        public List<ProcessSnapshot> Processes { get; } = new List<ProcessSnapshot>();

        public Dictionary<IntPtr, bool> WindowExists { get; } = new Dictionary<IntPtr, bool>();

        public HashSet<IntPtr> MinimizedAfterRestore { get; } = new HashSet<IntPtr>();

        public List<IntPtr> Restored { get; } = new List<IntPtr>();

        public int EnumerateCalls { get; set; }

        public List<TimeSpan> Delays { get; } = new List<TimeSpan>();

        public List<ProcessStartRequest> Starts { get; } = new List<ProcessStartRequest>();

        public Exception? StartError { get; set; }

        public int NextProcessId { get; set; } = 100;

        public int ClientWidth { get; set; } = 200;

        public int ClientHeight { get; set; } = 100;

        public int FrameX { get; set; } = 10;

        public int FrameY { get; set; } = 20;

        public int Border { get; set; } = 8;

        public int TitleBar { get; set; } = 31;

        public RecordingClick MouseEvents { get; } = new RecordingClick();

        public RecordingClick SendInputClicks { get; } = new RecordingClick();

        public RecordingKeyboard Keyboard { get; } = new RecordingKeyboard();

        public RecordingFocus Focus { get; } = new RecordingFocus();

        public List<string> Order { get; } = new List<string>();

        public int TargetIntegrity { get; set; } = 0x2000;

        public int CurrentIntegrity { get; set; } = 0x2000;

        public FakeCapture Capture { get; } = new FakeCapture();

        public FakeInteraction Interaction { get; } = new FakeInteraction();

        public FakeClipboard Clipboard { get; } = new FakeClipboard();

        public FakeWorkstationLock WorkstationLock { get; } = new FakeWorkstationLock();

        public WindowFrameGeometry FrameGeometry()
        {
            return new WindowFrameGeometry(
                FrameX,
                FrameY,
                Border + ClientWidth + Border,
                TitleBar + ClientHeight + Border,
                Border,
                TitleBar,
                ClientWidth,
                ClientHeight);
        }

        public AppComposition Build()
        {
            Focus.Order = Order;
            MouseEvents.Order = Order;
            SendInputClicks.Order = Order;
            Capture.Geometry = FrameGeometry();
            return AppComposition.Create(
                new FakeWindows(this),
                new FakeProcesses(this),
                new FakeStarter(this),
                new FakeNative(this),
                Capture,
                Keyboard,
                Focus,
                new FakeIntegrity(this),
                new FakeDelay(this),
                MouseEvents,
                SendInputClicks,
                WorkstationLock,
                Interaction,
                Clipboard,
                new InlineUiInvoker(),
                null);
        }

        public void AddWindow(int hwnd, string processName, string title, bool minimized = false, bool foreground = false, int zOrder = 0)
        {
            var handle = new IntPtr(hwnd);
            Windows.Add(new WindowCandidate
            {
                Hwnd = handle,
                ProcessId = hwnd,
                ProcessName = processName,
                Title = title,
                IsVisible = true,
                IsMinimized = minimized,
                IsForeground = foreground,
                ZOrder = zOrder
            });
            WindowExists[handle] = true;
            Processes.Add(new ProcessSnapshot
            {
                Id = hwnd,
                Name = processName,
                MainWindowHandle = handle,
                MainWindowTitle = title
            });
        }
    }

    internal sealed class FakeWorkstationLock : IWorkstationLock
    {
        public bool Locked { get; set; }

        public int LockCalls { get; private set; }

        public bool IsLocked()
        {
            return Locked;
        }

        public bool TryLock(out string? error)
        {
            LockCalls++;
            Locked = true;
            error = null;
            return true;
        }
    }

    internal sealed class RecordingClick : ClickHelper
    {
        private int _inside;
        private int _maxInside;

        public List<string> Calls { get; } = new List<string>();

        public List<string>? Order { get; set; }

        public bool ProbeOverlap { get; set; }

        public int MaxInside
        {
            get { return _maxInside; }
        }

        public int LastScroll { get; private set; }

        public void MoveTo(IntPtr hwnd, int clientX, int clientY)
        {
            Record("move " + clientX + "," + clientY);
        }

        public void ButtonDown(IntPtr hwnd, int clientX, int clientY, MouseButton button)
        {
            Record("down " + button);
        }

        public void ButtonUp(IntPtr hwnd, int clientX, int clientY, MouseButton button)
        {
            Record("up " + button);
        }

        public void Click(IntPtr hwnd, int clientX, int clientY, MouseButton button, int holdMs, bool doubleClick)
        {
            Record("click " + button + " " + holdMs + " " + doubleClick);
        }

        public void Scroll(IntPtr hwnd, int clientX, int clientY, int notches)
        {
            LastScroll = notches;
            Record("scroll " + notches);
        }

        public void ClickOnPoint(IntPtr hwnd, int x, int y, int clickDelayInMilliseconds = 100)
        {
            Record("point");
        }

        private void Record(string call)
        {
            Calls.Add(call);
            Order?.Add(call);
            if (!ProbeOverlap)
            {
                return;
            }

            int now = Interlocked.Increment(ref _inside);
            int observed;
            do
            {
                observed = _maxInside;
            }
            while (now > observed && Interlocked.CompareExchange(ref _maxInside, now, observed) != observed);

            Thread.Sleep(40);
            Interlocked.Decrement(ref _inside);
        }
    }

    internal sealed class RecordingKeyboard : IKeyboardSender
    {
        public List<string> Calls { get; } = new List<string>();

        public void KeyDown(IntPtr hwnd, byte virtualKey)
        {
            Calls.Add("down " + virtualKey);
        }

        public void KeyUp(IntPtr hwnd, byte virtualKey)
        {
            Calls.Add("up " + virtualKey);
        }

        public void Press(IntPtr hwnd, byte virtualKey, int holdMilliseconds)
        {
            Calls.Add("press " + virtualKey + " " + holdMilliseconds);
        }

        public void TypeUnicode(IntPtr hwnd, string text)
        {
            Calls.Add("text " + text);
        }
    }

    internal sealed class RecordingFocus : IFocusApi
    {
        public List<string>? Order { get; set; }

        public bool FirstSucceeds { get; set; } = true;

        public int ForegroundCalls { get; private set; }

        public List<bool> AttachFlags { get; } = new List<bool>();

        public int Raised { get; private set; }

        public bool SetForegroundWindow(IntPtr hwnd)
        {
            ForegroundCalls++;
            Order?.Add("focus");
            if (ForegroundCalls == 1)
            {
                return FirstSucceeds;
            }

            return true;
        }

        public IntPtr GetForegroundWindow()
        {
            return new IntPtr(1);
        }

        public uint GetWindowThreadId(IntPtr hwnd)
        {
            return hwnd == new IntPtr(1) ? 9u : 5u;
        }

        public uint GetCurrentThreadId()
        {
            return 3;
        }

        public bool AttachThreadInput(uint sourceThreadId, uint targetThreadId, bool attach)
        {
            AttachFlags.Add(attach);
            return true;
        }

        public void BringWindowToTop(IntPtr hwnd)
        {
            Raised++;
        }

        public void SetFocus(IntPtr hwnd)
        {
            Raised++;
        }
    }

    internal sealed class FakeCapture : IWindowCapture
    {
        public static readonly Color ChromeColor = Color.FromArgb(255, 210, 210, 210);

        public static readonly Color ClientColor = Color.FromArgb(255, 10, 20, 30);

        public static readonly WindowFrameGeometry DefaultGeometry = new WindowFrameGeometry(10, 20, 216, 139, 8, 31, 200, 100);

        public List<uint> Flags { get; } = new List<uint>();

        public int ScreenCalls { get; private set; }

        public WindowFrameGeometry Geometry { get; set; } = DefaultGeometry;

        public Func<uint, Bitmap>? PrintFactory { get; set; }

        public Func<Bitmap>? ScreenFactory { get; set; }

        public bool Covered { get; set; }

        public Bitmap CapturePrintWindow(IntPtr hwnd, uint flags)
        {
            Flags.Add(flags);
            return PrintFactory != null ? PrintFactory(flags) : Painted(Geometry, ChromeColor, ClientColor);
        }

        public Bitmap CaptureScreen(IntPtr hwnd)
        {
            ScreenCalls++;
            return ScreenFactory != null ? ScreenFactory() : Painted(Geometry, ChromeColor, ClientColor);
        }

        public bool IsCoveredByAnotherWindow(IntPtr hwnd)
        {
            return Covered;
        }

        public static Bitmap Solid(Color color, int width, int height)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(color);
            }

            return bitmap;
        }

        public static Bitmap Painted(WindowFrameGeometry geometry, Color chrome, Color client)
        {
            var bitmap = new Bitmap(geometry.FrameWidth, geometry.FrameHeight, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(chrome);
                if (geometry.ClientWidth > 0 && geometry.ClientHeight > 0)
                {
                    using var brush = new SolidBrush(client);
                    graphics.FillRectangle(
                        brush,
                        geometry.ClientOffsetX,
                        geometry.ClientOffsetY,
                        geometry.ClientWidth,
                        geometry.ClientHeight);
                }
            }

            return bitmap;
        }
    }

    internal sealed class FakeInteraction : IUserInteraction
    {
        public List<string> Messages { get; } = new List<string>();

        public List<string> SavePrompts { get; } = new List<string>();

        public string? NextPath { get; set; }

        public string? NextSavePath { get; set; }

        public SaveChangesChoice NextSaveChoice { get; set; } = SaveChangesChoice.Discard;

        public bool NextConfirm { get; set; } = true;

        public bool EditResult { get; set; } = true;

        public Action<ApplicationEditorViewModel>? EditApplicationAction { get; set; }

        public Action<ServerSettingsViewModel>? EditServerAction { get; set; }

        public string? LastConfigDirectory { get; set; }

        public void ShowMessage(string title, string message)
        {
            Messages.Add(message);
        }

        public string? PickConfigFile()
        {
            return NextPath;
        }

        public string? PickSaveConfigFile(string suggestedName)
        {
            return NextSavePath;
        }

        public SaveChangesChoice AskSaveChanges(string displayName)
        {
            SavePrompts.Add(displayName);
            return NextSaveChoice;
        }

        public bool Confirm(string title, string message, string confirmText = "OK")
        {
            Messages.Add(message);
            return NextConfirm;
        }

        public bool EditApplication(ApplicationEditorViewModel viewModel)
        {
            EditApplicationAction?.Invoke(viewModel);
            return EditResult;
        }

        public bool EditServerSettings(ServerSettingsViewModel viewModel)
        {
            EditServerAction?.Invoke(viewModel);
            return EditResult;
        }

        public string? PickExecutableFile()
        {
            return null;
        }

        public string? PickFolder()
        {
            return null;
        }

        public string? PickCertificateFile()
        {
            return null;
        }

        public string? NextImagePath { get; set; }

        public ConnectionReportViewModel? ConnectionReport { get; private set; }

        public string? PickSaveImageFile(string suggestedName)
        {
            return NextImagePath;
        }

        public void ShowConnectionReport(ConnectionReportViewModel viewModel)
        {
            ConnectionReport = viewModel;
        }
    }

    internal sealed class FakeClipboard : IClipboardService
    {
        public string? Text { get; private set; }

        public void SetText(string text)
        {
            Text = text;
        }
    }

    internal sealed class FakeWindows : IWindowEnumerator
    {
        private readonly FakeWorld _world;

        public FakeWindows(FakeWorld world)
        {
            _world = world;
        }

        public IReadOnlyList<WindowCandidate> Enumerate()
        {
            _world.EnumerateCalls++;
            return _world.Windows.ToArray();
        }
    }

    internal sealed class FakeProcesses : IProcessQuery
    {
        private readonly FakeWorld _world;

        public FakeProcesses(FakeWorld world)
        {
            _world = world;
        }

        public IReadOnlyList<ProcessSnapshot> GetProcesses()
        {
            return _world.Processes.ToArray();
        }

        public bool IsAlive(int processId)
        {
            return _world.Processes.Any(process => process.Id == processId);
        }

        public string? GetProcessName(int processId)
        {
            ProcessSnapshot? process = _world.Processes.FirstOrDefault(item => item.Id == processId);
            return process?.Name;
        }
    }

    internal sealed class FakeStarter : IProcessStarter
    {
        private readonly FakeWorld _world;

        public FakeStarter(FakeWorld world)
        {
            _world = world;
        }

        public int Start(ProcessStartRequest request)
        {
            if (_world.StartError != null)
            {
                throw _world.StartError;
            }

            _world.Starts.Add(request);
            return _world.NextProcessId++;
        }
    }

    internal sealed class FakeNative : INativeWindowApi
    {
        private readonly FakeWorld _world;

        public FakeNative(FakeWorld world)
        {
            _world = world;
        }

        public bool IsWindow(IntPtr hwnd)
        {
            return !_world.WindowExists.TryGetValue(hwnd, out bool exists) || exists;
        }

        public string GetTitle(IntPtr hwnd)
        {
            WindowCandidate? window = _world.Windows.FirstOrDefault(item => item.Hwnd == hwnd);
            return window?.Title ?? "";
        }

        public int GetProcessId(IntPtr hwnd)
        {
            WindowCandidate? window = _world.Windows.FirstOrDefault(item => item.Hwnd == hwnd);
            return window?.ProcessId ?? 0;
        }

        public bool IsMinimized(IntPtr hwnd)
        {
            return _world.MinimizedAfterRestore.Contains(hwnd);
        }

        public bool IsMaximized(IntPtr hwnd)
        {
            return false;
        }

        public (int Width, int Height) GetClientSize(IntPtr hwnd)
        {
            return (_world.ClientWidth, _world.ClientHeight);
        }

        public (int X, int Y, int Width, int Height) GetWindowRect(IntPtr hwnd)
        {
            WindowFrameGeometry frame = _world.FrameGeometry();
            return (frame.FrameX, frame.FrameY, frame.FrameWidth, frame.FrameHeight);
        }

        public WindowFrameGeometry GetFrameGeometry(IntPtr hwnd)
        {
            return _world.FrameGeometry();
        }

        public void Restore(IntPtr hwnd)
        {
            _world.Restored.Add(hwnd);
            _world.MinimizedAfterRestore.Remove(hwnd);
        }
    }

    internal sealed class FakeIntegrity : IIntegrityApi
    {
        private readonly FakeWorld _world;

        public FakeIntegrity(FakeWorld world)
        {
            _world = world;
        }

        public int GetIntegrityLevel(int processId)
        {
            return _world.TargetIntegrity;
        }

        public int GetCurrentIntegrityLevel()
        {
            return _world.CurrentIntegrity;
        }
    }

    internal sealed class FakeDelay : IDelay
    {
        private readonly FakeWorld _world;

        public FakeDelay(FakeWorld world)
        {
            _world = world;
        }

        public void Wait(TimeSpan delay)
        {
            _world.Delays.Add(delay);
        }
    }

    internal sealed class RecordingRecentFiles : IRecentFileRegistry
    {
        public List<string> Paths { get; } = new List<string>();

        public void Add(string path)
        {
            Paths.Add(path);
        }
    }
}

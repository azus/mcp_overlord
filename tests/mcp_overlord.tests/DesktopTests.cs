using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using game_automation;
using mcp_overlord.Configuration;
using mcp_overlord.Logging;
using mcp_overlord.Services;
using mcp_overlord.Services.Native;
using mcp_overlord.Tools;

namespace mcp_overlord.tests
{
    [Trait("Category", "Desktop")]
    public class DesktopControlTests
    {
        [Fact]
        public void RealWindowIsFoundCapturedAndClicked()
        {
            if (Environment.GetEnvironmentVariable("GAME_MCP_DESKTOP_TESTS") != "1")
            {
                Assert.Skip("Set GAME_MCP_DESKTOP_TESTS=1 to run desktop tests.");
            }

            string title = "game-mcp-desktop-" + Guid.NewGuid().ToString("N");
            using var window = DesktopProbe.Open(title, Color.FromArgb(255, 40, 90, 160));
            var log = new LogBuffer();
            var locator = new WindowLocator(
                new Win32WindowEnumerator(),
                new Win32ProcessQuery(),
                new Win32WindowApi(),
                new ThreadDelay(),
                log);
            var application = new ApplicationEntry("Probe", "", null, null, title, null, null, ClickBackend.MouseEvent);

            WindowLookup lookup = locator.Find(application, true, true);
            Assert.True(lookup.Found);
            Assert.Equal(window.Handle, lookup.Hwnd);

            var capture = new CaptureService(new Win32WindowCapture());
            CaptureOutcome printed = capture.Capture(lookup.Hwnd, lookup.Frame, new CaptureRequest());
            Assert.Null(printed.Error);
            Assert.Equal(lookup.Frame.FrameWidth, printed.WindowWidth);
            Assert.Equal(lookup.Frame.FrameHeight, printed.WindowHeight);
            using (var stream = new MemoryStream(printed.Bytes))
            using (var image = new Bitmap(stream))
            {
                Assert.Equal(lookup.Frame.FrameWidth, image.Width);
                Assert.Equal(lookup.Frame.FrameHeight, image.Height);
                Color pixel = image.GetPixel(lookup.Frame.ClientOffsetX + 30, lookup.Frame.ClientOffsetY + 40);
                Assert.InRange(pixel.R, 32, 48);
                Assert.InRange(pixel.G, 82, 98);
                Assert.InRange(pixel.B, 152, 168);
            }

            var scales = new ScreenshotScaleStore();
            var input = new InputService(
                locator,
                new ClickBackends(new MouseEventClickHelper(), new SendInputClickHelper()),
                new Win32KeyboardSender(),
                new FocusHelper(new Win32FocusApi()),
                new IntegrityChecker(new Win32IntegrityApi()),
                new InputGate(),
                scales,
                new Win32WorkstationLock());
            int imageX = lookup.Frame.ClientOffsetX + 30;
            int imageY = lookup.Frame.ClientOffsetY + 40;
            MouseActionResult mouseEvent = input.MouseClick(application, imageX, imageY, "left", false, 20, "image");
            window.WaitForClick(30, 40);
            MouseActionResult moved = input.MouseMove(application, 0, 0, "image");
            Assert.Null(moved.Error);
            Assert.Equal(0, moved.WindowX);
            Assert.Equal(0, moved.WindowY);
            Assert.True(GetCursorPos(out POINT cursor));
            WindowLookup current = locator.Find(application, true, true);
            Assert.InRange(cursor.X, current.Frame.FrameX - 2, current.Frame.FrameX + 2);
            Assert.InRange(cursor.Y, current.Frame.FrameY - 2, current.Frame.FrameY + 2);

            var sent = application with { ClickBackend = ClickBackend.SendInput };
            int windowX = lookup.Frame.ClientOffsetX + 60;
            int windowY = lookup.Frame.ClientOffsetY + 50;
            MouseActionResult sendInput = input.MouseClick(sent, windowX, windowY, "left", false, 20, "window_rect");
            window.WaitForClick(60, 50);
            Assert.Null(mouseEvent.Error);
            Assert.Null(sendInput.Error);

            window.FocusTextBox();
            KeyActionResult typed = input.TypeText(application, "Hi");
            KeyActionResult pressed = input.KeyPress(application, "X", 20);
            input.KeyDown(application, "Enter");
            input.KeyUp(application, "Enter");
            Assert.Null(typed.Error);
            Assert.Null(pressed.Error);
            window.WaitForText();
        }

        [Fact]
        public void AnOverlappingWindowIsLeftOutOfTheScreenshot()
        {
            if (Environment.GetEnvironmentVariable("GAME_MCP_DESKTOP_TESTS") != "1")
            {
                Assert.Skip("Set GAME_MCP_DESKTOP_TESTS=1 to run desktop tests.");
            }

            using DesktopProbe red = DesktopProbe.Open(
                "game-mcp-red-" + Guid.NewGuid().ToString("N"),
                Color.FromArgb(255, 180, 20, 20),
                new Point(40, 40),
                new Size(220, 160),
                topMost: false,
                FormBorderStyle.None);
            using DesktopProbe blue = DesktopProbe.Open(
                "game-mcp-blue-" + Guid.NewGuid().ToString("N"),
                Color.FromArgb(255, 20, 20, 200),
                new Point(80, 70),
                new Size(220, 160),
                topMost: true,
                FormBorderStyle.None);

            var api = new Win32WindowApi();
            (int redX, int redY, int redWidth, int redHeight) = api.GetWindowRect(red.Handle);
            (int blueX, int blueY, int blueWidth, int blueHeight) = api.GetWindowRect(blue.Handle);
            int left = Math.Max(redX, blueX);
            int top = Math.Max(redY, blueY);
            int right = Math.Min(redX + redWidth, blueX + blueWidth);
            int bottom = Math.Min(redY + redHeight, blueY + blueHeight);
            Assert.True(right > left && bottom > top);
            int pixelX = ((left + right) / 2) - redX;
            int pixelY = ((top + bottom) / 2) - redY;

            WindowFrameGeometry geometry = api.GetFrameGeometry(red.Handle);
            var capture = new CaptureService(new Win32WindowCapture());
            CaptureOutcome outcome = capture.Capture(red.Handle, geometry, new CaptureRequest());

            Assert.Null(outcome.Error);
            Assert.Equal("windowVisual", outcome.CaptureMethod);
            using var stream = new MemoryStream(outcome.Bytes);
            using var image = new Bitmap(stream);
            Color pixel = image.GetPixel(pixelX, pixelY);
            Assert.InRange(pixel.R, 170, 190);
            Assert.InRange(pixel.B, 0, 40);
        }

        [Fact]
        public void AWarmFullWindowPngStaysNearTheBlit()
        {
            if (Environment.GetEnvironmentVariable("GAME_MCP_DESKTOP_TESTS") != "1")
            {
                Assert.Skip("Set GAME_MCP_DESKTOP_TESTS=1 to run desktop tests.");
            }

            Process[] games = Process.GetProcessesByName("InfiniteMagicraid");
            IntPtr game = IntPtr.Zero;
            foreach (Process process in games)
            {
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    game = process.MainWindowHandle;
                    break;
                }
            }

            DesktopProbe? probe = null;
            IntPtr hwnd = game;
            try
            {
                if (hwnd == IntPtr.Zero)
                {
                    probe = DesktopProbe.Open(
                        "game-mcp-speed-" + Guid.NewGuid().ToString("N"),
                        Color.FromArgb(255, 40, 90, 160),
                        new Point(0, 0),
                        new Size(2582, 1496),
                        topMost: false,
                        FormBorderStyle.None);
                    hwnd = probe.Handle;
                }

                var log = new LogBuffer();
                var locator = new WindowLocator(
                    new Win32WindowEnumerator(),
                    new Win32ProcessQuery(),
                    new Win32WindowApi(),
                    new ThreadDelay(),
                    log);
                string title = hwnd == game ? "" : probe!.Handle == hwnd ? NativeWindowText.Read(hwnd) : "";
                var application = new ApplicationEntry(
                    "Speed",
                    "",
                    null,
                    null,
                    title,
                    hwnd == game ? "InfiniteMagicraid" : null,
                    null,
                    ClickBackend.MouseEvent);
                var capture = new CaptureService(new Win32WindowCapture());
                var samples = new List<double>();
                var captureSamples = new List<double>();
                int imageWidth = 0;
                int imageHeight = 0;
                for (int index = 0; index < 10; index++)
                {
                    var watch = Stopwatch.StartNew();
                    WindowLookup lookup = locator.Find(application, true, true);
                    long findTicks = watch.ElapsedTicks;
                    Assert.True(lookup.Found);
                    CaptureOutcome outcome = capture.Capture(lookup.Hwnd, lookup.Frame, new CaptureRequest());
                    long captureTicks = watch.ElapsedTicks - findTicks;
                    ToolResponses.Image("{}", outcome.Bytes, "image/png");
                    watch.Stop();
                    Assert.Null(outcome.Error);
                    Assert.Equal("windowVisual", outcome.CaptureMethod);
                    imageWidth = outcome.ImageWidth;
                    imageHeight = outcome.ImageHeight;
                    if (index >= 2)
                    {
                        samples.Add(watch.Elapsed.TotalMilliseconds);
                        captureSamples.Add(captureTicks * 1000.0 / Stopwatch.Frequency);
                    }

                    Thread.Sleep(40);
                }

                samples.Sort();
                captureSamples.Sort();
                double median = (samples[3] + samples[4]) / 2.0;
                double captureMedian = (captureSamples[3] + captureSamples[4]) / 2.0;
                Console.WriteLine(
                    "snapshot ms " + string.Join(", ", samples.Select(sample => sample.ToString("0.00")))
                    + " median " + median.ToString("0.00")
                    + " capture " + captureMedian.ToString("0.00")
                    + " source " + (hwnd == game ? "game" : "probe")
                    + " " + imageWidth + "x" + imageHeight);
                Assert.True(median <= 10.0, "Warm snapshot median was " + median.ToString("0.00") + " ms.");
            }
            finally
            {
                probe?.Dispose();
                foreach (Process process in games)
                {
                    process.Dispose();
                }
            }
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT point);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }
    }

    internal sealed class DesktopProbe : IDisposable
    {
        private readonly Thread _thread;
        private readonly ManualResetEvent _ready = new ManualResetEvent(false);
        private readonly List<Point> _clicks = new List<Point>();
        private readonly List<char> _chars = new List<char>();
        private readonly List<Keys> _keys = new List<Keys>();
        private Form? _form;
        private TextBox? _textBox;
        private Point _location = new Point(120, 120);
        private Size _clientSize = new Size(240, 160);
        private bool _topMost = true;
        private FormBorderStyle _border = FormBorderStyle.FixedToolWindow;

        private DesktopProbe(Thread thread)
        {
            _thread = thread;
        }

        public IntPtr Handle { get; private set; }

        public static DesktopProbe Open(string title, Color color)
        {
            return Open(title, color, new Point(120, 120), new Size(240, 160), true, FormBorderStyle.FixedToolWindow);
        }

        public static DesktopProbe Open(string title, Color color, Point location, Size clientSize, bool topMost, FormBorderStyle border)
        {
            DesktopProbe probe = null!;
            var thread = new Thread(() => probe.Run(title, color));
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            probe = new DesktopProbe(thread)
            {
                _location = location,
                _clientSize = clientSize,
                _topMost = topMost,
                _border = border
            };
            thread.Start();
            if (!probe._ready.WaitOne(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The desktop probe window did not open.");
            }

            return probe;
        }

        public void WaitForClick(int clientX, int clientY)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline)
            {
                lock (_clicks)
                {
                    if (_clicks.Any(point => Math.Abs(point.X - clientX) <= 2 && Math.Abs(point.Y - clientY) <= 2))
                    {
                        return;
                    }
                }

                Thread.Sleep(20);
            }

            string seen;
            lock (_clicks)
            {
                seen = _clicks.Count == 0
                    ? "no clicks"
                    : string.Join("; ", _clicks.Select(point => point.X + "," + point.Y));
            }

            Assert.Fail("Click did not arrive at " + clientX + "," + clientY + ". Saw " + seen + ".");
        }

        public void FocusTextBox()
        {
            _form!.Invoke(() => _textBox!.Focus());
        }

        public void BringToFront()
        {
            _form!.Invoke(() =>
            {
                _form.TopMost = false;
                _form.TopMost = true;
                SetWindowPos(_form.Handle, TopMostWindow, 0, 0, 0, 0, KeepPlaceAndShow);
            });
        }

        public void WaitForText()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline)
            {
                string text;
                bool enter;
                lock (_chars)
                {
                    text = new string(_chars.ToArray());
                }

                lock (_keys)
                {
                    enter = _keys.Contains(Keys.Enter);
                }

                if (text.Contains('H') && text.Contains('i') && enter)
                {
                    return;
                }

                Thread.Sleep(20);
            }

            Assert.Fail("Keyboard events did not arrive.");
        }

        public void Dispose()
        {
            if (_form != null && _form.IsHandleCreated)
            {
                _form.BeginInvoke(new Action(() => _form.Close()));
            }

            _thread.Join(TimeSpan.FromSeconds(3));
            _ready.Dispose();
        }

        private void Run(string title, Color color)
        {
            _form = new Form
            {
                Text = title,
                FormBorderStyle = _border,
                StartPosition = FormStartPosition.Manual,
                Location = _location,
                ClientSize = _clientSize,
                BackColor = color,
                KeyPreview = true,
                TopMost = _topMost
            };
            _textBox = new TextBox { Location = new Point(4, 4), Width = 140 };
            _form.Controls.Add(_textBox);
            _form.MouseDown += (_, args) =>
            {
                lock (_clicks)
                {
                    _clicks.Add(args.Location);
                }
            };
            _form.KeyPress += (_, args) =>
            {
                lock (_chars)
                {
                    _chars.Add(args.KeyChar);
                }
            };
            _form.KeyUp += (_, args) =>
            {
                lock (_keys)
                {
                    _keys.Add(args.KeyCode);
                }
            };
            _form.Shown += (_, _) =>
            {
                _form.Refresh();
                Handle = _form.Handle;
                _ready.Set();
            };
            System.Windows.Forms.Application.Run(_form);
        }

        private static readonly IntPtr TopMostWindow = new IntPtr(-1);
        private const uint KeepPlaceAndShow = 0x0001 | 0x0002 | 0x0010 | 0x0040;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    }
}

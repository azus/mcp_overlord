using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using game_automation;
using game_automation_in_csharp.util;

namespace automation_util.tests
{
    [Trait("Category", "Desktop")]
    public class DesktopInputTests
    {
        [Fact]
        public void RealWindowReceivesSearchCaptureClicksAndKeys()
        {
            DesktopGate.UnlessEnabled();
            string title = "automation-util-desktop-" + Guid.NewGuid().ToString("N");
            using var session = DesktopWindow.Open(title, Color.FromArgb(255, 20, 140, 60));

            WindowSearchResult found = WindowSearch.Search(null, title);
            Assert.True(found.Found);
            Assert.Equal(session.Handle, found.Chosen!.Hwnd);

            session.FlushPaint();
            var generator = new ScreenshotGenerator();
            using Bitmap printed = generator.CaptureWindow(session.Handle, ScreenshotGenerator.PW_RENDERFULLCONTENT);
            using Bitmap screen = generator.CaptureClientFromScreen(session.Handle);
            AssertWindowSize(session.Handle, printed);
            AssertClientColor(session.Handle, printed, Color.FromArgb(255, 20, 140, 60));
            AssertClientSize(session.Handle, screen);

            var mouseEvent = new MouseEventClickHelper();
            var sendInput = new SendInputClickHelper();
            session.ReceiveClick(
                () => mouseEvent.Click(session.Handle, 30, 40, MouseButton.Left, 20, false),
                30,
                40);
            session.ReceiveClick(
                () => sendInput.Click(session.Handle, 70, 55, MouseButton.Left, 20, false),
                70,
                55);

            session.FocusTextBox();
            KeyboardUtil.TypeUnicode(session.Handle, "Hi");
            KeyboardUtil.Press(session.Handle, (byte)KeyCode.VK_X, 30);
            KeyboardUtil.KeyDown(session.Handle, (byte)KeyCode.VK_RETURN);
            KeyboardUtil.KeyUp(session.Handle, (byte)KeyCode.VK_RETURN);
            session.WaitForTextAndEnter();
        }

        private static void AssertClientSize(IntPtr hwnd, Bitmap bitmap)
        {
            RECT client = new RECT();
            Assert.True(GetClientRect(hwnd, ref client));
            Assert.Equal(client.Right - client.Left, bitmap.Width);
            Assert.Equal(client.Bottom - client.Top, bitmap.Height);
        }

        private static void AssertWindowSize(IntPtr hwnd, Bitmap bitmap)
        {
            RECT rect = new RECT();
            Assert.True(GetWindowRect(hwnd, ref rect));
            Assert.Equal(rect.Right - rect.Left, bitmap.Width);
            Assert.Equal(rect.Bottom - rect.Top, bitmap.Height);
        }

        private static void AssertClientColor(IntPtr hwnd, Bitmap bitmap, Color expected)
        {
            RECT window = new RECT();
            RECT client = new RECT();
            POINT origin = new POINT();
            Assert.True(GetWindowRect(hwnd, ref window));
            Assert.True(GetClientRect(hwnd, ref client));
            Assert.True(ClientToScreen(hwnd, ref origin));
            int x = (origin.X - window.Left) + ((client.Right - client.Left) / 2);
            int y = (origin.Y - window.Top) + ((client.Bottom - client.Top) / 2);
            Assert.InRange(x, 0, bitmap.Width - 1);
            Assert.InRange(y, 0, bitmap.Height - 1);
            Color pixel = bitmap.GetPixel(x, y);
            Assert.InRange(pixel.R, expected.R - 8, expected.R + 8);
            Assert.InRange(pixel.G, expected.G - 8, expected.G + 8);
            Assert.InRange(pixel.B, expected.B - 8, expected.B + 8);
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hwnd, ref RECT rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hwnd, ref RECT rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }

    internal static class DesktopGate
    {
        public static void UnlessEnabled()
        {
            if (Environment.GetEnvironmentVariable("GAME_MCP_DESKTOP_TESTS") != "1")
            {
                Assert.Skip("Set GAME_MCP_DESKTOP_TESTS=1 to run desktop tests.");
            }
        }
    }

    internal sealed class DesktopWindow : IDisposable
    {
        private Thread? _thread;
        private readonly ManualResetEvent _ready = new ManualResetEvent(false);
        private readonly List<Point> _clicks = new List<Point>();
        private readonly List<Keys> _keysDown = new List<Keys>();
        private readonly List<Keys> _keysUp = new List<Keys>();
        private readonly List<char> _chars = new List<char>();
        private ProbeForm? _form;
        private TextBox? _textBox;

        public IntPtr Handle { get; private set; }

        public void Attach(Thread thread)
        {
            _thread = thread;
        }

        public static DesktopWindow Open(string title, Color color)
        {
            var window = new DesktopWindow();
            var thread = new Thread(() => window.Run(title, color));
            window.Attach(thread);
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            if (!window._ready.WaitOne(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The desktop test window did not open.");
            }

            return window;
        }

        public void ReceiveClick(Action click, int clientX, int clientY)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            while (true)
            {
                Raise();
                click();
                DateTime slice = DateTime.UtcNow.AddMilliseconds(300);
                while (DateTime.UtcNow < slice)
                {
                    if (HasClick(clientX, clientY))
                    {
                        return;
                    }

                    Thread.Sleep(20);
                }

                if (DateTime.UtcNow >= deadline)
                {
                    Assert.Fail("Click did not arrive at " + clientX + "," + clientY + ". Saw " + DescribeClicks() + ".");
                }
            }
        }

        public void Raise()
        {
            _form!.Invoke(() =>
            {
                _form.TopMost = false;
                _form.TopMost = true;
                SetWindowPos(_form.Handle, TOP_MOST_WINDOW, 0, 0, 0, 0, KEEP_PLACE_AND_SHOW);
            });
        }

        public void FlushPaint()
        {
            _form!.Invoke(() =>
            {
                _form.Activate();
                _form.Refresh();
            });
        }

        public void FocusTextBox()
        {
            Raise();
            _form!.Invoke(() => _textBox!.Focus());
        }

        public void WaitForTextAndEnter()
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

                lock (_keysUp)
                {
                    enter = _keysUp.Contains(Keys.Enter) || _keysDown.Contains(Keys.Enter);
                }

                if (text.Contains('H') && text.Contains('i') && text.IndexOf('x') >= 0 && enter)
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

            _thread?.Join(TimeSpan.FromSeconds(3));
            _ready.Dispose();
        }

        private void Run(string title, Color color)
        {
            Application.EnableVisualStyles();
            _form = new ProbeForm(color, _clicks, _keysDown, _keysUp, _chars);
            _form.Text = title;
            _textBox = new TextBox { Location = new Point(4, 4), Width = 120 };
            _form.Controls.Add(_textBox);
            _form.Shown += (_, _) =>
            {
                Handle = _form.Handle;
                _ready.Set();
            };
            Application.Run(_form);
        }

        private sealed class ProbeForm : Form
        {
            private readonly List<Point> _clicks;
            private readonly List<Keys> _keysDown;
            private readonly List<Keys> _keysUp;
            private readonly List<char> _chars;

            public ProbeForm(Color color, List<Point> clicks, List<Keys> keysDown, List<Keys> keysUp, List<char> chars)
            {
                _clicks = clicks;
                _keysDown = keysDown;
                _keysUp = keysUp;
                _chars = chars;
                FormBorderStyle = FormBorderStyle.FixedToolWindow;
                StartPosition = FormStartPosition.Manual;
                Location = new Point(80, 80);
                ClientSize = new Size(240, 160);
                BackColor = color;
                KeyPreview = true;
                TopMost = true;
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                lock (_clicks)
                {
                    _clicks.Add(e.Location);
                }

                base.OnMouseDown(e);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                lock (_keysDown)
                {
                    _keysDown.Add(e.KeyCode);
                }

                base.OnKeyDown(e);
            }

            protected override void OnKeyUp(KeyEventArgs e)
            {
                lock (_keysUp)
                {
                    _keysUp.Add(e.KeyCode);
                }

                base.OnKeyUp(e);
            }

            protected override void OnKeyPress(KeyPressEventArgs e)
            {
                lock (_chars)
                {
                    _chars.Add(e.KeyChar);
                }

                base.OnKeyPress(e);
            }
        }

        private bool HasClick(int clientX, int clientY)
        {
            lock (_clicks)
            {
                return _clicks.Any(point => Math.Abs(point.X - clientX) <= 2 && Math.Abs(point.Y - clientY) <= 2);
            }
        }

        private string DescribeClicks()
        {
            lock (_clicks)
            {
                return _clicks.Count == 0
                    ? "no clicks"
                    : string.Join("; ", _clicks.Select(point => point.X + "," + point.Y));
            }
        }

        private static readonly IntPtr TOP_MOST_WINDOW = new IntPtr(-1);
        private const uint KEEP_PLACE_AND_SHOW = 0x0001 | 0x0002 | 0x0010 | 0x0040;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    }
}

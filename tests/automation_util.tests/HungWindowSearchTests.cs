using System.Runtime.InteropServices;
using game_automation;

namespace automation_util.tests
{
    public class HungWindowSearchTests
    {
        private const int PopupStyle = unchecked((int)0x80000000);

        [Fact]
        public async Task EnumerateReturnsTheTitleOfAWindowWhoseThreadIsBlocked()
        {
            string title = "hung-window-" + Guid.NewGuid().ToString("N");
            using var created = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            IntPtr hwnd = IntPtr.Zero;
            var thread = new Thread(() =>
            {
                hwnd = CreateWindowEx(0, "Static", title, PopupStyle, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                created.Set();
                release.Wait();
                if (hwnd != IntPtr.Zero)
                {
                    DestroyWindow(hwnd);
                }
            });
            thread.IsBackground = true;
            thread.Start();
            try
            {
                CancellationToken cancellationToken = TestContext.Current.CancellationToken;
                Assert.True(created.Wait(TimeSpan.FromSeconds(5), cancellationToken));
                Assert.NotEqual(IntPtr.Zero, hwnd);

                Task<(string Title, IReadOnlyList<WindowCandidate> Windows)> enumerate = Task.Run(
                    () =>
                    {
                        string readTitle = NativeWindowText.Read(hwnd);
                        return (readTitle, WindowSearch.Enumerate());
                    },
                    cancellationToken);
                Task finished = await Task.WhenAny(enumerate, Task.Delay(TimeSpan.FromSeconds(2), cancellationToken));
                Assert.Same(enumerate, finished);
                (string readTitle, IReadOnlyList<WindowCandidate> windows) = await enumerate;
                Assert.Equal(title, readTitle);
                Assert.Contains(windows, candidate => candidate.Hwnd == hwnd && candidate.Title == title);
            }
            finally
            {
                release.Set();
                thread.Join(TimeSpan.FromSeconds(3));
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            int extendedStyle,
            string className,
            string windowName,
            int style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parent,
            IntPtr menu,
            IntPtr instance,
            IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr hwnd);
    }
}

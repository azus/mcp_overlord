using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using mcp_overlord.UI;
using mcp_overlord.UI.Placement;

namespace mcp_overlord.tests
{
    public class WindowBoundsClampTests
    {
        private static readonly MonitorInfo Secondary = new MonitorInfo(
            new PixelRect(-1920, 960, 1920, 1200),
            new PixelRect(-1920, 960, 1920, 1152),
            96,
            false);

        private static readonly MonitorInfo Primary = new MonitorInfo(
            new PixelRect(0, 0, 2560, 1440),
            new PixelRect(0, 0, 2560, 1392),
            144,
            true);

        private static readonly IReadOnlyList<MonitorInfo> Desk = new[] { Secondary, Primary };

        [Fact]
        public void RectangleAlreadyOnThePrimaryMonitorStaysPut()
        {
            var bounds = new PixelRect(100, 80, 800, 600);
            Assert.Equal(bounds, WindowBoundsClamp.Clamp(bounds, Desk, 0, 0));
        }

        [Fact]
        public void RectangleAlreadyOnTheSecondaryMonitorStaysPut()
        {
            var bounds = new PixelRect(-1800, 1000, 800, 600);
            Assert.Equal(bounds, WindowBoundsClamp.Clamp(bounds, Desk, 0, 0));
        }

        [Fact]
        public void PastTheLeftEdgeMovesOntoTheSecondaryWorkArea()
        {
            var bounds = new PixelRect(-2000, 1000, 400, 300);
            Assert.Equal(new PixelRect(-1920, 1000, 400, 300), WindowBoundsClamp.Clamp(bounds, Desk, 0, 0));
        }

        [Fact]
        public void PastTheRightEdgeMovesOntoThePrimaryWorkArea()
        {
            var bounds = new PixelRect(2400, 100, 400, 300);
            Assert.Equal(new PixelRect(2160, 100, 400, 300), WindowBoundsClamp.Clamp(bounds, Desk, 0, 0));
        }

        [Fact]
        public void PastTheTopMovesDown()
        {
            var bounds = new PixelRect(100, -40, 400, 300);
            Assert.Equal(new PixelRect(100, 0, 400, 300), WindowBoundsClamp.Clamp(bounds, Desk, 0, 0));
        }

        [Fact]
        public void PastTheBottomMovesAboveTheTaskbar()
        {
            var bounds = new PixelRect(100, 1200, 400, 300);
            Assert.Equal(new PixelRect(100, 1092, 400, 300), WindowBoundsClamp.Clamp(bounds, Desk, 0, 0));
        }

        [Fact]
        public void ATieGoesToTheMonitorWhoseCenterIsCloser()
        {
            var bounds = new PixelRect(-200, 1000, 400, 200);
            Assert.Equal(new PixelRect(-400, 1000, 400, 200), WindowBoundsClamp.Clamp(bounds, Desk, 0, 0));
        }

        [Fact]
        public void NoOverlapUsesTheNearestMonitor()
        {
            var bounds = new PixelRect(-8000, 1400, 200, 200);
            Assert.Equal(new PixelRect(-1920, 1400, 200, 200), WindowBoundsClamp.Clamp(bounds, Desk, 0, 0));
        }

        [Fact]
        public void ARectangleLargerThanTheWorkAreaShrinks()
        {
            var bounds = new PixelRect(0, 0, 4000, 3000);
            Assert.Equal(new PixelRect(0, 0, 2560, 1392), WindowBoundsClamp.Clamp(bounds, Desk, 0, 0));
        }

        [Fact]
        public void MinimumSizeUsesTheTargetMonitorDpi()
        {
            var onPrimary = new PixelRect(100, 100, 50, 40);
            Assert.Equal(new PixelRect(100, 100, 300, 225), WindowBoundsClamp.Clamp(onPrimary, Desk, 200, 150));

            var onSecondary = new PixelRect(-1800, 1000, 50, 40);
            Assert.Equal(new PixelRect(-1800, 1000, 200, 150), WindowBoundsClamp.Clamp(onSecondary, Desk, 200, 150));
        }

        [Fact]
        public void MinimumSizeCannotPushTheWindowOutOfTheWorkArea()
        {
            var bounds = new PixelRect(100, 100, 100, 100);
            Assert.Equal(new PixelRect(0, 0, 2560, 1392), WindowBoundsClamp.Clamp(bounds, Desk, 4000, 4000));
        }

        [Fact]
        public void AnEmptyMonitorListKeepsThePositionAndAppliesTheMinimum()
        {
            var bounds = new PixelRect(10, 20, 30, 40);
            Assert.Equal(new PixelRect(10, 20, 50, 60), WindowBoundsClamp.Clamp(bounds, Array.Empty<MonitorInfo>(), 50, 60));
        }

        [Fact]
        public void ANegativeSizeDoesNotThrow()
        {
            var bounds = new PixelRect(10, 20, -5, -8);
            Assert.Equal(new PixelRect(10, 20, 0, 0), WindowBoundsClamp.Clamp(bounds, Array.Empty<MonitorInfo>(), 0, 0));
        }

        [Fact]
        public void PixelsScaledByThePrimaryDpiLandBackOnTheSecondaryMonitor()
        {
            var bounds = new PixelRect(-2769, 1656, 2694, 1050);
            Assert.Equal(new PixelRect(-1920, 1062, 1920, 1050), WindowBoundsClamp.Clamp(bounds, Desk, 0, 0));
        }

        [Fact]
        public void AWindowSittingInTheTaskbarMovesIntoTheWorkArea()
        {
            var bounds = new PixelRect(-1800, 2112, 400, 40);
            Assert.Equal(new PixelRect(-1800, 2072, 400, 40), WindowBoundsClamp.Clamp(bounds, Desk, 0, 0));
        }

        [Fact]
        public void NullMonitorsThrow()
        {
            Assert.Throws<ArgumentNullException>(() => WindowBoundsClamp.Clamp(new PixelRect(0, 0, 10, 10), null!, 0, 0));
        }
    }

    public class LegacyBoundsConverterTests
    {
        private static readonly MonitorInfo Secondary = new MonitorInfo(
            new PixelRect(-1920, 960, 1920, 1200),
            new PixelRect(-1920, 960, 1920, 1152),
            96,
            false);

        private static readonly MonitorInfo Primary = new MonitorInfo(
            new PixelRect(0, 0, 2560, 1440),
            new PixelRect(0, 0, 2560, 1392),
            144,
            true);

        private static readonly IReadOnlyList<MonitorInfo> Desk = new[] { Secondary, Primary };

        [Fact]
        public void SavedSecondaryDipBecomesTheSamePhysicalPixels()
        {
            PixelRect? pixels = LegacyBoundsConverter.ToPixels(-1846, 1104, 1796, 700, Desk);
            Assert.Equal(new PixelRect(-1846, 1104, 1796, 700), pixels);
        }

        [Fact]
        public void SavedPrimaryDipIsScaledByThePrimaryDpi()
        {
            PixelRect? pixels = LegacyBoundsConverter.ToPixels(100, 80, 600, 400, Desk);
            Assert.Equal(new PixelRect(150, 120, 900, 600), pixels);
        }

        [Fact]
        public void SameDpiMonitorsKeepTheOneThatContainsTheRectangle()
        {
            var left = new MonitorInfo(
                new PixelRect(-1920, 0, 1920, 1080),
                new PixelRect(-1920, 0, 1920, 1040),
                96,
                false);
            var right = new MonitorInfo(
                new PixelRect(0, 0, 1920, 1080),
                new PixelRect(0, 0, 1920, 1040),
                96,
                true);
            MonitorInfo[] monitors = { left, right };

            Assert.Equal(
                new PixelRect(-1500, 40, 800, 600),
                LegacyBoundsConverter.ToPixels(-1500, 40, 800, 600, monitors));
            Assert.Equal(
                new PixelRect(120, 40, 800, 600),
                LegacyBoundsConverter.ToPixels(120, 40, 800, 600, monitors));
        }

        [Fact]
        public void OffScreenDipMovesToTheNearestWorkArea()
        {
            PixelRect? pixels = LegacyBoundsConverter.ToPixels(-10000, -10000, 200, 150, Desk);
            Assert.Equal(new PixelRect(-1920, 960, 200, 150), pixels);
        }

        [Fact]
        public void UnusableDipReturnsNull()
        {
            Assert.Null(LegacyBoundsConverter.ToPixels(double.NaN, 10, 100, 100, Desk));
            Assert.Null(LegacyBoundsConverter.ToPixels(10, double.NaN, 100, 100, Desk));
            Assert.Null(LegacyBoundsConverter.ToPixels(10, 10, 0, 100, Desk));
            Assert.Null(LegacyBoundsConverter.ToPixels(10, 10, 100, -1, Desk));
            Assert.Null(LegacyBoundsConverter.ToPixels(double.PositiveInfinity, 10, 100, 100, Desk));
        }

        [Fact]
        public void SavedPixelsWinOverLegacyDip()
        {
            var pixels = new PixelRect(120, 40, 800, 600);
            PixelRect? resolved = LegacyBoundsConverter.Resolve(pixels, -1846, 1104, 1796, 700, Desk, 0, 0);
            Assert.Equal(pixels, resolved);
        }

        [Fact]
        public void MissingPixelsFallBackToLegacyDip()
        {
            PixelRect? resolved = LegacyBoundsConverter.Resolve(null, -1846, 1104, 1796, 700, Desk, 0, 0);
            Assert.Equal(new PixelRect(-1846, 1104, 1796, 700), resolved);
        }

        [Fact]
        public void AnEmptyPixelRectangleFallsBackToLegacyDip()
        {
            PixelRect? resolved = LegacyBoundsConverter.Resolve(new PixelRect(1, 2, 0, 10), -1846, 1104, 1796, 700, Desk, 0, 0);
            Assert.Equal(new PixelRect(-1846, 1104, 1796, 700), resolved);
        }

        [Fact]
        public void OffScreenPixelsAreClampedEvenWhenLegacyDipIsMissing()
        {
            PixelRect? resolved = LegacyBoundsConverter.Resolve(
                new PixelRect(-2769, 1656, 2694, 1050),
                double.NaN,
                double.NaN,
                1100,
                700,
                Desk,
                0,
                0);
            Assert.Equal(new PixelRect(-1920, 1062, 1920, 1050), resolved);
        }

        [Fact]
        public void NothingSavedReturnsNull()
        {
            Assert.Null(LegacyBoundsConverter.Resolve(null, double.NaN, double.NaN, 1100, 700, Desk, 0, 0));
        }
    }

    public class UiSettingsPixelBoundsTests
    {
        [Fact]
        public void PixelBoundsRoundTripThroughJson()
        {
            string path = TempPath();
            try
            {
                var store = new UiSettingsStore();
                var settings = new UiSettings
                {
                    WindowBoundsPixels = new PixelRect(-1846, 1104, 1796, 700),
                    WindowState = "Normal",
                    Left = 12
                };

                store.Save(path, settings);
                string json = File.ReadAllText(path);
                Assert.Contains("\"windowBoundsPixels\"", json, StringComparison.Ordinal);
                Assert.Contains("\"x\": -1846", json, StringComparison.Ordinal);
                Assert.DoesNotContain("\"hasArea\"", json, StringComparison.Ordinal);
                Assert.DoesNotContain("\"right\"", json, StringComparison.Ordinal);

                UiSettings loaded = store.Load(path);
                Assert.Equal(new PixelRect(-1846, 1104, 1796, 700), loaded.WindowBoundsPixels);
                Assert.Equal(12, loaded.Left);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void AFileWrittenBeforePixelBoundsStillLoads()
        {
            string path = TempPath();
            try
            {
                File.WriteAllText(
                    path,
                    """
                    {
                      "left": -1846,
                      "top": 1104,
                      "width": 1796,
                      "height": 700,
                      "windowState": "Normal"
                    }
                    """);

                UiSettings loaded = new UiSettingsStore().Load(path);
                Assert.Null(loaded.WindowBoundsPixels);
                Assert.Equal(-1846, loaded.Left);
                Assert.Equal(1104, loaded.Top);
                Assert.Equal(1796, loaded.Width);
                Assert.Equal(700, loaded.Height);
                Assert.Equal("Normal", loaded.WindowState);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ACorruptFileReturnsDefaults()
        {
            string path = TempPath();
            try
            {
                File.WriteAllText(path, "{");
                UiSettings loaded = new UiSettingsStore().Load(path);
                Assert.Null(loaded.WindowBoundsPixels);
                Assert.True(double.IsNaN(loaded.Left));
                Assert.Equal(1100, loaded.Width);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string TempPath()
        {
            return Path.Combine(Path.GetTempPath(), "mcp-placement-" + Guid.NewGuid().ToString("N") + ".json");
        }
    }

    public class Win32MonitorLayoutTests
    {
        [Fact]
        public void AttachedMonitorsExposeAWorkAreaAndDpi()
        {
            IReadOnlyList<MonitorInfo> monitors = new Win32MonitorLayout().GetMonitors();
            Assert.True(monitors.Count(monitor => monitor.IsPrimary) <= 1);
            if (monitors.Count == 0)
            {
                return;
            }

            Assert.Equal(1, monitors.Count(monitor => monitor.IsPrimary));
            foreach (MonitorInfo monitor in monitors)
            {
                Assert.True(monitor.WorkArea.HasArea);
                Assert.True(monitor.Bounds.HasArea);
                Assert.True(monitor.Dpi >= 96);
                Assert.True(monitor.Bounds.Width >= monitor.WorkArea.Width);
                Assert.True(monitor.Bounds.Height >= monitor.WorkArea.Height);
                Assert.True(monitor.WorkArea.IntersectionArea(monitor.Bounds) > 0);
            }
        }
    }

    [Trait("Category", "Desktop")]
    public class WindowPlacementDesktopTests
    {
        private static readonly object Gate = new object();

        [Fact]
        public void EveryMonitorRestoresTheExactPixelsBeforeTheWindowIsVisible()
        {
            RequireDesktop();
            OnSta(() =>
            {
                IReadOnlyList<MonitorInfo> monitors = RequireMonitors();
                foreach (MonitorInfo monitor in monitors)
                {
                    if (!TryInset(monitor, out PixelRect requested))
                    {
                        continue;
                    }

                    PlacementObservation observation = Show(new Win32MonitorLayout(), requested, maximized: false);
                    Assert.Equal(requested, observation.FirstVisible);
                    Assert.Equal(requested, observation.Settled);
                    AssertVisibleSamples(observation, requested);
                    Assert.False(observation.Snapshot.IsMaximized);
                    Assert.Equal(requested, observation.Snapshot.NormalBounds);
                }
            });
        }

        [Fact]
        public void AnOffScreenRectangleIsAlreadyInsideAWorkAreaWhenShown()
        {
            RequireDesktop();
            OnSta(() =>
            {
                IReadOnlyList<MonitorInfo> monitors = RequireMonitors();
                var requested = new PixelRect(-100000, -100000, 480, 360);
                PixelRect expected = WindowBoundsClamp.Clamp(requested, monitors, 0, 0);
                PlacementObservation observation = Show(new Win32MonitorLayout(), requested, maximized: false);
                Assert.Equal(expected, observation.FirstVisible);
                Assert.Equal(expected, observation.Settled);
                AssertFullyInsideSomeWorkArea(observation.Settled, monitors);
            });
        }

        [Fact]
        public void RoundTripThroughTheSettingsFileKeepsTheSamePixels()
        {
            RequireDesktop();
            OnSta(() =>
            {
                IReadOnlyList<MonitorInfo> monitors = RequireMonitors();
                MonitorInfo monitor = monitors.FirstOrDefault(item => !item.IsPrimary) ?? monitors[0];
                if (!TryInset(monitor, out PixelRect requested))
                {
                    Assert.Skip("Work area is too small.");
                }

                string path = Path.Combine(Path.GetTempPath(), "mcp-placement-" + Guid.NewGuid().ToString("N") + ".json");
                try
                {
                    var layout = new Win32MonitorLayout();
                    PlacementObservation first = Show(layout, requested, maximized: false);
                    new UiSettingsStore().Save(path, new UiSettings
                    {
                        WindowBoundsPixels = first.Snapshot.NormalBounds,
                        WindowState = "Normal"
                    });

                    UiSettings loaded = new UiSettingsStore().Load(path);
                    PixelRect? resolved = LegacyBoundsConverter.Resolve(
                        loaded.WindowBoundsPixels,
                        loaded.Left,
                        loaded.Top,
                        loaded.Width,
                        loaded.Height,
                        monitors,
                        0,
                        0);
                    Assert.NotNull(resolved);
                    PlacementObservation second = Show(layout, resolved.Value, maximized: false);
                    Assert.Equal(first.Settled, second.FirstVisible);
                    Assert.Equal(first.Settled, second.Settled);
                }
                finally
                {
                    File.Delete(path);
                }
            });
        }

        [Fact]
        public void AMaximizedWindowReopensOnTheSameMonitorAndKeepsItsNormalBounds()
        {
            RequireDesktop();
            OnSta(() =>
            {
                IReadOnlyList<MonitorInfo> monitors = RequireMonitors();
                MonitorInfo monitor = monitors.FirstOrDefault(item => !item.IsPrimary) ?? monitors[0];
                if (!TryInset(monitor, out PixelRect normal))
                {
                    Assert.Skip("Work area is too small.");
                }

                var layout = new Win32MonitorLayout();
                PlacementObservation first = Show(layout, normal, maximized: true);
                Assert.True(first.Snapshot.IsMaximized);
                Assert.Equal(normal, first.Snapshot.NormalBounds);
                AssertCenterOn(first.FirstVisible, monitor);
                AssertCenterOn(first.Settled, monitor);

                PlacementObservation second = Show(layout, first.Snapshot.NormalBounds, maximized: true);
                Assert.True(second.Snapshot.IsMaximized);
                Assert.Equal(normal, second.Snapshot.NormalBounds);
                AssertCenterOn(second.FirstVisible, monitor);
                AssertCenterOn(second.Settled, monitor);
            });
        }

        [Fact]
        public void ADifferentDpiIsNotScaledAsIfItWereThePrimaryMonitor()
        {
            RequireDesktop();
            OnSta(() =>
            {
                IReadOnlyList<MonitorInfo> monitors = RequireMonitors();
                MonitorInfo? primary = monitors.FirstOrDefault(item => item.IsPrimary);
                MonitorInfo? other = monitors.FirstOrDefault(item => primary != null && item.Dpi != primary.Dpi);
                if (primary == null || other == null)
                {
                    Assert.Skip("Need two monitors with different DPI.");
                }

                if (!TryInset(other, out PixelRect requested))
                {
                    Assert.Skip("Work area is too small.");
                }

                PlacementObservation observation = Show(new Win32MonitorLayout(), requested, maximized: false);
                Assert.Equal(requested.Width, observation.FirstVisible.Width);
                Assert.Equal(requested.Height, observation.FirstVisible.Height);
                Assert.Equal(requested, observation.Settled);
                AssertCenterOn(observation.FirstVisible, other);
            });
        }

        private static void RequireDesktop()
        {
            if (Environment.GetEnvironmentVariable("GAME_MCP_DESKTOP_TESTS") != "1")
            {
                Assert.Skip("Set GAME_MCP_DESKTOP_TESTS=1 to run desktop tests.");
            }
        }

        private static IReadOnlyList<MonitorInfo> RequireMonitors()
        {
            IReadOnlyList<MonitorInfo> monitors = new Win32MonitorLayout().GetMonitors();
            if (monitors.Count == 0)
            {
                Assert.Skip("No attached monitors.");
            }

            return monitors;
        }

        private static void OnSta(Action action)
        {
            lock (Gate)
            {
                Exception? failure = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();
                if (!thread.Join(TimeSpan.FromSeconds(30)))
                {
                    throw new TimeoutException("Window placement test did not finish.");
                }

                if (failure != null)
                {
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }
            }
        }

        private static PlacementObservation Show(IMonitorLayout layout, PixelRect? requested, bool maximized)
        {
            PixelRect? shown = null;
            var visibleSamples = new List<PixelRect>();
            var window = new Window
            {
                Title = "game-mcp-placement",
                Width = 400,
                Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
                ShowActivated = true,
                Opacity = 0
            };
            var binder = new WindowPlacementBinder(window, layout);
            window.SourceInitialized += (_, _) => RememberShownRectangle(window, binder, () => shown, value => shown = value);
            window.LocationChanged += (_, _) => RememberVisible(window, binder, visibleSamples, () => shown != null);
            window.SizeChanged += (_, _) => RememberVisible(window, binder, visibleSamples, () => shown != null);

            try
            {
                binder.Restore(requested, maximized);
                window.Show();
                PumpUntilIdle(window.Dispatcher);
                if (!binder.TryGetOuterBounds(out PixelRect settled))
                {
                    throw new InvalidOperationException("The window has no outer bounds.");
                }

                if (shown == null)
                {
                    throw new InvalidOperationException("The window was never shown. Settled at " + settled);
                }

                return new PlacementObservation(shown.Value, settled, binder.Capture(), visibleSamples);
            }
            finally
            {
                window.Close();
            }
        }

        private static void RememberShownRectangle(
            Window window,
            WindowPlacementBinder binder,
            Func<PixelRect?> current,
            Action<PixelRect> store)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            HwndSource? source = HwndSource.FromHwnd(handle);
            if (source == null)
            {
                return;
            }

            source.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                const int WindowShow = 0x0018;
                const int WindowPosChanging = 0x0046;
                const uint ShowWindowFlag = 0x0040;
                bool showing = message == WindowShow && wParam != IntPtr.Zero;
                if (!showing && message == WindowPosChanging && lParam != IntPtr.Zero)
                {
                    showing = (Marshal.PtrToStructure<WindowPosNative>(lParam).Flags & ShowWindowFlag) != 0;
                }

                if (showing && current() == null && binder.TryGetOuterBounds(out PixelRect rect))
                {
                    store(rect);
                }

                return IntPtr.Zero;
            });
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

        private static void RememberVisible(
            Window window,
            WindowPlacementBinder binder,
            List<PixelRect> samples,
            Func<bool> shown)
        {
            if (!shown() || !window.IsVisible)
            {
                return;
            }

            if (binder.TryGetOuterBounds(out PixelRect rect))
            {
                samples.Add(rect);
            }
        }

        private static void PumpUntilIdle(Dispatcher dispatcher)
        {
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(
                new Action(() => frame.Continue = false),
                DispatcherPriority.ApplicationIdle);
            Dispatcher.PushFrame(frame);
        }

        private static bool TryInset(MonitorInfo monitor, out PixelRect bounds)
        {
            int width = Math.Min(640, monitor.WorkArea.Width - 96);
            int height = Math.Min(480, monitor.WorkArea.Height - 72);
            bounds = new PixelRect(monitor.WorkArea.X + 48, monitor.WorkArea.Y + 36, width, height);
            return width >= 200
                && height >= 160
                && bounds.Right <= monitor.WorkArea.Right
                && bounds.Bottom <= monitor.WorkArea.Bottom;
        }

        private static void AssertVisibleSamples(PlacementObservation observation, PixelRect expected)
        {
            foreach (PixelRect sample in observation.VisibleSamples)
            {
                Assert.Equal(expected, sample);
            }
        }

        private static void AssertFullyInsideSomeWorkArea(PixelRect rect, IReadOnlyList<MonitorInfo> monitors)
        {
            bool inside = monitors.Any(monitor =>
                rect.X >= monitor.WorkArea.X
                && rect.Y >= monitor.WorkArea.Y
                && rect.Right <= monitor.WorkArea.Right
                && rect.Bottom <= monitor.WorkArea.Bottom);
            Assert.True(inside, "Settled bounds were " + rect);
        }

        private static void AssertCenterOn(PixelRect rect, MonitorInfo monitor)
        {
            bool onMonitor = rect.CenterX >= monitor.Bounds.X
                && rect.CenterX < monitor.Bounds.Right
                && rect.CenterY >= monitor.Bounds.Y
                && rect.CenterY < monitor.Bounds.Bottom;
            Assert.True(onMonitor, "Center of " + rect + " is outside " + monitor.Bounds);
        }

        private sealed class PlacementObservation
        {
            public PlacementObservation(
                PixelRect firstVisible,
                PixelRect settled,
                WindowPlacementSnapshot snapshot,
                IReadOnlyList<PixelRect> visibleSamples)
            {
                FirstVisible = firstVisible;
                Settled = settled;
                Snapshot = snapshot;
                VisibleSamples = visibleSamples;
            }

            public PixelRect FirstVisible { get; }

            public PixelRect Settled { get; }

            public WindowPlacementSnapshot Snapshot { get; }

            public IReadOnlyList<PixelRect> VisibleSamples { get; }
        }
    }
}

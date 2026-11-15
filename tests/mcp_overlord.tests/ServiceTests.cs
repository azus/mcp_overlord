using System.Drawing;
using System.Drawing.Imaging;
using game_automation;
using mcp_overlord.Configuration;
using mcp_overlord.Logging;
using mcp_overlord.Services;
using mcp_overlord.Services.Native;
using mcp_overlord.Tools;
using ModelContextProtocol.Protocol;

namespace mcp_overlord.tests
{
    public class WindowLocatorTests
    {
        [Fact]
        public void TieBreaksAndReportsTheMatchCount()
        {
            var world = new FakeWorld();
            world.AddWindow(1, "Game", "Lobby", minimized: true, zOrder: 0);
            world.AddWindow(2, "Game", "Lobby", zOrder: 4);
            world.AddWindow(3, "Other", "Lobby");
            AppComposition composition = world.Build();
            ApplicationEntry application = App("Game", "Game", "Lobby");

            WindowLookup lookup = composition.Locator.Find(application, false, true);

            Assert.True(lookup.Found);
            Assert.Equal(2, lookup.Hwnd.ToInt64());
            Assert.Equal(2, lookup.MatchCount);
            Assert.Equal("normal", lookup.State);
            Assert.Contains("2 windows", lookup.Message);
            Assert.Contains(composition.Log.Snapshot(), entry => entry.Level == LogLevel.Warning && entry.Message.Contains("2 windows"));
        }

        [Fact]
        public void FindDoesNotRestoreAndInputDoes()
        {
            var world = new FakeWorld();
            world.AddWindow(5, "Game", "Lobby", minimized: true);
            world.MinimizedAfterRestore.Add(new IntPtr(5));
            AppComposition composition = world.Build();
            ApplicationEntry application = App("Game", "Game", "Lobby");

            WindowLookup listed = composition.Locator.Find(application, false);
            Assert.Equal("minimized", listed.State);
            Assert.Empty(world.Restored);

            WindowLookup restored = composition.Locator.Find(application, true);
            Assert.Equal(new[] { new IntPtr(5) }, world.Restored);
            Assert.Contains(TimeSpan.FromMilliseconds(200), world.Delays);
            Assert.Equal("normal", restored.State);
        }

        [Fact]
        public void ProcessWithoutAWindowAndAMissingProcessUseDifferentMessages()
        {
            var world = new FakeWorld();
            world.Processes.Add(new ProcessSnapshot { Id = 9, Name = "Game" });
            AppComposition composition = world.Build();

            WindowLookup running = composition.Locator.Find(App("Game", "Game", null, executable: "C:\\game.exe"), false);
            Assert.False(running.Found);
            Assert.True(running.ProcessRunning);
            Assert.Contains("process id 9", running.Message);
            Assert.Contains("launch_application", running.Message);

            world.Processes.Clear();
            WindowLookup absent = composition.Locator.Find(new ApplicationEntry("Watch", "", null, null, "Missing", null, null, ClickBackend.MouseEvent), false);
            Assert.False(absent.ProcessRunning);
            Assert.Contains("No matching window was found.", absent.Message);
            Assert.DoesNotContain("launch_application", absent.Message);
        }

        [Fact]
        public void CacheIsDroppedWhenTheWindowDisappears()
        {
            var world = new FakeWorld();
            world.AddWindow(1, "Game", "Lobby", zOrder: 2);
            AppComposition composition = world.Build();
            ApplicationEntry application = App("Game", "Game", "Lobby");
            Assert.Equal(1, composition.Locator.Find(application, false).Hwnd.ToInt64());

            world.WindowExists[new IntPtr(1)] = false;
            world.AddWindow(2, "Game", "Lobby", foreground: true);
            Assert.Equal(2, composition.Locator.Find(application, false).Hwnd.ToInt64());
        }

        [Fact]
        public void ACachedWindowDoesNotEnumerateAgain()
        {
            var world = new FakeWorld();
            world.AddWindow(1, "Game", "Lobby");
            AppComposition composition = world.Build();
            ApplicationEntry application = App("Game", "Game", "Lobby");

            Assert.Equal(1, composition.Locator.Find(application, false).Hwnd.ToInt64());
            int calls = world.EnumerateCalls;
            WindowLookup again = composition.Locator.Find(application, false);

            Assert.Equal(1, again.Hwnd.ToInt64());
            Assert.Equal(calls, world.EnumerateCalls);
        }

        private static ApplicationEntry App(string name, string? processName, string? title, string? executable = null)
        {
            return new ApplicationEntry(name, executable ?? "", null, null, title ?? "", processName, null, ClickBackend.MouseEvent);
        }
    }

    public class AppLauncherTests
    {
        [Fact]
        public void PassesArgumentsAndClampsTheWait()
        {
            var world = new FakeWorld();
            AppComposition composition = world.Build();
            var application = new ApplicationEntry(
                "Game",
                "C:\\game.exe",
                "-log \"quoted\"",
                "C:\\work",
                "Game",
                "Game",
                null,
                ClickBackend.MouseEvent);

            LaunchOutcome outcome = composition.Launcher.Launch(application, 200);

            Assert.Null(outcome.Error);
            Assert.True(outcome.Started);
            Assert.False(outcome.WindowFound);
            Assert.True(outcome.WaitWasClamped);
            Assert.Equal(120, outcome.AppliedWaitSeconds);
            Assert.Equal("C:\\game.exe", world.Starts[0].FileName);
            Assert.Equal("-log \"quoted\"", world.Starts[0].Arguments);
            Assert.Equal("C:\\work", world.Starts[0].WorkingDirectory);
        }

        [Fact]
        public void NotLaunchableAndStartFailureAreToolErrors()
        {
            var world = new FakeWorld();
            world.StartError = new InvalidOperationException("access denied");
            AppComposition composition = world.Build();

            LaunchOutcome missing = composition.Launcher.Launch(
                new ApplicationEntry("Watch", "", null, null, "Title", null, null, ClickBackend.MouseEvent),
                0);
            Assert.Equal(ToolErrors.NotLaunchable("Watch"), missing.Error);

            LaunchOutcome failed = composition.Launcher.Launch(
                new ApplicationEntry("Game", "C:\\game.exe", null, null, "Title", null, null, ClickBackend.MouseEvent),
                double.NaN);
            Assert.Contains("Failed to launch 'Game': access denied", failed.Error);
            Assert.Equal(0, failed.AppliedWaitSeconds);
            Assert.True(failed.WaitWasClamped);
        }

        [Fact]
        public void ZeroWaitDoesNotPoll()
        {
            var world = new FakeWorld();
            AppComposition composition = world.Build();
            composition.Launcher.Launch(
                new ApplicationEntry("Game", "C:\\game.exe", null, null, "Title", "Game", null, ClickBackend.MouseEvent),
                0);

            Assert.Empty(world.Delays);
        }
    }

    public class CaptureServiceTests
    {
        [Fact]
        public void ClipsTheRegionAndDownscales()
        {
            var capture = new FakeCapture();
            var service = new CaptureService(capture);
            CaptureOutcome outcome = service.Capture(
                new IntPtr(1),
                capture.Geometry,
                new CaptureRequest
                {
                    RegionX = -10,
                    RegionY = -5,
                    RegionWidth = 30,
                    RegionHeight = 20,
                    MaxWidth = 10,
                    Format = "png"
                });

            Assert.Null(outcome.Error);
            Assert.Equal(0, outcome.RegionX);
            Assert.Equal(0, outcome.RegionY);
            Assert.Equal(20, outcome.RegionWidth);
            Assert.Equal(15, outcome.RegionHeight);
            Assert.Equal(20, outcome.ImageWidth);
            Assert.Equal(1, outcome.Scale);
            Assert.Equal("png", outcome.Format);
            Assert.Equal(new byte[] { 137, 80, 78, 71 }, outcome.Bytes.Take(4).ToArray());

            CaptureOutcome scaled = service.Capture(
                new IntPtr(1),
                capture.Geometry,
                new CaptureRequest { MaxWidth = 10, Format = "png" });
            Assert.Null(scaled.Error);
            Assert.Equal(capture.Geometry.FrameWidth, scaled.RegionWidth);
            Assert.Equal(capture.Geometry.FrameHeight, scaled.RegionHeight);
            Assert.Equal(64, scaled.ImageWidth);
            Assert.Equal(64.0 / capture.Geometry.FrameWidth, scaled.Scale);
        }

        [Fact]
        public void TheSnapshotIncludesTheTitleBar()
        {
            var capture = new FakeCapture();
            var service = new CaptureService(capture);
            CaptureOutcome outcome = service.Capture(new IntPtr(1), capture.Geometry, new CaptureRequest());

            Assert.Null(outcome.Error);
            Assert.Equal(capture.Geometry.FrameWidth, outcome.WindowWidth);
            Assert.Equal(capture.Geometry.FrameHeight, outcome.WindowHeight);
            Assert.Equal(capture.Geometry.ClientOffsetX, outcome.ClientOffsetX);
            Assert.Equal(capture.Geometry.ClientOffsetY, outcome.ClientOffsetY);
            using var image = new Bitmap(new MemoryStream(outcome.Bytes));
            Assert.Equal(FakeCapture.ChromeColor.R, image.GetPixel(0, 0).R);
            Assert.Equal(
                FakeCapture.ClientColor.R,
                image.GetPixel(capture.Geometry.ClientOffsetX, capture.Geometry.ClientOffsetY).R);
        }

        [Fact]
        public void EmptyIntersectionIsAnError()
        {
            var capture = new FakeCapture();
            var service = new CaptureService(capture);
            CaptureOutcome outcome = service.Capture(
                new IntPtr(1),
                capture.Geometry,
                new CaptureRequest { RegionX = 400, RegionY = 400, RegionWidth = 10, RegionHeight = 10 });

            Assert.Equal(ToolErrors.RegionMiss(capture.Geometry.FrameWidth, capture.Geometry.FrameHeight), outcome.Error);
        }

        [Fact]
        public void ABitmapThatIsNotTheWindowSizeIsAnError()
        {
            var capture = new FakeCapture
            {
                PrintFactory = flags => FakeCapture.Solid(Color.Red, 4, 4),
                ScreenFactory = () => FakeCapture.Solid(Color.Red, 4, 4)
            };
            var service = new CaptureService(capture);
            CaptureOutcome outcome = service.Capture(new IntPtr(1), capture.Geometry, new CaptureRequest());

            Assert.Equal(
                ToolErrors.CaptureSizeMismatch(4, 4, capture.Geometry.FrameWidth, capture.Geometry.FrameHeight),
                outcome.Error);
            Assert.Equal(new uint[] { 0, 2 }, capture.Flags);
            Assert.Equal(1, capture.ScreenCalls);
        }

        [Fact]
        public void JpegAndUnknownFormat()
        {
            var capture = new FakeCapture();
            var service = new CaptureService(capture);
            CaptureOutcome jpeg = service.Capture(new IntPtr(1), capture.Geometry, new CaptureRequest { Format = "JPEG", Quality = 40 });
            CaptureOutcome bad = service.Capture(new IntPtr(1), capture.Geometry, new CaptureRequest { Format = "gif" });

            Assert.Equal("jpeg", jpeg.Format);
            Assert.Equal(new byte[] { 0xFF, 0xD8 }, jpeg.Bytes.Take(2).ToArray());
            Assert.Equal(ToolErrors.UnknownFormat("gif"), bad.Error);
        }

        [Fact]
        public void BlackFramesFallThroughToTheScreen()
        {
            var capture = new FakeCapture
            {
                PrintFactory = flags => FakeCapture.Solid(Color.Black, FakeCapture.DefaultGeometry.FrameWidth, FakeCapture.DefaultGeometry.FrameHeight),
                ScreenFactory = () => FakeCapture.Solid(Color.FromArgb(255, 30, 40, 50), FakeCapture.DefaultGeometry.FrameWidth, FakeCapture.DefaultGeometry.FrameHeight)
            };
            var service = new CaptureService(capture);
            CaptureOutcome outcome = service.Capture(new IntPtr(1), capture.Geometry, new CaptureRequest());

            Assert.Equal(new uint[] { 0, 2 }, capture.Flags);
            Assert.Equal(1, capture.ScreenCalls);
            Assert.Equal("screen", outcome.CaptureMethod);
            Assert.Null(outcome.Warning);
        }

        [Fact]
        public void ABlackClientUnderALightTitleBarIsBlack()
        {
            var capture = new FakeCapture
            {
                PrintFactory = flags => FakeCapture.Painted(FakeCapture.DefaultGeometry, Color.White, Color.Black),
                ScreenFactory = () => FakeCapture.Painted(FakeCapture.DefaultGeometry, Color.White, Color.Black)
            };
            var service = new CaptureService(capture);
            CaptureOutcome outcome = service.Capture(new IntPtr(1), capture.Geometry, new CaptureRequest());

            Assert.True(outcome.BlackFrame);
            Assert.Equal(CaptureService.BlackFrameWarning, outcome.Warning);
            Assert.NotEmpty(outcome.Bytes);
        }

        [Fact]
        public void AnAllBlackCaptureReturnsTheImageAndAWarning()
        {
            var geometry = new WindowFrameGeometry(0, 0, 20, 10, 0, 0, 20, 10);
            var capture = new FakeCapture
            {
                Geometry = geometry,
                PrintFactory = flags => FakeCapture.Solid(Color.Black, 20, 10),
                ScreenFactory = () => FakeCapture.Solid(Color.Black, 20, 10)
            };
            var service = new CaptureService(capture);
            CaptureOutcome outcome = service.Capture(new IntPtr(1), geometry, new CaptureRequest());

            Assert.True(outcome.BlackFrame);
            Assert.Equal(CaptureService.BlackFrameWarning, outcome.Warning);
            Assert.NotEmpty(outcome.Bytes);
        }

        [Fact]
        public void ThirtyTwoBitFramesStayAliveAfterTheBlackCheck()
        {
            using Bitmap bitmap = new Bitmap(8, 6, PixelFormat.Format32bppRgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.FromArgb(255, 40, 80, 120));
            }

            bool black = CaptureService.IsEffectivelyBlack(bitmap);
            Assert.False(black);
            Assert.Equal(40, bitmap.GetPixel(1, 1).R);

            var capture = new FakeCapture();
            capture.PrintFactory = flags => Solid32(
                Color.FromArgb(255, 10, 20, 30),
                capture.Geometry.FrameWidth,
                capture.Geometry.FrameHeight);
            var service = new CaptureService(capture);
            CaptureOutcome outcome = service.Capture(new IntPtr(1), capture.Geometry, new CaptureRequest());

            Assert.Null(outcome.Error);
            Assert.NotEmpty(outcome.Bytes);
            Assert.Equal(new byte[] { 137, 80, 78, 71 }, outcome.Bytes.Take(4).ToArray());
            Assert.Equal("printWindow", outcome.CaptureMethod);
            Assert.Equal(capture.Geometry.FrameWidth, outcome.ImageWidth);
            Assert.Equal(capture.Geometry.FrameHeight, outcome.ImageHeight);
        }

        [Fact]
        public void ACoveredWindowDoesNotUseTheScreen()
        {
            var capture = new FakeCapture
            {
                Covered = true,
                PrintFactory = flags => FakeCapture.Solid(Color.Black, FakeCapture.DefaultGeometry.FrameWidth, FakeCapture.DefaultGeometry.FrameHeight),
                ScreenFactory = () => FakeCapture.Solid(Color.FromArgb(255, 200, 20, 20), FakeCapture.DefaultGeometry.FrameWidth, FakeCapture.DefaultGeometry.FrameHeight)
            };
            var service = new CaptureService(capture);
            CaptureOutcome outcome = service.Capture(new IntPtr(1), capture.Geometry, new CaptureRequest());

            Assert.Equal(0, capture.ScreenCalls);
            Assert.True(outcome.BlackFrame);
            Assert.NotEqual("screen", outcome.CaptureMethod);
            Assert.Equal(CaptureService.BlackFrameWarning, outcome.Warning);
        }

        [Fact]
        public void StoredPngMatchesTheSourcePixelsWhenTheEncoderIsPresent()
        {
            if (!NativeStoredPng.IsAvailable)
            {
                Assert.Skip("snapenc.dll was not built.");
            }

            const int width = 5;
            const int height = 2;
            int stride = width * 4;
            byte[] bgra = new byte[stride * height];
            SetPixel(bgra, stride, 0, 0, 10, 20, 30);
            SetPixel(bgra, stride, 4, 0, 200, 10, 5);
            SetPixel(bgra, stride, 1, 1, 7, 8, 9);
            SetPixel(bgra, stride, 4, 1, 1, 2, 3);

            Assert.True(NativeStoredPng.TryEncode(bgra, width, height, stride, out byte[] png));
            Assert.Equal(NativeStoredPng.ByteCount(width, height), png.Length);
            Assert.True(NativeStoredPng.TryEncodeBands(bgra, width, height, stride, 1, out byte[] banded));
            Assert.Equal(png, banded);
            Assert.Equal(new byte[] { 137, 80, 78, 71 }, png.Take(4).ToArray());
            using var stream = new MemoryStream(png);
            using var image = new Bitmap(stream);
            AssertPixel(image, 0, 0, 10, 20, 30);
            AssertPixel(image, 4, 0, 200, 10, 5);
            AssertPixel(image, 1, 1, 7, 8, 9);
            AssertPixel(image, 4, 1, 1, 2, 3);
        }

        private static void SetPixel(byte[] bgra, int stride, int x, int y, byte red, byte green, byte blue)
        {
            int offset = (y * stride) + (x * 4);
            bgra[offset] = blue;
            bgra[offset + 1] = green;
            bgra[offset + 2] = red;
            bgra[offset + 3] = 255;
        }

        private static void AssertPixel(Bitmap image, int x, int y, int red, int green, int blue)
        {
            Color pixel = image.GetPixel(x, y);
            Assert.Equal(red, pixel.R);
            Assert.Equal(green, pixel.G);
            Assert.Equal(blue, pixel.B);
        }

        private static Bitmap Solid32(Color color, int width, int height)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(color);
            }

            return bitmap;
        }
    }

    public class CoordinateTests
    {
        [Fact]
        public void MapsImageAndWindowRectCoordinates()
        {
            var window = new WindowFrameGeometry(10, 20, 216, 139, 8, 31, 200, 100);
            var full = new ScreenshotFrame(1, 0, 0, 216, 139);
            var frame = new ScreenshotFrame(0.5, 10, 20, 100, 80);
            Assert.True(CoordinateMapper.TryMap(0, 0, "image", full, window, out MappedPoint origin));
            Assert.True(CoordinateMapper.TryMap(8, 31, "image", full, window, out MappedPoint imageCorner));
            Assert.True(CoordinateMapper.TryMap(10, 10, "image", frame, window, out MappedPoint image));
            Assert.True(CoordinateMapper.TryMap(10.5, 1.2, "window_rect", frame, window, out MappedPoint windowRect));
            Assert.False(CoordinateMapper.TryMap(-4, 500, "window_rect", null, window, out MappedPoint outside));
            Assert.True(CoordinateMapper.TryMap(3, 4, "image", null, window, out MappedPoint noShot));
            Assert.False(CoordinateMapper.TryMap(100, 100, "image", new ScreenshotFrame(0.5, 200, 100, 100, 80), window, out MappedPoint scaledOutside));
            Assert.True(CoordinateMapper.TryMap(215, 138, "window_rect", null, window, out MappedPoint lastPixel));
            Assert.False(CoordinateMapper.TryMap(216, 0, "window_rect", null, window, out MappedPoint pastWidth));

            Assert.Equal(new MappedPoint(-8, -31, 0, 0), origin);
            Assert.Equal(new MappedPoint(0, 0, 8, 31), imageCorner);
            Assert.Equal(new MappedPoint(22, 9, 30, 40), image);
            Assert.Equal(new MappedPoint(3, -30, 11, 1), windowRect);
            Assert.Equal(-4, outside.WindowX);
            Assert.Equal(500, outside.WindowY);
            Assert.Equal(new MappedPoint(-5, -27, 3, 4), noShot);
            Assert.Equal(400, scaledOutside.WindowX);
            Assert.Equal(300, scaledOutside.WindowY);
            Assert.Equal(new MappedPoint(207, 107, 215, 138), lastPixel);
            Assert.Equal(216, pastWidth.WindowX);

            var store = new ScreenshotScaleStore();
            Assert.False(store.TryGet("Game", out _));
            store.Store("Game", frame);
            Assert.True(store.TryGet("Game", out ScreenshotFrame stored));
            Assert.Equal(frame, stored);
        }
    }

    public class InputServiceTests
    {
        [Fact]
        public void FocusesBeforeInputAndSelectsTheBackend()
        {
            var world = new FakeWorld();
            world.AddWindow(4, "Game", "Lobby");
            AppComposition composition = world.Build();
            ApplicationEntry mouse = new ApplicationEntry("Game", "", null, null, "Lobby", "Game", null, ClickBackend.MouseEvent);
            ApplicationEntry sent = mouse with { Name = "Sent", ClickBackend = ClickBackend.SendInput, ProcessName = "Sent" };
            world.AddWindow(8, "Sent", "Lobby");

            MouseActionResult moved = composition.Input.MouseMove(mouse, 20, 44, "window_rect");
            MouseActionResult clicked = composition.Input.MouseClick(sent, 9, 32, "right", true, 15, "window_rect");

            Assert.Null(moved.Error);
            Assert.Equal(20, moved.WindowX);
            Assert.Equal(44, moved.WindowY);
            Assert.Contains("focus", world.Order);
            Assert.True(world.Order.IndexOf("focus") < world.Order.FindIndex(item => item.StartsWith("move", StringComparison.Ordinal)));
            Assert.Contains("move 12,13", world.MouseEvents.Calls);
            Assert.Contains("click Right 15 True", world.SendInputClicks.Calls);
            Assert.DoesNotContain(world.MouseEvents.Calls, call => call.StartsWith("click", StringComparison.Ordinal));
            Assert.Equal("right", clicked.Button);
        }

        [Fact]
        public void ClampsHoldAndScrollAndRejectsAHigherIntegrityProcess()
        {
            var world = new FakeWorld();
            world.AddWindow(4, "Game", "Lobby");
            world.TargetIntegrity = 0x4000;
            world.CurrentIntegrity = 0x2000;
            AppComposition blocked = world.Build();
            ApplicationEntry application = new ApplicationEntry("Game", "C:\\game.exe", null, null, "Lobby", "Game", null, ClickBackend.MouseEvent);

            MouseActionResult denied = blocked.Input.MouseMove(application, 1, 1, "window_rect");
            Assert.Equal(ToolErrors.HigherIntegrity(), denied.Error);
            Assert.Empty(world.MouseEvents.Calls);

            world.TargetIntegrity = 0x2000;
            AppComposition composition = world.Build();
            MouseActionResult click = composition.Input.MouseClick(application, 1, 1, null, false, 5000, "window_rect");
            MouseActionResult scroll = composition.Input.MouseScroll(application, 1, 1, 40, "window_rect");
            KeyActionResult text = composition.Input.TypeText(application, "");
            KeyActionResult key = composition.Input.KeyPress(application, "nope", null);

            Assert.True(click.HoldWasClamped);
            Assert.Equal(2000, click.AppliedHoldMilliseconds);
            Assert.True(scroll.DeltaWasClamped);
            Assert.Equal(30, scroll.AppliedDelta);
            Assert.Equal(30, world.MouseEvents.LastScroll);
            Assert.Equal(ToolErrors.TextLength(0), text.Error);
            Assert.Equal(ToolErrors.UnknownKey("nope"), key.Error);
            Assert.Equal(ToolErrors.UnknownButton("tilt"), composition.Input.MouseClick(application, 1, 1, "tilt", false, null, "window_rect").Error);
            Assert.Equal(ToolErrors.UnknownCoordinateSpace("world"), composition.Input.MouseMove(application, 1, 1, "world").Error);
            Assert.Equal(ToolErrors.UnknownCoordinateSpace("client"), composition.Input.MouseMove(application, 1, 1, "client").Error);
        }

        [Fact]
        public void RejectsPointsOutsideTheWindow()
        {
            var world = new FakeWorld();
            world.AddWindow(4, "Game", "Lobby");
            AppComposition composition = world.Build();
            ApplicationEntry application = new ApplicationEntry("Game", "", null, null, "Lobby", "Game", null, ClickBackend.MouseEvent);
            WindowFrameGeometry geometry = world.FrameGeometry();

            MouseActionResult negative = composition.Input.MouseMove(application, -200, 0, "window_rect");
            MouseActionResult pastWidth = composition.Input.MouseClick(application, geometry.FrameWidth, 0, "left", false, null, "window_rect");
            MouseActionResult imageNegative = composition.Input.MouseMove(application, -200, 0, "image");

            composition.Scales.Store(application.Name, new ScreenshotFrame(0.5, 0, 0, geometry.FrameWidth, geometry.FrameHeight));
            MouseActionResult scaled = composition.Input.MouseMove(application, 500, 0, "image");
            MouseActionResult scaledInside = composition.Input.MouseMove(application, 10, 0, "image");

            Assert.Equal(ToolErrors.PointOutsideWindow(-200, 0, geometry.FrameWidth, geometry.FrameHeight), negative.Error);
            Assert.Equal(ToolErrors.PointOutsideWindow(geometry.FrameWidth, 0, geometry.FrameWidth, geometry.FrameHeight), pastWidth.Error);
            Assert.Equal(ToolErrors.PointOutsideWindow(-200, 0, geometry.FrameWidth, geometry.FrameHeight), imageNegative.Error);
            Assert.Equal(ToolErrors.PointOutsideWindow(1000, 0, geometry.FrameWidth, geometry.FrameHeight), scaled.Error);
            Assert.Null(scaledInside.Error);
            Assert.Equal(20, scaledInside.WindowX);
            Assert.Equal(0, scaledInside.WindowY);
            Assert.Equal(new[] { "move 12,-31" }, world.MouseEvents.Calls);
            Assert.Empty(world.SendInputClicks.Calls);
            Assert.Equal(1, world.Focus.ForegroundCalls);
        }

        [Fact]
        public async Task GateSerializesOneWindowAndOverlapsDifferentWindows()
        {
            var same = new FakeWorld();
            same.AddWindow(4, "Game", "Lobby");
            same.MouseEvents.ProbeOverlap = true;
            AppComposition sameComposition = same.Build();
            ApplicationEntry application = new ApplicationEntry("Game", "", null, null, "Lobby", "Game", null, ClickBackend.MouseEvent);
            await Task.WhenAll(
                Task.Run(() => sameComposition.Input.MouseMove(application, 1, 1, "window_rect")),
                Task.Run(() => sameComposition.Input.MouseMove(application, 2, 2, "window_rect")));
            Assert.Equal(1, same.MouseEvents.MaxInside);

            var split = new FakeWorld();
            split.AddWindow(4, "One", "A");
            split.AddWindow(5, "Two", "B");
            split.MouseEvents.ProbeOverlap = true;
            AppComposition splitComposition = split.Build();
            var first = new ApplicationEntry("One", "", null, null, "A", "One", null, ClickBackend.MouseEvent);
            var second = new ApplicationEntry("Two", "", null, null, "B", "Two", null, ClickBackend.MouseEvent);
            await Task.WhenAll(
                Task.Run(() => splitComposition.Input.MouseMove(first, 1, 1, "window_rect")),
                Task.Run(() => splitComposition.Input.MouseMove(second, 1, 1, "window_rect")));
            Assert.Equal(2, split.MouseEvents.MaxInside);
        }

        [Fact]
        public void StopReleasesButtonsThatAreStillDown()
        {
            var world = new FakeWorld();
            world.AddWindow(4, "Game", "Lobby");
            AppComposition composition = world.Build();
            ApplicationEntry application = new ApplicationEntry("Game", "", null, null, "Lobby", "Game", null, ClickBackend.SendInput);
            composition.Input.MouseDown(application, 3, 4, "left", "window_rect");
            composition.Input.ReleaseHeldButtons();

            Assert.Contains("up Left", world.SendInputClicks.Calls);
        }

        [Fact]
        public void LockedScreenRejectsMouseAndKeyboard()
        {
            var world = new FakeWorld();
            world.AddWindow(4, "Game", "Lobby");
            world.WorkstationLock.Locked = true;
            AppComposition composition = world.Build();
            ApplicationEntry application = new ApplicationEntry("Game", "", null, null, "Lobby", "Game", null, ClickBackend.MouseEvent);

            Assert.Equal(ToolErrors.ScreenLocked, composition.Input.MouseMove(application, 1, 1, "window_rect").Error);
            Assert.Equal(ToolErrors.ScreenLocked, composition.Input.MouseClick(application, 1, 1, "tilt", false, null, "window_rect").Error);
            Assert.Equal(ToolErrors.ScreenLocked, composition.Input.MouseDown(application, 1, 1, "left", "window_rect").Error);
            Assert.Equal(ToolErrors.ScreenLocked, composition.Input.MouseUp(application, 1, 1, "left", "window_rect").Error);
            Assert.Equal(ToolErrors.ScreenLocked, composition.Input.MouseScroll(application, 1, 1, 1, "window_rect").Error);
            Assert.Equal(ToolErrors.ScreenLocked, composition.Input.KeyPress(application, "nope", null).Error);
            Assert.Equal(ToolErrors.ScreenLocked, composition.Input.KeyDown(application, "A").Error);
            Assert.Equal(ToolErrors.ScreenLocked, composition.Input.KeyUp(application, "A").Error);
            Assert.Equal(ToolErrors.ScreenLocked, composition.Input.TypeText(application, "").Error);
            Assert.Empty(world.MouseEvents.Calls);
            Assert.Empty(world.SendInputClicks.Calls);
            Assert.Empty(world.Keyboard.Calls);
            Assert.Equal(0, world.Focus.ForegroundCalls);

            FocusResult focus = composition.Input.Focus(application);
            Assert.Null(focus.Error);
            Assert.True(world.Focus.ForegroundCalls > 0);
        }

        [Fact]
        public void LockReleasesHeldButtonsAndThenRejectsInput()
        {
            var world = new FakeWorld();
            world.AddWindow(4, "Game", "Lobby");
            AppComposition composition = world.Build();
            ApplicationEntry application = new ApplicationEntry("Game", "", null, null, "Lobby", "Game", null, ClickBackend.SendInput);
            var tools = new SessionTools(composition.Input, world.WorkstationLock);

            CallToolResult before = tools.IsLocked();
            Assert.NotEqual(true, before.IsError);
            Assert.Contains("\"locked\":false", ToolText(before));

            composition.Input.MouseDown(application, 3, 4, "left", "window_rect");
            CallToolResult locked = tools.Lock();
            Assert.NotEqual(true, locked.IsError);
            Assert.Contains("\"locked\":true", ToolText(locked));
            Assert.Contains("up Left", world.SendInputClicks.Calls);
            Assert.Equal(1, world.WorkstationLock.LockCalls);
            Assert.True(world.WorkstationLock.Locked);

            CallToolResult afterLock = tools.IsLocked();
            Assert.Contains("\"locked\":true", ToolText(afterLock));

            MouseActionResult after = composition.Input.MouseMove(application, 1, 1, "window_rect");
            Assert.Equal(ToolErrors.ScreenLocked, after.Error);
            Assert.DoesNotContain(world.SendInputClicks.Calls, call => call.StartsWith("move", StringComparison.Ordinal));
        }

        [Fact]
        public void ShutdownDoesNotReleaseButtonsWhileLocked()
        {
            var world = new FakeWorld();
            world.AddWindow(4, "Game", "Lobby");
            AppComposition composition = world.Build();
            ApplicationEntry application = new ApplicationEntry("Game", "", null, null, "Lobby", "Game", null, ClickBackend.SendInput);
            composition.Input.MouseDown(application, 3, 4, "left", "window_rect");
            world.WorkstationLock.Locked = true;

            composition.Input.ReleaseHeldButtons();

            Assert.Contains("down Left", world.SendInputClicks.Calls);
            Assert.DoesNotContain("up Left", world.SendInputClicks.Calls);
        }

        private static string ToolText(CallToolResult result)
        {
            return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        }
    }

    public class WorkstationLockTests
    {
        [Theory]
        [InlineData(true, 0, true, "Default", true)]
        [InlineData(true, 1, true, "Default", false)]
        [InlineData(false, 0, true, "Default", false)]
        [InlineData(true, -1, true, "Default", false)]
        [InlineData(true, 1, false, null, true)]
        [InlineData(true, 1, true, "Winlogon", true)]
        [InlineData(true, 1, true, null, true)]
        [InlineData(true, 0, false, null, true)]
        public void DecidesLockFromSessionFlagsAndInputDesktop(
            bool hasSessionFlags,
            int sessionFlags,
            bool inputDesktopOpened,
            string? desktopName,
            bool locked)
        {
            int? flags = hasSessionFlags ? sessionFlags : null;
            Assert.Equal(locked, Win32WorkstationLock.IsLockedState(flags, inputDesktopOpened, desktopName));
        }
    }

    public class KeyAndFocusTests
    {
        [Theory]
        [InlineData("A")]
        [InlineData("z")]
        [InlineData("0")]
        [InlineData("9")]
        [InlineData("F1")]
        [InlineData("f24")]
        [InlineData("Enter")]
        [InlineData("return")]
        [InlineData("Escape")]
        [InlineData("Esc")]
        [InlineData("Tab")]
        [InlineData("Space")]
        [InlineData("Backspace")]
        [InlineData("Delete")]
        [InlineData("Del")]
        [InlineData("Insert")]
        [InlineData("Home")]
        [InlineData("End")]
        [InlineData("PageUp")]
        [InlineData("PageDown")]
        [InlineData("Left")]
        [InlineData("Right")]
        [InlineData("Up")]
        [InlineData("Down")]
        [InlineData("Shift")]
        [InlineData("Ctrl")]
        [InlineData("Control")]
        [InlineData("Alt")]
        [InlineData("Win")]
        public void KnownKeyNamesResolve(string name)
        {
            Assert.True(KeyNameMap.TryGetVirtualKey(name, out byte virtualKey));
            Assert.NotEqual(0, virtualKey);
        }

        [Fact]
        public void UnknownKeyNamesDoNotResolve()
        {
            Assert.False(KeyNameMap.TryGetVirtualKey("Nope", out _));
            Assert.False(KeyNameMap.TryGetVirtualKey("  ", out _));
        }

        [Fact]
        public void FocusFallsBackToAttachThreadInput()
        {
            var focus = new RecordingFocus { FirstSucceeds = false };
            var helper = new FocusHelper(focus);

            Assert.True(helper.TryFocus(new IntPtr(4)));
            Assert.Equal(new[] { true, false }, focus.AttachFlags);
            Assert.Equal(2, focus.ForegroundCalls);
            Assert.True(focus.Raised >= 2);
        }
    }

    public class ToolShapeTests
    {
        [Fact]
        public void ErrorMessagesMatchTheContract()
        {
            Assert.Equal("Unknown application 'Nope'. Configured names: a, b.", ToolErrors.UnknownName("Nope", new[] { "a", "b" }));
            Assert.Equal("Unknown application 'Nope'. Configured names: (none).", ToolErrors.UnknownName("Nope", Array.Empty<string>()));
            Assert.Equal("Application 'A' is not launchable. It has no executable in the configuration.", ToolErrors.NotLaunchable("A"));
            Assert.Equal("The window for 'A' was not found. Call launch_application, then retry.", ToolErrors.WindowNotAvailable("A", true));
            Assert.Equal("The window for 'A' was not found. Call find_window for the current state.", ToolErrors.WindowNotAvailable("A", false));
            Assert.Equal("The region does not intersect the window (8x9).", ToolErrors.RegionMiss(8, 9));
            Assert.Contains("4x4", ToolErrors.CaptureSizeMismatch(4, 4, 216, 139));
            Assert.Equal("Screenshot encode failed: boom", ToolErrors.CaptureFailed("boom"));
            Assert.Equal("text must be between 1 and 500 characters. The value has length 0.", ToolErrors.TextLength(0));
            Assert.Contains("higher integrity level", ToolErrors.HigherIntegrity());
            Assert.Contains("no unlock tool", ToolErrors.ScreenLocked);
            Assert.Equal("Failed to lock the workstation: denied", ToolErrors.LockFailed("denied"));
            Assert.Equal("No configuration is loaded.", ToolErrors.NoConfiguration);
            Assert.Equal("The tool failed (InvalidOperationException): boom", ToolErrors.Unexpected(new InvalidOperationException("boom")));
            Assert.Equal("coordinateSpace 'client' was not recognized. Use image or window_rect.", ToolErrors.UnknownCoordinateSpace("client"));
            Assert.Equal("The point (-200, 0) is outside the window (216x139).", ToolErrors.PointOutsideWindow(-200, 0, 216, 139));
        }

        [Fact]
        public void WindowJsonOmitsTheHandleWhenNothingMatched()
        {
            string missing = WindowTools.Format(new WindowLookup
            {
                Found = false,
                ProcessRunning = true,
                ProcessId = 9,
                Message = "waiting"
            });
            string found = WindowTools.Format(new WindowLookup
            {
                Found = true,
                Hwnd = new IntPtr(0x10),
                ProcessId = 4,
                ProcessName = "Game",
                Title = "Lobby",
                MatchCount = 1,
                ClientWidth = 200,
                ClientHeight = 100,
                ScreenX = 10,
                ScreenY = 20,
                ScreenWidth = 220,
                ScreenHeight = 140,
                State = "normal"
            });

            Assert.Contains("\"found\":false", missing);
            Assert.Contains("\"processId\":9", missing);
            Assert.DoesNotContain("hwnd", missing);
            Assert.Contains("\"hwnd\":\"0x00000010\"", found);
            Assert.DoesNotContain("client", found);
            Assert.Contains("\"window\":{\"width\":220,\"height\":140}", found);
            Assert.Contains("\"state\":\"normal\"", found);
        }

        [Fact]
        public void ListAndLaunchShapes()
        {
            var world = new FakeWorld();
            world.AddWindow(4, "Game", "Lobby");
            AppComposition composition = world.Build();
            composition.Store.Apply(
                new ConfigDocument(
                    1,
                    ServerSettings.CreateDefault(),
                    new[]
                    {
                        new ApplicationEntry("Game", "C:\\game.exe", "-x", "C:\\work", "Lobby", "Game", "Hello", ClickBackend.MouseEvent)
                    },
                    "config.json"),
                false);

            var applications = new ApplicationTools(composition.Store, composition.Locator, composition.Launcher);
            CallToolResult list = applications.ListApplications();
            string listText = ((TextContentBlock)list.Content[0]).Text;
            Assert.Contains("\"name\":\"Game\"", listText);
            Assert.Contains("\"running\":true", listText);
            Assert.Contains("\"hasDescription\":true", listText);

            CallToolResult launched = applications.LaunchApplication("Game", 0);
            string launchText = ((TextContentBlock)launched.Content[0]).Text;
            Assert.Contains("\"started\":true", launchText);
            Assert.Contains("\"windowFound\":false", launchText);
            Assert.NotEqual(true, launched.IsError);

            CallToolResult unknown = applications.GetApplicationDescription("Missing");
            Assert.True(unknown.IsError);
            Assert.Contains("Configured names: Game", ((TextContentBlock)unknown.Content[0]).Text);
        }
    }
}

using game_automation;

namespace automation_util.tests
{
    public class ClickGestureTests
    {
        [Fact]
        public void ClickPressesThenReleasesAfterTheHold()
        {
            var helper = new RecordingClickHelper();

            helper.Click(IntPtr.Zero, 12, 34, MouseButton.Right, 25, false);

            Assert.Equal(
                new[]
                {
                    "move 12,34",
                    "down Right 12,34",
                    "up Right 12,34"
                },
                helper.Events);
            Assert.Equal(new[] { 25 }, helper.Delays);
        }

        [Fact]
        public void DoubleClickWaitsTheSystemIntervalThenRepeats()
        {
            var helper = new RecordingClickHelper();

            helper.Click(IntPtr.Zero, 1, 2, MouseButton.Left, 10, true);

            Assert.Equal(
                new[]
                {
                    "move 1,2",
                    "down Left 1,2",
                    "up Left 1,2",
                    "move 1,2",
                    "down Left 1,2",
                    "up Left 1,2"
                },
                helper.Events);
            Assert.Equal(new[] { 10, 17, 10 }, helper.Delays);
        }

        [Fact]
        public void ScrollMovesThenWheels()
        {
            var helper = new RecordingClickHelper();

            helper.Scroll(IntPtr.Zero, 8, 9, -3);

            Assert.Equal(new[] { "move 8,9", "scroll -3 8,9" }, helper.Events);
        }

        private sealed class RecordingClickHelper : ClickHelperBase
        {
            public List<string> Events { get; } = new List<string>();

            public List<int> Delays { get; } = new List<int>();

            protected override void MoveAtScreen(int screenX, int screenY)
            {
                Events.Add("move " + screenX + "," + screenY);
            }

            protected override void ButtonDownAtScreen(int screenX, int screenY, MouseButton button)
            {
                Events.Add("down " + button + " " + screenX + "," + screenY);
            }

            protected override void ButtonUpAtScreen(int screenX, int screenY, MouseButton button)
            {
                Events.Add("up " + button + " " + screenX + "," + screenY);
            }

            protected override void ScrollAtScreen(int screenX, int screenY, int notches)
            {
                Events.Add("scroll " + notches + " " + screenX + "," + screenY);
            }

            protected override void Delay(int milliseconds)
            {
                Delays.Add(milliseconds);
            }

            protected override int GetDoubleClickDelay()
            {
                return 17;
            }
        }
    }

    public class WindowSelectorTests
    {
        [Fact]
        public void FiltersByProcessAndTitleTogether()
        {
            WindowCandidate[] windows = new[]
            {
                Candidate(1, "Game", "Lobby", true, false, false, 0),
                Candidate(2, "game", "Other", true, false, false, 1),
                Candidate(3, "Launcher", "Game Lobby", true, false, false, 2),
                Candidate(4, "Game", "", true, false, false, 3)
            };

            IReadOnlyList<WindowCandidate> processOnly = WindowCandidateSelector.Filter(windows, "GAME", null);
            IReadOnlyList<WindowCandidate> titleOnly = WindowCandidateSelector.Filter(windows, null, "lobby");
            IReadOnlyList<WindowCandidate> both = WindowCandidateSelector.Filter(windows, "Game", "Lobby");

            Assert.Equal(new long[] { 1, 2, 4 }, processOnly.Select(item => item.Hwnd.ToInt64()));
            Assert.Equal(new long[] { 1, 3 }, titleOnly.Select(item => item.Hwnd.ToInt64()));
            Assert.Equal(new long[] { 1 }, both.Select(item => item.Hwnd.ToInt64()));
        }

        [Fact]
        public void ChoosesVisibleThenForegroundThenLowestZOrder()
        {
            WindowCandidate minimized = Candidate(1, "Game", "A", true, true, true, 0);
            WindowCandidate visible = Candidate(2, "Game", "B", true, false, false, 5);
            WindowCandidate? chosen = WindowCandidateSelector.Choose(new[] { minimized, visible });
            Assert.Equal(2, chosen!.Hwnd.ToInt64());

            WindowCandidate back = Candidate(3, "Game", "C", true, false, false, 1);
            WindowCandidate front = Candidate(4, "Game", "D", true, false, true, 9);
            chosen = WindowCandidateSelector.Choose(new[] { back, front });
            Assert.Equal(4, chosen!.Hwnd.ToInt64());

            WindowCandidate lower = Candidate(5, "Game", "E", true, false, false, 2);
            WindowCandidate higher = Candidate(6, "Game", "F", true, false, false, 8);
            chosen = WindowCandidateSelector.Choose(new[] { higher, lower });
            Assert.Equal(5, chosen!.Hwnd.ToInt64());

            Assert.Null(WindowCandidateSelector.Choose(Array.Empty<WindowCandidate>()));
        }

        private static WindowCandidate Candidate(
            int hwnd,
            string processName,
            string title,
            bool visible,
            bool minimized,
            bool foreground,
            int zOrder)
        {
            return new WindowCandidate
            {
                Hwnd = new IntPtr(hwnd),
                ProcessName = processName,
                Title = title,
                IsVisible = visible,
                IsMinimized = minimized,
                IsForeground = foreground,
                ZOrder = zOrder
            };
        }
    }
}

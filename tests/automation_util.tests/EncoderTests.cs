using game_automation;

namespace automation_util.tests
{
    public class MouseEncoderTests
    {
        [Fact]
        public void ButtonFlagsMatchWin32()
        {
            Assert.Equal(MouseInputEncoder.MOUSEEVENTF_LEFTDOWN, MouseInputEncoder.ButtonFlag(MouseButton.Left, true));
            Assert.Equal(MouseInputEncoder.MOUSEEVENTF_LEFTUP, MouseInputEncoder.ButtonFlag(MouseButton.Left, false));
            Assert.Equal(MouseInputEncoder.MOUSEEVENTF_RIGHTDOWN, MouseInputEncoder.ButtonFlag(MouseButton.Right, true));
            Assert.Equal(MouseInputEncoder.MOUSEEVENTF_RIGHTUP, MouseInputEncoder.ButtonFlag(MouseButton.Right, false));
            Assert.Equal(MouseInputEncoder.MOUSEEVENTF_MIDDLEDOWN, MouseInputEncoder.ButtonFlag(MouseButton.Middle, true));
            Assert.Equal(MouseInputEncoder.MOUSEEVENTF_MIDDLEUP, MouseInputEncoder.ButtonFlag(MouseButton.Middle, false));
        }

        [Fact]
        public void WheelDataIsNotchesTimes120()
        {
            MouseInputDescription up = MouseInputEncoder.Wheel(2);
            MouseInputDescription down = MouseInputEncoder.Wheel(-1);

            Assert.Equal(MouseInputEncoder.MOUSEEVENTF_WHEEL, up.Flags);
            Assert.Equal(240u, up.MouseData);
            Assert.Equal(unchecked((uint)(-120)), down.MouseData);
        }

        [Fact]
        public void AbsoluteMoveNormalizesAcrossTheVirtualScreen()
        {
            var screen = new VirtualScreen(100, 50, 101, 51);
            (int originX, int originY) = MouseInputEncoder.Normalize(100, 50, screen);
            (int farX, int farY) = MouseInputEncoder.Normalize(200, 100, screen);
            (int clampedX, int clampedY) = MouseInputEncoder.Normalize(-50, 500, screen);

            Assert.Equal((0, 0), (originX, originY));
            Assert.Equal((MouseInputEncoder.ABSOLUTE_RANGE, MouseInputEncoder.ABSOLUTE_RANGE), (farX, farY));
            Assert.Equal((0, MouseInputEncoder.ABSOLUTE_RANGE), (clampedX, clampedY));

            MouseInputDescription move = MouseInputEncoder.MoveAbsolute(200, 100, screen);
            Assert.Equal(
                MouseInputEncoder.MOUSEEVENTF_MOVE | MouseInputEncoder.MOUSEEVENTF_ABSOLUTE | MouseInputEncoder.MOUSEEVENTF_VIRTUALDESK,
                move.Flags);
            Assert.Equal(MouseInputEncoder.ABSOLUTE_RANGE, move.Dx);
        }

        [Fact]
        public void DegenerateScreenNormalizesToZero()
        {
            Assert.Equal((0, 0), MouseInputEncoder.Normalize(10, 10, new VirtualScreen(0, 0, 1, 100)));
            Assert.Equal((0, 0), MouseInputEncoder.Normalize(10, 10, new VirtualScreen(0, 0, 100, 0)));
        }
    }

    public class KeyboardEncoderTests
    {
        [Fact]
        public void PressAndReleaseEmitsKeyUpInReverseOrder()
        {
            IReadOnlyList<KeyboardInputDescription> events = KeyboardInputEncoder.BuildPressAndRelease(new byte[] { 0x11, 0x41 });

            Assert.Equal(4, events.Count);
            Assert.Equal((ushort)0x11, events[0].VirtualKey);
            Assert.Equal(0u, events[0].Flags);
            Assert.Equal((ushort)0x41, events[1].VirtualKey);
            Assert.Equal(0u, events[1].Flags);
            Assert.Equal((ushort)0x41, events[2].VirtualKey);
            Assert.Equal(KeyboardInputEncoder.KEYEVENTF_KEYUP, events[2].Flags);
            Assert.Equal((ushort)0x11, events[3].VirtualKey);
            Assert.Equal(KeyboardInputEncoder.KEYEVENTF_KEYUP, events[3].Flags);
        }

        [Fact]
        public void NewlineBecomesReturnAndOtherCharactersStayUnicode()
        {
            IReadOnlyList<KeyboardInputDescription> events = KeyboardInputEncoder.TypeText("a\nb\r");

            Assert.Equal(8, events.Count);
            Assert.Equal(KeyboardInputEncoder.KEYEVENTF_UNICODE, events[0].Flags);
            Assert.Equal((ushort)'a', events[0].Scan);
            Assert.Equal(KeyboardInputEncoder.KEYEVENTF_UNICODE | KeyboardInputEncoder.KEYEVENTF_KEYUP, events[1].Flags);

            Assert.Equal(KeyboardInputEncoder.VK_RETURN, events[2].VirtualKey);
            Assert.Equal(0u, events[2].Flags);
            Assert.Equal(KeyboardInputEncoder.KEYEVENTF_KEYUP, events[3].Flags);

            Assert.Equal((ushort)'b', events[4].Scan);
            Assert.Equal(KeyboardInputEncoder.KEYEVENTF_UNICODE, events[4].Flags);
            Assert.Equal((ushort)'\r', events[6].Scan);
            Assert.Equal(KeyboardInputEncoder.KEYEVENTF_UNICODE, events[6].Flags);
        }
    }

    public class NativeStructSizeTests
    {
        [Fact]
        public void InputStructSizeMatchesTheProcess()
        {
            int expected = Environment.Is64BitProcess ? 40 : 28;
            Assert.Equal(expected, NativeInput.StructSize);
        }
    }
}

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace game_automation
{
    /// <summary>
    /// Sends <see cref="MouseInputDescription"/> and <see cref="KeyboardInputDescription"/> values through <c>SendInput</c>.
    /// </summary>
    public static class NativeInput
    {
        /// <summary>Mouse input type.</summary>
        public const uint INPUT_MOUSE = 0;

        /// <summary>Keyboard input type.</summary>
        public const uint INPUT_KEYBOARD = 1;

        /// <summary>
        /// Size of the native <c>INPUT</c> structure for this process.
        /// </summary>
        public static int StructSize
        {
            get { return Marshal.SizeOf<INPUT>(); }
        }

        /// <summary>
        /// Sends mouse events in one <c>SendInput</c> call.
        /// </summary>
        /// <param name="events">Events in order.</param>
        public static void SendMouse(params MouseInputDescription[] events)
        {
            if (events == null || events.Length == 0)
            {
                return;
            }

            INPUT[] inputs = new INPUT[events.Length];
            for (int index = 0; index < events.Length; index++)
            {
                MouseInputDescription description = events[index];
                inputs[index].Type = INPUT_MOUSE;
                inputs[index].Union.Mouse.DeltaX = description.Dx;
                inputs[index].Union.Mouse.DeltaY = description.Dy;
                inputs[index].Union.Mouse.MouseData = description.MouseData;
                inputs[index].Union.Mouse.Flags = description.Flags;
                inputs[index].Union.Mouse.Time = 0;
                inputs[index].Union.Mouse.ExtraInfo = IntPtr.Zero;
            }

            Send(inputs);
        }

        /// <summary>
        /// Sends one keyboard event.
        /// </summary>
        /// <param name="description">Key or Unicode event.</param>
        public static void SendKeyboard(KeyboardInputDescription description)
        {
            INPUT input = new INPUT();
            input.Type = INPUT_KEYBOARD;
            input.Union.Keyboard.VirtualKey = description.VirtualKey;
            input.Union.Keyboard.Scan = description.Scan;
            input.Union.Keyboard.Flags = description.Flags;
            input.Union.Keyboard.Time = 0;
            input.Union.Keyboard.ExtraInfo = IntPtr.Zero;
            Send(new[] { input });
        }

        private static void Send(INPUT[] inputs)
        {
            uint sent = SendInput((uint)inputs.Length, inputs, StructSize);
            if (sent != inputs.Length)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint eventCount, INPUT[] inputs, int inputSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint Type;
            public INPUTUNION Union;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)]
            public MOUSEINPUT Mouse;

            [FieldOffset(0)]
            public KEYBDINPUT Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int DeltaX;
            public int DeltaY;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort VirtualKey;
            public ushort Scan;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }
    }

    /// <summary>
    /// One keyboard event before it is packed into a native <c>INPUT</c> structure.
    /// </summary>
    /// <param name="VirtualKey">Virtual-key code. Zero for Unicode events.</param>
    /// <param name="Scan">Scan code, or the UTF-16 code unit for Unicode events.</param>
    /// <param name="Flags">Win32 <c>KEYEVENTF_*</c> flags.</param>
    public readonly record struct KeyboardInputDescription(ushort VirtualKey, ushort Scan, uint Flags);
}

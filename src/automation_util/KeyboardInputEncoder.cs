using System;
using System.Collections.Generic;

namespace game_automation
{
    /// <summary>
    /// Pure mapping from key gestures and text to keyboard events.
    /// </summary>
    public static class KeyboardInputEncoder
    {
        /// <summary>Release flag. A key-up event must include it.</summary>
        public const uint KEYEVENTF_KEYUP = 0x0002;

        /// <summary>Unicode character flag. The scan field carries the UTF-16 code unit.</summary>
        public const uint KEYEVENTF_UNICODE = 0x0004;

        /// <summary>Virtual key for Enter / Return.</summary>
        public const ushort VK_RETURN = 0x0D;

        /// <summary>
        /// Builds the down events in order, then the up events in reverse, with <see cref="KEYEVENTF_KEYUP"/> on each release.
        /// </summary>
        /// <param name="keys">Virtual keys. Null is rejected.</param>
        /// <returns>The press-and-release sequence.</returns>
        public static IReadOnlyList<KeyboardInputDescription> BuildPressAndRelease(byte[] keys)
        {
            if (keys == null)
            {
                throw new ArgumentNullException(nameof(keys));
            }

            var events = new List<KeyboardInputDescription>(keys.Length * 2);
            foreach (byte key in keys)
            {
                events.Add(KeyDown(key));
            }

            for (int index = keys.Length - 1; index >= 0; index--)
            {
                events.Add(KeyUp(keys[index]));
            }

            return events;
        }

        /// <summary>
        /// Builds a key-down event.
        /// </summary>
        /// <param name="virtualKey">Virtual-key code.</param>
        /// <returns>A key-down description.</returns>
        public static KeyboardInputDescription KeyDown(byte virtualKey)
        {
            return new KeyboardInputDescription(virtualKey, 0, 0);
        }

        /// <summary>
        /// Builds a key-up event.
        /// </summary>
        /// <param name="virtualKey">Virtual-key code.</param>
        /// <returns>A key-up description.</returns>
        public static KeyboardInputDescription KeyUp(byte virtualKey)
        {
            return new KeyboardInputDescription(virtualKey, 0, KEYEVENTF_KEYUP);
        }

        /// <summary>
        /// Builds Unicode key events for text. A newline is a Return press. Other code units use <c>KEYEVENTF_UNICODE</c>.
        /// </summary>
        /// <param name="text">Text to type. Null is rejected.</param>
        /// <returns>Down and up events for each character.</returns>
        public static IReadOnlyList<KeyboardInputDescription> TypeText(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            var events = new List<KeyboardInputDescription>(text.Length * 2);
            foreach (char character in text)
            {
                if (character == '\n')
                {
                    events.Add(KeyDown((byte)VK_RETURN));
                    events.Add(KeyUp((byte)VK_RETURN));
                    continue;
                }

                events.Add(new KeyboardInputDescription(0, character, KEYEVENTF_UNICODE));
                events.Add(new KeyboardInputDescription(0, character, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
            }

            return events;
        }
    }
}

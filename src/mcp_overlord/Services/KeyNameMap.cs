using game_automation_in_csharp.util;

namespace mcp_overlord.Services
{
    /// <summary>
    /// Maps key names to virtual-key codes. Matching is case-insensitive. Unknown names are not guessed.
    /// </summary>
    public static class KeyNameMap
    {
        private static readonly Dictionary<string, byte> Names = Build();

        /// <summary>
        /// Resolves a key name.
        /// </summary>
        /// <param name="name">Name such as <c>Enter</c>, <c>F5</c>, or <c>A</c>.</param>
        /// <param name="virtualKey">The virtual-key code when the name is known.</param>
        /// <returns>True when the name is in the table.</returns>
        public static bool TryGetVirtualKey(string? name, out byte virtualKey)
        {
            virtualKey = 0;
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            return Names.TryGetValue(name.Trim(), out virtualKey);
        }

        private static Dictionary<string, byte> Build()
        {
            var names = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            for (int letter = 0; letter < 26; letter++)
            {
                names[((char)('A' + letter)).ToString()] = (byte)(KeyCode.VK_A + letter);
            }

            for (int digit = 0; digit <= 9; digit++)
            {
                names[digit.ToString()] = (byte)(KeyCode.VK_0 + digit);
            }

            for (int function = 1; function <= 24; function++)
            {
                names["F" + function] = (byte)(KeyCode.VK_F1 + function - 1);
            }

            names["Enter"] = (byte)KeyCode.VK_RETURN;
            names["Return"] = (byte)KeyCode.VK_RETURN;
            names["Escape"] = (byte)KeyCode.VK_ESCAPE;
            names["Esc"] = (byte)KeyCode.VK_ESCAPE;
            names["Tab"] = (byte)KeyCode.VK_TAB;
            names["Space"] = (byte)KeyCode.VK_SPACE;
            names["Backspace"] = (byte)KeyCode.VK_BACK;
            names["Delete"] = (byte)KeyCode.VK_DELETE;
            names["Del"] = (byte)KeyCode.VK_DELETE;
            names["Insert"] = (byte)KeyCode.VK_INSERT;
            names["Home"] = (byte)KeyCode.VK_HOME;
            names["End"] = (byte)KeyCode.VK_END;
            names["PageUp"] = (byte)KeyCode.VK_PRIOR;
            names["PageDown"] = (byte)KeyCode.VK_NEXT;
            names["Left"] = (byte)KeyCode.VK_LEFT;
            names["Right"] = (byte)KeyCode.VK_RIGHT;
            names["Up"] = (byte)KeyCode.VK_UP;
            names["Down"] = (byte)KeyCode.VK_DOWN;
            names["Shift"] = (byte)KeyCode.VK_SHIFT;
            names["Ctrl"] = (byte)KeyCode.VK_CONTROL;
            names["Control"] = (byte)KeyCode.VK_CONTROL;
            names["Alt"] = (byte)KeyCode.VK_MENU;
            names["Win"] = (byte)KeyCode.VK_LWIN;
            return names;
        }
    }
}

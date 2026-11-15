using System.ComponentModel;
using mcp_overlord.Configuration;
using mcp_overlord.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace mcp_overlord.Tools
{
    /// <summary>Mouse and keyboard tools. Coordinates default to the last screenshot.</summary>
    [McpServerToolType]
    public sealed class InputTools
    {
        private const string CoordinateSpaceDescription =
            "image (default): pixels of the last screenshot, including its scale and crop. window_rect: physical pixels of the window rectangle, with no scale. A point outside the window is rejected and nothing is sent.";

        private readonly ConfigStore _store;
        private readonly InputService _input;

        /// <summary>
        /// Creates the tools.
        /// </summary>
        public InputTools(ConfigStore store, InputService input)
        {
            _store = store;
            _input = input;
        }

        /// <summary>Moves the cursor. No button change.</summary>
        [McpServerTool(Name = "mouse_move", OpenWorld = false),
         Description("Move the cursor. No button change. image coordinates are pixels of the last screenshot. window_rect coordinates are physical pixels of the window. A point outside the window is rejected.")]
        public CallToolResult MouseMove(
            [Description("Configured application name.")] string name,
            [Description("X in the selected coordinate space.")] double x,
            [Description("Y in the selected coordinate space.")] double y,
            [Description(CoordinateSpaceDescription)] string? coordinateSpace = null)
        {
            return Mouse(name, application => _input.MouseMove(application, x, y, coordinateSpace));
        }

        /// <summary>Clicks a button. double repeats the click once.</summary>
        [McpServerTool(Name = "mouse_click", OpenWorld = false),
         Description("Move, press, and release a mouse button. image coordinates are pixels of the last screenshot. window_rect coordinates are physical pixels of the window. A point outside the window is rejected. A point on the outer border can resize the window. On a maximized window, (0, 0) can be off screen.")]
        public CallToolResult MouseClick(
            [Description("Configured application name.")] string name,
            [Description("X in the selected coordinate space.")] double x,
            [Description("Y in the selected coordinate space.")] double y,
            [Description("left (default), right, or middle.")] string? button = null,
            [Description("When true, click a second time after the system double-click delay.")] bool @double = false,
            [Description("Milliseconds between press and release. Default 50, clamped to 0-2000.")] int? holdMs = null,
            [Description(CoordinateSpaceDescription)] string? coordinateSpace = null)
        {
            return Mouse(name, application => _input.MouseClick(application, x, y, button, @double, holdMs, coordinateSpace));
        }

        /// <summary>Presses a button and leaves it down.</summary>
        [McpServerTool(Name = "mouse_down", OpenWorld = false),
         Description("Move and press a mouse button. It stays down until mouse_up or a later click. A point outside the window is rejected.")]
        public CallToolResult MouseDown(
            [Description("Configured application name.")] string name,
            [Description("X in the selected coordinate space.")] double x,
            [Description("Y in the selected coordinate space.")] double y,
            [Description("left (default), right, or middle.")] string? button = null,
            [Description(CoordinateSpaceDescription)] string? coordinateSpace = null)
        {
            return Mouse(name, application => _input.MouseDown(application, x, y, button, coordinateSpace));
        }

        /// <summary>Releases a button.</summary>
        [McpServerTool(Name = "mouse_up", OpenWorld = false),
         Description("Move and release a mouse button. A point outside the window is rejected.")]
        public CallToolResult MouseUp(
            [Description("Configured application name.")] string name,
            [Description("X in the selected coordinate space.")] double x,
            [Description("Y in the selected coordinate space.")] double y,
            [Description("left (default), right, or middle.")] string? button = null,
            [Description(CoordinateSpaceDescription)] string? coordinateSpace = null)
        {
            return Mouse(name, application => _input.MouseUp(application, x, y, button, coordinateSpace));
        }

        /// <summary>Scrolls the wheel. Positive delta scrolls up.</summary>
        [McpServerTool(Name = "mouse_scroll", OpenWorld = false),
         Description("Move and scroll the wheel. Positive delta scrolls up, away from the user. Clamped to -30..30. A point outside the window is rejected.")]
        public CallToolResult MouseScroll(
            [Description("Configured application name.")] string name,
            [Description("X in the selected coordinate space.")] double x,
            [Description("Y in the selected coordinate space.")] double y,
            [Description("Wheel notches. Positive scrolls up.")] int delta,
            [Description(CoordinateSpaceDescription)] string? coordinateSpace = null)
        {
            return Mouse(name, application => _input.MouseScroll(application, x, y, delta, coordinateSpace));
        }

        /// <summary>Presses and releases a key.</summary>
        [McpServerTool(Name = "key_press", OpenWorld = false),
         Description("Focus the window, press a key, and release it.")]
        public CallToolResult KeyPress(
            [Description("Configured application name.")] string name,
            [Description("Key name such as A, Enter, Escape, F5, Ctrl, or Left.")] string key,
            [Description("Milliseconds between down and up. Default 50, clamped to 0-2000.")] int? holdMs = null)
        {
            return Key(name, application => _input.KeyPress(application, key, holdMs));
        }

        /// <summary>Sends a key down so the caller can build a chord.</summary>
        [McpServerTool(Name = "key_down", OpenWorld = false),
         Description("Focus the window and send a key down. Pair it with key_up.")]
        public CallToolResult KeyDown(
            [Description("Configured application name.")] string name,
            [Description("Key name.")] string key)
        {
            return Key(name, application => _input.KeyDown(application, key));
        }

        /// <summary>Sends a key up.</summary>
        [McpServerTool(Name = "key_up", OpenWorld = false),
         Description("Focus the window and send a key up.")]
        public CallToolResult KeyUp(
            [Description("Configured application name.")] string name,
            [Description("Key name.")] string key)
        {
            return Key(name, application => _input.KeyUp(application, key));
        }

        /// <summary>Types text. A newline is Return. Length is 1–500.</summary>
        [McpServerTool(Name = "type_text", OpenWorld = false),
         Description("Focus the window and type text. Newlines are sent as Return. Length 1 to 500.")]
        public CallToolResult TypeText(
            [Description("Configured application name.")] string name,
            [Description("Text to type. Length 1 to 500.")] string text)
        {
            return Key(name, application => _input.TypeText(application, text));
        }

        private CallToolResult Mouse(string name, Func<ApplicationEntry, MouseActionResult> action)
        {
            if (!ApplicationLookup.TryGet(_store, name, out ApplicationEntry? application, out CallToolResult? error))
            {
                return error!;
            }

            MouseActionResult result = action(application!);
            if (result.Error != null)
            {
                return ToolResponses.Error(result.Error);
            }

            string? message = ClampMessage(result);
            return ToolResponses.Text(ToolJson.Serialize(new
            {
                name = application!.Name,
                action = result.Action,
                button = result.Button,
                windowX = result.WindowX,
                windowY = result.WindowY,
                delta = result.AppliedDelta,
                message
            }));
        }

        private CallToolResult Key(string name, Func<ApplicationEntry, KeyActionResult> action)
        {
            if (!ApplicationLookup.TryGet(_store, name, out ApplicationEntry? application, out CallToolResult? error))
            {
                return error!;
            }

            KeyActionResult result = action(application!);
            if (result.Error != null)
            {
                return ToolResponses.Error(result.Error);
            }

            string? message = result.HoldWasClamped && result.AppliedHoldMilliseconds.HasValue
                ? $"holdMs was clamped to {result.AppliedHoldMilliseconds.Value}."
                : null;
            return ToolResponses.Text(ToolJson.Serialize(new
            {
                name = application!.Name,
                action = result.Action,
                key = result.Key,
                holdMs = result.AppliedHoldMilliseconds,
                length = result.Length,
                message
            }));
        }

        private static string? ClampMessage(MouseActionResult result)
        {
            var parts = new List<string>();
            if (result.HoldWasClamped && result.AppliedHoldMilliseconds.HasValue)
            {
                parts.Add($"holdMs was clamped to {result.AppliedHoldMilliseconds.Value}.");
            }

            if (result.DeltaWasClamped && result.AppliedDelta.HasValue)
            {
                parts.Add($"delta was clamped to {result.AppliedDelta.Value}.");
            }

            if (parts.Count == 0)
            {
                return null;
            }

            return string.Join(" ", parts);
        }
    }
}

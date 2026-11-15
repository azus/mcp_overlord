using System.ComponentModel;
using mcp_overlord.Configuration;
using mcp_overlord.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace mcp_overlord.Tools
{
    /// <summary>Finds and focuses a configured window. Callers pass a name, never a handle.</summary>
    [McpServerToolType]
    public sealed class WindowTools
    {
        private readonly ConfigStore _store;
        private readonly WindowLocator _locator;
        private readonly InputService _input;

        /// <summary>
        /// Creates the tools.
        /// </summary>
        public WindowTools(ConfigStore store, WindowLocator locator, InputService input)
        {
            _store = store;
            _locator = locator;
            _input = input;
        }

        /// <summary>Finds the window. A missing window is a successful result with found false.</summary>
        [McpServerTool(Name = "find_window", ReadOnly = true, Idempotent = true, OpenWorld = false),
         Description("Find the window for a configured application. found false means it is not on screen yet.")]
        public CallToolResult FindWindow(
            [Description("Configured application name.")] string name)
        {
            if (!ApplicationLookup.TryGet(_store, name, out ApplicationEntry? application, out CallToolResult? error))
            {
                return error!;
            }

            WindowLookup lookup = _locator.Find(application!, false, true);
            return ToolResponses.Text(Format(lookup));
        }

        /// <summary>Restores a minimized window and tries to focus it.</summary>
        [McpServerTool(Name = "focus_window", OpenWorld = false),
         Description("Restore the window if it is minimized and bring it to the foreground.")]
        public CallToolResult FocusWindow(
            [Description("Configured application name.")] string name)
        {
            if (!ApplicationLookup.TryGet(_store, name, out ApplicationEntry? application, out CallToolResult? error))
            {
                return error!;
            }

            FocusResult result = _input.Focus(application!);
            if (result.Error != null)
            {
                return ToolResponses.Error(result.Error);
            }

            return ToolResponses.Text(ToolJson.Serialize(new
            {
                name = application!.Name,
                focused = result.Focused,
                message = result.Message
            }));
        }

        /// <summary>
        /// Formats a window lookup as the JSON object from the design.
        /// </summary>
        /// <param name="lookup">Lookup to format.</param>
        /// <returns>JSON text.</returns>
        public static string Format(WindowLookup lookup)
        {
            if (!lookup.Found)
            {
                return ToolJson.Serialize(new
                {
                    found = false,
                    matchCount = lookup.MatchCount,
                    processRunning = lookup.ProcessRunning,
                    processId = lookup.ProcessRunning ? lookup.ProcessId : (int?)null,
                    message = lookup.Message
                });
            }

            return ToolJson.Serialize(new
            {
                found = true,
                hwnd = lookup.HwndHex,
                processId = lookup.ProcessId,
                processName = lookup.ProcessName,
                title = lookup.Title,
                matchCount = lookup.MatchCount,
                window = new { width = lookup.ScreenWidth, height = lookup.ScreenHeight },
                screen = new { x = lookup.ScreenX, y = lookup.ScreenY, width = lookup.ScreenWidth, height = lookup.ScreenHeight },
                state = lookup.State,
                message = lookup.Message
            });
        }
    }

    /// <summary>Shared name lookup for tools.</summary>
    public static class ApplicationLookup
    {
        /// <summary>
        /// Resolves a name against the snapshot taken at the start of the call.
        /// </summary>
        public static bool TryGet(ConfigStore store, string name, out ApplicationEntry? application, out CallToolResult? error)
        {
            ConfigDocument? document = store.Capture();
            if (document == null)
            {
                application = null;
                error = ToolResponses.Error(ToolErrors.NoConfiguration);
                return false;
            }

            foreach (ApplicationEntry entry in document.Applications)
            {
                if (string.Equals(entry.Name, name, StringComparison.Ordinal))
                {
                    application = entry;
                    error = null;
                    return true;
                }
            }

            application = null;
            error = ToolResponses.Error(ToolErrors.UnknownName(
                name ?? "",
                document.Applications.Select(entry => entry.Name).ToArray()));
            return false;
        }
    }
}

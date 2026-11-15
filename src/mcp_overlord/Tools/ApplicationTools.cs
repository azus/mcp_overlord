using System.ComponentModel;
using mcp_overlord.Configuration;
using mcp_overlord.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace mcp_overlord.Tools
{
    /// <summary>Lists applications, returns a description, and launches an allowlisted executable.</summary>
    [McpServerToolType]
    public sealed class ApplicationTools
    {
        private readonly ConfigStore _store;
        private readonly WindowLocator _locator;
        private readonly AppLauncher _launcher;

        /// <summary>
        /// Creates the tools.
        /// </summary>
        public ApplicationTools(ConfigStore store, WindowLocator locator, AppLauncher launcher)
        {
            _store = store;
            _locator = locator;
            _launcher = launcher;
        }

        /// <summary>Lists every configured application and whether it is running.</summary>
        [McpServerTool(Name = "list_applications", ReadOnly = true, Idempotent = true, OpenWorld = false),
         Description("List every configured application. Call this first to learn the names.")]
        public CallToolResult ListApplications()
        {
            ConfigDocument? document = _store.Capture();
            if (document == null)
            {
                return ToolResponses.Error(ToolErrors.NoConfiguration);
            }

            var applications = new List<object>();
            foreach (ApplicationEntry application in document.Applications)
            {
                WindowLookup lookup = _locator.Find(application, false);
                applications.Add(new
                {
                    name = application.Name,
                    description = application.Description ?? "",
                    hasDescription = !string.IsNullOrEmpty(application.Description),
                    launchable = application.IsLaunchable,
                    running = lookup.ProcessRunning || lookup.Found,
                    windowFound = lookup.Found
                });
            }

            return ToolResponses.Text(ToolJson.Serialize(new { applications }));
        }

        /// <summary>Returns the configured description. An empty description is still a success.</summary>
        [McpServerTool(Name = "get_application_description", ReadOnly = true, Idempotent = true, OpenWorld = false),
         Description("Return the configured description for an application. Call this when list_applications says hasDescription is true.")]
        public CallToolResult GetApplicationDescription(
            [Description("Configured application name.")] string name)
        {
            if (!TryGet(name, out ApplicationEntry? application, out CallToolResult? error))
            {
                return error!;
            }

            return ToolResponses.Text(ToolJson.Serialize(new
            {
                name = application!.Name,
                description = application.Description ?? ""
            }));
        }

        /// <summary>Starts the configured executable. Arguments come from the file, not from the caller.</summary>
        [McpServerTool(Name = "launch_application", OpenWorld = false),
         Description("Launch a configured executable. The path and arguments come from the configuration, not from this call.")]
        public CallToolResult LaunchApplication(
            [Description("Configured application name.")] string name,
            [Description("Seconds to wait for a window. Clamped to 0-120. Default 0.")] double waitForWindowSeconds = 0)
        {
            if (!TryGet(name, out ApplicationEntry? application, out CallToolResult? error))
            {
                return error!;
            }

            LaunchOutcome outcome = _launcher.Launch(application!, waitForWindowSeconds);
            if (outcome.Error != null)
            {
                return ToolResponses.Error(outcome.Error);
            }

            string? message = outcome.WaitWasClamped
                ? $"waitForWindowSeconds was clamped to {outcome.AppliedWaitSeconds}."
                : null;
            return ToolResponses.Text(ToolJson.Serialize(new
            {
                name = application!.Name,
                started = outcome.Started,
                processId = outcome.ProcessId,
                windowFound = outcome.WindowFound,
                message
            }));
        }

        private bool TryGet(string name, out ApplicationEntry? application, out CallToolResult? error)
        {
            ConfigDocument? document = _store.Capture();
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

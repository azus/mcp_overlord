using mcp_overlord.Services;
using mcp_overlord.Startup;

namespace mcp_overlord.Shell
{
    /// <summary>One Start menu task before it is turned into a shell jump item.</summary>
    public sealed class JumpTaskDescriptor
    {
        /// <summary>Label shown in the jump list.</summary>
        public string Title { get; init; } = "";

        /// <summary>Tooltip text.</summary>
        public string Description { get; init; } = "";

        /// <summary>Executable the task starts.</summary>
        public string ApplicationPath { get; init; } = "";

        /// <summary>Arguments passed to <see cref="ApplicationPath"/>.</summary>
        public string Arguments { get; init; } = "";

        /// <summary>Executable or icon file that supplies the task icon.</summary>
        public string IconResourcePath { get; init; } = "";

        /// <summary>Icon index inside <see cref="IconResourcePath"/>.</summary>
        public int IconResourceIndex { get; init; }
    }

    /// <summary>
    /// Builds the Start menu tasks. Open is supplied by Windows and is not repeated here.
    /// </summary>
    public static class JumpListBuilder
    {
        /// <summary>Identity shared by the process, the Start shortcut, and the .overlord ProgId.</summary>
        public const string AppUserModelId = "game_automation_in_csharp.MCPOverlord";

        /// <summary>Task that starts an elevated instance.</summary>
        public const string RunAsAdministratorTitle = "Run as Administrator";

        /// <summary>Task that uninstalls the installed product.</summary>
        public const string UninstallTitle = "Uninstall";

        /// <summary>
        /// Builds Run as Administrator, and Uninstall when <paramref name="productCode"/> is a product code.
        /// </summary>
        /// <param name="executablePath">Path of mcp_overlord.exe.</param>
        /// <param name="productCode">Installed product code, or null when this build is not installed.</param>
        /// <returns>The tasks, in display order.</returns>
        public static IReadOnlyList<JumpTaskDescriptor> Build(string executablePath, string? productCode)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new ArgumentException("An executable path is required.", nameof(executablePath));
            }

            var tasks = new List<JumpTaskDescriptor>
            {
                new JumpTaskDescriptor
                {
                    Title = RunAsAdministratorTitle,
                    Description = "Start MCP Overlord elevated",
                    ApplicationPath = executablePath,
                    Arguments = CommandLineParser.ElevateArgument,
                    IconResourcePath = executablePath,
                    IconResourceIndex = 0
                }
            };

            string? normalizedProductCode = InstallationInfo.NormalizeProductCode(productCode);
            if (normalizedProductCode != null)
            {
                tasks.Add(new JumpTaskDescriptor
                {
                    Title = UninstallTitle,
                    Description = "Uninstall MCP Overlord",
                    ApplicationPath = Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
                    Arguments = "/x " + normalizedProductCode,
                    IconResourcePath = executablePath,
                    IconResourceIndex = 0
                });
            }

            return tasks;
        }
    }
}

namespace mcp_overlord.Startup
{
    /// <summary>
    /// Result of parsing the process arguments.
    /// </summary>
    public sealed class CommandLineParseResult
    {
        /// <summary>True when the user asked for help. The process should print <see cref="Usage"/> and exit.</summary>
        public bool Help { get; init; }

        /// <summary>True when the process should start an elevated instance and exit if that start succeeds.</summary>
        public bool Elevate { get; init; }

        /// <summary>True when the process should publish the Start menu jump list and exit.</summary>
        public bool RegisterShell { get; init; }

        /// <summary>Absolute configuration path from <c>--config</c> or a positional path, when present.</summary>
        public string? ConfigPath { get; init; }

        /// <summary>Argument error. The window still opens and the server stays stopped.</summary>
        public string? Error { get; init; }

        /// <summary>Usage text.</summary>
        public string Usage { get; init; } = CommandLineParser.UsageText;
    }

    /// <summary>
    /// Parses a positional configuration path, <c>--config</c>, <c>--elevate</c>, <c>--register-shell</c>, <c>--help</c>, and <c>-h</c>.
    /// A second path, or any other argument, is an error.
    /// </summary>
    public static class CommandLineParser
    {
        /// <summary>Argument that starts an elevated instance and exits when that start succeeds.</summary>
        public const string ElevateArgument = "--elevate";

        /// <summary>Argument that publishes the Start menu jump list and exits.</summary>
        public const string RegisterShellArgument = "--register-shell";

        /// <summary>Usage printed for <c>--help</c>.</summary>
        public const string UsageText =
            "mcp_overlord [file.overlord] [--config <path>] [--elevate] [--register-shell] [--help]\r\n" +
            "\r\n" +
            "file.overlord      Load this file and start the server.\r\n" +
            "--config <path>    Load this file and start the server.\r\n" +
            "--elevate          Start an elevated instance and exit when it starts.\r\n" +
            "--register-shell   Rebuild the Start menu list and exit.\r\n" +
            "--help, -h         Show this help and exit.";

        /// <summary>
        /// Parses arguments. A relative configuration path is resolved against <paramref name="workingDirectory"/>.
        /// </summary>
        /// <param name="arguments">Process arguments, without the executable name.</param>
        /// <param name="workingDirectory">Directory relative paths resolve against.</param>
        /// <returns>The parse result.</returns>
        public static CommandLineParseResult Parse(string[] arguments, string workingDirectory)
        {
            if (arguments == null)
            {
                throw new ArgumentNullException(nameof(arguments));
            }

            string? configPath = null;
            bool elevate = false;
            bool registerShell = false;
            for (int index = 0; index < arguments.Length; index++)
            {
                string argument = arguments[index];
                if (argument == "--help" || argument == "-h")
                {
                    return new CommandLineParseResult
                    {
                        Help = true
                    };
                }

                if (argument == ElevateArgument)
                {
                    elevate = true;
                    continue;
                }

                if (argument == RegisterShellArgument)
                {
                    registerShell = true;
                    continue;
                }

                if (argument == "--config")
                {
                    if (index + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index + 1]))
                    {
                        return Error("Missing path after --config.");
                    }

                    if (configPath != null)
                    {
                        return Error("Only one configuration path can be supplied.");
                    }

                    configPath = ResolvePath(arguments[index + 1], workingDirectory);
                    index++;
                    continue;
                }

                if (argument.StartsWith('-'))
                {
                    return Error($"Unknown argument '{argument}'.");
                }

                if (configPath != null)
                {
                    return Error("Only one configuration path can be supplied.");
                }

                configPath = ResolvePath(argument, workingDirectory);
            }

            return new CommandLineParseResult
            {
                Elevate = elevate,
                RegisterShell = registerShell,
                ConfigPath = configPath
            };
        }

        private static string ResolvePath(string rawPath, string workingDirectory)
        {
            return Path.IsPathRooted(rawPath)
                ? Path.GetFullPath(rawPath)
                : Path.GetFullPath(Path.Combine(workingDirectory, rawPath));
        }

        private static CommandLineParseResult Error(string message)
        {
            return new CommandLineParseResult
            {
                Error = message
            };
        }
    }
}

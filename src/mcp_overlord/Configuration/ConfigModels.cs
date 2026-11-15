using System.IO;
using System.Net;

namespace mcp_overlord.Configuration
{
    /// <summary>
    /// Mouse backend selected by an application entry.
    /// </summary>
    public enum ClickBackend
    {
        /// <summary><c>mouse_event</c> plus <c>SetCursorPos</c>. This is the default.</summary>
        MouseEvent,

        /// <summary><c>SendInput</c> with absolute moves.</summary>
        SendInput
    }

    /// <summary>
    /// PKCS#12 certificate used when HTTPS is enabled.
    /// A null or empty <see cref="Path"/> means the server creates and reuses a certificate.
    /// </summary>
    /// <param name="Path">Absolute path of the .pfx file, or null when the server should create one.</param>
    /// <param name="Password">Password passed to the certificate loader. Empty when the file has none.</param>
    /// <param name="HostNames">DNS names and IP addresses included in a generated certificate.</param>
    public sealed record CertificateSettings(string? Path, string Password, IReadOnlyList<string> HostNames);

    /// <summary>
    /// Listener settings. Omitted file fields are already replaced with defaults.
    /// </summary>
    public sealed record ServerSettings(
        int HttpPort,
        int HttpsPort,
        bool EnableHttps,
        CertificateSettings? Certificate,
        string BindAddress,
        string? ApiKey,
        int LogRetention = 1000)
    {
        /// <summary>How many log lines are kept when the file omits <c>logRetention</c>.</summary>
        public const int DefaultLogRetention = 1000;

        /// <summary>Smallest <c>logRetention</c> the schema accepts.</summary>
        public const int MinimumLogRetention = 100;

        /// <summary>Largest <c>logRetention</c> the schema accepts.</summary>
        public const int MaximumLogRetention = 1000000;

        /// <summary>Defaults used when the file omits <c>server</c>.</summary>
        public static ServerSettings CreateDefault()
        {
            return new ServerSettings(5000, 5001, false, null, "127.0.0.1", null, DefaultLogRetention);
        }

        /// <summary>True when the bind address is a loopback IP.</summary>
        public bool IsLoopback
        {
            get { return NetworkAddresses.IsLoopback(BindAddress); }
        }

        /// <summary>
        /// True when the values that require a listener restart are the same.
        /// </summary>
        /// <param name="other">Settings to compare.</param>
        /// <returns>True when a restart is not required.</returns>
        public bool HasSameListener(ServerSettings other)
        {
            if (other == null)
            {
                return false;
            }

            return HttpPort == other.HttpPort
                && HttpsPort == other.HttpsPort
                && EnableHttps == other.EnableHttps
                && string.Equals(BindAddress, other.BindAddress, System.StringComparison.Ordinal)
                && string.Equals(ApiKey, other.ApiKey, System.StringComparison.Ordinal)
                && string.Equals(Certificate?.Path, other.Certificate?.Path, System.StringComparison.Ordinal)
                && string.Equals(Certificate?.Password ?? "", other.Certificate?.Password ?? "", System.StringComparison.Ordinal)
                && SameHostNames(Certificate?.HostNames, other.Certificate?.HostNames);
        }

        private static bool SameHostNames(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
        {
            IReadOnlyList<string> leftNames = left ?? System.Array.Empty<string>();
            IReadOnlyList<string> rightNames = right ?? System.Array.Empty<string>();
            if (leftNames.Count != rightNames.Count)
            {
                return false;
            }

            for (int index = 0; index < leftNames.Count; index++)
            {
                if (!string.Equals(leftNames[index], rightNames[index], System.StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// One configured application. Paths are stored absolute after a successful load.
    /// <see cref="Name"/>, <see cref="Executable"/>, and <see cref="WindowTitle"/> are required.
    /// </summary>
    public sealed record ApplicationEntry(
        string Name,
        string Executable,
        string? Parameters,
        string? WorkingDirectory,
        string WindowTitle,
        string? ProcessName,
        string? Description,
        ClickBackend ClickBackend)
    {
        /// <summary>True when <see cref="Executable"/> is set, so <c>launch_application</c> can start it.</summary>
        public bool IsLaunchable
        {
            get { return !string.IsNullOrEmpty(Executable); }
        }

        /// <summary>
        /// Process name used to locate the window. An omitted name falls back to the executable file name.
        /// </summary>
        public string? EffectiveProcessName
        {
            get
            {
                if (!string.IsNullOrEmpty(ProcessName))
                {
                    return ProcessName;
                }

                if (!string.IsNullOrEmpty(Executable))
                {
                    return Path.GetFileNameWithoutExtension(Executable);
                }

                return null;
            }
        }
    }

    /// <summary>
    /// A configuration document that has passed validation.
    /// </summary>
    /// <param name="SchemaVersion">Schema version. This build only produces 1.</param>
    /// <param name="Server">Listener settings.</param>
    /// <param name="Applications">Configured applications. May be empty.</param>
    /// <param name="SourcePath">Absolute path of the file that was loaded.</param>
    public sealed record ConfigDocument(
        int SchemaVersion,
        ServerSettings Server,
        IReadOnlyList<ApplicationEntry> Applications,
        string SourcePath);

    /// <summary>
    /// One validation failure. <see cref="Path"/> is a JSON pointer such as <c>/applications/1/name</c>.
    /// </summary>
    /// <param name="Path">JSON pointer, or the file path when the file itself is missing.</param>
    /// <param name="Message">Message safe to show in the log. It never includes secrets.</param>
    public sealed record ConfigValidationError(string Path, string Message);

    /// <summary>
    /// The outcome of loading a configuration file.
    /// </summary>
    public sealed class ConfigLoadResult
    {
        /// <summary>The document when validation succeeded.</summary>
        public ConfigDocument? Document { get; init; }

        /// <summary>Validation errors. Empty on success.</summary>
        public IReadOnlyList<ConfigValidationError> Errors { get; init; } = System.Array.Empty<ConfigValidationError>();

        /// <summary>Warnings that do not reject the file, such as a missing executable.</summary>
        public IReadOnlyList<string> Warnings { get; init; } = System.Array.Empty<string>();

        /// <summary>True when <see cref="Document"/> is usable.</summary>
        public bool Success
        {
            get { return Document != null && Errors.Count == 0; }
        }

        /// <summary>
        /// Creates a failed result.
        /// </summary>
        /// <param name="errors">One or more errors.</param>
        /// <returns>A failed result.</returns>
        public static ConfigLoadResult Failure(params ConfigValidationError[] errors)
        {
            return new ConfigLoadResult
            {
                Errors = errors
            };
        }
    }

    /// <summary>
    /// Loopback checks and secret scrubbing shared by the host and the loader.
    /// </summary>
    public static class NetworkAddresses
    {
        /// <summary>
        /// True when <paramref name="bindAddress"/> parses as a loopback IP.
        /// </summary>
        /// <param name="bindAddress">Address from the configuration.</param>
        /// <returns>True for 127.0.0.1 and ::1.</returns>
        public static bool IsLoopback(string bindAddress)
        {
            if (!IPAddress.TryParse(bindAddress, out IPAddress? address))
            {
                return false;
            }

            return IPAddress.IsLoopback(address);
        }
    }

    /// <summary>
    /// Thrown when the embedded JSON schema cannot be read. The application cannot validate configuration without it.
    /// </summary>
    public sealed class SchemaResourceMissingException : System.Exception
    {
        /// <summary>
        /// Creates the exception.
        /// </summary>
        /// <param name="message">What failed.</param>
        /// <param name="inner">The underlying failure, if any.</param>
        public SchemaResourceMissingException(string message, System.Exception? inner = null)
            : base(message, inner)
        {
        }
    }
}

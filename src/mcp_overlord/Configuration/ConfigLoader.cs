using System.Text.Json;
using Json.Schema;

namespace mcp_overlord.Configuration
{
    /// <summary>
    /// Reads a configuration file, checks <c>schemaVersion</c>, validates the embedded schema, then applies the extra rules.
    /// </summary>
    public sealed class ConfigLoader
    {
        /// <summary>Embedded resource name of <c>config.schema.json</c>.</summary>
        public const string SchemaResourceName = "mcp_overlord.Resources.config.schema.json";

        /// <summary>The only schema version this build accepts.</summary>
        public const int SupportedSchemaVersion = 1;

        /// <summary>Message used when <c>schemaVersion</c> is missing or not an integer.</summary>
        public const string SchemaVersionRequiredMessage =
            "schemaVersion is required and must be an integer. This build accepts schemaVersion 1.";

        private readonly JsonSchema _schema;

        /// <summary>
        /// Loads the embedded schema. A failure here is fatal for the process.
        /// </summary>
        public ConfigLoader()
        {
            try
            {
                SchemaText = ReadSchemaText();
                _schema = JsonSchema.FromText(SchemaText, new BuildOptions { SchemaRegistry = new SchemaRegistry() });
            }
            catch (SchemaResourceMissingException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new SchemaResourceMissingException(
                    "The embedded configuration schema could not be loaded. The application cannot validate configuration.",
                    exception);
            }
        }

        /// <summary>The embedded schema text. Tests compare it with the design.</summary>
        public string SchemaText { get; }

        /// <summary>
        /// Loads a file from disk.
        /// </summary>
        /// <param name="path">Absolute or relative path. Relative paths use the process working directory.</param>
        /// <returns>The load result. A missing file is a validation error.</returns>
        public ConfigLoadResult LoadFromFile(string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                return ConfigLoadResult.Failure(new ConfigValidationError(
                    fullPath,
                    $"Configuration file was not found: {fullPath}"));
            }

            string json;
            try
            {
                json = File.ReadAllText(fullPath);
            }
            catch (Exception exception)
            {
                return ConfigLoadResult.Failure(new ConfigValidationError(
                    fullPath,
                    "The configuration file could not be read: " + exception.Message));
            }

            return LoadJson(json, fullPath);
        }

        /// <summary>
        /// Validates JSON text. Relative paths resolve against the directory of <paramref name="sourcePath"/>.
        /// </summary>
        /// <param name="json">Raw JSON.</param>
        /// <param name="sourcePath">Path stored on the document and used to resolve relative paths.</param>
        /// <returns>The load result.</returns>
        public ConfigLoadResult LoadJson(string json, string sourcePath)
        {
            string fullSource = Path.GetFullPath(sourcePath);
            string? baseDirectory = Path.GetDirectoryName(fullSource);
            if (string.IsNullOrEmpty(baseDirectory))
            {
                baseDirectory = Environment.CurrentDirectory;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json);
            }
            catch (JsonException exception)
            {
                return ConfigLoadResult.Failure(new ConfigValidationError(
                    "/",
                    "The configuration file is not valid JSON: " + exception.Message));
            }

            using (document)
            {
                JsonElement root = document.RootElement;
                if (!TryReadSchemaVersion(root, out int schemaVersion))
                {
                    return ConfigLoadResult.Failure(new ConfigValidationError("/", SchemaVersionRequiredMessage));
                }

                if (schemaVersion != SupportedSchemaVersion)
                {
                    return ConfigLoadResult.Failure(new ConfigValidationError(
                        "/schemaVersion",
                        $"schemaVersion {schemaVersion} is not supported. This build accepts schemaVersion {SupportedSchemaVersion}."));
                }

                EvaluationResults evaluation = _schema.Evaluate(root, new EvaluationOptions
                {
                    OutputFormat = OutputFormat.List
                });

                if (!evaluation.IsValid)
                {
                    return new ConfigLoadResult
                    {
                        Errors = CollectSchemaErrors(evaluation)
                    };
                }

                return BuildDocument(root, fullSource, baseDirectory);
            }
        }

        private static bool TryReadSchemaVersion(JsonElement root, out int schemaVersion)
        {
            schemaVersion = 0;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!root.TryGetProperty("schemaVersion", out JsonElement property))
            {
                return false;
            }

            if (property.ValueKind != JsonValueKind.Number)
            {
                return false;
            }

            return property.TryGetInt32(out schemaVersion);
        }

        private static IReadOnlyList<ConfigValidationError> CollectSchemaErrors(EvaluationResults evaluation)
        {
            var errors = new List<ConfigValidationError>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Collect(evaluation, errors, seen);
            if (errors.Count == 0)
            {
                errors.Add(new ConfigValidationError("/", "The configuration does not match the schema."));
            }

            return errors;
        }

        private static void Collect(EvaluationResults node, List<ConfigValidationError> errors, HashSet<string> seen)
        {
            if (node.Errors != null)
            {
                string path = FormatPath(node.InstanceLocation.ToString());
                foreach (KeyValuePair<string, string> error in node.Errors)
                {
                    string message = string.IsNullOrEmpty(error.Value) ? error.Key : error.Value;
                    string key = path + "|" + message;
                    if (seen.Add(key))
                    {
                        errors.Add(new ConfigValidationError(path, message));
                    }
                }
            }

            if (node.Details == null)
            {
                return;
            }

            foreach (EvaluationResults child in node.Details)
            {
                Collect(child, errors, seen);
            }
        }

        private static string FormatPath(string? pointer)
        {
            if (string.IsNullOrEmpty(pointer) || pointer == "#")
            {
                return "/";
            }

            return pointer.StartsWith('/') ? pointer : "/" + pointer;
        }

        private static ConfigLoadResult BuildDocument(JsonElement root, string sourcePath, string baseDirectory)
        {
            var errors = new List<ConfigValidationError>();
            var warnings = new List<string>();
            ServerSettings server = ReadServer(root, baseDirectory, errors);
            IReadOnlyList<ApplicationEntry> applications = ReadApplications(root, baseDirectory, errors, warnings);
            if (errors.Count > 0)
            {
                return new ConfigLoadResult
                {
                    Errors = errors,
                    Warnings = warnings
                };
            }

            return new ConfigLoadResult
            {
                Document = new ConfigDocument(SupportedSchemaVersion, server, applications, sourcePath),
                Warnings = warnings
            };
        }

        private static ServerSettings ReadServer(JsonElement root, string baseDirectory, List<ConfigValidationError> errors)
        {
            ServerSettings defaults = ServerSettings.CreateDefault();
            if (!root.TryGetProperty("server", out JsonElement server) || server.ValueKind != JsonValueKind.Object)
            {
                return defaults;
            }

            int httpPort = ReadInt(server, "httpPort", defaults.HttpPort);
            int httpsPort = ReadInt(server, "httpsPort", defaults.HttpsPort);
            bool enableHttps = ReadBool(server, "enableHttps", defaults.EnableHttps);
            string? bindAddress = ReadOptionalString(server, "bindAddress");
            if (string.IsNullOrEmpty(bindAddress))
            {
                bindAddress = defaults.BindAddress;
            }

            string? apiKey = ReadOptionalString(server, "apiKey");

            CertificateSettings? certificate = null;
            if (server.TryGetProperty("certificate", out JsonElement certificateElement) && certificateElement.ValueKind == JsonValueKind.Object)
            {
                string? certificatePath = ReadOptionalString(certificateElement, "path");

                string password = "";
                if (certificateElement.TryGetProperty("password", out JsonElement passwordElement))
                {
                    password = passwordElement.GetString() ?? "";
                }

                if (!string.IsNullOrWhiteSpace(certificatePath))
                {
                    certificatePath = ResolvePath(baseDirectory, certificatePath);
                }
                else
                {
                    certificatePath = null;
                }

                certificate = new CertificateSettings(certificatePath, password, ReadStringList(certificateElement, "hostNames"));
            }

            if (enableHttps && httpPort == httpsPort)
            {
                errors.Add(new ConfigValidationError(
                    "/server/httpsPort",
                    "httpsPort must differ from httpPort when HTTPS is enabled."));
            }

            if (!IPAddressParses(bindAddress))
            {
                errors.Add(new ConfigValidationError(
                    "/server/bindAddress",
                    "bindAddress must be an IP address."));
            }

            string? configuredCertificatePath = certificate?.Path;
            if (enableHttps && !string.IsNullOrWhiteSpace(configuredCertificatePath) && !File.Exists(configuredCertificatePath))
            {
                errors.Add(new ConfigValidationError(
                    "/server/certificate/path",
                    "Certificate file was not found: " + configuredCertificatePath));
            }

            int logRetention = ReadInt(server, "logRetention", defaults.LogRetention);
            if (logRetention < ServerSettings.MinimumLogRetention || logRetention > ServerSettings.MaximumLogRetention)
            {
                errors.Add(new ConfigValidationError(
                    "/server/logRetention",
                    "logRetention must be an integer from "
                        + ServerSettings.MinimumLogRetention
                        + " to "
                        + ServerSettings.MaximumLogRetention
                        + "."));
            }

            return new ServerSettings(httpPort, httpsPort, enableHttps, certificate, bindAddress, apiKey, logRetention);
        }

        private static IReadOnlyList<ApplicationEntry> ReadApplications(
            JsonElement root,
            string baseDirectory,
            List<ConfigValidationError> errors,
            List<string> warnings)
        {
            var applications = new List<ApplicationEntry>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (!root.TryGetProperty("applications", out JsonElement array) || array.ValueKind != JsonValueKind.Array)
            {
                return applications;
            }

            int index = 0;
            foreach (JsonElement item in array.EnumerateArray())
            {
                string name = ReadString(item, "name") ?? "";
                if (string.IsNullOrEmpty(name))
                {
                    errors.Add(new ConfigValidationError(
                        $"/applications/{index}/name",
                        "name is required."));
                }
                else if (!names.Add(name))
                {
                    errors.Add(new ConfigValidationError(
                        $"/applications/{index}/name",
                        $"Application name '{name}' is duplicated."));
                }

                string executable = ReadString(item, "executable") ?? "";
                if (string.IsNullOrEmpty(executable))
                {
                    errors.Add(new ConfigValidationError(
                        $"/applications/{index}/executable",
                        "executable is required."));
                }
                else if (string.IsNullOrEmpty(Path.GetFileName(executable)))
                {
                    errors.Add(new ConfigValidationError(
                        $"/applications/{index}/executable",
                        "executable must include a file name."));
                }
                else
                {
                    executable = ResolvePath(baseDirectory, executable);
                    if (!File.Exists(executable))
                    {
                        warnings.Add($"Executable was not found: {executable}. Launch will fail until the file is present.");
                    }
                }

                string windowTitle = ReadString(item, "windowTitle") ?? "";
                if (string.IsNullOrEmpty(windowTitle))
                {
                    errors.Add(new ConfigValidationError(
                        $"/applications/{index}/windowTitle",
                        "windowTitle is required."));
                }

                string? workingDirectory = ReadOptionalString(item, "workingDirectory");
                if (!string.IsNullOrEmpty(workingDirectory))
                {
                    workingDirectory = ResolvePath(baseDirectory, workingDirectory);
                }
                else if (!string.IsNullOrEmpty(executable))
                {
                    workingDirectory = Path.GetDirectoryName(executable);
                }

                string? clickBackendText = ReadOptionalString(item, "clickBackend");
                ClickBackend clickBackend = clickBackendText == "sendInput" ? ClickBackend.SendInput : ClickBackend.MouseEvent;

                applications.Add(new ApplicationEntry(
                    name,
                    executable,
                    ReadOptionalString(item, "parameters"),
                    workingDirectory,
                    windowTitle,
                    ReadOptionalString(item, "processName"),
                    ReadOptionalString(item, "description"),
                    clickBackend));
                index++;
            }

            return applications;
        }

        private static string ResolvePath(string baseDirectory, string path)
        {
            if (Path.IsPathRooted(path))
            {
                return Path.GetFullPath(path);
            }

            return Path.GetFullPath(Path.Combine(baseDirectory, path));
        }

        private static bool IPAddressParses(string bindAddress)
        {
            return System.Net.IPAddress.TryParse(bindAddress, out _);
        }

        private static int ReadInt(JsonElement element, string name, int fallback)
        {
            if (element.TryGetProperty(name, out JsonElement property) && property.TryGetInt32(out int value))
            {
                return value;
            }

            return fallback;
        }

        private static bool ReadBool(JsonElement element, string name, bool fallback)
        {
            if (element.TryGetProperty(name, out JsonElement property)
                && (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False))
            {
                return property.GetBoolean();
            }

            return fallback;
        }

        private static string? ReadString(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return property.GetString();
        }

        /// <summary>
        /// Reads a string and treats a missing or empty value as not set.
        /// </summary>
        private static string? ReadOptionalString(JsonElement element, string name)
        {
            string? value = ReadString(element, name);
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            return value;
        }

        private static IReadOnlyList<string> ReadStringList(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            var values = new List<string>();
            foreach (JsonElement item in property.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string? value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value);
                }
            }

            return values;
        }

        private static string ReadSchemaText()
        {
            using Stream? stream = typeof(ConfigLoader).Assembly.GetManifestResourceStream(SchemaResourceName);
            if (stream == null)
            {
                throw new SchemaResourceMissingException(
                    "The embedded configuration schema was not found. The application cannot validate configuration.");
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}

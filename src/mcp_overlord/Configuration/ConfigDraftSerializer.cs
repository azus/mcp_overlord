using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace mcp_overlord.Configuration
{
    /// <summary>
    /// Reads and writes a <see cref="ConfigDraft"/> as version-1 JSON.
    /// Only included fields are written. Included empty strings are written as empty strings.
    /// </summary>
    public static class ConfigDraftSerializer
    {
        private static readonly JsonWriterOptions WriterOptions = new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>
        /// Reads a version-1 document. A property that is present is marked included.
        /// </summary>
        /// <param name="json">Configuration JSON.</param>
        /// <returns>The draft. Paths are not resolved.</returns>
        public static ConfigDraft Read(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            var draft = new ConfigDraft();
            if (root.TryGetProperty("schemaVersion", out JsonElement version) && version.TryGetInt32(out int schemaVersion))
            {
                draft.SchemaVersion = schemaVersion;
            }

            if (root.TryGetProperty("server", out JsonElement server) && server.ValueKind == JsonValueKind.Object)
            {
                ReadInt(server, "httpPort", draft.Server.HttpPort);
                ReadInt(server, "httpsPort", draft.Server.HttpsPort);
                ReadBool(server, "enableHttps", draft.Server.EnableHttps);
                ReadString(server, "bindAddress", draft.Server.BindAddress);
                ReadString(server, "apiKey", draft.Server.ApiKey);
                ReadInt(server, "logRetention", draft.Server.LogRetention);
                if (server.TryGetProperty("certificate", out JsonElement certificate) && certificate.ValueKind == JsonValueKind.Object)
                {
                    ReadString(certificate, "path", draft.Server.CertificatePath);
                    ReadString(certificate, "password", draft.Server.CertificatePassword);
                    if (certificate.TryGetProperty("hostNames", out JsonElement hostNames) && hostNames.ValueKind == JsonValueKind.Array)
                    {
                        draft.Server.HostNames.IsIncluded = true;
                        draft.Server.HostNames.Value = JoinHostNames(hostNames);
                    }
                }
            }

            if (root.TryGetProperty("applications", out JsonElement applications) && applications.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in applications.EnumerateArray())
                {
                    draft.Applications.Add(ReadApplication(item));
                }
            }

            return draft;
        }

        /// <summary>
        /// Writes the draft. The text ends with a newline and uses a stable property order.
        /// </summary>
        /// <param name="draft">Draft to write.</param>
        /// <returns>JSON text.</returns>
        public static string Write(ConfigDraft draft)
        {
            if (draft == null)
            {
                throw new ArgumentNullException(nameof(draft));
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, WriterOptions))
            {
                writer.WriteStartObject();
                writer.WriteNumber("schemaVersion", draft.SchemaVersion);
                WriteServer(writer, draft.Server);
                writer.WritePropertyName("applications");
                writer.WriteStartArray();
                foreach (ApplicationDraft application in draft.Applications)
                {
                    WriteApplication(writer, application);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
        }

        /// <summary>
        /// Splits host names written one per line. Blank lines are dropped.
        /// </summary>
        /// <param name="text">Editor text.</param>
        /// <returns>The names.</returns>
        public static IReadOnlyList<string> SplitHostNames(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return Array.Empty<string>();
            }

            var names = new List<string>();
            string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    names.Add(trimmed);
                }
            }

            return names;
        }

        /// <summary>
        /// Converts a click backend to the schema enum text.
        /// </summary>
        /// <param name="backend">Backend.</param>
        /// <returns><c>mouseEvent</c> or <c>sendInput</c>.</returns>
        public static string ClickBackendName(ClickBackend backend)
        {
            return backend == ClickBackend.SendInput ? "sendInput" : "mouseEvent";
        }

        /// <summary>
        /// Parses a click backend name. Anything other than <c>sendInput</c> is <see cref="ClickBackend.MouseEvent"/>.
        /// </summary>
        /// <param name="name">Schema text.</param>
        /// <returns>The backend.</returns>
        public static ClickBackend ParseClickBackend(string? name)
        {
            return string.Equals(name, "sendInput", StringComparison.Ordinal) ? ClickBackend.SendInput : ClickBackend.MouseEvent;
        }

        private static ApplicationDraft ReadApplication(JsonElement item)
        {
            var application = new ApplicationDraft
            {
                Name = ReadRequiredString(item, "name"),
                Executable = ReadRequiredString(item, "executable"),
                WindowTitle = ReadRequiredString(item, "windowTitle")
            };
            ReadString(item, "parameters", application.Parameters);
            ReadString(item, "workingDirectory", application.WorkingDirectory);
            ReadString(item, "processName", application.ProcessName);
            ReadString(item, "description", application.Description);
            if (item.TryGetProperty("clickBackend", out JsonElement clickBackend) && clickBackend.ValueKind == JsonValueKind.String)
            {
                application.ClickBackend.IsIncluded = true;
                application.ClickBackend.Value = ParseClickBackend(clickBackend.GetString());
            }

            return application;
        }

        private static void WriteServer(Utf8JsonWriter writer, ServerDraft server)
        {
            bool certificate = server.CertificatePath.IsIncluded
                || server.CertificatePassword.IsIncluded
                || server.HostNames.IsIncluded;
            bool any = server.BindAddress.IsIncluded
                || server.HttpPort.IsIncluded
                || server.HttpsPort.IsIncluded
                || server.EnableHttps.IsIncluded
                || server.ApiKey.IsIncluded
                || server.LogRetention.IsIncluded
                || certificate;
            if (!any)
            {
                return;
            }

            writer.WritePropertyName("server");
            writer.WriteStartObject();
            WriteString(writer, "bindAddress", server.BindAddress);
            WriteBool(writer, "enableHttps", server.EnableHttps);
            WriteInt(writer, "httpsPort", server.HttpsPort);
            WriteInt(writer, "httpPort", server.HttpPort);
            WriteString(writer, "apiKey", server.ApiKey);
            WriteInt(writer, "logRetention", server.LogRetention);
            if (certificate)
            {
                writer.WritePropertyName("certificate");
                writer.WriteStartObject();
                WriteString(writer, "path", server.CertificatePath);
                WriteString(writer, "password", server.CertificatePassword);
                if (server.HostNames.IsIncluded)
                {
                    writer.WritePropertyName("hostNames");
                    writer.WriteStartArray();
                    foreach (string name in SplitHostNames(server.HostNames.Value))
                    {
                        writer.WriteStringValue(name);
                    }

                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        private static void WriteApplication(Utf8JsonWriter writer, ApplicationDraft application)
        {
            writer.WriteStartObject();
            writer.WriteString("name", application.Name ?? "");
            writer.WriteString("executable", application.Executable ?? "");
            WriteString(writer, "parameters", application.Parameters);
            WriteString(writer, "workingDirectory", application.WorkingDirectory);
            writer.WriteString("windowTitle", application.WindowTitle ?? "");
            WriteString(writer, "processName", application.ProcessName);
            if (application.ClickBackend.IsIncluded)
            {
                writer.WriteString("clickBackend", ClickBackendName(application.ClickBackend.Value));
            }

            WriteString(writer, "description", application.Description);
            writer.WriteEndObject();
        }

        private static void WriteString(Utf8JsonWriter writer, string name, DraftField<string> field)
        {
            if (!field.IsIncluded)
            {
                return;
            }

            writer.WriteString(name, field.Value ?? "");
        }

        private static void WriteInt(Utf8JsonWriter writer, string name, DraftField<int> field)
        {
            if (!field.IsIncluded)
            {
                return;
            }

            writer.WriteNumber(name, field.Value);
        }

        private static void WriteBool(Utf8JsonWriter writer, string name, DraftField<bool> field)
        {
            if (!field.IsIncluded)
            {
                return;
            }

            writer.WriteBoolean(name, field.Value);
        }

        private static void ReadString(JsonElement element, string name, DraftField<string> field)
        {
            if (!element.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String)
            {
                return;
            }

            field.IsIncluded = true;
            field.Value = property.GetString() ?? "";
        }

        private static void ReadInt(JsonElement element, string name, DraftField<int> field)
        {
            if (!element.TryGetProperty(name, out JsonElement property) || !property.TryGetInt32(out int value))
            {
                return;
            }

            field.IsIncluded = true;
            field.Value = value;
        }

        private static void ReadBool(JsonElement element, string name, DraftField<bool> field)
        {
            if (!element.TryGetProperty(name, out JsonElement property))
            {
                return;
            }

            if (property.ValueKind != JsonValueKind.True && property.ValueKind != JsonValueKind.False)
            {
                return;
            }

            field.IsIncluded = true;
            field.Value = property.GetBoolean();
        }

        private static string ReadRequiredString(JsonElement element, string name)
        {
            if (element.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString() ?? "";
            }

            return "";
        }

        private static string JoinHostNames(JsonElement array)
        {
            var names = new List<string>();
            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string? value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    names.Add(value);
                }
            }

            return string.Join("\n", names);
        }
    }

    /// <summary>
    /// Turns relative paths into absolute paths when a file is saved into a different folder.
    /// </summary>
    public static class ConfigDraftRebase
    {
        /// <summary>
        /// Rewrites relative executable, working directory, and certificate paths using <paramref name="baseDirectory"/>.
        /// Absolute paths are left unchanged.
        /// </summary>
        /// <param name="draft">Draft to update.</param>
        /// <param name="baseDirectory">Directory the relative paths were written against.</param>
        public static void MakeRelativePathsAbsolute(ConfigDraft draft, string baseDirectory)
        {
            if (draft == null)
            {
                throw new ArgumentNullException(nameof(draft));
            }

            if (string.IsNullOrEmpty(baseDirectory))
            {
                throw new ArgumentException("A base directory is required.", nameof(baseDirectory));
            }

            draft.Server.CertificatePath.Value = ToAbsolute(draft.Server.CertificatePath.Value, baseDirectory);
            foreach (ApplicationDraft application in draft.Applications)
            {
                application.Executable = ToAbsolute(application.Executable, baseDirectory);
                application.WorkingDirectory.Value = ToAbsolute(application.WorkingDirectory.Value, baseDirectory);
            }
        }

        private static string ToAbsolute(string? path, string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            {
                return path ?? "";
            }

            return Path.GetFullPath(Path.Combine(baseDirectory, path));
        }
    }
}

namespace mcp_overlord.Configuration
{
    /// <summary>
    /// One editable value and whether the serializer writes it.
    /// An excluded field is left out of the file, so the loader uses its default.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    public sealed class DraftField<T>
    {
        /// <summary>The value shown in the editor.</summary>
        public T Value { get; set; } = default!;

        /// <summary>True when the value is written, even when it is empty.</summary>
        public bool IsIncluded { get; set; }

        /// <summary>
        /// Copies the value and the include flag onto <paramref name="target"/>.
        /// </summary>
        /// <param name="target">Field that receives the copy.</param>
        public void CopyTo(DraftField<T> target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            target.Value = Value;
            target.IsIncluded = IsIncluded;
        }
    }

    /// <summary>
    /// Editable server settings. Included fields are written; the others stay at the loader defaults.
    /// </summary>
    public sealed class ServerDraft
    {
        /// <summary>HTTP port. Default 5000.</summary>
        public DraftField<int> HttpPort { get; } = new DraftField<int> { Value = 5000 };

        /// <summary>HTTPS port. Default 5001.</summary>
        public DraftField<int> HttpsPort { get; } = new DraftField<int> { Value = 5001 };

        /// <summary>True when HTTPS is enabled.</summary>
        public DraftField<bool> EnableHttps { get; } = new DraftField<bool>();

        /// <summary>Bind address. Default 127.0.0.1.</summary>
        public DraftField<string> BindAddress { get; } = new DraftField<string> { Value = "127.0.0.1" };

        /// <summary>Shared secret. Empty means no key when the field is saved.</summary>
        public DraftField<string> ApiKey { get; } = new DraftField<string> { Value = "" };

        /// <summary>PKCS#12 path. Empty means the server creates a certificate.</summary>
        public DraftField<string> CertificatePath { get; } = new DraftField<string> { Value = "" };

        /// <summary>Certificate password.</summary>
        public DraftField<string> CertificatePassword { get; } = new DraftField<string> { Value = "" };

        /// <summary>Host names, one per line.</summary>
        public DraftField<string> HostNames { get; } = new DraftField<string> { Value = "" };

        /// <summary>How many log lines the window keeps. Default 1000.</summary>
        public DraftField<int> LogRetention { get; } = new DraftField<int> { Value = ServerSettings.DefaultLogRetention };

        /// <summary>
        /// Copies every field onto a new instance.
        /// </summary>
        /// <returns>The copy.</returns>
        public ServerDraft Clone()
        {
            var clone = new ServerDraft();
            CopyTo(clone);
            return clone;
        }

        /// <summary>
        /// Copies every field onto <paramref name="target"/>.
        /// </summary>
        /// <param name="target">Server draft that receives the copy.</param>
        public void CopyTo(ServerDraft target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            HttpPort.CopyTo(target.HttpPort);
            HttpsPort.CopyTo(target.HttpsPort);
            EnableHttps.CopyTo(target.EnableHttps);
            BindAddress.CopyTo(target.BindAddress);
            ApiKey.CopyTo(target.ApiKey);
            CertificatePath.CopyTo(target.CertificatePath);
            CertificatePassword.CopyTo(target.CertificatePassword);
            HostNames.CopyTo(target.HostNames);
            LogRetention.CopyTo(target.LogRetention);
        }
    }

    /// <summary>
    /// Editable application. Name, executable, and window title are always written.
    /// </summary>
    public sealed class ApplicationDraft
    {
        /// <summary>Unique application name.</summary>
        public string Name { get; set; } = "";

        /// <summary>Executable path. May be relative to the configuration file.</summary>
        public string Executable { get; set; } = "";

        /// <summary>Window title substring.</summary>
        public string WindowTitle { get; set; } = "";

        /// <summary>Command-line arguments.</summary>
        public DraftField<string> Parameters { get; } = new DraftField<string> { Value = "" };

        /// <summary>Working directory.</summary>
        public DraftField<string> WorkingDirectory { get; } = new DraftField<string> { Value = "" };

        /// <summary>Process name without .exe.</summary>
        public DraftField<string> ProcessName { get; } = new DraftField<string> { Value = "" };

        /// <summary>Description returned by get_application_description.</summary>
        public DraftField<string> Description { get; } = new DraftField<string> { Value = "" };

        /// <summary>Mouse backend. Omitted means mouseEvent.</summary>
        public DraftField<ClickBackend> ClickBackend { get; } = new DraftField<ClickBackend> { Value = Configuration.ClickBackend.MouseEvent };

        /// <summary>
        /// Copies the application.
        /// </summary>
        /// <returns>The copy.</returns>
        public ApplicationDraft Clone()
        {
            var clone = new ApplicationDraft
            {
                Name = Name,
                Executable = Executable,
                WindowTitle = WindowTitle
            };
            Parameters.CopyTo(clone.Parameters);
            WorkingDirectory.CopyTo(clone.WorkingDirectory);
            ProcessName.CopyTo(clone.ProcessName);
            Description.CopyTo(clone.Description);
            ClickBackend.CopyTo(clone.ClickBackend);
            return clone;
        }
    }

    /// <summary>
    /// Editable configuration. Values stay as the user typed them, including relative paths.
    /// </summary>
    public sealed class ConfigDraft
    {
        /// <summary>Schema version written to the file.</summary>
        public int SchemaVersion { get; set; } = ConfigLoader.SupportedSchemaVersion;

        /// <summary>Server settings.</summary>
        public ServerDraft Server { get; } = new ServerDraft();

        /// <summary>Applications in file order.</summary>
        public List<ApplicationDraft> Applications { get; } = new List<ApplicationDraft>();

        /// <summary>
        /// Creates an empty version-1 document with no included server fields.
        /// </summary>
        /// <returns>The new draft.</returns>
        public static ConfigDraft CreateNew()
        {
            return new ConfigDraft();
        }

        /// <summary>
        /// Copies the draft, including every application.
        /// </summary>
        /// <returns>The copy.</returns>
        public ConfigDraft Clone()
        {
            var clone = new ConfigDraft
            {
                SchemaVersion = SchemaVersion
            };
            Server.CopyTo(clone.Server);
            foreach (ApplicationDraft application in Applications)
            {
                clone.Applications.Add(application.Clone());
            }

            return clone;
        }

        /// <summary>
        /// Builds a draft from a document the loader already resolved.
        /// Fields the loader filled in are marked included when they differ from a missing value.
        /// </summary>
        /// <param name="document">Validated document.</param>
        /// <returns>The draft.</returns>
        public static ConfigDraft FromDocument(ConfigDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var draft = new ConfigDraft
            {
                SchemaVersion = document.SchemaVersion
            };
            ServerSettings server = document.Server;
            Include(draft.Server.HttpPort, server.HttpPort);
            Include(draft.Server.HttpsPort, server.HttpsPort);
            Include(draft.Server.EnableHttps, server.EnableHttps);
            Include(draft.Server.BindAddress, server.BindAddress);
            if (!string.IsNullOrEmpty(server.ApiKey))
            {
                Include(draft.Server.ApiKey, server.ApiKey);
            }

            draft.Server.LogRetention.Value = server.LogRetention;
            if (server.LogRetention != ServerSettings.DefaultLogRetention)
            {
                Include(draft.Server.LogRetention, server.LogRetention);
            }

            if (server.Certificate != null)
            {
                if (!string.IsNullOrEmpty(server.Certificate.Path))
                {
                    Include(draft.Server.CertificatePath, server.Certificate.Path);
                }

                if (!string.IsNullOrEmpty(server.Certificate.Password))
                {
                    Include(draft.Server.CertificatePassword, server.Certificate.Password);
                }

                if (server.Certificate.HostNames.Count > 0)
                {
                    Include(draft.Server.HostNames, string.Join("\n", server.Certificate.HostNames));
                }
            }

            foreach (ApplicationEntry application in document.Applications)
            {
                draft.Applications.Add(FromApplication(application));
            }

            return draft;
        }

        private static ApplicationDraft FromApplication(ApplicationEntry application)
        {
            var draft = new ApplicationDraft
            {
                Name = application.Name,
                Executable = application.Executable,
                WindowTitle = application.WindowTitle
            };
            IncludeWhenSet(draft.Parameters, application.Parameters);
            IncludeWhenSet(draft.WorkingDirectory, application.WorkingDirectory);
            IncludeWhenSet(draft.ProcessName, application.ProcessName);
            IncludeWhenSet(draft.Description, application.Description);
            if (application.ClickBackend != Configuration.ClickBackend.MouseEvent)
            {
                Include(draft.ClickBackend, application.ClickBackend);
            }

            return draft;
        }

        private static void IncludeWhenSet(DraftField<string> field, string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            Include(field, value);
        }

        private static void Include<T>(DraftField<T> field, T value)
        {
            field.Value = value;
            field.IsIncluded = true;
        }
    }
}

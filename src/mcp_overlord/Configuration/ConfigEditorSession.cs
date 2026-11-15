namespace mcp_overlord.Configuration
{
    /// <summary>
    /// Outcome of opening, applying, or saving a configuration draft.
    /// </summary>
    public sealed class ConfigEditResult
    {
        /// <summary>True when the operation completed.</summary>
        public bool Success { get; init; }

        /// <summary>True when Save was asked to write a document that has no path yet.</summary>
        public bool NeedsPath { get; init; }

        /// <summary>Failure that is not a validation error, such as a disk error.</summary>
        public string? Error { get; init; }

        /// <summary>Validation errors. Empty on success.</summary>
        public IReadOnlyList<ConfigValidationError> Errors { get; init; } = Array.Empty<ConfigValidationError>();

        /// <summary>Loader warnings, such as a missing executable.</summary>
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Creates a successful result.
        /// </summary>
        /// <param name="warnings">Warnings to keep.</param>
        /// <returns>The result.</returns>
        public static ConfigEditResult Succeeded(IReadOnlyList<string>? warnings = null)
        {
            return new ConfigEditResult
            {
                Success = true,
                Warnings = warnings ?? Array.Empty<string>()
            };
        }

        /// <summary>
        /// Creates a validation failure. The session is unchanged.
        /// </summary>
        /// <param name="errors">Errors from the loader.</param>
        /// <returns>The result.</returns>
        public static ConfigEditResult Failed(IReadOnlyList<ConfigValidationError> errors)
        {
            return new ConfigEditResult
            {
                Errors = errors
            };
        }

        /// <summary>
        /// Creates a failure with one message.
        /// </summary>
        /// <param name="error">Message safe to show.</param>
        /// <returns>The result.</returns>
        public static ConfigEditResult Failed(string error)
        {
            return new ConfigEditResult
            {
                Error = error
            };
        }

        /// <summary>
        /// Creates the result Save returns when the document has never been saved.
        /// </summary>
        /// <returns>The result.</returns>
        public static ConfigEditResult PathRequired()
        {
            return new ConfigEditResult
            {
                NeedsPath = true
            };
        }
    }

    /// <summary>
    /// The open configuration file: the editable draft, the path, and whether it differs from the last save.
    /// Applying a draft updates the running server immediately. Saving writes the draft and clears the dirty flag.
    /// </summary>
    public sealed class ConfigEditorSession
    {
        private readonly ConfigLoader _loader;
        private readonly ConfigStore _store;
        private readonly Func<bool> _serverIsRunning;
        private ConfigDraft _draft = new ConfigDraft();
        private string _baselineJson = "";
        private string? _filePath;
        private bool _hasDocument;

        /// <summary>
        /// Creates a session with no document. Call <see cref="New"/> or <see cref="Open"/> before editing.
        /// </summary>
        /// <param name="loader">Validates drafts before they are applied.</param>
        /// <param name="store">Receives every successful apply.</param>
        /// <param name="serverIsRunning">True while a listener is open, so listener changes stay pending.</param>
        public ConfigEditorSession(ConfigLoader loader, ConfigStore store, Func<bool> serverIsRunning)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _serverIsRunning = serverIsRunning ?? throw new ArgumentNullException(nameof(serverIsRunning));
        }

        /// <summary>Raised after the draft, the path, or the dirty baseline changes.</summary>
        public event Action? Changed;

        /// <summary>The editable document. Empty until <see cref="New"/>, <see cref="Open"/>, or <see cref="Adopt"/>.</summary>
        public ConfigDraft Draft
        {
            get { return _draft; }
        }

        /// <summary>Full path of the open file. Null for a new document and when nothing is open.</summary>
        public string? FilePath
        {
            get { return _filePath; }
        }

        /// <summary>True after New, Open, or a successful apply.</summary>
        public bool HasDocument
        {
            get { return _hasDocument; }
        }

        /// <summary>
        /// True when the JSON that would be written differs from the JSON last loaded or saved.
        /// Undoing an edit clears this.
        /// </summary>
        public bool IsDirty
        {
            get
            {
                if (!_hasDocument)
                {
                    return false;
                }

                return !string.Equals(ConfigDraftSerializer.Write(_draft), _baselineJson, StringComparison.Ordinal);
            }
        }

        /// <summary><c>New</c>, or the file name including its extension.</summary>
        public string DisplayName
        {
            get
            {
                if (string.IsNullOrEmpty(_filePath))
                {
                    return "New";
                }

                return Path.GetFileName(_filePath);
            }
        }

        /// <summary>
        /// Replaces the session with an empty document and applies it.
        /// </summary>
        public void New()
        {
            ConfigDraft created = ConfigDraft.CreateNew();
            _filePath = null;
            _baselineJson = ConfigDraftSerializer.Write(created);
            TryApply(created);
        }

        /// <summary>
        /// Loads a file. A failure leaves the current document in place.
        /// </summary>
        /// <param name="path">Configuration path.</param>
        /// <returns>The result. Success means the draft and the store now match the file.</returns>
        public ConfigEditResult Open(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return ConfigEditResult.Failed("A configuration path is required.");
            }

            ConfigLoadResult loaded = _loader.LoadFromFile(path);
            if (!loaded.Success || loaded.Document == null)
            {
                return ConfigEditResult.Failed(loaded.Errors);
            }

            string fullPath = Path.GetFullPath(path);
            ConfigDraft draft;
            try
            {
                draft = ConfigDraftSerializer.Read(File.ReadAllText(fullPath));
            }
            catch (Exception exception)
            {
                return ConfigEditResult.Failed("The configuration file could not be read: " + exception.Message);
            }

            _draft = draft;
            _filePath = fullPath;
            _hasDocument = true;
            _baselineJson = ConfigDraftSerializer.Write(draft);
            _store.Apply(loaded.Document, _serverIsRunning());
            Changed?.Invoke();
            return ConfigEditResult.Succeeded(loaded.Warnings);
        }

        /// <summary>
        /// Points the session at a document that was already applied to the store.
        /// Used when a caller loads through <see cref="ConfigLoader"/> directly.
        /// </summary>
        /// <param name="document">Document now held by the store.</param>
        public void Adopt(ConfigDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            _draft = ConfigDraft.FromDocument(document);
            _filePath = string.IsNullOrEmpty(document.SourcePath) ? null : document.SourcePath;
            _hasDocument = true;
            _baselineJson = ConfigDraftSerializer.Write(_draft);
            Changed?.Invoke();
        }

        /// <summary>
        /// Validates <paramref name="draft"/> and, on success, makes it the current document and applies it live.
        /// </summary>
        /// <param name="draft">Edited copy.</param>
        /// <returns>Validation errors when the draft is rejected. The previous document stays in place.</returns>
        public ConfigEditResult TryApply(ConfigDraft draft)
        {
            if (draft == null)
            {
                throw new ArgumentNullException(nameof(draft));
            }

            string json = ConfigDraftSerializer.Write(draft);
            string source = string.IsNullOrEmpty(_filePath)
                ? Path.Combine(Environment.CurrentDirectory, ConfigFile.DefaultFileName)
                : _filePath;
            ConfigLoadResult loaded = _loader.LoadJson(json, source);
            if (!loaded.Success || loaded.Document == null)
            {
                return ConfigEditResult.Failed(loaded.Errors);
            }

            ConfigDocument document = loaded.Document with
            {
                SourcePath = _filePath ?? ""
            };
            _draft = draft.Clone();
            _hasDocument = true;
            _store.Apply(document, _serverIsRunning());
            Changed?.Invoke();
            return ConfigEditResult.Succeeded(loaded.Warnings);
        }

        /// <summary>
        /// Writes the current draft to <see cref="FilePath"/>.
        /// </summary>
        /// <returns>A result with <see cref="ConfigEditResult.NeedsPath"/> when the document is new.</returns>
        public ConfigEditResult Save()
        {
            if (!_hasDocument)
            {
                return ConfigEditResult.Failed("There is no configuration to save.");
            }

            if (string.IsNullOrEmpty(_filePath))
            {
                return ConfigEditResult.PathRequired();
            }

            return WriteTo(_filePath, null);
        }

        /// <summary>
        /// Writes the current draft to <paramref name="path"/>.
        /// Relative paths are made absolute when the new folder differs from the previous file's folder.
        /// </summary>
        /// <param name="path">Destination. A relative path uses the process working directory.</param>
        /// <returns>The result.</returns>
        public ConfigEditResult SaveAs(string path)
        {
            if (!_hasDocument)
            {
                return ConfigEditResult.Failed("There is no configuration to save.");
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                return ConfigEditResult.Failed("A configuration path is required.");
            }

            string? previousDirectory = string.IsNullOrEmpty(_filePath) ? null : Path.GetDirectoryName(_filePath);
            return WriteTo(Path.GetFullPath(path), previousDirectory);
        }

        private ConfigEditResult WriteTo(string fullPath, string? previousDirectory)
        {
            ConfigDraft draft = _draft.Clone();
            RebaseWhenFolderChanges(draft, fullPath, previousDirectory);
            string json = ConfigDraftSerializer.Write(draft);
            try
            {
                string? directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(fullPath, json);
            }
            catch (Exception exception)
            {
                return ConfigEditResult.Failed("The configuration could not be saved: " + exception.Message);
            }

            _filePath = fullPath;
            _draft = draft;
            _baselineJson = json;
            _hasDocument = true;
            ConfigLoadResult loaded = _loader.LoadJson(json, fullPath);
            if (loaded.Success && loaded.Document != null)
            {
                _store.Apply(loaded.Document, _serverIsRunning());
            }

            Changed?.Invoke();
            return ConfigEditResult.Succeeded(loaded.Warnings);
        }

        private static void RebaseWhenFolderChanges(ConfigDraft draft, string fullPath, string? previousDirectory)
        {
            if (string.IsNullOrEmpty(previousDirectory))
            {
                return;
            }

            string? newDirectory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(newDirectory))
            {
                return;
            }

            if (string.Equals(Path.GetFullPath(previousDirectory), Path.GetFullPath(newDirectory), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ConfigDraftRebase.MakeRelativePathsAbsolute(draft, previousDirectory);
        }
    }
}

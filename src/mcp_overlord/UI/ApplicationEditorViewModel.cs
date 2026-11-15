using System.ComponentModel;
using System.Runtime.CompilerServices;
using mcp_overlord.Configuration;
using mcp_overlord.Services;

namespace mcp_overlord.UI
{
    /// <summary>
    /// Edits one application. Required fields are always saved. Optional fields are saved only when their checkbox is on.
    /// </summary>
    public sealed class ApplicationEditorViewModel : INotifyPropertyChanged
    {
        private readonly ConfigDraft _baseDraft;
        private readonly int _index;
        private readonly bool _isNew;
        private readonly IReadOnlyList<string> _otherNames;
        private readonly Func<ConfigDraft, ConfigEditResult> _apply;
        private string _name = "";
        private string _executable = "";
        private string _windowTitle = "";
        private string _parameters = "";
        private bool _includeParameters;
        private string _workingDirectory = "";
        private bool _includeWorkingDirectory;
        private string _processName = "";
        private bool _includeProcessName;
        private string _description = "";
        private bool _includeDescription;
        private string _clickBackendName = "mouseEvent";
        private bool _includeClickBackend;
        private string _nameError = "";
        private string _executableError = "";
        private string _windowTitleError = "";
        private string _formError = "";
        private string _dropStatus = "";
        private string _finderPreview = "Drag the target onto a window.";
        private bool _finderActive;

        /// <summary>
        /// Creates the editor for one application.
        /// </summary>
        /// <param name="baseDraft">Document the application belongs to. The editor clones it on OK.</param>
        /// <param name="application">Application being edited. A new application passes an empty draft.</param>
        /// <param name="index">Index in the document. A new application uses the index it will occupy.</param>
        /// <param name="isNew">True when OK adds an application instead of replacing one.</param>
        /// <param name="otherNames">Names that must stay unique, excluding this application.</param>
        /// <param name="apply">Validates and applies the whole document.</param>
        public ApplicationEditorViewModel(
            ConfigDraft baseDraft,
            ApplicationDraft application,
            int index,
            bool isNew,
            IReadOnlyList<string> otherNames,
            Func<ConfigDraft, ConfigEditResult> apply)
        {
            _baseDraft = baseDraft ?? throw new ArgumentNullException(nameof(baseDraft));
            _index = index;
            _isNew = isNew;
            _otherNames = otherNames ?? Array.Empty<string>();
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            _name = application.Name;
            _executable = application.Executable;
            _windowTitle = application.WindowTitle;
            _parameters = application.Parameters.Value;
            _includeParameters = application.Parameters.IsIncluded;
            _workingDirectory = application.WorkingDirectory.Value;
            _includeWorkingDirectory = application.WorkingDirectory.IsIncluded;
            _processName = application.ProcessName.Value;
            _includeProcessName = application.ProcessName.IsIncluded;
            _description = application.Description.Value;
            _includeDescription = application.Description.IsIncluded;
            _clickBackendName = ConfigDraftSerializer.ClickBackendName(application.ClickBackend.Value);
            _includeClickBackend = application.ClickBackend.IsIncluded;
        }

        /// <inheritdoc />
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>mouseEvent and sendInput.</summary>
        public IReadOnlyList<string> ClickBackendChoices { get; } = new[] { "mouseEvent", "sendInput" };

        /// <summary>Dialog title.</summary>
        public string DialogTitle
        {
            get { return _isNew ? "New application" : "Edit application"; }
        }

        /// <summary>True while the target is being dragged.</summary>
        public bool FinderActive
        {
            get { return _finderActive; }
            set { Set(ref _finderActive, value); }
        }

        /// <summary>Window under the target, or the idle hint.</summary>
        public string FinderPreview
        {
            get { return _finderPreview; }
            private set { Set(ref _finderPreview, value); }
        }

        /// <summary>What a drop or a capture filled.</summary>
        public string DropStatus
        {
            get { return _dropStatus; }
            set { Set(ref _dropStatus, value); }
        }

        /// <summary>Application name. Always saved.</summary>
        public string Name
        {
            get { return _name; }
            set { Set(ref _name, value ?? ""); }
        }

        /// <summary>Executable path. Always saved.</summary>
        public string Executable
        {
            get { return _executable; }
            set { Set(ref _executable, value ?? ""); }
        }

        /// <summary>Window title. Always saved.</summary>
        public string WindowTitle
        {
            get { return _windowTitle; }
            set { Set(ref _windowTitle, value ?? ""); }
        }

        /// <summary>Arguments.</summary>
        public string Parameters
        {
            get { return _parameters; }
            set { Set(ref _parameters, value ?? ""); }
        }

        /// <summary>True when parameters are written.</summary>
        public bool IncludeParameters
        {
            get { return _includeParameters; }
            set { Set(ref _includeParameters, value); }
        }

        /// <summary>Working directory.</summary>
        public string WorkingDirectory
        {
            get { return _workingDirectory; }
            set { Set(ref _workingDirectory, value ?? ""); }
        }

        /// <summary>True when the working directory is written.</summary>
        public bool IncludeWorkingDirectory
        {
            get { return _includeWorkingDirectory; }
            set { Set(ref _includeWorkingDirectory, value); }
        }

        /// <summary>Process name.</summary>
        public string ProcessName
        {
            get { return _processName; }
            set { Set(ref _processName, value ?? ""); }
        }

        /// <summary>True when the process name is written.</summary>
        public bool IncludeProcessName
        {
            get { return _includeProcessName; }
            set { Set(ref _includeProcessName, value); }
        }

        /// <summary>Description.</summary>
        public string Description
        {
            get { return _description; }
            set { Set(ref _description, value ?? ""); }
        }

        /// <summary>True when the description is written.</summary>
        public bool IncludeDescription
        {
            get { return _includeDescription; }
            set { Set(ref _includeDescription, value); }
        }

        /// <summary>mouseEvent or sendInput.</summary>
        public string ClickBackendName
        {
            get { return _clickBackendName; }
            set { Set(ref _clickBackendName, string.IsNullOrEmpty(value) ? "mouseEvent" : value); }
        }

        /// <summary>True when the click backend is written.</summary>
        public bool IncludeClickBackend
        {
            get { return _includeClickBackend; }
            set { Set(ref _includeClickBackend, value); }
        }

        /// <summary>Name validation message.</summary>
        public string NameError
        {
            get { return _nameError; }
            private set { Set(ref _nameError, value); }
        }

        /// <summary>Executable validation message.</summary>
        public string ExecutableError
        {
            get { return _executableError; }
            private set { Set(ref _executableError, value); }
        }

        /// <summary>Window title validation message.</summary>
        public string WindowTitleError
        {
            get { return _windowTitleError; }
            private set { Set(ref _windowTitleError, value); }
        }

        /// <summary>Errors that are not tied to one field.</summary>
        public string FormError
        {
            get { return _formError; }
            private set { Set(ref _formError, value); }
        }

        /// <summary>
        /// Fills the fields a shortcut or an executable provided and checks those fields.
        /// </summary>
        /// <param name="info">Dropped file.</param>
        public void ApplyShortcut(ShortcutInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            var filled = new List<string>();
            if (!string.IsNullOrWhiteSpace(info.Name))
            {
                Name = info.Name.Trim();
                filled.Add("name");
            }

            if (!string.IsNullOrWhiteSpace(info.TargetPath))
            {
                Executable = info.TargetPath;
                filled.Add("executable");
            }

            if (info.Arguments != null)
            {
                Parameters = info.Arguments;
                IncludeParameters = true;
                filled.Add("parameters");
            }

            if (info.WorkingDirectory != null)
            {
                WorkingDirectory = info.WorkingDirectory;
                IncludeWorkingDirectory = true;
                filled.Add("working directory");
            }

            if (!string.IsNullOrWhiteSpace(info.ProcessName))
            {
                ProcessName = info.ProcessName;
                IncludeProcessName = true;
                filled.Add("process name");
            }

            if (!string.IsNullOrWhiteSpace(info.Description))
            {
                Description = info.Description;
                IncludeDescription = true;
                filled.Add("description");
            }

            DropStatus = DescribeFilled(filled);
        }

        /// <summary>
        /// Fills the window and process fields captured from another window.
        /// </summary>
        /// <param name="info">Window under the cursor.</param>
        public void ApplyWindow(WindowInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            var filled = new List<string>();
            if (!string.IsNullOrWhiteSpace(info.Title))
            {
                WindowTitle = info.Title;
                filled.Add("window title");
            }

            if (!string.IsNullOrWhiteSpace(info.ExecutablePath))
            {
                Executable = info.ExecutablePath;
                filled.Add("executable");
            }

            if (!string.IsNullOrWhiteSpace(info.ProcessName))
            {
                ProcessName = info.ProcessName;
                IncludeProcessName = true;
                filled.Add("process name");
            }

            if (info.WorkingDirectory != null)
            {
                WorkingDirectory = info.WorkingDirectory;
                IncludeWorkingDirectory = true;
                filled.Add("working directory");
            }

            if (info.Arguments != null)
            {
                Parameters = info.Arguments;
                IncludeParameters = true;
                filled.Add("parameters");
            }

            DropStatus = DescribeFilled(filled);
        }

        /// <summary>
        /// Shows the window currently under the target.
        /// </summary>
        /// <param name="info">Window description.</param>
        public void SetFinderPreview(WindowInfo info)
        {
            if (info == null)
            {
                FinderPreview = "No window under the cursor.";
                return;
            }

            string title = string.IsNullOrEmpty(info.Title) ? "(no title)" : info.Title;
            string process = string.IsNullOrEmpty(info.ProcessName) ? "unknown process" : info.ProcessName;
            FinderPreview = title + " — " + process;
        }

        /// <summary>Restores the idle finder hint.</summary>
        public void ClearFinderPreview()
        {
            FinderPreview = FinderActive ? "No window under the cursor." : "Drag the target onto a window.";
        }

        /// <summary>
        /// Validates the fields and applies the document. The dialog stays open when this returns false.
        /// </summary>
        /// <returns>True when the document was applied.</returns>
        public bool TryCommit()
        {
            ClearErrors();
            if (!ValidateLocal())
            {
                return false;
            }

            ConfigDraft updated = _baseDraft.Clone();
            ApplicationDraft edited = ReadModel();
            if (_isNew)
            {
                updated.Applications.Add(edited);
            }
            else if (_index >= 0 && _index < updated.Applications.Count)
            {
                updated.Applications[_index] = edited;
            }
            else
            {
                FormError = "That application is no longer in the document.";
                return false;
            }

            ConfigEditResult result = _apply(updated);
            if (!result.Success)
            {
                MapErrors(result);
                return false;
            }

            return true;
        }

        private ApplicationDraft ReadModel()
        {
            var application = new ApplicationDraft
            {
                Name = Name.Trim(),
                Executable = Executable.Trim(),
                WindowTitle = WindowTitle.Trim()
            };
            application.Parameters.Value = Parameters;
            application.Parameters.IsIncluded = IncludeParameters;
            application.WorkingDirectory.Value = WorkingDirectory.Trim();
            application.WorkingDirectory.IsIncluded = IncludeWorkingDirectory;
            application.ProcessName.Value = ProcessName.Trim();
            application.ProcessName.IsIncluded = IncludeProcessName;
            application.Description.Value = Description;
            application.Description.IsIncluded = IncludeDescription;
            application.ClickBackend.Value = ConfigDraftSerializer.ParseClickBackend(ClickBackendName);
            application.ClickBackend.IsIncluded = IncludeClickBackend;
            return application;
        }

        private bool ValidateLocal()
        {
            bool valid = true;
            if (string.IsNullOrWhiteSpace(Name))
            {
                NameError = "Name is required.";
                valid = false;
            }
            else if (ContainsName(Name.Trim()))
            {
                NameError = "An application named '" + Name.Trim() + "' already exists.";
                valid = false;
            }

            if (string.IsNullOrWhiteSpace(Executable))
            {
                ExecutableError = "Executable is required.";
                valid = false;
            }
            else if (string.IsNullOrEmpty(Path.GetFileName(Executable.Trim())))
            {
                ExecutableError = "Executable must include a file name.";
                valid = false;
            }

            if (string.IsNullOrWhiteSpace(WindowTitle))
            {
                WindowTitleError = "Window title is required.";
                valid = false;
            }

            return valid;
        }

        private bool ContainsName(string name)
        {
            foreach (string other in _otherNames)
            {
                if (string.Equals(other, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void MapErrors(ConfigEditResult result)
        {
            var extra = new List<string>();
            if (!string.IsNullOrEmpty(result.Error))
            {
                extra.Add(result.Error);
            }

            string prefix = "/applications/" + _index + "/";
            foreach (ConfigValidationError error in result.Errors)
            {
                string field = error.Path.StartsWith(prefix, StringComparison.Ordinal)
                    ? error.Path.Substring(prefix.Length)
                    : "";
                if (field == "name")
                {
                    NameError = error.Message;
                }
                else if (field == "executable")
                {
                    ExecutableError = error.Message;
                }
                else if (field == "windowTitle")
                {
                    WindowTitleError = error.Message;
                }
                else
                {
                    extra.Add(error.Path + ": " + error.Message);
                }
            }

            FormError = string.Join(" ", extra);
        }

        private void ClearErrors()
        {
            NameError = "";
            ExecutableError = "";
            WindowTitleError = "";
            FormError = "";
        }

        private static string DescribeFilled(List<string> filled)
        {
            if (filled.Count == 0)
            {
                return "Nothing was filled.";
            }

            if (filled.Count == 1)
            {
                return "Filled " + filled[0] + ".";
            }

            if (filled.Count == 2)
            {
                return "Filled " + filled[0] + " and " + filled[1] + ".";
            }

            return "Filled " + string.Join(", ", filled.Take(filled.Count - 1)) + ", and " + filled[filled.Count - 1] + ".";
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}

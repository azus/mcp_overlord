using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using mcp_overlord.Configuration;
using mcp_overlord.Diagnostics;
using mcp_overlord.Hosting;
using mcp_overlord.Logging;
using mcp_overlord.Services;
using mcp_overlord.Shell;

namespace mcp_overlord.UI
{
    /// <summary>One application row.</summary>
    public sealed class ApplicationRow
    {
        /// <summary>Configured name.</summary>
        public string Name { get; init; } = "";

        /// <summary><c>running</c> or <c>stopped</c>.</summary>
        public string RunningText { get; init; } = "stopped";

        /// <summary>Configured description. Empty when none was set.</summary>
        public string Description { get; init; } = "";
    }

    /// <summary>One log row.</summary>
    public sealed class LogRow
    {
        /// <summary>
        /// Creates a row from an entry.
        /// </summary>
        /// <param name="entry">Log entry.</param>
        public LogRow(LogEntry entry)
        {
            Entry = entry;
            Timestamp = entry.Timestamp;
            Level = entry.Level.ToString();
            Category = entry.Category;
            Message = entry.Message;
            LevelValue = entry.Level;
        }

        /// <summary>The log entry this row shows.</summary>
        public LogEntry Entry { get; }

        /// <summary>Local timestamp. The grid formats it with milliseconds.</summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>Short tag such as server, config, or mcp.</summary>
        public string Category { get; }

        /// <summary>Info, Warning, or Error.</summary>
        public string Level { get; }

        /// <summary>Message text.</summary>
        public string Message { get; }

        /// <summary>Level used by the filter.</summary>
        public LogLevel LevelValue { get; }
    }

    /// <summary>One endpoint row.</summary>
    public sealed class EndpointRow
    {
        /// <summary>MCP URL.</summary>
        public string Url { get; init; } = "";

        /// <summary>True for HTTPS.</summary>
        public bool IsHttps { get; init; }
    }

    /// <summary>
    /// State of the main window: server button, log, stats, and the application list.
    /// </summary>
    public sealed class MainViewModel : INotifyPropertyChanged
    {
        /// <summary>Detail tab in the panel under the log.</summary>
        public const int DETAIL_TAB_INDEX = 0;

        /// <summary>Stats tab in the panel under the log.</summary>
        public const int STATS_TAB_INDEX = 1;

        /// <summary>Help tab in the panel under the log.</summary>
        public const int HELP_TAB_INDEX = 2;

        private readonly ConfigStore _store;
        private readonly ServerStateMachine _state;
        private readonly IServerHost _host;
        private readonly InputService _input;
        private readonly WindowLocator _locator;
        private readonly LogBuffer _log;
        private readonly StatsCounter _stats;
        private readonly IUserInteraction _interaction;
        private readonly IClipboardService _clipboard;
        private readonly IUiInvoker _ui;
        private readonly ConfigEditorSession _session;
        private readonly IElevationService _elevation;
        private readonly IRecentFileRegistry? _recentFiles;
        private readonly ConnectionDiagnostics _diagnostics;
        private readonly RelayCommand _toggleCommand;
        private readonly RelayCommand _saveCommand;
        private readonly RelayCommand _saveAsCommand;
        private readonly RelayCommand _settingsCommand;
        private readonly RelayCommand _addCommand;
        private readonly RelayCommand _editCommand;
        private readonly RelayCommand _duplicateCommand;
        private readonly RelayCommand _removeCommand;
        private int _logPullQueued;
        private int _applicationRefreshRunning;
        private int _applicationRefreshPending;
        private string? _configDetail;
        private string _configPath = "";
        private string _selectedLevelFilter = "All";
        private string? _selectedApplicationName;
        private int _bottomPanelTabIndex;
        private bool _refreshingApplications;
        private bool _closingForRestart;
        private LogRow? _selectedLogRow;

        /// <summary>
        /// Creates the view model and subscribes to the log, the state machine, and the configuration store.
        /// </summary>
        public MainViewModel(
            ConfigStore store,
            ServerStateMachine state,
            IServerHost host,
            InputService input,
            WindowLocator locator,
            LogBuffer log,
            StatsCounter stats,
            IUserInteraction interaction,
            IClipboardService clipboard,
            IUiInvoker ui,
            ConfigEditorSession session,
            IElevationService? elevation = null,
            ConnectionDiagnostics? diagnostics = null,
            IRecentFileRegistry? recentFiles = null)
        {
            _store = store;
            _state = state;
            _host = host;
            _input = input;
            _locator = locator;
            _log = log;
            _stats = stats;
            _interaction = interaction;
            _clipboard = clipboard;
            _ui = ui;
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _elevation = elevation ?? new ElevationService(new UnelevatedIntegrity());
            _recentFiles = recentFiles;
            _diagnostics = diagnostics ?? new ConnectionDiagnostics(new SystemDiagnosticProbes());
            Detail = new LogDetailViewModel(interaction, clipboard);

            Applications = new ObservableCollection<ApplicationRow>();
            LogRows = new ObservableCollection<LogRow>();
            Endpoints = new ObservableCollection<EndpointRow>();
            LevelFilters = new ObservableCollection<string> { "All", "Info", "Warning", "Error" };

            _toggleCommand = new RelayCommand(() => ToggleServerAsync(), CanToggleServer);
            LoadConfigCommand = new RelayCommand(LoadFromDialog);
            NewConfigCommand = new RelayCommand(NewConfig);
            _saveCommand = new RelayCommand(() => SaveConfig(), () => _session.HasDocument);
            _saveAsCommand = new RelayCommand(() => SaveConfigAs(), () => _session.HasDocument);
            _settingsCommand = new RelayCommand(EditServerSettings, () => _session.HasDocument);
            _addCommand = new RelayCommand(AddApplication, () => _session.HasDocument);
            _editCommand = new RelayCommand(EditSelected, HasSelectedApplication);
            _duplicateCommand = new RelayCommand(DuplicateSelected, HasSelectedApplication);
            _removeCommand = new RelayCommand(RemoveSelected, HasSelectedApplication);
            ClearLogCommand = new RelayCommand(ClearLog);
            CopyEndpointCommand = new RelayCommand<string>(CopyEndpoint);
            RestartAsAdministratorCommand = new RelayCommand(() => RestartAsAdministratorAsync(), () => !_elevation.IsElevated);
            CheckConnectionCommand = new RelayCommand(CheckConnection);
            ShowHelpCommand = new RelayCommand(() => BottomPanelTabIndex = HELP_TAB_INDEX);
            EditApplicationRowCommand = new RelayCommand<ApplicationRow>(EditApplicationRow);

            _state.Changed += () => _ui.Invoke(Publish);
            _store.Changed += () => _ui.Invoke(Publish);
            _session.Changed += () => _ui.Invoke(Publish);
            _log.EntriesAdded += QueueLogPull;
            Publish();
            PullLog();
        }

        /// <inheritdoc />
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Raised after the log collection is rebuilt, so the window can stick to the tail.</summary>
        public event Action? LogRebuilt;

        /// <summary>Applications in the loaded file.</summary>
        public ObservableCollection<ApplicationRow> Applications { get; }

        /// <summary>Log rows after the level filter.</summary>
        public ObservableCollection<LogRow> LogRows { get; }

        /// <summary>Endpoint URLs.</summary>
        public ObservableCollection<EndpointRow> Endpoints { get; }

        /// <summary>All, Info, Warning, Error.</summary>
        public ObservableCollection<string> LevelFilters { get; }

        /// <summary>Load config command.</summary>
        public ICommand LoadConfigCommand { get; }

        /// <summary>Starts a new unsaved configuration.</summary>
        public ICommand NewConfigCommand { get; }

        /// <summary>Saves the current file, or asks for a path when the document is new.</summary>
        public ICommand SaveConfigCommand
        {
            get { return _saveCommand; }
        }

        /// <summary>Saves the current document to a chosen path.</summary>
        public ICommand SaveConfigAsCommand
        {
            get { return _saveAsCommand; }
        }

        /// <summary>Opens the server settings editor.</summary>
        public ICommand EditServerSettingsCommand
        {
            get { return _settingsCommand; }
        }

        /// <summary>Adds an application.</summary>
        public ICommand AddApplicationCommand
        {
            get { return _addCommand; }
        }

        /// <summary>Edits the selected application.</summary>
        public ICommand EditApplicationCommand
        {
            get { return _editCommand; }
        }

        /// <summary>Edits the application passed as the command parameter.</summary>
        public ICommand EditApplicationRowCommand { get; }

        /// <summary>Duplicates the selected application.</summary>
        public ICommand DuplicateApplicationCommand
        {
            get { return _duplicateCommand; }
        }

        /// <summary>Removes the selected application.</summary>
        public ICommand RemoveApplicationCommand
        {
            get { return _removeCommand; }
        }

        /// <summary>Start or stop command.</summary>
        public ICommand ToggleServerCommand
        {
            get { return _toggleCommand; }
        }

        /// <summary>Clear log command.</summary>
        public ICommand ClearLogCommand { get; }

        /// <summary>Copy an endpoint URL. The parameter is the URL.</summary>
        public ICommand CopyEndpointCommand { get; }

        /// <summary>Selects the Help tab in the panel under the log.</summary>
        public ICommand ShowHelpCommand { get; }

        /// <summary>Stops, relaunches elevated, and closes this process.</summary>
        public ICommand RestartAsAdministratorCommand { get; }

        /// <summary>Opens the connection check.</summary>
        public ICommand CheckConnectionCommand { get; }

        /// <summary>The panel under the log.</summary>
        public LogDetailViewModel Detail { get; }

        /// <summary>Raised when an elevated process has been started and this one should exit.</summary>
        public event Action? ShutdownRequested;

        /// <summary>True when this process is elevated.</summary>
        public bool IsElevated
        {
            get { return _elevation.IsElevated; }
        }

        /// <summary>True when the restart button should show.</summary>
        public bool ShowRestartAsAdministrator
        {
            get { return !_elevation.IsElevated; }
        }

        /// <summary>The log line open in the detail panel.</summary>
        public LogRow? SelectedLogRow
        {
            get { return _selectedLogRow; }
            set
            {
                if (ReferenceEquals(_selectedLogRow, value))
                {
                    return;
                }

                _selectedLogRow = value;
                Detail.Show(value);
                OnPropertyChanged();
            }
        }

        /// <summary>Start, or Stop while the server is running.</summary>
        public string StartButtonText
        {
            get { return _state.State == ServerRunState.Running ? "Stop" : "Start"; }
        }

        /// <summary>True when the start icon should show.</summary>
        public bool ShowStartIcon
        {
            get { return _state.State != ServerRunState.Running; }
        }

        /// <summary>True when the stop icon should show.</summary>
        public bool ShowStopIcon
        {
            get { return _state.State == ServerRunState.Running; }
        }

        /// <summary>Status line.</summary>
        public string StatusText { get; private set; } = "Stopped";

        /// <summary>Stopped, Starting, Running, Error, or Config.</summary>
        public string StatusKind { get; private set; } = "Stopped";

        /// <summary>Full configuration path. Empty when nothing is loaded.</summary>
        public string ConfigPath
        {
            get { return _configPath; }
        }

        /// <summary>Path elided in the middle, or the empty-state text.</summary>
        public string ConfigPathDisplay { get; private set; } = "No configuration loaded";

        /// <summary>Title bar text. The suffix is New or the open file name.</summary>
        public string WindowTitle
        {
            get
            {
                string title = "MCP Overlord - " + _session.DisplayName;
                return _elevation.IsElevated ? "Administrator: " + title : title;
            }
        }

        /// <summary>True when the open document differs from the last load or save.</summary>
        public bool IsDirty
        {
            get { return _session.IsDirty; }
        }

        /// <summary>True when a document is open, including a new unsaved one.</summary>
        public bool HasDocument
        {
            get { return _session.HasDocument; }
        }

        /// <summary>The selected application row, matched by name across refreshes.</summary>
        public ApplicationRow? SelectedApplication
        {
            get
            {
                foreach (ApplicationRow row in Applications)
                {
                    if (string.Equals(row.Name, _selectedApplicationName, StringComparison.Ordinal))
                    {
                        return row;
                    }
                }

                return null;
            }
            set
            {
                if (_refreshingApplications)
                {
                    return;
                }

                string? name = value?.Name;
                if (string.Equals(_selectedApplicationName, name, StringComparison.Ordinal))
                {
                    return;
                }

                _selectedApplicationName = name;
                OnPropertyChanged();
                RaiseApplicationCommands();
            }
        }

        /// <summary>True when listener settings are waiting for the next start.</summary>
        public bool ListenerSettingsPending { get; private set; }

        /// <summary>Pending listener message.</summary>
        public string ListenerPendingText { get; private set; } = "";

        /// <summary>True when the shown URLs are the live listener.</summary>
        public bool EndpointsAreLive { get; private set; }

        /// <summary><c>not live</c> while stopped, otherwise empty.</summary>
        public string EndpointNote { get; private set; } = "";

        /// <summary>Help text.</summary>
        public string HelpText
        {
            get { return HelpContent.Text; }
        }

        /// <summary>Selected tab under the log. 0 is detail, 1 is stats, 2 is help.</summary>
        public int BottomPanelTabIndex
        {
            get { return _bottomPanelTabIndex; }
            set
            {
                if (value < DETAIL_TAB_INDEX)
                {
                    value = DETAIL_TAB_INDEX;
                }
                else if (value > HELP_TAB_INDEX)
                {
                    value = HELP_TAB_INDEX;
                }

                if (_bottomPanelTabIndex == value)
                {
                    return;
                }

                _bottomPanelTabIndex = value;
                OnPropertyChanged();
            }
        }

        /// <summary>Selected log level filter.</summary>
        public string SelectedLevelFilter
        {
            get { return _selectedLevelFilter; }
            set
            {
                if (string.Equals(_selectedLevelFilter, value, StringComparison.Ordinal))
                {
                    return;
                }

                _selectedLevelFilter = string.IsNullOrEmpty(value) ? "All" : value;
                OnPropertyChanged();
                PullLog();
            }
        }

        /// <summary>Uptime of the current run.</summary>
        public string UptimeText { get; private set; } = "00:00:00";

        /// <summary>Tool calls accepted.</summary>
        public string RequestsText { get; private set; } = "0";

        /// <summary>Tool errors plus failed HTTP checks.</summary>
        public string ErrorsText { get; private set; } = "0";

        /// <summary>Last tool latency.</summary>
        public string LastLatencyText { get; private set; } = "0 ms";

        /// <summary>Average tool latency.</summary>
        public string AverageLatencyText { get; private set; } = "0 ms";

        /// <summary>Successful screenshots.</summary>
        public string ScreenshotsText { get; private set; } = "0";

        /// <summary>Encoded image bytes.</summary>
        public string BytesText { get; private set; } = "0 B";

        /// <summary>Input events sent.</summary>
        public string InputsText { get; private set; } = "0";

        /// <summary>Requests in flight.</summary>
        public string SessionsText { get; private set; } = "0";

        /// <summary>Per-tool counts.</summary>
        public string PerToolText { get; private set; } = "";

        /// <summary>
        /// Loads a file. On failure the previous document stays and the status explains the error.
        /// </summary>
        /// <param name="path">Configuration path.</param>
        /// <param name="autoStart">True when this load was requested by <c>--config</c>.</param>
        /// <returns>True when the document is valid.</returns>
        public bool LoadConfig(string path, bool autoStart)
        {
            ConfigEditResult result = _session.Open(path);
            if (!result.Success)
            {
                foreach (ConfigValidationError error in result.Errors)
                {
                    _log.Append(LogLevel.Error, "config", error.Path + ": " + error.Message);
                }

                if (!string.IsNullOrEmpty(result.Error))
                {
                    _log.Append(LogLevel.Error, "config", result.Error);
                }

                int count = result.Errors.Count == 0 ? 1 : result.Errors.Count;
                _configDetail = "Configuration invalid, " + count + " errors — see log";
                if (autoStart)
                {
                    _state.MarkError(_configDetail);
                }

                Publish();
                return false;
            }

            return FinishSuccessfulLoad(result.Warnings);
        }

        /// <summary>
        /// Applies a load result. Tests use this without touching the disk.
        /// </summary>
        /// <param name="result">Loader result.</param>
        /// <param name="autoStart">True when a failure should move the state machine to Error.</param>
        /// <returns>True when the document is valid.</returns>
        public bool ApplyLoad(ConfigLoadResult result, bool autoStart)
        {
            if (!result.Success || result.Document == null)
            {
                foreach (ConfigValidationError error in result.Errors)
                {
                    _log.Append(LogLevel.Error, "config", error.Path + ": " + error.Message);
                }

                _configDetail = "Configuration invalid, " + result.Errors.Count + " errors — see log";
                if (autoStart)
                {
                    _state.MarkError(_configDetail);
                }

                Publish();
                return false;
            }

            bool running = _state.State == ServerRunState.Running;
            _store.Apply(result.Document, running);
            _session.Adopt(result.Document);
            _configDetail = null;
            _log.Append(
                LogLevel.Info,
                "config",
                "Loaded " + result.Document.SourcePath + " (" + result.Document.Applications.Count + " applications).");
            LogServerLine(result.Document.Server);
            LogWarnings(result.Warnings);
            LogListenerPending();
            if (_state.State == ServerRunState.Error)
            {
                _state.DismissError();
            }

            Publish();
            return true;
        }

        /// <summary>
        /// Asks to save when the document is dirty, then continues. Cancel leaves the document open.
        /// </summary>
        /// <returns>True when the caller may continue, including after a successful save or a discard.</returns>
        public bool ConfirmClose()
        {
            if (_closingForRestart)
            {
                return true;
            }

            return ConfirmSaveIfDirty();
        }

        /// <summary>
        /// Saves when the document is dirty, stops the listener, and starts an elevated process.
        /// A cancelled elevation prompt leaves this process open and starts the listener again when it was running.
        /// </summary>
        public async Task RestartAsAdministratorAsync()
        {
            if (_elevation.IsElevated)
            {
                return;
            }

            if (!ConfirmSaveIfDirty())
            {
                return;
            }

            bool wasRunning = _state.State == ServerRunState.Running || _host.IsRunning;
            if (wasRunning)
            {
                await StopAsync();
            }

            string? configPath = string.IsNullOrEmpty(_session.FilePath) ? null : _session.FilePath;
            bool started;
            try
            {
                _log.Append(LogLevel.Info, "server", "Restarting as administrator.");
                started = _elevation.RestartElevated(configPath);
            }
            catch (Exception exception)
            {
                _log.Append(LogLevel.Error, "server", "Could not restart as administrator: " + exception.Message);
                await ResumeAfterCancelledRestart(wasRunning);
                return;
            }

            if (!started)
            {
                _log.Append(LogLevel.Info, "server", "Restart as administrator was cancelled.");
                await ResumeAfterCancelledRestart(wasRunning);
                return;
            }

            _closingForRestart = true;
            ShutdownRequested?.Invoke();
        }

        /// <summary>Opens the connection report for the endpoints that are shown.</summary>
        public void CheckConnection()
        {
            ServerSettings? settings = EndpointSettings();
            bool running = _state.State == ServerRunState.Running;
            var report = new ConnectionReportViewModel(
                (progress, token) => _diagnostics.CheckAsync(settings, running, progress, token),
                _clipboard);
            _interaction.ShowConnectionReport(report);
        }

        /// <summary>Replaces the document with an empty one after asking about unsaved changes.</summary>
        public void NewConfig()
        {
            if (!ConfirmSaveIfDirty())
            {
                return;
            }

            _selectedApplicationName = null;
            _session.New();
            _configDetail = null;
            if (_state.State == ServerRunState.Error)
            {
                _state.DismissError();
            }

            _log.Append(LogLevel.Info, "config", "New configuration.");
            Publish();
        }

        /// <summary>
        /// Saves the current file. A new document asks for a path.
        /// </summary>
        /// <returns>True when the file was written.</returns>
        public bool SaveConfig()
        {
            if (!_session.HasDocument)
            {
                return false;
            }

            ConfigEditResult result = _session.Save();
            if (result.NeedsPath)
            {
                return SaveConfigAs();
            }

            return FinishSave(result);
        }

        /// <summary>
        /// Saves the document to a path the operator chooses.
        /// </summary>
        /// <returns>True when the file was written. False when the operator cancels or the write fails.</returns>
        public bool SaveConfigAs()
        {
            if (!_session.HasDocument)
            {
                return false;
            }

            string suggested = string.IsNullOrEmpty(_session.FilePath) ? ConfigFile.DefaultFileName : _session.DisplayName;
            string? path = _interaction.PickSaveConfigFile(suggested);
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return FinishSave(_session.SaveAs(path));
        }

        /// <summary>Opens the editor for a new application.</summary>
        public void AddApplication()
        {
            if (!_session.HasDocument)
            {
                return;
            }

            OpenApplicationEditor(new ApplicationDraft(), _session.Draft.Applications.Count, true);
        }

        /// <summary>Opens the editor for the selected application.</summary>
        public void EditSelected()
        {
            if (string.IsNullOrEmpty(_selectedApplicationName))
            {
                return;
            }

            EditByName(_selectedApplicationName);
        }

        /// <summary>Opens the editor with a copy of the selected application.</summary>
        public void DuplicateSelected()
        {
            int index = IndexOf(_selectedApplicationName);
            if (index < 0)
            {
                return;
            }

            ApplicationDraft copy = _session.Draft.Applications[index].Clone();
            copy.Name = UniqueName(copy.Name);
            OpenApplicationEditor(copy, _session.Draft.Applications.Count, true);
        }

        /// <summary>Removes the selected application after a confirmation.</summary>
        public void RemoveSelected()
        {
            int index = IndexOf(_selectedApplicationName);
            if (index < 0)
            {
                return;
            }

            string name = _session.Draft.Applications[index].Name;
            if (!_interaction.Confirm("Remove application", "Remove '" + name + "'?", "Remove"))
            {
                return;
            }

            ConfigDraft updated = _session.Draft.Clone();
            updated.Applications.RemoveAt(index);
            ApplyDraft(updated);
            if (string.Equals(_selectedApplicationName, name, StringComparison.Ordinal))
            {
                _selectedApplicationName = null;
            }

            Publish();
        }

        /// <summary>Opens the server settings editor.</summary>
        public void EditServerSettings()
        {
            if (!_session.HasDocument)
            {
                return;
            }

            var editor = new ServerSettingsViewModel(_session.Draft.Clone(), ApplyDraft);
            _interaction.EditServerSettings(editor);
            Publish();
        }

        /// <summary>Loads <paramref name="path"/> and starts the server when the file is valid.</summary>
        /// <param name="path">Configuration path from <c>--config</c>.</param>
        public async Task AutoStartAsync(string path)
        {
            if (LoadConfig(path, true))
            {
                await StartAsync();
            }
        }

        /// <summary>Starts when stopped or in error, and stops when running.</summary>
        public async Task ToggleServerAsync()
        {
            if (_state.State == ServerRunState.Running)
            {
                await StopAsync();
                return;
            }

            await StartAsync();
        }

        /// <summary>Starts the listener. A non-loopback bind requires HTTPS and an API key.</summary>
        public async Task StartAsync()
        {
            ConfigDocument? document = _store.Capture();
            if (document == null || _state.State == ServerRunState.Starting || _state.State == ServerRunState.Running)
            {
                return;
            }

            if (!document.Server.IsLoopback && (!document.Server.EnableHttps || string.IsNullOrEmpty(document.Server.ApiKey)))
            {
                _log.Append(LogLevel.Error, "server", "A network bind requires HTTPS and an API key.");
                return;
            }

            _state.BeginStart();
            _log.Append(LogLevel.Info, "server", "Starting");
            try
            {
                await _host.StartAsync(document.Server);
                _store.MarkBound(document.Server);
                _state.MarkRunning();
                foreach (EndpointAddress endpoint in EndpointFormatter.PublicEndpoints(document.Server))
                {
                    _log.Append(LogLevel.Info, "server", endpoint.Url);
                }
            }
            catch (Exception exception)
            {
                string detail = EndpointFormatter.Scrub(exception.ToString(), document.Server);
                _log.Append(LogLevel.Error, "server", detail);
                _state.MarkError(EndpointFormatter.Scrub(exception.Message, document.Server));
            }
        }

        /// <summary>Releases held buttons, closes the listener, and returns to Stopped.</summary>
        public async Task StopAsync()
        {
            if (_state.State != ServerRunState.Running && !_host.IsRunning)
            {
                return;
            }

            try
            {
                _input.ReleaseHeldButtons();
                await _host.StopAsync();
            }
            catch (Exception exception)
            {
                _log.Append(LogLevel.Error, "server", exception.Message);
            }
            finally
            {
                MarkListenerStopped();
            }
        }

        /// <summary>
        /// Releases held buttons and stops the listener when the window is closing.
        /// The wait uses the task from <see cref="IServerHost.StopAsync"/> and does not pump the dispatcher.
        /// State is updated on the caller after the wait. Close continues if the listener is still stopping after five seconds.
        /// </summary>
        public void StopOnClose()
        {
            if (_state.State != ServerRunState.Running && !_host.IsRunning)
            {
                return;
            }

            try
            {
                _input.ReleaseHeldButtons();
            }
            catch (Exception exception)
            {
                _log.Append(LogLevel.Error, "server", exception.Message);
            }

            string? stopError = WaitForListenerStop();
            if (stopError != null)
            {
                _log.Append(LogLevel.Error, "server", stopError);
            }

            MarkListenerStopped();
        }

        private string? WaitForListenerStop()
        {
            try
            {
                Task stop = _host.StopAsync();
                if (!stop.Wait(McpHost.CloseWaitTimeout))
                {
                    return "The listener did not stop within 5 seconds. The process will still exit.";
                }

                return null;
            }
            catch (AggregateException exception)
            {
                return exception.GetBaseException().Message;
            }
        }

        private void MarkListenerStopped()
        {
            _stats.SetActiveSessions(0);
            _store.MarkUnbound();
            _state.MarkStopped();
            _log.Append(LogLevel.Info, "server", "Stopped");
        }

        /// <summary>True while a background application refresh is in progress.</summary>
        internal bool IsApplicationRefreshRunning
        {
            get { return Volatile.Read(ref _applicationRefreshRunning) == 1; }
        }

        /// <summary>
        /// Rebuilds the application rows from the locator. Safe to call from a timer.
        /// The window search runs off the caller thread. A search that is still running does not start another one.
        /// A request made while it is running is done once afterward, using the document at that time.
        /// </summary>
        public void RefreshApplications()
        {
            Interlocked.Exchange(ref _applicationRefreshPending, 1);
            if (Interlocked.CompareExchange(ref _applicationRefreshRunning, 1, 0) != 0)
            {
                return;
            }

            Task.Run(RefreshApplicationsWorker);
        }

        private void RefreshApplicationsWorker()
        {
            try
            {
                do
                {
                    Interlocked.Exchange(ref _applicationRefreshPending, 0);
                    try
                    {
                        ReplaceApplicationRows(CollectApplicationRows(_store.Capture()));
                    }
                    catch (Exception exception)
                    {
                        _log.Append(LogLevel.Error, "ui", "Application refresh failed: " + exception.Message);
                    }
                }
                while (Volatile.Read(ref _applicationRefreshPending) == 1);
            }
            finally
            {
                Interlocked.Exchange(ref _applicationRefreshRunning, 0);
                if (Volatile.Read(ref _applicationRefreshPending) == 1)
                {
                    RefreshApplications();
                }
            }
        }

        private void ReplaceApplicationRows(List<ApplicationRow> rows)
        {
            _ui.Invoke(() => ReplaceApplications(rows));
        }

        private List<ApplicationRow> CollectApplicationRows(ConfigDocument? document)
        {
            var rows = new List<ApplicationRow>();
            if (document == null)
            {
                return rows;
            }

            foreach (ApplicationEntry application in document.Applications)
            {
                WindowLookup lookup = _locator.Find(application, false);
                bool running = lookup.Found || lookup.ProcessRunning;
                rows.Add(new ApplicationRow
                {
                    Name = application.Name,
                    RunningText = running ? "running" : "stopped",
                    Description = application.Description ?? ""
                });
            }

            return rows;
        }

        /// <summary>Copies the stats snapshot into the bound text.</summary>
        public void RefreshStats()
        {
            StatsSnapshot snapshot = _stats.Snapshot();
            TimeSpan uptime = TimeSpan.Zero;
            if (_state.State == ServerRunState.Running && _state.RunningSince.HasValue)
            {
                uptime = DateTimeOffset.Now - _state.RunningSince.Value;
            }

            UptimeText = StatsText.FormatUptime(uptime);
            RequestsText = snapshot.Requests.ToString();
            ErrorsText = snapshot.Errors.ToString();
            LastLatencyText = snapshot.LastLatencyMilliseconds + " ms";
            AverageLatencyText = Math.Round(snapshot.AverageLatencyMilliseconds).ToString("0") + " ms";
            ScreenshotsText = snapshot.Screenshots.ToString();
            BytesText = StatsText.FormatBytes(snapshot.Bytes);
            InputsText = snapshot.Inputs.ToString();
            SessionsText = snapshot.ActiveSessions.ToString();
            PerToolText = StatsText.FormatPerTool(snapshot.PerTool);
            OnPropertyChanged(nameof(UptimeText));
            OnPropertyChanged(nameof(RequestsText));
            OnPropertyChanged(nameof(ErrorsText));
            OnPropertyChanged(nameof(LastLatencyText));
            OnPropertyChanged(nameof(AverageLatencyText));
            OnPropertyChanged(nameof(ScreenshotsText));
            OnPropertyChanged(nameof(BytesText));
            OnPropertyChanged(nameof(InputsText));
            OnPropertyChanged(nameof(SessionsText));
            OnPropertyChanged(nameof(PerToolText));
        }

        /// <summary>Rebuilds the filtered log from the buffer.</summary>
        public void PullLog()
        {
            IReadOnlyList<LogEntry> snapshot = _log.Snapshot();
            LogRows.Clear();
            foreach (LogEntry entry in snapshot)
            {
                if (PassesFilter(entry.Level))
                {
                    LogRows.Add(new LogRow(entry));
                }
            }

            LogRebuilt?.Invoke();
        }

        /// <summary>True when Start or Stop can be pressed.</summary>
        public bool CanToggleServer()
        {
            if (_state.State == ServerRunState.Starting)
            {
                return false;
            }

            if (_state.State == ServerRunState.Running)
            {
                return true;
            }

            return _store.Capture() != null;
        }

        private void LoadFromDialog()
        {
            if (!ConfirmSaveIfDirty())
            {
                return;
            }

            string? path = _interaction.PickConfigFile();
            if (!string.IsNullOrEmpty(path))
            {
                LoadConfig(path, false);
            }
        }

        private bool ConfirmSaveIfDirty()
        {
            if (!_session.HasDocument || !_session.IsDirty)
            {
                return true;
            }

            SaveChangesChoice choice = _interaction.AskSaveChanges(_session.DisplayName);
            if (choice == SaveChangesChoice.Cancel)
            {
                return false;
            }

            if (choice == SaveChangesChoice.Discard)
            {
                return true;
            }

            return SaveConfig();
        }

        private bool FinishSave(ConfigEditResult result)
        {
            if (!result.Success)
            {
                _interaction.ShowMessage("MCP Overlord", result.Error ?? "The configuration could not be saved.");
                return false;
            }

            _log.Append(LogLevel.Info, "config", "Saved " + _session.FilePath + ".");
            LogWarnings(result.Warnings);
            RememberRecentFile();
            Publish();
            return true;
        }

        private bool FinishSuccessfulLoad(IReadOnlyList<string> warnings)
        {
            _configDetail = null;
            ConfigDocument? document = _store.Document;
            if (document != null)
            {
                _log.Append(
                    LogLevel.Info,
                    "config",
                    "Loaded " + document.SourcePath + " (" + document.Applications.Count + " applications).");
                LogServerLine(document.Server);
            }

            LogWarnings(warnings);
            LogListenerPending();
            RememberRecentFile();
            if (_state.State == ServerRunState.Error)
            {
                _state.DismissError();
            }

            Publish();
            return true;
        }

        private void RememberRecentFile()
        {
            string? path = _session.FilePath;
            if (string.IsNullOrWhiteSpace(path) || _recentFiles == null)
            {
                return;
            }

            try
            {
                _recentFiles.Add(path);
            }
            catch (Exception)
            {
                // The recent list is shell chrome. A failure leaves the saved document in place.
            }
        }

        private void EditApplicationRow(ApplicationRow? row)
        {
            if (row == null)
            {
                return;
            }

            _selectedApplicationName = row.Name;
            OnPropertyChanged(nameof(SelectedApplication));
            RaiseApplicationCommands();
            EditByName(row.Name);
        }

        private void EditByName(string name)
        {
            int index = IndexOf(name);
            if (index < 0)
            {
                return;
            }

            OpenApplicationEditor(_session.Draft.Applications[index].Clone(), index, false);
        }

        private void OpenApplicationEditor(ApplicationDraft application, int index, bool isNew)
        {
            var names = new List<string>();
            for (int itemIndex = 0; itemIndex < _session.Draft.Applications.Count; itemIndex++)
            {
                if (!isNew && itemIndex == index)
                {
                    continue;
                }

                names.Add(_session.Draft.Applications[itemIndex].Name);
            }

            var editor = new ApplicationEditorViewModel(
                _session.Draft.Clone(),
                application,
                index,
                isNew,
                names,
                ApplyDraft);
            _interaction.EditApplication(editor);
            Publish();
        }

        private ConfigEditResult ApplyDraft(ConfigDraft draft)
        {
            bool wasPending = _store.ListenerSettingsPending;
            ConfigEditResult result = _session.TryApply(draft);
            if (!result.Success)
            {
                return result;
            }

            _configDetail = null;
            LogWarnings(result.Warnings);
            if (_store.ListenerSettingsPending && !wasPending)
            {
                _log.Append(
                    LogLevel.Info,
                    "config",
                    "Listener settings take effect on the next start. The running endpoints still use the previous values.");
            }

            if (_state.State == ServerRunState.Error)
            {
                _state.DismissError();
            }

            return result;
        }

        private int IndexOf(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return -1;
            }

            for (int index = 0; index < _session.Draft.Applications.Count; index++)
            {
                if (string.Equals(_session.Draft.Applications[index].Name, name, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        private string UniqueName(string source)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ApplicationDraft application in _session.Draft.Applications)
            {
                names.Add(application.Name);
            }

            int suffix = 2;
            string candidate = source + " (" + suffix + ")";
            while (!names.Add(candidate))
            {
                suffix++;
                candidate = source + " (" + suffix + ")";
            }

            return candidate;
        }

        private bool HasSelectedApplication()
        {
            return IndexOf(_selectedApplicationName) >= 0;
        }

        private void RaiseApplicationCommands()
        {
            _editCommand.RaiseCanExecuteChanged();
            _duplicateCommand.RaiseCanExecuteChanged();
            _removeCommand.RaiseCanExecuteChanged();
        }

        private void LogWarnings(IReadOnlyList<string> warnings)
        {
            foreach (string warning in warnings)
            {
                _log.Append(LogLevel.Warning, "config", warning);
            }
        }

        private void LogListenerPending()
        {
            if (_store.ListenerSettingsPending)
            {
                _log.Append(
                    LogLevel.Info,
                    "config",
                    "Listener settings take effect on the next start. The running endpoints still use the previous values.");
            }
        }

        private void ClearLog()
        {
            _log.Clear();
        }

        private void CopyEndpoint(string? url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return;
            }

            _clipboard.SetText(url);
            _log.Append(LogLevel.Info, "server", "Copied " + url);
        }

        private void QueueLogPull()
        {
            if (Interlocked.Exchange(ref _logPullQueued, 1) == 1)
            {
                return;
            }

            _ui.Invoke(() =>
            {
                Interlocked.Exchange(ref _logPullQueued, 0);
                PullLog();
            });
        }

        private void Publish()
        {
            if (_state.State == ServerRunState.Error)
            {
                StatusKind = "Error";
                StatusText = string.IsNullOrEmpty(_state.ErrorMessage) ? "Error" : "Error: " + _state.ErrorMessage;
            }
            else if (_state.State == ServerRunState.Stopped && !string.IsNullOrEmpty(_configDetail))
            {
                StatusKind = "Config";
                StatusText = _configDetail;
            }
            else
            {
                StatusKind = _state.State.ToString();
                StatusText = _state.State.ToString();
            }

            if (_store.Document != null)
            {
                _log.Retention = _store.Document.Server.LogRetention;
            }

            _configPath = _session.FilePath ?? "";
            if (!_session.HasDocument)
            {
                ConfigPathDisplay = "No configuration loaded";
            }
            else if (string.IsNullOrEmpty(_configPath))
            {
                ConfigPathDisplay = "New configuration";
            }
            else
            {
                ConfigPathDisplay = StatsText.EllipsizeMiddle(_configPath, 72);
            }

            ListenerSettingsPending = _store.ListenerSettingsPending;
            ListenerPendingText = ListenerSettingsPending
                ? "Listener settings take effect on the next start. The running endpoints still use the previous values."
                : "";

            ReplaceEndpoints();
            OnPropertyChanged(nameof(StartButtonText));
            OnPropertyChanged(nameof(ShowStartIcon));
            OnPropertyChanged(nameof(ShowStopIcon));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusKind));
            OnPropertyChanged(nameof(ConfigPath));
            OnPropertyChanged(nameof(ConfigPathDisplay));
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(HasDocument));
            OnPropertyChanged(nameof(SelectedApplication));
            _saveCommand.RaiseCanExecuteChanged();
            _saveAsCommand.RaiseCanExecuteChanged();
            _settingsCommand.RaiseCanExecuteChanged();
            _addCommand.RaiseCanExecuteChanged();
            RaiseApplicationCommands();
            OnPropertyChanged(nameof(ListenerSettingsPending));
            OnPropertyChanged(nameof(ListenerPendingText));
            OnPropertyChanged(nameof(EndpointsAreLive));
            OnPropertyChanged(nameof(EndpointNote));
            _toggleCommand.RaiseCanExecuteChanged();
            RefreshApplications();
            RefreshStats();
        }

        private void ReplaceEndpoints()
        {
            Endpoints.Clear();
            ServerSettings? source = null;
            if (_state.State == ServerRunState.Running && _store.BoundSettings != null)
            {
                source = _store.BoundSettings;
                EndpointsAreLive = true;
                EndpointNote = "";
            }
            else if (_store.Document != null)
            {
                source = _store.Document.Server;
                EndpointsAreLive = false;
                EndpointNote = "not live";
            }
            else
            {
                EndpointsAreLive = false;
                EndpointNote = "";
            }

            if (source == null)
            {
                return;
            }

            foreach (EndpointAddress endpoint in EndpointFormatter.PublicEndpoints(source))
            {
                Endpoints.Add(new EndpointRow
                {
                    Url = endpoint.Url,
                    IsHttps = endpoint.IsHttps
                });
            }
        }

        private void ReplaceApplications(List<ApplicationRow> rows)
        {
            _refreshingApplications = true;
            try
            {
                Applications.Clear();
                foreach (ApplicationRow row in rows)
                {
                    Applications.Add(row);
                }
            }
            finally
            {
                _refreshingApplications = false;
            }

            OnPropertyChanged(nameof(SelectedApplication));
            RaiseApplicationCommands();
        }

        private bool PassesFilter(LogLevel level)
        {
            if (string.Equals(_selectedLevelFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(level.ToString(), _selectedLevelFilter, StringComparison.OrdinalIgnoreCase);
        }

        private void LogServerLine(ServerSettings server)
        {
            string https = server.EnableHttps ? server.HttpsPort.ToString() : "disabled";
            string apiKey = string.IsNullOrEmpty(server.ApiKey) ? "not set" : "set";
            _log.Append(
                LogLevel.Info,
                "config",
                "Server bind " + server.BindAddress + ", HTTP " + server.HttpPort + ", HTTPS " + https + ", API key: " + apiKey + ", log retention " + server.LogRetention + ".");
        }

        private async Task ResumeAfterCancelledRestart(bool wasRunning)
        {
            if (wasRunning)
            {
                await StartAsync();
            }
        }

        private ServerSettings? EndpointSettings()
        {
            if (_state.State == ServerRunState.Running && _store.BoundSettings != null)
            {
                return _store.BoundSettings;
            }

            return _store.Document?.Server;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>Used when a caller does not supply an elevation service. The process is treated as not elevated.</summary>
        private sealed class UnelevatedIntegrity : IIntegrityApi
        {
            /// <inheritdoc />
            public int GetIntegrityLevel(int processId)
            {
                return 0x2000;
            }

            /// <inheritdoc />
            public int GetCurrentIntegrityLevel()
            {
                return 0x2000;
            }
        }
    }

    /// <summary>Command that receives the endpoint URL.</summary>
    public sealed class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;

        /// <summary>
        /// Creates the command.
        /// </summary>
        /// <param name="execute">Action that receives the parameter.</param>
        public RelayCommand(Action<T?> execute)
        {
            _execute = execute;
        }

        /// <inheritdoc />
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        /// <inheritdoc />
        public bool CanExecute(object? parameter)
        {
            return true;
        }

        /// <inheritdoc />
        public void Execute(object? parameter)
        {
            _execute(parameter is T value ? value : default);
        }
    }
}

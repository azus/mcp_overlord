using System.Windows.Threading;
using game_automation;
using mcp_overlord.Configuration;
using mcp_overlord.Hosting;
using mcp_overlord.Logging;
using mcp_overlord.Services;
using mcp_overlord.Services.Native;
using mcp_overlord.Shell;
using mcp_overlord.UI;

namespace mcp_overlord
{
    /// <summary>
    /// Objects the window and the MCP host share. Tests build the same graph with fakes.
    /// </summary>
    public sealed class AppComposition
    {
        private AppComposition(
            ConfigLoader loader,
            ConfigStore store,
            LogBuffer log,
            StatsCounter stats,
            InFlightCounter inFlight,
            WindowLocator locator,
            AppLauncher launcher,
            InputService input,
            ScreenshotScaleStore scales,
            ServerStateMachine state,
            IServerHost host,
            MainViewModel viewModel,
            UiSettingsStore settingsStore,
            WpfUserInteraction? interaction)
        {
            Loader = loader;
            Store = store;
            Log = log;
            Stats = stats;
            InFlight = inFlight;
            Locator = locator;
            Launcher = launcher;
            Input = input;
            Scales = scales;
            State = state;
            Host = host;
            ViewModel = viewModel;
            SettingsStore = settingsStore;
            Interaction = interaction;
        }

        /// <summary>Configuration loader.</summary>
        public ConfigLoader Loader { get; }

        /// <summary>Active document.</summary>
        public ConfigStore Store { get; }

        /// <summary>In-memory log.</summary>
        public LogBuffer Log { get; }

        /// <summary>Counters.</summary>
        public StatsCounter Stats { get; }

        /// <summary>In-flight requests.</summary>
        public InFlightCounter InFlight { get; }

        /// <summary>Window resolver.</summary>
        public WindowLocator Locator { get; }

        /// <summary>Allowlisted process start.</summary>
        public AppLauncher Launcher { get; }

        /// <summary>Mouse and keyboard.</summary>
        public InputService Input { get; }

        /// <summary>Last screenshot scale and region for each application.</summary>
        public ScreenshotScaleStore Scales { get; }

        /// <summary>Listener state.</summary>
        public ServerStateMachine State { get; }

        /// <summary>MCP listener.</summary>
        public IServerHost Host { get; }

        /// <summary>Main window state.</summary>
        public MainViewModel ViewModel { get; }

        /// <summary>Layout file.</summary>
        public UiSettingsStore SettingsStore { get; }

        /// <summary>Dialogs. Null in tests that pass their own interaction.</summary>
        public WpfUserInteraction? Interaction { get; }

        /// <summary>
        /// Creates the composition used by the WPF process.
        /// </summary>
        /// <param name="dispatcher">UI dispatcher.</param>
        /// <returns>The composition. Throws <see cref="SchemaResourceMissingException"/> when the schema is missing.</returns>
        public static AppComposition Create(Dispatcher dispatcher)
        {
            var interaction = new WpfUserInteraction();
            return Create(
                new Win32WindowEnumerator(),
                new Win32ProcessQuery(),
                new Win32ProcessStarter(),
                new Win32WindowApi(),
                new Win32WindowCapture(),
                new Win32KeyboardSender(),
                new Win32FocusApi(),
                new Win32IntegrityApi(),
                new ThreadDelay(),
                new MouseEventClickHelper(),
                new SendInputClickHelper(),
                new Win32WorkstationLock(),
                interaction,
                new WpfClipboard(),
                new DispatcherUiInvoker(dispatcher),
                interaction,
                new ShellRecentFileRegistry());
        }

        /// <summary>
        /// Creates the composition from the platform services. Tests pass fakes here.
        /// </summary>
        public static AppComposition Create(
            IWindowEnumerator windows,
            IProcessQuery processes,
            IProcessStarter starter,
            INativeWindowApi nativeWindows,
            IWindowCapture capture,
            IKeyboardSender keyboard,
            IFocusApi focusApi,
            IIntegrityApi integrityApi,
            IDelay delay,
            ClickHelper mouseEventClick,
            ClickHelper sendInputClick,
            IWorkstationLock workstationLock,
            IUserInteraction interaction,
            IClipboardService clipboard,
            IUiInvoker ui,
            WpfUserInteraction? wpfInteraction,
            IRecentFileRegistry? recentFiles = null)
        {
            var log = new LogBuffer();
            var stats = new StatsCounter();
            var inFlight = new InFlightCounter();
            var loader = new ConfigLoader();
            var store = new ConfigStore();
            var locator = new WindowLocator(windows, processes, nativeWindows, delay, log);
            store.Changed += () => locator.ClearCache();
            var launcher = new AppLauncher(starter, locator, delay);
            var captureService = new CaptureService(capture);
            var scales = new ScreenshotScaleStore();
            var input = new InputService(
                locator,
                new ClickBackends(mouseEventClick, sendInputClick),
                keyboard,
                new FocusHelper(focusApi),
                new IntegrityChecker(integrityApi),
                new InputGate(),
                scales,
                workstationLock);
            var state = new ServerStateMachine();
            var host = new McpHost(store, locator, launcher, captureService, scales, input, workstationLock, log, stats, inFlight);
            var session = new ConfigEditorSession(loader, store, () => state.State == ServerRunState.Running);
            var elevation = new ElevationService(integrityApi);
            var viewModel = new MainViewModel(
                store,
                state,
                host,
                input,
                locator,
                log,
                stats,
                interaction,
                clipboard,
                ui,
                session,
                elevation,
                recentFiles: recentFiles);
            return new AppComposition(loader, store, log, stats, inFlight, locator, launcher, input, scales, state, host, viewModel, new UiSettingsStore(), wpfInteraction);
        }
    }
}

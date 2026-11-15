namespace mcp_overlord.Configuration
{
    /// <summary>
    /// Holds the active configuration snapshot. Tool calls keep the reference they read at the start.
    /// Listener settings that change while the server is running stay pending until the next start.
    /// </summary>
    public sealed class ConfigStore
    {
        private readonly object _gate = new object();
        private ConfigDocument? _document;
        private ServerSettings? _boundSettings;
        private bool _listenerPending;

        /// <summary>Raised after the document, the bound listener, or the pending flag changes.</summary>
        public event Action? Changed;

        /// <summary>The active document, or null when nothing has loaded.</summary>
        public ConfigDocument? Document
        {
            get
            {
                lock (_gate)
                {
                    return _document;
                }
            }
        }

        /// <summary>Listener settings the running server actually bound. Null when stopped.</summary>
        public ServerSettings? BoundSettings
        {
            get
            {
                lock (_gate)
                {
                    return _boundSettings;
                }
            }
        }

        /// <summary>True when a reload changed listener settings that are not yet applied.</summary>
        public bool ListenerSettingsPending
        {
            get
            {
                lock (_gate)
                {
                    return _listenerPending;
                }
            }
        }

        /// <summary>
        /// Returns the document reference a tool call should keep for its whole execution.
        /// </summary>
        /// <returns>The current document, or null.</returns>
        public ConfigDocument? Capture()
        {
            lock (_gate)
            {
                return _document;
            }
        }

        /// <summary>
        /// Replaces the active document. Application changes apply immediately.
        /// Listener changes while <paramref name="serverIsRunning"/> is true are marked pending.
        /// </summary>
        /// <param name="document">The validated document.</param>
        /// <param name="serverIsRunning">True when a listener is currently open.</param>
        public void Apply(ConfigDocument document, bool serverIsRunning)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            lock (_gate)
            {
                _document = document;
                _listenerPending = serverIsRunning
                    && _boundSettings != null
                    && !_boundSettings.HasSameListener(document.Server);
            }

            Changed?.Invoke();
        }

        /// <summary>
        /// Records the settings a successful start bound, and clears the pending flag.
        /// </summary>
        /// <param name="settings">Settings passed to the host.</param>
        public void MarkBound(ServerSettings settings)
        {
            lock (_gate)
            {
                _boundSettings = settings;
                _listenerPending = false;
            }

            Changed?.Invoke();
        }

        /// <summary>
        /// Clears the bound listener after stop. The loaded document stays in place.
        /// </summary>
        public void MarkUnbound()
        {
            lock (_gate)
            {
                _boundSettings = null;
                _listenerPending = false;
            }

            Changed?.Invoke();
        }
    }
}

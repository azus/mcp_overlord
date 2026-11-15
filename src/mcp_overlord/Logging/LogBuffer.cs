namespace mcp_overlord.Logging
{
    /// <summary>
    /// Bounded in-memory log. Producers append from request threads. The oldest entry is dropped past <see cref="Retention"/>.
    /// </summary>
    public sealed class LogBuffer
    {
        /// <summary>How many lines are kept when nothing else is configured.</summary>
        public const int DefaultRetention = 1000;

        /// <summary>Smallest retention that can be set.</summary>
        public const int MinimumRetention = 100;

        private readonly LinkedList<LogEntry> _entries = new LinkedList<LogEntry>();
        private readonly object _gate = new object();
        private int _retention = DefaultRetention;

        /// <summary>Raised after an append or a clear. Handlers must not run UI work inline.</summary>
        public event Action? EntriesAdded;

        /// <summary>
        /// How many lines are kept. A smaller value drops the oldest lines immediately.
        /// Values below <see cref="MinimumRetention"/> are raised to that minimum.
        /// </summary>
        public int Retention
        {
            get
            {
                lock (_gate)
                {
                    return _retention;
                }
            }
            set
            {
                int applied = value < MinimumRetention ? MinimumRetention : value;
                bool dropped = false;
                lock (_gate)
                {
                    if (_retention == applied && _entries.Count <= applied)
                    {
                        return;
                    }

                    _retention = applied;
                    while (_entries.Count > _retention)
                    {
                        _entries.RemoveFirst();
                        dropped = true;
                    }
                }

                if (dropped)
                {
                    EntriesAdded?.Invoke();
                }
            }
        }

        /// <summary>Number of entries currently stored.</summary>
        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _entries.Count;
                }
            }
        }

        /// <summary>
        /// Appends an entry and drops the oldest when the buffer is over capacity.
        /// </summary>
        /// <param name="level">Severity.</param>
        /// <param name="category">Short tag.</param>
        /// <param name="message">Message. Callers must not pass secrets.</param>
        /// <param name="detail">Captured request and tool data. Null for lines that have none.</param>
        public void Append(LogLevel level, string category, string message, LogDetail? detail = null)
        {
            var entry = new LogEntry(DateTimeOffset.Now, level, category ?? "", message ?? "", detail);
            lock (_gate)
            {
                _entries.AddLast(entry);
                while (_entries.Count > _retention)
                {
                    _entries.RemoveFirst();
                }
            }

            EntriesAdded?.Invoke();
        }

        /// <summary>Drops every entry.</summary>
        public void Clear()
        {
            lock (_gate)
            {
                _entries.Clear();
            }

            EntriesAdded?.Invoke();
        }

        /// <summary>
        /// Copies the entries in order, oldest first.
        /// </summary>
        /// <returns>A snapshot the caller can read without holding the lock.</returns>
        public IReadOnlyList<LogEntry> Snapshot()
        {
            lock (_gate)
            {
                var copy = new LogEntry[_entries.Count];
                _entries.CopyTo(copy, 0);
                return copy;
            }
        }
    }
}

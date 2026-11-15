namespace mcp_overlord.Logging
{
    /// <summary>
    /// Numbers the stats panel displays. The snapshot is a copy. The UI never reads the live fields.
    /// </summary>
    /// <param name="Requests">Tool calls accepted since process start.</param>
    /// <param name="Errors">Tool errors plus failed HTTP authentication or origin checks.</param>
    /// <param name="Screenshots">Successful take_screenshot calls.</param>
    /// <param name="Bytes">Sum of encoded image payload sizes.</param>
    /// <param name="Inputs">Mouse and keyboard calls that sent an event.</param>
    /// <param name="ActiveSessions">Requests currently in flight.</param>
    /// <param name="LastLatencyMilliseconds">Duration of the most recent tool call.</param>
    /// <param name="AverageLatencyMilliseconds">Mean duration of tool calls since process start.</param>
    /// <param name="PerTool">Count for each tool that has been called at least once.</param>
    public sealed record StatsSnapshot(
        long Requests,
        long Errors,
        long Screenshots,
        long Bytes,
        long Inputs,
        int ActiveSessions,
        long LastLatencyMilliseconds,
        double AverageLatencyMilliseconds,
        IReadOnlyDictionary<string, long> PerTool);

    /// <summary>
    /// Counters updated from request threads. Counts use <see cref="Interlocked"/>. Latency totals use a lock.
    /// </summary>
    public sealed class StatsCounter
    {
        private long _requests;
        private long _errors;
        private long _screenshots;
        private long _bytes;
        private long _inputs;
        private int _activeSessions;
        private long _lastLatencyMilliseconds;
        private long _totalLatencyMilliseconds;
        private long _latencyCount;
        private readonly object _latencyGate = new object();
        private readonly Dictionary<string, long> _perTool = new Dictionary<string, long>(StringComparer.Ordinal);

        /// <summary>
        /// Records a successful tool call.
        /// </summary>
        /// <param name="toolName">Tool name.</param>
        /// <param name="elapsedMilliseconds">Call duration.</param>
        /// <param name="screenshot">True when the call returned an image.</param>
        /// <param name="bytes">Encoded image size. Zero for other tools.</param>
        /// <param name="input">True when the call sent mouse or keyboard input.</param>
        public void RecordToolSuccess(string toolName, long elapsedMilliseconds, bool screenshot, long bytes, bool input)
        {
            Interlocked.Increment(ref _requests);
            Interlocked.Exchange(ref _lastLatencyMilliseconds, elapsedMilliseconds);
            if (screenshot)
            {
                Interlocked.Increment(ref _screenshots);
            }

            if (bytes > 0)
            {
                Interlocked.Add(ref _bytes, bytes);
            }

            if (input)
            {
                Interlocked.Increment(ref _inputs);
            }

            AddLatency(toolName, elapsedMilliseconds);
        }

        /// <summary>
        /// Records a tool call that returned an error or threw.
        /// </summary>
        /// <param name="toolName">Tool name.</param>
        /// <param name="elapsedMilliseconds">Call duration.</param>
        public void RecordToolError(string toolName, long elapsedMilliseconds)
        {
            Interlocked.Increment(ref _requests);
            Interlocked.Increment(ref _errors);
            Interlocked.Exchange(ref _lastLatencyMilliseconds, elapsedMilliseconds);
            AddLatency(toolName, elapsedMilliseconds);
        }

        /// <summary>Records an HTTP 401 or 403. This is not a tool call.</summary>
        public void RecordHttpFailure()
        {
            Interlocked.Increment(ref _errors);
        }

        /// <summary>
        /// Sets the number of MCP requests currently in flight.
        /// </summary>
        /// <param name="count">In-flight requests.</param>
        public void SetActiveSessions(int count)
        {
            Interlocked.Exchange(ref _activeSessions, count);
        }

        /// <summary>
        /// Copies the current numbers.
        /// </summary>
        /// <returns>A snapshot.</returns>
        public StatsSnapshot Snapshot()
        {
            long requests = Interlocked.Read(ref _requests);
            long errors = Interlocked.Read(ref _errors);
            long screenshots = Interlocked.Read(ref _screenshots);
            long bytes = Interlocked.Read(ref _bytes);
            long inputs = Interlocked.Read(ref _inputs);
            int sessions = Interlocked.CompareExchange(ref _activeSessions, 0, 0);
            long last = Interlocked.Read(ref _lastLatencyMilliseconds);

            lock (_latencyGate)
            {
                double average = _latencyCount == 0 ? 0 : (double)_totalLatencyMilliseconds / _latencyCount;
                return new StatsSnapshot(
                    requests,
                    errors,
                    screenshots,
                    bytes,
                    inputs,
                    sessions,
                    last,
                    average,
                    new Dictionary<string, long>(_perTool));
            }
        }

        private void AddLatency(string toolName, long elapsedMilliseconds)
        {
            lock (_latencyGate)
            {
                _totalLatencyMilliseconds += elapsedMilliseconds;
                _latencyCount++;
                _perTool.TryGetValue(toolName, out long count);
                _perTool[toolName] = count + 1;
            }
        }
    }
}

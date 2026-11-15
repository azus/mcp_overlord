namespace mcp_overlord.Services
{
    /// <summary>
    /// One lock per target window so a click's down/up pair cannot interleave with another client's keys.
    /// Different windows may be driven in parallel.
    /// </summary>
    public sealed class InputGate
    {
        private readonly object _directoryGate = new object();
        private readonly Dictionary<IntPtr, object> _gates = new Dictionary<IntPtr, object>();

        /// <summary>
        /// Takes the lock for <paramref name="hwnd"/> until the returned handle is disposed.
        /// </summary>
        /// <param name="hwnd">Target window.</param>
        /// <returns>A handle that releases the lock.</returns>
        public IDisposable Acquire(IntPtr hwnd)
        {
            object gate = GetGate(hwnd);
            Monitor.Enter(gate);
            return new Releaser(gate);
        }

        private object GetGate(IntPtr hwnd)
        {
            lock (_directoryGate)
            {
                if (!_gates.TryGetValue(hwnd, out object? gate))
                {
                    gate = new object();
                    _gates[hwnd] = gate;
                }

                return gate;
            }
        }

        private sealed class Releaser : IDisposable
        {
            private object? _gate;

            public Releaser(object gate)
            {
                _gate = gate;
            }

            public void Dispose()
            {
                object? gate = Interlocked.Exchange(ref _gate, null);
                if (gate != null)
                {
                    Monitor.Exit(gate);
                }
            }
        }
    }
}

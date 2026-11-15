namespace mcp_overlord.Hosting
{
    /// <summary>Listener lifecycle shown in the window.</summary>
    public enum ServerRunState
    {
        /// <summary>No listener is open.</summary>
        Stopped,

        /// <summary>Start has been requested and the listener is not open yet.</summary>
        Starting,

        /// <summary>The listener is open.</summary>
        Running,

        /// <summary>The last start failed. Start can be tried again.</summary>
        Error
    }

    /// <summary>
    /// Stopped, Starting, Running, and Error. The window reads this on the dispatcher.
    /// </summary>
    public sealed class ServerStateMachine
    {
        /// <summary>Raised after every transition.</summary>
        public event Action? Changed;

        /// <summary>Current state.</summary>
        public ServerRunState State { get; private set; } = ServerRunState.Stopped;

        /// <summary>Exception message when <see cref="State"/> is <see cref="ServerRunState.Error"/>.</summary>
        public string? ErrorMessage { get; private set; }

        /// <summary>When the current Running transition happened. Null otherwise.</summary>
        public DateTimeOffset? RunningSince { get; private set; }

        /// <summary>Move to Starting. Start and Stop stay disabled.</summary>
        public void BeginStart()
        {
            State = ServerRunState.Starting;
            ErrorMessage = null;
            RunningSince = null;
            Changed?.Invoke();
        }

        /// <summary>Move to Running and start the uptime clock.</summary>
        public void MarkRunning()
        {
            State = ServerRunState.Running;
            ErrorMessage = null;
            RunningSince = DateTimeOffset.Now;
            Changed?.Invoke();
        }

        /// <summary>Move to Stopped. Cumulative counters are left alone.</summary>
        public void MarkStopped()
        {
            State = ServerRunState.Stopped;
            ErrorMessage = null;
            RunningSince = null;
            Changed?.Invoke();
        }

        /// <summary>Move to Error. The listener must already be closed.</summary>
        /// <param name="message">Exception message shown in the status text.</param>
        public void MarkError(string message)
        {
            State = ServerRunState.Error;
            ErrorMessage = message;
            RunningSince = null;
            Changed?.Invoke();
        }

        /// <summary>Leave Error and return to Stopped.</summary>
        public void DismissError()
        {
            if (State == ServerRunState.Error)
            {
                MarkStopped();
            }
        }
    }
}

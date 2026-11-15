using game_automation;
using mcp_overlord.Configuration;

namespace mcp_overlord.Services
{
    /// <summary>
    /// The two mouse backends. A gesture keeps the instance it started with.
    /// </summary>
    public sealed class ClickBackends
    {
        private readonly ClickHelper _mouseEvent;
        private readonly ClickHelper _sendInput;

        /// <summary>
        /// Creates the registry.
        /// </summary>
        /// <param name="mouseEvent">Default backend.</param>
        /// <param name="sendInput">Backend used when an entry sets <c>sendInput</c>.</param>
        public ClickBackends(ClickHelper mouseEvent, ClickHelper sendInput)
        {
            _mouseEvent = mouseEvent;
            _sendInput = sendInput;
        }

        /// <summary>
        /// Selects the backend for an application entry.
        /// </summary>
        /// <param name="backend">Value from the snapshot.</param>
        /// <returns>The helper to use for this gesture.</returns>
        public ClickHelper Resolve(ClickBackend backend)
        {
            if (backend == ClickBackend.SendInput)
            {
                return _sendInput;
            }

            return _mouseEvent;
        }
    }
}

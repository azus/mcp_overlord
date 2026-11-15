using mcp_overlord.Configuration;

namespace mcp_overlord.Services
{
    /// <summary>Result of <c>launch_application</c>.</summary>
    public sealed class LaunchOutcome
    {
        /// <summary>Tool error text, or null when the process was started.</summary>
        public string? Error { get; init; }

        /// <summary>True when a child process was created.</summary>
        public bool Started { get; init; }

        /// <summary>Child process id. This is the process that was started, not necessarily the window owner.</summary>
        public int ProcessId { get; init; }

        /// <summary>True when a window matched before the wait elapsed.</summary>
        public bool WindowFound { get; init; }

        /// <summary>Wait that was applied after clamping to 0–120 seconds.</summary>
        public double AppliedWaitSeconds { get; init; }

        /// <summary>True when the requested wait was outside 0–120.</summary>
        public bool WaitWasClamped { get; init; }
    }

    /// <summary>
    /// Starts the executable stored on a configured application and optionally waits for its window.
    /// </summary>
    public sealed class AppLauncher
    {
        private readonly IProcessStarter _starter;
        private readonly WindowLocator _locator;
        private readonly IDelay _delay;

        /// <summary>
        /// Creates the launcher.
        /// </summary>
        /// <param name="starter">Process start. Failures throw.</param>
        /// <param name="locator">Used while waiting for a window.</param>
        /// <param name="delay">Wait between polls. About four polls a second.</param>
        public AppLauncher(IProcessStarter starter, WindowLocator locator, IDelay delay)
        {
            _starter = starter;
            _locator = locator;
            _delay = delay;
        }

        /// <summary>
        /// Starts <paramref name="application"/> when it has an executable.
        /// </summary>
        /// <param name="application">Configured application from the call snapshot.</param>
        /// <param name="waitForWindowSeconds">How long to poll for a window. Clamped to 0–120.</param>
        /// <returns>The launch outcome. <see cref="LaunchOutcome.Error"/> is set when the start fails.</returns>
        public LaunchOutcome Launch(ApplicationEntry application, double waitForWindowSeconds)
        {
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            if (!application.IsLaunchable || string.IsNullOrEmpty(application.Executable))
            {
                return new LaunchOutcome
                {
                    Error = ToolErrors.NotLaunchable(application.Name)
                };
            }

            double appliedWait = ClampWait(waitForWindowSeconds, out bool waitWasClamped);
            try
            {
                int processId = _starter.Start(new ProcessStartRequest
                {
                    FileName = application.Executable,
                    Arguments = application.Parameters ?? "",
                    WorkingDirectory = application.WorkingDirectory
                });

                bool windowFound = WaitForWindow(application, appliedWait);
                return new LaunchOutcome
                {
                    Started = true,
                    ProcessId = processId,
                    WindowFound = windowFound,
                    AppliedWaitSeconds = appliedWait,
                    WaitWasClamped = waitWasClamped
                };
            }
            catch (Exception exception)
            {
                return new LaunchOutcome
                {
                    Error = ToolErrors.StartFailed(application.Name, exception.Message),
                    AppliedWaitSeconds = appliedWait,
                    WaitWasClamped = waitWasClamped
                };
            }
        }

        /// <summary>
        /// Clamps a wait to 0–120 seconds. Non-finite values become 0.
        /// </summary>
        /// <param name="seconds">Requested wait.</param>
        /// <param name="wasClamped">True when the value changed.</param>
        /// <returns>The applied wait.</returns>
        public static double ClampWait(double seconds, out bool wasClamped)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                wasClamped = true;
                return 0;
            }

            double clamped = Math.Clamp(seconds, 0, 120);
            wasClamped = clamped != seconds;
            return clamped;
        }

        private bool WaitForWindow(ApplicationEntry application, double seconds)
        {
            if (seconds <= 0)
            {
                return false;
            }

            var target = TimeSpan.FromSeconds(seconds);
            var waited = TimeSpan.Zero;
            var slice = TimeSpan.FromMilliseconds(250);
            while (true)
            {
                if (_locator.Find(application, false).Found)
                {
                    return true;
                }

                if (waited >= target)
                {
                    return false;
                }

                TimeSpan step = slice;
                if (waited + step > target)
                {
                    step = target - waited;
                }

                _delay.Wait(step);
                waited += step;
            }
        }
    }
}

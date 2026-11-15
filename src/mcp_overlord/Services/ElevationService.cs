using System.ComponentModel;
using System.Diagnostics;

namespace mcp_overlord.Services
{
    /// <summary>
    /// Reports whether this process is elevated and can start a new elevated instance.
    /// </summary>
    public interface IElevationService
    {
        /// <summary>True when this process is running at high integrity or above.</summary>
        bool IsElevated { get; }

        /// <summary>
        /// Starts this executable with the <c>runas</c> verb.
        /// </summary>
        /// <param name="configPath">Configuration file passed to the new process, or null.</param>
        /// <returns>False when the user cancels the elevation prompt.</returns>
        bool RestartElevated(string? configPath);
    }

    /// <summary>
    /// Elevation through the process integrity level and <c>Process.Start</c> with <c>runas</c>.
    /// </summary>
    public sealed class ElevationService : IElevationService
    {
        /// <summary>High integrity RID. The same boundary the elevated file drop uses.</summary>
        public const int HighIntegrityLevel = 0x3000;

        /// <summary>Win32 <c>ERROR_CANCELLED</c>. The user dismissed the elevation prompt.</summary>
        public const int ElevationCancelled = 1223;

        private readonly IIntegrityApi _integrity;

        /// <summary>
        /// Creates the service.
        /// </summary>
        /// <param name="integrity">Reads this process integrity level.</param>
        public ElevationService(IIntegrityApi integrity)
        {
            _integrity = integrity ?? throw new ArgumentNullException(nameof(integrity));
        }

        /// <inheritdoc />
        public bool IsElevated
        {
            get { return _integrity.GetCurrentIntegrityLevel() >= HighIntegrityLevel; }
        }

        /// <inheritdoc />
        public bool RestartElevated(string? configPath)
        {
            string? executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
            {
                throw new InvalidOperationException("The process path is not available.");
            }

            var start = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true,
                Verb = "runas"
            };
            if (!string.IsNullOrWhiteSpace(configPath))
            {
                start.ArgumentList.Add(configPath);
            }

            try
            {
                using Process? process = Process.Start(start);
                if (process == null)
                {
                    throw new InvalidOperationException("Windows did not start the elevated process.");
                }

                return true;
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == ElevationCancelled)
            {
                return false;
            }
        }
    }
}

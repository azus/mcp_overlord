namespace mcp_overlord.UI.Placement
{
    /// <summary>
    /// Supplies the monitors that are attached and receiving a desktop.
    /// </summary>
    public interface IMonitorLayout
    {
        /// <summary>
        /// Returns the attached monitors. An empty list means the layout could not be read.
        /// </summary>
        /// <returns>The monitors, in enumeration order.</returns>
        IReadOnlyList<MonitorInfo> GetMonitors();
    }
}

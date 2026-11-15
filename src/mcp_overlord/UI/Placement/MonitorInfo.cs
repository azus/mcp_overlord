namespace mcp_overlord.UI.Placement
{
    /// <summary>
    /// One attached monitor: its full bounds, the work area that excludes the taskbar, and its DPI.
    /// A monitor that is off or disconnected is not enumerated by Windows and therefore never appears here.
    /// </summary>
    public sealed class MonitorInfo
    {
        /// <summary>
        /// Creates a monitor description.
        /// </summary>
        /// <param name="bounds">Full monitor rectangle, in physical pixels.</param>
        /// <param name="workArea">Work area, in physical pixels. The taskbar is excluded.</param>
        /// <param name="dpi">Effective horizontal DPI. Values below 96 are treated as 96.</param>
        /// <param name="isPrimary">True for the primary monitor.</param>
        public MonitorInfo(PixelRect bounds, PixelRect workArea, int dpi, bool isPrimary)
        {
            Bounds = bounds;
            WorkArea = workArea;
            Dpi = dpi >= 96 ? dpi : 96;
            IsPrimary = isPrimary;
        }

        /// <summary>Full monitor rectangle, in physical pixels.</summary>
        public PixelRect Bounds { get; }

        /// <summary>Work area, in physical pixels. The taskbar is excluded.</summary>
        public PixelRect WorkArea { get; }

        /// <summary>Effective DPI of the monitor.</summary>
        public int Dpi { get; }

        /// <summary>True for the primary monitor.</summary>
        public bool IsPrimary { get; }
    }
}

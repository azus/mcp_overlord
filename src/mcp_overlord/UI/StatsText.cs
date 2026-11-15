using System.Globalization;
using mcp_overlord.Logging;

namespace mcp_overlord.UI
{
    /// <summary>Formats stats for the window.</summary>
    public static class StatsText
    {
        /// <summary>
        /// Formats a byte count. Values of a megabyte or more use one decimal megabyte.
        /// </summary>
        /// <param name="bytes">Byte count.</param>
        /// <returns>The display text.</returns>
        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes + " B";
            }

            if (bytes < 1024 * 1024)
            {
                return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
            }

            return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        /// <summary>
        /// Formats an uptime as <c>hh:mm:ss</c>.
        /// </summary>
        /// <param name="uptime">Elapsed time.</param>
        /// <returns>The display text.</returns>
        public static string FormatUptime(TimeSpan uptime)
        {
            if (uptime < TimeSpan.Zero)
            {
                uptime = TimeSpan.Zero;
            }

            return ((int)uptime.TotalHours).ToString("00") + ":" + uptime.Minutes.ToString("00") + ":" + uptime.Seconds.ToString("00");
        }

        /// <summary>
        /// Formats per-tool counts in name order.
        /// </summary>
        /// <param name="counts">Tool name to count.</param>
        /// <returns>One line, or an empty string.</returns>
        public static string FormatPerTool(IReadOnlyDictionary<string, long> counts)
        {
            if (counts == null || counts.Count == 0)
            {
                return "";
            }

            var names = new List<string>(counts.Keys);
            names.Sort(StringComparer.Ordinal);
            var parts = new List<string>(names.Count);
            foreach (string name in names)
            {
                parts.Add(name + " " + counts[name]);
            }

            return string.Join("   ", parts);
        }

        /// <summary>
        /// Shortens a path in the middle.
        /// </summary>
        /// <param name="value">Full path.</param>
        /// <param name="maxLength">Maximum characters.</param>
        /// <returns>The path, or the original when it already fits.</returns>
        public static string EllipsizeMiddle(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength || maxLength < 5)
            {
                return value ?? "";
            }

            int keep = (maxLength - 3) / 2;
            return value.Substring(0, keep) + "..." + value.Substring(value.Length - keep);
        }
    }
}

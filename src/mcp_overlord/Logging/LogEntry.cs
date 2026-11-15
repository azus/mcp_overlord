namespace mcp_overlord.Logging
{
    /// <summary>Severity of a log entry.</summary>
    public enum LogLevel
    {
        /// <summary>Normal operation.</summary>
        Info,

        /// <summary>Something unexpected that did not fail the call.</summary>
        Warning,

        /// <summary>A failed call or a failed start.</summary>
        Error
    }

    /// <summary>
    /// One in-memory log line. A file sink can subscribe to the same shape later.
    /// </summary>
    /// <param name="Timestamp">Local time the entry was recorded.</param>
    /// <param name="Level">Severity.</param>
    /// <param name="Category">Short tag such as server, config, mcp, launch, capture, or input.</param>
    /// <param name="Message">Text shown in the window. Never includes an API key or certificate password.</param>
    /// <param name="Detail">Captured request, tool call, and images. Null for lines that are not tied to a request.</param>
    public sealed record LogEntry(
        DateTimeOffset Timestamp,
        LogLevel Level,
        string Category,
        string Message,
        LogDetail? Detail = null);
}

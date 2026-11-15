namespace mcp_overlord.Logging
{
    /// <summary>One image returned by a tool call, kept decoded so the log panel can show it.</summary>
    public sealed class LoggedImage
    {
        /// <summary>MIME type, such as <c>image/png</c>.</summary>
        public string MimeType { get; init; } = "image/png";

        /// <summary>Decoded image bytes.</summary>
        public byte[] Bytes { get; init; } = Array.Empty<byte>();

        /// <summary>Width reported with the image. Zero when the call did not say.</summary>
        public int Width { get; init; }

        /// <summary>Height reported with the image. Zero when the call did not say.</summary>
        public int Height { get; init; }
    }

    /// <summary>
    /// Extra data for one log line: the HTTP exchange, the tool call, and any images.
    /// Lines that are not tied to a request leave this null.
    /// </summary>
    public sealed class LogDetail
    {
        /// <summary>The HTTP request this line belongs to. Response fields fill in when the request ends.</summary>
        public HttpExchange? Exchange { get; init; }

        /// <summary>Tool name, when this line is a tool call.</summary>
        public string? ToolName { get; init; }

        /// <summary>Tool arguments as JSON. Null when the call had none.</summary>
        public string? ToolArgumentsJson { get; init; }

        /// <summary>Tool result as JSON, without embedded image bytes. Null on the request line.</summary>
        public string? ToolResultJson { get; init; }

        /// <summary>Images taken from the tool result.</summary>
        public IReadOnlyList<LoggedImage> Images { get; init; } = Array.Empty<LoggedImage>();
    }
}

namespace mcp_overlord.Logging
{
    /// <summary>One request or response header. Secret values are already replaced.</summary>
    public sealed class HttpHeaderValue
    {
        /// <summary>Header name.</summary>
        public string Name { get; init; } = "";

        /// <summary>Header value. An API key or Authorization value is <c>***</c>.</summary>
        public string Value { get; init; } = "";
    }

    /// <summary>
    /// One HTTP request captured for a log line.
    /// Response fields are filled when the response finishes. A log line keeps this instance, so a later read sees the body.
    /// </summary>
    public sealed class HttpExchange
    {
        /// <summary>Key used in <c>HttpContext.Items</c>.</summary>
        public const string ItemKey = "mcp_overlord.HttpExchange";

        /// <summary>Connection identifier from the server.</summary>
        public string ConnectionId { get; set; } = "";

        /// <summary>Client address.</summary>
        public string RemoteAddress { get; set; } = "";

        /// <summary>Client port.</summary>
        public int RemotePort { get; set; }

        /// <summary>Address this process accepted the connection on.</summary>
        public string LocalAddress { get; set; } = "";

        /// <summary>Local port.</summary>
        public int LocalPort { get; set; }

        /// <summary><c>http</c> or <c>https</c>.</summary>
        public string Scheme { get; set; } = "";

        /// <summary>HTTP method.</summary>
        public string Method { get; set; } = "";

        /// <summary>Path and query.</summary>
        public string Path { get; set; } = "";

        /// <summary>Request headers, with secrets removed.</summary>
        public IReadOnlyList<HttpHeaderValue> RequestHeaders { get; set; } = Array.Empty<HttpHeaderValue>();

        /// <summary>Raw request body.</summary>
        public byte[] RequestBody { get; set; } = Array.Empty<byte>();

        /// <summary>Response status. Null until the pipeline sets one.</summary>
        public int? ResponseStatus { get; set; }

        /// <summary>Response headers, with secrets removed.</summary>
        public IReadOnlyList<HttpHeaderValue> ResponseHeaders { get; set; } = Array.Empty<HttpHeaderValue>();

        /// <summary>Raw response body. Null until the response is finished and the body was stored.</summary>
        public byte[]? ResponseBody { get; set; }

        /// <summary>True when the body was skipped, such as a long-lived GET event stream.</summary>
        public bool ResponseBodyOmitted { get; set; }

        /// <summary>When the request was first seen.</summary>
        public DateTimeOffset Started { get; set; }

        /// <summary>How long the request stayed in the pipeline. Null until it finishes.</summary>
        public TimeSpan? Duration { get; set; }
    }

    /// <summary>The exchange for the request currently running on this async flow.</summary>
    public static class HttpExchangeAmbient
    {
        private static readonly AsyncLocal<HttpExchange?> Current = new AsyncLocal<HttpExchange?>();

        /// <summary>The exchange set by the capture middleware, or null.</summary>
        public static HttpExchange? Exchange
        {
            get { return Current.Value; }
            set { Current.Value = value; }
        }
    }
}

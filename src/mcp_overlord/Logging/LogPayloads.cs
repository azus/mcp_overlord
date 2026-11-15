using System.Text;

namespace mcp_overlord.Logging
{
    /// <summary>The texts and byte views the log detail panel shows for one line.</summary>
    public sealed class LogPayloadView
    {
        /// <summary>True when a line is selected.</summary>
        public bool HasRow { get; init; }

        /// <summary>True when a connection was captured.</summary>
        public bool HasNetwork { get; init; }

        /// <summary>Shown when the line has no connection. Empty otherwise.</summary>
        public string NetworkNote { get; init; } = "";

        /// <summary>Label and value rows for the summary.</summary>
        public IReadOnlyList<LogPayloadField> Fields { get; init; } = Array.Empty<LogPayloadField>();

        /// <summary>Request headers, one per line.</summary>
        public string RequestHeaders { get; init; } = "";

        /// <summary>Response headers, one per line.</summary>
        public string ResponseHeaders { get; init; } = "";

        /// <summary>Note above the request body.</summary>
        public string RequestNote { get; init; } = "";

        /// <summary>Note above the response body.</summary>
        public string ResponseNote { get; init; } = "";

        /// <summary>Pretty JSON for the request body.</summary>
        public string RequestJson { get; init; } = "";

        /// <summary>Pretty JSON for the response body.</summary>
        public string ResponseJson { get; init; } = "";

        /// <summary>Hex lines for the request body.</summary>
        public ComputedLineList RequestHexLines { get; init; } = new ComputedLineList(0, _ => "");

        /// <summary>Hex lines for the response body.</summary>
        public ComputedLineList ResponseHexLines { get; init; } = new ComputedLineList(0, _ => "");

        /// <summary>Text lines for the request body.</summary>
        public ComputedLineList RequestTextLines { get; init; } = new ComputedLineList(0, _ => "");

        /// <summary>Text lines for the response body.</summary>
        public ComputedLineList ResponseTextLines { get; init; } = new ComputedLineList(0, _ => "");

        /// <summary>Images on the line.</summary>
        public IReadOnlyList<LoggedImage> Images { get; init; } = Array.Empty<LoggedImage>();
    }

    /// <summary>One summary row.</summary>
    public sealed class LogPayloadField
    {
        /// <summary>Field name.</summary>
        public string Label { get; init; } = "";

        /// <summary>Field value.</summary>
        public string Value { get; init; } = "";
    }

    /// <summary>Builds a <see cref="LogPayloadView"/> from a log line.</summary>
    public static class LogPayloads
    {
        /// <summary>
        /// Describes <paramref name="entry"/>. A null entry is the empty selection.
        /// </summary>
        /// <param name="entry">Log line, or null when nothing is selected.</param>
        /// <returns>The view.</returns>
        public static LogPayloadView Describe(LogEntry? entry)
        {
            if (entry == null)
            {
                return new LogPayloadView();
            }

            LogDetail? detail = entry.Detail;
            HttpExchange? exchange = detail?.Exchange;
            bool hasNetwork = HasNetwork(exchange);
            Body request = RequestBody(exchange, detail);
            Body response = ResponseBody(exchange, detail);
            return new LogPayloadView
            {
                HasRow = true,
                HasNetwork = hasNetwork,
                NetworkNote = hasNetwork ? "" : "This line has no captured connection.",
                Fields = Fields(entry, exchange, hasNetwork),
                RequestHeaders = Headers(exchange?.RequestHeaders),
                ResponseHeaders = Headers(exchange?.ResponseHeaders),
                RequestNote = request.Note,
                ResponseNote = response.Note,
                RequestJson = JsonFormatter.Format(request.Bytes),
                ResponseJson = JsonFormatter.Format(response.Bytes),
                RequestHexLines = HexDumpFormatter.Lines(request.Bytes),
                ResponseHexLines = HexDumpFormatter.Lines(response.Bytes),
                RequestTextLines = TextLineList.FromBytes(request.Bytes),
                ResponseTextLines = TextLineList.FromBytes(response.Bytes),
                Images = detail?.Images ?? Array.Empty<LoggedImage>()
            };
        }

        private static bool HasNetwork(HttpExchange? exchange)
        {
            if (exchange == null)
            {
                return false;
            }

            return !string.IsNullOrEmpty(exchange.Method)
                || !string.IsNullOrEmpty(exchange.RemoteAddress)
                || !string.IsNullOrEmpty(exchange.LocalAddress);
        }

        private static IReadOnlyList<LogPayloadField> Fields(LogEntry entry, HttpExchange? exchange, bool hasNetwork)
        {
            var fields = new List<LogPayloadField>
            {
                Field("Time", entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff")),
                Field("Level", entry.Level.ToString()),
                Field("Category", entry.Category),
                Field("Message", entry.Message)
            };
            if (!hasNetwork || exchange == null)
            {
                return fields;
            }

            fields.Add(Field("Source", Endpoint(exchange.RemoteAddress, exchange.RemotePort)));
            fields.Add(Field("Destination", Endpoint(exchange.LocalAddress, exchange.LocalPort)));
            fields.Add(Field("Scheme", exchange.Scheme));
            fields.Add(Field("Method", exchange.Method));
            fields.Add(Field("Path", exchange.Path));
            fields.Add(Field("Status", exchange.ResponseStatus?.ToString() ?? ""));
            fields.Add(Field("Duration", Duration(exchange.Duration)));
            if (!string.IsNullOrEmpty(exchange.ConnectionId))
            {
                fields.Add(Field("Connection", exchange.ConnectionId));
            }

            return fields;
        }

        private static Body RequestBody(HttpExchange? exchange, LogDetail? detail)
        {
            if (exchange != null && exchange.RequestBody.Length > 0)
            {
                return new Body(exchange.RequestBody, "");
            }

            if (!string.IsNullOrEmpty(detail?.ToolArgumentsJson))
            {
                return new Body(
                    Encoding.UTF8.GetBytes(detail.ToolArgumentsJson),
                    "No HTTP request body was captured. Showing the tool arguments.");
            }

            return new Body(Array.Empty<byte>(), "");
        }

        private static Body ResponseBody(HttpExchange? exchange, LogDetail? detail)
        {
            if (exchange != null && exchange.ResponseBodyOmitted)
            {
                return Fallback(detail, "The response body was not stored because this request is a long-lived event stream.");
            }

            if (exchange?.ResponseBody == null)
            {
                return Fallback(detail, "The HTTP response was not captured.");
            }

            if (exchange.ResponseBody.Length > 0)
            {
                return new Body(exchange.ResponseBody, "");
            }

            return Fallback(detail, "The HTTP response body is empty.");
        }

        private static Body Fallback(LogDetail? detail, string note)
        {
            if (!string.IsNullOrEmpty(detail?.ToolResultJson))
            {
                return new Body(Encoding.UTF8.GetBytes(detail.ToolResultJson), note + " Showing the tool result.");
            }

            return new Body(Array.Empty<byte>(), note);
        }

        private static LogPayloadField Field(string label, string? value)
        {
            return new LogPayloadField
            {
                Label = label,
                Value = value ?? ""
            };
        }

        private static string Endpoint(string address, int port)
        {
            if (string.IsNullOrEmpty(address) && port == 0)
            {
                return "";
            }

            string host = address.Contains(':') ? "[" + address + "]" : address;
            return host + ":" + port;
        }

        private static string Duration(TimeSpan? duration)
        {
            if (duration == null)
            {
                return "";
            }

            return duration.Value.TotalMilliseconds.ToString("0") + " ms";
        }

        private static string Headers(IReadOnlyList<HttpHeaderValue>? headers)
        {
            if (headers == null || headers.Count == 0)
            {
                return "";
            }

            var builder = new StringBuilder();
            foreach (HttpHeaderValue header in headers)
            {
                builder.Append(header.Name);
                builder.Append(": ");
                builder.AppendLine(header.Value);
            }

            return builder.ToString().TrimEnd();
        }

        private readonly struct Body
        {
            public Body(byte[] bytes, string note)
            {
                Bytes = bytes;
                Note = note;
            }

            public byte[] Bytes { get; }

            public string Note { get; }
        }
    }
}

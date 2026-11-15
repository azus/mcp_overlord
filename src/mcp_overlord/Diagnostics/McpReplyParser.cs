using System.Text.Json;

namespace mcp_overlord.Diagnostics
{
    /// <summary>The fields the connection check reads from one JSON-RPC message.</summary>
    public sealed class McpReply
    {
        /// <summary>True when a JSON-RPC result or error was found.</summary>
        public bool Parsed { get; init; }

        /// <summary>True when the message is an error.</summary>
        public bool HasError { get; init; }

        /// <summary>Error message.</summary>
        public string Error { get; init; } = "";

        /// <summary>Negotiated protocol version.</summary>
        public string ProtocolVersion { get; init; } = "";

        /// <summary>Server name from <c>serverInfo</c>.</summary>
        public string ServerName { get; init; } = "";

        /// <summary>Server version from <c>serverInfo</c>.</summary>
        public string ServerVersion { get; init; } = "";

        /// <summary>Number of tools. -1 when the message has no tool list.</summary>
        public int ToolCount { get; init; } = -1;

        /// <summary>The first tool names, comma separated.</summary>
        public string ToolNames { get; init; } = "";
    }

    /// <summary>Reads a JSON-RPC body, including a server-sent event stream.</summary>
    public static class McpReplyParser
    {
        /// <summary>
        /// Parses <paramref name="body"/>. A stream of <c>data:</c> lines is searched for the first result or error.
        /// </summary>
        /// <param name="body">Response text.</param>
        /// <returns>The reply. <see cref="McpReply.Parsed"/> is false when nothing matched.</returns>
        public static McpReply Parse(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return new McpReply();
            }

            foreach (string candidate in Candidates(body))
            {
                McpReply? reply = TryParse(candidate);
                if (reply != null)
                {
                    return reply;
                }
            }

            return new McpReply();
        }

        private static IEnumerable<string> Candidates(string body)
        {
            string trimmed = body.Trim();
            if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            {
                yield return trimmed;
            }

            var data = new System.Text.StringBuilder();
            foreach (string raw in body.Split('\n'))
            {
                string line = raw.EndsWith('\r') ? raw.Substring(0, raw.Length - 1) : raw;
                if (line.Length == 0)
                {
                    if (data.Length > 0)
                    {
                        yield return data.ToString();
                        data.Clear();
                    }

                    continue;
                }

                if (!line.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }

                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                data.Append(line.Substring("data:".Length).TrimStart());
            }

            if (data.Length > 0)
            {
                yield return data.ToString();
            }
        }

        private static McpReply? TryParse(string json)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                if (root.TryGetProperty("error", out JsonElement error))
                {
                    return ErrorReply(error);
                }

                if (!root.TryGetProperty("result", out JsonElement result))
                {
                    return null;
                }

                return ResultReply(result);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static McpReply ErrorReply(JsonElement error)
        {
            string message = "";
            if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out JsonElement messageElement))
            {
                message = messageElement.GetString() ?? "";
            }

            if (string.IsNullOrEmpty(message))
            {
                message = error.ToString();
            }

            return new McpReply
            {
                Parsed = true,
                HasError = true,
                Error = message
            };
        }

        private static McpReply ResultReply(JsonElement result)
        {
            string protocol = ReadString(result, "protocolVersion");
            string name = "";
            string version = "";
            if (result.TryGetProperty("serverInfo", out JsonElement info) && info.ValueKind == JsonValueKind.Object)
            {
                name = ReadString(info, "name");
                version = ReadString(info, "version");
            }

            int toolCount = -1;
            string toolNames = "";
            if (result.TryGetProperty("tools", out JsonElement tools) && tools.ValueKind == JsonValueKind.Array)
            {
                toolCount = tools.GetArrayLength();
                toolNames = ToolNames(tools);
            }

            return new McpReply
            {
                Parsed = true,
                ProtocolVersion = protocol,
                ServerName = name,
                ServerVersion = version,
                ToolCount = toolCount,
                ToolNames = toolNames
            };
        }

        private static string ToolNames(JsonElement tools)
        {
            var names = new List<string>();
            foreach (JsonElement tool in tools.EnumerateArray())
            {
                if (names.Count == 8)
                {
                    names.Add("...");
                    break;
                }

                if (tool.ValueKind == JsonValueKind.Object && tool.TryGetProperty("name", out JsonElement name))
                {
                    string? text = name.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        names.Add(text);
                    }
                }
            }

            return string.Join(", ", names);
        }

        private static string ReadString(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement property))
            {
                return "";
            }

            return property.GetString() ?? "";
        }
    }
}

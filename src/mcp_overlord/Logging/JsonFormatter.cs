using System.Text;
using System.Text.Json;

namespace mcp_overlord.Logging
{
    /// <summary>
    /// Pretty-prints JSON and server-sent events.
    /// A base64 string longer than <see cref="Base64ElideLength"/> is replaced with a short note.
    /// </summary>
    public static class JsonFormatter
    {
        /// <summary>Base64 strings longer than this are not written out.</summary>
        public const int Base64ElideLength = 1024;

        /// <summary>Formatted text is cut here so one payload cannot freeze the text box.</summary>
        public const int MaximumFormattedLength = 2_000_000;

        private static readonly JsonWriterOptions WriterOptions = new JsonWriterOptions
        {
            Indented = true
        };

        /// <summary>
        /// Formats UTF-8 bytes. Server-sent <c>data:</c> frames are parsed one event at a time.
        /// </summary>
        /// <param name="bytes">Body. Null or empty returns an empty string.</param>
        /// <returns>Formatted text, or a note when the body is not JSON.</returns>
        public static string Format(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return "";
            }

            return Format(Encoding.UTF8.GetString(bytes));
        }

        /// <summary>
        /// Formats text that is JSON or a server-sent event stream.
        /// </summary>
        /// <param name="text">Body text.</param>
        /// <returns>Formatted text.</returns>
        public static string Format(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            string formatted = LooksLikeEventStream(text) ? FormatEventStream(text) : FormatDocument(text);
            if (formatted.Length <= MaximumFormattedLength)
            {
                return formatted;
            }

            return formatted.Substring(0, MaximumFormattedLength)
                + Environment.NewLine
                + "[Formatted JSON was cut at 2 MB. The Hex view has the full body.]";
        }

        private static bool LooksLikeEventStream(string text)
        {
            return text.Contains("\ndata:", StringComparison.Ordinal) || text.StartsWith("data:", StringComparison.Ordinal);
        }

        private static string FormatEventStream(string text)
        {
            var builder = new StringBuilder();
            var data = new StringBuilder();
            string[] lines = text.Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.EndsWith('\r') ? raw.Substring(0, raw.Length - 1) : raw;
                if (line.Length == 0)
                {
                    FlushEvent(builder, data);
                    continue;
                }

                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    if (data.Length > 0)
                    {
                        data.Append('\n');
                    }

                    data.Append(line.Substring("data:".Length).TrimStart());
                    continue;
                }

                builder.AppendLine(line);
            }

            FlushEvent(builder, data);
            return builder.ToString().TrimEnd();
        }

        private static void FlushEvent(StringBuilder builder, StringBuilder data)
        {
            if (data.Length == 0)
            {
                return;
            }

            string payload = data.ToString();
            data.Clear();
            builder.AppendLine(FormatDocument(payload));
        }

        private static string FormatDocument(string text)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(text);
                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream, WriterOptions))
                {
                    WriteElement(writer, document.RootElement);
                }

                return Encoding.UTF8.GetString(stream.ToArray());
            }
            catch (JsonException)
            {
                return "This body is not JSON." + Environment.NewLine + Environment.NewLine + text;
            }
        }

        private static void WriteElement(Utf8JsonWriter writer, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    WriteObject(writer, element);
                    break;
                case JsonValueKind.Array:
                    WriteArray(writer, element);
                    break;
                case JsonValueKind.String:
                    writer.WriteStringValue(Elide(element.GetString() ?? ""));
                    break;
                default:
                    element.WriteTo(writer);
                    break;
            }
        }

        private static void WriteObject(Utf8JsonWriter writer, JsonElement element)
        {
            writer.WriteStartObject();
            foreach (JsonProperty property in element.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                WriteElement(writer, property.Value);
            }

            writer.WriteEndObject();
        }

        private static void WriteArray(Utf8JsonWriter writer, JsonElement element)
        {
            writer.WriteStartArray();
            foreach (JsonElement item in element.EnumerateArray())
            {
                WriteElement(writer, item);
            }

            writer.WriteEndArray();
        }

        private static string Elide(string value)
        {
            if (!IsLongBase64(value))
            {
                return value;
            }

            return "[base64 " + value.Length + " characters — see the Image tab]";
        }

        private static bool IsLongBase64(string value)
        {
            if (value.Length <= Base64ElideLength)
            {
                return false;
            }

            int check = Math.Min(value.Length, 128);
            for (int index = 0; index < check; index++)
            {
                if (!IsBase64Char(value[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsBase64Char(char value)
        {
            return (value >= 'A' && value <= 'Z')
                || (value >= 'a' && value <= 'z')
                || (value >= '0' && value <= '9')
                || value == '+'
                || value == '/'
                || value == '='
                || value == '-'
                || value == '_';
        }
    }
}

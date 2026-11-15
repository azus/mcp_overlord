using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace mcp_overlord.Tools
{
    /// <summary>JSON settings for tool results. Null properties are omitted.</summary>
    public static class ToolJson
    {
        /// <summary>Camel-case serializer used for every tool payload.</summary>
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Serializes a payload.
        /// </summary>
        /// <param name="value">Anonymous object or DTO.</param>
        /// <returns>JSON text.</returns>
        public static string Serialize(object value)
        {
            return JsonSerializer.Serialize(value, Options);
        }
    }

    /// <summary>Builds MCP tool results.</summary>
    public static class ToolResponses
    {
        /// <summary>A successful text result.</summary>
        /// <param name="text">JSON or plain text.</param>
        /// <returns>The tool result.</returns>
        public static CallToolResult Text(string text)
        {
            return new CallToolResult
            {
                Content = new List<ContentBlock>
                {
                    new TextContentBlock { Text = text }
                }
            };
        }

        /// <summary>A tool error. The host stays up.</summary>
        /// <param name="message">Message for the model.</param>
        /// <returns>The tool result with <c>isError</c> set.</returns>
        public static CallToolResult Error(string message)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = new List<ContentBlock>
                {
                    new TextContentBlock { Text = message }
                }
            };
        }

        /// <summary>Metadata text followed by an image block.</summary>
        /// <param name="metadata">JSON metadata.</param>
        /// <param name="bytes">Encoded image.</param>
        /// <param name="mimeType"><c>image/png</c> or <c>image/jpeg</c>.</param>
        /// <returns>The tool result.</returns>
        public static CallToolResult Image(string metadata, byte[] bytes, string mimeType)
        {
            return new CallToolResult
            {
                Content = new List<ContentBlock>
                {
                    new TextContentBlock { Text = metadata },
                    ImageContentBlock.FromBytes(bytes, mimeType)
                }
            };
        }
    }
}

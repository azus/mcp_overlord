using System.Diagnostics;
using System.Text.Json;
using mcp_overlord.Logging;
using mcp_overlord.Services;
using mcp_overlord.Tools;
using ModelContextProtocol.Protocol;

namespace mcp_overlord.Hosting
{
    /// <summary>
    /// Writes the request and response log lines for a tool call and updates the counters.
    /// Image payloads are summarized as dimensions and byte size. They are never written as base64.
    /// </summary>
    public static class ToolCallLog
    {
        /// <summary>Argument text is cut here so one call cannot flood the log.</summary>
        public const int ArgumentLimit = 500;

        /// <summary>How long a tool call may run before the client receives an error. The call itself is left running.</summary>
        public static readonly TimeSpan TOOL_CALL_TIMEOUT = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Runs the tool, logs the outcome, and turns an unexpected exception into a tool error.
        /// A call that exceeds <paramref name="timeout"/> returns <see cref="ToolErrors.TimedOut"/> and is logged as a warning.
        /// </summary>
        /// <param name="log">Log buffer.</param>
        /// <param name="stats">Counters.</param>
        /// <param name="parameters">Tool name and arguments. May be null.</param>
        /// <param name="next">The rest of the tool pipeline.</param>
        /// <param name="timeout">Deadline. Null uses <see cref="TOOL_CALL_TIMEOUT"/>.</param>
        /// <returns>The tool result, a timeout error, or a tool error if <paramref name="next"/> throws.</returns>
        public static async ValueTask<CallToolResult> Invoke(
            LogBuffer log,
            StatsCounter stats,
            CallToolRequestParams? parameters,
            Func<ValueTask<CallToolResult>> next,
            TimeSpan? timeout = null,
            Func<HttpExchange?>? exchange = null)
        {
            string toolName = string.IsNullOrEmpty(parameters?.Name) ? "unknown" : parameters!.Name;
            void Write(LogLevel level, string message, CallToolResult? result, string? failureJson = null)
            {
                log.Append(level, "mcp", message, DescribeCall(parameters, result, exchange?.Invoke(), failureJson));
            }

            Write(LogLevel.Info, "request " + toolName + " " + CompactArguments(parameters?.Arguments), null);
            var watch = Stopwatch.StartNew();
            TimeSpan deadline = timeout ?? TOOL_CALL_TIMEOUT;
            using var timeoutSource = new CancellationTokenSource();
            try
            {
                Task<CallToolResult> running = next().AsTask();
                Task delay = Task.Delay(deadline, timeoutSource.Token);
                Task finished = await Task.WhenAny(running, delay).ConfigureAwait(false);
                timeoutSource.Cancel();
                ObserveCompleted(delay);
                if (finished != running)
                {
                    watch.Stop();
                    long timedOutElapsed = watch.ElapsedMilliseconds;
                    ObserveCompleted(running);
                    Write(LogLevel.Warning, "timeout " + toolName + " " + ToolErrors.TimedOut + " " + timedOutElapsed + " ms", null, FailureJson("Timeout", ToolErrors.TimedOut));
                    stats.RecordToolError(toolName, timedOutElapsed);
                    return ToolResponses.Error(ToolErrors.TimedOut);
                }

                CallToolResult result = await running.ConfigureAwait(false);
                watch.Stop();
                long elapsed = watch.ElapsedMilliseconds;
                if (result.IsError == true)
                {
                    Write(LogLevel.Error, "error " + toolName + " " + FirstText(result) + " " + elapsed + " ms", result);
                    stats.RecordToolError(toolName, elapsed);
                    return result;
                }

                bool screenshot = toolName == "take_screenshot";
                long bytes = screenshot ? ImageByteCount(result) : 0;
                Write(LogLevel.Info, "response " + toolName + " " + Summarize(result) + " " + elapsed + " ms", result);
                stats.RecordToolSuccess(toolName, elapsed, screenshot, bytes, IsInput(toolName));
                return result;
            }
            catch (Exception exception)
            {
                watch.Stop();
                long elapsed = watch.ElapsedMilliseconds;
                Write(
                    LogLevel.Error,
                    "error " + toolName + " " + exception.GetType().Name + ": " + exception.Message + " " + elapsed + " ms" + Environment.NewLine + exception,
                    null,
                    FailureJson(exception.GetType().Name, exception.Message));
                stats.RecordToolError(toolName, elapsed);
                return new CallToolResult
                {
                    IsError = true,
                    Content = new List<ContentBlock>
                    {
                        new TextContentBlock
                        {
                            Text = "The tool failed (" + exception.GetType().Name + "): " + exception.Message
                        }
                    }
                };
            }
        }

        private static void ObserveCompleted(Task task)
        {
            _ = task.ContinueWith(
                completed =>
                {
                    _ = completed.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        /// <summary>True for mouse and keyboard tools that send an event.</summary>
        /// <param name="toolName">Tool name.</param>
        /// <returns>True when the call counts as an input.</returns>
        public static bool IsInput(string toolName)
        {
            return toolName.StartsWith("mouse_", StringComparison.Ordinal)
                || toolName.StartsWith("key_", StringComparison.Ordinal)
                || toolName == "type_text";
        }

        private static string CompactArguments(IDictionary<string, JsonElement>? arguments)
        {
            if (arguments == null || arguments.Count == 0)
            {
                return "";
            }

            string json = JsonSerializer.Serialize(arguments);
            if (json.Length <= ArgumentLimit)
            {
                return json;
            }

            return json.Substring(0, ArgumentLimit) + "...";
        }

        private static string Summarize(CallToolResult result)
        {
            ImageContentBlock? image = null;
            string? text = null;
            foreach (ContentBlock block in result.Content)
            {
                if (text == null && block is TextContentBlock textBlock)
                {
                    text = textBlock.Text;
                }

                if (block is ImageContentBlock imageBlock)
                {
                    image = imageBlock;
                }
            }

            if (image != null)
            {
                return DescribeImage(text, image);
            }

            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            string oneLine = text.Replace('\r', ' ').Replace('\n', ' ');
            if (oneLine.Length <= ArgumentLimit)
            {
                return oneLine;
            }

            return oneLine.Substring(0, ArgumentLimit) + "...";
        }

        private static string DescribeImage(string? metadata, ImageContentBlock image)
        {
            string format = image.MimeType == "image/jpeg" ? "jpeg" : "png";
            int width = 0;
            int height = 0;
            if (!string.IsNullOrEmpty(metadata))
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(metadata);
                    JsonElement root = document.RootElement;
                    if (root.TryGetProperty("imageWidth", out JsonElement widthElement))
                    {
                        width = widthElement.GetInt32();
                    }

                    if (root.TryGetProperty("imageHeight", out JsonElement heightElement))
                    {
                        height = heightElement.GetInt32();
                    }

                    if (root.TryGetProperty("format", out JsonElement formatElement))
                    {
                        format = formatElement.GetString() ?? format;
                    }
                }
                catch (JsonException)
                {
                    // The summary still reports the byte size.
                }
            }

            int bytes = ImageBytes(image);
            return format + " " + width + "x" + height + " " + bytes + " bytes";
        }

        private static long ImageByteCount(CallToolResult result)
        {
            foreach (ContentBlock block in result.Content)
            {
                if (block is ImageContentBlock image)
                {
                    return ImageBytes(image);
                }
            }

            return 0;
        }

        private static int ImageBytes(ImageContentBlock image)
        {
            return image.DecodedData.Length;
        }

        private static LogDetail DescribeCall(CallToolRequestParams? parameters, CallToolResult? result, HttpExchange? exchange, string? failureJson)
        {
            return new LogDetail
            {
                Exchange = exchange,
                ToolName = string.IsNullOrEmpty(parameters?.Name) ? null : parameters!.Name,
                ToolArgumentsJson = SerializeArguments(parameters?.Arguments),
                ToolResultJson = result != null ? SerializeResult(result) : failureJson,
                Images = result == null ? Array.Empty<LoggedImage>() : CollectImages(result)
            };
        }

        private static string? SerializeArguments(IDictionary<string, JsonElement>? arguments)
        {
            if (arguments == null || arguments.Count == 0)
            {
                return null;
            }

            return JsonSerializer.Serialize(arguments);
        }

        private static string SerializeResult(CallToolResult result)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteBoolean("isError", result.IsError == true);
                writer.WritePropertyName("content");
                writer.WriteStartArray();
                if (result.Content != null)
                {
                    foreach (ContentBlock block in result.Content)
                    {
                        WriteBlock(writer, block);
                    }
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        private static void WriteBlock(Utf8JsonWriter writer, ContentBlock block)
        {
            writer.WriteStartObject();
            if (block is TextContentBlock text)
            {
                writer.WriteString("type", "text");
                writer.WriteString("text", text.Text ?? "");
            }
            else if (block is ImageContentBlock image)
            {
                writer.WriteString("type", "image");
                writer.WriteString("mimeType", image.MimeType ?? "");
                writer.WriteNumber("byteLength", CopyImage(image).Length);
                writer.WriteString("data", "[see the Image tab]");
            }
            else
            {
                writer.WriteString("type", block.GetType().Name);
            }

            writer.WriteEndObject();
        }

        private static List<LoggedImage> CollectImages(CallToolResult result)
        {
            var images = new List<LoggedImage>();
            if (result.Content == null)
            {
                return images;
            }

            string? metadata = null;
            foreach (ContentBlock block in result.Content)
            {
                if (metadata == null && block is TextContentBlock text && !string.IsNullOrEmpty(text.Text))
                {
                    metadata = text.Text;
                }
            }

            ReadImageSize(metadata, out int width, out int height);
            foreach (ContentBlock block in result.Content)
            {
                if (block is not ImageContentBlock image)
                {
                    continue;
                }

                byte[] bytes = CopyImage(image);
                images.Add(new LoggedImage
                {
                    MimeType = string.IsNullOrEmpty(image.MimeType) ? "image/png" : image.MimeType,
                    Bytes = bytes,
                    Width = images.Count == 0 ? width : 0,
                    Height = images.Count == 0 ? height : 0
                });
            }

            return images;
        }

        private static byte[] CopyImage(ImageContentBlock image)
        {
            ReadOnlyMemory<byte> data = image.DecodedData;
            return data.ToArray();
        }

        private static void ReadImageSize(string? metadata, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (string.IsNullOrEmpty(metadata))
            {
                return;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(metadata);
                JsonElement root = document.RootElement;
                if (root.TryGetProperty("imageWidth", out JsonElement widthElement))
                {
                    width = widthElement.GetInt32();
                }

                if (root.TryGetProperty("imageHeight", out JsonElement heightElement))
                {
                    height = heightElement.GetInt32();
                }
            }
            catch (JsonException)
            {
                width = 0;
                height = 0;
            }
        }

        private static string FailureJson(string type, string message)
        {
            return JsonSerializer.Serialize(new { exception = type, message });
        }

        private static string FirstText(CallToolResult result)
        {
            foreach (ContentBlock block in result.Content)
            {
                if (block is TextContentBlock textBlock && !string.IsNullOrEmpty(textBlock.Text))
                {
                    string oneLine = textBlock.Text.Replace('\r', ' ').Replace('\n', ' ');
                    return oneLine.Length <= ArgumentLimit ? oneLine : oneLine.Substring(0, ArgumentLimit) + "...";
                }
            }

            return "";
        }
    }
}

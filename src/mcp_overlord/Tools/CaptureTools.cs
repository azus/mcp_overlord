using System.ComponentModel;
using mcp_overlord.Configuration;
using mcp_overlord.Logging;
using mcp_overlord.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace mcp_overlord.Tools
{
    /// <summary>Region of the window, in window pixels. Origin is the top left of <c>GetWindowRect</c>.</summary>
    public sealed class ScreenshotRegion
    {
        /// <summary>Left edge.</summary>
        public int X { get; set; }

        /// <summary>Top edge.</summary>
        public int Y { get; set; }

        /// <summary>Width.</summary>
        public int Width { get; set; }

        /// <summary>Height.</summary>
        public int Height { get; set; }
    }

    /// <summary>Captures a window and returns an MCP image plus scale metadata.</summary>
    [McpServerToolType]
    public sealed class CaptureTools
    {
        private readonly ConfigStore _store;
        private readonly WindowLocator _locator;
        private readonly CaptureService _capture;
        private readonly ScreenshotScaleStore _scales;
        private readonly LogBuffer _log;

        /// <summary>
        /// Creates the tool.
        /// </summary>
        public CaptureTools(
            ConfigStore store,
            WindowLocator locator,
            CaptureService capture,
            ScreenshotScaleStore scales,
            LogBuffer log)
        {
            _store = store;
            _locator = locator;
            _capture = capture;
            _scales = scales;
            _log = log;
        }

        /// <summary>Captures the whole window. Coordinates of later clicks are pixels of this image unless coordinateSpace is window_rect.</summary>
        [McpServerTool(Name = "take_screenshot", ReadOnly = true, OpenWorld = false),
         Description("Capture the whole window, including the border and the title bar. Overlapping windows are not included, and what is behind the window is not included. Call this before clicking. Click coordinates are pixels of this image. Origin (0, 0) is the top left of the window. A click in the outer border can resize the window. On a maximized window, (0, 0) can be off screen.")]
        public CallToolResult TakeScreenshot(
            [Description("Configured application name.")] string name,
            [Description("Optional crop in window pixels: x, y, width, height. Origin is the top left of the window.")] ScreenshotRegion? region = null,
            [Description("Downscale when the image is wider than this. Clamped to 64-4096.")] int? maxWidth = null,
            [Description("png or jpeg. Default png.")] string format = "png",
            [Description("JPEG quality from 1 to 100. Default 80. Ignored for png.")] int quality = 80)
        {
            if (!ApplicationLookup.TryGet(_store, name, out ApplicationEntry? application, out CallToolResult? error))
            {
                return error!;
            }

            WindowLookup lookup = _locator.Find(application!, true, true);
            if (!lookup.Found)
            {
                return ToolResponses.Error(ToolErrors.WindowNotAvailable(application!.Name, application.IsLaunchable));
            }

            var request = new CaptureRequest
            {
                RegionX = region?.X,
                RegionY = region?.Y,
                RegionWidth = region?.Width,
                RegionHeight = region?.Height,
                MaxWidth = maxWidth,
                Format = format,
                Quality = quality
            };

            if (region == null)
            {
                request = new CaptureRequest
                {
                    MaxWidth = maxWidth,
                    Format = format,
                    Quality = quality
                };
            }

            CaptureOutcome outcome = _capture.Capture(lookup.Hwnd, lookup.Frame, request);
            if (outcome.Error != null)
            {
                return ToolResponses.Error(outcome.Error);
            }

            _scales.Store(application!.Name, new ScreenshotFrame(
                outcome.Scale,
                outcome.RegionX,
                outcome.RegionY,
                outcome.RegionWidth,
                outcome.RegionHeight));

            if (outcome.Warning != null)
            {
                _log.Append(LogLevel.Warning, "capture", outcome.Warning);
            }

            string metadata = ToolJson.Serialize(new
            {
                name = application.Name,
                windowWidth = outcome.WindowWidth,
                windowHeight = outcome.WindowHeight,
                region = new { x = outcome.RegionX, y = outcome.RegionY, width = outcome.RegionWidth, height = outcome.RegionHeight },
                imageWidth = outcome.ImageWidth,
                imageHeight = outcome.ImageHeight,
                scale = outcome.Scale,
                format = outcome.Format,
                captureMethod = outcome.CaptureMethod,
                warning = outcome.Warning
            });

            string mimeType = outcome.Format == "jpeg" ? "image/jpeg" : "image/png";
            return ToolResponses.Image(metadata, outcome.Bytes, mimeType);
        }
    }
}

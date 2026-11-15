namespace mcp_overlord.Services
{
    /// <summary>
    /// Scale and crop stored after the last screenshot of an application.
    /// </summary>
    /// <param name="Scale">imageWidth / region width. 1 when the image was not scaled.</param>
    /// <param name="RegionX">Region origin X in window pixels of the snapshot.</param>
    /// <param name="RegionY">Region origin Y in window pixels of the snapshot.</param>
    /// <param name="RegionWidth">Region width in window pixels.</param>
    /// <param name="RegionHeight">Region height in window pixels.</param>
    public readonly record struct ScreenshotFrame(
        double Scale,
        int RegionX,
        int RegionY,
        int RegionWidth,
        int RegionHeight);

    /// <summary>A point after mapping into the current window. Client fields are only for delivering the click.</summary>
    /// <param name="ClientX">Client X passed to the click helper. Not part of a tool result.</param>
    /// <param name="ClientY">Client Y passed to the click helper. Not part of a tool result.</param>
    /// <param name="WindowX">X in the current <c>GetWindowRect</c>.</param>
    /// <param name="WindowY">Y in the current <c>GetWindowRect</c>.</param>
    public readonly record struct MappedPoint(int ClientX, int ClientY, int WindowX, int WindowY);

    /// <summary>
    /// Remembers the last screenshot scale for each application name.
    /// </summary>
    public sealed class ScreenshotScaleStore
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, ScreenshotFrame> _frames = new Dictionary<string, ScreenshotFrame>(StringComparer.Ordinal);

        /// <summary>
        /// Stores the frame for an application, replacing the previous one.
        /// </summary>
        /// <param name="name">Application name.</param>
        /// <param name="frame">Scale and region.</param>
        public void Store(string name, ScreenshotFrame frame)
        {
            lock (_gate)
            {
                _frames[name] = frame;
            }
        }

        /// <summary>
        /// Reads the last frame for an application.
        /// </summary>
        /// <param name="name">Application name.</param>
        /// <param name="frame">The stored frame.</param>
        /// <returns>True when a screenshot has been taken for this name.</returns>
        public bool TryGet(string name, out ScreenshotFrame frame)
        {
            lock (_gate)
            {
                return _frames.TryGetValue(name, out frame);
            }
        }
    }

    /// <summary>
    /// Converts image or window-rect coordinates into a point inside the current window rectangle.
    /// </summary>
    public static class CoordinateMapper
    {
        /// <summary>
        /// Maps a point. Image coordinates use the last screenshot scale and region origin.
        /// When no screenshot has been taken, scale is 1 and the origin is (0, 0), so image pixels are window pixels.
        /// Window-rect coordinates are physical pixels of the current window and ignore the screenshot.
        /// A point outside the current window is rejected and <paramref name="point"/> keeps the converted window coordinates.
        /// </summary>
        /// <param name="x">X in the selected coordinate space.</param>
        /// <param name="y">Y in the selected coordinate space.</param>
        /// <param name="coordinateSpace"><c>image</c> or <c>window_rect</c>.</param>
        /// <param name="frame">Last screenshot, or null. Only the scale and region are used, and only for image coordinates.</param>
        /// <param name="current">Window rectangle read at the time of the input.</param>
        /// <param name="point">The mapped point. Window coordinates are set even when the point is outside.</param>
        /// <returns>True when the window point is inside the current window rectangle.</returns>
        public static bool TryMap(
            double x,
            double y,
            string coordinateSpace,
            ScreenshotFrame? frame,
            WindowFrameGeometry current,
            out MappedPoint point)
        {
            int windowX;
            int windowY;
            if (string.Equals(coordinateSpace, "window_rect", StringComparison.OrdinalIgnoreCase))
            {
                windowX = Round(x);
                windowY = Round(y);
            }
            else
            {
                double scale = frame?.Scale ?? 1;
                if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
                {
                    scale = 1;
                }

                int originX = frame?.RegionX ?? 0;
                int originY = frame?.RegionY ?? 0;
                windowX = originX + Round(x / scale);
                windowY = originY + Round(y / scale);
            }

            if (windowX < 0 || windowY < 0 || windowX >= current.FrameWidth || windowY >= current.FrameHeight)
            {
                point = new MappedPoint(0, 0, windowX, windowY);
                return false;
            }

            point = new MappedPoint(
                windowX - current.ClientOffsetX,
                windowY - current.ClientOffsetY,
                windowX,
                windowY);
            return true;
        }

        private static int Round(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0;
            }

            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }
    }
}

namespace mcp_overlord.UI.Placement
{
    /// <summary>
    /// Moves and shrinks a pixel rectangle so the window lies fully inside one visible work area.
    /// </summary>
    public static class WindowBoundsClamp
    {
        /// <summary>
        /// Returns <paramref name="bounds"/> moved and resized so it fits one work area.
        /// The monitor with the largest overlap is preferred. When there is no overlap, the monitor
        /// whose work-area center is closest to the rectangle center is used.
        /// An empty monitor list leaves the position unchanged and only enforces the minimum size.
        /// </summary>
        /// <param name="bounds">Requested outer bounds, in physical pixels.</param>
        /// <param name="monitors">Attached monitors.</param>
        /// <param name="minimumWidthDip">Minimum width in device-independent pixels. Zero skips the minimum.</param>
        /// <param name="minimumHeightDip">Minimum height in device-independent pixels. Zero skips the minimum.</param>
        /// <returns>The clamped rectangle.</returns>
        public static PixelRect Clamp(
            PixelRect bounds,
            IReadOnlyList<MonitorInfo> monitors,
            double minimumWidthDip,
            double minimumHeightDip)
        {
            if (monitors == null)
            {
                throw new ArgumentNullException(nameof(monitors));
            }

            MonitorInfo? monitor = SelectMonitor(bounds, monitors);
            if (monitor == null)
            {
                return NormalizeSize(bounds, DipToPixels(minimumWidthDip, 96), DipToPixels(minimumHeightDip, 96));
            }

            int minimumWidth = DipToPixels(minimumWidthDip, monitor.Dpi);
            int minimumHeight = DipToPixels(minimumHeightDip, monitor.Dpi);
            PixelRect workArea = monitor.WorkArea;
            int width = ClampDimension(bounds.Width, minimumWidth, workArea.Width);
            int height = ClampDimension(bounds.Height, minimumHeight, workArea.Height);
            int left = ClampPosition(bounds.X, workArea.X, workArea.Width, width);
            int top = ClampPosition(bounds.Y, workArea.Y, workArea.Height, height);
            return new PixelRect(left, top, width, height);
        }

        #region Private Helpers

        private static MonitorInfo? SelectMonitor(PixelRect bounds, IReadOnlyList<MonitorInfo> monitors)
        {
            MonitorInfo? best = null;
            long bestArea = -1;
            long bestDistance = long.MaxValue;

            foreach (MonitorInfo monitor in monitors)
            {
                if (!monitor.WorkArea.HasArea)
                {
                    continue;
                }

                long area = bounds.IntersectionArea(monitor.WorkArea);
                long distance = DistanceSquared(bounds, monitor.WorkArea);
                bool betterArea = area > bestArea;
                bool nearerTie = area == bestArea && distance < bestDistance;
                if (best == null || betterArea || nearerTie)
                {
                    best = monitor;
                    bestArea = area;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static long DistanceSquared(PixelRect bounds, PixelRect workArea)
        {
            long deltaX = workArea.CenterX - bounds.CenterX;
            long deltaY = workArea.CenterY - bounds.CenterY;
            return (deltaX * deltaX) + (deltaY * deltaY);
        }

        private static int ClampDimension(int size, int minimum, int maximum)
        {
            if (size < minimum)
            {
                size = minimum;
            }

            if (maximum > 0 && size > maximum)
            {
                size = maximum;
            }

            if (size < 1 && maximum != 0)
            {
                size = 1;
            }

            if (size < 0)
            {
                size = 0;
            }

            return size;
        }

        private static int ClampPosition(int origin, int workOrigin, int workSize, int size)
        {
            int maximumOrigin = workOrigin + workSize - size;
            if (maximumOrigin < workOrigin)
            {
                return workOrigin;
            }

            if (origin < workOrigin)
            {
                return workOrigin;
            }

            if (origin > maximumOrigin)
            {
                return maximumOrigin;
            }

            return origin;
        }

        private static PixelRect NormalizeSize(PixelRect bounds, int minimumWidth, int minimumHeight)
        {
            int width = bounds.Width < minimumWidth ? minimumWidth : bounds.Width;
            int height = bounds.Height < minimumHeight ? minimumHeight : bounds.Height;
            if (width < 0)
            {
                width = 0;
            }

            if (height < 0)
            {
                height = 0;
            }

            return new PixelRect(bounds.X, bounds.Y, width, height);
        }

        private static int DipToPixels(double dip, int dpi)
        {
            if (double.IsNaN(dip) || double.IsInfinity(dip) || dip <= 0)
            {
                return 0;
            }

            if (dpi <= 0)
            {
                dpi = 96;
            }

            int pixels = (int)Math.Ceiling(dip * dpi / 96.0);
            return pixels < 0 ? 0 : pixels;
        }

        #endregion
    }
}

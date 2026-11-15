namespace mcp_overlord.UI.Placement
{
    /// <summary>
    /// Converts window bounds saved as WPF device-independent pixels into physical pixels.
    /// Older builds stored <c>Left</c> and <c>Top</c> in the DIP of whichever monitor the window
    /// was on. Replaying those numbers with the primary monitor's DPI pushes a window off a
    /// 96 DPI screen. Each monitor is tried with its own DPI, and the rectangle that lands
    /// inside that monitor is kept.
    /// </summary>
    public static class LegacyBoundsConverter
    {
        private const int FitTolerancePixels = 4;
        private const int FallbackDpi = 96;

        /// <summary>
        /// Picks the pixel rectangle to restore. Saved physical pixels win. Older DIP fields are
        /// used only when those pixels are missing, and the result is clamped to a visible work area.
        /// </summary>
        /// <param name="savedPixels">Physical bounds from the current file, or null.</param>
        /// <param name="legacyLeft">Older left edge, in DIP.</param>
        /// <param name="legacyTop">Older top edge, in DIP.</param>
        /// <param name="legacyWidth">Older width, in DIP.</param>
        /// <param name="legacyHeight">Older height, in DIP.</param>
        /// <param name="monitors">Attached monitors.</param>
        /// <param name="minimumWidthDip">Minimum width in device-independent pixels.</param>
        /// <param name="minimumHeightDip">Minimum height in device-independent pixels.</param>
        /// <returns>The rectangle to restore, or null when nothing usable was saved.</returns>
        public static PixelRect? Resolve(
            PixelRect? savedPixels,
            double legacyLeft,
            double legacyTop,
            double legacyWidth,
            double legacyHeight,
            IReadOnlyList<MonitorInfo> monitors,
            double minimumWidthDip,
            double minimumHeightDip)
        {
            if (monitors == null)
            {
                throw new ArgumentNullException(nameof(monitors));
            }

            PixelRect? bounds = savedPixels;
            if (bounds == null || !bounds.Value.HasArea)
            {
                bounds = ToPixels(legacyLeft, legacyTop, legacyWidth, legacyHeight, monitors);
            }

            if (bounds == null)
            {
                return null;
            }

            return WindowBoundsClamp.Clamp(bounds.Value, monitors, minimumWidthDip, minimumHeightDip);
        }

        /// <summary>
        /// Converts a legacy DIP rectangle to physical pixels and clamps it to a visible work area.
        /// </summary>
        /// <param name="left">Saved left edge, in DIP.</param>
        /// <param name="top">Saved top edge, in DIP.</param>
        /// <param name="width">Saved width, in DIP.</param>
        /// <param name="height">Saved height, in DIP.</param>
        /// <param name="monitors">Attached monitors.</param>
        /// <returns>The pixel rectangle, or null when the DIP values cannot be used.</returns>
        public static PixelRect? ToPixels(
            double left,
            double top,
            double width,
            double height,
            IReadOnlyList<MonitorInfo> monitors)
        {
            if (monitors == null)
            {
                throw new ArgumentNullException(nameof(monitors));
            }

            if (!IsUsable(left, top, width, height))
            {
                return null;
            }

            PixelRect? matched = MatchMonitor(left, top, width, height, monitors);
            PixelRect scaled = matched ?? Scale(left, top, width, height, FallbackDpi);
            return WindowBoundsClamp.Clamp(scaled, monitors, 0, 0);
        }

        #region Private Helpers

        private static PixelRect? MatchMonitor(
            double left,
            double top,
            double width,
            double height,
            IReadOnlyList<MonitorInfo> monitors)
        {
            PixelRect? contained = FindBest(left, top, width, height, monitors, requireContained: true);
            if (contained != null)
            {
                return contained;
            }

            return FindBest(left, top, width, height, monitors, requireContained: false);
        }

        private static PixelRect? FindBest(
            double left,
            double top,
            double width,
            double height,
            IReadOnlyList<MonitorInfo> monitors,
            bool requireContained)
        {
            PixelRect? best = null;
            long bestArea = -1;

            foreach (MonitorInfo monitor in monitors)
            {
                if (!monitor.Bounds.HasArea)
                {
                    continue;
                }

                PixelRect scaled = Scale(left, top, width, height, monitor.Dpi);
                if (!IsCandidate(scaled, monitor.Bounds, requireContained))
                {
                    continue;
                }

                long area = scaled.IntersectionArea(monitor.Bounds);
                if (best == null || area > bestArea)
                {
                    best = scaled;
                    bestArea = area;
                }
            }

            return best;
        }

        private static bool IsCandidate(PixelRect scaled, PixelRect monitorBounds, bool requireContained)
        {
            if (requireContained)
            {
                return FitsWithin(scaled, monitorBounds, FitTolerancePixels);
            }

            return ContainsPoint(monitorBounds, scaled.CenterX, scaled.CenterY);
        }

        private static bool FitsWithin(PixelRect rect, PixelRect area, int tolerance)
        {
            return rect.HasArea
                && rect.X >= area.X - tolerance
                && rect.Y >= area.Y - tolerance
                && rect.Right <= area.Right + tolerance
                && rect.Bottom <= area.Bottom + tolerance;
        }

        private static bool ContainsPoint(PixelRect area, long x, long y)
        {
            return x >= area.X && y >= area.Y && x < area.Right && y < area.Bottom;
        }

        private static PixelRect Scale(double left, double top, double width, double height, int dpi)
        {
            if (dpi <= 0)
            {
                dpi = FallbackDpi;
            }

            double scale = dpi / 96.0;
            return new PixelRect(
                (int)Math.Round(left * scale),
                (int)Math.Round(top * scale),
                Math.Max(1, (int)Math.Round(width * scale)),
                Math.Max(1, (int)Math.Round(height * scale)));
        }

        private static bool IsUsable(double left, double top, double width, double height)
        {
            return IsFinite(left)
                && IsFinite(top)
                && IsFinite(width)
                && IsFinite(height)
                && width > 0
                && height > 0;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        #endregion
    }
}

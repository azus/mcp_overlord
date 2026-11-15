using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace mcp_overlord.Services
{
    /// <summary>What <c>take_screenshot</c> asked to capture.</summary>
    public sealed class CaptureRequest
    {
        /// <summary>Region X in window pixels. Null when the whole window is requested.</summary>
        public int? RegionX { get; init; }

        /// <summary>Region Y.</summary>
        public int? RegionY { get; init; }

        /// <summary>Region width.</summary>
        public int? RegionWidth { get; init; }

        /// <summary>Region height.</summary>
        public int? RegionHeight { get; init; }

        /// <summary>Maximum image width. Null means no downscale.</summary>
        public int? MaxWidth { get; init; }

        /// <summary><c>png</c> or <c>jpeg</c>.</summary>
        public string Format { get; init; } = "png";

        /// <summary>JPEG quality from 1 to 100. Ignored for PNG.</summary>
        public int Quality { get; init; } = 80;
    }

    /// <summary>A captured image plus the metadata the tool returns.</summary>
    public sealed class CaptureOutcome
    {
        /// <summary>Tool error, or null on success.</summary>
        public string? Error { get; init; }

        /// <summary>Encoded bytes.</summary>
        public byte[] Bytes { get; init; } = System.Array.Empty<byte>();

        /// <summary>Client width.</summary>
        public int ClientWidth { get; init; }

        /// <summary>Client height.</summary>
        public int ClientHeight { get; init; }

        /// <summary>Captured window width. This is the snapshot width.</summary>
        public int WindowWidth { get; init; }

        /// <summary>Captured window height. This is the snapshot height.</summary>
        public int WindowHeight { get; init; }

        /// <summary>Client origin X inside the snapshot.</summary>
        public int ClientOffsetX { get; init; }

        /// <summary>Client origin Y inside the snapshot.</summary>
        public int ClientOffsetY { get; init; }

        /// <summary>Region X after clipping.</summary>
        public int RegionX { get; init; }

        /// <summary>Region Y after clipping.</summary>
        public int RegionY { get; init; }

        /// <summary>Region width after clipping.</summary>
        public int RegionWidth { get; init; }

        /// <summary>Region height after clipping.</summary>
        public int RegionHeight { get; init; }

        /// <summary>Encoded image width.</summary>
        public int ImageWidth { get; init; }

        /// <summary>Encoded image height.</summary>
        public int ImageHeight { get; init; }

        /// <summary>imageWidth / region width.</summary>
        public double Scale { get; init; } = 1;

        /// <summary><c>png</c> or <c>jpeg</c>.</summary>
        public string Format { get; init; } = "png";

        /// <summary><c>windowVisual</c>, <c>windowDc</c>, <c>printWindow</c>, <c>printWindowFull</c>, or <c>screen</c>.</summary>
        public string CaptureMethod { get; init; } = "";

        /// <summary>Set when every capture method returned a black frame.</summary>
        public string? Warning { get; init; }

        /// <summary>True when the client area inside the snapshot is black.</summary>
        public bool BlackFrame { get; init; }
    }

    /// <summary>
    /// Captures a whole window, clips, downscales, and encodes PNG or JPEG.
    /// </summary>
    public sealed class CaptureService
    {
        /// <summary>Mean luminance below this (0–255) is treated as a black frame.</summary>
        public const double BlackLuminanceThreshold = 2.0;

        /// <summary>Warning returned when every capture method is black.</summary>
        public const string BlackFrameWarning =
            "The captured frame is black. The game is likely using exclusive fullscreen or a protected surface.";

        private const uint PrintWindowDefault = 0;
        private const uint PrintWindowFullContent = 2;

        private readonly IWindowCapture _capture;

        /// <summary>
        /// Creates the service.
        /// </summary>
        /// <param name="capture">PrintWindow and screen blit.</param>
        public CaptureService(IWindowCapture capture)
        {
            _capture = capture;
        }

        /// <summary>
        /// Captures <paramref name="hwnd"/>. The bitmap must match <paramref name="geometry"/> or the call fails.
        /// A requested region is in window pixels. The default region is the whole bitmap.
        /// </summary>
        /// <param name="hwnd">Window to capture.</param>
        /// <param name="geometry">Current <c>GetWindowRect</c> and client offset.</param>
        /// <param name="request">Region, scale, and format.</param>
        /// <returns>The outcome. <see cref="CaptureOutcome.Error"/> is set when the region misses, the size disagrees, or encoding fails.</returns>
        public CaptureOutcome Capture(IntPtr hwnd, WindowFrameGeometry geometry, CaptureRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            string? formatError = NormalizeFormat(request.Format, out string format);
            if (formatError != null)
            {
                return new CaptureOutcome { Error = formatError };
            }

            if (geometry.FrameWidth <= 0 || geometry.FrameHeight <= 0)
            {
                return new CaptureOutcome { Error = ToolErrors.CaptureFailed("The window has no size.") };
            }

            if (CanUseFastPng(request, format) && _capture.TryCaptureFastPng(hwnd, geometry, out byte[] fastPng, out string fastMethod))
            {
                return FastPngOutcome(fastPng, geometry, fastMethod);
            }

            var failures = new List<Exception>();
            CapturedFrame? frame = CaptureWithFallback(hwnd, geometry, failures, out string? sizeError);
            if (frame == null)
            {
                if (sizeError != null)
                {
                    return new CaptureOutcome { Error = sizeError };
                }

                string message = failures.Count == 0 ? "Capture returned no image." : failures[failures.Count - 1].Message;
                return new CaptureOutcome { Error = ToolErrors.CaptureFailed(message) };
            }

            using (frame.Bitmap)
            {
                int windowWidth = frame.Bitmap.Width;
                int windowHeight = frame.Bitmap.Height;
                if (!TryResolveRegion(request, windowWidth, windowHeight, out Rectangle region, out string? regionError))
                {
                    return new CaptureOutcome { Error = regionError };
                }

                Rectangle bitmapRegion = ClipToBitmap(region, windowWidth, windowHeight);
                if (bitmapRegion.Width <= 0 || bitmapRegion.Height <= 0)
                {
                    return new CaptureOutcome
                    {
                        Error = ToolErrors.RegionMiss(windowWidth, windowHeight)
                    };
                }

                using Bitmap cropped = Crop(frame.Bitmap, bitmapRegion);
                int? maxWidth = request.MaxWidth.HasValue ? ClampMaxWidth(request.MaxWidth.Value) : null;
                using Bitmap scaled = Downscale(cropped, maxWidth);
                int quality = Math.Clamp(request.Quality, 1, 100);
                byte[] bytes;
                try
                {
                    bytes = Encode(scaled, format, quality);
                }
                catch (Exception exception)
                {
                    return new CaptureOutcome { Error = ToolErrors.CaptureFailed(exception.Message) };
                }

                double scale = bitmapRegion.Width == 0 ? 1 : (double)scaled.Width / bitmapRegion.Width;
                return new CaptureOutcome
                {
                    Bytes = bytes,
                    ClientWidth = geometry.ClientWidth,
                    ClientHeight = geometry.ClientHeight,
                    WindowWidth = windowWidth,
                    WindowHeight = windowHeight,
                    ClientOffsetX = geometry.ClientOffsetX,
                    ClientOffsetY = geometry.ClientOffsetY,
                    RegionX = bitmapRegion.X,
                    RegionY = bitmapRegion.Y,
                    RegionWidth = bitmapRegion.Width,
                    RegionHeight = bitmapRegion.Height,
                    ImageWidth = scaled.Width,
                    ImageHeight = scaled.Height,
                    Scale = scale,
                    Format = format,
                    CaptureMethod = frame.Method,
                    Warning = frame.Black ? BlackFrameWarning : null,
                    BlackFrame = frame.Black
                };
            }
        }

        /// <summary>
        /// True when the mean luminance of the whole bitmap is under <see cref="BlackLuminanceThreshold"/>.
        /// </summary>
        /// <param name="bitmap">Image to sample.</param>
        /// <returns>True when the frame is effectively black.</returns>
        public static bool IsEffectivelyBlack(Bitmap bitmap)
        {
            return IsEffectivelyBlack(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        }

        /// <summary>
        /// True when the mean luminance of <paramref name="area"/> is under <see cref="BlackLuminanceThreshold"/>.
        /// The area is clipped to the bitmap. An empty area is treated as black.
        /// </summary>
        /// <param name="bitmap">Image to sample.</param>
        /// <param name="area">Rectangle to sample, in bitmap pixels.</param>
        /// <returns>True when that area is effectively black.</returns>
        public static bool IsEffectivelyBlack(Bitmap bitmap, Rectangle area)
        {
            if (bitmap.Width <= 0 || bitmap.Height <= 0)
            {
                return true;
            }

            Rectangle sampleArea = Rectangle.Intersect(area, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            if (sampleArea.Width <= 0 || sampleArea.Height <= 0)
            {
                return true;
            }

            Bitmap sample = Ensure32Bit(bitmap, out bool dispose);
            try
            {
                Rectangle bounds = new Rectangle(0, 0, sample.Width, sample.Height);
                BitmapData data = sample.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int step = SampleStep(sampleArea.Width, sampleArea.Height);
                    long total = 0;
                    long count = 0;
                    int stride = data.Stride;
                    IntPtr scan = data.Scan0;
                    for (int y = sampleArea.Top; y < sampleArea.Bottom; y += step)
                    {
                        for (int x = sampleArea.Left; x < sampleArea.Right; x += step)
                        {
                            int offset = (y * stride) + (x * 4);
                            byte blue = System.Runtime.InteropServices.Marshal.ReadByte(scan, offset);
                            byte green = System.Runtime.InteropServices.Marshal.ReadByte(scan, offset + 1);
                            byte red = System.Runtime.InteropServices.Marshal.ReadByte(scan, offset + 2);
                            total += (long)((0.2126 * red) + (0.7152 * green) + (0.0722 * blue));
                            count++;
                        }
                    }

                    if (count == 0)
                    {
                        return true;
                    }

                    return ((double)total / count) < BlackLuminanceThreshold;
                }
                finally
                {
                    sample.UnlockBits(data);
                }
            }
            finally
            {
                if (dispose)
                {
                    sample.Dispose();
                }
            }
        }

        private CapturedFrame? CaptureWithFallback(
            IntPtr hwnd,
            WindowFrameGeometry geometry,
            List<Exception> failures,
            out string? sizeError)
        {
            sizeError = null;
            CapturedFrame? lastBlack = null;
            if (KeepOrReturn(AttemptOwned(hwnd, geometry, failures, ref sizeError), ref lastBlack, out CapturedFrame? chosen))
            {
                return chosen;
            }

            if (KeepOrReturn(Attempt(hwnd, geometry, PrintWindowDefault, "printWindow", failures, ref sizeError), ref lastBlack, out chosen))
            {
                return chosen;
            }

            if (KeepOrReturn(Attempt(hwnd, geometry, PrintWindowFullContent, "printWindowFull", failures, ref sizeError), ref lastBlack, out chosen))
            {
                return chosen;
            }

            CapturedFrame? screen = _capture.IsCoveredByAnotherWindow(hwnd)
                ? null
                : AttemptScreen(hwnd, geometry, failures, ref sizeError);
            if (KeepOrReturn(screen, ref lastBlack, out chosen))
            {
                return chosen;
            }

            return lastBlack;
        }

        private static bool KeepOrReturn(CapturedFrame? frame, ref CapturedFrame? lastBlack, out CapturedFrame? chosen)
        {
            chosen = null;
            if (frame == null)
            {
                return false;
            }

            if (!frame.Black)
            {
                lastBlack?.Bitmap.Dispose();
                lastBlack = null;
                chosen = frame;
                return true;
            }

            lastBlack?.Bitmap.Dispose();
            lastBlack = frame;
            return false;
        }

        private CapturedFrame? AttemptOwned(
            IntPtr hwnd,
            WindowFrameGeometry geometry,
            List<Exception> failures,
            ref string? sizeError)
        {
            try
            {
                Bitmap? bitmap = _capture.CaptureOwned(hwnd, out string ownedMethod);
                if (bitmap == null)
                {
                    return null;
                }

                return Accept(bitmap, geometry, ownedMethod, ref sizeError);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                return null;
            }
        }

        private CapturedFrame? Attempt(
            IntPtr hwnd,
            WindowFrameGeometry geometry,
            uint flags,
            string method,
            List<Exception> failures,
            ref string? sizeError)
        {
            try
            {
                Bitmap bitmap = _capture.CapturePrintWindow(hwnd, flags);
                return Accept(bitmap, geometry, method, ref sizeError);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                return null;
            }
        }

        private CapturedFrame? AttemptScreen(
            IntPtr hwnd,
            WindowFrameGeometry geometry,
            List<Exception> failures,
            ref string? sizeError)
        {
            try
            {
                Bitmap bitmap = _capture.CaptureScreen(hwnd);
                return Accept(bitmap, geometry, "screen", ref sizeError);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                return null;
            }
        }

        private static CapturedFrame? Accept(Bitmap bitmap, WindowFrameGeometry geometry, string method, ref string? sizeError)
        {
            if (bitmap.Width != geometry.FrameWidth || bitmap.Height != geometry.FrameHeight)
            {
                sizeError = ToolErrors.CaptureSizeMismatch(bitmap.Width, bitmap.Height, geometry.FrameWidth, geometry.FrameHeight);
                bitmap.Dispose();
                return null;
            }

            Rectangle client = new Rectangle(
                geometry.ClientOffsetX,
                geometry.ClientOffsetY,
                geometry.ClientWidth,
                geometry.ClientHeight);
            return new CapturedFrame(bitmap, method, IsEffectivelyBlack(bitmap, client));
        }

        private static bool TryResolveRegion(
            CaptureRequest request,
            int windowWidth,
            int windowHeight,
            out Rectangle region,
            out string? error)
        {
            bool hasRegion = request.RegionX.HasValue || request.RegionY.HasValue || request.RegionWidth.HasValue || request.RegionHeight.HasValue;
            if (!hasRegion)
            {
                region = new Rectangle(0, 0, windowWidth, windowHeight);
                error = null;
                return true;
            }

            var requested = new Rectangle(
                request.RegionX ?? 0,
                request.RegionY ?? 0,
                request.RegionWidth ?? 0,
                request.RegionHeight ?? 0);
            region = Rectangle.Intersect(requested, new Rectangle(0, 0, windowWidth, windowHeight));
            if (region.Width <= 0 || region.Height <= 0)
            {
                error = ToolErrors.RegionMiss(windowWidth, windowHeight);
                region = Rectangle.Empty;
                return false;
            }

            error = null;
            return true;
        }

        private static Rectangle ClipToBitmap(Rectangle region, int bitmapWidth, int bitmapHeight)
        {
            return Rectangle.Intersect(region, new Rectangle(0, 0, bitmapWidth, bitmapHeight));
        }

        private static int ClampMaxWidth(int maxWidth)
        {
            return Math.Clamp(maxWidth, 64, 4096);
        }

        private static Bitmap Crop(Bitmap source, Rectangle region)
        {
            if (region.X == 0 && region.Y == 0 && region.Width == source.Width && region.Height == source.Height)
            {
                return (Bitmap)source.Clone();
            }

            var cropped = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            using Graphics graphics = Graphics.FromImage(cropped);
            graphics.DrawImage(source, new Rectangle(0, 0, region.Width, region.Height), region, GraphicsUnit.Pixel);
            return cropped;
        }

        private static Bitmap Downscale(Bitmap source, int? maxWidth)
        {
            if (!maxWidth.HasValue || source.Width <= maxWidth.Value)
            {
                return (Bitmap)source.Clone();
            }

            double ratio = maxWidth.Value / (double)source.Width;
            int width = maxWidth.Value;
            int height = Math.Max(1, (int)Math.Round(source.Height * ratio, MidpointRounding.AwayFromZero));
            var scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using Graphics graphics = Graphics.FromImage(scaled);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.DrawImage(source, 0, 0, width, height);
            return scaled;
        }

        private static byte[] Encode(Bitmap bitmap, string format, int quality)
        {
            using var stream = new MemoryStream();
            if (format == "jpeg")
            {
                ImageCodecInfo? codec = FindJpegCodec();
                if (codec == null)
                {
                    throw new InvalidOperationException("The JPEG encoder is not available.");
                }

                using var parameters = new EncoderParameters(1);
                parameters.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);
                bitmap.Save(stream, codec, parameters);
            }
            else
            {
                bitmap.Save(stream, ImageFormat.Png);
            }

            return stream.ToArray();
        }

        private static ImageCodecInfo? FindJpegCodec()
        {
            foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders())
            {
                if (string.Equals(codec.MimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
                {
                    return codec;
                }
            }

            return null;
        }

        private static string? NormalizeFormat(string? format, out string normalized)
        {
            normalized = string.IsNullOrWhiteSpace(format) ? "png" : format.Trim().ToLowerInvariant();
            if (normalized == "png" || normalized == "jpeg")
            {
                return null;
            }

            return ToolErrors.UnknownFormat(format ?? "");
        }

        private static bool CanUseFastPng(CaptureRequest request, string format)
        {
            if (format != "png" || request.MaxWidth.HasValue)
            {
                return false;
            }

            return !request.RegionX.HasValue
                && !request.RegionY.HasValue
                && !request.RegionWidth.HasValue
                && !request.RegionHeight.HasValue;
        }

        private static CaptureOutcome FastPngOutcome(byte[] png, WindowFrameGeometry geometry, string captureMethod)
        {
            return new CaptureOutcome
            {
                Bytes = png,
                ClientWidth = geometry.ClientWidth,
                ClientHeight = geometry.ClientHeight,
                WindowWidth = geometry.FrameWidth,
                WindowHeight = geometry.FrameHeight,
                ClientOffsetX = geometry.ClientOffsetX,
                ClientOffsetY = geometry.ClientOffsetY,
                RegionX = 0,
                RegionY = 0,
                RegionWidth = geometry.FrameWidth,
                RegionHeight = geometry.FrameHeight,
                ImageWidth = geometry.FrameWidth,
                ImageHeight = geometry.FrameHeight,
                Scale = 1,
                Format = "png",
                CaptureMethod = captureMethod,
                Warning = null,
                BlackFrame = false
            };
        }

        internal static int SampleStep(int width, int height)
        {
            long total = (long)width * height;
            if (total <= 20000)
            {
                return 1;
            }

            return Math.Max(1, (int)Math.Sqrt(total / 10000.0));
        }

        private static Bitmap Ensure32Bit(Bitmap source, out bool dispose)
        {
            if (source.PixelFormat == PixelFormat.Format32bppArgb || source.PixelFormat == PixelFormat.Format32bppRgb)
            {
                dispose = false;
                return source;
            }

            var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(copy))
            {
                graphics.DrawImage(source, 0, 0, source.Width, source.Height);
            }

            dispose = true;
            return copy;
        }

        private sealed class CapturedFrame
        {
            public CapturedFrame(Bitmap bitmap, string method, bool black)
            {
                Bitmap = bitmap;
                Method = method;
                Black = black;
            }

            public Bitmap Bitmap { get; }

            public string Method { get; }

            public bool Black { get; }
        }
    }
}

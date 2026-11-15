using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace mcp_overlord.Services.Native
{
    /// <summary>
    /// Copies a window's own device context into a reused bitmap and, for a full PNG, encodes it there.
    /// The device context does not include windows that overlap the target.
    /// </summary>
    internal sealed class FastWindowSnapshot
    {
        private const uint SRCCOPY = 0x00CC0020;
        private const int BandRows = 160;

        private readonly object _gate = new object();
        private readonly WindowVisualCapture _visual = new WindowVisualCapture();
        private readonly AutoResetEvent _workReady = new AutoResetEvent(false);
        private readonly AutoResetEvent _workDone = new AutoResetEvent(false);
        private IntPtr _memoryDc;
        private IntPtr _dib;
        private IntPtr _bits;
        private int _width;
        private int _height;
        private Thread? _worker;
        private NativeStoredPng.EncodeSession? _workSession;
        private int _workY;
        private int _workCount;
        private int _workFailed;

        /// <summary>
        /// Blits the window and encodes a full-frame PNG. False when the client area is black, the size moved,
        /// or the native encoder is unavailable, so the caller can try another method.
        /// </summary>
        /// <param name="hwnd">Window to capture.</param>
        /// <param name="geometry">Expected window rectangle and client origin.</param>
        /// <param name="png">Encoded PNG. Empty when this method returns false.</param>
        /// <param name="captureMethod"><c>windowVisual</c> or <c>windowDc</c>. Empty when this method returns false.</param>
        /// <returns>True when <paramref name="png"/> is the window's own pixels and the client area is not black.</returns>
        public bool TryCaptureFastPng(IntPtr hwnd, WindowFrameGeometry geometry, out byte[] png, out string captureMethod)
        {
            png = Array.Empty<byte>();
            captureMethod = "";
            if (!NativeStoredPng.IsAvailable)
            {
                return false;
            }

            lock (_gate)
            {
                if (!TryReadSize(hwnd, out int width, out int height) || width != geometry.FrameWidth || height != geometry.FrameHeight)
                {
                    return false;
                }

                if (!EnsureSurface(width, height))
                {
                    return false;
                }

                NativeStoredPng.EncodeSession? visualSession = NativeStoredPng.TryBegin(_bits, width, height, width * 4);
                if (visualSession != null && EncodeVisual(hwnd, width, height, visualSession))
                {
                    if (IsClientBlack(geometry))
                    {
                        visualSession.Release();
                        return false;
                    }

                    captureMethod = "windowVisual";
                    return NativeStoredPng.TryFinish(visualSession, out png);
                }

                visualSession?.Release();

                if (!PrepareSurface(hwnd, out width, out height, out IntPtr source))
                {
                    return false;
                }

                try
                {
                    if (width != geometry.FrameWidth || height != geometry.FrameHeight)
                    {
                        return false;
                    }

                    NativeStoredPng.EncodeSession? session = NativeStoredPng.TryBegin(_bits, width, height, width * 4);
                    if (session == null)
                    {
                        return false;
                    }

                    if (!EncodeWhileBlitting(source, width, height, session))
                    {
                        session.Release();
                        return false;
                    }

                    if (IsClientBlack(geometry))
                    {
                        session.Release();
                        return false;
                    }

                    captureMethod = "windowDc";
                    return NativeStoredPng.TryFinish(session, out png);
                }
                finally
                {
                    ReleaseDC(hwnd, source);
                }
            }
        }

        private bool EncodeVisual(IntPtr hwnd, int width, int height, NativeStoredPng.EncodeSession session)
        {
            EnsureWorker();
            _workFailed = 0;
            bool busy = false;
            bool failed = false;
            bool copied = _visual.TryCopy(hwnd, _bits, width, height, (row, count) =>
            {
                if (failed)
                {
                    return;
                }

                if (busy && !WaitForWorker())
                {
                    failed = true;
                    return;
                }

                _workSession = session;
                _workY = row;
                _workCount = count;
                _workReady.Set();
                busy = true;
            });

            if (busy && !WaitForWorker())
            {
                failed = true;
            }

            return copied && !failed;
        }

        private bool EncodeWhileBlitting(IntPtr source, int width, int height, NativeStoredPng.EncodeSession session)
        {
            EnsureWorker();
            _workFailed = 0;
            bool busy = false;
            for (int y = 0; y < height;)
            {
                int count = Math.Min(BandRows, height - y);
                if (!BitBlt(_memoryDc, 0, y, width, count, source, 0, y, SRCCOPY))
                {
                    if (busy && !WaitForWorker())
                    {
                        return false;
                    }

                    return false;
                }

                if (busy && !WaitForWorker())
                {
                    return false;
                }

                _workSession = session;
                _workY = y;
                _workCount = count;
                _workReady.Set();
                busy = true;
                y += count;
            }

            if (busy && !WaitForWorker())
            {
                return false;
            }

            return _workFailed == 0;
        }

        private bool WaitForWorker()
        {
            if (!_workDone.WaitOne(5000))
            {
                _workFailed = 1;
                return false;
            }

            return _workFailed == 0;
        }

        private void EnsureWorker()
        {
            if (_worker != null)
            {
                return;
            }

            var worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "snapshot-encode"
            };
            _worker = worker;
            worker.Start();
        }

        private void WorkerLoop()
        {
            while (true)
            {
                _workReady.WaitOne();
                try
                {
                    NativeStoredPng.EncodeSession? session = _workSession;
                    bool encoded = session != null && NativeStoredPng.EncodeRows(session, _workY, _workCount);
                    if (!encoded)
                    {
                        _workFailed = 1;
                    }
                }
                catch (Exception)
                {
                    _workFailed = 1;
                }

                _workDone.Set();
            }
        }

        /// <summary>
        /// Blits the window's own pixels into a new bitmap. Null when the blit fails.
        /// </summary>
        /// <param name="hwnd">Window to capture.</param>
        /// <param name="captureMethod"><c>windowVisual</c> or <c>windowDc</c>. Empty when this method returns null.</param>
        /// <returns>A 32-bit bitmap the caller disposes, or null.</returns>
        public Bitmap? CopyBitmap(IntPtr hwnd, out string captureMethod)
        {
            captureMethod = "";
            lock (_gate)
            {
                if (!TryReadSize(hwnd, out int width, out int height) || !EnsureSurface(width, height))
                {
                    return null;
                }

                bool filled = _visual.TryCopy(hwnd, _bits, width, height);
                if (!filled && !TryBlit(hwnd, out width, out height))
                {
                    return null;
                }

                captureMethod = filled ? "windowVisual" : "windowDc";

                var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                BitmapData data = bitmap.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppArgb);
                try
                {
                    CopyRows(width, height, data.Scan0, data.Stride);
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }

                return bitmap;
            }
        }

        private bool TryBlit(IntPtr hwnd, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (!PrepareSurface(hwnd, out width, out height, out IntPtr source))
            {
                return false;
            }

            try
            {
                return BitBlt(_memoryDc, 0, 0, width, height, source, 0, 0, SRCCOPY);
            }
            finally
            {
                ReleaseDC(hwnd, source);
            }
        }

        private bool TryReadSize(IntPtr hwnd, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out RECT rect))
            {
                return false;
            }

            width = rect.Right - rect.Left;
            height = rect.Bottom - rect.Top;
            return width > 0 && height > 0;
        }

        private bool PrepareSurface(IntPtr hwnd, out int width, out int height, out IntPtr source)
        {
            width = 0;
            height = 0;
            source = IntPtr.Zero;
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out RECT rect))
            {
                return false;
            }

            width = rect.Right - rect.Left;
            height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0 || !EnsureSurface(width, height))
            {
                return false;
            }

            source = GetWindowDC(hwnd);
            return source != IntPtr.Zero;
        }

        private bool EnsureSurface(int width, int height)
        {
            if (_bits != IntPtr.Zero && _width == width && _height == height)
            {
                return true;
            }

            if (_memoryDc == IntPtr.Zero)
            {
                _memoryDc = CreateCompatibleDC(IntPtr.Zero);
                if (_memoryDc == IntPtr.Zero)
                {
                    return false;
                }
            }

            IntPtr dib = CreateSurface(width, height, out IntPtr bits);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero)
            {
                if (dib != IntPtr.Zero)
                {
                    DeleteObject(dib);
                }

                return false;
            }

            if (SelectObject(_memoryDc, dib) == IntPtr.Zero)
            {
                DeleteObject(dib);
                return false;
            }

            if (_dib != IntPtr.Zero)
            {
                DeleteObject(_dib);
            }

            _dib = dib;
            _bits = bits;
            _width = width;
            _height = height;
            return true;
        }

        private unsafe void CopyRows(int width, int height, IntPtr destination, int destinationStride)
        {
            int sourceStride = width * 4;
            byte* source = (byte*)_bits;
            byte* dest = (byte*)destination;
            for (int y = 0; y < height; y++)
            {
                Buffer.MemoryCopy(source + (y * sourceStride), dest + (y * destinationStride), destinationStride, sourceStride);
            }
        }

        private unsafe bool IsClientBlack(WindowFrameGeometry geometry)
        {
            int left = geometry.ClientOffsetX;
            int top = geometry.ClientOffsetY;
            int right = left + geometry.ClientWidth;
            int bottom = top + geometry.ClientHeight;
            if (left < 0)
            {
                left = 0;
            }

            if (top < 0)
            {
                top = 0;
            }

            if (right > _width)
            {
                right = _width;
            }

            if (bottom > _height)
            {
                bottom = _height;
            }

            int sampleWidth = right - left;
            int sampleHeight = bottom - top;
            if (sampleWidth <= 0 || sampleHeight <= 0)
            {
                return true;
            }

            int step = CaptureService.SampleStep(sampleWidth, sampleHeight);
            int xCount = SampleCount(left, right, step);
            int yCount = SampleCount(top, bottom, step);
            int remaining = xCount * yCount;
            if (remaining <= 0)
            {
                return true;
            }

            long total = 0;
            int count = 0;
            byte* scan = (byte*)_bits;
            int stride = _width * 4;
            for (int y = top; y < bottom; y += step)
            {
                byte* row = scan + (y * stride);
                for (int x = left; x < right; x += step)
                {
                    byte* pixel = row + (x * 4);
                    total += (long)((0.2126 * pixel[2]) + (0.7152 * pixel[1]) + (0.0722 * pixel[0]));
                    count++;
                    remaining--;
                    if (count >= 16 && (double)total / (count + remaining) >= CaptureService.BlackLuminanceThreshold)
                    {
                        return false;
                    }
                }
            }

            return count == 0 || ((double)total / count) < CaptureService.BlackLuminanceThreshold;
        }

        private static int SampleCount(int start, int end, int step)
        {
            if (end <= start || step <= 0)
            {
                return 0;
            }

            return ((end - start - 1) / step) + 1;
        }

        private static IntPtr CreateSurface(int width, int height, out IntPtr bits)
        {
            var header = new BITMAPINFOHEADER
            {
                Size = Marshal.SizeOf<BITMAPINFOHEADER>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
                Compression = 0,
                SizeImage = width * height * 4
            };
            return CreateDIBSection(IntPtr.Zero, ref header, 0, out bits, IntPtr.Zero, 0);
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int xSrc, int ySrc, uint rop);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER header, uint usage, out IntPtr bits, IntPtr section, uint offset);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int Size;
            public int Width;
            public int Height;
            public short Planes;
            public short BitCount;
            public int Compression;
            public int SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public int ClrUsed;
            public int ClrImportant;
        }
    }
}

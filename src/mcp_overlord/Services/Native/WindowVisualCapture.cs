using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace mcp_overlord.Services.Native
{
    /// <summary>
    /// Copies a window's composed visual: the frame, the title bar, and the client area.
    /// Pixels behind the window and windows that overlap it are not part of the image.
    /// The invisible resize margin is filled from the frame edge so that margin is not the desktop.
    /// </summary>
    internal sealed class WindowVisualCapture
    {
        private const int DwmExtendedFrameBounds = 9;
        private const int TextureFormatBgra = 87;
        private const int TextureFormatBgraTypeless = 90;
        private const int TextureFormatBgraSrgb = 91;
        private const int StagingUsage = 3;
        private const uint CpuAccessRead = 0x20000;
        private const int MapRead = 1;
        private const int CreateTextureIndex = 5;
        private const int GetDescriptionIndex = 10;
        private const int MapIndex = 14;
        private const int UnmapIndex = 15;
        private const int CopyResourceIndex = 47;

        private readonly object _gate = new object();
        private readonly ManualResetEventSlim _frameReady = new ManualResetEventSlim(false);
        private IDirect3DDevice? _winrtDevice;
        private IntPtr _device;
        private IntPtr _context;
        private IntPtr _staging;
        private TextureDescription _stagingDescription;
        private GraphicsCaptureItem? _item;
        private Direct3D11CaptureFramePool? _pool;
        private GraphicsCaptureSession? _session;
        private Direct3D11CaptureFrame? _latest;
        private IntPtr _hwnd;
        private int _generation;
        private int _captureWidth;
        private int _captureHeight;
        private int _offsetX;
        private int _offsetY;
        private CreateTextureDelegate? _createTexture;
        private MapDelegate? _map;
        private UnmapDelegate? _unmap;
        private CopyDelegate? _copyResource;

        /// <summary>
        /// Copies the window into <paramref name="destination"/>. The buffer is <paramref name="width"/> by <paramref name="height"/>, top-down, 32 bits, blue first.
        /// </summary>
        /// <param name="hwnd">Window to capture.</param>
        /// <param name="destination">Top-down pixel buffer.</param>
        /// <param name="width">Buffer width. This must be the current window width.</param>
        /// <param name="height">Buffer height. This must be the current window height.</param>
        /// <param name="onRowsReady">Called after each band of destination rows is final, from top to bottom. Null copies the whole frame before returning.</param>
        /// <returns>True when <paramref name="destination"/> holds the window frame and client area.</returns>
        public bool TryCopy(IntPtr hwnd, IntPtr destination, int width, int height, Action<int, int>? onRowsReady = null)
        {
            if (hwnd == IntPtr.Zero || destination == IntPtr.Zero || width <= 0 || height <= 0)
            {
                return false;
            }

            bool waitForFrame = false;
            lock (_gate)
            {
                try
                {
                    if (!EnsureSession(hwnd, width, height))
                    {
                        return false;
                    }

                    waitForFrame = _latest == null;
                }
                catch (Exception)
                {
                    CloseSession();
                    return false;
                }
            }

            if (waitForFrame && !_frameReady.Wait(300))
            {
                return false;
            }

            lock (_gate)
            {
                try
                {
                    Direct3D11CaptureFrame? frame = _latest;
                    if (frame == null || _context == IntPtr.Zero)
                    {
                        return false;
                    }

                    return ReadFrame(frame, destination, width, height, onRowsReady);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        private bool EnsureSession(IntPtr hwnd, int width, int height)
        {
            if (!TryReadPlacement(hwnd, width, height, out int offsetX, out int offsetY, out int captureWidth, out int captureHeight))
            {
                return false;
            }

            _offsetX = offsetX;
            _offsetY = offsetY;
            bool sameWindow = _session != null && _hwnd == hwnd && _captureWidth == captureWidth && _captureHeight == captureHeight;
            if (sameWindow)
            {
                return true;
            }

            if (!EnsureDevice())
            {
                return false;
            }

            CloseSession();
            _hwnd = hwnd;
            _captureWidth = captureWidth;
            _captureHeight = captureHeight;
            int generation = ++_generation;
            GraphicsCaptureItem item = CaptureItemFactory.CreateForWindow(hwnd);
            Direct3D11CaptureFramePool pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _winrtDevice!,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                new SizeInt32(captureWidth, captureHeight));
            pool.FrameArrived += (sender, _) => OnFrameArrived(sender, generation);
            GraphicsCaptureSession session = pool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = false;
            session.IsBorderRequired = false;
            _item = item;
            _pool = pool;
            _session = session;
            _frameReady.Reset();
            item.Closed += OnItemClosed;
            session.StartCapture();
            return true;
        }

        private void OnItemClosed(GraphicsCaptureItem sender, object args)
        {
            if (!Monitor.TryEnter(_gate))
            {
                return;
            }

            try
            {
                if (ReferenceEquals(_item, sender))
                {
                    CloseSession();
                }
            }
            finally
            {
                Monitor.Exit(_gate);
            }
        }

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, int generation)
        {
            Direct3D11CaptureFrame? next = null;
            try
            {
                next = sender.TryGetNextFrame();
                if (next == null)
                {
                    return;
                }

                lock (_gate)
                {
                    if (generation != _generation || !ReferenceEquals(_pool, sender))
                    {
                        next.Dispose();
                        return;
                    }

                    _latest?.Dispose();
                    _latest = next;
                    next = null;
                }

                _frameReady.Set();
            }
            catch (Exception)
            {
                next?.Dispose();
            }
        }

        private bool ReadFrame(Direct3D11CaptureFrame frame, IntPtr destination, int width, int height, Action<int, int>? onRowsReady)
        {
            if (frame.ContentSize.Width != _captureWidth || frame.ContentSize.Height != _captureHeight)
            {
                return false;
            }

            IDirect3DSurface surface = frame.Surface;
            Guid textureId = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
            IntPtr texture = surface.As<IDirect3DDxgiInterfaceAccess>().GetInterface(ref textureId);
            try
            {
                GetDescriptionDelegate getDescription = Vtable<GetDescriptionDelegate>(texture, GetDescriptionIndex);
                getDescription(texture, out TextureDescription description);
                bool bgra = description.Format == TextureFormatBgra
                    || description.Format == TextureFormatBgraTypeless
                    || description.Format == TextureFormatBgraSrgb;
                if (!bgra || description.SampleCount != 1)
                {
                    return false;
                }

                if (!EnsureStaging(description))
                {
                    return false;
                }

                _copyResource!(_context, _staging, texture);
                int mapResult = _map!(_context, _staging, 0, MapRead, 0, out MappedSubresource mapped);
                if (mapResult < 0 || mapped.Data == IntPtr.Zero)
                {
                    return false;
                }

                try
                {
                    PlaceFrame(mapped, destination, width, height, onRowsReady);
                }
                finally
                {
                    _unmap!(_context, _staging, 0);
                }

                return true;
            }
            finally
            {
                Marshal.Release(texture);
            }
        }

        private unsafe void PlaceFrame(MappedSubresource mapped, IntPtr destination, int width, int height, Action<int, int>? onRowsReady)
        {
            int offsetX = _offsetX;
            int offsetY = _offsetY;
            int copyWidth = _captureWidth;
            int copyHeight = _captureHeight;
            int sourceX = 0;
            int sourceY = 0;
            if (offsetX < 0)
            {
                sourceX = -offsetX;
                copyWidth -= sourceX;
                offsetX = 0;
            }

            if (offsetY < 0)
            {
                sourceY = -offsetY;
                copyHeight -= sourceY;
                offsetY = 0;
            }

            if (offsetX + copyWidth > width)
            {
                copyWidth = width - offsetX;
            }

            if (offsetY + copyHeight > height)
            {
                copyHeight = height - offsetY;
            }

            if (copyWidth <= 0 || copyHeight <= 0)
            {
                throw new InvalidOperationException("The window visual does not overlap the window rectangle.");
            }

            byte* source = (byte*)mapped.Data;
            byte* target = (byte*)destination;
            int targetStride = width * 4;
            const int BandRows = 160;
            for (int y = 0; y < height;)
            {
                int count = Math.Min(BandRows, height - y);
                for (int row = y; row < y + count; row++)
                {
                    WriteRow(source, mapped.RowPitch, sourceX, sourceY, target, targetStride, offsetX, offsetY, copyWidth, copyHeight, row);
                }

                onRowsReady?.Invoke(y, count);
                y += count;
            }
        }

        private static unsafe void WriteRow(
            byte* source,
            uint sourceStride,
            int sourceX,
            int sourceY,
            byte* target,
            int targetStride,
            int offsetX,
            int offsetY,
            int copyWidth,
            int copyHeight,
            int row)
        {
            int contentRow = row;
            if (contentRow < offsetY)
            {
                contentRow = offsetY;
            }
            else if (contentRow >= offsetY + copyHeight)
            {
                contentRow = offsetY + copyHeight - 1;
            }

            int sourceRow = sourceY + (contentRow - offsetY);
            byte* sourceLine = source + (sourceRow * sourceStride) + (sourceX * 4);
            byte* targetLine = target + (row * targetStride);
            Buffer.MemoryCopy(sourceLine, targetLine + (offsetX * 4), copyWidth * 4, copyWidth * 4);
            byte* left = targetLine + (offsetX * 4);
            for (int column = 0; column < offsetX; column++)
            {
                Buffer.MemoryCopy(left, targetLine + (column * 4), 4, 4);
            }

            byte* right = targetLine + ((offsetX + copyWidth - 1) * 4);
            for (int column = offsetX + copyWidth; column < targetStride / 4; column++)
            {
                Buffer.MemoryCopy(right, targetLine + (column * 4), 4, 4);
            }
        }

        private bool EnsureStaging(TextureDescription description)
        {
            if (_staging != IntPtr.Zero
                && _stagingDescription.Width == description.Width
                && _stagingDescription.Height == description.Height
                && _stagingDescription.Format == description.Format)
            {
                return true;
            }

            if (_staging != IntPtr.Zero)
            {
                Marshal.Release(_staging);
                _staging = IntPtr.Zero;
            }

            TextureDescription staging = description;
            staging.MipLevels = 1;
            staging.ArraySize = 1;
            staging.SampleCount = 1;
            staging.SampleQuality = 0;
            staging.Usage = StagingUsage;
            staging.BindFlags = 0;
            staging.CpuAccessFlags = CpuAccessRead;
            staging.MiscFlags = 0;
            int result = _createTexture!(_device, ref staging, IntPtr.Zero, out IntPtr texture);
            if (result < 0 || texture == IntPtr.Zero)
            {
                return false;
            }

            _staging = texture;
            _stagingDescription = staging;
            return true;
        }

        private bool EnsureDevice()
        {
            if (_device != IntPtr.Zero && _winrtDevice != null && _context != IntPtr.Zero)
            {
                return true;
            }

            int result = D3D11CreateDevice(
                IntPtr.Zero,
                1,
                IntPtr.Zero,
                0x20,
                IntPtr.Zero,
                0,
                7,
                out IntPtr device,
                out _,
                out IntPtr context);
            if (result < 0 || device == IntPtr.Zero || context == IntPtr.Zero)
            {
                return false;
            }

            _createTexture = Vtable<CreateTextureDelegate>(device, CreateTextureIndex);
            _map = Vtable<MapDelegate>(context, MapIndex);
            _unmap = Vtable<UnmapDelegate>(context, UnmapIndex);
            _copyResource = Vtable<CopyDelegate>(context, CopyResourceIndex);

            Guid dxgiId = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            result = Marshal.QueryInterface(device, in dxgiId, out IntPtr dxgiDevice);
            if (result < 0)
            {
                Marshal.Release(device);
                Marshal.Release(context);
                return false;
            }

            result = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out IntPtr graphicsDevice);
            Marshal.Release(dxgiDevice);
            if (result < 0)
            {
                Marshal.Release(device);
                Marshal.Release(context);
                return false;
            }

            _device = device;
            _context = context;
            _winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(graphicsDevice);
            return true;
        }

        private bool TryReadPlacement(
            IntPtr hwnd,
            int width,
            int height,
            out int offsetX,
            out int offsetY,
            out int captureWidth,
            out int captureHeight)
        {
            offsetX = 0;
            offsetY = 0;
            captureWidth = 0;
            captureHeight = 0;
            if (!GetWindowRect(hwnd, out RECT window))
            {
                return false;
            }

            int windowWidth = window.Right - window.Left;
            int windowHeight = window.Bottom - window.Top;
            if (windowWidth != width || windowHeight != height || windowWidth <= 0 || windowHeight <= 0)
            {
                return false;
            }

            if (DwmGetWindowAttribute(hwnd, DwmExtendedFrameBounds, out RECT extended, Marshal.SizeOf<RECT>()) != 0)
            {
                return false;
            }

            captureWidth = extended.Right - extended.Left;
            captureHeight = extended.Bottom - extended.Top;
            offsetX = extended.Left - window.Left;
            offsetY = extended.Top - window.Top;
            if (captureWidth < width / 2 || captureHeight < height / 2)
            {
                return false;
            }

            return true;
        }

        private void CloseSession()
        {
            _generation++;
            _latest?.Dispose();
            _latest = null;
            _session?.Dispose();
            _session = null;
            _pool?.Dispose();
            _pool = null;
            _item = null;
            _hwnd = IntPtr.Zero;
            _captureWidth = 0;
            _captureHeight = 0;
            _frameReady.Reset();
        }

        private static T Vtable<T>(IntPtr comObject, int index)
            where T : Delegate
        {
            IntPtr table = Marshal.ReadIntPtr(comObject);
            IntPtr method = Marshal.ReadIntPtr(table, index * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer<T>(method);
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int size);

        [DllImport("d3d11.dll")]
        private static extern int D3D11CreateDevice(
            IntPtr adapter,
            int driverType,
            IntPtr software,
            uint flags,
            IntPtr featureLevels,
            uint featureLevelCount,
            uint sdkVersion,
            out IntPtr device,
            out int featureLevel,
            out IntPtr immediateContext);

        [DllImport("d3d11.dll")]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TextureDescription
        {
            public uint Width;
            public uint Height;
            public uint MipLevels;
            public uint ArraySize;
            public uint Format;
            public uint SampleCount;
            public uint SampleQuality;
            public uint Usage;
            public uint BindFlags;
            public uint CpuAccessFlags;
            public uint MiscFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MappedSubresource
        {
            public IntPtr Data;
            public uint RowPitch;
            public uint DepthPitch;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateTextureDelegate(IntPtr self, ref TextureDescription description, IntPtr initialData, out IntPtr texture);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetDescriptionDelegate(IntPtr self, out TextureDescription description);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int MapDelegate(IntPtr self, IntPtr resource, uint subresource, uint mapType, uint mapFlags, out MappedSubresource mapped);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void UnmapDelegate(IntPtr self, IntPtr resource, uint subresource);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void CopyDelegate(IntPtr self, IntPtr destination, IntPtr source);

        [ComImport]
        [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDirect3DDxgiInterfaceAccess
        {
            IntPtr GetInterface(ref Guid iid);
        }
    }

    /// <summary>Creates a capture item for a window handle.</summary>
    internal static class CaptureItemFactory
    {
        private static readonly Guid ItemId = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");

        /// <summary>Creates the capture item for <paramref name="hwnd"/>.</summary>
        /// <param name="hwnd">Window handle.</param>
        /// <returns>The capture item.</returns>
        public static GraphicsCaptureItem CreateForWindow(IntPtr hwnd)
        {
            Guid interopId = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
            WindowsCreateString(
                "Windows.Graphics.Capture.GraphicsCaptureItem",
                "Windows.Graphics.Capture.GraphicsCaptureItem".Length,
                out IntPtr className);
            int result = RoGetActivationFactory(className, ref interopId, out IntPtr factory);
            WindowsDeleteString(className);
            if (result < 0)
            {
                Marshal.ThrowExceptionForHR(result);
            }

            IGraphicsCaptureItemInterop interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory);
            Marshal.Release(factory);
            Guid itemId = ItemId;
            IntPtr item = interop.CreateForWindow(hwnd, ref itemId);
            return GraphicsCaptureItem.FromAbi(item);
        }

        [DllImport("combase.dll")]
        private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string source, int length, out IntPtr handle);

        [DllImport("combase.dll")]
        private static extern int WindowsDeleteString(IntPtr handle);

        [DllImport("combase.dll")]
        private static extern int RoGetActivationFactory(IntPtr className, ref Guid iid, out IntPtr factory);

        [ComImport]
        [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
        {
            IntPtr CreateForWindow(IntPtr window, ref Guid iid);

            IntPtr CreateForMonitor(IntPtr monitor, ref Guid iid);
        }
    }
}

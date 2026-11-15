using System.Runtime.InteropServices;

namespace mcp_overlord.Services.Native
{
    /// <summary>
    /// Encodes a top-down BGRA buffer as an uncompressed PNG through snapenc.dll.
    /// A fresh multi-megabyte buffer pays for its pages on first use, so the next buffer is touched ahead of time.
    /// </summary>
    internal static class NativeStoredPng
    {
        private const int PageSize = 4096;

        private static readonly object LoadGate = new object();
        private static readonly object PoolGate = new object();
        private static StoredIntoDelegate? _storedInto;
        private static BeginDelegate? _begin;
        private static RowsDelegate? _rows;
        private static EndDelegate? _end;
        private static bool _loadAttempted;
        private static byte[]? _ready;
        private static int _warmingLength;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void InitDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int StoredIntoDelegate(IntPtr bgra, int width, int height, int stride, IntPtr dest, int capacity);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int BeginDelegate(IntPtr bgra, int width, int height, int stride, IntPtr dest, int capacity, ref SnapState state);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int RowsDelegate(ref SnapState state, int y, int count);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int EndDelegate(ref SnapState state);

        /// <summary>Native encoder state. Field order matches <c>SnapState</c> in snap.cpp.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct SnapState
        {
            public IntPtr Bgra;
            public IntPtr Png;
            public IntPtr Cursor;
            public int Width;
            public int Height;
            public int Stride;
            public int RowBytes;
            public int ZlibLength;
            public int Length;
            public uint Adler;
            public uint Crc;
            public int NextY;
        }

        /// <summary>A pinned PNG buffer whose rows can be encoded a band at a time.</summary>
        internal sealed class EncodeSession
        {
            public byte[] Png { get; set; } = Array.Empty<byte>();

            public SnapState State;

            public GCHandle Pin;

            public void Release()
            {
                if (Pin.IsAllocated)
                {
                    Pin.Free();
                }
            }
        }

        /// <summary>True when snapenc.dll loaded and initialized.</summary>
        public static bool IsAvailable
        {
            get
            {
                EnsureLoaded();
                return _storedInto != null && _begin != null && _rows != null && _end != null;
            }
        }

        /// <summary>
        /// Byte count of the stored PNG for this size. Zero when the image cannot be encoded.
        /// Keep this in step with <c>PngLength</c> in snap.cpp.
        /// </summary>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="height">Image height in pixels.</param>
        /// <returns>The PNG length, or 0.</returns>
        public static int ByteCount(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return 0;
            }

            int rowBytes = (width * 3) + 1;
            if (rowBytes > 65535)
            {
                return 0;
            }

            long raw = (long)rowBytes * height;
            long zlib = 2 + raw + ((long)height * 5) + 4;
            long png = 57 + zlib;
            if (png <= 0 || png > int.MaxValue)
            {
                return 0;
            }

            return (int)png;
        }

        /// <summary>
        /// Encodes <paramref name="bgra"/> into a PNG the caller owns.
        /// </summary>
        /// <param name="bgra">Top-down 32-bit pixels, blue first.</param>
        /// <param name="width">Image width.</param>
        /// <param name="height">Image height.</param>
        /// <param name="stride">Bytes from one row to the next. At least <paramref name="width"/> times 4.</param>
        /// <param name="png">The encoded file. Empty when encoding fails.</param>
        /// <returns>True when <paramref name="png"/> is a complete PNG.</returns>
        public static bool TryEncode(IntPtr bgra, int width, int height, int stride, out byte[] png)
        {
            png = Array.Empty<byte>();
            EnsureLoaded();
            StoredIntoDelegate? storedInto = _storedInto;
            if (storedInto == null || bgra == IntPtr.Zero)
            {
                return false;
            }

            int length = ByteCount(width, height);
            if (length <= 0 || stride < width * 4)
            {
                return false;
            }

            byte[] buffer = Rent(length);
            GCHandle pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                int written = storedInto(bgra, width, height, stride, pin.AddrOfPinnedObject(), buffer.Length);
                if (written != length)
                {
                    return false;
                }
            }
            finally
            {
                pin.Free();
            }

            png = buffer;
            ScheduleWarm(length);
            return true;
        }

        /// <summary>Encodes a managed BGRA buffer.</summary>
        /// <param name="bgra">Top-down 32-bit pixels, blue first.</param>
        /// <param name="width">Image width.</param>
        /// <param name="height">Image height.</param>
        /// <param name="stride">Bytes from one row to the next.</param>
        /// <param name="png">The encoded file. Empty when encoding fails.</param>
        /// <returns>True when <paramref name="png"/> is a complete PNG.</returns>
        public static bool TryEncode(byte[] bgra, int width, int height, int stride, out byte[] png)
        {
            if (bgra == null)
            {
                png = Array.Empty<byte>();
                return false;
            }

            GCHandle pin = GCHandle.Alloc(bgra, GCHandleType.Pinned);
            try
            {
                return TryEncode(pin.AddrOfPinnedObject(), width, height, stride, out png);
            }
            finally
            {
                pin.Free();
            }
        }

        /// <summary>
        /// Encodes the same PNG as <see cref="TryEncode(byte[], int, int, int, out byte[])"/>, one band of rows at a time.
        /// </summary>
        /// <param name="bgra">Top-down 32-bit pixels, blue first.</param>
        /// <param name="width">Image width.</param>
        /// <param name="height">Image height.</param>
        /// <param name="stride">Bytes from one row to the next.</param>
        /// <param name="bandRows">Rows encoded in each call. Must be at least 1.</param>
        /// <param name="png">The encoded file. Empty when encoding fails.</param>
        /// <returns>True when <paramref name="png"/> is a complete PNG.</returns>
        public static bool TryEncodeBands(byte[] bgra, int width, int height, int stride, int bandRows, out byte[] png)
        {
            png = Array.Empty<byte>();
            if (bgra == null || bandRows <= 0)
            {
                return false;
            }

            GCHandle pin = GCHandle.Alloc(bgra, GCHandleType.Pinned);
            try
            {
                EncodeSession? session = TryBegin(pin.AddrOfPinnedObject(), width, height, stride);
                if (session == null)
                {
                    return false;
                }

                for (int y = 0; y < height;)
                {
                    int count = Math.Min(bandRows, height - y);
                    if (!EncodeRows(session, y, count))
                    {
                        session.Release();
                        return false;
                    }

                    y += count;
                }

                return TryFinish(session, out png);
            }
            finally
            {
                pin.Free();
            }
        }

        /// <summary>
        /// Starts a PNG in a warm buffer. The caller encodes rows, then <see cref="TryFinish"/>.
        /// </summary>
        /// <param name="bgra">Top-down 32-bit pixels.</param>
        /// <param name="width">Image width.</param>
        /// <param name="height">Image height.</param>
        /// <param name="stride">Bytes from one row to the next.</param>
        /// <returns>The session, or null when encoding cannot start.</returns>
        public static EncodeSession? TryBegin(IntPtr bgra, int width, int height, int stride)
        {
            EnsureLoaded();
            if (_begin == null || bgra == IntPtr.Zero)
            {
                return null;
            }

            int length = ByteCount(width, height);
            if (length <= 0 || stride < width * 4)
            {
                return null;
            }

            byte[] buffer = Rent(length);
            var session = new EncodeSession
            {
                Png = buffer,
                Pin = GCHandle.Alloc(buffer, GCHandleType.Pinned)
            };
            int started = _begin(bgra, width, height, stride, session.Pin.AddrOfPinnedObject(), buffer.Length, ref session.State);
            if (started == 0 || session.State.Length != length)
            {
                session.Release();
                return null;
            }

            return session;
        }

        /// <summary>Encodes <paramref name="count"/> rows starting at <paramref name="y"/>. Rows must be in order.</summary>
        /// <param name="session">Session from <see cref="TryBegin"/>.</param>
        /// <param name="y">First row.</param>
        /// <param name="count">How many rows.</param>
        /// <returns>True when those rows were written.</returns>
        public static bool EncodeRows(EncodeSession session, int y, int count)
        {
            if (session == null || _rows == null)
            {
                return false;
            }

            return _rows(ref session.State, y, count) != 0;
        }

        /// <summary>Finishes the PNG and releases the pin.</summary>
        /// <param name="session">Session from <see cref="TryBegin"/>.</param>
        /// <param name="png">The encoded file. Empty when finishing fails.</param>
        /// <returns>True when <paramref name="png"/> is complete.</returns>
        public static bool TryFinish(EncodeSession session, out byte[] png)
        {
            png = Array.Empty<byte>();
            if (session == null || _end == null)
            {
                session?.Release();
                return false;
            }

            try
            {
                int written = _end(ref session.State);
                if (written != session.Png.Length)
                {
                    return false;
                }

                png = session.Png;
                ScheduleWarm(session.Png.Length);
                return true;
            }
            finally
            {
                session.Release();
            }
        }

        private static void EnsureLoaded()
        {
            if (_loadAttempted)
            {
                return;
            }

            lock (LoadGate)
            {
                if (_loadAttempted)
                {
                    return;
                }

                _loadAttempted = true;
                try
                {
                    string path = Path.Combine(AppContext.BaseDirectory, "snapenc.dll");
                    if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out IntPtr library))
                    {
                        return;
                    }

                    if (!NativeLibrary.TryGetExport(library, "snap_init", out IntPtr init)
                        || !NativeLibrary.TryGetExport(library, "snap_stored_into", out IntPtr stored)
                        || !NativeLibrary.TryGetExport(library, "snap_begin", out IntPtr begin)
                        || !NativeLibrary.TryGetExport(library, "snap_rows", out IntPtr rows)
                        || !NativeLibrary.TryGetExport(library, "snap_end", out IntPtr end))
                    {
                        return;
                    }

                    Marshal.GetDelegateForFunctionPointer<InitDelegate>(init)();
                    _storedInto = Marshal.GetDelegateForFunctionPointer<StoredIntoDelegate>(stored);
                    _begin = Marshal.GetDelegateForFunctionPointer<BeginDelegate>(begin);
                    _rows = Marshal.GetDelegateForFunctionPointer<RowsDelegate>(rows);
                    _end = Marshal.GetDelegateForFunctionPointer<EndDelegate>(end);
                }
                catch (Exception)
                {
                    _storedInto = null;
                }
            }
        }

        private static byte[] Rent(int length)
        {
            lock (PoolGate)
            {
                if (_ready != null && _ready.Length == length)
                {
                    byte[] ready = _ready;
                    _ready = null;
                    return ready;
                }
            }

            return GC.AllocateUninitializedArray<byte>(length);
        }

        private static void ScheduleWarm(int length)
        {
            lock (PoolGate)
            {
                if (_warmingLength == length || (_ready != null && _ready.Length == length))
                {
                    return;
                }

                _warmingLength = length;
            }

            ThreadPool.UnsafeQueueUserWorkItem(
                _ =>
                {
                    Warm(length);
                },
                null);
        }

        private static void Warm(int length)
        {
            try
            {
                byte[] buffer = GC.AllocateUninitializedArray<byte>(length);
                Touch(buffer);
                lock (PoolGate)
                {
                    if (_ready == null || _ready.Length != length)
                    {
                        _ready = buffer;
                    }

                    if (_warmingLength == length)
                    {
                        _warmingLength = 0;
                    }
                }
            }
            catch (Exception)
            {
                lock (PoolGate)
                {
                    if (_warmingLength == length)
                    {
                        _warmingLength = 0;
                    }
                }
            }
        }

        private static void Touch(byte[] buffer)
        {
            for (int index = 0; index < buffer.Length; index += PageSize)
            {
                buffer[index] = 0;
            }

            if (buffer.Length > 0)
            {
                buffer[buffer.Length - 1] = 0;
            }
        }
    }
}

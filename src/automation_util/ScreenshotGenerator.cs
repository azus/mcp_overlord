using System;
using System.Drawing;
using System.Runtime.InteropServices;

public class ScreenshotGenerator
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetClientRect(IntPtr hWnd, ref RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetWindowRect(IntPtr hWnd, ref RECT lpRect);

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

    [DllImport("gdi32.dll")]
    static extern bool BitBlt(IntPtr hdc, int x, int y, int width, int height, IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

    /// <summary>PrintWindow flag that asks the window to render its full contents, including DirectX surfaces.</summary>
    public const uint PW_RENDERFULLCONTENT = 2;

    private const uint SRCCOPY = 0x00CC0020;

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr hdc);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// Captures the whole window with <c>PrintWindow</c> and a flags value of 0.
    /// The bitmap has the size of <c>GetWindowRect</c>, so the border and the title bar are included.
    /// </summary>
    /// <param name="windowHandle">Window to capture.</param>
    /// <returns>A bitmap of the window rectangle.</returns>
    public virtual Bitmap CaptureWindow(IntPtr windowHandle)
    {
        return CaptureWindow(windowHandle, 0);
    }

    /// <summary>
    /// Captures the whole window with <c>PrintWindow</c>.
    /// The bitmap has the size of <c>GetWindowRect</c>. <c>PrintWindow</c> draws from that outer corner.
    /// </summary>
    /// <param name="windowHandle">Window to capture.</param>
    /// <param name="flags">PrintWindow flags. Use <see cref="PW_RENDERFULLCONTENT"/> for the full-content retry.</param>
    /// <returns>A bitmap of the window rectangle.</returns>
    public virtual Bitmap CaptureWindow(IntPtr windowHandle, uint flags)
    {
        RECT rect = ReadWindowRect(windowHandle);
        int width = rect.right - rect.left;
        int height = rect.bottom - rect.top;

        IntPtr hdcSrc = GetDC(windowHandle);
        IntPtr hdcDest = CreateCompatibleDC(hdcSrc);
        IntPtr hBitmap = CreateCompatibleBitmap(hdcSrc, width, height);
        IntPtr hOld = SelectObject(hdcDest, hBitmap);

        PrintWindow(windowHandle, hdcDest, flags);

        Bitmap bmp = Bitmap.FromHbitmap(hBitmap);

        SelectObject(hdcDest, hOld);
        DeleteObject(hBitmap);
        DeleteDC(hdcDest);
        ReleaseDC(windowHandle, hdcSrc);

        return bmp;
    }

    /// <summary>
    /// Captures the whole window by blitting the screen over <c>GetWindowRect</c>.
    /// This only sees pixels that are actually visible and not covered. The origin and size match <see cref="CaptureWindow(IntPtr, uint)"/>.
    /// </summary>
    /// <param name="windowHandle">Window to capture.</param>
    /// <returns>A bitmap of the window rectangle as it appears on screen.</returns>
    public virtual Bitmap CaptureWindowFromScreen(IntPtr windowHandle)
    {
        RECT rect = ReadWindowRect(windowHandle);
        int width = rect.right - rect.left;
        int height = rect.bottom - rect.top;

        IntPtr hdcScreen = GetDC(IntPtr.Zero);
        IntPtr hdcDest = CreateCompatibleDC(hdcScreen);
        IntPtr hBitmap = CreateCompatibleBitmap(hdcScreen, width, height);
        IntPtr hOld = SelectObject(hdcDest, hBitmap);

        BitBlt(hdcDest, 0, 0, width, height, hdcScreen, rect.left, rect.top, SRCCOPY);

        Bitmap bmp = Bitmap.FromHbitmap(hBitmap);

        SelectObject(hdcDest, hOld);
        DeleteObject(hBitmap);
        DeleteDC(hdcDest);
        ReleaseDC(IntPtr.Zero, hdcScreen);

        return bmp;
    }

    private static RECT ReadWindowRect(IntPtr windowHandle)
    {
        RECT rect = new RECT();
        if (!GetWindowRect(windowHandle, ref rect))
        {
            throw new InvalidOperationException("The window rectangle could not be read.");
        }

        int width = rect.right - rect.left;
        int height = rect.bottom - rect.top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("The window has no size.");
        }

        return rect;
    }

    /// <summary>
    /// Captures the client area by blitting the screen at the window's client origin.
    /// This only sees pixels that are actually visible and not covered.
    /// </summary>
    /// <param name="windowHandle">Window to capture.</param>
    /// <returns>A bitmap of the client area as it appears on screen.</returns>
    public virtual Bitmap CaptureClientFromScreen(IntPtr windowHandle)
    {
        RECT rect = new RECT();
        GetClientRect(windowHandle, ref rect);
        int width = rect.right - rect.left;
        int height = rect.bottom - rect.top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("The window client area has no size.");
        }

        POINT origin = new POINT();
        ClientToScreen(windowHandle, ref origin);

        IntPtr hdcScreen = GetDC(IntPtr.Zero);
        IntPtr hdcDest = CreateCompatibleDC(hdcScreen);
        IntPtr hBitmap = CreateCompatibleBitmap(hdcScreen, width, height);
        IntPtr hOld = SelectObject(hdcDest, hBitmap);

        BitBlt(hdcDest, 0, 0, width, height, hdcScreen, origin.X, origin.Y, SRCCOPY);

        Bitmap bmp = Bitmap.FromHbitmap(hBitmap);

        SelectObject(hdcDest, hOld);
        DeleteObject(hBitmap);
        DeleteDC(hdcDest);
        ReleaseDC(IntPtr.Zero, hdcScreen);

        return bmp;
    }
}

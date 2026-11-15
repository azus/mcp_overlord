namespace mcp_overlord.Services
{
    /// <summary>
    /// Outer window rectangle from <c>GetWindowRect</c> and the client origin inside it.
    /// Coordinates are physical pixels. <paramref name="ClientOffsetX"/> is <c>ClientToScreen(0, 0).X - FrameX</c>.
    /// </summary>
    /// <param name="FrameX">Left edge of the window, in screen pixels.</param>
    /// <param name="FrameY">Top edge of the window, in screen pixels.</param>
    /// <param name="FrameWidth">Window width.</param>
    /// <param name="FrameHeight">Window height.</param>
    /// <param name="ClientOffsetX">Client origin X measured from <paramref name="FrameX"/>.</param>
    /// <param name="ClientOffsetY">Client origin Y measured from <paramref name="FrameY"/>.</param>
    /// <param name="ClientWidth">Client width.</param>
    /// <param name="ClientHeight">Client height.</param>
    public readonly record struct WindowFrameGeometry(
        int FrameX,
        int FrameY,
        int FrameWidth,
        int FrameHeight,
        int ClientOffsetX,
        int ClientOffsetY,
        int ClientWidth,
        int ClientHeight);
}

namespace mcp_overlord.Hosting
{
    /// <summary>Short operating rules sent to the model as the MCP server instructions.</summary>
    public static class ServerInstructions
    {
        /// <summary>Instruction string registered with the MCP server.</summary>
        public const string Text =
            "You control Windows applications through this server. " +
            "Call list_applications to learn the names. " +
            "Call take_screenshot before clicking. The image is the whole window, including the border and the title bar. A window that overlaps the target is not part of the image, and what is behind the window is not part of the image. Click coordinates are pixels of that image, origin at the top left of the window. " +
            "coordinateSpace image (the default) uses those pixels, including scale and crop. coordinateSpace window_rect uses physical pixels of the window rectangle and ignores scale. " +
            "A point outside the window is rejected and no mouse input is sent. " +
            "The server focuses the target window before input. " +
            "Launch only works for entries that have an executable. " +
            "Read get_application_description when the list entry says a description exists. " +
            "Call is_locked to see whether the workstation screen is locked. " +
            "lock locks the screen. There is no unlock tool. " +
            "While the screen is locked, mouse and keyboard tools fail and say why.";
    }
}

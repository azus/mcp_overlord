namespace mcp_overlord.Configuration
{
    /// <summary>
    /// File type of an MCP Overlord configuration document.
    /// The contents are JSON. The extension is <c>.overlord</c>.
    /// </summary>
    public static class ConfigFile
    {
        /// <summary>Document extension, including the leading dot.</summary>
        public const string Extension = ".overlord";

        /// <summary>Name offered when a new document is saved.</summary>
        public const string DefaultFileName = "config.overlord";

        /// <summary>Open and save dialog filter. All files remains so an older <c>.json</c> configuration can still be chosen.</summary>
        public const string DialogFilter = "Overlord configuration (*.overlord)|*.overlord|All files (*.*)|*.*";
    }
}

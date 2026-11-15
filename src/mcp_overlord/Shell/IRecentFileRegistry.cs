namespace mcp_overlord.Shell
{
    /// <summary>Records a configuration file in the Start menu recent list.</summary>
    public interface IRecentFileRegistry
    {
        /// <summary>
        /// Adds <paramref name="path"/> to the recent list. A shell rejection does not fail the caller.
        /// </summary>
        /// <param name="path">Configuration file that was loaded or saved.</param>
        void Add(string path);
    }
}

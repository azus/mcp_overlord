using System.Windows.Shell;

namespace mcp_overlord.Shell
{
    /// <summary>Recent-file list backed by the shell jump list.</summary>
    public sealed class ShellRecentFileRegistry : IRecentFileRegistry
    {
        /// <inheritdoc />
        public void Add(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                JumpList.AddToRecentCategory(path);
            }
            catch (Exception)
            {
                // Windows rejects a path the file association does not own. The document is already loaded or saved.
            }
        }
    }
}

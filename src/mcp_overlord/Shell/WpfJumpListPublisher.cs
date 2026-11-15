using System.Windows;
using System.Windows.Shell;

namespace mcp_overlord.Shell
{
    /// <summary>Publishes the Start menu jump list for this process.</summary>
    public static class WpfJumpListPublisher
    {
        /// <summary>
        /// Registers Run as Administrator, Uninstall when a product code is present, and the recent-file category.
        /// </summary>
        /// <param name="executablePath">Path of this process. Nothing is published when it is missing.</param>
        /// <param name="productCode">Installed product code, or null.</param>
        public static void Publish(string? executablePath, string? productCode)
        {
            if (Application.Current == null || string.IsNullOrWhiteSpace(executablePath))
            {
                return;
            }

            var jumpList = new JumpList
            {
                ShowFrequentCategory = false,
                ShowRecentCategory = true
            };

            foreach (JumpTaskDescriptor task in JumpListBuilder.Build(executablePath, productCode))
            {
                jumpList.JumpItems.Add(new JumpTask
                {
                    Title = task.Title,
                    Description = task.Description,
                    ApplicationPath = task.ApplicationPath,
                    Arguments = task.Arguments,
                    IconResourcePath = task.IconResourcePath,
                    IconResourceIndex = task.IconResourceIndex
                });
            }

            JumpList.SetJumpList(Application.Current, jumpList);
            jumpList.Apply();
        }
    }
}

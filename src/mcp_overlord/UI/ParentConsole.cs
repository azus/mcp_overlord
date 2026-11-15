using System.Runtime.InteropServices;

namespace mcp_overlord.UI
{
    /// <summary>Writes help to the parent console when the process was started from a terminal.</summary>
    public static class ParentConsole
    {
        private const int ATTACH_PARENT_PROCESS = -1;

        /// <summary>
        /// Attaches to the parent console and writes a line.
        /// </summary>
        /// <param name="text">Usage text.</param>
        public static void WriteLine(string text)
        {
            AttachConsole(ATTACH_PARENT_PROCESS);
            Console.WriteLine(text);
        }

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);
    }
}

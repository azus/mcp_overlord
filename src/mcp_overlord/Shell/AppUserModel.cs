using System.Runtime.InteropServices;

namespace mcp_overlord.Shell
{
    /// <summary>
    /// Sets the process AppUserModelID so the jump list attaches to the Start shortcut.
    /// Call this before any window is created.
    /// </summary>
    public static class AppUserModel
    {
        /// <summary>Applies <see cref="JumpListBuilder.AppUserModelId"/> to this process.</summary>
        public static void SetCurrentProcess()
        {
            SetCurrentProcessExplicitAppUserModelID(JumpListBuilder.AppUserModelId);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appUserModelId);
    }
}

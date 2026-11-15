namespace mcp_overlord.UI
{
    /// <summary>Runs a callback on the UI thread. Tests run it inline.</summary>
    public interface IUiInvoker
    {
        /// <summary>Runs <paramref name="action"/> on the UI thread.</summary>
        /// <param name="action">Work that touches the view model collections.</param>
        void Invoke(Action action);
    }

    /// <summary>Invoker that runs the action immediately. Used by tests.</summary>
    public sealed class InlineUiInvoker : IUiInvoker
    {
        /// <inheritdoc />
        public void Invoke(Action action)
        {
            action();
        }
    }

    /// <summary>The operator's answer when a document has unsaved changes.</summary>
    public enum SaveChangesChoice
    {
        /// <summary>Write the file, then continue.</summary>
        Save,

        /// <summary>Drop the changes and continue.</summary>
        Discard,

        /// <summary>Stay where we are.</summary>
        Cancel
    }

    /// <summary>File dialogs, confirmations, and the configuration editors.</summary>
    public interface IUserInteraction
    {
        /// <summary>Folder remembered for the next configuration dialog.</summary>
        string? LastConfigDirectory { get; set; }

        /// <summary>Shows a message.</summary>
        /// <param name="title">Dialog title.</param>
        /// <param name="message">Dialog body.</param>
        void ShowMessage(string title, string message);

        /// <summary>Asks for a JSON configuration file to open.</summary>
        /// <returns>The path, or null when the operator cancels.</returns>
        string? PickConfigFile();

        /// <summary>
        /// Asks where to save a JSON configuration file.
        /// </summary>
        /// <param name="suggestedName">File name offered in the dialog.</param>
        /// <returns>The path, or null when the operator cancels.</returns>
        string? PickSaveConfigFile(string suggestedName);

        /// <summary>
        /// Asks whether to save <paramref name="displayName"/> before continuing.
        /// </summary>
        /// <param name="displayName">File name, or New.</param>
        /// <returns>Save, discard, or cancel.</returns>
        SaveChangesChoice AskSaveChanges(string displayName);

        /// <summary>
        /// Asks a yes/no question.
        /// </summary>
        /// <param name="title">Dialog title.</param>
        /// <param name="message">Dialog body.</param>
        /// <param name="confirmText">Label of the accepting button.</param>
        /// <returns>True when the operator accepts.</returns>
        bool Confirm(string title, string message, string confirmText = "OK");

        /// <summary>
        /// Shows the application editor. The view model applies the document when the operator presses OK.
        /// </summary>
        /// <param name="viewModel">Editor state.</param>
        /// <returns>True when the operator accepts a valid application.</returns>
        bool EditApplication(ApplicationEditorViewModel viewModel);

        /// <summary>
        /// Shows the server settings editor.
        /// </summary>
        /// <param name="viewModel">Editor state.</param>
        /// <returns>True when the operator accepts valid settings.</returns>
        bool EditServerSettings(ServerSettingsViewModel viewModel);

        /// <summary>Asks for an executable.</summary>
        /// <returns>The path, or null when the operator cancels.</returns>
        string? PickExecutableFile();

        /// <summary>Asks for a folder.</summary>
        /// <returns>The path, or null when the operator cancels.</returns>
        string? PickFolder();

        /// <summary>Asks for a PKCS#12 certificate.</summary>
        /// <returns>The path, or null when the operator cancels.</returns>
        string? PickCertificateFile();

        /// <summary>Asks where to save an image from the log.</summary>
        /// <param name="suggestedName">File name offered in the dialog.</param>
        /// <returns>The path, or null when the operator cancels.</returns>
        string? PickSaveImageFile(string suggestedName);

        /// <summary>Shows the connection check and runs it.</summary>
        /// <param name="viewModel">Report state. The dialog starts the check.</param>
        void ShowConnectionReport(ConnectionReportViewModel viewModel);
    }

    /// <summary>Copies text to the clipboard.</summary>
    public interface IClipboardService
    {
        /// <summary>Places text on the clipboard.</summary>
        /// <param name="text">Text to copy.</param>
        void SetText(string text);
    }
}

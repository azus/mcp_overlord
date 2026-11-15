using System.Windows;

namespace mcp_overlord.UI
{
    /// <summary>Dark confirmation and message dialog.</summary>
    public partial class PromptDialog : Window
    {
        private SaveChangesChoice _choice = SaveChangesChoice.Cancel;

        /// <summary>
        /// Creates the dialog.
        /// </summary>
        public PromptDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Shows a message or a yes/no question.
        /// </summary>
        /// <param name="owner">Owner window. Null centers on the screen.</param>
        /// <param name="title">Title.</param>
        /// <param name="message">Body.</param>
        /// <param name="confirm">True to offer Cancel. False shows only OK.</param>
        /// <param name="confirmText">Label of the accepting button.</param>
        /// <returns>True when the operator accepts.</returns>
        public static bool Show(Window? owner, string title, string message, bool confirm, string confirmText = "OK")
        {
            return ShowChoice(owner, title, message, confirm, false, confirmText) == SaveChangesChoice.Save;
        }

        /// <summary>
        /// Asks whether to save, discard, or cancel.
        /// </summary>
        /// <param name="owner">Owner window. Null centers on the screen.</param>
        /// <param name="title">Title.</param>
        /// <param name="message">Body.</param>
        /// <returns>The operator's choice. Closing the window is cancel.</returns>
        public static SaveChangesChoice ShowSaveChanges(Window? owner, string title, string message)
        {
            return ShowChoice(owner, title, message, true, true, "Save");
        }

        private static SaveChangesChoice ShowChoice(
            Window? owner,
            string title,
            string message,
            bool confirm,
            bool discard,
            string confirmText)
        {
            var dialog = new PromptDialog();
            dialog.Title = title;
            dialog.MessageText.Text = message;
            dialog.ConfirmButton.Content = confirmText;
            dialog.CancelButton.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
            dialog.DiscardButton.Visibility = discard ? Visibility.Visible : Visibility.Collapsed;
            if (owner != null)
            {
                dialog.Owner = owner;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            dialog.ShowDialog();
            return dialog._choice;
        }

        private void OnConfirm(object sender, RoutedEventArgs e)
        {
            _choice = SaveChangesChoice.Save;
            DialogResult = true;
        }

        private void OnDiscard(object sender, RoutedEventArgs e)
        {
            _choice = SaveChangesChoice.Discard;
            DialogResult = true;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            _choice = SaveChangesChoice.Cancel;
            DialogResult = false;
        }
    }
}

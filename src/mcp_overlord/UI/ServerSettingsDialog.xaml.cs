using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;

namespace mcp_overlord.UI
{
    /// <summary>Modal editor for the server block of the configuration file.</summary>
    public partial class ServerSettingsDialog : Window
    {
        private readonly ServerSettingsViewModel _viewModel;
        private readonly IUserInteraction _interaction;
        private bool _updatingSecret;

        /// <summary>
        /// Creates the dialog.
        /// </summary>
        /// <param name="viewModel">Editor state.</param>
        /// <param name="interaction">Certificate file picker.</param>
        public ServerSettingsDialog(ServerSettingsViewModel viewModel, IUserInteraction interaction)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
            InitializeComponent();
            DataContext = _viewModel;
            ApiKeyBox.Password = _viewModel.ApiKey;
            CertificatePasswordBox.Password = _viewModel.CertificatePassword;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            SourceInitialized += (_, _) => DarkTitleBar.Apply(new WindowInteropHelper(this).Handle);
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (_viewModel.TryCommit())
            {
                DialogResult = true;
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void OnGenerate(object sender, RoutedEventArgs e)
        {
            _viewModel.GenerateApiKey();
        }

        private void OnToggleApiKey(object sender, RoutedEventArgs e)
        {
            _viewModel.ToggleApiKeyVisibility();
        }

        private void OnBrowseCertificate(object sender, RoutedEventArgs e)
        {
            string? path = _interaction.PickCertificateFile();
            if (!string.IsNullOrEmpty(path))
            {
                _viewModel.CertificatePath = path;
                _viewModel.IncludeCertificatePath = true;
            }
        }

        private void OnApiKeyPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_updatingSecret)
            {
                return;
            }

            _viewModel.ApiKey = ApiKeyBox.Password;
        }

        private void OnCertificatePasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_updatingSecret)
            {
                return;
            }

            _viewModel.CertificatePassword = CertificatePasswordBox.Password;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ServerSettingsViewModel.ApiKey) && ApiKeyBox.Password != _viewModel.ApiKey)
            {
                _updatingSecret = true;
                ApiKeyBox.Password = _viewModel.ApiKey;
                _updatingSecret = false;
            }
        }
    }
}

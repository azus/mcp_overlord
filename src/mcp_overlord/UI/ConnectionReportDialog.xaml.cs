using System.Windows;
using System.Windows.Interop;

namespace mcp_overlord.UI
{
    /// <summary>Modal report for the connection check.</summary>
    public partial class ConnectionReportDialog : Window
    {
        private readonly ConnectionReportViewModel _viewModel;

        /// <summary>
        /// Creates the dialog and starts the check when it is shown.
        /// </summary>
        /// <param name="viewModel">Report state.</param>
        public ConnectionReportDialog(ConnectionReportViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            InitializeComponent();
            DataContext = _viewModel;
            SourceInitialized += (_, _) => DarkTitleBar.Apply(new WindowInteropHelper(this).Handle);
            Loaded += async (_, _) => await _viewModel.RunAsync();
            Closed += (_, _) => _viewModel.Cancel();
        }
    }
}

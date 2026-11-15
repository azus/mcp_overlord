using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using mcp_overlord.Diagnostics;

namespace mcp_overlord.UI
{
    /// <summary>
    /// Runs a connection check and holds the report the dialog shows.
    /// </summary>
    public sealed class ConnectionReportViewModel : INotifyPropertyChanged
    {
        private readonly Func<IProgress<string>, CancellationToken, Task<ConnectionReport>> _run;
        private readonly IClipboardService _clipboard;
        private readonly RelayCommand _runCommand;
        private CancellationTokenSource _cancellation = new CancellationTokenSource();
        private string _statusText = "Ready.";
        private string _reportText = "";
        private bool _isRunning;

        /// <summary>
        /// Creates the report.
        /// </summary>
        /// <param name="run">The check. The progress receives a status line.</param>
        /// <param name="clipboard">Copies the report.</param>
        public ConnectionReportViewModel(
            Func<IProgress<string>, CancellationToken, Task<ConnectionReport>> run,
            IClipboardService clipboard)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
            Steps = new ObservableCollection<ConnectionCheckStep>();
            _runCommand = new RelayCommand(() => RunAsync(), () => !IsRunning);
            CopyCommand = new RelayCommand(CopyReport);
        }

        /// <inheritdoc />
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Pass and fail lines.</summary>
        public ObservableCollection<ConnectionCheckStep> Steps { get; }

        /// <summary>Starts the check again.</summary>
        public ICommand RunCommand
        {
            get { return _runCommand; }
        }

        /// <summary>Copies the full report.</summary>
        public ICommand CopyCommand { get; }

        /// <summary>What the check is doing, or the outcome.</summary>
        public string StatusText
        {
            get { return _statusText; }
            private set { Set(ref _statusText, value); }
        }

        /// <summary>Full report, including insights.</summary>
        public string ReportText
        {
            get { return _reportText; }
            private set { Set(ref _reportText, value); }
        }

        /// <summary>True while a check is in progress.</summary>
        public bool IsRunning
        {
            get { return _isRunning; }
            private set
            {
                if (Set(ref _isRunning, value))
                {
                    _runCommand.RaiseCanExecuteChanged();
                    OnPropertyChanged(nameof(CanRun));
                }
            }
        }

        /// <summary>True when Run again can be pressed.</summary>
        public bool CanRun
        {
            get { return !IsRunning; }
        }

        /// <summary>Stops the check. The dialog calls this when it closes.</summary>
        public void Cancel()
        {
            _cancellation.Cancel();
        }

        /// <summary>Runs the check and fills the steps and the report.</summary>
        public async Task RunAsync()
        {
            if (IsRunning)
            {
                return;
            }

            if (_cancellation.IsCancellationRequested)
            {
                _cancellation.Dispose();
                _cancellation = new CancellationTokenSource();
            }

            IsRunning = true;
            StatusText = "Checking...";
            Steps.Clear();
            ReportText = "";
            try
            {
                var progress = new Progress<string>(text => StatusText = text);
                ConnectionReport report = await _run(progress, _cancellation.Token);
                Apply(report);
                StatusText = "Finished.";
            }
            catch (OperationCanceledException)
            {
                StatusText = "Cancelled.";
            }
            catch (Exception exception)
            {
                StatusText = "The check failed.";
                ReportText = exception.Message;
            }
            finally
            {
                IsRunning = false;
            }
        }

        private void Apply(ConnectionReport report)
        {
            Steps.Clear();
            foreach (ConnectionCheckStep step in report.Steps)
            {
                Steps.Add(step);
            }

            ReportText = report.Text ?? "";
        }

        private void CopyReport()
        {
            if (!string.IsNullOrEmpty(ReportText))
            {
                _clipboard.SetText(ReportText);
            }
        }

        private bool Set(ref string field, string value, [CallerMemberName] string? propertyName = null)
        {
            if (string.Equals(field, value, StringComparison.Ordinal))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private bool Set(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
        {
            if (field == value)
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

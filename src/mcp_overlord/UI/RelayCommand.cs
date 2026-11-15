using System.Windows.Input;

namespace mcp_overlord.UI
{
    /// <summary>A command that runs a synchronous or asynchronous action.</summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action? _execute;
        private readonly Func<Task>? _executeAsync;
        private readonly Func<bool> _canExecute;

        /// <summary>
        /// Creates a synchronous command.
        /// </summary>
        /// <param name="execute">Action to run.</param>
        /// <param name="canExecute">Enabled check. Null means always enabled.</param>
        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute ?? (() => true);
        }

        /// <summary>
        /// Creates an asynchronous command.
        /// </summary>
        /// <param name="executeAsync">Action to run.</param>
        /// <param name="canExecute">Enabled check. Null means always enabled.</param>
        public RelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
        {
            _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
            _canExecute = canExecute ?? (() => true);
        }

        /// <inheritdoc />
        public event EventHandler? CanExecuteChanged;

        /// <inheritdoc />
        public bool CanExecute(object? parameter)
        {
            return _canExecute();
        }

        /// <inheritdoc />
        public async void Execute(object? parameter)
        {
            if (_executeAsync != null)
            {
                await _executeAsync();
                return;
            }

            _execute?.Invoke();
        }

        /// <summary>Tells bindings to read <see cref="CanExecute"/> again.</summary>
        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

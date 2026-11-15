using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using mcp_overlord.Services;

namespace mcp_overlord.UI
{
    /// <summary>
    /// Modal editor for one application. Accepts a dropped shortcut and a dragged window target.
    /// </summary>
    public partial class ApplicationEditorDialog : Window
    {
        private readonly ApplicationEditorViewModel _viewModel;
        private readonly IShortcutReader _shortcuts;
        private readonly IWindowInspector _inspector;
        private readonly IUserInteraction _interaction;
        private WindowHighlightOverlay? _overlay;
        private bool _finding;
        private IntPtr _hoverWindow;

        /// <summary>
        /// Creates the dialog.
        /// </summary>
        /// <param name="viewModel">Editor state.</param>
        /// <param name="shortcuts">Reads a dropped .lnk file.</param>
        /// <param name="inspector">Reads the window under the cursor.</param>
        /// <param name="interaction">File and folder pickers.</param>
        public ApplicationEditorDialog(
            ApplicationEditorViewModel viewModel,
            IShortcutReader shortcuts,
            IWindowInspector inspector,
            IUserInteraction interaction)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
            _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
            _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
            InitializeComponent();
            DataContext = _viewModel;
            SourceInitialized += (_, _) => OnSourceInitialized();
            Closed += (_, _) => _overlay?.Close();
        }

        private void OnSourceInitialized()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            DarkTitleBar.Apply(handle);
            ElevatedFileDrop.EnableIfElevated(this, ApplyDroppedFile);
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

        private void OnBrowseExecutable(object sender, RoutedEventArgs e)
        {
            string? path = _interaction.PickExecutableFile();
            if (!string.IsNullOrEmpty(path))
            {
                _viewModel.Executable = path;
            }
        }

        private void OnBrowseWorkingDirectory(object sender, RoutedEventArgs e)
        {
            string? path = _interaction.PickFolder();
            if (!string.IsNullOrEmpty(path))
            {
                _viewModel.WorkingDirectory = path;
                _viewModel.IncludeWorkingDirectory = true;
            }
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
            {
                return;
            }

            ApplyDroppedFile(paths[0]);
        }

        private void ApplyDroppedFile(string path)
        {
            if (!ShortcutFile.TryRead(path, _shortcuts, out ShortcutInfo info, out string? error))
            {
                _viewModel.DropStatus = error ?? "The file could not be read.";
                return;
            }

            _viewModel.ApplyShortcut(info);
        }

        private void OnFinderPressed(object sender, MouseButtonEventArgs e)
        {
            _finding = true;
            _hoverWindow = IntPtr.Zero;
            _viewModel.FinderActive = true;
            _overlay ??= new WindowHighlightOverlay();
            Mouse.Capture(this);
            Cursor = Cursors.Cross;
            e.Handled = true;
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_finding || !GetCursorPos(out PointNative point))
            {
                return;
            }

            IntPtr window = _inspector.GetTopLevelWindowAt(point.X, point.Y);
            _hoverWindow = window;
            if (window != IntPtr.Zero
                && _inspector.TryGetWindowRect(window, out int x, out int y, out int width, out int height)
                && _inspector.TryDescribe(window, out WindowInfo info))
            {
                _overlay?.MoveTo(x, y, width, height);
                _viewModel.SetFinderPreview(info);
                return;
            }

            _overlay?.HideHighlight();
            _viewModel.ClearFinderPreview();
        }

        private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_finding)
            {
                return;
            }

            IntPtr window = _hoverWindow;
            WindowInfo? chosen = null;
            if (window != IntPtr.Zero && _inspector.TryDescribe(window, out WindowInfo info))
            {
                chosen = info;
            }

            EndFind();
            if (chosen != null)
            {
                _viewModel.ApplyWindow(chosen);
            }

            e.Handled = true;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_finding || e.Key != Key.Escape)
            {
                return;
            }

            EndFind();
            e.Handled = true;
        }

        private void EndFind()
        {
            _finding = false;
            _hoverWindow = IntPtr.Zero;
            if (Mouse.Captured == this)
            {
                Mouse.Capture(null);
            }

            Cursor = null;
            _viewModel.FinderActive = false;
            _overlay?.HideHighlight();
            _viewModel.ClearFinderPreview();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PointNative
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out PointNative point);
    }
}

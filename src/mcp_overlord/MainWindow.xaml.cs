using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using mcp_overlord.UI;
using mcp_overlord.UI.Placement;

namespace mcp_overlord
{
    /// <summary>
    /// Main window. Layout, log column widths, and the log sort are saved on close and restored on start.
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int DETAIL_BODY_FIT_ATTEMPT_LIMIT = 5;
        private const double DETAIL_BODY_HEIGHT_SLACK = 4;
        private const string DETAIL_BODY_TAG = "DetailBody";

        private readonly MainViewModel _viewModel;
        private readonly UiSettingsStore _settingsStore;
        private readonly string _settingsPath;
        private readonly IMonitorLayout _monitorLayout = new Win32MonitorLayout();
        private readonly WindowPlacementBinder _placement;
        private UiSettings _settings = new UiSettings();
        private DispatcherTimer? _applicationTimer;
        private DispatcherTimer? _statsTimer;
        private bool _stickToTail = true;
        private string? _sortColumn;
        private string _sortDirection = "Ascending";
        private int _toolbarMinimumWidthGeneration;
        private double _windowChromeWidth;
        private bool _windowChromeSettled;
        private bool _detailBodyFitQueued;
        private bool _fittingDetailBody;
        private bool _forwardingDetailWheel;
        private int _detailBodyFitAttempts;

        /// <summary>
        /// Creates the window.
        /// </summary>
        /// <param name="viewModel">Window state.</param>
        /// <param name="settingsStore">Layout persistence.</param>
        /// <param name="settingsPath">File that stores the layout. Null uses the default LocalAppData path.</param>
        public MainWindow(
            MainViewModel viewModel,
            UiSettingsStore settingsStore,
            string? settingsPath = null,
            WpfUserInteraction? interaction = null)
        {
            _viewModel = viewModel;
            _settingsStore = settingsStore;
            _settingsPath = settingsPath ?? UiSettingsStore.DefaultPath;
            InitializeComponent();
            _placement = new WindowPlacementBinder(this, _monitorLayout);
            _settings = _settingsStore.Load(_settingsPath);
            if (interaction != null)
            {
                interaction.Settings = _settings;
                interaction.SettingsStore = _settingsStore;
                interaction.SettingsPath = _settingsPath;
            }

            RestoreWindowPlacement(_settings);
            DataContext = _viewModel;
            _viewModel.LogRebuilt += OnLogRebuilt;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Detail.PropertyChanged += OnDetailPropertyChanged;
            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            Closing += OnClosing;
            DpiChanged += OnDpiChanged;
            StateChanged += OnWindowStateChanged;
            SizeChanged += OnWindowSizeChanged;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            DarkTitleBar.Apply(handle);
            if (_viewModel.IsElevated)
            {
                DarkTitleBar.ApplyCaptionColor(handle, CaptionColor());
            }

            _viewModel.SelectedLevelFilter = string.IsNullOrEmpty(_settings.LogLevelFilter) ? "All" : _settings.LogLevelFilter;
            _viewModel.BottomPanelTabIndex = ClampBottomPanelTab(_settings.BottomPanelTabIndex);
            _viewModel.Detail.TabIndex = _settings.LogDetailTabIndex;
            _viewModel.Detail.BodyView = _settings.LogDetailBodyView;
        }

        private System.Windows.Media.Color CaptionColor()
        {
            object? resource = TryFindResource("AdministratorTitleBarColor");
            if (resource is System.Windows.Media.Color color)
            {
                return color;
            }

            return DarkTitleBar.AdministratorCaptionColor;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_settings.ApplicationsPanelWidth >= 120)
            {
                ApplicationsColumn.Width = new GridLength(_settings.ApplicationsPanelWidth);
            }

            if (_settings.BottomPanelHeight >= 80)
            {
                BottomPanelRow.Height = new GridLength(_settings.BottomPanelHeight);
            }

            ApplyColumnWidths(_settings);
            ApplySort(_settings);
            ScrollViewer? viewer = FindScrollViewer(LogGrid);
            if (viewer != null)
            {
                viewer.ScrollChanged += OnLogScrollChanged;
            }

            _applicationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _applicationTimer.Tick += (_, _) => _viewModel.RefreshApplications();
            _applicationTimer.Start();

            _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _statsTimer.Tick += (_, _) => _viewModel.RefreshStats();
            _statsTimer.Start();
            _viewModel.RefreshStats();
            _viewModel.RefreshApplications();
            QueueToolbarMinimumWidth();
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (!_viewModel.ConfirmClose())
            {
                e.Cancel = true;
                return;
            }

            _applicationTimer?.Stop();
            _statsTimer?.Stop();
            WindowPlacementSnapshot placement = _placement.Capture();
            _settings.WindowBoundsPixels = placement.NormalBounds;
            _settings.WindowState = placement.IsMaximized ? "Maximized" : "Normal";
            _settings.ApplicationsPanelWidth = ApplicationsColumn.ActualWidth;
            _settings.BottomPanelHeight = BottomPanelRow.ActualHeight;
            _settings.BottomPanelTabIndex = _viewModel.BottomPanelTabIndex;
            _settings.LogDetailTabIndex = _viewModel.Detail.TabIndex;
            _settings.LogDetailBodyView = _viewModel.Detail.BodyView;
            _settings.LogLevelFilter = _viewModel.SelectedLevelFilter;
            _settings.LogSortColumn = _sortColumn;
            _settings.LogSortDirection = _sortDirection;
            _settings.LogColumnWidths = new Dictionary<string, double>();
            foreach (DataGridColumn column in LogGrid.Columns)
            {
                if (!string.IsNullOrEmpty(column.SortMemberPath) && !double.IsNaN(column.ActualWidth))
                {
                    _settings.LogColumnWidths[column.SortMemberPath] = column.ActualWidth;
                }
            }

            _settingsStore.Save(_settingsPath, _settings);
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Detail.PropertyChanged -= OnDetailPropertyChanged;
            _viewModel.StopOnClose();
        }

        private void OnApplicationsDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_viewModel.SelectedApplication != null)
            {
                _viewModel.EditSelected();
            }
        }

        private void OnLogSorting(object sender, DataGridSortingEventArgs e)
        {
            bool nextIsDescending = e.Column.SortDirection != ListSortDirection.Descending
                && e.Column.SortDirection == ListSortDirection.Ascending;
            _sortColumn = e.Column.SortMemberPath;
            _sortDirection = nextIsDescending ? "Descending" : "Ascending";
        }

        private void OnLogScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.ExtentHeightChange == 0)
            {
                _stickToTail = e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 4;
            }
        }

        private void OnLogRebuilt()
        {
            if (!_stickToTail || LogGrid.Items.Count == 0)
            {
                return;
            }

            LogGrid.ScrollIntoView(LogGrid.Items[LogGrid.Items.Count - 1]);
        }

        private static int ClampBottomPanelTab(int tabIndex)
        {
            if (tabIndex == MainViewModel.DETAIL_TAB_INDEX
                || tabIndex == MainViewModel.STATS_TAB_INDEX
                || tabIndex == MainViewModel.HELP_TAB_INDEX)
            {
                return tabIndex;
            }

            return MainViewModel.DETAIL_TAB_INDEX;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == null
                || e.PropertyName == nameof(MainViewModel.ShowRestartAsAdministrator)
                || e.PropertyName == nameof(MainViewModel.StartButtonText)
                || e.PropertyName == nameof(MainViewModel.StatusText))
            {
                QueueToolbarMinimumWidth();
            }
        }

        private void OnDpiChanged(object sender, DpiChangedEventArgs e)
        {
            _windowChromeWidth = 0;
            _windowChromeSettled = false;
            QueueToolbarMinimumWidth();
        }

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            QueueToolbarMinimumWidth();
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_windowChromeSettled && WindowState == WindowState.Normal)
            {
                QueueToolbarMinimumWidth();
            }
        }

        private void QueueToolbarMinimumWidth()
        {
            int generation = ++_toolbarMinimumWidthGeneration;
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    if (generation != _toolbarMinimumWidthGeneration)
                    {
                        return;
                    }

                    ApplyToolbarMinimumWidth();
                }));
        }

        private void ApplyToolbarMinimumWidth()
        {
            if (!IsLoaded)
            {
                return;
            }

            FileCommands.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            ServerCommands.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double buttons = FileCommands.DesiredSize.Width + ServerCommands.DesiredSize.Width;
            FileCommands.InvalidateMeasure();
            ServerCommands.InvalidateMeasure();
            if (buttons <= 0 || double.IsNaN(buttons) || double.IsInfinity(buttons))
            {
                return;
            }

            double required = Math.Ceiling(buttons + ToolbarHorizontalMargin() + WindowChromeWidth());
            if (double.IsNaN(required) || double.IsInfinity(required) || required < 1)
            {
                return;
            }

            if (Math.Abs(MinWidth - required) < 1)
            {
                return;
            }

            MinWidth = required;
        }

        private double ToolbarHorizontalMargin()
        {
            if (FileCommands.Parent is FrameworkElement commandBar
                && commandBar.Parent is FrameworkElement toolbar)
            {
                return toolbar.Margin.Left + toolbar.Margin.Right;
            }

            return 0;
        }

        private double WindowChromeWidth()
        {
            if (WindowState != WindowState.Normal)
            {
                return _windowChromeWidth;
            }

            if (Content is not FrameworkElement content || content.ActualWidth <= 0 || ActualWidth <= 0)
            {
                return _windowChromeWidth;
            }

            if (ActualWidth > content.ActualWidth)
            {
                _windowChromeWidth = ActualWidth - content.ActualWidth;
            }

            _windowChromeSettled = true;
            return _windowChromeWidth;
        }

        private void RestoreWindowPlacement(UiSettings settings)
        {
            PixelRect? bounds = LegacyBoundsConverter.Resolve(
                settings.WindowBoundsPixels,
                settings.Left,
                settings.Top,
                settings.Width,
                settings.Height,
                _monitorLayout.GetMonitors(),
                MinWidth,
                MinHeight);
            bool maximized = string.Equals(settings.WindowState, "Maximized", StringComparison.OrdinalIgnoreCase);
            _placement.Restore(bounds, maximized);
        }

        private void ApplyColumnWidths(UiSettings settings)
        {
            if (settings.LogColumnWidths == null)
            {
                return;
            }

            foreach (DataGridColumn column in LogGrid.Columns)
            {
                if (!string.IsNullOrEmpty(column.SortMemberPath)
                    && settings.LogColumnWidths.TryGetValue(column.SortMemberPath, out double width)
                    && width >= 40)
                {
                    column.Width = new DataGridLength(width);
                }
            }
        }

        private void ApplySort(UiSettings settings)
        {
            if (string.IsNullOrEmpty(settings.LogSortColumn))
            {
                return;
            }

            DataGridColumn? column = null;
            foreach (DataGridColumn candidate in LogGrid.Columns)
            {
                if (candidate.SortMemberPath == settings.LogSortColumn)
                {
                    column = candidate;
                    break;
                }
            }

            if (column == null)
            {
                return;
            }

            var direction = string.Equals(settings.LogSortDirection, "Descending", StringComparison.OrdinalIgnoreCase)
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
            column.SortDirection = direction;
            LogGrid.Items.SortDescriptions.Clear();
            LogGrid.Items.SortDescriptions.Add(new SortDescription(settings.LogSortColumn, direction));
            _sortColumn = settings.LogSortColumn;
            _sortDirection = direction == ListSortDirection.Descending ? "Descending" : "Ascending";
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject root)
        {
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, index);
                if (child is ScrollViewer viewer)
                {
                    return viewer;
                }

                ScrollViewer? nested = FindScrollViewer(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        #region Detail panel

        private void OnDetailPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(LogDetailViewModel.ActiveHexLines)
                && e.PropertyName != nameof(LogDetailViewModel.ActiveTextLines)
                && e.PropertyName != nameof(LogDetailViewModel.ShowHex)
                && e.PropertyName != nameof(LogDetailViewModel.ShowAscii))
            {
                return;
            }

            _detailBodyFitAttempts = 0;
            QueueDetailBodyFit();
        }

        private void OnDetailScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!e.HeightChanged)
            {
                return;
            }

            _detailBodyFitAttempts = 0;
            QueueDetailBodyFit();
        }

        private void OnDetailBodyLoaded(object sender, RoutedEventArgs e)
        {
            _detailBodyFitAttempts = 0;
            QueueDetailBodyFit();
        }

        private void OnDetailBodyVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is not true)
            {
                return;
            }

            _detailBodyFitAttempts = 0;
            QueueDetailBodyFit();
        }

        private void OnDetailBodyPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_forwardingDetailWheel || e.Handled || sender is not DependencyObject source)
            {
                return;
            }

            if (CanScrollDetailBody(FindScrollViewer(source), e.Delta))
            {
                return;
            }

            e.Handled = true;
            _forwardingDetailWheel = true;
            try
            {
                var forwarded = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = DetailScrollViewer
                };
                DetailScrollViewer.RaiseEvent(forwarded);
            }
            finally
            {
                _forwardingDetailWheel = false;
            }
        }

        private void QueueDetailBodyFit()
        {
            if (_detailBodyFitQueued || _detailBodyFitAttempts >= DETAIL_BODY_FIT_ATTEMPT_LIMIT)
            {
                return;
            }

            _detailBodyFitQueued = true;
            _detailBodyFitAttempts++;
            Dispatcher.BeginInvoke(ApplyDetailBodyFit, DispatcherPriority.Loaded);
        }

        private void ApplyDetailBodyFit()
        {
            _detailBodyFitQueued = false;
            FitVisibleDetailBodies();
        }

        private void FitVisibleDetailBodies()
        {
            if (_fittingDetailBody)
            {
                return;
            }

            var bodies = new List<ListBox>();
            CollectDetailBodies(DetailScrollViewer, bodies);
            foreach (ListBox listBox in bodies)
            {
                FitDetailBody(listBox);
            }
        }

        /// <summary>
        /// A short hex or text body shrinks to its lines. A long one keeps the pane height so the list stays virtualized.
        /// </summary>
        private void FitDetailBody(ListBox listBox)
        {
            if (!listBox.IsVisible)
            {
                return;
            }

            double maximumHeight = DetailScrollViewer.ViewportHeight;
            if (!IsPositiveLength(maximumHeight))
            {
                QueueDetailBodyFit();
                return;
            }

            if (!TryMeasureDetailBody(listBox, maximumHeight, out double contentHeight))
            {
                QueueDetailBodyFit();
                return;
            }

            if (!double.IsNaN(listBox.Height) && Math.Abs(listBox.Height - contentHeight) <= 1)
            {
                return;
            }

            _fittingDetailBody = true;
            try
            {
                listBox.SetCurrentValue(FrameworkElement.HeightProperty, contentHeight);
            }
            finally
            {
                _fittingDetailBody = false;
            }
        }

        private static bool TryMeasureDetailBody(ListBox listBox, double maximumHeight, out double contentHeight)
        {
            contentHeight = maximumHeight;
            ScrollViewer? viewer = FindScrollViewer(listBox);
            if (viewer == null || viewer.ActualHeight <= 1)
            {
                return false;
            }

            if (listBox.Items.Count == 0)
            {
                contentHeight = 0;
                return true;
            }

            if (!TryDetailBodyItemsHeight(listBox, viewer, out double itemsHeight))
            {
                return false;
            }

            double chrome = Math.Max(0, listBox.ActualHeight - viewer.ActualHeight);
            if (viewer.ScrollableWidth > 0)
            {
                itemsHeight += SystemParameters.HorizontalScrollBarHeight;
            }

            contentHeight = Math.Min(maximumHeight, itemsHeight + chrome + DETAIL_BODY_HEIGHT_SLACK);
            return true;
        }

        private static bool TryDetailBodyItemsHeight(ListBox listBox, ScrollViewer viewer, out double itemsHeight)
        {
            itemsHeight = 0;
            if (listBox.ItemContainerGenerator.ContainerFromIndex(0) is FrameworkElement firstItem
                && firstItem.ActualHeight > 0)
            {
                itemsHeight = firstItem.ActualHeight * listBox.Items.Count;
                return true;
            }

            if (viewer.ScrollableHeight > 0.5)
            {
                itemsHeight = double.PositiveInfinity;
                return true;
            }

            return false;
        }

        private static void CollectDetailBodies(DependencyObject root, List<ListBox> bodies)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < count; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, index);
                if (child is ListBox listBox && Equals(listBox.Tag, DETAIL_BODY_TAG))
                {
                    bodies.Add(listBox);
                }

                CollectDetailBodies(child, bodies);
            }
        }

        private static bool CanScrollDetailBody(ScrollViewer? viewer, int delta)
        {
            if (viewer == null || viewer.ScrollableHeight <= 0 || delta == 0)
            {
                return false;
            }

            if (delta > 0)
            {
                return viewer.VerticalOffset > 0;
            }

            return viewer.VerticalOffset < viewer.ScrollableHeight;
        }

        private static bool IsPositiveLength(double length)
        {
            return !double.IsNaN(length) && !double.IsInfinity(length) && length > 1;
        }

        #endregion
    }
}

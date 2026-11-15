using System.Windows;
using System.Windows.Threading;
using mcp_overlord.Configuration;
using mcp_overlord.Services.Native;
using mcp_overlord.UI.Placement;
using Microsoft.Win32;

namespace mcp_overlord.UI
{
    /// <summary>File picker, confirmations, and the configuration editors.</summary>
    public sealed class WpfUserInteraction : IUserInteraction
    {
        private string? _lastDirectory;

        /// <summary>Window that owns the dialogs.</summary>
        public Window? Owner { get; set; }

        /// <summary>Layout settings shared with the main window. Dialog bounds are stored here.</summary>
        public UiSettings? Settings { get; set; }

        /// <summary>Writes <see cref="Settings"/> after a dialog closes.</summary>
        public UiSettingsStore? SettingsStore { get; set; }

        /// <summary>Path passed to <see cref="SettingsStore"/>.</summary>
        public string? SettingsPath { get; set; }

        /// <inheritdoc />
        public string? LastConfigDirectory
        {
            get
            {
                if (Settings != null && !string.IsNullOrEmpty(Settings.LastConfigDirectory))
                {
                    return Settings.LastConfigDirectory;
                }

                return _lastDirectory;
            }
            set
            {
                _lastDirectory = value;
                if (Settings != null)
                {
                    Settings.LastConfigDirectory = value;
                }
            }
        }

        /// <inheritdoc />
        public void ShowMessage(string title, string message)
        {
            PromptDialog.Show(Owner, title, message, false);
        }

        /// <inheritdoc />
        public string? PickConfigFile()
        {
            return PickOpen("Load configuration", ConfigFile.DialogFilter);
        }

        /// <inheritdoc />
        public string? PickSaveConfigFile(string suggestedName)
        {
            var dialog = new SaveFileDialog
            {
                Filter = ConfigFile.DialogFilter,
                Title = "Save configuration",
                FileName = string.IsNullOrWhiteSpace(suggestedName) ? ConfigFile.DefaultFileName : suggestedName,
                AddExtension = true,
                DefaultExt = ConfigFile.Extension,
                OverwritePrompt = true
            };
            ApplyDirectory(dialog);
            bool? chosen = Owner == null ? dialog.ShowDialog() : dialog.ShowDialog(Owner);
            if (chosen != true)
            {
                return null;
            }

            Remember(dialog.FileName);
            return dialog.FileName;
        }

        /// <inheritdoc />
        public SaveChangesChoice AskSaveChanges(string displayName)
        {
            string name = string.IsNullOrEmpty(displayName) ? "New" : displayName;
            return PromptDialog.ShowSaveChanges(
                Owner,
                "MCP Overlord",
                "Save changes to " + name + "?");
        }

        /// <inheritdoc />
        public bool Confirm(string title, string message, string confirmText = "OK")
        {
            return PromptDialog.Show(Owner, title, message, true, confirmText);
        }

        /// <inheritdoc />
        public bool EditApplication(ApplicationEditorViewModel viewModel)
        {
            var dialog = new ApplicationEditorDialog(
                viewModel,
                new Win32ShortcutReader(),
                new Win32WindowInspector(),
                this);
            return ShowPlaced(
                dialog,
                settings => settings.ApplicationEditorBoundsPixels,
                (settings, bounds) => settings.ApplicationEditorBoundsPixels = bounds);
        }

        /// <inheritdoc />
        public bool EditServerSettings(ServerSettingsViewModel viewModel)
        {
            var dialog = new ServerSettingsDialog(viewModel, this);
            return ShowPlaced(
                dialog,
                settings => settings.ServerSettingsBoundsPixels,
                (settings, bounds) => settings.ServerSettingsBoundsPixels = bounds);
        }

        /// <inheritdoc />
        public string? PickExecutableFile()
        {
            return PickOpen("Executable", "Applications (*.exe)|*.exe|All files (*.*)|*.*");
        }

        /// <inheritdoc />
        public string? PickFolder()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Working directory"
            };
            if (!string.IsNullOrEmpty(LastConfigDirectory) && Directory.Exists(LastConfigDirectory))
            {
                dialog.InitialDirectory = LastConfigDirectory;
            }

            bool? chosen = Owner == null ? dialog.ShowDialog() : dialog.ShowDialog(Owner);
            if (chosen != true)
            {
                return null;
            }

            return dialog.FolderName;
        }

        /// <inheritdoc />
        public string? PickCertificateFile()
        {
            return PickOpen("Certificate", "Certificate files (*.pfx)|*.pfx|All files (*.*)|*.*");
        }

        /// <inheritdoc />
        public string? PickSaveImageFile(string suggestedName)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg|All files (*.*)|*.*",
                Title = "Save image",
                FileName = string.IsNullOrWhiteSpace(suggestedName) ? "image.png" : suggestedName,
                AddExtension = true,
                OverwritePrompt = true
            };
            bool? chosen = Owner == null ? dialog.ShowDialog() : dialog.ShowDialog(Owner);
            if (chosen != true)
            {
                return null;
            }

            return dialog.FileName;
        }

        /// <inheritdoc />
        public void ShowConnectionReport(ConnectionReportViewModel viewModel)
        {
            if (viewModel == null)
            {
                throw new ArgumentNullException(nameof(viewModel));
            }

            var dialog = new ConnectionReportDialog(viewModel);
            ShowPlaced(
                dialog,
                settings => settings.ConnectionReportBoundsPixels,
                (settings, bounds) => settings.ConnectionReportBoundsPixels = bounds);
        }

        private string? PickOpen(string title, string filter)
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = filter
            };
            ApplyDirectory(dialog);
            bool? chosen = Owner == null ? dialog.ShowDialog() : dialog.ShowDialog(Owner);
            if (chosen != true)
            {
                return null;
            }

            Remember(dialog.FileName);
            return dialog.FileName;
        }

        private void ApplyDirectory(FileDialog dialog)
        {
            if (!string.IsNullOrEmpty(LastConfigDirectory) && Directory.Exists(LastConfigDirectory))
            {
                dialog.InitialDirectory = LastConfigDirectory;
            }
        }

        private void Remember(string path)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                LastConfigDirectory = directory;
            }
        }

        private bool ShowPlaced(Window dialog, Func<UiSettings, PixelRect?> read, Action<UiSettings, PixelRect?> write)
        {
            if (Owner != null)
            {
                dialog.Owner = Owner;
            }

            var binder = new WindowPlacementBinder(dialog, new Win32MonitorLayout());
            if (Settings != null)
            {
                PixelRect? bounds = read(Settings);
                if (bounds != null)
                {
                    binder.Restore(bounds, false);
                }
            }

            PixelRect? captured = null;
            dialog.Closing += (_, _) =>
            {
                captured = binder.Capture().NormalBounds;
            };

            bool accepted = dialog.ShowDialog() == true;
            if (Settings != null && captured != null)
            {
                write(Settings, captured);
                if (SettingsStore != null && !string.IsNullOrEmpty(SettingsPath))
                {
                    SettingsStore.Save(SettingsPath, Settings);
                }
            }

            return accepted;
        }
    }

    /// <summary>Clipboard backed by WPF.</summary>
    public sealed class WpfClipboard : IClipboardService
    {
        /// <inheritdoc />
        public void SetText(string text)
        {
            Clipboard.SetText(text ?? "");
        }
    }

    /// <summary>Marshals work onto the WPF dispatcher. A burst of log lines can share one turn.</summary>
    public sealed class DispatcherUiInvoker : IUiInvoker
    {
        private readonly Dispatcher _dispatcher;

        /// <summary>
        /// Creates the invoker.
        /// </summary>
        /// <param name="dispatcher">UI dispatcher.</param>
        public DispatcherUiInvoker(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        /// <inheritdoc />
        public void Invoke(Action action)
        {
            if (_dispatcher.CheckAccess())
            {
                action();
                return;
            }

            _dispatcher.BeginInvoke(action, DispatcherPriority.Background);
        }
    }
}

using System.Windows;
using System.Windows.Threading;
using mcp_overlord.Configuration;
using mcp_overlord.Hosting;
using mcp_overlord.Logging;
using mcp_overlord.Services;
using mcp_overlord.Services.Native;
using mcp_overlord.Shell;
using mcp_overlord.Startup;
using mcp_overlord.UI;

namespace mcp_overlord
{
    /// <summary>
    /// Application entry. Parses the command line, builds the services, and shows the main window.
    /// </summary>
    public partial class App : Application
    {
        private AppComposition? _composition;

        /// <inheritdoc />
        protected override void OnStartup(StartupEventArgs e)
        {
            AppUserModel.SetCurrentProcess();
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            DispatcherUnhandledException += OnUnhandled;

            CommandLineParseResult parsed = CommandLineParser.Parse(e.Args, Environment.CurrentDirectory);
            if (parsed.Help)
            {
                ParentConsole.WriteLine(parsed.Usage);
                Shutdown(0);
                return;
            }

            if (parsed.RegisterShell)
            {
                RegisterShellAndExit();
                return;
            }

            if (parsed.Elevate && TryStartElevated(parsed.ConfigPath))
            {
                Shutdown(0);
                return;
            }

            try
            {
                _composition = AppComposition.Create(Dispatcher);
            }
            catch (SchemaResourceMissingException exception)
            {
                PromptDialog.Show(null, "MCP Overlord", exception.Message, false);
                Shutdown(1);
                return;
            }

            _composition.ViewModel.ShutdownRequested += () => Shutdown();
            var window = new MainWindow(
                _composition.ViewModel,
                _composition.SettingsStore,
                interaction: _composition.Interaction);
            if (_composition.Interaction != null)
            {
                _composition.Interaction.Owner = window;
            }

            window.Loaded += (_, _) => PublishStartMenu();

            if (!string.IsNullOrEmpty(parsed.Error))
            {
                string error = parsed.Error;
                _composition.Log.Append(LogLevel.Error, "config", error);
                window.Loaded += (_, _) => _composition.Interaction?.ShowMessage("MCP Overlord", error);
            }
            else if (!string.IsNullOrEmpty(parsed.ConfigPath))
            {
                string configPath = parsed.ConfigPath;
                window.Loaded += async (_, _) =>
                {
                    try
                    {
                        await _composition.ViewModel.AutoStartAsync(configPath);
                    }
                    catch (Exception exception)
                    {
                        _composition.Log.Append(LogLevel.Error, "server", exception.Message);
                    }
                };
            }

            MainWindow = window;
            window.Show();
        }

        /// <inheritdoc />
        protected override void OnExit(ExitEventArgs e)
        {
            if (_composition != null && _composition.Host.IsRunning)
            {
                WaitForHostStop(_composition);
            }

            base.OnExit(e);
        }

        private static bool TryStartElevated(string? configPath)
        {
            var elevation = new ElevationService(new Win32IntegrityApi());
            if (elevation.IsElevated)
            {
                return false;
            }

            try
            {
                return elevation.RestartElevated(configPath);
            }
            catch (Exception exception)
            {
                PromptDialog.Show(null, "MCP Overlord", "Could not start as administrator: " + exception.Message, false);
                return false;
            }
        }

        private void RegisterShellAndExit()
        {
            try
            {
                WpfJumpListPublisher.Publish(Environment.ProcessPath, InstallationInfo.ReadProductCode());
            }
            catch (Exception)
            {
                // The installer runs this with no window and no log. A failure must not show a dialog.
            }

            Shutdown(0);
        }

        private void PublishStartMenu()
        {
            try
            {
                WpfJumpListPublisher.Publish(Environment.ProcessPath, InstallationInfo.ReadProductCode());
            }
            catch (Exception exception)
            {
                _composition?.Log.Append(
                    LogLevel.Warning,
                    "server",
                    "Could not publish the Start menu list: " + exception.Message);
            }
        }

        private static void WaitForHostStop(AppComposition composition)
        {
            try
            {
                Task stop = composition.Host.StopAsync();
                if (!stop.Wait(McpHost.CloseWaitTimeout))
                {
                    composition.Log.Append(
                        LogLevel.Error,
                        "server",
                        "The listener did not stop within 5 seconds. The process will still exit.");
                }
            }
            catch (Exception exception)
            {
                composition.Log.Append(LogLevel.Error, "server", exception.GetBaseException().Message);
            }
        }

        private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            string message = e.Exception.Message;
            _composition?.Log.Append(LogLevel.Error, "server", e.Exception.ToString());
            PromptDialog.Show(MainWindow, "MCP Overlord", message, false);
            e.Handled = true;
            Shutdown(1);
        }
    }
}

using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using mcp_overlord.Configuration;
using mcp_overlord.Services;
using mcp_overlord.Services.Native;
using mcp_overlord.UI;

namespace mcp_overlord.tests
{
    public class EditorDesktopTests
    {
        private static readonly object Gate = new object();

        [Fact]
        public void TheCurrentProcessCommandLineAndDirectoryCanBeRead()
        {
            var inspector = new Win32WindowInspector();
            bool read = inspector.TryReadProcess(
                Environment.ProcessId,
                out string executable,
                out string processName,
                out string? workingDirectory,
                out string? arguments);

            Assert.True(read);
            Assert.Equal(Environment.ProcessPath, executable, ignoreCase: true);
            Assert.False(string.IsNullOrEmpty(processName));
            Assert.NotNull(workingDirectory);
            Assert.Equal(
                Path.TrimEndingDirectorySeparator(Environment.CurrentDirectory),
                Path.TrimEndingDirectorySeparator(workingDirectory),
                ignoreCase: true);
            Assert.NotNull(arguments);
        }

        [Fact]
        public void AShortcutRoundTripsThroughTheShell()
        {
            OnSta(() =>
            {
                string directory = Path.Combine(Path.GetTempPath(), "mcp-lnk-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "Game.lnk");
                Win32ShortcutReader.Write(path, "C:\\game.exe", "-log \"a\"", "C:\\work", "A game");

                var reader = new Win32ShortcutReader();
                Assert.True(reader.TryRead(path, out ShortcutInfo info, out string? error), error);
                Assert.Equal("Game", info.Name);
                Assert.Equal("C:\\game.exe", info.TargetPath);
                Assert.Equal("-log \"a\"", info.Arguments);
                Assert.Equal("C:\\work", info.WorkingDirectory);
                Assert.Equal("A game", info.Description);
                Assert.Equal("game", info.ProcessName);
            });
        }

        [Fact]
        public void ATestWindowReportsItsTitleProcessAndExecutable()
        {
            string title = "mcp-editor-" + Guid.NewGuid().ToString("N");
            DesktopProbe? probe = null;
            try
            {
                probe = DesktopProbe.Open(title, System.Drawing.Color.FromArgb(255, 40, 90, 160));
            }
            catch (Exception exception)
            {
                Assert.Skip("No interactive desktop: " + exception.Message);
            }

            using (probe)
            {
                var inspector = new Win32WindowInspector();
                Assert.True(inspector.TryDescribe(probe!.Handle, out WindowInfo info));
                Assert.Equal(title, info.Title);
                Assert.False(string.IsNullOrEmpty(info.ProcessName));
                Assert.Equal(Environment.ProcessPath, info.ExecutablePath, ignoreCase: true);
                Assert.True(inspector.TryGetWindowRect(probe.Handle, out _, out _, out int width, out int height));
                Assert.True(width > 0 && height > 0);
                IntPtr foreignWindow = WaitUntilOwnWindowIsTop(inspector, probe);
                Assert.True(foreignWindow == IntPtr.Zero, DescribeForeignWindow(inspector, foreignWindow));
            }
        }

        [Fact]
        public void AWindowAllowsTheFileDropMessages()
        {
            Assert.False(ElevatedFileDrop.AllowFileDrop(IntPtr.Zero));

            DesktopProbe? probe = null;
            try
            {
                probe = DesktopProbe.Open(
                    "mcp-drop-" + Guid.NewGuid().ToString("N"),
                    System.Drawing.Color.FromArgb(255, 40, 90, 160));
            }
            catch (Exception exception)
            {
                Assert.Skip("No interactive desktop: " + exception.Message);
            }

            using (probe)
            {
                Assert.True(ElevatedFileDrop.AllowFileDrop(probe!.Handle));
            }
        }

        [Fact]
        public void AShellFileDropReachesTheWindowWhenOleDropIsOff()
        {
            OnSta(() =>
            {
                EnsureApplication();
                string? received = null;
                var window = new Window
                {
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    Width = 120,
                    Height = 80,
                    Left = -20000,
                    Top = -20000,
                    AllowDrop = true
                };
                window.SourceInitialized += (_, _) => ElevatedFileDrop.Attach(window, value => received = value);
                window.Show();
                string path = @"C:\mcp drop\game.exe";
                SendShellFileDrop(new WindowInteropHelper(window).Handle, path);
                Assert.False(window.AllowDrop);
                Assert.Equal(path, received);
                window.Close();
            });
        }

        [Fact]
        public void BothEditorsBuildOnAnStaThread()
        {
            OnSta(() =>
            {
                System.Windows.Application application = System.Windows.Application.Current
                    ?? new System.Windows.Application
                    {
                        ShutdownMode = ShutdownMode.OnExplicitShutdown
                    };

                var styles = new Uri("pack://application:,,,/mcp_overlord;component/Resources/Styles.xaml", UriKind.Absolute);
                bool present = false;
                foreach (ResourceDictionary dictionary in application.Resources.MergedDictionaries)
                {
                    if (dictionary.Source == styles)
                    {
                        present = true;
                        break;
                    }
                }

                if (!present)
                {
                    application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = styles });
                }

                var session = new ConfigEditorSession(new ConfigLoader(), new ConfigStore(), () => false);
                session.New();
                var applicationEditor = new ApplicationEditorViewModel(
                    session.Draft.Clone(),
                    new ApplicationDraft(),
                    0,
                    true,
                    Array.Empty<string>(),
                    session.TryApply);
                var interaction = new FakeInteraction();
                var applicationDialog = new ApplicationEditorDialog(
                    applicationEditor,
                    new Win32ShortcutReader(),
                    new Win32WindowInspector(),
                    interaction);
                applicationDialog.WindowStartupLocation = WindowStartupLocation.Manual;
                applicationDialog.Left = -20000;
                applicationDialog.Top = -20000;
                applicationDialog.Show();
                applicationDialog.UpdateLayout();
                bool elevated = new Win32IntegrityApi().GetCurrentIntegrityLevel() >= 0x3000;
                Assert.Equal(!elevated, applicationDialog.AllowDrop);
                var dropZone = (FrameworkElement)applicationDialog.FindName("DropZone");
                var finder = (FrameworkElement)applicationDialog.FindName("FinderTargetHost");
                Assert.True(
                    dropZone.ActualHeight > 0 && dropZone.ActualHeight < 96,
                    "Drop zone height was " + dropZone.ActualHeight);
                Assert.InRange(finder.ActualWidth, 1, dropZone.ActualHeight + 1);
                Assert.InRange(finder.ActualHeight, 1, dropZone.ActualHeight + 1);
                AssertComboCaption(applicationDialog, "mouseEvent");

                var serverEditor = new ServerSettingsViewModel(session.Draft.Clone(), session.TryApply);
                var serverDialog = new ServerSettingsDialog(serverEditor, interaction);
                serverDialog.WindowStartupLocation = WindowStartupLocation.Manual;
                serverDialog.Left = -20000;
                serverDialog.Top = -20000;
                serverDialog.Show();
                serverDialog.UpdateLayout();
                AssertEditableComboIsDark(serverDialog);
                Assert.Equal("New application", applicationEditor.DialogTitle);
                Assert.Equal("Server settings", serverDialog.Title);
                applicationDialog.Close();
                serverDialog.Close();
            });
        }

        private static void AssertComboCaption(DependencyObject root, string caption)
        {
            System.Windows.Controls.ComboBox? combo = FindDescendant<System.Windows.Controls.ComboBox>(root);
            Assert.NotNull(combo);
            System.Windows.Controls.Border? face = FindDescendant<System.Windows.Controls.Border>(combo);
            Assert.NotNull(face);
            AssertDark(face.Background);
            System.Windows.Controls.TextBlock? text = FindText(combo, caption);
            Assert.NotNull(text);
            AssertLight(text.Foreground);
        }

        private static void AssertEditableComboIsDark(DependencyObject root)
        {
            System.Windows.Controls.ComboBox? combo = FindDescendant<System.Windows.Controls.ComboBox>(root);
            Assert.NotNull(combo);
            var editor = combo.Template.FindName("PART_EditableTextBox", combo) as System.Windows.Controls.TextBox;
            Assert.NotNull(editor);
            AssertLight(editor.Foreground);
            AssertLight(editor.CaretBrush);
            var background = Assert.IsAssignableFrom<SolidColorBrush>(editor.Background);
            Assert.True(
                background.Color.A == 0 || (background.Color.R < 80 && background.Color.G < 80 && background.Color.B < 90),
                background.Color.ToString());
            System.Windows.Controls.Border? face = FindDescendant<System.Windows.Controls.Border>(combo);
            Assert.NotNull(face);
            AssertDark(face.Background);
        }

        private static void AssertDark(System.Windows.Media.Brush? brush)
        {
            var solid = Assert.IsAssignableFrom<SolidColorBrush>(brush);
            Assert.True(solid.Color.R < 80 && solid.Color.G < 80 && solid.Color.B < 90, solid.Color.ToString());
        }

        private static void AssertLight(System.Windows.Media.Brush? brush)
        {
            var solid = Assert.IsAssignableFrom<SolidColorBrush>(brush);
            Assert.True(solid.Color.R > 180 && solid.Color.G > 180 && solid.Color.B > 180, solid.Color.ToString());
        }

        private static T? FindDescendant<T>(DependencyObject root)
            where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < count; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, index);
                if (child is T match)
                {
                    return match;
                }

                T? nested = FindDescendant<T>(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static System.Windows.Controls.TextBlock? FindText(DependencyObject root, string text)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < count; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, index);
                if (child is System.Windows.Controls.TextBlock block && block.Text == text)
                {
                    return block;
                }

                System.Windows.Controls.TextBlock? nested = FindText(child, text);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static void EnsureApplication()
        {
            if (System.Windows.Application.Current != null)
            {
                return;
            }

            new System.Windows.Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
        }

        private static void SendShellFileDrop(IntPtr windowHandle, string path)
        {
            byte[] pathBytes = Encoding.Unicode.GetBytes(path + "\0\0");
            int headerSize = Marshal.SizeOf<ShellDropHeader>();
            int total = headerSize + pathBytes.Length;
            IntPtr dropHandle = GlobalAlloc(MoveableZeroedMemory, (UIntPtr)total);
            if (dropHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("The shell drop allocation failed.");
            }

            IntPtr locked = GlobalLock(dropHandle);
            try
            {
                var header = new ShellDropHeader
                {
                    FilesOffset = headerSize,
                    WideCharacters = 1
                };
                Marshal.StructureToPtr(header, locked, false);
                Marshal.Copy(pathBytes, 0, IntPtr.Add(locked, headerSize), pathBytes.Length);
            }
            finally
            {
                GlobalUnlock(dropHandle);
            }

            SendMessage(windowHandle, DropFilesMessage, dropHandle, IntPtr.Zero);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ShellDropHeader
        {
            public int FilesOffset;
            public int PointX;
            public int PointY;
            public int NonClient;
            public int WideCharacters;
        }

        private const uint MoveableZeroedMemory = 0x0042;
        private const int DropFilesMessage = 0x0233;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr handle);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr windowHandle, int message, IntPtr wordParameter, IntPtr longParameter);

        private static IntPtr WaitUntilOwnWindowIsTop(Win32WindowInspector inspector, DesktopProbe probe)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(1);
            IntPtr foreignWindow = IntPtr.Zero;
            while (true)
            {
                probe.BringToFront();
                if (!inspector.TryGetWindowRect(probe.Handle, out int x, out int y, out int width, out int height))
                {
                    return new IntPtr(-1);
                }

                foreignWindow = inspector.GetTopLevelWindowAt(x + (width / 2), y + (height / 2));
                if (foreignWindow == IntPtr.Zero || DateTime.UtcNow >= deadline)
                {
                    return foreignWindow;
                }

                Thread.Sleep(20);
            }
        }

        private static string DescribeForeignWindow(Win32WindowInspector inspector, IntPtr window)
        {
            if (!inspector.TryDescribe(window, out WindowInfo info))
            {
                return "A foreign window " + window + " covers the probe.";
            }

            return "A foreign window covers the probe: " + info.Title + " (" + info.ProcessName + ").";
        }

        private static void OnSta(Action action)
        {
            lock (Gate)
            {
                Exception? failure = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();
                if (!thread.Join(TimeSpan.FromSeconds(30)))
                {
                    throw new TimeoutException("The editor desktop test did not finish.");
                }

                if (failure != null)
                {
                    throw new Exception(failure.ToString(), failure);
                }
            }
        }
    }
}

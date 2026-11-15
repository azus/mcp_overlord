using mcp_overlord.Configuration;
using mcp_overlord.Hosting;
using mcp_overlord.Services;
using mcp_overlord.UI;

namespace mcp_overlord.tests
{
    public class EditorViewModelTests
    {
        [Fact]
        public void RequiredFieldsAndDuplicateNamesAreRejected()
        {
            var session = Session();
            session.New();
            var editor = Editor(session, new ApplicationDraft(), 0, true, Array.Empty<string>());

            Assert.False(editor.TryCommit());
            Assert.Equal("Name is required.", editor.NameError);
            Assert.Equal("Executable is required.", editor.ExecutableError);
            Assert.Equal("Window title is required.", editor.WindowTitleError);
            Assert.Empty(session.Draft.Applications);

            editor.Name = "Game";
            editor.Executable = "C:\\";
            editor.WindowTitle = "Lobby";
            Assert.False(editor.TryCommit());
            Assert.Equal("Executable must include a file name.", editor.ExecutableError);

            editor.Executable = "C:\\game.exe";
            Assert.True(editor.TryCommit());
            Assert.Equal("Game", session.Draft.Applications[0].Name);
            Assert.True(session.IsDirty);

            var duplicate = Editor(session, new ApplicationDraft(), 1, true, new[] { "Game" });
            duplicate.Name = "Game";
            duplicate.Executable = "C:\\game.exe";
            duplicate.WindowTitle = "Lobby";
            Assert.False(duplicate.TryCommit());
            Assert.Contains("already exists", duplicate.NameError);
            Assert.Single(session.Draft.Applications);
        }

        [Fact]
        public void IncludeTogglesDecideWhatIsWritten()
        {
            var session = Session();
            session.New();
            var editor = Editor(session, new ApplicationDraft(), 0, true, Array.Empty<string>());
            editor.Name = "Game";
            editor.Executable = "C:\\game.exe";
            editor.WindowTitle = "Lobby";
            editor.Parameters = "-x";
            editor.IncludeParameters = false;
            editor.Description = "Hello";
            editor.IncludeDescription = true;
            Assert.True(editor.TryCommit());

            string hidden = ConfigDraftSerializer.Write(session.Draft);
            Assert.DoesNotContain("parameters", hidden);
            Assert.Contains("\"description\": \"Hello\"", hidden);

            ApplicationDraft current = session.Draft.Applications[0].Clone();
            var again = Editor(session, current, 0, false, Array.Empty<string>());
            again.Parameters = "";
            again.IncludeParameters = true;
            Assert.True(again.TryCommit());
            Assert.Contains("\"parameters\": \"\"", ConfigDraftSerializer.Write(session.Draft));
        }

        [Fact]
        public void ShortcutAndWindowCaptureFillOnlyTheProvidedFields()
        {
            var session = Session();
            session.New();
            var editor = Editor(session, new ApplicationDraft { Name = "Keep" }, 0, true, Array.Empty<string>());
            editor.Parameters = "kept";
            editor.IncludeParameters = false;
            editor.ApplyShortcut(new ShortcutInfo
            {
                Name = "Notepad",
                TargetPath = "C:\\Windows\\notepad.exe",
                Arguments = "/a",
                WorkingDirectory = "C:\\Windows",
                ProcessName = "notepad",
                Description = "Editor"
            });

            Assert.Equal("Notepad", editor.Name);
            Assert.Equal("C:\\Windows\\notepad.exe", editor.Executable);
            Assert.Equal("/a", editor.Parameters);
            Assert.True(editor.IncludeParameters);
            Assert.True(editor.IncludeWorkingDirectory);
            Assert.True(editor.IncludeProcessName);
            Assert.True(editor.IncludeDescription);
            Assert.Contains("Filled", editor.DropStatus);
            Assert.Contains("name", editor.DropStatus);

            editor.ApplyWindow(new WindowInfo
            {
                Title = "Lobby",
                ExecutablePath = "C:\\game.exe",
                ProcessName = "game",
                WorkingDirectory = null,
                Arguments = null
            });

            Assert.Equal("Notepad", editor.Name);
            Assert.Equal("Lobby", editor.WindowTitle);
            Assert.Equal("C:\\game.exe", editor.Executable);
            Assert.Equal("/a", editor.Parameters);
            Assert.Equal("C:\\Windows", editor.WorkingDirectory);
            Assert.Equal("game", editor.ProcessName);
        }

        [Fact]
        public void LoaderErrorsAreMappedOntoTheMatchingField()
        {
            var editor = new ApplicationEditorViewModel(
                new ConfigDraft(),
                new ApplicationDraft(),
                0,
                true,
                Array.Empty<string>(),
                _ => ConfigEditResult.Failed(
                    new[]
                    {
                        new ConfigValidationError("/applications/0/executable", "executable must include a file name."),
                        new ConfigValidationError("/server/httpPort", "bad port")
                    }));
            editor.Name = "Game";
            editor.Executable = "C:\\game.exe";
            editor.WindowTitle = "Lobby";

            Assert.False(editor.TryCommit());
            Assert.Equal("executable must include a file name.", editor.ExecutableError);
            Assert.Contains("bad port", editor.FormError);
        }

        [Fact]
        public void ServerRulesCoverPortsAddressesKeysAndGeneration()
        {
            var session = Session();
            session.New();
            var editor = new ServerSettingsViewModel(session.Draft.Clone(), session.TryApply);

            editor.HttpPortText = "0";
            editor.IncludeHttpPort = true;
            Assert.False(editor.TryCommit());
            Assert.Contains("HTTP port", editor.ErrorText);

            editor.HttpPortText = "5000";
            editor.HttpsPortText = "5000";
            editor.IncludeHttpsPort = true;
            editor.EnableHttps = true;
            editor.IncludeEnableHttps = true;
            Assert.False(editor.TryCommit());
            Assert.Contains("differ", editor.ErrorText);

            editor.HttpsPortText = "5001";
            editor.BindAddress = "localhost";
            editor.IncludeBindAddress = true;
            Assert.False(editor.TryCommit());
            Assert.Contains("IP address", editor.ErrorText);

            editor.BindAddress = "10.0.0.8";
            editor.EnableHttps = false;
            editor.ApiKey = "short";
            editor.IncludeApiKey = true;
            Assert.False(editor.TryCommit());
            Assert.Contains("16", editor.ErrorText);
            Assert.Contains("requires HTTPS", editor.WarningText);

            editor.ApiKey = "";
            Assert.Contains("requires HTTPS", editor.WarningText);

            editor.EnableHttps = true;
            editor.GenerateApiKey();
            Assert.Equal(32, editor.ApiKey.Length);
            Assert.True(editor.IncludeApiKey);
            Assert.True(editor.ShowApiKey);
            Assert.Equal("", editor.WarningText);
            editor.HostNamesText = "play.example.com\r\n\r\n10.0.0.8";
            editor.IncludeHostNames = true;
            editor.HttpPortText = "5010";
            Assert.True(editor.TryCommit());
            Assert.Equal(5010, session.Draft.Server.HttpPort.Value);
            Assert.Equal(
                new[] { "play.example.com", "10.0.0.8" },
                ConfigDraftSerializer.SplitHostNames(session.Draft.Server.HostNames.Value));
            Assert.Contains("127.0.0.1", editor.BindAddressChoices);
            Assert.Contains("0.0.0.0", ServerSettingsViewModel.BuildBindAddressChoices(null));
            Assert.Contains("10.0.0.8", ServerSettingsViewModel.BuildBindAddressChoices("10.0.0.8"));
        }

        [Fact]
        public void WindowTitleShowsNewUntilTheFileIsSaved()
        {
            (MainViewModel viewModel, FakeWorld world, ConfigStore store) = Create();

            Assert.Equal("MCP Overlord - New", viewModel.WindowTitle);
            Assert.Equal("No configuration loaded", viewModel.ConfigPathDisplay);
            Assert.False(viewModel.IsDirty);

            viewModel.NewConfig();
            Assert.Equal("New configuration", viewModel.ConfigPathDisplay);
            world.Interaction.EditApplicationAction = editor =>
            {
                editor.Name = "Game";
                editor.Executable = "C:\\game.exe";
                editor.WindowTitle = "Lobby";
                Assert.True(editor.TryCommit());
            };
            viewModel.AddApplication();
            Assert.True(viewModel.IsDirty);
            Assert.Equal("Game", store.Document!.Applications[0].Name);
            WaitForApplications(viewModel, 1, "Game");

            world.Interaction.NextSaveChoice = SaveChangesChoice.Cancel;
            Assert.False(viewModel.ConfirmClose());
            Assert.Equal("Game", viewModel.Applications[0].Name);

            world.Interaction.NextSaveChoice = SaveChangesChoice.Save;
            world.Interaction.NextSavePath = null;
            Assert.False(viewModel.ConfirmClose());
            Assert.True(viewModel.IsDirty);

            string path = Path.Combine(NewDirectory(), "magic.json");
            world.Interaction.NextSavePath = path;
            Assert.True(viewModel.ConfirmClose());
            Assert.False(viewModel.IsDirty);
            Assert.Equal("MCP Overlord - magic.json", viewModel.WindowTitle);
            Assert.True(File.Exists(path));
        }

        [Fact]
        public void NewAndLoadAskBeforeDiscardingEdits()
        {
            (MainViewModel viewModel, FakeWorld world, _) = Create();
            viewModel.NewConfig();
            world.Interaction.EditApplicationAction = editor =>
            {
                editor.Name = "Game";
                editor.Executable = "C:\\game.exe";
                editor.WindowTitle = "Lobby";
                Assert.True(editor.TryCommit());
            };
            viewModel.AddApplication();
            WaitForApplications(viewModel, 1, "Game");

            world.Interaction.NextSaveChoice = SaveChangesChoice.Cancel;
            viewModel.NewConfig();
            Assert.Single(viewModel.Applications);

            world.Interaction.NextSaveChoice = SaveChangesChoice.Discard;
            viewModel.NewConfig();
            WaitForApplications(viewModel, 0, null);
            Assert.Empty(viewModel.Applications);
            Assert.Equal("MCP Overlord - New", viewModel.WindowTitle);

            viewModel.AddApplication();
            string other = Path.Combine(NewDirectory(), "other.json");
            File.WriteAllText(
                other,
                "{\"schemaVersion\":1,\"applications\":[{\"name\":\"Other\",\"executable\":\"C:\\\\other.exe\",\"windowTitle\":\"Other\"}]}\n");
            world.Interaction.NextSaveChoice = SaveChangesChoice.Cancel;
            world.Interaction.NextPath = other;
            viewModel.LoadConfigCommand.Execute(null);
            WaitForApplications(viewModel, 1, "Game");
            Assert.Contains(viewModel.Applications, row => row.Name == "Game");

            world.Interaction.NextSaveChoice = SaveChangesChoice.Discard;
            viewModel.LoadConfigCommand.Execute(null);
            WaitForApplications(viewModel, 1, "Other");
            Assert.Equal("MCP Overlord - other.json", viewModel.WindowTitle);
            Assert.Equal("Other", viewModel.Applications[0].Name);
        }

        [Fact]
        public void DuplicateRemoveAndServerSettingsUpdateTheLiveDocument()
        {
            (MainViewModel viewModel, FakeWorld world, ConfigStore store) = Create();
            viewModel.NewConfig();
            world.Interaction.EditApplicationAction = editor =>
            {
                if (string.IsNullOrEmpty(editor.Name))
                {
                    editor.Name = "Game";
                    editor.Executable = "C:\\game.exe";
                    editor.WindowTitle = "Lobby";
                }

                Assert.True(editor.TryCommit());
            };
            viewModel.AddApplication();
            WaitForApplications(viewModel, 1, "Game");
            viewModel.SelectedApplication = viewModel.Applications[0];
            viewModel.DuplicateSelected();
            WaitForApplications(viewModel, 2, "Game");

            Assert.Equal(2, viewModel.Applications.Count);
            Assert.Equal("Game (2)", viewModel.Applications[1].Name);
            Assert.Equal("Lobby", store.Document!.Applications[1].WindowTitle);

            viewModel.SelectedApplication = viewModel.Applications[1];
            world.Interaction.NextConfirm = false;
            viewModel.RemoveSelected();
            Assert.Equal(2, viewModel.Applications.Count);

            world.Interaction.NextConfirm = true;
            viewModel.RemoveSelected();
            WaitForApplications(viewModel, 1, "Game");
            Assert.Single(viewModel.Applications);

            world.Interaction.EditServerAction = editor =>
            {
                editor.HttpPortText = "5010";
                editor.IncludeHttpPort = true;
                Assert.True(editor.TryCommit());
            };
            viewModel.EditServerSettings();
            Assert.Equal(5010, store.Document.Server.HttpPort);
            Assert.True(viewModel.IsDirty);
            Assert.Contains("Unsaved changes", HelpContent.Text);
            Assert.Contains("The target sits to the right", HelpContent.Text);
            Assert.Contains("Generate", HelpContent.Text);
            Assert.Contains("Start menu", HelpContent.Text);
            Assert.Contains("--elevate", HelpContent.Text);
            Assert.Contains("Uninstall", HelpContent.Text);
        }

        private static void WaitForApplications(MainViewModel viewModel, int count, string? firstName)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    if (viewModel.Applications.Count == count
                        && (firstName == null || viewModel.Applications[0].Name == firstName))
                    {
                        return;
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                }
                catch (InvalidOperationException)
                {
                }

                Thread.Sleep(5);
            }

            Assert.Fail("Application list did not reach " + count + " rows.");
        }

        private static ConfigEditorSession Session()
        {
            return new ConfigEditorSession(new ConfigLoader(), new ConfigStore(), () => false);
        }

        private static ApplicationEditorViewModel Editor(
            ConfigEditorSession session,
            ApplicationDraft application,
            int index,
            bool isNew,
            IReadOnlyList<string> names)
        {
            return new ApplicationEditorViewModel(
                session.Draft.Clone(),
                application,
                index,
                isNew,
                names,
                session.TryApply);
        }

        private static (MainViewModel ViewModel, FakeWorld World, ConfigStore Store) Create()
        {
            var world = new FakeWorld();
            AppComposition composition = world.Build();
            var session = new ConfigEditorSession(
                composition.Loader,
                composition.Store,
                () => composition.State.State == ServerRunState.Running);
            var viewModel = new MainViewModel(
                composition.Store,
                composition.State,
                new FakeServerHost(),
                composition.Input,
                composition.Locator,
                composition.Log,
                composition.Stats,
                world.Interaction,
                world.Clipboard,
                new InlineUiInvoker(),
                session);
            return (viewModel, world, composition.Store);
        }

        private static string NewDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "mcp-editor-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}

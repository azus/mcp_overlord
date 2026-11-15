using mcp_overlord.Configuration;
using mcp_overlord.Services;
using mcp_overlord.UI;
using mcp_overlord.UI.Placement;

namespace mcp_overlord.tests
{
    public class ConfigEditorTests
    {
        private readonly ConfigLoader _loader = new ConfigLoader();

        [Fact]
        public void CreateNewValidatesAndOmitsUnsetServerFields()
        {
            string json = ConfigDraftSerializer.Write(ConfigDraft.CreateNew());

            ConfigLoadResult result = _loader.LoadJson(json, "config.json");

            Assert.True(result.Success);
            Assert.Empty(result.Document!.Applications);
            Assert.Equal(5000, result.Document.Server.HttpPort);
            Assert.DoesNotContain("\"server\"", json);
            Assert.Equal(json, ConfigDraftSerializer.Write(ConfigDraftSerializer.Read(json)));
        }

        [Fact]
        public void IncludedEmptyFieldsAreWrittenAndExcludedFieldsAreLeftOut()
        {
            var draft = new ConfigDraft();
            draft.Server.ApiKey.Value = "";
            draft.Server.ApiKey.IsIncluded = true;
            draft.Server.BindAddress.Value = "10.0.0.8";
            draft.Server.BindAddress.IsIncluded = true;
            var application = new ApplicationDraft
            {
                Name = "Game",
                Executable = "game.exe",
                WindowTitle = "Lobby"
            };
            application.Parameters.Value = "";
            application.Parameters.IsIncluded = true;
            application.Description.Value = "hidden";
            application.Description.IsIncluded = false;
            application.ClickBackend.Value = ClickBackend.MouseEvent;
            application.ClickBackend.IsIncluded = true;
            draft.Applications.Add(application);

            string json = ConfigDraftSerializer.Write(draft);

            Assert.Contains("\"parameters\": \"\"", json);
            Assert.Contains("\"apiKey\": \"\"", json);
            Assert.Contains("\"clickBackend\": \"mouseEvent\"", json);
            Assert.DoesNotContain("description", json);
            Assert.True(json.IndexOf("\"name\"", StringComparison.Ordinal) < json.IndexOf("\"executable\"", StringComparison.Ordinal));
            Assert.True(json.IndexOf("\"executable\"", StringComparison.Ordinal) < json.IndexOf("\"windowTitle\"", StringComparison.Ordinal));
            Assert.True(json.IndexOf("\"bindAddress\"", StringComparison.Ordinal) < json.IndexOf("\"apiKey\"", StringComparison.Ordinal));

            ConfigLoadResult loaded = _loader.LoadJson(json, Path.Combine(Path.GetTempPath(), "config.json"));
            Assert.True(loaded.Success);
            Assert.Null(loaded.Document!.Server.ApiKey);
            Assert.Null(loaded.Document.Applications[0].Parameters);
            Assert.Null(loaded.Document.Applications[0].Description);
            Assert.Equal(ClickBackend.MouseEvent, loaded.Document.Applications[0].ClickBackend);
        }

        [Fact]
        public void SampleRoundTripsWithoutLosingMeaning()
        {
            string sample = Path.Combine(AppContext.BaseDirectory, "Fixtures", "config_loader_sample.json");
            string json = File.ReadAllText(sample);
            ConfigLoadResult original = _loader.LoadJson(json, sample);
            string written = ConfigDraftSerializer.Write(ConfigDraftSerializer.Read(json));
            ConfigLoadResult again = _loader.LoadJson(written, sample);

            Assert.True(original.Success);
            Assert.True(again.Success);
            Assert.Equal(original.Document!.Server.BindAddress, again.Document!.Server.BindAddress);
            Assert.Equal(original.Document.Server.HttpPort, again.Document.Server.HttpPort);
            Assert.Equal(original.Document.Server.EnableHttps, again.Document.Server.EnableHttps);
            Assert.Equal(original.Document.Server.ApiKey, again.Document.Server.ApiKey);
            Assert.Equal(original.Document.Applications.Count, again.Document.Applications.Count);
            for (int index = 0; index < original.Document.Applications.Count; index++)
            {
                ApplicationEntry left = original.Document.Applications[index];
                ApplicationEntry right = again.Document.Applications[index];
                Assert.Equal(left.Name, right.Name);
                Assert.Equal(left.Executable, right.Executable);
                Assert.Equal(left.Parameters, right.Parameters);
                Assert.Equal(left.WorkingDirectory, right.WorkingDirectory);
                Assert.Equal(left.WindowTitle, right.WindowTitle);
                Assert.Equal(left.ProcessName, right.ProcessName);
                Assert.Equal(left.Description, right.Description);
                Assert.Equal(left.ClickBackend, right.ClickBackend);
            }
        }

        [Fact]
        public void EditingMarksTheDocumentDirtyAndUndoingClearsIt()
        {
            var session = NewSession(out _);
            session.New();
            Assert.False(session.IsDirty);
            Assert.Equal("New", session.DisplayName);

            ConfigDraft edited = session.Draft.Clone();
            edited.Server.HttpPort.Value = 5008;
            edited.Server.HttpPort.IsIncluded = true;
            Assert.True(session.TryApply(edited).Success);
            Assert.True(session.IsDirty);
            Assert.Equal(5008, session.Draft.Server.HttpPort.Value);

            Assert.True(session.TryApply(ConfigDraft.CreateNew()).Success);
            Assert.False(session.IsDirty);
            Assert.Equal(5000, session.Draft.Server.HttpPort.Value);
            Assert.False(session.Draft.Server.HttpPort.IsIncluded);
        }

        [Fact]
        public void SaveOnANewDocumentNeedsAPathAndSaveAsUpdatesTheName()
        {
            var session = NewSession(out ConfigStore store);
            session.New();
            ConfigEditResult needsPath = session.Save();
            Assert.True(needsPath.NeedsPath);
            Assert.False(needsPath.Success);

            string directory = NewDirectory();
            string path = Path.Combine(directory, "magic.json");
            ConfigEditResult saved = session.SaveAs(path);

            Assert.True(saved.Success);
            Assert.Equal(path, session.FilePath);
            Assert.Equal("magic.json", session.DisplayName);
            Assert.False(session.IsDirty);
            Assert.True(File.Exists(path));
            Assert.Equal(path, store.Document!.SourcePath);
        }

        [Fact]
        public void SaveAsToAnotherFolderMakesRelativePathsAbsolute()
        {
            string sourceDirectory = NewDirectory();
            string targetDirectory = NewDirectory();
            string source = Path.Combine(sourceDirectory, "config.json");
            File.WriteAllText(
                source,
                "{\"schemaVersion\":1,\"applications\":[{\"name\":\"Game\",\"executable\":\"game.exe\",\"workingDirectory\":\"work\",\"windowTitle\":\"Lobby\"}]}\n");
            var session = NewSession(out _);
            Assert.True(session.Open(source).Success);

            string destination = Path.Combine(targetDirectory, "moved.json");
            Assert.True(session.SaveAs(destination).Success);

            string written = File.ReadAllText(destination);
            string executable = Path.GetFullPath(Path.Combine(sourceDirectory, "game.exe"));
            string work = Path.GetFullPath(Path.Combine(sourceDirectory, "work"));
            Assert.Contains(executable.Replace("\\", "\\\\"), written);
            Assert.Contains(work.Replace("\\", "\\\\"), written);
            Assert.False(session.IsDirty);
            Assert.Equal(executable, session.Draft.Applications[0].Executable);
        }

        [Fact]
        public void SaveAsInTheSameFolderKeepsRelativePaths()
        {
            string directory = NewDirectory();
            string source = Path.Combine(directory, "config.json");
            File.WriteAllText(
                source,
                "{\"schemaVersion\":1,\"applications\":[{\"name\":\"Game\",\"executable\":\"game.exe\",\"windowTitle\":\"Lobby\"}]}\n");
            var session = NewSession(out _);
            Assert.True(session.Open(source).Success);

            string destination = Path.Combine(directory, "copy.json");
            Assert.True(session.SaveAs(destination).Success);

            string written = File.ReadAllText(destination);
            Assert.Contains("\"game.exe\"", written);
            Assert.DoesNotContain(directory.Replace("\\", "\\\\"), written);
        }

        [Fact]
        public void AnInvalidDraftIsNotApplied()
        {
            var session = NewSession(out ConfigStore store);
            session.New();
            ConfigDraft broken = session.Draft.Clone();
            broken.Applications.Add(new ApplicationDraft
            {
                Name = "",
                Executable = "",
                WindowTitle = ""
            });

            ConfigEditResult result = session.TryApply(broken);

            Assert.False(result.Success);
            Assert.NotEmpty(result.Errors);
            Assert.Empty(session.Draft.Applications);
            Assert.Empty(store.Document!.Applications);
        }

        [Fact]
        public void AFailedOpenKeepsTheCurrentDocument()
        {
            var session = NewSession(out _);
            session.New();
            ConfigEditResult result = session.Open(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));

            Assert.False(result.Success);
            Assert.True(session.HasDocument);
            Assert.Null(session.FilePath);
            Assert.Equal("New", session.DisplayName);
        }

        [Fact]
        public void ListenerChangesApplyLiveAndStayPendingWhileTheServerRuns()
        {
            bool running = false;
            var store = new ConfigStore();
            var session = new ConfigEditorSession(new ConfigLoader(), store, () => running);
            session.New();
            store.MarkBound(store.Document!.Server);
            running = true;

            ConfigDraft edited = session.Draft.Clone();
            var application = new ApplicationDraft
            {
                Name = "Game",
                Executable = "C:\\game.exe",
                WindowTitle = "Lobby"
            };
            edited.Applications.Add(application);
            edited.Server.HttpPort.Value = 5008;
            edited.Server.HttpPort.IsIncluded = true;

            Assert.True(session.TryApply(edited).Success);
            Assert.Equal("Game", store.Document!.Applications[0].Name);
            Assert.Equal(5008, store.Document.Server.HttpPort);
            Assert.True(store.ListenerSettingsPending);
            Assert.Equal(5000, store.BoundSettings!.HttpPort);
        }

        [Fact]
        public void CommandLineSplitterKeepsTheArgumentString()
        {
            Assert.Equal("-log \"a\"", CommandLineSplitter.GetArguments("\"C:\\Program Files\\game.exe\" -log \"a\""));
            Assert.Equal("-x", CommandLineSplitter.GetArguments("C:\\game.exe -x"));
            Assert.Equal("a b", CommandLineSplitter.GetArguments("game.exe   a b"));
            Assert.Equal("", CommandLineSplitter.GetArguments("C:\\game.exe"));
            Assert.Equal("", CommandLineSplitter.GetArguments("   "));
            Assert.Equal("", CommandLineSplitter.GetArguments(null));
            Assert.Equal("", CommandLineSplitter.GetArguments("\"unterminated"));
        }

        [Fact]
        public void AnExecutableDropFillsNameTargetAndProcess()
        {
            var reader = new RecordingShortcutReader();
            Assert.True(ShortcutFile.TryRead("C:\\Games\\Lobby.exe", reader, out ShortcutInfo info, out string? error));

            Assert.Null(error);
            Assert.Equal("Lobby", info.Name);
            Assert.Equal("C:\\Games\\Lobby.exe", info.TargetPath);
            Assert.Equal("Lobby", info.ProcessName);
            Assert.Equal("C:\\Games", info.WorkingDirectory);
            Assert.Null(info.Arguments);
            Assert.False(reader.Called);
        }

        [Fact]
        public void DialogBoundsAndTheLastFolderRoundTrip()
        {
            string path = Path.Combine(Path.GetTempPath(), "mcp-ui-" + Guid.NewGuid().ToString("N") + ".json");
            var store = new UiSettingsStore();
            var settings = new UiSettings
            {
                ApplicationEditorBoundsPixels = new PixelRect(10, 20, 640, 480),
                ServerSettingsBoundsPixels = new PixelRect(30, 40, 700, 500),
                LastConfigDirectory = "C:\\configs"
            };

            store.Save(path, settings);
            UiSettings loaded = store.Load(path);

            Assert.Equal(new PixelRect(10, 20, 640, 480), loaded.ApplicationEditorBoundsPixels);
            Assert.Equal(new PixelRect(30, 40, 700, 500), loaded.ServerSettingsBoundsPixels);
            Assert.Equal("C:\\configs", loaded.LastConfigDirectory);
        }

        private static ConfigEditorSession NewSession(out ConfigStore store)
        {
            store = new ConfigStore();
            return new ConfigEditorSession(new ConfigLoader(), store, () => false);
        }

        private static string NewDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "mcp-editor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private sealed class RecordingShortcutReader : IShortcutReader
        {
            public bool Called { get; private set; }

            public bool TryRead(string path, out ShortcutInfo info, out string? error)
            {
                Called = true;
                info = new ShortcutInfo();
                error = null;
                return true;
            }
        }
    }
}

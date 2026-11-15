using mcp_overlord.Configuration;
using mcp_overlord.Startup;

namespace mcp_overlord.tests
{
    public class ConfigLoaderTests
    {
        private readonly ConfigLoader _loader = new ConfigLoader();

        [Fact]
        public void EmbeddedSchemaMatchesTheContract()
        {
            Assert.Contains("https://json-schema.org/draft/2020-12/schema", _loader.SchemaText);
            Assert.Contains("\"const\": 1", _loader.SchemaText);
            Assert.Contains("clickBackend", _loader.SchemaText);
            Assert.Contains("\"mouseEvent\"", _loader.SchemaText);
            Assert.Contains("\"sendInput\"", _loader.SchemaText);
            Assert.Contains("anyOf", _loader.SchemaText);
            Assert.Contains("\"minLength\": 16", _loader.SchemaText);
        }

        [Fact]
        public void MissingSchemaVersionIsRejectedBeforeTheSchema()
        {
            ConfigLoadResult result = _loader.LoadJson("{\"applications\":[]}", "config.json");

            Assert.False(result.Success);
            Assert.Equal(ConfigLoader.SchemaVersionRequiredMessage, result.Errors[0].Message);
        }

        [Fact]
        public void NonIntegerSchemaVersionIsRejected()
        {
            ConfigLoadResult result = _loader.LoadJson("{\"schemaVersion\":\"1\",\"applications\":[]}", "config.json");

            Assert.False(result.Success);
            Assert.Equal(ConfigLoader.SchemaVersionRequiredMessage, result.Errors[0].Message);
        }

        [Fact]
        public void UnknownSchemaVersionNamesTheAcceptedVersion()
        {
            ConfigLoadResult result = _loader.LoadJson("{\"schemaVersion\":2,\"applications\":[]}", "config.json");

            Assert.False(result.Success);
            Assert.Equal(
                "schemaVersion 2 is not supported. This build accepts schemaVersion 1.",
                result.Errors[0].Message);
            Assert.Equal("/schemaVersion", result.Errors[0].Path);
        }

        [Fact]
        public void SchemaRejectsAShortApiKeyAndAnApplicationWithoutALocator()
        {
            ConfigLoadResult shortKey = _loader.LoadJson(
                "{\"schemaVersion\":1,\"server\":{\"apiKey\":\"short\"},\"applications\":[]}",
                "config.json");
            ConfigLoadResult noLocator = _loader.LoadJson(
                "{\"schemaVersion\":1,\"applications\":[{\"name\":\"Only\"}]}",
                "config.json");

            Assert.False(shortKey.Success);
            Assert.Contains(shortKey.Errors, error => error.Path.Contains("apiKey") || error.Message.Contains("apiKey") || error.Message.Contains("16"));
            Assert.False(noLocator.Success);
        }

        [Fact]
        public void DuplicateNamePortClashBadBindAndMissingCertificateAreReported()
        {
            string directory = NewDirectory();
            string certificate = Path.Combine(directory, "missing.pfx");
            string json =
                "{" +
                "\"schemaVersion\":1," +
                "\"server\":{\"httpPort\":5000,\"httpsPort\":5000,\"enableHttps\":true,\"bindAddress\":\"localhost\",\"certificate\":{\"path\":" + Quote(certificate) + "}}," +
                "\"applications\":[" +
                "{\"name\":\"Same\",\"executable\":\"C:\\\\game.exe\",\"windowTitle\":\"A\"}," +
                "{\"name\":\"Same\",\"executable\":\"C:\\\\game.exe\",\"windowTitle\":\"B\"}" +
                "]}";

            ConfigLoadResult result = _loader.LoadJson(json, Path.Combine(directory, "config.json"));

            Assert.False(result.Success);
            Assert.Contains(result.Errors, error => error.Message == "Application name 'Same' is duplicated.");
            Assert.Contains(result.Errors, error => error.Message == "httpsPort must differ from httpPort when HTTPS is enabled.");
            Assert.Contains(result.Errors, error => error.Message == "bindAddress must be an IP address.");
            Assert.Contains(result.Errors, error => error.Message.StartsWith("Certificate file was not found:", StringComparison.Ordinal));
        }

        [Fact]
        public void HttpsWithoutACertificateFileIsAccepted()
        {
            ConfigLoadResult generated = _loader.LoadJson(
                "{\"schemaVersion\":1,\"server\":{\"enableHttps\":true},\"applications\":[]}",
                "config.json");
            ConfigLoadResult named = _loader.LoadJson(
                "{\"schemaVersion\":1,\"server\":{\"enableHttps\":true,\"certificate\":{\"hostNames\":[\"play.example.com\"]}},\"applications\":[]}",
                "config.json");

            Assert.True(generated.Success);
            Assert.Null(generated.Document!.Server.Certificate);
            Assert.True(named.Success);
            Assert.Null(named.Document!.Server.Certificate!.Path);
            Assert.Equal("play.example.com", named.Document.Server.Certificate.HostNames[0]);
        }

        [Fact]
        public void HostNamesArePartOfTheListenerIdentity()
        {
            var left = new ServerSettings(5000, 5001, true, new CertificateSettings(null, "", new[] { "a.example" }), "127.0.0.1", null);
            var right = new ServerSettings(5000, 5001, true, new CertificateSettings(null, "", new[] { "b.example" }), "127.0.0.1", null);
            var same = new ServerSettings(5000, 5001, true, new CertificateSettings(null, "", new[] { "A.Example" }), "127.0.0.1", null);

            Assert.False(left.HasSameListener(right));
            Assert.True(left.HasSameListener(same));
        }

        [Fact]
        public void LogRetentionDefaultsAndRoundTrips()
        {
            ConfigLoadResult omitted = _loader.LoadJson("{\"schemaVersion\":1,\"applications\":[]}", "config.json");
            Assert.True(omitted.Success);
            Assert.Equal(ServerSettings.DefaultLogRetention, omitted.Document!.Server.LogRetention);

            ConfigLoadResult loaded = _loader.LoadJson(
                "{\"schemaVersion\":1,\"server\":{\"logRetention\":250},\"applications\":[]}",
                "config.json");
            Assert.True(loaded.Success);
            Assert.Equal(250, loaded.Document!.Server.LogRetention);

            ConfigDraft draft = ConfigDraftSerializer.Read("{\"schemaVersion\":1,\"server\":{\"logRetention\":250},\"applications\":[]}");
            Assert.True(draft.Server.LogRetention.IsIncluded);
            Assert.Equal(250, draft.Server.LogRetention.Value);
            string written = ConfigDraftSerializer.Write(draft);
            ConfigLoadResult again = _loader.LoadJson(written, "config.json");
            Assert.True(again.Success);
            Assert.Equal(250, again.Document!.Server.LogRetention);

            ConfigLoadResult tooSmall = _loader.LoadJson(
                "{\"schemaVersion\":1,\"server\":{\"logRetention\":50},\"applications\":[]}",
                "config.json");
            Assert.False(tooSmall.Success);

            var changed = ServerSettings.CreateDefault() with { LogRetention = 5000 };
            Assert.True(ServerSettings.CreateDefault().HasSameListener(changed));
        }

        [Fact]
        public void RelativeExecutableResolvesAndAMissingFileIsAWarning()
        {
            string directory = NewDirectory();
            string source = Path.Combine(directory, "config.json");
            ConfigLoadResult missing = _loader.LoadJson(
                "{\"schemaVersion\":1,\"applications\":[{\"name\":\"A\",\"executable\":\"game.exe\",\"windowTitle\":\"Game\",\"clickBackend\":\"sendInput\"}]}",
                source);

            Assert.True(missing.Success);
            Assert.Equal(Path.GetFullPath(Path.Combine(directory, "game.exe")), missing.Document!.Applications[0].Executable);
            Assert.Equal(directory, missing.Document.Applications[0].WorkingDirectory);
            Assert.Equal(ClickBackend.SendInput, missing.Document.Applications[0].ClickBackend);
            Assert.Contains(missing.Warnings, warning => warning.StartsWith("Executable was not found:", StringComparison.Ordinal));

            File.WriteAllText(missing.Document.Applications[0].Executable!, "x");
            ConfigLoadResult present = _loader.LoadJson(
                "{\"schemaVersion\":1,\"applications\":[{\"name\":\"A\",\"executable\":\"game.exe\",\"windowTitle\":\"Game\",\"workingDirectory\":\"work\"}]}",
                source);
            Assert.Empty(present.Warnings);
            Assert.Equal(Path.GetFullPath(Path.Combine(directory, "work")), present.Document!.Applications[0].WorkingDirectory);
            Assert.Equal(ClickBackend.MouseEvent, present.Document.Applications[0].ClickBackend);
        }

        [Fact]
        public void EmptyOptionalStringsAreNotSetAndAnEmptyApiKeyIsAccepted()
        {
            string directory = NewDirectory();
            ConfigLoadResult result = _loader.LoadJson(
                "{\"schemaVersion\":1,\"server\":{\"bindAddress\":\"\",\"apiKey\":\"\",\"httpPort\":5010}," +
                "\"applications\":[{\"name\":\"A\",\"executable\":\"game.exe\",\"windowTitle\":\"Lobby\"," +
                "\"parameters\":\"\",\"workingDirectory\":\"\",\"processName\":\"\",\"description\":\"\"}]}",
                Path.Combine(directory, "config.json"));

            Assert.True(result.Success);
            Assert.Equal("127.0.0.1", result.Document!.Server.BindAddress);
            Assert.Null(result.Document.Server.ApiKey);
            Assert.Equal(5010, result.Document.Server.HttpPort);
            Assert.Null(result.Document.Applications[0].Parameters);
            Assert.Null(result.Document.Applications[0].ProcessName);
            Assert.Null(result.Document.Applications[0].Description);
            Assert.Equal("game", result.Document.Applications[0].EffectiveProcessName);
            Assert.Equal(directory, result.Document.Applications[0].WorkingDirectory);

            ConfigLoadResult shortKey = _loader.LoadJson(
                "{\"schemaVersion\":1,\"server\":{\"apiKey\":\"short-key\"},\"applications\":[]}",
                "config.json");
            Assert.False(shortKey.Success);
        }

        [Fact]
        public void MissingExecutableOrWindowTitleIsRejected()
        {
            ConfigLoadResult missingExecutable = _loader.LoadJson(
                "{\"schemaVersion\":1,\"applications\":[{\"name\":\"A\",\"windowTitle\":\"Lobby\"}]}",
                "config.json");
            ConfigLoadResult missingTitle = _loader.LoadJson(
                "{\"schemaVersion\":1,\"applications\":[{\"name\":\"A\",\"executable\":\"game.exe\"}]}",
                "config.json");

            Assert.False(missingExecutable.Success);
            Assert.False(missingTitle.Success);
        }

        [Fact]
        public void ExecutableWithoutAFileNameIsRejected()
        {
            ConfigLoadResult result = _loader.LoadJson(
                "{\"schemaVersion\":1,\"applications\":[{\"name\":\"A\",\"executable\":\"C:\\\\\",\"windowTitle\":\"Game\"}]}",
                "config.json");

            Assert.False(result.Success);
            Assert.Contains(result.Errors, error => error.Message == "executable must include a file name.");
        }

        [Fact]
        public void MissingFileIsAnError()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            ConfigLoadResult result = _loader.LoadFromFile(path);

            Assert.False(result.Success);
            Assert.Contains("Configuration file was not found:", result.Errors[0].Message);
        }

        [Fact]
        public void SampleFileLoads()
        {
            string sample = Path.Combine(AppContext.BaseDirectory, "Fixtures", "config_loader_sample.json");

            ConfigLoadResult result = _loader.LoadFromFile(sample);

            Assert.True(result.Success);
            Assert.Equal(1, result.Document!.SchemaVersion);
            Assert.Equal("127.0.0.1", result.Document.Server.BindAddress);
            Assert.Equal(5000, result.Document.Server.HttpPort);
            Assert.False(result.Document.Server.EnableHttps);
            Assert.Null(result.Document.Server.ApiKey);
            Assert.Equal(2, result.Document.Applications.Count);
            Assert.Equal(ClickBackend.MouseEvent, result.Document.Applications[0].ClickBackend);
            Assert.Equal("InfiniteMagicraid", result.Document.Applications[1].ProcessName);
            Assert.Equal(ClickBackend.SendInput, result.Document.Applications[1].ClickBackend);
            Assert.Contains("output_log.txt", result.Document.Applications[1].Parameters);
        }

        private static string NewDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "mcp-config-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\\", "\\\\") + "\"";
        }
    }

    public class ConfigStoreTests
    {
        [Fact]
        public void ReloadKeepsThePreviousListenerUntilTheNextStartAndDropsTheCache()
        {
            var world = new FakeWorld();
            world.AddWindow(1, "Game", "Lobby", zOrder: 1);
            AppComposition composition = world.Build();
            ApplicationEntry application = Entry("Game", "Lobby");

            Assert.Equal(1, composition.Locator.Find(application, false).Hwnd.ToInt64());
            world.AddWindow(2, "Game", "Lobby", foreground: true, zOrder: 0);
            Assert.Equal(1, composition.Locator.Find(application, false).Hwnd.ToInt64());

            var first = new ServerSettings(5000, 5001, false, null, "127.0.0.1", null);
            var second = new ServerSettings(5002, 5001, false, null, "127.0.0.1", null);
            composition.Store.Apply(Document(first, application), false);
            composition.Store.MarkBound(first);
            composition.Store.Apply(Document(second, application), true);

            Assert.True(composition.Store.ListenerSettingsPending);
            Assert.Equal(5000, composition.Store.BoundSettings!.HttpPort);
            Assert.Equal(2, composition.Locator.Find(application, false).Hwnd.ToInt64());

            composition.Store.MarkBound(second);
            Assert.False(composition.Store.ListenerSettingsPending);
            composition.Store.MarkUnbound();
            Assert.Null(composition.Store.BoundSettings);
            Assert.NotNull(composition.Store.Document);
        }

        private static ApplicationEntry Entry(string processName, string title)
        {
            return new ApplicationEntry("Game", "", null, null, title, processName, null, ClickBackend.MouseEvent);
        }

        private static ConfigDocument Document(ServerSettings server, ApplicationEntry application)
        {
            return new ConfigDocument(1, server, new[] { application }, "config.json");
        }
    }

    public class CommandLineTests
    {
        [Fact]
        public void ParsesConfigHelpAndUnknownArguments()
        {
            CommandLineParseResult config = CommandLineParser.Parse(new[] { "--config", "a.json" }, @"C:\work");
            CommandLineParseResult help = CommandLineParser.Parse(new[] { "-h" }, @"C:\work");
            CommandLineParseResult missing = CommandLineParser.Parse(new[] { "--config" }, @"C:\work");
            CommandLineParseResult unknown = CommandLineParser.Parse(new[] { "--nope" }, @"C:\work");

            Assert.Equal(Path.GetFullPath(Path.Combine(@"C:\work", "a.json")), config.ConfigPath);
            Assert.True(help.Help);
            Assert.Equal("Missing path after --config.", missing.Error);
            Assert.Equal("Unknown argument '--nope'.", unknown.Error);
            Assert.Contains("--config", CommandLineParser.UsageText);
            Assert.Contains("file.overlord", CommandLineParser.UsageText);
            Assert.Contains(CommandLineParser.ElevateArgument, CommandLineParser.UsageText);
            Assert.Contains(CommandLineParser.RegisterShellArgument, CommandLineParser.UsageText);
        }

        [Fact]
        public void ParsesRegisterShellAlone()
        {
            CommandLineParseResult alone = CommandLineParser.Parse(
                new[] { CommandLineParser.RegisterShellArgument },
                @"C:\work");

            Assert.True(alone.RegisterShell);
            Assert.False(alone.Elevate);
            Assert.False(alone.Help);
            Assert.Null(alone.ConfigPath);
            Assert.Null(alone.Error);
        }

        [Fact]
        public void ParsesElevateAloneAndWithAPath()
        {
            CommandLineParseResult alone = CommandLineParser.Parse(new[] { "--elevate" }, @"C:\work");
            CommandLineParseResult withPath = CommandLineParser.Parse(
                new[] { "--elevate", "game.overlord" },
                @"C:\work");
            CommandLineParseResult pathFirst = CommandLineParser.Parse(
                new[] { "game.overlord", "--elevate" },
                @"C:\work");

            Assert.True(alone.Elevate);
            Assert.Null(alone.ConfigPath);
            Assert.Null(alone.Error);
            Assert.False(alone.Help);
            Assert.True(withPath.Elevate);
            Assert.Equal(Path.GetFullPath(Path.Combine(@"C:\work", "game.overlord")), withPath.ConfigPath);
            Assert.True(pathFirst.Elevate);
            Assert.Equal(Path.GetFullPath(Path.Combine(@"C:\work", "game.overlord")), pathFirst.ConfigPath);
        }

        [Fact]
        public void ParsesOnePositionalConfigurationPath()
        {
            CommandLineParseResult positional = CommandLineParser.Parse(new[] { "game.overlord" }, @"C:\work");
            CommandLineParseResult flagAndPath = CommandLineParser.Parse(
                new[] { "--config", "a.overlord", "b.overlord" },
                @"C:\work");
            CommandLineParseResult twoPaths = CommandLineParser.Parse(
                new[] { "a.overlord", "b.overlord" },
                @"C:\work");

            Assert.Null(positional.Error);
            Assert.Equal(Path.GetFullPath(Path.Combine(@"C:\work", "game.overlord")), positional.ConfigPath);
            Assert.Equal("Only one configuration path can be supplied.", flagAndPath.Error);
            Assert.Equal("Only one configuration path can be supplied.", twoPaths.Error);
        }
    }
}

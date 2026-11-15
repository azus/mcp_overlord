using mcp_overlord.Configuration;
using mcp_overlord.Hosting;
using mcp_overlord.Services;
using mcp_overlord.Shell;
using mcp_overlord.Startup;
using mcp_overlord.UI;

namespace mcp_overlord.tests
{
    public class JumpListTests
    {
        [Fact]
        public void BuildsRunAsAdministratorWhenThereIsNoProductCode()
        {
            const string executablePath = @"C:\Program Files\MCP Overlord\mcp_overlord.exe";

            IReadOnlyList<JumpTaskDescriptor> missing = JumpListBuilder.Build(executablePath, null);
            IReadOnlyList<JumpTaskDescriptor> blank = JumpListBuilder.Build(executablePath, " ");
            IReadOnlyList<JumpTaskDescriptor> invalid = JumpListBuilder.Build(executablePath, "not-a-product-code");

            Assert.Single(missing);
            Assert.Single(blank);
            Assert.Single(invalid);
            Assert.Equal(JumpListBuilder.RunAsAdministratorTitle, missing[0].Title);
            Assert.Equal(executablePath, missing[0].ApplicationPath);
            Assert.Equal(CommandLineParser.ElevateArgument, missing[0].Arguments);
            Assert.Equal(executablePath, missing[0].IconResourcePath);
            Assert.Equal(0, missing[0].IconResourceIndex);
        }

        [Fact]
        public void BuildsUninstallWhenTheProductCodeIsAGuid()
        {
            const string executablePath = @"C:\App\mcp_overlord.exe";
            const string productCode = "{EA1684D4-1935-4FF8-8193-BB8098476FA6}";

            IReadOnlyList<JumpTaskDescriptor> tasks = JumpListBuilder.Build(
                executablePath,
                "ea1684d4-1935-4ff8-8193-bb8098476fa6");

            Assert.Equal(2, tasks.Count);
            Assert.Equal(JumpListBuilder.UninstallTitle, tasks[1].Title);
            Assert.Equal(
                Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
                tasks[1].ApplicationPath);
            Assert.Equal("/x " + productCode, tasks[1].Arguments);
            Assert.Equal(executablePath, tasks[1].IconResourcePath);
        }

        [Fact]
        public void RejectsAProductCodeThatIsNotAGuid()
        {
            Assert.Null(InstallationInfo.NormalizeProductCode(null));
            Assert.Null(InstallationInfo.NormalizeProductCode(" "));
            Assert.Null(InstallationInfo.NormalizeProductCode("not-a-product-code"));
            Assert.Equal(
                "{EA1684D4-1935-4FF8-8193-BB8098476FA6}",
                InstallationInfo.NormalizeProductCode("{ea1684d4-1935-4ff8-8193-bb8098476fa6}"));
        }

        [Fact]
        public void LoadAndSaveRecordTheRecentFile()
        {
            var world = new FakeWorld();
            AppComposition composition = world.Build();
            var session = new ConfigEditorSession(
                composition.Loader,
                composition.Store,
                () => composition.State.State == ServerRunState.Running);
            var recent = new RecordingRecentFiles();
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
                session,
                recentFiles: recent);

            string directory = Path.Combine(Path.GetTempPath(), "mcp-recent-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string missing = Path.Combine(directory, "missing.overlord");
            string path = Path.Combine(directory, "game.overlord");
            File.WriteAllText(
                path,
                "{\"schemaVersion\":1,\"applications\":[{\"name\":\"Game\",\"executable\":\"C:\\\\game.exe\",\"windowTitle\":\"Lobby\"}]}\n");

            Assert.False(viewModel.LoadConfig(missing, false));
            Assert.Empty(recent.Paths);

            Assert.True(viewModel.LoadConfig(path, false));
            Assert.True(viewModel.SaveConfig());

            string fullPath = Path.GetFullPath(path);
            Assert.Equal(new[] { fullPath, fullPath }, recent.Paths);
        }
    }
}

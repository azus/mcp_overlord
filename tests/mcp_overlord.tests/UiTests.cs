using System.Diagnostics;
using game_automation;
using mcp_overlord;
using mcp_overlord.Configuration;
using mcp_overlord.Tools;
using mcp_overlord.Hosting;
using mcp_overlord.Logging;
using mcp_overlord.Services;
using mcp_overlord.UI;
using ModelContextProtocol.Protocol;

namespace mcp_overlord.tests
{
    public class LogAndStatsTests
    {
        [Fact]
        public void LogBufferDropsTheOldestAndSurvivesConcurrentAppends()
        {
            var buffer = new LogBuffer();
            Assert.Equal(LogBuffer.DefaultRetention, buffer.Retention);
            buffer.Retention = 1;
            Assert.Equal(LogBuffer.MinimumRetention, buffer.Retention);
            buffer.Retention = 120;
            for (int index = 0; index < 121; index++)
            {
                buffer.Append(LogLevel.Info, "test", index.ToString());
            }

            Assert.Equal(120, buffer.Count);
            Assert.Equal("1", buffer.Snapshot()[0].Message);
            Assert.Equal("120", buffer.Snapshot()[^1].Message);
            buffer.Retention = 100;
            Assert.Equal(100, buffer.Count);
            Assert.Equal("21", buffer.Snapshot()[0].Message);

            var concurrent = new LogBuffer();
            Parallel.For(0, 200, index => concurrent.Append(LogLevel.Warning, "test", "m" + index));
            Assert.Equal(200, concurrent.Count);
            concurrent.Clear();
            Assert.Equal(0, concurrent.Count);
        }

        [Fact]
        public void StatsAccumulateRequestsErrorsAndAverages()
        {
            var stats = new StatsCounter();
            Parallel.For(0, 50, index => stats.RecordToolSuccess("mouse_move", 10, false, 0, true));
            stats.RecordToolSuccess("take_screenshot", 30, true, 100, false);
            stats.RecordToolError("key_press", 20);
            stats.RecordHttpFailure();
            stats.SetActiveSessions(3);

            StatsSnapshot snapshot = stats.Snapshot();
            Assert.Equal(52, snapshot.Requests);
            Assert.Equal(2, snapshot.Errors);
            Assert.Equal(1, snapshot.Screenshots);
            Assert.Equal(100, snapshot.Bytes);
            Assert.Equal(50, snapshot.Inputs);
            Assert.Equal(3, snapshot.ActiveSessions);
            Assert.Equal(20, snapshot.LastLatencyMilliseconds);
            Assert.Equal(52, snapshot.PerTool.Values.Sum());
            Assert.Equal((50 * 10 + 30 + 20) / 52.0, snapshot.AverageLatencyMilliseconds, 3);
            Assert.Equal("0 B", StatsText.FormatBytes(0));
            Assert.Equal("1.5 KB", StatsText.FormatBytes(1536));
            Assert.Equal("2.0 MB", StatsText.FormatBytes(2 * 1024 * 1024));
            Assert.Equal("01:01:01", StatsText.FormatUptime(TimeSpan.FromSeconds(3661)));
            Assert.Equal("key_press 1   mouse_move 50", StatsText.FormatPerTool(new Dictionary<string, long>
            {
                ["mouse_move"] = 50,
                ["key_press"] = 1
            }));
            Assert.Contains("...", StatsText.EllipsizeMiddle(new string('a', 20) + new string('b', 20), 15));
        }

        [Fact]
        public async Task ToolCallLogRecordsSuccessFailureAndExceptions()
        {
            var log = new LogBuffer();
            var stats = new StatsCounter();
            CallToolResult success = await ToolCallLog.Invoke(
                log,
                stats,
                new CallToolRequestParams { Name = "mouse_move" },
                () => new ValueTask<CallToolResult>(ToolResponses.Text("{\"ok\":true}")));

            CallToolResult failed = await ToolCallLog.Invoke(
                log,
                stats,
                new CallToolRequestParams { Name = "key_press" },
                () => new ValueTask<CallToolResult>(ToolResponses.Error("nope")));

            CallToolResult thrown = await ToolCallLog.Invoke(
                log,
                stats,
                new CallToolRequestParams { Name = "type_text" },
                () => throw new InvalidOperationException("boom"));

            Assert.NotEqual(true, success.IsError);
            Assert.True(failed.IsError);
            Assert.True(thrown.IsError);
            Assert.Contains("InvalidOperationException", ((TextContentBlock)thrown.Content[0]).Text);
            Assert.Contains(log.Snapshot(), entry => entry.Message.StartsWith("request mouse_move", StringComparison.Ordinal));
            Assert.Contains(log.Snapshot(), entry => entry.Message.Contains("response mouse_move", StringComparison.Ordinal));
            Assert.Contains(log.Snapshot(), entry => entry.Message.Contains("error key_press nope", StringComparison.Ordinal));
            Assert.Equal(3, stats.Snapshot().Requests);
            Assert.Equal(2, stats.Snapshot().Errors);
            Assert.True(ToolCallLog.IsInput("mouse_click"));
            Assert.False(ToolCallLog.IsInput("find_window"));
        }

        [Fact]
        public async Task ToolCallLogReturnsWhenTheToolDoesNotFinish()
        {
            var log = new LogBuffer();
            var stats = new StatsCounter();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CallToolResult result = await ToolCallLog.Invoke(
                log,
                stats,
                new CallToolRequestParams { Name = "list_applications" },
                async () =>
                {
                    await gate.Task;
                    return ToolResponses.Text("late");
                },
                TimeSpan.FromMilliseconds(50));

            Assert.True(result.IsError);
            Assert.Equal(ToolErrors.TimedOut, ((TextContentBlock)result.Content[0]).Text);
            Assert.Contains(log.Snapshot(), entry => entry.Level == LogLevel.Warning && entry.Message.Contains(ToolErrors.TimedOut, StringComparison.Ordinal));
            Assert.Equal(1, stats.Snapshot().Requests);
            Assert.Equal(1, stats.Snapshot().Errors);
            gate.TrySetResult();
        }
    }

    public class EndpointAndStateTests
    {
        [Fact]
        public void UrlsOriginsAndScrubbing()
        {
            var settings = new ServerSettings(5000, 5001, true, new CertificateSettings("c.pfx", "secret-pass", Array.Empty<string>()), "10.0.0.8", "api-key-value-123");
            Assert.Equal("http://127.0.0.1:5000/mcp", EndpointFormatter.PublicEndpoints(settings)[0].Url);
            Assert.Equal("https://10.0.0.8:5001/mcp", EndpointFormatter.PublicEndpoints(settings)[1].Url);
            Assert.Contains("http://127.0.0.1:5000", EndpointFormatter.BindUrls(settings));
            Assert.Contains("https://10.0.0.8:5001", EndpointFormatter.BindUrls(settings));
            Assert.DoesNotContain("http://10.0.0.8:5000", EndpointFormatter.BindUrls(settings));
            Assert.DoesNotContain(EndpointFormatter.BindUrls(settings), url => url.Contains("/mcp"));
            Assert.Contains("http://127.0.0.1:5000", EndpointFormatter.AllowedOrigins(settings));
            Assert.Contains("https://[::1]:5001", EndpointFormatter.AllowedOrigins(settings));
            Assert.Equal("key *** pass ***", EndpointFormatter.Scrub("key api-key-value-123 pass secret-pass", settings));

            var local = new ServerSettings(5000, 5001, true, new CertificateSettings("c.pfx", "secret-pass", Array.Empty<string>()), "127.0.0.1", null);
            Assert.Equal("http://127.0.0.1:5000/mcp", EndpointFormatter.PublicEndpoints(local)[0].Url);
            Assert.Equal("https://127.0.0.1:5001/mcp", EndpointFormatter.PublicEndpoints(local)[1].Url);
            Assert.Contains("https://127.0.0.1:5001", EndpointFormatter.BindUrls(local));

            var named = new ServerSettings(5000, 5001, true, new CertificateSettings(null, "", new[] { "play.example.com" }), "0.0.0.0", "api-key-value-123");
            Assert.Equal("https://play.example.com:5001/mcp", EndpointFormatter.PublicEndpoints(named)[1].Url);
            Assert.Contains("https://0.0.0.0:5001", EndpointFormatter.BindUrls(named));
            Assert.Contains("https://play.example.com:5001", EndpointFormatter.AllowedOrigins(named));

            var wildcard = new ServerSettings(5000, 5001, true, null, "0.0.0.0", "api-key-value-123");
            Assert.Equal("https://" + Environment.MachineName + ":5001/mcp", EndpointFormatter.PublicEndpoints(wildcard)[1].Url);
        }

        [Fact]
        public void StateMachineMovesThroughTheListenerStates()
        {
            var state = new ServerStateMachine();
            int changes = 0;
            state.Changed += () => changes++;
            state.BeginStart();
            Assert.Equal(ServerRunState.Starting, state.State);
            state.MarkRunning();
            Assert.NotNull(state.RunningSince);
            state.MarkError("port");
            Assert.Equal("port", state.ErrorMessage);
            state.DismissError();
            Assert.Equal(ServerRunState.Stopped, state.State);
            Assert.Null(state.RunningSince);
            Assert.True(changes >= 4);
        }
    }

    public class MainViewModelTests
    {
        [Fact]
        public async Task StartStopPendingAndInvalidConfig()
        {
            var world = new FakeWorld();
            world.AddWindow(4, "Game", "Lobby");
            AppComposition composition = world.Build();
            var host = new FakeServerHost();
            var session = new ConfigEditorSession(
                composition.Loader,
                composition.Store,
                () => composition.State.State == ServerRunState.Running);
            var viewModel = new MainViewModel(
                composition.Store,
                composition.State,
                host,
                composition.Input,
                composition.Locator,
                composition.Log,
                composition.Stats,
                world.Interaction,
                world.Clipboard,
                new InlineUiInvoker(),
                session);

            Assert.Equal("Start", viewModel.StartButtonText);
            Assert.False(viewModel.CanToggleServer());
            Assert.Equal("No configuration loaded", viewModel.ConfigPathDisplay);

            ConfigLoadResult invalid = composition.Loader.LoadJson("{}", "missing.json");
            Assert.False(viewModel.ApplyLoad(invalid, true));
            Assert.Equal(ServerRunState.Error, composition.State.State);
            Assert.Contains("Configuration invalid", viewModel.StatusText);

            ConfigLoadResult local = composition.Loader.LoadJson(
                "{\"schemaVersion\":1,\"applications\":[{\"name\":\"Game\",\"executable\":\"C:\\\\game.exe\",\"windowTitle\":\"Lobby\"}]}",
                "config.json");
            Assert.True(viewModel.ApplyLoad(local, false));
            Assert.Equal(ServerRunState.Stopped, composition.State.State);
            Assert.True(viewModel.CanToggleServer());

            ConfigLoadResult remote = composition.Loader.LoadJson(
                "{\"schemaVersion\":1,\"server\":{\"bindAddress\":\"10.0.0.8\"},\"applications\":[{\"name\":\"Game\",\"executable\":\"C:\\\\game.exe\",\"windowTitle\":\"Lobby\"}]}",
                "remote.json");
            Assert.True(viewModel.ApplyLoad(remote, false));
            await viewModel.StartAsync();
            Assert.Equal(ServerRunState.Stopped, composition.State.State);

            ConfigLoadResult httpsWithoutKey = composition.Loader.LoadJson(
                "{\"schemaVersion\":1,\"server\":{\"bindAddress\":\"10.0.0.8\",\"enableHttps\":true},\"applications\":[{\"name\":\"Game\",\"executable\":\"C:\\\\game.exe\",\"windowTitle\":\"Lobby\"}]}",
                "remote.json");
            Assert.True(viewModel.ApplyLoad(httpsWithoutKey, false));
            await viewModel.StartAsync();
            Assert.Equal(ServerRunState.Stopped, composition.State.State);
            Assert.Equal(2, composition.Log.Snapshot().Count(entry => entry.Level == LogLevel.Error && entry.Message.Contains("requires HTTPS and an API key")));

            ConfigLoadResult secured = composition.Loader.LoadJson(
                "{\"schemaVersion\":1,\"server\":{\"bindAddress\":\"10.0.0.8\",\"enableHttps\":true,\"apiKey\":\"0123456789abcdef\"},\"applications\":[{\"name\":\"Game\",\"executable\":\"C:\\\\game.exe\",\"windowTitle\":\"Lobby\"}]}",
                "remote.json");
            Assert.True(viewModel.ApplyLoad(secured, false));
            host.Release = new TaskCompletionSource();
            Task starting = viewModel.StartAsync();
            await host.Started.Task;
            Assert.Equal(ServerRunState.Starting, composition.State.State);
            Assert.False(viewModel.CanToggleServer());
            host.Release.SetResult();
            await starting;
            Assert.Equal("Stop", viewModel.StartButtonText);

            ConfigLoadResult changedPort = composition.Loader.LoadJson(
                "{\"schemaVersion\":1,\"server\":{\"bindAddress\":\"10.0.0.8\",\"enableHttps\":true,\"apiKey\":\"0123456789abcdef\",\"httpPort\":5008},\"applications\":[{\"name\":\"Game\",\"executable\":\"C:\\\\game.exe\",\"windowTitle\":\"Lobby\"}]}",
                "remote.json");
            Assert.True(viewModel.ApplyLoad(changedPort, false));
            Assert.True(viewModel.ListenerSettingsPending);

            await viewModel.StopAsync();
            Assert.Equal("Start", viewModel.StartButtonText);
            Assert.False(host.IsRunning);
            Assert.Contains(composition.Log.Snapshot(), entry => entry.Message == "Stopped");

            host.Error = new IOException("address already in use");
            await viewModel.StartAsync();
            Assert.Equal(ServerRunState.Error, composition.State.State);
            Assert.Contains("address already in use", viewModel.StatusText);
        }

        [Fact]
        public void RefreshApplicationsReturnsWhileEnumerationIsBlockedAndSkipsAnother()
        {
            var world = new FakeWorld();
            AppComposition composition = world.Build();
            var blocking = new BlockingWindowEnumerator();
            var applied = new ManualResetEventSlim(false);
            var locator = new WindowLocator(
                blocking,
                new FakeProcesses(world),
                new FakeNative(world),
                new FakeDelay(world),
                composition.Log);
            var session = new ConfigEditorSession(
                composition.Loader,
                composition.Store,
                () => false);
            composition.Store.Apply(SampleDocument(), false);
            MainViewModel viewModel;
            try
            {
                var watch = Stopwatch.StartNew();
                viewModel = new MainViewModel(
                    composition.Store,
                    composition.State,
                    new FakeServerHost(),
                    composition.Input,
                    locator,
                    composition.Log,
                    composition.Stats,
                    world.Interaction,
                    world.Clipboard,
                    new SignalingUiInvoker(applied),
                    session);
                watch.Stop();
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1));
                Assert.True(blocking.Entered.Wait(TimeSpan.FromSeconds(2)));
                int calls = blocking.Calls;
                var skipped = Stopwatch.StartNew();
                viewModel.RefreshApplications();
                skipped.Stop();
                Assert.True(skipped.Elapsed < TimeSpan.FromSeconds(1));
                Assert.Equal(calls, blocking.Calls);
                blocking.Release.Set();
                Assert.True(applied.Wait(TimeSpan.FromSeconds(2)));
                DateTime settled = DateTime.UtcNow.AddSeconds(2);
                while (DateTime.UtcNow < settled && viewModel.IsApplicationRefreshRunning)
                {
                    Thread.Sleep(5);
                }

                Assert.False(viewModel.IsApplicationRefreshRunning);
                Assert.Equal("Game", viewModel.Applications[0].Name);
                Assert.Equal("stopped", viewModel.Applications[0].RunningText);
            }
            finally
            {
                blocking.Release.Set();
            }
        }

        private static ConfigDocument SampleDocument()
        {
            return new ConfigDocument(
                1,
                ServerSettings.CreateDefault(),
                new[]
                {
                    new ApplicationEntry("Game", "C:\\game.exe", null, null, "Lobby", null, null, ClickBackend.MouseEvent)
                },
                "config.json");
        }

        [Fact]
        public void SettingsRoundTrip()
        {
            string path = Path.Combine(Path.GetTempPath(), "mcp-ui-" + Guid.NewGuid().ToString("N") + ".json");
            var store = new UiSettingsStore();
            var settings = new UiSettings
            {
                Left = 12,
                Top = 24,
                Width = 900,
                Height = 600,
                WindowState = "Maximized",
                ApplicationsPanelWidth = 320,
                BottomPanelHeight = 340,
                BottomPanelTabIndex = 2,
                LogColumnWidths = new Dictionary<string, double> { ["Message"] = 400 },
                LogSortColumn = "Level",
                LogSortDirection = "Descending",
                LogLevelFilter = "Warning"
            };

            store.Save(path, settings);
            UiSettings loaded = store.Load(path);
            Assert.Equal(12, loaded.Left);
            Assert.Equal("Maximized", loaded.WindowState);
            Assert.Equal(340, loaded.BottomPanelHeight);
            Assert.Equal(2, loaded.BottomPanelTabIndex);
            Assert.Equal(400, loaded.LogColumnWidths["Message"]);
            Assert.Equal("Descending", loaded.LogSortDirection);
            Assert.Equal("Warning", loaded.LogLevelFilter);

            File.WriteAllText(path, "{");
            UiSettings broken = store.Load(path);
            Assert.True(double.IsNaN(broken.Left));
            Assert.Equal(1100, broken.Width);
        }
    }

    internal sealed class BlockingWindowEnumerator : IWindowEnumerator
    {
        private int _calls;
        private readonly ManualResetEventSlim _entered = new ManualResetEventSlim(false);
        private readonly ManualResetEventSlim _release = new ManualResetEventSlim(false);

        public ManualResetEventSlim Entered
        {
            get { return _entered; }
        }

        public ManualResetEventSlim Release
        {
            get { return _release; }
        }

        public int Calls
        {
            get { return Volatile.Read(ref _calls); }
        }

        public IReadOnlyList<WindowCandidate> Enumerate()
        {
            Interlocked.Increment(ref _calls);
            _entered.Set();
            _release.Wait();
            return Array.Empty<WindowCandidate>();
        }
    }

    internal sealed class SignalingUiInvoker : IUiInvoker
    {
        private readonly ManualResetEventSlim _applied;

        public SignalingUiInvoker(ManualResetEventSlim applied)
        {
            _applied = applied;
        }

        public void Invoke(Action action)
        {
            action();
            _applied.Set();
        }
    }

    internal sealed class FakeServerHost : IServerHost
    {
        public bool IsRunning { get; private set; }

        public Exception? Error { get; set; }

        public TaskCompletionSource Started { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource? Release { get; set; }

        public async Task StartAsync(ServerSettings settings, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            if (Release != null)
            {
                await Release.Task;
            }

            if (Error != null)
            {
                throw Error;
            }

            IsRunning = true;
        }

        public Task StopAsync()
        {
            IsRunning = false;
            return Task.CompletedTask;
        }
    }
}

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using mcp_overlord.Configuration;
using mcp_overlord.Hosting;
using mcp_overlord.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace mcp_overlord.tests
{
    public class HttpIntegrationTests
    {
        private static readonly string[] ToolNames =
        {
            "list_applications",
            "get_application_description",
            "launch_application",
            "find_window",
            "focus_window",
            "take_screenshot",
            "mouse_move",
            "mouse_click",
            "mouse_down",
            "mouse_up",
            "mouse_scroll",
            "key_press",
            "key_down",
            "key_up",
            "type_text",
            "is_locked",
            "lock"
        };

        [Fact]
        public async Task ClientListsAndCallsEveryTool()
        {
            await using HostSession session = await HostSession.Start(apiKey: null, https: false);
            await using McpClient client = await Connect(session.HttpUrl, null);

            IList<McpClientTool> tools = await client.ListToolsAsync();
            foreach (string name in ToolNames)
            {
                Assert.Contains(tools, tool => tool.Name == name);
            }

            CallToolResult list = await client.CallToolAsync("list_applications", new Dictionary<string, object?>());
            Assert.NotEqual(true, list.IsError);
            Assert.Contains("Game", Text(list));

            CallToolResult description = await client.CallToolAsync(
                "get_application_description",
                new Dictionary<string, object?> { ["name"] = "Game" });
            Assert.Contains("Hello", Text(description));

            CallToolResult launched = await client.CallToolAsync(
                "launch_application",
                new Dictionary<string, object?> { ["name"] = "Game", ["waitForWindowSeconds"] = 0 });
            Assert.Contains("started", Text(launched));

            CallToolResult found = await client.CallToolAsync(
                "find_window",
                new Dictionary<string, object?> { ["name"] = "Game" });
            Assert.Contains("\"found\":true", Text(found).Replace(" ", ""));

            CallToolResult focused = await client.CallToolAsync(
                "focus_window",
                new Dictionary<string, object?> { ["name"] = "Game" });
            Assert.NotEqual(true, focused.IsError);

            CallToolResult screenshot = await client.CallToolAsync(
                "take_screenshot",
                new Dictionary<string, object?> { ["name"] = "Game" });
            Assert.NotEqual(true, screenshot.IsError);
            Assert.Contains(screenshot.Content, block => block is ImageContentBlock);
            Assert.Contains("png", Text(screenshot));

            await CallMouse(client, "mouse_move");
            await CallMouse(client, "mouse_click");
            await CallMouse(client, "mouse_down");
            await CallMouse(client, "mouse_up");
            CallToolResult scroll = await client.CallToolAsync(
                "mouse_scroll",
                new Dictionary<string, object?> { ["name"] = "Game", ["x"] = 1, ["y"] = 1, ["delta"] = 1 });
            Assert.NotEqual(true, scroll.IsError);

            CallToolResult key = await client.CallToolAsync(
                "key_press",
                new Dictionary<string, object?> { ["name"] = "Game", ["key"] = "A" });
            Assert.NotEqual(true, key.IsError);
            CallToolResult down = await client.CallToolAsync(
                "key_down",
                new Dictionary<string, object?> { ["name"] = "Game", ["key"] = "B" });
            Assert.NotEqual(true, down.IsError);
            CallToolResult up = await client.CallToolAsync(
                "key_up",
                new Dictionary<string, object?> { ["name"] = "Game", ["key"] = "B" });
            Assert.NotEqual(true, up.IsError);
            CallToolResult typed = await client.CallToolAsync(
                "type_text",
                new Dictionary<string, object?> { ["name"] = "Game", ["text"] = "hi" });
            Assert.NotEqual(true, typed.IsError);

            CallToolResult locked = await client.CallToolAsync("is_locked", new Dictionary<string, object?>());
            Assert.NotEqual(true, locked.IsError);
            Assert.Contains("\"locked\":false", Text(locked).Replace(" ", ""));

            CallToolResult lockResult = await client.CallToolAsync("lock", new Dictionary<string, object?>());
            Assert.NotEqual(true, lockResult.IsError);
            Assert.Contains("\"locked\":true", Text(lockResult).Replace(" ", ""));
        }

        [Fact]
        public async Task ToolCallKeepsTheConnectionAndTheImage()
        {
            await using HostSession session = await HostSession.Start(null, false);
            await using McpClient client = await Connect(session.HttpUrl, null);
            CallToolResult list = await client.CallToolAsync("list_applications", new Dictionary<string, object?>());
            Assert.NotEqual(true, list.IsError);

            LogEntry? call = FindDetail(session, "list_applications");
            Assert.NotNull(call);
            HttpExchange exchange = call!.Detail!.Exchange!;
            Assert.True(IsLoopback(exchange.RemoteAddress));
            Assert.True(IsLoopback(exchange.LocalAddress));
            Assert.True(exchange.RemotePort > 0);
            Assert.True(exchange.LocalPort > 0);
            Assert.Contains("list_applications", System.Text.Encoding.UTF8.GetString(exchange.RequestBody));
            Assert.NotNull(exchange.ResponseBody);
            Assert.NotEmpty(exchange.ResponseBody!);

            CallToolResult shot = await client.CallToolAsync(
                "take_screenshot",
                new Dictionary<string, object?> { ["name"] = "Game" });
            Assert.NotEqual(true, shot.IsError);
            LogEntry? image = FindDetail(session, "response take_screenshot");
            Assert.NotNull(image);
            Assert.NotEmpty(image!.Detail!.Images);
            Assert.NotEmpty(image.Detail.Images[0].Bytes);
        }

        [Fact]
        public async Task ApiKeyAndOriginAreEnforced()
        {
            const string key = "0123456789abcdef";
            await using HostSession session = await HostSession.Start(key, false);

            using var http = new HttpClient();
            HttpResponseMessage missing = await http.GetAsync(session.HttpUrl);
            Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

            using var wrong = new HttpRequestMessage(HttpMethod.Get, session.HttpUrl);
            wrong.Headers.TryAddWithoutValidation("Authorization", "Bearer 0000000000000000");
            HttpResponseMessage wrongResponse = await http.SendAsync(wrong);
            Assert.Equal(HttpStatusCode.Unauthorized, wrongResponse.StatusCode);

            using var bearer = new HttpRequestMessage(HttpMethod.Get, session.HttpUrl);
            bearer.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
            HttpResponseMessage bearerResponse = await http.SendAsync(bearer);
            Assert.NotEqual(HttpStatusCode.Unauthorized, bearerResponse.StatusCode);

            using var header = new HttpRequestMessage(HttpMethod.Get, session.HttpUrl);
            header.Headers.TryAddWithoutValidation("X-API-Key", key);
            HttpResponseMessage headerResponse = await http.SendAsync(header);
            Assert.NotEqual(HttpStatusCode.Unauthorized, headerResponse.StatusCode);

            using var foreign = new HttpRequestMessage(HttpMethod.Get, session.HttpUrl);
            foreign.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
            foreign.Headers.TryAddWithoutValidation("Origin", "https://evil.example");
            HttpResponseMessage foreignResponse = await http.SendAsync(foreign);
            Assert.Equal(HttpStatusCode.Forbidden, foreignResponse.StatusCode);

            using var noOrigin = new HttpRequestMessage(HttpMethod.Get, session.HttpUrl);
            noOrigin.Headers.TryAddWithoutValidation("X-API-Key", key);
            HttpResponseMessage noOriginResponse = await http.SendAsync(noOrigin);
            Assert.NotEqual(HttpStatusCode.Forbidden, noOriginResponse.StatusCode);

            await using McpClient client = await Connect(session.HttpUrl, key);
            CallToolResult list = await client.CallToolAsync("list_applications", new Dictionary<string, object?>());
            Assert.NotEqual(true, list.IsError);
        }

        [Fact]
        public async Task StopCompletesOnABlockedSynchronizationContextWhileAClientIsConnected()
        {
            await using HostSession session = await HostSession.Start(null, false);
            await using McpClient client = await Connect(session.HttpUrl, null);
            Assert.NotNull(client);

            Exception? failure = null;
            using var finished = new ManualResetEventSlim(false);
            var thread = new Thread(StopOnBlockedContext);
            thread.IsBackground = true;
            thread.Start();

            Assert.True(
                finished.Wait(TimeSpan.FromSeconds(10)),
                "StopAsync did not finish while a synchronization context was blocked.");
            Assert.Null(failure);

            void StopOnBlockedContext()
            {
                try
                {
                    SynchronizationContext.SetSynchronizationContext(new IdleSynchronizationContext());
                    session.Composition.Host.StopAsync().GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    finished.Set();
                }
            }
        }

        [Fact]
        public async Task StopReleasesThePortAndABusyPortFails()
        {
            int port = FreePort();
            await using HostSession session = await HostSession.Start(null, false, port);
            await session.Composition.Host.StopAsync();

            await session.Composition.Host.StartAsync(session.Settings);
            await session.Composition.Host.StopAsync();

            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            try
            {
                await Assert.ThrowsAnyAsync<Exception>(() => session.Composition.Host.StartAsync(session.Settings));
            }
            finally
            {
                listener.Stop();
            }

            await session.Composition.Host.StartAsync(session.Settings);
        }

        [Fact]
        public async Task HttpsAcceptsAConfiguredCertificate()
        {
            await using HostSession session = await HostSession.Start(null, true);
            using var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using var http = new HttpClient(handler);
            HttpResponseMessage response = await http.GetAsync(session.HttpsUrl);
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }

        private static async Task CallMouse(McpClient client, string tool)
        {
            CallToolResult result = await client.CallToolAsync(
                tool,
                new Dictionary<string, object?> { ["name"] = "Game", ["x"] = 4, ["y"] = 5 });
            Assert.NotEqual(true, result.IsError);
        }

        private static async Task<McpClient> Connect(string url, string? apiKey)
        {
            var headers = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(apiKey))
            {
                headers["Authorization"] = "Bearer " + apiKey;
            }

            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(url),
                Name = "mcp_overlord.tests",
                AdditionalHeaders = headers
            });
            return await McpClient.CreateAsync(transport);
        }

        private static LogEntry? FindDetail(HostSession session, string messagePart)
        {
            foreach (LogEntry entry in session.Composition.Log.Snapshot())
            {
                if (entry.Detail?.Exchange != null
                    && entry.Message.Contains(messagePart, StringComparison.Ordinal)
                    && entry.Detail.Exchange.ResponseBody is { Length: > 0 })
                {
                    return entry;
                }
            }

            return null;
        }

        private static bool IsLoopback(string address)
        {
            return IPAddress.TryParse(address, out IPAddress? parsed) && IPAddress.IsLoopback(parsed);
        }

        private static string Text(CallToolResult result)
        {
            return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        /// <summary>
        /// Drops posted callbacks. A stop that resumes on the caller's synchronization context never finishes.
        /// </summary>
        private sealed class IdleSynchronizationContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state)
            {
                // Drop the callback. A blocked UI thread never runs it, so stop must not wait for it.
            }

            public override void Send(SendOrPostCallback d, object? state)
            {
                d(state);
            }
        }

        private sealed class HostSession : IAsyncDisposable
        {
            private HostSession(AppComposition composition, ServerSettings settings, string httpUrl, string? httpsUrl)
            {
                Composition = composition;
                Settings = settings;
                HttpUrl = httpUrl;
                HttpsUrl = httpsUrl;
            }

            public AppComposition Composition { get; }

            public ServerSettings Settings { get; }

            public string HttpUrl { get; }

            public string? HttpsUrl { get; }

            public static async Task<HostSession> Start(string? apiKey, bool https, int? port = null)
            {
                int httpPort = port ?? FreePort();
                int httpsPort = FreePort();
                string? certificatePath = null;
                if (https)
                {
                    certificatePath = WriteCertificate();
                }

                var world = new FakeWorld();
                world.AddWindow(4, "Game", "Lobby");
                AppComposition composition = world.Build();
                string certificateJson = https
                    ? ",\"enableHttps\":true,\"httpsPort\":" + httpsPort + ",\"certificate\":{\"path\":\"" + certificatePath!.Replace("\\", "\\\\") + "\",\"password\":\"secret\"}"
                    : "";
                string json =
                    "{\"schemaVersion\":1,\"server\":{\"bindAddress\":\"127.0.0.1\",\"httpPort\":" + httpPort + certificateJson + "},\"applications\":[{\"name\":\"Game\",\"executable\":\"C:\\\\game.exe\",\"windowTitle\":\"Lobby\",\"processName\":\"Game\",\"description\":\"Hello\"}]}";
                ConfigLoadResult loaded = composition.Loader.LoadJson(json, Path.Combine(Path.GetTempPath(), "mcp-http.json"));
                if (!loaded.Success)
                {
                    throw new InvalidOperationException(string.Join("; ", loaded.Errors.Select(error => error.Path + " " + error.Message)));
                }

                ServerSettings settings = loaded.Document!.Server;
                if (!string.IsNullOrEmpty(apiKey))
                {
                    settings = settings with { ApiKey = apiKey };
                }

                composition.Store.Apply(loaded.Document, false);
                await composition.Host.StartAsync(settings);
                return new HostSession(
                    composition,
                    settings,
                    "http://127.0.0.1:" + httpPort + "/mcp",
                    https ? "https://127.0.0.1:" + httpsPort + "/mcp" : null);
            }

            public async ValueTask DisposeAsync()
            {
                await Composition.Host.StopAsync();
            }

            private static string WriteCertificate()
            {
                using RSA rsa = RSA.Create(2048);
                var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    true));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },
                    false));
                var alternative = new SubjectAlternativeNameBuilder();
                alternative.AddIpAddress(IPAddress.Loopback);
                alternative.AddDnsName("localhost");
                request.CertificateExtensions.Add(alternative.Build());
                using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
                byte[] exported = certificate.Export(X509ContentType.Pfx, "secret");
                string path = Path.Combine(Path.GetTempPath(), "mcp-" + Guid.NewGuid().ToString("N") + ".pfx");
                File.WriteAllBytes(path, exported);
                return path;
            }
        }
    }
}

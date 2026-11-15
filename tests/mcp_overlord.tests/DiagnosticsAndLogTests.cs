using System.Net;
using mcp_overlord.Configuration;
using mcp_overlord.Diagnostics;
using mcp_overlord.Hosting;
using mcp_overlord.Logging;
using mcp_overlord.Services;
using mcp_overlord.UI;

namespace mcp_overlord.tests
{
    public class FormatterTests
    {
        [Fact]
        public void HexDumpShowsOffsetBytesAndAscii()
        {
            byte[] hello = System.Text.Encoding.ASCII.GetBytes("Hello");
            string line = HexDumpFormatter.FormatLine(hello, 0);

            Assert.StartsWith("00000000", line);
            Assert.Contains("48 65 6C 6C 6F", line);
            Assert.EndsWith("Hello", line);
            Assert.Equal(1, HexDumpFormatter.LineCount(hello.Length));

            byte[] seventeen = new byte[17];
            seventeen[16] = 0x41;
            Assert.Equal(2, HexDumpFormatter.LineCount(seventeen.Length));
            string second = HexDumpFormatter.FormatLine(seventeen, 1);
            Assert.StartsWith("00000010", second);
            Assert.EndsWith("A", second);
        }

        [Fact]
        public void JsonFormatterPrettyPrintsElidesBase64AndReadsEventFrames()
        {
            string formatted = JsonFormatter.Format("{\"ok\":true}");
            Assert.Contains("\"ok\": true", formatted);

            string payload = new string('A', 1100);
            string elided = JsonFormatter.Format("{\"image\":\"" + payload + "\"}");
            Assert.Contains("Image tab", elided);
            Assert.DoesNotContain(payload, elided);

            string stream = "event: message\r\ndata: {\"ok\":true}\r\n\r\n";
            string events = JsonFormatter.Format(stream);
            Assert.Contains("event: message", events);
            Assert.Contains("\"ok\": true", events);

            McpReply reply = McpReplyParser.Parse(stream.Replace("{\"ok\":true}", "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[{\"name\":\"lock\"}]}}"));
            Assert.Equal(1, reply.ToolCount);
            Assert.Equal("lock", reply.ToolNames);
        }

        [Fact]
        public void PayloadViewNamesTheConnection()
        {
            var exchange = new HttpExchange
            {
                RemoteAddress = "::1",
                RemotePort = 9,
                LocalAddress = "127.0.0.1",
                LocalPort = 5000,
                Scheme = "http",
                Method = "POST",
                Path = "/mcp",
                ResponseStatus = 200,
                RequestBody = System.Text.Encoding.UTF8.GetBytes("{\"a\":1}"),
                ResponseBody = System.Text.Encoding.UTF8.GetBytes("{\"b\":2}")
            };
            var entry = new LogEntry(DateTimeOffset.Now, LogLevel.Info, "mcp", "hello", new LogDetail { Exchange = exchange });
            LogPayloadView view = LogPayloads.Describe(entry);

            Assert.True(view.HasNetwork);
            Assert.Contains(view.Fields, field => field.Label == "Source" && field.Value == "[::1]:9");
            Assert.Contains(view.Fields, field => field.Label == "Destination" && field.Value == "127.0.0.1:5000");
            Assert.Contains("\"a\": 1", view.RequestJson);
            Assert.Contains("\"b\": 2", view.ResponseJson);
        }
    }

    public class ElevationViewModelTests
    {
        [Fact]
        public void ElevatedProcessPrefixesTheTitle()
        {
            AppComposition composition = new FakeWorld().Build();
            var viewModel = Create(composition, new ScriptedElevation { IsElevated = true }, new FakeServerHost());

            viewModel.NewConfig();

            Assert.Equal("Administrator: MCP Overlord - New", viewModel.WindowTitle);
            Assert.False(viewModel.ShowRestartAsAdministrator);
        }

        [Fact]
        public async Task CancelledRestartStaysOpenAndRestartsTheServer()
        {
            AppComposition composition = new FakeWorld().Build();
            var host = new FakeServerHost();
            var viewModel = Create(composition, new ScriptedElevation { Started = false }, host);
            int shutdowns = 0;
            viewModel.ShutdownRequested += () => shutdowns++;
            viewModel.NewConfig();
            await viewModel.StartAsync();

            await viewModel.RestartAsAdministratorAsync();

            Assert.Equal(0, shutdowns);
            Assert.Contains(composition.Log.Snapshot(), entry => entry.Message.Contains("cancelled", StringComparison.Ordinal));
            Assert.True(host.IsRunning);
        }

        [Fact]
        public async Task AcceptedRestartShutsDownWithTheSavedPath()
        {
            AppComposition composition = new FakeWorld().Build();
            var host = new FakeServerHost();
            var elevation = new ScriptedElevation();
            var viewModel = Create(composition, elevation, host);
            int shutdowns = 0;
            viewModel.ShutdownRequested += () => shutdowns++;
            ConfigLoadResult loaded = composition.Loader.LoadJson("{\"schemaVersion\":1,\"applications\":[]}", "config.json");
            viewModel.ApplyLoad(loaded, false);

            await viewModel.RestartAsAdministratorAsync();

            Assert.Equal(1, shutdowns);
            Assert.Equal(loaded.Document!.SourcePath, elevation.LastPath);
            Assert.False(host.IsRunning);
        }

        private static MainViewModel Create(AppComposition composition, IElevationService elevation, FakeServerHost host)
        {
            return new MainViewModel(
                composition.Store,
                composition.State,
                host,
                composition.Input,
                composition.Locator,
                composition.Log,
                composition.Stats,
                new FakeInteraction(),
                new FakeClipboard(),
                new InlineUiInvoker(),
                new ConfigEditorSession(composition.Loader, composition.Store, () => composition.State.State == ServerRunState.Running),
                elevation);
        }

        private sealed class ScriptedElevation : IElevationService
        {
            public bool IsElevated { get; init; }

            public bool Started { get; init; } = true;

            public string? LastPath { get; private set; }

            public bool RestartElevated(string? configPath)
            {
                LastPath = configPath;
                return Started;
            }
        }
    }

    public class ConnectionDiagnosticsTests
    {
        [Fact]
        public async Task RefusedConnectionExplainsTheFailure()
        {
            var probes = new ScriptedProbes
            {
                Tcp = _ => new ProbeOutcome { Kind = ProbeKinds.Refused, Detail = "actively refused" }
            };

            ConnectionReport report = await Check(probes, ServerSettings.CreateDefault(), true);

            Assert.Contains("The connection was refused", report.Text);
            Assert.DoesNotContain("Trace route", report.Text);
        }

        [Fact]
        public async Task TimeoutOnAForeignPublicAddressAddsARoute()
        {
            var probes = new ScriptedProbes
            {
                Local = new[] { IPAddress.Parse("192.168.1.20") },
                Tcp = address => IPAddress.IsLoopback(address)
                    ? new ProbeOutcome { Kind = ProbeKinds.Ok, Detail = "connected" }
                    : new ProbeOutcome { Kind = ProbeKinds.Timeout, Detail = "timed out" }
            };

            ConnectionReport report = await Check(probes, PublicServer(), true);
            string text = report.Text;

            Assert.Contains("The connection timed out", text);
            Assert.Contains("firewall", text);
            Assert.Contains("NAT loopback", text);
            Assert.Contains("port-forward", text);
            Assert.Contains("Trace route:", text);
        }

        [Fact]
        public async Task RejectedKeyAndOriginAreNamed()
        {
            ConnectionReport key = await Check(new ScriptedProbes { Post = _ => Http(401, "") }, ServerSettings.CreateDefault(), true);
            ConnectionReport origin = await Check(new ScriptedProbes { Post = _ => Http(403, "") }, ServerSettings.CreateDefault(), true);

            Assert.Contains("API key", key.Text);
            Assert.DoesNotContain("Trace route", key.Text);
            Assert.Contains("Origin", origin.Text);
        }

        [Fact]
        public async Task CertificateProblemsAreNamed()
        {
            var probes = new ScriptedProbes
            {
                Tls = _ => new TlsProbeResult
                {
                    Succeeded = true,
                    Kind = ProbeKinds.Tls,
                    Detail = "name",
                    Subject = "CN=other",
                    HostNameMismatch = true,
                    Untrusted = true
                }
            };

            ConnectionReport report = await Check(probes, HttpsLoopback(), true);
            string text = report.Text;

            Assert.Contains("not valid for this host name", text);
            Assert.Contains("not trusted", text);
            Assert.Contains("server.cer", text);
            Assert.Contains("FAIL", text);
            Assert.DoesNotContain("Trace route:", text);
        }

        [Fact]
        public async Task GeneratedSelfSignedCertificateWarns()
        {
            var probes = new ScriptedProbes
            {
                GeneratedThumbprint = "ABC123",
                Tls = _ => SelfSignedTls("abc123", mismatch: false, untrusted: false)
            };

            ConnectionReport report = await Check(probes, HttpsLoopback(), true);
            string text = report.Text;
            ConnectionCheckStep tls = TlsStep(report);

            Assert.Equal(CheckStepStatus.Warning, tls.Status);
            Assert.True(tls.Passed);
            Assert.Contains("0 failed, 1 warning.", text);
            Assert.Contains("WARN", text);
            Assert.Contains("The certificate is self-signed.", text);
            Assert.Contains("No certificate path is configured", text);
            Assert.Contains("%LOCALAPPDATA%\\mcp_overlord", text);
            Assert.Contains("No certificate authority vouches for it", text);
            Assert.Contains("server.cer", text);
            Assert.Contains("API key could be intercepted", text);
            Assert.Contains("Signed certificate section", text);
            Assert.DoesNotContain("The certificate is not trusted.", text);
            Assert.DoesNotContain("configured certificate file", text);
            Assert.DoesNotContain("FAIL", text);
            Assert.DoesNotContain("Trace route:", text);
        }

        [Fact]
        public async Task ConfiguredSelfSignedCertificateWarns()
        {
            var probes = new ScriptedProbes
            {
                GeneratedThumbprint = "ABC123",
                Tls = _ => SelfSignedTls("OTHER", mismatch: false)
            };

            ConnectionReport report = await Check(probes, HttpsLoopback(), true);
            string text = report.Text;

            Assert.Equal(CheckStepStatus.Warning, TlsStep(report).Status);
            Assert.Contains("0 failed, 1 warning.", text);
            Assert.Contains("configured certificate file", text);
            Assert.Contains("that certificate", text);
            Assert.DoesNotContain("No certificate path is configured", text);
            Assert.DoesNotContain("FAIL", text);
            Assert.DoesNotContain("Trace route:", text);
        }

        [Fact]
        public async Task SelfSignedNameMismatchStillFails()
        {
            var probes = new ScriptedProbes
            {
                GeneratedThumbprint = "ABC123",
                Tls = _ => SelfSignedTls("ABC123", mismatch: true)
            };

            ConnectionReport report = await Check(probes, HttpsLoopback(), true);
            string text = report.Text;
            ConnectionCheckStep tls = TlsStep(report);

            Assert.Equal(CheckStepStatus.Fail, tls.Status);
            Assert.False(tls.Passed);
            Assert.Contains("1 failed, 0 warnings.", text);
            Assert.Contains("FAIL", text);
            Assert.Contains("not valid for this host name", text);
            Assert.DoesNotContain("WARN", text);
            Assert.DoesNotContain("No certificate path is configured", text);
        }

        private static TlsProbeResult SelfSignedTls(string thumbprint, bool mismatch, bool untrusted = true)
        {
            return new TlsProbeResult
            {
                Succeeded = true,
                Kind = ProbeKinds.Ok,
                Detail = "The handshake completed.",
                Subject = "CN=localhost",
                SelfSigned = true,
                Untrusted = untrusted,
                HostNameMismatch = mismatch,
                Thumbprint = thumbprint
            };
        }

        private static ConnectionCheckStep TlsStep(ConnectionReport report)
        {
            return Assert.Single(report.Steps, step => step.Name.EndsWith("  TLS", StringComparison.Ordinal));
        }

        private static Task<ConnectionReport> Check(ScriptedProbes probes, ServerSettings settings, bool running)
        {
            return new ConnectionDiagnostics(probes).CheckAsync(settings, running, null, CancellationToken.None);
        }

        private static ServerSettings PublicServer()
        {
            return new ServerSettings(
                5000,
                5001,
                true,
                new CertificateSettings(null, "", new[] { "play.example" }),
                "70.83.53.59",
                "0123456789abcdef");
        }

        private static ServerSettings HttpsLoopback()
        {
            return new ServerSettings(
                5000,
                5001,
                true,
                new CertificateSettings(null, "", new[] { "localhost" }),
                "127.0.0.1",
                null);
        }

        private static HttpProbeResult Http(int status, string body)
        {
            return new HttpProbeResult
            {
                Succeeded = true,
                Kind = ProbeKinds.Http,
                StatusCode = status,
                Body = body,
                Detail = "HTTP " + status
            };
        }

        private sealed class ScriptedProbes : IDiagnosticProbes
        {
            public bool Listening { get; set; } = true;

            public IPAddress[] Local { get; set; } = new[] { IPAddress.Loopback };

            public Func<IPAddress, ProbeOutcome>? Tcp { get; set; }

            public Func<string, TlsProbeResult>? Tls { get; set; }

            public Func<string, HttpProbeResult>? Post { get; set; }

            public string? GeneratedThumbprint { get; set; }

            public bool IsPortListening(int port)
            {
                return Listening;
            }

            public IReadOnlyList<IPAddress> LocalAddresses()
            {
                return Local;
            }

            public Task<DnsProbeResult> ResolveAsync(string host, CancellationToken cancellationToken)
            {
                IPAddress address = IPAddress.TryParse(host, out IPAddress? parsed) ? parsed : IPAddress.Loopback;
                return Task.FromResult(new DnsProbeResult
                {
                    Succeeded = true,
                    Detail = address.ToString(),
                    Addresses = new[] { address }
                });
            }

            public Task<ProbeOutcome> ConnectTcpAsync(IPAddress address, int port, TimeSpan timeout, CancellationToken cancellationToken)
            {
                ProbeOutcome outcome = Tcp?.Invoke(address) ?? new ProbeOutcome
                {
                    Succeeded = true,
                    Kind = ProbeKinds.Ok,
                    Detail = "connected"
                };
                return Task.FromResult(outcome);
            }

            public Task<TlsProbeResult> HandshakeTlsAsync(string host, IPAddress address, int port, TimeSpan timeout, CancellationToken cancellationToken)
            {
                TlsProbeResult result = Tls?.Invoke(host) ?? new TlsProbeResult
                {
                    Succeeded = true,
                    Kind = ProbeKinds.Ok,
                    Detail = "trusted",
                    Subject = "CN=" + host
                };
                return Task.FromResult(result);
            }

            public string? GeneratedCertificateThumbprint()
            {
                return GeneratedThumbprint;
            }

            public Task<HttpProbeResult> PostJsonAsync(
                Uri url,
                string? apiKey,
                string json,
                string? sessionId,
                TimeSpan timeout,
                CancellationToken cancellationToken)
            {
                if (Post != null)
                {
                    return Task.FromResult(Post(json));
                }

                string body = json.Contains("tools/list", StringComparison.Ordinal)
                    ? "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[{\"name\":\"lock\"}]}}"
                    : "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"2025-11-25\",\"serverInfo\":{\"name\":\"mcp_overlord\",\"version\":\"1.0.0\"}}}";
                if (json.Contains("\"method\":\"notifications/initialized\"", StringComparison.Ordinal))
                {
                    body = "";
                }

                return Task.FromResult(new HttpProbeResult
                {
                    Succeeded = true,
                    Kind = ProbeKinds.Http,
                    StatusCode = json.Contains("notifications/initialized", StringComparison.Ordinal) ? 202 : 200,
                    Body = body,
                    Detail = "HTTP 200",
                    SessionId = "session"
                });
            }

            public Task<PingProbeResult> PingAsync(IPAddress address, int ttl, TimeSpan timeout, CancellationToken cancellationToken)
            {
                return Task.FromResult(new PingProbeResult
                {
                    Kind = ProbeKinds.Ok,
                    ReplyAddress = address.ToString(),
                    Detail = address + " 1 ms"
                });
            }
        }
    }
}

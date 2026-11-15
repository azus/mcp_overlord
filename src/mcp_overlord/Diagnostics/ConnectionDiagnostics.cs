using System.Net;
using System.Text;
using mcp_overlord.Configuration;
using mcp_overlord.Hosting;

namespace mcp_overlord.Diagnostics
{
    /// <summary>
    /// Checks every public MCP endpoint and explains a failure with ping, traceroute, and a short hint.
    /// </summary>
    public sealed class ConnectionDiagnostics
    {
        /// <summary>Protocol version sent on the check's initialize request.</summary>
        public const string ProtocolVersion = "2025-11-25";

        /// <summary>How long a TCP or TLS probe waits.</summary>
        public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

        /// <summary>How long one HTTP call waits.</summary>
        public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(5);

        /// <summary>How long one traceroute hop waits.</summary>
        public static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(1);

        /// <summary>Maximum traceroute hops.</summary>
        public const int TraceHops = 20;

        private const string InitializeBody =
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"" + ProtocolVersion + "\",\"capabilities\":{},\"clientInfo\":{\"name\":\"mcp-overlord-check\",\"version\":\"1.0.0\"}}}";

        private const string InitializedBody =
            "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}";

        private const string ToolsBody =
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}";

        private readonly IDiagnosticProbes _probes;

        /// <summary>
        /// Creates the check.
        /// </summary>
        /// <param name="probes">Network calls. Production uses <see cref="SystemDiagnosticProbes"/>.</param>
        public ConnectionDiagnostics(IDiagnosticProbes probes)
        {
            _probes = probes ?? throw new ArgumentNullException(nameof(probes));
        }

        /// <summary>
        /// Checks <paramref name="settings"/>. A null document produces a report that says so.
        /// </summary>
        /// <param name="settings">Listener settings. Null when nothing is loaded.</param>
        /// <param name="serverRunning">True when this process is currently listening.</param>
        /// <param name="progress">Receives a status line as each endpoint starts. May be null.</param>
        /// <param name="cancellationToken">Cancels the check.</param>
        /// <returns>The report.</returns>
        public async Task<ConnectionReport> CheckAsync(
            ServerSettings? settings,
            bool serverRunning,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            if (settings == null)
            {
                return new ConnectionReport
                {
                    Text = "No configuration is loaded, so there is no endpoint to check."
                };
            }

            var works = new List<EndpointWork>();
            foreach (EndpointAddress endpoint in EndpointFormatter.PublicEndpoints(settings))
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report("Checking " + endpoint.Url);
                works.Add(await CheckEndpoint(endpoint, settings, serverRunning, cancellationToken).ConfigureAwait(false));
            }

            return new ConnectionReport
            {
                Text = Format(works),
                Steps = works.SelectMany(work => work.Steps).ToList()
            };
        }

        private async Task<EndpointWork> CheckEndpoint(
            EndpointAddress endpoint,
            ServerSettings settings,
            bool serverRunning,
            CancellationToken cancellationToken)
        {
            var work = new EndpointWork(endpoint.Url);
            if (!Uri.TryCreate(endpoint.Url, UriKind.Absolute, out Uri? uri))
            {
                work.Add(false, "URL", "The endpoint URL is not valid.");
                return work;
            }

            CheckServer(work, serverRunning);
            CheckPort(work, uri.Port);
            await CheckName(work, uri.Host, cancellationToken).ConfigureAwait(false);
            if (work.Address == null)
            {
                await AddInsights(work, cancellationToken).ConfigureAwait(false);
                return work;
            }

            ClassifyAddress(work);
            await CheckTcp(work, uri.Port, cancellationToken).ConfigureAwait(false);
            if (work.TcpKind != ProbeKinds.Ok)
            {
                await AddInsights(work, cancellationToken).ConfigureAwait(false);
                return work;
            }

            if (endpoint.IsHttps)
            {
                await CheckTls(work, uri.Host, uri.Port, cancellationToken).ConfigureAwait(false);
            }

            await CheckMcp(work, uri, settings.ApiKey, endpoint.IsHttps, cancellationToken).ConfigureAwait(false);
            await AddInsights(work, cancellationToken).ConfigureAwait(false);
            return work;
        }

        private static void CheckServer(EndpointWork work, bool serverRunning)
        {
            work.ServerStopped = !serverRunning;
            work.Add(serverRunning, "Server", serverRunning
                ? "This process is listening for clients."
                : "This process is stopped.");
        }

        private void CheckPort(EndpointWork work, int port)
        {
            bool listening = _probes.IsPortListening(port);
            work.PortClosed = !listening;
            work.Add(listening, "Port", listening
                ? "Port " + port + " has a listener."
                : "Nothing is listening on port " + port + ".");
        }

        private async Task CheckName(EndpointWork work, string host, CancellationToken cancellationToken)
        {
            DnsProbeResult dns = await _probes.ResolveAsync(host, cancellationToken).ConfigureAwait(false);
            work.DnsFailed = !dns.Succeeded;
            work.Add(dns.Succeeded, "DNS", dns.Succeeded ? dns.Detail : "The host name did not resolve. " + dns.Detail);
            if (dns.Succeeded && dns.Addresses.Count > 0)
            {
                work.Address = dns.Addresses[0];
            }
        }

        private void ClassifyAddress(EndpointWork work)
        {
            IPAddress address = work.Address!;
            bool local = IsLocal(address);
            work.AddressIsLocal = local;
            work.AddressIsPublic = !local && !IsPrivateOrLoopback(address);
            string detail = local
                ? address + " is assigned to this machine."
                : address + " is not assigned to this machine.";
            work.Add(true, "Address", detail);
        }

        private async Task CheckTcp(EndpointWork work, int port, CancellationToken cancellationToken)
        {
            ProbeOutcome tcp = await _probes.ConnectTcpAsync(work.Address!, port, ConnectTimeout, cancellationToken).ConfigureAwait(false);
            work.TcpKind = tcp.Succeeded ? ProbeKinds.Ok : tcp.Kind;
            work.Add(tcp.Succeeded, "TCP", tcp.Detail);
        }

        private async Task CheckTls(EndpointWork work, string host, int port, CancellationToken cancellationToken)
        {
            TlsProbeResult tls = await _probes.HandshakeTlsAsync(host, work.Address!, port, ConnectTimeout, cancellationToken).ConfigureAwait(false);
            RememberTls(work, tls);
            work.Add(ClassifyTls(tls), "TLS", DescribeTls(tls));
        }

        private void RememberTls(EndpointWork work, TlsProbeResult tls)
        {
            work.TlsUntrusted = tls.Untrusted;
            work.TlsMismatch = tls.HostNameMismatch;
            work.TlsTransportFailed = !tls.Succeeded;
            work.TlsSelfSigned = tls.SelfSigned;
            work.TlsAutoCreated = IsAutoCreated(tls);
        }

        private bool IsAutoCreated(TlsProbeResult tls)
        {
            if (!tls.SelfSigned || string.IsNullOrEmpty(tls.Thumbprint))
            {
                return false;
            }

            string? generated = _probes.GeneratedCertificateThumbprint();
            return !string.IsNullOrEmpty(generated)
                && string.Equals(tls.Thumbprint, generated, StringComparison.OrdinalIgnoreCase);
        }

        private static CheckStepStatus ClassifyTls(TlsProbeResult tls)
        {
            if (!tls.Succeeded || tls.HostNameMismatch)
            {
                return CheckStepStatus.Fail;
            }

            if (tls.SelfSigned)
            {
                return CheckStepStatus.Warning;
            }

            if (tls.Untrusted)
            {
                return CheckStepStatus.Fail;
            }

            return CheckStepStatus.Pass;
        }

        private async Task CheckMcp(EndpointWork work, Uri url, string? apiKey, bool https, CancellationToken cancellationToken)
        {
            HttpProbeResult initialize = await Post(url, apiKey, InitializeBody, null, cancellationToken).ConfigureAwait(false);
            work.HttpStatus = initialize.StatusCode;
            work.HttpKind = initialize.Kind;
            McpReply reply = McpReplyParser.Parse(initialize.Body);
            bool passed = initialize.Succeeded && initialize.StatusCode >= 200 && initialize.StatusCode < 300 && reply.Parsed && !reply.HasError;
            work.Add(passed, "MCP initialize", DescribeInitialize(initialize, reply, https));
            if (!passed)
            {
                return;
            }

            await Post(url, apiKey, InitializedBody, initialize.SessionId, cancellationToken).ConfigureAwait(false);
            HttpProbeResult tools = await Post(url, apiKey, ToolsBody, initialize.SessionId, cancellationToken).ConfigureAwait(false);
            if (tools.StatusCode != 0)
            {
                work.HttpStatus = tools.StatusCode;
                work.HttpKind = tools.Kind;
            }

            McpReply toolReply = McpReplyParser.Parse(tools.Body);
            bool toolsPassed = tools.Succeeded && tools.StatusCode >= 200 && tools.StatusCode < 300 && toolReply.Parsed && !toolReply.HasError && toolReply.ToolCount >= 0;
            work.Add(toolsPassed, "MCP tools/list", DescribeTools(tools, toolReply));
        }

        private Task<HttpProbeResult> Post(Uri url, string? apiKey, string json, string? sessionId, CancellationToken cancellationToken)
        {
            return _probes.PostJsonAsync(url, apiKey, json, sessionId, HttpTimeout, cancellationToken);
        }

        private async Task AddInsights(EndpointWork work, CancellationToken cancellationToken)
        {
            if (!work.Failed && !work.Warned)
            {
                return;
            }

            work.Insights.AddRange(Hints(work));
            if (!ShouldProbeRoute(work))
            {
                return;
            }

            PingProbeResult ping = await _probes.PingAsync(work.Address!, 128, PingTimeout, cancellationToken).ConfigureAwait(false);
            work.Insights.Add("Ping: " + (string.IsNullOrEmpty(ping.Detail) ? ping.Kind : ping.Detail));
            work.Insights.Add("Trace route:");
            work.Insights.AddRange(await Trace(work.Address!, cancellationToken).ConfigureAwait(false));
        }

        private async Task<IReadOnlyList<string>> Trace(IPAddress address, CancellationToken cancellationToken)
        {
            var hops = new List<string>();
            for (int ttl = 1; ttl <= TraceHops; ttl++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PingProbeResult hop = await _probes.PingAsync(address, ttl, PingTimeout, cancellationToken).ConfigureAwait(false);
                string who = string.IsNullOrEmpty(hop.ReplyAddress) ? hop.Detail : hop.ReplyAddress;
                if (string.IsNullOrEmpty(who))
                {
                    who = hop.Kind;
                }

                hops.Add(ttl + "  " + who);
                if (hop.Kind == ProbeKinds.Ok || hop.Kind == ProbeKinds.Unreachable)
                {
                    break;
                }
            }

            return hops;
        }

        private bool IsLocal(IPAddress address)
        {
            IPAddress compare = Normalize(address);
            foreach (IPAddress local in _probes.LocalAddresses())
            {
                if (Normalize(local).Equals(compare))
                {
                    return true;
                }
            }

            return IPAddress.IsLoopback(compare);
        }

        private static IReadOnlyList<string> Hints(EndpointWork work)
        {
            var hints = new List<string>();
            if (work.ServerStopped)
            {
                hints.Add("The server is stopped. Start it before expecting a client to connect.");
            }

            if (work.PortClosed)
            {
                hints.Add("Nothing is listening on the port. The listener is not bound.");
            }

            if (work.DnsFailed)
            {
                hints.Add("The host name did not resolve.");
            }

            if (work.TcpKind == ProbeKinds.Refused || work.HttpKind == ProbeKinds.Refused)
            {
                hints.Add("The connection was refused. The port is closed, or a firewall rejected it.");
            }

            if (work.TcpKind == ProbeKinds.Timeout || work.HttpKind == ProbeKinds.Timeout)
            {
                hints.Add("The connection timed out. A firewall may be dropping the packets, or the route does not reach the host.");
            }

            if (work.TlsTransportFailed)
            {
                hints.Add("The TLS handshake did not complete. A firewall may be dropping the packets, or the certificate negotiation failed.");
            }

            if (work.AddressIsPublic && NetworkFailed(work))
            {
                hints.Add("The address is a public IP that is not assigned to this machine. A client on this same machine often cannot reach it without NAT loopback or a port-forward rule.");
            }
            else if (work.Address != null && !work.AddressIsLocal && !IPAddress.IsLoopback(Normalize(work.Address)) && NetworkFailed(work))
            {
                hints.Add("The address is not assigned to this machine. Check that the host is up and that this machine can route to it.");
            }

            if (work.HttpStatus == 401)
            {
                hints.Add("The server rejected the API key.");
            }

            if (work.HttpStatus == 403)
            {
                hints.Add("The server rejected the Origin header.");
            }

            hints.AddRange(CertificateHints(work));
            return hints;
        }

        private static IReadOnlyList<string> CertificateHints(EndpointWork work)
        {
            if (work.TlsSelfSigned && !work.TlsTransportFailed && !work.TlsMismatch)
            {
                return SelfSignedHints(work.TlsAutoCreated);
            }

            var hints = new List<string>();
            if (work.TlsUntrusted)
            {
                hints.Add("The certificate is not trusted. Install the server.cer file in the client's trusted root store.");
            }

            if (work.TlsMismatch)
            {
                hints.Add("The certificate is not valid for this host name.");
            }

            return hints;
        }

        private static IReadOnlyList<string> SelfSignedHints(bool autoCreated)
        {
            var hints = new List<string>();
            if (autoCreated)
            {
                hints.Add("No certificate path is configured, so the server created this self-signed certificate in %LOCALAPPDATA%\\mcp_overlord.");
            }
            else
            {
                hints.Add("This self-signed certificate came from the configured certificate file.");
            }

            hints.Add(autoCreated
                ? "No certificate authority vouches for it, so clients reject it until server.cer is in their Trusted Root Certification Authorities store."
                : "No certificate authority vouches for it, so clients reject it until that certificate is in their Trusted Root Certification Authorities store.");
            hints.Add("Until the certificate is trusted, TLS cannot prove the identity of the server, and the API key could be intercepted.");
            hints.Add("For remote use, Help explains how to get a signed certificate. See the Signed certificate section.");
            return hints;
        }

        private static bool NetworkFailed(EndpointWork work)
        {
            return work.TcpKind == ProbeKinds.Refused
                || work.TcpKind == ProbeKinds.Timeout
                || work.TcpKind == ProbeKinds.Error
                || work.HttpKind == ProbeKinds.Refused
                || work.HttpKind == ProbeKinds.Timeout
                || work.TlsTransportFailed;
        }

        private static bool ShouldProbeRoute(EndpointWork work)
        {
            if (work.Address == null || !NetworkFailed(work))
            {
                return false;
            }

            return !IPAddress.IsLoopback(Normalize(work.Address));
        }

        private static string DescribeTls(TlsProbeResult tls)
        {
            var parts = new List<string> { tls.Detail };
            if (!string.IsNullOrEmpty(tls.Subject))
            {
                parts.Add("Subject " + tls.Subject);
            }

            if (!string.IsNullOrEmpty(tls.AlternativeNames))
            {
                parts.Add("Names " + tls.AlternativeNames);
            }

            if (tls.Expires.HasValue)
            {
                parts.Add("Expires " + tls.Expires.Value.ToString("yyyy-MM-dd"));
            }

            if (!string.IsNullOrEmpty(tls.ChainStatus))
            {
                parts.Add(tls.ChainStatus);
            }

            if (tls.HostNameMismatch)
            {
                parts.Add("The certificate name does not match the host.");
            }

            if (tls.SelfSigned)
            {
                parts.Add("The certificate is self-signed.");
            }
            else if (tls.Untrusted)
            {
                parts.Add("The certificate is not trusted.");
            }

            return string.Join(" ", parts);
        }

        private static string DescribeInitialize(HttpProbeResult http, McpReply reply, bool https)
        {
            if (!http.Succeeded)
            {
                return http.Detail;
            }

            var parts = new List<string> { http.Detail };
            if (https)
            {
                parts.Add("Certificate checks were skipped on this call so the status could be read.");
            }

            if (reply.HasError)
            {
                parts.Add(reply.Error);
            }
            else if (reply.Parsed)
            {
                if (!string.IsNullOrEmpty(reply.ServerName))
                {
                    parts.Add("Server " + reply.ServerName + " " + reply.ServerVersion);
                }

                if (!string.IsNullOrEmpty(reply.ProtocolVersion))
                {
                    parts.Add("Protocol " + reply.ProtocolVersion);
                }
            }
            else if (http.StatusCode >= 200 && http.StatusCode < 300)
            {
                parts.Add("The response was not a JSON-RPC result.");
            }

            return string.Join(" ", parts);
        }

        private static string DescribeTools(HttpProbeResult http, McpReply reply)
        {
            if (!http.Succeeded)
            {
                return http.Detail;
            }

            if (reply.HasError)
            {
                return http.Detail + " " + reply.Error;
            }

            if (reply.ToolCount >= 0)
            {
                return http.Detail + " " + reply.ToolCount + " tools" + (string.IsNullOrEmpty(reply.ToolNames) ? "" : " (" + reply.ToolNames + ")");
            }

            return http.Detail + " The response did not list tools.";
        }

        private static string Format(IReadOnlyList<EndpointWork> works)
        {
            int failed = works.Count(work => work.Failed);
            int warnings = works.Count(work => work.Warned);
            var builder = new StringBuilder();
            builder.Append("Checked ");
            builder.Append(works.Count);
            builder.Append(works.Count == 1 ? " endpoint. " : " endpoints. ");
            builder.Append(failed);
            builder.Append(" failed, ");
            builder.Append(warnings);
            builder.Append(warnings == 1 ? " warning." : " warnings.");
            builder.AppendLine();
            foreach (EndpointWork work in works)
            {
                builder.AppendLine();
                builder.AppendLine(work.Url);
                foreach (ConnectionCheckStep step in work.Steps)
                {
                    builder.Append(StatusLabel(step.Status));
                    builder.Append(StepName(work.Url, step.Name));
                    builder.Append("  ");
                    builder.AppendLine(step.Detail);
                }

                if (work.Insights.Count == 0)
                {
                    continue;
                }

                builder.AppendLine("  Insights");
                foreach (string insight in work.Insights)
                {
                    builder.Append("    ");
                    builder.AppendLine(insight);
                }
            }

            return builder.ToString().TrimEnd();
        }

        private static string StatusLabel(CheckStepStatus status)
        {
            if (status == CheckStepStatus.Pass)
            {
                return "  PASS  ";
            }

            if (status == CheckStepStatus.Warning)
            {
                return "  WARN  ";
            }

            return "  FAIL  ";
        }

        private static string StepName(string url, string name)
        {
            string prefix = url + "  ";
            return name.StartsWith(prefix, StringComparison.Ordinal) ? name.Substring(prefix.Length) : name;
        }

        private static IPAddress Normalize(IPAddress address)
        {
            return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        }

        private static bool IsPrivateOrLoopback(IPAddress address)
        {
            IPAddress value = Normalize(address);
            if (IPAddress.IsLoopback(value))
            {
                return true;
            }

            byte[] bytes = value.GetAddressBytes();
            if (value.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return bytes[0] == 10
                    || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                    || (bytes[0] == 192 && bytes[1] == 168)
                    || (bytes[0] == 169 && bytes[1] == 254);
            }

            if (value.IsIPv6LinkLocal)
            {
                return true;
            }

            return (bytes[0] & 0xFE) == 0xFC;
        }

        private sealed class EndpointWork
        {
            public EndpointWork(string url)
            {
                Url = url;
            }

            public string Url { get; }

            public List<ConnectionCheckStep> Steps { get; } = new List<ConnectionCheckStep>();

            public List<string> Insights { get; } = new List<string>();

            public bool ServerStopped { get; set; }

            public bool PortClosed { get; set; }

            public bool DnsFailed { get; set; }

            public IPAddress? Address { get; set; }

            public bool AddressIsLocal { get; set; }

            public bool AddressIsPublic { get; set; }

            public string TcpKind { get; set; } = "";

            public bool TlsUntrusted { get; set; }

            public bool TlsMismatch { get; set; }

            public bool TlsTransportFailed { get; set; }

            public bool TlsSelfSigned { get; set; }

            public bool TlsAutoCreated { get; set; }

            public int HttpStatus { get; set; }

            public string HttpKind { get; set; } = "";

            public bool Failed
            {
                get { return Steps.Any(step => step.Status == CheckStepStatus.Fail); }
            }

            public bool Warned
            {
                get { return Steps.Any(step => step.Status == CheckStepStatus.Warning); }
            }

            public void Add(bool passed, string name, string detail)
            {
                Add(passed ? CheckStepStatus.Pass : CheckStepStatus.Fail, name, detail);
            }

            public void Add(CheckStepStatus status, string name, string detail)
            {
                Steps.Add(new ConnectionCheckStep
                {
                    Name = Url + "  " + name,
                    Status = status,
                    Detail = detail ?? ""
                });
            }
        }
    }
}
